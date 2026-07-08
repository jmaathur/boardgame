import { describe, expect, test } from "bun:test";
import {
	BOARD_HEIGHT,
	BOARD_WIDTH,
	isInBounds,
	parseClientMessage,
	parseServerMessage,
} from "./protocol";

describe("parseClientMessage", () => {
	test("accepts a valid join message", () => {
		const result = parseClientMessage(
			JSON.stringify({ type: "join", roomId: "lobby", playerName: "Jeev" }),
		);
		expect(result.ok).toBe(true);
		if (result.ok) {
			expect(result.message.type).toBe("join");
		}
	});

	test("accepts a valid placeUnit message", () => {
		const result = parseClientMessage(
			JSON.stringify({ type: "placeUnit", unitType: "archer", row: 0, col: 0 }),
		);
		expect(result.ok).toBe(true);
	});

	test("rejects invalid JSON", () => {
		const result = parseClientMessage("not json{");
		expect(result.ok).toBe(false);
	});

	test("rejects unknown message types", () => {
		const result = parseClientMessage(JSON.stringify({ type: "explode" }));
		expect(result.ok).toBe(false);
	});

	test("accepts any non-empty unit-type string (catalog-validated at runtime)", () => {
		// As of M1 the wire no longer pins unitType to a fixed enum — validity is
		// checked against the loaded catalog by the server, not the schema.
		const ok = parseClientMessage(
			JSON.stringify({
				type: "placeUnit",
				unitType: "ballista",
				row: 0,
				col: 0,
			}),
		);
		expect(ok.ok).toBe(true);
	});

	test("still rejects an empty unit-type string", () => {
		const result = parseClientMessage(
			JSON.stringify({ type: "placeUnit", unitType: "", row: 0, col: 0 }),
		);
		expect(result.ok).toBe(false);
	});

	test("rejects out-of-bounds placement coordinates", () => {
		for (const [row, col] of [
			[BOARD_WIDTH, 0],
			[0, BOARD_HEIGHT],
			[-1, 0],
			[0, -1],
			[1.5, 0],
		]) {
			const result = parseClientMessage(
				JSON.stringify({ type: "placeUnit", unitType: "archer", row, col }),
			);
			expect(result.ok).toBe(false);
		}
	});

	test("rejects empty room and player names", () => {
		expect(
			parseClientMessage(
				JSON.stringify({ type: "join", roomId: "", playerName: "Jeev" }),
			).ok,
		).toBe(false);
		expect(
			parseClientMessage(
				JSON.stringify({ type: "join", roomId: "lobby", playerName: "" }),
			).ok,
		).toBe(false);
	});
});

describe("parseServerMessage", () => {
	test("accepts a valid state message", () => {
		const result = parseServerMessage(
			JSON.stringify({
				type: "state",
				state: {
					board: { width: BOARD_WIDTH, height: BOARD_HEIGHT },
					players: [{ id: "p1", name: "Jeev" }],
					units: [
						{ id: "u1", ownerId: "p1", unitType: "archer", row: 3, col: 4 },
					],
				},
			}),
		);
		expect(result.ok).toBe(true);
	});

	test("accepts a catalog-driven board size (no longer pinned to 72x60)", () => {
		const result = parseServerMessage(
			JSON.stringify({
				type: "state",
				state: { board: { width: 32, height: 48 }, players: [], units: [] },
			}),
		);
		expect(result.ok).toBe(true);
	});

	test("rejects a non-positive board size", () => {
		const result = parseServerMessage(
			JSON.stringify({
				type: "state",
				state: { board: { width: 0, height: -5 }, players: [], units: [] },
			}),
		);
		expect(result.ok).toBe(false);
	});
});

describe("isInBounds", () => {
	test("accepts corners and rejects edges beyond them", () => {
		expect(isInBounds(0, 0)).toBe(true);
		expect(isInBounds(BOARD_WIDTH - 1, BOARD_HEIGHT - 1)).toBe(true);
		expect(isInBounds(BOARD_WIDTH, 0)).toBe(false);
		expect(isInBounds(0, BOARD_HEIGHT)).toBe(false);
		expect(isInBounds(-1, 0)).toBe(false);
		expect(isInBounds(0.5, 0)).toBe(false);
	});
});
