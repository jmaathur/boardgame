import { createMatchServer } from "./matchServer";

// Entry point for the protocol-V2 match server (the Mechabellum-style loop).
// The V1 placement-demo server still lives at src/index.ts until the M5 cutover.
const { server, catalog } = createMatchServer();

console.log(
	`match-server listening on http://localhost:${server.port} (WebSocket: /ws, protocol v2, catalog ${catalog.hash.slice(0, 12)})`,
);
