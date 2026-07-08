import { BOARD_HEIGHT, BOARD_WIDTH, gameStateSchema } from "@core/types";
import { describe, expect, test } from "bun:test";
import { Room } from "./room";

function roomWithPlayer(playerId = "p1"): Room {
	const room = new Room("test");
	room.addPlayer(playerId, "Tester");
	return room;
}

describe("Room.placeUnit", () => {
	test("places a unit on an empty in-bounds tile", () => {
		const room = roomWithPlayer();
		const result = room.placeUnit("p1", "archer", 10, 12);
		expect(result.ok).toBe(true);
		if (result.ok) {
			expect(result.unit).toMatchObject({
				ownerId: "p1",
				unitType: "archer",
				row: 10,
				col: 12,
			});
		}
		expect(room.getState().units).toHaveLength(1);
	});

	test("rejects placement by a player not in the room", () => {
		const room = roomWithPlayer();
		const result = room.placeUnit("ghost", "archer", 0, 0);
		expect(result).toMatchObject({ ok: false, code: "notJoined" });
	});

	test("rejects out-of-bounds placement", () => {
		const room = roomWithPlayer();
		expect(room.placeUnit("p1", "archer", BOARD_WIDTH, 0)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
		expect(room.placeUnit("p1", "archer", 0, BOARD_HEIGHT)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
		expect(room.placeUnit("p1", "archer", -1, 0)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
	});

	test("rejects placement on an occupied tile", () => {
		const room = roomWithPlayer();
		expect(room.placeUnit("p1", "archer", 5, 5).ok).toBe(true);
		expect(room.placeUnit("p1", "whelp", 5, 5)).toMatchObject({
			ok: false,
			code: "tileOccupied",
		});
	});
});

describe("Room.moveUnit", () => {
	test("moves a unit its owner controls", () => {
		const room = roomWithPlayer();
		const placed = room.placeUnit("p1", "footman", 1, 1);
		if (!placed.ok) throw new Error("placement failed");
		const result = room.moveUnit("p1", placed.unit.id, 2, 3);
		expect(result.ok).toBe(true);
		expect(room.getState().units[0]).toMatchObject({ row: 2, col: 3 });
	});

	test("rejects moving an unknown unit", () => {
		const room = roomWithPlayer();
		expect(room.moveUnit("p1", "u999", 0, 0)).toMatchObject({
			ok: false,
			code: "unknownUnit",
		});
	});

	test("rejects moving another player's unit", () => {
		const room = roomWithPlayer();
		room.addPlayer("p2", "Rival");
		const placed = room.placeUnit("p1", "holyKnight", 4, 4);
		if (!placed.ok) throw new Error("placement failed");
		expect(room.moveUnit("p2", placed.unit.id, 5, 5)).toMatchObject({
			ok: false,
			code: "notYourUnit",
		});
	});

	test("rejects moving onto an occupied tile but allows moving in place", () => {
		const room = roomWithPlayer();
		const a = room.placeUnit("p1", "archer", 1, 1);
		const b = room.placeUnit("p1", "whelp", 2, 2);
		if (!a.ok || !b.ok) throw new Error("placement failed");
		expect(room.moveUnit("p1", a.unit.id, 2, 2)).toMatchObject({
			ok: false,
			code: "tileOccupied",
		});
		expect(room.moveUnit("p1", a.unit.id, 1, 1).ok).toBe(true);
	});

	test("rejects out-of-bounds moves", () => {
		const room = roomWithPlayer();
		const placed = room.placeUnit("p1", "archer", 1, 1);
		if (!placed.ok) throw new Error("placement failed");
		expect(room.moveUnit("p1", placed.unit.id, BOARD_WIDTH, 0)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
	});
});

describe("Room membership", () => {
	test("removing a player removes their units and can empty the room", () => {
		const room = new Room("test");
		room.addPlayer("p1", "One");
		room.addPlayer("p2", "Two");
		room.placeUnit("p1", "archer", 0, 0);
		room.placeUnit("p2", "whelp", 1, 1);

		room.removePlayer("p1");
		const state = room.getState();
		expect(state.players).toHaveLength(1);
		expect(state.units).toHaveLength(1);
		expect(state.units[0]?.ownerId).toBe("p2");
		expect(room.isEmpty).toBe(false);

		room.removePlayer("p2");
		expect(room.isEmpty).toBe(true);
	});

	test("getState matches the shared protocol schema", () => {
		const room = roomWithPlayer();
		room.placeUnit("p1", "cathedral", 30, 20);
		expect(gameStateSchema.safeParse(room.getState()).success).toBe(true);
	});
});
