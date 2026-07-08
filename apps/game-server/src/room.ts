import {
	BOARD_HEIGHT,
	BOARD_WIDTH,
	type ErrorCode,
	type GameState,
	type Player,
	type Unit,
	type UnitType,
	isInBounds,
} from "@core/types";

export type ActionResult =
	| { ok: true; unit: Unit }
	| { ok: false; code: ErrorCode; message: string };

/**
 * Authoritative state for one game room: who is in it and which units sit on
 * which tiles of the 72x60 board. Pure logic — no transport concerns — so it
 * can be unit-tested directly.
 */
export class Room {
	readonly id: string;
	private readonly players = new Map<string, Player>();
	private readonly units = new Map<string, Unit>();
	private nextUnitId = 1;

	constructor(id: string) {
		this.id = id;
	}

	get isEmpty(): boolean {
		return this.players.size === 0;
	}

	addPlayer(id: string, name: string): Player {
		const player: Player = { id, name };
		this.players.set(id, player);
		return player;
	}

	/** Removes the player and every unit they own. */
	removePlayer(id: string): void {
		this.players.delete(id);
		for (const [unitId, unit] of this.units) {
			if (unit.ownerId === id) this.units.delete(unitId);
		}
	}

	hasPlayer(id: string): boolean {
		return this.players.has(id);
	}

	placeUnit(
		ownerId: string,
		unitType: UnitType,
		row: number,
		col: number,
	): ActionResult {
		if (!this.players.has(ownerId)) {
			return {
				ok: false,
				code: "notJoined",
				message: "player is not in this room",
			};
		}
		if (!isInBounds(row, col)) {
			return {
				ok: false,
				code: "outOfBounds",
				message: `(${row},${col}) is outside the ${BOARD_WIDTH}x${BOARD_HEIGHT} board`,
			};
		}
		const occupant = this.unitAt(row, col);
		if (occupant) {
			return {
				ok: false,
				code: "tileOccupied",
				message: `tile (${row},${col}) is occupied by unit ${occupant.id}`,
			};
		}
		const unit: Unit = {
			id: `u${this.nextUnitId++}`,
			ownerId,
			unitType,
			row,
			col,
		};
		this.units.set(unit.id, unit);
		return { ok: true, unit };
	}

	moveUnit(
		playerId: string,
		unitId: string,
		row: number,
		col: number,
	): ActionResult {
		const unit = this.units.get(unitId);
		if (!unit) {
			return { ok: false, code: "unknownUnit", message: `no unit ${unitId}` };
		}
		if (unit.ownerId !== playerId) {
			return {
				ok: false,
				code: "notYourUnit",
				message: `unit ${unitId} belongs to another player`,
			};
		}
		if (!isInBounds(row, col)) {
			return {
				ok: false,
				code: "outOfBounds",
				message: `(${row},${col}) is outside the ${BOARD_WIDTH}x${BOARD_HEIGHT} board`,
			};
		}
		const occupant = this.unitAt(row, col);
		if (occupant && occupant.id !== unitId) {
			return {
				ok: false,
				code: "tileOccupied",
				message: `tile (${row},${col}) is occupied by unit ${occupant.id}`,
			};
		}
		const moved: Unit = { ...unit, row, col };
		this.units.set(unitId, moved);
		return { ok: true, unit: moved };
	}

	getState(): GameState {
		return {
			board: { width: BOARD_WIDTH, height: BOARD_HEIGHT },
			players: [...this.players.values()],
			units: [...this.units.values()],
		};
	}

	private unitAt(row: number, col: number): Unit | undefined {
		for (const unit of this.units.values()) {
			if (unit.row === row && unit.col === col) return unit;
		}
		return undefined;
	}
}
