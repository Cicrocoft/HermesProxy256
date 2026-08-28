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

| — | **XP bar missing — SOLVED, confirmed live 28 Aug, committed e5ef5c1.** `SMSG_AUTH_RESPONSE` carried `LegacyVersion.ExpansionVersion` (the version string's leading digit, `"V2_4_3_8606"` → **2**) where the field is Blizzard's `Expansion` enum (TBC = **1**). `GetMaxLevelForPlayerExpansion` (rva 0x0C36B40) bounds-checks against 2 (rva 0x245EF0 = `mov eax,2; ret`) before indexing `{[0]=60,[1]=70}` at rva 0x2F7DCC8, and **returns 0 out of range** — so every FrameXML "at max level?" test is true at every level and the bar is hidden. `CharacterHandler.cs:340` already applied the −1 for `SMSG_INITIAL_SETUP`; this site never did. Same class as `WatchedFactionIndex`: a legacy-sized id in a field the client **resolves**. | auth · `SMSG_AUTH_RESPONSE` | **SOLVED — `HERMES_256_EXPANSIONENUM`** | `/xpprobe`: `GetMaxLevelForExpansionLevel(0/1/2) = 60/70/0`, `GetMaxLevelForPlayerExpansion() = 0`, `GetAccountExpansionLevel() = 2` (client does **not** clamp); bar appeared at 1064/1400 with the knob on. `IsXPUserDisabled()` false throughout → excluded. Blizzard sends `01 00` in `w15_s0.bin`; we sent `02 02` |
| — | **`HERMES_256_MAXLEVEL70` is dead code.** The expression is `apd?.MaxLevel ?? (s_maxLevel70 ? 70 : 0)` and 2.4.3 *does* report `PLAYER_FIELD_MAX_LEVEL` (`UpdateHandler.cs:3636`), so `apd.MaxLevel` is 60 and the `??` never fires. Flipping the knob changes nothing on the wire. **Do not spend a session on it.** | — | **excluded** | read at the site; 28 Aug wire dump shows MaxLevel 60 |
| — | **Quest never shows Complete — SOLVED, confirmed live 28 Aug.** The `u32` immediately after `QuestLog[25]` in `PlayerData`, which we hardcoded to 0, is a **count followed by that many `{u32 questID, u32 slotIndex}` pairs** — a questID→slot lookup map. `REFERENCE-256-CLIENT.md:4826` recorded it as "unknown and safe as zeros"; **falsified.** Blizzard's `ap_rowine.bin` carries 3 pairs. Writing ours flipped `isComplete` nil→1 and `IsQuestWatched` nil→true, so shift-click tracking works too. | `PlayerData.QuestLogExtraMap` | **SOLVED (create path) — `HERMES_256_QUESTIDMAP`, not yet committed** | client reader 0x73F600 reads the count, loops it, hash-inserts by the first u32 (MurmurHash3 finalizer constants); stream continuity 2636→2664; `/qprobe` before/after |
| — | **The objective counter is NOT the completion mechanism — excluded 28 Aug.** The live 2/3/5/6/8 climb for quest 179 in `w15_s1.bin` is real but **not what flips the quest**: with counters empty on the wire and the map present, `isComplete` = 1 and the objective still renders 8/8. The client counts item objectives from its own bags. The synthesis path built on this hypothesis (prefetch, self-query, dedupe, fill, push) is being **deleted**, not parked behind a knob — the hypothesis it implemented is known wrong. | quest log | **excluded, measured** | proxy6.log 19:49:53 `progress=[,,,,,]` + `questidmap: wrote 1 pair`; `/qprobe` 19:51 `isComplete=1` |
| — | **`StateFlags` excluded as the completion mechanism.** Blizzard sends `0x0000` on every quest (`ap_rowine.bin`); `HERMES_256_QUESTSTATE0` writes Blizzard's 0 and changed nothing, and the map flipped completion with `StateFlags` left at cmangos' 1. | quest log | **excluded** | two live tests |
| P6 | **`QUESTIDMAP` regression: abandon does not clear until relog.** The map is maintained on the **create path only**; the values path has no bit for it, so after an abandon the stale pair still resolves `179 → slot 0` and the entry stays in the log. Bounded — the map is rebuilt on every create, so a relog clears it (confirmed). **A stale pair is worse than a missing one:** abandon-then-accept-something-else makes a by-id lookup return the *wrong* quest's data. | `PlayerData.QuestLogExtraMap` values path | **open — blocks committing QUESTIDMAP** | user-reported, reproduced, clears on relog as predicted |
| P7 | **The values-mask bit for `QuestLogExtraMap` is bit 25 — measured; its payload encoding is NOT.** Bit 25 appears on all five quest-membership events in `w15_s1.bin` (packets 359, 4403, 4573, 4588, 4612) and on **none** of the progress-only or AvgItemLevel-only blocks, and those bodies carry the quest id **twice** (one is `QuestLog`'s own `QuestID`; the second has no other home). **This also explains `HERMES_256_PDSHIFT1`:** bit 25 in 553 is `ChosenTitle`, the field our create writer emits immediately *after* the map — a field inserted there takes bit 25 and pushes everything after it by one, which is the empirical +1 PDSHIFT1 already applies to `PartyType` 44, `QuestLog` 47, `VisibleItems` 73, `AvgItemLevel` 93. **The shift and the map are the same fact.** Payload is variable-length in an unresolved way (quest id at tail offset 8 in packets 359/4588, offset 3 in 4573/4612), so the bit is **not** shipped: a mis-encoded mask desyncs the cursor for every later part in the same block. | `PlayerData` values path | **open — bit measured, payload open** | `gt_pdscan.py` over 52 PlayerData values blocks; framing self-validated (7-byte progress tail ending in the 2/3/5/6/8 series) |

### Vendors, trainers and repair — scoped 28 Aug against capture #13 (§137)

Full evidence in `REFERENCE-256-CLIENT.md` §137. Headline: **the vendor leg is already correct end to
end** and needs one visual confirmation, not development. The trainer leg fails three ways, and
repair is not a packet problem at all.

#### STATE (updated 29 Aug, end of the vendor/trainer session)

**Trainer, vendor and buyback all work end to end.** Eight knobs are now default on in `run256.sh`
and every one was confirmed in a live session, not inferred: `TRAINEROPCODE`, `TRAINER553`,
`LEARNEDSPELLS3`, `NPCGOSSIPBIT`, `BUYITEM553`, `BUYBACKVALUES`, `INVSLOTMAP` (plus the quest chain
that was already committed). Raw logs kept as `proxy-trainer-run1.log` … `run5.log`.

What now works: the trainer list renders and spells train and reach the spellbook immediately;
vendors, repairers and bankers are clickable; buying delivers the item clicked; selling returns the
money and files the item into the buyback tab with its price; buying back returns it to the bag.

**Three results worth not re-deriving, each an exclusion as much as a fix:**

1. **The two trainer symptoms had ONE cause.** "The spell does not appear in the spellbook" and "the
   spell does not leave the trainer list" both came from `SMSG_LEARNED_SPELLS` being 4 bytes short.
   The client filters the trainer list against **its own spellbook**, not against the `Usable` byte —
   the server was already sending `Usable = 0 (Known)` while the spell still showed. There is no
   separate list-refresh mechanism to find. Relatedly, `SMSG_TRAINER_BUY_SUCCEEDED` staying unhandled
   is **correct**: the modern engine has no such opcode (CypherCore carries only `TrainerBuyFailed`),
   so the `No handler` line in the log is not a gap.
2. **It is bit 0 specifically, not "any service bit".** Adlin Pridedrift carries `QuestGiver` and the
   client would not interact with him either. Only `Gossip` starts an interaction on this engine.
3. **"Item not found" on buyback was not a buyback bug** but an era mismatch in `AdjustInventorySlot`
   — see T-O. Expect the same class elsewhere: any modern→legacy index table written against the
   wrong era's constants fails silently, on the ranges that happen to differ.

**Still open in this subsystem:**

* **Repair (T-F) — never reached in the session, so still entirely unconfirmed.** The repairers are
  clickable now, so it is testable. It is a values-update problem, not a packet problem, and
  `ItemData` has no measured values numbering; expect "money went down, the durability bar did not
  move".
* ~~T-P: the ground-truth tooling decodes `ActivePlayerData` at 14 bits~~ — **fixed 29 Aug**, both
  tools corrected and `bit-inventory.md` regenerated; APD identification 27/58 -> 48/59. Note this
  never affected the proxy: the shipped encoder always used 15. It was a poisoned *reference*, and it
  had already cost one round of confusion (the "Coinage = 9" vs "Coinage = 42" contradiction). **The
  `ItemData` section T-F needs still does not exist** — `gt_bitinventory.py`'s `BLOCKS` decodes only
  Object/Unit/Player/ActivePlayer, so repair remains blocked on adding `ItemData` to that tool.
* Buyback timestamps are relative rather than Unix on our path (see the last register row).
* `LfgDungeonsID` (OQ-3), `GossipOptionID` resolution (OQ-4), `ItemExtendedCost` misses (T-G2).

**A retail capture is NOT needed for any of the above.** Capture #13 already carried the ten sells
that settled T-N and the `CMSG_REPAIR_ITEM` that T-F needs; what was missing was never data, it was
the tool's mask width. The one thing #13 provably does not contain is a `CMSG_BUY_BACK_ITEM` — and
that has now been settled live instead. Before asking for a new capture, name the packet #13 lacks.

**Two traps found on 29 Aug, both fixed, both worth knowing:**

* **`run256.sh` had dead exports.** The five quest knobs sat *after* `exec "$DOTNET"`, which replaces
  the shell — so they never ran, and since they default OFF in C# the script alone launched with the
  whole committed quest chain disabled while the file read as if it were on. Moved above `exec`, with
  a barrier comment. **Nothing may be added below that line.**
* **"the proxy is not running" is not something `tasklist | grep hermes` can tell you.** The process
  is `.NET Host` / `dotnet.exe`. A build against a running proxy fails only at the *copy* step with
  `MSB3027 ... locked by ".NET Host"`, and piping the build through `tail` hides the exit code, so it
  can look like a clean build. Kill by command line (`Win32_Process ... CommandLine -like
  '*HermesProxy.dll*'`), as `run256.sh` itself does. A real build reports ~60 warnings; **0 warnings
  means the resource-less no-op build** described in the build-environment trap below.

**`gate256.py` is FAILING today, and it is NOT from this work.** Three reader legs and `frag-bit`
fail (`UnitData emit 892 / reader 898`, `PlayerData 2652 / 990`, `ActivePlayerData 6568 / 6294`).
The gate reads none of the files this session touched — verified. Note the gate calls `streamwalk.py`
with a hardcoded `flags=1` while the writer now ships `0x07` (VEJB=1), which is exactly the "compare
like with like" trap: **that is a hypothesis, not a finding — it has not been checked.** Either way
the gate needs a session of its own before it can guard anything again.

**Do not re-derive these — they are measured and closed:**
* the vendor leg (gossip, vendor inventory, sell, repair reads, `ItemInstance`) is byte-correct
  against live Blizzard packets. One visual confirmation, no development.
* `TrainerID` is safe: the client has **no `Trainer` DB2** and echoes the id back. T-G.
* OQ-1 is a plain `u8`, OQ-2 is WPP's order, `CMSG_BUY_ITEM` is 4×u32-then-`ItemInstance`. T-J/K/L.
* `bit-inventory.md` has already been regenerated with the corrected opcode; do not regenerate again.

**Still genuinely open:** whether an `ItemExtendedCost` miss is fatal or a benign null (T-G2),
`LfgDungeonsID` (OQ-3), whether the client resolves `GossipOptionID` or only echoes it (OQ-4), and
repair visibility, which needs an `ItemData` values-bit inventory that does not exist yet (T-F).

| # | finding | status | evidence |
|---|---|---|---|
| — | **Gossip, `SMSG_VENDOR_INVENTORY`, `SMSG_SELL_RESPONSE`, `CMSG_SELL_ITEM`, `CMSG_REPAIR_ITEM` and the 14-byte `ItemInstance` are all measured correct** | **confirm visually, do not re-derive** | §137.1 — 3 vendor packets tile exactly (19 B header, 47 B element, 301/395 B totals); 10 sell responses byte-for-byte |
| T-A | **`SMSG_TRAINER_LIST` is shipped as `0x460188`, which is `SMSG_THREAT_UPDATE`.** The real opcode is `0x46018D`. The comment at `Opcode.cs:979-983` already predicted the threat shape and said the trainer list was "squatting" on it. Delivered under the wrong number it lands in the client's threat reader — the harmless-sink pattern §125 diagnosed for the old vendor number, which is why "trainers do nothing" never produced a crash report. | **SOLVED — confirmed live 29 Aug, `HERMES_256_TRAINEROPCODE` now default on** | §137.2: 29 packets of exactly 32 B all decode as threat; 0x46018D identified over-determined (same guid across 5 client packets, `TrainerID=22` echoed back, 5 Dun Morogh warrior spells with matching levels, 41-byte greeting matching its own 11-bit length, 232 B with zero slack) |
| T-B | **`TrainerList` layout wrong twice:** header 3 B too long (`i32 TrainerType` where the wire has 1 byte), element 4 B too short (30 vs **34**). WPP 5.5.0 independently reads the extra `Unk440`. `SpellID` is a **resolved** field, so this is crash-shaped, not cosmetic. **`MaxSize` uses `SpellSize = 30`: raising the element without raising it overruns a pooled buffer.** | **SOLVED — confirmed live 29 Aug, `HERMES_256_TRAINER553` now default on** | §137.2, plus a wire proof the capture could not give: with the 34-byte stride the `Usable` byte of element 0 lands at offset **48**, and offset 48 is the **only** byte that differed between the trainer list before and after a purchase (`01` Available -> `00` Known). Header decodes as guid 7 B + `u8 TrainerType = 0` + `TrainerID 912` + `Count 5`; elements are 6673 Battle Shout (9c, lvl 1), 6343 Thunder Clap (95c, lvl 6), 3127 Parry (95c) |
| T-C | **`SMSG_LEARNED_SPELLS` is 4 B short** — live is 18 B with **three** header u32; we write two. A 69110 deviation from 5.5.0, visible only in the capture. The spell id lands at offset 9 instead of 13, so the client learns a garbage id. | **SOLVED — confirmed live 29 Aug, `HERMES_256_LEARNEDSPELLS3` now default on** | §137.3; live wire `bodyLen=18 01000000 00000000 00000000 00 64000000 00` — spell **100 Charge** at offset 13, byte-identical in shape to Blizzard's [5432] |
| T-D | **The `ISpanWritable` trap.** `TrainerList`, `LearnedSpells`, `BuyFailed` and `BuySucceeded` are all `ISpanWritable`, and `Packet.cs:158` prefers `WriteToSpan`. **Fixing `Write()` alone changes nothing on the wire** — the same trap that hid the `BagResult` width fix. | **rule** | §137.4 |
| T-E | **`CMSG_BUY_ITEM`: reference disagreement, no measurement.** We read the 9.x/3.4.3 shape (3-bit `ItemType` after the instance); WPP 5.5.0 has `i32 ItemType` before it. If WPP is right the item id forwarded to cmangos is garbage. `BuyFailed.Reason` is u8 where WPP reads i32 — the same bug class as the already-fixed `SellResponse`. | **SOLVED — confirmed live 29 Aug, `HERMES_256_BUYITEM553` now default on** | §137.5; bought from a vendor and the right item arrived |
| T-F | **Repair is not a packet problem.** Neither era sends a response; both mutate durability and money. It is entirely a values-update problem, and **`ItemData` has no measured values numbering at all** (`bit-inventory.md` has no `ItemData` section). Expect "money went down, durability bar did not move". Do it **last**. | **open — lowest priority** | §137.7 |
| T-G | **`TrainerList.TrainerID` is SAFE — no seam-rule trap. Measured 28 Aug, do not spend a session on it.** The client has **no `Trainer` DB2 store to resolve against**: `db2map.py` enumerated **940** stores from the client image and none is named Trainer; our own `DB2Hash.cs` has **886** entries and none; TrinityCore's `DB2Stores.cpp`/`DB2Metadata.h` have none either and keep trainers in the world DB (`Entities/Creature/Trainer.cpp`). The only bare `Trainer\0` in the binary (`.rdata` RVA 0x034474A0) sits inside the `GossipOptionNpc` enum-name table. Confirmed **positively** as an echo token: capture #13 [1013] sends `TrainerID = 22` straight back in `CMSG_TRAINER_BUY_SPELL`. The cmangos creature entry at `NPCHandler.cs:155` is fine. | **CLOSED — safe** | `db2map.py` reproduced §129's controls exactly (`AnimationData 0..801 / 802 rows`, `SpellVisualKit 12 rows`), so the readings are sound |
| T-G2 | **`VendorItem.ExtendedCostID` IS exposed — the real §129 risk here.** Store `0x04141740`, ids **1..11456** over only **371 rows** — 3.2 % dense, so a legacy id almost certainly *misses*. Unlike the Blizzard capture (0 on all 14 items) the exposure is live on our path: `Client/NPCHandler.cs:132` forwards cmangos' `ExtendedCost` column **verbatim**. Hits honour/badge vendors, not food and water. **Whether a miss is fatal or a benign null is still open.** `ItemExtendedCost` *could* be hotfix-served — `DB2Hash.ItemExtendedCost = 0xBB858355` exists and `CMSG_HOTFIX_REQUEST` already pre-pushes 18 tables — but cmangos 2.4.3 has no server-side source, so the data would have to come from a 2.4.3 DBC dump. | **open** | `db2map.py`; `HotfixHandler` paths verified, not assumed: `CMSG_DB_QUERY_BULK` serves 3 tables, `CMSG_HOTFIX_REQUEST` 18, neither includes it |
| T-H | **Tooling bug fixed and `bit-inventory.md` regenerated — §137.8's prediction confirmed.** `opcodes69110.py`: `0x460188` → `SMSG_THREAT_UPDATE`, `0x46018D` → `SMSG_TRAINER_LIST`, and `0x460184` → `SMSG_UNK_460184` (its reader is `U8 U8 BYTES`, no guid, so it cannot be a threat update). `gt_session.py`'s non-existent `SMSG_SELL_ITEM` → `SMSG_SELL_RESPONSE`, plus `CMSG_LIST_INVENTORY`/`CMSG_BUY_BACK_ITEM` added. A `"threat"` event was added so the correction **relabels** rather than deletes — without it the tags simply vanished, the trainer list firing exactly once in the whole corpus. **Result: every `trainerxN` is gone, and the APD quartet 37/52/101/104 now reads `meleex4,castx3,threatx3` — an unambiguous combat-state lead.** The mislabelling was the only evidence any APD bit was trainer-related; there is now none. §136's line at 6263 corrected in place. | **DONE** | regenerated inventory; `grep trainer bit-inventory.md` empty |
| T-I | **New lead from the relabelling: `UnitData` bit 21's top neighbour flipped to `threatx11`** (was `meleex8`). Its 68 solo samples are packed guids of 2/9/10/11/12/14 bytes — a guid-valued field that changes with threat. That is **`Target`-shaped**, and it is the strongest new lead in the file. | **open, unworked** | regenerated `bit-inventory.md` |
| T-J | **OQ-1 SETTLED: the byte at offset 10 is a plain `u8 TrainerType`,** not a 2-bit field. The reader calls the u8 primitive once and stores the whole byte with **no shift and no mask**. The contrast is inside the same function: the greeting length reads two bytes and assembles 11 bits MSB-first, discarding the low 5 — that is what bit-reading looks like here, and a `WriteBits(2)+FlushBits` field would need a `>>6` that does not exist. | **CLOSED** | client reader `0x5BBB90`, reached via GetId stub `0x5BBDA0` (`mov dword ptr [rdx], 0x46018D`) → dispatcher `0x6229DC` → ctor `0x5BBDB0`; `.pdata` 3 chunks, 470 B |
| T-K | **OQ-2 SETTLED: WPP 5.5.0's field names and order are right.** The element is stride 0x24 / 34 B on the wire, and a **three-trip inner loop** (esi=3) pins `ReqAbility[3]` at +0x10/+0x14/+0x18 *structurally*, which forces `ReqSkillLine, ReqSkillRank` before it and `Unk440` after — exactly `NpcHandler.cs:279-288`. Bonus: `0x460188`'s own reader is `GUID U32 GUID U64`, so the client independently confirms the T-A eviction. | **CLOSED** | same reader walk |
| T-L | **T-E SETTLED — WPP is right and we are wrong.** No capture on disk contains a `CMSG_BUY_ITEM` (`w13_c2s`, `w15_c2s`, `world11_c2s` all checked), so it was settled against the **client's serialiser**: `mov edx, 0x3F002F` at `0x65A468`, function `0x65A450` writes `opcode, guid, guid, u32 x4, then ItemInstance last, with no trailing bit field`. Controlled against two measured siblings by the same method: `CMSG_SELL_ITEM` = `0x65A360` → 26 B (matches the ten live sells) and `CMSG_TRAINER_BUY_SPELL` = `0x65A730` → 18 B (matches [1013]). Today `ItemPackets.cs` reads three u32, so `Item.ItemID` lands on the `ItemType` word and the id forwarded to cmangos is garbage. | **CLOSED and CONFIRMED LIVE 29 Aug** | client writer walk; the buy round-trips and the item that arrives is the one clicked |

| T-M | **Nothing with a cmangos vendor/repair/banker flag was clickable at all — SOLVED, confirmed live 29 Aug.** The client selected the unit (`CMSG_SET_SELECTION`) and then sent **nothing**: no `CMSG_TALK_TO_GOSSIP`, no `CMSG_LIST_INVENTORY`. Cause: **this engine reaches every NPC service through gossip, and cmangos does not set the `Gossip` bit on a plain vendor** because the 2.4.3 client had a direct path this one no longer has. Live agrees — capture #13 opens the vendor via `CMSG_GOSSIP_SELECT_OPTION`, never a bare `CMSG_LIST_INVENTORY` (§137.1). **Not an alignment fault:** the create blocks of the working and non-working NPCs are identical apart from `NpcFlags`, and the right value was on the wire. Applied in **both** the create and values writers — cmangos re-sends `NpcFlags` on quest-state changes and would otherwise clear the bit mid-session. | **SOLVED — `HERMES_256_NPCGOSSIPBIT`, now default on** | Anvilmar, 29 Aug: all seven trainers carry `19 = Gossip\|QuestGiver\|Trainer` and work; Adlin Pridedrift (`130 = QuestGiver\|Vendor`), Wren Darkspring (130), Rybrad Coldbank and Grundel Harkin (`4224 = Vendor\|Repair`) carry no bit 0 and were dead. Durnan Furcutter (`4227`, has bit 0) worked. **Exclusion worth keeping: Adlin carries `QuestGiver` and the client does not act on that either — it is bit 0 specifically, not "any service bit".** |
| T-N | **Buyback tab always empty — SOLVED, confirmed live 29 Aug.** Selling worked (money returned, the item guid reached `InvSlots[94+n]`) but nothing rendered, because **`BuybackPrice` and `BuybackTimestamp` were never emitted on the values path at all.** Numbering measured, not derived: **gate 338, `BuybackPrice[12]` at 339+i (u32), `BuybackTimestamp[12]` at 351+i (i64)**, one gate for both arrays. | **SOLVED — `HERMES_256_BUYBACKVALUES`, now default on** | ten sells in `ground-truth/w13_s2.bin` decoded at the **15-bit** APD mask width: ten blocks, every one consuming to its last byte, each setting `{32, 42, 149, <bag slot vacated>, <InvSlots 94+n>, 338, 339+n, 351+n}` with n advancing 0→9. The two arrays are exactly 12 apart in all ten pairs. **Confirmed by arithmetic rather than pattern-match:** `Coinage` rides in the same block and its delta between consecutive sells IS the price in the later block — 6+5=11, 11+3=14, 14+4=18, 24+7=31, 31+12=43, 55+3=58, 58+15=73, seven exact closures |
| T-O | **Buying back failed with "item not found" — SOLVED, confirmed live 29 Aug.** Not a buyback bug: `AdjustInventorySlot`'s 69110 arm maps its **tail against Vanilla's slot table** while the legacy server is TBC 2.4.3, and **omitted the buyback range entirely**, so modern slot 94 fell through to `return slot` — and TBC slot 94 sits inside its **keyring** range 86-117, so cmangos looked up a keyring slot. Two more of the same defect corrected together and **explicitly not separately tested**: bank bags targeted 63 (Vanilla `BankBagStart`; TBC is 67) and the keyring targeted 69, which is Vanilla's *buyback* start (TBC's keyring is 86). Bank items only looked correct because Vanilla and TBC both start at 39. Bounds now resolve through the legacy build's own constants so this cannot drift again. | **SOLVED — `HERMES_256_INVSLOTMAP`, now default on** | full round trip logged: guid leaves bag slot 45 -> `InvSlots[94]` + `Price[0]=1`, then returns to bag slot 39 while 94 and its price clear; `Coinage` 9991->9990 closes against the price. Zero inventory-change failures in the session |
| T-P | **`bit-inventory.md`'s entire `ActivePlayerData` section is decoded at the WRONG mask width.** `gt_apddec.py:31` and `gt_bitinventory.py:24` both use **14**, while `befc8d2` established the APD mask header is **15 bits** and the shipped encoder uses 15. Decoding at 14 shifts every observed bit down by 33 — the file's "Coinage = bit 9" is the encoder's bit 42, the same field. **Any APD bit read out of that file today is wrong**, which also blocks the `ItemData`/repair track (T-F), since it needs the same tool. | **SOLVED 29 Aug — tools corrected and `bit-inventory.md` regenerated** | width 14->15 in both tools, `is_gate`/`proven` renumbered, and `SMSG_SELL_RESPONSE` added to `EVENTS` (it was missing, so every sell-correlated bit was invisible). Control: the regenerated file now reproduces the shipped encoder independently — Coinage at **42** with an 8-byte solo payload, InvSlots gate **149**, Buyback gate **338** appearing exactly **10** times for the ten sells. APD identification went 27/58 -> **48/59** |
| — | **Buyback timestamps are not Unix time on our path.** Blizzard sends Unix seconds (`1787865557`); cmangos' value arrives as `108007`, apparently relative. The item renders regardless, so display does not depend on it — but WoW expires buyback entries after 30 minutes, so if entries never expire or vanish immediately, this is where to look. **Not touched: what the client does with the field is unmeasured.** | **open, low priority** | `Stamp[0]=108007` in the 29 Aug log vs live `1787865557` |

| Q-1 | **SOLVED, confirmed live 29 Aug — `IsQuestFlaggedCompleted` works.** `QuestCompleted` is **`BitVectors[11]`** in `ActivePlayerData` (client store `obj+0x13A8` = `obj+0x1140 + 11*0x38`), a dynamic array of `u32 count` + `count x u64`; live sends **64** words. An earlier version of this row said the field was NOT in ActivePlayerData — that was **wrong**, and instructively so: it is a dynamic array and `clientfields-ActivePlayerData.json` was walked over OUR block where the count is 0, so the element loop was never entered. A walk over a block that does not contain a thing cannot prove the thing absent. **The proxy needed no new logic** — `CompletedQuestTracker` already indexed `(questBit-1)>>6 / &63`, its store is persisted, and `UpdateHandler.cs:392` already filled the array; the only missing piece was one entry of one array in the writer. Confirmed on the wire: count 64 at the offset the client reads, `word[2]` bit 59 set for quest 179, and the announced set is exactly the two quests cmangos has `rewarded=1`. | **SOLVED — `HERMES_256_QUESTCOMPLETED`, now default on** | `/run print(C_QuestLog.IsQuestFlaggedCompleted(179))` false -> true |
| Q-1d | **The emit index is 9, not 11 — and that is the APD overhang made concrete.** The client stores the count to `obj+0x13A8`, its `BitVectors` entry 11. But `clientfields` (walked over our own block, flags=7) shows the reader taking its FIRST `BitVectors` count at APD-rel **4794** while our loop's first write lands at **4802** — so it consumes 8 bytes we emit before the loop as its entries 0 and 1, and its entry 11 is our entry 9. Proven by measurement, not reasoning: at index 11 the payload emitted at APD-rel 4846, the count the client read at 4838 was still 0, and the API stayed false. **This is the first time the ~271-byte overhang (reader consumes 6288 of the 6559 we emit) has been localised to a specific 8 bytes at a specific field.** Fixing it is the remaining APD-tail work, and **when it is fixed the index must go back to 11** — it is pinned to the bug, not to the format. Two search criteria I gave the agent were also measured wrong and are recorded so nobody reuses them: on Blizzard blocks the APD walk **never** terminates normally (the known-good `ap_rowine@3640` aborts too, after consuming its block in sync), and every cleanly-terminating base was a zero-path false positive. | **open — 8 bytes localised, overhang not yet fixed** | index-11 vs index-9 runs |
| Q-1a | **`0x4601f4` (our `SMSG_DURABILITY_DAMAGE_DEATH`) is NOT the completed set — excluded by cross-character comparison.** It looked ideal: ~680 B, in the login init block between spell-charges and currency, and its length varies per character (688 B in #13, 672 B in #16). But its entries are **byte-identical across two different characters in two different sessions** (2259, 2260, 2261, 2262, 2263, 2264, {17223,1}, {17647,1}, {17648,1}, {3085,379} …), so it is a global table, not per-character state. Recorded because the packet is otherwise a perfect decoy and will be found again. | **excluded, measured** | `w13_s2` [28] vs `w16_s2` [19] |
| Q-1b | **Three brute-force searches for the array failed to converge — do not repeat them.** (1) structural scan of the 2.8 GB dump for the pointer+count signature, even with heap-only, vtable-into-module and sparse-bitmap filters: **4115 candidates**, top hits full of pointers. (2) the same anchored on the player's ObjectGuid taken from the capture: **956 candidates**. (3) a full opcode inventory of the login window in four captures: nothing bitmap-shaped; the quest family 0x640000-0x640023 is complete and carries no bulk set. Also dead: the player-object getter rva 0x1807720 has **2367 callers** (it is the generic local-player getter) and its prolog is an Arxan stub with no RIP-relative global to follow. **The remaining instrument is the one the `wow-client-re` skill names: a live breakpoint on rva 0x1A58840, which receives the player object in a register — no scanning, no guessing.** | **open — next step named** | this session |
| Q-1c | **Capture #16 is a sixth live ActivePlayer create, and it is the SAME character as `ap_rowine.bin`** — "Rowine" is at offset 3583 in `ap_w16.bin` and the QuestCompleted words are byte-identical across both. An earlier version of this row called it "a different character (Gnome warrior)"; measured false. Its value is being a **second capture of one character**, which is what made the byte-identical cross-check possible. Artifacts: `ap_w16.bin` (11360 B), `ground-truth/w16_s2.bin`, `w16.pkt`, `w16_parsed.txt`, `updates_16.bin`, `qcompleted_rowine.bin`. Block map, measured: ObjectData 0-18, UnitData 18-922 (904 B), PlayerData 922-3632 (2710 B), **ActivePlayerData 3632-11360 (7728 B)** — note the APD start is **3632**, not the 3640 this plan documented. | **asset landed** | key in 73 s; 446/446 decrypt |

| C-1 | **Character creation always failed — SOLVED, confirmed live 29 Aug.** `CreateCharacter.Read()` is missing `i32 TimerunningSeasonID` between the customization count and the name (WPP 5.5.0 `CharacterHandler.cs:794` reads it), so the **name** was read as that field's four zero bytes. **One root cause, two symptoms, and the loud one pointed at the wrong thing:** the client showed "failed" with `SMSG_CREATE_CHAR` code 26 (`CharCreateFailed`), while cmangos' own `Server.log` said `Account:[5] attempted to create character of invalid Class (0) or Race (0)` — because the NUL name is forwarded as a cstring, the emulator stops at the first NUL, consumes one byte, and reads race/class out of the remaining padding. Race and class were being read correctly all along. Settled from the raw body, not from the reference: `10 80 | 03 01 01 | 05000000 | 00000000 | 66 67 66 67` = bits, race 3, class 1, sex 1, count 5, **the skipped i32**, then the real name. Note `GetData()` includes the 4-byte opcode; the body starts at +4. Result code 26 -> **24 CharCreateSuccess**, `Create Character:[Dfgd] (guid: 2)` in `Char.log`, row present in `characters`. **The missing field is version-independent, so this is an upstream HermesProxy bug rather than a 69110 deviation — a real PR candidate.** The knob also adds WPP's 4th leading bit (`HardcoreSelfFound`), which is **inert**: `ReadUInt8` calls `ResetBitPos()` and 6+3 and 6+4 bits both flush to byte 2. It is included because the reference has it, not because it fixed anything. | **SOLVED — `HERMES_256_CHARCREATE553`, now default on** | raw body in the log; cmangos `Server.log` + `Char.log`; `characters` table |

**Built 28 Aug, all four knobs default OFF, compile-checked out of tree, not committed:**

| knob | file | effect when ON |
|---|---|---|
| `HERMES_256_TRAINEROPCODE` | `NPCPackets.cs` | trainer list ships under `0x46018D` |
| `HERMES_256_TRAINER553` | `NPCPackets.cs` | `u8 TrainerType` header + 34-byte element (`Unk440` written 0 — no 2.4.3 source, zero on live) |
| `HERMES_256_LEARNEDSPELLS3` | `SpellPackets.cs` | third leading u32 in the 5.5.x arm |
| `HERMES_256_BUYITEM553` | `ItemPackets.cs` | the measured 4×u32-then-`ItemInstance` read |

`Opcode.cs` now carries the corrected numbers with the evidence in the comment. **Nothing in the
proxy *sends* a threat message, so that move is wire-inert:** with `TRAINEROPCODE` off, `TrainerList`
is constructed as `Opcode.SMSG_THREAT_UPDATE` and still ships `0x460188`, byte-identical to today.
**Session heads-up: with the knob off, logs now name trainer lists `SMSG_THREAT_UPDATE`. That is
accurate, not a bug.**

Both §137 traps were handled: every change is in **both** `Write()` and `WriteToSpan()` (the
`ISpanWritable` arm is the one that ships), and `SpellSize` was raised 30→**34 unconditionally** —
sized for the larger element regardless of knob state, because under-renting the pooled buffer is
the failure that matters. `LearnedSpells.MaxSize` got the same +4.

**The session, batched.** `TRAINEROPCODE=1 TRAINER553=1` **together** — neither works alone by
construction (opcode-only delivers a wrong body to the right reader; body-only delivers a right body
to the threat reader), and they are split so a negative says *which half* was wrong. Then
`LEARNEDSPELLS3=1`: buy a spell and check the learned id is the one you bought. Then `BUYITEM553=1`:
buy from a vendor, and the discriminator is whether cmangos receives the right item id. Script for
the human: right-click a general-goods vendor, buy one stack, sell it back, open the buyback tab,
repair at an armourer, then a class trainer — open, read the list, train one spell. If the trainer
frame still misbehaves with 1+2 on, T-G says `TrainerID` is not the cause, and that exclusion is
worth having.

**Build-environment trap, cost two agent cycles — read before delegating.** `dotnet` on PATH is
`C:\Program Files\dotnet\dotnet.exe`, a **runtime-only 8.0.8 install with no SDKs**, so a plain
`dotnet build` anywhere returns "No .NET SDKs were found". The real SDK 10.0.400 is at
`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. An agent that hits this in a scratchpad copy will
conclude the copy is broken and retry **in-tree**, where it appears to work because the repo's `obj`
is already primed — and a copy blocked by the running proxy leaves a `bin/Debug` DLL **missing its
embedded resources**, which fails only at startup with `Resource not found: 'HermesProxy.BNetServer.pfx'`
and is invisible to a later incremental build. **Observed twice, 28 Aug, each time costing a live
test: once an agent has run `-t:Compile` against the repo's `obj`, every subsequent plain
`dotnet build` is a no-op that leaves the resource-less DLL in place — it reports `0 Error(s)` and,
tellingly, `0 Warning(s)` where a real build of this project reports 60. Only
`dotnet build --no-incremental` repairs it (~2m45s). Do not verify the repair by grepping the DLL
for `BNetServer.pfx`: the resource NAME is present in a broken build too, so the string check passes
while the data is missing. The only reliable check is that the proxy starts and binds WorldSocket.**
Recipe for an out-of-tree check:
`robocopy <proj> <dest> /E /XD bin obj` for `Framework`, `HermesProxy`, `HermesProxy.SourceGen` plus
`Directory.Packages.props`, `.editorconfig`, `HermesProxy.sln`, `GitVersion.yml`; then
`-p:DisableGitVersionTask=true` **and** a throwaway `GitVersionInformation` stub, or you get 22
`CS0103` errors that look like real compile failures.

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
