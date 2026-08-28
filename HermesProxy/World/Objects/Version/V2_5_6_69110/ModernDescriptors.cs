// GENERATED from TrinityCore master's UpdateFields.cpp WriteCreate bodies by
// tools-256-spike/gen_uf.py, then hand-edited only where a value is filled in.
//
// The 11.x create block has no changes mask: every field of every descriptor is written
// unconditionally, in order, so the whole shape has to be present even when the values are
// meaningless. Zeros are correct for most of it; the handful that must be real are marked
// "VALUE" and set from our own object data.
//
// If the client rejects this, the conclusion is that the Anniversary build carries a Burning
// Crusade field set rather than retail's, and the layout has to come out of the client binary
// instead — see REFERENCE-256-CLIENT.md section 32.

using Framework.IO;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Objects.Version.V2_5_6_69110;

public partial class ObjectUpdateBuilder
{

    void WriteObjectData(WorldPacket w)
    {
        w.WriteUInt32((uint)(m_updateData.ObjectData?.EntryID ?? 0));        // EntryID
        w.WriteUInt32((uint)(m_updateData.ObjectData?.DynamicFlags ?? 0));        // DynamicFlags
        w.WriteFloat(m_updateData.ObjectData?.Scale ?? 1.0f);        // Scale   zero makes the object non-renderable
    }

    // This build's UnitData carries 26 bytes between MaxHealth and Level that TrinityCore
    // master's 11.x field set does not have there, so the generated body below is 26 bytes short
    // at that point and every field from Level onwards lands early. Measured, not guessed: we
    // write Level at UnitData+275 and the client reads it at +301, and the constant 0x08000000 the
    // UI showed is exactly what a read 26 bytes late yields, where FactionTemplate=3 abuts Flags=8.
    //
    // The gap is local, not global. Twenty-six bytes at the FRONT of the block crashes the client,
    // because that also moves DisplayID through MaxHealth, which already land correctly.
    //
    // Zeros are correct here only in the sense that nothing has objected to them; the fields these
    // bytes belong to are still unnamed. HERMES_256_PRELEVEL overrides the width for re-measuring.
    const int UnitDataPreLevelGap = 0;

    static readonly int s_preLevel =
        int.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_PRELEVEL"), out var pl) ? pl : UnitDataPreLevelGap;

    static readonly uint s_flagsMark =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_FLAGSMARK"), out var fm) ? fm : 0u;

    static readonly uint s_levelMark =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_LEVELMARK"), out var lm) ? lm : 0u;

    static readonly int s_unitLead =
        int.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_UNITLEAD"), out var ul) ? ul : 0;

    static readonly uint s_rulerFrom =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_RULERFROM"), out var rf) ? rf : 0u;
    static readonly uint s_rulerTo =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_RULERTO"), out var rt) ? rt : 0u;

    // The ruler value carries a tag in its high bits, so a run of them cannot be confused with
    // unrelated ascending data and stays recognisable even when the window starts at wire 0.
    static readonly uint s_rulerTag =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_RULERTAG"), out var rg) ? rg : 0u;

    static readonly bool s_noCustom =
        System.Environment.GetEnvironmentVariable("HERMES_256_NOCUSTOM") == "1";

    static readonly bool s_ownerGate =
        System.Environment.GetEnvironmentVariable("HERMES_256_OWNERGATE") != "0";

    static readonly int s_nativeSex =
        int.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_NATIVESEX"), out var ns) ? ns : -1;

    static readonly uint s_playerFlags =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_PLAYERFLAGS"), out var pf) ? pf : 0u;

    static readonly uint s_itemMark =
        uint.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_ITEMMARK"), out var im) ? im : 0u;

    // Anchors ActivePlayerData: Coinage sits at +300 and GetMoney() reads exactly that
    // field, so a sentinel arriving proves every byte of PlayerData before it as well.
    static readonly ulong s_money =
        ulong.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_MONEY"), out var mo) ? mo : 0UL;

    /// <summary>
    /// HERMES_256_APDPROBE=1 writes three sentinels into ActivePlayerData that the client exposes
    /// through three independent Lua calls:
    ///
    ///   Coinage     +300  GetMoney()           7
    ///   XP          +316  UnitXP("player")     13
    ///   NextLevelXP +320  UnitXPMax("player")  19
    ///
    /// The values are deliberately tiny. The first version used 123456/777/888 and froze the client
    /// at world-enter every time, in a read-and-allocate loop - which is what a large number does
    /// when the offset we call Coinage is a dynamic array count in the client's layout. That is
    /// itself the answer the probe was built to find, but a number that is harmless when misread as
    /// a count lets the client stay up long enough to report what it actually sees.
    ///
    /// GetMoney() returning zero has three possible causes and the money sentinel alone cannot
    /// separate them. All three arriving means the block is read correctly and Coinage is simply
    /// not carried across from the legacy core - a translation gap, not a layout bug. All three
    /// zero means the block is never read. Values landing in the wrong readout means a real
    /// misalignment, and which one holds which number gives its size.
    ///
    /// The dump taken earlier to settle this had no sentinel active, which is why it settled
    /// nothing. See REFERENCE-256-CLIENT.md section 97.
    /// </summary>
    /// <summary>
    /// InvSlots carries the guids of equipped items and bags. Writing them is only safe once the
    /// item objects themselves have been serialised: a modern client resolves each guid, and this
    /// build never receives those objects, so the slots point at nothing.
    ///
    /// Measured: with the slots filled the client entered the world and a thread froze for sixty
    /// seconds (ERROR #109) in the same second the active player create went out. Empty slots make
    /// the character look unequipped, which is wrong but does not hang the client - the same
    /// trade-off GetSlotGuidValue already documents for V3_4_3.
    ///
    /// The proper fix is to serialise the items via WriteCreateItemData; set HERMES_256_INVSLOTS=1
    /// when working on that.
    /// </summary>
    /// <summary>
    /// The fields wired in section 104 - the ones a handler populates and the generated writer used
    /// to overwrite with zero. Off by default while the ERROR #109 freeze is being bisected: the
    /// error history shows no freeze before they were wired, and both freezes began in the same
    /// second as the player's create block, which is where the Owner-gated members of this set go.
    /// HERMES_256_UNITFIELDS=1 turns them back on.
    /// </summary>
    /// <summary>
    /// The item appearance wiring from section 103 - VisibleItems on the player and VirtualItems on
    /// every unit. Off by default while the ERROR #109 freeze is being bisected. A non-zero ItemID
    /// is the one change that makes the client fetch something rather than just store it: it walks
    /// ItemModifiedAppearance to ItemAppearance to ItemDisplayInfo through the hotfix records we
    /// generate, and whether every legacy DisplayID resolves on this build was never confirmed.
    /// HERMES_256_ITEMLOOK=1 turns it back on.
    /// </summary>
    /// <summary>
    /// The NpcFlags wiring from section 103. Off by default while the ERROR #109 freeze is being
    /// bisected - it is the last change from today still reaching every unit, and the frozen stack
    /// is a read-and-allocate loop, which is what per-unit UI work looks like.
    /// HERMES_256_NPCFLAGS=1 turns it back on.
    /// </summary>
    static readonly bool s_npcFlags =
        System.Environment.GetEnvironmentVariable("HERMES_256_NPCFLAGS") == "1";

    /// <summary>
    /// Send GameObjectData.DisplayID as the raw legacy id. Off by default: a legacy id with no
    /// record on this build crashes the client at world entry. See the note at the write site.
    /// </summary>
    /// <summary>
    /// Send 1860 - this build's empty animation-state id - instead of 0 for the gameobject and unit
    /// state-anim fields, so the client never starts the default AnimKit. See the write site.
    /// </summary>
    /// <summary>
    /// Drop the three arrays this build's ActivePlayerData reader never reads - TrackResourceMask[2],
    /// RestInfo[2] and CombatRatings[32], 146 bytes between them. Measured by running reader
    /// 0x713E50 over a real captured block: we emit 6377 where it consumes 6231, and each array sits
    /// between two dword-adjacent object fields, so there is provably no room for it.
    ///
    /// They must go together - dropping any one alone leaves the block a different wrong length. The
    /// direction is shortening, which rule 2 calls the dangerous one, so this is opt-in until a
    /// session confirms it. They predate the 22 Aug wiring pass and were simply invisible while the
    /// block started 29 bytes late.
    /// </summary>
    static readonly bool s_apdDropArr =
        System.Environment.GetEnvironmentVariable("HERMES_256_APDDROPARR") == "1";

    // HERMES_256_APDARR2=1 keeps TrackResourceMask[2] (8B) and RestInfo[2] (10B) ON the wire even when
    // APDDROPARR is set - because the live 69110 client DOES read them. Live sentinel bisection (25 Aug,
    // catch_numbackpack.py APDPROBE sentinels): with them dropped, char-sheet fields land -8 (after
    // TrackResourceMask, before Versatility) then -18 (after RestInfo), and NumBackpackSlots reads 0 =>
    // GetContainerNumSlots(0)=0, bags won't open. CombatRatings[32] stays dropped (MaxLevel measured -18,
    // not -146, so the client does NOT read it - the WatchedFactionIndex->MaxLevel dword-adjacency holds).
    static readonly bool s_apdArr2 =
        System.Environment.GetEnvironmentVariable("HERMES_256_APDARR2") == "1";

    /// <summary>Set UNIT_FLAG_PLAYER_CONTROLLED on the player, so the client's attackability test
    /// takes its wider threshold. See the write site for the disassembly.</summary>
    static readonly bool s_pcFlag =
        System.Environment.GetEnvironmentVariable("HERMES_256_PCFLAG") == "1";

    /// <summary>PersonalTabard is 10 u32 on 69110 (client reader 0x738FB0, sub 0x666D70), not 553's
    /// 5. Emitting 5 leaves PlayerData 20 bytes short, so ActivePlayerData starts early and Coinage,
    /// XP, NextLevelXP and InvSlots all read 0/garbage on the wire. Measured from a live create block
    /// walked by the client's own reader: with 10 the whole player block parses and Coinage/XP land
    /// exactly (Hvarne: Coinage 66, XP 1486; Rowine anchors Coinage 737 @+... all correct). Default
    /// off (old 5) - this is a create-path length change, so gate + one live session confirming the
    /// coin/XP display before it becomes default. See tools-256-spike/LIVE-CAPTURE-METHOD.md.</summary>
    static readonly bool s_tabard10 =
        System.Environment.GetEnvironmentVariable("HERMES_256_TABARD10") == "1";

    /// <summary>Send this build's empty animation state (1860) in UnitData.StateAnimID, as
    /// GameObjectData already does. See the write site.</summary>
    static readonly bool s_unitAnim =
        System.Environment.GetEnvironmentVariable("HERMES_256_UNITANIM") == "1";

    static readonly bool s_animRaw =
        System.Environment.GetEnvironmentVariable("HERMES_256_ANIMRAW") == "1";

    static readonly bool s_noGobDisplay =
        System.Environment.GetEnvironmentVariable("HERMES_256_NOGOBDISPLAY") == "1";

    /// <summary>
    /// HERMES_256_GOBFIX=1 makes WriteGameObjectData emit what this build's create reader at
    /// 0x2C705A0 actually consumes: 80 bytes in 24 reads, against the 100 in 29 we send today.
    /// Three changes, all measured from the reader itself (clientfields-GameObjectData.json):
    ///
    ///  * `StateWorldEffectsQuestObjectiveID` does NOT exist here. The reader goes straight from
    ///    the StateWorldEffectIDs vector count (obj+0x18, wire +20) to CreatedBy (obj+0x30) and
    ///    GuildGUID (obj+0x40) as packed guids at +24/+26, with no u32 between them. WPP's
    ///    UpdateFieldsHandler553 - the 5.5.3 arm this build is - has no such field either; it
    ///    appears first in the 11.x line. Our extra u32 is what displaces everything after it.
    ///  * `Level` sits between FactionTemplate and State, not after CustomParam. The reader's
    ///    run between GuildGUID and the State/TypeID/PercentHealth byte triple is EIGHT dwords,
    ///    obj+0x50 .. obj+0x6C, where the writer has seven. Disassembled at 0x2C709AA..0x2C70B77:
    ///    obj+0x50 and obj+0x54 each carry a change-notification block (save old value, read,
    ///    compare, dispatch through the statics at 0x6032980 / 0x60329C0) and obj+0x58, 0x5C,
    ///    0x60, 0x64 are four bare uniform reads with no notification - the quaternion. obj+0x68
    ///    and obj+0x6C are read through the stack temp like every other scalar. So the run is
    ///    Flags, FlagsB, ParentRotation[4], FactionTemplate, Level, and both TrinityCore master
    ///    (UpdateFields.cpp:7448) and the memory order put Flags before FlagsB.
    ///  * The tail from wire +79 on is read by nothing. The client stops at 80 with one u8 into
    ///    obj+0xF0, the AssistActionData optional bit. `AnimGroupInstance` and the three
    ///    `UiWidgetItem*` fields are 11.x additions; 553 does not have them and neither does this
    ///    reader. 16 bytes of the 21 we over-send.
    ///
    /// What it costs today, and why this is not cosmetic. Our two empty guid masks land on the
    /// client's Flags, so gameobject Flags reads 0; our Flags lands in FlagsB; our ParentRotation
    /// lands one slot late so the client's FactionTemplate takes ParentRotation[3] = 1.0f =
    /// 0x3F800000 = 1065353216, far outside FactionTemplate's 1..3152; our FactionTemplate lands
    /// in Level; and our `Level` lands on the client's WorldEffects VECTOR COUNT, so any
    /// gameobject with a non-zero GAMEOBJECT_LEVEL makes the reader resize that vector and read
    /// that many dwords past the end of what we meant to send.
    ///
    /// The section 128/129 anchors do not move: DisplayID +0, SpellVisualID +4,
    /// StateSpellVisualID +8 and SpawnTrackingStateAnimID +12 are all before the first change at
    /// +24, so the 1860 that stopped the animation crash still lands in obj+0xC.
    ///
    /// Direction is shortening (100 -> 80), which rule 2 calls the dangerous one - but the values
    /// blob is length-prefixed, so a short block cannot desynchronise the batch; it only means
    /// the reader stops before the declared end. Opt-in until a session confirms it.
    /// </summary>
    static readonly bool s_gobFix =
        System.Environment.GetEnvironmentVariable("HERMES_256_GOBFIX") == "1";

    /// <summary>
    /// HERMES_256_DYNFIX=1 drops `ScriptVisualID` from WriteDynamicObjectData. This build has no
    /// two-field SpellCastVisual struct: the inline reader in chain 0x18E3DE0 (descriptor at
    /// obj+0x170) makes six reads for 19 bytes - packed guid obj+0x0, u8 obj+0x10, u32 obj+0x14,
    /// u32 obj+0x18, float obj+0x1C, u32 obj+0x20 - which is exactly 553's Caster, Type,
    /// SpellXSpellVisualID, SpellID, Radius, CastTime. The SpellCastVisual pair is TrinityCore
    /// master's shape (UpdateFields.h:1542), not this arm's.
    ///
    /// Every position after the extra field is a same-width dword, so the index-wise order leg
    /// sees zero disagreements and only the byte count moves - this is the same-width swap that
    /// leg is blind to, and it was caught by the count alone. On the wire today our SpellID is
    /// read into the client's float Radius slot, our Radius into CastTime, and our CastTime is
    /// never read.
    /// </summary>
    static readonly bool s_dynFix =
        System.Environment.GetEnvironmentVariable("HERMES_256_DYNFIX") == "1";

    /// <summary>
    /// HERMES_256_CORPSEFIX=1 drops the trailing `StateSpellVisualKitID` from WriteCorpseData.
    /// The create reader at 0x2C6F650 makes 30 reads for 105 bytes and stops at FactionTemplate
    /// (obj+0x7C); 553's ReadCreateCorpseData ends in the same place. Our 31st field is not read.
    ///
    /// This is the safest of the three by construction: the writer is aligned field-for-field on
    /// all 30 of the client's reads, CorpseData is the last descriptor in a Corpse create, and
    /// the values blob is length-prefixed - so the client consumes 105 whether we send 105 or
    /// 109 and nothing it parses changes. Only the declared size does.
    /// </summary>
    static readonly bool s_corpseFix =
        System.Environment.GetEnvironmentVariable("HERMES_256_CORPSEFIX") == "1";

    static readonly bool s_itemLook =
        System.Environment.GetEnvironmentVariable("HERMES_256_ITEMLOOK") == "1";

    /// <summary>
    /// HERMES_256_UNITTAIL=1 emits the end of UnitData that this build's create-reader actually
    /// reads and the V2_5_5 field set does not have: a packed guid at obj+0x308, VirtualItems[3]
    /// as the 23-byte element of reader 0x741E60, and a 7-byte name trailer. It replaces the two
    /// trailing create-bits, so the block grows by 77 bytes (creature 569 -> 646, player 869 -> 946).
    ///
    /// Measured, not inferred. Running the client's own reader 0x2C5D120 over a creature's captured
    /// create block (a 586-byte values blob carved out of PacketsLog, so 5 header + 12 ObjectData +
    /// 569 UnitData) with the visibility byte we actually send, the reader consumes 598 bytes where
    /// we emit 569 - it reads 29 bytes past our data, into the next object's update block. This
    /// knob is the direction that ENDS that over-read: it can only make the block longer, and
    /// over-sending is harmless where under-sending is not (PLAN-256.md rule 2). On its own it
    /// changes nothing the client renders, because the misaligned mid-block VirtualItems below is
    /// still there; it is the safe half of the fix and it is enabled first for that reason.
    /// </summary>
    static readonly bool s_unitTail =
        System.Environment.GetEnvironmentVariable("HERMES_256_UNITTAIL") == "1";

    /// <summary>
    /// HERMES_256_UNITDROPVI=1 stops emitting VirtualItems[3] (3 x 16 = 48 bytes) between
    /// FactionTemplate and Flags, where this build's reader does not read it. The block shrinks by
    /// 48 bytes and everything from Flags onward moves back into place.
    ///
    /// The client's instruction stream is unambiguous here: at RVA 0x2C5E8BA it reads FactionTemplate
    /// into obj+0x124, and the very next stream read (0x2C5E985 / 0x2C5E99D, the two arms of the
    /// notification fork - dedup by store offset, PLAN-256.md rule 5) goes to obj+0x128 = Flags,
    /// then 0x12C, 0x130, 0x134, 0x138 = Flags2..AuraState. There is no room for a 3-element array
    /// between 0x124 and 0x128, and the reader's own VirtualItems loop is at the end of the block
    /// (0x2C62EC0, `cmp eax,3`, object array at obj+0x3E8, stride 0x1C).
    ///
    /// Turn this on only WITH HERMES_256_UNITTAIL: alone it shortens the block while the reader
    /// still wants the tail, taking the over-read from 29 bytes to 77.
    /// </summary>
    static readonly bool s_unitDropVi =
        System.Environment.GetEnvironmentVariable("HERMES_256_UNITDROPVI") == "1";

    static readonly bool s_unitFields =
        System.Environment.GetEnvironmentVariable("HERMES_256_UNITFIELDS") == "1";

    // HERMES_256_UNITTRAILER1=1 emits the UnitData name trailer as ONE byte instead of seven.
    // Live landmark measurement (catch_pd_landmarks.py, 25 Aug): the client's reader consumes exactly
    // 1 byte between the last UnitData VirtualItem and PlayerData-start (VirtualItems-end 3782 ->
    // PlayerData head-start 3783), not the 7 we emit (u8 flags + u8 + u32 + u8 length). With the empty
    // name path (flags bit7=0) the reader reads a single byte and stops. Emitting 7 over-shoots the
    // whole self-create by 6 bytes, so the ActivePlayerData reader reads Coinage 6 bytes early -
    // GetMoney returned 0x0007000000000000 (= 7 << 48), the smoking gun. Default off (current 7-byte).
    static readonly bool s_unitTrailer1 =
        System.Environment.GetEnvironmentVariable("HERMES_256_UNITTRAILER1") == "1";

    static readonly bool s_invSlots =
        System.Environment.GetEnvironmentVariable("HERMES_256_INVSLOTS") == "1";

    // ON plumbs the real character name into the PlayerData Name field; with the 6-bit length
    // left 0 the character window shows "unknown" (confirmed live, 24 Aug). Length-variable when
    // on: the client reads exactly nameLen bytes right after the 12-byte DungeonScoreSummary
    // (live Rowine: name-bits byte 0x18 = len 6, then "Rowine"). Default off = current behavior.
    static readonly bool s_playerName =
        System.Environment.GetEnvironmentVariable("HERMES_256_PLAYERNAME") == "1";

    // ON writes ItemBonusKey.ItemID = the item's own EntryID in every item create. The live
    // 69110 session does this on EVERY item without exception (live5_s1_inflated.bin: entry 38
    // carries 38, 2654 carries 0x0A5E, 769 carries 0x0301, ... 12/12 items checked), where we
    // sent 0 = "no bonus key". On modern builds the item instance is keyed off the bonus key,
    // so 0 is a candidate for the empty paper doll. Default off = current behavior.
    static readonly bool s_itemBonusKey =
        System.Environment.GetEnvironmentVariable("HERMES_256_ITEMBONUSKEY") == "1";

    static readonly bool s_apdProbe =
        System.Environment.GetEnvironmentVariable("HERMES_256_APDPROBE") == "1";

    /// <summary>
    /// Live-exact ActivePlayerData structure (24 Aug): the full 553-reader walk over the LIVE
    /// populated 0x07 create (ap_rowine.bin, APD 3640..11323) closes end-to-end ONLY with these
    /// corrections, every one anchored on real values (Coinage 737, XP 4061/4500, skills,
    /// WatchedFactionIndex -1, MaxLevel 70, NumBackpackSlots 16, ModPetHaste 1.0, HonorNextLevel
    /// 5500, PvpInfo bracket indices at 66-byte stride):
    ///   1. TWO extra u32 (zero) after the AccountDataElements count.
    ///   2. RestInfo[2] {u32 Threshold, u8 StateID} IS on the wire (live: {650,1},{0,2}).
    ///   3. CombatRatings[32] IS on the wire (128 zero bytes) after WatchedFactionIndex.
    ///      The "reader does not read them / -146 bytes" belief was measured over an all-zero
    ///      block where a skipped field and a zero field are indistinguishable. The live wire
    ///      carries all 146 bytes; without them every field after - including NumBackpackSlots,
    ///      which is why GetContainerNumSlots(0)==0 - lands shifted in the client model.
    ///   4. The post-PvpInfo tail is 161 bytes on live (ours hand-derived 105): emitted verbatim
    ///      from the live block (all zeros + the final 19-byte cluster).
    /// ON also makes APDPAD irrelevant (the exact tail replaces the heap-cover pad).
    /// Default off = current behavior.
    /// </summary>

    /// <summary>
    /// Appends the five bytes between what WriteActivePlayerData emits (6231) and what the
    /// client's own ActivePlayerData reader walks over real captured bytes (6236) - a u32 that
    /// starts at wire +6230 and an empty packed guid at +6234. See the long note at the end of
    /// WriteActivePlayerData. Off by default; lengthening a block can only end an over-read.
    /// </summary>
    // XP bar shows a reputation (e.g. "Bloodsail Buccaneers") instead of XP (24 Aug). UpdateHandler
    // fills WatchedFactionIndex from the legacy 2.4.3 PLAYER_FIELD_WATCHED_FACTION_INDEX - a rep INDEX
    // into the 2.4.3 faction ordering - and the writer passes it through, so the 69110 client (whose
    // reputation list is keyed differently) resolves it to the wrong faction and watches it on the XP
    // bar. Confirmed on the wire: the client's own reader reads 18 into obj+0x1524 (not -1), with
    // MaxLevel=60 dword-adjacent at obj+0x1528 - so it is a value passthrough, not an alignment shift.
    // ON forces the field to -1 (the documented "none" every other version writer uses); the XP bar
    // then shows XP. Length-neutral. Correct legacy->modern mapping would need SMSG_INITIALIZE_FACTIONS.
    static readonly bool s_watchedFactionNone =
        System.Environment.GetEnvironmentVariable("HERMES_256_WATCHEDFACTION") == "1";

    // ON writes ActivePlayerData.MaxLevel = 70 in the create so the client shows the XP bar (a
    // character at MaxLevel 0 is treated as max-level and the bar is hidden). See WriteActivePlayerData.
    // See the HERMES_256_QUESTSTATE0 note in the QuestLog loop. Default off.
    static readonly bool s_questState0 =
        System.Environment.GetEnvironmentVariable("HERMES_256_QUESTSTATE0") == "1";

    static readonly bool s_maxLevel70 =
        System.Environment.GetEnvironmentVariable("HERMES_256_MAXLEVEL70") == "1";

    static readonly bool s_apdTail =
        System.Environment.GetEnvironmentVariable("HERMES_256_APDTAIL") == "1";

    /// <summary>
    /// World-entry freeze, root-caused from the client's own minidump (24 Aug 2026).
    ///
    /// ERROR #109 (thread frozen 60s) fires in the ActivePlayerData CREATE reader: the freeze stack
    /// is <c>0x72E4E4</c> (a resize/element loop) &lt;- <c>0x729AA0</c> &lt;- the APD reader
    /// <c>0x713E50</c> at <c>0x71AE1D</c> (the map at obj+0x1B58). In the minidump (WowClassic base
    /// 0x7FF689270000, frozen thread 13616) the stream cursor is <b>11052</b> - one past the end of
    /// the 11051-byte packet buffer - and the loop count is <b>3,361,744,641 (0xC8602701)</b>, a value
    /// that does NOT appear anywhere in the packet: it was read from <b>heap</b> past the buffer end.
    ///
    /// Cause: the client's APD create reader consumes ~44 bytes MORE than <see cref="WriteActivePlayerData"/>
    /// emits, so by the time it reaches the obj+0x1B58 map's u32 count that count is read from beyond
    /// the buffer. Whatever stale heap sits there becomes an array length handed to a resize+element
    /// loop - a 60-second spin when it is large. This is why the SAME block "sometimes" entered the
    /// world: the older note here called good and frozen captures byte-identical and streamwalk
    /// consumes 6236 over both, which is exactly the signature of an over-read into non-deterministic
    /// heap rather than a value in the block. The emulator cannot see it (it does not model the bit
    /// context at 0x71AA75 or the tail arms), and no live ActivePlayer create exists to diff.
    ///
    /// The deterministic fix until the exact missing field is identified: append N zero bytes to this
    /// (last) block so the reader's tail reads - the map counts included - land on in-buffer zeros
    /// instead of heap. Every count then reads 0, every element loop is skipped, and the reader
    /// finishes inside the buffer. Trailing zeros on the final block of a batch are inert (the client
    /// resynchronises on the batch's declared size). Default 0 (current behaviour); set
    /// HERMES_256_APDPAD=128 to cover the measured ~44-byte over-read with margin.
    /// </summary>
    static readonly int s_apdPad =
        int.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_APDPAD"), out var _apdPad)
            ? _apdPad : 0;

    /// <summary>
    /// The legacy unit flags are not carried across. Measured: copying them raw makes every
    /// creature draw a second, pale model - the block itself is fine, since name, level and
    /// health all read back correctly, so a bit that meant something harmless in 2.4.3 means
    /// mounted or vehicle in the 5.5.0 engine. Zero renders correctly.
    ///
    /// This drops real information - not-selectable, in-combat and the rest - so it is a
    /// placeholder until the legacy bits are mapped to this build's. HERMES_256_RAWFLAGS=1
    /// restores the raw copy for that work.
    /// </summary>
    static readonly bool s_noFlags =
        System.Environment.GetEnvironmentVariable("HERMES_256_RAWFLAGS") != "1";

    /// <summary>
    /// Writes UnitData, optionally replacing a window of it with self-locating values so the
    /// client's object memory reveals which wire offset each of its fields came from.
    /// A ruler over the whole block killed the client twice - outright, then by allocating on a
    /// slot read as a count - so the window keeps everything outside it real.
    /// </summary>
    void WriteUnitData(WorldPacket w)
    {
        if (s_rulerTo <= s_rulerFrom)
        {
            WriteUnitDataReal(w);
            return;
        }

        WorldPacket probe = new();
        WriteUnitDataReal(probe);
        byte[] real = probe.GetData();

        // The 4-byte grid has to start at the window, not at offset 0: UnitData has single-byte
        // fields in it, so a grid anchored at 0 straddles field boundaries and the ruler values
        // land split across two fields instead of one each.
        uint from = System.Math.Min(s_rulerFrom, (uint)real.Length);
        uint to = System.Math.Min(s_rulerTo, (uint)real.Length);
        w.WriteBytes(real, from);
        uint off = from;
        for (; off + 4 <= to; off += 4)
            w.WriteUInt32(s_rulerTag | off);
        for (; off < (uint)real.Length; off++)
            w.WriteUInt8(real[off]);
    }

    /// <summary>
    /// Generated from WowPacketParser's ReadCreateUnitData for V2_5_5_64796
    /// (UpdateFieldsHandler255.cs) by tools-256-spike/gen_writer.py, with the fields we populate
    /// substituted back by name. That handler is generated per version and mirrors the client's own
    /// create reader, so it is wire order - the UpdateFields class files are not, and believing
    /// them is what produced the previous body.
    ///
    /// Three things it settles that hand-editing had wrong: there is no CreatureType byte between
    /// Sex and DisplayPower; the regen arrays are gated on Owner|UnitAll, so the gating measured
    /// in-game was right; and both power blocks are interleaved loops, not separate arrays.
    ///
    /// VERIFIED AGAINST THE CLIENT, 23 Aug. A creature's create block was carved out of a live
    /// capture (PacketsLog, 586-byte values blob = 5 fragment header + 12 ObjectData + 569 UnitData)
    /// and fed to the client's own create-reader 0x2C5D120 with the visibility byte we really send -
    /// 0/None for every world unit, not Owner. Every one of the reader's first 62 reads lands
    /// exactly on a field boundary of this body, at the same width, with the values we wrote
    /// (Health 100 at +0, DisplayID 11416 at +16, Level 2 at +219, FactionTemplate 32 at +244).
    /// The head of this body is byte-exact from +0 to +247 and there is nothing missing or spare
    /// in it - in particular there is no "26-byte pre-Level gap" and no 76-byte excess.
    ///
    /// There is exactly ONE divergence, at +248, and it is VirtualItems. See the two knobs
    /// s_unitTail and s_unitDropVi. With both on the block totals 598 for a creature and 898 for
    /// the player, which is what the reader consumes in each case.
    ///
    /// tools-256-spike/udoffsets.py prints this body's offsets for a given visibility;
    /// `streamwalk.py 2C5D120 base=<n> flags=<vis> dump=<block.bin>` prints the client's.
    /// </summary>
    void WriteUnitDataReal(WorldPacket w)
    {
        w.WriteUInt64((ulong)(m_updateData.UnitData?.Health ?? 1));        // Health
        w.WriteUInt64((ulong)(m_updateData.UnitData?.MaxHealth ?? 1));        // MaxHealth
        w.WriteUInt32((uint)(m_updateData.UnitData?.DisplayID ?? 0));        // DisplayID
        // NpcFlags carries the gossip (0x1) and questgiver (0x2) markers a client needs to
        // draw the ! / ? over a unit and to open its gossip. The handler already fills
        // UnitData.NpcFlags from the legacy UNIT_NPC_FLAGS (NPC flag bits are stable across
        // versions); writing 0 here dropped that. NpcFlags2 has no legacy source, so 0 stands.
        w.WriteUInt32(s_npcFlags ? (uint)(m_updateData.UnitData?.NpcFlags[0] ?? 0) : 0);        // NpcFlags
        w.WriteUInt32(s_npcFlags ? (uint)(m_updateData.UnitData?.NpcFlags[1] ?? 0) : 0);        // NpcFlags2
        w.WriteUInt32((uint)(m_updateData.UnitData?.StateSpellVisualID ?? 0));        // StateSpellVisualID
        // >>> StateAnimID. Same field, same value and same reason as GameObjectData's
        // SpawnTrackingStateAnimID: TrinityCore emits DB2Mgr::GetEmptyAnimStateID() for both, and
        // section 129 measured that as 1860 on this build. Zero selects the default AnimKit 5102,
        // whose segment asks for animation 1672, and AnimationData here holds only the TBC subset
        // (ids 0..801). We fixed the gameobject field - which is what made gameobjects render - and
        // left this one at 0 for every unit including the player. Candidate for the kneeling NPCs.
        // HERMES_256_UNITANIM=1 sends 1860. Untested live.
        w.WriteUInt32(s_unitAnim ? 1860u : (uint)(m_updateData.UnitData?.StateAnimID ?? 0));        // StateAnimID
        w.WriteUInt32((uint)(m_updateData.UnitData?.StateAnimKitID ?? 0));        // StateAnimKitID
        w.WriteUInt32(0);        // ?
        // [elements of StateWorldEffectIDs go here]
        w.WritePackedGuid128(m_updateData.UnitData?.Charm ?? WowGuid128.Empty);        // Charm
        w.WritePackedGuid128(m_updateData.UnitData?.Summon ?? WowGuid128.Empty);        // Summon
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            w.WritePackedGuid128(m_updateData.UnitData?.Critter ?? WowGuid128.Empty);        // Critter
        }
        w.WritePackedGuid128(m_updateData.UnitData?.CharmedBy ?? WowGuid128.Empty);        // CharmedBy
        w.WritePackedGuid128(m_updateData.UnitData?.SummonedBy ?? WowGuid128.Empty);        // SummonedBy
        w.WritePackedGuid128(m_updateData.UnitData?.CreatedBy ?? WowGuid128.Empty);        // CreatedBy
        w.WritePackedGuid128(m_updateData.UnitData?.DemonCreator ?? WowGuid128.Empty);        // DemonCreator
        w.WritePackedGuid128(m_updateData.UnitData?.LookAtControllerTarget ?? WowGuid128.Empty);        // LookAtControllerTarget
        w.WritePackedGuid128(m_updateData.UnitData?.Target ?? WowGuid128.Empty);        // Target
        w.WritePackedGuid128(WowGuid128.Empty);        // BattlePetCompanionGUID
        w.WriteUInt64(0);        // BattlePetDBID
        // >>> ChannelData - the handler fills UnitData.ChannelData from the legacy
        // UNIT_CHANNEL_SPELL (SpellID plus a resolved SpellXSpellVisualID); writing 0 here
        // dropped the channel visual on any unit mid-channel at create time.
        w.WriteInt32(m_updateData.UnitData?.ChannelData?.SpellID ?? 0);        // SpellID
        w.WriteInt32(m_updateData.UnitData?.ChannelData?.SpellXSpellVisualID ?? 0);        // SpellXSpellVisualID
        w.WriteUInt32(0);        // StartTimeMs - no legacy source: UNIT_CHANNEL_SPELL carries no timing
        w.WriteUInt32(0);        // Duration - no legacy source: UNIT_CHANNEL_SPELL carries no timing
        w.WriteUInt32((uint)(m_updateData.UnitData?.SummonedByHomeRealm ?? 0));        // SummonedByHomeRealm
        w.WriteUInt8(m_updateData.UnitData?.RaceId ?? 0);        // Race
        w.WriteUInt8(m_updateData.UnitData?.ClassId ?? 0);        // ClassId
        w.WriteUInt8(m_updateData.UnitData?.ClassId ?? 0);        // PlayerClassId
        w.WriteUInt8(m_updateData.UnitData?.SexId ?? 0);        // Sex
        w.WriteUInt8((byte)(m_updateData.UnitData?.DisplayPower ?? 0));        // DisplayPower
        w.WriteUInt32((uint)(m_updateData.UnitData?.OverrideDisplayPowerID ?? 0));        // OverrideDisplayPowerID
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner) || GetFieldVisibility().HasFlag(FieldVisibility.UnitAll))
        {
            for (int i1 = 0; i1 < 10; ++i1)
            {
                w.WriteFloat(0.0f);        // PowerRegenFlatModifier
                w.WriteFloat(0.0f);        // PowerRegenInterruptedFlatModifier
            }
        }
        for (int i0 = 0; i0 < 10; ++i0)
        {
            w.WriteInt32(s_unitFields ? i0 < 7 ? (m_updateData.UnitData?.Power[i0] ?? 0) : 0 : 0);        // Power
            w.WriteInt32(s_unitFields ? i0 < 7 ? (m_updateData.UnitData?.MaxPower[i0] ?? 0) : 0 : 0);        // MaxPower
            w.WriteFloat(s_unitFields ? i0 < 7 ? (m_updateData.UnitData?.ModPowerRegen[i0] ?? 0.0f) : 0.0f : 0.0f);        // ModPowerRegen
        }
        w.WriteUInt32(s_levelMark != 0 ? s_levelMark : (uint)(m_updateData.UnitData?.Level ?? 1));        // Level
        w.WriteUInt32((uint)(m_updateData.UnitData?.EffectiveLevel ?? m_updateData.UnitData?.Level ?? 1));        // EffectiveLevel
        w.WriteInt32(0);        // ContentTuningID
        w.WriteInt32(0);        // ScalingLevelMin
        w.WriteInt32(0);        // ScalingLevelMax
        w.WriteInt32(0);        // ScalingLevelDelta
        w.WriteUInt8(0);        // ScalingFactionGroup
        w.WriteUInt32((uint)(m_updateData.UnitData?.FactionTemplate ?? 0));        // FactionTemplate
        // >>> VirtualItems (creature/NPC weapon slots), in the V2_5_5 position and element size.
        // This build reads NEITHER here: FactionTemplate (obj+0x124) is followed immediately by
        // Flags (obj+0x128), and VirtualItems is a 23-byte element read at the END of the block.
        // These 48 bytes are therefore consumed by the client as Flags, Flags2, Flags3, Flags4,
        // AuraState, AttackRoundBaseTime[3] and the first four of BoundingRadius..MountDisplayID -
        // which is the white names, the grey health bars, the unattackable neutrals and the
        // kneeling guards, all of it. HERMES_256_UNITDROPVI=1 removes them; see the knob's note.
        if (!s_unitDropVi)
        {
            for (int i0 = 0; i0 < 3; ++i0)
            {
                // The handler resolves the legacy UNIT_VIRTUAL_ITEM_SLOT_DISPLAY display id back
                // to an item entry; the modern client renders the held item's appearance from that
                // ItemID via its ItemModifiedAppearance db2 (+ the hotfix records the proxy
                // pushes), so ItemID alone is enough here.
                var vi = m_updateData.UnitData?.VirtualItems[i0];
                w.WriteInt32(s_itemLook ? (vi?.ItemID ?? 0) : 0);                    // ItemID
                w.WriteInt32(0);                                   // SecondaryItemModifiedAppearanceID
                w.WriteInt32(0);                                   // ConditionalItemAppearanceID
                w.WriteUInt16(s_itemLook ? (vi?.ItemAppearanceModID ?? 0) : (ushort)0);      // ItemAppearanceModID
                w.WriteUInt16(s_itemLook ? (vi?.ItemVisual ?? 0) : (ushort)0);               // ItemVisual
            }
        }
        // The legacy unit flags are copied across raw, and the bit meanings are not the same
        // in the 5.5.0 engine. A bit that was harmless in 2.4.3 can mean mounted or vehicle
        // here, which draws a second model on a unit whose block is otherwise correct - the
        // creatures read back the right name, level and health, so this is not a misalignment.
        // HERMES_256_NOFLAGS=1 zeroes them to test that.
        // >>> PLAYER_CONTROLLED (0x8). Measured out of CGUnit_C::CanAttack (rva 0x1906BE0): the final
        // decision has two thresholds and the gate between them is Flags & 0x8 on EITHER unit -
        //     0x1906EE6  test byte [rdi+0x12608], 8      attacker's Flags (descriptor+0x128)
        //     0x1906EEF  test byte [rbx+0x12608], 8      target's
        //     neither set -> attackable only if reaction <= 1   (hated / hostile)
        //     either  set -> attackable if reaction < 4         (through neutral)
        // We send Flags = 0 for every unit including the player, so the client sits permanently on
        // the strict path: hostile creatures attackable, neutral ones not. Exactly the symptom.
        //
        // The fix is this one bit on the PLAYER, not HERMES_256_RAWFLAGS - copying the raw 2.4.3
        // bits across made every creature draw a second pale model, which is why s_noFlags exists.
        // HERMES_256_PCFLAG=1 turns it on. LIVE-CONFIRMED 28 Aug: neutral mobs became attackable,
        // and the reasoning above was verified against the live Blizzard capture first — for the
        // very same creature entries (Coldridge Valley: 705 Ragged Young Wolf, 704, 707, 708, 721,
        // 853) Blizzard sends the IDENTICAL FactionTemplate (32/32/36/189/31/57) and the identical
        // Flags = 0, so the difference was never in the creature. The client's own predicate
        // FUN@0x1906BE0 branches on this bit ON THE PLAYER: set -> the PvP path falls through to
        // "attackable if reaction < 4" (neutral passes); clear -> the NPC-vs-NPC path demands
        // reaction <= 1, which only hostiles satisfy.
        uint unitFlags = s_noFlags ? 0u
                                   : (s_flagsMark != 0 ? s_flagsMark : (uint)(m_updateData.UnitData?.Flags ?? 0));
        if (s_pcFlag && m_objectType == Enums.ObjectTypeBCC.ActivePlayer)
            unitFlags |= 0x8u;   // UNIT_FLAG_PLAYER_CONTROLLED
        w.WriteUInt32(unitFlags);        // Flags
        w.WriteUInt32(s_noFlags ? 0u : (uint)(m_updateData.UnitData?.Flags2 ?? 0));        // Flags2
        w.WriteUInt32(0);        // Flags3
        w.WriteUInt32(0);        // Flags4
        w.WriteUInt32((uint)(m_updateData.UnitData?.AuraState ?? 0));        // AuraState
        for (int i0 = 0; i0 < 3; ++i0)
        {
            // 3 wire slots, 2 in our model (legacy UNIT_FIELD_BASEATTACKTIME is main + offhand
            // only); the third slot has no legacy source and stays zero.
            w.WriteUInt32(i0 < 2 ? (m_updateData.UnitData?.AttackRoundBaseTime[i0] ?? 0) : 0);        // AttackRoundBaseTime
        }
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            w.WriteUInt32((uint)(m_updateData.UnitData?.RangedAttackRoundBaseTime ?? 0));        // RangedAttackRoundBaseTime
        }
        w.WriteFloat(m_updateData.UnitData?.BoundingRadius ?? 0.389f);        // BoundingRadius
        w.WriteFloat(m_updateData.UnitData?.CombatReach ?? 1.5f);        // CombatReach
        w.WriteFloat(m_updateData.UnitData?.DisplayScale ?? 1.0f);        // DisplayScale - handler derives it from the legacy display id's native scale
        w.WriteUInt32((uint)(m_updateData.UnitData?.NativeDisplayID ?? 0));        // NativeDisplayID
        w.WriteFloat(1.0f);        // NativeXDisplayScale
        w.WriteUInt32((uint)(m_updateData.UnitData?.MountDisplayID ?? 0));        // MountDisplayID
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner) || GetFieldVisibility().HasFlag(FieldVisibility.Empath))
        {
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MinDamage ?? 0.0f : 0.0f);        // MinDamage
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MaxDamage ?? 0.0f : 0.0f);        // MaxDamage
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MinOffHandDamage ?? 0.0f : 0.0f);        // MinOffHandDamage
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MaxOffHandDamage ?? 0.0f : 0.0f);        // MaxOffHandDamage
        }
        w.WriteUInt8((byte)(m_updateData.UnitData?.StandState ?? 0));        // StandState
        w.WriteUInt8(0);        // PetTalentPoints
        w.WriteUInt8(s_unitFields ? m_updateData.UnitData?.VisFlags ?? 0 : (byte)0);        // VisFlags
        w.WriteUInt8(s_unitFields ? m_updateData.UnitData?.AnimTier ?? 0 : (byte)0);        // AnimTier
        w.WriteUInt32(s_unitFields ? m_updateData.UnitData?.PetNumber ?? 0 : 0);        // PetNumber
        w.WriteUInt32(m_updateData.UnitData?.PetNameTimestamp ?? 0);        // PetNameTimestamp
        w.WriteUInt32(m_updateData.UnitData?.PetExperience ?? 0);        // PetExperience
        w.WriteUInt32(m_updateData.UnitData?.PetNextLevelExperience ?? 0);        // PetNextLevelExperience
        // The handler fills UnitData.ModCastSpeed from the legacy UNIT_MOD_CAST_SPEED; 1.0 is
        // the neutral multiplier when the legacy mask did not carry the field.
        w.WriteFloat(m_updateData.UnitData?.ModCastSpeed ?? 1.0f);        // ModCastingSpeed
        w.WriteFloat(0.0f);        // ModSpellHaste
        w.WriteFloat(0.0f);        // ModHaste
        w.WriteFloat(0.0f);        // ModRangedHaste
        w.WriteFloat(0.0f);        // ModHasteRegen
        w.WriteFloat(0.0f);        // ModTimeRate
        w.WriteInt32(s_unitFields ? m_updateData.UnitData?.CreatedBySpell ?? 0 : 0);        // CreatedBySpell
        w.WriteUInt32((uint)(m_updateData.UnitData?.EmoteState ?? 0));        // EmoteState
        w.WriteInt16(s_unitFields ? (short)(m_updateData.UnitData?.TrainingPointsUsed ?? 0) : (short)0);        // TrainingPointsUsed
        w.WriteInt16(s_unitFields ? (short)(m_updateData.UnitData?.TrainingPointsTotal ?? 0) : (short)0);        // TrainingPointsTotal
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            for (int i1 = 0; i1 < 5; ++i1)
            {
                w.WriteInt32(s_unitFields ? m_updateData.UnitData?.Stats[i1] ?? 0 : 0);        // Stats
                w.WriteInt32(s_unitFields ? m_updateData.UnitData?.StatPosBuff[i1] ?? 0 : 0);        // StatPosBuff
                w.WriteInt32(s_unitFields ? m_updateData.UnitData?.StatNegBuff[i1] ?? 0 : 0);        // StatNegBuff
            }
        }
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner) || GetFieldVisibility().HasFlag(FieldVisibility.Empath))
        {
            for (int i1 = 0; i1 < 7; ++i1)
            {
                w.WriteInt32(s_unitFields ? m_updateData.UnitData?.Resistances[i1] ?? 0 : 0);        // Resistances
            }
        }
        for (int i0 = 0; i0 < 7; ++i0)
        {
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.ResistanceBuffModsPositive[i0] ?? 0 : 0);        // ResistanceBuffModsPositive
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.ResistanceBuffModsNegative[i0] ?? 0 : 0);        // ResistanceBuffModsNegative
        }
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            for (int i1 = 0; i1 < 7; ++i1)
            {
                w.WriteInt32(s_unitFields ? m_updateData.UnitData?.PowerCostModifier[i1] ?? 0 : 0);        // PowerCostModifier
                w.WriteFloat(s_unitFields ? m_updateData.UnitData?.PowerCostMultiplier[i1] ?? 0.0f : 0.0f);        // PowerCostMultiplier
            }
        }
        w.WriteUInt32((uint)(m_updateData.UnitData?.BaseMana ?? 0));        // BaseMana
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            w.WriteUInt32((uint)(m_updateData.UnitData?.BaseHealth ?? 0));        // BaseHealth
        }
        w.WriteUInt8(s_unitFields ? m_updateData.UnitData?.SheatheState ?? 0 : (byte)0);        // SheatheState
        w.WriteUInt8(s_unitFields ? m_updateData.UnitData?.PvpFlags ?? 0 : (byte)0);        // PvpFlags
        w.WriteUInt8(s_unitFields ? m_updateData.UnitData?.PetFlags ?? 0 : (byte)0);        // PetFlags
        w.WriteUInt8(s_unitFields ? m_updateData.UnitData?.ShapeshiftForm ?? 0 : (byte)0);        // ShapeshiftForm
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.AttackPower ?? 0 : 0);        // AttackPower
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.AttackPowerModPos ?? 0 : 0);        // AttackPowerModPos
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.AttackPowerModNeg ?? 0 : 0);        // AttackPowerModNeg
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.AttackPowerMultiplier ?? 0.0f : 0.0f);        // AttackPowerMultiplier
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.RangedAttackPower ?? 0 : 0);        // RangedAttackPower
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.RangedAttackPowerModPos ?? 0 : 0);        // RangedAttackPowerModPos
            w.WriteInt32(s_unitFields ? m_updateData.UnitData?.RangedAttackPowerModNeg ?? 0 : 0);        // RangedAttackPowerModNeg
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.RangedAttackPowerMultiplier ?? 0.0f : 0.0f);        // RangedAttackPowerMultiplier
            w.WriteInt32(0);        // SetAttackSpeedAura
            w.WriteFloat(0.0f);        // Lifesteal
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MinRangedDamage ?? 0.0f : 0.0f);        // MinRangedDamage
            w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MaxRangedDamage ?? 0.0f : 0.0f);        // MaxRangedDamage
        }
        w.WriteFloat(s_unitFields ? m_updateData.UnitData?.MaxHealthModifier ?? 0.0f : 0.0f);        // MaxHealthModifier
        w.WriteFloat(0.0f);        // HoverHeight
        w.WriteInt32(0);        // MinItemLevelCutoff
        w.WriteInt32(0);        // MinItemLevel
        w.WriteInt32(0);        // MaxItemLevel
        w.WriteInt32(0);        // WildBattlePetLevel
        w.WriteUInt32(0);        // BattlePetCompanionNameTimestamp
        w.WriteInt32(0);        // InteractSpellID
        w.WriteInt32(0);        // ScaleDuration
        w.WriteInt32(0);        // LooksLikeMountID
        w.WriteInt32(0);        // LooksLikeCreatureID
        w.WriteInt32(0);        // LookAtControllerID
        w.WriteInt32(0);        // PerksVendorItemID
        // GuildGUID kept Empty, but the old reason has expired: it was held back because the block
        // length was under live measurement (the "868 sent vs 851 consumed" figure, which section
        // 130 retracted - the knob that produced it never truncated anything). A packed guid is
        // symmetric, so a real value costs the same on both sides: the client reads it at 0x2C623EA
        // into obj+0x210 and consumes mask + popcount bytes, exactly as we write them. Safe to wire
        // once the two knobs below have shipped and the block length is confirmed in game.
        w.WritePackedGuid128(WowGuid128.Empty);        // GuildGUID obj+0x210
        w.WriteUInt32(0);        // PassiveSpells size
        w.WriteUInt32(0);        // WorldEffects size
        // ChannelObjects size kept 0 DELIBERATELY, and this one is still load-bearing: these three
        // counts size the client's three tail vector loops, which run AFTER the obj+0x308 guid and
        // read their elements from the stream. A non-zero count with no matching elements written
        // would desynchronise the rest of the block.
        w.WriteUInt32(0);        // ChannelObjects size
        w.WritePackedGuid128(WowGuid128.Empty);        // SkinningOwnerGUID
        w.WriteInt32(0);        // FlightCapabilityID
        w.WriteFloat(0.0f);        // GlideEventSpeedDivisor
        w.WriteInt32(0);        // DriveCapabilityID
        w.WriteUInt32(0);        // SilencedSchoolMask
        w.WriteUInt32(0);        // CurrentAreaID
        if (GetFieldVisibility().HasFlag(FieldVisibility.Owner))
        {
            w.WritePackedGuid128(WowGuid128.Empty);        // ComboTarget (kept Empty, see FarsightObject note)
        }
        w.WriteFloat(0.0f);        // Field_2F0
        w.WriteFloat(0.0f);        // Field_2F4
        // [elements of PassiveSpells go here]
        // [elements of WorldEffects go here]
        // [elements of ChannelObjects go here]
        // The three counts above are written as 0, so the client's three tail vector loops
        // (0x2C62A50 over obj+0x220/0x228, 0x2C62C80, 0x2C62CD1) iterate zero times. Confirmed:
        // the captured creature block goes straight from the obj+0x308 guid into VirtualItems.
        if (!s_unitTail)
        {
            // The V2_5_5 ending: two create-bits, flushed to one byte. This build's reader does
            // not read them - it reads the 78 bytes below instead. See the s_unitTail note.
            w.WriteBit(false);        // Field_314
            w.WriteBit(false);        // HasAssistActionData
            // [skipped: empty for the data we send]
        }
        if (s_unitTail)
        {
            // >>> The tail this build's reader actually reads, transcribed from the client.
            //
            // 1. A packed guid at obj+0x308, read unconditionally right after the two trailing
            //    floats (client 0x2C62A22 `lea rdx,[rdi+0x308]`, 0x2C62A34 call packed-guid128).
            //    Not in the V2_5_5 field set. Empty = 2 bytes.
            w.WritePackedGuid128(WowGuid128.Empty);        // obj+0x308, unnamed on this build

            // 2. VirtualItems[3], element reader 0x741E60, loop 0x2C62EC0 (`cmp eax,3`, object
            //    array at obj+0x3E8 with a 0x1C stride). The element is 23 wire bytes, not the 16
            //    of V2_5_5 - the same growth section 110 found for PlayerData's VisibleItem.
            //    Ghidra's decompilation of 0x741E60 gives the order exactly:
            //      u32 -> elem+0x00, u32 -> +0x04, u32 -> +0x08, u16 -> +0x0C, u16 -> +0x0E,
            //      u32 -> +0x14, u8 -> +0x18, u8 -> +0x19, u8 -> two bools (bit7 -> +0x10,
            //      bit6 -> +0x11).
            //    The last one is a whole byte read through the u8 primitive, not a bit-pack, so it
            //    is written as a byte here.
            for (int i0 = 0; i0 < 3; ++i0)
            {
                var vi = m_updateData.UnitData?.VirtualItems[i0];
                w.WriteInt32(s_itemLook ? (vi?.ItemID ?? 0) : 0);                          // ItemID
                w.WriteInt32(0);                                     // SecondaryItemModifiedAppearanceID
                w.WriteInt32(0);                                     // ConditionalItemAppearanceID
                w.WriteUInt16(s_itemLook ? (vi?.ItemAppearanceModID ?? 0) : (ushort)0);    // ItemAppearanceModID
                w.WriteUInt16(s_itemLook ? (vi?.ItemVisual ?? 0) : (ushort)0);             // ItemVisual
                w.WriteUInt32(0);        // elem+0x14, unnamed on this build
                w.WriteUInt8(0);         // elem+0x18, unnamed on this build
                w.WriteUInt8(0);         // elem+0x19, unnamed on this build
                w.WriteUInt8(0);         // flag byte: bit7 -> elem+0x10, bit6 -> elem+0x11
            }

            // 3. A 7-byte name trailer, and then a length-prefixed string. Disassembled at
            //    0x2C633E2..0x2C63570:
            //      u8   flags; bit 7 selects a path on the object slot at obj+0x318 (0x2C633FD
            //           `shr dl,7`). 0 keeps it off.
            //      u8   stored at the head of that struct (0x2C63516).
            //      u32  stored at struct+0x34 (0x2C63535).
            //      u8   length byte; the client takes `>> 2` as a character count (0x2C6355E) and
            //           then reads exactly that many raw bytes (0x2C6356B) and NUL-terminates.
            //           0 means no string bytes follow, which is what we want - this is not the
            //           creature name the tooltip shows, that comes from the name query.
            if (s_unitTrailer1)
            {
                // The client (empty-name path, flags bit7=0) reads exactly ONE byte here and stops -
                // measured live. The 7-byte form below over-shoots the block by 6 bytes.
                w.WriteUInt8(0);     // name flags/length in one byte (bit7=0, >>2 = 0 characters)
            }
            else
            {
                w.WriteUInt8(0);         // name flags (obj+0x318 path gate, bit 7 = 0)
                w.WriteUInt8(0);         // name struct head byte
                w.WriteUInt32(0);        // name struct+0x34
                w.WriteUInt8(0);         // name length byte (>> 2 = 0 characters)
            }
        }
    }

    static readonly int s_playerLead =
        int.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_PLAYERLEAD"), out var pd) ? pd : 0;

    /// <summary>
    /// Written from the client's own PlayerData reader at RVA 0x738FB0, walked and sync-validated
    /// by tools-256-spike/pdwalk.py. That reader consumes 1030 bytes where the previous body sent
    /// 897 - and the entire 133-byte shortfall is one element size: VisibleItem is 23 bytes on this
    /// build, not 16, and the array is read at the END of PlayerData rather than after QuestLog.
    /// 19 slots times 7 bytes is 133 exactly.
    ///
    /// The previous body faithfully reproduced WowPacketParser's ReadCreatePlayerData for
    /// V2_5_5_64796 - a field-by-field audit found no transcription error anywhere. The assumption
    /// that failed was that 69110's PlayerData is 64796's PlayerData. Section 85 matched the field
    /// *set* to that build and the match was real; the wire layout moved two builds later.
    ///
    /// See REFERENCE-256-CLIENT.md sections 108-110. The previous body is kept verbatim at
    /// tools-256-spike/playerdata_previous.cs.
    /// </summary>
    void WritePlayerData(WorldPacket w)
    {
        var pd = m_updateData.PlayerData;
        var custom = s_noCustom ? null : pd?.Customizations;
        int customCount = 0;
        if (custom != null)
        {
            foreach (var c in custom)
            {
                if (c != null) customCount++;
            }
        }
        // DuelArbiter kept Empty DELIBERATELY: the handler fills PlayerData.DuelArbiter during a
        // duel, but a non-empty packed guid emits more bytes than an empty one; per the current
        // rules a wiring that can change a block length is reported, not applied. Consequence:
        // duel requests will not show their flag object until this is wired.
        w.WritePackedGuid128(WowGuid128.Empty);        // DuelArbiter (see note above)
        w.WritePackedGuid128(WowGuid128.Empty);        // WowAccount
        w.WritePackedGuid128(WowGuid128.Empty);        // BnetAccount
        w.WriteUInt64(0);        // GuildClubMemberID
        w.WritePackedGuid128(WowGuid128.Empty);        // LootTargetGUID
        w.WriteUInt32(s_playerFlags != 0 ? s_playerFlags : (uint)(pd?.PlayerFlags ?? 0));        // PlayerFlags
        w.WriteUInt32((uint)(pd?.PlayerFlagsEx ?? 0));        // PlayerFlagsEx
        w.WriteUInt32((uint)(pd?.GuildRankID ?? 0));        // GuildRankID
        w.WriteUInt32((uint)(pd?.GuildDeleteDate ?? 0));        // GuildDeleteDate
        w.WriteUInt32((uint)(pd?.GuildLevel ?? 0));        // GuildLevel
        w.WriteUInt32((uint)customCount);        // Customizations size
        for (int i0 = 0; i0 < 2; ++i0)
        {
            w.WriteUInt8(0);        // PartyType
        }
        w.WriteUInt8(pd?.NumBankSlots ?? 0);        // NumBankSlots
        w.WriteUInt8(s_nativeSex >= 0 ? (byte)s_nativeSex : (pd?.NativeSex ?? m_updateData.UnitData?.SexId ?? 0));        // NativeSex
        w.WriteUInt8(pd?.Inebriation ?? 0);        // Inebriation
        w.WriteUInt8(pd?.PvpTitle ?? 0);        // PvpTitle (legacy city-protector byte)
        w.WriteUInt8(pd?.ArenaFaction ?? 0);        // ArenaFaction
        w.WriteUInt8(pd?.PvPRank ?? 0);        // PvpRank (legacy honor rank byte)
        w.WriteInt32(0);        // Field_88
        w.WriteUInt32(pd?.DuelTeam ?? 0);        // DuelTeam
        w.WriteInt32(pd?.GuildTimeStamp ?? 0);        // GuildTimeStamp
        if (GetFieldVisibility().HasFlag(FieldVisibility.PartyMember))
        {
            for (int i1 = 0; i1 < 25; ++i1)
            {
                // >>> QuestLog. Client element reader 0x742110: the u32 after EndTime is NEW
                // on this build (stored at entry+0x10; WPP's V2_5_5 entry stops at EndTime).
                //
                // The handler (UpdateHandler.ReadQuestLogEntry) fills these from the legacy
                // PLAYER_QUEST_LOG fields; writing zeros here is why the quest log was empty.
                // Entries the legacy mask did not carry are null and still write zeros.
                // The wire slot count matches the model (25), but on a legacy core with a
                // smaller log (vanilla: 20) the tail entries simply stay null - guarded anyway.
                var q = pd != null && i1 < pd.QuestLog.Length ? pd.QuestLog[i1] : null;
                // Diagnostic: a quest the server considers complete does not show as complete and
                // cannot be shift-click tracked. Record what actually reaches the wire per entry —
                // whether StateFlags carries the completion bit, or whether the objective counters
                // arrive at all — before assuming which of the two the client reads.
                if (q?.QuestID != null && q.QuestID != 0)
                    Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                        $"[256-spike] questlog-create[{i1}] id={q.QuestID} stateFlags={q.StateFlags} " +
                        $"progress=[{string.Join(",", System.Linq.Enumerable.Take(q.ObjectiveProgress, 6))}] " +
                        $"endTime={q.EndTime}");
                w.WriteInt32(q?.QuestID ?? 0);        // QuestID
                // HERMES_256_QUESTSTATE0: do not pass the legacy quest state through. cmangos sets
                // its QUEST_STATE_COMPLETE (1) on a finished quest and this writer forwarded that
                // number into a modern field whose meaning here was never checked — the same class
                // of unverified passthrough that has caused every other fault in this build.
                // Ground truth says the field is simply not used that way: in a real Blizzard
                // self-create (tools-256-spike/ap_rowine.bin) EVERY quest carries stateFlags 0x0000
                // and the real counts live in ObjectiveProgress (quest 313 -> [8], 317 -> [4,2],
                // 384 -> [6]). Measured symptom that fits: with our 1 on the wire the client had
                // the objective at 8/8 and finished=true — it counts item objectives from the bags
                // itself, and GetItemCount(750) returned 8 — yet IsQuestComplete stayed false.
                // Default off; on, the field is written as Blizzard writes it.
                w.WriteUInt16(s_questState0 ? (ushort)0 : (ushort)(q?.StateFlags ?? 0));        // StateFlags
                for (int i2 = 0; i2 < 24; ++i2)
                {
                    // 24 wire slots; the legacy field packs at most 4 objective counters
                    w.WriteUInt16((ushort)(q?.ObjectiveProgress[i2] ?? 0));        // ObjectiveProgress
                }
                w.WriteInt64(q?.EndTime ?? 0);        // EndTime
                // Probably AcceptTime (the model carries that field right after EndTime and the
                // client stores this u32 at entry+0x10), but no legacy handler ever fills
                // AcceptTime and the name is unverified against the client, so zero stands.
                w.WriteUInt32(0);        // Unknown_69110 (trailing u32, client entry+0x10)
            }
            // NEW on this build: a u32->u32 map follows QuestLog inside the same PartyMember
            // gate (client reader 0x73F600, hash-inserts key/value pairs at object+0xB0).
            // Empty is one zero count.
            w.WriteUInt32(0);        // QuestLogExtraMap size (count * { u32 key, u32 value })
        }
        // NOTE: VisibleItems[19] does NOT go here on this build. The client reads it at the
        // end of PlayerData, right before the name/flags bits byte - see below.
        w.WriteInt32(pd?.ChosenTitle ?? 0);        // PlayerTitle - handler fills PlayerData.ChosenTitle from PLAYER_CHOSEN_TITLE
        w.WriteInt32(0);        // FakeInebriation
        w.WriteUInt32(0);        // VirtualPlayerRealm
        w.WriteUInt32(0);        // CurrentSpecID
        w.WriteInt32(0);        // TaxiMountAnimKitID
        w.WriteInt32(0);        // Unk
        for (int i0 = 0; i0 < 6; ++i0)
        {
            w.WriteFloat(0.0f);        // AvgItemLevel
        }
        w.WriteUInt8(0);        // CurrentBattlePetBreedQuality
        w.WriteInt32(0);        // HonorLevel
        w.WriteInt64(0);        // LogoutTime
        w.WriteUInt32(0);        // ArenaCooldowns size
        for (int i0 = 0; i0 < 32; ++i0)
        {
            // >>> ForcedReactions
            w.WriteInt32(0);        // FactionID
            w.WriteInt32(0);        // Reaction
        }
        w.WriteInt32(0);        // Field_13C
        w.WriteInt32(0);        // Field_140
        w.WriteInt32(0);        // CurrentBattlePetSpeciesID
        w.WriteUInt32(0);        // PetNames size
        w.WriteUInt32(0);        // VisualItemReplacements size
        for (int i0 = 0; i0 < 19; ++i0)
        {
            w.WriteUInt32(0);        // Field_3120
        }
        // >>> PersonalTabard - 5 u32 on 553, but this build reads 10 (see s_tabard10). The extra 5
        // are zero (no legacy source; the era does not use the retail personal-tabard fields), but
        // the client consumes them, so omitting them shifts everything after by 20 bytes.
        w.WriteInt32(0);        // EmblemStyle
        w.WriteInt32(0);        // EmblemColor
        w.WriteInt32(0);        // BorderStyle
        w.WriteInt32(0);        // BorderColor
        w.WriteInt32(0);        // BackgroundColor
        if (s_tabard10)
        {
            w.WriteInt32(0); w.WriteInt32(0); w.WriteInt32(0); w.WriteInt32(0); w.WriteInt32(0); // +5 -> 10 u32
        }
        if (custom != null)
        {
            foreach (var c in custom)
            {
                if (c == null) continue;
                w.WriteUInt32(c.ChrCustomizationOptionID);        // Customizations[].ChrCustomizationOptionID
                w.WriteUInt32(c.ChrCustomizationChoiceID);        // Customizations[].ChrCustomizationChoiceID
            }
        }
        // [elements of ArenaCooldowns go here: 7 x u32 + u8 each, count written above is 0]
        // [elements of VisualItemReplacements go here: u32 each, count written above is 0]
        for (int i0 = 0; i0 < 19; ++i0)
        {
            // >>> VisibleItems, at the position and size the client actually reads them
            // (element reader 0x741E60, called 19 times from the loop at RVA 0x73B310, i.e.
            // AFTER the dynamic-array elements and BEFORE the name bits byte).
            //
            // The element is 23 bytes on this build: after ItemVisual the client reads a u32
            // (stored at elem+0x14), two u8 (elem+0x18/0x19) and one bits byte of which it
            // keeps bit 7 (elem+0x10) and bit 6 (elem+0x11). Writing 16-byte elements is what
            // made PlayerData 133 bytes short and pushed LeaverInfo into ActivePlayerData.
            //
            // The handler fills PlayerData.VisibleItems from the legacy PLAYER_VISIBLE_ITEM
            // fields: ItemID is the item entry and ItemVisual is the enchant visual. Writing 0
            // rendered the player and NPCs naked. ItemID resolves appearance on this build via
            // ItemModifiedAppearance.
            var vi = m_updateData.PlayerData?.VisibleItems[i0];
            w.WriteInt32(s_itemLook ? (vi?.ItemID ?? 0) : 0);                    // ItemID
            w.WriteInt32(0);                                   // SecondaryItemModifiedAppearanceID
            w.WriteInt32(0);                                   // ConditionalItemAppearanceID
            w.WriteUInt16(s_itemLook ? (vi?.ItemAppearanceModID ?? 0) : (ushort)0);      // ItemAppearanceModID
            w.WriteUInt16(s_itemLook ? (vi?.ItemVisual ?? 0) : (ushort)0);               // ItemVisual
            w.WriteUInt32(0);        // Unknown_69110_A (u32, client elem+0x14)
            w.WriteUInt8(0);         // Unknown_69110_B (u8, client elem+0x18)
            w.WriteUInt8(0);         // Unknown_69110_C (u8, client elem+0x19)
            w.WriteBits(0, 2);       // two flag bits (client elem+0x10 / +0x11)
            w.FlushBits();           // the client reads the pair as one whole byte
        }
        // Real character name, gated behind HERMES_256_PLAYERNAME: the writer never plumbed
        // a name, so the client window showed "unknown". Source is the session player cache
        // (NAME_QUERY / char enum); the own character falls back to CurrentPlayerInfo. An
        // unknown name still writes length 0, byte-identical to the old behavior.
        byte[] playerNameBytes = System.Array.Empty<byte>();
        if (s_playerName)
        {
            var playerName = m_gameState.GetPlayerName(m_updateData.Guid);
            if (string.IsNullOrEmpty(playerName) &&
                m_gameState.CurrentPlayerInfo?.CharacterGuid == m_updateData.Guid)
                playerName = m_gameState.CurrentPlayerInfo.Name ?? "";
            if (!string.IsNullOrEmpty(playerName))
            {
                playerNameBytes = System.Text.Encoding.UTF8.GetBytes(playerName);
                if (playerNameBytes.Length > 63)        // 6-bit length field
                    playerNameBytes = playerNameBytes[..63];
            }
        }
        w.WriteBits(playerNameBytes.Length, 6);        // Name length
        w.WriteBit(false);        // HasLevelLink
        w.WriteBit(false);        // HasDeclinedNames
        // The three fields above pack into ONE byte (client reads a u8 at 0x73B7A4:
        // bits 7..2 = name length, bit 1 = HasLevelLink, bit 0 = HasDeclinedNames).
        // DungeonScoreSummary, verified against the client's reader 0x66DCC0:
        // f32, f32, u32 count - 12 bytes when empty, exactly this. (A non-empty run element
        // would be i32 ChallengeModeID, f32 MapScore, i32 BestRunLevel, i32 BestRunDuration,
        // u8, then one bits byte with bit 7 = FinishedSuccess - 18 bytes.)
        w.WriteFloat(0.0f);        // DungeonScore.OverallScoreCurrentSeason
        w.WriteFloat(0.0f);        // DungeonScore.LadderScoreCurrentSeason
        w.WriteUInt32(0);        // DungeonScore.Runs size
        // Name string bytes follow here when the 6-bit length above is non-zero - the live
        // block places them directly after DungeonScoreSummary and before LeaverInfo.
        if (playerNameBytes.Length > 0)
            w.WriteBytes(playerNameBytes);        // Name string (nameLen bytes, no terminator)
        // >>> LeaverInfo (client reader 0x669430, 43 bytes, all verified)
        w.WritePackedGuid128(WowGuid128.Empty);        // BnetAccountGUID
        w.WriteFloat(0.0f);        // LeaveScore
        w.WriteUInt32(0);        // SeasonID
        w.WriteUInt32(0);        // TotalLeaves
        w.WriteUInt32(0);        // TotalSuccesses
        w.WriteInt32(0);        // ConsecutiveSuccesses
        w.WriteInt64(0);        // LastPenaltyTime
        w.WriteInt64(0);        // LeaverExpirationTime
        w.WriteInt32(0);        // Unknown_1120
        w.WriteBits(0, 1);        // LeaverStatus (client reads one whole byte, keeps bit 7)
        // [elements of PetNames go here: u32 CreatureID + u8 length + name bytes each]
        // [DeclinedNames go here only when HasDeclinedNames above is 1 - we send 0]
    }

    /// <summary>
    /// Written from the client's own ActivePlayerData reader at RVA 0x713E50, walked and
    /// sync-verified over all 6012 instructions by tools-256-spike/apd_walk.py. That reader consumes
    /// 6377 bytes for an empty payload where the previous body sent 5632 - the same over-read that
    /// PlayerData had (section 110).
    ///
    /// This reader differs from PlayerData's in one way that matters: it reads through
    /// value-returning wrappers, so the store to track is the *return* value's, not rdx's. Fields
    /// are still deduplicated by object store offset, which is what collapses the fused
    /// read-and-notify forks.
    ///
    /// Coinage is confirmed at +300 and the first divergence is at wire offset 3912, so the
    /// HERMES_256_APDPROBE sentinels were already aligned before this change.
    ///
    /// See REFERENCE-256-CLIENT.md section 117. Previous body kept verbatim at
    /// tools-256-spike/activeplayerdata_previous.cs.
    /// </summary>

    // Maps a modern 69110 InvSlots index to the corresponding legacy slot array. Ranges are the
    // same as V3_4_3's GetModernInvSlot and were VERIFIED byte-for-byte against the live 69110
    // ActivePlayer create (tools-256-spike/ap_rowine.bin): bag@30 = legacy[19], backpack@36/38-49
    // = PackSlots, bank@63-65 = BankSlots, all with Coinage landing exactly at +534. Returns null
    // for a modern slot with no legacy source (client reads Empty there).
    static WowGuid128? GetModern69110InvSlot(ActivePlayerData a, int modernIdx)
    {
        if (a == null) return null;
        if (modernIdx <= 18)
            return a.InvSlots != null && modernIdx < a.InvSlots.Length ? a.InvSlots[modernIdx] : null;
        if (modernIdx >= 30 && modernIdx <= 33)
        {
            int legacyIdx = 19 + (modernIdx - 30);
            return a.InvSlots != null && legacyIdx < a.InvSlots.Length ? a.InvSlots[legacyIdx] : null;
        }
        if (modernIdx >= 35 && modernIdx <= 58)
        {
            int idx = modernIdx - 35;
            return a.PackSlots != null && idx < a.PackSlots.Length ? a.PackSlots[idx] : null;
        }
        if (modernIdx >= 59 && modernIdx <= 86)
        {
            int idx = modernIdx - 59;
            return a.BankSlots != null && idx < a.BankSlots.Length ? a.BankSlots[idx] : null;
        }
        if (modernIdx >= 87 && modernIdx <= 93)
        {
            int idx = modernIdx - 87;
            return a.BankBagSlots != null && idx < a.BankBagSlots.Length ? a.BankBagSlots[idx] : null;
        }
        if (modernIdx >= 94 && modernIdx <= 105)
        {
            int idx = modernIdx - 94;
            return a.BuyBackSlots != null && idx < a.BuyBackSlots.Length ? a.BuyBackSlots[idx] : null;
        }
        if (modernIdx >= 106 && modernIdx <= 137)
        {
            int idx = modernIdx - 106;
            return a.KeyringSlots != null && idx < a.KeyringSlots.Length ? a.KeyringSlots[idx] : null;
        }
        return null;
    }

    void WriteActivePlayerData(WorldPacket w)
    {
        var apd = m_updateData.ActivePlayerData;
        for (int i0 = 0; i0 < 146; ++i0)
        {
            // Full slot mapping, VERIFIED against the live 69110 ActivePlayer create
            // (tools-256-spike/ap_rowine.bin, APD@3640, Coinage 737 lands exactly). The 146-slot
            // 69110 layout uses the SAME index ranges as V3_4_3's GetModernInvSlot(141): equipment
            // 0-18, bags 30-33, backpack 35-58, bank 59-86, bankbag 87-93, buyback 94-105, keyring
            // 106-137. Rowine confirms bag@30 (legacy 19), backpack@36/38-49 (PackSlots), bank@63-65
            // - no reagent-bag shift below slot 137, so the extra 5 slots sit past the keyring and do
            // not touch these ranges. The old writer emitted legacy bags at modern 19-22 (an unused
            // region on this build) and never emitted the backpack at all - that is why bags did not
            // open and carried items were missing. Still gated behind HERMES_256_INVSLOTS; default
            // off writes all-Empty exactly as before.
            w.WritePackedGuid128(s_invSlots
            ? (GetModern69110InvSlot(apd, i0) ?? WowGuid128.Empty)
            : WowGuid128.Empty);        // InvSlots (count = client global 0x33EFE64 = 146)
        }
        // FarsightObject kept Empty DELIBERATELY: the handler fills it from PLAYER_FARSIGHT,
        // but a non-empty packed guid emits more bytes than an empty one (length-variable) and
        // the guid would point at an object this build is not yet sent. Same reasoning below
        // for ComboTarget (handler-filled from the legacy combo target).
        w.WritePackedGuid128(WowGuid128.Empty);        // FarsightObject (obj+0x0, see note above)
        w.WritePackedGuid128(WowGuid128.Empty);        // SummonedBattlePetGUID (obj+0x10)
        w.WriteUInt32(0);        // KnownTitles size - kept 0 DELIBERATELY: the handler fills 12 legacy u32 title-mask words, but emitting u64 elements would grow the block; wire count and elements together in a follow-up
        w.WriteUInt64(s_apdProbe ? 7UL
                       : s_money != 0 ? s_money
                       : (ulong)(m_updateData.ActivePlayerData?.Coinage ?? 0));        // Coinage (obj+0x58, wire +300 - CONFIRMED)
        w.WriteUInt64(0);        // AccountBankCoinage (obj+0x60)
        w.WriteInt32(s_apdProbe ? 13 : (int)(m_updateData.ActivePlayerData?.XP ?? 0));        // XP (obj+0x68, wire +316)
        w.WriteInt32(s_apdProbe ? 19 : (int)(m_updateData.ActivePlayerData?.NextLevelXP ?? 400));        // NextLevelXP (obj+0x6C, wire +320)
        w.WriteInt32(0);        // TrialXP (obj+0x70)
        // >>> Skill - sub-reader 0x710620: 300 slots, NOT 256 (cmp r15d, 0x12C; object
        // stride 0x258 = 300 u16 per sub-array). 7 u16 fields interleaved per slot.
        for (int i0 = 0; i0 < 300; ++i0)
        {
            // The handler fills ActivePlayerData.Skill from the legacy PLAYER_SKILL_INFO
            // triplets; writing zeros here is why the skill pane knew nothing. 300 slots on
            // the wire, 256 in our model - every write is index-guarded. SkillStartingRank
            // has no legacy source and stays zero through the null model field.
            var sk = i0 < 256 ? apd?.Skill : null;
            w.WriteUInt16(sk?.SkillLineID[i0] ?? 0);        // SkillLineID       (block+0x000)
            w.WriteUInt16(sk?.SkillStep[i0] ?? 0);        // SkillStep         (block+0x258)
            w.WriteUInt16(sk?.SkillRank[i0] ?? 0);        // SkillRank         (block+0x4B0)
            w.WriteUInt16(sk?.SkillStartingRank[i0] ?? 0);        // SkillStartingRank (block+0x708)
            w.WriteUInt16(sk?.SkillMaxRank[i0] ?? 0);        // SkillMaxRank      (block+0x960)
            w.WriteInt16(sk?.SkillTempBonus[i0] ?? 0);         // SkillTempBonus    (block+0xBB8)
            w.WriteUInt16(sk?.SkillPermBonus[i0] ?? 0);        // SkillPermBonus    (block+0xE10)
        }
        w.WriteInt32(apd?.CharacterPoints ?? 0);        // CharacterPoints (obj+0x10DC) - unspent talent points
        w.WriteInt32(0);        // MaxTalentTiers (obj+0x10E0)
        w.WriteUInt32(apd?.TrackCreatureMask ?? 0);        // TrackCreatureMask (obj+0x10E4)
        // >>> TrackResourceMask[2] - 8 bytes this build's reader does not read. At obj+0x10E4 the
        // client goes straight from TrackCreatureMask to MainhandExpertise at obj+0x10E8: two
        // dword-adjacent fields with no room between them. Measured over real captured bytes, and
        // the reader has no 2-iteration loop anywhere. Same signature as UnitData.VirtualItems
        // (section 131), except these are over-sends to delete rather than fields to relocate.
        if (!s_apdDropArr || s_apdArr2)   // APDARR2: client DOES read TrackResourceMask (live sentinel)
        {
            for (int i0 = 0; i0 < 2; ++i0)
            {
                w.WriteUInt32(apd != null && i0 < apd.TrackResourceMask.Length ? apd.TrackResourceMask[i0] ?? 0 : 0);        // TrackResourceMask
            }
        }
        w.WriteFloat(apd?.MainhandExpertise ?? 0.0f);        // MainhandExpertise (obj+0x10E8)
        w.WriteFloat(apd?.OffhandExpertise ?? 0.0f);        // OffhandExpertise (obj+0x10EC)
        w.WriteFloat(0.0f);        // RangedExpertise (obj+0x10F0) - no legacy source (TBC has no ranged expertise field)
        w.WriteFloat(0.0f);        // CombatRatingExpertise (obj+0x10F4) - no legacy source
        w.WriteFloat(apd?.BlockPercentage ?? 0.0f);        // BlockPercentage (obj+0x10F8)
        w.WriteFloat(apd?.DodgePercentage ?? 0.0f);        // DodgePercentage (obj+0x10FC)
        w.WriteFloat(0.0f);        // DodgePercentageFromAttribute (obj+0x1100) - no legacy source
        w.WriteFloat(apd?.ParryPercentage ?? 0.0f);        // ParryPercentage (obj+0x1104)
        w.WriteFloat(0.0f);        // ParryPercentageFromAttribute (obj+0x1108) - no legacy source
        w.WriteFloat(apd?.CritPercentage ?? 0.0f);        // CritPercentage (obj+0x110C)
        w.WriteFloat(apd?.RangedCritPercentage ?? 0.0f);        // RangedCritPercentage (obj+0x1110)
        w.WriteFloat(apd?.OffhandCritPercentage ?? 0.0f);        // OffhandCritPercentage (obj+0x1114)
        for (int i0 = 0; i0 < 7; ++i0)
        {
            // count = client global 0x33EFE70 = 7, matching the model's 7 spell schools
            w.WriteFloat(apd != null && i0 < apd.SpellCritPercentage.Length ? apd.SpellCritPercentage[i0] ?? 0.0f : 0.0f);        // SpellCritPercentage
            w.WriteInt32(apd != null && i0 < apd.ModDamageDonePos.Length ? apd.ModDamageDonePos[i0] ?? 0 : 0);        // ModDamageDonePos
            w.WriteInt32(apd != null && i0 < apd.ModDamageDoneNeg.Length ? apd.ModDamageDoneNeg[i0] ?? 0 : 0);        // ModDamageDoneNeg
            w.WriteFloat(apd != null && i0 < apd.ModDamageDonePercent.Length ? apd.ModDamageDonePercent[i0] ?? 0.0f : 0.0f);        // ModDamageDonePercent
        }
        w.WriteInt32(s_apdProbe ? 0xB001 : (apd?.ShieldBlock ?? 0));        // ShieldBlock (obj+0x1118) [APDPROBE sentinel]
        w.WriteFloat(0.0f);        // ShieldBlockCritPercentage (obj+0x111C)
        w.WriteFloat(0.0f);        // Mastery (obj+0x1120)
        w.WriteFloat(0.0f);        // Speed (obj+0x1124)
        w.WriteFloat(0.0f);        // Avoidance (obj+0x1128)
        w.WriteFloat(0.0f);        // Sturdiness (obj+0x112C)
        w.WriteInt32(s_apdProbe ? 0xB002 : 0);        // Versatility (obj+0x1130) [APDPROBE sentinel]
        w.WriteFloat(0.0f);        // VersatilityBonus (obj+0x1134)
        w.WriteFloat(0.0f);        // PvpPowerDamage (obj+0x1138)
        w.WriteFloat(0.0f);        // PvpPowerHealing (obj+0x113C)
        // >>> BitVectors - sub-reader 0x70FB90 loops cmp r15d, 0xE = 14 entries, NOT 13.
        // Per entry: u32 count + count x u64 words (elements are u64, not u32).
        // Kept empty DELIBERATELY even though the handler fills ActivePlayerData.ExploredZones
        // (the modern client carries explored zones in one of these vectors): emitting words
        // would grow the block, and which of the 14 entries is ExploredZones is unverified.
        // Consequence: the map shows no explored zones. Report, do not guess.
        for (int i0 = 0; i0 < 14; ++i0)
        {
            w.WriteUInt32(0);        // BitVectors[i].Values size
        }
        w.WriteUInt32(0);        // CharacterDataElements size (obj+0x1450, elements deferred)
        w.WriteUInt32(0);        // AccountDataElements size (obj+0x1488, elements deferred)
        // >>> RestInfo[2] - 10 bytes the reader does not read: it goes from AccountDataElements
        // straight to ModHealingDonePos at obj+0x14C0. Element reader 0x712440 exists, but nothing
        // in 0x713E50 calls it over this block.
        if (!s_apdDropArr || s_apdArr2)   // APDARR2: client DOES read RestInfo (live sentinel: -18 = TrackResource+RestInfo)
        {
            for (int i0 = 0; i0 < 2; ++i0)
            {
                var ri = apd != null && i0 < apd.RestInfo.Length ? apd.RestInfo[i0] : null;
                w.WriteUInt32(ri?.Threshold ?? 0);        // Threshold
                w.WriteUInt8((byte)(ri?.StateID ?? 0));        // StateID
            }
        }
        w.WriteInt32(apd?.ModHealingDonePos ?? 0);        // ModHealingDonePos (obj+0x14C0)
        w.WriteFloat(0.0f);        // ModHealingPercent (obj+0x14C4)
        w.WriteFloat(0.0f);        // ModHealingDonePercent (obj+0x14C8)
        w.WriteFloat(0.0f);        // ModPeriodicHealingDonePercent (obj+0x14CC)
        for (int i0 = 0; i0 < 3; ++i0)
        {
            // count = client global 0x3086BD0 = 3
            w.WriteFloat(0.0f);        // WeaponDmgMultipliers
            w.WriteFloat(0.0f);        // WeaponAtkSpeedMultipliers
        }
        w.WriteFloat(0.0f);        // ModSpellPowerPercent (obj+0x14D0)
        w.WriteFloat(0.0f);        // ModResiliencePercent (obj+0x14D4)
        w.WriteFloat(0.0f);        // OverrideSpellPowerByAPPercent (obj+0x14D8)
        w.WriteFloat(0.0f);        // OverrideAPBySpellPowerPercent (obj+0x14DC)
        w.WriteInt32(apd?.ModTargetResistance ?? 0);        // ModTargetResistance (obj+0x14E0)
        w.WriteInt32(apd?.ModTargetPhysicalResistance ?? 0);        // ModTargetPhysicalResistance (obj+0x14E4)
        w.WriteUInt32(s_apdProbe ? 0xB003u : (apd?.LocalFlags ?? 0));        // LocalFlags [APDPROBE sentinel]
        w.WriteUInt8(apd?.GrantableLevels ?? 0);        // GrantableLevels (obj+0x14EC)
        w.WriteUInt8(apd?.MultiActionBars ?? 0);        // MultiActionBars (obj+0x14ED)
        w.WriteUInt8(apd?.LifetimeMaxRank ?? 0);        // LifetimeMaxRank (obj+0x14EE)
        w.WriteUInt8(0);        // NumRespecs (obj+0x14EF) - no legacy source
        w.WriteInt32((int)(apd?.AmmoID ?? 0));        // AmmoID (obj+0x14F0)
        w.WriteUInt32(s_apdProbe ? 0xB004u : (apd?.PvpMedals ?? 0));        // PvpMedals (obj+0x14F4) [APDPROBE sentinel]
        for (int i0 = 0; i0 < 12; ++i0)
        {
            // count = client global 0x33EFE60 = 12, matching the model's 12 buyback slots
            w.WriteUInt32(apd != null && i0 < apd.BuybackPrice.Length ? apd.BuybackPrice[i0] ?? 0 : 0);        // BuybackPrice
            w.WriteInt64(apd != null && i0 < apd.BuybackTimestamp.Length ? apd.BuybackTimestamp[i0] ?? 0 : 0);        // BuybackTimestamp (u32 in the legacy field, i64 on the wire)
        }
        w.WriteUInt16(apd?.TodayHonorableKills ?? 0);        // TodayHonorableKills (obj+0x14F8)
        w.WriteUInt16(apd?.TodayDishonorableKills ?? 0);        // TodayDishonorableKills (obj+0x14FA)
        w.WriteUInt16(apd?.YesterdayHonorableKills ?? 0);        // YesterdayHonorableKills (obj+0x14FC)
        w.WriteUInt16(apd?.YesterdayDishonorableKills ?? 0);        // YesterdayDishonorableKills (obj+0x14FE)
        w.WriteUInt16(apd?.LastWeekHonorableKills ?? 0);        // LastWeekHonorableKills (obj+0x1500)
        w.WriteUInt16(apd?.LastWeekDishonorableKills ?? 0);        // LastWeekDishonorableKills (obj+0x1502)
        w.WriteUInt16(apd?.ThisWeekHonorableKills ?? 0);        // ThisWeekHonorableKills (obj+0x1504)
        w.WriteUInt16(apd?.ThisWeekDishonorableKills ?? 0);        // ThisWeekDishonorableKills (obj+0x1506)
        w.WriteUInt32(apd?.ThisWeekContribution ?? 0);        // ThisWeekContribution (obj+0x1508)
        w.WriteUInt32(apd?.LifetimeHonorableKills ?? 0);        // LifetimeHonorableKills (obj+0x150C)
        w.WriteUInt32(apd?.LifetimeDishonorableKills ?? 0);        // LifetimeDishonorableKills (obj+0x1510)
        w.WriteUInt32(0);        // Field_F24 (obj+0x1514)
        w.WriteUInt32(apd?.YesterdayContribution ?? 0);        // YesterdayContribution (obj+0x1518)
        w.WriteUInt32(apd?.LastWeekContribution ?? 0);        // LastWeekContribution (obj+0x151C)
        w.WriteUInt32(apd?.LastWeekRank ?? 0);        // LastWeekRank (obj+0x1520)
        // -1, not 0. Zero names a real slot in the client's reputation list and switches the XP bar
        // to that faction's standing; -1 is the documented "none" sentinel and is what every other
        // version writer here uses (V3_4_3_54261/ObjectUpdateBuilder.cs:866). Length-neutral.
        w.WriteInt32(s_watchedFactionNone ? -1 : (apd?.WatchedFactionIndex ?? -1));        // WatchedFactionIndex (obj+0x1524) - legacy rep INDEX; HERMES_256_WATCHEDFACTION forces -1 (none) so the XP bar shows XP, not a mis-resolved faction
        // >>> CombatRatings[32] - 128 bytes, and the costly one. The client goes from
        // WatchedFactionIndex at obj+0x1524 to MaxLevel at obj+0x1528, dword-adjacent, so MaxLevel
        // reads our CombatRatings[0]. This is what puts a faction on the XP bar: WatchedFactionIndex
        // is itself displaced 18 bytes by the two arrays above, and -1 never reaches the field.
        // The reader has no 32-iteration loop over this block.
        if (!s_apdDropArr)
        {
            for (int i0 = 0; i0 < 32; ++i0)
            {
                w.WriteInt32(apd != null && i0 < apd.CombatRatings.Length ? apd.CombatRatings[i0] ?? 0 : 0);        // CombatRatings
            }
        }
        // MaxLevel 0 makes the client treat the character as max-level and HIDE the XP bar. TBC 2.4.3
        // has no MaxLevel field (apd.MaxLevel is null -> 0), so HERMES_256_MAXLEVEL70 writes 70 (the
        // TBC/Anniversary cap) instead. Stored at obj+0x1528, not resolved against any table, so it is
        // safe. Length-neutral.
        w.WriteInt32(s_apdProbe ? 0xB005 : (apd?.MaxLevel ?? (s_maxLevel70 ? 70 : 0)));        // MaxLevel (obj+0x1528) [APDPROBE sentinel; MAXLEVEL70 -> 70 so the XP bar shows]
        w.WriteInt32(0);        // ScalingPlayerLevelDelta (obj+0x152C)
        w.WriteInt32(0);        // MaxCreatureScalingLevel (obj+0x1530)
        w.WriteUInt8(0);        // NEW on this build: u8 at obj+0x1534 (RVA 0x71663B), name unknown
        for (int i0 = 0; i0 < 4; ++i0)
        {
            w.WriteUInt32(0);        // NoReagentCostMask (count = client global 0x33EFE68 = 4)
        }
        w.WriteInt32(0);        // PetSpellPower (obj+0x1538)
        for (int i0 = 0; i0 < 2; ++i0)
        {
            w.WriteInt32(0);        // ProfessionSkillLine (count = client global 0x33E3070 = 2)
        }
        w.WriteFloat(0.0f);        // UiHitModifier (obj+0x153C)
        w.WriteFloat(0.0f);        // UiSpellHitModifier (obj+0x1540)
        w.WriteInt32(0);        // HomeRealmTimeOffset (obj+0x1544)
        w.WriteFloat(0.0f);        // ModPetHaste (obj+0x1548)
        w.WriteUInt8(0);        // LocalRegenFlags (obj+0x154C)
        w.WriteUInt8(apd?.AuraVision ?? 0);        // AuraVision (obj+0x154D)
        w.WriteUInt8(16);        // NumBackpackSlots (obj+0x154E)
        w.WriteInt32(0);        // OverrideSpellsID (obj+0x1550)
        w.WriteInt32(0);        // LfgBonusFactionID (obj+0x1554)
        w.WriteUInt16(0);        // LootSpecID (obj+0x1558)
        w.WriteUInt32(0);        // OverrideZonePVPType (obj+0x155C)
        for (int i0 = 0; i0 < 4; ++i0)
        {
            w.WriteUInt32(0);        // BagSlotFlags (count = client global 0x33EFE5C = 4)
        }
        for (int i0 = 0; i0 < 7; ++i0)
        {
            w.WriteUInt32(0);        // BankBagSlotFlags - SEVEN on this build (global 0x33EFE58 = 7), not 6
        }
        w.WriteInt32(0);        // Honor (obj+0x1560)
        w.WriteInt32(0);        // HonorNextLevel (obj+0x1564)
        w.WriteInt32(0);        // Field_F74 (obj+0x1568)
        w.WriteUInt8(0);        // Field_1261 (obj+0x156D)
        w.WriteInt32(0);        // PvpTierMaxFromWins (obj+0x1570)
        w.WriteInt32(0);        // PvpLastWeeksTierMaxFromWins (obj+0x1574)
        w.WriteUInt8(apd?.PvPRankProgress ?? 0);        // PvpRankProgress (obj+0x1579)
        w.WriteInt32(0);        // PerksProgramCurrency (obj+0x157C)
        // >>> Research block: 3 counts, elements inline right after (u16 / u32 / u16 loops
        // over the resized arrays - zero-length for us)
        w.WriteUInt32(0);        // ResearchSites size
        w.WriteUInt32(0);        // ResearchSiteProgress size
        w.WriteUInt32(0);        // Research size
        // [elements of the three research arrays go here - empty]
        //
        // >>> Dynamic-array count chain: TWENTY-FOUR u32 counts on this build (writer had 14
        // + a separate TraitConfigs count). Names for the first 14 follow WPP's V2_5_5 order;
        // the mapping of the remaining 10 to names is unverified (shapes are known - see
        // header comment - but which name goes with which offset is not).
        w.WriteUInt32(0);        // #1  obj+0x1580  u32 elems   (DailyQuestsCompleted?)
        w.WriteUInt32(0);        // #2  obj+0x15B8  u32 elems   (Field_1000?)
        w.WriteUInt32(0);        // #3  obj+0x15F0  u32 elems   (AvailableQuestLineXQuestIDs?)
        w.WriteUInt32(0);        // #4  obj+0x1628  u32 elems   (Heirlooms?)
        w.WriteUInt32(0);        // #5  obj+0x1660  u32 elems   (HeirloomFlags?)
        w.WriteUInt32(0);        // #6  obj+0x1698  u32 elems   (Toys?)
        w.WriteUInt32(0);        // #7  obj+0x16D0  u32 elems   (Transmog?)
        w.WriteUInt32(0);        // #8  obj+0x1708  u32 elems   (ConditionalTransmog?)
        w.WriteUInt32(0);        // #9  obj+0x1740  u32 elems   (SelfResSpells? - the handler fills ActivePlayerData.SelfResSpells, but the slot name is unverified and elements would grow the block; kept 0 DELIBERATELY)
        w.WriteUInt32(0);        // #10 obj+0x1778  u32 elems   (WarbandScenes?)
        w.WriteUInt32(0);        // #11 obj+0x17B0  u32 elems
        w.WriteUInt32(0);        // #12 obj+0x17E8  u32 elems
        w.WriteUInt32(0);        // #13 obj+0x1820  u32 elems
        w.WriteUInt32(0);        // #14 obj+0x1858  u32 elems
        w.WriteUInt32(0);        // #15 obj+0x1890  u32 elems
        w.WriteUInt32(0);        // #16 obj+0x18C8  u32 elems
        w.WriteUInt32(0);        // #17 obj+0x1900  u32 elems   (CharacterRestrictions?)
        w.WriteUInt32(0);        // #18 obj+0x1938  25-byte wire elems u32,u32,u32,u8,u32,u32,u32 (TraitConfigs? read at END)
        w.WriteUInt32(0);        // #19 obj+0x1970  12-byte elems u32,u32,u32 (SpellPctModByLabel?)
        w.WriteUInt32(0);        // #20 obj+0x19A8  12-byte elems (SpellFlatModByLabel?)
        w.WriteUInt32(0);        // #21 obj+0x19E0  12-byte elems
        w.WriteUInt32(0);        // #22 obj+0x1A18  12-byte elems
        w.WriteUInt32(0);        // #23 obj+0x1AC0  struct elems via 0x742110 (TaskQuests shape)
        w.WriteUInt32(0);        // #24 obj+0x1AF8  u32 elems
        w.WriteInt32(0);        // TimerunningSeasonID (obj+0x1B30)
        w.WriteInt32(0);        // TransportServerTime (obj+0x1B34)
        w.WriteUInt32(0);        // ActiveCombatTraitConfigID (obj+0x1B78) - NO TraitConfigs count here
        for (int i0 = 0; i0 < 9; ++i0)
        {
            // imm 9 loop, two u32 per iteration
            w.WriteUInt32(0);        // GlyphSlots
            w.WriteUInt32(0);        // Glyphs
        }
        w.WriteUInt16(0);        // GlyphsEnabled (obj+0x1558 region)
        w.WriteUInt8(0);        // LfgRoles
        w.WriteUInt32(0);        // CategoryCooldownMods size (obj+0x1B80, elems u32+u32 deferred)
        w.WriteUInt32(0);        // WeeklySpellUses size (obj+0x1BB8, elems u32+u8 deferred)
        w.WriteUInt8(0);        // NumStableSlots
        for (int i0 = 0; i0 < 13; ++i0)
        {
            w.WriteUInt64(0);        // Field_4348 (imm 13 loop of u64)
        }
        w.WriteInt32(0);        // Field_17B8
        // ---- deferred dynamic-array elements: KnownTitles (u64 each), CharacterDataElements
        // (u32,u32,u64 each), AccountDataElements, arrays #1..#17, #19..#24, then
        // CategoryCooldownMods and WeeklySpellUses elements. All counts are 0 -> no bytes. ----
        //
        // >>> PvpInfo[9] - element reader 0x712000: u8 + 16 x u32 + ONE WHOLE trailing byte
        // for the Disqualified bit. 66 bytes per element. The old per-iteration WriteBit
        // never flushed and smeared bits across elements - emit the byte explicitly.
        for (int i0 = 0; i0 < 9; ++i0)
        {
            // The handler fills ActivePlayerData.PvpInfo[team] from the legacy arena team
            // fields (WeeklyPlayed/SeasonPlayed/SeasonWon/Rating). 9 wire slots, 6 in our
            // model - guarded. Bracket mirrors the legacy team index only when the element
            // carries data, so empty elements stay byte-identical to before.
            var pi = apd != null && i0 < apd.PvpInfo.Length ? apd.PvpInfo[i0] : null;
            w.WriteUInt8(pi != null ? (byte)i0 : (byte)0);        // Bracket
            w.WriteInt32(0);        // PvpRatingID - no legacy source
            w.WriteUInt32(pi?.WeeklyPlayed ?? 0);        // WeeklyPlayed
            w.WriteUInt32(pi?.WeeklyWon ?? 0);        // WeeklyWon
            w.WriteUInt32(pi?.SeasonPlayed ?? 0);        // SeasonPlayed
            w.WriteUInt32(pi?.SeasonWon ?? 0);        // SeasonWon
            w.WriteUInt32(pi?.Rating ?? 0);        // Rating
            w.WriteUInt32(pi?.WeeklyBestRating ?? 0);        // WeeklyBestRating
            w.WriteUInt32(pi?.SeasonBestRating ?? 0);        // SeasonBestRating
            w.WriteUInt32(pi?.PvpTierID ?? 0);        // PvpTierID
            w.WriteUInt32(pi?.WeeklyBestWinPvpTierID ?? 0);        // WeeklyBestWinPvpTierID
            w.WriteUInt32(pi?.Field_28 ?? 0);        // Field_28
            w.WriteUInt32(pi?.Field_2C ?? 0);        // Field_2C
            w.WriteUInt32(0);        // WeeklyRoundsPlayed - no legacy source
            w.WriteUInt32(0);        // WeeklyRoundsWon - no legacy source
            w.WriteUInt32(0);        // SeasonRoundsPlayed - no legacy source
            w.WriteUInt32(0);        // SeasonRoundsWon - no legacy source
            w.WriteUInt8((byte)(pi is { Disqualified: true } ? 0x80 : 0));        // Disqualified bits byte (client reads a full u8 per element, keeps bit 7)
        }
        // >>> trailing bits byte - 6 bits, MSB first, one byte total (client creates a fresh
        // bit context here; ReadBit x3 then ReadBits(3))
        w.WriteBit(false);        // SortBagsRightToLeft (bit 7, stored obj+0x156C)
        w.WriteBit(false);        // InsertItemsLeftToRight (bit 6, stored obj+0x1578)
        w.WriteBit(false);        // has optional TraitConfig-like struct at obj+0x1BF0 (bit 5) - MUST be 0 or the client reads a 0x737820 struct at the end
        w.WriteBits(0, 3);        // bank-tab-settings ELEMENT COUNT (bits 4..2) - each element is a 0x214C-byte struct read at the end; MUST be 0
        w.FlushBits();
        // >>> ResearchHistory (reader 0x706A10): u32 count + count x {u64, u32, u32}
        w.WriteUInt32(0);        // ResearchHistory.CompletedProjects size
        // >>> FrozenPerksVendorItem (reader 0x66F9D0) - UNCONDITIONAL 49 bytes, was missing
        w.WriteUInt64(0);        // PerksVendorItem u64 (AvailableUntil?)
        for (int i0 = 0; i0 < 10; ++i0)
        {
            w.WriteUInt32(0);        // PerksVendorItem u32 fields
        }
        w.WriteUInt8(0);        // PerksVendorItem trailing u8 (Disabled bits byte)
        // >>> DungeonScore (reader 0x66DB00): u32 SeasonCount + u32 - 8 bytes empty,
        // NOT f32/f32/u32, and no trailing "?" u32
        w.WriteUInt32(0);        // DungeonScore.Seasons size
        w.WriteUInt32(0);        // DungeonScore u32 (stored obj+0x1B38+0x18; TotalRuns?)
        // >>> map at obj+0x1B58 (reader 0x729AA0): u32 count + count x {u32 key, struct 0x667A00}
        w.WriteUInt32(0);        // map obj+0x1B58 size
        // >>> map at obj+0x1C40 (reader 0x72A2B0/0x72E070): u32 count + count x {u32 key, struct 0x669D40}
        w.WriteUInt32(0);        // map obj+0x1C40 size
        // >>> ONE unconditional struct 0x669D40 at obj+0x1C60 - 23 bytes empty
        w.WriteUInt32(0);        // 0x1C60 head u32
        w.WriteUInt32(0);        // 0x1C60 count (drives 16-byte-elem loop)
        w.WriteUInt32(0);        // 0x1C60 count (drives 17-byte-elem loop via 0x669770)
        w.WriteUInt32(0);        // 0x1C60 count (drives 0x5A74D0 u32x4 loop)
        w.WriteUInt8(0);        // 0x1C60 tail u8 (0x669840)
        w.WriteUInt32(0);        // 0x1C60 tail u32
        w.WriteUInt8(0);        // 0x1C60 tail string length - 0, so no string bytes follow
        w.WriteUInt8(0);        // 0x1C60 tail flags u8 (top bit kept)
        // >>> ONE fixed struct 0x6696B0 at obj+0x1D78 - 12 bytes
        w.WriteUInt8(0);
        w.WriteUInt32(0);
        w.WriteUInt8(0);
        w.WriteUInt8(0);
        w.WriteUInt32(0);
        w.WriteUInt8(0);
        // ---- end of block. The comment that used to stand here said "remaining client reads
        // are all zero-length for us". MEASURED 23 Aug, and it is not true of the reader's own
        // walk: streamwalk over the REAL captured bytes of two different sessions consumes
        // 6236 in both, while this writer emits 6231. The last two reads in the client field
        // map are a u32 at wire +6230 and a PACKED GUID at +6234 - and we stop at +6230, so the
        // u32 takes three bytes of stale heap and the guid takes its two mask bytes from stale
        // heap as well. A packed-guid mask read out of garbage is length-variable, which is the
        // one shape rule 2 says is fatal rather than merely wrong.
        //
        // WITHDRAWN, same day, and the withdrawal is the useful part. Those last two reads are
        // NOT taken: they belong to the optional 0x737820 struct at obj+0x1BF0, and that arm is
        // gated by the very bit this writer emits as 0 a few bytes earlier:
        //
        //     0x71AB0F  CALL 0x2D83880        read one bit  <- our bit
        //     0x71AB1D  SETNZ DL
        //     0x71AB20  CALL 0x72E190         optional-manager(obj+0x1BF0, present = DL)
        //     0x71AB25  TEST AL,AL
        //     0x71AB27  JZ 0x71AB95           skips the struct when the bit is 0
        //
        // So the block really does end at 6231, the "-6" correction in model-256.md is right,
        // and this writer is NOT short. streamwalk and clientfields both walk that arm only
        // because neither can evaluate a gate whose input is a bit we choose.
        //
        // It follows that the 23 Aug 03:46 world-entry freeze is NOT explained by a create-path
        // over-read. The client hung in a hash-map deserialiser (0x72E070) whose element count is
        // a u32 read at its entry (0x72E096), reached from this reader at 0x71AE6F for the
        // container at obj+0x1C40 - and this writer emits that count, as zero, at wire +6191.
        // Both the good and the frozen session's captured blocks are byte-identical in structure
        // and the client's own reader consumes 6236 over both. The cause is still open.
        //
        // HERMES_256_APDTAIL=1 appends the five bytes that would take the block from 6231 to the
        // 6236 streamwalk walks. On the evidence above it is INERT - the client does not read
        // them - so it is kept only as the one-knob test should the gate's "-5" ever be doubted
        // again, and it stays OFF. Lengthening the last block on the wire cannot break anything
        // (the client resynchronises on the declared size), but nor does it fix anything here.
        if (s_apdTail)
        {
            w.WriteUInt8(0);   // bytes 1..3 of the u32 the reader starts at wire +6230
            w.WriteUInt8(0);
            w.WriteUInt8(0);
            w.WriteUInt8(0);   // the 2-byte mask of the packed guid at wire +6234, i.e. empty
            w.WriteUInt8(0);
        }
        // Freeze mitigation: pad the last block so the APD reader's ~44-byte over-read (minidump,
        // 24 Aug) lands on in-buffer zeros instead of heap - every tail count then reads 0 and the
        // obj+0x1B58 resize loop is skipped. Default off (s_apdPad == 0) = current behaviour. See s_apdPad.
        if (s_apdPad > 0)
        {
            for (int i0 = 0; i0 < s_apdPad; ++i0)
                w.WriteUInt8(0);
        }
    }

    // The handler fills GameObjectData (DisplayID, Flags, State, TypeID, FactionTemplate, Level,
    // ArtKit, ParentRotation for the special transports) and this writer discarded all of it -
    // the same bug class as QuestLog/VisibleItems/NpcFlags, invisible to zeroaudit.py because
    // its handler regex only knows the four player-side block names. DisplayID 0 is why no
    // gameobject could render as itself.
    void WriteGameObjectData(WorldPacket w)
    {
        var go = m_updateData.GameObjectData;
        // >>> DisplayID. Measured, 22 Aug: a non-zero value here kills the client at world entry
        // with an access violation at rva 0x047C318 - `movzx eax, byte ptr [rax+rdx]` with the sum
        // 2, i.e. a lookup that returned nothing being indexed anyway. The id comes from a 2.4.3
        // core and this build resolves it against its own GameObjectDisplayInfo; when there is no
        // record, the client does not check.
        //
        // Isolated by bisection against a packet capture from a session that worked: blanking the
        // whole 99-byte GameObjectData stopped the crash, blanking these four bytes alone stopped
        // it too, and everything else the same wiring pass changed - the entire ActivePlayerData
        // skill block included - was exonerated by restoring the create byte-for-byte.
        //
        // Zero is what shipped before the wiring and is safe: gameobjects render with no model,
        // which is visibly wrong and survivable. The real fix is to emit a display id this client
        // can resolve, either by translating the legacy id or by generating the hotfix record for
        // it the way GameData already does for ItemAppearance. Sending the raw id is safe once the state-anim field
        // carries 1860 - see SpawnTrackingStateAnimID below. HERMES_256_NOGOBDISPLAY=1 forces zero.
        w.WriteUInt32(s_noGobDisplay ? 0 : (uint)(go?.DisplayID ?? 0));        // DisplayID
        w.WriteUInt32(go?.SpellVisualID ?? 0);        // SpellVisualID
        w.WriteUInt32(go?.StateSpellVisualID ?? 0);        // StateSpellVisualID
        // >>> SpawnTrackingStateAnimID. 1860 is this build's "no state animation" value - the model
        // code compares this field against 0x744 at nine sites and skips the whole state-AnimKit
        // path when it matches. TrinityCore sets both this and UnitData.StateAnimID from
        // DB2Mgr.GetEmptyAnimStateID() for exactly that reason; we sent 0, which selects the
        // default AnimKit 5102, whose segment asks for animation id 1672 - and AnimationData on
        // this build is the TBC subset, ids 0..801. The lookup returns null and the client indexes
        // it anyway: GetU8Field(record=null, field=1) reads byte [0+2], the access violation at
        // address 2 (section 129).
        //
        // Hypothesis, not established: the field at [owner+0x194] was never proven to be this one,
        // and world units run with StateAnimID = 0 without crashing. Confirmed live 22 Aug 23:27:
        // with 1860 here the client enters the world with a non-zero DisplayID, which reliably
        // crashed it before. HERMES_256_ANIMRAW=1 sends the legacy value again.
        w.WriteUInt32(s_animRaw ? (go?.StateAnimID ?? 0) : 1860u);        // SpawnTrackingStateAnimID (model name: StateAnimID)
        w.WriteUInt32(go?.StateAnimKitID ?? 0);        // SpawnTrackingStateAnimKitID (model name: StateAnimKitID)
        w.WriteUInt32(0);        // stateWorldEffectIDs count (obj+0x18) - kept 0: dynamic array, elements would grow the block; no legacy source fills the model array anyway
        // >>> StateWorldEffectsQuestObjectiveID does NOT exist on this build. The reader goes
        // straight from the vector count at wire +20 to the two packed guids at +24 and +26 with
        // no u32 between them (clientfields-GameObjectData.json entries 5/6/7; 553 has no such
        // field either - it is an 11.x addition). This u32 is what puts every later field two
        // positions out. HERMES_256_GOBFIX=1 removes it; see the knob's note.
        if (!s_gobFix)
        {
            w.WriteUInt32(0);        // StateWorldEffectsQuestObjectiveID - not read on 69110
        }
        // CreatedBy and GuildGUID kept Empty DELIBERATELY: handler fills CreatedBy, but a
        // non-empty packed guid is length-variable; report, do not wire, per the current rules.
        w.WritePackedGuid128(WowGuid128.Empty);        // CreatedBy (see note above) obj+0x30
        w.WritePackedGuid128(WowGuid128.Empty);        // GuildGUID (no legacy source) obj+0x40
        w.WriteUInt32(go?.Flags ?? 0);        // Flags obj+0x50
        w.WriteUInt32(0);        // FlagsB obj+0x54 - no legacy counterpart
        // ParentRotation: handler fills it only for the special transports (Deeprun Tram,
        // Zangarmarsh elevator); identity quaternion (0,0,0,1) is the neutral value otherwise.
        w.WriteFloat(go?.ParentRotation[0] ?? 0.0f);        // ParentRotationx obj+0x58
        w.WriteFloat(go?.ParentRotation[1] ?? 0.0f);        // ParentRotationy obj+0x5C
        w.WriteFloat(go?.ParentRotation[2] ?? 0.0f);        // ParentRotationz obj+0x60
        w.WriteFloat(go?.ParentRotation[3] ?? 1.0f);        // ParentRotationw obj+0x64
        w.WriteUInt32((uint)(go?.FactionTemplate ?? 0));        // FactionTemplate obj+0x68
        // >>> Level belongs HERE, between FactionTemplate and State - the reader's run between
        // GuildGUID and the byte triple is eight dwords (obj+0x50..0x6C) where the writer has
        // seven, and 553 reads Level in exactly this position. Where we currently put it, after
        // CustomParam, the client reads it as the WorldEffects vector COUNT.
        if (s_gobFix)
        {
            w.WriteUInt32((uint)(go?.Level ?? 0));        // Level obj+0x6C
        }
        w.WriteUInt8((byte)(go?.State ?? 0));        // State obj+0x70
        w.WriteUInt8((byte)(go?.TypeID ?? 0));        // TypeID obj+0x71
        w.WriteUInt8(go?.PercentHealth ?? 0);        // PercentHealth obj+0x72 - no legacy source (destructible buildings do not exist on this core)
        w.WriteUInt32(go?.ArtKit ?? 0);        // ArtKit obj+0x74
        w.WriteUInt32(0);        // EnableDoodadSets count (obj+0x78) - dynamic, no legacy source
        w.WriteUInt32(go?.CustomParam ?? 0);        // CustomParam obj+0xB0
        // >>> The 20 bytes below are read by NOTHING on this build. Level moves up (see above);
        // AnimGroupInstance and the three UiWidgetItem fields are 11.x additions that neither 553
        // nor this reader has. The client stops at 80 bytes with the AssistActionData bit.
        if (!s_gobFix)
        {
            w.WriteUInt32((uint)(go?.Level ?? 0));        // Level - wrong position, read as the WorldEffects count
            w.WriteUInt32(0);        // AnimGroupInstance - not read on 69110
            w.WriteUInt32(0);        // UiWidgetItemID - not read on 69110
            w.WriteUInt32(0);        // UiWidgetItemQuality - not read on 69110
            w.WriteUInt32(0);        // UiWidgetItemCount - not read on 69110
        }
        w.WriteUInt32(0);        // WorldEffects count (obj+0xB8)
        w.WriteBits(0, 1);   // AssistActionData.has_value() obj+0xF0
        w.FlushBits();
    }

    // The handler fills ItemData completely (Owner, ContainedIn, Creator, GiftCreator,
    // StackCount, Duration, SpellCharges, Flags, Enchantment, PropertySeed, RandomProperty,
    // Durability, MaxDurability) and the generated writer discarded the lot - the same bug class
    // as QuestLog/VisibleItems/NpcFlags, invisible to zeroaudit.py because its handler regex only
    // knows the four player-side block names. The guids here are the point: the client resolves
    // InvSlots/ContainerData.Slots against these objects, so they must be real.
    //
    // REWRITTEN against the client's own ItemData create reader at RVA 0x709290 (the function on
    // the 22:08:52 SocketedGem crash stack), cross-confirmed field-for-field by WowPacketParser's
    // version-generated UpdateFieldsHandler1158 (the 5.5.x arm). Two facts the TC-master-generated
    // body had wrong, and one rule it ignored:
    //
    //  * The [Owner] groups are REAL GATES, not comments. The client tests the visibility flags
    //    byte we send at the head of the values body (reader: `and edi,1; je` at 0x709389,
    //    0x70989B, 0x7099FD, 0x709C99, 0x709E79) and SKIPS those reads when the bit is clear.
    //    We send GetFieldVisibility() there, which is None for items - so the old writer's
    //    unconditionally-emitted owner fields shifted every later read: the client's Gems count
    //    landed on [3 zero enchantment bytes + Durability's low byte] = 30<<24 = 0x1E000000,
    //    and 0x1E000000 gems x 40 bytes = the 20,132,659,200-byte BLZ_ALLOC in both crash
    //    reports. The writer now gates exactly where the reader gates, so it stays correct
    //    under either flags value.
    //  * This build still carries PropertySeed and RandomPropertiesID (ungated, stores
    //    obj+0x4C/+0x50) - TC master, which gen_uf.py generated from, dropped them. The handler
    //    fills both from the legacy random-property fields.
    //  * ItemBonusKey is a real 8-byte read (reader 0x6D2460: u32 ItemID + u32 BonusListIDs
    //    count + count x u32) - the generated body wrote nothing for it.
    //
    // Tail shapes, for when these stop being empty: ArtifactPowers element = u16,u8,u8;
    // SocketedGem element = u32 ItemID + 16 x u16 + u8 Context (37 wire bytes, 40 in memory);
    // ItemModList = one byte whose top 7 bits are the count (client reads `byte >> 1` at
    // 0x6D216D - WPP's ReadBits(6) is wrong here, the client wins), then u8+u32 per element.
    /// <summary>
    /// HERMES_256_ITEMZERO=1 restores the pre-wiring all-zero item body (the confirmed-clean
    /// 20:58 baseline: Empty guids, TC-master field order, 258-byte blocks). It is the escape
    /// hatch if the reshaped body below turns out wrong anywhere: no OOM, but item guids do not
    /// resolve and bags/equipment stay empty. Default off = the corrected body.
    /// </summary>
    static readonly bool s_itemZero =
        System.Environment.GetEnvironmentVariable("HERMES_256_ITEMZERO") == "1";

    void WriteItemDataZeroStub(WorldPacket w)
    {
        for (int g = 0; g < 4; ++g)
            w.WritePackedGuid128(WowGuid128.Empty);        // Owner, ContainedIn, Creator, GiftCreator
        for (int i = 0; i < 7; ++i)
            w.WriteUInt32(0);        // StackCount, Expiration, SpellCharges[5]
        w.WriteUInt32(0);        // DynamicFlags
        for (int i = 0; i < 13; ++i)
        {
            w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt16(0); w.WriteUInt16(0);        // Enchantment
        }
        w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(0);        // Durability, MaxDurability, CreatePlayedTime
        w.WriteUInt8(0);        // Context
        w.WriteUInt64(0);        // CreateTime
        w.WriteUInt64(0);        // ArtifactXP
        w.WriteUInt8(0);        // ItemAppearanceModID
        w.WriteUInt32(0);        // ArtifactPowers count
        w.WriteUInt32(0);        // Gems count
        w.WriteUInt32(0);        // ZoneFlags
        w.WriteUInt16(0);        // DEBUGItemLevel
        w.WriteBits(0, 7);
        w.FlushBits();
    }

    void WriteItemData(WorldPacket w)
    {
        if (s_itemZero)
        {
            WriteItemDataZeroStub(w);
            return;
        }
        var item = m_updateData.ItemData;
        bool owner = GetFieldVisibility().HasFlag(FieldVisibility.Owner);
        w.WritePackedGuid128(item?.Owner ?? WowGuid128.Empty);        // Owner
        w.WritePackedGuid128(item?.ContainedIn ?? WowGuid128.Empty);        // ContainedIn
        w.WritePackedGuid128(item?.Creator ?? WowGuid128.Empty);        // Creator
        w.WritePackedGuid128(item?.GiftCreator ?? WowGuid128.Empty);        // GiftCreator
        if (owner)
        {
            w.WriteUInt32(item?.StackCount ?? 0);        // StackCount
            w.WriteUInt32(item?.Duration ?? 0);        // Expiration - handler fills ItemData.Duration from ITEM_FIELD_DURATION
            for (int i = 0; i < 5; ++i)
            {
                // model is also 5 wide - guarded anyway
                w.WriteUInt32((uint)(item != null && i < item.SpellCharges.Length ? item.SpellCharges[i] ?? 0 : 0));        // SpellCharges[i]
            }
        }
        w.WriteUInt32(item?.Flags ?? 0);        // DynamicFlags - handler fills ItemData.Flags from ITEM_FIELD_FLAGS
        for (int i = 0; i < 13; ++i)
        {
            // >>> Enchantment : ItemEnchantment - handler fills 13 legacy enchantment slots;
            // model is also 13 wide, guarded. Unfilled slots are null and write zeros.
            // Ungated and 12 bytes per element on the client (loop 0x709620, trip 13).
            var ench = item != null && i < item.Enchantment.Length ? item.Enchantment[i] : null;
                w.WriteUInt32((uint)(ench?.ID ?? 0));        // ID
                w.WriteUInt32(ench?.Duration ?? 0);        // Duration
                w.WriteUInt16(ench?.Charges ?? 0);        // Charges
                w.WriteUInt16(ench?.Inactive ?? 0);        // Inactive
        }
        w.WriteUInt32(item?.PropertySeed ?? 0);        // PropertySeed (client obj+0x4C, ungated)
        w.WriteUInt32(item?.RandomProperty ?? 0);        // RandomPropertiesID (client obj+0x50, ungated)
        if (owner)
        {
            w.WriteUInt32(item?.Durability ?? 0);        // Durability
            w.WriteUInt32(item?.MaxDurability ?? 0);        // MaxDurability
        }
        w.WriteUInt32(item?.CreatePlayedTime ?? 0);        // CreatePlayedTime (ungated on this build, unlike TC master)
        w.WriteUInt8((byte)(item?.Context ?? 0));        // Context
        w.WriteUInt64(0);        // CreateTime - no legacy source
        if (owner)
        {
            w.WriteUInt64(item?.ArtifactXP ?? 0);        // ArtifactXP - retail machinery, model field never filled by a legacy handler
            w.WriteUInt8((byte)(item?.ItemAppearanceModID ?? 0));        // ItemAppearanceModID
        }
        w.WriteUInt32(0);        // ArtifactPowers count (resize only; elements follow DEBUGItemLevel)
        w.WriteUInt32(0);        // Gems count (resize only; elements follow DEBUGItemLevel)
        if (owner)
        {
            w.WriteUInt32(0);        // ZoneFlags - no legacy source
        }
        // >>> ItemBonusKey - client reader 0x6D2460, unconditional 8 bytes when empty
        // The live session fills this with the item's own EntryID on every single item
        // (verified over 12 live item creates, ItemBonusKey.ItemID == ObjectData.EntryID in
        // all of them); legacy random properties still travel in PropertySeed /
        // RandomPropertiesID above. Live also writes ZoneFlags (offset 230) as 7 on every
        // equippable and 4 on every consumable - semantics unknown, so that one stays 0
        // until it is understood (see the project rule about plausible values).
        w.WriteUInt32(s_itemBonusKey
            ? (uint)(m_updateData.ObjectData?.EntryID ?? 0)
            : 0);        // ItemBonusKey.ItemID
        w.WriteUInt32(0);        // ItemBonusKey.BonusListIDs count
        if (owner)
        {
            w.WriteUInt16(0);        // DEBUGItemLevel - no legacy source
        }
        // [ArtifactPowers elements go here: u16,u8,u8 each - count is 0]
        // [Gems elements go here: u32 + 16 x u16 + u8 each - count is 0]
        // >>> Modifiers : ItemModList - one byte, count in the top 7 bits (client 0x6D2140)
            w.WriteBits(0, 7);   // Values.size()
            w.FlushBits();
    }

    // Same bug class as WriteItemData above: the handler fills ContainerData.Slots and NumSlots
    // and the writer discarded them. 36 slots on this build - the generated 98 was TC master's
    // retail count; WPP's version-generated 5.5.x handler (UpdateFieldsHandler1158) reads 36,
    // matching our model exactly. No visibility gate on either field there.
    void WriteContainerData(WorldPacket w)
    {
        if (s_itemZero)
        {
            // Pre-wiring stub: 98 Empty slot guids (TC master's count) + NumSlots 0.
            for (int i = 0; i < 98; ++i)
                w.WritePackedGuid128(WowGuid128.Empty);
            w.WriteUInt32(0);
            return;
        }
        var cont = m_updateData.ContainerData;
        for (int i = 0; i < 36; ++i)
        {
            w.WritePackedGuid128(cont != null && i < cont.Slots.Length
                ? (cont.Slots[i] ?? WowGuid128.Empty)
                : WowGuid128.Empty);        // Slots
        }
        w.WriteUInt32(cont?.NumSlots ?? 0);        // NumSlots
    }

    // Same bug class: the handler fills DynamicObjectData (Caster, SpellID, a resolved
    // SpellXSpellVisualID, Radius) and the writer discarded it - a ground-effect spell's
    // visual area could never draw as anything.
    void WriteDynamicObjectData(WorldPacket w)
    {
        var dyn = m_updateData.DynamicObjectData;
        // Caster kept Empty DELIBERATELY: the handler fills it, but a non-empty packed guid is
        // length-variable; report, do not wire, per the current rules.
        w.WritePackedGuid128(WowGuid128.Empty);        // Caster (see note above) obj+0x0
        w.WriteUInt8((byte)(dyn?.Type ?? 0));        // Type obj+0x10 - model field exists, no legacy handler fills it
        // >>> SpellVisual. TrinityCore master carries a two-field SpellCastVisual struct here
        // (SpellXSpellVisualID + ScriptVisualID); this build carries a single u32, exactly as
        // 553's ReadCreateDynamicObjectData does. The inline reader in chain 0x18E3DE0 makes six
        // reads for 19 bytes and there is no seventh. HERMES_256_DYNFIX=1 drops the second half.
        w.WriteUInt32((uint)(dyn?.SpellXSpellVisualID ?? 0));        // SpellXSpellVisualID obj+0x14
        if (!s_dynFix)
        {
            w.WriteUInt32(0);        // ScriptVisualID - not read on 69110; no legacy source either
        }
        w.WriteUInt32((uint)(dyn?.SpellID ?? 0));        // SpellID obj+0x18
        w.WriteFloat(dyn?.Radius ?? 0.0f);        // Radius obj+0x1C - a float on the wire; the old u32 zero was width-identical
        w.WriteUInt32(dyn?.CastTime ?? 0);        // CastTime obj+0x20 - model field exists, no legacy handler fills it
    }

    // Same bug class: the handler fills CorpseData (DynamicFlags, DisplayID, Items, RaceId,
    // SexId, Flags, plus the Owner/GuildGUID guids) and the writer discarded it - a player
    // corpse would render as nothing.
    void WriteCorpseData(WorldPacket w)
    {
        var corpse = m_updateData.CorpseData;
        w.WriteUInt32(corpse?.DynamicFlags ?? 0);        // DynamicFlags obj+0x0
        // Owner/PartyGUID/GuildGUID kept Empty DELIBERATELY: the handler fills Owner and
        // GuildGUID, but non-empty packed guids are length-variable; report, do not wire,
        // per the current rules. Consequence: corpse ownership (loot/release UI) is untested.
        w.WritePackedGuid128(WowGuid128.Empty);        // Owner (see note above) obj+0x8
        w.WritePackedGuid128(WowGuid128.Empty);        // PartyGUID (no legacy source) obj+0x18
        w.WritePackedGuid128(WowGuid128.Empty);        // GuildGUID (see note above) obj+0x28
        w.WriteUInt32(corpse?.DisplayID ?? 0);        // DisplayID obj+0x38
        for (int i = 0; i < 19; ++i)
        {
            // model is also 19 wide (itemDisplayId | inventoryType << 24) - guarded anyway
            // The client's run is 0x80 .. 0xC8 in the descriptor, stride 4. No `obj+0x` anchor on
            // this line: one comment serves all 19 iterations and fieldcheck would join it to
            // every one of them.
            w.WriteUInt32(corpse != null && i < corpse.Items.Length ? corpse.Items[i] ?? 0 : 0);        // Items[i]
        }
        w.WriteUInt8(corpse?.RaceId ?? 0);        // RaceID obj+0x3C
        w.WriteUInt8(corpse?.SexId ?? 0);        // Sex obj+0x3D
        w.WriteUInt8(corpse?.ClassId ?? 0);        // Class obj+0x3E - model field exists, no legacy handler fills it
        w.WriteUInt32(0);        // Customizations count (obj+0x40) - kept 0 DELIBERATELY: the handler fills legacy-derived Customizations, but emitting elements would grow the block; wire count and elements together once corpse rendering is testable
        w.WriteUInt32(corpse?.Flags ?? 0);        // Flags obj+0x78
        w.WriteUInt32((uint)(corpse?.FactionTemplate ?? 0));        // FactionTemplate obj+0x7C - model field exists, no legacy handler fills it
        // >>> The reader at 0x2C6F650 stops here: 30 reads, 105 bytes, last store obj+0x7C, and
        // 553's ReadCreateCorpseData ends in the same place. This 31st field is not read.
        // HERMES_256_CORPSEFIX=1 drops it; see the knob's note.
        if (!s_corpseFix)
        {
            w.WriteUInt32(0);        // StateSpellVisualKitID - not read on 69110
        }
    }

}