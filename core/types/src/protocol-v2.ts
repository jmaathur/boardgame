import { z } from "zod";
import { orientationSchema } from "./catalog-schema";

/**
 * Wire protocol V2 — the Mechabellum-style match loop (design doc §8).
 *
 * Replaces the V1 placement demo (protocol.ts) for real matches. JSON over
 * WebSocket text frames, discriminated by `type`. The C# BattleServer (M5)
 * ports the MatchRoom reducer against this exact contract; the only difference
 * the client sees at cutover is that `battleLog` starts appearing.
 *
 * Coordinates: `row` = world-X (0..board.w-1, the wide/lateral axis),
 * `col` = world-Z (0..board.h-1, the deep axis). Anchor = footprint min corner.
 */

export const PROTOCOL_VERSION = 2;

export const seatSchema = z.union([z.literal(0), z.literal(1)]);
export type Seat = z.infer<typeof seatSchema>;

// ---------------------------------------------------------------------------
// Blueprint & state on the wire (member layout is a pure function of the
// catalog; the wire never enumerates members).
// ---------------------------------------------------------------------------

/** A purchased placement in a player's army blueprint. */
export const squadCardSchema = z
	.object({
		cardId: z.string(),
		unitId: z.string(),
		anchor: z.object({ row: z.number().int(), col: z.number().int() }).strict(),
		orientation: orientationSchema,
		level: z.number().int().min(1),
		xp: z.number().nonnegative(),
		/** Round the card was purchased (for this-round free sell/revert). */
		purchasedRound: z.number().int().nonnegative(),
		/** Total coin invested (deploy + level-ups), for survivor valuation. */
		invested: z.number().int().nonnegative(),
	})
	.strict();
export type SquadCard = z.infer<typeof squadCardSchema>;

/** Per-unit-type tech/unlock progression for one seat. */
export const seatTechStateSchema = z
	.object({
		unlockedUnits: z.array(z.string()),
		purchasedTechs: z.array(z.string()),
		/** unitId → next tech price for that type (escalates on each purchase). */
		techPriceByUnit: z.record(z.string(), z.number().int()),
	})
	.strict();
export type SeatTechState = z.infer<typeof seatTechStateSchema>;

/** The full server-authoritative view of ONE seat (its own private state). */
export const seatViewSchema = z
	.object({
		seat: seatSchema,
		playerName: z.string(),
		connected: z.boolean(),
		commanderId: z.string().nullable(),
		hp: z.number().int(),
		coin: z.number().int(),
		deploysRemaining: z.number().int(),
		unlocksRemaining: z.number().int(),
		ready: z.boolean(),
		cards: z.array(squadCardSchema),
		tech: seatTechStateSchema,
	})
	.strict();
export type SeatView = z.infer<typeof seatViewSchema>;

/** The opponent as last revealed (end of previous round) — no hidden info. */
export const opponentViewSchema = z
	.object({
		seat: seatSchema,
		playerName: z.string(),
		connected: z.boolean(),
		commanderId: z.string().nullable(),
		hp: z.number().int(),
		/** Revealed army from the last plan-lock (empty before round 1 battle). */
		cards: z.array(squadCardSchema),
	})
	.strict();
export type OpponentView = z.infer<typeof opponentViewSchema>;

export const phaseSchema = z.enum([
	"lobby",
	"commanderPick",
	"planning",
	"battle",
	"results",
	"matchEnded",
]);
export type Phase = z.infer<typeof phaseSchema>;

/** matchConfig is derived from catalog MatchRules; the essentials the client needs. */
export const matchConfigSchema = z
	.object({
		board: z.object({ w: z.number().int(), h: z.number().int() }).strict(),
		deploysPerRound: z.number().int(),
		unlocksPerRound: z.number().int(),
		incomePerRoundIncrement: z.number().int(),
		deploySeconds: z.number().int(),
		battleSeconds: z.number().int(),
		commanderPickSeconds: z.number().int(),
		commandersOffered: z.number().int(),
	})
	.strict();
export type MatchConfig = z.infer<typeof matchConfigSchema>;

/** A complete match snapshot for one seat (own private view + opponent public). */
export const matchSnapshotSchema = z
	.object({
		phase: phaseSchema,
		round: z.number().int(),
		/** Server epoch ms when the current phase's deadline fires (0 = none). */
		phaseDeadline: z.number().int(),
		commanderOffers: z.array(z.string()),
		own: seatViewSchema,
		opponent: opponentViewSchema.nullable(),
	})
	.strict();
export type MatchSnapshot = z.infer<typeof matchSnapshotSchema>;

// ---------------------------------------------------------------------------
// Client → server
// ---------------------------------------------------------------------------

export const joinV2Schema = z
	.object({
		type: z.literal("join"),
		roomId: z.string().min(1).max(64),
		playerName: z.string().min(1).max(32),
		protocolVersion: z.number().int(),
		resumeToken: z.string().optional(),
		catalogHash: z.string().optional(),
	})
	.strict();

export const pickCommanderSchema = z
	.object({
		type: z.literal("pickCommander"),
		cmdId: z.string(),
		commanderId: z.string(),
	})
	.strict();

const anchor = z
	.object({ row: z.number().int(), col: z.number().int() })
	.strict();

export const buySquadSchema = z
	.object({
		type: z.literal("buySquad"),
		cmdId: z.string(),
		unitId: z.string(),
		anchor,
		orientation: orientationSchema.default("north"),
	})
	.strict();

export const moveSquadSchema = z
	.object({
		type: z.literal("moveSquad"),
		cmdId: z.string(),
		cardId: z.string(),
		anchor,
		orientation: orientationSchema.default("north"),
	})
	.strict();

export const sellSquadSchema = z
	.object({
		type: z.literal("sellSquad"),
		cmdId: z.string(),
		cardId: z.string(),
	})
	.strict();

export const unlockUnitSchema = z
	.object({
		type: z.literal("unlockUnit"),
		cmdId: z.string(),
		unitId: z.string(),
	})
	.strict();

export const buyTechSchema = z
	.object({
		type: z.literal("buyTech"),
		cmdId: z.string(),
		unitId: z.string(),
		techId: z.string(),
	})
	.strict();

export const buyLevelSchema = z
	.object({
		type: z.literal("buyLevel"),
		cmdId: z.string(),
		cardId: z.string(),
	})
	.strict();

export const setReadySchema = z
	.object({
		type: z.literal("setReady"),
		cmdId: z.string(),
		ready: z.boolean(),
	})
	.strict();

export const battleAckSchema = z
	.object({ type: z.literal("battleAck") })
	.strict();

export const pingV2Schema = z.object({ type: z.literal("ping") }).strict();

export const clientMessageV2Schema = z.discriminatedUnion("type", [
	joinV2Schema,
	pickCommanderSchema,
	buySquadSchema,
	moveSquadSchema,
	sellSquadSchema,
	unlockUnitSchema,
	buyTechSchema,
	buyLevelSchema,
	setReadySchema,
	battleAckSchema,
	pingV2Schema,
]);
export type ClientMessageV2 = z.infer<typeof clientMessageV2Schema>;
export type JoinV2 = z.infer<typeof joinV2Schema>;
export type PickCommander = z.infer<typeof pickCommanderSchema>;
export type BuySquad = z.infer<typeof buySquadSchema>;
export type MoveSquad = z.infer<typeof moveSquadSchema>;
export type SellSquad = z.infer<typeof sellSquadSchema>;
export type UnlockUnit = z.infer<typeof unlockUnitSchema>;
export type BuyTech = z.infer<typeof buyTechSchema>;
export type BuyLevel = z.infer<typeof buyLevelSchema>;
export type SetReady = z.infer<typeof setReadySchema>;

/** A command carrying a cmdId (everything except join/battleAck/ping). */
export type PlanningCommand =
	| PickCommander
	| BuySquad
	| MoveSquad
	| SellSquad
	| UnlockUnit
	| BuyTech
	| BuyLevel
	| SetReady;

// ---------------------------------------------------------------------------
// Server → client
// ---------------------------------------------------------------------------

export const cmdRejectCodeSchema = z.enum([
	"badMessage",
	"notJoined",
	"wrongPhase",
	"notYourTurn",
	"unknownCommander",
	"commanderAlreadyPicked",
	"unknownUnit",
	"notUnlocked",
	"insufficientFunds",
	"noDeploysLeft",
	"noUnlocksLeft",
	"alreadyUnlocked",
	"outOfBounds",
	"outsideOwnHalf",
	"tileOccupied",
	"unknownCard",
	"notThisRoundPurchase",
	"unknownTech",
	"techAlreadyOwned",
	"xpNotReady",
	"internal",
]);
export type CmdRejectCode = z.infer<typeof cmdRejectCodeSchema>;

export const welcomeV2Schema = z
	.object({
		type: z.literal("welcome"),
		seat: seatSchema,
		resumeToken: z.string(),
		catalogJson: z.string(),
		catalogHash: z.string(),
		matchConfig: matchConfigSchema,
		match: matchSnapshotSchema.nullable(),
	})
	.strict();

export const phaseMessageSchema = z
	.object({
		type: z.literal("phase"),
		match: matchSnapshotSchema,
	})
	.strict();

export const cmdAcceptedSchema = z
	.object({
		type: z.literal("cmdAccepted"),
		cmdId: z.string(),
		match: matchSnapshotSchema,
	})
	.strict();

export const cmdRejectedSchema = z
	.object({
		type: z.literal("cmdRejected"),
		cmdId: z.string(),
		code: cmdRejectCodeSchema,
		message: z.string(),
	})
	.strict();

export const revealSnapshotSchema = z
	.object({
		type: z.literal("revealSnapshot"),
		round: z.number().int(),
		armies: z.array(
			z.object({ seat: seatSchema, cards: z.array(squadCardSchema) }).strict(),
		),
	})
	.strict();

export const battleStartedSchema = z
	.object({
		type: z.literal("battleStarted"),
		round: z.number().int(),
		startAtServerMs: z.number().int(),
		/** Present once the real sim exists (M5); absent in the stub era. */
		hasBattleLog: z.boolean(),
	})
	.strict();

export const battleLogSchema = z
	.object({
		type: z.literal("battleLog"),
		round: z.number().int(),
		/** Opaque to the protocol; the sim's event log (M5). */
		log: z.unknown(),
	})
	.strict();

export const roundResultSchema = z
	.object({
		type: z.literal("roundResult"),
		round: z.number().int(),
		winnerSeat: seatSchema.nullable(),
		hpDamage: z.array(
			z.object({ seat: seatSchema, amount: z.number().int() }).strict(),
		),
		hp: z.array(z.object({ seat: seatSchema, hp: z.number().int() }).strict()),
		summary: z.string(),
	})
	.strict();

export const matchEndedSchema = z
	.object({
		type: z.literal("matchEnded"),
		winnerSeat: seatSchema.nullable(),
		finalHp: z.array(
			z.object({ seat: seatSchema, hp: z.number().int() }).strict(),
		),
	})
	.strict();

export const errorV2Schema = z
	.object({
		type: z.literal("error"),
		code: z.string(),
		message: z.string(),
	})
	.strict();

export const pongV2Schema = z.object({ type: z.literal("pong") }).strict();

export const serverMessageV2Schema = z.discriminatedUnion("type", [
	welcomeV2Schema,
	phaseMessageSchema,
	cmdAcceptedSchema,
	cmdRejectedSchema,
	revealSnapshotSchema,
	battleStartedSchema,
	battleLogSchema,
	roundResultSchema,
	matchEndedSchema,
	errorV2Schema,
	pongV2Schema,
]);
export type ServerMessageV2 = z.infer<typeof serverMessageV2Schema>;
export type WelcomeV2 = z.infer<typeof welcomeV2Schema>;
export type PhaseMessage = z.infer<typeof phaseMessageSchema>;
export type CmdAccepted = z.infer<typeof cmdAcceptedSchema>;
export type CmdRejected = z.infer<typeof cmdRejectedSchema>;
export type RevealSnapshot = z.infer<typeof revealSnapshotSchema>;
export type BattleStarted = z.infer<typeof battleStartedSchema>;
export type RoundResult = z.infer<typeof roundResultSchema>;
export type MatchEnded = z.infer<typeof matchEndedSchema>;

// ---------------------------------------------------------------------------
// Parsing helpers
// ---------------------------------------------------------------------------

export type ParseResultV2<T> =
	| { ok: true; message: T }
	| { ok: false; error: string };

function parseJson(raw: string): { ok: true; value: unknown } | { ok: false } {
	try {
		return { ok: true, value: JSON.parse(raw) };
	} catch {
		return { ok: false };
	}
}

export function parseClientMessageV2(
	raw: string,
): ParseResultV2<ClientMessageV2> {
	const json = parseJson(raw);
	if (!json.ok) return { ok: false, error: "invalid JSON" };
	const result = clientMessageV2Schema.safeParse(json.value);
	if (!result.success)
		return { ok: false, error: z.prettifyError(result.error) };
	return { ok: true, message: result.data };
}

export function parseServerMessageV2(
	raw: string,
): ParseResultV2<ServerMessageV2> {
	const json = parseJson(raw);
	if (!json.ok) return { ok: false, error: "invalid JSON" };
	const result = serverMessageV2Schema.safeParse(json.value);
	if (!result.success)
		return { ok: false, error: z.prettifyError(result.error) };
	return { ok: true, message: result.data };
}
