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

<!-- Subsequent milestones appended as they complete. -->
