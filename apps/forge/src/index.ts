import { createForgeServer } from "./server";

const { server } = createForgeServer();

console.log(`Forge v0 running at http://localhost:${server.port}`);
