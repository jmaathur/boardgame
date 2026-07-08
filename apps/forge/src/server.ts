import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, join } from "node:path";
import {
	DIST_DIR,
	MATCH_RULES_FILE,
	PACKS_DIR,
	buildCatalog,
	canonicalize,
	hashCatalog,
} from "@core/catalog";
import { catalogSchema } from "@core/types";
import type { Server } from "bun";
import { z } from "zod";
import { INDEX_HTML } from "./ui";

export const DEFAULT_PORT = 7780;
const DIST_CATALOG = join(DIST_DIR, "catalog.json");
const DIST_HASH = join(DIST_DIR, "catalog.hash");

export type ForgeServer = {
	server: Server<undefined>;
	stop: () => void;
};

/**
 * Resolves the listen port from FORGE_PORT, failing loudly on garbage
 * (Number("abc") is NaN and Number("") is 0 — Bun.serve silently binds a
 * random ephemeral port for both instead of erroring). Mirrors game-server.
 */
export function portFromEnv(): number {
	const raw = process.env.FORGE_PORT;
	if (raw === undefined || raw === "") return DEFAULT_PORT;
	const parsed = Number(raw);
	if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) {
		throw new Error(
			`invalid FORGE_PORT ${JSON.stringify(raw)} — expected an integer between 1 and 65535`,
		);
	}
	return parsed;
}

const html = (body: string, status = 200): Response =>
	new Response(body, {
		status,
		headers: { "content-type": "text/html; charset=utf-8" },
	});

/** Reject anything that is not a bare `<name>.json` (no traversal, no dirs). */
function isSafePackFile(file: string): boolean {
	return (
		file.endsWith(".json") &&
		!file.includes("/") &&
		!file.includes("\\") &&
		!file.includes("\0") &&
		basename(file) === file
	);
}

function jsonSchemaExport(): unknown {
	try {
		const toJSONSchema = (z as { toJSONSchema?: (s: unknown) => unknown })
			.toJSONSchema;
		if (typeof toJSONSchema !== "function") return { unavailable: true };
		return toJSONSchema(catalogSchema);
	} catch {
		return { unavailable: true };
	}
}

/**
 * Forge v0 backend. Serves the browse/validate/build UI plus a small JSON API
 * over @core/catalog. Pass `port: 0` to bind an ephemeral port (used by tests).
 */
export function createForgeServer(
	options: { port?: number } = {},
): ForgeServer {
	const server = Bun.serve({
		port: options.port ?? portFromEnv(),
		async fetch(req) {
			const url = new URL(req.url);
			const { pathname } = url;
			const method = req.method.toUpperCase();

			// --- UI ---
			if (
				(pathname === "/" || pathname === "/index.html") &&
				method === "GET"
			) {
				return html(INDEX_HTML);
			}

			// --- GET /api/catalog ---
			if (pathname === "/api/catalog" && method === "GET") {
				const { report, catalog, hash } = buildCatalog();
				return Response.json({ report, catalog, hash });
			}

			// --- GET /api/packs ---
			if (pathname === "/api/packs" && method === "GET") {
				const files = readdirSync(PACKS_DIR)
					.filter((f) => f.endsWith(".json"))
					.sort();
				return Response.json({ packs: files });
			}

			// --- GET /api/packs/:file ---
			if (pathname.startsWith("/api/packs/") && method === "GET") {
				const file = decodeURIComponent(pathname.slice("/api/packs/".length));
				if (!isSafePackFile(file)) {
					return Response.json(
						{ error: `invalid pack filename ${JSON.stringify(file)}` },
						{ status: 400 },
					);
				}
				try {
					const raw = readFileSync(join(PACKS_DIR, file), "utf8");
					return Response.json(JSON.parse(raw));
				} catch {
					return Response.json(
						{ error: `pack not found: ${file}` },
						{ status: 404 },
					);
				}
			}

			// --- GET /api/match-rules ---
			if (pathname === "/api/match-rules" && method === "GET") {
				try {
					const raw = readFileSync(MATCH_RULES_FILE, "utf8");
					return Response.json(JSON.parse(raw));
				} catch {
					return Response.json(
						{ error: "match-rules.json not found" },
						{ status: 404 },
					);
				}
			}

			// --- GET /api/schema ---
			if (pathname === "/api/schema" && method === "GET") {
				return Response.json(jsonSchemaExport());
			}

			// --- POST /api/build ---
			if (pathname === "/api/build" && method === "POST") {
				const result = buildCatalog();
				if (!result.report.ok || !result.catalog) {
					return Response.json({ ok: false, report: result.report });
				}
				const canonicalJson = canonicalize(result.catalog);
				const hash = hashCatalog(canonicalJson);
				mkdirSync(DIST_DIR, { recursive: true });
				writeFileSync(DIST_CATALOG, canonicalJson);
				writeFileSync(DIST_HASH, `${hash}\n`);
				return Response.json({ ok: true, hash, report: result.report });
			}

			return new Response("not found", { status: 404 });
		},
	});

	return {
		server,
		stop: () => server.stop(true),
	};
}
