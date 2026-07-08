# game-client

The Unity game client (Unity 6000.0.32f1, URP). This is a standard Unity
project — open **this directory** (`apps/game-client`) in Unity Hub.

> The project moved here from the repository root when the repo became a
> monorepo. If Unity Hub still lists the old path, remove that entry and
> add `apps/game-client` instead. `Library/`, `Temp/`, etc. regenerate on
> first open.

## Talking to the game server

`Assets/BoardGame/Scripts/Net/` contains the multiplayer client:

- `GameProtocol.cs` — C# mirror of the wire protocol in
  `core/types/src/protocol.ts` (keep them in sync).
- `GameServerClient.cs` — `MonoBehaviour` that connects to the game server
  over WebSocket, joins a room, and raises main-thread events
  (`WelcomeReceived`, `StateReceived`, `ErrorReceived`).

Usage:

1. Start the server: `bun dev` from the repo root (or `bun run dev` in
   `apps/game-server`). It listens on `ws://localhost:7777/ws`.
2. Add the `GameServerClient` component to a GameObject in the scene and set
   the room and player name in the inspector.
3. Subscribe from your own scripts:

```csharp
var client = GetComponent<BoardGame.Net.GameServerClient>();
client.StateReceived += state => Debug.Log($"units on board: {state.units.Length}");
client.SendPlaceUnit(BoardGame.Net.GameProtocol.UnitArcher, row: 10, col: 12);
```

## Board conventions

- The wire protocol declares a 72x60 board (`GameProtocol.BoardWidth` x
  `BoardHeight`). Note that `BoardManager.cs` currently builds a 32x32 grid
  from its own local constants — reconcile the two when the final board size
  is decided.
- `row` is the world-X tile index, `col` is the world-Z tile index; a tile
  sits at world position `(row, 0, col)` — see `BoardManager.cs`.
