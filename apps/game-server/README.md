# game-server

Authoritative multiplayer server for the board game, built on `Bun.serve`.
It owns room membership and unit positions on the 72x60 board; clients send
intents and receive authoritative state broadcasts.

## Endpoints

- `GET /health` — liveness probe: `{ "status": "ok", "rooms": n, "players": n }`
- `GET /ws` — WebSocket upgrade. One JSON message per text frame, using the
  protocol defined in `@core/types` (`core/types/src/protocol.ts`).

## Protocol flow

1. Client connects to `/ws` and sends `{"type":"join","roomId":"lobby","playerName":"Jeev"}`.
2. Server replies with `welcome` (contains your `playerId` and the room state);
   everyone else in the room receives a `state` broadcast.
3. Client sends `placeUnit` / `moveUnit` intents. Valid actions trigger a
   `state` broadcast to the whole room; invalid ones return an `error` to the
   sender only.
4. Disconnecting removes the player and their units; empty rooms are deleted.

The Unity client mirror of this protocol lives in
`apps/game-client/Assets/BoardGame/Scripts/Net/`.

## Commands

```bash
bun run dev          # watch mode on PORT (default 7777)
bun run start        # run once
bun run test         # room logic + WebSocket integration tests
bun run check-types  # tsc --noEmit
bun run build        # compile a standalone executable to dist/game-server
```
