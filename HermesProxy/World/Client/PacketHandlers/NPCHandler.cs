using Framework;
using Framework.GameMath;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using System;

namespace HermesProxy.World.Client;

public partial class WorldClient
{
    // Handlers for SMSG opcodes coming the legacy world server
    [PacketHandler(Opcode.SMSG_GOSSIP_MESSAGE)]
    void HandleGossipmessage(WorldPacket packet)
    {
        GossipMessagePkt gossip = new GossipMessagePkt();
        gossip.GossipGUID = packet.ReadGuid().To128(GetSession().GameState);
        GetSession().GameState.CurrentInteractedWithNPC = gossip.GossipGUID;

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
            gossip.GossipID = packet.ReadInt32();
        else
            gossip.GossipID = (int)gossip.GossipGUID.GetEntry();

        gossip.TextID = packet.ReadInt32();

        uint optionsCount = packet.ReadUInt32();

        for (uint i = 0; i < optionsCount; i++)
        {
            ClientGossipOption option = new ClientGossipOption();
            option.OptionIndex = packet.ReadInt32();
            option.OptionIcon = packet.ReadUInt8();
            option.OptionFlags = (byte)(packet.ReadBool() ? 1 : 0); // Code Box

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                option.OptionCost = packet.ReadInt32();

            option.Text = packet.ReadCString();

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                option.Confirm = packet.ReadCString();
            gossip.GossipOptions.Add(option);
        }

        uint questsCount = packet.ReadUInt32();

        for (uint i = 0; i < questsCount; i++)
        {
            ClientGossipQuest quest = ReadGossipQuestOption(packet);
            gossip.GossipQuests.Add(quest);
        }

        SendPacketToClient(gossip);
    }

    [PacketHandler(Opcode.SMSG_GOSSIP_COMPLETE)]
    void HandleGossipComplete(WorldPacket packet)
    {
        GossipComplete gossip = new GossipComplete();
        SendPacketToClient(gossip);
    }

    [PacketHandler(Opcode.SMSG_GOSSIP_POI)]
    void HandleGossipPoi(WorldPacket packet)
    {
        GossipPOI poi = new();
        poi.Flags = packet.ReadUInt32();
        var pos2d = packet.ReadVector2();
        poi.Pos = new Vector3(pos2d.X, pos2d.Y, 0);
        poi.Icon = packet.ReadUInt32();
        poi.Importance = packet.ReadUInt32();
        poi.Name = packet.ReadCString();
        SendPacketToClient(poi);
    }

    [PacketHandler(Opcode.SMSG_BINDER_CONFIRM)]
    void HandleBinderConfirm(WorldPacket packet)
    {
        BinderConfirm confirm = new BinderConfirm();
        confirm.Guid = packet.ReadGuid().To128(GetSession().GameState);
        GetSession().GameState.CurrentInteractedWithNPC = confirm.Guid;
        SendPacketToClient(confirm);
    }

    // Shown (greyed, out of stock) only when a legacy vendor's entire stock is class-filtered to
    // empty, purely so the modern client opens the merchant frame so the player can still sell.
    // Cheap era-ubiquitous item; change freely.
    private const uint PlaceholderVendorItemId = 6948; // Hearthstone

    [PacketHandler(Opcode.SMSG_VENDOR_INVENTORY)]
    void HandleVendorInventory(WorldPacket packet)
    {
        VendorInventory vendor = new VendorInventory();
        vendor.VendorGUID = packet.ReadGuid().To128(GetSession().GameState);
        GetSession().GameState.CurrentInteractedWithNPC = vendor.VendorGUID;
        byte itemsCount = packet.ReadUInt8();

        if (itemsCount == 0)
        {
            vendor.Reason = packet.ReadUInt8();
            // cMaNGOS class-filters a vendor's whole stock server-side (e.g. Cylina Darkheart, a
            // warlock-only vendor, sends 0 items to non-warlocks). The modern client opens the
            // merchant frame only when the list has >=1 item and Reason==0, so otherwise the player
            // can't even sell. Inject one out-of-stock placeholder (Quantity=0 => greyed + unbuyable)
            // so the frame opens. (#88)
            vendor.Reason = 0;
            vendor.Items.Add(new VendorItem
            {
                Slot = 1,
                Quantity = 0,        // 0 left in stock -> client greys it and blocks purchase
                StackCount = 1,
                Price = 1,
                Item = { ItemID = PlaceholderVendorItemId },
            });
            SendPacketToClient(vendor);
            return;
        }

        for (byte i = 0; i < itemsCount; i++)
        {
            VendorItem vendorItem = new();
            vendorItem.Slot = packet.ReadInt32();
            vendorItem.Item.ItemID = packet.ReadUInt32();
            packet.ReadUInt32(); // Display Id
            vendorItem.Quantity = packet.ReadInt32();
            vendorItem.Price = packet.ReadUInt32();
            vendorItem.Durability = packet.ReadInt32();
            vendorItem.StackCount = packet.ReadUInt32();
            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                vendorItem.ExtendedCostID = packet.ReadInt32();
            GetSession().GameState.SetItemBuyCount(vendorItem.Item.ItemID, vendorItem.StackCount);
            vendor.Items.Add(vendorItem);
        }

        SendPacketToClient(vendor);
    }

    [PacketHandler(Opcode.SMSG_SHOW_BANK)]
    void HandleShowBank(WorldPacket packet)
    {
        ShowBank bank = new ShowBank();
        bank.Guid = packet.ReadGuid().To128(GetSession().GameState);
        GetSession().GameState.CurrentInteractedWithNPC = bank.Guid;
        SendPacketToClient(bank);
    }

    [PacketHandler(Opcode.SMSG_TRAINER_LIST)]
    void HandleTrainerList(WorldPacket packet)
    {
        TrainerList trainer = new TrainerList();
        trainer.TrainerGUID = packet.ReadGuid().To128(GetSession().GameState);
        GetSession().GameState.CurrentInteractedWithNPC = trainer.TrainerGUID;
        trainer.TrainerID = trainer.TrainerGUID.GetEntry();
        trainer.TrainerType = packet.ReadInt32();
        int count = packet.ReadInt32();
        for (int i = 0; i < count; ++i)
        {
            TrainerListSpell spell = new();
            uint spellId = packet.ReadUInt32();
            if (ModernVersion.ExpansionVersion > 1 &&
                LegacyVersion.ExpansionVersion <= 1)
            {
                // in vanilla the server sends learn spell with effect 36
                // in expansions the server sends the actual spell
                uint realSpellId = GameData.GetRealSpell(spellId);
                if (realSpellId != spellId)
                {
                    GetSession().GameState.StoreRealSpell(realSpellId, spellId);
                    spellId = realSpellId;
                }
            }
            spell.SpellID = spellId;
            TrainerSpellStateLegacy stateOld = (TrainerSpellStateLegacy)packet.ReadUInt8();
            TrainerSpellStateModern stateNew = stateOld.CastEnum<TrainerSpellStateModern>();
            spell.Usable = stateNew;
            spell.MoneyCost = packet.ReadUInt32();
            packet.ReadInt32(); // Profession Dialog
            packet.ReadInt32(); // Profession Button
            spell.ReqLevel = packet.ReadUInt8();
            spell.ReqSkillLine = packet.ReadUInt32();
            spell.ReqSkillRank = packet.ReadUInt32();
            spell.ReqAbility[0] = packet.ReadUInt32();
            spell.ReqAbility[1] = packet.ReadUInt32();
            spell.ReqAbility[2] = packet.ReadUInt32();
            trainer.Spells.Add(spell);
        }
        trainer.Greeting = packet.ReadCString();
        SendPacketToClient(trainer);
    }

    [PacketHandler(Opcode.SMSG_TRAINER_BUY_FAILED)]
    void HandleTrainerBuyFailed(WorldPacket packet)
    {
        TrainerBuyFailed buy = new();
        buy.TrainerGUID = packet.ReadGuid().To128(GetSession().GameState);
        buy.SpellID = packet.ReadUInt32();
        buy.TrainerFailedReason = packet.ReadUInt32();
        SendPacketToClient(buy);
        ChatPkt chat = new ChatPkt(GetSession(), ChatMessageTypeModern.System, $"Failed to learn Spell {buy.SpellID} (Reason {buy.TrainerFailedReason}).");
        SendPacketToClient(chat);
    }

    [PacketHandler(Opcode.MSG_TALENT_WIPE_CONFIRM)]
    void HandleTalentWipeConfirm(WorldPacket packet)
    {
        RespecWipeConfirm respec = new();
        respec.TrainerGUID = packet.ReadGuid().To128(GetSession().GameState);
        respec.Cost = packet.ReadUInt32();
        SendPacketToClient(respec);
    }

    /// <summary>
    /// HERMES_256_SPIRITHEALER: answer the legacy confirm ourselves, because the modern client
    /// cannot.
    ///
    /// TBC's flow is a handshake: pick the spirit healer's gossip option -> server sends
    /// SMSG_SPIRIT_HEALER_CONFIRM -> client shows "resurrect with sickness?" -> client sends
    /// CMSG_SPIRIT_HEALER_ACTIVATE -> server resurrects. **SMSG_SPIRIT_HEALER_CONFIRM does not
    /// exist on modern builds** — it is absent from 69110's opcode table, from WPP's 5.5.0 table
    /// and from TrinityCore master, which has no such packet or class at all. So SendPacketToClient
    /// resolves no opcode and the packet is dropped in silence, the client waits, and two seconds
    /// later sends CMSG_CLOSE_INTERACTION and gives up. Observed live 29 Aug 23:13:40-42.
    ///
    /// The player already consented by choosing the gossip option, so replying on their behalf is
    /// completing the handshake, not deciding for them. Same class of fault as the Gossip bit:
    /// legacy expects a step the modern protocol dropped.
    /// </summary>
    static readonly bool s_spiritHealer =
        System.Environment.GetEnvironmentVariable("HERMES_256_SPIRITHEALER") == "1";

    [PacketHandler(Opcode.SMSG_SPIRIT_HEALER_CONFIRM)]
    void HandleSpiritHealerConfirm(WorldPacket packet)
    {
        WowGuid64 healerLegacy = packet.ReadGuid();

        SpiritHealerConfirm confirm = new SpiritHealerConfirm();
        confirm.Guid = healerLegacy.To128(GetSession().GameState);
        SendPacketToClient(confirm);

        if (s_spiritHealer)
        {
            // Same body the modern client's own activate would produce: opcode + the healer guid.
            WorldPacket activate = new WorldPacket(Opcode.CMSG_SPIRIT_HEALER_ACTIVATE);
            activate.WriteGuid(healerLegacy);
            SendPacketToServer(activate);
            Log.Print(LogType.Network, "[256-spike] SPIRITHEALER: auto-answered the legacy confirm "
                                       + $"for healer {healerLegacy}");
        }
    }
}
