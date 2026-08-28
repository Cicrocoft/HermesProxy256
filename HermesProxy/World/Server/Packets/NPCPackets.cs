/*
 * Copyright (C) 2012-2020 CypherCore <http://github.com/CypherCore>
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */


using Framework.Constants;
using Framework.GameMath;
using Framework.IO;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HermesProxy.World.Server.Packets;

public class InteractWithNPC : ClientPacket
{
    public InteractWithNPC(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        CreatureGUID = _worldPacket.ReadPackedGuid128();
    }

    public WowGuid128 CreatureGUID;
}

public class GossipMessagePkt : ServerPacket
{
    public GossipMessagePkt() : base(Opcode.SMSG_GOSSIP_MESSAGE) { }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) layout, confirmed against the client's own readers (top level
            // 0x63F120, per-option 0x589C80, treasure list 0x66A3C0, per-quest 0x63EFA0):
            // guid, GossipID, LfgDungeonsID, FriendshipFactionID, two counts, a 2-bit block
            // (HasTextID, HasBroadcastTextID), the options, then the OPTIONAL TextID and
            // BroadcastTextID, then the quests. The per-option OptionNPC field is a u32 - the
            // client takes the V5_5_3_64802+ arm of WPP's reader, settled by the disassembly
            // (0x589C80 reads u32 GossipOptionID, u32 OptionNPC, u8 OptionFlags, u64 OptionCost).
            _worldPacket.WritePackedGuid128(GossipGUID);
            _worldPacket.WriteInt32(GossipID);
            _worldPacket.WriteInt32(0); // LfgDungeonsID - no legacy source
            _worldPacket.WriteInt32(FriendshipFactionID);

            _worldPacket.WriteInt32(GossipOptions.Count);
            _worldPacket.WriteInt32(GossipQuests.Count);

            _worldPacket.WriteBit(true);  // HasTextID - the legacy message always carries one
            _worldPacket.WriteBit(false); // HasBroadcastTextID
            _worldPacket.FlushBits();

            foreach (ClientGossipOption options in GossipOptions)
            {
                _worldPacket.WriteInt32(options.OptionIndex);      // GossipOptionID
                _worldPacket.WriteUInt32(options.OptionIcon);      // OptionNPC (u32 on 69110; legacy icon ids 0-10 coincide)
                _worldPacket.WriteUInt8(options.OptionFlags);
                _worldPacket.WriteUInt64((ulong)options.OptionCost);
                _worldPacket.WriteUInt32(options.Language);
                _worldPacket.WriteInt32(0);                        // Flags - no legacy source
                _worldPacket.WriteInt32(options.OptionIndex);      // OrderIndex

                _worldPacket.WriteBits(options.Text.GetByteCount(), 12);
                _worldPacket.WriteBits(options.Confirm.GetByteCount(), 12);
                _worldPacket.WriteBits((byte)options.Status, 2);
                _worldPacket.WriteBit(options.SpellID.HasValue);
                _worldPacket.WriteBit(false);                      // HasOverrideIconID
                _worldPacket.WriteBits(0, 8);                      // FailureDescription length
                _worldPacket.FlushBits();

                options.Treasure.Write(_worldPacket);

                _worldPacket.WriteString(options.Text);
                _worldPacket.WriteString(options.Confirm);

                if (options.SpellID.HasValue)
                    _worldPacket.WriteInt32(options.SpellID.Value);
                // No OverrideIconID, no FailureDescription (flags/length written as 0 above).
            }

            _worldPacket.WriteInt32(TextID);

            foreach (ClientGossipQuest quest in GossipQuests)
                quest.Write(_worldPacket);
            return;
        }

        _worldPacket.WritePackedGuid128(GossipGUID);
        _worldPacket.WriteInt32(GossipID);
        _worldPacket.WriteInt32(FriendshipFactionID);
        _worldPacket.WriteInt32(TextID);

        _worldPacket.WriteInt32(GossipOptions.Count);
        _worldPacket.WriteInt32(GossipQuests.Count);

        foreach (ClientGossipOption options in GossipOptions)
        {
            _worldPacket.WriteInt32(options.OptionIndex);
            _worldPacket.WriteUInt8(options.OptionIcon);
            _worldPacket.WriteUInt8(options.OptionFlags);
            _worldPacket.WriteInt32(options.OptionCost);
            if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
                _worldPacket.WriteUInt32(options.Language);

            _worldPacket.WriteBits(options.Text.GetByteCount(), 12);
            _worldPacket.WriteBits(options.Confirm.GetByteCount(), 12);
            _worldPacket.WriteBits((byte)options.Status, 2);
            _worldPacket.WriteBit(options.SpellID.HasValue);
            _worldPacket.FlushBits();

            options.Treasure.Write(_worldPacket);

            _worldPacket.WriteString(options.Text);
            _worldPacket.WriteString(options.Confirm);

            if (options.SpellID.HasValue)
                _worldPacket.WriteInt32(options.SpellID.Value);
        }

        foreach (ClientGossipQuest text in GossipQuests)
            text.Write(_worldPacket);
    }

    public List<ClientGossipOption> GossipOptions = new();
    public int FriendshipFactionID;
    public WowGuid128 GossipGUID;
    public List<ClientGossipQuest> GossipQuests = new();
    public int TextID;
    public int GossipID;
}

public class ClientGossipOption
{
    public int OptionIndex;
    public byte OptionIcon;
    public byte OptionFlags;
    public int OptionCost;
    public uint Language;
    public GossipOptionStatus Status;
    public string Text = string.Empty;
    public string Confirm = string.Empty;
    public TreasureLootList Treasure = new();
    public int? SpellID;
}

public class TreasureLootList
{
    public List<TreasureItem> Items = new();

    public void Write(WorldPacket data)
    {
        data.WriteInt32(Items.Count);
        foreach (TreasureItem treasureItem in Items)
            treasureItem.Write(data);
    }
}

public struct TreasureItem
{
    public GossipOptionRewardType Type;
    public int ID;
    public int Quantity;

    public void Write(WorldPacket data)
    {
        data.WriteBits((byte)Type, 1);
        data.WriteInt32(ID);
        data.WriteInt32(Quantity);
        // 69110's treasure item carries a trailing ItemContext byte (client reader 0x66A3C0:
        // u8 bit byte, u32 ID, u32 Quantity, u8 ItemContext).
        if (ModernVersion.Uses550Engine)
            data.WriteUInt8(0); // ItemContext - no legacy source
    }
}

public class ClientGossipQuest
{
    public uint QuestID;
    public uint ContentTuningID;
    public int QuestType; // 2 not taken, 4 taken
    public int QuestLevel;
    public int QuestMaxLevel = 255;
    public bool Repeatable;
    public string QuestTitle = string.Empty;
    public uint QuestFlags = 8;
    public uint QuestFlagsEx;

    public void Write(WorldPacket data)
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) gossip quest entry, confirmed against the client's own struct reader
            // (0x63EFA0, shared by SMSG_GOSSIP_MESSAGE and SMSG_QUEST_GIVER_QUEST_LIST_MESSAGE):
            // ten u32s (QuestID, ContentTuningID, QuestType, QuestLevel, QuestMaxScalingLevel,
            // Unused1102, Flags, FlagsEx, FlagsEx2, FlagsEx3), then a 13-bit block (4 flag bits
            // + 9-bit title length), then the title. WPP ReadGossipQuestTextData agrees.
            data.WriteUInt32(QuestID);
            data.WriteUInt32(ContentTuningID);
            data.WriteInt32(QuestType);
            data.WriteInt32(QuestLevel);
            data.WriteInt32(QuestMaxLevel); // QuestMaxScalingLevel
            data.WriteInt32(0);             // Unused1102
            data.WriteUInt32(QuestFlags);
            data.WriteUInt32(QuestFlagsEx);
            data.WriteUInt32(0);            // FlagsEx2 - no legacy source
            data.WriteUInt32(0);            // FlagsEx3 - no legacy source

            data.WriteBit(Repeatable);
            data.WriteBit(false);           // ResetByScheduler
            data.WriteBit(false);           // Important
            data.WriteBit(false);           // Meta
            data.WriteBits(QuestTitle.GetByteCount(), 9);
            data.FlushBits();

            data.WriteString(QuestTitle);
            return;
        }

        data.WriteUInt32(QuestID);
        data.WriteUInt32(ContentTuningID);
        data.WriteInt32(QuestType);
        data.WriteInt32(QuestLevel);
        data.WriteInt32(QuestMaxLevel);
        data.WriteUInt32(QuestFlags);
        data.WriteUInt32(QuestFlagsEx);

        data.WriteBit(Repeatable);
        data.WriteBits(QuestTitle.GetByteCount(), 9);
        data.FlushBits();

        data.WriteString(QuestTitle);
    }
}

public class GossipSelectOption : ClientPacket
{
    public GossipSelectOption(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        GossipUnit = _worldPacket.ReadPackedGuid128();
        GossipID = _worldPacket.ReadUInt32();
        GossipIndex = _worldPacket.ReadUInt32();

        uint length = _worldPacket.ReadBits<uint>(8);
        PromotionCode = _worldPacket.ReadString(length);
    }

    public WowGuid128 GossipUnit;
    public uint GossipIndex;
    public uint GossipID;
    public string PromotionCode = string.Empty;
}

public class GossipComplete : ServerPacket, ISpanWritable
{
    public GossipComplete() : base(Opcode.SMSG_GOSSIP_COMPLETE) { }

    public override void Write()
    {
        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 2, 2, 5, 3))
        {
            _worldPacket.WriteBit(SuppressSound);
            _worldPacket.FlushBits();
        }
    }

    // MaxSize: optional bit (1 byte when flushed)
    public int MaxSize => 1;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 2, 2, 5, 3))
        {
            writer.WriteBit(SuppressSound);
            writer.FlushBits();
        }
        return writer.Position;
    }

    public bool SuppressSound;
}

public class BinderConfirm : ServerPacket, ISpanWritable
{
    public BinderConfirm() : base(Opcode.SMSG_BINDER_CONFIRM) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Guid);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(Guid.Low, Guid.High);
        return writer.Position;
    }

    public WowGuid128 Guid;
}

public class VendorInventory : ServerPacket
{
    public VendorInventory() : base(Opcode.SMSG_VENDOR_INVENTORY, ConnectionType.Instance) { }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            // 69110's own reader, 0x5A6B60, GetId-linked to opcode 0x46005C by stub_reader_link.py
            // (case 0x61DC6B calls 0x5A6B60 = stub 0x5A6B50 + 0x10) and walked with pdwalk.py
            // (119 instructions, sync verified): guid, u32 Reason, u32 Count, then per item
            // u64 Price FIRST, six u32, one flushed bits byte, ItemInstance LAST. The jump-table
            // derivation (opcode_bodies_jt.txt 0x46005C) gives the identical token sequence, and
            // WowPacketParser's V5_5_0 arm reads the same order. Note Reason is a u32 here, not
            // the u8 of the 3.4.3/10.x shape below, and Durability is not on the wire at all.
            _worldPacket.WritePackedGuid128(VendorGUID);
            _worldPacket.WriteUInt32(Reason);
            _worldPacket.WriteInt32(Items.Count);

            foreach (VendorItem item in Items)
                item.Write550(_worldPacket);
            return;
        }

        _worldPacket.WritePackedGuid128(VendorGUID);
        _worldPacket.WriteUInt8(Reason);
        _worldPacket.WriteInt32(Items.Count);

        foreach (VendorItem item in Items)
            item.Write(_worldPacket);
    }

    public byte Reason = 0;
    public List<VendorItem> Items = new();
    public WowGuid128 VendorGUID;
}

public class VendorItem
{
    public void Write(WorldPacket data)
    {
        data.WriteInt32(Slot);
        data.WriteInt32(Type);
        data.WriteInt32(Quantity);
        data.WriteUInt64(Price);
        data.WriteInt32(Durability);
        data.WriteUInt32(StackCount);
        data.WriteInt32(ExtendedCostID);
        data.WriteInt32(PlayerConditionFailed);
        Item.Write(data);
        data.WriteBit(DoNotFilterOnVendor);
        data.WriteBit(Refundable);
        data.FlushBits();
    }

    // The 5.5.x per-item body — see the evidence note in VendorInventory.Write. The widths and
    // positions (u64 then six u32 then bits then ItemInstance) are client-confirmed; the NAMES of
    // the six u32 are inferred from WowPacketParser's V5_5_0_61735 arm (Muid, Type, StackCount,
    // Quantity, ExtendedCostID, PlayerConditionFailed — StackCount before Quantity, unlike 10.x).
    // All six are plain fixed-width reads, so a wrong name cannot desync the parse.
    public void Write550(WorldPacket data)
    {
        data.WriteUInt64(Price);
        data.WriteInt32(Slot);              // Muid
        data.WriteInt32(Type);
        data.WriteUInt32(StackCount);
        data.WriteInt32(Quantity);
        data.WriteInt32(ExtendedCostID);
        data.WriteInt32(PlayerConditionFailed);
        data.WriteBit(Locked);
        data.WriteBit(DoNotFilterOnVendor);
        data.WriteBit(Refundable);
        data.FlushBits();
        Item.Write(data);
    }

    public int Slot;
    public int Type = 1;
    public ItemInstance Item = new();
    public int Quantity = -1;
    public ulong Price;
    public int Durability;
    public uint StackCount;
    public int ExtendedCostID;
    public int PlayerConditionFailed;
    public bool Locked;
    public bool DoNotFilterOnVendor;
    public bool Refundable;
}

public class ShowBank : ServerPacket, ISpanWritable
{
    public ShowBank() : base(Opcode.SMSG_SHOW_BANK, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Guid);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(Guid.Low, Guid.High);
        return writer.Position;
    }

    public WowGuid128 Guid;
}

public class BuyBankSlot : ClientPacket
{
    public BuyBankSlot(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        Guid = _worldPacket.ReadPackedGuid128();
    }

    public WowGuid128 Guid;
}

/// <summary>
/// Knobs for the two independent faults in SMSG_TRAINER_LIST on build 69110. Both default OFF,
/// so turning neither on reproduces the wire bytes this build has always shipped.
/// </summary>
/// <remarks>
/// They are separate because they fail differently. TRAINEROPCODE only changes which number the
/// packet goes out under; TRAINER553 only changes the body. Turning on the opcode alone delivers
/// a wrongly-shaped body to the real trainer reader; turning on the body alone delivers a
/// correctly-shaped trainer list to the threat-update reader. Neither is expected to work by
/// itself — the point of splitting them is that a negative result then says WHICH half was wrong.
/// </remarks>
internal static class Trainer256
{
    /// <summary>
    /// HERMES_256_TRAINEROPCODE=1 ships the trainer list under 0x46018D, the number the client's
    /// own GetId stub (RVA 0x5BBDA0) returns for the class whose reader parses a trainer list.
    /// With the knob off it ships under 0x460188, which capture #13 and the client's reader both
    /// show is SMSG_THREAT_UPDATE — see the comment block in V2_5_6_69110/Opcode.cs.
    /// </summary>
    public static readonly bool UseTrainerOpcode =
        System.Environment.GetEnvironmentVariable("HERMES_256_TRAINEROPCODE") == "1";

    /// <summary>
    /// HERMES_256_TRAINER553=1 writes the header and element the client actually reads.
    /// </summary>
    /// <remarks>
    /// Measured off the client's reader at RVA 0x5BBB90 and corroborated byte-for-byte by capture
    /// #13 [5424] (232 B, no slack). Two independent deviations from what we ship today, and in a
    /// linear unmasked body either one is fatal from its own offset onward:
    ///
    ///   * the header's TrainerType is <b>one byte</b>, not an int32. The reader calls the u8
    ///     primitive (0x2D9E4B0) once at 0x5BBBBD and stores the whole byte to [obj+0x30] with no
    ///     shift and no mask — so it is a plain u8, not WriteBits(2)+FlushBits, which would have
    ///     needed a >>6 to recover. That makes our header 3 bytes too long.
    ///   * the element is <b>34 bytes</b>, not 30: an extra u32 sits between ReqAbility[2] and
    ///     Usable. The reader writes object offsets 0x00,0x04,0x08,0x0C, then a three-trip inner
    ///     loop into 0x10/0x14/0x18 (that loop is what pins ReqAbility[3] rather than naming it by
    ///     analogy), then 0x1C, then two u8 at 0x20/0x21, and advances r15 by 0x24 per element.
    ///     WowPacketParser 5.5.0 NpcHandler.cs:279-288 reads the same order and calls the extra
    ///     field Unk440.
    ///
    /// Unk440 has no 2.4.3 source and is 0 in all five live elements, so it is written as zero and
    /// must stay that way: filling a field with a plausible value is how two of this project's
    /// crashes happened.
    /// </remarks>
    public static readonly bool Use553Layout =
        System.Environment.GetEnvironmentVariable("HERMES_256_TRAINER553") == "1";
}

public class TrainerList : ServerPacket, ISpanWritable
{
    // With TRAINEROPCODE off this resolves to 0x460188 — the same number, and therefore the same
    // wire behaviour, as before the enum was corrected. See Trainer256.UseTrainerOpcode.
    public TrainerList()
        : base(Trainer256.UseTrainerOpcode ? Opcode.SMSG_TRAINER_LIST : Opcode.SMSG_THREAT_UPDATE,
               ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(TrainerGUID);

        if (Trainer256.Use553Layout)
            _worldPacket.WriteUInt8((byte)TrainerType);
        else
            _worldPacket.WriteInt32(TrainerType);

        _worldPacket.WriteUInt32(TrainerID);

        _worldPacket.WriteInt32(Spells.Count);
        foreach (TrainerListSpell spell in Spells)
        {
            _worldPacket.WriteUInt32(spell.SpellID);
            _worldPacket.WriteUInt32(spell.MoneyCost);
            _worldPacket.WriteUInt32(spell.ReqSkillLine);
            _worldPacket.WriteUInt32(spell.ReqSkillRank);

            for (uint i = 0; i < 3; ++i)
                _worldPacket.WriteUInt32(spell.ReqAbility[i]);

            if (Trainer256.Use553Layout)
                _worldPacket.WriteUInt32(0);    // Unk440 — no 2.4.3 source, zero on live, keep it zero

            _worldPacket.WriteUInt8((byte)spell.Usable);
            _worldPacket.WriteUInt8(spell.ReqLevel);
        }

        _worldPacket.WriteBits(Greeting.GetByteCount(), 11);
        _worldPacket.FlushBits();
        _worldPacket.WriteString(Greeting);
    }

    // MaxSize: GUID(18) + header(12) + max 200 spells + bits(2) + greeting(256).
    // SpellSize is the 553 element (34) unconditionally rather than per-knob: it only sizes the
    // pooled rent, and under-renting is the failure mode that matters. The pre-553 element is
    // 4 uints(16) + 3 ReqAbility(12) + 2 bytes(2) = 30; the 553 element adds Unk440 -> 34.
    // The header is 12 with the knob off (int32 TrainerType + uint32 TrainerID + int32 Count) and
    // 9 with it on, so 12 covers both.
    private const int MaxSpells = 200;
    private const int SpellSize = 34;
    private const int MaxGreetingBytes = 256;
    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 12 + MaxSpells * SpellSize + 2 + MaxGreetingBytes;

    public int WriteToSpan(Span<byte> buffer)
    {
        int greetingBytes = Encoding.UTF8.GetByteCount(Greeting ?? "");
        if (Spells.Count > MaxSpells || greetingBytes > 2047) // 11 bits max
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(TrainerGUID.Low, TrainerGUID.High);

        // Keep in lockstep with Write(). Packet.WritePacketData prefers this arm whenever the
        // packet fits MaxSize, so fixing Write() alone would change nothing on the wire.
        if (Trainer256.Use553Layout)
            writer.WriteUInt8((byte)TrainerType);
        else
            writer.WriteInt32(TrainerType);

        writer.WriteUInt32(TrainerID);

        writer.WriteInt32(Spells.Count);
        foreach (var spell in Spells)
        {
            writer.WriteUInt32(spell.SpellID);
            writer.WriteUInt32(spell.MoneyCost);
            writer.WriteUInt32(spell.ReqSkillLine);
            writer.WriteUInt32(spell.ReqSkillRank);

            for (int i = 0; i < 3; ++i)
                writer.WriteUInt32(spell.ReqAbility[i]);

            if (Trainer256.Use553Layout)
                writer.WriteUInt32(0);          // Unk440

            writer.WriteUInt8((byte)spell.Usable);
            writer.WriteUInt8(spell.ReqLevel);
        }

        writer.WriteBits((uint)greetingBytes, 11);
        writer.FlushBits();
        writer.WriteString(Greeting ?? "");
        return writer.Position;
    }

    public WowGuid128 TrainerGUID;
    public int TrainerType;
    public uint TrainerID = 1;
    public List<TrainerListSpell> Spells = new();
    public string Greeting = string.Empty;
}

public class TrainerListSpell
{
    public uint SpellID;
    public uint MoneyCost;
    public uint ReqSkillLine;
    public uint ReqSkillRank;
    public uint[] ReqAbility = new uint[3];
    public TrainerSpellStateModern Usable;
    public byte ReqLevel;
}

class TrainerBuySpell : ClientPacket
{
    public TrainerBuySpell(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        TrainerGUID = _worldPacket.ReadPackedGuid128();
        TrainerID = _worldPacket.ReadUInt32();
        SpellID = _worldPacket.ReadUInt32();
    }

    public WowGuid128 TrainerGUID;
    public uint TrainerID;
    public uint SpellID;
}

class TrainerBuyFailed : ServerPacket, ISpanWritable
{
    public TrainerBuyFailed() : base(Opcode.SMSG_TRAINER_BUY_FAILED) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(TrainerGUID);
        _worldPacket.WriteUInt32(SpellID);
        _worldPacket.WriteUInt32(TrainerFailedReason);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 8; // GUID + 2 uints

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(TrainerGUID.Low, TrainerGUID.High);
        writer.WriteUInt32(SpellID);
        writer.WriteUInt32(TrainerFailedReason);
        return writer.Position;
    }

    public WowGuid128 TrainerGUID;
    public uint SpellID;
    public uint TrainerFailedReason;
}

class RespecWipeConfirm : ServerPacket, ISpanWritable
{
    public RespecWipeConfirm() : base(Opcode.SMSG_RESPEC_WIPE_CONFIRM) { }

    public override void Write()
    {
        _worldPacket.WriteInt8((sbyte)RespecType);
        _worldPacket.WriteUInt32(Cost);
        _worldPacket.WritePackedGuid128(TrainerGUID);
    }

    public int MaxSize => 5 + PackedGuidHelper.MaxPackedGuid128Size; // sbyte + uint + GUID

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteInt8((sbyte)RespecType);
        writer.WriteUInt32(Cost);
        writer.WritePackedGuid128(TrainerGUID.Low, TrainerGUID.High);
        return writer.Position;
    }

    public SpecResetType RespecType = SpecResetType.Talents;
    public uint Cost;
    public WowGuid128 TrainerGUID;
}

class ConfirmRespecWipe : ClientPacket
{
    public ConfirmRespecWipe(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        TrainerGUID = _worldPacket.ReadPackedGuid128();
        RespecType = (SpecResetType)_worldPacket.ReadUInt8();
    }

    public WowGuid128 TrainerGUID;
    public SpecResetType RespecType;
}

class GossipPOI : ServerPacket, ISpanWritable
{
    public GossipPOI() : base(Opcode.SMSG_GOSSIP_POI) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(Id);
        _worldPacket.WriteFloat(Pos.X);
        _worldPacket.WriteFloat(Pos.Y);
        _worldPacket.WriteFloat(Pos.Z);
        _worldPacket.WriteUInt32(Icon);
        _worldPacket.WriteUInt32(Importance);
        _worldPacket.WriteUInt32(Unknown905);
        _worldPacket.WriteBits(Flags, 14);
        _worldPacket.WriteBits(Name.GetByteCount(), 6);
        _worldPacket.FlushBits();
        _worldPacket.WriteString(Name);
    }

    // Cap for POI name - limited by 6 bits = 64 bytes max
    private const int MaxNameBytes = 64;
    // 4 uint(16) + 3 floats(12) + 3 bytes for bits + name
    public int MaxSize => 16 + 12 + 3 + MaxNameBytes;

    public int WriteToSpan(Span<byte> buffer)
    {
        int nameBytes = Encoding.UTF8.GetByteCount(Name);
        if (nameBytes > MaxNameBytes)
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(Id);
        writer.WriteFloat(Pos.X);
        writer.WriteFloat(Pos.Y);
        writer.WriteFloat(Pos.Z);
        writer.WriteUInt32(Icon);
        writer.WriteUInt32(Importance);
        writer.WriteUInt32(Unknown905);
        writer.WriteBits(Flags, 14);
        writer.WriteBits((uint)nameBytes, 6);
        writer.FlushBits();
        writer.WriteString(Name);
        return writer.Position;
    }

    public uint Id = 1;
    public uint Flags;
    public Vector3 Pos;
    public uint Icon;
    public uint Importance;
    public uint Unknown905;
    public string Name = string.Empty;
}

public class SpiritHealerConfirm : ServerPacket, ISpanWritable
{
    public SpiritHealerConfirm() : base(Opcode.SMSG_SPIRIT_HEALER_CONFIRM) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Guid);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(Guid.Low, Guid.High);
        return writer.Position;
    }

    public WowGuid128 Guid;
}
