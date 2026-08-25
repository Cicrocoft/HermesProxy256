using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using System.Collections.Generic;

namespace HermesProxy.World.Client;

public partial class WorldClient
{
    // Handlers for SMSG opcodes coming the legacy world server
    [PacketHandler(Opcode.SMSG_ENUM_CHARACTERS_RESULT)]
    void HandleEnumCharactersResult(WorldPacket packet)
    {
        Log.Print(LogType.Trace, "[Trace] HandleEnumCharactersResult: ENTER — translating legacy SMSG_ENUM_CHARACTERS_RESULT to modern");
        EnumCharactersResult charEnum = new();
        charEnum.Success = true;
        charEnum.IsDeletedCharacters = false;
        charEnum.IsNewPlayerRestrictionSkipped = false;
        charEnum.IsNewPlayerRestricted = false;
        // Must be false for the 3.4.3 client to render existing characters — true marks the
        // result as "new account, no characters yet" and the client suppresses the list.
        charEnum.IsNewPlayer = false;
        charEnum.IsAlliedRacesCreationAllowed = false;
        charEnum.DisabledClassesMask = null;

        GetSession().GameState.OwnCharacters.Clear();

        byte count = packet.ReadUInt8();
        Log.Print(LogType.Network, $"[CharEnum] legacy count={count}");
        uint virtualRealmAddress = GetSession().Realm?.Id.GetAddress() ?? 0u;
        Log.Print(LogType.Trace, $"[Trace] HandleEnumCharactersResult: realm.GetAddress()=0x{virtualRealmAddress:X8} ({virtualRealmAddress})");
        for (byte i = 0; i < count; i++)
        {
            EnumCharactersResult.CharacterInfo char1 = new EnumCharactersResult.CharacterInfo();
            char1.ListPosition = i;
            char1.VirtualRealmAddress = virtualRealmAddress;
            PlayerCache cache = new PlayerCache();
            char1.Guid = packet.ReadGuid().To128(GetSession().GameState);
            char1.Name = cache.Name = packet.ReadCString();
            char1.RaceId = cache.RaceId = (Race)packet.ReadUInt8();
            char1.ClassId = cache.ClassId = (Class)packet.ReadUInt8();
            char1.SexId = cache.SexId = (Gender)packet.ReadUInt8();

            byte skin = packet.ReadUInt8();
            byte face = packet.ReadUInt8();
            byte hairStyle = packet.ReadUInt8();
            byte hairColor = packet.ReadUInt8();
            byte facialHair = packet.ReadUInt8();
            char1.Customizations = CharacterCustomizations.ConvertLegacyCustomizationsToModern((Race)char1.RaceId, (Gender)char1.SexId, skin, face, hairStyle, hairColor, facialHair);
            // Phase 5a diagnostic: clear customizations to test whether the choice IDs from
            // GetModernCustomizationChoice (17160-17209 range — looks like Shadowlands retail
            // DB2 IDs) are rejected by the 3.4.3.54261 client's ChrCustomizationChoice.db2.
            // If the character renders with default appearance after this, the IDs are wrong
            // and we need DB2-correct values from wago.tools.
            if (ClientVersionBuild.V3_4_3_54261 == ModernVersion.Build)
                char1.Customizations.Clear();

            char1.ExperienceLevel = cache.Level = packet.ReadUInt8();
            if (char1.ExperienceLevel > charEnum.MaxCharacterLevel)
                charEnum.MaxCharacterLevel = char1.ExperienceLevel;

            GetSession().GameState.UpdatePlayerCache(char1.Guid, cache);

            char1.ZoneId = packet.ReadUInt32();
            char1.MapId = packet.ReadUInt32();
            char1.PreloadPos = packet.ReadVector3();
            uint guildId = packet.ReadUInt32();
            GetSession().GameState.StorePlayerGuildId(char1.Guid, guildId);
            char1.GuildGuid = guildId != 0 ? WowGuid128.Create(HighGuidType703.Guild, guildId) : WowGuid128.Empty;
            char1.Flags = (CharacterFlags)packet.ReadUInt32();
            // Zeroing these on 2.5.6 was tried and made things worse: the glue screen's Create
            // Character button went from enabled to greyed out, so the client does read them. Only
            // 3.4.3 keeps the override.
            if (ClientVersionBuild.V3_4_3_54261 == ModernVersion.Build)
            {
                Log.Print(LogType.Network, $"[CharFlags] legacy flags=0x{(uint)char1.Flags:X8} — overriding to 0 for diagnostic");
                char1.Flags = 0;
            }
            // HERMES_256_ENUMFLAGS: surgically clear only Rename|Declined (the bits that make the 69110
            // client emit a rename request during login), keeping the rest so the glue screen stays intact.
            else if (s_enumFlags && ModernVersion.Uses550Engine)
            {
                uint before = (uint)char1.Flags;
                char1.Flags = (CharacterFlags)(before & ~((uint)CharacterFlags.Rename | (uint)CharacterFlags.Declined));
                if (before != (uint)char1.Flags)
                    Log.Print(LogType.Network, $"[256-spike] ENUMFLAGS: cleared Rename|Declined 0x{before:X8}->0x{(uint)char1.Flags:X8}");
            }

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                char1.Flags2 = packet.ReadUInt32(); // Customization Flags

            char1.FirstLogin = packet.ReadUInt8() != 0;
            char1.PetCreatureDisplayId = packet.ReadUInt32();
            char1.PetExperienceLevel = packet.ReadUInt32();
            char1.PetCreatureFamilyId = packet.ReadUInt32();

            for (int j = EquipmentSlot.Start; j < EquipmentSlot.End; j++)
            {
                char1.VisualItems[j].DisplayId = packet.ReadUInt32();
                char1.VisualItems[j].InvType = packet.ReadUInt8();

                if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                    char1.VisualItems[j].DisplayEnchantId = packet.ReadUInt32();
            }

            int bagCount = LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685) ? 4 : 1;
            for (int j = 0; j < bagCount; j++)
            {
                char1.VisualItems[EquipmentSlot.Bag1 + j].DisplayId = packet.ReadUInt32();
                char1.VisualItems[EquipmentSlot.Bag1 + j].InvType = packet.ReadUInt8();

                if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                    char1.VisualItems[EquipmentSlot.Bag1 + j].DisplayEnchantId = packet.ReadUInt32();
            }

            // Reset Flags2 — the legacy CustomizationFlags read above are not the same field as
            // the modern Flags2, and stale bits there cause the 3.4.3 client to silently drop
            // the character entry.
            char1.Flags2 = 0;
            char1.Flags3 = 0;
            char1.Flags4 = 0;
            char1.ProfessionIds[0] = 0;
            char1.ProfessionIds[1] = 0;
            char1.LastPlayedTime = (ulong) Time.UnixTime;
            char1.SpecID = 0;
            char1.Unknown703 = 0;
            char1.LastLoginVersion = (uint)ModernVersion.BuildInt;
            char1.OverrideSelectScreenFileDataID = 0;
            char1.BoostInProgress = false;
            char1.unkWod61x = 0;
            char1.ExpansionChosen = true;
            Log.Print(LogType.Network,
                $"[Trace] HandleEnumCharactersResult: built char[{i}] guid={char1.Guid} name='{char1.Name}' " +
                $"race={char1.RaceId} class={char1.ClassId} sex={char1.SexId} level={char1.ExperienceLevel} " +
                $"zone={char1.ZoneId} map={char1.MapId} guildGuid={char1.GuildGuid} customizations={char1.Customizations.Count}");
            charEnum.Characters.Add(char1);

            GetSession().GameState.OwnCharacters.Add(new OwnCharacterInfo
            {
                AccountId = GetSession().GameAccountInfo.WoWAccountGuid,
                CharacterGuid = char1.Guid,
                Realm = GetSession().Realm!,
                LastLoginUnixSec = char1.LastPlayedTime,
                Name = char1.Name,
                RaceId = char1.RaceId,
                ClassId = char1.ClassId,
                SexId = char1.SexId,
                Level = char1.ExperienceLevel,
            });
        }

        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(1, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(2, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(3, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(4, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(5, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(6, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(7, true, false, false));
        charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(8, true, false, false));
        if (ModernVersion.ExpansionVersion >= 2 &&
            LegacyVersion.ExpansionVersion >= 2)
        {
            charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(10, true, false, false));
            charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(11, true, false, false));
        }
        Log.Print(LogType.Network,
            $"[Trace] HandleEnumCharactersResult: SEND — chars={charEnum.Characters.Count} maxLvl={charEnum.MaxCharacterLevel} " +
            $"races={charEnum.RaceUnlockData.Count} success={charEnum.Success} isNewPlayer={charEnum.IsNewPlayer} " +
            $"disabledClassesMask={(charEnum.DisabledClassesMask.HasValue ? "set" : "null")}");
        SendPacketToClient(charEnum);
        Log.Print(LogType.Trace, "[Trace] HandleEnumCharactersResult: EXIT — translation complete, packet queued for modern client");
    }

    [PacketHandler(Opcode.SMSG_CREATE_CHAR)]
    void HandleCreateChar(WorldPacket packet)
    {
        byte result = packet.ReadUInt8();

        CreateChar createChar = new CreateChar();
        createChar.Guid = new WowGuid128();
        createChar.Code = ModernVersion.ConvertResponseCodesValue(result);
        SendPacketToClient(createChar);
    }

    [PacketHandler(Opcode.SMSG_DELETE_CHAR)]
    void HandleDeleteChar(WorldPacket packet)
    {
        byte result = packet.ReadUInt8();

        DeleteChar deleteChar = new DeleteChar();
        deleteChar.Code = ModernVersion.ConvertResponseCodesValue(result);
        SendPacketToClient(deleteChar);
    }

    // 69110 has no singular SMSG_QUERY_PLAYER_NAME_RESPONSE opcode - only the plural
    // SMSG_QUERY_PLAYER_NAMES_RESPONSE - so the singular packet resolves to opcode 0 and is
    // never seen by the client ("unknown" names). ON re-wraps the same lookup data in the
    // plural envelope on that build. Default off = current behavior.
    static readonly bool s_namesResponse256 =
        System.Environment.GetEnvironmentVariable("HERMES_256_NAMESRESPONSE") == "1";

    // The 5.5.x client runs SendInitialPacketsBeforeAddToMap (CypherCore Player.cs) and appears to
    // gate "login complete -> initialise the descriptor-backed subsystems (inventory, skills)" on
    // receiving that full set. Several members of it are MODERN-ONLY (no 2.4.3 source, so this
    // 2.4.3->modern proxy never synthesises them) and are absent from our login stream where the
    // live 69110 session sends them: SMSG_SETUP_CURRENCY and SMSG_SEND_SPELL_CHARGES are the two we
    // already have wire classes for. ON emits empty-but-well-formed versions right after
    // SMSG_INITIAL_SETUP. Diagnostic: probe backpack/skills after login - if they populate, the
    // client's init gate wanted these. Default off. (INITIAL_SETUP itself is already sent and is
    // byte-identical to live, so it is NOT the gap.)
    static readonly bool s_loginInit =
        System.Environment.GetEnvironmentVariable("HERMES_256_LOGININIT") == "1";

    // HERMES_256_SETUPLAST: in CypherCore, SMSG_INITIAL_SETUP is the FINAL packet of
    // SendInitialPacketsBeforeAddToMap (Player.cs:6035), sent immediately before AddToMap emits the
    // self create. We send it ~40 packets early (on LOGIN_VERIFY_WORLD), so the client marks its
    // before-map phase complete up front and, when the self create finally arrives, processes it in
    // an "already set up" context - the ActivePlayerData descriptor never commits into the
    // inventory/skill subsystems. ON holds INITIAL_SETUP and emits it right before the self create
    // (SendOwnObjectFirstIfNeeded), matching the reference order. Default off.
    static readonly bool s_setupLast =
        System.Environment.GetEnvironmentVariable("HERMES_256_SETUPLAST") == "1";

    // HERMES_256_ENUMFLAGS: cmangos sets CHARACTER_FLAG_RENAME (0x4000) / DECLINED (0x02000000) on the
    // enum entry, and the 69110 client acts on them by emitting CMSG_CHARACTER_RENAME_REQUEST during
    // login - an abnormal pre-world identity state (confirmed on the wire). A blanket Flags=0 was tried
    // and greyed out the glue screen's Create Character button, so clear ONLY those two bits, keeping
    // the rest. Diagnostic for whether the rename anomaly gates the active-player subsystem commit
    // (money/bags/skills). Default off.
    static readonly bool s_enumFlags =
        System.Environment.GetEnvironmentVariable("HERMES_256_ENUMFLAGS") == "1";

    void SendNameQueryResponseToClient(QueryPlayerNameResponse response)
    {
        if (s_namesResponse256 && ModernVersion.Build == ClientVersionBuild.V2_5_6_69110)
        {
            QueryPlayerNamesResponse plural = new QueryPlayerNamesResponse();
            plural.Player = response.Player;
            plural.Result = response.Result;
            plural.Data = response.Data;
            SendPacketToClient(plural);
            return;
        }
        SendPacketToClient(response);
    }

    [PacketHandler(Opcode.SMSG_QUERY_PLAYER_NAME_RESPONSE)]
    void HandleQueryPlayerNameResponse(WorldPacket packet)
    {
        QueryPlayerNameResponse response = new QueryPlayerNameResponse();
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
        {
            response.Player = response.Data.GuidActual = packet.ReadPackedGuid().To128(GetSession().GameState);
            var fail = packet.ReadBool();
            if (fail)
            {
                response.Result = (byte)Enums.V2_5_2_39570.ResponseCodes.Failure;
                SendNameQueryResponseToClient(response);
                return;
            }
        }
        else
            response.Player = response.Data.GuidActual = packet.ReadGuid().To128(GetSession().GameState);

        PlayerCache cache = new PlayerCache();
        response.Data.Name = cache.Name = packet.ReadCString();
        packet.ReadCString(); // realm name

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
        {
            response.Data.RaceID = cache.RaceId = (Race)packet.ReadUInt8();
            response.Data.Sex = cache.SexId = (Gender)packet.ReadUInt8();
            response.Data.ClassID = cache.ClassId =(Class)packet.ReadUInt8();
        }
        else
        {
            response.Data.RaceID = cache.RaceId = (Race)packet.ReadUInt32();
            response.Data.Sex = cache.SexId = (Gender)packet.ReadUInt32();
            response.Data.ClassID = cache.ClassId = (Class)packet.ReadInt32();
        }

        if (GetSession().GameState.CachedPlayers.ContainsKey(response.Player))
            response.Data.Level = GetSession().GameState.CachedPlayers[response.Player].Level;
        if (response.Data.Level == 0)
            response.Data.Level = 1;

        GetSession().GameState.UpdatePlayerCache(response.Player, cache);

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
        {
            if (packet.ReadBool())
            {
                for (var i = 0; i < 5; i++)
                    response.Data.DeclinedNames.name[i] = packet.ReadCString();
            }
        }

        response.Data.IsDeleted = false;
        response.Data.AccountID = GetSession().GetGameAccountGuidForPlayer(response.Player);
        response.Data.BnetAccountID = GetSession().GetBnetAccountGuidForPlayer(response.Player);
        response.Data.VirtualRealmAddress = GetSession().RealmId.GetAddress();
        SendNameQueryResponseToClient(response);
    }

    [PacketHandler(Opcode.SMSG_LOGIN_VERIFY_WORLD)]
    void HandleLoginVerifyWorld(WorldPacket packet)
    {
        LoginVerifyWorld verify = new LoginVerifyWorld();
        verify.MapID = packet.ReadUInt32();
        GetSession().GameState.CurrentMapId = verify.MapID;
        verify.Pos.X = packet.ReadFloat();
        verify.Pos.Y = packet.ReadFloat();
        verify.Pos.Z = packet.ReadFloat();
        verify.Pos.Orientation = packet.ReadFloat();
        SendPacketToClient(verify);

        GetSession().GameState.IsInWorld = true;

        // The client's 256-bit feature-enable bitmap is built only by the handler for 0x460272, and
        // nothing has ever sent it - so the table pointer at RVA 0x42C03D0 stays null and the camera
        // faults on the first movement-flag change. Ten crash reports over two days are that fault.
        // See CameraFeatureBitmap and REFERENCE-256-CLIENT.md section 111.
        if (ModernVersion.Uses550Engine && CameraFeatureBitmap.Enabled)
            SendPacketToClient(new CameraFeatureBitmap());

        WorldServerInfo info = new();
        if (verify.MapID > 1)
        {
            info.DifficultyID = 1;
            info.InstanceGroupSize = 5;
        }
        SendPacketToClient(info);

        SetAllTaskProgress tasks = new();
        SendPacketToClient(tasks);

        InitialSetup setup = new();
        setup.ServerExpansionLevel = (byte)(LegacyVersion.ExpansionVersion - 1);
        // HERMES_256_SETUPLAST: defer INITIAL_SETUP to immediately before the self create (matching
        // CypherCore's BeforeAddToMap order, Player.cs:6035) instead of sending it early here.
        if (s_setupLast && ModernVersion.Uses550Engine)
            GetSession().GameState.PendingInitialSetup = setup;
        else
            SendPacketToClient(setup);

        // HERMES_256_LOGININIT: modern-only BeforeAddToMap members the client may require to finish
        // login init and commit the ActivePlayerData descriptor into the inventory/skill subsystems.
        if (s_loginInit)
        {
            SendPacketToClient(new SetupCurrency());        // empty currency list (2.4.3 has no currencies)
            SendPacketToClient(new SendSpellCharges());     // empty charge list
        }

        LoadCUFProfiles cuf = new();
        cuf.Data = GetSession().AccountDataMgr.LoadCUFProfiles();
        SendPacketToClient(cuf);

        // Issue #80: modern 1.14+ Classic clients auto-cancel wand `Shoot` when target enters
        // melee range (`autoRangedCombat` CVar default-ON). Priests rely on wand for downtime
        // DPS — hint them to disable the CVar so wand keeps firing in melee.
        if (GetSession().GameState.CurrentPlayerInfo?.ClassId == Class.Priest &&
            ModernVersion.ExpansionVersion == 1 &&
            ModernVersion.AddedInVersion(ClientVersionBuild.V1_14_0_39802))
        {
            ChatPkt hint = new ChatPkt(GetSession(), ChatMessageTypeModern.System,
                "[HermesProxy] Wand tip: type |cff00ff00/console autoRangedCombat 0|r to keep your wand firing when mobs enter melee range.");
            SendPacketToClient(hint);
        }
    }

    [PacketHandler(Opcode.SMSG_CHARACTER_LOGIN_FAILED)]
    void HandleCharacterLoginFailed(WorldPacket packet)
    {
        CharacterLoginFailed failed = new CharacterLoginFailed();
        failed.Code = (Framework.Constants.LoginFailureReason)packet.ReadUInt8();
        SendPacketToClient(failed);

        GetSession().GameState.IsInWorld = false;
    }

    [PacketHandler(Opcode.SMSG_UPDATE_ACTION_BUTTONS)]
    void HandleUpdateActionButtons(WorldPacket packet)
    {
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
        {
            byte type = packet.ReadUInt8();
            if (type == 2)
                return;
        }

        List<int> buttons = new List<int>();

        int buttonCount = 120;
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
            buttonCount = 144;
        else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
            buttonCount = 132;

        for (int i = 0; i < buttonCount; i++)
        {
            int packed = packet.ReadInt32();
            buttons.Add(packed);
        }

        while (buttons.Count < 132)
            buttons.Add(0);

        GetSession().GameState.ActionButtons = buttons;
    }

    [PacketHandler(Opcode.SMSG_LOGOUT_RESPONSE)]
    void HandleLogoutResponse(WorldPacket packet)
    {
        LogoutResponse logout = new LogoutResponse();
        logout.LogoutResult = packet.ReadInt32();
        logout.Instant = packet.ReadBool();
        SendPacketToClient(logout);
    }

    [PacketHandler(Opcode.SMSG_LOGOUT_COMPLETE)]
    void HandleLogoutComplete(WorldPacket packet)
    {
        LogoutComplete logout = new LogoutComplete();
        SendPacketToClient(logout);

        GetSession().GameState = GameSessionData.CreateNewGameSessionData(GetSession());
        GetSession().InstanceSocket.CloseSocket();
        GetSession().InstanceSocket = null!;
    }

    [PacketHandler(Opcode.SMSG_LOGOUT_CANCEL_ACK)]
    void HandleLogoutCancelAck(WorldPacket packet)
    {
        LogoutCancelAck logout = new LogoutCancelAck();
        SendPacketToClient(logout);
    }

    [PacketHandler(Opcode.SMSG_LOG_XP_GAIN)]
    void HandleLogXPGain(WorldPacket packet)
    {
        LogXPGain log = new();
        log.Victim = packet.ReadGuid().To128(GetSession().GameState);
        log.Original = packet.ReadInt32();
        log.Reason = (PlayerLogXPReason)packet.ReadUInt8();
        if (log.Reason == PlayerLogXPReason.Kill)
        {
            log.Amount = packet.ReadInt32();
            log.GroupBonus = packet.ReadFloat();
        }
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089) && packet.CanRead())
            log.RAFBonus = packet.ReadUInt8();
        SendPacketToClient(log);
    }

    [PacketHandler(Opcode.SMSG_PLAYED_TIME)]
    void HandlePlayedTime(WorldPacket packet)
    {
        PlayedTime played = new();
        played.TotalTime = packet.ReadUInt32();
        played.LevelTime = packet.ReadUInt32();
        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            played.TriggerEvent = packet.ReadBool();
        else
            played.TriggerEvent = GetSession().GameState.ShowPlayedTime;
        SendPacketToClient(played);
    }

    [PacketHandler(Opcode.SMSG_LEVEL_UP_INFO)]
    void HandleLevelUpInfo(WorldPacket packet)
    {
        LevelUpInfo info = new LevelUpInfo();
        info.Level = packet.ReadInt32();
        info.HealthDelta = packet.ReadInt32();

        for (var i = 0; i < LegacyVersion.GetPowersCount(); i++)
            info.PowerDelta[i] = packet.ReadInt32();

        for (var i = 0; i < 5; i++)
            info.StatDelta[i] = packet.ReadInt32();

        SendPacketToClient(info);
    }

    [PacketHandler(Opcode.SMSG_UPDATE_COMBO_POINTS)]
    void HandleUpdateComboPoints(WorldPacket packet)
    {
        ObjectUpdate updateData = new ObjectUpdate(GetSession().GameState.CurrentPlayerGuid, UpdateTypeModern.Values, GetSession());
        updateData.ActivePlayerData.ComboTarget = packet.ReadPackedGuid().To128(GetSession().GameState);
        byte comboPoints = packet.ReadUInt8();
        sbyte powerSlot = ClassPowerTypes.GetPowerSlotForClass(GetSession().GameState.GetUnitClass(GetSession().GameState.CurrentPlayerGuid), PowerType.ComboPoints);
        if (powerSlot >= 0)
            updateData.UnitData.Power[powerSlot] = comboPoints;

        UpdateObject updatePacket = new UpdateObject(GetSession().GameState);
        updatePacket.ObjectUpdates.Add(updateData);
        SendPacketToClient(updatePacket);
    }

    [PacketHandler(Opcode.SMSG_INSPECT_RESULT)]
    [PacketHandler(Opcode.SMSG_INSPECT_TALENT)]
    void HandleInspectResult(WorldPacket packet)
    {
        InspectResult inspect = new InspectResult();
        if (packet.GetUniversalOpcode(false) == Opcode.SMSG_INSPECT_RESULT)
            inspect.DisplayInfo.GUID = packet.ReadGuid().To128(GetSession().GameState);
        else
            inspect.DisplayInfo.GUID = packet.ReadPackedGuid().To128(GetSession().GameState);

        PlayerCache? cache;
        if (!GetSession().GameState.CachedPlayers.TryGetValue(inspect.DisplayInfo.GUID, out cache) || cache == null)
            return;

        inspect.DisplayInfo.Name = cache.Name ?? string.Empty;
        inspect.DisplayInfo.ClassId = cache.ClassId;
        inspect.DisplayInfo.RaceId = cache.RaceId;
        inspect.DisplayInfo.SexId = cache.SexId;

        var updates = GetSession().GameState.GetCachedObjectFieldsLegacy(inspect.DisplayInfo.GUID);
        if (updates != null)
        {
            int PLAYER_VISIBLE_ITEM_1_0 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_0);
            if (PLAYER_VISIBLE_ITEM_1_0 >= 0) // vanilla and tbc
            {
                byte offset = (byte)(LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 16 : 12);
                for (byte i = 0; i < 19; i++)
                {
                    if (updates.ContainsKey(PLAYER_VISIBLE_ITEM_1_0 + i * offset))
                    {
                        uint itemId = updates[PLAYER_VISIBLE_ITEM_1_0 + i * offset].UInt32Value;
                        if (itemId != 0)
                        {
                            InspectItemData itemData = new InspectItemData();
                            itemData.Index = i;
                            itemData.Item.ItemID = itemId;
                            inspect.DisplayInfo.Items.Add(itemData);
                        }
                    }
                }
            }
            int PLAYER_VISIBLE_ITEM_1_ENTRYID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID);
            if (PLAYER_VISIBLE_ITEM_1_ENTRYID >= 0) // wotlk
            {
                int offset = 2;
                for (byte i = 0; i < 19; i++)
                {
                    if (updates.ContainsKey(PLAYER_VISIBLE_ITEM_1_ENTRYID + i * offset))
                    {
                        uint itemId = updates[PLAYER_VISIBLE_ITEM_1_ENTRYID + i * offset].UInt32Value;
                        if (itemId != 0)
                        {
                            InspectItemData itemData = new InspectItemData();
                            itemData.Index = i;
                            itemData.Item.ItemID = itemId;
                            inspect.DisplayInfo.Items.Add(itemData);
                        }
                    }
                }
            }
            int PLAYER_GUILDID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_GUILDID);
            if (PLAYER_GUILDID >= 0 && updates.ContainsKey(PLAYER_GUILDID))
            {
                inspect.GuildData = new InspectGuildData();
                inspect.GuildData.GuildGUID = WowGuid128.Create(HighGuidType703.Guild, updates[PLAYER_GUILDID].UInt32Value);
            }
            int PLAYER_FIELD_BYTES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BYTES);
            if (PLAYER_FIELD_BYTES >= 0 && updates.ContainsKey(PLAYER_FIELD_BYTES))
            {
                inspect.LifetimeMaxRank = (byte)((updates[PLAYER_FIELD_BYTES].UInt32Value >> 24) & 0xFF);
            }
        }

        // TODO: format seems to be different in new client
        if (packet.GetUniversalOpcode(false) == Opcode.SMSG_INSPECT_TALENT)
        {
            uint talentsCount = packet.ReadUInt32();
            for (uint i = 0; i < talentsCount; i++)
            {
                byte talent = packet.ReadUInt8();
                if (i < 25)
                    inspect.Talents.Add(talent);
            }
        }

        SendPacketToClient(inspect);
    }

    [PacketHandler(Opcode.MSG_INSPECT_HONOR_STATS, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
    void HandleInspectHonorStatsVanilla(WorldPacket packet)
    {
        WowGuid128 playerGuid = packet.ReadGuid().To128(GetSession().GameState);
        byte lifetimeHighestRank = packet.ReadUInt8();
        ushort todayHonorableKills = packet.ReadUInt16();
        ushort todayDishonorableKills = packet.ReadUInt16();
        ushort yesterdayHonorableKills = packet.ReadUInt16();
        ushort yesterdayDishonorableKills = packet.ReadUInt16();
        ushort lastWeekHonorableKills = packet.ReadUInt16();
        ushort lastWeekDishonorableKills = packet.ReadUInt16();
        ushort thisWeekHonorableKills = packet.ReadUInt16();
        ushort thisWeekDishonorableKills = packet.ReadUInt16();
        uint lifetimeHonorableKills = packet.ReadUInt32();
        uint lifetimeDishonorableKills = packet.ReadUInt32();
        uint yesterdayHonor = packet.ReadUInt32();
        uint lastWeekHonor = packet.ReadUInt32();
        uint thisWeekHonor = packet.ReadUInt32();
        uint standing = packet.ReadUInt32();
        byte rankProgress = packet.ReadUInt8();

        if (ModernVersion.ExpansionVersion == 1)
        {
            InspectHonorStatsResultClassic inspect = new InspectHonorStatsResultClassic();
            inspect.PlayerGUID = playerGuid;
            inspect.LifetimeHighestRank = lifetimeHighestRank;
            inspect.TodayHonorableKills = todayHonorableKills;
            inspect.TodayDishonorableKills = todayDishonorableKills;
            inspect.YesterdayHonorableKills = yesterdayHonorableKills;
            inspect.YesterdayDishonorableKills = yesterdayDishonorableKills;
            inspect.LastWeekHonorableKills = lastWeekHonorableKills;
            inspect.LastWeekDishonorableKills = lastWeekDishonorableKills;
            inspect.ThisWeekHonorableKills = thisWeekHonorableKills;
            inspect.ThisWeekDishonorableKills = thisWeekDishonorableKills;
            inspect.LifetimeHonorableKills = lifetimeHonorableKills;
            inspect.LifetimeDishonorableKills = lifetimeDishonorableKills;
            inspect.YesterdayHonor = yesterdayHonor;
            inspect.LastWeekHonor = lastWeekHonor;
            inspect.ThisWeekHonor = thisWeekHonor;
            inspect.Standing = standing;
            inspect.RankProgress = rankProgress;
            SendPacketToClient(inspect);
        }
        else
        {
            InspectHonorStatsResultTBC inspect = new InspectHonorStatsResultTBC();
            inspect.PlayerGUID = playerGuid;
            inspect.LifetimeHighestRank = lifetimeHighestRank;
            inspect.YesterdayHonorableKills = yesterdayHonorableKills;
            inspect.LifetimeHonorableKills = (ushort)lifetimeHonorableKills;
            SendPacketToClient(inspect);
        }
    }

    [PacketHandler(Opcode.MSG_INSPECT_HONOR_STATS, ClientVersionBuild.V2_0_1_6180)]
    void HandleInspectHonorStatsTBC(WorldPacket packet)
    {
        WowGuid128 playerGuid = packet.ReadGuid().To128(GetSession().GameState);
        byte lifetimeHighestRank = packet.ReadUInt8();
        ushort todayHonorableKills = packet.ReadUInt16();
        ushort yesterdayHonorableKills = packet.ReadUInt16();
        uint todayHonor = packet.ReadUInt32();
        uint yesterdayHonor = packet.ReadUInt32();
        uint lifetimeHonorableKills = packet.ReadUInt32();

        if (ModernVersion.ExpansionVersion == 1)
        {
            InspectHonorStatsResultClassic inspect = new InspectHonorStatsResultClassic();
            inspect.PlayerGUID = playerGuid;
            inspect.LifetimeHighestRank = lifetimeHighestRank;
            inspect.TodayHonorableKills = todayHonorableKills;
            inspect.YesterdayHonorableKills = yesterdayHonorableKills;
            inspect.LifetimeHonorableKills = lifetimeHonorableKills;
            inspect.YesterdayHonor = yesterdayHonor;
            inspect.LastWeekHonor = todayHonor;
            SendPacketToClient(inspect);
        }
        else
        {
            InspectHonorStatsResultTBC inspect = new InspectHonorStatsResultTBC();
            inspect.PlayerGUID = playerGuid;
            inspect.LifetimeHighestRank = lifetimeHighestRank;
            inspect.YesterdayHonorableKills = yesterdayHonorableKills;
            inspect.LifetimeHonorableKills = (ushort)lifetimeHonorableKills;
            SendPacketToClient(inspect);
        }
    }

    [PacketHandler(Opcode.MSG_INSPECT_ARENA_TEAMS)]
    void HandleInspectArenaTeams(WorldPacket packet)
    {
        InspectPvP inspect = new InspectPvP();
        inspect.PlayerGUID = packet.ReadGuid().To128(GetSession().GameState);
        ArenaTeamInspectData team = new ArenaTeamInspectData();
        byte slot = packet.ReadUInt8();
        uint teamId = packet.ReadUInt32();
        team.TeamGuid = WowGuid128.Create(HighGuidType703.ArenaTeam, teamId);
        team.TeamRating = packet.ReadInt32();
        team.TeamGamesPlayed = packet.ReadInt32();
        team.TeamGamesWon = packet.ReadInt32();
        team.PersonalGamesPlayed = packet.ReadInt32();
        team.PersonalRating = packet.ReadInt32();
        GetSession().GameState.StoreArenaTeamDataForPlayer(inspect.PlayerGUID, slot, team);
        for (byte i = 0; i < ArenaTeamConst.MaxArenaSlot; i++)
            inspect.ArenaTeams.Add(GetSession().GameState.GetArenaTeamDataForPlayer(inspect.PlayerGUID, slot));
        SendPacketToClient(inspect);
    }

    [PacketHandler(Opcode.SMSG_CHARACTER_RENAME_RESULT)]
    void HandleCharacterRenameResult(WorldPacket packet)
    {
        byte result = packet.ReadUInt8();

        CharacterRenameResult rename = new();
        rename.Result = ModernVersion.ConvertResponseCodesValue(result);
        if (rename.Result == (byte)Enums.V1_12_1_5875.ResponseCodes.Success)
        {
            rename.Guid = packet.ReadGuid().To128(GetSession().GameState);
            rename.Name = packet.ReadCString();
        }
        SendPacketToClient(rename);
    }
}
