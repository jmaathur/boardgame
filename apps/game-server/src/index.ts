import { createGameServer } from "./server";

const { server } = createGameServer();

console.log(
	`game-server listening on http://localhost:${server.port} (WebSocket endpoint: /ws)`,
);
