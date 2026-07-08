# Unit Packs & Mechabellum-Style Match Loop — Design

Status: designed, not yet implemented. Builds on the LeanCore server architecture
(engine-free `dotnet/BoardGame.Core` sim dual-homed into Unity, `.NET 8 BattleServer`,
interim Bun server retained until cutover).

Goal: adding or tuning a unit pack — stats, abilities, damage, range, models,
animations — must not require engine-code changes for common cases, and the game
plays as a Mechabellum-style auto-battler: simultaneous hidden deployment each
round, hands-off battles, persistent army across rounds, per-round economy and
unit tech.

## 1. The match model (verified against Mechabellum)

- **Match start**: each player picks a **commander** (1 of N offered,
  simultaneous, revealed when round 1 planning begins). The commander
  determines starting units, commander HP, and a passive ability. Both sides
  always start with a cathedral + barracks. (This is Mechabellum's
  "specialist" system, pulled into scope as pure data.)
- A match is a series of identical rounds until one player's HP reaches 0
  (typically 8–14 rounds).
- **Deployment phase** (~70s, simultaneous, hidden): receive income
  (`200 × round`), place up to 2 new unit cards, unlock at most 1 new unit type,
  buy unit-type techs, pay to level squads whose XP threshold is met. You see
  the opponent's army as it stood at the END of last round; everything they do
  this round is hidden until battle.
- **Battle phase**: fully automatic, zero input, runs until one side is wiped
  or the timer caps (~120s). The army is a persistent *blueprint*: every card
  fights again every round at full HP in its position. Deaths are never
  permanent; XP/levels/techs persist.
- **Round result**: loser takes commander-HP damage equal to the value of the
  winner's surviving units (v1: prorated by surviving members per squad).
- A unit card = a placeable squad of N members with a footprint. **Each member
  has its own HP** (24 crawlers = 24 HP bars): AoE genuinely kills part of a
  squad, single-target overkill wastes damage on one member.
- Leveling is per placed card: XP from kills/damage; pay half base cost to take
  a level; HP/ATK scale linearly with level. Techs are per unit *type* per
  player and apply to all current and future squads of that type; each tech
  purchase raises that type's other tech prices (+200).

Deferred (data seams exist, no machinery in v1): reinforcement cards,
command/research-center upgrades, flank deploy zones, projectile interception,
battlefield spells, active commander abilities (v1 commander abilities are
passive stat/economy mods).

## 2. Design pillars

1. **Sim data is data.** All gameplay numbers live in JSON ContentPacks,
   validated by zod, built into one deterministic `catalog.json` whose bytes
   are read identically by the Bun server, the .NET server, and Unity.
2. **Catalog over the wire.** `welcome` delivers the catalog JSON + sha256
   hash. A balance patch is a server restart; no client rebuild. Clients
   render unknown units as labeled placeholders, so content is playable
   before art exists.
3. **Abilities are trigger + effect compositions**, not code — with an honest
   escape hatch (`behavior` effect kind) for genuinely novel mechanics that
   are one small C# class each.
4. **Squad = N sim member entities** owned by one SquadState record. Pooled-HP
   "cosmetic followers" is rejected (cannot express partial AoE kills,
   per-member shields, per-member death effects).
5. **Battles are pre-simulated.** The server runs the whole battle in one
   <100ms burst at plan-lock and ships one battle event log; clients are
   playback puppets (pause/2×/4× local, room pacing server-scheduled).
   No cross-runtime determinism requirement, no client re-simulation.
6. **The sim is internally deterministic** (seeded RNG, fixed phase order) so
   the headless balance harness and server-restart recompute work on the same
   binary.

## 3. Vocabulary

| Term | Meaning |
|---|---|
| **ContentPack** | One JSON file bundling `units[]`, `statuses[]`, `zones[]`; a themed content drop = 1 JSON + 1 Unity manifest asset |
| **UnitDef / `unitId`** | A placeable squad-card type (kebab-case string id; replaces the old `UNIT_TYPES` enum — wire validity is checked against the runtime catalog, not the schema) |
| **SquadCard** | A purchased placement in the match blueprint (`sq7`): unitId, anchor, orientation, level, XP, purchasedRound |
| **SquadState / members** | Battle-time expansion: int `battleSquadId` + N member entities (hp, shield, position, statuses, target, cooldowns) |
| **level** | Squad-card veterancy; `ScaledValue = number \| {base, perLevel}` on any numeric effect field |
| **Units** | Distances in **tiles** (1 tile = 1 world unit; Mechabellum meter values **÷ 6** when transcribing, calibrated to the 48-deep board), seconds in authored data → integer ticks at load. Sim = 20 Hz, battle cap 120s, position keyframes at 5 Hz |

## 4. Repo layout & single-sourcing

```
core/types/src/catalog-schema.ts        # zod: contentPack, unitDef, weapon, trigger,
                                        #   effect, status, zone, tech, matchRules
core/types/src/protocol.ts              # protocol V2
core/types/scripts/generate-csharp.ts   # zod -> C# DTO emitter (TYPES only, never data)
core/catalog/data/packs/*.json          # ContentPacks (base.json = 6 migrated units)
core/catalog/data/match-rules.json      # rules-as-data
core/catalog/scripts/build.ts           # validate + cross-pack lints
                                        #   -> dist/catalog.json (key-sorted, minified,
                                        #      deterministic bytes) + dist/catalog.hash
dotnet/BoardGame.Core/
  Runtime/Generated/*.g.cs              # checked in, drift-guarded by a bun test
  Runtime/Catalog/                      # loader, hash gate, footprint/formation fns
  Runtime/Match/                        # placement validation (ported BoardManager lines)
  Runtime/Sim/                          # battle sim (see §7)
  Runtime/Events/                       # BattleEvent DTOs + IBattleEventSink
dotnet/BoardGame.BattleServer/          # .NET 8 host (LeanCore, unchanged)
apps/game-client/Assets/StreamingAssets/catalog.json      # offline/menu fallback (catalog:sync)
apps/game-client/Assets/Packs/<Pack>/.../<Pack>Manifest.asset  # visual half of a pack
```

Rules:
- Sim data lives ONLY in catalog JSON; codegen emits type definitions, never data
  (preserves the over-the-wire nerf story).
- The hash is computed over bytes-as-transmitted; canonicalization happens exactly
  once, in `build.ts`.
- `schemaVersion` (hand-bumped int) gates shape changes; `catalogVersion`
  (content hash) identifies data.
- Serializer: **Newtonsoft JSON on both runtimes** (`com.unity.nuget.newtonsoft-json`
  in Unity; protocol V2's unions/optionals rule out JsonUtility). Generated
  converters discriminate unions on `kind`; keep them reflection-light and test an
  IL2CPP build early.
- The catalog contains **no visual data**; Unity manifests key on `unitId`.

## 5. Catalog schema (abridged)

```
UnitDef = {
  id, name, description, tier,
  cost: { deployCost, unlockCost },
  placement: { footprint: {w, h}, domain: ground|air|building },
  squad: { count, formation: [{x, z}], xpToLevel },   // offsets CENTER-relative
  member: { hp, speed, flatBlock?, weapons: [Weapon], abilities: [Ability] },
  techs: [Tech]                                        // live on the UnitDef
}

Weapon = {
  id,                              // REQUIRED: manifests, tech patches, events join on it
  targets: ["ground"|"air"], range, minRange = 0, interval,
  damage?: ScaledValue, splashRadius? = 0,             // sugar -> leading onImpact effect
  barrels?: { count, independentTargets },
  fire: instant | volley{count, spacingS, spread} | beam{tickIntervalS, ramp?},
  projectile?: { speed, arcing, hp },                  // hp > 0 = interceptable (later)
  onImpact: [Effect], onBeamTick: [Effect]
}

Ability = { id, trigger: Trigger, area?: {radius, filter}, effects: [Effect] }

Trigger  = onSpawn | onDeath | onKill | onDamaged | onHpBelow{pct, once}
         | periodic{intervalS, startDelayS, charges} | aura{radius, refreshS, filter}

Effect (discriminated on `kind` — THE extension surface)
  v1:   damage | areaDamage | applyStatus | heal | grantShield | spawnUnits
      | modifySelf
  v1.5: chain | execute | pctHpDamage | groundZone | clearZones | resurrectSelf
  escape hatch: behavior{ behaviorId, params }   // bespoke C# classes registered by
                                                 // string id (mind control, interception,
                                                 // sweep beam, kamikaze...), still
                                                 // data-configured and tech-grantable

Status  = { id, mods: [StatMod], dot?, flags (techsDisabled, untargetable, ...), tags }
          // stacking: buffs additive, debuffs multiplicative; every runtime mod is
          // source-tagged (innate|tech|status) so EMP tech-disable works
Zone    = circle reapplying a status on an interval; clearZones removes by tag
Tech    = { id, name, cost, effects: statMod | grantAbility | modifyWeapon(patch)
          | grantImmunity | modifyProjectile }

MatchRules (data, single source; the wire matchConfig is derived from it) =
  board{w: 32, h: 48}   // DECIDED v1 default: 32 lateral (row/world-X) ×
                        // 48 deep (col/world-Z); halves meet at the midline
                        // with NO no-man's-land: P1 = cols 0–23 (near),
                        // P2 = cols 24–47 (far)
  deployZones (rects, availableFromRound — flanks are a data add later),
  income{perRoundIncrement: 200, carryOver}, deploysPerRound: 2,
  unlocksPerRound: 1, timers{deploySeconds: 70, battleSeconds: 120},
  leveling{hpFactorPerLevel: 1, atkFactorPerLevel: 1, upgradeCostFraction: 0.5},
  techPriceEscalation: 200,
  startingBuildings[]   // cathedral + barracks placements, both sides
  commanders[]          // pick 1 of N at match start:
                        //   { id, name, description, hp,
                        //     startingUnits: [{unitId, anchor, orientation}],
                        //     ability: [statMod | economyMod] }  // passive in v1
                        // commander HP IS the player HP pool
```

Build lints: unique `unitId` across all packs; every `spawnUnits`/`statusId`/
`zoneId`/`techId` reference resolves; spawn chains depth-capped; formation length
== squad count; center-relative offsets within the footprint; footprints fit the
deploy zones; every weapon targets ≥ 1 domain.

### Example pack (excerpt, tile units, first-draft numbers)

```jsonc
{
  "packId": "base", "version": "1.0.0",
  "units": [
    // Chaff swarm: 24 cheap melee bodies, per-member HP
    { "id": "footman", "name": "Footman", "tier": 1,
      "cost": { "deployCost": 100, "unlockCost": 0 },
      "placement": { "footprint": { "w": 3, "h": 2 }, "domain": "ground" },
      "squad": { "count": 24, "xpToLevel": 450,
                 "formation": [ { "x": -1.3, "z": -0.8 }, { "x": -0.8, "z": -0.8 } /* ... 24 total */ ] },
      "member": { "hp": 260, "speed": 2.7,
        "weapons": [ { "id": "sword", "targets": ["ground"], "range": 1,
                       "interval": 0.9, "damage": 86, "fire": { "mode": "instant" } } ],
        "abilities": [] } },

    // Artillery volley: arcing rockets, splash on impact
    { "id": "stormcaller", "name": "Stormcaller", "tier": 2,
      "cost": { "deployCost": 200, "unlockCost": 50 },
      "placement": { "footprint": { "w": 3, "h": 2 }, "domain": "ground" },
      "squad": { "count": 4, "xpToLevel": 700,
                 "formation": [ { "x": -0.9, "z": -0.5 }, { "x": 0.9, "z": -0.5 },
                                { "x": -0.9, "z": 0.5 },  { "x": 0.9, "z": 0.5 } ] },
      "member": { "hp": 1330, "speed": 1.3,
        "weapons": [ { "id": "rocketRack", "targets": ["ground"],
                       "range": 30, "minRange": 7, "interval": 7.0,
                       "fire": { "mode": "volley", "count": 3, "spacingS": 0.2, "spread": 1.2 },
                       "projectile": { "speed": 4.7, "arcing": true, "hp": 4200 },
                       "onImpact": [ { "kind": "areaDamage", "amount": 610,
                                       "radius": 1.3, "falloff": "linear" } ] } ] } },

    // Ramping beam: damage climbs per 0.2s tick, resets on target switch
    { "id": "meltingPoint", "name": "Melting Point", "tier": 4,
      "cost": { "deployCost": 400, "unlockCost": 200 },
      "placement": { "footprint": { "w": 5, "h": 5 }, "domain": "ground" },
      "squad": { "count": 1, "xpToLevel": 1800, "formation": [ { "x": 0, "z": 0 } ] },
      "member": { "hp": 33800, "speed": 0.7,
        "weapons": [ { "id": "fusionBeam", "targets": ["ground", "air"], "range": 16,
                       "interval": 0,
                       "fire": { "mode": "beam", "tickIntervalS": 0.2,
                                 "ramp": { "addPctPerTick": 6, "maxPct": 1800,
                                           "resetOnTargetSwitch": true } },
                       "onBeamTick": [ { "kind": "damage", "amount": 72 } ] } ] } },

    // Spawn-on-death carrier
    { "id": "steelBall", "name": "Steel Ball", "tier": 2,
      "cost": { "deployCost": 200, "unlockCost": 0 },
      "placement": { "footprint": { "w": 3, "h": 2 }, "domain": "ground" },
      "squad": { "count": 4, "xpToLevel": 650,
                 "formation": [ { "x": -0.9, "z": 0 }, { "x": -0.1, "z": 0 },
                                { "x": 0.7, "z": 0 },  { "x": -0.1, "z": -0.7 } ] },
      "member": { "hp": 5220, "speed": 1.7,
        "weapons": [ { "id": "shockRoller", "targets": ["ground"], "range": 1.5,
                       "interval": 1.4, "damage": 780 } ],
        "abilities": [ { "id": "burstOnDeath", "trigger": { "kind": "onDeath" },
                         "effects": [ { "kind": "spawnUnits", "unitId": "footman",
                                        "count": 5, "level": "inherit",
                                        "placement": "aroundSelf" } ] } ] } },

    // Aura support: buffs nearby allies while alive
    { "id": "warBanner", "name": "War Banner", "tier": 2,
      "cost": { "deployCost": 200, "unlockCost": 50 },
      "placement": { "footprint": { "w": 2, "h": 2 }, "domain": "ground" },
      "squad": { "count": 1, "xpToLevel": 600, "formation": [ { "x": 0, "z": 0 } ] },
      "member": { "hp": 4000, "speed": 1.3,
        "abilities": [ { "id": "rallyAura",
                         "trigger": { "kind": "aura", "radius": 5, "refreshS": 0.2,
                                      "filter": { "side": "ally", "domain": "any" } },
                         "effects": [ { "kind": "applyStatus", "statusId": "rallied" } ] } ] } }
  ],
  "statuses": [
    { "id": "rallied", "mods": [ { "stat": "damage", "addPct": 15 } ], "tags": ["buff"] }
  ],
  "zones": []
}
```

Coverage: the v1 + v1.5 effect/trigger vocabulary maps onto the full verified
Mechabellum behavior inventory (~28 families); the ~6 that don't compose
(interception, mind control, sweep beam, damage link, kamikaze, stance) are
`behavior` escape-hatch classes, priced at one C# record + one resolver case +
one zod variant + schemaVersion bump each.

## 6. Sim execution (BoardGame.Core/Runtime/Sim)

Single-threaded, seeded xorshift consumed in fixed phase order, dense-id
iteration. Tick phases:

A. statuses/zones expire + dirty-stat recompute →
B. scheduled triggers →
C. targeting ("soonest attackable", sticky, id tiebreak) + steering movement
   (no NavMesh) →
D. weapons (instant / volley sub-timers / beam ramp) →
E. projectiles →
F. FIFO effect queue with same-tick death cascades (depth cap 8) + XP attribution
   (kill = 50% victim worth to killer, 50% split by damage share) →
G. cleanup, event emission, end check. On wipe/cap, in-flight ordnance resolves
   before the survivor tally.

One damage pipeline: `max(1, raw × damageTakenMul) − flatBlock`, shield absorbs
first. `FinalStat = base × level × (1 + Σ buffs) × Π debuffs`, skipping
tech-sourced mods while EMP'd. Mid-battle `spawnUnits` creates NEW synthetic
squads (cardId null, survivor value 0) — members are never appended, so member
indices stay stable for the event log.

Member storage struct-of-arrays + an 8-tile spatial hash: a full 2-minute battle
resolves in well under 100ms of server CPU, once per room per round.

Headless balance harness in Core.Tests:
`fight base.footman×5 vs base.stormcaller×2 --seeds 100` → winrates, battle
length, damage tables. A repeat-seed equality test guards determinism from day one.

## 7. Battle delivery: pre-simulated event log

The server ships one `battleLog` message per round; clients play it back.
Event vocabulary (~18 types):

`BattleStarted{seed, tickRate, durationTicks, logVersion}`,
`SquadSpawned{battleSquadId, cardId?, ownerSeat, unitId, level, orientation,
anchor, members:[{index, x, z}]}`, `PositionKeyframes` (moved members only,
every 4th tick), `AttackFired`, `ProjectileSpawned{impactTick, target}`,
`ProjectileDestroyed`, `DamageApplied{layer: hull|shield, kind: hit|splash|dot,
hpAfter}`, `HealApplied`, `ShieldChanged`, `StatusApplied/StatusRemoved`
(aura refresh de-duped: true enter/expiry only), `BeamStarted/BeamEnded`,
`ZoneCreated/ZoneExpired`, `MemberDied`, `OwnershipChanged`,
`AbilityTriggered` (generic view hook keyed by abilityId), `BattleEnded`.

Events are split state-vs-transient so `SeekTo` works (reconnect catch-up,
skip-to-end, future replay scrubber). Both clients start playback at
`startAtServerMs`; the next planning phase is server-scheduled at
`startAt + duration + resultsHold`, so local speed controls never stall the
room. Worst late-game log ≈ 0.5–1 MB gzipped, once per round.

## 8. Protocol V2 & match loop

- `join{roomId, playerName, protocolVersion, resumeToken?, catalogHash?}` →
  `welcome{seat, resumeToken, catalogJson (exact dist bytes), catalogHash,
  matchConfig, match? (full resume snapshot)}`. Rooms pin the boot-time catalog.
- Phase machine: `lobby → commanderPick → planning(1) → plan-lock → battle →
  results → planning(N+1) | matchEnded`. `commanderPick`: each seat gets N
  commander offers from the catalog, sends `pickCommander{commanderId}`
  (simultaneous, hidden); both picked (or deadline → random) → picks revealed,
  starting buildings + commander units materialize as round-0 blueprint,
  planning(1) begins.
- **Planning**: income `200 × N` on entry. Commands: `buySquad`, `moveSquad`,
  `sellSquad` (this-round purchases only = free revert), `unlockUnit`,
  `buyTech` (+200 sibling escalation), `buyLevel`, `setReady`. Each carries a
  `cmdId` → `cmdAccepted{full own-state}` | `cmdRejected{code}`. Hidden
  simultaneous planning is just per-seat views — no commit-reveal crypto needed.
- **Plan-lock**: both ready or the 70s deadline. A `revealSnapshot` is captured
  once and used both as the `battleStarted{round, armies}` reveal beat and as
  next round's opponent view.
- **Battle**: `battleStarted` then `battleLog` (absent in the stub era). Both
  clients `battleAck` or deadline → `roundResult{summary, hpDamage, hp}`.
- **Survivor damage** = Σ `card.invested × membersAlive/memberCount` over the
  winner's card-backed survivors (synthetic spawns count 0); battle-cap timeout
  → both sides take it.
- **Blueprint on the wire/DB never enumerates members** — member layout is a
  pure function of (catalog, unitId, anchor, orientation).
- **Reconnect**: resumeToken → full snapshot; mid-battle resume re-sends the
  battleLog with the original `startAtServerMs` and the client seeks.
- **Persistence** (BattleServer era): one SQLite row per room at phase
  transitions (blueprints as JSON, hp, seed, RNG state).

## 9. Unity presentation pipeline

**Orientation & camera** (decided): the game plays in **landscape** with a
**free camera** — pan, pinch-zoom, and rotate (`FreeCameraController.cs` is the
starting point). The enemy owns the far half of the board, the player the near
half; the default framing sits behind the player's half looking toward the
enemy, so the landscape screen's wide axis carries the front line and depth
recedes with perspective. Placement uses zoom + a magnified drag-ghost; unit
models may visually overflow their logical footprint by ~1.3–1.5× for
readability at distance.

Strict one-way flow: Core events → `BattlePlayback` (playhead driver, local
speed controls) → `BattleViewRouter` → `SquadView` / `MemberView` /
`ProjectileView` / `ZoneView`.

- **UnitPackManifest** (ScriptableObject per ContentPack), units keyed by
  `unitId`: normalized wrapper prefab (origin = feet-center, +Z = forward —
  kills the per-model pivot offsets at the source), icon,
  `ProceduralProfile{flyer, hoverHeight, bob, lunge}`, per-**weaponId** visuals
  `{motion: Instant|Linear|Arc|Beam, muzzle/projectile/impact prefabs, sounds}`;
  pack-level tables: statusId → looping VFX, zoneType → decal, abilityId →
  one-shot VFX.
- **Placeholder fallback**: `VisualCatalog.Resolve(unitId)` never returns null —
  unknown units render as owner-tinted capsules (boxes for buildings, hovering
  for air) scaled to the catalog footprint, with TMP label, HP bars, and a
  default tracer weapon. A unit added server-side is immediately playable on a
  stale client, just ugly.
- **Animation v1 is procedural** (WalkBob/Hover/AttackAction/HitReact/
  DeathSequence/Turn) behind `IUnitAnimation`; an Animator mode is a manifest
  enum for when rigged models exist.
- **Editor tooling**: Pack Validator EditMode test (manifest ↔ catalog
  referential integrity — the contract between content and presentation) and a
  Formation Preview gizmo window with the orientation toggle.
- **Migration of the six existing units**: 6 wrapper prefabs; formations
  transcribed into the catalog (re-centered; whelp's y-offsets become the
  manifest's hoverHeight); delete the 8 `*Details.cs`/`IUnitDetails` files, the
  6 `.asset`s, and `GameProtocol.cs` unit constants; BoardManager's four
  placement-mode bools collapse to one `selectedUnitId`; the board is built
  from `matchConfig` (finally resolving the 32×32 vs 72×60 drift with data).

## 10. Day-in-the-life acceptance stories

- **Nerf archer damage**: edit one number in `base.json` → `bun run
  catalog:check` → commit → restart server. Every joining client gets the new
  catalog on welcome; tooltips, placement, and sim agree because they read the
  same bytes. ~2 minutes, no Unity build.
- **Add "Ballista" (composes existing mechanics)**: new UnitDef in a pack file →
  check → restart. Purchasable, placeable, fights, renders as a labeled
  placeholder; model + manifest entry later at leisure. Zero engine code.
- **Add a mind-control unit (genuinely new mechanic)**: one C# behavior class +
  one zod variant + codegen rerun + schemaVersion bump + client update. The
  `OwnershipChanged` event and mutable-owner member model already exist.

## 11. Implementation slices (layered onto the LeanCore migration)

1. **Catalog spine** (TS only): `core/catalog` workspace, schema, build +
   lints + hash, 6 migrated units + 1 proof unit, protocol `unitId` swap,
   catalog-on-welcome; Bun server validates placement from catalog footprints.
   *Ship test: nerf a number, restart, hash changes.*
2. **Unity consumes the catalog** (lands with the Core UPM extraction):
   codegen + drift guard, Newtonsoft, CatalogService (welcome + StreamingAssets
   fallback), catalog-driven placement/formations, manifests + placeholders,
   Formation Preview + Pack Validator, delete the Details/asset files.
   *Payoff: add/tune a unit in JSON, place it in Unity in minutes.*
3. **Match loop on Bun, stub resolver**: full round loop (hidden planning,
   economy, reveal beat, survivor-value stub resolution via invested value,
   reconnect) with no combat code. Its test suite becomes the spec for the C#
   port. *Payoff: multiplayer hidden-planning tension is playable.*
4. **Sim v1 + offline fight night** ← *first slice where you place two custom
   packs and watch them fight.* Battle sim (per-member squads, instant/volley/
   beam weapons, splash, statuses, shields, v1 triggers/effects, leveling),
   event log, balance harness, BattlePlayback + procedural animation, and a
   sandbox scene running the dual-homed Core sim in-process. Requires slices
   1–2 only; independent of slice 3.
5. **BattleServer cutover** (= the established .NET phase, now precise): port
   the match loop to C# against the ported test suite; rooms run the sim at
   plan-lock and ship `battleStarted + battleLog + roundResult`; `buyLevel`
   activates (real XP exists now); SQLite persistence; Bun retired. The
   protocol is unchanged from slice 3 except `battleLog` appears — cutover is
   a server swap, not a client migration.
6. **Vocabulary growth, on demand**: v1.5 effects (chain, execute, zones, EMP,
   resurrect), then interception/kamikaze/stance/flanks/commander spells/cards
   and specialists — each riding the same effect-kind seam. Never speculative.

## 12. Key risks

- **Codegen emitter scope creep** — cap to the exact schema subset used;
  checked-in output with byte-equality drift test; >~400 lines = hand-maintain
  the DTOs instead.
- **Invented balance numbers** — no combat code exists; all stats are first
  drafts. The headless harness is the tuning tool; expect a real tuning tax in
  slice 4. Mechabellum ratios ÷ 6 are the starting point.
- **Event-log bloat** — aura de-dup, moved-only keyframes, gzip, `logVersion`
  escape hatch; measure at slice 4.
- **Determinism discipline** — one stray `Dictionary` iteration order or
  `System.Random` breaks replay silently; the repeat-seed equality test guards it.
- **Newtonsoft + IL2CPP** — test a device build early in slice 2.
- **Stub-era feel** — slice 3's instant battles are non-representative pacing;
  timebox the loop UI polish.

## 13. Decisions (resolved 2026-07-08)

1. **Board: 32×48, landscape, free camera.** 32 lateral (row/world-X) ×
   48 deep (col/world-Z); halves meet at the midline with no no-man's-land —
   player = cols 0–23 (near), enemy = cols 24–47 (far). Chosen because with a
   free pan/zoom/rotate camera the binding constraints are placement precision
   at your-half zoom (~2.3mm tiles), range-band depth (24-deep halves fit
   three bands at ÷6), front-line width for flanking, and early-round density
   — and 32 lateral preserves every existing footprint proportion and the
   current scene width. Mechabellum transcription divisor: **÷6** (ranges,
   speeds, radii). Midline-adjacent melee placement = legitimate front-loading
   play.
2. **Fast-forward: local speed controls in live matches** (room pacing stays
   server-scheduled; an impatient player just waits at results).
3. **Theme: medieval fantasy** (Elden Ring / Warcraft 3 register), **keeping
   Mechabellum-derived stat ratios**. Slice 1 authors the base pack with
   medieval identities mapped onto Mechabellum archetypes (footman = chaff
   swarm, ballista = artillery, cathedral = command building, …); a naming
   map lives alongside the pack for tuning reference.
4. **Starting set: cathedral + barracks per side, plus commander selection** —
   pick 1 of N commanders at match start, each with different starting units,
   commander HP (= the player HP pool), and a passive ability. Commanders are
   catalog data (see §5 MatchRules).
5. **Partial-squad survivor valuation: prorated** by members alive.
6. **Slice order: 3 → 4** — multiplayer planning loop first (on the interim
   Bun server), offline sim + fight-night second.
