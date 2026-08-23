# HermesProxy

WoW protocol translation proxy — allows modern retail clients to connect to legacy server emulators by translating between protocol versions.

## Solution Structure

| Project | Purpose |
|---|---|
| `Framework` | Shared library: networking, cryptography, packet I/O, protobuf, utilities |
| `HermesProxy` | Main proxy executable (console app) |
| `HermesProxy.Tests` | xUnit test suite |
| `HermesProxy.Benchmarks` | BenchmarkDotNet performance benchmarks |

## Build & Run

```bash
dotnet build                                    # Build all projects
dotnet run --project HermesProxy                # Run the proxy
dotnet test                                     # Run all tests
dotnet run --project HermesProxy.Benchmarks -c Release -- --filter "*Name*"  # Run benchmarks
```

## Target Framework & Global Settings

- **.NET 10.0** — set centrally in `Directory.Packages.props`
- **Central package management** — all versions in `Directory.Packages.props`; projects use `<PackageReference>` without version attributes
- **Nullable** enabled solution-wide
- **Global using**: `System.Numerics`

## Code Style

- **PascalCase** for types, methods, properties, public fields
- **_camelCase** for private fields (leading underscore)
- **File-scoped namespaces** in newer code (`namespace Foo;`)
- **CypherCore GPL v3 headers** on legacy/ported files — preserve these when editing
- Prefer `var` when the type is obvious from context

## Performance Philosophy

- Zero-allocation hot paths — avoid allocations in packet processing loops
- `Span<T>` / `ref struct` for packet I/O (`SpanPacketReader`, `SpanPacketWriter`)
- `ArrayPool<byte>` for temporary buffers
- `FrozenDictionary` / `FrozenSet` for static lookup tables
- `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on hot-path methods

## Key Architecture

```
Modern Client <--BNet/TCP--> BNetServer  ──┐
                                           ├── HermesProxy ──> AuthClient ──> Legacy Emulator
Modern Client <---TCP-----> WorldServer ──┘                   WorldClient ──> Legacy Emulator
```

- **BNetServer** — accepts modern client Battle.net connections (TLS, protobuf)
- **AuthClient** — connects to legacy emulator auth/login server
- **WorldServer** — accepts modern client game connections
- **WorldClient** — connects to legacy emulator world server
- Packets are translated bidirectionally between modern and legacy opcodes

## Working on the 2.5.6 (69110) path

This build's create blocks are **linear and unmasked**: no field mask, no length prefix, no way for
the client to resynchronise. A wrong field width or order shifts everything after it. Three separate
client crashes and two hangs came from exactly that in one week. The rules below are what stopped it;
follow them where they apply and say so when they do not.

**Run the gate before anything reaches a live session.**

```
python tools-256-spike/gate256.py
```

Length, field order and what the client's own reader consumes are **three independent checks** and
none subsumes another — one block matched its byte total exactly while a 48-byte array sat in the
wrong place. The gate verifies *parsing*, never behaviour. Whether a bar moves or an item lands still
needs a session, so batch the knobs and spend one session on several.

**A search proves presence. It can never prove absence.** Every one of the five things declared to
need reverse engineering in a single night was already on disk, and every one was an *absence* claim
made after one grep came back empty: WowPacketParser's `ReadUpdate*` decoders, TrinityCore's
`WriteUpdate` encoders, `UpdateFieldsHandler553`, a finished Ghidra database nobody had queried, and
`.cyphercore/UpdateFields.cs` — 12,507 lines of C# implementing the very encoder being written from
scratch at the time.

So: **before writing "X does not exist" or "this needs RE", inventory rather than search**, and name
what you inventoried. Note that `.git/info/exclude` hides the most valuable files in this repo from
`git status` and from any review of changed files — `REFERENCE-256-CLIENT.md`, `tools-256-spike/`,
`.cyphercore/`, `.wpp/`, `START.md`, `HANDOFF-256-ENCRYPTED-MODE.md`. `ls` the repo root; do not
trust a status listing to tell you what is here.

Positive findings from a targeted lookup are sound — you saw the lines. Absence claims from the same
method are worth nothing.

The reference stack, in order, with the reason:

| for | use | why |
|---|---|---|
| field set, order, bit numbering | `.wpp/…V5_5_0_61735/…/UpdateFieldsHandler553.cs` | 69110 is a 5.5.3 engine arm |
| encoder algorithm, visibility masks | `/c/projekter/TrinityCore/…/UpdateFields.cpp` | mechanism is engine-level |
| anything the two disagree on | the client | it is the only authority |

**Stay on 553 — but not for the reason this file used to give.** "Never V2_5_5, it is the Classic
line and the source of three shipped faults" was measured on 23 Aug and retired: `wppdiff.py 255 553`
scores `UnitData` **0.996**, and all three faults it was blamed for (`VirtualItems`' position,
`VisibleItems` after `QuestLog`, the 78-byte tail) sit identically in 255, 553, 1158 **and** retail
11.2.7. They are 69110's own deviations; no reference carries them. Among the Classic-line handlers
the choice barely matters — stay on 553 because the writers are built on it, not to avoid a trap.

**Retail 11.x/12.x is the choice that does matter, and it is the wrong one** for these blocks:
`ActivePlayerData` 0.718, `UnitData` 0.842, `PlayerData` 0.844 against 553, diverging in exactly the
TBC content Anniversary needs and retail deleted. Mechanism is shared; the field set is not.

**Two axes, and they answer different questions.** The engine references decide a field's *layout*;
2.4.3 and our 2.5.2/2.5.3 writers decide whether it *exists* in this era at all. A field with no
legacy source is dead weight, and filling it with a plausible value is how two of this project's
crashes happened. Blizzard's published notes on what Anniversary changed are a legitimate source for
the presence question and have not been consulted — read them before deriving a subsystem's shape
from scratch.

**Compare like with like.** A "76-byte fault" was a creature layout held against a player layout —
`GetFieldVisibility()` returns `None` for a `Unit` and `Owner` for the player, which changes the
branches. Always state the visibility a measurement was taken at.

**Verify a diagnostic on the wire before building on it.** `HERMES_256_UNITTRIM` lowered a write
cursor but not the buffer length, so it never shortened a packet in its entire life; two agents
argued for hours over the number it produced. The block size is in the log as
`[256-spike] block: type=… values=N`.

**A legacy id is safe only in a field the client *stores*.** If the client *resolves* it, back it
with a record or send the table's documented "none" value. This build is TBC content on a modern
engine, so retail-sized bounds sit over era-sized tables and the holes are fatal — see
`REFERENCE-256-CLIENT.md` section 129 for the field-by-field inventory.

**Every behavioural change ships behind a `HERMES_256_*` knob defaulting to current behaviour**, so a
knob is something you turn ON. Prefer a change that does nothing by default over one that is
"probably right".

**Design for negative results.** A change that fixes something correlates; a change that does nothing
*excludes*, and excludes far more. The three most valuable measurements in this project all failed to
change anything.

`REFERENCE-256-CLIENT.md` is the evidence, `PLAN-256.md` the state and the work programme,
`tools-256-spike/model-256.md` the current layout model.
