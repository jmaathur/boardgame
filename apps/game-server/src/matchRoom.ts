import type { ServerCatalog } from "./catalog";
import { footprintTiles, footprintsOverlap } from "./catalog";
import type {
	CmdRejectCode,
	MatchConfig,
	MatchSnapshot,
	OpponentView,
	Orientation,
	Phase,
	Seat,
	SeatView,
	SquadCard,
	UnitDef,
} from "@core/types";

/**
 * MatchRoom — the pure reducer for one Mechabellum-style match (design doc §8).
 *
 * Phase machine: lobby → commanderPick → planning(1) → battle → results →
 * planning(N+1) | matchEnded. Hidden simultaneous planning is just per-seat
 * views. Battle resolution is a STUB in M3: no combat, damage = prorated
 * invested value of the winner's survivors (invented per side by comparing
 * total invested value). The real sim + battleLog arrive at M4/M5.
 *
 * Transport-free and clock-injected (`now` passed into every mutator, deadlines
 * advanced via tick(now)) so it is fully deterministic and unit-testable — and
 * a faithful spec for the C# port. It never uses Date.now() or Math.random().
 */

export type CommandOutcome =
	| { ok: true }
	| { ok: false; code: CmdRejectCode; message: string };

type PendingBattle = {
	/** Captured blueprint per seat at plan-lock (the reveal). */
	armies: { seat: Seat; cards: SquadCard[] }[];
	acked: Set<Seat>;
};

const SEATS: Seat[] = [0, 1];

type SeatState = {
	seat: Seat;
	playerId: string | null;
	playerName: string;
	resumeToken: string;
	connected: boolean;
	commanderId: string | null;
	commanderOffers: string[];
	hp: number;
	coin: number;
	deploysRemaining: number;
	unlocksRemaining: number;
	ready: boolean;
	cards: SquadCard[];
	unlockedUnits: Set<string>;
	purchasedTechs: Set<string>;
	techPriceByUnit: Map<string, number>;
	/** Snapshot of this seat's cards at the last plan-lock (opponent view). */
	revealedCards: SquadCard[];
};

export class MatchRoom {
	readonly id: string;
	private readonly catalog: ServerCatalog;
	private readonly rules: ServerCatalog["catalog"]["matchRules"];

	private phase: Phase = "lobby";
	private round = 0;
	private phaseDeadline = 0;
	private nextCardId = 1;
	private pendingBattle: PendingBattle | null = null;
	private matchWinner: Seat | null = null;

	private readonly seats: SeatState[];

	constructor(
		id: string,
		catalog: ServerCatalog,
		tokenGen: (seat: Seat) => string,
	) {
		this.id = id;
		this.catalog = catalog;
		this.rules = catalog.catalog.matchRules;
		this.seats = SEATS.map((seat) => ({
			seat,
			playerId: null,
			playerName: "",
			resumeToken: tokenGen(seat),
			connected: false,
			commanderId: null,
			commanderOffers: [],
			hp: 0,
			coin: 0,
			deploysRemaining: 0,
			unlocksRemaining: 0,
			ready: false,
			cards: [],
			unlockedUnits: new Set(),
			purchasedTechs: new Set(),
			techPriceByUnit: new Map(),
			revealedCards: [],
		}));
	}

	// -----------------------------------------------------------------------
	// Membership / connection
	// -----------------------------------------------------------------------

	get currentPhase(): Phase {
		return this.phase;
	}
	get currentRound(): number {
		return this.round;
	}
	get isEmpty(): boolean {
		return this.seats.every((s) => !s.connected);
	}
	get winner(): Seat | null {
		return this.matchWinner;
	}

	/** True if both seats are occupied by a connected player. */
	private get bothConnected(): boolean {
		return this.seats.every((s) => s.connected);
	}

	seatByResumeToken(token: string): Seat | null {
		const s = this.seats.find((s) => s.resumeToken === token);
		return s ? s.seat : null;
	}

	/**
	 * Seat a player. If `resumeToken` matches an existing seat, that seat
	 * reconnects (keeps all state). Otherwise the first free seat is taken.
	 * Returns the seat + its resumeToken, or null if the room is full.
	 */
	join(
		playerId: string,
		playerName: string,
		now: number,
		resumeToken?: string,
	): { seat: Seat; resumeToken: string } | null {
		if (resumeToken) {
			const seatNo = this.seatByResumeToken(resumeToken);
			if (seatNo !== null) {
				const s = this.seats[seatNo];
				s.playerId = playerId;
				s.playerName = playerName || s.playerName;
				s.connected = true;
				return { seat: s.seat, resumeToken: s.resumeToken };
			}
		}
		const free = this.seats.find((s) => !s.connected && s.playerId === null);
		if (!free) return null;
		free.playerId = playerId;
		free.playerName = playerName;
		free.connected = true;
		this.maybeStartCommanderPick(now);
		return { seat: free.seat, resumeToken: free.resumeToken };
	}

	/** Mark a seat disconnected (state is retained for reconnect). */
	disconnect(playerId: string): void {
		const s = this.seats.find((s) => s.playerId === playerId);
		if (!s) return;
		s.connected = false;
	}

	seatOfPlayer(playerId: string): Seat | null {
		const s = this.seats.find((s) => s.playerId === playerId);
		return s ? s.seat : null;
	}

	// -----------------------------------------------------------------------
	// Phase transitions
	// -----------------------------------------------------------------------

	private maybeStartCommanderPick(now: number): void {
		if (this.phase !== "lobby" || !this.bothConnected) return;
		this.phase = "commanderPick";
		const offered = this.rules.commandersOffered;
		const all = this.rules.commanders.map((c) => c.id);
		for (const s of this.seats) {
			// Deterministic offers: rotate the commander list by seat so both
			// seats see a stable, reproducible set (no RNG in the reducer).
			s.commanderOffers = rotate(all, s.seat).slice(0, offered);
			s.commanderId = null;
		}
		this.phaseDeadline = now + this.rules.timers.commanderPickSeconds * 1000;
	}

	/** Advance time-based deadlines. Returns true if a phase transition occurred. */
	tick(now: number): boolean {
		if (this.phaseDeadline !== 0 && now < this.phaseDeadline) return false;
		switch (this.phase) {
			case "commanderPick":
				// Deadline hit: auto-pick the first offer for anyone undecided.
				for (const s of this.seats) {
					if (!s.commanderId) s.commanderId = s.commanderOffers[0] ?? null;
				}
				this.beginPlanning(1, now);
				return true;
			case "planning":
				this.planLock(now);
				return true;
			case "battle":
				this.resolveBattle(now);
				return true;
			case "results":
				this.afterResults(now);
				return true;
			default:
				return false;
		}
	}

	private allCommandersPicked(): boolean {
		return this.seats.every((s) => s.commanderId !== null);
	}

	private beginPlanning(round: number, now: number): void {
		this.phase = "planning";
		this.round = round;
		for (const s of this.seats) {
			// Materialize commander starting units + starting buildings on round 1.
			if (round === 1) this.materializeStartingArmy(s);
			// Income: 200 * round (+ commander economy mods), carried over.
			const income = this.incomeForSeat(s, round);
			s.coin += income;
			s.deploysRemaining = this.deploysForSeat(s);
			s.unlocksRemaining = this.unlocksForSeat(s);
			s.ready = false;
		}
		this.phaseDeadline = now + this.rules.timers.deploySeconds * 1000;
	}

	private planLock(now: number): void {
		// Capture the reveal snapshot once — used as the battle reveal and as
		// next round's opponent view.
		for (const s of this.seats) s.revealedCards = s.cards.map(cloneCard);
		this.pendingBattle = {
			armies: this.seats.map((s) => ({
				seat: s.seat,
				cards: s.revealedCards.map(cloneCard),
			})),
			acked: new Set(),
		};
		this.phase = "battle";
		this.phaseDeadline = now + this.rules.timers.battleSeconds * 1000;
	}

	/** Record a battle ack; both acked (or deadline via tick) ends the battle. */
	battleAck(playerId: string, now: number): void {
		if (this.phase !== "battle" || !this.pendingBattle) return;
		const seat = this.seatOfPlayer(playerId);
		if (seat === null) return;
		this.pendingBattle.acked.add(seat);
		if (this.pendingBattle.acked.size === SEATS.length) this.resolveBattle(now);
	}

	private lastResult: {
		round: number;
		winnerSeat: Seat | null;
		hpDamage: { seat: Seat; amount: number }[];
	} | null = null;

	private resolveBattle(now: number): void {
		if (this.phase !== "battle") return;
		const armies = this.pendingBattle?.armies ?? [];

		// STUB resolver: total invested value decides the winner; the loser takes
		// HP damage equal to the winner's prorated survivor value. With no combat,
		// "survivors" = all of the winner's card-backed units at full strength, so
		// survivor value == the winner's invested value scaled by a margin factor.
		const investedBySeat = new Map<Seat, number>();
		for (const army of armies) {
			const total = army.cards.reduce((sum, c) => sum + c.invested, 0);
			investedBySeat.set(army.seat, total);
		}
		const a = investedBySeat.get(0) ?? 0;
		const b = investedBySeat.get(1) ?? 0;

		let winnerSeat: Seat | null;
		let damage: number;
		if (a === b) {
			// Draw (incl. both empty) → both take the timeout survivor value.
			winnerSeat = null;
			damage = Math.round(Math.min(a, b) * SURVIVOR_FACTOR);
		} else {
			winnerSeat = a > b ? 0 : 1;
			const winnerValue = Math.max(a, b);
			const loserValue = Math.min(a, b);
			// Prorated survivor value: the margin the winner kept, floored so a win
			// always stings even against a near-equal army.
			damage = Math.max(
				MIN_ROUND_DAMAGE,
				Math.round((winnerValue - loserValue) * SURVIVOR_FACTOR),
			);
		}

		const hpDamage: { seat: Seat; amount: number }[] = [];
		for (const s of this.seats) {
			const takesDamage = winnerSeat === null || s.seat !== winnerSeat;
			const amount = takesDamage ? damage : 0;
			if (amount > 0) s.hp = Math.max(0, s.hp - amount);
			hpDamage.push({ seat: s.seat, amount });
		}

		this.lastResult = { round: this.round, winnerSeat, hpDamage };
		this.pendingBattle = null;
		this.phase = "results";
		this.phaseDeadline = now + this.rules.timers.resultsHoldSeconds * 1000;
	}

	private afterResults(now: number): void {
		if (this.phase !== "results") return;
		const dead = this.seats.filter((s) => s.hp <= 0);
		if (dead.length > 0) {
			// Match over. Winner = the seat with more HP (or null if both dead).
			const alive = this.seats.filter((s) => s.hp > 0);
			this.matchWinner = alive.length === 1 ? alive[0].seat : null;
			this.phase = "matchEnded";
			this.phaseDeadline = 0;
			return;
		}
		this.beginPlanning(this.round + 1, now);
	}

	// -----------------------------------------------------------------------
	// Economy helpers
	// -----------------------------------------------------------------------

	private incomeForSeat(s: SeatState, round: number): number {
		let income = this.rules.income.perRoundIncrement * round;
		if (round === 1) income += this.rules.income.startingIncome;
		income += this.commanderEconomy(s).incomePerRoundAdd;
		return income;
	}

	private deploysForSeat(s: SeatState): number {
		return this.rules.deploysPerRound + this.commanderEconomy(s).deploySlotsAdd;
	}

	private unlocksForSeat(s: SeatState): number {
		return this.rules.unlocksPerRound + this.commanderEconomy(s).unlockSlotsAdd;
	}

	private commanderEconomy(s: SeatState): {
		incomePerRoundAdd: number;
		deploySlotsAdd: number;
		unlockSlotsAdd: number;
		startingIncomeAdd: number;
	} {
		const acc = {
			incomePerRoundAdd: 0,
			deploySlotsAdd: 0,
			unlockSlotsAdd: 0,
			startingIncomeAdd: 0,
		};
		if (!s.commanderId) return acc;
		const cmd = this.rules.commanders.find((c) => c.id === s.commanderId);
		if (!cmd) return acc;
		for (const ability of cmd.ability) {
			if (ability.kind === "economyMod") {
				acc.incomePerRoundAdd += ability.incomePerRoundAdd;
				acc.deploySlotsAdd += ability.deploySlotsAdd;
				acc.unlockSlotsAdd += ability.unlockSlotsAdd;
				acc.startingIncomeAdd += ability.startingIncomeAdd;
			}
		}
		return acc;
	}

	private materializeStartingArmy(s: SeatState): void {
		// Commander HP is the player HP pool.
		const cmd = this.rules.commanders.find((c) => c.id === s.commanderId);
		s.hp = cmd?.hp ?? 5000;
		s.coin = this.commanderEconomy(s).startingIncomeAdd;
		// Starting buildings for this seat.
		for (const b of this.rules.startingBuildings) {
			if (b.seat !== s.seat) continue;
			this.addCard(s, b.unitId, b.anchor.row, b.anchor.col, b.orientation, 0, {
				free: true,
			});
		}
		// Commander starting units.
		for (const su of cmd?.startingUnits ?? []) {
			this.addCard(
				s,
				su.unitId,
				su.anchor.row,
				su.anchor.col,
				su.orientation,
				0,
				{
					free: true,
				},
			);
		}
	}

	// -----------------------------------------------------------------------
	// Planning commands
	// -----------------------------------------------------------------------

	pickCommander(
		playerId: string,
		commanderId: string,
		now: number,
	): CommandOutcome {
		const s = this.requireSeat(playerId);
		if (!s) return fail("notJoined", "not in this room");
		if (this.phase !== "commanderPick")
			return fail("wrongPhase", "not in commander pick");
		if (!s.commanderOffers.includes(commanderId))
			return fail("unknownCommander", `commander ${commanderId} not offered`);
		if (s.commanderId) return fail("commanderAlreadyPicked", "already picked");
		s.commanderId = commanderId;
		if (this.allCommandersPicked()) this.beginPlanning(1, now);
		return { ok: true };
	}

	buySquad(
		playerId: string,
		unitId: string,
		row: number,
		col: number,
		orientation: Orientation,
	): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		const unit = this.catalog.unitById.get(unitId);
		if (!unit) return fail("unknownUnit", `no unit ${unitId}`);
		if (unit.cost.unlockCost > 0 && !s.unlockedUnits.has(unitId))
			return fail("notUnlocked", `${unitId} is not unlocked`);
		if (s.deploysRemaining <= 0)
			return fail("noDeploysLeft", "no deploys left");
		if (s.coin < unit.cost.deployCost)
			return fail("insufficientFunds", "not enough coin");
		const placement = this.validatePlacement(
			s,
			unit,
			row,
			col,
			orientation,
			null,
		);
		if (placement) return placement;

		s.coin -= unit.cost.deployCost;
		s.deploysRemaining -= 1;
		this.addCard(s, unitId, row, col, orientation, unit.cost.deployCost, {
			free: false,
		});
		return { ok: true };
	}

	moveSquad(
		playerId: string,
		cardId: string,
		row: number,
		col: number,
		orientation: Orientation,
	): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		const card = s.cards.find((c) => c.cardId === cardId);
		if (!card) return fail("unknownCard", `no card ${cardId}`);
		const unit = this.catalog.unitById.get(card.unitId);
		if (!unit) return fail("unknownUnit", `card unit ${card.unitId} missing`);
		const placement = this.validatePlacement(
			s,
			unit,
			row,
			col,
			orientation,
			cardId,
		);
		if (placement) return placement;
		card.anchor = { row, col };
		card.orientation = orientation;
		return { ok: true };
	}

	sellSquad(playerId: string, cardId: string): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		const idx = s.cards.findIndex((c) => c.cardId === cardId);
		if (idx < 0) return fail("unknownCard", `no card ${cardId}`);
		const card = s.cards[idx];
		// Only this-round purchases can be sold (free revert).
		if (card.purchasedRound !== this.round)
			return fail(
				"notThisRoundPurchase",
				"can only sell this round's purchases",
			);
		s.cards.splice(idx, 1);
		s.coin += card.invested;
		s.deploysRemaining += 1;
		return { ok: true };
	}

	unlockUnit(playerId: string, unitId: string): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		const unit = this.catalog.unitById.get(unitId);
		if (!unit) return fail("unknownUnit", `no unit ${unitId}`);
		if (s.unlockedUnits.has(unitId))
			return fail("alreadyUnlocked", "already unlocked");
		if (s.unlocksRemaining <= 0)
			return fail("noUnlocksLeft", "no unlocks left");
		if (s.coin < unit.cost.unlockCost)
			return fail("insufficientFunds", "not enough coin");
		s.coin -= unit.cost.unlockCost;
		s.unlocksRemaining -= 1;
		s.unlockedUnits.add(unitId);
		return { ok: true };
	}

	buyTech(playerId: string, unitId: string, techId: string): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		const unit = this.catalog.unitById.get(unitId);
		if (!unit) return fail("unknownUnit", `no unit ${unitId}`);
		const tech = unit.techs.find((t) => t.id === techId);
		if (!tech) return fail("unknownTech", `no tech ${techId} on ${unitId}`);
		if (s.purchasedTechs.has(techId))
			return fail("techAlreadyOwned", "already owned");
		const price = s.techPriceByUnit.get(unitId) ?? tech.cost;
		if (s.coin < price) return fail("insufficientFunds", "not enough coin");
		s.coin -= price;
		s.purchasedTechs.add(techId);
		// Escalate this unit type's other tech prices.
		s.techPriceByUnit.set(
			unitId,
			(s.techPriceByUnit.get(unitId) ?? tech.cost) +
				this.rules.techPriceEscalation,
		);
		return { ok: true };
	}

	buyLevel(playerId: string, cardId: string): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		const card = s.cards.find((c) => c.cardId === cardId);
		if (!card) return fail("unknownCard", `no card ${cardId}`);
		const unit = this.catalog.unitById.get(card.unitId);
		if (!unit) return fail("unknownUnit", `card unit ${card.unitId} missing`);
		if (card.xp < unit.squad.xpToLevel)
			return fail("xpNotReady", "not enough xp to level");
		const cost = Math.round(
			unit.cost.deployCost * this.rules.leveling.upgradeCostFraction,
		);
		if (s.coin < cost) return fail("insufficientFunds", "not enough coin");
		s.coin -= cost;
		card.xp -= unit.squad.xpToLevel;
		card.level += 1;
		card.invested += cost;
		return { ok: true };
	}

	setReady(playerId: string, ready: boolean, now: number): CommandOutcome {
		const s = this.requirePlanningSeat(playerId);
		if ("code" in s) return s;
		s.ready = ready;
		if (this.seats.every((s) => s.ready)) this.planLock(now);
		return { ok: true };
	}

	// -----------------------------------------------------------------------
	// Snapshots
	// -----------------------------------------------------------------------

	matchConfig(): MatchConfig {
		return {
			board: { w: this.rules.board.w, h: this.rules.board.h },
			deploysPerRound: this.rules.deploysPerRound,
			unlocksPerRound: this.rules.unlocksPerRound,
			incomePerRoundIncrement: this.rules.income.perRoundIncrement,
			deploySeconds: this.rules.timers.deploySeconds,
			battleSeconds: this.rules.timers.battleSeconds,
			commanderPickSeconds: this.rules.timers.commanderPickSeconds,
			commandersOffered: this.rules.commandersOffered,
		};
	}

	snapshotFor(seat: Seat): MatchSnapshot {
		const own = this.seats[seat];
		const opp = this.seats[seat === 0 ? 1 : 0];
		return {
			phase: this.phase,
			round: this.round,
			phaseDeadline: this.phaseDeadline,
			commanderOffers: own.commanderOffers,
			own: this.seatView(own),
			opponent: opp.connected || opp.playerId ? this.opponentView(opp) : null,
		};
	}

	lastRoundResult(): {
		round: number;
		winnerSeat: Seat | null;
		hpDamage: { seat: Seat; amount: number }[];
		hp: { seat: Seat; hp: number }[];
	} | null {
		if (!this.lastResult) return null;
		return {
			...this.lastResult,
			hp: this.seats.map((s) => ({ seat: s.seat, hp: s.hp })),
		};
	}

	revealArmies(): { seat: Seat; cards: SquadCard[] }[] {
		return this.seats.map((s) => ({
			seat: s.seat,
			cards: s.revealedCards.map(cloneCard),
		}));
	}

	finalHp(): { seat: Seat; hp: number }[] {
		return this.seats.map((s) => ({ seat: s.seat, hp: s.hp }));
	}

	private seatView(s: SeatState): SeatView {
		return {
			seat: s.seat,
			playerName: s.playerName,
			connected: s.connected,
			commanderId: s.commanderId,
			hp: s.hp,
			coin: s.coin,
			deploysRemaining: s.deploysRemaining,
			unlocksRemaining: s.unlocksRemaining,
			ready: s.ready,
			cards: s.cards.map(cloneCard),
			tech: {
				unlockedUnits: [...s.unlockedUnits],
				purchasedTechs: [...s.purchasedTechs],
				techPriceByUnit: Object.fromEntries(s.techPriceByUnit),
			},
		};
	}

	private opponentView(s: SeatState): OpponentView {
		return {
			seat: s.seat,
			playerName: s.playerName,
			connected: s.connected,
			commanderId: s.commanderId,
			hp: s.hp,
			// Only the LAST revealed army is visible — never this round's hidden plan.
			cards: s.revealedCards.map(cloneCard),
		};
	}

	// -----------------------------------------------------------------------
	// Internals
	// -----------------------------------------------------------------------

	private requireSeat(playerId: string): SeatState | null {
		return this.seats.find((s) => s.playerId === playerId) ?? null;
	}

	private requirePlanningSeat(
		playerId: string,
	): SeatState | { ok: false; code: CmdRejectCode; message: string } {
		const s = this.requireSeat(playerId);
		if (!s) return fail("notJoined", "not in this room");
		if (this.phase !== "planning") return fail("wrongPhase", "not in planning");
		return s;
	}

	private addCard(
		s: SeatState,
		unitId: string,
		row: number,
		col: number,
		orientation: Orientation,
		invested: number,
		opts: { free: boolean },
	): SquadCard {
		const card: SquadCard = {
			cardId: `sq${this.nextCardId++}`,
			unitId,
			anchor: { row, col },
			orientation,
			level: 1,
			xp: 0,
			purchasedRound: opts.free ? 0 : this.round,
			invested,
		};
		s.cards.push(card);
		return card;
	}

	/**
	 * Placement validation reusing the shared footprint helpers: board bounds,
	 * own-half (deploy zone, buildings exempt), and no overlap with the seat's
	 * other cards (ignoreCardId lets a move overlap its own current tiles).
	 */
	private validatePlacement(
		s: SeatState,
		unit: UnitDef,
		row: number,
		col: number,
		_orientation: Orientation,
		ignoreCardId: string | null,
	): { ok: false; code: CmdRejectCode; message: string } | null {
		if (!Number.isInteger(row) || !Number.isInteger(col))
			return fail("outOfBounds", "non-integer tile");
		const tiles = footprintTiles(unit, row, col);
		const board = this.rules.board;
		if (
			tiles.rowStart < 0 ||
			tiles.colStart < 0 ||
			tiles.rowEnd > board.w - 1 ||
			tiles.colEnd > board.h - 1
		)
			return fail(
				"outOfBounds",
				`does not fit the ${board.w}x${board.h} board`,
			);

		if (
			unit.placement.domain !== "building" &&
			!this.withinSeatZones(s.seat, tiles)
		)
			return fail("outsideOwnHalf", "outside your deploy zone");

		for (const other of s.cards) {
			if (other.cardId === ignoreCardId) continue;
			const otherUnit = this.catalog.unitById.get(other.unitId);
			if (!otherUnit) continue;
			const otherTiles = footprintTiles(
				otherUnit,
				other.anchor.row,
				other.anchor.col,
			);
			if (footprintsOverlap(tiles, otherTiles))
				return fail("tileOccupied", `overlaps card ${other.cardId}`);
		}
		return null;
	}

	private withinSeatZones(
		seat: Seat,
		tiles: {
			rowStart: number;
			rowEnd: number;
			colStart: number;
			colEnd: number;
		},
	): boolean {
		for (const zone of this.rules.deployZones) {
			if (zone.seat !== seat) continue;
			const r = zone.rect;
			if (
				tiles.rowStart >= r.row &&
				tiles.rowEnd <= r.row + r.w - 1 &&
				tiles.colStart >= r.col &&
				tiles.colEnd <= r.col + r.h - 1
			) {
				return true;
			}
		}
		return false;
	}
}

// Stub-resolver tuning: fraction of value converted to HP damage, and a floor.
const SURVIVOR_FACTOR = 0.5;
const MIN_ROUND_DAMAGE = 100;

function cloneCard(c: SquadCard): SquadCard {
	return { ...c, anchor: { ...c.anchor } };
}

function rotate<T>(arr: T[], by: number): T[] {
	if (arr.length === 0) return arr;
	const n = ((by % arr.length) + arr.length) % arr.length;
	return [...arr.slice(n), ...arr.slice(0, n)];
}

function fail(
	code: CmdRejectCode,
	message: string,
): {
	ok: false;
	code: CmdRejectCode;
	message: string;
} {
	return { ok: false, code, message };
}
