#!/usr/bin/env bun
/**
 * One-shot authoring generator for the base content pack + match rules.
 *
 * The emitted JSON (data/packs/base.json, data/match-rules.json) is the source
 * of truth going forward — editable via Forge or a text editor. This script
 * only exists so the initial ÷6 Mechabellum transcription (and the tedious
 * 24-member formations) can be expressed cleanly and re-emitted deterministically
 * if we ever want to regenerate from scratch. It is NOT part of the build.
 *
 * Medieval identities mapped onto Mechabellum archetypes (the tuning naming
 * map): footman = chaff swarm, archer = ranged squad, whelp = cheap flyer,
 * holyKnight = bruiser, ballista = artillery, arbalest = sniper,
 * gargoyle = anti-air, warBanner = aura support, barracks/cathedral = buildings.
 * Stats are Mechabellum meters/HP/damage ÷6 (distances & radii), calibrated to
 * the 48-deep board. First-draft numbers; the M4 balance harness is the tuner.
 */
import { writeFileSync } from "node:fs";
import { MATCH_RULES_FILE, PACKS_DIR } from "../src/index";
import { join } from "node:path";

type Off = { x: number; z: number };

/** Center-relative grid formation that fits within a wxh footprint. */
function grid(count: number, cols: number, w: number, h: number): Off[] {
	const rows = Math.ceil(count / cols);
	const usableW = w - 0.4;
	const usableH = h - 0.4;
	const dx = cols > 1 ? usableW / (cols - 1) : 0;
	const dz = rows > 1 ? usableH / (rows - 1) : 0;
	const out: Off[] = [];
	let n = 0;
	for (let r = 0; r < rows && n < count; r++) {
		const inRow = Math.min(cols, count - n);
		const rowW = (inRow - 1) * dx;
		const startX = -rowW / 2;
		for (let c = 0; c < inRow; c++) {
			const x = startX + c * dx;
			const z = rows > 1 ? -usableH / 2 + r * dz : 0;
			out.push({ x: +x.toFixed(2), z: +z.toFixed(2) });
			n++;
		}
	}
	return out;
}

const base = {
	packId: "base",
	version: "1.0.0",
	units: [
		// Chaff swarm: 24 cheap melee bodies, per-member HP.
		// (Mechabellum Crawler ≈ hp 260, dmg 86, spd 16m→2.7, rng 6m→1)
		{
			id: "footman",
			name: "Footman",
			description:
				"A swarm of cheap melee levy. Dies in droves; wins by numbers.",
			tier: 1,
			cost: { deployCost: 100, unlockCost: 0 },
			placement: { footprint: { w: 5, h: 3 }, domain: "ground" },
			squad: { count: 24, xpToLevel: 450, formation: grid(24, 6, 5, 3) },
			member: {
				hp: 260,
				speed: 2.7,
				weapons: [
					{
						id: "sword",
						targets: ["ground"],
						range: 1,
						interval: 0.9,
						damage: 86,
						fire: { mode: "instant" },
					},
				],
			},
		},
		// Ranged squad: transcribed from the shipped ArcherDetails (7 archers).
		// (Mechabellum Marksman-ish ≈ hp 520, dmg 120, rng 60m→10)
		{
			id: "archer",
			name: "Archer",
			description: "A ranged file of longbowmen. Fragile but reaches deep.",
			tier: 1,
			cost: { deployCost: 100, unlockCost: 0 },
			placement: { footprint: { w: 4, h: 2 }, domain: "ground" },
			squad: { count: 7, xpToLevel: 500, formation: grid(7, 4, 4, 2) },
			member: {
				hp: 520,
				speed: 1.7,
				weapons: [
					{
						id: "longbow",
						targets: ["ground", "air"],
						range: 10,
						interval: 1.2,
						damage: 120,
						projectile: { speed: 6, arcing: false, hp: 0 },
						fire: { mode: "instant" },
					},
				],
				abilities: [],
			},
			techs: [
				{
					id: "archerFireArrows",
					name: "Fire Arrows",
					cost: 100,
					effects: [
						{ kind: "statMod", mods: [{ stat: "damage", addPct: 25 }] },
					],
				},
				{
					id: "archerLongdraw",
					name: "Longdraw",
					cost: 150,
					effects: [
						{
							kind: "modifyWeapon",
							weaponId: "longbow",
							patch: { range: 13 },
						},
					],
				},
			],
		},
		// Cheap flyer: 8 whelps. Air domain; only air-capable weapons hit them.
		// (Mechabellum Wasp ≈ hp 130, dmg 40, spd 30m→5)
		{
			id: "whelp",
			name: "Whelp",
			description: "A flight of dragon whelps. Cheap air pressure.",
			tier: 1,
			cost: { deployCost: 100, unlockCost: 0 },
			placement: { footprint: { w: 4, h: 3 }, domain: "air" },
			squad: { count: 8, xpToLevel: 450, formation: grid(8, 4, 4, 3) },
			member: {
				hp: 130,
				speed: 5,
				weapons: [
					{
						id: "firebreath",
						targets: ["ground"],
						range: 3,
						interval: 1,
						damage: 40,
						fire: { mode: "instant" },
					},
				],
			},
		},
		// Bruiser: 4 holy knights, high HP, flat block.
		// (Mechabellum Fang-ish bruiser ≈ hp 2400, dmg 260)
		{
			id: "holyKnight",
			name: "Holy Knight",
			description: "Armored knights. Slow, durable, and hit hard up close.",
			tier: 2,
			cost: { deployCost: 200, unlockCost: 50 },
			placement: { footprint: { w: 3, h: 2 }, domain: "ground" },
			squad: { count: 4, xpToLevel: 650, formation: grid(4, 2, 3, 2) },
			member: {
				hp: 2400,
				speed: 1.5,
				flatBlock: 20,
				weapons: [
					{
						id: "greatsword",
						targets: ["ground"],
						range: 1.5,
						interval: 1.3,
						damage: 260,
						fire: { mode: "instant" },
					},
				],
			},
		},
		// Artillery volley: 3 ballistae, arcing splash, min-range.
		// (Mechabellum Stormcaller ≈ hp 1330, splash 610, rng 30, minRng 7)
		{
			id: "ballista",
			name: "Ballista",
			description:
				"Siege artillery. Arcing bolts that burst on impact; blind up close.",
			tier: 2,
			cost: { deployCost: 200, unlockCost: 50 },
			placement: { footprint: { w: 4, h: 2 }, domain: "ground" },
			squad: { count: 3, xpToLevel: 700, formation: grid(3, 3, 4, 2) },
			member: {
				hp: 1330,
				speed: 1.1,
				weapons: [
					{
						id: "boltThrower",
						targets: ["ground"],
						range: 22,
						minRange: 6,
						interval: 5,
						fire: { mode: "volley", count: 3, spacingS: 0.2, spread: 1.2 },
						projectile: { speed: 4.7, arcing: true, hp: 0 },
						onImpact: [
							{
								kind: "areaDamage",
								amount: 610,
								radius: 1.3,
								falloff: "linear",
							},
						],
					},
				],
			},
		},
		// Sniper: 2 arbalests, long single-target, slow reload.
		// (Mechabellum Marksman/sniper ≈ hp 900, dmg 1800, rng 50m→over-range sniper)
		{
			id: "arbalest",
			name: "Arbalest",
			description:
				"Heavy crossbowmen. Devastating single shots at extreme range.",
			tier: 3,
			cost: { deployCost: 300, unlockCost: 100 },
			placement: { footprint: { w: 3, h: 2 }, domain: "ground" },
			squad: { count: 2, xpToLevel: 900, formation: grid(2, 2, 3, 2) },
			member: {
				hp: 900,
				speed: 1,
				weapons: [
					{
						id: "heavyBolt",
						targets: ["ground", "air"],
						range: 20,
						minRange: 3,
						interval: 3,
						damage: 1800,
						projectile: { speed: 12, arcing: false, hp: 0 },
						fire: { mode: "instant" },
					},
				],
			},
		},
		// Anti-air: 6 gargoyles that only target air.
		// (Mechabellum Phoenix/anti-air ≈ hp 700, dmg 150 vs air)
		{
			id: "gargoyle",
			name: "Gargoyle",
			description:
				"Stone sentinels that spit at flyers. Cannot strike the ground.",
			tier: 2,
			cost: { deployCost: 200, unlockCost: 50 },
			placement: { footprint: { w: 4, h: 2 }, domain: "ground" },
			squad: { count: 6, xpToLevel: 650, formation: grid(6, 3, 4, 2) },
			member: {
				hp: 700,
				speed: 1.4,
				weapons: [
					{
						id: "aetherSpit",
						targets: ["air"],
						range: 12,
						interval: 0.8,
						damage: 150,
						projectile: { speed: 9, arcing: false, hp: 0 },
						fire: { mode: "instant" },
					},
				],
			},
		},
		// Aura support: 1 war banner buffing nearby allies while alive.
		{
			id: "warBanner",
			name: "War Banner",
			description: "A rallying standard. Boosts the damage of nearby allies.",
			tier: 2,
			cost: { deployCost: 200, unlockCost: 50 },
			placement: { footprint: { w: 2, h: 2 }, domain: "ground" },
			squad: { count: 1, xpToLevel: 600, formation: [{ x: 0, z: 0 }] },
			member: {
				hp: 4000,
				speed: 1.3,
				abilities: [
					{
						id: "rallyAura",
						trigger: {
							kind: "aura",
							radius: 5,
							refreshS: 0.2,
							filter: { side: "ally", domain: "any" },
						},
						effects: [
							{ kind: "applyStatus", statusId: "rallied", durationS: 0.4 },
						],
					},
				],
			},
		},
		// Buildings — fixed starting placements, no deploy cost, they don't move.
		{
			id: "barracks",
			name: "Barracks",
			description: "A muster hall. A durable anchor on the back line.",
			tier: 1,
			cost: { deployCost: 0, unlockCost: 0 },
			placement: { footprint: { w: 3, h: 3 }, domain: "building" },
			squad: { count: 1, xpToLevel: 1000, formation: [{ x: 0, z: 0 }] },
			member: { hp: 6000, speed: 0 },
		},
		{
			id: "cathedral",
			name: "Cathedral",
			description: "The command sanctuary. The heart of the army's line.",
			tier: 1,
			cost: { deployCost: 0, unlockCost: 0 },
			placement: { footprint: { w: 4, h: 4 }, domain: "building" },
			squad: { count: 1, xpToLevel: 1000, formation: [{ x: 0, z: 0 }] },
			member: { hp: 9000, speed: 0 },
		},
	],
	statuses: [
		{
			id: "rallied",
			mods: [{ stat: "damage", addPct: 15 }],
			tags: ["buff"],
		},
	],
	zones: [],
};

// --- match rules: 32x48 board, halves at col 24, no no-man's-land ---
const matchRules = {
	board: { w: 32, h: 48 },
	deployZones: [
		{ seat: 0, rect: { row: 0, col: 0, w: 32, h: 24 }, availableFromRound: 1 },
		{ seat: 1, rect: { row: 0, col: 24, w: 32, h: 24 }, availableFromRound: 1 },
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
	commandersOffered: 3,
	// cathedral + barracks per side, on each seat's back line.
	startingBuildings: [
		{ seat: 0, unitId: "cathedral", anchor: { row: 14, col: 0 } },
		{ seat: 0, unitId: "barracks", anchor: { row: 6, col: 0 } },
		{ seat: 1, unitId: "cathedral", anchor: { row: 14, col: 44 } },
		{ seat: 1, unitId: "barracks", anchor: { row: 6, col: 44 } },
	],
	commanders: [
		{
			id: "warlord",
			name: "The Warlord",
			description:
				"An extra deploy each round, but a smaller life pool. Tempo over safety.",
			hp: 4000,
			startingUnits: [{ unitId: "footman", anchor: { row: 12, col: 4 } }],
			ability: [{ kind: "economyMod", deploySlotsAdd: 1 }],
		},
		{
			id: "steward",
			name: "The Steward",
			description: "Richer coffers every round. Snowballs an economic lead.",
			hp: 5000,
			startingUnits: [{ unitId: "archer", anchor: { row: 12, col: 4 } }],
			ability: [{ kind: "economyMod", incomePerRoundAdd: 50 }],
		},
		{
			id: "zealot",
			name: "The Zealot",
			description:
				"Starts with a knightly escort and a deeper life pool. Frontline resilience.",
			hp: 6000,
			startingUnits: [{ unitId: "holyKnight", anchor: { row: 12, col: 4 } }],
			ability: [
				{
					kind: "statMod",
					unitFilter: ["holyKnight"],
					mods: [{ stat: "hp", addPct: 15 }],
				},
			],
		},
	],
};

writeFileSync(
	join(PACKS_DIR, "base.json"),
	`${JSON.stringify(base, null, "\t")}\n`,
);
writeFileSync(MATCH_RULES_FILE, `${JSON.stringify(matchRules, null, "\t")}\n`);
console.log("wrote data/packs/base.json and data/match-rules.json");
