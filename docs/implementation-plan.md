# Technical Implementation Plan

Status: approved plan, implementation not started. Companion to
`docs/unit-packs-and-match-loop.md` (the content-system + match-loop design,
including the resolved decisions in its §13: 32×48 landscape board, free
camera, commanders, medieval theme on Mechabellum-ratio numbers at ÷6,
prorated survivor value, multiplayer-first ordering).

The goal state: **you author units, commanders, and every game rule through an
editor interface; a build pipeline turns that data plus Unity art into a
playable game; the engine simulates battles server-authoritatively; a Unity
client plays it on mobile (landscape) against a deployed server.**

---

## 1. Target architecture

```
                       ┌─────────────────────────────┐
                       │  apps/forge  (content editor UI)│
                       │  Bun.serve + React, localhost   │
                       └────────────┬────────────────┘
                              reads/writes
                                    ▼
   core/types ────schemas───► core/catalog/data/*.json  ◄── you, or any editor
   (zod: protocol,                  │
    catalog, rules)                 │ build.ts (validate + lints + canonicalize)
        │                           ▼
        │ generate-csharp.ts   core/catalog/dist/catalog.json + .hash
        ▼                           │
   dotnet/BoardGame.Core ◄──────────┼──────────────► apps/game-server (Bun, interim)
   (engine: rules + sim,            │                apps → dotnet/BoardGame.BattleServer
    dual-homed into Unity)          │                (.NET 8, WebSockets, SQLite)
        │                           │                        │
        ▼                           ▼                        │ welcome{catalogJson}
   apps/game-client (Unity 6) ◄─ StreamingAssets fallback ◄──┘ battleLog
   landscape, free camera, mobile
```

One rule above all: **sim data exists only in catalog JSON**; the same built
bytes are read by every runtime. Codegen emits *types*, never data.

Target repo tree (new items marked +):

```
core/types/                      # + catalog-schema.ts, protocol v2, scripts/generate-csharp.ts
+ core/catalog/                  # data/packs/*.json, data/match-rules.json, scripts/build.ts, dist/
+ apps/forge/                    # content editor (Bun.serve API + React UI)
apps/game-server/                # interim Bun server; evolves to match loop (M3); retired at M5
apps/game-client/                # Unity 6 client
+ dotnet/BoardGame.sln
+ dotnet/BoardGame.Core/         # engine (netstandard2.1, C#9, UnityEngine-free, dual-homed UPM pkg)
+ dotnet/BoardGame.Core.Tests/   # xunit + balance harness
+ dotnet/BoardGame.BattleServer/ # .NET 8 production server
+ apps/battle-server/            # thin bun wrapper package delegating to dotnet CLI (turbo integration)
+ .github/workflows/ci.yml
```

---

## 2. Workstream A — Content platform

### A1. Catalog package (`core/catalog`)

- `data/packs/base.json` — the medieval base ContentPack: the six existing
  units re-authored (footman = chaff swarm ×24, archer = ranged squad,
  whelp = cheap flyer, holyKnight = bruiser, barracks/cathedral = buildings)
  plus enough roster for real matches (ballista = artillery volley,
  arbalest = sniper, warBanner = aura support, gargoyle = anti-air, …).
  Stats transcribed from Mechabellum at ÷6 with a `_source` naming-map comment
  per unit for tuning reference.
- `data/packs/base.commanders.json` — commanders: `{id, name, description,
  hp, startingUnits[], ability: [statMod|economyMod]}`. 3–4 to start
  (e.g. Warlord: +1 deploy slot / lower HP; Steward: +50 income; Zealot:
  starts with holyKnight escort / higher HP).
- `data/match-rules.json` — board {w:32, h:48}, halves at col 24 (no gap),
  income 200×N, 2 deploys/round, timers 70s/120s, leveling, tech escalation,
  startingBuildings (cathedral + barracks per side).
- `scripts/build.ts` — zod-validate every file, run cross-pack lints (unique
  ids, refs resolve, formation length == count, offsets inside footprint,
  footprints fit deploy zones, spawn-chain depth cap), canonicalize
  (sorted keys, minified) → `dist/catalog.json` + sha256 `dist/catalog.hash`.
- Package scripts: `build`, `check` (build + `git diff --exit-code dist`),
  `test` (lint suite as bun tests). Wired into turbo `build`/`test`.

### A2. Schemas & codegen (`core/types`)

- `catalog-schema.ts`: contentPack / unitDef / weapon / trigger / effect /
  status / zone / tech / commander / matchRules (shapes per design doc §5).
- `protocol.ts` v2: replaces the v1 enum-based protocol. Message set per
  design doc §8 (`join/welcome/pickCommander/buySquad/moveSquad/sellSquad/
  unlockUnit/buyTech/buyLevel/setReady/battleAck` → `welcome/phase/
  cmdAccepted/cmdRejected/revealSnapshot/battleStarted/battleLog/roundResult/
  matchEnded/error/pong`).
- `scripts/generate-csharp.ts` — zod→C# emitter covering exactly the subset
  used: objects, `kind`/`type`-discriminated unions (abstract base + generated
  Newtonsoft converter), enums, arrays, optionals, `ScaledValue`. Emits
  `CatalogDto.g.cs` + `ProtocolDto.g.cs` into
  `dotnet/BoardGame.Core/Runtime/Generated/` (checked in). A bun test
  regenerates to a temp dir and byte-compares — drift fails CI. Budget: if the
  emitter exceeds ~400 lines, stop and hand-maintain DTOs instead.

### A3. Forge — the customization interface (`apps/forge`)

A local-only web app (no auth, no DB; the repo's git is the undo button).
Backend: `Bun.serve` on `localhost:7780` serving the built React UI plus a
JSON API. Frontend: Vite + React + TypeScript, importing the zod schemas
directly from `@core/types` so forms and validation are always in sync with
the real contract.

API:
- `GET  /api/catalog` — parsed catalog + validation report
- `GET/PUT /api/packs/:file` — read/write one pack (PUT validates with zod +
  lints, writes canonicalized JSON; rejects invalid with field-level errors)
- `GET/PUT /api/match-rules`
- `POST /api/build` — run catalog build; return hash + lint results
- `POST /api/balance` — (M4+) spawn the dotnet balance harness
  (`lineupA vs lineupB --seeds N`), stream winrate/length/damage tables
- `GET  /api/schema` — JSON-schema export of the zod shapes (drives form
  generation)

UI (left-nav sections):
- **Units** — stat fields, cost/tier, footprint editor, **formation grid
  editor** (click members onto the footprint grid, orientation preview),
  weapons list with fire-mode sub-forms, **ability builder** (trigger picker +
  effect list composed from the schema), techs.
- **Commanders** — hp, passive ability mods, starting units placed on a board
  mini-map.
- **Statuses / Zones** — shared tables.
- **Match Rules** — board, zones, economy, timers, leveling.
- **Build** — build button, hash display, lint panel, diff-before-save.
- **Balance** (M4+) — matchup matrix runner over the headless sim.

Phasing: **Forge v0** (browse + validate + build button) in M1;
**Forge v1** (full form editing incl. formation/commander editors) alongside
M3; **Forge v2** (balance tab) with M4. Deferred indefinitely: hosting, auth,
multi-user, in-browser sim preview.

### A4. Unity-side authoring

- `UnitPackManifest` ScriptableObject per pack (unitId → wrapper prefab, icon,
  ProceduralProfile, per-weaponId VFX; pack tables for status/zone/ability FX).
- **Pack Validator** EditMode test: every catalog unitId has a manifest entry
  or is consciously placeholder; every manifest entry references a live
  catalog id; weaponIds match.
- **Formation Preview** editor window: renders catalog formations + footprint
  + orientation toggle over the grid.
- Asset intake pipeline for Meshy-generated models (documented convention):
  import → decimate/LOD → wrap in normalized prefab (origin feet-center,
  +Z forward, uniform scale) → manifest entry. Placeholder capsule fallback
  means art never blocks gameplay work.

---

## 3. Workstream B — Engine (`dotnet/BoardGame.Core`)

netstandard2.1, LangVersion 9 (Unity 6 compatible), `noEngineReferences`
asmdef + UPM `package.json` (`com.boardgame.core`), consumed by Unity via
`manifest.json` `file:` reference; `Directory.Build.props` redirects bin/obj
out of the package folder.

Modules (Runtime/):
- `Generated/` — DTOs (from A2).
- `Catalog/` — CatalogLoader (parse, schemaVersion gate, hash-as-received,
  indexed lookups), Footprint/Formation functions (center-relative offsets,
  orientation = swap w/h + rotate (x,z)→(z,−x)) shared by placement and sim.
- `Match/` — Placement validation (bounds, own-half, overlap — port of the
  ~50 BoardManager lines), MatchRules access, blueprint model (SquadCard),
  economy arithmetic (income, costs, tech escalation, level pricing).
- `Sim/` — the battle engine. Fixed 20 Hz tick, phases: statuses/zones →
  scheduled triggers → targeting (soonest-attackable, sticky, id tiebreak) +
  steering movement → weapons (instant/volley/beam-ramp) → projectiles →
  FIFO effect queue (death cascades, depth cap 8, XP attribution) → cleanup/
  events/end-check. SquadState + struct-of-arrays members, 8-tile spatial
  hash, seeded xorshift consumed in fixed phase order. Damage pipeline and
  stat stacking per design doc §6. Mid-battle spawns = new synthetic squads.
- `Events/` — BattleEvent DTOs (~18 types per design doc §7) + IBattleEventSink
  (log collector; null sink for headless balance runs).
- `Xp/` — kill/damage-share attribution, level thresholds.

Testing (`BoardGame.Core.Tests`, xunit):
- Golden-scenario tests per effect/trigger primitive.
- **Repeat-seed determinism test from day one** (same inputs+seed → identical
  event log bytes).
- Placement property tests (mirror of the Bun room tests).
- **Balance harness CLI**: `dotnet run --project BoardGame.Core.Tests --
  fight "base.footman×5" vs "base.ballista×2" --seeds 100` → winrate/length/
  damage tables (also the Forge balance backend).

---

## 4. Workstream C — Servers

### C1. Interim Bun server (M3 — evolves `apps/game-server`)

- `MatchRoom` reducer (pure TS, room.ts lineage): phases
  `lobby → commanderPick → planning(N) → plan-lock → battle(stub) → results →
  … → matchEnded`.
- Loads `core/catalog/dist` at boot; validates placement against catalog
  footprints/zones; full economy (income, deploy slots, unlocks, tech
  escalation); hidden simultaneous planning via per-seat views;
  `revealSnapshot` at plan-lock; **stub battle resolution** (invested-value
  comparison, prorated survivor damage) — instant, no combat code.
- Reconnect: resumeToken → full snapshot.
- Its test suite is the executable spec later ported to C#.

### C2. BattleServer (`dotnet/BoardGame.BattleServer`, M5)

- ASP.NET Core minimal host: `UseWebSockets()` + `/health`; Newtonsoft.
- Room actor model: one single-threaded loop per room —
  `PeriodicTimer` + `Channel<Command>`, no locks.
- At plan-lock: run the Core sim in one burst (<100 ms), ship
  `battleStarted` + `battleLog` + `roundResult`; next phase server-scheduled
  at `startAt + duration + resultsHold`; `buyLevel` activates (real XP).
- SQLite (`Microsoft.Data.Sqlite`): one row per room at phase transitions
  (blueprints JSON, hp, seed, RNG state) — restart-safe; matches archive table.
- Port the Bun test suite to C# integration tests over a real socket before
  cutover; the client sees only "battleLog now present".
- Deploy: `dotnet publish -c Release -r linux-x64 --self-contained` (or the
  `mcr.microsoft.com/dotnet/aspnet:8.0` container) → one small VPS
  (Hetzner/Fly). No Unity license anywhere in the server story.

---

## 5. Workstream D — Unity client (`apps/game-client`)

Assemblies: `com.boardgame.core` (UPM, the engine), `BoardGame.Client.asmdef`,
`BoardGame.Client.Editor.asmdef`. Newtonsoft via
`com.unity.nuget.newtonsoft-json`.

- **Scenes**: `Boot` (catalog: welcome-delivered or StreamingAssets fallback;
  server address; connect) → `MainMenu` (name, room join/create) → `Match`
  (one scene; UI states per phase).
- **Networking**: GameServerClient evolves to protocol v2 + Newtonsoft
  (fragmented-frame handling and main-thread dispatch retained);
  `MatchClient` store holds server-authoritative match state; resumeToken in
  PlayerPrefs for reconnect.
- **Camera** (`MatchCameraRig`, from FreeCameraController): default framing
  behind the near half facing the enemy; touch: one finger = select/drag,
  two-finger drag = pan, pinch = zoom, twist = rotate yaw; pitch clamp
  ~20–70°, zoom and pan clamped to board + margin; double-tap = reset framing.
- **Board rendering**: replace 1,024 per-tile GameObjects + TMP captions with
  one quad + grid shader; tile picking = ray→plane math (no colliders);
  hover/validity as decal highlights. (Also kills the 4,320-object risk at any
  future board size.)
- **Placement UX**: landscape shop panel (unit cards with cost/count from
  catalog, greyed while unaffordable/locked); drag-out with magnified ghost +
  footprint validity coloring; rotate-orientation button; tap own squad →
  move (free, this round) / sell (this-round purchases) / level-up when
  eligible; tech tree sheet per unit type.
- **Match UI**: commander pick screen (simultaneous, hidden), round/income/HP
  header, ready button + opponent-ready indicator, reveal beat on
  `battleStarted`, results panel (survivors, HP damage), match end.
- **Battle playback**: BattlePlayback playhead + BattleViewRouter + pooled
  SquadView/MemberView/ProjectileView/ZoneView; procedural animation v1
  (WalkBob/Hover/AttackAction/HitReact/DeathSequence) behind `IUnitAnimation`;
  billboard HP bars; speed controls (pause/1×/2×/4×) — local-only per
  decision; skip-to-results.
- **Mobile hardening**: landscape lock, URP mobile renderer settings, 60 fps
  target, GPU instancing for squad members, Meshy model decimation/LOD pass,
  **IL2CPP device build with Newtonsoft converters validated early (M2)**.
- Deletions on schedule: `*Details.cs`/`IUnitDetails` + 6 `.asset`s (M2),
  GameProtocol unit constants (M2), per-tile board objects (M3 window).

---

## 6. Workstream E — Build & CI

- Turbo: `apps/battle-server` wrapper package delegates `build/test/dev` to
  dotnet CLI with `$TURBO_ROOT$`-based inputs
  (`["$TURBO_ROOT$/dotnet/**", "!$TURBO_ROOT$/dotnet/artifacts/**"]`) for
  correct caching. Root `bun dev` filter retargets from game-server to
  battle-server at M5 cutover.
- `.github/workflows/ci.yml`, two jobs:
  1. **ts**: bun install → `turbo run check-types test build` (includes
     catalog `check`, codegen drift test, protocol/room/match tests).
  2. **dotnet**: `actions/setup-dotnet` → `dotnet test dotnet/BoardGame.sln`
     → publish server artifact on tags.
- Unity builds stay **local** for v1 (File → Build, iOS/Android, landscape);
  GameCI/Unity Cloud Build is a documented later option, not a v1 dependency.
- `catalog:sync` script copies dist → StreamingAssets; the in-editor Pack
  Validator warns when the fallback hash is stale.

---

## 7. Testing strategy (cross-cutting)

| Layer | Harness | Gate |
|---|---|---|
| Catalog data | bun tests (zod + lints) in `core/catalog` | CI ts job |
| Codegen | byte-equality drift test | CI ts job |
| Protocol/match loop | Bun tests on MatchRoom reducer + socket integration | CI ts job |
| Engine | xunit goldens, repeat-seed determinism, placement properties | CI dotnet job |
| Balance | headless harness (also via Forge) | manual, M4+ |
| C# server | ported conformance suite over real sockets | CI dotnet job (M5) |
| Client | Pack Validator + Formation Preview (EditMode); manual play via Multiplayer Play Mode virtual players | local |
| Device | IL2CPP smoke build checklist | manual, M2 then each milestone |

---

## 8. Milestones

Effort assumes solo, part-time. Each milestone ends with a demo you can run.

**M0 — Groundwork (½ day).** Fix Archer footprint asset-vs-comment mismatch;
note board decision in CLAUDE.md; scaffold `dotnet/` solution + empty
projects + CI skeleton.
*Done when: CI green on an empty engine.*

**M1 — Catalog spine + Forge v0 (3–5 days).** A1 + A2 schemas (catalog only)
+ catalog build/lints/hash; base pack authored (medieval names, ÷6 numbers);
Forge v0 (browse/validate/build). Bun server loads catalog and validates
placement footprints (still protocol v1 semantics).
*Done when: edit a number → `bun run catalog:check` → hash changes; Forge
renders every unit and flags an injected error.*

**M2 — Unity consumes the catalog (4–7 days).** Codegen + drift test;
Newtonsoft; Core UPM package with CatalogLoader + Placement; CatalogService
(StreamingAssets fallback); catalog-driven placement/formations + orientation
toggle; manifests + placeholder pipeline; Pack Validator + Formation Preview;
delete Details/assets; grid-shader board at 32×48; **IL2CPP device smoke
build**.
*Done when: add a unit in JSON → place it in Unity (placeholder) in minutes,
on device.*

**M3 — Multiplayer planning loop (1–1.5 weeks).** Protocol v2 in `@core/types`;
Bun MatchRoom (C1) with commanderPick, economy, hidden planning, stub battles,
reconnect; client match UI (commander pick, shop, drag placement, ready,
reveal beat, results, HP); camera rig + touch gestures; Forge v1 (editing).
*Done when: two phones (or editor + Multiplayer Play Mode) play a full
hidden-planning match to HP zero — no combat yet.*

**M4 — Engine + fight night (2–3 weeks, the core build).** Sim v1 (weapons:
instant/volley/beam; splash; statuses; shields; v1 triggers/effects; leveling;
XP), event log, determinism test, balance harness; Unity BattlePlayback stack
+ procedural animation; **offline sandbox scene** (place both armies, run the
dual-homed sim in-process, watch with speed controls); Forge v2 balance tab.
*Done when: two custom packs authored in Forge fight on screen from a battle
log, and the harness prints a winrate table.*

**M5 — BattleServer cutover (1–1.5 weeks).** C2: port MatchRoom to C# against
the ported test suite; real battles in multiplayer (`battleLog` appears);
buyLevel live; SQLite persistence; deploy to VPS; retire `apps/game-server`;
retarget `bun dev`.
*Done when: two phones on real networks play a full match with simulated
battles against the deployed server; server restart mid-match resumes.*

**M6 — Mobile polish & content pass (1 week + ongoing).** Perf profiling on
device (instancing, LODs, draw calls), battle readability (HP bars, hit
flashes, death clarity), tuning sessions via harness + Forge, roster to
~10–12 units + 4 commanders.
*Done when: 60 fps on a mid-tier phone in a late-game battle; the game is fun
enough to keep testing voluntarily.*

**M7+ — Vocabulary growth (on demand).** v1.5 effects (chain, execute, zones,
EMP, resurrect), interception, kamikaze, flanks-as-data, battlefield spells,
reinforcement cards, 2v2 — each riding the established effect-kind seam.

Critical path: M0 → M1 → M2 → M3 → M4 → M5. Forge v1 and M6 items can
interleave. Total to first online autobattle (end of M5): **~6–8 part-time
weeks**.

---

## 9. Risks & mitigations

- **Codegen emitter creep** — capped subset, checked-in output, drift test;
  >~400 lines ⇒ switch to hand-maintained DTOs.
- **Invented balance numbers** — Mechabellum ratios ÷6 as priors; harness +
  Forge balance tab; budget real tuning time in M4/M6.
- **Determinism regressions** — repeat-seed test from day one; no
  `System.Random`/unordered iteration in sim code (review checklist).
- **Newtonsoft + IL2CPP AOT** — device smoke build in M2, not after the sim.
- **Forge scope creep** — local tool, no auth/DB/hosting; git is the undo;
  balance tab is its only integration.
- **Event-log bloat** — de-dup + moved-only keyframes + gzip escape hatch;
  measure with the harness in M4.
- **Solo-dev surface area** — every milestone demos something; M1+M2 alone
  already deliver the content-customization requirement; M4 delivers fights
  without any server work if energy dips.
- **Stub-era pacing (M3)** — timebox loop-UI polish; it validates economy and
  reveal tension, not feel.

---

## 10. Definition of Done (v1)

Two players on phones join a room, pick commanders, play hidden simultaneous
deployment rounds with economy/tech/leveling, watch server-simulated battles
play back with full camera freedom and local speed control, lose commander HP
by prorated survivor value, and finish a match — where every unit, commander,
stat, ability, and rule involved was authored as catalog data (via Forge or a
text editor), every model/animation binding as a Unity pack manifest, and the
server runs from one deployed self-contained binary.
