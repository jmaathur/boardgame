import { z } from "zod";

/**
 * Catalog schema — the single source of truth for all sim DATA.
 *
 * ContentPacks (`units[]`, `statuses[]`, `zones[]`), commanders, and match
 * rules are authored as JSON, validated here by zod, and built into one
 * deterministic `catalog.json` (see core/catalog/scripts/build.ts) whose exact
 * bytes are read identically by the Bun server, the .NET server, and Unity.
 *
 * Codegen (core/types/scripts/generate-csharp.ts, M2) emits C# TYPE
 * definitions from these shapes — never data — preserving the over-the-wire
 * balance-patch story.
 *
 * Distances are in TILES (1 tile = 1 world unit); Mechabellum meter values are
 * transcribed ÷6, calibrated to the 48-deep board. Times are in SECONDS in
 * authored data and converted to integer ticks (20 Hz) at sim load.
 *
 * `schemaVersion` (hand-bumped) gates shape changes; `catalogVersion` (the
 * content hash) identifies data. Bump SCHEMA_VERSION whenever these shapes
 * change in a way the generated C# DTOs must track.
 */
export const SCHEMA_VERSION = 1;

// ---------------------------------------------------------------------------
// Primitives
// ---------------------------------------------------------------------------

/** kebab-case identifier used for units, statuses, zones, techs, weapons, etc. */
export const idSchema = z
	.string()
	.min(1)
	.max(64)
	.regex(
		/^[a-z][a-zA-Z0-9]*$/,
		"id must be camelCase starting with a lowercase letter",
	);

/**
 * A numeric value that may scale with squad level.
 * `5` is shorthand for `{ base: 5, perLevel: 0 }`; the object form scales as
 * `base + perLevel * (level - 1)`.
 */
export const scaledValueSchema = z.union([
	z.number(),
	z
		.object({
			base: z.number(),
			perLevel: z.number(),
		})
		.strict(),
]);
export type ScaledValue = z.infer<typeof scaledValueSchema>;

/** A stat that mods (statuses, techs, commander abilities) can adjust. */
export const modStatSchema = z.enum([
	"hp",
	"damage",
	"speed",
	"range",
	"attackInterval",
	"damageTaken",
	"shield",
]);
export type ModStat = z.infer<typeof modStatSchema>;

/**
 * One stat modifier. Buffs are additive (`addPct` summed), debuffs
 * multiplicative (`mulPct` multiplied); `add` is a flat offset. Every runtime
 * mod is source-tagged (innate|tech|status) by the sim so EMP tech-disable
 * works — the source is implied by where the StatMod lives, not stored here.
 */
export const statModSchema = z
	.object({
		stat: modStatSchema,
		add: z.number().optional(),
		addPct: z.number().optional(),
		mulPct: z.number().optional(),
	})
	.strict();
export type StatMod = z.infer<typeof statModSchema>;

export const domainSchema = z.enum(["ground", "air", "building"]);
export type Domain = z.infer<typeof domainSchema>;

export const targetDomainSchema = z.enum(["ground", "air"]);
export type TargetDomain = z.infer<typeof targetDomainSchema>;

// ---------------------------------------------------------------------------
// Effects — the extension surface, discriminated on `kind`.
// v1 set only; v1.5 (chain/execute/pctHpDamage/groundZone/...) and the
// `behavior` escape hatch are added when their milestone lands.
// ---------------------------------------------------------------------------

export const falloffSchema = z.enum(["none", "linear"]);

const damageEffect = z
	.object({
		kind: z.literal("damage"),
		amount: scaledValueSchema,
	})
	.strict();

const areaDamageEffect = z
	.object({
		kind: z.literal("areaDamage"),
		amount: scaledValueSchema,
		radius: z.number().positive(),
		falloff: falloffSchema.default("none"),
	})
	.strict();

const applyStatusEffect = z
	.object({
		kind: z.literal("applyStatus"),
		statusId: idSchema,
		durationS: z.number().nonnegative().optional(),
	})
	.strict();

const healEffect = z
	.object({
		kind: z.literal("heal"),
		amount: scaledValueSchema,
	})
	.strict();

const grantShieldEffect = z
	.object({
		kind: z.literal("grantShield"),
		amount: scaledValueSchema,
	})
	.strict();

const spawnUnitsEffect = z
	.object({
		kind: z.literal("spawnUnits"),
		unitId: idSchema,
		count: z.number().int().positive(),
		level: z.union([z.number().int().positive(), z.literal("inherit")]),
		placement: z.enum(["aroundSelf", "atSelf"]).default("aroundSelf"),
	})
	.strict();

const modifySelfEffect = z
	.object({
		kind: z.literal("modifySelf"),
		mods: z.array(statModSchema).min(1),
		durationS: z.number().nonnegative().optional(),
	})
	.strict();

export const effectSchema = z.discriminatedUnion("kind", [
	damageEffect,
	areaDamageEffect,
	applyStatusEffect,
	healEffect,
	grantShieldEffect,
	spawnUnitsEffect,
	modifySelfEffect,
]);
export type Effect = z.infer<typeof effectSchema>;

// ---------------------------------------------------------------------------
// Triggers — discriminated on `kind`.
// ---------------------------------------------------------------------------

export const filterSchema = z
	.object({
		side: z.enum(["ally", "enemy", "any"]).default("any"),
		domain: z.enum(["ground", "air", "building", "any"]).default("any"),
	})
	.strict();
export type Filter = z.infer<typeof filterSchema>;

const onSpawnTrigger = z.object({ kind: z.literal("onSpawn") }).strict();
const onDeathTrigger = z.object({ kind: z.literal("onDeath") }).strict();
const onKillTrigger = z.object({ kind: z.literal("onKill") }).strict();
const onDamagedTrigger = z.object({ kind: z.literal("onDamaged") }).strict();

const onHpBelowTrigger = z
	.object({
		kind: z.literal("onHpBelow"),
		pct: z.number().min(0).max(100),
		once: z.boolean().default(true),
	})
	.strict();

const periodicTrigger = z
	.object({
		kind: z.literal("periodic"),
		intervalS: z.number().positive(),
		startDelayS: z.number().nonnegative().default(0),
		charges: z.number().int().positive().optional(),
	})
	.strict();

const auraTrigger = z
	.object({
		kind: z.literal("aura"),
		radius: z.number().positive(),
		refreshS: z.number().positive(),
		filter: filterSchema,
	})
	.strict();

export const triggerSchema = z.discriminatedUnion("kind", [
	onSpawnTrigger,
	onDeathTrigger,
	onKillTrigger,
	onDamagedTrigger,
	onHpBelowTrigger,
	periodicTrigger,
	auraTrigger,
]);
export type Trigger = z.infer<typeof triggerSchema>;

// ---------------------------------------------------------------------------
// Abilities
// ---------------------------------------------------------------------------

export const abilitySchema = z
	.object({
		id: idSchema,
		trigger: triggerSchema,
		area: z
			.object({
				radius: z.number().positive(),
				filter: filterSchema,
			})
			.strict()
			.optional(),
		effects: z.array(effectSchema).min(1),
	})
	.strict();
export type Ability = z.infer<typeof abilitySchema>;

// ---------------------------------------------------------------------------
// Weapons
// ---------------------------------------------------------------------------

const instantFire = z.object({ mode: z.literal("instant") }).strict();

const volleyFire = z
	.object({
		mode: z.literal("volley"),
		count: z.number().int().positive(),
		spacingS: z.number().nonnegative(),
		spread: z.number().nonnegative().default(0),
	})
	.strict();

const beamFire = z
	.object({
		mode: z.literal("beam"),
		tickIntervalS: z.number().positive(),
		ramp: z
			.object({
				addPctPerTick: z.number(),
				maxPct: z.number(),
				resetOnTargetSwitch: z.boolean().default(true),
			})
			.strict()
			.optional(),
	})
	.strict();

export const fireModeSchema = z.discriminatedUnion("mode", [
	instantFire,
	volleyFire,
	beamFire,
]);
export type FireMode = z.infer<typeof fireModeSchema>;

export const projectileSchema = z
	.object({
		speed: z.number().positive(),
		arcing: z.boolean().default(false),
		/** hp > 0 = interceptable (machinery lands later). */
		hp: z.number().nonnegative().default(0),
	})
	.strict();
export type Projectile = z.infer<typeof projectileSchema>;

export const weaponSchema = z
	.object({
		/** REQUIRED: manifests, tech patches, and battle events join on it. */
		id: idSchema,
		targets: z.array(targetDomainSchema).min(1),
		range: z.number().positive(),
		minRange: z.number().nonnegative().default(0),
		interval: z.number().nonnegative(),
		/** Sugar: lowered into a leading damage/areaDamage onImpact effect. */
		damage: scaledValueSchema.optional(),
		splashRadius: z.number().nonnegative().default(0),
		barrels: z
			.object({
				count: z.number().int().positive(),
				independentTargets: z.boolean(),
			})
			.strict()
			.optional(),
		fire: fireModeSchema,
		projectile: projectileSchema.optional(),
		onImpact: z.array(effectSchema).default([]),
		onBeamTick: z.array(effectSchema).default([]),
	})
	.strict();
export type Weapon = z.infer<typeof weaponSchema>;

// ---------------------------------------------------------------------------
// Techs — per unit TYPE per player; apply to all current & future squads of
// that type. Each purchase escalates the type's other tech prices.
// ---------------------------------------------------------------------------

const statModTech = z
	.object({
		kind: z.literal("statMod"),
		mods: z.array(statModSchema).min(1),
	})
	.strict();

const grantAbilityTech = z
	.object({
		kind: z.literal("grantAbility"),
		ability: abilitySchema,
	})
	.strict();

const modifyWeaponTech = z
	.object({
		kind: z.literal("modifyWeapon"),
		weaponId: idSchema,
		patch: z
			.object({
				range: z.number().positive().optional(),
				minRange: z.number().nonnegative().optional(),
				interval: z.number().nonnegative().optional(),
				damageAddPct: z.number().optional(),
				splashRadius: z.number().nonnegative().optional(),
				addTargets: z.array(targetDomainSchema).optional(),
			})
			.strict(),
	})
	.strict();

const grantImmunityTech = z
	.object({
		kind: z.literal("grantImmunity"),
		statusTag: z.string().min(1),
	})
	.strict();

export const techEffectSchema = z.discriminatedUnion("kind", [
	statModTech,
	grantAbilityTech,
	modifyWeaponTech,
	grantImmunityTech,
]);
export type TechEffect = z.infer<typeof techEffectSchema>;

export const techSchema = z
	.object({
		id: idSchema,
		name: z.string().min(1),
		cost: z.number().int().nonnegative(),
		effects: z.array(techEffectSchema).min(1),
	})
	.strict();
export type Tech = z.infer<typeof techSchema>;

// ---------------------------------------------------------------------------
// Units
// ---------------------------------------------------------------------------

/** Center-relative member offset within the footprint. */
export const formationOffsetSchema = z
	.object({
		x: z.number(),
		z: z.number(),
	})
	.strict();
export type FormationOffset = z.infer<typeof formationOffsetSchema>;

export const unitDefSchema = z
	.object({
		id: idSchema,
		name: z.string().min(1),
		description: z.string().default(""),
		tier: z.number().int().min(1).max(6),
		cost: z
			.object({
				deployCost: z.number().int().nonnegative(),
				unlockCost: z.number().int().nonnegative(),
			})
			.strict(),
		placement: z
			.object({
				footprint: z
					.object({
						w: z.number().int().positive(),
						h: z.number().int().positive(),
					})
					.strict(),
				domain: domainSchema,
			})
			.strict(),
		squad: z
			.object({
				count: z.number().int().positive(),
				xpToLevel: z.number().int().positive(),
				formation: z.array(formationOffsetSchema).min(1),
			})
			.strict(),
		member: z
			.object({
				hp: z.number().positive(),
				speed: z.number().nonnegative(),
				flatBlock: z.number().nonnegative().default(0),
				weapons: z.array(weaponSchema).default([]),
				abilities: z.array(abilitySchema).default([]),
			})
			.strict(),
		techs: z.array(techSchema).default([]),
	})
	.strict();
export type UnitDef = z.infer<typeof unitDefSchema>;

// ---------------------------------------------------------------------------
// Statuses & Zones
// ---------------------------------------------------------------------------

export const statusSchema = z
	.object({
		id: idSchema,
		mods: z.array(statModSchema).default([]),
		dot: z
			.object({
				amountPerSecond: z.number().positive(),
			})
			.strict()
			.optional(),
		flags: z
			.object({
				techsDisabled: z.boolean().default(false),
				untargetable: z.boolean().default(false),
				stunned: z.boolean().default(false),
			})
			.strict()
			.default({ techsDisabled: false, untargetable: false, stunned: false }),
		tags: z.array(z.string().min(1)).default([]),
	})
	.strict();
export type Status = z.infer<typeof statusSchema>;

export const zoneSchema = z
	.object({
		id: idSchema,
		radius: z.number().positive(),
		reapplyIntervalS: z.number().positive(),
		statusId: idSchema,
		tags: z.array(z.string().min(1)).default([]),
	})
	.strict();
export type Zone = z.infer<typeof zoneSchema>;

// ---------------------------------------------------------------------------
// ContentPack
// ---------------------------------------------------------------------------

export const contentPackSchema = z
	.object({
		packId: idSchema,
		version: z.string().min(1),
		units: z.array(unitDefSchema).default([]),
		statuses: z.array(statusSchema).default([]),
		zones: z.array(zoneSchema).default([]),
	})
	.strict();
export type ContentPack = z.infer<typeof contentPackSchema>;

// ---------------------------------------------------------------------------
// Commanders
// ---------------------------------------------------------------------------

export const orientationSchema = z.enum(["north", "east", "south", "west"]);
export type Orientation = z.infer<typeof orientationSchema>;

/** Passive commander ability in v1: stat mods and/or economy mods. */
const commanderStatMod = z
	.object({
		kind: z.literal("statMod"),
		/** Empty filter targets all of the commander's units. */
		unitFilter: z.array(idSchema).default([]),
		mods: z.array(statModSchema).min(1),
	})
	.strict();

const commanderEconomyMod = z
	.object({
		kind: z.literal("economyMod"),
		incomePerRoundAdd: z.number().int().default(0),
		deploySlotsAdd: z.number().int().default(0),
		unlockSlotsAdd: z.number().int().default(0),
		startingIncomeAdd: z.number().int().default(0),
	})
	.strict();

export const commanderAbilitySchema = z.discriminatedUnion("kind", [
	commanderStatMod,
	commanderEconomyMod,
]);
export type CommanderAbility = z.infer<typeof commanderAbilitySchema>;

export const placedUnitSchema = z
	.object({
		unitId: idSchema,
		anchor: z
			.object({
				row: z.number().int().nonnegative(),
				col: z.number().int().nonnegative(),
			})
			.strict(),
		orientation: orientationSchema.default("north"),
	})
	.strict();
export type PlacedUnit = z.infer<typeof placedUnitSchema>;

export const commanderSchema = z
	.object({
		id: idSchema,
		name: z.string().min(1),
		description: z.string().default(""),
		/** Commander HP IS the player HP pool. */
		hp: z.number().int().positive(),
		startingUnits: z.array(placedUnitSchema).default([]),
		ability: z.array(commanderAbilitySchema).default([]),
	})
	.strict();
export type Commander = z.infer<typeof commanderSchema>;

// ---------------------------------------------------------------------------
// Match rules — board, zones, economy, timers, leveling, tech escalation,
// starting buildings, commanders. The single source of truth for the board
// size (resolves the historical 72x60-vs-32x32 drift with data).
// ---------------------------------------------------------------------------

export const deployZoneSchema = z
	.object({
		seat: z.number().int().min(0).max(1),
		rect: z
			.object({
				row: z.number().int().nonnegative(),
				col: z.number().int().nonnegative(),
				w: z.number().int().positive(),
				h: z.number().int().positive(),
			})
			.strict(),
		availableFromRound: z.number().int().positive().default(1),
	})
	.strict();
export type DeployZone = z.infer<typeof deployZoneSchema>;

/** A starting building placement materialized for one seat every match. */
export const startingBuildingSchema = z
	.object({
		seat: z.number().int().min(0).max(1),
		unitId: idSchema,
		anchor: z
			.object({
				row: z.number().int().nonnegative(),
				col: z.number().int().nonnegative(),
			})
			.strict(),
		orientation: orientationSchema.default("north"),
	})
	.strict();
export type StartingBuilding = z.infer<typeof startingBuildingSchema>;

export const matchRulesSchema = z
	.object({
		board: z
			.object({
				w: z.number().int().positive(),
				h: z.number().int().positive(),
			})
			.strict(),
		deployZones: z.array(deployZoneSchema).min(1),
		income: z
			.object({
				perRoundIncrement: z.number().int().nonnegative(),
				startingIncome: z.number().int().nonnegative().default(0),
				carryOver: z.boolean().default(true),
			})
			.strict(),
		deploysPerRound: z.number().int().positive(),
		unlocksPerRound: z.number().int().nonnegative(),
		timers: z
			.object({
				deploySeconds: z.number().int().positive(),
				battleSeconds: z.number().int().positive(),
				resultsHoldSeconds: z.number().int().nonnegative().default(5),
				commanderPickSeconds: z.number().int().positive().default(30),
			})
			.strict(),
		leveling: z
			.object({
				hpFactorPerLevel: z.number().nonnegative(),
				atkFactorPerLevel: z.number().nonnegative(),
				upgradeCostFraction: z.number().positive(),
			})
			.strict(),
		techPriceEscalation: z.number().int().nonnegative(),
		commandersOffered: z.number().int().positive().default(3),
		startingBuildings: z.array(startingBuildingSchema).default([]),
		commanders: z.array(commanderSchema).min(1),
	})
	.strict();
export type MatchRules = z.infer<typeof matchRulesSchema>;

// ---------------------------------------------------------------------------
// The built catalog — the deterministic bytes shipped over the wire and read
// by every runtime. Assembled by core/catalog/scripts/build.ts.
// ---------------------------------------------------------------------------

export const catalogSchema = z
	.object({
		schemaVersion: z.literal(SCHEMA_VERSION),
		packs: z.array(contentPackSchema).min(1),
		matchRules: matchRulesSchema,
	})
	.strict();
export type Catalog = z.infer<typeof catalogSchema>;
