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

<!-- Subsequent milestones appended as they complete. -->
