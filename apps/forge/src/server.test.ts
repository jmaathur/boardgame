import { afterEach, expect, test } from "bun:test";
import { type ForgeServer, createForgeServer, portFromEnv } from "./server";

const servers: ForgeServer[] = [];

function startServer(): ForgeServer {
	const fs = createForgeServer({ port: 0 });
	servers.push(fs);
	return fs;
}

function base(fs: ForgeServer): string {
	return `http://localhost:${fs.server.port}`;
}

// biome-ignore lint/suspicious/noExplicitAny: test-only loose JSON access.
async function json(res: Response): Promise<any> {
	return res.json();
}

afterEach(() => {
	for (const fs of servers.splice(0)) fs.stop();
});

test("GET /api/catalog returns ok with 10 units and a hash", async () => {
	const fs = startServer();
	const res = await fetch(`${base(fs)}/api/catalog`);
	expect(res.status).toBe(200);
	const data = await json(res);
	expect(data.report.ok).toBe(true);
	expect(typeof data.hash).toBe("string");
	expect(data.hash.length).toBeGreaterThan(0);
	const units = data.catalog.packs.flatMap(
		(p: { units: unknown[] }) => p.units,
	);
	expect(units.length).toBe(10);
});

test("GET /api/packs includes base.json", async () => {
	const fs = startServer();
	const res = await fetch(`${base(fs)}/api/packs`);
	expect(res.status).toBe(200);
	const data = await json(res);
	expect(data.packs).toContain("base.json");
});

test("GET /api/packs/:file rejects path traversal with 400", async () => {
	const fs = startServer();
	const res = await fetch(`${base(fs)}/api/packs/..%2f..%2fetc`);
	expect(res.status).toBe(400);
});

test("GET /api/packs/base.json returns the parsed pack", async () => {
	const fs = startServer();
	const res = await fetch(`${base(fs)}/api/packs/base.json`);
	expect(res.status).toBe(200);
	const data = await json(res);
	expect(data.packId).toBe("base");
});

test("POST /api/build returns ok with a hash matching /api/catalog", async () => {
	const fs = startServer();
	const cat = await json(await fetch(`${base(fs)}/api/catalog`));
	const build = await json(
		await fetch(`${base(fs)}/api/build`, { method: "POST" }),
	);
	expect(build.ok).toBe(true);
	expect(build.hash).toBe(cat.hash);
});

test("GET / returns HTML containing 'Forge'", async () => {
	const fs = startServer();
	const res = await fetch(`${base(fs)}/`);
	expect(res.status).toBe(200);
	expect(res.headers.get("content-type")).toContain("text/html");
	const body = await res.text();
	expect(body).toContain("Forge");
});

test("portFromEnv rejects garbage FORGE_PORT", () => {
	const prev = process.env.FORGE_PORT;
	process.env.FORGE_PORT = "not-a-port";
	expect(() => portFromEnv()).toThrow();
	if (prev === undefined) delete process.env.FORGE_PORT;
	else process.env.FORGE_PORT = prev;
});
