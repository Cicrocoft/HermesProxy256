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


using System;
using Framework.Constants;
using Framework.GameMath;
using Framework.IO;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using System.Collections.Generic;

namespace HermesProxy.World.Server.Packets;

public class InitializeFactions : ServerPacket, ISpanWritable
{
    const ushort FactionCount = 400;

    public InitializeFactions() : base(Opcode.SMSG_INITIALIZE_FACTIONS, ConnectionType.Instance) { }

    // 5.5.0-engine body, verified against the 69110 client's own message reader
    // (ctor rva 0x5BFBC0: u32 count, u32 bonusCount, count x { u32, u16, u32 },
    // bonusCount x { u32, u8 }; the bonus bit is flushed to a full byte per entry):
    //   i32 FactionCount, i32 BonusCount
    //   FactionCount x { u32 FactionID, u16 Flags, i32 Standing }
    //   BonusCount   x { u32 FactionID, 1 bit FactionHasBonus (FlushBits per entry) }
    // Entries carry Faction IDs, not reputation-index slots, so the legacy slots are
    // translated through GameData.GetFactionIdByReputationIndex; slots with no mapping
    // are gaps in the reputation index space and are skipped. Within the 10-byte entry
    // the u16 pins Flags; FactionID-before-Standing follows WPP 5.5.0 and TrinityCore.
    // The low flag byte is bit-compatible between legacy and modern (Visible/AtWar/
    // Hidden/ForcedInvisible/Peaceful/Inactive/ShowPropagated/HeaderShowsBar).
    void Write550(ref SpanPacketWriter writer)
    {
        int count = 0;
        for (uint i = 0; i < FactionCount; ++i)
            if (GameData.GetFactionIdByReputationIndex(i) != 0)
                ++count;

        writer.WriteInt32(count);
        writer.WriteInt32(0); // BonusCount - legacy data has no faction bonus info

        for (uint i = 0; i < FactionCount; ++i)
        {
            uint factionId = GameData.GetFactionIdByReputationIndex(i);
            if (factionId == 0)
                continue;
            writer.WriteUInt32(factionId);
            writer.WriteUInt16((ushort)((ushort)FactionFlags[i] & 0xFF));
            writer.WriteInt32(FactionStandings[i]);
        }
    }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            int count = 0;
            for (uint i = 0; i < FactionCount; ++i)
                if (GameData.GetFactionIdByReputationIndex(i) != 0)
                    ++count;

            _worldPacket.WriteInt32(count);
            _worldPacket.WriteInt32(0); // BonusCount - legacy data has no faction bonus info

            for (uint i = 0; i < FactionCount; ++i)
            {
                uint factionId = GameData.GetFactionIdByReputationIndex(i);
                if (factionId == 0)
                    continue;
                _worldPacket.WriteUInt32(factionId);
                _worldPacket.WriteUInt16((ushort)((ushort)FactionFlags[i] & 0xFF));
                _worldPacket.WriteInt32(FactionStandings[i]);
            }
            return;
        }

        for (ushort i = 0; i < FactionCount; ++i)
        {
            _worldPacket.WriteUInt8((byte)((ushort)FactionFlags[i] & 0xFF));
            _worldPacket.WriteInt32(FactionStandings[i]);
        }

        for (ushort i = 0; i < FactionCount; ++i)
            _worldPacket.WriteBit(FactionHasBonus[i]);

        _worldPacket.FlushBits();
    }

    // Legacy fixed size: 400 factions × (byte + int) + 400 bits = 2000 + 50 = 2050 bytes
    // 5.5.0: 2 counts + one 10-byte entry per tracked faction (75 on TBC data)
    public int MaxSize => ModernVersion.Uses550Engine
        ? 8 + GameData.FactionIdByRepIndex.Count * 10
        : FactionCount * 5 + 50;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);

        if (ModernVersion.Uses550Engine)
        {
            Write550(ref writer);
            return writer.Position;
        }

        for (ushort i = 0; i < FactionCount; ++i)
        {
            writer.WriteUInt8((byte)((ushort)FactionFlags[i] & 0xFF));
            writer.WriteInt32(FactionStandings[i]);
        }

        for (ushort i = 0; i < FactionCount; ++i)
            writer.WriteBit(FactionHasBonus[i]);

        writer.FlushBits();
        return writer.Position;
    }

    public int[] FactionStandings = new int[FactionCount];
    public bool[] FactionHasBonus = new bool[FactionCount]; //@todo: implement faction bonus
    public ReputationFlags[] FactionFlags = new ReputationFlags[FactionCount];
}

class SetFactionStanding : ServerPacket, ISpanWritable
{
    public SetFactionStanding() : base(Opcode.SMSG_SET_FACTION_STANDING, ConnectionType.Instance) { }

    // 5.5.0-engine body, verified against the 69110 client's own message reader
    // (ctor rva 0x5C0150: u32, u32 count, count x { u32, u32, u32 }, u8 bit byte):
    //   f32 BonusFromAchievementSystem
    //   i32 Count, Count x { i32 Index, i32 Standing, i32 FactionID }
    //   1 bit ShowVisual
    // One leading float, not the pre-5.5.0 two. Field sizes and loop structure are
    // client-verified; the order within the 12-byte entry (Index, Standing, FactionID)
    // follows WPP 5.5.0 and TrinityCore, whose structs agree.
    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            _worldPacket.WriteFloat(BonusFromAchievementSystem);

            _worldPacket.WriteInt32(Factions.Count);
            foreach (FactionStandingData factionStanding in Factions)
            {
                _worldPacket.WriteInt32(factionStanding.Index);
                _worldPacket.WriteInt32(factionStanding.Standing);
                _worldPacket.WriteInt32((int)GameData.GetFactionIdByReputationIndex((uint)factionStanding.Index));
            }

            _worldPacket.WriteBit(ShowVisual);
            _worldPacket.FlushBits();
            return;
        }

        _worldPacket.WriteFloat(ReferAFriendBonus);
        _worldPacket.WriteFloat(BonusFromAchievementSystem);

        _worldPacket.WriteInt32(Factions.Count);
        foreach (FactionStandingData factionStanding in Factions)
            factionStanding.Write(_worldPacket);

        _worldPacket.WriteBit(ShowVisual);
        _worldPacket.FlushBits();
    }

    // Cap for faction standing changes - usually just a few at once
    private const int MaxFactions = 16;
    // legacy: 2 floats(8) + count(4) + factions(8 each) + 1 bit
    // 5.5.0:  1 float(4) + count(4) + factions(12 each) + 1 bit
    public int MaxSize => ModernVersion.Uses550Engine
        ? 4 + 4 + MaxFactions * 12 + 1
        : 8 + 4 + MaxFactions * 8 + 1;

    public int WriteToSpan(Span<byte> buffer)
    {
        if (Factions.Count > MaxFactions)
            return -1;

        var writer = new SpanPacketWriter(buffer);

        if (ModernVersion.Uses550Engine)
        {
            writer.WriteFloat(BonusFromAchievementSystem);
            writer.WriteInt32(Factions.Count);
            foreach (FactionStandingData factionStanding in Factions)
            {
                writer.WriteInt32(factionStanding.Index);
                writer.WriteInt32(factionStanding.Standing);
                writer.WriteInt32((int)GameData.GetFactionIdByReputationIndex((uint)factionStanding.Index));
            }
            writer.WriteBit(ShowVisual);
            writer.FlushBits();
            return writer.Position;
        }

        writer.WriteFloat(ReferAFriendBonus);
        writer.WriteFloat(BonusFromAchievementSystem);
        writer.WriteInt32(Factions.Count);
        foreach (FactionStandingData factionStanding in Factions)
        {
            writer.WriteInt32(factionStanding.Index);
            writer.WriteInt32(factionStanding.Standing);
        }
        writer.WriteBit(ShowVisual);
        writer.FlushBits();
        return writer.Position;
    }

    public float ReferAFriendBonus;
    public float BonusFromAchievementSystem;
    public List<FactionStandingData> Factions = new();
    public bool ShowVisual;
}

struct FactionStandingData
{
    public void Write(WorldPacket data)
    {
        data.WriteInt32(Index);
        data.WriteInt32(Standing);
    }

    public int Index;
    public int Standing;
}

class SetFactionAtWar : ClientPacket
{
    public SetFactionAtWar(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        FactionIndex = _worldPacket.ReadUInt8();
    }

    public byte FactionIndex;
}

class SetFactionNotAtWar : ClientPacket
{
    public SetFactionNotAtWar(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        FactionIndex = _worldPacket.ReadUInt8();
    }

    public byte FactionIndex;
}

class SetFactionInactive : ClientPacket
{
    public SetFactionInactive(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        FactionIndex = _worldPacket.ReadUInt32();
        State = _worldPacket.HasBit();
    }

    public uint FactionIndex;
    public bool State;
}

class SetWatchedFaction : ClientPacket
{
    public SetWatchedFaction(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        FactionIndex = _worldPacket.ReadUInt32();
    }

    public uint FactionIndex;
}

class SetForcedReactions : ServerPacket, ISpanWritable
{
    public SetForcedReactions() : base(Opcode.SMSG_SET_FORCED_REACTIONS, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteInt32(Reactions.Count);
        foreach (ForcedReaction reaction in Reactions)
            reaction.Write(_worldPacket);
    }

    // Cap for forced reactions - rarely more than a few
    private const int MaxReactions = 8;
    // count(4) + reactions(8 each)
    public int MaxSize => 4 + MaxReactions * 8;

    public int WriteToSpan(Span<byte> buffer)
    {
        if (Reactions.Count > MaxReactions)
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WriteInt32(Reactions.Count);
        foreach (ForcedReaction reaction in Reactions)
        {
            writer.WriteInt32(reaction.Faction);
            writer.WriteInt32(reaction.Reaction);
        }
        return writer.Position;
    }

    public List<ForcedReaction> Reactions = new();
}

struct ForcedReaction
{
    public void Write(WorldPacket data)
    {
        data.WriteInt32(Faction);
        data.WriteInt32(Reaction);
    }

    public int Faction;
    public int Reaction;
}

class SetFactionVisible : ServerPacket, ISpanWritable
{
    public SetFactionVisible(bool visible) : base(visible ? Opcode.SMSG_SET_FACTION_VISIBLE : Opcode.SMSG_SET_FACTION_NOT_VISIBLE, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(FactionIndex);
    }

    public int MaxSize => 4; // uint

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(FactionIndex);
        return writer.Position;
    }

    public uint FactionIndex;
}
