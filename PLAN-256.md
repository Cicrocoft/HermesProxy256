# 2.5.6 (build 69110) — work plan

Companion to `REFERENCE-256-CLIENT.md`, which is the evidence.

**Rewritten after the work drifted into symptom-chasing.** The old phase structure served its
purpose — phases 0 to 3 are done — but by the end each report was producing a one-line fix or another
knob, and the state under the running agents kept moving. Everything visible now traces to **four
root causes**. Fix those; stop patching what they emit.

**Working:** login (now reliably — section 126), world entry, movement, NPC movement, quest pickup,
money and XP.
**Broken:** gameobjects render with no model (`DisplayID` sent as zero, section 128), health never
changes, quest log empty, bags and equipment empty, vendors do not open, NPCs kneel, some names
render white, neutral mobs cannot be attacked.

---

## Rules of engagement

Paid for, between them, with five crashes, one hang and a great deal of time.

1. **The client wins.** WowPacketParser's version-generated files are wire order; its hand-written
   parsers are a field *list*, not a sequence. Trusting one has caused three crashes.
2. **Over-sending is harmless; under-sending crashes** — specifically when a packed guid cannot
   complete. A short scalar read is a silent misparse.
3. **Gate on the check you already have.** `undersend_gate.py` had flagged the crashing opcode before
   it shipped. A check nobody runs is not a check.
4. **Check the history before believing a diff.** Three times a correlated change was blamed for a
   symptom that predated it.
5. **Dedup reader walks by store offset.** These readers duplicate each field's read on both arms of
   the previous field's notification fork; a plain sweep invents fields.
6. **One variable per session**, behind a `HERMES_256_*` knob defaulting to current behaviour.
7. **Convert hex with a tool.**
7-0. **This client is a seam, and the bugs live on it.** Blizzard did not maintain a separate engine
   for Anniversary: 2.5.6 is TBC *content* on a modern *engine* — roughly 2.5.x data over 5.5.x/11.x
   infrastructure. Everything measured so far fits, and the split predicts where to look:

   * **Wire format, readers, descriptor layout, movement, DB2 id spaces** — modern. Reference the
     11.x/12.x line.
   * **Ids, tables, templates, content** — TBC-era values, in tables whose id space is retail-sized
     but whose contents are not.
   * **The faults are where those meet**, and neither of 22 August's crashes was a coding error in
     the ordinary sense. `GameObjectDisplayInfo`: a TBC display id into a retail-sized table, hitting
     a hole. `AnimationData`: a retail-sized engine bound (`id <= 1859`) over a table holding the TBC
     subset (0..801). Blizzard glued two layers together and did not check the join.

   Practical consequence: **a legacy id is safe only in a field the client stores.** If the client
   resolves it, the record has to exist, or the field must carry the table's documented "none" value.
   The tier-1/2/3 inventory in section 129 is that principle applied field by field, and it is the
   list to work through before the next symptom is discovered by crashing.

7a. **Look forward, not sideways.** 2.5.6 is built on the 5.5 / 11.0 engine, so when a field's shape
   is in doubt the reference is the modern retail line — TrinityCore master
   (`/c/projekter/TrinityCore/src/server/game/Entities/Object/Updates/UpdateFields.h`) and
   WowPacketParser's version-generated 11.x/12.x handlers — **not** 2.5.5 or 3.4.3, which are the
   Classic line and a different engine. This repo has no sibling to copy from: 2.5.6 is the only
   build here using the linear unmasked format, and every other version directory holds a masked
   `ObjectUpdateBuilder` where a wrong field is merely ignored rather than fatal.

   Worked example, found in two greps once the principle was applied. Our `QuestLog` entry is
   modelled on WPP's V2_5_5 and is 66 bytes:

       QuestID i32 | StateFlags u16 | ObjectiveProgress u16[24] | EndTime i64 | "Unknown_69110" u32

   TrinityCore master's is 70:

       QuestID i32 | StateFlags u16 | EndTime i64 | ObjectiveFlags u32 | EnabledObjectivesMask u32 | ObjectiveProgress i16[24]

   Two differences: `ObjectiveProgress` sits **last** on the modern engine, not third, and there are
   **two** u32s between `EndTime` and the array where we emit one. That is a strong candidate for the
   empty quest log. It is a hypothesis, not a fact — rule 1 still applies, and `streamwalk.py` over a
   real capture settles it — but it is a far better starting point than the Classic-line layout.

7b. **Diff the wire, not the source.** `PacketsLog/*.pkt` holds full captures. When a session works
   and the next does not, the differing bytes are the change set — measured, complete, and available
   even when the working source no longer exists. `HERMES_256_ZERO` blanks ranges back without
   moving a block boundary. This found section 128's single field in five logins after three
   sessions of knob-guessing had found nothing.
8. **Read what already exists before deriving it.** Four times the answer was written down — in the
   gate's own output, in the aligner's docstring, in `_qb.lua`, in the vendor's documentation.
9. **Verify the build landed, not that it compiled.** A running proxy holds `Framework.dll`, so
   MSBuild's copy to `bin/Debug` fails with MSB3021/MSB3027 while the compile itself reports no
   `error CS`. Filtering build output for `error CS` therefore reports success on a build that never
   shipped: every build between 14:52 and 21:45 on 22 Aug silently kept the old binary live, and
   hours of "live testing" ran code that had already been replaced on disk. Stop the proxy first,
   and check the output timestamp — not the compiler's exit text.
10. **The user's observations have outperformed the analysis.** `1792`, the 851 measurement and the
   whole update-path discovery came from playing, not from tooling. Ask for a measurement before
   building a model.

---

## Root cause 1 — the writers were generated as zeros

`gen_writer.py` emitted each descriptor as all-zeros, and a hand-written table re-attached the fields
someone thought of. **Every field nobody thought of still reads zero**, silently.

Each instance looked like its own bug at the time:

| symptom | field |
|---|---|
| character rendered naked | `PlayerData.VisibleItems` |
| no `!` or `?` over questgivers | `UnitData.NpcFlags` |
| mana bar reads 0/0 | `UnitData.Power` / `MaxPower` |
| **quest log empty** | `PlayerData.QuestLog` — the handler fills it, the writer emits 25 empty entries |

`tools-256-spike/zeroaudit.py` enumerates the class and reports **19 fields still in it**. One
mechanical pass, not nineteen investigations.

**Deliverable:** every field the handlers populate is either written or carries a comment saying why
not; `zeroaudit.py` returns clean and is run after any regeneration.

**Closed, then reopened by its own consequences.** The pass ran and the audit is clean, but it went
live only on 22 Aug (every build since 14:52 had silently failed its copy — rule 9) and two of its
wirings were wrong in the same way: they changed a block's length. `QuestLog` added 1650 bytes to
PlayerData and froze the client (section 127); `ItemData`'s packed guids added 6 bytes and the client
now allocates 19.2 GB for a gem count read out of the neighbouring field (section 128).

**The lesson is a rule, not a bug.** In a linear unmasked block, wiring a field is only free when it
cannot change the block's length. Every wiring that touches a packed guid, an array or a
visibility-gated section must be measured against the `descriptor ranges` log before it ships.

---

## Root cause 2 — `UnitData` is 17 bytes too long

Measured, not inferred: the client consumes **851** where we emit 868 (section 121). `VirtualItems`
is confirmed a 23-byte element read at the block end, accounting for +21 in the wrong direction; a
walk of the reader's instructions gives 898 where the packet gives 851, and that 47-byte
disagreement is unexplained.

This displaces everything after `FactionTemplate`. **Kneeling NPCs, white tooltip names and grey
health bars are all this one fault** — the kneeling tracks `HERMES_256_ITEMLOOK` exactly, because
with item ids landing in the bytes the client reads as `StandState`, a weapon becomes a posture.

*Two agents are on the 851-versus-898 disagreement.*

**Deliverable:** a writer that totals 851 with each divergence evidenced. `UNITTRIM` is scaffolding
and comes out with the fix.

---

## Root cause 3 — the values-update path is the legacy masked format

The create path was rewritten for this build and verified. The update path still calls
`BuildValuesUpdate`, which writes a 2.4.3-era masked update-field array after a modern fragment
header. If that holds, **no value update on this build has ever been parsed**.

Health is the most visible instance: damage lands on the server and both bars stay full. Auras, mana,
positions and state changes travel the same path.

*One agent is on it.*

**Deliverable:** an update body the client's own reader accepts, gated until one session confirms
health moves.

---

## Root cause 4 — item objects are never serialised

cmangos packs equipped and container items with a non-standard `0x4700` high guid and the proxy skips
their CreateObject blocks. Three symptoms, one cause:

* **bags empty** — `InvSlots` is gated off because guids the client cannot resolve hung it for sixty
  seconds;
* **equipment browser empty**;
* **vendors do not open** — and `SMSG_VENDOR_INVENTORY` additionally sits on the wrong opcode
  (`0x46005C` is the real one; number and body must move together).

**Deliverable:** items serialised through `WriteCreateItemData` so their guids resolve, then
`InvSlots` re-enabled and the vendor packet moved.

---

## The work programme

Symptom-driven work found the causes; it does not scale to closing them. `.wpp/…V5_5_0_61735` is
now established as the base model (`tools-256-spike/model-256.md`), and it holds **15 descriptor
readers** and a parser per subsystem. That turns most of the remaining work into a systematic audit
that needs neither the client nor a person watching.

### Track A — descriptor blocks, one deviation table each

`ObjectData`, `PlayerData`, `ActivePlayerData`, `ItemData`, `ContainerData`, `GameObjectData`,
`DynamicObjectData`, `CorpseData`. `UnitData` is **done** and is the template: source-diff the field
order against 553, then `streamwalk` against a real capture, then record base + deviations.

**23 Aug: all nine now have a client map (`clientfields-*.json`) and the last three divergences are
closed.** `GameObjectData` (100 B / 29 fields -> 80 / 24), `DynamicObjectData` (23 / 7 -> 19 / 6)
and `CorpseData` (109 / 31 -> 105 / 30) each sit behind their own knob - `HERMES_256_GOBFIX`,
`HERMES_256_DYNFIX`, `HERMES_256_CORPSEFIX` - defaulting to the old behaviour, and
`KNOWN_DIVERGENCE` in `fieldcheck.py` is empty. What is left in this track is not divergence but
**coverage**: four of the five newer maps were walked over zeros, so their fixed skeletons are
measured and every dynamic array in them is unmeasured. The first non-empty `StateWorldEffectIDs`,
`EnableDoodadSets`, `WorldEffects` or `Customizations` on the wire is untested territory.

Both checks are offline. Order and total are independent — 898 matched while the order was wrong by
48 bytes, so run both. `blocklen.py --check` guards every edit.

### Track B — packet bodies, one deviation table each

Our 35 writers in `World/Server/Packets/` against 553's parsers. This is where the remaining
under-send crash candidates live — `SMSG_SPELL_START`, `SMSG_MAIL_QUERY_NEXT_TIME_RESULT`,
`SMSG_THREAT_UPDATE`, `SMSG_SET_DUNGEON_DIFFICULTY`, and the whole loot family, which has **never
run**: no `SMSG_LOOT_*` appears in 168,320 logged sends, so the gate has no ground truth for it.

Also offline, and it front-runs the crashes instead of waiting for them.

### Track C — the values-update encoder. **WPP has the decoder; invert it.**

*Correction, and the useful kind: an earlier version of this section said "WPP cannot help with C".
That was wrong, and wrong by rule 8 — it was written after checking `ReadCreate*` and never looking
for `ReadUpdate*`. Found by the user.*

`UpdateFieldsHandler553.cs` carries **all fifteen `ReadUpdate*Data` decoders**, live, including
`ReadUpdateActivePlayerData` (which is commented out in the 2.5.5 handler, but not here). The wire
format is a hierarchical changes mask:

```csharp
rawMaskMask[0] = packet.ReadBits(8);            // UnitData: 8 blocks
for (var i = 0; i < 8; ++i)
    if (maskMask[i]) rawChangesMask[i] = packet.ReadBits(32);   // up to 256 change bits
// then the set bits' values, in field order
```

`ActivePlayerData` uses 14 blocks, so 448 bits. The bit-to-field mapping is in the same generated
file, next to the create readers we already validated `UnitData` against on both axes.

**So this is an implementation job, not a reverse-engineering one.** Write the inverse of 553's
`ReadUpdate*Data` and delete the 2.4.3 masked field array from the modern payload entirely.
`0x24D380` stops being the starting point and becomes a **diff instrument**: run the encoder, see
what 69110 does that 5.5.3 does not, and reverse only that. On the create path that difference was
two deviations for `UnitData` and none for `PlayerData`, so the update path may be close to free.

Take the field set from **553, not 2.5.5** — 2.5.5 is the Classic line and is the source of three
faults we shipped, including `VirtualItems` in the wrong position and a missing 78-byte tail.

The old text, kept because the diagnosis of the fault itself still stands: `BuildValuesUpdate` still writes a 2.4.3 masked
field array behind a modern fragment header, so **no value update on this build has ever been
parsed**. Route B — re-emitting units as creates — crashed the client on target. Route A needs the
client's own decoder read out of `0x24D380`, which now has a fully analysed Ghidra database behind it.

**Suspected scope, unproven:** quest log empty, XP and money zero, health never moving may be one
fault rather than three. All three have byte-exact geometry and absent content, and the emulator
delivers most of those fields in value updates rather than in the create. Cheap first step: log
whether `updates` even contains `PLAYER_XP` at create time.

Track C is the only one of the three that makes the game playable. A and B are for correctness and
for not discovering the next fault by crashing.

## Not root causes — the remainder

* `SMSG_MAIL_QUERY_NEXT_TIME_RESULT`, `SMSG_THREAT_UPDATE`/`_REMOVE` — number and body together.
* `SMSG_SET_DUNGEON_DIFFICULTY` — number undetermined, guarded, deferred to last.
* `SMSG_SPELL_START` — held back pending a capture at its new opcode.
* Neutral mobs cannot be attacked, aggressive ones can. Parked: combat is unusable while health never
  changes, and this may fall out of root cause 2 or 3.
* **`InvSlots` misfiles items — and this is progress, not a new fault.** With
  `HERMES_256_INVSLOTS=1` the client no longer hangs for sixty seconds and items reach the inventory
  UI for the first time; Recruit's Boots (legacy `EQUIPMENT_SLOT_FEET`, index 7) simply lands in a
  bag slot, and the bags cannot be opened because the real bags are not where the client looks.

  The writer already predicted this in a comment and left the mapping unfilled **deliberately**:
  3.4.3's `GetModernInvSlot` maps 141 slots, this client's count is 146 (global `0x33EFE64`), so
  every range above the shared equipment+bags prefix has drifted — a 5.5.x reagent-bag slot alone
  shifts them all by one. We pass indices 0..22 straight through and leave the rest empty.

  The comment also names the fix: **read the client's slot enum from the `InvSlots` consumer.** That
  needed a Ghidra database with real cross-references, which did not exist when it was written and
  now does. This is a transcription job, not an investigation.

* **Ragged Young Wolves render too small — and only them.** "Only them" is the useful half of the
  observation: a fault that hit every creature would be the writer, one that hits a subset is a
  per-creature value.

  `WriteUnitDataReal` sends `DisplayScale` from the handler, which derives it from the legacy display
  id's native scale, and `NativeXDisplayScale` hardcoded to 1.0f. On a 2.4.3 core a creature's size
  is `creature_template.Scale` x `CreatureDisplayInfo.CreatureModelScale`; the modern client applies
  `DisplayScale` **on top of** the model scale its own DB2 already carries. If we pass the legacy
  product rather than the template factor alone, anything whose model scale is not 1.0 is squared —
  a young wolf at 0.7 renders at 0.49, while a normal creature at 1.0 is unaffected. That matches the
  symptom exactly and is cheap to test: send `1.0f` and see whether the wolves grow.

  Same family as section 129's rule, from the other direction: not an id the client resolves, but a
  value the client combines with data it already has.

* **The XP bar shows Bloodsail Buccaneers reputation instead of experience.** That bar switches from
  XP to reputation when `ActivePlayerData.WatchedFactionIndex` names a faction, so the client is
  reading a valid index where it should read "none".

  We know more about this field than about most: it is the `+5091` value from the 22 Aug byte diff
  that looked like a corrupted array index. It is an index into the client's own reputation list,
  **not** a DB2 id, and **-1 is the correct sentinel** — zero is the dangerous value, because zero
  names a real list slot. The wiring pass changed it from `00000000` to `ffffffff`, which is the
  right direction.

  So either -1 is not arriving as written, or the field is still being read somewhere else. The
  second is worth checking first: this sits at the far end of `ActivePlayerData`, and every
  measurement of that block before 23 Aug 00:31 was taken through the 29-byte `UnitData`
  displacement (section 132). Re-measure before theorising.

* **The world-entry freeze correlates with the client's creature cache.** ERROR #109, a thread frozen
  for 60 seconds, intermittent, at world entry. Measured 23 Aug:

  | | world entries | freezes | `creaturecache.wdb` |
  |---|---|---|---|
  | 12:45-12:53 | several | **3** | 7.9 MB |
  | after clearing WDB at 12:57 | **10** | **0** | not yet rewritten |

  Ten consecutive clean entries is the bar the analysis set for this fault: the per-attempt failure
  rate looked like a half to three quarters, so five clean entries would still be 3% luck and ten
  is under a per mille.

  **What is established:** clearing the cache stops it. **What is not:** that the cache is the cause
  rather than a symptom. The chain — malformed `SMSG_QUERY_CREATURE_RESPONSE` → the client caches
  something it cannot re-read → read back at login → freeze — is a hypothesis. Only the malformed
  response is independently measured (against WPP 5.5.3: two type-flag u32 where it reads three,
  `CreatureType` i32 where it reads u8, `Classification` i32 where it reads i8, a missing
  `QuestCurrencies` u32; net −2 bytes, and `QueryCreatureResponse.Write` has no version branch at
  all — it is the Classic-line body).

  **The falsifiable test:** the freeze should return as the cache regrows, and should stop returning
  once the query response is fixed, regardless of cache size.

  Four things predicted by this one defect: the freeze, the 7.9 MB cache, `UnitCreatureType`
  returning nil, and creature scale. Confirm or exclude each separately — they share a packet, not
  necessarily a cause.

  Also measured while chasing it, and worth keeping: **the player's ActivePlayer create is
  byte-identical between sessions that hang and sessions that do not** — 8382-byte record, values
  blob at offset 204, 5 bytes differing across the whole blob (XP 85→169, a skill 4→5, one float).
  Whatever the trigger is, it is not that packet's content.

* **Chat does not work, and it is not the chat code.** Typing in `/say` produces the client's own
  "You cannot speak that language" and **no packet leaves the client at all** — the proxy log shows
  only `CMSG_PING` and `CMSG_TIME_SYNC_RESPONSE`. That message is a client-side check: languages are
  spells (Common 668, Dwarvish 672), so a client that knows no language spells refuses to send.

  This also blocks every GM command, since those travel as chat. The account is not the problem —
  `tbcrealmd.account` has `gmlevel = 3` for QBTEST, which is `SEC_ADMINISTRATOR` on this core.

  The lead, unverified: `SMSG_SEND_KNOWN_SPELLS` is 185 bytes — `u8` flag, `u32` count = 44, then a
  `u32` **zero** before the ids begin. 1 + 4 + 44*4 = 181, so the trailing count appears to be
  written at the front. If so the client reads `id[0] = 0`, the whole list shifts one place, and the
  last spell is lost — which would silently drop a language if it sorts last. Read the full packet
  out of `PacketsLog/*.pkt` (the log truncates at 128 bytes) and check whether 668 / 672 are present
  and where.

  Worth fixing early: it is small, it is self-contained, and it unblocks GM commands, which make
  every other symptom faster to test.

* `ChatMessage.Read` reads 9 bits where the field is 11; `SMSG_COOLDOWN_CHEAT` writes a guid where the
  client reads a byte; `ItemModList`'s read side is inferred from symmetry. None of these are the
  cause of the chat failure above — they are on the receive side of a path the client never enters.

## The loot path has still never run

No `SMSG_LOOT_*` appears in 168,320 logged sends, so the gate has no ground truth and three packets
are crash candidates on first use. One kill-and-loot session is worth more than any amount of static
analysis — blocked on root causes 2 and 3.

## The Ghidra pass — finished, and it survived

**Done, 22 Aug 21:45.** `144,066 functions, 9,283,389 instructions`, verified by reopening the
project and recounting. The run ended with `IOException: Unable to lock due to active transaction`
out of `prog.save()`, which was alarming and cosmetic — pyghidra's context manager holds its own
transaction, an explicit save inside it collides, and the database had already been flushed.

This removes the standing caveat on every Ghidra answer in this project. The old database held
90,173 one-byte stubs with **zero** instructions, so "no references to this" was uninformative
rather than negative, and three investigations were misled by it.

    ssh root@192.168.1.105
    GHIDRA_INSTALL_DIR=/opt/ghidra_12.1.2_PUBLIC  JAVA_TOOL_OPTIONS=-Xmx6g
    /opt/binanana-venv/bin/python <script>          # see /opt/devserver/checkanalysis.py
    PROJ = /opt/binanana/profile/tbc-classic-2.5.6-69110-windows-win64/ghidra_project
    NAME = binanana_tbc-classic-2.5.6-69110-windows-win64
    BIN  = /opt/devserver/wow-256-live.exe

**Always `analyze=False`.** Re-running costs sixteen hours. Open read-only; do not save.

What it is for now: cross-references to the stream primitives (`0x2D9E4B0` u8, `0x2D9E500` u16,
`0x2D9E550` u32, `0x2D9E5A0` u64, `0x2DF08D0` packed guid) make a descriptor reader's true field
sequence readable rather than inferable — which is exactly what root cause 2 needs.

## Before anything ships

**Remove the `[256-spike] CAPTURE` logging — it prints key material in clear text.** Then the
`FIXME(256-spike)` markers, the packet dumps, the descriptor-range logging and the thirty-odd knobs.
`VersionChecker` still borrows 3.4.3's response codes. Smoke-test 1.14, 2.5.x and 3.4.3.
