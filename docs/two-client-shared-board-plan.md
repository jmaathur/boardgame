# Implementation Plan — Two Clients Placing Units on One Shared Board

Status: ready to build. Detailed, execution-ready plan.

**Goal.** Two Unity clients connected to the same room see and place units on one
shared, **server-authoritative** board. Every client renders every unit,
color-coded by owner; placements appear live on both screens; the server is the
single source of truth.

**Decisions (locked):**
1. Server = **v1 `apps/game-server`** (simple `placeUnit`/`moveUnit` protocol).
2. Ownership = **open + color-coded** (both see all units; no hidden info, no
   own-half restriction).
3. Editor work = **guided**; all C# written here, user does a short click-list.

---

## 0. Ground truth (verified against the code)

| Fact | Detail | Source |
|---|---|---|
| Server is already multiplayer | Each socket → unique `playerId` (`crypto.randomUUID()`) used as `ownerId`. Valid `placeUnit`/`moveUnit` → `broadcastState(roomId)` to the `room:<id>` topic. | `apps/game-server/src/server.ts`, `room.ts` |
| Server board is catalog-driven | `getState().board = { width: catalog.board.w, height: catalog.board.h }` = **32 × 48**. Placement validated for footprint, bounds, multi-tile overlap. | `room.ts:132` |
| `unitType` is any catalog id | Server validates `unitType` against the loaded catalog, not a fixed enum. | `room.ts:72` |
| Client net layer exists, unused | `GameServerClient.cs` connects, sends `join`/`placeUnit`/`moveUnit`, raises `WelcomeReceived`/`StateReceived`/`ErrorReceived` on the main thread. **Not in the scene; referenced by nothing.** | `Net/GameServerClient.cs` |
| Client DTOs exist | `GameStateDto{ board, players[], units[] }`, `UnitDto{ id, ownerId, unitType, row, col }`, `WelcomeMessage{ playerId, roomId, state }`, `StateMessage{ state }`, `ErrorMessage{ code, message }`. | `Net/GameProtocol.cs` |
| Board manager is fully local | `PlaceUnit(IUnitDetails, Player, x, y)` spawns models immediately; own occupancy array; placement modes place only as `Player.Player1`. Hardcoded `BOARD_WIDTH=BOARD_HEIGHT=32`. | `BoardManager.cs` |
| Scene UI depends on toggle names | `FootmanButton/ArcherButton/WhelpButton` + a HolyKnight button are wired to `Toggle{Footman,Archer,Whelp,HolyKnight}PlacementMode()` — **these method names must survive.** | `SampleScene.unity` |
| Unit visual contract | `IUnitDetails{ UnitName, ModelPrefab, ModelPositionOffset, ModelRotation, ModelHeight, FootprintSize, GetSquadFormation() }`; 6 `*Details` ScriptableObjects exist. | `IUnitDetails.cs`, `BaseUnitDetails.cs` |

**Consequence:** the server needs **no changes**. All work is in three client
files plus scene wiring.

---

## 1. Architecture: the board is a *view* of server state

```
 placement-mode click on a tile
        │  (NO local spawn anymore)
        ▼
 BoardManager.RequestPlace(unitType, row, col)
        └─► client.SendPlaceUnit(unitType, row, col) ──► v1 server
                                                          validate → store
                                                          broadcast `state`
        ┌──────────────────────────────────────────────────┘
        ▼  to EVERY socket in the room (incl. the placer)
 GameServerClient.StateReceived(GameStateDto)
        └─► BoardManager.RenderState(state)   // diff by unit.id → spawn/despawn
```

Two invariants this buys us:
- **No divergence.** Neither client mutates its board directly; both mirror the
  same broadcast. Two screens can't disagree.
- **Server rejections are automatic.** If the server refuses (occupied / bounds),
  no `state` change arrives, so nothing renders. An `error` gives feedback.

---

## 2. File-by-file work

### 2.1 `GameProtocol.cs` — trust the server's board size

**Problem.** Constants say `72×60`; server says `32×48`; `BoardManager` uses
`32×32`. Three disagreeing numbers.

**Change.** Demote the constants to a *fallback only* and stop treating them as
truth. Concretely:
- Keep `BoardWidth`/`BoardHeight` but rename intent in a comment to
  "pre-connect fallback"; the live board comes from `GameStateDto.board`.
- Leave `IsInBounds` as-is (only used, if at all, before a state arrives).
- No DTO changes needed — `BoardDto{width,height}` already carries the real size.

*Risk:* none. Purely a semantics/comment change; the client already reads
`state.board`.

### 2.2 `GameServerClient.cs` — make it configurable + expose errors

Small, additive changes so two instances can share a room with distinct names
and so BoardManager can show rejection feedback.

- Add public setters/inspector fields already exist (`serverUrl`, `roomId`,
  `playerName`, `connectOnStart`) — **no change needed** for config; they're
  `[SerializeField]` and set per-instance in the Editor.
- `ErrorReceived` event **already exists** — BoardManager just needs to
  subscribe. No client change required.
- **Optional nicety:** add a `public string PlayerName { get => playerName; set => playerName = value; }`
  and `public string RoomId { … }` so two virtual players (MPPM) can be told
  apart at runtime without separate scenes. Only needed if using MPPM; skip for
  the Editor+build path.

*Net:* `GameServerClient.cs` likely needs **zero** changes for the core feature.
Its API is already sufficient.

### 2.3 `BoardManager.cs` — the real rewrite

This is 90% of the work. Rewrite around a diff-based renderer. Keep the public
`Toggle*PlacementMode()` methods (scene buttons depend on them).

**New/changed fields:**
```csharp
[SerializeField] private BoardGame.Net.GameServerClient client; // assigned in Editor
[SerializeField] private Material mineMaterial;                 // your units
[SerializeField] private Material theirsMaterial;               // opponent units
// unitType (string) -> details, built from the existing 6 SO fields:
private Dictionary<string, IUnitDetails> detailsByType;
// rendered units keyed by SERVER unit id, so we can diff broadcasts:
private readonly Dictionary<string, RenderedUnit> rendered = new();
private string myPlayerId;
private int boardW = 32, boardH = 48; // overwritten from welcome/state
private string selectedUnitType = null; // replaces the 4 placement-mode bools
```
where `RenderedUnit` holds `{ GameObject root; UnitDto last; }`.

**`detailsByType` construction** (keep the 6 serialized `*Details` fields; build
the map in `Awake`):
```csharp
detailsByType = new() {
  ["footman"]    = footmanDetails,
  ["archer"]     = archerDetails,
  ["whelp"]      = whelpDetails,
  ["holyKnight"] = holyKnightDetails,
  ["barracks"]   = barracksDetails,
  ["cathedral"]  = cathedralDetails,
};
```
Unknown `unitType` → render a **placeholder** (a tinted primitive cube scaled to
the catalog footprint) so a server-added unit type never crashes the client.

**Lifecycle:**
- `Awake()`: build `detailsByType`. If `client == null`, `Debug.LogError` and
  bail (guided-wiring guard).
- `Start()`: subscribe
  `client.WelcomeReceived += OnWelcome;`
  `client.StateReceived  += OnState;`
  `client.ErrorReceived  += OnError;`
  Do **not** call `InitializeBoard()` with hardcoded size or place any starting
  units. (The server materializes nothing in v1 — starting buildings are M1/M3
  server-side and not part of v1 `game-server`; the board starts empty, which is
  correct for "two clients place units.")
- `OnDestroy()`: unsubscribe.

**`OnWelcome(WelcomeMessage w)`:**
- `myPlayerId = w.playerId;`
- If the tile grid isn't built yet, build it from `w.state.board` (`BuildGrid(w.state.board.width, w.state.board.height)`).
- `RenderState(w.state);`

**`OnState(GameStateDto s)`:**
- If board dims changed / grid unbuilt, `BuildGrid(s.board.width, s.board.height)`.
- `RenderState(s);`

**`RenderState(GameStateDto s)` — the reconciler (core algorithm):**
```
seen = new HashSet<string>()
foreach unit in s.units:
    seen.add(unit.id)
    if rendered contains unit.id:
        existing = rendered[unit.id]
        if existing.last.row != unit.row || col changed || unitType changed:
            // simplest correct v1: destroy + respawn at new spot
            Destroy(existing.root); SpawnUnit(unit) → rendered[unit.id]
        // else unchanged: leave it
    else:
        SpawnUnit(unit) → rendered[unit.id]
// remove units the server no longer reports (sold / owner left)
foreach id in rendered.keys not in seen:
    Destroy(rendered[id].root); rendered.remove(id)
```

**`SpawnUnit(UnitDto u)`** — reuse the existing squad-spawn logic, generalized:
- Resolve `details = detailsByType.GetValueOrDefault(u.unitType)` (else placeholder path).
- `bool mine = (u.ownerId == myPlayerId);`
- Rotation: base `details.ModelRotation`; if `!mine`, pre-multiply
  `Quaternion.Euler(0,180,0)` (opponent faces you — replaces the old
  `Player.Player2` branch, now keyed on ownership not a hardcoded enum).
- Spawn each model of `details.GetSquadFormation()` at
  `new Vector3(u.row, details.ModelHeight, u.col) + formationOffset + details.ModelPositionOffset`,
  parented under a `squadParent` GameObject named `{u.ownerId}_{u.unitType}_{u.id}`.
- **Tint:** apply `mine ? mineMaterial : theirsMaterial` to each model's
  renderers (or a `MaterialPropertyBlock` color if you'd rather not swap
  materials). This is the visible ownership coding.
- Return `new RenderedUnit { root = squadParent, last = u }`.

**Placement input (`Update` → click handler), replacing the 4 bool modes:**
- The `Toggle*PlacementMode()` methods now just set `selectedUnitType`:
  ```csharp
  public void ToggleFootmanPlacementMode()  => selectedUnitType = Flip("footman");
  public void ToggleArcherPlacementMode()   => selectedUnitType = Flip("archer");
  public void ToggleWhelpPlacementMode()    => selectedUnitType = Flip("whelp");
  public void ToggleHolyKnightPlacementMode()=> selectedUnitType = Flip("holyKnight");
  // Flip(t): returns t if not already selected, else null (toggle off)
  ```
- On left-click over a `BoardTile`:
  ```csharp
  if (selectedUnitType == null) return;             // just inspecting
  int row = bt.row, col = bt.col;                   // bt.row=x, bt.col=y
  client.SendPlaceUnit(selectedUnitType, row, col); // ask the server
  selectedUnitType = null;                          // one placement per selection
  // DO NOT spawn locally — wait for the state broadcast
  ```

**`OnError(ErrorMessage e)`:** `Debug.LogWarning($"[place rejected] {e.code}: {e.message}")`
and (optional polish) briefly flash the last-clicked tile red. No unit appears —
correct, because the server sent no state change.

**Delete/retire from `BoardManager`:**
- Local `tileOccupancy` array + `AreTilesAvailable`/`MarkTilesOccupied`/`ClearTileOccupancy`
  (server is the authority now).
- The 4 placement-mode bools.
- The 4 hardcoded starting-building `PlaceUnit(...)` calls in the old
  `InitializeBoard`.
- The old `PlaceUnit(IUnitDetails, Player, x, y)` signature (superseded by
  `SpawnUnit(UnitDto)`), unless you keep it for an offline mode.

**Keep:** `BuildGrid` (formerly `InitializeBoard`, now parameterized by w/h),
`GetTile`, the `BoardTile` tag raycast, `ParentTransform`/`BoardTilePrefab`.

---

## 3. Editor wiring checklist (user does)

1. Open `Assets/Scenes/SampleScene.unity`.
2. Create empty GameObject **`Net`** → Add Component → **GameServerClient**.
3. On `GameServerClient`: `serverUrl = ws://localhost:7777/ws`,
   `roomId = lobby`, `playerName = A` (use `B` in the second instance),
   `connectOnStart = true`.
4. Select **`_BoardManager`** → drag **`Net`** into the new **Client** field.
5. Create two simple materials (e.g. teal + clay), assign to **Mine** / **Theirs**.
6. Confirm the Footman/Archer/Whelp/HolyKnight buttons still point at the
   `Toggle*PlacementMode` methods (they will — names unchanged).

## 4. Running two clients

```bash
# 1. start the authoritative server FIRST
cd apps/game-server && bun run dev        # ws://localhost:7777/ws
```
Then two client instances, same `roomId=lobby`:
- **Simplest:** File → Build (a standalone build), run it, and press Play in the
  Editor. Two processes.
- **Zero-build:** Multiplayer Play Mode (`com.unity.multiplayer.center` is
  installed) → Window → Multiplayer → Multiplayer Play Mode → enable a 2nd
  virtual player.

## 5. Acceptance criteria

- [ ] Two clients join `lobby` and see the identical set of units.
- [ ] A places a Footman → within a frame B sees it, tinted as opponent; and the
      reverse for B.
- [ ] Placing on an occupied/out-of-bounds tile shows nothing on either client;
      the placer gets an error log/flash.
- [ ] Disconnecting one client removes its units from the other's board (the
      server prunes a departed player and broadcasts).
- [ ] The placer's own unit appears only after the server confirms (no local
      pre-spawn).

## 6. Edge cases & how they're handled

| Case | Handling |
|---|---|
| Placer clicks twice fast | Each click sends `placeUnit`; server validates each; overlaps rejected. `selectedUnitType=null` after one send prevents accidental double-place from one selection. |
| Unknown `unitType` from server | `SpawnUnit` placeholder path (tinted cube sized to footprint) — never crashes. |
| Board size differs from 32×48 later | Grid rebuilt from `state.board`; no hardcoded size remains. |
| Reconnect / late joiner | `welcome.state` already carries the full `units[]`; `RenderState` builds them all. |
| Two placements same tile, race | Server serializes per room; second is rejected with `tileOccupied`. |
| Move (drag) support | Out of scope for v1 shared-placement; if added, `moveUnit` keeps the id so `RenderState` sees a position change and respawns. |

## 7. What this deliberately is NOT

- Not the v2 match loop — no commanders, economy, hidden planning, or battles.
- No own-half restriction — placement is open across the whole 32×48 board
  (the v1 server has no deploy-zone check; adding one is a separate server task).
- No client-side prediction — placement waits for the server round-trip (fine at
  LAN/local latency; the whole point is a single authoritative truth).

## 8. Commit slices

1. `GameProtocol.cs` comment/semantics (board size from state).
2. `BoardManager.cs` rewrite: state-driven render + server-routed placement +
   ownership tint + catalog-size grid; keep `Toggle*` method names.
3. Docs + this checklist.

No TS/dotnet changes → the existing CI (both jobs) stays green; the feature is
verified by running the server + two clients per §4–5.

## 9. Effort estimate

- `GameProtocol.cs`: ~10 min (trivial).
- `BoardManager.cs`: ~2–3 hrs (the reconciler + spawn generalization + input
  rewrite + material tinting).
- Editor wiring + first two-client test: ~30 min.
- Placeholder-unit + tile-flash polish: ~30 min (optional).

Total: **~half a day** to a working two-client shared board, most of it in one
file, with zero server changes.
