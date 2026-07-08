import { createHash } from "node:crypto";
import type { Catalog } from "@core/types";

/**
 * Canonicalization + hashing. Done EXACTLY ONCE, here, so the bytes shipped
 * over the wire, hashed, and read by every runtime are identical.
 *
 * Canonical form: recursively key-sorted, minified JSON. The hash is sha256 of
 * those exact bytes (hex).
 */

function sortValue(value: unknown): unknown {
	if (Array.isArray(value)) return value.map(sortValue);
	if (value && typeof value === "object") {
		const sorted: Record<string, unknown> = {};
		for (const key of Object.keys(value as Record<string, unknown>).sort()) {
			sorted[key] = sortValue((value as Record<string, unknown>)[key]);
		}
		return sorted;
	}
	return value;
}

/** Deterministic, key-sorted, minified JSON string for `catalog`. */
export function canonicalize(catalog: Catalog): string {
	return JSON.stringify(sortValue(catalog));
}

/** sha256 (hex) of the exact canonical bytes. */
export function hashCatalog(canonicalJson: string): string {
	return createHash("sha256").update(canonicalJson, "utf8").digest("hex");
}
