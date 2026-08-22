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

using Framework.IO;
using HermesProxy.World.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace HermesProxy.World.Server.Packets;

public class GameRuleValuePair
{
    public void Write(WorldPacket data)
    {
        data.WriteInt32(Rule);
        data.WriteInt32(Value);
    }
    public int Rule;
    public int Value;
}

public class FeatureSystemStatus : ServerPacket
{
    public FeatureSystemStatus() : base(Opcode.SMSG_FEATURE_SYSTEM_STATUS)
    {
    }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            Write550();
            return;
        }

        _worldPacket.WriteUInt8(ComplaintStatus);

        _worldPacket.WriteUInt32(ScrollOfResurrectionRequestsRemaining);
        _worldPacket.WriteUInt32(ScrollOfResurrectionMaxRequestsPerDay);

        _worldPacket.WriteUInt32(CfgRealmID);
        _worldPacket.WriteInt32(CfgRealmRecID);

        _worldPacket.WriteUInt32(RAFSystem.MaxRecruits);
        _worldPacket.WriteUInt32(RAFSystem.MaxRecruitMonths);
        _worldPacket.WriteUInt32(RAFSystem.MaxRecruitmentUses);
        _worldPacket.WriteUInt32(RAFSystem.DaysInCycle);

        _worldPacket.WriteUInt32(TwitterPostThrottleLimit);
        _worldPacket.WriteUInt32(TwitterPostThrottleCooldown);

        _worldPacket.WriteUInt32(TokenPollTimeSeconds);
        _worldPacket.WriteUInt32(KioskSessionMinutes);
        _worldPacket.WriteInt64(TokenBalanceAmount);

        _worldPacket.WriteUInt32(BpayStoreProductDeliveryDelay);
        _worldPacket.WriteUInt32(ClubsPresenceUpdateTimer);
        _worldPacket.WriteUInt32(HiddenUIClubsPresenceUpdateTimer);

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            _worldPacket.WriteInt32(ActiveSeason);
            _worldPacket.WriteInt32(GameRuleValues.Count);

            if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 2, 2, 5, 3))
                _worldPacket.WriteInt16(MaxPlayerNameQueriesPerPacket);

            if (ModernVersion.AddedInVersion(9, 2, 7, 1, 14, 4, 3, 4, 0))
                _worldPacket.WriteInt16(PlayerNameQueryTelemetryInterval);

            foreach (var rulePair in GameRuleValues)
                rulePair.Write(_worldPacket);
        }

        _worldPacket.WriteBit(VoiceEnabled);
        _worldPacket.WriteBit(EuropaTicketSystemStatus != null);
        _worldPacket.WriteBit(ScrollOfResurrectionEnabled);
        _worldPacket.WriteBit(BpayStoreEnabled);
        _worldPacket.WriteBit(BpayStoreAvailable);
        _worldPacket.WriteBit(BpayStoreDisabledByParentalControls);
        _worldPacket.WriteBit(ItemRestorationButtonEnabled);
        _worldPacket.WriteBit(BrowserEnabled);
        _worldPacket.WriteBit(SessionAlert != null);
        _worldPacket.WriteBit(RAFSystem.Enabled);
        _worldPacket.WriteBit(RAFSystem.RecruitingEnabled);
        _worldPacket.WriteBit(CharUndeleteEnabled);
        _worldPacket.WriteBit(RestrictedAccount);
        _worldPacket.WriteBit(CommerceSystemEnabled);
        _worldPacket.WriteBit(TutorialsEnabled);
        _worldPacket.WriteBit(TwitterEnabled);
        _worldPacket.WriteBit(Unk67);
        _worldPacket.WriteBit(WillKickFromWorld);
        _worldPacket.WriteBit(KioskModeEnabled);
        _worldPacket.WriteBit(CompetitiveModeEnabled);
        _worldPacket.WriteBit(TokenBalanceEnabled);
        _worldPacket.WriteBit(WarModeFeatureEnabled);
        _worldPacket.WriteBit(ClubsEnabled);
        _worldPacket.WriteBit(ClubsBattleNetClubTypeAllowed);
        _worldPacket.WriteBit(ClubsCharacterClubTypeAllowed);
        _worldPacket.WriteBit(ClubsPresenceUpdateEnabled);
        _worldPacket.WriteBit(VoiceChatDisabledByParentalControl);
        _worldPacket.WriteBit(VoiceChatMutedByParentalControl);
        _worldPacket.WriteBit(QuestSessionEnabled);
        _worldPacket.WriteBit(IsMuted);
        _worldPacket.WriteBit(ClubFinderEnabled);
        _worldPacket.WriteBit(Unknown901CheckoutRelated);

        if (ModernVersion.AddedInVersion(9, 1, 5, 1, 14, 1, 2, 5, 3))
        {
            _worldPacket.WriteBit(TextToSpeechFeatureEnabled);
            _worldPacket.WriteBit(ChatDisabledByDefault);
            _worldPacket.WriteBit(ChatDisabledByPlayer);
            _worldPacket.WriteBit(LFGListCustomRequiresAuthenticator);
        }

        if (ModernVersion.IsClassicVersionBuild())
        {
            _worldPacket.WriteBit(BattlegroundsEnabled);
            _worldPacket.WriteBit(RaceClassExpansionLevels.Count > 0);
        }
        
        _worldPacket.FlushBits();

        {
            _worldPacket.WriteBit(QuickJoinConfig.ToastsDisabled);
            _worldPacket.WriteFloat(QuickJoinConfig.ToastDuration);
            _worldPacket.WriteFloat(QuickJoinConfig.DelayDuration);
            _worldPacket.WriteFloat(QuickJoinConfig.QueueMultiplier);
            _worldPacket.WriteFloat(QuickJoinConfig.PlayerMultiplier);
            _worldPacket.WriteFloat(QuickJoinConfig.PlayerFriendValue);
            _worldPacket.WriteFloat(QuickJoinConfig.PlayerGuildValue);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleInitialThreshold);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleDecayTime);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePrioritySpike);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleMinThreshold);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePvPPriorityNormal);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePvPPriorityLow);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePvPHonorThreshold);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListPriorityDefault);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListPriorityAbove);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListPriorityBelow);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListIlvlScalingAbove);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListIlvlScalingBelow);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleRfPriorityAbove);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleRfIlvlScalingAbove);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleDfMaxItemLevel);
            _worldPacket.WriteFloat(QuickJoinConfig.ThrottleDfBestPriority);
        }

        if (SessionAlert != null)
        {
            _worldPacket.WriteInt32(SessionAlert.Delay);
            _worldPacket.WriteInt32(SessionAlert.Period);
            _worldPacket.WriteInt32(SessionAlert.DisplayTime);
        }

        if (ModernVersion.IsClassicVersionBuild())
        {
            if (RaceClassExpansionLevels.Count > 0)
            {
                _worldPacket.WriteInt32(RaceClassExpansionLevels.Count);
                for (var i = 0; i < RaceClassExpansionLevels.Count; ++i)
                    _worldPacket.WriteUInt8(RaceClassExpansionLevels[i]);
            }
        }

        _worldPacket.WriteBit(Squelch.IsSquelched);
        _worldPacket.WritePackedGuid128(Squelch.BnetAccountGuid);
        _worldPacket.WritePackedGuid128(Squelch.GuildGuid);

        if (EuropaTicketSystemStatus != null)
            EuropaTicketSystemStatus.Write(_worldPacket);
    }

    public bool VoiceEnabled;
    public bool BrowserEnabled;
    public bool BpayStoreAvailable;
    public bool BpayStoreEnabled;
    public SessionAlertConfig SessionAlert = null!;
    public uint ScrollOfResurrectionMaxRequestsPerDay;
    public bool ScrollOfResurrectionEnabled;
    public EuropaTicketConfig EuropaTicketSystemStatus = null!;
    public uint ScrollOfResurrectionRequestsRemaining;
    public uint CfgRealmID;
    public byte ComplaintStatus;
    public int CfgRealmRecID;
    public uint TwitterPostThrottleLimit;
    public uint TwitterPostThrottleCooldown;
    public uint TokenPollTimeSeconds;
    public long TokenBalanceAmount;
    public uint BpayStoreProductDeliveryDelay;
    public uint ClubsPresenceUpdateTimer;
    public uint HiddenUIClubsPresenceUpdateTimer; // Timer for updating club presence when communities ui frame is hidden
    public int ActiveSeason;
    public List<GameRuleValuePair> GameRuleValues = new List<GameRuleValuePair>();
    public short MaxPlayerNameQueriesPerPacket;
    public short PlayerNameQueryTelemetryInterval;
    public uint KioskSessionMinutes;
    public bool ItemRestorationButtonEnabled;
    public bool CharUndeleteEnabled; // Implemented
    public bool BpayStoreDisabledByParentalControls;
    public bool TwitterEnabled;
    public bool CommerceSystemEnabled;
    public bool Unk67;
    public bool WillKickFromWorld;
    public bool RestrictedAccount;
    public bool TutorialsEnabled;
    public bool KioskModeEnabled;
    public bool CompetitiveModeEnabled;
    public bool TokenBalanceEnabled;
    public bool WarModeFeatureEnabled;
    public bool ClubsEnabled;
    public bool ClubsBattleNetClubTypeAllowed;
    public bool ClubsCharacterClubTypeAllowed;
    public bool ClubsPresenceUpdateEnabled;
    public bool VoiceChatDisabledByParentalControl;
    public bool VoiceChatMutedByParentalControl;
    public bool QuestSessionEnabled;
    public bool IsMuted;
    public bool ClubFinderEnabled;
    public bool Unknown901CheckoutRelated;
    public bool TextToSpeechFeatureEnabled;
    public bool ChatDisabledByDefault;
    public bool ChatDisabledByPlayer;
    public bool LFGListCustomRequiresAuthenticator;
    public bool BattlegroundsEnabled;
    public List<byte> RaceClassExpansionLevels = new();

    public SocialQueueConfig QuickJoinConfig;
    public SquelchInfo Squelch;
    public RafSystemFeatureInfo RAFSystem;

    public class SessionAlertConfig
    {
        public int Delay;
        public int Period;
        public int DisplayTime;
    }

    public struct SocialQueueConfig
    {
        public bool ToastsDisabled;
        public float ToastDuration;
        public float DelayDuration;
        public float QueueMultiplier;
        public float PlayerMultiplier;
        public float PlayerFriendValue;
        public float PlayerGuildValue;
        public float ThrottleInitialThreshold;
        public float ThrottleDecayTime;
        public float ThrottlePrioritySpike;
        public float ThrottleMinThreshold;
        public float ThrottlePvPPriorityNormal;
        public float ThrottlePvPPriorityLow;
        public float ThrottlePvPHonorThreshold;
        public float ThrottleLfgListPriorityDefault;
        public float ThrottleLfgListPriorityAbove;
        public float ThrottleLfgListPriorityBelow;
        public float ThrottleLfgListIlvlScalingAbove;
        public float ThrottleLfgListIlvlScalingBelow;
        public float ThrottleRfPriorityAbove;
        public float ThrottleRfIlvlScalingAbove;
        public float ThrottleDfMaxItemLevel;
        public float ThrottleDfBestPriority;
    }

    public struct SquelchInfo
    {
        public bool IsSquelched;
        public WowGuid128 BnetAccountGuid;
        public WowGuid128 GuildGuid;
    }

    public struct RafSystemFeatureInfo
    {
        public bool Enabled;
        public bool RecruitingEnabled;
        public uint MaxRecruits;
        public uint MaxRecruitMonths;
        public uint MaxRecruitmentUses;
        public uint DaysInCycle;
    }

    /// <summary>
    /// 2.5.6 build 69110 body, read out of the client itself - not ported from a parser.
    ///
    /// Derivation (see REFERENCE-256-CLIENT.md sections 89/106): the group 0x46 jump table
    /// dispatches case 0x460063 to the parse wrapper at RVA 0x5A8210, which calls the body reader
    /// at RVA 0x5A7830. That reader was walked instruction by instruction
    /// (tools-256-spike/apdreader.py 5A7830) and cross-checked against the jump-table field
    /// sequence in tools-256-spike/opcode_bodies_jt.txt. The two derivations agree everywhere
    /// both can see; the jump-table line additionally misses three sub-readers that only the
    /// instruction walk exposes (QuickJoinConfig at 0x5A7560: 22 floats + 1 bit byte = 89
    /// unconditional bytes; two EuropaTicket throttle blocks at 0x5A74D0: 4 uint32 each).
    ///
    /// The previous three crashes came from transcribing WowPacketParser handler order. The
    /// client's real shape (nearest named relative: WPP module V12_0_0_65390 plus the classic-only
    /// extras of V5_5_0_61735):
    ///
    ///   +0    uint8   ComplaintStatus
    ///   +1    9 x uint32  CfgRealmID, CfgRealmRecID, RAF{MaxRecruits, MaxRecruitMonths,
    ///                     MaxRecruitmentUses, DaysInCycle, RewardsVersion},
    ///                     CommercePricePollTimeSeconds, KioskSessionDurationMinutes
    ///   +37   uint64  RedeemForBalanceAmount
    ///   +45   7 x uint32  ClubsPresenceDelay, ClubPresenceUnsubscribeDelay, ContentSetID,
    ///                     DisabledGameModesCount, GameRulesCount, ActiveTimerunningSeasonID,
    ///                     RemainingTimerunningSeasonSeconds
    ///   +73   2 x int16   MaxPlayerGuidLookupsPerRequest, NameLookupTelemetryInterval
    ///   +77   12 x u32    NotFoundCacheTimeSeconds, RealmPvpTypeOverride, AddonChatThrottle x3,
    ///                     GuildChatThrottle x2, GroupChatThrottle x2, AddonPerformanceMsg x3 (floats)
    ///   +125  DisabledGameModes[count] of { uint8, uint32, uint32 }   (9 wire bytes each)
    ///         GameRules[count] of { int32 Rule, int32 Value, float ValueF }
    ///         8 bit-bytes, MSB-first: 43 flags, a 10-bit string length, 7 flags, 4 pad bits.
    ///             Gates verified in the client: bit 1 -> EuropaTicket tail, bit 4 -> SessionAlert,
    ///             bit 34 -> RaceClassExpansionLevels array. NOT bits-first: the previous body put
    ///             a 56-bit block at the front; the client reads the bits only here, after the
    ///             scalars (the bits-first shape belongs to the glue-screen sibling 0x460064).
    ///         QuickJoinConfig: 22 floats + 1 bit-byte (ToastsDisabled in the MSB)  [0x5A7560]
    ///         [bit 4]  SessionAlert { int32 Delay, Period, DisplayTime }
    ///         [bit 34] uint32 count + count x uint8 (RaceClassExpansionLevels)
    ///         string bytes, length = the 10-bit value above (Unknown1027; we send 0)
    ///         1 bit-byte (IsSquelched in the MSB), packed guid128 BnetAccountGuid, packed
    ///         guid128 GuildGuid
    ///         [bit 1]  EuropaTicket: 1 bit-byte (Tickets/Bugs/Complaints/Suggestions in the top
    ///                  4 bits) + TWO throttle blocks of 4 uint32 each. Both WPP modules show only
    ///                  one throttle block here - the client reader (two calls to 0x5A74D0)
    ///                  disagrees, and the client wins.
    ///
    /// Minimum body (all gates off, counts 0, empty guids): 227 bytes. The old body was 137
    /// bytes, which is why the client ran off the end and crashed in the packed-guid assembler.
    /// </summary>
    void Write550()
    {
        _worldPacket.WriteUInt8(ComplaintStatus);              // +0
        _worldPacket.WriteUInt32(CfgRealmID);                  // +1
        _worldPacket.WriteInt32(CfgRealmRecID);                // +5
        _worldPacket.WriteUInt32(RAFSystem.MaxRecruits);       // +9
        _worldPacket.WriteUInt32(RAFSystem.MaxRecruitMonths);  // +13
        _worldPacket.WriteUInt32(RAFSystem.MaxRecruitmentUses);// +17
        _worldPacket.WriteUInt32(RAFSystem.DaysInCycle);       // +21
        _worldPacket.WriteUInt32(0);                           // +25 RAFSystem.RewardsVersion
        _worldPacket.WriteUInt32(TokenPollTimeSeconds);        // +29 CommercePricePollTimeSeconds
        _worldPacket.WriteUInt32(KioskSessionMinutes);         // +33 KioskSessionDurationMinutes
        _worldPacket.WriteInt64(TokenBalanceAmount);           // +37 RedeemForBalanceAmount
        _worldPacket.WriteUInt32(ClubsPresenceUpdateTimer);    // +45 ClubsPresenceDelay
        _worldPacket.WriteUInt32(HiddenUIClubsPresenceUpdateTimer); // +49 ClubPresenceUnsubscribeDelay
        _worldPacket.WriteInt32(0);                            // +53 ContentSetID
        _worldPacket.WriteInt32(0);                            // +57 DisabledGameModesCount
        _worldPacket.WriteInt32(GameRuleValues.Count);         // +61 GameRulesCount
        _worldPacket.WriteInt32(ActiveSeason);                 // +65 ActiveTimerunningSeasonID
        _worldPacket.WriteInt32(0);                            // +69 RemainingTimerunningSeasonSeconds
        _worldPacket.WriteInt16(50);                           // +73 MaxPlayerGuidLookupsPerRequest
        _worldPacket.WriteInt16(600);                          // +75 NameLookupTelemetryInterval
        _worldPacket.WriteUInt32(10);                          // +77 NotFoundCacheTimeSeconds
        _worldPacket.WriteUInt32(0);                           // +81 RealmPvpTypeOverride
        _worldPacket.WriteInt32(0);                            // +85 AddonChatThrottle.MaxTries
        _worldPacket.WriteInt32(0);                            // +89 AddonChatThrottle.TriesRestoredPerSecond
        _worldPacket.WriteInt32(0);                            // +93 AddonChatThrottle.UsedTriesPerMessage
        _worldPacket.WriteInt32(0);                            // +97 GuildChatThrottle.UsedTriesPerMessage
        _worldPacket.WriteInt32(0);                            // +101 GuildChatThrottle.TriesRestoredPerSecond
        _worldPacket.WriteInt32(0);                            // +105 GroupChatThrottle.UsedTriesPerMessage
        _worldPacket.WriteInt32(0);                            // +109 GroupChatThrottle.TriesRestoredPerSecond
        _worldPacket.WriteFloat(0.0f);                         // +113 AddonPerformanceMsgWarning
        _worldPacket.WriteFloat(0.0f);                         // +117 AddonPerformanceMsgError
        _worldPacket.WriteFloat(0.0f);                         // +121 AddonPerformanceMsgOverall

        // DisabledGameModes: count written as 0 above; each element would be u8 + u32 + u32.

        foreach (var rule in GameRuleValues)                   // { int32, int32, float } on 5.5.0
        {
            _worldPacket.WriteInt32(rule.Rule);
            _worldPacket.WriteInt32(rule.Value);
            _worldPacket.WriteFloat(0.0f);                     // ValueF - no legacy counterpart
        }

        // The bit block: 43 flags, a 10-bit string length, 7 flags - 60 bits flushed to exactly
        // 8 bytes, MSB-first. Names for bits 0..33 follow WPP V12_0_0_65390; the three gate bits
        // (1, 4, 34) and the length position are verified against the client's reader and are the
        // only bits that change the byte stream. Bits 35..42 are this classic build's feature
        // block (LFD/LFR/pet happiness/guild flags among them, exact order not recoverable) and
        // are all sent clear, as are the 7 post-length bits.
        _worldPacket.WriteBit(VoiceEnabled);                        // 0
        _worldPacket.WriteBit(EuropaTicketSystemStatus != null);    // 1  gate: EuropaTicket tail
        _worldPacket.WriteBit(BpayStoreAvailable);                  // 2
        _worldPacket.WriteBit(ItemRestorationButtonEnabled);        // 3
        _worldPacket.WriteBit(SessionAlert != null);                // 4  gate: SessionAlert
        _worldPacket.WriteBit(RAFSystem.Enabled);                   // 5
        _worldPacket.WriteBit(RAFSystem.RecruitingEnabled);         // 6
        _worldPacket.WriteBit(CharUndeleteEnabled);                 // 7
        _worldPacket.WriteBit(RestrictedAccount);                   // 8
        _worldPacket.WriteBit(CommerceSystemEnabled);               // 9
        _worldPacket.WriteBit(TutorialsEnabled);                    // 10
        _worldPacket.WriteBit(Unk67);                               // 11 VeteranTokenRedeemWillKick
        _worldPacket.WriteBit(WillKickFromWorld);                   // 12 WorldTokenRedeemWillKick
        _worldPacket.WriteBit(KioskModeEnabled);                    // 13
        _worldPacket.WriteBit(CompetitiveModeEnabled);              // 14
        _worldPacket.WriteBit(TokenBalanceEnabled);                 // 15 RedeemForBalanceAvailable
        _worldPacket.WriteBit(WarModeFeatureEnabled);               // 16
        _worldPacket.WriteBit(ClubsEnabled);                        // 17 CommunitiesEnabled
        _worldPacket.WriteBit(ClubsBattleNetClubTypeAllowed);       // 18 BnetGroupsEnabled
        _worldPacket.WriteBit(ClubsCharacterClubTypeAllowed);       // 19 CharacterCommunitiesEnabled
        _worldPacket.WriteBit(ClubsPresenceUpdateEnabled);          // 20 ClubPresenceAllowSubscribeAll
        _worldPacket.WriteBit(VoiceChatDisabledByParentalControl);  // 21
        _worldPacket.WriteBit(VoiceChatMutedByParentalControl);     // 22
        _worldPacket.WriteBit(QuestSessionEnabled);                 // 23
        _worldPacket.WriteBit(IsMuted);                             // 24 IsChatMuted
        _worldPacket.WriteBit(ClubFinderEnabled);                   // 25
        _worldPacket.WriteBit(false);                               // 26 CommunityFinderEnabled
        _worldPacket.WriteBit(false);                               // 27 BrowserCrashReporterEnabled
        _worldPacket.WriteBit(false);                               // 28 SpeakForMeAllowed
        _worldPacket.WriteBit(false);                               // 29 DoesAccountNeedAADCPrompt
        _worldPacket.WriteBit(false);                               // 30 IsAccountOptedInToAADC
        _worldPacket.WriteBit(LFGListCustomRequiresAuthenticator);  // 31 LfgRequireAuthenticatorEnabled
        _worldPacket.WriteBit(false);                               // 32 ScriptsDisallowedForBeta
        _worldPacket.WriteBit(false);                               // 33 TimerunningEnabled / WarGamesEnabled
        _worldPacket.WriteBit(RaceClassExpansionLevels.Count > 0);  // 34 gate: byte array below
        for (int i = 35; i <= 42; ++i)
            _worldPacket.WriteBit(false);                           // 35..42 classic feature block
        _worldPacket.WriteBits(0, 10);                              // Unknown1027 string length
        for (int i = 0; i < 7; ++i)
            _worldPacket.WriteBit(false);                           // 7 post-length flags
        _worldPacket.FlushBits();                                   // 60 bits -> 8 bytes

        // QuickJoinConfig, read unconditionally by the sub-reader at 0x5A7560: 22 floats then
        // ToastsDisabled as the MSB of one trailing bit-byte. 89 bytes. Both the jump-table line
        // and the crash-frame primitive counts missed this block entirely, which alone accounts
        // for most of the shortfall that crashed the client.
        _worldPacket.WriteFloat(QuickJoinConfig.ToastDuration);
        _worldPacket.WriteFloat(QuickJoinConfig.DelayDuration);
        _worldPacket.WriteFloat(QuickJoinConfig.QueueMultiplier);
        _worldPacket.WriteFloat(QuickJoinConfig.PlayerMultiplier);
        _worldPacket.WriteFloat(QuickJoinConfig.PlayerFriendValue);
        _worldPacket.WriteFloat(QuickJoinConfig.PlayerGuildValue);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleInitialThreshold);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleDecayTime);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePrioritySpike);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleMinThreshold);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePvPPriorityNormal);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePvPPriorityLow);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottlePvPHonorThreshold);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListPriorityDefault);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListPriorityAbove);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListPriorityBelow);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListIlvlScalingAbove);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleLfgListIlvlScalingBelow);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleRfPriorityAbove);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleRfIlvlScalingAbove);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleDfMaxItemLevel);
        _worldPacket.WriteFloat(QuickJoinConfig.ThrottleDfBestPriority);
        _worldPacket.WriteBit(QuickJoinConfig.ToastsDisabled);
        _worldPacket.FlushBits();

        if (SessionAlert != null)
        {
            _worldPacket.WriteInt32(SessionAlert.Delay);
            _worldPacket.WriteInt32(SessionAlert.Period);
            _worldPacket.WriteInt32(SessionAlert.DisplayTime);
        }

        if (RaceClassExpansionLevels.Count > 0)
        {
            _worldPacket.WriteInt32(RaceClassExpansionLevels.Count);
            foreach (var level in RaceClassExpansionLevels)
                _worldPacket.WriteUInt8(level);
        }

        // Unknown1027 string bytes would go here; its 10-bit length above is 0.

        _worldPacket.WriteBit(Squelch.IsSquelched);
        _worldPacket.FlushBits();
        _worldPacket.WritePackedGuid128(Squelch.BnetAccountGuid);
        _worldPacket.WritePackedGuid128(Squelch.GuildGuid);

        if (EuropaTicketSystemStatus != null)
        {
            // The client reads one bit-byte and then TWO 4-uint32 throttle blocks (calls the
            // reader at 0x5A74D0 twice, into adjacent structs). We only carry one throttle
            // state, so it goes in the first block and the second is zeroed.
            _worldPacket.WriteBit(EuropaTicketSystemStatus.TicketsEnabled);
            _worldPacket.WriteBit(EuropaTicketSystemStatus.BugsEnabled);
            _worldPacket.WriteBit(EuropaTicketSystemStatus.ComplaintsEnabled);
            _worldPacket.WriteBit(EuropaTicketSystemStatus.SuggestionsEnabled);
            _worldPacket.FlushBits();
            EuropaTicketSystemStatus.ThrottleState.Write(_worldPacket);
            _worldPacket.WriteUInt32(0);                   // second throttle block: MaxTries
            _worldPacket.WriteUInt32(0);                   //   PerMilliseconds
            _worldPacket.WriteUInt32(0);                   //   TryCount
            _worldPacket.WriteUInt32(0);                   //   LastResetTimeBeforeNow
        }
    }
}


public class FeatureSystemStatusGlueScreen : ServerPacket
{
    public FeatureSystemStatusGlueScreen() : base(Opcode.SMSG_FEATURE_SYSTEM_STATUS_GLUE_SCREEN) { }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            // Layout read directly out of the client's own parser (RVA 0x5A8320 in build 69110),
            // not derived from WowPacketParser. See REFERENCE-256-CLIENT.md section 14.
            //
            // The previous body here was a guess, and it was also sent under the wrong opcode:
            // this build inserts a message before index 0x62 of group 0x46, so the glue-screen
            // status is 0x460064 and 0x460063 is the in-game SMSG_FEATURE_SYSTEM_STATUS. Handing
            // this body to the in-game parser is what crashed the client.
            //
            // Field names are not recoverable from the binary — only widths and order — so the
            // comments below give the client's struct offsets instead of invented names. Every
            // value is zero: that is the layout the client accepts, but the values have not been
            // shown to be the ones it wants.

            // 48 bits, MSB-first, exactly six bytes.
            for (int i = 0; i < 8; i++)
                _worldPacket.WriteBit(false);       // +0x20 +0x21 +0x22 +0x23 +0x28 +0x29 +0x2a +0x2b
            for (int i = 0; i < 8; i++)
                _worldPacket.WriteBit(false);       // +0x30 +0x31 +0x32 +0x33 +0x44 +0x45 +0x46 +0x47

            _worldPacket.WriteBit(false);           // +0x60
            _worldPacket.WriteBit(false);           // +0x61
            _worldPacket.WriteBit(false);           // +0x62
            _worldPacket.WriteBit(false);           // has-block A (+0x98): 33 more bytes when set
            _worldPacket.WriteBit(false);           // +0x9c
            _worldPacket.WriteBit(false);           // has-field B (+0xa4): one more uint32 when set
            _worldPacket.WriteBit(false);           // +0xa8
            _worldPacket.WriteBit(false);           // +0xa9

            for (int i = 0; i < 7; i++)
                _worldPacket.WriteBit(false);       // +0xaa +0xab +0x100 +0x10e +0x118 +0x119 +0x11a

            _worldPacket.WriteBits(0, 11);          // length of the trailing alert string (+0x148)

            for (int i = 0; i < 6; i++)
                _worldPacket.WriteBit(false);       // +0x16c +0x16d +0x16e +0x16f +0x170 +0x171

            _worldPacket.FlushBits();

            // Block A is absent because its bit is clear.

            // The 23 scalars line up one for one with CypherCore's FeatureSystemStatusGlueScreen —
            // same order, same widths, including the int64 in third place and the two int16s near
            // the end — so the client's struct offsets can be given their real names.
            _worldPacket.WriteUInt32(0);            // CommercePricePollTimeSeconds      +0x24
            _worldPacket.WriteUInt32(0);            // KioskSessionDurationMinutes       +0x2c
            _worldPacket.WriteInt64(0);             // RedeemForBalanceAmount            +0x38

            // Zero here means "this realm allows no characters", which is enough on its own to
            // leave the character-select screen blank however correct the character list is.
            _worldPacket.WriteInt32(10);            // MaxCharactersOnThisRealm          +0x40

            _worldPacket.WriteUInt32(0);            // LiveRegionCharacterCopySourceRegions count
            _worldPacket.WriteInt32(0);             // ActiveBoostType                   +0x64
            _worldPacket.WriteInt32(0);             // TrialBoostType                    +0x68
            _worldPacket.WriteInt32(0);             // MinimumExpansionLevel             +0x6c

            // The character is a TBC one, so a maximum of 0 (Classic) would exclude it.
            _worldPacket.WriteInt32((int)ModernVersion.ExpansionVersion); // MaximumExpansionLevel +0x70

            _worldPacket.WriteInt32(0);             // ContentSetID                      +0xac
            _worldPacket.WriteUInt32(0);            // DisabledGameModes count   { u8, u32, u32 }
            _worldPacket.WriteUInt32(0);            // GameRules count           { u32, u32, f32 }
            _worldPacket.WriteUInt32(0);            // AvailableGameModeIDs count
            _worldPacket.WriteInt32(0);             // ActiveTimerunningSeasonID         +0xf8
            _worldPacket.WriteInt32(0);             // RemainingTimerunningSeasonSeconds +0xfc
            _worldPacket.WriteInt32(0);             // TimerunningConversionMinCharacterAge +0x104
            _worldPacket.WriteInt32(0);             // TimerunningConversionMaxSeasonID  +0x108
            _worldPacket.WriteInt16(0);             // MaxPlayerGuidLookupsPerRequest    +0x10c
            _worldPacket.WriteInt16(0);             // NameLookupTelemetryInterval       +0x110
            _worldPacket.WriteUInt32(0);            // NotFoundCacheTimeSeconds          +0x114
            _worldPacket.WriteUInt32(0);            // DebugTimeEvents count     { u32, u8, string }
            _worldPacket.WriteInt32(0);             // MostRecentTimeEventID             +0x138
            _worldPacket.WriteUInt32(0);            // EventRealmQueues                  +0x168

            // Field B is absent, the alert string is empty, and all five arrays have count zero,
            // so the body ends here at 98 bytes.
            return;
        }

        if (ModernVersion.UsesModernEngine)
        {
            // 3.4.3 (WotLK Classic) layout per WPP V3_4_0_45166 MiscellaneousHandler.cs:124-203
            // (gated on V3_4_3_51505+, before V3_4_4_59817). Reads 30 bits + EuropaTicket-conditional
            // payload + 11 trailing uint/int fields. Critical: this packet is sent BEFORE
            // SMSG_ENUM_CHARACTERS_RESULT — getting it wrong misaligns every byte after, including
            // the character list, breaking client deserialization silently.
            _worldPacket.WriteBit(BpayStoreEnabled);
            _worldPacket.WriteBit(BpayStoreAvailable);
            _worldPacket.WriteBit(BpayStoreDisabledByParentalControls);
            _worldPacket.WriteBit(CharUndeleteEnabled);
            _worldPacket.WriteBit(CommerceSystemEnabled);
            _worldPacket.WriteBit(Unk14);
            _worldPacket.WriteBit(WillKickFromWorld);
            _worldPacket.WriteBit(IsExpansionPreorderInStore);

            _worldPacket.WriteBit(KioskModeEnabled);
            _worldPacket.WriteBit(CompetitiveModeEnabled);
            _worldPacket.WriteBit(false); // IsBoostEnabled
            _worldPacket.WriteBit(TrialBoostEnabled);
            _worldPacket.WriteBit(TokenBalanceEnabled);
            _worldPacket.WriteBit(LiveRegionCharacterListEnabled);
            _worldPacket.WriteBit(LiveRegionCharacterCopyEnabled);
            _worldPacket.WriteBit(LiveRegionAccountCopyEnabled);

            _worldPacket.WriteBit(LiveRegionKeyBindingsCopyEnabled);
            _worldPacket.WriteBit(Unknown901CheckoutRelated);
            _worldPacket.WriteBit(false); // SoftTargetEnabled
            _worldPacket.WriteBit(EuropaTicketSystemStatus != null); // IsEuropaTicketSystemStatusEnabled
            _worldPacket.WriteBit(false); // IsNameReservationEnabled
            _worldPacket.WriteBit(false); // IsLaunchETA
            _worldPacket.WriteBit(false); // AddonsDisabled
            _worldPacket.WriteBit(false); // Unk

            _worldPacket.WriteBit(false); // Unk
            _worldPacket.WriteBit(false); // SoMNotificationEnabled
            _worldPacket.WriteBit(false); // AccountSaveDataExportEnabled
            _worldPacket.WriteBit(false); // AccountLockedByExport
            _worldPacket.WriteBit(false); // Unk
            _worldPacket.WriteBit(false); // IsRealmHiddenAlert (no following 11-bit payload since false)

            _worldPacket.FlushBits();

            if (EuropaTicketSystemStatus != null)
                EuropaTicketSystemStatus.Write(_worldPacket);

            _worldPacket.WriteUInt32(TokenPollTimeSeconds);
            _worldPacket.WriteUInt32(KioskSessionMinutes);
            _worldPacket.WriteInt64(TokenBalanceAmount);
            _worldPacket.WriteInt32(MaxCharactersPerRealm);
            _worldPacket.WriteInt32(LiveRegionCharacterCopySourceRegions.Count);
            _worldPacket.WriteUInt32(BpayStoreProductDeliveryDelay);
            _worldPacket.WriteInt32(ActiveCharacterUpgradeBoostType);
            _worldPacket.WriteInt32(ActiveClassTrialBoostType);
            _worldPacket.WriteInt32(MinimumExpansionLevel);
            _worldPacket.WriteInt32(MaximumExpansionLevel);
            _worldPacket.WriteInt32(ActiveSeason);
            _worldPacket.WriteInt32(GameRuleValues.Count);
            _worldPacket.WriteInt16(MaxPlayerNameQueriesPerPacket);
            _worldPacket.WriteInt16(PlayerNameQueryTelemetryInterval);
            _worldPacket.WriteInt32(0);  // PlayerNameQueryInterval
            _worldPacket.WriteInt32(0);  // DebugTimeEventsSize
            _worldPacket.WriteInt32(0);  // Unused1007

            // No IsLaunchETA payload (bit was false), no DebugTimeEvents loop (count = 0).

            foreach (var sourceRegion in LiveRegionCharacterCopySourceRegions)
                _worldPacket.WriteInt32(sourceRegion);

            foreach (var rulePair in GameRuleValues)
                rulePair.Write(_worldPacket);

            return;
        }

        // Legacy modern (V1_14, V2_5) layout — preserve current behavior.
        _worldPacket.WriteBit(BpayStoreEnabled);
        _worldPacket.WriteBit(BpayStoreAvailable);
        _worldPacket.WriteBit(BpayStoreDisabledByParentalControls);
        _worldPacket.WriteBit(CharUndeleteEnabled);
        _worldPacket.WriteBit(CommerceSystemEnabled);
        _worldPacket.WriteBit(Unk14);
        _worldPacket.WriteBit(WillKickFromWorld);
        _worldPacket.WriteBit(IsExpansionPreorderInStore);
        _worldPacket.WriteBit(KioskModeEnabled);
        _worldPacket.WriteBit(CompetitiveModeEnabled);
        _worldPacket.WriteBit(TrialBoostEnabled);
        _worldPacket.WriteBit(TokenBalanceEnabled);
        _worldPacket.WriteBit(LiveRegionCharacterListEnabled);
        _worldPacket.WriteBit(LiveRegionCharacterCopyEnabled);
        _worldPacket.WriteBit(LiveRegionAccountCopyEnabled);
        _worldPacket.WriteBit(LiveRegionKeyBindingsCopyEnabled);
        _worldPacket.WriteBit(Unknown901CheckoutRelated);
        _worldPacket.WriteBit(EuropaTicketSystemStatus != null);
        _worldPacket.FlushBits();

        if (EuropaTicketSystemStatus != null)
            EuropaTicketSystemStatus.Write(_worldPacket);

        _worldPacket.WriteUInt32(TokenPollTimeSeconds);
        _worldPacket.WriteUInt32(KioskSessionMinutes);
        _worldPacket.WriteInt64(TokenBalanceAmount);
        _worldPacket.WriteInt32(MaxCharactersPerRealm);
        _worldPacket.WriteInt32(LiveRegionCharacterCopySourceRegions.Count);
        _worldPacket.WriteUInt32(BpayStoreProductDeliveryDelay);
        _worldPacket.WriteInt32(ActiveCharacterUpgradeBoostType);
        _worldPacket.WriteInt32(ActiveClassTrialBoostType);
        _worldPacket.WriteInt32(MinimumExpansionLevel);
        _worldPacket.WriteInt32(MaximumExpansionLevel);

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            _worldPacket.WriteInt32(ActiveSeason);
            _worldPacket.WriteInt32(GameRuleValues.Count);

            if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 2, 2, 5, 3))
                _worldPacket.WriteInt16(MaxPlayerNameQueriesPerPacket);

            if (ModernVersion.AddedInVersion(9, 2, 7, 1, 14, 4, 3, 4, 0))
                _worldPacket.WriteInt16(PlayerNameQueryTelemetryInterval);
        }

        foreach (var sourceRegion in LiveRegionCharacterCopySourceRegions)
            _worldPacket.WriteInt32(sourceRegion);

        if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
        {
            foreach (var rulePair in GameRuleValues)
                rulePair.Write(_worldPacket);
        }
    }

    public bool BpayStoreAvailable; // NYI
    public bool BpayStoreDisabledByParentalControls; // NYI
    public bool CharUndeleteEnabled;
    public bool BpayStoreEnabled; // NYI
    public bool CommerceSystemEnabled; // NYI
    public bool Unk14; // NYI
    public bool WillKickFromWorld; // NYI
    public bool IsExpansionPreorderInStore; // NYI
    public bool KioskModeEnabled; // NYI
    public bool CompetitiveModeEnabled; // NYI
    public bool TrialBoostEnabled; // NYI
    public bool TokenBalanceEnabled; // NYI
    public bool LiveRegionCharacterListEnabled; // NYI
    public bool LiveRegionCharacterCopyEnabled; // NYI
    public bool LiveRegionAccountCopyEnabled; // NYI
    public bool LiveRegionKeyBindingsCopyEnabled = false;
    public bool Unknown901CheckoutRelated = false; // NYI
    public EuropaTicketConfig EuropaTicketSystemStatus = null!;
    public List<int> LiveRegionCharacterCopySourceRegions = new();
    public uint TokenPollTimeSeconds;     // NYI
    public long TokenBalanceAmount;     // NYI 
    public int MaxCharactersPerRealm;
    public uint BpayStoreProductDeliveryDelay;     // NYI
    public int ActiveCharacterUpgradeBoostType;     // NYI
    public int ActiveClassTrialBoostType;     // NYI
    public int MinimumExpansionLevel;
    public int MaximumExpansionLevel;
    public int ActiveSeason;
    public List<GameRuleValuePair> GameRuleValues = new List<GameRuleValuePair>();
    public short MaxPlayerNameQueriesPerPacket;
    public short PlayerNameQueryTelemetryInterval;
    public uint KioskSessionMinutes;
}

public class MOTD : ServerPacket, ISpanWritable
{
    public MOTD() : base(Opcode.SMSG_MOTD) { }

    public override void Write()
    {
        _worldPacket.WriteBits(Text.Count, 4);
        _worldPacket.FlushBits();

        foreach (var line in Text)
        {
            _worldPacket.WriteBits(line.GetByteCount(), 7);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(line);
        }
    }

    // Cap for MOTD lines - reduced from 16 to 4 based on typical usage (1 byte = empty)
    private const int MaxLines = 4;
    // Cap per line (7 bits = max 128 chars)
    private const int MaxLineBytes = 128;
    // 4 bits(1) + per line: 7 bits(1) + text
    public int MaxSize => 1 + MaxLines * (1 + MaxLineBytes);

    public int WriteToSpan(Span<byte> buffer)
    {
        if (Text.Count > MaxLines)
            return -1;

        // Pre-validate all line lengths
        foreach (var line in Text)
        {
            if (Encoding.UTF8.GetByteCount(line) > MaxLineBytes)
                return -1;
        }

        var writer = new SpanPacketWriter(buffer);
        writer.WriteBits((uint)Text.Count, 4);
        writer.FlushBits();

        foreach (var line in Text)
        {
            int lineBytes = Encoding.UTF8.GetByteCount(line);
            writer.WriteBits((uint)lineBytes, 7);
            writer.FlushBits();
            writer.WriteString(line);
        }
        return writer.Position;
    }

    public List<string> Text = new List<string>();
}

public class SetTimeZoneInformation : ServerPacket, ISpanWritable
{
    public SetTimeZoneInformation() : base(Opcode.SMSG_SET_TIME_ZONE_INFORMATION) { }

    public override void Write()
    {
        // This build reads three 7-bit lengths and three strings, not two: the client's
        // constructor is u8, u8, u8 then three string reads. CypherCore has the same third field
        // (ServerRegionalTimeTZ); the older two-string form would leave the client one string short.
        if (ModernVersion.Uses550Engine)
        {
            _worldPacket.WriteBits(ServerTimeTZ.GetByteCount(), 7);
            _worldPacket.WriteBits(GameTimeTZ.GetByteCount(), 7);
            _worldPacket.WriteBits(ServerRegionalTimeTZ.GetByteCount(), 7);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(ServerTimeTZ);
            _worldPacket.WriteString(GameTimeTZ);
            _worldPacket.WriteString(ServerRegionalTimeTZ);
            return;
        }

        _worldPacket.WriteBits(ServerTimeTZ.GetByteCount(), 7);
        _worldPacket.WriteBits(GameTimeTZ.GetByteCount(), 7);
        _worldPacket.WriteString(ServerTimeTZ);
        _worldPacket.WriteString(GameTimeTZ);
    }

    // Cap for timezone strings - 7 bits = max 128 chars each
    private const int MaxTZBytes = 64;
    // 14 bits (2 bytes) + 2 strings
    public int MaxSize => 2 + MaxTZBytes * 2;

    public int WriteToSpan(Span<byte> buffer)
    {
        int serverBytes = Encoding.UTF8.GetByteCount(ServerTimeTZ);
        int gameBytes = Encoding.UTF8.GetByteCount(GameTimeTZ);
        if (serverBytes > MaxTZBytes || gameBytes > MaxTZBytes)
            return -1;

        if (ModernVersion.Uses550Engine)
            return -1;      // three-string form, take the ordinary Write() path

        var writer = new SpanPacketWriter(buffer);
        writer.WriteBits((uint)serverBytes, 7);
        writer.WriteBits((uint)gameBytes, 7);
        writer.WriteString(ServerTimeTZ);
        writer.WriteString(GameTimeTZ);
        return writer.Position;
    }

    public string ServerTimeTZ = string.Empty;
    public string GameTimeTZ = string.Empty;
    public string ServerRegionalTimeTZ = string.Empty;
}

public struct SavedThrottleObjectState
{
    public uint MaxTries;
    public uint PerMilliseconds;
    public uint TryCount;
    public uint LastResetTimeBeforeNow;

    public void Write(WorldPacket data)
    {
        data.WriteUInt32(MaxTries);
        data.WriteUInt32(PerMilliseconds);
        data.WriteUInt32(TryCount);
        data.WriteUInt32(LastResetTimeBeforeNow);
    }
}

public class EuropaTicketConfig
{
    public bool TicketsEnabled;
    public bool BugsEnabled;
    public bool ComplaintsEnabled;
    public bool SuggestionsEnabled;

    public SavedThrottleObjectState ThrottleState;

    public void Write(WorldPacket data)
    {
        data.WriteBit(TicketsEnabled);
        data.WriteBit(BugsEnabled);
        data.WriteBit(ComplaintsEnabled);
        data.WriteBit(SuggestionsEnabled);

        ThrottleState.Write(data);
    }
}
