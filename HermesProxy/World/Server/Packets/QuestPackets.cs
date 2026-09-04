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
using System.Text;

namespace HermesProxy.World.Server.Packets;

public class QuestGiverQueryQuest : ClientPacket
{
    public QuestGiverQueryQuest(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
        QuestID = _worldPacket.ReadUInt32();
        RespondToGiver = _worldPacket.HasBit();
    }

    public WowGuid128 QuestGiverGUID;
    public uint QuestID;
    public bool RespondToGiver;
}

public class QuestGiverQuestDetails : ServerPacket
{
    public QuestGiverQuestDetails() : base(Opcode.SMSG_QUEST_GIVER_QUEST_DETAILS)
    {
        for (int i = 0; i < QuestConst.QuestRewardReputationsCount; i++)
            Rewards.FactionCapIn[i] = 7;
    }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) layout, confirmed against the client's own reader (function 0x63A430,
            // called from dispatcher case 0x64043D): guid, guid, 12 u32 (incl. FlagsEx2/FlagsEx3),
            // 3 counts, StartItem, QuestInfoID, SessionBonus, QuestGiverCreatureID, conditional
            // text count, the three arrays (objectives are 4 x u32: Id, Type, ObjectID, Amount),
            // a 75-bit block (7 string lengths + 6 flag bits), rewards, 7 strings, conditional
            // texts. WPP V5_5_0_61735 QuestHandler.cs agrees field for field.
            _worldPacket.WritePackedGuid128(QuestGiverGUID);
            _worldPacket.WritePackedGuid128(InformUnit);
            _worldPacket.WriteUInt32(QuestID);
            _worldPacket.WriteInt32(QuestPackageID);
            _worldPacket.WriteUInt32(PortraitGiver);
            _worldPacket.WriteUInt32(PortraitGiverMount);
            _worldPacket.WriteUInt32(PortraitGiverModelSceneID);
            _worldPacket.WriteUInt32(PortraitTurnIn);
            _worldPacket.WriteUInt32(QuestFlags[0]); // Flags
            _worldPacket.WriteUInt32(QuestFlags[1]); // FlagsEx
            _worldPacket.WriteUInt32(0);             // FlagsEx2 - no legacy source
            _worldPacket.WriteUInt32(0);             // FlagsEx3 - no legacy source
            _worldPacket.WriteUInt32(SuggestedPartyMembers);
            _worldPacket.WriteInt32(LearnSpells.Count);
            _worldPacket.WriteInt32(DescEmotes.Length);
            _worldPacket.WriteInt32(Objectives.Count);
            _worldPacket.WriteInt32(QuestStartItemID);
            _worldPacket.WriteInt32(QuestInfoID);
            _worldPacket.WriteInt32(QuestSessionBonus);
            _worldPacket.WriteUInt32(QuestGiverGUID != null ? QuestGiverGUID.GetEntry() : 0); // QuestGiverCreatureID
            _worldPacket.WriteUInt32(0);             // ConditionalDescriptionText count

            foreach (uint spell in LearnSpells)
                _worldPacket.WriteUInt32(spell);

            foreach (QuestDescEmote emote in DescEmotes)
            {
                _worldPacket.WriteUInt32(emote.Type);
                _worldPacket.WriteUInt32(emote.Delay);
            }

            foreach (QuestObjectiveSimple obj in Objectives)
            {
                _worldPacket.WriteUInt32(obj.Id);
                _worldPacket.WriteUInt32(obj.Type);     // widened to u32 and moved before ObjectID
                _worldPacket.WriteInt32(obj.ObjectID);
                _worldPacket.WriteInt32(obj.Amount);
            }

            _worldPacket.WriteBits(QuestTitle.GetByteCount(), 9);
            _worldPacket.WriteBits(DescriptionText.GetByteCount(), 12);
            _worldPacket.WriteBits(LogDescription.GetByteCount(), 12);
            _worldPacket.WriteBits(PortraitGiverText.GetByteCount(), 10);
            _worldPacket.WriteBits(PortraitGiverName.GetByteCount(), 8);
            _worldPacket.WriteBits(PortraitTurnInText.GetByteCount(), 10);
            _worldPacket.WriteBits(PortraitTurnInName.GetByteCount(), 8);
            _worldPacket.WriteBit(AutoLaunched);
            _worldPacket.WriteBit(false);   // FromContentPush
            _worldPacket.WriteBit(false);   // Unused
            _worldPacket.WriteBit(false);   // ResetByScheduler
            _worldPacket.WriteBit(StartCheat);
            _worldPacket.WriteBit(DisplayPopup);
            _worldPacket.FlushBits();

            Rewards.Write(_worldPacket);

            _worldPacket.WriteString(QuestTitle);
            _worldPacket.WriteString(DescriptionText);
            _worldPacket.WriteString(LogDescription);
            _worldPacket.WriteString(PortraitGiverText);
            _worldPacket.WriteString(PortraitGiverName);
            _worldPacket.WriteString(PortraitTurnInText);
            _worldPacket.WriteString(PortraitTurnInName);
            // No conditional description texts (count written as 0 above).
            return;
        }

        _worldPacket.WritePackedGuid128(QuestGiverGUID);
        _worldPacket.WritePackedGuid128(InformUnit);
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteInt32(QuestPackageID);
        _worldPacket.WriteUInt32(PortraitGiver);
        _worldPacket.WriteUInt32(PortraitGiverMount);
        _worldPacket.WriteUInt32(PortraitGiverModelSceneID);
        _worldPacket.WriteUInt32(PortraitTurnIn);
        _worldPacket.WriteUInt32(QuestFlags[0]); // Flags
        _worldPacket.WriteUInt32(QuestFlags[1]); // FlagsEx
        _worldPacket.WriteUInt32(SuggestedPartyMembers);
        _worldPacket.WriteInt32(LearnSpells.Count);
        _worldPacket.WriteInt32(DescEmotes.Length);
        _worldPacket.WriteInt32(Objectives.Count);
        _worldPacket.WriteInt32(QuestStartItemID);
        _worldPacket.WriteInt32(QuestSessionBonus);

        foreach (uint spell in LearnSpells)
            _worldPacket.WriteUInt32(spell);

        foreach (QuestDescEmote emote in DescEmotes)
        {
            _worldPacket.WriteUInt32(emote.Type);
            _worldPacket.WriteUInt32(emote.Delay);
        }

        foreach (QuestObjectiveSimple obj in Objectives)
        {
            _worldPacket.WriteUInt32(obj.Id);
            _worldPacket.WriteInt32(obj.ObjectID);
            _worldPacket.WriteInt32(obj.Amount);
            _worldPacket.WriteUInt8((byte)obj.Type);
        }

        _worldPacket.WriteBits(QuestTitle.GetByteCount(), 9);
        _worldPacket.WriteBits(DescriptionText.GetByteCount(), 12);
        _worldPacket.WriteBits(LogDescription.GetByteCount(), 12);
        _worldPacket.WriteBits(PortraitGiverText.GetByteCount(), 10);
        _worldPacket.WriteBits(PortraitGiverName.GetByteCount(), 8);
        _worldPacket.WriteBits(PortraitTurnInText.GetByteCount(), 10);
        _worldPacket.WriteBits(PortraitTurnInName.GetByteCount(), 8);
        _worldPacket.WriteBit(AutoLaunched);
        _worldPacket.WriteBit(false);   // unused in client
        _worldPacket.WriteBit(StartCheat);
        _worldPacket.WriteBit(DisplayPopup);
        _worldPacket.FlushBits();

        Rewards.Write(_worldPacket);

        _worldPacket.WriteString(QuestTitle);
        _worldPacket.WriteString(DescriptionText);
        _worldPacket.WriteString(LogDescription);
        _worldPacket.WriteString(PortraitGiverText);
        _worldPacket.WriteString(PortraitGiverName);
        _worldPacket.WriteString(PortraitTurnInText);
        _worldPacket.WriteString(PortraitTurnInName);
    }

    public WowGuid128 QuestGiverGUID;
    public WowGuid128 InformUnit;
    public uint QuestID;
    public int QuestPackageID;
    public uint[] QuestFlags = new uint[2];
    public uint SuggestedPartyMembers;
    public QuestRewards Rewards = new();
    public List<QuestObjectiveSimple> Objectives = new();
    public QuestDescEmote[] DescEmotes = new QuestDescEmote[QuestConst.QuestEmoteCount];
    public List<uint> LearnSpells = new();
    public uint PortraitTurnIn;
    public uint PortraitGiver;
    public uint PortraitGiverMount;
    public uint PortraitGiverModelSceneID;
    public int QuestStartItemID;
    public int QuestInfoID;      // QuestInfo.db2 type (group/dungeon/raid/pvp); 0 when the legacy side has no source
    public int QuestSessionBonus;
    public string PortraitGiverText = "";
    public string PortraitGiverName = "";
    public string PortraitTurnInText = "";
    public string PortraitTurnInName = "";
    public string QuestTitle = "";
    public string DescriptionText = "";
    public string LogDescription = "";
    public bool DisplayPopup;
    public bool StartCheat;
    public bool AutoLaunched;
}

public class QuestRewards
{
    public QuestRewards()
    {
        for (int i = 0; i < QuestConst.QuestRewardChoicesCount; i++)
            ChoiceItems[i] = new();
    }
    public uint ChoiceItemCount;
    public uint ItemCount;
    public uint Money;
    public uint XP;
    public uint ArtifactXP;
    public uint ArtifactCategoryID;
    public uint Honor;
    public uint Title;
    public uint FactionFlags;
    public int[] SpellCompletionDisplayID = new int[QuestConst.QuestRewardDisplaySpellCount];
    public uint SpellCompletionID;
    public uint SkillLineID;
    public uint NumSkillUps;
    public uint TreasurePickerID;
    public QuestChoiceItem[] ChoiceItems = new QuestChoiceItem[QuestConst.QuestRewardChoicesCount];
    public uint[] ItemID = new uint[QuestConst.QuestRewardItemCount];
    public uint[] ItemQty = new uint[QuestConst.QuestRewardItemCount];
    public uint[] FactionID = new uint[QuestConst.QuestRewardReputationsCount];
    public int[] FactionValue = new int[QuestConst.QuestRewardReputationsCount];
    public int[] FactionOverride = new int[QuestConst.QuestRewardReputationsCount];
    public int[] FactionCapIn = new int[QuestConst.QuestRewardReputationsCount];
    public uint[] CurrencyID = new uint[QuestConst.QuestRewardCurrencyCount];
    public uint[] CurrencyQty = new uint[QuestConst.QuestRewardCurrencyCount];
    public bool IsBoostSpell;

    public void Write(WorldPacket data)
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) reward block, confirmed against the client's own reader (function
            // 0x58A620, shared by QUEST_DETAILS and OFFER_REWARD): 4x(ItemID,Qty),
            // 4x(CurrencyID,Qty,BonusQty), ChoiceItemCount, ItemCount, Money, XP, u64 ArtifactXP,
            // ArtifactCategoryID, Honor, Title, FactionFlags, 5x faction quad, 3x display spell,
            // SpellCompletionID, SkillLineID, NumSkillUps, treasure picker count + ids,
            // 6x choice item, IsBoostSpell bit. WPP V5_5_0_61735 ReadQuestRewards agrees.
            for (int i = 0; i < QuestConst.QuestRewardItemCount; ++i)
            {
                data.WriteUInt32(ItemID[i]);
                data.WriteUInt32(ItemQty[i]);
            }

            for (int i = 0; i < QuestConst.QuestRewardCurrencyCount; ++i)
            {
                data.WriteUInt32(CurrencyID[i]);
                data.WriteUInt32(CurrencyQty[i]);
                data.WriteUInt32(0); // CurrencyBonusQty - no legacy source
            }

            data.WriteUInt32(ChoiceItemCount);
            data.WriteUInt32(ItemCount);
            data.WriteUInt32(Money);
            data.WriteUInt32(XP);
            data.WriteUInt64(ArtifactXP);
            data.WriteUInt32(ArtifactCategoryID);
            data.WriteUInt32(Honor);
            data.WriteUInt32(Title);
            data.WriteUInt32(FactionFlags);

            for (int i = 0; i < QuestConst.QuestRewardReputationsCount; ++i)
            {
                data.WriteUInt32(FactionID[i]);
                data.WriteInt32(FactionValue[i]);
                data.WriteInt32(FactionOverride[i]);
                data.WriteInt32(FactionCapIn[i]);
            }

            foreach (var id in SpellCompletionDisplayID)
                data.WriteInt32(id);

            data.WriteUInt32(SpellCompletionID);
            data.WriteUInt32(SkillLineID);
            data.WriteUInt32(NumSkillUps);

            // Dynamic treasure picker list replaces the single u32 of the 9.x layout.
            if (TreasurePickerID != 0)
            {
                data.WriteUInt32(1);
                data.WriteUInt32(TreasurePickerID);
            }
            else
                data.WriteUInt32(0);

            foreach (var choice in ChoiceItems)
                choice.Write(data);

            data.WriteBit(IsBoostSpell);
            data.FlushBits();
            return;
        }

        data.WriteUInt32(ChoiceItemCount);
        data.WriteUInt32(ItemCount);

        for (int i = 0; i < QuestConst.QuestRewardItemCount; ++i)
        {
            data.WriteUInt32(ItemID[i]);
            data.WriteUInt32(ItemQty[i]);
        }

        data.WriteUInt32(Money);
        data.WriteUInt32(XP);
        data.WriteUInt64(ArtifactXP);
        data.WriteUInt32(ArtifactCategoryID);
        data.WriteUInt32(Honor);
        data.WriteUInt32(Title);
        data.WriteUInt32(FactionFlags);

        for (int i = 0; i < QuestConst.QuestRewardReputationsCount; ++i)
        {
            data.WriteUInt32(FactionID[i]);
            data.WriteInt32(FactionValue[i]);
            data.WriteInt32(FactionOverride[i]);
            data.WriteInt32(FactionCapIn[i]);
        }

        foreach (var id in SpellCompletionDisplayID)
            data.WriteInt32(id);

        data.WriteUInt32(SpellCompletionID);

        for (int i = 0; i < QuestConst.QuestRewardCurrencyCount; ++i)
        {
            data.WriteUInt32(CurrencyID[i]);
            data.WriteUInt32(CurrencyQty[i]);
        }

        data.WriteUInt32(SkillLineID);
        data.WriteUInt32(NumSkillUps);
        data.WriteUInt32(TreasurePickerID);

        foreach (var choice in ChoiceItems)
            choice.Write(data);

        data.WriteBit(IsBoostSpell);
        data.FlushBits();
    }
}

public class QuestChoiceItem
{
    public byte LootItemType;
    public ItemInstance Item = new();
    public uint Quantity;

    public void Read(WorldPacket data)
    {
        data.ResetBitPos();
        LootItemType = data.ReadBits<byte>(2);
        Item.Read(data);
        Quantity = data.ReadUInt32();
    }

    public void Write(WorldPacket data)
    {
        data.WriteBits(LootItemType, 2);
        Item.Write(data);
        data.WriteUInt32(Quantity);
    }
}

public struct QuestObjectiveSimple
{
    public uint Id;
    public int ObjectID;
    public int Amount;
    public byte Type;
}

public struct QuestDescEmote
{
    public uint Type;
    public uint Delay;
}

public class QuestGiverAcceptQuest : ClientPacket
{
    public QuestGiverAcceptQuest(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
        QuestID = _worldPacket.ReadUInt32();
        StartCheat = _worldPacket.HasBit();
    }

    public WowGuid128 QuestGiverGUID;
    public uint QuestID;
    public bool StartCheat;

}

public class QuestLogRemoveQuest : ClientPacket
{
    public QuestLogRemoveQuest(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        Slot = _worldPacket.ReadUInt8();
    }

    public byte Slot;
}

public class QuestGiverStatusQuery : ClientPacket
{
    public QuestGiverStatusQuery(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
    }

    public WowGuid128 QuestGiverGUID;
}

public class QuestGiverStatusMultipleQuery : ClientPacket
{
    public QuestGiverStatusMultipleQuery(WorldPacket packet) : base(packet) { }

    public override void Read() { }
}

/// <summary>
/// Translates the 9.x-era <see cref="QuestGiverStatusModern"/> flags into the 5.5.x u64 flag set
/// the 2.5.6 (69110) client reads.
///
/// The wire WIDTH is confirmed against the client's own reader: the SMSG_QUEST_GIVER_STATUS
/// dispatcher case (0x64001B, case RVA 0x6408CD) reads packed guid + u64, and the
/// SMSG_QUEST_GIVER_STATUS_MULTIPLE case (0x640011) reads u32 count then guid + u64 per entry.
/// The VALUE mapping below is inferred from TrinityCore master / WPP V5_5_0_61735 enum usage:
/// "Quest" (0x400000) is the available '!', "RewardCompletePOI" (0x400000000) the turn-in '?',
/// "Reward" (0x2000) the incomplete grey '?'. If a marker glyph renders wrong in game, adjust
/// this table - the u64 width is not in question.
/// </summary>
public static class QuestGiverStatus550
{
    public static ulong Convert(QuestGiverStatusModern status)
    {
        ulong result = 0;
        void Map(QuestGiverStatusModern from, ulong to)
        {
            if (status.HasAnyFlag(from))
                result |= to;
        }

        Map(QuestGiverStatusModern.Unavailable,               0x000000000002); // Future
        Map(QuestGiverStatusModern.LowLevelAvailable,         0x000000000040); // Trivial
        Map(QuestGiverStatusModern.LowLevelRewardRep,         0x000000000020); // TrivialRepeatableTurnin
        Map(QuestGiverStatusModern.LowLevelAvailableRep,      0x000000000100); // TrivialRepeatableQuest
        Map(QuestGiverStatusModern.Incomplete,                0x000000002000); // Reward (incomplete grey '?')
        Map(QuestGiverStatusModern.IncompleteJourney,         0x000000010000); // JourneyReward
        Map(QuestGiverStatusModern.IncompleteCovenantCalling, 0x000000020000); // CovenantCallingReward
        Map(QuestGiverStatusModern.RewardRep,                 0x000000100000); // RepeatableTurnin
        Map(QuestGiverStatusModern.AvailableRep,              0x000001000000); // RepeatableQuest
        Map(QuestGiverStatusModern.Available,                 0x000000400000); // Quest ('!')
        Map(QuestGiverStatusModern.Reward2,                   0x000200000000); // RewardCompleteNoPOI
        Map(QuestGiverStatusModern.Reward,                    0x000400000000); // RewardCompletePOI ('?')
        Map(QuestGiverStatusModern.AvailableLegendaryQuest,   0x000040000000); // LegendaryQuest
        Map(QuestGiverStatusModern.Reward2Legendary,          0x080000000000); // LegendaryRewardCompleteNoPOI
        Map(QuestGiverStatusModern.RewardLegendary,           0x100000000000); // LegendaryRewardCompletePOI
        Map(QuestGiverStatusModern.AvailableJourney,          0x000010000000); // JourneyQuest
        Map(QuestGiverStatusModern.Reward2Journey,            0x020000000000); // JourneyRewardCompleteNoPOI
        Map(QuestGiverStatusModern.RewardJourney,             0x040000000000); // JourneyRewardCompletePOI
        Map(QuestGiverStatusModern.AvailableCovenantCalling,  0x000004000000); // CovenantCallingQuest
        Map(QuestGiverStatusModern.Reward2CovenantCalling,    0x008000000000); // CovenantCallingRewardCompleteNoPOI
        Map(QuestGiverStatusModern.RewardCovenantCalling,     0x010000000000); // CovenantCallingRewardCompletePOI
        return result;
    }
}

public class QuestGiverStatusPkt : ServerPacket, ISpanWritable
{
    public QuestGiverStatusPkt() : base(Opcode.SMSG_QUEST_GIVER_STATUS, ConnectionType.Instance)
    {
        QuestGiver = new QuestGiverInfo();
    }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(QuestGiver.Guid);
        // 2.5.6 (5.5.0 engine) reads Status as u64 - confirmed against the client's own reader
        // (dispatcher case 0x64001B: packed guid + u64). Older moderns read u32.
        if (ModernVersion.Uses550Engine)
            _worldPacket.WriteUInt64(QuestGiverStatus550.Convert(QuestGiver.Status));
        else
            _worldPacket.WriteUInt32((uint)QuestGiver.Status);
    }

    // GUID(18) + up to ulong(8) = 26 bytes
    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 8;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(QuestGiver.Guid.Low, QuestGiver.Guid.High);
        if (ModernVersion.Uses550Engine)
            writer.WriteUInt64(QuestGiverStatus550.Convert(QuestGiver.Status));
        else
            writer.WriteUInt32((uint)QuestGiver.Status);
        return writer.Position;
    }

    public QuestGiverInfo QuestGiver;
}

public class QuestGiverStatusMultiple : ServerPacket, ISpanWritable
{
    public QuestGiverStatusMultiple() : base(Opcode.SMSG_QUEST_GIVER_STATUS_MULTIPLE, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteInt32(QuestGivers.Count);
        foreach (QuestGiverInfo questGiver in QuestGivers)
        {
            _worldPacket.WritePackedGuid128(questGiver.Guid);
            // Same u64 widening as SMSG_QUEST_GIVER_STATUS - confirmed by the client's
            // STATUS_MULTIPLE case (0x640011): u32 count, then guid + u64 per entry.
            if (ModernVersion.Uses550Engine)
                _worldPacket.WriteUInt64(QuestGiverStatus550.Convert(questGiver.Status));
            else
                _worldPacket.WriteUInt32((uint)questGiver.Status);
        }
    }

    // Cap for quest givers in view - typically only a handful visible at once
    private const int MaxQuestGivers = 32;
    // Each entry: PackedGuid128 (18) + up to ulong (8) = 26 bytes
    public int MaxSize => 4 + MaxQuestGivers * (PackedGuidHelper.MaxPackedGuid128Size + 8);

    public int WriteToSpan(Span<byte> buffer)
    {
        if (QuestGivers.Count > MaxQuestGivers)
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WriteInt32(QuestGivers.Count);
        foreach (QuestGiverInfo questGiver in QuestGivers)
        {
            writer.WritePackedGuid128(questGiver.Guid.Low, questGiver.Guid.High);
            if (ModernVersion.Uses550Engine)
                writer.WriteUInt64(QuestGiverStatus550.Convert(questGiver.Status));
            else
                writer.WriteUInt32((uint)questGiver.Status);
        }
        return writer.Position;
    }

    public List<QuestGiverInfo> QuestGivers = new();
}

public class QuestGiverInfo
{
    public QuestGiverInfo() { }
    public QuestGiverInfo(WowGuid128 guid, QuestGiverStatusModern status)
    {
        Guid = guid;
        Status = status;
    }

    public WowGuid128 Guid;
    public QuestGiverStatusModern Status = QuestGiverStatusModern.None;
}

public class QuestGiverHello : ClientPacket
{
    public QuestGiverHello(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
    }

    public WowGuid128 QuestGiverGUID;
}

public class QuestGiverQuestListMessage : ServerPacket
{
    public QuestGiverQuestListMessage() : base(Opcode.SMSG_QUEST_GIVER_QUEST_LIST_MESSAGE) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(QuestGiverGUID);
        _worldPacket.WriteUInt32(GreetEmoteDelay);
        _worldPacket.WriteUInt32(GreetEmoteType);
        _worldPacket.WriteInt32(QuestOptions.Count);
        _worldPacket.WriteBits(Greeting.GetByteCount(), 11);
        _worldPacket.FlushBits();

        foreach (ClientGossipQuest quest in QuestOptions)
            quest.Write(_worldPacket);

        _worldPacket.WriteString(Greeting);
    }

    public WowGuid128 QuestGiverGUID;
    public uint GreetEmoteDelay;
    public uint GreetEmoteType;
    public List<ClientGossipQuest> QuestOptions = new();
    public string Greeting = "";
}

public class QuestGiverRequestItems : ServerPacket
{
    public QuestGiverRequestItems() : base(Opcode.SMSG_QUEST_GIVER_REQUEST_ITEMS) { }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) layout, confirmed against the client's own reader (function 0x58AB40
            // + case tail 0x63ABC0, dispatcher case 0x6404A5): CollectCount and CurrencyCount
            // FIRST, then guid, FOUR flag u32s, StatusFlags, QuestGiverCreatureID, QuestID,
            // EmoteDelay, EmoteType, SuggestPartyMembers, MoneyToGet, QuestInfoID, the two
            // arrays, a 2-bit block, then QuestGiverCreatureID again, conditional text count,
            // 9+12 bit lengths, conditional texts, title, completion text.
            // WPP V5_5_0_61735 HandleQuestGiverRequestItems agrees field for field.
            _worldPacket.WriteInt32(Collect.Count);
            _worldPacket.WriteInt32(Currency.Count);
            _worldPacket.WritePackedGuid128(QuestGiverGUID);
            _worldPacket.WriteUInt32(QuestFlags[0]);
            _worldPacket.WriteUInt32(QuestFlags[1]);
            _worldPacket.WriteUInt32(0); // FlagsEx2 - no legacy source
            _worldPacket.WriteUInt32(0); // FlagsEx3 - no legacy source
            _worldPacket.WriteUInt32(StatusFlags);
            _worldPacket.WriteUInt32(QuestGiverCreatureID);
            _worldPacket.WriteUInt32(QuestID);
            _worldPacket.WriteUInt32(CompEmoteDelay);
            _worldPacket.WriteUInt32(CompEmoteType);
            _worldPacket.WriteUInt32(SuggestPartyMembers);
            _worldPacket.WriteInt32(MoneyToGet);
            _worldPacket.WriteInt32(0); // QuestInfoID - no legacy source

            foreach (QuestObjectiveCollect obj in Collect)
            {
                _worldPacket.WriteUInt32(obj.ObjectID);
                _worldPacket.WriteUInt32(obj.Amount);
                _worldPacket.WriteUInt32(obj.Flags);
            }
            foreach (QuestCurrency cur in Currency)
            {
                _worldPacket.WriteUInt32(cur.CurrencyID);
                _worldPacket.WriteInt32(cur.Amount);
            }

            _worldPacket.WriteBit(AutoLaunched);
            _worldPacket.WriteBit(false); // ResetByScheduler
            _worldPacket.FlushBits();

            _worldPacket.WriteUInt32(QuestGiverCreatureID); // repeated for conditional text selection
            _worldPacket.WriteUInt32(0);                    // ConditionalCompletionText count

            _worldPacket.WriteBits(QuestTitle.GetByteCount(), 9);
            _worldPacket.WriteBits(CompletionText.GetByteCount(), 12);
            _worldPacket.FlushBits();

            // No conditional texts (count written as 0 above).
            _worldPacket.WriteString(QuestTitle);
            _worldPacket.WriteString(CompletionText);
            return;
        }

        _worldPacket.WritePackedGuid128(QuestGiverGUID);
        _worldPacket.WriteUInt32(QuestGiverCreatureID);
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteUInt32(CompEmoteDelay);
        _worldPacket.WriteUInt32(CompEmoteType);
        _worldPacket.WriteUInt32(QuestFlags[0]);
        _worldPacket.WriteUInt32(QuestFlags[1]);
        _worldPacket.WriteUInt32(SuggestPartyMembers);
        _worldPacket.WriteInt32(MoneyToGet);
        _worldPacket.WriteInt32(Collect.Count);
        _worldPacket.WriteInt32(Currency.Count);
        _worldPacket.WriteUInt32(StatusFlags);

        foreach (QuestObjectiveCollect obj in Collect)
        {
            _worldPacket.WriteUInt32(obj.ObjectID);
            _worldPacket.WriteUInt32(obj.Amount);
            _worldPacket.WriteUInt32(obj.Flags);
        }
        foreach (QuestCurrency cur in Currency)
        {
            _worldPacket.WriteUInt32(cur.CurrencyID);
            _worldPacket.WriteInt32(cur.Amount);
        }

        _worldPacket.WriteBit(AutoLaunched);
        _worldPacket.FlushBits();

        _worldPacket.WriteBits(QuestTitle.GetByteCount(), 9);
        _worldPacket.WriteBits(CompletionText.GetByteCount(), 12);

        _worldPacket.WriteString(QuestTitle);
        _worldPacket.WriteString(CompletionText);
    }

    public WowGuid128 QuestGiverGUID;
    public uint QuestGiverCreatureID;
    public uint QuestID;
    public uint CompEmoteDelay;
    public uint CompEmoteType;
    public bool AutoLaunched;
    public uint SuggestPartyMembers;
    public int MoneyToGet;
    public List<QuestObjectiveCollect> Collect = new();
    public List<QuestCurrency> Currency = new();
    public uint StatusFlags;
    public uint[] QuestFlags = new uint[2];
    public string QuestTitle = "";
    public string CompletionText = "";
}

public struct QuestObjectiveCollect
{
    public uint ObjectID;
    public uint Amount;
    public uint Flags;
}

public struct QuestCurrency
{
    public uint CurrencyID;
    public int Amount;
}

public class QuestGiverRequestReward : ClientPacket
{
    public QuestGiverRequestReward(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
        QuestID = _worldPacket.ReadUInt32();
    }

    public WowGuid128 QuestGiverGUID;
    public uint QuestID;
}

public class QuestGiverOfferRewardMessage : ServerPacket
{
    public QuestGiverOfferRewardMessage() : base(Opcode.SMSG_QUEST_GIVER_OFFER_REWARD_MESSAGE) { }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) outer layout, confirmed against the client's own reader (function
            // 0x63AD90, dispatcher case 0x6404DE): inner data block (see QuestGiverOfferReward),
            // then QuestPackageID, the four portrait ids, QuestGiverCreatureID, conditional text
            // count, a 57-bit length block, conditional texts, six strings.
            // WPP V5_5_0_61735 QuestGiverOfferReward agrees field for field.
            QuestData.Write(_worldPacket);
            _worldPacket.WriteUInt32(QuestPackageID);
            _worldPacket.WriteUInt32(PortraitGiver);
            _worldPacket.WriteUInt32(PortraitGiverMount);
            _worldPacket.WriteUInt32(PortraitGiverModelSceneID);
            _worldPacket.WriteUInt32(PortraitTurnIn);
            _worldPacket.WriteUInt32(QuestData.QuestGiverCreatureID); // repeated for conditional text selection
            _worldPacket.WriteUInt32(0);                              // ConditionalRewardText count

            _worldPacket.WriteBits(QuestTitle.GetByteCount(), 9);
            _worldPacket.WriteBits(RewardText.GetByteCount(), 12);
            _worldPacket.WriteBits(PortraitGiverText.GetByteCount(), 10);
            _worldPacket.WriteBits(PortraitGiverName.GetByteCount(), 8);
            _worldPacket.WriteBits(PortraitTurnInText.GetByteCount(), 10);
            _worldPacket.WriteBits(PortraitTurnInName.GetByteCount(), 8);
            _worldPacket.FlushBits();

            // No conditional texts (count written as 0 above).
            _worldPacket.WriteString(QuestTitle);
            _worldPacket.WriteString(RewardText);
            _worldPacket.WriteString(PortraitGiverText);
            _worldPacket.WriteString(PortraitGiverName);
            _worldPacket.WriteString(PortraitTurnInText);
            _worldPacket.WriteString(PortraitTurnInName);
            return;
        }

        QuestData.Write(_worldPacket);
        _worldPacket.WriteUInt32(QuestPackageID);
        _worldPacket.WriteUInt32(PortraitGiver);
        _worldPacket.WriteUInt32(PortraitGiverMount);
        _worldPacket.WriteUInt32(PortraitGiverModelSceneID);
        _worldPacket.WriteUInt32(PortraitTurnIn);

        _worldPacket.WriteBits(QuestTitle.GetByteCount(), 9);
        _worldPacket.WriteBits(RewardText.GetByteCount(), 12);
        _worldPacket.WriteBits(PortraitGiverText.GetByteCount(), 10);
        _worldPacket.WriteBits(PortraitGiverName.GetByteCount(), 8);
        _worldPacket.WriteBits(PortraitTurnInText.GetByteCount(), 10);
        _worldPacket.WriteBits(PortraitTurnInName.GetByteCount(), 8);

        _worldPacket.WriteString(QuestTitle);
        _worldPacket.WriteString(RewardText);
        _worldPacket.WriteString(PortraitGiverText);
        _worldPacket.WriteString(PortraitGiverName);
        _worldPacket.WriteString(PortraitTurnInText);
        _worldPacket.WriteString(PortraitTurnInName);
    }

    public uint PortraitTurnIn;
    public uint PortraitGiver;
    public uint PortraitGiverMount;
    public uint PortraitGiverModelSceneID;
    public string QuestTitle = "";
    public string RewardText = "";
    public string PortraitGiverText = "";
    public string PortraitGiverName = "";
    public string PortraitTurnInText = "";
    public string PortraitTurnInName = "";
    public QuestGiverOfferReward QuestData = new();
    public uint QuestPackageID;
}

public class QuestGiverOfferReward
{
    public void Write(WorldPacket data)
    {
        if (ModernVersion.Uses550Engine)
        {
            // 2.5.6 (69110) inner layout, confirmed against the client's own reader (0x63AD90):
            // the REWARDS BLOCK comes first, then EmotesCount, guid, Flags, FlagsEx, FlagsEx2,
            // FlagsEx3, QuestGiverCreatureID, QuestID, SuggestedPartyMembers, QuestInfoID, the
            // emote array, and a 3-bit block. WPP ReadQuestGiverOfferRewardData agrees.
            Rewards.Write(data);
            data.WriteInt32(Emotes.Count);
            data.WritePackedGuid128(QuestGiverGUID);
            data.WriteUInt32(QuestFlags[0]); // Flags
            data.WriteUInt32(QuestFlags[1]); // FlagsEx
            data.WriteUInt32(0);             // FlagsEx2 - no legacy source
            data.WriteUInt32(0);             // FlagsEx3 - no legacy source
            data.WriteUInt32(QuestGiverCreatureID);
            data.WriteUInt32(QuestID);
            data.WriteUInt32(SuggestedPartyMembers);
            data.WriteInt32(0);              // QuestInfoID - no legacy source

            foreach (QuestDescEmote emote in Emotes)
            {
                data.WriteUInt32(emote.Type);
                data.WriteUInt32(emote.Delay);
            }

            data.WriteBit(AutoLaunched);
            data.WriteBit(false);   // Unused
            data.WriteBit(false);   // ResetByScheduler
            data.FlushBits();
            return;
        }

        data.WritePackedGuid128(QuestGiverGUID);
        data.WriteUInt32(QuestGiverCreatureID);
        data.WriteUInt32(QuestID);
        data.WriteUInt32(QuestFlags[0]); // Flags
        data.WriteUInt32(QuestFlags[1]); // FlagsEx
        data.WriteUInt32(SuggestedPartyMembers);

        data.WriteInt32(Emotes.Count);
        foreach (QuestDescEmote emote in Emotes)
        {
            data.WriteUInt32(emote.Type);
            data.WriteUInt32(emote.Delay);
        }

        data.WriteBit(AutoLaunched);
        data.WriteBit(false);   // Unused
        data.FlushBits();

        Rewards.Write(data);
    }

    public WowGuid128 QuestGiverGUID;
    public uint QuestGiverCreatureID = 0;
    public uint QuestID = 0;
    public bool AutoLaunched = false;
    public uint SuggestedPartyMembers = 0;
    public QuestRewards Rewards = new();
    public List<QuestDescEmote> Emotes = new();
    public uint[] QuestFlags = new uint[2]; // Flags and FlagsEx
}

public class QuestGiverChooseReward : ClientPacket
{
    public QuestGiverChooseReward(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
        QuestID = _worldPacket.ReadUInt32();
        Choice.Read(_worldPacket);
    }

    public WowGuid128 QuestGiverGUID;
    public uint QuestID;
    public QuestChoiceItem Choice = new();
}

public class QuestGiverQuestComplete : ServerPacket
{
    public QuestGiverQuestComplete() : base(Opcode.SMSG_QUEST_GIVER_QUEST_COMPLETE) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteUInt32(XPReward);
        _worldPacket.WriteInt64(MoneyReward);
        _worldPacket.WriteUInt32(SkillLineIDReward);
        _worldPacket.WriteUInt32(NumSkillUpsReward);

        _worldPacket.WriteBit(UseQuestReward);
        _worldPacket.WriteBit(LaunchGossip);
        _worldPacket.WriteBit(LaunchQuest);
        _worldPacket.WriteBit(HideChatMessage);

        ItemReward.Write(_worldPacket);
    }

    public uint QuestID;
    public uint XPReward;
    public long MoneyReward;
    public uint SkillLineIDReward;
    public uint NumSkillUpsReward;
    public bool UseQuestReward;
    public bool LaunchGossip;
    public bool LaunchQuest = true;
    public bool HideChatMessage;
    public ItemInstance ItemReward = new();
}

public class DisplayToast : ServerPacket
{
    public DisplayToast() : base(Opcode.SMSG_DISPLAY_TOAST, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(Quantity);
        _worldPacket.WriteUInt8(DisplayToastMethod);
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteBit(Mailed);
        _worldPacket.WriteBits(Type, 2);

        if (Type == 0)
        {
            _worldPacket.WriteBit(BonusRoll);
            _worldPacket.FlushBits();
            ItemReward.Write(_worldPacket);
            _worldPacket.WriteUInt32(SpecializationID);
            _worldPacket.WriteUInt32(ItemQuantity);
        }
        else
            _worldPacket.FlushBits();

        if (Type == 1)
            _worldPacket.WriteUInt32(CurrencyID);
    }

    public ulong Quantity;
    public byte DisplayToastMethod = 16;
    public uint QuestID;
    public bool Mailed;
    public byte Type;
    public bool BonusRoll;
    public ItemInstance ItemReward = new();
    public uint SpecializationID;
    public uint ItemQuantity;
    public uint CurrencyID;
}

public class QuestGiverCompleteQuest : ClientPacket
{
    public QuestGiverCompleteQuest(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestGiverGUID = _worldPacket.ReadPackedGuid128();
        QuestID = _worldPacket.ReadUInt32();
        FromScript = _worldPacket.HasBit();
    }

    public WowGuid128 QuestGiverGUID; // NPC / GameObject guid for normal quest completion. Player guid for self-completed quests
    public uint QuestID;
    public bool FromScript; // 0 - standart complete quest mode with npc, 1 - auto-complete mode
}

class QuestGiverQuestFailed : ServerPacket, ISpanWritable
{
    public QuestGiverQuestFailed() : base(Opcode.SMSG_QUEST_GIVER_QUEST_FAILED) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteUInt32((uint)Reason);
    }

    public int MaxSize => 8; // 2 uints

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(QuestID);
        writer.WriteUInt32((uint)Reason);
        return writer.Position;
    }

    public uint QuestID;
    public InventoryResult Reason;
}

class QuestGiverInvalidQuest : ServerPacket, ISpanWritable
{
    public QuestGiverInvalidQuest() : base(Opcode.SMSG_QUEST_GIVER_INVALID_QUEST) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32((uint)Reason);
        _worldPacket.WriteInt32(ContributionRewardID);

        _worldPacket.WriteBit(SendErrorMessage);
        _worldPacket.WriteBits(ReasonText.GetByteCount(), 9);
        _worldPacket.FlushBits();

        _worldPacket.WriteString(ReasonText);
    }

    // Cap for reason text - usually short error messages
    private const int MaxReasonTextBytes = 256;
    // uint(4) + int(4) + 10 bits(2) + text
    public int MaxSize => 4 + 4 + 2 + MaxReasonTextBytes;

    public int WriteToSpan(Span<byte> buffer)
    {
        int textBytes = Encoding.UTF8.GetByteCount(ReasonText);
        if (textBytes > MaxReasonTextBytes)
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32((uint)Reason);
        writer.WriteInt32(ContributionRewardID);
        writer.WriteBit(SendErrorMessage);
        writer.WriteBits((uint)textBytes, 9);
        writer.FlushBits();
        writer.WriteString(ReasonText);
        return writer.Position;
    }

    public QuestFailedReasons Reason;
    public int ContributionRewardID;
    public bool SendErrorMessage = true;
    public string ReasonText = "";
}

class QuestUpdateStatus : ServerPacket, ISpanWritable
{
    public QuestUpdateStatus(Opcode opcode) : base(opcode) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(QuestID);
    }

    public int MaxSize => 4; // uint

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(QuestID);
        return writer.Position;
    }

    public uint QuestID;
}
public class QuestUpdateAddCredit : ServerPacket, ISpanWritable
{
    public QuestUpdateAddCredit() : base(Opcode.SMSG_QUEST_UPDATE_ADD_CREDIT, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(VictimGUID);
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteInt32(ObjectID);
        _worldPacket.WriteUInt16(Count);
        _worldPacket.WriteUInt16(Required);
        // ObjectiveType is a uint32 on the 5.5.0 engine (TrinityCore QuestPackets.cpp:265,
        // `_worldPacket << uint32(ObjectiveType)`), a byte before it. Sending the byte made the
        // packet 21 bytes where the client reads 24: an UNDER-send, the class that faults readers,
        // and no objective ever printed "Boar slain: 1/8" in chat or over the top of the screen.
        if (ModernVersion.Uses550Engine)
            _worldPacket.WriteUInt32((uint)ObjectiveType);
        else
            _worldPacket.WriteUInt8((byte)ObjectiveType);
    }

    // GUID + uint + int + 2 ushorts + the objective type, four bytes wide on 5.5.0
    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 16;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(VictimGUID.Low, VictimGUID.High);
        writer.WriteUInt32(QuestID);
        writer.WriteInt32(ObjectID);
        writer.WriteUInt16(Count);
        writer.WriteUInt16(Required);
        if (ModernVersion.Uses550Engine)
            writer.WriteUInt32((uint)ObjectiveType);
        else
            writer.WriteUInt8((byte)ObjectiveType);
        return writer.Position;
    }

    public WowGuid128 VictimGUID;
    public int ObjectID;
    public uint QuestID;
    public ushort Count;
    public ushort Required;
    public QuestObjectiveType ObjectiveType;
}

class QuestUpdateAddCreditSimple : ServerPacket, ISpanWritable
{
    public QuestUpdateAddCreditSimple() : base(Opcode.SMSG_QUEST_UPDATE_ADD_CREDIT_SIMPLE, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WriteInt32(ObjectID);
        _worldPacket.WriteUInt8((byte)ObjectiveType);
    }

    public int MaxSize => 9; // uint + int + byte

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(QuestID);
        writer.WriteInt32(ObjectID);
        writer.WriteUInt8((byte)ObjectiveType);
        return writer.Position;
    }

    public uint QuestID;
    public int ObjectID;
    public QuestObjectiveType ObjectiveType;
}

class QuestConfirmAccept : ServerPacket, ISpanWritable
{
    public QuestConfirmAccept() : base(Opcode.SMSG_QUEST_CONFIRM_ACCEPT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(QuestID);
        _worldPacket.WritePackedGuid128(InitiatedBy);

        _worldPacket.WriteBits(QuestTitle.GetByteCount(), 10);
        _worldPacket.WriteString(QuestTitle);
    }

    // Cap for quest title - most are well under 128 bytes
    private const int MaxTitleBytes = 128;
    // uint(4) + GUID(18) + 10 bits(2) + title
    public int MaxSize => 4 + PackedGuidHelper.MaxPackedGuid128Size + 2 + MaxTitleBytes;

    public int WriteToSpan(Span<byte> buffer)
    {
        int titleBytes = Encoding.UTF8.GetByteCount(QuestTitle);
        if (titleBytes > MaxTitleBytes)
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(QuestID);
        writer.WritePackedGuid128(InitiatedBy.Low, InitiatedBy.High);
        writer.WriteBits((uint)titleBytes, 10);
        writer.WriteString(QuestTitle);
        return writer.Position;
    }

    public WowGuid128 InitiatedBy;
    public uint QuestID;
    public string QuestTitle = string.Empty;
}

class QuestConfirmAcceptResponse : ClientPacket
{
    public QuestConfirmAcceptResponse(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestID = _worldPacket.ReadUInt32();
    }

    public uint QuestID;
}

class PushQuestToParty : ClientPacket
{
    public PushQuestToParty(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        QuestID = _worldPacket.ReadUInt32();
    }

    public uint QuestID;
}

class QuestPushResult : ServerPacket, ISpanWritable
{
    public QuestPushResult() : base(Opcode.SMSG_QUEST_PUSH_RESULT) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(SenderGUID);
        _worldPacket.WriteUInt8((byte)Result);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 1; // GUID + byte

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(SenderGUID.Low, SenderGUID.High);
        writer.WriteUInt8((byte)Result);
        return writer.Position;
    }

    public WowGuid128 SenderGUID;
    public QuestPushReason Result;
}

class QuestPushResultResponse : ClientPacket
{
    public QuestPushResultResponse(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        SenderGUID = _worldPacket.ReadPackedGuid128();
        QuestID = _worldPacket.ReadUInt32();
        Result = (QuestPushReason)_worldPacket.ReadUInt8();
    }

    public WowGuid128 SenderGUID;
    public uint QuestID;
    public QuestPushReason Result;
}
