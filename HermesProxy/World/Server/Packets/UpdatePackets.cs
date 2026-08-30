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
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Server.Packets;

public class CreateObjectData
{
    public ObjectType ObjectType;
    public MovementInfo MoveInfo = null!;
    public ServerSideMovement MoveSpline = null!;
    public bool NoBirthAnim;
    public bool EnablePortals;
    public bool PlayHoverAnim;
    public bool ThisIsYou;
    public WowGuid128? AutoAttackVictim;
}
public class ObjectUpdate
{
    public ObjectUpdate(WowGuid128 guid, UpdateTypeModern type, GlobalSessionData globalSession)
    {
        Type = type;
        Guid = guid;
        GlobalSession = globalSession;
        ObjectData = new ObjectData();

        switch (type)
        {
            case UpdateTypeModern.CreateObject1:
            case UpdateTypeModern.CreateObject2:
                CreateData = new CreateObjectData();
                break;
        }

        switch (guid.GetObjectType())
        {
            case ObjectType.Item:
            case ObjectType.Container:
                ItemData = new ItemData();
                ContainerData = new ContainerData();
                break;
            case ObjectType.Unit:
                UnitData = new UnitData();
                break;
            case ObjectType.Player:
            case ObjectType.ActivePlayer:
                UnitData = new UnitData();
                PlayerData = new PlayerData();
                ActivePlayerData = new ActivePlayerData();
                break;
            case ObjectType.GameObject:
                GameObjectData = new GameObjectData();
                break;
            case ObjectType.DynamicObject:
                DynamicObjectData = new DynamicObjectData();
                break;
            case ObjectType.Corpse:
                CorpseData = new CorpseData();
                break;
        }
    }

    public UpdateTypeModern Type;
    public WowGuid128 Guid;
    public GlobalSessionData GlobalSession;
    public CreateObjectData CreateData = null!;
    public ObjectData ObjectData;
    public ItemData ItemData = null!;
    public ContainerData ContainerData = null!;
    public UnitData UnitData = null!;
    public PlayerData PlayerData = null!;
    public ActivePlayerData ActivePlayerData = null!;
    public GameObjectData GameObjectData = null!;
    public DynamicObjectData DynamicObjectData = null!;
    public CorpseData CorpseData = null!;

    /// <summary>
    /// HERMES_256_APDVALUES diagnostic: when set on a VALUES ObjectUpdate for the player, the 69110
    /// builder encodes it through the modern values encoder (ModernValuesUpdate.cs) and includes
    /// ActivePlayerData, regardless of the HERMES_256_VALUESUPDATE ladder - and only for this one
    /// object, so the normal value-update flow is unchanged. Default false.
    /// </summary>
    public bool ForceApdValuesTest;

    public void InitializePlaceholders()
    {
        if (CreateData == null)
            return;

        if (CreateData.MoveInfo != null)
        {
            if (CreateData.MoveInfo.WalkSpeed == 0)
                CreateData.MoveInfo.WalkSpeed = 2.5f;
            if (CreateData.MoveInfo.RunSpeed == 0)
                CreateData.MoveInfo.RunSpeed = 7;
            if (CreateData.MoveInfo.RunBackSpeed == 0)
                CreateData.MoveInfo.RunBackSpeed = 4.5f;
            if (CreateData.MoveInfo.SwimSpeed == 0)
                CreateData.MoveInfo.SwimSpeed = 4.722222f;
            if (CreateData.MoveInfo.SwimBackSpeed == 0)
                CreateData.MoveInfo.SwimBackSpeed = 2.5f;
            if (CreateData.MoveInfo.FlightSpeed == 0)
                CreateData.MoveInfo.FlightSpeed = 7;
            if (CreateData.MoveInfo.FlightBackSpeed == 0)
                CreateData.MoveInfo.FlightBackSpeed = 4.5f;
            if (CreateData.MoveInfo.TurnRate == 0)
                CreateData.MoveInfo.TurnRate = 3.141594f;
            if (CreateData.MoveInfo.PitchRate == 0)
                CreateData.MoveInfo.PitchRate = CreateData.MoveInfo.TurnRate;
            if (CreateData.MoveInfo.Flags.HasAnyFlag(MovementFlagModern.WalkMode) && (CreateData.MoveSpline != null))
                CreateData.MoveInfo.Flags &= ~(uint)MovementFlagModern.WalkMode;
            // CreateObject MoveInfo placeholder. `FlagsExtra = 512` is PreventChangePitch (0x200).
            // Required by all modern Classic clients (V1_14 / V2_5 / V3_4_3) so legacy-server-spawned
            // creatures render — without it creatures spawn invisible while combat events still fire.
            // Issue #74 reopen confirmed V1_14 needs it too.
            if (CreateData.MoveInfo.FlagsExtra == 0)
                CreateData.MoveInfo.FlagsExtra = 512;
        }
        if (CreateData.MoveSpline != null)
        {
            // CreateObject MoveSpline placeholder. `Unknown5 | Steering | Unknown10` is the same
            // modern Classic decoration as FlagsExtra above — universal across V1_14 / V2_5 / V3_4_3.
            // Was previously the decimal literal cast `(SplineFlagModern)2432696320` =
            // `0x01000000 | 0x10000000 | 0x80000000`.
            if (CreateData.MoveSpline.SplineFlags == 0)
                CreateData.MoveSpline.SplineFlags = SplineFlagModern.Unknown5
                                                  | SplineFlagModern.Steering
                                                  | SplineFlagModern.Unknown10;

            // Opt-in placeholder trace. Enable with HERMES_TRACE_MOVEMENT=1.
            if (MovementTrace.Enabled)
                Log.Print(LogType.Server,
                    $"[CreateObj-Move ] v{ModernVersion.ExpansionVersion} guid=0x{Guid.Low:X} entry={Guid.GetEntry()} " +
                    $"face={CreateData.MoveSpline.SplineType} " +
                    $"splineFlags=0x{(uint)CreateData.MoveSpline.SplineFlags:X8} " +
                    $"flagsExtra={CreateData.MoveInfo?.FlagsExtra} " +
                    $"orient={CreateData.MoveSpline.FinalOrientation:F3} " +
                    $"faceGuid=0x{CreateData.MoveSpline.FinalFacingGuid.Low:X}");
        }
        if (GameObjectData != null)
        {
            if ((GameObjectData.PercentHealth == null) &&
                (GameObjectData.State != null || GameObjectData.TypeID != null || GameObjectData.ArtKit != null))
                GameObjectData.PercentHealth = 255;
            if (GameObjectData.ParentRotation[3] == null)
                GameObjectData.ParentRotation[3] = 1;
            if (GameObjectData.StateAnimID == null)
                GameObjectData.StateAnimID = ModernVersion.GetGameObjectStateAnimId();
            if (Guid.GetHighType() == HighGuidType.Transport)
            {
                var transportTimer = CreateData.MoveInfo!.TransportPathTimer;
                uint period = GameData.GetTransportPeriod((uint)ObjectData.EntryID!);
                if (period != 0)
                {
                    if (GameObjectData.Level == null)
                        GameObjectData.Level = (int)period;
                    if (ObjectData.DynamicFlags == null)
                        ObjectData.DynamicFlags = (((uint)(((float)(transportTimer % period) / (float)period) * System.UInt16.MaxValue)) << 16);
                    GameObjectData.Flags = 1048616;
                }
                else if (ObjectData.DynamicFlags == null)
                    ObjectData.DynamicFlags = ((transportTimer % System.UInt16.MaxValue) << 16);
            }
        }
        if (CorpseData != null)
        {
            if (CorpseData.ClassId == null)
            {
                if (CorpseData.Owner != null)
                    CorpseData.ClassId = (byte)GlobalSession.GameState.GetUnitClass(CorpseData.Owner.Value);
                else
                    CorpseData.ClassId = 1;
            }
            if (CorpseData.FactionTemplate == null && CorpseData.Owner != null)
            {
                int ownerFaction = GlobalSession.GameState.GetLegacyFieldValueInt32(CorpseData.Owner.Value, UnitField.UNIT_FIELD_FACTIONTEMPLATE);
                if (ownerFaction != 0)
                    CorpseData.FactionTemplate = ownerFaction;
                else if (CorpseData.RaceId != null)
                    CorpseData.FactionTemplate = (int)GameData.GetFactionForRace((uint)CorpseData.RaceId);
            }
        }
        if (UnitData != null)
        {
            for (int i = 0; i < 6; i++)
            {
                if (UnitData.ModPowerRegen[i] == null)
                    UnitData.ModPowerRegen[i] = 1;
            }
            if (UnitData.Flags2 == null)
                UnitData.Flags2 = 2048;
            if (UnitData.DisplayScale == null)
                UnitData.DisplayScale = 1;
            if (UnitData.NativeXDisplayScale == null)
                UnitData.NativeXDisplayScale = 1;
            if (UnitData.ModCastHaste == null)
                UnitData.ModCastHaste = 1;
            if (UnitData.ModHaste == null)
                UnitData.ModHaste = 1;
            if (UnitData.ModRangedHaste == null)
                UnitData.ModRangedHaste = 1;
            if (UnitData.ModHasteRegen == null)
                UnitData.ModHasteRegen = 1;
            if (UnitData.ModTimeRate == null)
                UnitData.ModTimeRate = 1;
            if (UnitData.HoverHeight == null)
                UnitData.HoverHeight = 1;
            if (UnitData.ScaleDuration == null)
                UnitData.ScaleDuration = 100;
            if (UnitData.LookAtControllerID == null)
                UnitData.LookAtControllerID = -1;
            if (UnitData.ChannelObject == null &&
                Guid == GlobalSession.GameState.CurrentPlayerGuid)
                UnitData.ChannelObject = WowGuid128.Empty;
        }
        if (PlayerData != null)
        {
            if (PlayerData.WowAccount == null)
            {
                if (CreateData.ThisIsYou == true)
                    PlayerData.WowAccount = WowGuid128.Create(HighGuidType703.WowAccount, GlobalSession.GameAccountInfo.Id);
                else
                    PlayerData.WowAccount = WowGuid128.Create(HighGuidType703.WowAccount, Guid.GetCounter());
            }
            if (PlayerData.VirtualPlayerRealm == null)
                PlayerData.VirtualPlayerRealm = GlobalSession.RealmId.GetAddress();
            if (PlayerData.HonorLevel == null)
                PlayerData.HonorLevel = 1;
            if (PlayerData.AvgItemLevel[3] == null)
                PlayerData.AvgItemLevel[3] = 1;
        }
        if (ActivePlayerData != null)
        {
            if (ActivePlayerData.RestInfo[0] == null)
                ActivePlayerData.RestInfo[0] = new RestInfo();
            if (ActivePlayerData.RestInfo[0].Threshold == null)
                ActivePlayerData.RestInfo[0].Threshold = 1;
            if (ActivePlayerData.RestInfo[0].StateID == null)
                ActivePlayerData.RestInfo[0].StateID = 0;
            for (int i = 0; i < 7; i++)
            {
                if (ActivePlayerData.ModDamageDonePercent[i] == null)
                    ActivePlayerData.ModDamageDonePercent[i] = 1;
            }
            if (ActivePlayerData.ModHealingPercent == null)
                ActivePlayerData.ModHealingPercent = 1;
            if (ActivePlayerData.ModHealingDonePercent == null)
                ActivePlayerData.ModHealingDonePercent = 1;
            if (ActivePlayerData.ModPeriodicHealingDonePercent == null)
                ActivePlayerData.ModPeriodicHealingDonePercent = 1;
            for (int i = 0; i < 3; i++)
            {
                if (ActivePlayerData.WeaponDmgMultipliers[i] == null)
                    ActivePlayerData.WeaponDmgMultipliers[i] = 1;
                if (ActivePlayerData.WeaponAtkSpeedMultipliers[i] == null)
                    ActivePlayerData.WeaponAtkSpeedMultipliers[i] = 1;
            }
            if (ActivePlayerData.ModSpellPowerPercent == null)
                ActivePlayerData.ModSpellPowerPercent = 1;
            if (ActivePlayerData.NumBackpackSlots == null)
                ActivePlayerData.NumBackpackSlots = 16;
            if (ActivePlayerData.MultiActionBars == null)
                ActivePlayerData.MultiActionBars = 7;
            if (ActivePlayerData.MaxLevel == null)
                ActivePlayerData.MaxLevel = LegacyVersion.GetMaxLevel();
            if (ActivePlayerData.ModPetHaste == null)
                ActivePlayerData.ModPetHaste = 1;
            if (ActivePlayerData.HonorNextLevel == null)
                ActivePlayerData.HonorNextLevel = 5500;
            if (ActivePlayerData.PvPTierMaxFromWins == null)
                ActivePlayerData.PvPTierMaxFromWins = 4294967295;
            if (ActivePlayerData.PvPLastWeeksTierMaxFromWins == null)
                ActivePlayerData.PvPLastWeeksTierMaxFromWins = 4294967295;
        }
    }
}

public class UpdateObject : ServerPacket
{
    // ON writes the SMSG_UPDATE_OBJECT envelope's leading bit as FALSE, matching every packet of
    // the live 69110 session (see Write below). Default off = current behavior (TC-master TRUE).
    static readonly bool s_envelopeBit =
        System.Environment.GetEnvironmentVariable("HERMES_256_ENVELOPEBIT") == "1";

    public UpdateObject(GameSessionData gameState) : base(Opcode.SMSG_UPDATE_OBJECT, ConnectionType.Instance)
    {
        _gameState = gameState;
    }

    public override void Write()
    {
        NumObjUpdates = (uint)ObjectUpdates.Count;
        MapID = (ushort)_gameState.CurrentMapId!;

        WorldPacket buffer = new();

        if (ModernVersion.Uses550Engine)
        {
            // The 5.5.0 envelope, from TrinityCore master's UpdateData::BuildPacket. Two changes
            // from the older form: MapID comes first and the block count second, and there is an
            // extra bit before the destroyed-guid bit. Writing the old order hands the client the
            // low half of the count as its map id, so nothing after that can line up.
            _worldPacket.WriteUInt16(MapID);
            _worldPacket.WriteUInt32(NumObjUpdates);

            // TC master hardcodes this leading bit TRUE ("unk"), and so did we - but the LIVE
            // 69110 session sends it FALSE on every SMSG_UPDATE_OBJECT (live7_s1: byte 0x00
            // where ours is 0x80; both followed by no removals and the same size+data shape).
            // Retail semantics do not bind a classic-line client; match live under the knob.
            buffer.WriteBit(!s_envelopeBit);
            bool hasRemovals = !OutOfRangeGuids.Empty() || !DestroyedGuids.Empty();
            buffer.WriteBit(hasRemovals);
            buffer.FlushBits();

            if (hasRemovals)
            {
                buffer.WriteUInt16((ushort)DestroyedGuids.Count);
                buffer.WriteUInt32((uint)(DestroyedGuids.Count + OutOfRangeGuids.Count));

                foreach (var destroyGuid in DestroyedGuids)
                    buffer.WritePackedGuid128(destroyGuid);

                foreach (var outOfRangeGuid in OutOfRangeGuids)
                    buffer.WritePackedGuid128(outOfRangeGuid);
            }
        }
        else
        {
            _worldPacket.WriteUInt32(NumObjUpdates);
            _worldPacket.WriteUInt16(MapID);

            if (buffer.WriteBit(!OutOfRangeGuids.Empty() || !DestroyedGuids.Empty()))
            {
                buffer.WriteUInt16((ushort)DestroyedGuids.Count);
                buffer.WriteInt32(DestroyedGuids.Count + OutOfRangeGuids.Count);

                foreach (var destroyGuid in DestroyedGuids)
                    buffer.WritePackedGuid128(destroyGuid);

                foreach (var outOfRangeGuid in OutOfRangeGuids)
                    buffer.WritePackedGuid128(outOfRangeGuid);
            }
        }

        WorldPacket data = new();

        foreach (var update in ObjectUpdates)
        {
            update.InitializePlaceholders();
            switch (ModernVersion.GetUpdateFieldsDefiningBuild())
            {
                case ClientVersionBuild.V1_14_0_40237:
                {
                    Objects.Version.V1_14_0_40237.ObjectUpdateBuilder builder = new Objects.Version.V1_14_0_40237.ObjectUpdateBuilder(update, _gameState);
                    builder.WriteToPacket(data);
                    break;
                }
                case ClientVersionBuild.V1_14_1_40688:
                {
                    Objects.Version.V1_14_1_40688.ObjectUpdateBuilder builder = new Objects.Version.V1_14_1_40688.ObjectUpdateBuilder(update, _gameState);
                    builder.WriteToPacket(data);
                    break;
                }
                case ClientVersionBuild.V2_5_2_39570:
                {
                    Objects.Version.V2_5_2_39570.ObjectUpdateBuilder builder = new Objects.Version.V2_5_2_39570.ObjectUpdateBuilder(update, _gameState);
                    builder.WriteToPacket(data);
                    break;
                }
                case ClientVersionBuild.V2_5_3_41750:
                {
                    Objects.Version.V2_5_3_41750.ObjectUpdateBuilder builder = new Objects.Version.V2_5_3_41750.ObjectUpdateBuilder(update, _gameState);
                    builder.WriteToPacket(data);
                    break;
                }
                case ClientVersionBuild.V3_4_3_54261:
                {
                    Objects.Version.V3_4_3_54261.ObjectUpdateBuilder builder = new Objects.Version.V3_4_3_54261.ObjectUpdateBuilder(update, _gameState);
                    builder.WriteToPacket(data);
                    break;
                }
                case ClientVersionBuild.V2_5_6_69110:
                {
                    Objects.Version.V2_5_6_69110.ObjectUpdateBuilder builder = new Objects.Version.V2_5_6_69110.ObjectUpdateBuilder(update, _gameState);
                    builder.WriteToPacket(data);
                    break;
                }
                default:
                    throw new System.ArgumentOutOfRangeException("No object update builder defined for current build.");
            }
        }    
        
        var bytes = data.GetData();
        buffer.WriteInt32(bytes.Length);
        buffer.WriteBytes(bytes);
        Data = buffer.GetData();

        _worldPacket.WriteBytes(Data);
    }

    GameSessionData _gameState;
    public uint NumObjUpdates;
    public ushort MapID;
    public byte[] Data = Array.Empty<byte>();

    public List<WowGuid128> OutOfRangeGuids = new List<WowGuid128>();
    public List<WowGuid128> DestroyedGuids = new List<WowGuid128>();
    public List<ObjectUpdate> ObjectUpdates = new List<ObjectUpdate>();
}

public class PowerUpdate : ServerPacket, ISpanWritable
{
    // WoW has ~20 power types (mana, rage, focus, energy, combo points, runes, etc.)
    // Practical cap is much lower since a unit only has a few power types
    private const int MaxPowerTypes = 16;

    static readonly bool s_powerOrder =
        System.Environment.GetEnvironmentVariable("HERMES_256_POWERORDER") == "1";

    public PowerUpdate(WowGuid128 guid) : base(Opcode.SMSG_POWER_UPDATE)
    {
        Guid = guid;
        Powers = new List<PowerUpdatePower>();
    }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(Guid);
        _worldPacket.WriteInt32(Powers.Count);
        // HERMES_256_POWERORDER: the client reads { u8 powerType, u32 value } per element, not
        // { u32, u8 }. Read off its own reader at RVA 0x5BB200 in the 69110 dump — the function
        // that names itself `WowGetRawTypeName<struct ClientPowerUpdatePower>` — which after the
        // guid and the u32 count does a byte read stored at +0 and then a dword read stored at +4.
        // Same 5 bytes per element either way, so nothing desyncs; the VALUES are simply swapped,
        // and the client read powerType 0 with value 16777216 where we meant type 1 value 0.
        foreach (var power in Powers)
        {
            if (s_powerOrder)
            {
                _worldPacket.WriteUInt8(power.PowerType);
                _worldPacket.WriteInt32(power.Power);
            }
            else
            {
                _worldPacket.WriteInt32(power.Power);
                _worldPacket.WriteUInt8(power.PowerType);
            }
        }
    }

    // MaxSize: PackedGuid128 (18) + int (4) + 16 * (int (4) + byte (1)) = 102
    public int MaxSize => PackedGuidHelper.MaxPackedGuid128Size + 4 + MaxPowerTypes * 5;

    public int WriteToSpan(Span<byte> buffer)
    {
        if (Powers.Count > MaxPowerTypes)
            return -1;

        var writer = new SpanPacketWriter(buffer);
        writer.WritePackedGuid128(Guid.Low, Guid.High);
        writer.WriteInt32(Powers.Count);

        foreach (var power in Powers)
        {
            if (s_powerOrder)
            {
                writer.WriteUInt8(power.PowerType);
                writer.WriteInt32(power.Power);
            }
            else
            {
                writer.WriteInt32(power.Power);
                writer.WriteUInt8(power.PowerType);
            }
        }

        return writer.Position;
    }

    public WowGuid128 Guid;
    public List<PowerUpdatePower> Powers;
}

public struct PowerUpdatePower
{
    public PowerUpdatePower(int power, byte powerType)
    {
        Power = power;
        PowerType = powerType;
    }

    public int Power;
    public byte PowerType;
}

public class ObjectUpdateFailed : ClientPacket
{
    public ObjectUpdateFailed(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        ObjectGuid = _worldPacket.ReadPackedGuid128();
    }

    public WowGuid128 ObjectGuid;
}
