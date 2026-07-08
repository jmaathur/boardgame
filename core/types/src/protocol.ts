import { z } from "zod";

/**
 * Wire protocol shared by the game server (apps/game-server) and the Unity
 * game client (apps/game-client — mirrored by hand in
 * Assets/BoardGame/Scripts/Net/GameProtocol.cs; keep the two in sync).
 *
 * Transport: WebSocket text frames, one JSON-encoded message per frame.
 *
 * Board coordinates follow the Unity client's convention:
 * `row` is the world-X tile index (0..BOARD_WIDTH-1) and `col` is the
 * world-Z tile index (0..BOARD_HEIGHT-1). A tile sits at world (row, 0, col).
 */

export const BOARD_WIDTH = 72;
export const BOARD_HEIGHT = 60;

export const UNIT_TYPES = [
	"archer",
	"whelp",
	"footman",
	"holyKnight",
	"barracks",
	"cathedral",
] as const;

export type UnitType = (typeof UNIT_TYPES)[number];

const row = z
	.number()
	.int()
	.min(0)
	.max(BOARD_WIDTH - 1);
const col = z
	.number()
	.int()
	.min(0)
	.max(BOARD_HEIGHT - 1);

export function isInBounds(r: number, c: number): boolean {
	return (
		Number.isInteger(r) &&
		Number.isInteger(c) &&
		r >= 0 &&
		r < BOARD_WIDTH &&
		c >= 0 &&
		c < BOARD_HEIGHT
	);
}

// ---------------------------------------------------------------------------
// Game state
// ---------------------------------------------------------------------------

export const playerSchema = z.object({
	id: z.string(),
	name: z.string(),
});

export const unitSchema = z.object({
	id: z.string(),
	ownerId: z.string(),
	unitType: z.enum(UNIT_TYPES),
	row,
	col,
});

export const gameStateSchema = z.object({
	board: z.object({
		width: z.literal(BOARD_WIDTH),
		height: z.literal(BOARD_HEIGHT),
	}),
	players: z.array(playerSchema),
	units: z.array(unitSchema),
});

export type Player = z.infer<typeof playerSchema>;
export type Unit = z.infer<typeof unitSchema>;
export type GameState = z.infer<typeof gameStateSchema>;

// ---------------------------------------------------------------------------
// Client → server messages
// ---------------------------------------------------------------------------

export const joinMessageSchema = z.object({
	type: z.literal("join"),
	roomId: z.string().min(1).max(64),
	playerName: z.string().min(1).max(32),
});

export const placeUnitMessageSchema = z.object({
	type: z.literal("placeUnit"),
	unitType: z.enum(UNIT_TYPES),
	row,
	col,
});

export const moveUnitMessageSchema = z.object({
	type: z.literal("moveUnit"),
	unitId: z.string().min(1),
	row,
	col,
});

export const pingMessageSchema = z.object({
	type: z.literal("ping"),
});

export const clientMessageSchema = z.discriminatedUnion("type", [
	joinMessageSchema,
	placeUnitMessageSchema,
	moveUnitMessageSchema,
	pingMessageSchema,
]);

export type JoinMessage = z.infer<typeof joinMessageSchema>;
export type PlaceUnitMessage = z.infer<typeof placeUnitMessageSchema>;
export type MoveUnitMessage = z.infer<typeof moveUnitMessageSchema>;
export type PingMessage = z.infer<typeof pingMessageSchema>;
export type ClientMessage = z.infer<typeof clientMessageSchema>;

// ---------------------------------------------------------------------------
// Server → client messages
// ---------------------------------------------------------------------------

export const errorCodeSchema = z.enum([
	"badMessage",
	"notJoined",
	"alreadyJoined",
	"outOfBounds",
	"tileOccupied",
	"unknownUnit",
	"notYourUnit",
]);

export type ErrorCode = z.infer<typeof errorCodeSchema>;

export const welcomeMessageSchema = z.object({
	type: z.literal("welcome"),
	playerId: z.string(),
	roomId: z.string(),
	state: gameStateSchema,
});

export const stateMessageSchema = z.object({
	type: z.literal("state"),
	state: gameStateSchema,
});

export const errorMessageSchema = z.object({
	type: z.literal("error"),
	code: errorCodeSchema,
	message: z.string(),
});

export const pongMessageSchema = z.object({
	type: z.literal("pong"),
});

export const serverMessageSchema = z.discriminatedUnion("type", [
	welcomeMessageSchema,
	stateMessageSchema,
	errorMessageSchema,
	pongMessageSchema,
]);

export type WelcomeMessage = z.infer<typeof welcomeMessageSchema>;
export type StateMessage = z.infer<typeof stateMessageSchema>;
export type ErrorMessage = z.infer<typeof errorMessageSchema>;
export type PongMessage = z.infer<typeof pongMessageSchema>;
export type ServerMessage = z.infer<typeof serverMessageSchema>;

// ---------------------------------------------------------------------------
// Parsing helpers
// ---------------------------------------------------------------------------

export type ParseResult<T> =
	| { ok: true; message: T }
	| { ok: false; error: string };

function parseJson(raw: string): ParseResult<unknown> {
	try {
		return { ok: true, message: JSON.parse(raw) };
	} catch {
		return { ok: false, error: "invalid JSON" };
	}
}

export function parseClientMessage(raw: string): ParseResult<ClientMessage> {
	const json = parseJson(raw);
	if (!json.ok) return json;
	const result = clientMessageSchema.safeParse(json.message);
	if (!result.success) {
		return { ok: false, error: z.prettifyError(result.error) };
	}
	return { ok: true, message: result.data };
}

export function parseServerMessage(raw: string): ParseResult<ServerMessage> {
	const json = parseJson(raw);
	if (!json.ok) return json;
	const result = serverMessageSchema.safeParse(json.message);
	if (!result.success) {
		return { ok: false, error: z.prettifyError(result.error) };
	}
	return { ok: true, message: result.data };
}
