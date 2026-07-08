import {
	type Catalog,
	type ContentPack,
	type Effect,
	type MatchRules,
	type UnitDef,
	contentPackSchema,
	matchRulesSchema,
} from "@core/types";

/**
 * Cross-pack lint suite. These are the invariants zod alone cannot express:
 * cross-references resolve, ids are globally unique, formations match squad
 * counts, offsets sit inside the footprint, footprints fit the deploy zones,
 * and spawn chains terminate.
 *
 * A lint failure is a hard build error (the catalog would produce a broken or
 * non-deterministic game). Forge surfaces the same messages field-by-field.
 */

export type LintIssue = {
	/** Dotted path into the offending data, best-effort for UI highlighting. */
	path: string;
	message: string;
};

/** Max depth for onDeath/onSpawn spawn chains before we call it a cycle. */
const SPAWN_CHAIN_DEPTH_CAP = 8;

function unitIndex(packs: ContentPack[]): Map<string, UnitDef> {
	const index = new Map<string, UnitDef>();
	for (const pack of packs) {
		for (const unit of pack.units) index.set(unit.id, unit);
	}
	return index;
}

/** Collect every unitId referenced by a spawnUnits effect anywhere on a unit. */
function spawnedUnitIds(unit: UnitDef): string[] {
	const ids: string[] = [];
	const scanEffects = (effects: Effect[]) => {
		for (const e of effects) {
			if (e.kind === "spawnUnits") ids.push(e.unitId);
		}
	};
	for (const ability of unit.member.abilities) scanEffects(ability.effects);
	for (const weapon of unit.member.weapons) {
		scanEffects(weapon.onImpact);
		scanEffects(weapon.onBeamTick);
	}
	for (const tech of unit.techs) {
		for (const effect of tech.effects) {
			if (effect.kind === "grantAbility") scanEffects(effect.ability.effects);
		}
	}
	return ids;
}

export function lintCatalog(
	packs: ContentPack[],
	matchRules: MatchRules,
): LintIssue[] {
	const issues: LintIssue[] = [];
	const push = (path: string, message: string) =>
		issues.push({ path, message });

	// --- unique unit ids across all packs ---
	const seenUnit = new Map<string, string>();
	for (const pack of packs) {
		for (const unit of pack.units) {
			const prev = seenUnit.get(unit.id);
			if (prev !== undefined) {
				push(
					`packs.${pack.packId}.units.${unit.id}`,
					`duplicate unitId "${unit.id}" (also in pack "${prev}")`,
				);
			} else {
				seenUnit.set(unit.id, pack.packId);
			}
		}
	}

	// --- unique status / zone ids across all packs ---
	const statusIds = new Set<string>();
	const seenStatus = new Map<string, string>();
	for (const pack of packs) {
		for (const status of pack.statuses) {
			if (seenStatus.has(status.id)) {
				push(
					`packs.${pack.packId}.statuses.${status.id}`,
					`duplicate statusId "${status.id}"`,
				);
			}
			seenStatus.set(status.id, pack.packId);
			statusIds.add(status.id);
		}
	}
	const zoneIds = new Set<string>();
	for (const pack of packs) {
		for (const zone of pack.zones) {
			if (zoneIds.has(zone.id)) {
				push(
					`packs.${pack.packId}.zones.${zone.id}`,
					`duplicate zoneId "${zone.id}"`,
				);
			}
			zoneIds.add(zone.id);
		}
	}

	const units = unitIndex(packs);

	// --- per-unit structural lints ---
	for (const pack of packs) {
		for (const unit of pack.units) {
			const base = `packs.${pack.packId}.units.${unit.id}`;

			// formation length == squad count
			if (unit.squad.formation.length !== unit.squad.count) {
				push(
					`${base}.squad.formation`,
					`formation has ${unit.squad.formation.length} offsets but squad.count is ${unit.squad.count}`,
				);
			}

			// center-relative offsets inside the footprint (half-extent each way)
			const halfW = unit.placement.footprint.w / 2;
			const halfH = unit.placement.footprint.h / 2;
			unit.squad.formation.forEach((off, i) => {
				if (Math.abs(off.x) > halfW + 1e-6 || Math.abs(off.z) > halfH + 1e-6) {
					push(
						`${base}.squad.formation.${i}`,
						`offset (${off.x}, ${off.z}) is outside the ${unit.placement.footprint.w}x${unit.placement.footprint.h} footprint`,
					);
				}
			});

			// applyStatus references resolve
			const checkStatusRefs = (effects: Effect[], where: string) => {
				for (const e of effects) {
					if (e.kind === "applyStatus" && !statusIds.has(e.statusId)) {
						push(
							where,
							`applyStatus references unknown statusId "${e.statusId}"`,
						);
					}
				}
			};
			for (const ability of unit.member.abilities) {
				checkStatusRefs(ability.effects, `${base}.abilities.${ability.id}`);
			}
			for (const weapon of unit.member.weapons) {
				checkStatusRefs(
					weapon.onImpact,
					`${base}.weapons.${weapon.id}.onImpact`,
				);
				checkStatusRefs(
					weapon.onBeamTick,
					`${base}.weapons.${weapon.id}.onBeamTick`,
				);
				// every weapon targets >= 1 domain (schema enforces min 1, but keep
				// the message here for symmetry / future relaxations)
				if (weapon.targets.length === 0) {
					push(
						`${base}.weapons.${weapon.id}.targets`,
						"weapon must target at least one domain",
					);
				}
			}

			// spawnUnits references resolve
			for (const spawnedId of spawnedUnitIds(unit)) {
				if (!units.has(spawnedId)) {
					push(
						`${base}`,
						`spawnUnits references unknown unitId "${spawnedId}"`,
					);
				}
			}
		}
	}

	// --- spawn-chain depth cap (cycle guard) ---
	for (const start of units.values()) {
		const seen = new Set<string>();
		const walk = (unit: UnitDef, depth: number): void => {
			if (depth > SPAWN_CHAIN_DEPTH_CAP) {
				push(
					`packs.units.${start.id}`,
					`spawn chain from "${start.id}" exceeds depth cap ${SPAWN_CHAIN_DEPTH_CAP} (cycle?)`,
				);
				return;
			}
			for (const nextId of spawnedUnitIds(unit)) {
				if (seen.has(nextId)) continue;
				seen.add(nextId);
				const next = units.get(nextId);
				if (next) walk(next, depth + 1);
			}
		};
		walk(start, 0);
	}

	// --- match rules: board, deploy zones, buildings, commanders ---
	const board = matchRules.board;

	const rectFitsBoard = (
		row: number,
		col: number,
		w: number,
		h: number,
	): boolean =>
		row >= 0 && col >= 0 && row + w <= board.w && col + h <= board.h;

	matchRules.deployZones.forEach((zone, i) => {
		const { row, col, w, h } = zone.rect;
		if (!rectFitsBoard(row, col, w, h)) {
			push(
				`matchRules.deployZones.${i}`,
				`deploy zone rect (${row},${col} ${w}x${h}) does not fit the ${board.w}x${board.h} board`,
			);
		}
	});

	// footprints fit *some* deploy zone for their seat (ground/air units) —
	// buildings are exempt (they use fixed starting placements).
	const zonesBySeat = new Map<number, typeof matchRules.deployZones>();
	for (const zone of matchRules.deployZones) {
		const list = zonesBySeat.get(zone.seat) ?? [];
		list.push(zone);
		zonesBySeat.set(zone.seat, list);
	}
	for (const seat of [0, 1]) {
		const zones = zonesBySeat.get(seat) ?? [];
		if (zones.length === 0) {
			push("matchRules.deployZones", `no deploy zone defined for seat ${seat}`);
			continue;
		}
		const maxW = Math.max(...zones.map((z) => z.rect.w));
		const maxH = Math.max(...zones.map((z) => z.rect.h));
		for (const unit of units.values()) {
			if (unit.placement.domain === "building") continue;
			const { w, h } = unit.placement.footprint;
			if (w > maxW || h > maxH) {
				push(
					`packs.units.${unit.id}.placement.footprint`,
					`footprint ${w}x${h} does not fit any seat-${seat} deploy zone (max ${maxW}x${maxH})`,
				);
			}
		}
	}

	// starting buildings reference known units and fit the board
	matchRules.startingBuildings.forEach((b, i) => {
		const unit = units.get(b.unitId);
		if (!unit) {
			push(
				`matchRules.startingBuildings.${i}`,
				`references unknown unitId "${b.unitId}"`,
			);
			return;
		}
		const { w, h } = unit.placement.footprint;
		if (!rectFitsBoard(b.anchor.row, b.anchor.col, w, h)) {
			push(
				`matchRules.startingBuildings.${i}`,
				`building "${b.unitId}" at (${b.anchor.row},${b.anchor.col}) ${w}x${h} does not fit the board`,
			);
		}
	});

	// commander starting units reference known units
	matchRules.commanders.forEach((cmd) => {
		cmd.startingUnits.forEach((su, i) => {
			if (!units.has(su.unitId)) {
				push(
					`matchRules.commanders.${cmd.id}.startingUnits.${i}`,
					`references unknown unitId "${su.unitId}"`,
				);
			}
		});
	});

	// enough commanders to offer
	if (matchRules.commanders.length < matchRules.commandersOffered) {
		push(
			"matchRules.commanders",
			`only ${matchRules.commanders.length} commanders but commandersOffered is ${matchRules.commandersOffered}`,
		);
	}

	return issues;
}

// ---------------------------------------------------------------------------
// Validation helpers used by build.ts, the server, and Forge.
// ---------------------------------------------------------------------------

export type ValidationReport = {
	ok: boolean;
	/** zod parse errors, keyed by which file failed. */
	schemaErrors: { file: string; error: string }[];
	/** cross-pack lint issues (only meaningful when schemaErrors is empty). */
	lintIssues: LintIssue[];
};

/** Parse + lint raw pack/match-rules objects. Never throws. */
export function validateCatalogInputs(
	rawPacks: { file: string; data: unknown }[],
	rawMatchRules: { file: string; data: unknown },
): {
	report: ValidationReport;
	packs?: ContentPack[];
	matchRules?: MatchRules;
} {
	const schemaErrors: { file: string; error: string }[] = [];
	const packs: ContentPack[] = [];

	for (const { file, data } of rawPacks) {
		const parsed = contentPackSchema.safeParse(data);
		if (!parsed.success) {
			schemaErrors.push({ file, error: prettyZod(parsed.error) });
		} else {
			packs.push(parsed.data);
		}
	}

	const rulesParsed = matchRulesSchema.safeParse(rawMatchRules.data);
	if (!rulesParsed.success) {
		schemaErrors.push({
			file: rawMatchRules.file,
			error: prettyZod(rulesParsed.error),
		});
	}

	if (schemaErrors.length > 0) {
		return { report: { ok: false, schemaErrors, lintIssues: [] } };
	}

	const matchRules = rulesParsed.success ? rulesParsed.data : undefined;
	if (!matchRules) {
		return { report: { ok: false, schemaErrors, lintIssues: [] } };
	}

	const lintIssues = lintCatalog(packs, matchRules);
	return {
		report: { ok: lintIssues.length === 0, schemaErrors: [], lintIssues },
		packs,
		matchRules,
	};
}

function prettyZod(err: unknown): string {
	if (
		err &&
		typeof err === "object" &&
		"issues" in err &&
		Array.isArray((err as { issues: unknown[] }).issues)
	) {
		const issues = (
			err as { issues: { path: (string | number)[]; message: string }[] }
		).issues;
		return issues
			.map((i) => `${i.path.join(".") || "(root)"}: ${i.message}`)
			.join("; ");
	}
	return String(err);
}
