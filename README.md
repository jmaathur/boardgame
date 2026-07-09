# Warcaller

**Warcaller: Legion Tactics** — a Mechabellum-style medieval-fantasy
auto-battler. Two commanders muster squads onto a shared board, spend
per-round income to reinforce, and watch the battle auto-resolve. Built as a
Unity client and an authoritative Bun game server, organized as a
bun-workspaces + Turborepo monorepo.

> Working title. The monorepo package names (`boardgame`, `game-client`,
> `com.boardgame.core`) are technical identifiers and intentionally left
> unchanged.

## Layout

```
apps/
  game-client/   Unity 6 project (open this folder in Unity Hub)
  game-server/   Bun WebSocket game server (rooms, unit placement, state sync)
core/
  types/         @core/types — shared wire protocol (zod schemas, board constants)
  typescript-config/  @core/typescript-config — shared tsconfig bases
```

## Getting started

```bash
bun install        # install all workspace dependencies
bun dev            # start the game server in watch mode (port 7777)
bun run test       # run all tests (protocol + server, via turbo)
bun check-types    # tsc --noEmit across workspaces (via turbo)
bun run build      # build all packages (game server compiles to a binary)
bun run format     # biome format --write
```

The Unity client is opened separately in Unity Hub (`apps/game-client`); it
connects to the server via `Assets/BoardGame/Scripts/Net/GameServerClient.cs`.

## Protocol

Client and server speak JSON over WebSocket text frames. The contract lives
in `core/types/src/protocol.ts` and is mirrored for Unity in
`apps/game-client/Assets/BoardGame/Scripts/Net/GameProtocol.cs` — change them
together. See `apps/game-server/README.md` for the message flow.
