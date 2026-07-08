#!/usr/bin/env bun
/**
 * catalog:sync — copy the built catalog into the Unity client's StreamingAssets
 * so the client has an offline/menu fallback (used when no server `welcome`
 * catalog has been delivered yet). The in-editor Pack Validator warns when this
 * copy's hash is stale relative to dist.
 *
 * The bytes are copied VERBATIM (never re-serialized) so the StreamingAssets
 * hash matches dist exactly.
 */
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { DIST_CATALOG, DIST_HASH } from "../src/index";

const here = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(here, "..", "..", "..");
const STREAMING = join(
	REPO_ROOT,
	"apps",
	"game-client",
	"Assets",
	"StreamingAssets",
);

mkdirSync(STREAMING, { recursive: true });
copyFileSync(DIST_CATALOG, join(STREAMING, "catalog.json"));
copyFileSync(DIST_HASH, join(STREAMING, "catalog.hash"));
console.log(`✓ synced catalog → ${STREAMING}`);
