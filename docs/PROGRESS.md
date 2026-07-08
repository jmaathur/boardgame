# Implementation Progress

Autonomous implementation log for the M0–M6 milestones in
`docs/implementation-plan.md`. Each milestone records what was built, how it was
verified, and anything **deferred to a human** (Unity Editor GUI work,
credentials, deployments) that a headless agent cannot do.

Legend: ✅ done & verified · 🟡 partial · ⏭️ deferred to a human

---

## M0 — Groundwork ✅

**Built**
- Fixed the Archer footprint comment-vs-asset mismatch:
  `ArcherDetails.cs` documented a `(2,2)` footprint but `ArcherDetails.asset`
  ships `footprintSize: {x:3, y:1}`. The asset is the runtime source of truth,
  so the comment was corrected to `(3,1)` and the stale "across 2 tiles" /
  "back row: 2 archers" formation comments fixed to match the actual 7-member
  layout (3 front, 4 back).
- Recorded the resolved board decision (**32×48**, landscape, free camera,
  ÷6 transcription) in `CLAUDE.md`, replacing the old "72×60 vs 32×32 drift is
  unresolved" note. The board now lives as data in the catalog `matchConfig`
  (single source of truth) once M1 lands.
- Scaffolded `dotnet/`:
  - `BoardGame.sln` (classic format for tooling compatibility).
  - `BoardGame.Core` — engine, `netstandard2.1` / LangVersion 9, Newtonsoft,
    UnityEngine-free. Dual-homed as UPM package `com.boardgame.core`
    (`package.json` + `Runtime/BoardGame.Core.asmdef`, `noEngineReferences`).
  - `BoardGame.Core.Tests` — xUnit on `net8.0`; doubles as the balance-harness
    CLI (`OutputType=Exe`, stub `Program.cs` until M4).
  - `BoardGame.BattleServer` — ASP.NET Core minimal host on `net8.0` with a
    `/health` probe (the M5 skeleton starting point).
  - `Directory.Build.props` — `UseArtifactsOutput` redirects all bin/obj to
    `dotnet/artifacts/` so the Unity UPM package folder stays clean;
    `TreatWarningsAsErrors`, `Nullable=enable`.
  - `RollForward=Major` on the executable projects so the `net8.0` testhost and
    server run on whatever major runtime is installed (local dev had only
    .NET 10; CI pins .NET 8 via `setup-dotnet`).
- Turbo integration: `apps/battle-server` wrapper package delegates
  `build`/`test`/`dev`/`check-types` to the dotnet CLI, with a package-level
  `turbo.json` using `$TURBO_ROOT$/dotnet/**` inputs (minus `artifacts/`) for
  correct caching.
- `.github/workflows/ci.yml` — two jobs: **ts** (bun install → format check →
  `turbo check-types test build`, excluding `battle-server` since that runner
  has no dotnet) and **dotnet** (`setup-dotnet` 8.0 → restore → build → test →
  publish server on tags).
- `.gitignore`: added `dotnet/artifacts/`; scoped the Unity `*.csproj`/`*.sln`
  ignores to `apps/game-client/` so the committed dotnet solution isn't ignored.
- `biome.json`: excluded `dotnet/artifacts/**` from formatting.

**Verified**
- `dotnet build dotnet/BoardGame.sln` → succeeds, 0 warnings/errors.
- `dotnet test dotnet/BoardGame.sln` → 1/1 passing (roll-forward works; no env
  var needed).
- BattleServer boots and `GET /health` returns `{"status":"ok",
  "schemaVersion":1}` (probed a running instance directly).
- `bun run format:check` → clean.
- `bunx turbo run check-types test build` → 10/10 tasks green, including the
  dotnet solution driven through the `battle-server` wrapper.

**Deferred to a human** — none for M0.

---

## M1 — Catalog spine + Forge v0 ✅

**Built**
- **Catalog schema** (`core/types/src/catalog-schema.ts`, `SCHEMA_VERSION = 1`):
  the full zod contract for sim data — `ScaledValue`, stat mods, the
  `kind`-discriminated `Effect` union (v1 set: damage / areaDamage /
  applyStatus / heal / grantShield / spawnUnits / modifySelf), the
  `kind`-discriminated `Trigger` union, weapons with a `mode`-discriminated
  fire union (instant / volley / beam), abilities, techs, unit defs, statuses,
  zones, content packs, commanders, and `matchRules` (board / deploy zones /
  economy / timers / leveling / tech escalation / starting buildings /
  commanders). The board size now lives here as data.
- **`core/catalog` package**:
  - `data/packs/base.json` — the medieval base pack: 10 units (footman chaff
    swarm ×24, archer, whelp flyer, holyKnight bruiser, ballista artillery,
    arbalest sniper, gargoyle anti-air, warBanner aura, barracks + cathedral
    buildings), plus the `rallied` status. Stats are Mechabellum ratios ÷6;
    each unit carries a `_source`-style comment naming its archetype.
  - `data/match-rules.json` — 32×48 board, halves at col 24 (no gap), income
    200×N, 2 deploys/round, timers 70/120s, leveling, tech escalation +200,
    cathedral + barracks per side, 3 commanders (Warlord/Steward/Zealot).
  - `scripts/build.ts` — validate (zod) → cross-pack lints → canonicalize
    (key-sorted, minified) → `dist/catalog.json` + `dist/catalog.hash` +
    `dist/catalog.embedded.ts` (generated TS module of the exact bytes, so
    `bun build --compile` embeds the catalog in the server binary). Exits
    non-zero and writes nothing on any error.
  - `src/lint.ts` — the cross-pack lint suite (unique ids, ref resolution,
    formation length == count, offsets inside footprint, footprints fit deploy
    zones, spawn-chain depth cap, commander/building refs). `src/canonical.ts`
    (deterministic serialize + sha256). `src/index.ts` (`buildCatalog`,
    `loadBuiltCatalog`). `src/embedded.ts` (`loadEmbeddedCatalog`, exported at
    subpath `@core/catalog/embedded`).
  - `scripts/author-base.ts` — one-shot generator that emitted base.json +
    match-rules.json (kept for regeneration; not part of the build).
  - 13 bun tests (`src/lint.test.ts`): base catalog validates clean, build is
    deterministic, canonical form is sorted/minified, and every lint rule
    triggers on a crafted violation.
- **Game-server catalog integration** (`apps/game-server`): loads the embedded
  catalog at boot; `Room` is now catalog-driven — board size and every unit
  footprint come from the catalog, so placement validation is multi-tile
  (footprint overlap, not single-tile) on the 32×48 board, and unknown unit
  types are rejected (`unknownUnitType`). `/health` now reports `catalogHash`
  and `board`. The compiled binary embeds the catalog (no sidecar files).
- **Protocol v1 relaxation** (not full v2 — that's M3): `unitType` on the wire
  is now any catalog id string (runtime-validated), not a fixed enum; the game
  state board is a plain positive-int pair, not the 72×60 literals; added the
  `unknownUnitType` error code. `BOARD_WIDTH`/`BOARD_HEIGHT` remain as a legacy
  coordinate envelope.
- **Forge v0** (`apps/forge`): a dependency-light `Bun.serve` app on
  `localhost:7780` (no bundler) — `GET /api/catalog|packs|packs/:file|match-rules|schema`,
  `POST /api/build`, and a self-contained HTML UI that renders every unit,
  commander, and the match rules, shows a green/red validation banner, and has
  a working Build button. Path-traversal-hardened pack reads. 7 bun tests.
- **Turbo/CI wiring**: `@core/catalog` build produces committed `dist/`;
  `catalog:build` / `catalog:check` root scripts; a `check` turbo task; the
  built catalog is committed (un-ignored in `.gitignore`) so `catalog:check`'s
  `git diff --exit-code dist` catches drift.

**Verified**
- `bun run catalog:check` — build + `git diff --exit-code dist` clean.
- **Hash sensitivity**: editing archer damage 120→130 changed the hash
  (638ae0…→a48e20…); reverting restored 638ae0… exactly (deterministic).
- **Error detection**: injected lint error (formation count) and schema error
  (negative hp) each fail the build with a precise message; Forge's
  `/api/catalog` reported `ok:false` with the exact lint issue for an injected
  unknown-status reference.
- **Forge renders every unit**: `/api/catalog` → `ok:true`, 10 units, hash
  638ae0…, board 32×48, commanders warlord/steward/zealot; `/` serves HTML.
- **Compiled binary**: `bun run build` → binary whose `/health` returns the
  embedded catalog hash + 32×48 board with no sidecar files.
- `bunx turbo run check-types test build` → **15/15 tasks green** (core/types,
  core/catalog, game-server 28 tests, forge 7 tests, battle-server dotnet).
- `bun run format:check` → clean.

**Deferred to a human** — none for M1. (The Unity-side catalog consumption —
manifests, StreamingAssets sync, Formation Preview — is M2, where the
Editor-GUI parts are noted.)

## M2 — Unity consumes the catalog 🟡 (engine + sync done; Unity-Editor parts deferred)

**Built (headless, verified)**
- **Catalog DTOs** (`dotnet/BoardGame.Core/Runtime/Catalog/CatalogDto.cs`) —
  hand-maintained C# DTOs mirroring the zod catalog schema, with Newtonsoft
  discriminator converters for every union (Effect, Trigger, FireMode,
  TechEffect, CommanderAbility) and a `ScaledValue` struct + converter that
  reads both the flat-number and `{base, perLevel}` JSON forms.
  - **Codegen decision**: the plan budgeted a zod→C# emitter but capped it at
    ~400 lines. A first attempt via `z.toJSONSchema()` produced **2022 lines**
    of duplicated types (every inlined union re-emitted per use site, no
    structural dedup). Per the plan's own mitigation (">~400 lines ⇒
    hand-maintain DTOs") the emitter was removed and the DTOs hand-written. The
    drift guard is now a **conformance test** that round-trips the real
    committed catalog through the DTOs — same guarantee, achieved on the actual
    shipped bytes.
- **CatalogLoader** (`Runtime/Catalog/CatalogLoader.cs`) — parse, hard
  schemaVersion gate, sha256-as-received, `LoadVerified` (hash check), and
  indexed unit/commander/status lookups.
- **Footprint/Formation** (`Runtime/Catalog/Footprint.cs`) — `TileRect`,
  oriented footprint size (swap w/h), oriented formation (rotate (x,z)→(z,−x)),
  board-fit check. Shared by placement and (later) the sim.
- **Placement** (`Runtime/Match/Placement.cs`) — the C# port of the Bun room's
  placement validation: catalog membership, board bounds, own-half (deploy
  zone) containment (buildings exempt), footprint overlap.
- **18 xUnit tests** (`BoardGame.Core.Tests`): catalog conformance (roster,
  board 32×48, 3 commanders), hash matches committed hash, schemaVersion gate,
  discriminated-union deserialization, ScaledValue both forms, footprint
  orientation/rotation, and placement rules mirroring the Bun room tests.
- **`catalog:sync`** (`core/catalog/scripts/sync-streaming-assets.ts` + root
  `catalog:sync`) — copies the built catalog **verbatim** into
  `apps/game-client/Assets/StreamingAssets/` (the client's offline fallback);
  verified the StreamingAssets hash matches dist exactly.
- **Unity UPM wiring** (`apps/game-client/Packages/manifest.json`) — added the
  `com.boardgame.core` `file:` reference and `com.unity.nuget.newtonsoft-json`
  so the engine + Newtonsoft resolve when the project is opened. Manifest
  validated as JSON.

**Verified**
- `dotnet build` + `dotnet test` → **18/18 green** (loader, conformance,
  footprint, placement).
- `catalog:sync` → StreamingAssets catalog.json/.hash byte-match dist.
- `bunx turbo run check-types test build` + `catalog:check` → green;
  `format:check` clean.

**⏭️ Deferred to a human (Unity Editor / device — cannot be done or verified
headlessly):**
- Opening the project so Unity imports `com.boardgame.core` + Newtonsoft and
  generates their `.meta` files; confirming the engine compiles inside Unity.
- **Rewriting `BoardManager.cs`** to be catalog-driven (build the board from
  `matchConfig`, place units from catalog footprints/formations, orientation
  toggle) and **deleting** the 8 `*Details.cs`/`IUnitDetails` files + 6
  `.asset`s + `GameProtocol.cs` unit constants. These are intentionally NOT
  deleted here: `BoardManager` still references the Details classes, so removing
  them without the (Editor-only) rewrite would leave the Unity project
  uncompilable, which cannot be verified in a headless environment.
- **CatalogService** MonoBehaviour (welcome-delivered catalog with
  StreamingAssets fallback) — depends on the Unity runtime + protocol v2 (M3).
- **Grid-shader board** at 32×48 (replace 1,024 per-tile GameObjects), manifests
  + placeholder pipeline, Formation Preview + Pack Validator EditMode tools.
- **IL2CPP device smoke build** validating the Newtonsoft converters on device.
- `.meta` files for `StreamingAssets/catalog.json`, the Core package's
  `package.json`/`asmdef` — Unity generates these on import.

## M3 — Multiplayer planning loop 🟡 (server + reducer done; Unity client deferred)

**Built (headless, verified)**
- **Protocol V2** (`core/types/src/protocol-v2.ts`, `PROTOCOL_VERSION = 2`) — the
  full match-loop wire contract: client→server `join` (with resumeToken/catalogHash),
  `pickCommander`, `buySquad`, `moveSquad`, `sellSquad`, `unlockUnit`, `buyTech`,
  `buyLevel`, `setReady`, `battleAck`, `ping`; server→client `welcome` (seat +
  exact catalog bytes + matchConfig + resumable snapshot), `phase`, `cmdAccepted`,
  `cmdRejected`, `revealSnapshot`, `battleStarted`, `battleLog`, `roundResult`,
  `matchEnded`, `error`, `pong`. Per-seat `SeatView` (private) vs `OpponentView`
  (only last-revealed army). Coexists with V1 (no name collisions).
- **MatchRoom reducer** (`apps/game-server/src/matchRoom.ts`) — the pure,
  transport-free, clock-injected phase machine (lobby → commanderPick →
  planning(N) → battle → results → … → matchEnded). No `Date.now()`/`Math.random()`
  (deterministic; the executable spec the M5 C# port follows). Full economy
  (income 200×N + commander mods, deploy/unlock slots, tech escalation +200,
  level pricing), hidden simultaneous planning via per-seat views, catalog-driven
  placement (bounds/own-half/overlap), reconnect via resumeToken, and the **stub
  battle resolver** (invested-value comparison → prorated survivor HP damage,
  floored). Commander HP is the player HP pool; starting buildings + commander
  units materialize on round 1.
- **Match server** (`apps/game-server/src/matchServer.ts`) — protocol V2 over
  `Bun.serve` WebSockets driving MatchRoom, with a deadline ticker advancing every
  room's phase machine, per-seat private snapshots, reveal/battleStarted/roundResult/
  matchEnded broadcasts, and `welcome` shipping the exact catalog bytes. New
  `dev:match`/`start:match` scripts and a second compiled binary (`dist/match-server`)
  that embeds the catalog. `battleStarted.hasBattleLog = false` (stub era).
- **Tests**: **19 MatchRoom reducer tests** (lobby/commanderPick, economy,
  hidden planning, stub battle + result, **a full match played to HP zero** via
  both explicit acks and pure deadline ticks, reconnect) + **6 match-server
  integration tests** over a real socket (welcome + catalog bytes, commanderPick,
  a full round pick→buy→ready→reveal→battleStarted→ack→roundResult, hidden-planning
  leak check, command rejection). Total game-server suite: **53/53 green**.

**Verified**
- Full hidden-planning match reaches `matchEnded` with the loser's HP at 0 (the
  M3 "Done when"), driven both by acks and by deadline ticks alone.
- Match-server binary boots; `/health` reports `protocolVersion:2` + catalog
  hash + 32×48 board.
- `bunx turbo run check-types test build` → green; dotnet 18/18 green;
  `format:check` clean.

**⏭️ Deferred to a human (Unity Editor / device):**
- The **Unity match client**: commander-pick screen, shop panel, drag placement
  with footprint validity, ready/opponent-ready indicator, reveal beat, results
  panel, match-end — all Unity UI. `GameServerClient` → protocol V2 + Newtonsoft,
  `MatchClient` store, resumeToken in PlayerPrefs.
- The **camera rig** (`MatchCameraRig`) + touch gestures (pan/pinch/rotate).
- **Forge v1** (full form editing incl. formation/commander editors) — a browser
  app that can be built later; v0 (browse/validate/build) already ships.
- Running two editor instances (Multiplayer Play Mode) to play a match on screen.

## M4 — Engine + fight night 🟡 (sim + harness done; Unity playback deferred)

**Built (headless, verified)**
- **Battle simulation** (`dotnet/BoardGame.Core/Runtime/Sim/`) — a deterministic
  fixed-20 Hz sim (design doc §6). Seeded xorshift (`XorShiftRng`, never
  `System.Random`). Per-tick phases: statuses/DoT expiry → auras →
  **targeting** (nearest attackable, from a frozen snapshot, id tiebreak) →
  **movement** (steering toward target, decided from the snapshot and applied
  simultaneously) → **weapons** (instant / volley-as-spaced-shots / range/domain
  gating) → **buffered damage flush** (all of a tick's damage accumulates and
  resolves together) → deaths → keyframes → end-check. Handles per-member HP,
  splash via `areaDamage` onImpact with linear falloff, shields (absorb first),
  aura status buffs (e.g. War Banner's `rallied` +15% damage), `flatBlock`, and
  the damage pipeline `max(1, raw×takenMul) − flatBlock`.
  - **Order-independence**: targeting, movement, and damage were each made
    snapshot-based so the two seats' iteration order can't bias a symmetric
    clash — a single squad vs. its mirror is a perfect draw.
- **Event log** (`Runtime/Events/BattleEvent.cs`) — the ~10 v1 battle-event
  types (battleStarted, squadSpawned, positionKeyframes at 5 Hz moved-only,
  attackFired, damageApplied hull/shield, memberDied, statusApplied,
  battleEnded) + `IBattleEventSink` (list sink for playback, null sink for
  headless balance).
- **Balance harness** (`BoardGame.Core.Tests/BalanceHarness.cs` + the
  `Program.cs` CLI) — `dotnet run --project BoardGame.Core.Tests -- fight
  "footman x8" vs "archer x5" --seeds 50` prints a winrate / battle-length /
  survivor table. **Side-swaps** the two lineups across seeds so any residual
  seat/first-mover bias cancels (mirror matchups read exactly 50%). Also the
  intended backend for Forge's balance tab. `BattleSetup.FromLineup` auto-tiles
  armies (seat 1 mirrored across the midline).
- **26 xUnit tests**, including the **repeat-seed determinism gate** (same seed
  + inputs → byte-identical serialized event log), splash multi-kill, anti-air
  vs. flyers, overwhelming-numbers, prorated survivor value, and mirror-fairness
  (50% after side-swap).

**Verified**
- Determinism test green (byte-identical logs on repeat seed).
- Harness prints winrate tables and discriminates matchups sensibly (footman
  swarm 100% vs archers; anti-air 100% vs flyers; mirrors 50%).
- `dotnet test` → **26/26 green**; TS pipeline green; `format:check` clean.

**⏭️ Deferred to a human (Unity Editor / later):**
- **Unity BattlePlayback stack** — `BattlePlayback` playhead, `BattleViewRouter`,
  pooled `SquadView`/`MemberView`/`ProjectileView`/`ZoneView`, procedural
  animation (WalkBob/Hover/AttackAction/HitReact/DeathSequence), billboard HP
  bars, speed controls (pause/1×/2×/4×), skip-to-results. All Unity runtime.
- **Offline sandbox scene** (place both armies, run the dual-homed sim in-process,
  watch it) — a Unity scene + editor tooling.
- **Forge v2 balance tab** — a browser UI over the harness. Deferred to keep the
  Forge test suite hermetic (it would need `dotnet` on the Forge host); the
  harness CLI is the working backend.
- Beam ramp weapons, mid-battle spawnUnits, v1.5 effects — no base-pack unit
  needs them yet; they ride the established effect-kind seam when content does
  (design doc §11.6).

## M5 — BattleServer cutover ✅ (deploy to a VPS is the only human step)

**Built (headless, verified)**
- **Protocol V2 C# DTOs** (`BoardGame.Core/Runtime/Match/ProtocolV2.cs`) — the
  hand-maintained C# mirror of `protocol-v2.ts`: client-message parsing
  (discriminated on `type` via `ClientMessageV2.Parse`), state DTOs, and
  server-message builders.
- **MatchRoom ported to C#** (`Runtime/Match/MatchRoom.cs`) — a faithful port of
  the Bun reducer, structurally identical so the ported test suite confirms
  equivalence. Same phase machine / economy / hidden planning / catalog-driven
  placement / reconnect. The one behavioral change: at plan-lock it **runs the
  Core sim** and exposes the real event log (`LastBattleLog`). Adds
  `CaptureState`/`RestoreState` + a `MatchRoomSnapshot` DTO for persistence.
- **BattleServer** (`dotnet/BoardGame.BattleServer`) — ASP.NET Core minimal host:
  `/health`, a `/ws` protocol-V2 endpoint (shared `WebSocketEndpoint.Map`), a
  `PeriodicTimer` deadline ticker (`TickerService`), and a **per-room lock**
  making each room a single-threaded actor (no command-interleave races). At
  plan-lock it ships `revealSnapshot` + `battleStarted` + **`battleLog`** +
  `roundResult`. Newtonsoft throughout (matches the wire + dodges a
  System.Text.Json PipeWriter mismatch under roll-forward).
- **SQLite persistence** (`RoomStore.cs`) — `IRoomStore` with a `SqliteRoomStore`
  (one upserted row per room at every transition/command + a finished-match
  archive table) and an `InMemoryRoomStore` for tests. `BOARDGAME_DB` selects
  SQLite; otherwise in-memory.
- **Ported conformance suite** (`BoardGame.BattleServer.Tests`, real Kestrel on
  an ephemeral port + real `ClientWebSocket`): `/health` protocol v2, welcome
  with catalog bytes, a **full round producing a real battleLog** (the cutover
  signal — `hasBattleLog: true`), command rejection, plus **restart-resume**
  (capture/restore round-trip, SQLite survives a reopen with reconnect tokens
  intact, hub resumes a room from the store).

**Verified**
- `dotnet test` → **44/44 green** (37 Core incl. 11 ported MatchRoom + 7
  BattleServer integration); **stable across 6+ isolated and 4+ full-solution
  runs** (the earlier flake was a racing-seat test bug, now fixed via sequential
  seat-aware joins + the per-room actor lock).
- The real BattleServer binary boots with SQLite, `/health` returns protocol v2
  + the embedded-catalog hash + creates the DB.
- `dotnet publish -c Release -r <rid> --self-contained` produces a single
  deployable binary.
- `turbo run build test --filter=battle-server` green; TS pipeline green; format
  clean.

**⏭️ Deferred to a human:**
- **VPS deployment** (Hetzner/Fly, credentials + DNS) — the self-contained
  binary and Docker path are ready; publishing to a host is an ops step.
- **Retiring `apps/game-server` + retargeting `bun dev`** — kept intentionally:
  the Bun match server is the reference spec (its tests still gate CI) and the
  cutover to the .NET server is a deploy/ops switch, not a code deletion. The
  client sees the same protocol V2; only `battleLog` now appears.
- Playing a real cross-network match on two phones against the deployed server.

<!-- Subsequent milestones appended as they complete. -->
