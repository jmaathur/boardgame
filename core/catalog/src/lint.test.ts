import { describe, expect, test } from "bun:test";
import type { ContentPack, MatchRules, UnitDef } from "@core/types";
import { buildCatalog } from "./index";
import { lintCatalog, validateCatalogInputs } from "./lint";

// A minimal valid unit builder so tests can express only what they exercise.
function unit(overrides: Partial<UnitDef> & { id: string }): UnitDef {
	return {
		name: overrides.id,
		description: "",
		tier: 1,
		cost: { deployCost: 100, unlockCost: 0 },
		placement: { footprint: { w: 2, h: 2 }, domain: "ground" },
		squad: { count: 1, xpToLevel: 100, formation: [{ x: 0, z: 0 }] },
		member: { hp: 100, speed: 1, flatBlock: 0, weapons: [], abilities: [] },
		techs: [],
		...overrides,
	} as UnitDef;
}

function pack(units: UnitDef[]): ContentPack {
	return { packId: "t", version: "1.0.0", units, statuses: [], zones: [] };
}

function rules(overrides: Partial<MatchRules> = {}): MatchRules {
	return {
		board: { w: 32, h: 48 },
		deployZones: [
			{
				seat: 0,
				rect: { row: 0, col: 0, w: 32, h: 24 },
				availableFromRound: 1,
			},
			{
				seat: 1,
				rect: { row: 0, col: 24, w: 32, h: 24 },
				availableFromRound: 1,
			},
		],
		income: { perRoundIncrement: 200, startingIncome: 200, carryOver: true },
		deploysPerRound: 2,
		unlocksPerRound: 1,
		timers: {
			deploySeconds: 70,
			battleSeconds: 120,
			resultsHoldSeconds: 5,
			commanderPickSeconds: 30,
		},
		leveling: {
			hpFactorPerLevel: 1,
			atkFactorPerLevel: 1,
			upgradeCostFraction: 0.5,
		},
		techPriceEscalation: 200,
		commandersOffered: 1,
		startingBuildings: [],
		commanders: [
			{
				id: "c1",
				name: "C1",
				description: "",
				hp: 5000,
				startingUnits: [],
				ability: [],
			},
		],
		...overrides,
	};
}

describe("catalog build (real data)", () => {
	test("the base catalog validates and lints clean", () => {
		const { report } = buildCatalog();
		expect(report.schemaErrors).toEqual([]);
		expect(report.lintIssues).toEqual([]);
		expect(report.ok).toBe(true);
	});

	test("the build is deterministic (same bytes, same hash)", () => {
		const a = buildCatalog();
		const b = buildCatalog();
		expect(a.hash).toBe(b.hash);
		expect(a.canonicalJson).toBe(b.canonicalJson);
	});

	test("the canonical json is key-sorted and minified", () => {
		const { canonicalJson } = buildCatalog();
		expect(canonicalJson).toBeDefined();
		// minified: no newlines, no ": " spacing
		expect(canonicalJson).not.toContain("\n");
		expect(canonicalJson).not.toContain(": ");
		// schemaVersion sorts before other top-level keys
		expect(canonicalJson!.startsWith('{"matchRules"')).toBe(true);
	});
});

describe("lint: cross-references", () => {
	test("flags a duplicate unitId across packs", () => {
		const issues = lintCatalog(
			[pack([unit({ id: "dup" })]), pack([unit({ id: "dup" })])],
			rules(),
		);
		expect(issues.some((i) => i.message.includes("duplicate unitId"))).toBe(
			true,
		);
	});

	test("flags an unknown spawnUnits reference", () => {
		const spawner = unit({
			id: "spawner",
			member: {
				hp: 100,
				speed: 1,
				flatBlock: 0,
				weapons: [],
				abilities: [
					{
						id: "die",
						trigger: { kind: "onDeath" },
						effects: [
							{
								kind: "spawnUnits",
								unitId: "ghost",
								count: 1,
								level: "inherit",
								placement: "aroundSelf",
							},
						],
					},
				],
			},
		});
		const issues = lintCatalog([pack([spawner])], rules());
		expect(
			issues.some((i) => i.message.includes('unknown unitId "ghost"')),
		).toBe(true);
	});

	test("flags an unknown applyStatus reference", () => {
		const u = unit({
			id: "buffer",
			member: {
				hp: 100,
				speed: 1,
				flatBlock: 0,
				weapons: [],
				abilities: [
					{
						id: "buff",
						trigger: { kind: "onSpawn" },
						effects: [{ kind: "applyStatus", statusId: "missing" }],
					},
				],
			},
		});
		const issues = lintCatalog([pack([u])], rules());
		expect(
			issues.some((i) => i.message.includes('unknown statusId "missing"')),
		).toBe(true);
	});
});

describe("lint: formation & footprint", () => {
	test("flags formation length != squad count", () => {
		const u = unit({
			id: "mismatch",
			squad: { count: 3, xpToLevel: 100, formation: [{ x: 0, z: 0 }] },
		});
		const issues = lintCatalog([pack([u])], rules());
		expect(issues.some((i) => i.message.includes("but squad.count is 3"))).toBe(
			true,
		);
	});

	test("flags a formation offset outside the footprint", () => {
		const u = unit({
			id: "spilled",
			placement: { footprint: { w: 2, h: 2 }, domain: "ground" },
			squad: { count: 1, xpToLevel: 100, formation: [{ x: 5, z: 0 }] },
		});
		const issues = lintCatalog([pack([u])], rules());
		expect(issues.some((i) => i.message.includes("outside"))).toBe(true);
	});
});

describe("lint: match rules", () => {
	test("flags a starting building that references an unknown unit", () => {
		const issues = lintCatalog(
			[pack([unit({ id: "known" })])],
			rules({
				startingBuildings: [
					{
						seat: 0,
						unitId: "unknownBuilding",
						anchor: { row: 0, col: 0 },
						orientation: "north",
					},
				],
			}),
		);
		expect(
			issues.some((i) =>
				i.message.includes('unknown unitId "unknownBuilding"'),
			),
		).toBe(true);
	});

	test("flags too few commanders for commandersOffered", () => {
		const issues = lintCatalog(
			[pack([unit({ id: "u" })])],
			rules({ commandersOffered: 5 }),
		);
		expect(issues.some((i) => i.message.includes("commandersOffered"))).toBe(
			true,
		);
	});

	test("flags a footprint too large for any deploy zone", () => {
		const huge = unit({
			id: "huge",
			placement: { footprint: { w: 40, h: 40 }, domain: "ground" },
			squad: { count: 1, xpToLevel: 100, formation: [{ x: 0, z: 0 }] },
		});
		const issues = lintCatalog([pack([huge])], rules());
		expect(issues.some((i) => i.message.includes("does not fit any"))).toBe(
			true,
		);
	});
});

describe("validateCatalogInputs", () => {
	test("reports schema errors for malformed data without throwing", () => {
		const { report } = validateCatalogInputs(
			[
				{
					file: "bad.json",
					data: { packId: "b", version: "1", units: [{ id: "x" }] },
				},
			],
			{ file: "rules.json", data: rules() },
		);
		expect(report.ok).toBe(false);
		expect(report.schemaErrors.length).toBeGreaterThan(0);
	});

	test("accepts well-formed inputs", () => {
		const { report } = validateCatalogInputs(
			[{ file: "ok.json", data: pack([unit({ id: "ok" })]) }],
			{ file: "rules.json", data: rules() },
		);
		expect(report.ok).toBe(true);
	});
});
