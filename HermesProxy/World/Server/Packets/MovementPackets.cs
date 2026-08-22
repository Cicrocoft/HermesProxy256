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
using Framework.Logging;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Server.Packets;

public class ClientPlayerMovement : ClientPacket
{
    public ClientPlayerMovement(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        Guid = _worldPacket.ReadPackedGuid128(); ;
        MoveInfo = new MovementInfo();
        MoveInfo.ReadMovementInfoModern(_worldPacket);
    }

    public WowGuid128 Guid;
    public MovementInfo MoveInfo = null!;
}
public class MoveUpdate : ServerPacket, ISpanWritable
{
    public MoveUpdate() : base(Opcode.SMSG_MOVE_UPDATE, ConnectionType.Instance) { }

    public override void Write()
    {
        MoveInfo.WriteMovementInfoModern(_worldPacket, MoverGUID);
    }

    public int MaxSize => MovementInfo.MaxMovementInfoSize;

    public int WriteToSpan(Span<byte> buffer)
    {
        return MoveInfo.WriteMovementInfoModernToSpan(buffer, MoverGUID.Low, MoverGUID.High);
    }

    public WowGuid128 MoverGUID;
    public MovementInfo MoveInfo = null!;
}

public class MonsterMove : ServerPacket, ISpanWritable
{
    // Practical cap for spline points - covers real-world movement patterns
    // Real usage: Points=0-2 (next destination), PackedDeltas=0-15 (obstacle smoothing)
    // Reduced from 64 to 16 based on actual usage data (74-116 bytes observed)
    // If exceeded, WriteToSpan returns -1 to trigger fallback to Write()
    private const int MaxSplinePoints = 16;

    /// <summary>
    /// Emit SMSG_ON_MONSTER_MOVE in the 5.5.3 shape that the 69110 client actually reads, instead
    /// of the current 5.5.0/9.x shape. Verified against the client's own reader: the group-0x5E
    /// case at RVA 0x6F05BD -> spline body sub_66C220 reads Face as a full byte early (before
    /// Elapsed), does the facing switch before the point count, reads PointsCount as two bytes
    /// (Hi&lt;&lt;8 | Lo), carries an extra "SplineBitsUnused" bit, and has NO Destination Vector3.
    /// This is an exact match for WowPacketParser V5_5_0_61735 ReadMovementSpline553, gated in WPP
    /// on V5_5_3_64802 (69110 &gt; 64802). The current path emits Face as 2 bits, PointsCount as 16
    /// bits, a spurious 12-byte Destination, and omits the turn/anim-tier bits. Default off (current
    /// behaviour) because it is unverified on the wire; flip the default once one session with the
    /// knob on loads the world and NPCs move without desync (this is the single most frequent
    /// packet, ~32k/session, so a wrong field order breaks world entry). Only affects Uses550Engine.
    /// </summary>
    static readonly bool s_monsterMove553 =
        System.Environment.GetEnvironmentVariable("HERMES_256_MONSTERMOVE") != "0";

    private bool Use553Spline => ModernVersion.Uses550Engine && s_monsterMove553;

    public MonsterMove(WowGuid128 guid, ServerSideMovement moveSpline) : base(Opcode.SMSG_ON_MONSTER_MOVE, ConnectionType.Instance)
    {
        if (moveSpline.SplineFlags.HasFlag(SplineFlagModern.UncompressedPath))
        {
            if (!moveSpline.SplineFlags.HasFlag(SplineFlagModern.Cyclic))
            {
                foreach (var point in moveSpline.SplinePoints)
                    Points.Add(point);

                if (moveSpline.EndPosition != Vector3.Zero)
                    Points.Add(moveSpline.EndPosition);
            }
            else
            {
                if (moveSpline.EndPosition != Vector3.Zero)
                    Points.Add(moveSpline.EndPosition);

                foreach (var point in moveSpline.SplinePoints)
                    Points.Add(point);
            }
        }
        else if (moveSpline.EndPosition != Vector3.Zero)
        {
            Points.Add(moveSpline.EndPosition);

            if (moveSpline.SplinePoints.Count > 0)
            {
                Vector3 middle = (moveSpline.StartPosition + moveSpline.EndPosition) / 2.0f;

                // first and last points already appended
                for (int i = 0; i < moveSpline.SplinePoints.Count; ++i)
                    PackedDeltas.Add(middle - moveSpline.SplinePoints[i]);
            }
        }
        MoverGUID = guid;
        MoveSpline = moveSpline;
    }

    public override void Write()
    {
        if (Use553Spline)
        {
            Write553(_worldPacket);
            return;
        }

        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteVector3(MoveSpline.StartPosition);

        _worldPacket.WriteUInt32(MoveSpline.SplineId);
        _worldPacket.WriteVector3(Vector3.Zero); // Destination
        _worldPacket.WriteBit(false); // CrzTeleport
        _worldPacket.WriteBits(Points.Count == 0 ? 2 : 0, 3); // StopDistanceTolerance

        _worldPacket.WriteUInt32((uint)MoveSpline.SplineFlags);
        _worldPacket.WriteInt32(0); // Elapsed
        _worldPacket.WriteUInt32(MoveSpline.SplineTimeFull);
        _worldPacket.WriteUInt32(0); // FadeObjectTime
        _worldPacket.WriteUInt8(MoveSpline.SplineMode);
        _worldPacket.WritePackedGuid128(MoveSpline.TransportGuid); // != default ? MoveSpline.TransportGuid : WowGuid128.Empty
        _worldPacket.WriteInt8(MoveSpline.TransportSeat);
        _worldPacket.WriteBits((byte)MoveSpline.SplineType, 2);
        _worldPacket.WriteBits(Points.Count, 16);
        _worldPacket.WriteBit(false); // VehicleExitVoluntary ;
        _worldPacket.WriteBit(false); // Interpolate
        _worldPacket.WriteBits(PackedDeltas.Count, 16);
        _worldPacket.WriteBit(false); // SplineFilter.HasValue
        _worldPacket.WriteBit(false); // SpellEffectExtraData.HasValue
        _worldPacket.WriteBit(false); // JumpExtraData.HasValue
        _worldPacket.FlushBits();

        //if (SplineFilter.HasValue)
        //    SplineFilter.Value.Write(data);

        switch (MoveSpline.SplineType)
        {
            case SplineTypeModern.FacingSpot:
                _worldPacket.WriteVector3(MoveSpline.FinalFacingSpot);
                break;
            case SplineTypeModern.FacingTarget:
                // Universal modern Classic wire: float FaceDirection + PackedGuid128 FaceGUID.
                // Matches TrinityCore wotlk_classic src/server/game/Server/Packets/MovementPackets.cpp
                // (operator<< for MovementSpline, MONSTER_MOVE_FACING_TARGET case). Applies to V1_14,
                // V2_5, V3_4_3 — dropping the float corrupts FaceGUID + every subsequent point by
                // 4 bytes on the client side (issue #74 reopen, modern_*_parsed.txt confirms WPP
                // ArgumentOutOfRangeException on FacingGUID high-byte after FaceDirection read).
                _worldPacket.WriteFloat(MoveSpline.FinalOrientation);
                _worldPacket.WritePackedGuid128(MoveSpline.FinalFacingGuid);
                break;
            case SplineTypeModern.FacingAngle:
                _worldPacket.WriteFloat(MoveSpline.FinalOrientation);
                break;
        }

        foreach (Vector3 pos in Points)
            _worldPacket.WriteVector3(pos);

        foreach (Vector3 pos in PackedDeltas)
            _worldPacket.WritePackXYZ(pos);

        /*
        if (SpellEffectExtraData.HasValue)
            SpellEffectExtraData.Value.Write(data);

        if (JumpExtraData.HasValue)
            JumpExtraData.Value.Write(data);
        */

        // Opt-in wire trace. Enable with HERMES_TRACE_MOVEMENT=1 for cross-OS / cross-version
        // packet comparison (issue #74 reopen, macOS vs Windows V1_14_x divergence).
        if (MovementTrace.Enabled)
            Log.Print(LogType.Server,
                $"[MonsterMove/Write] v{ModernVersion.ExpansionVersion} mover=0x{MoverGUID.Low:X} entry={MoverGUID.GetEntry()} " +
                $"face={MoveSpline.SplineType} flags=0x{(uint)MoveSpline.SplineFlags:X8} mode={MoveSpline.SplineMode} " +
                $"pts={Points.Count} deltas={PackedDeltas.Count} " +
                $"orient={MoveSpline.FinalOrientation:F3} faceGuid=0x{MoveSpline.FinalFacingGuid.Low:X} " +
                $"wire={_worldPacket.GetSize()}B");
    }

    // SMSG_ON_MONSTER_MOVE in the 5.5.3 shape, verified field-for-field against the client's own
    // reader (group-0x5E case 0x6F05BD -> spline body sub_66C220, == WPP V5_5_0_61735
    // ReadMovementSpline553). Reached only through Write() when HERMES_256_MONSTERMOVE=1 and
    // Uses550Engine; the span writer returns -1 to fall back here so the field order lives once.
    private void Write553(WorldPacket data)
    {
        data.WritePackedGuid128(MoverGUID);
        data.WriteVector3(MoveSpline.StartPosition);            // Position

        // ReadMovementMonsterSpline: Id then the preamble bits. No Destination Vector3 on this arm.
        data.WriteUInt32(MoveSpline.SplineId);                  // Id
        data.WriteBit(false);                                   // CrzTeleport
        data.WriteBit(false);                                   // StopUseFaceDirection
        data.WriteBits(Points.Count == 0 ? 2 : 0, 3);          // StopDistanceTolerance
        // The client reads these 5 bits as one byte (its next read is a u32, which byte-aligns).

        // ReadMovementSpline553 body.
        data.WriteUInt32((uint)MoveSpline.SplineFlags);         // Flags
        data.WriteUInt8((byte)MoveSpline.SplineType);          // Face — a full byte on 5.5.3, read before Elapsed
        data.WriteInt32(0);                                     // Elapsed
        data.WriteUInt32(MoveSpline.SplineTimeFull);            // MoveTime
        data.WriteUInt32(0);                                    // FadeObjectTime
        data.WriteUInt8(MoveSpline.SplineMode);                 // Mode
        data.WritePackedGuid128(MoveSpline.TransportGuid);      // TransportGUID
        data.WriteInt8(MoveSpline.TransportSeat);               // VehicleSeat

        // Facing switch is emitted before the point count on 5.5.3.
        switch (MoveSpline.SplineType)
        {
            case SplineTypeModern.FacingSpot:
                data.WriteVector3(MoveSpline.FinalFacingSpot);
                break;
            case SplineTypeModern.FacingTarget:
                data.WriteFloat(MoveSpline.FinalOrientation);
                data.WritePackedGuid128(MoveSpline.FinalFacingGuid);
                break;
            case SplineTypeModern.FacingAngle:
                data.WriteFloat(MoveSpline.FinalOrientation);
                break;
        }

        // PointsCount as two bytes: pointsCount = (Hi << 8) | Lo.
        data.WriteUInt8((byte)((Points.Count >> 8) & 0xFF));   // PointsCountHi
        data.WriteUInt8((byte)(Points.Count & 0xFF));          // PointsCountLo

        data.WriteBit(false);                                   // VehicleExitVoluntary
        data.WriteBit(false);                                   // Interpolate
        data.WriteBits(PackedDeltas.Count, 16);                 // PackedDeltasCount
        data.WriteBit(false);                                   // HasSplineFilter
        data.WriteBit(false);                                   // HasSpellEffectExtraData
        data.WriteBit(false);                                   // HasJumpExtraData
        data.WriteBit(false);                                   // HasTurnData
        data.WriteBit(false);                                   // HasAnimTier
        data.WriteBit(false);                                   // SplineBitsUnused (5.5.3 only)
        data.FlushBits();

        foreach (Vector3 pos in Points)
            data.WriteVector3(pos);

        foreach (Vector3 pos in PackedDeltas)
            data.WritePackXYZ(pos);

        if (MovementTrace.Enabled)
            Log.Print(LogType.Server,
                $"[MonsterMove/553  ] v{ModernVersion.ExpansionVersion} mover=0x{MoverGUID.Low:X} entry={MoverGUID.GetEntry()} " +
                $"face={MoveSpline.SplineType} flags=0x{(uint)MoveSpline.SplineFlags:X8} mode={MoveSpline.SplineMode} " +
                $"pts={Points.Count} deltas={PackedDeltas.Count} " +
                $"orient={MoveSpline.FinalOrientation:F3} faceGuid=0x{MoveSpline.FinalFacingGuid.Low:X} " +
                $"wire={data.GetSize()}B");
    }

    // MaxSize computed from MaxSplinePoints:
    // Fixed: GUID(18) + StartPos(12) + SplineId(4) + Dest(12) + flags/times(36) + bits(6) = 88
    // SplineType FacingTarget (worst case, V2_5+): float(4) + GUID(18) = 22
    // Points: MaxSplinePoints * Vector3(12)
    // PackedDeltas: MaxSplinePoints * PackedXYZ(4)
    private const int FixedSize = 88 + 22; // 110 bytes
    public int MaxSize => FixedSize + MaxSplinePoints * 12 + MaxSplinePoints * 4;

    public int WriteToSpan(Span<byte> buffer)
    {
        // The 5.5.3 shape (HERMES_256_MONSTERMOVE) is implemented only in Write(); returning -1
        // makes the framework fall back to it, keeping the 553 field order in one place.
        if (Use553Spline)
            return -1;

        // Check if we exceed the cap - if so, return -1 to trigger fallback
        if (Points.Count > MaxSplinePoints || PackedDeltas.Count > MaxSplinePoints)
            return -1;

        var writer = new SpanPacketWriter(buffer);

        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteVector3(MoveSpline.StartPosition);

        writer.WriteUInt32(MoveSpline.SplineId);
        writer.WriteVector3(Vector3.Zero); // Destination
        writer.WriteBit(false); // CrzTeleport
        writer.WriteBits((uint)(Points.Count == 0 ? 2 : 0), 3); // StopDistanceTolerance

        writer.WriteUInt32((uint)MoveSpline.SplineFlags);
        writer.WriteInt32(0); // Elapsed
        writer.WriteUInt32(MoveSpline.SplineTimeFull);
        writer.WriteUInt32(0); // FadeObjectTime
        writer.WriteUInt8(MoveSpline.SplineMode);
        writer.WritePackedGuid128(MoveSpline.TransportGuid.Low, MoveSpline.TransportGuid.High);
        writer.WriteInt8(MoveSpline.TransportSeat);
        writer.WriteBits((uint)MoveSpline.SplineType, 2);
        writer.WriteBits((uint)Points.Count, 16);
        writer.WriteBit(false); // VehicleExitVoluntary
        writer.WriteBit(false); // Interpolate
        writer.WriteBits((uint)PackedDeltas.Count, 16);
        writer.WriteBit(false); // SplineFilter.HasValue
        writer.WriteBit(false); // SpellEffectExtraData.HasValue
        writer.WriteBit(false); // JumpExtraData.HasValue
        writer.FlushBits();

        switch (MoveSpline.SplineType)
        {
            case SplineTypeModern.FacingSpot:
                writer.WriteVector3(MoveSpline.FinalFacingSpot);
                break;
            case SplineTypeModern.FacingTarget:
                // See Write() above — universal modern Classic layout. TC wotlk_classic ground truth.
                writer.WriteFloat(MoveSpline.FinalOrientation);
                writer.WritePackedGuid128(MoveSpline.FinalFacingGuid.Low, MoveSpline.FinalFacingGuid.High);
                break;
            case SplineTypeModern.FacingAngle:
                writer.WriteFloat(MoveSpline.FinalOrientation);
                break;
        }

        foreach (Vector3 pos in Points)
            writer.WriteVector3(pos);

        foreach (Vector3 pos in PackedDeltas)
            writer.WritePackXYZ(pos);

        // Opt-in wire trace — see Write() above.
        if (MovementTrace.Enabled)
            Log.Print(LogType.Server,
                $"[MonsterMove/Span ] v{ModernVersion.ExpansionVersion} mover=0x{MoverGUID.Low:X} entry={MoverGUID.GetEntry()} " +
                $"face={MoveSpline.SplineType} flags=0x{(uint)MoveSpline.SplineFlags:X8} mode={MoveSpline.SplineMode} " +
                $"pts={Points.Count} deltas={PackedDeltas.Count} " +
                $"orient={MoveSpline.FinalOrientation:F3} faceGuid=0x{MoveSpline.FinalFacingGuid.Low:X} " +
                $"wire={writer.Position}B");

        return writer.Position;
    }

    public WowGuid128 MoverGUID;
    public ServerSideMovement MoveSpline;
    public List<Vector3> Points = new();
    public List<Vector3> PackedDeltas = new();
}

class MoveTeleportAck : ClientPacket
{
    public MoveTeleportAck(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        MoverGUID = _worldPacket.ReadPackedGuid128();
        MoveCounter = _worldPacket.ReadUInt32();
        MoveTime = _worldPacket.ReadUInt32();
    }

    public WowGuid128 MoverGUID;
    public uint MoveCounter;
    public uint MoveTime;
}

public class MoveTeleport : ServerPacket, ISpanWritable
{
    public MoveTeleport() : base(Opcode.SMSG_MOVE_TELEPORT, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteUInt32(MoveCounter);
        _worldPacket.WriteVector3(Position);
        _worldPacket.WriteFloat(Orientation);
        _worldPacket.WriteUInt8(PreloadWorld);

        _worldPacket.WriteBit(TransportGUID != default);
        _worldPacket.WriteBit(Vehicle != null);
        _worldPacket.FlushBits();

        if (Vehicle != null)
        {
            _worldPacket.WriteInt8(Vehicle.VehicleSeatIndex);
            _worldPacket.WriteBit(Vehicle.VehicleExitVoluntary);
            _worldPacket.WriteBit(Vehicle.VehicleExitTeleport);
            _worldPacket.FlushBits();
        }

        if (TransportGUID != default)
            _worldPacket.WritePackedGuid128(TransportGUID);
    }

    // MaxSize: GUID (18) + uint (4) + Vector3 (12) + float (4) + byte (1) + bits (1) + Vehicle (2) + TransportGUID (18) = 60
    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 4 + 12 + 4 + 1 + 1 + 2 + PackedGuidHelper.MaxPackedGuid128Size;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteUInt32(MoveCounter);
        writer.WriteVector3(Position);
        writer.WriteFloat(Orientation);
        writer.WriteUInt8(PreloadWorld);

        writer.WriteBit(TransportGUID != default);
        writer.WriteBit(Vehicle != null);
        writer.FlushBits();

        if (Vehicle != null)
        {
            writer.WriteInt8(Vehicle.VehicleSeatIndex);
            writer.WriteBit(Vehicle.VehicleExitVoluntary);
            writer.WriteBit(Vehicle.VehicleExitTeleport);
            writer.FlushBits();
        }

        if (TransportGUID != default)
            writer.WritePackedGuid128(TransportGUID.Low, TransportGUID.High);

        return writer.Position;
    }

    public Vector3 Position;
    public VehicleTeleport Vehicle = null!;
    public uint MoveCounter;
    public WowGuid128 MoverGUID;
    public WowGuid128 TransportGUID;
    public float Orientation;
    public byte PreloadWorld;
}

public class VehicleTeleport
{
    public sbyte VehicleSeatIndex;
    public bool VehicleExitVoluntary;
    public bool VehicleExitTeleport;
}

public class TransferPending : ServerPacket, ISpanWritable
{
    public TransferPending() : base(Opcode.SMSG_TRANSFER_PENDING) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(MapID);
        _worldPacket.WriteVector3(OldMapPosition);
        _worldPacket.WriteBit(Ship != null);
        _worldPacket.WriteBit(TransferSpellID.HasValue);

        if (Ship != null)
        {
            _worldPacket.WriteUInt32(Ship.Id);
            _worldPacket.WriteInt32(Ship.OriginMapID);
        }

        if (TransferSpellID.HasValue)
            _worldPacket.WriteInt32(TransferSpellID.Value);

        _worldPacket.FlushBits();
    }

    // MaxSize: uint (4) + Vector3 (12) + bits (1) + Ship (8) + TransferSpellID (4) = 29
    public int MaxSize => 4 + 12 + 1 + 8 + 4;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(MapID);
        writer.WriteVector3(OldMapPosition);
        writer.WriteBit(Ship != null);
        writer.WriteBit(TransferSpellID.HasValue);

        if (Ship != null)
        {
            writer.WriteUInt32(Ship.Id);
            writer.WriteInt32(Ship.OriginMapID);
        }

        if (TransferSpellID.HasValue)
            writer.WriteInt32(TransferSpellID.Value);

        writer.FlushBits();
        return writer.Position;
    }

    public uint MapID;
    public Vector3 OldMapPosition;
    public ShipTransferPending Ship = null!;
    public int? TransferSpellID;

    public class ShipTransferPending
    {
        public uint Id;              // gameobject_template.entry of the transport the player is teleporting on
        public int OriginMapID;     // Map id the player is currently on (before teleport)
    }
}

public class TransferAborted : ServerPacket, ISpanWritable
{
    public TransferAborted() : base(Opcode.SMSG_TRANSFER_ABORTED) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(MapID);
        _worldPacket.WriteUInt8(Arg);
        _worldPacket.WriteInt32(MapDifficultyXConditionID);
        _worldPacket.WriteBits(Reason, 6);
        _worldPacket.FlushBits();
    }

    public int MaxSize => 10; // uint + byte + int + 1 byte for 6 bits

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(MapID);
        writer.WriteUInt8(Arg);
        writer.WriteInt32(MapDifficultyXConditionID);
        writer.WriteBits((uint)Reason, 6);
        writer.FlushBits();
        return writer.Position;
    }

    public uint MapID;
    public byte Arg;
    public int MapDifficultyXConditionID = -6;
    public TransferAbortReasonModern Reason;
}

public class NewWorld : ServerPacket, ISpanWritable
{
    public NewWorld() : base(Opcode.SMSG_NEW_WORLD) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(MapID);
        _worldPacket.WriteVector3(Position);
        _worldPacket.WriteFloat(Orientation);
        _worldPacket.WriteUInt32(Reason);
        _worldPacket.WriteVector3(MovementOffset);
    }

    public int MaxSize => 36; // 2 uint + 2 Vector3 (24 bytes) + float = 36

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(MapID);
        writer.WriteVector3(Position);
        writer.WriteFloat(Orientation);
        writer.WriteUInt32(Reason);
        writer.WriteVector3(MovementOffset);
        return writer.Position;
    }

    public uint MapID;
    public uint Reason;
    public Vector3 Position = new();
    public float Orientation;
    public Vector3 MovementOffset;    // Adjusts all pending movement events by this offset
}

public class WorldPortResponse : ClientPacket
{
    public WorldPortResponse(WorldPacket packet) : base(packet) { }

    public override void Read() { }
}

// for server controlled units
public class MoveSplineSetSpeed : ServerPacket, ISpanWritable
{
    public MoveSplineSetSpeed(Opcode opcode) : base(opcode, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteFloat(Speed);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 4; // GUID + float

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteFloat(Speed);
        return writer.Position;
    }

    public WowGuid128 MoverGUID;
    public float Speed = 1.0f;
}

// for own player
public class MoveSetSpeed : ServerPacket, ISpanWritable
{
    public MoveSetSpeed(Opcode opcode) : base(opcode, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteUInt32(MoveCounter);
        _worldPacket.WriteFloat(Speed);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 8; // GUID + uint + float

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteUInt32(MoveCounter);
        writer.WriteFloat(Speed);
        return writer.Position;
    }

    public WowGuid128 MoverGUID;
    public uint MoveCounter = 0;
    public float Speed = 1.0f;
}

public class MovementSpeedAck : ClientPacket
{
    public MovementSpeedAck(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        MoverGUID = _worldPacket.ReadPackedGuid128();
        Ack.Read(_worldPacket);
        Speed = _worldPacket.ReadFloat();
    }

    public WowGuid128 MoverGUID;
    public MovementAck Ack;
    public float Speed;
}

public struct MovementAck
{
    public void Read(WorldPacket data)
    {
        MoveInfo = new();
        MoveInfo.ReadMovementInfoModern(data);
        MoveCounter = data.ReadUInt32();
    }

    public MovementInfo MoveInfo;
    public uint MoveCounter;
}

// for other players
public class MoveUpdateSpeed : ServerPacket, ISpanWritable
{
    public MoveUpdateSpeed(Opcode opcode) : base(opcode, ConnectionType.Instance) { }

    public override void Write()
    {
        MoveInfo.WriteMovementInfoModern(_worldPacket, MoverGUID);
        _worldPacket.WriteFloat(Speed);
    }

    public int MaxSize => MovementInfo.MaxMovementInfoSize + 4; // MovementInfo + float

    public int WriteToSpan(Span<byte> buffer)
    {
        int written = MoveInfo.WriteMovementInfoModernToSpan(buffer, MoverGUID.Low, MoverGUID.High);
        var writer = new SpanPacketWriter(buffer.Slice(written));
        writer.WriteFloat(Speed);
        return written + writer.Position;
    }

    public WowGuid128 MoverGUID;
    public MovementInfo MoveInfo = null!;
    public float Speed = 1.0f;
}

public class MoveSplineSetFlag : ServerPacket, ISpanWritable
{
    public MoveSplineSetFlag(Opcode opcode) : base(opcode, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size; // Just GUID

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        return writer.Position;
    }

    public WowGuid128 MoverGUID;
}

public class MoveSetFlag : ServerPacket, ISpanWritable
{
    public MoveSetFlag(Opcode opcode) : base(opcode, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteUInt32(MoveCounter);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 4; // GUID + uint

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteUInt32(MoveCounter);
        return writer.Position;
    }

    public WowGuid128 MoverGUID;
    public uint MoveCounter = 0;
}

public class MovementAckMessage : ClientPacket
{
    public MovementAckMessage(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        MoverGUID = _worldPacket.ReadPackedGuid128();
        Ack.Read(_worldPacket);
    }

    public WowGuid128 MoverGUID;
    public MovementAck Ack;
}

public class MoveSetCollisionHeightAck : ClientPacket
{
    public MoveSetCollisionHeightAck(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        MoverGUID = _worldPacket.ReadPackedGuid128();
        Ack.Read(_worldPacket);
        Height = _worldPacket.ReadFloat();
        MountDisplayID = _worldPacket.ReadUInt32();
        Reason = _worldPacket.ReadUInt8();
    }

    public WowGuid128 MoverGUID;
    public MovementAck Ack;
    public float Height;
    public uint MountDisplayID;
    public byte Reason;
}

class MoveSetCollisionHeight : ServerPacket, ISpanWritable
{
    public MoveSetCollisionHeight() : base(Opcode.SMSG_MOVE_SET_COLLISION_HEIGHT) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteUInt32(SequenceIndex);
        _worldPacket.WriteFloat(Height);
        _worldPacket.WriteFloat(Scale);
        _worldPacket.WriteByteEnum(Reason);
        _worldPacket.WriteUInt32(MountDisplayID);
        _worldPacket.WriteInt32(ScaleDuration);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 21; // GUID + uint + 2 float + byte + uint + int

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteUInt32(SequenceIndex);
        writer.WriteFloat(Height);
        writer.WriteFloat(Scale);
        writer.WriteUInt8((byte)Reason);
        writer.WriteUInt32(MountDisplayID);
        writer.WriteInt32(ScaleDuration);
        return writer.Position;
    }

    public WowGuid128 MoverGUID;
    public uint SequenceIndex = 1;
    public float Height = 1.0f;
    public float Scale = 1.0f;
    public UpdateCollisionHeightReason Reason;
    public uint MountDisplayID;
    public int ScaleDuration = 2000; // time it takes for "scale"-animation

    public enum UpdateCollisionHeightReason : byte
    {
        Scale = 0,
        Mount = 1,
        Force = 2,
    }
}

class MoveKnockBack : ServerPacket, ISpanWritable
{
    public MoveKnockBack() : base(Opcode.SMSG_MOVE_KNOCK_BACK, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
        _worldPacket.WriteUInt32(MoveCounter);
        _worldPacket.WriteVector2(Direction);
        _worldPacket.WriteFloat(HorizontalSpeed);
        _worldPacket.WriteFloat(VerticalSpeed);
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 20; // GUID + uint + 2 floats (Vector2) + 2 floats

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(MoverGUID.Low, MoverGUID.High);
        writer.WriteUInt32(MoveCounter);
        writer.WriteVector2(Direction);
        writer.WriteFloat(HorizontalSpeed);
        writer.WriteFloat(VerticalSpeed);
        return writer.Position;
    }

    public WowGuid128 MoverGUID;
    public uint MoveCounter;
    public Vector2 Direction;
    public float HorizontalSpeed;
    public float VerticalSpeed;
}

public class MoveUpdateKnockBack : ServerPacket, ISpanWritable
{
    public MoveUpdateKnockBack() : base(Opcode.SMSG_MOVE_UPDATE_KNOCK_BACK) { }

    public override void Write()
    {
        MoveInfo.WriteMovementInfoModern(_worldPacket, MoverGUID);
    }

    public int MaxSize => MovementInfo.MaxMovementInfoSize;

    public int WriteToSpan(Span<byte> buffer)
    {
        return MoveInfo.WriteMovementInfoModernToSpan(buffer, MoverGUID.Low, MoverGUID.High);
    }

    public WowGuid128 MoverGUID;
    public MovementInfo MoveInfo = null!;
}

class SuspendToken : ServerPacket, ISpanWritable
{
    public SuspendToken() : base(Opcode.SMSG_SUSPEND_TOKEN, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(SequenceIndex);
        _worldPacket.WriteBits(Reason, 2);
        _worldPacket.FlushBits();
    }

    public int MaxSize => 5; // uint + 1 byte for 2 bits

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(SequenceIndex);
        writer.WriteBits(Reason, 2);
        writer.FlushBits();
        return writer.Position;
    }

    public uint SequenceIndex = 1;
    public uint Reason = 1;
}

class ResumeToken : ServerPacket, ISpanWritable
{
    public ResumeToken() : base(Opcode.SMSG_RESUME_TOKEN, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(SequenceIndex);
        _worldPacket.WriteBits(Reason, 2);
        _worldPacket.FlushBits();
    }

    public int MaxSize => 5; // uint + 1 byte for 2 bits

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(SequenceIndex);
        writer.WriteBits(Reason, 2);
        writer.FlushBits();
        return writer.Position;
    }

    public uint SequenceIndex = 1;
    public uint Reason = 1;
}

public class ControlUpdate : ServerPacket, ISpanWritable
{
    public ControlUpdate() : base(Opcode.SMSG_CONTROL_UPDATE) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Guid);
        _worldPacket.WriteBit(HasControl);
        _worldPacket.FlushBits();
    }

    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 1; // GUID + 1 byte for bit

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(Guid.Low, Guid.High);
        writer.WriteBit(HasControl);
        writer.FlushBits();
        return writer.Position;
    }

    public WowGuid128 Guid;
    public bool HasControl;
}

public class SetActiveMover : ClientPacket
{
    public SetActiveMover(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        MoverGUID = _worldPacket.ReadPackedGuid128();
    }

    public WowGuid128 MoverGUID;
}

public class InitActiveMoverComplete : ClientPacket
{
    public InitActiveMoverComplete(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        Ticks = _worldPacket.ReadUInt32();
    }

    public uint Ticks;
}

class MoveSplineDone : ClientPacket
{
    public MoveSplineDone(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        Guid = _worldPacket.ReadPackedGuid128();
        MoveInfo = new();
        MoveInfo.ReadMovementInfoModern(_worldPacket);
        SplineID = _worldPacket.ReadInt32();
    }

    public WowGuid128 Guid;
    public MovementInfo MoveInfo = null!;
    public int SplineID;
}
class MoveTimeSkipped : ClientPacket
{
    public MoveTimeSkipped(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        MoverGUID = _worldPacket.ReadPackedGuid128();
        TimeSkipped = _worldPacket.ReadUInt32();
    }

    public WowGuid128 MoverGUID;
    public uint TimeSkipped;
}

/// <summary>
/// Hands the client control of its own character. Older clients volunteer CMSG_SET_ACTIVE_MOVER
/// after login, so the proxy never had to send this; the 5.5.0 engine waits to be told instead and
/// replies with CMSG_MOVE_INIT_ACTIVE_MOVER_COMPLETE. Without it the world renders but nothing in
/// it responds, because the client has no mover to apply input to.
/// </summary>
public class MoveSetActiveMover : ServerPacket
{
    public MoveSetActiveMover() : base(Opcode.SMSG_MOVE_SET_ACTIVE_MOVER) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(MoverGUID);
    }

    public WowGuid128 MoverGUID = WowGuid128.Empty;
}
