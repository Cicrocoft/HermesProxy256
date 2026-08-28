// The values-update (updateType 0) encoder for build 69110 - root cause 3, PLAN-256 Track C.
//
// WHAT THIS REPLACES. Until now the non-create branch of WriteToPacket wrote a modern fragment
// header followed by the 2.4.3 masked update-field array that BuildValuesUpdate produces. The
// client's CGObject update deserialiser (vtable+0x1C8, thunk RVA 0x24D380) is a modern
// UpdateField changes-mask reader and cannot parse that, so no value update on this build has
// ever been applied. Health, power, target and every state change travel this path.
//
// TWO SOURCES, DELIBERATELY SPLIT.
//
//   * The MECHANISM - block-mask emission, FlushBits placement, the nested-mask and
//     ignoreNestedChangesMask distinction, and the allowed-mask-for-target filter - is taken from
//     a working encoder for the same engine family:
//     /c/projekter/TrinityCore/src/server/game/Entities/Object/Updates/UpdateFields.cpp
//     (UnitData::WriteUpdate line 1279, PlayerData::WriteUpdate line 2706, QuestLog::WriteUpdate
//     line 2154, VisibleItem::WriteUpdate line 892).
//
//   * The FIELD SET and BIT NUMBERING come from the 5.5.3 arm this client actually is:
//     .wpp/WowPacketParserModule.V5_5_0_61735/Parsers/UpdateFieldsHandler553.cs
//     TrinityCore master is 11.x and its numbering is wrong here - its UnitData carries Field_314
//     at bit 1 and DisplayID at 6, where 553 (and this client's create block) has Health at 5,
//     MaxHealth at 6 and DisplayID at 7. Using TC's numbering would write every field into the
//     wrong slot.
//
// THE FRAMING, transcribed from WPP's own UpdateHandler.cs (lines 84-208), which is the reader:
//
//     u8    IsOwned
//     u8    HasFragmentUpdates        0 => no fragment-id re-merge  (we never change the list)
//     bytes changedFragments          (fragmentBitCount + 7) / 8, LSB-indexed
//                                     one bit per updateable fragment, TWO if it is indirect
//     if changedFragments[objIdx] && changedFragments[objIdx + 1]:
//         u32 updateTypeFlag          1 << modern TypeID  (Object 0x1 ... Corpse 0x400)
//         for each set bit, in ascending TypeID order: that descriptor's ReadUpdate*Data
//
// Our fragment list is [CGObject, Tag_*...] sorted ascending, only CGObject is updateable, so
// objIdx == 0, fragmentBitCount == 2 and the mask byte is 0b11 == 3. **The byte 3 the old code
// wrote was therefore already correct.** Section 124's note that it is "not a two-bit CGObject
// active|changed pair" was half right - it is a per-fragment mask, and for one indirect fragment
// that mask is exactly two bits, changed and active. What was wrong was everything after it.
//
// Each ReadUpdate*Data is a hierarchical changes mask, exactly TC's WriteUpdate inverted:
//
//     WriteBits(GetBlocksMask(0), nBlocks)    which 32-bit blocks follow   (small descriptors use
//     for each non-zero block: WriteBits(block, 32)                        one flat WriteBits(n))
//     ... then every set bit's value, in ASCENDING BIT ORDER
//
// Bit 0 (and 32, 64, 96 ... in the larger descriptors) is a group gate: the fields it covers are
// read only when it is set. Arrays get a gate bit plus one bit per element.
//
// THE ORDER THAT MATTERS HERE IS NOT THE CREATE ORDER. 553's create reader and 553's update
// decoder disagree about where arrays live - ReadCreateUnitData reads VirtualItems between
// FactionTemplate and Flags, ReadUpdateUnitData carries it at bit 170, after every scalar. Our own
// create writer shows the same split from the other side: WriteActivePlayerData emits InvSlots
// first on the wire while the update decoder numbers it 136..282. So the two deviations recorded
// in tools-256-spike/model-256.md for the CREATE path (VirtualItems at the block end, the +78
// tail) say nothing about the update bit numbering, and are deliberately NOT applied here.
//
// WHERE THE EVIDENCE STOPS, stated rather than discovered later:
//
//   * The bit numbering is 5.5.3's and was NOT checked against 69110's own reader (0x24D380).
//     Two recorded create deviations are consistent with 69110 declaring extra UnitData fields
//     (a packed guid at obj+0x308 and a 7-byte name trailer) and with it NOT declaring
//     ActivePlayerData's TrackResourceMask[2] / RestInfo[2] / CombatRatings[32]. If either is a
//     declaration difference rather than a create-serialiser difference then the bits AFTER the
//     affected field have shifted, and only the bits before it are safe. That is what the
//     escalation ladder on HERMES_256_VALUESUPDATE is for - see the knob.
//   * Dynamic (variable-length) fields are never sent. Their per-element update mask has its own
//     encoding (WriteUpdateMask / WriteCompleteDynamicFieldUpdateMask) which is not implemented
//     here; the bits that gate them are never set, so the reader never looks for one.
//   * Nothing above ActivePlayerData bit 282 is ever sent, for the reason above.
//
// Gated on HERMES_256_VALUESUPDATE, default 0 == the previous behaviour, byte for byte.

using Framework.IO;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Objects.Version.V2_5_6_69110;

public partial class ObjectUpdateBuilder
{
    /// <summary>
    /// The escalation ladder. One variable per session, each step adding exactly one thing that
    /// could be wrong, in increasing order of how far its bits sit from evidence.
    /// <list type="bullet">
    /// <item>0 - off. The legacy 2.4.3 masked array, previous behaviour, byte for byte.</item>
    /// <item>1 - modern encoder, scalar fields only. No arrays, no ActivePlayerData, no quest log.
    ///       This is the arm that carries UnitData.Health (bit 5) and MaxHealth (bit 6), which are
    ///       the two earliest bits in the descriptor and cannot be displaced by any deviation
    ///       recorded so far. **Watch a mob's health bar during melee.**</item>
    /// <item>2 - adds the fixed arrays: Power/MaxPower/ModPowerRegen (bits 140-169), VirtualItems
    ///       (171-173), AttackRoundBaseTime (175-177), Stats (179-193), Resistances and the power
    ///       cost arrays (195-229), PlayerData.VisibleItems (73-91), PartyType, AvgItemLevel, and
    ///       ActivePlayerData.InvSlots (137-159, and only with HERMES_256_INVSLOTS=1). If 1 works
    ///       and 2 does not, the array bit base is a real 69110 deviation.</item>
    /// <item>3 - adds ActivePlayerData: Coinage bit 33, XP 35, NextLevelXP 36, WatchedFactionIndex
    ///       99 and the rest of the scalars below bit 283.</item>
    /// <item>4 - adds PlayerData.QuestLog (bits 47-71) through the update path, in create form.
    ///       This is the one route to a populated quest log that does not need the 1654-byte
    ///       PartyMember block in the create, which is what froze the client on 22 Aug.</item>
    /// </list>
    /// </summary>
    static readonly int s_valuesUpdate =
        int.TryParse(Environment.GetEnvironmentVariable("HERMES_256_VALUESUPDATE"), out var vu) ? vu : 0;

    internal static bool ModernValuesEnabled => s_valuesUpdate >= 1;
    static bool ModernValuesArrays => s_valuesUpdate >= 2;
    static bool ModernValuesActive => s_valuesUpdate >= 3;
    static bool ModernValuesQuestLog => s_valuesUpdate >= 4;

    // See the HERMES_256_PDSHIFT1 ground-truth note in BuildPlayerDataUpdate. Default off.
    static readonly bool s_pdShift1 =
        System.Environment.GetEnvironmentVariable("HERMES_256_PDSHIFT1") == "1";

    // See the HERMES_256_UNITARR1 ground-truth note in BuildUnitDataUpdate. Default off.
    static readonly bool s_unitArr1 =
        System.Environment.GetEnvironmentVariable("HERMES_256_UNITARR1") == "1";

    /// <summary>
    /// A changes mask over <c>blocks * 32</c> bits, written the way 553's decoder reads it and
    /// TrinityCore's <c>WriteUpdate</c> writes it.
    /// <para>
    /// The bit-to-integer mapping is fixed by WPP's <c>new BitArray(int[])</c>: bit <c>n</c> is
    /// bit <c>n % 32</c> (LSB-first) of block <c>n / 32</c>. <c>ReadBits</c> is MSB-first over the
    /// stream and so is <see cref="ByteBuffer.WriteBits(uint,int)"/>, so writing the same integer
    /// reproduces the same bit positions. Verified against the create path, which already round
    /// trips WriteCreateBitsModern through this client.
    /// </para>
    /// </summary>
    sealed class UfMask
    {
        readonly uint[] _blocks;
        public UfMask(int blocks) => _blocks = new uint[blocks];

        public void Set(int bit) => _blocks[bit >> 5] |= 1u << (bit & 31);
        public void Clear(int bit) => _blocks[bit >> 5] &= ~(1u << (bit & 31));
        public bool Get(int bit) => (_blocks[bit >> 5] & (1u << (bit & 31))) != 0;

        public bool Any
        {
            get
            {
                foreach (var b in _blocks)
                    if (b != 0)
                        return true;
                return false;
            }
        }

        public bool AnyInRange(int from, int to)
        {
            for (int i = from; i <= to; ++i)
                if (Get(i))
                    return true;
            return false;
        }

        public void ClearRange(int from, int to)
        {
            for (int i = from; i <= to; ++i)
                Clear(i);
        }

        /// <summary>
        /// A copy with every bit at or above <paramref name="firstBit"/> moved up by
        /// <paramref name="by"/>. This build's descriptors renumber only PART of a mask relative to
        /// the 553 field list the writers were generated from, so the mask is built in our numbering
        /// (where the payload writer's Get() calls live) and translated to the wire numbering here.
        /// The shift is monotonic, so ascending order — and therefore payload order — is preserved.
        /// </summary>
        public UfMask ShiftedAbove(int firstBit, int by)
        {
            var r = new UfMask(_blocks.Length);
            for (int b = 0; b < _blocks.Length * 32; ++b)
                if (Get(b))
                    r.Set(b >= firstBit ? b + by : b);
            return r;
        }

        /// <summary>TC: <c>WriteBits(GetBlocksMask(0), n)</c> then every non-zero block.</summary>
        public void WriteHierarchical(WorldPacket w)
        {
            uint maskMask = 0;
            for (int i = 0; i < _blocks.Length; ++i)
                if (_blocks[i] != 0)
                    maskMask |= 1u << i;
            w.WriteBits(maskMask, _blocks.Length);
            for (int i = 0; i < _blocks.Length; ++i)
                if (_blocks[i] != 0)
                    w.WriteBits(_blocks[i], 32);
        }

        /// <summary>Flat form, used by descriptors with fewer than 32 fields (no block mask).</summary>
        public void WriteFlat(WorldPacket w, int bitCount) => w.WriteBits(_blocks[0], bitCount);
    }

    // ---------------------------------------------------------------------------------------
    // Entry point
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the body of a values update - everything after IsOwned and HasFragmentUpdates -
    /// into <paramref name="body"/>. Returns false when nothing changed, in which case the caller
    /// writes a zero changed-fragment mask and no body: a well-formed empty update.
    /// </summary>
    bool WriteModernValuesUpdate(WorldPacket body)
    {
        // Each descriptor is built separately. Every ReadUpdate*Data opens with ResetBitReader and
        // our sub-buffers are flushed by GetData(), so both ends are byte-aligned and the pieces
        // concatenate without a seam.
        uint typeFlag = 0;
        var parts = new List<byte[]>(4);

        void Add(uint flag, WorldPacket p)
        {
            typeFlag |= flag;
            parts.Add(p.GetData());
        }

        WorldPacket? objectPart = BuildObjectDataUpdate();
        if (objectPart != null)
            Add(0x0001u, objectPart);

        switch (m_objectType)
        {
            case Enums.ObjectTypeBCC.Item:
            case Enums.ObjectTypeBCC.Container:
            {
                var item = BuildItemDataUpdate();
                if (item != null)
                    Add(0x0002u, item);
                if (m_objectType == Enums.ObjectTypeBCC.Container)
                {
                    var container = BuildContainerDataUpdate();
                    if (container != null)
                        Add(0x0004u, container);
                }
                break;
            }
            case Enums.ObjectTypeBCC.Unit:
            case Enums.ObjectTypeBCC.Player:
            case Enums.ObjectTypeBCC.ActivePlayer:
            {
                // HERMES_256_APDINV116: on this build the client's values-mask numbering differs
                // from the 553 numbering some of these builders use (proven for APD InvSlots:
                // gate 116, not 136). A mis-numbered mask makes the client read the wrong fields
                // AND desyncs the cursor for every later part in the same block — an equip's
                // mis-numbered part ahead of the APD part turned two slot guids into
                // Coinage=462086913954257 live. UNIT data demonstrably diverges from 553 on this
                // build (the VirtualItems create divergence), so the self Unit part stays
                // suppressed until renumbered. PLAYER data's 69110 create layout matched 553
                // (pdapd_walk, 24-25 Aug), so its 553-numbered values part is kept — it carries
                // QuestLog (bits 47+i) and VisibleItems; if equip money-corruption returns, this
                // was wrong and the Player part must be suppressed again.
                // The self Unit part was suppressed only because its numbering was unverified;
                // HERMES_256_UNITARR1 fixes it from ground truth, so it ships again when that is on.
                bool suppressUnitSelf = s_apdInv116 && !s_unitArr1
                    && m_objectType == Enums.ObjectTypeBCC.ActivePlayer;
                if (suppressUnitSelf && m_updateData.UnitData != null)
                    Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                        "[256-spike] APDINV116: suppressed 553-numbered Unit part on self values update");
                var unit = suppressUnitSelf ? null : BuildUnitDataUpdate();
                if (unit != null)
                    Add(0x0020u, unit);

                // Build the APD part first so the Player part can be dropped when they'd combine.
                // A backpack move/split (APD InvSlots ONLY, flag 0x80) renders live; an equip/unequip
                // ALSO changes VisibleItems (Player, flag 0x40), producing a mixed 0xC0 body whose
                // Player part — still on unverified 69110 array numbering — desyncs the cursor so the
                // APD InvSlots never apply and the item does not move. When APDINV116 is on and the
                // self update carries an APD part, suppress the Player part so equip/unequip render
                // via InvSlots like a backpack move (paper-doll appearance then lags to relog). A
                // QuestLog update carries no APD part, so quests keep their live Player update.
                WorldPacket? active = null;
                if (m_objectType == Enums.ObjectTypeBCC.ActivePlayer && (ModernValuesActive || m_updateData.ForceApdValuesTest))
                    active = BuildActivePlayerDataUpdate();

                if (m_objectType != Enums.ObjectTypeBCC.Unit)
                {
                    bool dropPlayerSelf = s_apdInv116
                        && m_objectType == Enums.ObjectTypeBCC.ActivePlayer
                        && active != null;
                    if (dropPlayerSelf && m_updateData.PlayerData != null)
                        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                            "[256-spike] APDINV116: suppressed Player part on self APD update (equip/unequip renders via InvSlots)");
                    var player = dropPlayerSelf ? null : BuildPlayerDataUpdate();
                    if (player != null)
                        Add(0x0040u, player);
                }

                if (active != null)
                    Add(0x0080u, active);
                break;
            }
            case Enums.ObjectTypeBCC.GameObject:
            {
                var go = BuildGameObjectDataUpdate();
                if (go != null)
                    Add(0x0100u, go);
                break;
            }
            case Enums.ObjectTypeBCC.DynamicObject:
            {
                var dyn = BuildDynamicObjectDataUpdate();
                if (dyn != null)
                    Add(0x0200u, dyn);
                break;
            }
            case Enums.ObjectTypeBCC.Corpse:
            {
                var corpse = BuildCorpseDataUpdate();
                if (corpse != null)
                    Add(0x0400u, corpse);
                break;
            }
        }

        if (typeFlag == 0)
            return false;

        // Diagnostic: parts are concatenated with no length prefix, so if any part's payload does
        // not match what its own mask promises, every later part is read at the wrong offset. This
        // prints each part's byte length so a seam can be located without guessing.
        if (parts.Count > 1)
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] values parts flag=0x{typeFlag:X} lengths=[{string.Join(",", parts.ConvertAll(p => p.Length))}]" +
                $" hex={string.Join("|", parts.ConvertAll(p => System.Convert.ToHexString(p)))}");

        body.WriteUInt32(typeFlag);
        foreach (var part in parts)
            body.WriteBytes(part);
        return true;
    }

    // ---------------------------------------------------------------------------------------
    // ObjectData - ReadUpdateObjectData, flat 4-bit mask
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildObjectDataUpdate()
    {
        var d = m_updateData.ObjectData;
        if (d == null)
            return null;

        var m = new UfMask(1);
        if (d.EntryID.HasValue) m.Set(1);
        if (d.DynamicFlags.HasValue) m.Set(2);
        if (d.Scale.HasValue) m.Set(3);
        if (!m.Any)
            return null;
        m.Set(0);

        var w = new WorldPacket();
        m.WriteFlat(w, 4);
        w.FlushBits();
        if (m.Get(1)) w.WriteInt32(d.EntryID!.Value);
        if (m.Get(2)) w.WriteUInt32(d.DynamicFlags!.Value);
        if (m.Get(3)) w.WriteFloat(d.Scale!.Value);
        return w;
    }

    // ---------------------------------------------------------------------------------------
    // UnitData - ReadUpdateUnitData, 8 blocks / 230 bits
    //
    // Group gates: 0 -> 1..31, 32 -> 33..63, 64 -> 65..95, 96 -> 97..118.
    // Top-level array gates: 119 (power arrays), 170 (VirtualItems), 174 (AttackRoundBaseTime),
    // 178 (Stats), 194 (Resistances / PowerCost).
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildUnitDataUpdate()
    {
        var d = m_updateData.UnitData;
        if (d == null)
            return null;

        var m = new UfMask(8);

        // --- gate 0: bits 5..31 -------------------------------------------------------------
        if (d.Health.HasValue) m.Set(5);
        if (d.MaxHealth.HasValue) m.Set(6);
        if (d.DisplayID.HasValue) m.Set(7);
        if (d.NpcFlags[0].HasValue) m.Set(8);
        if (d.NpcFlags[1].HasValue) m.Set(9);
        if (d.StateSpellVisualID.HasValue) m.Set(10);
        if (d.StateAnimID.HasValue) m.Set(11);
        if (d.StateAnimKitID.HasValue) m.Set(12);
        if (d.Charm != null) m.Set(13);
        if (d.Summon != null) m.Set(14);
        if (d.Critter != null) m.Set(15);
        if (d.CharmedBy != null) m.Set(16);
        if (d.SummonedBy != null) m.Set(17);
        if (d.CreatedBy != null) m.Set(18);
        if (d.DemonCreator != null) m.Set(19);
        if (d.LookAtControllerTarget != null) m.Set(20);
        if (d.Target != null) m.Set(21);
        if (d.BattlePetCompanionGUID != null) m.Set(22);
        if (d.BattlePetDBID.HasValue) m.Set(23);
        if (d.ChannelData.HasValue) m.Set(24);
        if (d.SummonedByHomeRealm.HasValue) m.Set(25);
        if (d.RaceId.HasValue) m.Set(26);
        if (d.ClassId.HasValue) { m.Set(27); m.Set(28); }   // ClassId and PlayerClassId
        if (d.SexId.HasValue) m.Set(29);
        if (d.DisplayPower.HasValue) m.Set(30);
        if (d.OverrideDisplayPowerID.HasValue) m.Set(31);

        // --- gate 32: bits 33..63 -----------------------------------------------------------
        if (d.Level.HasValue) m.Set(33);
        if (d.EffectiveLevel.HasValue) m.Set(34);
        if (d.ContentTuningID.HasValue) m.Set(35);
        if (d.ScalingLevelMin.HasValue) m.Set(36);
        if (d.ScalingLevelMax.HasValue) m.Set(37);
        if (d.ScalingLevelDelta.HasValue) m.Set(38);
        if (d.ScalingFactionGroup.HasValue) m.Set(39);
        if (d.FactionTemplate.HasValue) m.Set(40);
        if (d.Flags.HasValue) m.Set(41);
        if (d.Flags2.HasValue) m.Set(42);
        if (d.Flags3.HasValue) m.Set(43);
        // 44 Flags4 - no source in our model, never sent.
        if (d.AuraState.HasValue) m.Set(45);
        if (d.RangedAttackRoundBaseTime.HasValue) m.Set(46);
        if (d.BoundingRadius.HasValue) m.Set(47);
        if (d.CombatReach.HasValue) m.Set(48);
        if (d.DisplayScale.HasValue) m.Set(49);
        if (d.NativeDisplayID.HasValue) m.Set(50);
        if (d.NativeXDisplayScale.HasValue) m.Set(51);
        if (d.MountDisplayID.HasValue) m.Set(52);
        if (d.MinDamage.HasValue) m.Set(53);
        if (d.MaxDamage.HasValue) m.Set(54);
        if (d.MinOffHandDamage.HasValue) m.Set(55);
        if (d.MaxOffHandDamage.HasValue) m.Set(56);
        if (d.StandState.HasValue) m.Set(57);
        // 58 PetTalentPoints - WriteUnitDataReal emits 0 there (our PetLoyaltyIndex is not the same
        // field on this engine), so leaving it unset keeps create and update consistent.
        if (d.VisFlags.HasValue) m.Set(59);
        if (d.AnimTier.HasValue) m.Set(60);
        if (d.PetNumber.HasValue) m.Set(61);
        if (d.PetNameTimestamp.HasValue) m.Set(62);
        if (d.PetExperience.HasValue) m.Set(63);

        // --- gate 64: bits 65..95 -----------------------------------------------------------
        if (d.PetNextLevelExperience.HasValue) m.Set(65);
        if (d.ModCastSpeed.HasValue) m.Set(66);      // ModCastingSpeed
        if (d.ModCastHaste.HasValue) m.Set(67);      // ModSpellHaste
        if (d.ModHaste.HasValue) m.Set(68);
        if (d.ModRangedHaste.HasValue) m.Set(69);
        if (d.ModHasteRegen.HasValue) m.Set(70);
        if (d.ModTimeRate.HasValue) m.Set(71);
        if (d.CreatedBySpell.HasValue) m.Set(72);
        if (d.EmoteState.HasValue) m.Set(73);
        if (d.TrainingPointsUsed.HasValue) m.Set(74);
        if (d.TrainingPointsTotal.HasValue) m.Set(75);
        if (d.BaseMana.HasValue) m.Set(76);
        if (d.BaseHealth.HasValue) m.Set(77);
        if (d.SheatheState.HasValue) m.Set(78);
        if (d.PvpFlags.HasValue) m.Set(79);
        if (d.PetFlags.HasValue) m.Set(80);
        if (d.ShapeshiftForm.HasValue) m.Set(81);
        if (d.AttackPower.HasValue) m.Set(82);
        if (d.AttackPowerModPos.HasValue) m.Set(83);
        if (d.AttackPowerModNeg.HasValue) m.Set(84);
        if (d.AttackPowerMultiplier.HasValue) m.Set(85);
        if (d.RangedAttackPower.HasValue) m.Set(86);
        if (d.RangedAttackPowerModPos.HasValue) m.Set(87);
        if (d.RangedAttackPowerModNeg.HasValue) m.Set(88);
        if (d.RangedAttackPowerMultiplier.HasValue) m.Set(89);
        if (d.AttackSpeedAura.HasValue) m.Set(90);   // SetAttackSpeedAura
        if (d.Lifesteal.HasValue) m.Set(91);
        if (d.MinRangedDamage.HasValue) m.Set(92);
        if (d.MaxRangedDamage.HasValue) m.Set(93);
        if (d.MaxHealthModifier.HasValue) m.Set(94);
        if (d.HoverHeight.HasValue) m.Set(95);

        // --- gate 96: bits 97..118 ----------------------------------------------------------
        if (d.MinItemLevelCutoff.HasValue) m.Set(97);
        if (d.MinItemLevel.HasValue) m.Set(98);
        if (d.MaxItemLevel.HasValue) m.Set(99);
        if (d.WildBattlePetLevel.HasValue) m.Set(100);
        if (d.BattlePetCompanionNameTimestamp.HasValue) m.Set(101);
        if (d.InteractSpellID.HasValue) m.Set(102);
        if (d.ScaleDuration.HasValue) m.Set(103);
        if (d.LooksLikeMountID.HasValue) m.Set(104);
        if (d.LooksLikeCreatureID.HasValue) m.Set(105);
        if (d.LookAtControllerID.HasValue) m.Set(106);
        if (d.GuildGUID != null) m.Set(108);

        // --- arrays -------------------------------------------------------------------------
        if (ModernValuesArrays)
        {
            // Our model holds 7 power slots, the wire has 10. Bits 120-139 (the two PowerRegen
            // arrays) have no source and are never set.
            for (int i = 0; i < 7; ++i)
            {
                if (d.Power[i].HasValue) m.Set(140 + i);
                if (d.MaxPower[i].HasValue) m.Set(150 + i);
                if (d.ModPowerRegen[i].HasValue) m.Set(160 + i);
            }
            for (int i = 0; i < 3; ++i)
                if (d.VirtualItems[i].HasValue) m.Set(171 + i);
            for (int i = 0; i < 2; ++i)
                if (d.AttackRoundBaseTime[i].HasValue) m.Set(175 + i);
            for (int i = 0; i < 5; ++i)
            {
                if (d.Stats[i].HasValue) m.Set(179 + i);
                if (d.StatPosBuff[i].HasValue) m.Set(184 + i);
                if (d.StatNegBuff[i].HasValue) m.Set(189 + i);
            }
            for (int i = 0; i < 7; ++i)
            {
                if (d.Resistances[i].HasValue) m.Set(195 + i);
                if (d.ResistanceBuffModsPositive[i].HasValue) m.Set(202 + i);
                if (d.ResistanceBuffModsNegative[i].HasValue) m.Set(209 + i);
                if (d.PowerCostModifier[i].HasValue) m.Set(216 + i);
                if (d.PowerCostMultiplier[i].HasValue) m.Set(223 + i);
            }
        }

        FilterUnitDataByVisibility(m);

        if (!m.Any)
            return null;

        // Group gates, set only when the group carries something - so an unused gate never costs
        // the reader a bit it would then look for.
        if (m.AnyInRange(1, 31)) m.Set(0);
        if (m.AnyInRange(33, 63)) m.Set(32);
        if (m.AnyInRange(65, 95)) m.Set(64);
        if (m.AnyInRange(97, 118)) m.Set(96);
        if (m.AnyInRange(120, 169)) m.Set(119);
        if (m.AnyInRange(171, 173)) m.Set(170);
        if (m.AnyInRange(175, 177)) m.Set(174);
        if (m.AnyInRange(179, 193)) m.Set(178);
        if (m.AnyInRange(195, 229)) m.Set(194);

        var w = new WorldPacket();
        // HERMES_256_UNITARR1: measured against the live Blizzard captures (gt_unitbits.py over
        // tools-256-spike/ground-truth/w13_s2.bin + w14_s2.bin — a session with real combat), this
        // build's UnitData values-mask matches 553 for the SCALARS but is one bit higher from the
        // array region on. Every observation is unanimous:
        //   unshifted  bits 0, 5 Health, 6 MaxHealth, 32, 33 Level, 41 Flags, 45 AuraState, 64,
        //              76 BaseMana, 78 SheatheState, 96   (135/118/77/52 confirmations, 0 against)
        //   +1         gate 119->120, Power[i] 140->141, MaxPower[i] 150->151, array gate 194->195
        //              (224/160/60 confirmations, 0 against)
        // Payload lengths settle it exactly: {0,5,64,78} = 9 B = Health u64 + SheatheState u8, and
        // {0,5,6,32,33,64,76,120,141,151,195} = 32 B only if 195 is our array gate 194 shifted.
        // The inserted field lands in 109..118, where this writer emits nothing, so translating at
        // the mask keeps every payload Get() below on our own numbering.
        (s_unitArr1 ? m.ShiftedAbove(119, 1) : m).WriteHierarchical(w);
        // Bit 1 (StateWorldEffectIDs) and bits 2-4 (the dynamic fields) are never set, so no
        // dynamic count and no per-element mask follow the block mask. TC flushes here twice; one
        // flush is equivalent when nothing was written between them.
        w.FlushBits();

        if (m.Get(5)) w.WriteInt64(d.Health!.Value);
        if (m.Get(6)) w.WriteInt64(d.MaxHealth!.Value);
        if (m.Get(7)) w.WriteInt32(d.DisplayID!.Value);
        if (m.Get(8)) w.WriteUInt32(d.NpcFlags[0]!.Value);
        if (m.Get(9)) w.WriteUInt32(d.NpcFlags[1]!.Value);
        if (m.Get(10)) w.WriteUInt32(d.StateSpellVisualID!.Value);
        if (m.Get(11)) w.WriteUInt32(d.StateAnimID!.Value);
        if (m.Get(12)) w.WriteUInt32(d.StateAnimKitID!.Value);
        if (m.Get(13)) w.WritePackedGuid128(d.Charm!.Value);
        if (m.Get(14)) w.WritePackedGuid128(d.Summon!.Value);
        if (m.Get(15)) w.WritePackedGuid128(d.Critter!.Value);
        if (m.Get(16)) w.WritePackedGuid128(d.CharmedBy!.Value);
        if (m.Get(17)) w.WritePackedGuid128(d.SummonedBy!.Value);
        if (m.Get(18)) w.WritePackedGuid128(d.CreatedBy!.Value);
        if (m.Get(19)) w.WritePackedGuid128(d.DemonCreator!.Value);
        if (m.Get(20)) w.WritePackedGuid128(d.LookAtControllerTarget!.Value);
        if (m.Get(21)) w.WritePackedGuid128(d.Target!.Value);
        if (m.Get(22)) w.WritePackedGuid128(d.BattlePetCompanionGUID!.Value);
        if (m.Get(23)) w.WriteUInt64(d.BattlePetDBID!.Value);
        if (m.Get(24))
        {
            // ReadUpdateUnitChannel: no mask of its own, four fields.
            w.WriteInt32(d.ChannelData!.Value.SpellID);
            w.WriteInt32(d.ChannelData!.Value.SpellXSpellVisualID);
            w.WriteUInt32(0);      // StartTimeMs - UNIT_CHANNEL_SPELL carries no timing
            w.WriteUInt32(0);      // Duration
        }
        if (m.Get(25)) w.WriteUInt32(d.SummonedByHomeRealm!.Value);
        if (m.Get(26)) w.WriteUInt8(d.RaceId!.Value);
        if (m.Get(27)) w.WriteUInt8(d.ClassId!.Value);
        if (m.Get(28)) w.WriteUInt8(d.ClassId!.Value);
        if (m.Get(29)) w.WriteUInt8(d.SexId!.Value);
        if (m.Get(30)) w.WriteUInt8((byte)d.DisplayPower!.Value);
        if (m.Get(31)) w.WriteUInt32(d.OverrideDisplayPowerID!.Value);

        if (m.Get(33)) w.WriteInt32(d.Level!.Value);
        if (m.Get(34)) w.WriteInt32(d.EffectiveLevel!.Value);
        if (m.Get(35)) w.WriteInt32(d.ContentTuningID!.Value);
        if (m.Get(36)) w.WriteInt32(d.ScalingLevelMin!.Value);
        if (m.Get(37)) w.WriteInt32(d.ScalingLevelMax!.Value);
        if (m.Get(38)) w.WriteInt32(d.ScalingLevelDelta!.Value);
        if (m.Get(39)) w.WriteUInt8((byte)d.ScalingFactionGroup!.Value);
        if (m.Get(40)) w.WriteInt32(d.FactionTemplate!.Value);
        if (m.Get(41)) w.WriteUInt32(d.Flags!.Value);
        if (m.Get(42)) w.WriteUInt32(d.Flags2!.Value);
        if (m.Get(43)) w.WriteUInt32(d.Flags3!.Value);
        if (m.Get(45)) w.WriteUInt32(d.AuraState!.Value);
        if (m.Get(46)) w.WriteUInt32(d.RangedAttackRoundBaseTime!.Value);
        if (m.Get(47)) w.WriteFloat(d.BoundingRadius!.Value);
        if (m.Get(48)) w.WriteFloat(d.CombatReach!.Value);
        if (m.Get(49)) w.WriteFloat(d.DisplayScale!.Value);
        if (m.Get(50)) w.WriteInt32(d.NativeDisplayID!.Value);
        if (m.Get(51)) w.WriteFloat(d.NativeXDisplayScale!.Value);
        if (m.Get(52)) w.WriteInt32(d.MountDisplayID!.Value);
        if (m.Get(53)) w.WriteFloat(d.MinDamage!.Value);
        if (m.Get(54)) w.WriteFloat(d.MaxDamage!.Value);
        if (m.Get(55)) w.WriteFloat(d.MinOffHandDamage!.Value);
        if (m.Get(56)) w.WriteFloat(d.MaxOffHandDamage!.Value);
        if (m.Get(57)) w.WriteUInt8(d.StandState!.Value);
        if (m.Get(59)) w.WriteUInt8(d.VisFlags!.Value);
        if (m.Get(60)) w.WriteUInt8(d.AnimTier!.Value);
        if (m.Get(61)) w.WriteUInt32(d.PetNumber!.Value);
        if (m.Get(62)) w.WriteUInt32(d.PetNameTimestamp!.Value);
        if (m.Get(63)) w.WriteUInt32(d.PetExperience!.Value);

        if (m.Get(65)) w.WriteUInt32(d.PetNextLevelExperience!.Value);
        if (m.Get(66)) w.WriteFloat(d.ModCastSpeed!.Value);
        if (m.Get(67)) w.WriteFloat(d.ModCastHaste!.Value);
        if (m.Get(68)) w.WriteFloat(d.ModHaste!.Value);
        if (m.Get(69)) w.WriteFloat(d.ModRangedHaste!.Value);
        if (m.Get(70)) w.WriteFloat(d.ModHasteRegen!.Value);
        if (m.Get(71)) w.WriteFloat(d.ModTimeRate!.Value);
        if (m.Get(72)) w.WriteInt32(d.CreatedBySpell!.Value);
        if (m.Get(73)) w.WriteInt32(d.EmoteState!.Value);
        if (m.Get(74)) w.WriteInt16((short)d.TrainingPointsUsed!.Value);
        if (m.Get(75)) w.WriteInt16((short)d.TrainingPointsTotal!.Value);
        if (m.Get(76)) w.WriteInt32(d.BaseMana!.Value);
        if (m.Get(77)) w.WriteInt32(d.BaseHealth!.Value);
        if (m.Get(78)) w.WriteUInt8(d.SheatheState!.Value);
        if (m.Get(79)) w.WriteUInt8(d.PvpFlags!.Value);
        if (m.Get(80)) w.WriteUInt8(d.PetFlags!.Value);
        if (m.Get(81)) w.WriteUInt8(d.ShapeshiftForm!.Value);
        if (m.Get(82)) w.WriteInt32(d.AttackPower!.Value);
        if (m.Get(83)) w.WriteInt32(d.AttackPowerModPos!.Value);
        if (m.Get(84)) w.WriteInt32(d.AttackPowerModNeg!.Value);
        if (m.Get(85)) w.WriteFloat(d.AttackPowerMultiplier!.Value);
        if (m.Get(86)) w.WriteInt32(d.RangedAttackPower!.Value);
        if (m.Get(87)) w.WriteInt32(d.RangedAttackPowerModPos!.Value);
        if (m.Get(88)) w.WriteInt32(d.RangedAttackPowerModNeg!.Value);
        if (m.Get(89)) w.WriteFloat(d.RangedAttackPowerMultiplier!.Value);
        if (m.Get(90)) w.WriteInt32(d.AttackSpeedAura!.Value);
        if (m.Get(91)) w.WriteFloat(d.Lifesteal!.Value);
        if (m.Get(92)) w.WriteFloat(d.MinRangedDamage!.Value);
        if (m.Get(93)) w.WriteFloat(d.MaxRangedDamage!.Value);
        if (m.Get(94)) w.WriteFloat(d.MaxHealthModifier!.Value);
        if (m.Get(95)) w.WriteFloat(d.HoverHeight!.Value);

        if (m.Get(97)) w.WriteInt32(d.MinItemLevelCutoff!.Value);
        if (m.Get(98)) w.WriteInt32(d.MinItemLevel!.Value);
        if (m.Get(99)) w.WriteInt32(d.MaxItemLevel!.Value);
        if (m.Get(100)) w.WriteInt32(d.WildBattlePetLevel!.Value);
        if (m.Get(101)) w.WriteUInt32(d.BattlePetCompanionNameTimestamp!.Value);
        if (m.Get(102)) w.WriteInt32(d.InteractSpellID!.Value);
        if (m.Get(103)) w.WriteInt32(d.ScaleDuration!.Value);
        if (m.Get(104)) w.WriteInt32(d.LooksLikeMountID!.Value);
        if (m.Get(105)) w.WriteInt32(d.LooksLikeCreatureID!.Value);
        if (m.Get(106)) w.WriteInt32(d.LookAtControllerID!.Value);
        if (m.Get(108)) w.WritePackedGuid128(d.GuildGUID!.Value);

        // The decoder does ResetBitReader here and then, ONLY when gate 96 is set, reads one
        // unconditional bit (HasAssistActionData) ahead of bit 118's optional substructure.
        if (m.Get(96))
        {
            w.FlushBits();
            w.WriteBit(false);     // HasAssistActionData - never sent
            w.FlushBits();
        }

        if (m.Get(119))
        {
            for (int i = 0; i < 10; ++i)
            {
                if (m.Get(140 + i)) w.WriteInt32(d.Power[i]!.Value);
                if (m.Get(150 + i)) w.WriteInt32(d.MaxPower[i]!.Value);
                if (m.Get(160 + i)) w.WriteFloat(d.ModPowerRegen[i]!.Value);
            }
        }
        if (m.Get(170))
        {
            for (int i = 0; i < 3; ++i)
                if (m.Get(171 + i))
                    WriteUpdateVisibleItem(w, d.VirtualItems[i]!.Value);
        }
        if (m.Get(174))
        {
            for (int i = 0; i < 3; ++i)
                if (m.Get(175 + i))
                    w.WriteUInt32(d.AttackRoundBaseTime[i]!.Value);
        }
        if (m.Get(178))
        {
            for (int i = 0; i < 5; ++i)
            {
                if (m.Get(179 + i)) w.WriteInt32(d.Stats[i]!.Value);
                if (m.Get(184 + i)) w.WriteInt32(d.StatPosBuff[i]!.Value);
                if (m.Get(189 + i)) w.WriteInt32(d.StatNegBuff[i]!.Value);
            }
        }
        if (m.Get(194))
        {
            for (int i = 0; i < 7; ++i)
            {
                if (m.Get(195 + i)) w.WriteInt32(d.Resistances[i]!.Value);
                if (m.Get(202 + i)) w.WriteInt32(d.ResistanceBuffModsPositive[i]!.Value);
                if (m.Get(209 + i)) w.WriteInt32(d.ResistanceBuffModsNegative[i]!.Value);
                if (m.Get(216 + i)) w.WriteInt32(d.PowerCostModifier[i]!.Value);
                if (m.Get(223 + i)) w.WriteFloat(d.PowerCostMultiplier[i]!.Value);
            }
        }
        return w;
    }

    /// <summary>
    /// TrinityCore's <c>UnitData::FilterDisallowedFieldsMaskForFlag</c>, with the mask derived
    /// locally instead of copied.
    /// <para>
    /// TC's hex masks are 11.x bit numbers and cannot be transplanted onto 553's numbering, so the
    /// gated field list is taken from 553's own create reader - the only place WPP models
    /// visibility - and mapped onto the update bits. Extracted from
    /// <c>ReadCreateUnitData</c>'s <c>if ((flags &amp; UpdateFieldFlag.X) != None)</c> blocks:
    /// Critter, RangedAttackRoundBaseTime, Stats/StatPosBuff/StatNegBuff, PowerCostModifier,
    /// PowerCostMultiplier, BaseHealth, the AttackPower and RangedAttackPower groups,
    /// SetAttackSpeedAura, Lifesteal and Min/MaxRangedDamage are Owner; Min/MaxDamage,
    /// Min/MaxOffHandDamage and Resistances are Owner or Empath; the two PowerRegen arrays are
    /// Owner or UnitAll (and are never sent anyway).
    /// </para>
    /// <para>
    /// This matters because <c>GetFieldVisibility()</c> returns None for every world unit, so a
    /// creature's create block does not carry these fields either. Sending them only in updates
    /// would be a create/update disagreement, and the header's IsOwned byte tells the client which
    /// of the two it is looking at.
    /// </para>
    /// </summary>
    void FilterUnitDataByVisibility(UfMask m)
    {
        var vis = GetFieldVisibility();
        bool owner = vis.HasFlag(FieldVisibility.Owner);
        bool empath = vis.HasFlag(FieldVisibility.Empath);
        bool unitAll = vis.HasFlag(FieldVisibility.UnitAll);

        if (!owner)
        {
            m.Clear(15);                    // Critter
            m.Clear(46);                    // RangedAttackRoundBaseTime
            m.Clear(77);                    // BaseHealth
            m.ClearRange(82, 91);           // AttackPower..Lifesteal
            m.ClearRange(92, 93);           // Min/MaxRangedDamage
            m.Clear(115);                   // ComboTarget (never set here, listed for completeness)
            m.ClearRange(179, 193);         // Stats, StatPosBuff, StatNegBuff
            m.ClearRange(216, 229);         // PowerCostModifier, PowerCostMultiplier
        }
        if (!owner && !empath)
        {
            m.ClearRange(53, 56);           // Min/Max damage, main and off hand
            m.ClearRange(195, 201);         // Resistances
        }
        if (!owner && !unitAll)
            m.ClearRange(120, 139);         // the two PowerRegen arrays
    }

    /// <summary>
    /// ReadUpdateVisibleItem: a flat 6-bit mask, then the set fields. TC's equivalent
    /// (<c>VisibleItem::WriteUpdate</c>) sets the whole nested mask when the caller passes
    /// <c>ignoreNestedChangesMask</c>; we send the three fields our model sources and leave the
    /// two retail-only appearance ids out, which the reader handles because it is mask driven.
    /// </summary>
    static void WriteUpdateVisibleItem(WorldPacket w, VisibleItem vi)
    {
        var m = new UfMask(1);
        m.Set(0);
        m.Set(1);   // ItemID
        m.Set(4);   // ItemAppearanceModID
        m.Set(5);   // ItemVisual
        m.WriteFlat(w, 6);
        w.FlushBits();
        w.WriteInt32(vi.ItemID);
        w.WriteUInt16(vi.ItemAppearanceModID);
        w.WriteUInt16(vi.ItemVisual);
    }

    // ---------------------------------------------------------------------------------------
    // PlayerData - ReadUpdatePlayerData, 5 blocks / 152 bits
    //
    // Gate 0 -> 1..31, gate 32 -> 33..42, array gates 43 (PartyType), 46 (QuestLog),
    // 72 (VisibleItems), 92 (AvgItemLevel), 99 (ForcedReactions), 132 (Field_3120).
    //
    // The bit right after the mask blocks is noQuestLogChangesMask and is UNCONDITIONAL - TC
    // writes it the same way (PlayerData::WriteUpdate line 2713). We always write 1, which selects
    // the create-form 66-byte quest-log entry, exactly as TC does for IsQuestLogChangesMaskSkipped.
    //
    // Gate 32 is deliberately never set: the only field of that group our model carries is
    // HonorLevel, which no legacy handler fills, and the group additionally requires an
    // unconditional HasDeclinedNames bit that would then have to be tracked for nothing.
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildPlayerDataUpdate()
    {
        var d = m_updateData.PlayerData;
        if (d == null)
            return null;

        var m = new UfMask(5);

        if (d.DuelArbiter != null) m.Set(6);
        if (d.WowAccount != null) m.Set(7);
        if (d.LootTargetGUID != null) m.Set(10);
        if (d.PlayerFlags.HasValue) m.Set(11);
        if (d.PlayerFlagsEx.HasValue) m.Set(12);
        if (d.GuildRankID.HasValue) m.Set(13);
        if (d.GuildDeleteDate.HasValue) m.Set(14);
        if (d.GuildLevel.HasValue) m.Set(15);
        if (d.NumBankSlots.HasValue) m.Set(16);
        if (d.NativeSex.HasValue) m.Set(17);
        if (d.Inebriation.HasValue) m.Set(18);
        if (d.PvpTitle.HasValue) m.Set(19);
        if (d.ArenaFaction.HasValue) m.Set(20);
        if (d.PvPRank.HasValue) m.Set(21);
        if (d.DuelTeam.HasValue) m.Set(23);
        if (d.GuildTimeStamp.HasValue) m.Set(24);
        if (d.ChosenTitle.HasValue) m.Set(25);       // PlayerTitle
        if (d.FakeInebriation.HasValue) m.Set(26);
        if (d.VirtualPlayerRealm.HasValue) m.Set(27);
        if (d.CurrentSpecID.HasValue) m.Set(28);
        if (d.TaxiMountAnimKitID.HasValue) m.Set(30);
        if (d.CurrentBattlePetBreedQuality.HasValue) m.Set(31);

        // HERMES_256_PDSHIFT1: ground truth from the live captures (gt_pdscan.py over world11/12):
        // on 69110 the PlayerData values-mask ARRAY region sits one bit HIGHER than the 553
        // numbering — Rowine's own quest-progress updates set {47, 49} = QuestLog gate 47 +
        // entry[1] 49 (553 says gate 46), and AvgItemLevel updates set {93, 94} = gate 93 +
        // entry[0] 94 carrying ONE float. Scalars are unshifted (PlayerFlags at 11 with group
        // gate 0 observed). So 69110 added one field between the scalar region and the arrays.
        int s = s_pdShift1 ? 1 : 0;
        if (ModernValuesArrays)
        {
            // Our model carries one PartyType byte where the wire has two; send index 0 only.
            if (d.PartyType.HasValue) m.Set(44 + s);
            for (int i = 0; i < 19; ++i)
                if (d.VisibleItems[i].HasValue) m.Set(73 + s + i);
            for (int i = 0; i < 6; ++i)
                if (d.AvgItemLevel[i].HasValue) m.Set(93 + s + i);
        }
        if (ModernValuesQuestLog)
        {
            // NOT filtered by PartyMember, unlike the create path. The update decoder takes no
            // visibility flags at all - it reads purely by mask bit - so this is the one route to
            // a populated quest log that does not need the 1654-byte PartyMember block in the
            // create, which is what froze the client on 22 Aug (section 127/132).
            for (int i = 0; i < 25; ++i)
                if (d.QuestLog[i] != null) m.Set(47 + s + i);
        }

        if (!m.Any)
            return null;

        if (m.AnyInRange(1, 31)) m.Set(0);
        if (m.AnyInRange(44 + s, 45 + s)) m.Set(43 + s);
        if (m.AnyInRange(47 + s, 71 + s)) m.Set(46 + s);
        if (m.AnyInRange(73 + s, 91 + s)) m.Set(72 + s);
        if (m.AnyInRange(93 + s, 98 + s)) m.Set(92 + s);

        var w = new WorldPacket();
        m.WriteHierarchical(w);
        w.WriteBit(true);          // noQuestLogChangesMask: entries are in create form
        // Bit 1 (HasLevelLink) and the dynamic-field masks at bits 2-5 are never set.
        w.FlushBits();

        if (m.Get(6)) w.WritePackedGuid128(d.DuelArbiter!.Value);
        if (m.Get(7)) w.WritePackedGuid128(d.WowAccount!.Value);
        if (m.Get(10)) w.WritePackedGuid128(d.LootTargetGUID!.Value);
        if (m.Get(11)) w.WriteUInt32(d.PlayerFlags!.Value);
        if (m.Get(12)) w.WriteUInt32(d.PlayerFlagsEx!.Value);
        if (m.Get(13)) w.WriteUInt32(d.GuildRankID!.Value);
        if (m.Get(14)) w.WriteUInt32(d.GuildDeleteDate!.Value);
        if (m.Get(15)) w.WriteInt32(d.GuildLevel!.Value);
        if (m.Get(16)) w.WriteUInt8(d.NumBankSlots!.Value);
        if (m.Get(17)) w.WriteUInt8(d.NativeSex!.Value);
        if (m.Get(18)) w.WriteUInt8(d.Inebriation!.Value);
        if (m.Get(19)) w.WriteUInt8(d.PvpTitle!.Value);
        if (m.Get(20)) w.WriteUInt8(d.ArenaFaction!.Value);
        if (m.Get(21)) w.WriteUInt8(d.PvPRank!.Value);
        if (m.Get(23)) w.WriteUInt32(d.DuelTeam!.Value);
        if (m.Get(24)) w.WriteInt32(d.GuildTimeStamp!.Value);
        if (m.Get(25)) w.WriteInt32(d.ChosenTitle!.Value);
        if (m.Get(26)) w.WriteInt32(d.FakeInebriation!.Value);
        if (m.Get(27)) w.WriteUInt32(d.VirtualPlayerRealm!.Value);
        if (m.Get(28)) w.WriteUInt32(d.CurrentSpecID!.Value);
        if (m.Get(30)) w.WriteInt32(d.TaxiMountAnimKitID!.Value);
        if (m.Get(31)) w.WriteUInt8((byte)d.CurrentBattlePetBreedQuality!.Value);

        if (m.Get(43 + s))
        {
            for (int i = 0; i < 2; ++i)
                if (m.Get(44 + s + i))
                    w.WriteUInt8(d.PartyType!.Value);
        }
        if (m.Get(46 + s))
        {
            for (int i = 0; i < 25; ++i)
            {
                if (!m.Get(47 + s + i))
                    continue;
                // ReadCreateQuestLog - the arm noQuestLogChangesMask == 1 selects, and the same
                // 66-byte shape WritePlayerData emits on the create path.
                var q = d.QuestLog[i];
                w.WriteInt32(q.QuestID ?? 0);
                w.WriteUInt16((ushort)(q.StateFlags ?? 0));
                for (int j = 0; j < 24; ++j)
                    w.WriteUInt16((ushort)(q.ObjectiveProgress[j] ?? 0));
                w.WriteInt64(q.EndTime ?? 0);
                w.WriteUInt32(0);      // ObjectiveFlags - the trailing u32 at entry+0x10
            }
        }
        if (m.Get(72 + s))
        {
            for (int i = 0; i < 19; ++i)
                if (m.Get(73 + s + i))
                    WriteUpdateVisibleItem(w, d.VisibleItems[i]!.Value);
        }
        if (m.Get(92 + s))
        {
            for (int i = 0; i < 6; ++i)
                if (m.Get(93 + s + i))
                    w.WriteFloat(d.AvgItemLevel[i]!.Value);
        }
        return w;
    }

    // ---------------------------------------------------------------------------------------
    // ActivePlayerData - ReadUpdateActivePlayerData, 14 blocks / 441 bits
    //
    // Gate 0 -> 1..37, gate 38 -> 39..69, gate 70 -> 71..101, gate 102 -> 103..133 plus a second
    // section after bit 135, gate 134 -> 135, array gate 136 -> InvSlots[146] at 137..282.
    //
    // Nothing above 282 is sent. TrackResourceMask[2], RestInfo[2] and CombatRatings[32] are the
    // three arrays 69110's create reader does NOT read (model-256.md); if that is a declaration
    // difference rather than a create-serialiser difference then every bit from 283 upwards has
    // shifted, and everything below it is unaffected either way.
    // ---------------------------------------------------------------------------------------

    // HERMES_256_APDINV116: ground truth from the live Blizzard split captures (world12, decoded
    // with tools-256-spike/ground-truth/gt_apddec.py): on THIS build the APD values-mask InvSlots
    // section sits at gate bit 116 with slots at 117+i — exactly 20 bits LOWER than the 5.5.0/553
    // numbering (136/137+i) this file was generated from, and with NO chunk-gate bit alongside
    // (Blizzard's InvSlots updates set exactly {116, 117+slot}). 69110 dropped ~20 APD head fields,
    // shifting the whole mask numbering. With the old numbering the client read our InvSlots
    // updates as entirely different fields (its bit 136 = InvSlots[19]) — which is why a split/move
    // never rendered its destination slot. Default off.
    static readonly bool s_apdInv116 =
        System.Environment.GetEnvironmentVariable("HERMES_256_APDINV116") == "1";

    WorldPacket? BuildActivePlayerDataUpdate()
    {
        var d = m_updateData.ActivePlayerData;
        if (d == null)
            return null;

        // HERMES_256_APDINV116: emit the ActivePlayerData values body with 69110's OWN mask
        // numbering, read straight from the client's update reader FUN@0x71B460 (Ghidra, decompile
        // in tools-256-spike/ground-truth/apd_update_decomp.c; extractor parse_apd_reader.py). The
        // 553 numbering this file was generated from is wrong on this build — the client dropped/
        // renumbered fields, and each region shifted by a DIFFERENT amount, so nothing can be
        // derived; the binary is the only authority. Confirmed bit→field, object offsets matching
        // WriteActivePlayerData exactly:
        //   Coinage      bit 42  (obj+0x58, u64)   — 553 said 33
        //   XP           bit 44  (obj+0x68, u32)   — 553 said 35
        //   NextLevelXP  bit 45  (obj+0x6C, u32)   — 553 said 36
        //   InvSlots     gate 116, entries 117+i   — 553 said 136/137+i (live-proven)
        // Scalars 33-63 are gated under group bit 32 (reader `if ((dword1 & 1)!=0)` at 0x71B4F0-ish);
        // the InvSlots array is UNGATED (proven: the InvSlots-only body works without any group gate).
        if (s_apdInv116)
        {
            var m69 = new UfMask(14);
            var log = new System.Text.StringBuilder();

            bool wantCoinage = d.Coinage.HasValue;
            bool wantXP = d.XP.HasValue;
            bool wantNextXP = d.NextLevelXP.HasValue;
            if (wantCoinage || wantXP || wantNextXP)
            {
                m69.Set(32);                       // group gate for dword-1 scalars (33-63)
                if (wantCoinage) { m69.Set(42); log.Append($" Coinage={d.Coinage}"); }
                if (wantXP)      { m69.Set(44); log.Append($" XP={d.XP}"); }
                if (wantNextXP)  { m69.Set(45); log.Append($" NextXP={d.NextLevelXP}"); }
            }

            var slots = new System.Collections.Generic.List<int>();
            if (s_invSlots && (ModernValuesArrays || m_updateData.ForceApdValuesTest))
                for (int i = 0; i < 146; ++i)
                    if (GetModern69110InvSlot(d, i) != null) slots.Add(i);
            if (slots.Count > 0)
            {
                m69.Set(116);                      // InvSlots gate (ungated array — no group gate)
                foreach (int i in slots) m69.Set(117 + i);
            }

            if (!m69.Any)
                return null;

            var w69 = new WorldPacket();
            m69.WriteHierarchical(w69);
            w69.FlushBits();
            // Payloads in ascending bit order: scalars (dword-1) then InvSlots guids (dword-3+).
            if (m69.Get(42)) w69.WriteUInt64(d.Coinage!.Value);
            if (m69.Get(44)) w69.WriteInt32(d.XP!.Value);
            if (m69.Get(45)) w69.WriteInt32(d.NextLevelXP!.Value);
            if (m69.Get(116))
                foreach (int i in slots)
                {
                    var g = GetModern69110InvSlot(d, i)!.Value;
                    w69.WritePackedGuid128(g);
                    log.Append($" [{i}]={g}");
                }
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] APD-VALUES(69110):{log}");
            return w69;
        }

        var m = new UfMask(14);

        if (d.FarsightObject != null) m.Set(31);
        if (d.SummonedBattlePetGUID != null) m.Set(32);
        if (d.Coinage.HasValue) m.Set(33);
        if (d.XP.HasValue) m.Set(35);
        if (d.NextLevelXP.HasValue) m.Set(36);
        if (d.TrialXP.HasValue) m.Set(37);

        if (d.CharacterPoints.HasValue) m.Set(40);
        if (d.MaxTalentTiers.HasValue) m.Set(41);
        if (d.TrackCreatureMask.HasValue) m.Set(42);
        if (d.MainhandExpertise.HasValue) m.Set(43);
        if (d.OffhandExpertise.HasValue) m.Set(44);
        if (d.RangedExpertise.HasValue) m.Set(45);
        if (d.CombatRatingExpertise.HasValue) m.Set(46);
        if (d.BlockPercentage.HasValue) m.Set(47);
        if (d.DodgePercentage.HasValue) m.Set(48);
        if (d.DodgePercentageFromAttribute.HasValue) m.Set(49);
        if (d.ParryPercentage.HasValue) m.Set(50);
        if (d.ParryPercentageFromAttribute.HasValue) m.Set(51);
        if (d.CritPercentage.HasValue) m.Set(52);
        if (d.RangedCritPercentage.HasValue) m.Set(53);
        if (d.OffhandCritPercentage.HasValue) m.Set(54);
        if (d.ShieldBlock.HasValue) m.Set(55);
        if (d.Mastery.HasValue) m.Set(57);
        if (d.Speed.HasValue) m.Set(58);
        if (d.Avoidance.HasValue) m.Set(59);
        if (d.Sturdiness.HasValue) m.Set(60);
        if (d.Versatility.HasValue) m.Set(61);
        if (d.VersatilityBonus.HasValue) m.Set(62);
        if (d.PvpPowerDamage.HasValue) m.Set(63);
        if (d.PvpPowerHealing.HasValue) m.Set(64);
        if (d.ModHealingDonePos.HasValue) m.Set(66);
        if (d.ModHealingPercent.HasValue) m.Set(67);
        if (d.ModHealingDonePercent.HasValue) m.Set(68);
        if (d.ModPeriodicHealingDonePercent.HasValue) m.Set(69);

        if (d.ModSpellPowerPercent.HasValue) m.Set(71);
        if (d.ModResiliencePercent.HasValue) m.Set(72);
        if (d.OverrideSpellPowerByAPPercent.HasValue) m.Set(73);
        if (d.OverrideAPBySpellPowerPercent.HasValue) m.Set(74);
        if (d.ModTargetResistance.HasValue) m.Set(75);
        if (d.ModTargetPhysicalResistance.HasValue) m.Set(76);
        if (d.LocalFlags.HasValue) m.Set(77);
        if (d.GrantableLevels.HasValue) m.Set(78);
        if (d.MultiActionBars.HasValue) m.Set(79);
        if (d.LifetimeMaxRank.HasValue) m.Set(80);
        if (d.NumRespecs.HasValue) m.Set(81);
        if (d.AmmoID.HasValue) m.Set(82);
        if (d.PvpMedals.HasValue) m.Set(83);
        if (d.TodayHonorableKills.HasValue) m.Set(84);
        if (d.TodayDishonorableKills.HasValue) m.Set(85);
        if (d.YesterdayHonorableKills.HasValue) m.Set(86);
        if (d.YesterdayDishonorableKills.HasValue) m.Set(87);
        if (d.LastWeekHonorableKills.HasValue) m.Set(88);
        if (d.LastWeekDishonorableKills.HasValue) m.Set(89);
        if (d.ThisWeekHonorableKills.HasValue) m.Set(90);
        if (d.ThisWeekDishonorableKills.HasValue) m.Set(91);
        if (d.ThisWeekContribution.HasValue) m.Set(92);
        if (d.LifetimeHonorableKills.HasValue) m.Set(93);
        if (d.LifetimeDishonorableKills.HasValue) m.Set(94);
        if (d.YesterdayContribution.HasValue) m.Set(96);
        if (d.LastWeekContribution.HasValue) m.Set(97);
        if (d.LastWeekRank.HasValue) m.Set(98);
        if (d.WatchedFactionIndex.HasValue) m.Set(99);
        if (d.MaxLevel.HasValue) m.Set(100);
        if (d.ScalingPlayerLevelDelta.HasValue) m.Set(101);

        if (d.MaxCreatureScalingLevel.HasValue) m.Set(103);
        if (d.PetSpellPower.HasValue) m.Set(104);
        if (d.UiHitModifier.HasValue) m.Set(105);
        if (d.UiSpellHitModifier.HasValue) m.Set(106);
        if (d.HomeRealmTimeOffset.HasValue) m.Set(107);
        if (d.ModPetHaste.HasValue) m.Set(108);
        if (d.LocalRegenFlags.HasValue) m.Set(109);
        if (d.AuraVision.HasValue) m.Set(110);
        if (d.NumBackpackSlots.HasValue) m.Set(111);
        if (d.OverrideSpellsID.HasValue) m.Set(112);
        if (d.LfgBonusFactionID.HasValue) m.Set(113);
        if (d.LootSpecID.HasValue) m.Set(114);
        if (d.OverrideZonePVPType.HasValue) m.Set(115);
        if (d.Honor.HasValue) m.Set(116);
        if (d.HonorNextLevel.HasValue) m.Set(117);
        if (d.PvPTierMaxFromWins.HasValue) m.Set(120);
        if (d.PvPLastWeeksTierMaxFromWins.HasValue) m.Set(121);
        if (d.PvPRankProgress.HasValue) m.Set(122);

        if ((ModernValuesArrays || m_updateData.ForceApdValuesTest) && s_invSlots)
        {
            // Map EVERY modern InvSlots index (0-145) through the same legacy->modern slot
            // mapping the create writer uses (GetModern69110InvSlot), not just the first 23.
            // The old code only checked d.InvSlots[0..22] (equipment + equipped bags), so a
            // backpack change (which lives in d.PackSlots, modern slots 35-58), a bank change
            // (59-86) etc. was NEVER emitted incrementally - the item object was created but the
            // slot it landed in stayed empty. Live-proven by a backpack split: PackSlots[5] must
            // reach modern InvSlots[40].
            for (int i = 0; i < 146; ++i)
                if (GetModern69110InvSlot(d, i) != null) m.Set(137 + i);
        }

        if (!m.Any)
            return null;

        if (m.AnyInRange(1, 37)) m.Set(0);
        if (m.AnyInRange(39, 69)) m.Set(38);
        if (m.AnyInRange(71, 101)) m.Set(70);
        if (m.AnyInRange(103, 133)) m.Set(102);
        if (m.AnyInRange(137, 282)) m.Set(136);

        var w = new WorldPacket();
        m.WriteHierarchical(w);
        // Bits 1 and 2 (SortBagsRightToLeft / InsertItemsLeftToRight) and every dynamic-field mask
        // bit stay unset, so no bits follow the block mask here.
        w.FlushBits();

        if (m.Get(31)) w.WritePackedGuid128(d.FarsightObject!.Value);
        if (m.Get(32)) w.WritePackedGuid128(d.SummonedBattlePetGUID!.Value);
        if (m.Get(33)) w.WriteUInt64(d.Coinage!.Value);
        if (m.Get(35)) w.WriteInt32(d.XP!.Value);
        if (m.Get(36)) w.WriteInt32(d.NextLevelXP!.Value);
        if (m.Get(37)) w.WriteInt32(d.TrialXP!.Value);

        if (m.Get(40)) w.WriteInt32(d.CharacterPoints!.Value);
        if (m.Get(41)) w.WriteInt32(d.MaxTalentTiers!.Value);
        if (m.Get(42)) w.WriteUInt32(d.TrackCreatureMask!.Value);
        if (m.Get(43)) w.WriteFloat(d.MainhandExpertise!.Value);
        if (m.Get(44)) w.WriteFloat(d.OffhandExpertise!.Value);
        if (m.Get(45)) w.WriteFloat(d.RangedExpertise!.Value);
        if (m.Get(46)) w.WriteFloat(d.CombatRatingExpertise!.Value);
        if (m.Get(47)) w.WriteFloat(d.BlockPercentage!.Value);
        if (m.Get(48)) w.WriteFloat(d.DodgePercentage!.Value);
        if (m.Get(49)) w.WriteFloat(d.DodgePercentageFromAttribute!.Value);
        if (m.Get(50)) w.WriteFloat(d.ParryPercentage!.Value);
        if (m.Get(51)) w.WriteFloat(d.ParryPercentageFromAttribute!.Value);
        if (m.Get(52)) w.WriteFloat(d.CritPercentage!.Value);
        if (m.Get(53)) w.WriteFloat(d.RangedCritPercentage!.Value);
        if (m.Get(54)) w.WriteFloat(d.OffhandCritPercentage!.Value);
        if (m.Get(55)) w.WriteInt32(d.ShieldBlock!.Value);
        if (m.Get(57)) w.WriteFloat(d.Mastery!.Value);
        if (m.Get(58)) w.WriteFloat(d.Speed!.Value);
        if (m.Get(59)) w.WriteFloat(d.Avoidance!.Value);
        if (m.Get(60)) w.WriteFloat(d.Sturdiness!.Value);
        if (m.Get(61)) w.WriteInt32(d.Versatility!.Value);
        if (m.Get(62)) w.WriteFloat(d.VersatilityBonus!.Value);
        if (m.Get(63)) w.WriteFloat(d.PvpPowerDamage!.Value);
        if (m.Get(64)) w.WriteFloat(d.PvpPowerHealing!.Value);
        if (m.Get(66)) w.WriteInt32(d.ModHealingDonePos!.Value);
        if (m.Get(67)) w.WriteFloat(d.ModHealingPercent!.Value);
        if (m.Get(68)) w.WriteFloat(d.ModHealingDonePercent!.Value);
        if (m.Get(69)) w.WriteFloat(d.ModPeriodicHealingDonePercent!.Value);

        if (m.Get(71)) w.WriteFloat(d.ModSpellPowerPercent!.Value);
        if (m.Get(72)) w.WriteFloat(d.ModResiliencePercent!.Value);
        if (m.Get(73)) w.WriteFloat(d.OverrideSpellPowerByAPPercent!.Value);
        if (m.Get(74)) w.WriteFloat(d.OverrideAPBySpellPowerPercent!.Value);
        if (m.Get(75)) w.WriteInt32(d.ModTargetResistance!.Value);
        if (m.Get(76)) w.WriteInt32(d.ModTargetPhysicalResistance!.Value);
        if (m.Get(77)) w.WriteUInt32(d.LocalFlags!.Value);
        if (m.Get(78)) w.WriteUInt8(d.GrantableLevels!.Value);
        if (m.Get(79)) w.WriteUInt8(d.MultiActionBars!.Value);
        if (m.Get(80)) w.WriteUInt8(d.LifetimeMaxRank!.Value);
        if (m.Get(81)) w.WriteUInt8(d.NumRespecs!.Value);
        if (m.Get(82)) w.WriteInt32((int)d.AmmoID!.Value);
        if (m.Get(83)) w.WriteUInt32(d.PvpMedals!.Value);
        if (m.Get(84)) w.WriteUInt16(d.TodayHonorableKills!.Value);
        if (m.Get(85)) w.WriteUInt16(d.TodayDishonorableKills!.Value);
        if (m.Get(86)) w.WriteUInt16(d.YesterdayHonorableKills!.Value);
        if (m.Get(87)) w.WriteUInt16(d.YesterdayDishonorableKills!.Value);
        if (m.Get(88)) w.WriteUInt16(d.LastWeekHonorableKills!.Value);
        if (m.Get(89)) w.WriteUInt16(d.LastWeekDishonorableKills!.Value);
        if (m.Get(90)) w.WriteUInt16(d.ThisWeekHonorableKills!.Value);
        if (m.Get(91)) w.WriteUInt16(d.ThisWeekDishonorableKills!.Value);
        if (m.Get(92)) w.WriteUInt32(d.ThisWeekContribution!.Value);
        if (m.Get(93)) w.WriteUInt32(d.LifetimeHonorableKills!.Value);
        if (m.Get(94)) w.WriteUInt32(d.LifetimeDishonorableKills!.Value);
        if (m.Get(96)) w.WriteUInt32(d.YesterdayContribution!.Value);
        if (m.Get(97)) w.WriteUInt32(d.LastWeekContribution!.Value);
        if (m.Get(98)) w.WriteUInt32(d.LastWeekRank!.Value);
        if (m.Get(99)) w.WriteInt32(d.WatchedFactionIndex!.Value);
        if (m.Get(100)) w.WriteInt32(d.MaxLevel!.Value);
        if (m.Get(101)) w.WriteInt32(d.ScalingPlayerLevelDelta!.Value);

        if (m.Get(103)) w.WriteInt32(d.MaxCreatureScalingLevel!.Value);
        if (m.Get(104)) w.WriteInt32(d.PetSpellPower!.Value);
        if (m.Get(105)) w.WriteFloat(d.UiHitModifier!.Value);
        if (m.Get(106)) w.WriteFloat(d.UiSpellHitModifier!.Value);
        if (m.Get(107)) w.WriteInt32(d.HomeRealmTimeOffset!.Value);
        if (m.Get(108)) w.WriteFloat(d.ModPetHaste!.Value);
        if (m.Get(109)) w.WriteUInt8(d.LocalRegenFlags!.Value);
        if (m.Get(110)) w.WriteUInt8(d.AuraVision!.Value);
        if (m.Get(111)) w.WriteUInt8(d.NumBackpackSlots!.Value);
        if (m.Get(112)) w.WriteInt32(d.OverrideSpellsID!.Value);
        if (m.Get(113)) w.WriteInt32(d.LfgBonusFactionID!.Value);
        if (m.Get(114)) w.WriteUInt16((ushort)d.LootSpecID!.Value);
        if (m.Get(115)) w.WriteUInt32(d.OverrideZonePVPType!.Value);
        if (m.Get(116)) w.WriteInt32(d.Honor!.Value);
        if (m.Get(117)) w.WriteInt32(d.HonorNextLevel!.Value);
        if (m.Get(120)) w.WriteInt32((int)d.PvPTierMaxFromWins!.Value);
        if (m.Get(121)) w.WriteInt32((int)d.PvPLastWeeksTierMaxFromWins!.Value);
        if (m.Get(122)) w.WriteUInt8(d.PvPRankProgress!.Value);

        // Gate 134 -> bit 135 (Field_17B8) would go here; never set.
        // Then a SECOND section under gate 102, opening with an unconditional HasPetStable bit.
        if (m.Get(102))
        {
            w.FlushBits();
            w.WriteBit(false);     // HasPetStable - never sent
            w.FlushBits();
        }

        if (m.Get(136))
        {
            // The write loop must resolve through the SAME mapping the mask loop used —
            // d.InvSlots[i] is null for the remapped indices (backpack/bank live in PackSlots etc.).
            var changed = new System.Text.StringBuilder();
            for (int i = 0; i < 146; ++i)
                if (m.Get(137 + i))
                {
                    var g = GetModern69110InvSlot(d, i)!.Value;
                    w.WritePackedGuid128(g);
                    changed.Append($" [{i}]={g}");
                }
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] APD-VALUES InvSlots changed:{changed}");
        }
        return w;
    }

    // ---------------------------------------------------------------------------------------
    // GameObjectData - ReadUpdateGameObjectData, flat 21-bit mask
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildGameObjectDataUpdate()
    {
        var d = m_updateData.GameObjectData;
        if (d == null)
            return null;

        var m = new UfMask(1);
        if (d.DisplayID.HasValue) m.Set(4);
        if (d.SpellVisualID.HasValue) m.Set(5);
        if (d.StateSpellVisualID.HasValue) m.Set(6);
        if (d.StateAnimID.HasValue) m.Set(7);        // SpawnTrackingStateAnimID
        if (d.StateAnimKitID.HasValue) m.Set(8);     // SpawnTrackingStateAnimKitID
        if (d.CreatedBy != null) m.Set(9);
        if (d.GuildGUID != null) m.Set(10);
        if (d.Flags.HasValue) m.Set(11);
        if (d.ParentRotation[0].HasValue) m.Set(12);
        if (d.FactionTemplate.HasValue) m.Set(13);
        if (d.Level.HasValue) m.Set(14);
        if (d.State.HasValue) m.Set(15);
        if (d.TypeID.HasValue) m.Set(16);
        if (d.PercentHealth.HasValue) m.Set(17);
        if (d.ArtKit.HasValue) m.Set(18);
        if (d.CustomParam.HasValue) m.Set(19);
        if (!m.Any)
            return null;
        m.Set(0);

        var w = new WorldPacket();
        m.WriteFlat(w, 21);
        w.FlushBits();

        if (m.Get(4)) w.WriteInt32(d.DisplayID!.Value);
        if (m.Get(5)) w.WriteUInt32(d.SpellVisualID!.Value);
        if (m.Get(6)) w.WriteUInt32(d.StateSpellVisualID!.Value);
        if (m.Get(7)) w.WriteUInt32(d.StateAnimID!.Value);
        if (m.Get(8)) w.WriteUInt32(d.StateAnimKitID!.Value);
        if (m.Get(9)) w.WritePackedGuid128(d.CreatedBy!.Value);
        if (m.Get(10)) w.WritePackedGuid128(d.GuildGUID!.Value);
        if (m.Get(11)) w.WriteUInt32(d.Flags!.Value);
        if (m.Get(12))
        {
            w.WriteFloat(d.ParentRotation[0] ?? 0.0f);
            w.WriteFloat(d.ParentRotation[1] ?? 0.0f);
            w.WriteFloat(d.ParentRotation[2] ?? 0.0f);
            w.WriteFloat(d.ParentRotation[3] ?? 0.0f);
        }
        if (m.Get(13)) w.WriteInt32(d.FactionTemplate!.Value);
        if (m.Get(14)) w.WriteInt32(d.Level!.Value);
        if (m.Get(15)) w.WriteInt8(d.State!.Value);
        if (m.Get(16)) w.WriteInt8(d.TypeID!.Value);
        if (m.Get(17)) w.WriteUInt8(d.PercentHealth!.Value);
        if (m.Get(18)) w.WriteUInt32(d.ArtKit!.Value);
        if (m.Get(19)) w.WriteUInt32(d.CustomParam!.Value);

        // Unconditional inside gate 0, ahead of bit 20's optional substructure.
        w.FlushBits();
        w.WriteBit(false);         // HasAssistActionData
        w.FlushBits();
        return w;
    }

    // ---------------------------------------------------------------------------------------
    // DynamicObjectData - ReadUpdateDynamicObjectData, flat 7-bit mask
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildDynamicObjectDataUpdate()
    {
        var d = m_updateData.DynamicObjectData;
        if (d == null)
            return null;

        var m = new UfMask(1);
        if (d.Caster != null) m.Set(1);
        if (d.Type.HasValue) m.Set(2);
        if (d.SpellXSpellVisualID.HasValue) m.Set(3);
        if (d.SpellID.HasValue) m.Set(4);
        if (d.Radius.HasValue) m.Set(5);
        if (d.CastTime.HasValue) m.Set(6);
        if (!m.Any)
            return null;
        m.Set(0);

        var w = new WorldPacket();
        m.WriteFlat(w, 7);
        w.FlushBits();
        if (m.Get(1)) w.WritePackedGuid128(d.Caster!.Value);
        if (m.Get(2)) w.WriteUInt8((byte)d.Type!.Value);
        if (m.Get(3)) w.WriteInt32(d.SpellXSpellVisualID!.Value);
        if (m.Get(4)) w.WriteInt32(d.SpellID!.Value);
        if (m.Get(5)) w.WriteFloat(d.Radius!.Value);
        if (m.Get(6)) w.WriteUInt32(d.CastTime!.Value);
        return w;
    }

    // ---------------------------------------------------------------------------------------
    // CorpseData - ReadUpdateCorpseData, one block behind a 1-bit block mask
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildCorpseDataUpdate()
    {
        var d = m_updateData.CorpseData;
        if (d == null)
            return null;

        var m = new UfMask(1);
        if (d.DynamicFlags.HasValue) m.Set(2);
        if (d.Owner != null) m.Set(3);
        if (d.PartyGUID != null) m.Set(4);
        if (d.GuildGUID != null) m.Set(5);
        if (d.DisplayID.HasValue) m.Set(6);
        if (d.RaceId.HasValue) m.Set(7);
        if (d.SexId.HasValue) m.Set(8);
        if (d.ClassId.HasValue) m.Set(9);
        if (d.Flags.HasValue) m.Set(10);
        if (d.FactionTemplate.HasValue) m.Set(11);
        if (ModernValuesArrays)
        {
            for (int i = 0; i < 19; ++i)
                if (d.Items[i].HasValue) m.Set(13 + i);
        }
        if (!m.Any)
            return null;
        if (m.AnyInRange(1, 11)) m.Set(0);
        if (m.AnyInRange(13, 31)) m.Set(12);

        var w = new WorldPacket();
        m.WriteHierarchical(w);
        w.FlushBits();
        if (m.Get(2)) w.WriteUInt32(d.DynamicFlags!.Value);
        if (m.Get(3)) w.WritePackedGuid128(d.Owner!.Value);
        if (m.Get(4)) w.WritePackedGuid128(d.PartyGUID!.Value);
        if (m.Get(5)) w.WritePackedGuid128(d.GuildGUID!.Value);
        if (m.Get(6)) w.WriteUInt32(d.DisplayID!.Value);
        if (m.Get(7)) w.WriteUInt8(d.RaceId!.Value);
        if (m.Get(8)) w.WriteUInt8(d.SexId!.Value);
        if (m.Get(9)) w.WriteUInt8(d.ClassId!.Value);
        if (m.Get(10)) w.WriteUInt32(d.Flags!.Value);
        if (m.Get(11)) w.WriteInt32(d.FactionTemplate!.Value);
        if (m.Get(12))
        {
            for (int i = 0; i < 19; ++i)
                if (m.Get(13 + i))
                    w.WriteUInt32(d.Items[i]!.Value);
        }
        return w;
    }

    // ---------------------------------------------------------------------------------------
    // ItemData / ContainerData - ReadUpdateItemData (2 blocks), ReadUpdateContainerData (2)
    // ---------------------------------------------------------------------------------------

    WorldPacket? BuildItemDataUpdate()
    {
        var d = m_updateData.ItemData;
        if (d == null)
            return null;

        var m = new UfMask(2);
        if (d.Owner != null) m.Set(3);
        if (d.ContainedIn != null) m.Set(4);
        if (d.Creator != null) m.Set(5);
        if (d.GiftCreator != null) m.Set(6);
        if (d.StackCount.HasValue) m.Set(7);
        if (d.Duration.HasValue) m.Set(8);         // Expiration
        if (d.Flags.HasValue) m.Set(9);            // DynamicFlags
        if (d.PropertySeed.HasValue) m.Set(10);
        if (d.RandomProperty.HasValue) m.Set(11);  // RandomPropertiesID
        if (d.Durability.HasValue) m.Set(12);
        if (d.MaxDurability.HasValue) m.Set(13);
        if (ModernValuesArrays)
        {
            for (int i = 0; i < 5; ++i)
                if (d.SpellCharges[i].HasValue) m.Set(24 + i);
        }
        if (!m.Any)
            return null;
        if (m.AnyInRange(1, 22)) m.Set(0);
        if (m.AnyInRange(24, 28)) m.Set(23);

        var w = new WorldPacket();
        m.WriteHierarchical(w);
        w.FlushBits();
        if (m.Get(3)) w.WritePackedGuid128(d.Owner!.Value);
        if (m.Get(4)) w.WritePackedGuid128(d.ContainedIn!.Value);
        if (m.Get(5)) w.WritePackedGuid128(d.Creator!.Value);
        if (m.Get(6)) w.WritePackedGuid128(d.GiftCreator!.Value);
        if (m.Get(7)) w.WriteUInt32(d.StackCount!.Value);
        if (m.Get(8)) w.WriteUInt32(d.Duration!.Value);
        if (m.Get(9)) w.WriteUInt32(d.Flags!.Value);
        if (m.Get(10)) w.WriteInt32((int)d.PropertySeed!.Value);
        if (m.Get(11)) w.WriteInt32((int)d.RandomProperty!.Value);
        if (m.Get(12)) w.WriteUInt32(d.Durability!.Value);
        if (m.Get(13)) w.WriteUInt32(d.MaxDurability!.Value);
        if (m.Get(23))
        {
            for (int i = 0; i < 5; ++i)
                if (m.Get(24 + i))
                    w.WriteInt32(d.SpellCharges[i]!.Value);
        }
        return w;
    }

    WorldPacket? BuildContainerDataUpdate()
    {
        var d = m_updateData.ContainerData;
        if (d == null)
            return null;

        var m = new UfMask(2);
        if (d.NumSlots.HasValue) m.Set(1);
        if (ModernValuesArrays)
        {
            for (int i = 0; i < 36; ++i)
                if (d.Slots[i] != null) m.Set(3 + i);
        }
        if (!m.Any)
            return null;
        if (m.Get(1)) m.Set(0);
        if (m.AnyInRange(3, 38)) m.Set(2);

        var w = new WorldPacket();
        m.WriteHierarchical(w);
        w.FlushBits();
        if (m.Get(1)) w.WriteUInt32(d.NumSlots!.Value);
        if (m.Get(2))
        {
            for (int i = 0; i < 36; ++i)
                if (m.Get(3 + i))
                    w.WritePackedGuid128(d.Slots[i]!.Value);
        }
        return w;
    }
}
