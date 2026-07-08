import { loadBuiltCatalog } from "@core/catalog";
import { type ServerCatalog, indexCatalog } from "./catalog";

/**
 * The real built catalog, loaded once for tests. Using the committed dist keeps
 * room/server tests honest against the actual shipped footprints and board.
 */
let cached: ServerCatalog | undefined;

export function testCatalog(): ServerCatalog {
	if (!cached) {
		const { catalog, hash } = loadBuiltCatalog();
		cached = indexCatalog(catalog, hash);
	}
	return cached;
}
