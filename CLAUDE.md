# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A multiplayer board game monorepo (bun workspaces + Turborepo, modeled on the golf-app repo conventions):

- **apps/game-client/** — Unity 6000.0.32f1 project (URP, TextMesh Pro, New Input System). Open this folder in Unity Hub.
- **apps/game-server/** — Authoritative game server on `Bun.serve` (WebSocket rooms, unit placement/movement, state broadcast).
- **core/types/** — `@core/types`: shared wire protocol (zod schemas + board constants).
- **core/typescript-config/** — `@core/typescript-config`: shared tsconfig bases (`base.json`, `bun.json`).

Formatting is Biome (tabs); the Unity project is excluded from Biome. There is no eslint.

## Development Commands

```bash
bun install        # install workspace deps
bun dev            # game server in watch mode (PORT env, default 7777)
bun run test       # turbo run test (bun test in core/types and apps/game-server)
bun check-types    # turbo run check-types (tsc --noEmit)
bun run build      # turbo run build; game-server compiles a binary to dist/
bun run format     # biome format --write
```

Run a single test file: `bun test src/server.test.ts` from within the package directory. Note: bare `bun test` is Bun's built-in runner — it runs all tests fine but bypasses the package.json script and therefore turbo's check-types gate; use `bun run test` for the full pipeline.

## Wire Protocol (client ⇄ server)

JSON messages over WebSocket text frames, discriminated by `type`. The source of truth is `core/types/src/protocol.ts`; it is mirrored by hand in `apps/game-client/Assets/BoardGame/Scripts/Net/GameProtocol.cs` — **any protocol change must update both files**.

- Client → server: `join` (roomId, playerName), `placeUnit` (unitType, row, col), `moveUnit` (unitId, row, col), `ping`.
- Server → client: `welcome` (playerId + state), `state` (broadcast after every mutation), `error` (code + message, sender only), `pong`.
- Flow: connect to `/ws`, send `join` first; valid actions broadcast fresh `state` to the room; invalid ones return `error` to the sender only. `GET /health` is the liveness probe.

Server internals: `apps/game-server/src/room.ts` is pure game logic (unit-testable, no transport); `src/server.ts` wires it to `Bun.serve` WebSockets using per-room pub/sub topics (`room:<id>`).

## Unity Client Architecture

### Board System

- **BoardManager** (`apps/game-client/Assets/BoardGame/Scripts/BoardManager.cs`): Manages the tile grid — currently 32x32 via its own local `BOARD_WIDTH`/`BOARD_HEIGHT` constants, smaller than the 72x60 board the wire protocol declares (see Important Notes)
  - Instantiates tiles at Start via `InitializeBoard()`
  - Stores tiles in a 2D array `gameTiles[BOARD_WIDTH, BOARD_HEIGHT]`
  - Each tile is positioned at (x, 0, y) in world space
  - Tiles are parented to `ParentTransform` using `SetParent(ParentTransform, false)` to preserve world positions
  - Sets up each tile's `BoardTile` component with row/col coordinates and caption text format: "B1:[xx,yy]"
  - Handles mouse click detection using raycasting to detect tiles with "BoardTile" tag

- **BoardTile** (`apps/game-client/Assets/BoardTile.cs`): Individual tile component
  - Stores grid position (`row`, `col`)
  - Has a TextMesh Pro text component (`boardTileCaptionText`) for displaying tile coordinates
  - Must be tagged with "BoardTile" for click detection to work

### Networking

- **GameServerClient** (`apps/game-client/Assets/BoardGame/Scripts/Net/GameServerClient.cs`): `MonoBehaviour` WebSocket client for the game server. Background receive loop; events (`WelcomeReceived`, `StateReceived`, `ErrorReceived`) are dispatched on the main thread via a queue drained in `Update()`.
- **GameProtocol** (`apps/game-client/Assets/BoardGame/Scripts/Net/GameProtocol.cs`): protocol constants and `JsonUtility` DTOs mirroring `@core/types`.

### Key Interaction Pattern

Mouse clicks are handled via Physics.Raycast in BoardManager.Update():
1. Ray cast from camera through mouse position
2. Detect hits on objects tagged "BoardTile"
3. Retrieve BoardTile component and log caption text

## Important Notes

- The wire protocol declares a 72x60 board (`BOARD_WIDTH` x `BOARD_HEIGHT` in `@core/types`, `GameProtocol.BoardWidth/Height` in C#), but `BoardManager.cs` currently instantiates a 32x32 grid from its own local constants — a known mismatch to reconcile when the final board size is decided (the server accepts placements up to 72x60 that have no tile on the current Unity board)
- `row` is the world-X tile index, `col` is the world-Z tile index — row indexes the wide axis even though that reads "sideways"
- Tiles must have "BoardTile" tag for click detection
- Caption text format is "B1:[xx,yy]" where xx=row, yy=col (zero-padded to 2 digits)
- New C# scripts need Unity `.meta` files; match the existing slim format (fileFormatVersion + guid, no trailing newline) with a fresh GUID
- Unity generates `Library/`, `Temp/`, `*.csproj`, `*.sln` inside `apps/game-client` — all gitignored
