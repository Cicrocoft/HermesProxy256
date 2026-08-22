using Framework.GameMath;
using Framework.IO;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HermesProxy.World.Objects;

public sealed class MovementInfo
{
    static readonly bool s_gravity =
        System.Environment.GetEnvironmentVariable("HERMES_256_GRAVITY") != "0";
    /// <summary>
    /// Whether splines are sent on the 5.5.0 engine. The flag bit here and the spline data
    /// block in ObjectUpdateBuilder are written by different code, and the client rejects the
    /// packet if they disagree - which is what the original reason-7 disconnect was. They must
    /// therefore be driven from one place; this is it.
    /// </summary>
    public static readonly bool SendSplines =
        System.Environment.GetEnvironmentVariable("HERMES_256_SPLINE") == "1";

    /// <summary>
    /// Whether the inbound (CMSG) MovementInfo reader uses the 5.5.0/69110 field set that the
    /// outbound writer already emits: a GravityModifier float after MoveIndex, a leading
    /// HasStandingOnGameObjectGUID bit, and trailing HasAdvFlying/HasDriveStatus bits (plus their
    /// optional bodies). The writer (WriteMovementInfoModern) is byte-verified against the client
    /// (REFERENCE-256-CLIENT.md section 97) and removing GravityModifier disconnects the client,
    /// so the client's movement layout on 69110 has these fields — but the reader never got the
    /// matching branch, so every inbound CMSG_MOVE_* body misparses from GravityModifier onward.
    /// The full field set is now verified byte-for-byte against the client's own CMSG movement
    /// serialiser (shared function RVA 0x66AF00, reached by every group-0x42 CMSG_MOVE_* writer):
    /// GravityModifier is a float right after MoveIndex, the has-bit block is exactly the nine bits
    /// below in this order, and the optional-body order is transport, standing-guid, inertia,
    /// adv-flying, fall, drive-status. Confirmed in game: walking, turning, jumping and falling all
    /// behave and position is stable, so this is now the default. HERMES_256_MOVEREADER=0 restores
    /// the old reader.
    ///
    /// Low risk by construction, and the reason is worth keeping: this is the *read* side. A reader
    /// error can mis-set the movement values we forward to the legacy core, but it can never make
    /// the client read a short buffer, so it cannot cause the packed-guid null dereference that has
    /// produced every crash of that class. That risk lives only on the write side.
    /// </summary>
    static readonly bool s_moveReader550 =
        System.Environment.GetEnvironmentVariable("HERMES_256_MOVEREADER") != "0";

    public const float DEFAULT_WALK_SPEED = 2.5f;
    public const float DEFAULT_RUN_SPEED = 7.0f;
    public const float DEFAULT_RUN_BACK_SPEED = 4.5f;
    public const float DEFAULT_SWIM_SPEED = 4.72222f;
    public const float DEFAULT_SWIM_BACK_SPEED = 2.5f;
    public const float DEFAULT_FLY_SPEED = 7.0f;
    public const float DEFAULT_FLY_BACK_SPEED = 4.5f;
    public const float DEFAULT_TURN_RATE = 3.141593f;
    public const float DEFAULT_PITCH_RATE = 3.141593f;

    public uint Flags;
    public uint FlagsExtra;
    public uint FlagsExtra2;
    public uint MoveTime;
    public float SwimPitch;

    /// <summary>Added on the 5.5.0 engine. One means normal gravity.</summary>
    public float GravityModifier = 1.0f;
    public uint FallTime;
    public float JumpHorizontalSpeed;
    public float JumpVerticalSpeed;
    public float JumpCosAngle;
    public float JumpSinAngle;
    public float SplineElevation;
    public bool HasSplineData;
    public Vector3 Position;
    public float Orientation;
    public float CorpseOrientation;
    public WowGuid128 TransportGuid;
    public Vector3 TransportOffset;
    public float TransportOrientation;
    public uint TransportTime;
    public uint TransportTime2;
    public sbyte TransportSeat = -1;
    public Quaternion Rotation;
    public float WalkSpeed;
    public float RunSpeed;
    public float RunBackSpeed;
    public float SwimSpeed;
    public float SwimBackSpeed;
    public float FlightSpeed;
    public float FlightBackSpeed;
    public float TurnRate;
    public float PitchRate;
    public bool Hover;
    public float VehicleOrientation;
    public uint VehicleId; // Not exactly related to movement but it is read in ReadMovementUpdateBlock
    public uint TransportPathTimer; // only set for transports

    public MovementInfo CopyFromMe()
    {
        MovementInfo copy = new MovementInfo();
        copy.Flags = this.Flags;
        copy.FlagsExtra = this.FlagsExtra;
        copy.SwimPitch = this.SwimPitch;
        copy.FallTime = this.FallTime;
        copy.JumpHorizontalSpeed = this.JumpHorizontalSpeed;
        copy.JumpVerticalSpeed = this.JumpVerticalSpeed;
        copy.JumpCosAngle = this.JumpCosAngle;
        copy.JumpSinAngle = this.JumpSinAngle;
        copy.SplineElevation = this.SplineElevation;
        copy.HasSplineData = this.HasSplineData;
        copy.Position = this.Position;
        copy.Orientation = this.Orientation;
        copy.CorpseOrientation = this.CorpseOrientation;
        copy.TransportGuid = this.TransportGuid;
        copy.TransportOffset = this.TransportOffset;
        copy.TransportOrientation = this.TransportOrientation;
        copy.TransportTime = this.TransportTime;
        copy.TransportTime2 = this.TransportTime2;
        copy.TransportSeat = this.TransportSeat;
        copy.Rotation = this.Rotation;
        copy.WalkSpeed = this.WalkSpeed;
        copy.RunSpeed = this.RunSpeed;
        copy.RunBackSpeed = this.RunBackSpeed;
        copy.SwimSpeed = this.SwimSpeed;
        copy.SwimBackSpeed = this.SwimBackSpeed;
        copy.FlightSpeed = this.FlightSpeed;
        copy.FlightBackSpeed = this.FlightBackSpeed;
        copy.TurnRate = this.TurnRate;
        copy.PitchRate = this.PitchRate;
        copy.Hover = this.Hover;
        copy.VehicleId = this.VehicleId;
        copy.VehicleOrientation = this.VehicleOrientation;
        copy.TransportPathTimer = this.TransportPathTimer;
        return copy;
    }

    public void SetMovementFlags(MovementFlagModern f) { Flags = (uint)f; }
    public void AddMovementFlag(MovementFlagModern f) { Flags |= (uint)f; }
    public void RemoveMovementFlag(MovementFlagModern f) { Flags &= ~(uint)f; }
    public bool HasMovementFlag(MovementFlagModern f) { return (Flags & (uint)f) != 0; }

    public void ReadMovementInfoLegacy(WorldPacket packet, GameSessionData gameState)
    {
        MovementInfo info = this;

        bool hasPitch;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
        {
            MovementFlagWotLK flags = (MovementFlagWotLK)packet.ReadUInt32();
            info.Flags = (uint)flags;
            info.FlagsExtra = packet.ReadUInt16();
            hasPitch = flags.HasAnyFlag(MovementFlagWotLK.Swimming | MovementFlagWotLK.Flying) || info.FlagsExtra.HasAnyFlag(MovementFlagExtra.AlwaysAllowPitching);
        }
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
        {
            MovementFlagTBC flags = (MovementFlagTBC)packet.ReadUInt32();
            info.Flags = (uint)flags.CastFlags<MovementFlagWotLK>();
            info.FlagsExtra = packet.ReadUInt8();
            hasPitch = flags.HasAnyFlag(MovementFlagTBC.Swimming | MovementFlagTBC.Flying2);
        }
        else
        {
            MovementFlagVanilla flags = (MovementFlagVanilla)packet.ReadUInt32();
            info.Flags = (uint)flags.CastFlags<MovementFlagWotLK>();
            hasPitch = flags.HasAnyFlag(MovementFlagVanilla.Swimming);
            Hover = flags.HasAnyFlag(MovementFlagVanilla.FixedZ);
        }

        info.MoveTime = packet.ReadUInt32();

        info.Position = packet.ReadVector3();
        info.Orientation = packet.ReadFloat();

        if (info.Flags.HasAnyFlag(MovementFlagWotLK.OnTransport))
        {
            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
                info.TransportGuid = packet.ReadPackedGuid().To128(gameState);
            else
                info.TransportGuid = packet.ReadGuid().To128(gameState);

            info.TransportOffset = packet.ReadVector3();
            info.TransportOrientation = packet.ReadFloat();

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                info.TransportTime = packet.ReadUInt32();

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                info.TransportSeat = packet.ReadInt8();

            if (info.FlagsExtra.HasAnyFlag(MovementFlagExtra.InterpolateMove))
                info.TransportTime2 = packet.ReadUInt32();
        }

        if (hasPitch)
            info.SwimPitch = packet.ReadFloat();

        info.FallTime = packet.ReadUInt32();
        if (info.Flags.HasAnyFlag(MovementFlagWotLK.Falling))
        {
            info.JumpVerticalSpeed = packet.ReadFloat();
            info.JumpSinAngle = packet.ReadFloat();
            info.JumpCosAngle = packet.ReadFloat();
            info.JumpHorizontalSpeed = packet.ReadFloat();
        }

        if (info.Flags.HasAnyFlag(MovementFlagWotLK.SplineElevation))
            info.SplineElevation = packet.ReadFloat();
    }

    public void WriteMovementInfoLegacy(WorldPacket data)
    {
        MovementInfo info = this;

        uint flags;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            flags = (uint)(((MovementFlagModern)info.Flags).CastFlags<MovementFlagWotLK>());
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            flags = (uint)(((MovementFlagModern)info.Flags).CastFlags<MovementFlagTBC>());
        else
            flags = (uint)(((MovementFlagModern)info.Flags).CastFlags<MovementFlagVanilla>());

        if (info.TransportGuid != default)
        {
            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                flags |= (uint)MovementFlagWotLK.OnTransport;
            else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                flags |= (uint)MovementFlagTBC.OnTransport;
            else
                flags |= (uint)MovementFlagVanilla.OnTransport;
        }
        
        data.WriteUInt32(flags);

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            data.WriteUInt16((ushort)info.FlagsExtra);
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            data.WriteUInt8((byte)info.FlagsExtra);

        data.WriteUInt32(info.MoveTime);
        data.WriteVector3(info.Position);
        data.WriteFloat(info.Orientation);

        bool hasTransport;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            hasTransport = flags.HasAnyFlag(MovementFlagWotLK.OnTransport);
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            hasTransport = flags.HasAnyFlag(MovementFlagTBC.OnTransport);
        else
            hasTransport = flags.HasAnyFlag(MovementFlagVanilla.OnTransport);

        if (hasTransport)
        {
            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
                data.WritePackedGuid(info.TransportGuid.To64());
            else
                data.WriteGuid(info.TransportGuid.To64());

            data.WriteVector3(info.TransportOffset);
            data.WriteFloat(info.TransportOrientation);

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                data.WriteUInt32(info.TransportTime);

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                data.WriteInt8(info.TransportSeat);

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) &&
                info.FlagsExtra.HasAnyFlag(MovementFlagExtra.InterpolateMove))
                data.WriteUInt32(info.TransportTime2);
        }

        bool hasSwimPitch;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            hasSwimPitch = flags.HasAnyFlag(MovementFlagWotLK.Swimming | MovementFlagWotLK.Flying) || info.FlagsExtra.HasAnyFlag(MovementFlagExtra.AlwaysAllowPitching);
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            hasSwimPitch = flags.HasAnyFlag(MovementFlagTBC.Swimming | MovementFlagTBC.Flying2);
        else
            hasSwimPitch = flags.HasAnyFlag(MovementFlagVanilla.Swimming);

        if (hasSwimPitch)
            data.WriteFloat(info.SwimPitch);

        data.WriteUInt32(info.FallTime);

        bool hasFallDirection;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            hasFallDirection = flags.HasAnyFlag(MovementFlagWotLK.Falling);
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            hasFallDirection = flags.HasAnyFlag(MovementFlagTBC.Falling);
        else
            hasFallDirection = flags.HasAnyFlag(MovementFlagVanilla.Falling);

        if (hasFallDirection)
        {
            data.WriteFloat(info.JumpVerticalSpeed);
            data.WriteFloat(info.JumpSinAngle);
            data.WriteFloat(info.JumpCosAngle);
            data.WriteFloat(info.JumpHorizontalSpeed);
        }

        bool hasSplineElevation;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            hasSplineElevation = flags.HasAnyFlag(MovementFlagWotLK.SplineElevation);
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            hasSplineElevation = flags.HasAnyFlag(MovementFlagTBC.SplineElevation);
        else
            hasSplineElevation = flags.HasAnyFlag(MovementFlagVanilla.SplineElevation);

        if (hasSplineElevation)
            data.WriteFloat(info.SplineElevation);
    }

    public void ReadMovementInfoModern(WorldPacket data)
    {
        var moveInfo = this;

        // On the 5.5.0/69110 engine the client sends a longer MovementInfo than the 9.x path:
        // a GravityModifier float after MoveIndex, a leading HasStandingOnGameObjectGUID bit and
        // trailing HasAdvFlying/HasDriveStatus bits, each with an optional body. Verified field-for-
        // field against the client's own CMSG movement serialiser 0x66AF00 (not just inferred from
        // the writer): GravityModifier = WriteF32([+0xf8]) after MoveIndex; the nine has-bits in the
        // order below; body order transport, standing-guid, inertia, adv-flying, fall, drive-status.
        // Corroborated by TrinityCore master MovementInfo operator>> and WPP V11/V5_5_0. Gated by
        // HERMES_256_MOVEREADER (default off).
        bool use550 = ModernVersion.Uses550Engine && s_moveReader550;

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            moveInfo.Flags = data.ReadUInt32();
            moveInfo.FlagsExtra = data.ReadUInt32();
            moveInfo.FlagsExtra2 = data.ReadUInt32();
        }

        moveInfo.MoveTime = data.ReadUInt32();
        moveInfo.Position = data.ReadVector3();
        moveInfo.Orientation = data.ReadFloat();

        moveInfo.SwimPitch = data.ReadFloat();
        moveInfo.SplineElevation = data.ReadFloat();

        uint removeMovementForcesCount = data.ReadUInt32();

        uint moveIndex = data.ReadUInt32();

        // GravityModifier: read right after MoveIndex and before the (usually empty) remove-forces
        // loop, matching TrinityCore master and WPP V11. The writer emits it in the same slot.
        if (use550)
            moveInfo.GravityModifier = data.ReadFloat();

        for (uint i = 0; i < removeMovementForcesCount; ++i)
        {
            data.ReadPackedGuid128();
        }

        // ResetBitReader

        if (!ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            moveInfo.Flags = data.ReadBits<uint>(30);
            moveInfo.FlagsExtra = data.ReadBits<uint>(18);
        }

        bool hasStandingOnGameObject = use550 && data.HasBit();  // HasStandingOnGameObjectGUID
        bool hasTransport = data.HasBit();
        bool hasFall = data.HasBit();
        bool hasSpline = data.HasBit(); // todo 6.x read this infos

        data.ReadBit(); // HeightChangeFailed
        data.ReadBit(); // RemoteTimeValid
        bool hasInertia = ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3) ? data.HasBit() : false;
        bool hasAdvFlying = use550 && data.HasBit();
        bool hasDriveStatus = use550 && data.HasBit();

        // Optional-body order (TrinityCore / WPP V11): transport, standing-on-gameobject guid,
        // inertia, adv-flying, fall, drive-status.
        if (hasTransport)
            ReadTransportInfoModern(data);

        if (hasStandingOnGameObject)
            data.ReadPackedGuid128(); // StandingOnGameObjectGUID

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            if (hasInertia)
            {
                // The Inertia ID is a u32 on 69110, verified against the client's own CMSG
                // movement serialiser 0x66AF00 (it emits WriteU32([+0xc4]) here, not a packed
                // guid). Matches WPP V5_5_0 ReadInertiaData / V11 (ReadInt32 "ID"). The legacy
                // path keeps its original packed-guid read so non-550 behaviour is unchanged.
                if (use550)
                    data.ReadUInt32();        // ID
                else
                    data.ReadPackedGuid128(); // legacy/9.x ID (unchanged)
                data.ReadVector3(); // Force
                data.ReadUInt32(); // Lifetime
            }
        }

        if (hasAdvFlying)
        {
            data.ReadFloat(); // ForwardVelocity
            data.ReadFloat(); // UpVelocity
        }

        if (hasFall)
        {
            moveInfo.FallTime = data.ReadUInt32();
            moveInfo.JumpVerticalSpeed = data.ReadFloat();

            // ResetBitReader

            bool hasFallDirection = data.HasBit();
            if (hasFallDirection)
            {
                moveInfo.JumpSinAngle = data.ReadFloat();
                moveInfo.JumpCosAngle = data.ReadFloat();
                moveInfo.JumpHorizontalSpeed = data.ReadFloat();
            }
        }

        if (hasDriveStatus)
        {
            // 5.5.0 arm (WPP V5_5_0_61735 ReadDriveStatusData): two floats then two bits. Never
            // fires from a TBC client (no vehicle/drive movement in this content), so untested.
            data.ReadFloat(); // Speed
            data.ReadFloat(); // MovementAngle
            data.ReadBit();   // Accelerating
            data.ReadBit();   // Drifting
        }
    }

    public void ReadTransportInfoModern(WorldPacket data)
    {
        var moveInfo = this;
        moveInfo.TransportGuid = data.ReadPackedGuid128();
        moveInfo.TransportOffset = data.ReadVector3();
        moveInfo.TransportOrientation = data.ReadFloat();
        moveInfo.TransportSeat = data.ReadInt8();           // VehicleSeatIndex
        moveInfo.TransportTime = data.ReadUInt32();         // MoveTime

        bool hasPrevTime = data.HasBit();
        bool hasVehicleId = data.HasBit();

        if (hasPrevTime)
            moveInfo.TransportTime2 = data.ReadUInt32();    // PrevMoveTime

        if (hasVehicleId)
            moveInfo.VehicleId = data.ReadUInt32();         // VehicleRecID
    }

    public void WriteMovementInfoModern(WorldPacket data, WowGuid128 guid)
    {
        MovementInfo moveInfo = this;
        bool hasFallDirection = moveInfo.Flags.HasAnyFlag(MovementFlagModern.Falling | MovementFlagModern.FallingFar);
        bool hasFall = hasFallDirection || moveInfo.FallTime != 0;

        data.WritePackedGuid128(guid);                                  // MoverGUID

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            data.WriteUInt32(Flags);
            data.WriteUInt32(FlagsExtra);
            data.WriteUInt32(FlagsExtra2);
        }

        data.WriteUInt32(moveInfo.MoveTime);                            // MoveTime
        data.WriteFloat(moveInfo.Position.X);
        data.WriteFloat(moveInfo.Position.Y);
        data.WriteFloat(moveInfo.Position.Z);
        data.WriteFloat(moveInfo.Orientation);

        data.WriteFloat(moveInfo.SwimPitch);                            // Pitch
        data.WriteFloat(moveInfo.SplineElevation);                      // StepUpStartElevation

        data.WriteUInt32(0);                                            // RemoveForcesIDs.size()
        data.WriteUInt32(0);                                            // MoveIndex

        //for (public uint i = 0; i < RemoveForcesIDs.Count; ++i)
        //    *data << ObjectGuid(RemoveForcesIDs);

        if (!ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            data.WriteBits(moveInfo.Flags, 30);
            data.WriteBits(moveInfo.FlagsExtra, 18);
        }
            
        // The 5.5.0 engine added a gravity modifier here and three more optional-block bits.
        // Omitting them leaves the create block four bytes and three bits short, which shifts the
        // fragment list and every value after it — the client answers that with an immediate
        // disconnect rather than a crash.
        // WowPacketParser's ReadMovementUpdateBlock for the 5.5.0 module goes straight from
        // MoveIndex to the bit field - there is no gravity float there. It may be a 69110
        // addition that 61735 predates, or four bytes we invented; the source cannot say.
        // HERMES_256_GRAVITY=0 drops it so the two can be told apart by testing.
        if (ModernVersion.Uses550Engine && s_gravity)
            data.WriteFloat(moveInfo.GravityModifier);

        if (ModernVersion.Uses550Engine)
            data.WriteBit(false);                                      // HasStandingOnGameObjectGUID
        data.WriteBit(moveInfo.TransportGuid != default);                 // HasTransport
        data.WriteBit(hasFall);                                        // HasFall
        // Never claim spline movement on 5.5.0 while the spline block itself is suppressed in
        // ObjectUpdateBuilder. Setting this bit makes the client prepare a spline sub-structure it
        // then never fills, and its create-block validator checks that structure (FUN_0x67C590 on
        // the region at +0x570) before accepting the packet. The two flags have to agree.
        // Suppressing this was the reason-7 workaround, from before the descriptors were
        // correct. HERMES_256_SPLINE=1 sends splines normally again, which is what the parser
        // expects; worth retrying now that so much else has been fixed.
        data.WriteBit(HasSplineData && (SendSplines || !ModernVersion.Uses550Engine));  // HasSpline
        data.WriteBit(false);                                          // HeightChangeFailed
        data.WriteBit(false);                                          // RemoteTimeValid
        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
            data.WriteBit(false);                                      // HasInertia
        if (ModernVersion.Uses550Engine)
        {
            data.WriteBit(false);                                      // HasAdvFlying
            data.WriteBit(false);                                      // HasDriveStatus
        }
        data.FlushBits();

        if (moveInfo.TransportGuid != default)
            WriteTransportInfoModern(data);

        /*
        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            if (Inertia != null)
            {
                data.WritePackedGuid128(Inertia.Guid);
                data.WriteVector3(Inertia.Force);
                data.WriteUInt32(Inertia.Lifetime);
            }
        }
        */

        if (hasFall)
        {
            data.WriteUInt32(moveInfo.FallTime);                              // Time
            data.WriteFloat(moveInfo.JumpVerticalSpeed);                      // JumpVelocity
            data.WriteBit(hasFallDirection);
            data.FlushBits();

            if (hasFallDirection)
            {
                data.WriteFloat(moveInfo.JumpSinAngle);                       // Direction
                data.WriteFloat(moveInfo.JumpCosAngle);
                data.WriteFloat(moveInfo.JumpHorizontalSpeed);                // Speed
            }
        }
    }
    public void WriteTransportInfoModern(WorldPacket data)
    {
        MovementInfo moveInfo = this;
        bool hasPrevTime = false;
        bool hasVehicleId = moveInfo.VehicleId != 0;

        data.WritePackedGuid128(moveInfo.TransportGuid);
        data.WriteFloat(moveInfo.TransportOffset.X);
        data.WriteFloat(moveInfo.TransportOffset.Y);
        data.WriteFloat(moveInfo.TransportOffset.Z);
        data.WriteFloat(moveInfo.TransportOrientation);
        data.WriteInt8(moveInfo.TransportSeat);
        data.WriteUInt32(moveInfo.TransportTime);

        data.WriteBit(hasPrevTime);
        data.WriteBit(hasVehicleId);
        data.FlushBits();

        if (hasPrevTime)
            data.WriteUInt32(0); // PrevMoveTime

        if (hasVehicleId)
            data.WriteUInt32(moveInfo.VehicleId);
    }

    /// <summary>
    /// Maximum size for movement info when written with Span writer.
    /// Includes: GUID(18) + flags(12) + times/positions(40) + bits(6) + transport(48) + inertia(34) + fall(21) = ~179
    /// Reduced from 256 to 192 based on actual usage data (54-75 bytes typical, theoretical max ~179)
    /// </summary>
    public const int MaxMovementInfoSize = 192;

    /// <summary>
    /// Writes movement info using SpanPacketWriter for zero-allocation hot path.
    /// </summary>
    public int WriteMovementInfoModernToSpan(Span<byte> buffer, ulong guidLow, ulong guidHigh)
    {
        MovementInfo moveInfo = this;
        bool hasFallDirection = moveInfo.Flags.HasAnyFlag(MovementFlagModern.Falling | MovementFlagModern.FallingFar);
        bool hasFall = hasFallDirection || moveInfo.FallTime != 0;

        var writer = new SpanPacketWriter(buffer);

        writer.WritePackedGuid128(guidLow, guidHigh);                    // MoverGUID

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            writer.WriteUInt32(Flags);
            writer.WriteUInt32(FlagsExtra);
            writer.WriteUInt32(FlagsExtra2);
        }

        writer.WriteUInt32(moveInfo.MoveTime);                           // MoveTime
        writer.WriteFloat(moveInfo.Position.X);
        writer.WriteFloat(moveInfo.Position.Y);
        writer.WriteFloat(moveInfo.Position.Z);
        writer.WriteFloat(moveInfo.Orientation);

        writer.WriteFloat(moveInfo.SwimPitch);                           // Pitch
        writer.WriteFloat(moveInfo.SplineElevation);                     // StepUpStartElevation

        writer.WriteUInt32(0);                                           // RemoveForcesIDs.size()
        writer.WriteUInt32(0);                                           // MoveIndex

        if (!ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            writer.WriteBits(moveInfo.Flags, 30);
            writer.WriteBits(moveInfo.FlagsExtra, 18);
        }

        writer.WriteBit(moveInfo.TransportGuid != default);              // HasTransport
        writer.WriteBit(hasFall);                                        // HasFall
        writer.WriteBit(HasSplineData);                                  // HasSpline
        writer.WriteBit(false);                                          // HeightChangeFailed
        writer.WriteBit(false);                                          // RemoteTimeValid
        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
            writer.WriteBit(false);                                      // HasInertia
        writer.FlushBits();

        if (moveInfo.TransportGuid != default)
            WriteTransportInfoModernToSpan(ref writer);

        // Inertia would go here if needed (9.2.0+)

        if (hasFall)
        {
            writer.WriteUInt32(moveInfo.FallTime);                       // Time
            writer.WriteFloat(moveInfo.JumpVerticalSpeed);               // JumpVelocity
            writer.WriteBit(hasFallDirection);
            writer.FlushBits();

            if (hasFallDirection)
            {
                writer.WriteFloat(moveInfo.JumpSinAngle);                // Direction
                writer.WriteFloat(moveInfo.JumpCosAngle);
                writer.WriteFloat(moveInfo.JumpHorizontalSpeed);         // Speed
            }
        }

        return writer.Position;
    }

    /// <summary>
    /// Writes transport info using SpanPacketWriter.
    /// </summary>
    private void WriteTransportInfoModernToSpan(ref SpanPacketWriter writer)
    {
        MovementInfo moveInfo = this;
        bool hasPrevTime = false;
        bool hasVehicleId = moveInfo.VehicleId != 0;

        writer.WritePackedGuid128(moveInfo.TransportGuid.Low, moveInfo.TransportGuid.High);
        writer.WriteFloat(moveInfo.TransportOffset.X);
        writer.WriteFloat(moveInfo.TransportOffset.Y);
        writer.WriteFloat(moveInfo.TransportOffset.Z);
        writer.WriteFloat(moveInfo.TransportOrientation);
        writer.WriteInt8(moveInfo.TransportSeat);
        writer.WriteUInt32(moveInfo.TransportTime);

        writer.WriteBit(hasPrevTime);
        writer.WriteBit(hasVehicleId);
        writer.FlushBits();

        if (hasPrevTime)
            writer.WriteUInt32(0); // PrevMoveTime

        if (hasVehicleId)
            writer.WriteUInt32(moveInfo.VehicleId);
    }

    public static void ClampOrientation(ref float orientation)
    {
        while (orientation < 0)
            orientation += (float)(Math.PI * 2f);
        while (orientation > (float)(Math.PI * 2f))
            orientation -= (float)(Math.PI * 2f);
    }

    // Must be called only after movement flags are converted to modern enum!
    public void ValidateMovementInfo()
    {
        ClampOrientation(ref Orientation);
        ClampOrientation(ref TransportOrientation);

        var RemoveViolatingFlags = new Action<bool, MovementFlagModern>((check, maskToRemove) =>
        {
            if (check)
            {
                Log.Print(LogType.Error, $"Violation of MovementFlags found ({check}). MovementFlags: {Flags}, MovementFlags2: {FlagsExtra}. Mask {maskToRemove} will be removed.");
                RemoveMovementFlag(maskToRemove);
            }
        });

        /*! This must be a packet spoofing attempt. MOVEMENTFLAG_ROOT sent from the client is not valid
            in conjunction with any of the moving movement flags such as MOVEMENTFLAG_FORWARD.
            It will freeze clients that receive this player's movement info.
        */
        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.Root) && HasMovementFlag(MovementFlagModern.MaskMoving), MovementFlagModern.MaskMoving);

        //! Cannot ascend and descend at the same time
        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.Ascending) && HasMovementFlag(MovementFlagModern.Descending),
            MovementFlagModern.Ascending | MovementFlagModern.Descending);

        //! Cannot move left and right at the same time
        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.TurnLeft) && HasMovementFlag(MovementFlagModern.TurnRight),
            MovementFlagModern.TurnLeft | MovementFlagModern.TurnRight);

        //! Cannot strafe left and right at the same time
        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.StrafeLeft) && HasMovementFlag(MovementFlagModern.StrafeRight),
            MovementFlagModern.StrafeLeft | MovementFlagModern.StrafeRight);

        //! Cannot pitch up and down at the same time
        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.PitchUp) && HasMovementFlag(MovementFlagModern.PitchDown),
            MovementFlagModern.PitchUp | MovementFlagModern.PitchDown);

        //! Cannot move forwards and backwards at the same time
        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.Forward) && HasMovementFlag(MovementFlagModern.Backward),
            MovementFlagModern.Forward | MovementFlagModern.Backward);

        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.DisableGravity | MovementFlagModern.CanFly) && HasMovementFlag(MovementFlagModern.Falling),
            MovementFlagModern.Falling);

        RemoveViolatingFlags(HasMovementFlag(MovementFlagModern.SplineElevation) && MathF.Abs(SplineElevation) <= 1e-5f, MovementFlagModern.SplineElevation);

        // Client first checks if spline elevation != 0, then verifies flag presence
        if (MathF.Abs(SplineElevation) > 1e-5f)
            AddMovementFlag(MovementFlagModern.SplineElevation);
    }
}
