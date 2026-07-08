import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { type Catalog, SCHEMA_VERSION, catalogSchema } from "@core/types";
import { canonicalize, hashCatalog } from "./canonical";
import { type ValidationReport, validateCatalogInputs } from "./lint";

export * from "./lint";
export * from "./canonical";

const here = dirname(fileURLToPath(import.meta.url));
/** core/catalog package root (…/core/catalog). */
export const CATALOG_ROOT = resolve(here, "..");
export const DATA_DIR = join(CATALOG_ROOT, "data");
export const PACKS_DIR = join(DATA_DIR, "packs");
export const MATCH_RULES_FILE = join(DATA_DIR, "match-rules.json");
export const DIST_DIR = join(CATALOG_ROOT, "dist");
export const DIST_CATALOG = join(DIST_DIR, "catalog.json");
export const DIST_HASH = join(DIST_DIR, "catalog.hash");

/** A raw pack file is any *.json in data/packs whose name ends in ".json". */
function listPackFiles(): string[] {
	return readdirSync(PACKS_DIR)
		.filter((f) => f.endsWith(".json"))
		.sort()
		.map((f) => join(PACKS_DIR, f));
}

function readJson(file: string): unknown {
	return JSON.parse(readFileSync(file, "utf8"));
}

export type BuildResult = {
	report: ValidationReport;
	/** Present only when the report is ok. */
	catalog?: Catalog;
	canonicalJson?: string;
	hash?: string;
};

/**
 * Read data/ from disk, validate + lint, and (on success) assemble the
 * canonical catalog + hash. Pure — writes nothing. build.ts is the only writer.
 */
export function buildCatalog(): BuildResult {
	const packFiles = listPackFiles();
	const rawPacks = packFiles.map((file) => ({ file, data: readJson(file) }));
	const rawMatchRules = {
		file: MATCH_RULES_FILE,
		data: readJson(MATCH_RULES_FILE),
	};

	const { report, packs, matchRules } = validateCatalogInputs(
		rawPacks,
		rawMatchRules,
	);
	if (!report.ok || !packs || !matchRules) return { report };

	const catalog: Catalog = {
		schemaVersion: SCHEMA_VERSION,
		packs,
		matchRules,
	};

	// Belt-and-suspenders: the assembled catalog must itself parse.
	const finalParse = catalogSchema.safeParse(catalog);
	if (!finalParse.success) {
		return {
			report: {
				ok: false,
				schemaErrors: [
					{ file: "(assembled catalog)", error: String(finalParse.error) },
				],
				lintIssues: [],
			},
		};
	}

	const canonicalJson = canonicalize(finalParse.data);
	const hash = hashCatalog(canonicalJson);
	return { report, catalog: finalParse.data, canonicalJson, hash };
}

/** Load the built catalog from dist/. Throws if missing or hash-mismatched. */
export function loadBuiltCatalog(): { catalog: Catalog; hash: string } {
	const canonicalJson = readFileSync(DIST_CATALOG, "utf8");
	const declaredHash = readFileSync(DIST_HASH, "utf8").trim();
	const actualHash = hashCatalog(canonicalJson);
	if (declaredHash !== actualHash) {
		throw new Error(
			`dist/catalog.hash (${declaredHash}) does not match dist/catalog.json (${actualHash}) — run catalog build`,
		);
	}
	const parsed = catalogSchema.parse(JSON.parse(canonicalJson));
	return { catalog: parsed, hash: actualHash };
}
