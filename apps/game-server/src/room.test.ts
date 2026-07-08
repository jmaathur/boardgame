import { gameStateSchema } from "@core/types";
import { describe, expect, test } from "bun:test";
import { Room } from "./room";
import { testCatalog } from "./testCatalog";

function roomWithPlayer(playerId = "p1"): Room {
	const room = new Room("test", testCatalog());
	room.addPlayer(playerId, "Tester");
	return room;
}

describe("Room.placeUnit (catalog-driven footprints)", () => {
	test("places a unit on an empty in-bounds spot", () => {
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

	test("rejects an unknown unit type", () => {
		const room = roomWithPlayer();
		expect(room.placeUnit("p1", "dragonlord", 0, 0)).toMatchObject({
			ok: false,
			code: "unknownUnitType",
		});
	});

	test("rejects placement by a player not in the room", () => {
		const room = roomWithPlayer();
		expect(room.placeUnit("ghost", "archer", 0, 0)).toMatchObject({
			ok: false,
			code: "notJoined",
		});
	});

	test("rejects a footprint that spills off the board", () => {
		const room = roomWithPlayer();
		// board is 32x48; cathedral is 4x4, so row 30 => rows 30..33 > 31.
		expect(room.placeUnit("p1", "cathedral", 30, 0)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
		// negative anchor
		expect(room.placeUnit("p1", "archer", -1, 0)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
		// col axis: archer is 4x2, col 47 => cols 47..48 > 47.
		expect(room.placeUnit("p1", "archer", 0, 47)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
	});

	test("rejects placement whose footprint overlaps another unit", () => {
		const room = roomWithPlayer();
		// archer 4x2 at (5,5) occupies rows 5..8, cols 5..6.
		expect(room.placeUnit("p1", "archer", 5, 5).ok).toBe(true);
		// whelp 4x3 at (6,5) occupies rows 6..9, cols 5..7 — overlaps.
		expect(room.placeUnit("p1", "whelp", 6, 5)).toMatchObject({
			ok: false,
			code: "tileOccupied",
		});
	});

	test("allows two units whose footprints do not overlap", () => {
		const room = roomWithPlayer();
		// archer 4x2 at (0,0) => rows 0..3, cols 0..1.
		expect(room.placeUnit("p1", "archer", 0, 0).ok).toBe(true);
		// archer 4x2 at (0,2) => rows 0..3, cols 2..3 — abuts, no overlap.
		expect(room.placeUnit("p1", "archer", 0, 2).ok).toBe(true);
		expect(room.getState().units).toHaveLength(2);
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
		expect(room.moveUnit("p2", placed.unit.id, 8, 8)).toMatchObject({
			ok: false,
			code: "notYourUnit",
		});
	});

	test("rejects moving onto an overlapping spot but allows moving in place", () => {
		const room = roomWithPlayer();
		// two archers far apart
		const a = room.placeUnit("p1", "archer", 0, 0); // rows 0..3, cols 0..1
		const b = room.placeUnit("p1", "archer", 0, 10); // rows 0..3, cols 10..11
		if (!a.ok || !b.ok) throw new Error("placement failed");
		// moving a onto b's footprint overlaps
		expect(room.moveUnit("p1", a.unit.id, 0, 10)).toMatchObject({
			ok: false,
			code: "tileOccupied",
		});
		// moving a in place (its own tiles) is legal
		expect(room.moveUnit("p1", a.unit.id, 0, 0).ok).toBe(true);
	});

	test("rejects out-of-bounds moves", () => {
		const room = roomWithPlayer();
		const placed = room.placeUnit("p1", "archer", 1, 1);
		if (!placed.ok) throw new Error("placement failed");
		expect(room.moveUnit("p1", placed.unit.id, 30, 47)).toMatchObject({
			ok: false,
			code: "outOfBounds",
		});
	});
});

describe("Room membership", () => {
	test("removing a player removes their units and can empty the room", () => {
		const room = new Room("test", testCatalog());
		room.addPlayer("p1", "One");
		room.addPlayer("p2", "Two");
		room.placeUnit("p1", "archer", 0, 0);
		room.placeUnit("p2", "whelp", 10, 10);

		room.removePlayer("p1");
		const state = room.getState();
		expect(state.players).toHaveLength(1);
		expect(state.units).toHaveLength(1);
		expect(state.units[0]?.ownerId).toBe("p2");
		expect(room.isEmpty).toBe(false);

		room.removePlayer("p2");
		expect(room.isEmpty).toBe(true);
	});

	test("getState uses the catalog board and matches the shared schema", () => {
		const room = roomWithPlayer();
		room.placeUnit("p1", "cathedral", 4, 4);
		const state = room.getState();
		expect(state.board).toEqual({
			width: testCatalog().board.w,
			height: testCatalog().board.h,
		});
		expect(gameStateSchema.safeParse(state).success).toBe(true);
	});
});
