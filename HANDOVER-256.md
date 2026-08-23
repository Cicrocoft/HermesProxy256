# Handover: 2.5.6 (build 69110), 23 August

Supersedes `START.md` and `HANDOFF-256-ENCRYPTED-MODE.md`, both of which describe a state we passed
long ago — they were written when the character list was the frontier. Do not act on them.

Read this, then `CLAUDE.md`'s "Working on the 2.5.6 (69110) path", then `tools-256-spike/model-256.md`.
`REFERENCE-256-CLIENT.md` is the evidence (132 sections) and `PLAN-256.md` the work programme.

---

## Read this part first, even if you skip the rest

**Four of this project's faults were its own diagnostics.** Not the protocol, not the client — the
instruments built to see it better:

| instrument | what it actually did |
|---|---|
| `HERMES_256_UNITTRIM` | never shortened a packet at all. `Truncate` lowered `_position` but not `_length`, and `GetData()` copies `_length`. Two agents argued for hours over numbers it produced. |
| `HERMES_256_APDPROBE` | wrote sentinels 7/13/19 into `Coinage`, `XP`, `NextLevelXP`. It ran for five hours while we investigated why money and XP read zero. |
| `HERMES_256_VALUESNOOP` | wrote the CGObject teardown byte while its own comment called it "a clean no-op". |
| the packet logger | `WorldSocket.SendPacket` calls `LogPacket` **before** the send, and nothing in that path caught anything. A throw there escapes past the send and the packet never reaches the wire. Latent, now guarded. |

The fourth is **latent, not established** as a cause of anything observed — and how it was almost
established is the more useful story. It was first reported with a signature: eight captures that day
ending on exact 64 KB `FileStream` boundaries, "what a flush that succeeds N times and then throws
looks like". Broken down per day the signature evaporates — **65 of 274 captures end on a 64 KB
boundary, across four days with 17+ GB free**. It is an ordinary buffered stream killed without a
clean shutdown, which is how most sessions end here. Two files had been picked out of a population of
sixty-five and read as a cluster.

The causal arrow also ran backwards. Measured: **our bad count → the client requests 34 GB → Windows
grows the pagefile to 25 GB → the disk hits zero → every process on the machine stalls.** The disk
filling is *downstream* of the fault, not upstream. Closing the client returned 14.4 GB.

What that leaves: while the disk was at zero, an unguarded throw in the log path *could* have dropped
sends — the mechanism is real, its occurrence is not shown. The guard is worth having because it
stops the loop closing on itself (our bug fills the disk, the full disk corrupts the wire, we then
analyse captures of our own corruption), not because it fixes anything.

**Measurements taken on 23 Aug while the disk was at zero remain suspect regardless** — the machine
was stalling on IO, and `ERROR #109` is "a thread was unresponsive for 60 seconds". That covers the
WDB-cache result and the `GOBFIX` correlation; treat both as unproven.

`CLAUDE.md` says to verify a diagnostic knob on the wire before building on it. **That rule is too
narrow.** It covers knobs; it does not cover loggers, dump hooks or probes. Treat any instrument
with the same suspicion, and make sure it cannot affect what it observes — the send path should
never be downstream of logging.

---

## What works, and what does not

**Working:** login (reliably — see below), world entry, movement, NPC movement, quest pickup,
gameobjects rendering, hostile creatures attackable, mob health tracking damage in combat, items
reaching the inventory UI.

**Broken:** an intermittent world-entry freeze/OOM (the one live fault — see "The open fault");
neutral creatures cannot be attacked; the quest log is empty; money and XP read zero; bags misfile
items; vendors do not open; chat is blocked; some friendly NPCs kneel.

### Closed on 22-23 August, each with the cause named

* **Login failed ~2 in 3.** `BnetSRP6v2Base.CalculateX` reinterprets a PBKDF2 output as signed when
  its top bit is set, and the salt is fresh per challenge — so which reading applied was a coin flip
  per attempt. Fixed by rejecting salts where the two readings can disagree. **This is an upstream
  bug affecting every SRP v2 client, not a 2.5.6 quirk.** Section 126.
* **Freeze at the loading screen.** `PlayerData.QuestLog` under `FieldVisibility.PartyMember` added
  1650 bytes to a block that was already displaced. Section 127, superseded in part by 131/132.
* **19.2 GB allocation.** `WriteItemData` ignored the visibility byte the client honours, so the
  socketed-gem count was read out of `Durability`. Section 128's method notes.
* **Access violation at world entry.** `GameObjectData.DisplayID` selects a model whose default
  AnimKit asks for animation 1672; `AnimationData` on this build holds the TBC subset, ids 0..801.
  Sending 1860 (the empty animation state) in `SpawnTrackingStateAnimID` stops the client starting
  that AnimKit. **Gameobjects rendered for the first time in this project.** Section 129.
* **Hostile mobs could not be attacked.** `UnitData.VirtualItems` sat between `FactionTemplate` and
  `Flags` where this build reads them at the block end at 23 bytes each, and a 78-byte tail was
  missing entirely — `Flags` was 48 bytes late. Section 131.
* **Health never changed in combat.** The values-update path had never been rewritten for this
  build: every update went out as a 2.4.3 masked array behind a modern header and was discarded.
  Section 124, and Track C below.
* **Crash on death.** Writing `changedFragmentMask = 0` for an empty delta **tears CGObject down**;
  the next update then calls a lazy constructor at `registry+0x78`, which is NULL for CGObject.
  Always write 3 plus a `u32 updateTypeFlag`, which is TrinityCore's and CypherCore's own empty form.

---

## The open fault

**Intermittent freeze (`ERROR #109`, 60 s) or OOM (`ERROR #8`) at world entry**, both the same fault:
the client reads a vector count as enormous inside `ActivePlayerData`'s **create** reader. A moderate
count loops; a large one asks the allocator for 34 GB and dies.

Shared stack frames across both: `0x729ACE`, `0x71AE22`, `0x17F7A2D` (the return from the third
create reader in `0x17F79B0`'s chain, which calls UnitData → PlayerData → ActivePlayerData with no
branches between them, so only the player's own create reaches it).

**What is measured and survives the disk contamination:**

* The count is **read from the stream**, not from the object (`0x72E3BA`).
* `0xFFFFFFFF` appears **zero times** in our 8178-byte blob at any alignment, and the last non-zero
  byte is APD+5135 with 1095 bytes of zeros after it. **At any cursor position inside our block the
  count reads 0.** The cursor must already be past the block's end.
* The player's ActivePlayer create is **byte-identical** between sessions that hang and sessions that
  do not — same 8382-byte record, values blob at offset 204, five bytes differing in the whole blob
  (XP, one skill, one float).
* The allocation size cannot be factored to name the field: the `×8` is a bucket-pointer array whose
  count goes through a float (`CVTSI2SS / DIVSS / CVTTSS2SI`), and at that magnitude a float has 24
  bits of mantissa, so many counts round to the same power of two.

**So the block is right and the cursor is wrong.** A dropped or truncated packet upstream explains
that exactly — but *which* upstream packet, and why, is not established. The logging bug is one
mechanism that could do it and is not shown to have done it.

**The first thing a fresh session should do is ten consecutive clean world entries on a healthy
disk**, with the guard in place and nothing else changed. That is the only way to know whether the
fault survives its own contamination. Every other measurement waits on it.

---

## The instrument: `python tools-256-spike/gate256.py`

Ten legs, all derived from the client itself rather than from references. Run it before anything
reaches a live session.

| leg | what it catches |
|---|---|
| `length` | a create block's emitted size changing (`blocklen.py`, per writer, against a baseline) |
| `order` | field order and object-offset drift across all nine descriptors (`fieldcheck.py`) |
| `reader` | what the client's own reader consumes over a real capture (`streamwalk.py`) |
| `update` | the values encoder as a faithful inverse of WPP 553, bit by bit (`updatemask.py`) |
| `wire` | the values blocks we actually sent, decoded from a `.pkt` (`valuesblock.py`) |
| `headroom` | a block ending inside a length-variable read |
| `frag` | fragment ids, header shape, and `updateTypeFlag` bits against the client's registry |
| `batch` | that every block in a real batch tiles the buffer exactly (`batchwalk.py`) |

**Length and order are independent** — one block matched its byte total exactly while a 48-byte array
sat in the wrong place. And `DynamicObjectData`'s fault produced **zero** index-wise disagreements
because every field after it was a same-width dword; only the count caught it.

**The gate verifies parsing, never behaviour.** Green means the client can read our bytes. It says
nothing about whether the values mean what we think — which is the entire remaining fault class.

Everything it does not cover, it prints: `ActivePlayerData` past wire +6126, `PlayerData`'s 7-byte
`DeclinedNames` tail, and the four maps walked over zeros.

---

## The model, and the reference stack

`tools-256-spike/model-256.md` is authoritative. In short: **69110 is TBC content on a 5.5.3 engine**,
and it is **5.5.3 plus a short list of measured deviations**, not a new build.

| for | use | why |
|---|---|---|
| field set, order, bit numbering | `.wpp/…V5_5_0_61735/…/UpdateFieldsHandler553.cs` | the exact engine arm |
| encoder mechanism, visibility masks | `.cyphercore/UpdateFields.cs` (C#, in this repo) or TrinityCore | engine-level, shared |
| whether a field exists in this era at all | cmangos 2.4.3 data, our 2.5.2/2.5.3 writers | presence, not layout |
| anything the above disagree on | **the client** | the only authority |

**Never V2_5_5.** It is the Classic line and the source of three shipped faults.

**The seam rule.** Retail-sized bounds sit over era-sized tables, and both crashes lived there: a
legacy id resolved against a table whose id space is retail-sized but whose contents are not. **A
legacy id is safe only in a field the client *stores*.** If the client *resolves* it, back it with a
record or send the table's documented "none" value. Section 129 has the field-by-field inventory;
`UnitData.DisplayID`, `NativeDisplayID`, `MountDisplayID` and `EmoteState` are still live and unproven
in tier 1.

**And the quieter half, which nothing catches:** an id that resolves but whose record drifted between
2.4.3 and 2.5.6. Nothing errors; the client renders something wrong. That is the likely shape of the
neutral-mob and kneeling-NPC faults.

---

## Running it

**`tools-256-spike/run256.sh` is the only supported way to start the proxy.** The whole 2.5.6
configuration lives in environment variables — `appsettings.json` is committed with 2.5.2 defaults
and no `AccountOptions` — and each omission fails differently and silently. It also stops whatever
is already running and prints what it stopped.

Traps that each cost a debugging round:

* The process is named **`.NET Host` / `dotnet.exe`**, not `HermesProxy`.
* A live proxy holds `Framework.dll`, so MSBuild's copy into `bin/Debug` fails with MSB3021/3027
  **while the compile reports no `error CS`**. Check the output file's timestamp, never the compiler
  text alone.
* The SDK is at `%LOCALAPPDATA%\Microsoft\dotnet`; the one on PATH in `C:\Program Files\dotnet` is
  runtime-only.
* `.git/info/exclude` hides `REFERENCE-256-CLIENT.md`, `tools-256-spike/`, `.cyphercore/`, `.wpp/`,
  `START.md` and this file's predecessors from `git status`. **`ls` the repo root.**

Knob defaults in the launcher all reflect proven behaviour. The ones that are opt-in and why:
`QUESTLOG` (geometry fine, contents empty), `INVSLOTS` (items land in the wrong slots — the mapping
is measured except for a ±1 on each group base), `ITEMZERO`/`ANIMRAW`/`NOGOBDISPLAY`/`APDTAIL`
(escape hatches), `DYNFIX`/`CORPSEFIX` (measured, untested live).

---

## Four knobs are built, measured and untested — run these first

All default off, all in `ModernDescriptors.cs` / `QueryPackets.cs`, all compiled into the current
binary. Each is one variable.

| knob | what it does | evidence |
|---|---|---|
| **`HERMES_256_PCFLAG`** | sets `UNIT_FLAG_PLAYER_CONTROLLED` (0x8) on the **player's** `UnitData.Flags` | `CGUnit_C::CanAttack` (rva 0x1906BE0) tests `Flags & 8` on **either** unit: neither set → attackable only at reaction ≤ 1; either set → reaction < 4. We send Flags = 0 for every unit, so the client is permanently strict. **This is why neutral creatures cannot be attacked.** |
| **`HERMES_256_UNITANIM`** | sends **1860** in `UnitData.StateAnimID` | TrinityCore emits `GetEmptyAnimStateID()` for this *and* for `GameObjectData.SpawnTrackingStateAnimID`, which section 129 measured as 1860 and which is what made gameobjects render. We fixed one and left the other at 0. Candidate for the kneeling NPCs. |
| **`HERMES_256_CREATUREQUERY`** | 2.5.6 body for `SMSG_QUERY_CREATURE_RESPONSE` | Layout walked out of the client's own reader at rva **0x6B4580**: three type-flag u32 (not two), `CreatureType` u8, `Classification` one byte, and a second resize count for `QuestCurrencies`. `cqprobe.py` walks a synthetic body through that reader — the new layout is consumed exactly, 28/28 fields at the right offset, width and value; the current one runs **832,820 bytes** past a 147-byte body. |
| `HERMES_256_GOBFIX` / `DYNFIX` / `CORPSEFIX` | the three descriptor realignments | measured against the client's field maps; `GOBFIX` ran live once but during the contaminated window |

**`UnitCreatureType` returning nil is confirmed and exact**: the client takes `CreatureType` from the
byte where we write `Family`'s low byte. The troll is `Type=7, Family=0`, so it reads 0 = None. A
falsifiable side-prediction from the same analysis: a wolf (`Type=1, Family=1`) reads 1 = Beast **by
coincidence**, so `UnitCreatureType` should *not* be nil on a wolf even today.

**The over-read does not crash**, which is why this packet never appeared in a crash report:
`0x2D9E550` bounds-checks and clamps the cursor to `end+1` on failure. The cost is garbage fields,
196,608 no-op iterations and a **65,536-element vector resize per creature query** — and world entry
queries a batch at once.

### Two hypotheses of mine that were measured and are dead

* **Creature scale is not the query response.** `creature_template.Scale` for the wolf is 1, the wire
  `ObjectData.Scale` is 1.0 across 378 create blocks, and `UnitData.DisplayScale`'s only assignment is
  gated `RemovedInVersion(V2_0_1_6180)` — vanilla only, so it never runs against a 2.4.3 core. The
  squared-scale story was mine and it was wrong. The wolf's size lives in
  `CreatureDisplayInfo(855).CreatureModelScale = 0.4` in the client's own data.
* **The faction template does not drift.** The client holds all 605 of 2.4.3's ids; 602 are identical
  field-for-field, and the three that differ are nowhere near our creatures. Computing reaction from
  the client's own records reproduces both observed values exactly — `UnitReaction=4` on the wolf is
  **correct**. `tools-256-spike/db2rows.py` decodes any DB2 store's contents from client memory and
  its `--selftest` re-runs that diff.

**Unexplained and not to be built on:** `creaturecache.wdb` reached 7.9 MB. The cache stores raw
response payloads of 129–176 bytes, so ~56,000 records would be needed, which implies re-appending.
Nobody has read the WDB write path. A failed parse is a precondition either way.

## What to do next, in order

1. **Ten consecutive clean world entries** on a healthy disk. Nothing else is worth measuring until
   the freeze is either gone or confirmed on clean data. The bar is ten because the per-attempt
   failure rate looked like a half to three quarters, and five clean entries would still be 3% luck.
2. **`SMSG_QUERY_CREATURE_RESPONSE`** — `QueryCreatureResponse.Write` has **no version branch**; it
   sends the Classic-line body. Measured against 553: two type-flag u32 where it reads three,
   `CreatureType` i32 where it reads u8, `Classification` i32 where it reads i8, a missing
   `QuestCurrencies` u32; net −2 bytes. Plausibly explains `UnitCreatureType` returning nil, creature
   scale, and the 7.9 MB `creaturecache.wdb`.
3. **The faction-template question** — does the client's `FactionTemplate` record for a given legacy
   id mean what 2.4.3 meant? If not, push TBC semantics as a hotfix; that mechanism is already used
   eighteen times in `GameData.cs` and `DB2Hash.FactionTemplate` exists. Do not change the server's
   data: cmangos's own AI reads those tables, and the client reads its own regardless.
4. **The values ladder** — `HERMES_256_VALUESUPDATE` 2 (fixed arrays), 3 (money and XP), 4 (quest
   log). Each step is one variable and a failure localises.
5. **`InvSlots`** — 146 guids at `obj+0x1D88`, stride 0x10, group spacings identical to 3.4.3. One
   ±1 per group remains, settled by one probe login.
6. **Chat** — no packet leaves the client; "You cannot speak that language" is its own check, and
   languages are spells. Look at `SMSG_SEND_KNOWN_SPELLS`, whose 185-byte body has a `u32` zero
   between the count and the ids.

Track B (the 35 SMSG writers against 553) is untouched and is where the remaining crash candidates
live, including the loot family — **zero `SMSG_LOOT_*` in 168,320 logged sends**, so it has never run.

---

## Before this becomes a PR

Remove the `[256-spike] CAPTURE` logging — **it prints key material in clear text**, and those logs
must not be shared. Then the `FIXME(256-spike)` markers, the packet dumps, the descriptor-range
logging and the thirty-odd knobs. `VersionChecker` still borrows 3.4.3's response codes. Smoke-test
1.14, 2.5.x and 3.4.3 — all three are byte-identical today and must stay so.

The SRP salt selection and the both-conventions search in `DoVerifyClientEvidence` should **stay**:
they fix a real upstream bug, not a 2.5.6 quirk.
