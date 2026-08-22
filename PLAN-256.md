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

## Not root causes — the remainder

* `SMSG_MAIL_QUERY_NEXT_TIME_RESULT`, `SMSG_THREAT_UPDATE`/`_REMOVE` — number and body together.
* `SMSG_SET_DUNGEON_DIFFICULTY` — number undetermined, guarded, deferred to last.
* `SMSG_SPELL_START` — held back pending a capture at its new opcode.
* Neutral mobs cannot be attacked, aggressive ones can. Parked: combat is unusable while health never
  changes, and this may fall out of root cause 2 or 3.
* `ChatMessage.Read` reads 9 bits where the field is 11; `SMSG_COOLDOWN_CHEAT` writes a guid where the
  client reads a byte; `ItemModList`'s read side is inferred from symmetry.

## The loot path has still never run

No `SMSG_LOOT_*` appears in 168,320 logged sends, so the gate has no ground truth and three packets
are crash candidates on first use. One kill-and-loot session is worth more than any amount of static
analysis — blocked on root causes 2 and 3.

## The Ghidra pass

Running on the lab server, sixteen-plus hours in, in `FindNoReturnFunctionsAnalyzer`. Nothing is
written to disk until it finishes, so the whole run is at risk in its entirety, and its expected
value has fallen: the work it was meant to support was done by hand. Its remaining use is the
decompiler for naming fields still written as zeros.

## Before anything ships

**Remove the `[256-spike] CAPTURE` logging — it prints key material in clear text.** Then the
`FIXME(256-spike)` markers, the packet dumps, the descriptor-range logging and the thirty-odd knobs.
`VersionChecker` still borrows 3.4.3's response codes. Smoke-test 1.14, 2.5.x and 3.4.3.
