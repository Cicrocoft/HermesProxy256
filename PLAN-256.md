# 2.5.6 (build 69110) — the plan, on Vej B

Companion to `REFERENCE-256-CLIENT.md` (the evidence) and `tools-256-spike/model-256.md` (the layout
model). This is the **work programme**, and it is a **living document**: the register below grows as
each finding lands, so priority follows evidence instead of the loudest symptom.

**Rewritten 23 Aug 2026, and the reason is a genuine shift.** The old plan chased four "root causes"
by deriving layouts from references. We no longer derive — **we measure.**
`tools-256-spike/LIVE-CAPTURE-METHOD.md` is a proven pipeline that turns a live 2.5.6 session into
ground-truth create blocks parsed by WowPacketParser. Two keys recovered, 100% decrypt, the player's
own create block in hand. The plan is now built around that instrument.

---

## The decision: Vej B — send what Blizzard sends

Measured: the live server sends the player's own create at **visibility `0x07`** (Owner | PartyMember
| UnitAll). Our writer sends **`0x01`** (Owner). That single fact is the spine of the plan.

**The visibility byte is a contract, not a toggle.** The create block is linear and unmasked, so the
byte tells the client which field groups to read; declaring a bit *requires* having written every
field that bit gates, or the client reads the next group from the wrong bytes, drifts, and crashes.
So visibility and field-filling are the **same work**.

* **Vej A (stay 0x01):** only the Owner group to fill — but PartyMember-gated fields (**QuestLog**) and
  UnitAll-gated fields are *permanently absent*. The player is a valid but incomplete subset; the
  empty questlog and the "walked over zeros" arrays can never be right.
* **Vej B (go to 0x07, Blizzard's own):** the only path that makes the player complete. Costs more
  fields to get exactly right (each a crash chance in the unmasked block) — but the reason 0x01 was
  chosen (we couldn't fill the extra fields safely) **is gone**: the flags=7 capture is ground truth
  for exactly those fields.

**We go Vej B, incrementally, each bit a gated + tested step:**

```
0x01  →  0x03  (add PartyMember: QuestLog + the rest of the party group)  →  0x07  (add UnitAll group)
```

Each step: fill the group from the live layout, gate behind a `HERMES_256_*` knob defaulting off,
run `gate256.py`, then confirm one session. §127 (PartyMember/QuestLog once froze the client) was a
*guessed* `QuestLog[25]` layout; with the measured layout that risk is retired.

---

## The loop (how every register item gets closed)

1. **Measure** — from a live capture, parse the block with the 69110-patched WPP
   (`LIVE-CAPTURE-METHOD.md`) → clean field values + the group's real layout.
2. **Diff** — layout is already covered by `fieldcheck.py` against `clientfields-*.json`; the live
   data adds **values** (faction/scale/id resolution) and **populated dynamic arrays**. Diff those
   against our writer.
3. **Fix** — fill the field group, advance the visibility bit if the group completes one.
4. **Gate** — `gate256.py` green (length/order/reader/update/wire/frag/batch all independent).
5. **Confirm** — one live session, batching several knobs.

---

## The register (prioritized, grown as we find things)

Priority ≈ symptom visibility × ground-truth confidence ÷ crash risk. **Add rows as findings land;
do not delete — move to Done with the evidence.**

| # | finding / gap | subsystem · visibility gate | status | evidence |
|---|---|---|---|---|
| **P0-FREEZE** | **World-entry freeze — SOLVED (mitigated), confirmed live 24 Aug.** Root cause from the **client's own minidump** (ERROR #109, thread frozen 60 s, base 0x7FF689270000): the **ActivePlayerData create block is ~44 bytes shorter than the client's APD reader `0x713E50` consumes**, so the reader over-runs the packet buffer and reads the obj+0x1B58 map's element count from **stale heap** (observed 0xC8602701) → resize spin at `0x72E4E4`. Intermittent because heap past the buffer is sometimes zero (enters) / sometimes garbage (freezes) — why good and frozen captures were byte-identical and streamwalk read a clean 6236 over both (the emulator can't model the APD tail). **NOT** creature UnitData (all 65 walk clean, 598 B) and **NOT** the query response (that was a separate real bug, also fixed). Fix `HERMES_256_APDPAD=128` pads the last block so the over-read lands on in-buffer zeros → tail counts read 0, resize skipped. **Mitigation, not the field-level fix** — the APD tail (bank/research/dungeon maps, and InvSlots past obj+0x1B58) stays empty; the exact ~44 missing bytes need a live ActivePlayer create or a bit-accurate tail walk. | `ActivePlayerData` tail (over-read) | **SOLVED (mitigated) — committed 3b777db** | client minidump; frozen stack 0x72E4E4←0x729AA0←0x713E50@0x71AE1D; cursor 11052 past 11051-byte buffer; count 0xC8602701 not in packet |
| **P0-QUERY** | **`SMSG_QUERY_CREATURE_RESPONSE` corruption — SOLVED, confirmed live.** Empty creature name slots shipped length **1** (`GetByteCount()+1` unconditional) while the empty-guarded body wrote **0 bytes** → tail shift → `CreatureDisplay.Count` read as 0x48000000 → resize. Live Blizzard encodes empty slots as **0**. Also the gate-clean default shipped the wrong Classic body shape. | `QueryPackets.cs` | **SOLVED — committed 3b777db** | `HERMES_256_CREATURENAMELEN` + `HERMES_256_CREATUREQUERY`; wire `[(7,0),(0,0),(0,0),(0,0)]` matches live; mobs now have correct names in-game |
| **P0** | **PlayerData tail layout — SOLVED for ungeared, 11-byte parse-gap for geared.** | `PlayerData` tail · flags=7 | **mostly done** (writer fix next) | Client PlayerData reader 0x738FB0 walked (Fable). Tail = PersonalTabard **10×u32** (not 5), VisibleItem **23B** at block end, QuestLog[25]+`QuestLogExtraMap` u32 count, name-len `ReadBits(6)`, DungeonScore, name, LeaverInfo, DeclinedNames. **Verified end-to-end: Hvarne (ungeared) parses complete — Coinage 66, XP 1486, NextLevelXP 2100, AccountBankCoinage 0.** Rowine (geared) ActivePlayerData values correct at their offsets but WPP's PlayerData ends 11 B short of the true start (3640) — the post-VisibleItems dynamic-array element readers for a char that HAS elements. **Parse-only gap; our writer sends those arrays count=0 so it does not block the writer fix.** |
| ~~P2b~~ | **`PersonalTabard` 10×u32 — REJECTED (24 Aug).** The prior claim ("client reads 10, our 5 shifts APD 20 B") is **contradicted by the client's own reader.** `streamwalk` over 0x738FB0 consumes **1030 = 5 tabard**; `TABARD10=1` makes PlayerData **1050** and **fails `gate256.py`** (`reader PlayerData emit 1050 reader 1037 -7 = 1030`, +20). The Aug-23 capture that "localised the freeze" (loopI, ranges `19 917 1967 8198` → PlayerData **1050**) was **captured with `TABARD10=1`**, so **that freeze was TABARD10-induced**, not evidence for it. Default (5 tabard) is gate-clean and the client reader walks it. **Keep TABARD10 OFF. Fable's 10-tabard walk needs re-checking against 0x738FB0.** | `PlayerData` writer | **rejected, keep off** | gate reader-check; ranges file; streamwalk |
| P1 | **Empty questlog** — layout now **confirmed correct**; empty on the capture char is *genuine* (no active quests). So the private-server symptom is purely visibility: we send 0x01, `QuestLog` (PartyMember-gated) never reaches the wire | `PlayerData.QuestLog` · 0x01→0x03 | fix = advance visibility (after P0) | Fable: emulated client PlayerData matches 553 through QuestLog; QuestID 0 ×25 is real |
| P2 | **Money / XP read zero** | `ActivePlayerData` Coinage/XP · Owner | blocked on P0 | shifts because PlayerData tail is short; values readable once P0 closes |
| P3 | **InvSlots misfiles items** — bags unopenable | `ActivePlayerData` InvSlots · Owner | blocked on P0 | same shift; 146 guids obj+0x1D88 stride 0x10 |
| P4 | **The four "walked over zeros" arrays** now measurable at flags=7 | Player/ActivePlayer dynamic arrays | measuring | live flags=7 populates them; readable once P0 closes |
| P5 | **Health never changes** — values-update path still 2.4.3 masked | values update | open, separate track | §124; independent of the create-block work |
| — | **UnitData fully confirmed a value-diff source** | `UnitData` · flags=7 | **done, confirmed live** | parses byte-exact (898 B): Health 147, Race 3 Dwarf, Class 1 Warrior, Level 4, pos = Dun Morogh. §131 VirtualItems-at-end + 78-tail confirmed at the exact break |
| — | **VisibleItem element = 23 bytes** (i32,i32,i32,u16,u16,i32,u8,u8,u8), shared by UnitData.VirtualItems and PlayerData.VisibleItems | UnitData/PlayerData | **done, confirmed live** | both client maps; corrects model (element shape was unstated) |
| — | **`SpawnTrackingStateAnimID` = 1860**; **movement block byte-perfect** | GameObject/Unit; movement | **done, confirmed live** | §129; position/speeds/GravityModifier exact |

### In-world inventory — live on Mememe, 24 Aug (the "fill from truth" phase)

With the freeze gone the client plays, but the legacy→modern **value plumbing** is unfinished. The
create blocks parse; the sources are empty or placeholders. Handed to the Fable agent as one
prioritized batch (each behind a `HERMES_256_*` knob, verified against the client reader + raw live
`live2_deb.pkt`/`live3s2_deb.pkt`). Priority order:

| # | symptom (observed in-game) | cause (hypothesis) | class |
|---|---|---|---|
| V1 | XP bar shows **Bloodsail Buccaneers** rep (faction 87, not in rep list) | `WatchedFactionIndex` (obj+0x1524): legacy rep INDEX passed through; modern keying differs (§103). Verify value-passthrough vs alignment | gated/value |
| V2 | **All mobs 100 HP** (Rabbit, guards) | `UnitData.Health` filled with a placeholder, not the real legacy creature health (source: UpdateHandler.cs) | value-plumbing |
| V3 | **Cannot attack neutral mobs** (Ragged Young Wolf) | faction reaction / `UnitFlags` (parked PCFLAG) — a wrong reaction or a NON_ATTACKABLE flag | value/flags |
| V4 | **No skills** | `apd.Skill` not filled from legacy `PLAYER_SKILL_INFO` (300-slot array emitted but 0). Sits *before* the APD shortfall, so it lands once populated | value-plumbing |
| V5 | **Spells present but don't work** | values-update path (`VALUESUPDATE`) / cooldowns / server-side — may be separate from creates | separate track |
| V6 | **Cannot interact with vendors** | NpcFlags value and/or the gossip/vendor response path | gated/value |
| V7 | **Bags don't open / no money** | `InvSlots` (P3) OFF *and* sits after the obj+0x1B58 APD shortfall — needs the exact APD field fix + the fill (seam rule: guid must back a sent item) | gated + P0 tail |
| V8 | **No gear on the model** | `VisibleItems` / §103 appearance wiring off; element = 23 B | gated/value |
| V9 | **Questlog empty** | genuine (Mememe may have no quests) or visibility (we send 0x01; QuestLog is PartyMember-gated → 0x03) | P1 |

**High leverage:** V7/V8 and the empty APD tail all trace to the ~44-byte APD shortfall that
`APDPAD` only mitigates. Identifying the exact missing tail bytes (needs a live ActivePlayer create
capture or a bit-accurate tail walk) is the one fix that unblocks the tail wholesale.

*Still parked:* kneeling NPCs (`UNITANIM`), creature scale, chat (`SMSG_SEND_KNOWN_SPELLS`), the
loot family (never run).

---

## Rules that still hold (from the evidence)

* **The client wins.** References (553, TrinityCore, our writers) decide *candidates*; the client's
  own reader and the live wire decide. Never V2_5_5-as-lineage, never retail-as-layout (both retired,
  see `model-256.md`).
* **The seam rule.** A legacy id is safe only in a field the client *stores*; if it *resolves* it,
  back it with a record or the table's "none" value. §129.
* **Two axes.** Engine references give layout; 2.4.3 / our 2.5.2-3 writers give *presence*. A field
  with no era source is dead weight. Live data now settles presence directly.
* **One variable per session, behind a knob defaulting to current behaviour.** A change that does
  nothing by default is safer than one "probably right".
* **Run `gate256.py` before anything reaches a live session.** Parsing ≠ behaviour.

---

## Superseded

The old four-root-cause / Track A-B-C symptom programme is **absorbed into the register above**. Its
findings survive as register rows or Done rows; its framing (derive-then-guess) is replaced by
measure-then-fill. RC2 (UnitData) is confirmed done; RC1 (zeros) becomes "fill from truth"; RC3
(values update) is P5; RC4 (items) is P3. The evidence sections in `REFERENCE-256-CLIENT.md` are
unchanged and remain the detail behind every row.

## Before a PR

Remove the `[256-spike] CAPTURE` logging (**prints key material in clear text**), the `FIXME(256-spike)`
markers, the packet dumps, the descriptor-range logging and the knobs; keep the SRP salt fix and the
both-conventions evidence check (real upstream fixes). Smoke-test 1.14, 2.5.x, 3.4.3 — byte-identical
today, must stay so. The live-capture tools and `WPP_DUMP_DESC`/`WPP_69110` patches are dev-only and
stay out of any HermesProxy PR.
