import type { ErrorCode, GameState, Player, Unit, UnitDef } from "@core/types";
import {
	type ServerCatalog,
	footprintTiles,
	footprintsOverlap,
} from "./catalog";

export type ActionResult =
	| { ok: true; unit: Unit }
	| { ok: false; code: ErrorCode; message: string };

/**
 * Authoritative state for one game room: who is in it and which units sit on
 * which tiles. As of M1 the room is CATALOG-DRIVEN — the board size and every
 * unit's footprint come from the loaded catalog, so placement validation is
 * multi-tile (a 5x3 footman squad occupies 15 tiles, not one) and the board is
 * the catalog's 32x48, not the old hardcoded 72x60. Pure logic — no transport
 * concerns — so it can be unit-tested directly.
 */
export class Room {
	readonly id: string;
	private readonly catalog: ServerCatalog;
	private readonly players = new Map<string, Player>();
	private readonly units = new Map<string, Unit>();
	private nextUnitId = 1;

	constructor(id: string, catalog: ServerCatalog) {
		this.id = id;
		this.catalog = catalog;
	}

	get isEmpty(): boolean {
		return this.players.size === 0;
	}

	/** The catalog board dimensions this room runs on. */
	get board(): { w: number; h: number } {
		return this.catalog.board;
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
		unitType: string,
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
		const def = this.catalog.unitById.get(unitType);
		if (!def) {
			return {
				ok: false,
				code: "unknownUnitType",
				message: `no unit "${unitType}" in the catalog`,
			};
		}
		const bounds = this.footprintInBounds(def, row, col);
		if (bounds) return bounds;
		const overlap = this.footprintCollision(def, row, col, null);
		if (overlap) return overlap;

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
		const def = this.catalog.unitById.get(unit.unitType);
		if (!def) {
			// Should be impossible (it was placed from the catalog), but guard it.
			return {
				ok: false,
				code: "unknownUnitType",
				message: `unit ${unitId} has type "${unit.unitType}" absent from the catalog`,
			};
		}
		const bounds = this.footprintInBounds(def, row, col);
		if (bounds) return bounds;
		const overlap = this.footprintCollision(def, row, col, unitId);
		if (overlap) return overlap;

		const moved: Unit = { ...unit, row, col };
		this.units.set(unitId, moved);
		return { ok: true, unit: moved };
	}

	getState(): GameState {
		return {
			board: { width: this.catalog.board.w, height: this.catalog.board.h },
			players: [...this.players.values()],
			units: [...this.units.values()],
		};
	}

	/**
	 * Returns an error result if the unit's footprint at (row, col) spills off
	 * the catalog board, else null.
	 */
	private footprintInBounds(
		def: UnitDef,
		row: number,
		col: number,
	): { ok: false; code: ErrorCode; message: string } | null {
		if (!Number.isInteger(row) || !Number.isInteger(col)) {
			return {
				ok: false,
				code: "outOfBounds",
				message: `(${row},${col}) is not an integer tile`,
			};
		}
		const tiles = footprintTiles(def, row, col);
		const { w, h } = this.catalog.board;
		if (
			tiles.rowStart < 0 ||
			tiles.colStart < 0 ||
			tiles.rowEnd > w - 1 ||
			tiles.colEnd > h - 1
		) {
			return {
				ok: false,
				code: "outOfBounds",
				message: `${def.id} (${def.placement.footprint.w}x${def.placement.footprint.h}) at (${row},${col}) does not fit the ${w}x${h} board`,
			};
		}
		return null;
	}

	/**
	 * Returns an error result if the unit's footprint at (row, col) would
	 * overlap any other unit's footprint, else null. `ignoreUnitId` is the unit
	 * being moved (so it can overlap its own current tiles — a no-op move stays
	 * legal).
	 */
	private footprintCollision(
		def: UnitDef,
		row: number,
		col: number,
		ignoreUnitId: string | null,
	): { ok: false; code: ErrorCode; message: string } | null {
		const placed = footprintTiles(def, row, col);
		for (const other of this.units.values()) {
			if (other.id === ignoreUnitId) continue;
			const otherDef = this.catalog.unitById.get(other.unitType);
			if (!otherDef) continue;
			const otherTiles = footprintTiles(otherDef, other.row, other.col);
			if (footprintsOverlap(placed, otherTiles)) {
				return {
					ok: false,
					code: "tileOccupied",
					message: `footprint at (${row},${col}) overlaps unit ${other.id}`,
				};
			}
		}
		return null;
	}
}
