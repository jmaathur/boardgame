import { type Catalog, type UnitDef, catalogSchema } from "@core/types";

/**
 * Server-side catalog access. The server loads the built catalog once at boot
 * (from @core/catalog's dist bytes) and validates placement against real unit
 * footprints and the catalog board — resolving the old hardcoded 72x60 vs
 * 32x32 board drift with data. Protocol stays v1 for M1; only the placement
 * VALIDATION becomes catalog-driven.
 */

export type ServerCatalog = {
	catalog: Catalog;
	hash: string;
	unitById: Map<string, UnitDef>;
	board: { w: number; h: number };
};

/** Index a parsed catalog into the lookups the room needs. */
export function indexCatalog(catalog: Catalog, hash: string): ServerCatalog {
	const unitById = new Map<string, UnitDef>();
	for (const pack of catalog.packs) {
		for (const unit of pack.units) unitById.set(unit.id, unit);
	}
	return { catalog, hash, unitById, board: catalog.matchRules.board };
}

/**
 * Parse raw catalog bytes (the exact dist JSON) into a ServerCatalog. Throws on
 * a schema mismatch — a boot-time failure is correct; a server must never run
 * on an unparseable catalog.
 */
export function parseServerCatalog(
	canonicalJson: string,
	hash: string,
): ServerCatalog {
	const catalog = catalogSchema.parse(JSON.parse(canonicalJson));
	return indexCatalog(catalog, hash);
}

/**
 * Tiles occupied by a unit anchored at (row, col). The anchor is the min
 * corner; the footprint extends +w along the row axis (world-X, wide) and +h
 * along the col axis (world-Z, deep). Returns the inclusive tile-index ranges.
 */
export function footprintTiles(
	unit: UnitDef,
	row: number,
	col: number,
): { rowStart: number; rowEnd: number; colStart: number; colEnd: number } {
	const { w, h } = unit.placement.footprint;
	return {
		rowStart: row,
		rowEnd: row + w - 1,
		colStart: col,
		colEnd: col + h - 1,
	};
}

/** Do two axis-aligned footprint rectangles overlap? */
export function footprintsOverlap(
	a: { rowStart: number; rowEnd: number; colStart: number; colEnd: number },
	b: { rowStart: number; rowEnd: number; colStart: number; colEnd: number },
): boolean {
	return (
		a.rowStart <= b.rowEnd &&
		b.rowStart <= a.rowEnd &&
		a.colStart <= b.colEnd &&
		b.colStart <= a.colEnd
	);
}
