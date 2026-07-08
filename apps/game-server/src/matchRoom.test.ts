import { describe, expect, test } from "bun:test";
import type { Seat } from "@core/types";
import { MatchRoom } from "./matchRoom";
import { testCatalog } from "./testCatalog";

// Deterministic resume tokens so reconnect tests are reproducible.
function newRoom(id = "m"): MatchRoom {
	return new MatchRoom(id, testCatalog(), (seat) => `tok-${id}-${seat}`);
}

/** Seat both players and pick commanders, landing in planning(1). */
function startMatch(room: MatchRoom, now = 0): void {
	room.join("p0", "Alice", now);
	room.join("p1", "Bob", now);
	expect(room.currentPhase).toBe("commanderPick");
	const off0 = room.snapshotFor(0).commanderOffers[0];
	const off1 = room.snapshotFor(1).commanderOffers[0];
	expect(room.pickCommander("p0", off0, now).ok).toBe(true);
	expect(room.pickCommander("p1", off1, now).ok).toBe(true);
	expect(room.currentPhase).toBe("planning");
	expect(room.currentRound).toBe(1);
}

describe("MatchRoom — lobby & commander pick", () => {
	test("stays in lobby until both seats connect", () => {
		const room = newRoom();
		room.join("p0", "Alice", 0);
		expect(room.currentPhase).toBe("lobby");
		room.join("p1", "Bob", 0);
		expect(room.currentPhase).toBe("commanderPick");
	});

	test("rejects a commander that was not offered", () => {
		const room = newRoom();
		room.join("p0", "Alice", 0);
		room.join("p1", "Bob", 0);
		const r = room.pickCommander("p0", "notARealCommander", 0);
		expect(r).toMatchObject({ ok: false, code: "unknownCommander" });
	});

	test("both picks advance to planning(1) and set commander HP", () => {
		const room = newRoom();
		startMatch(room);
		const s0 = room.snapshotFor(0).own;
		expect(s0.commanderId).not.toBeNull();
		expect(s0.hp).toBeGreaterThan(0);
	});

	test("a third player cannot join a full room", () => {
		const room = newRoom();
		room.join("p0", "Alice", 0);
		room.join("p1", "Bob", 0);
		expect(room.join("p2", "Carol", 0)).toBeNull();
	});
});

describe("MatchRoom — economy & planning", () => {
	test("income is 200*round plus commander mods and starting buildings appear", () => {
		const room = newRoom();
		startMatch(room);
		const own = room.snapshotFor(0).own;
		// starting cathedral + barracks materialized (buildings are free cards)
		expect(own.cards.length).toBeGreaterThanOrEqual(2);
		expect(own.cards.some((c) => c.unitId === "cathedral")).toBe(true);
		// round-1 income at least 200 (+ startingIncome 200 in base rules)
		expect(own.coin).toBeGreaterThanOrEqual(200);
	});

	test("buying a squad spends coin, consumes a deploy, and places a card", () => {
		const room = newRoom();
		startMatch(room);
		const before = room.snapshotFor(0).own;
		const coin = before.coin;
		const deploys = before.deploysRemaining;
		// (0,10) is clear of the seat-0 starting buildings (back line, cols 0-3).
		const r = room.buySquad("p0", "footman", 0, 10, "north");
		expect(r.ok).toBe(true);
		const after = room.snapshotFor(0).own;
		expect(after.coin).toBe(coin - 100); // footman deployCost
		expect(after.deploysRemaining).toBe(deploys - 1);
		expect(after.cards.some((c) => c.unitId === "footman")).toBe(true);
	});

	test("cannot buy beyond the deploy limit", () => {
		const room = newRoom();
		startMatch(room);
		// deploysPerRound is 2 by default; warlord commander may grant +1.
		// Place along row 0 (clear of the back-line buildings), spaced by footprint.
		let bought = 0;
		let col = 6;
		for (let i = 0; i < 6; i++) {
			const r = room.buySquad("p0", "footman", 0, col, "north");
			col += 6; // footman is 5 wide → space them out to avoid overlap
			if (r.ok) bought += 1;
			else {
				expect(
					r.code === "noDeploysLeft" || r.code === "insufficientFunds",
				).toBe(true);
				break;
			}
		}
		expect(bought).toBeGreaterThanOrEqual(2);
		expect(room.snapshotFor(0).own.deploysRemaining).toBe(0);
	});

	test("rejects a placement outside the own half", () => {
		const room = newRoom();
		startMatch(room);
		// seat 0 owns cols 0..23; col 40 is enemy half
		const r = room.buySquad("p0", "footman", 2, 40, "north");
		expect(r).toMatchObject({ ok: false, code: "outsideOwnHalf" });
	});

	test("rejects overlapping placement", () => {
		const room = newRoom();
		startMatch(room);
		expect(room.buySquad("p0", "archer", 0, 10, "north").ok).toBe(true);
		// archer 4x2 at (0,10) → rows 0..3 cols 10..11; overlap at (1,10)
		const r = room.buySquad("p0", "archer", 1, 10, "north");
		expect(r).toMatchObject({ ok: false, code: "tileOccupied" });
	});

	test("sell refunds and frees a deploy, but only for this round's buys", () => {
		const room = newRoom();
		startMatch(room);
		const buy = room.buySquad("p0", "footman", 0, 10, "north");
		expect(buy.ok).toBe(true);
		const card = room
			.snapshotFor(0)
			.own.cards.find((c) => c.unitId === "footman" && c.purchasedRound === 1)!;
		const coinAfterBuy = room.snapshotFor(0).own.coin;
		const sell = room.sellSquad("p0", card.cardId);
		expect(sell.ok).toBe(true);
		expect(room.snapshotFor(0).own.coin).toBe(coinAfterBuy + 100);
		// cannot sell a starting building (purchasedRound 0)
		const building = room
			.snapshotFor(0)
			.own.cards.find((c) => c.unitId === "cathedral")!;
		expect(room.sellSquad("p0", building.cardId)).toMatchObject({
			ok: false,
			code: "notThisRoundPurchase",
		});
	});

	test("unlock gates a locked unit before it can be bought", () => {
		const room = newRoom();
		startMatch(room);
		// ballista has unlockCost 50 → buying before unlock is rejected
		expect(room.buySquad("p0", "ballista", 0, 10, "north")).toMatchObject({
			ok: false,
			code: "notUnlocked",
		});
		expect(room.unlockUnit("p0", "ballista").ok).toBe(true);
		// now buyable (funds permitting)
		const r = room.buySquad("p0", "ballista", 0, 10, "north");
		expect(r.ok || r.code === "insufficientFunds").toBe(true);
	});

	test("buying a tech escalates that unit type's next tech price", () => {
		const room = newRoom();
		startMatch(room);
		// give seat plenty of coin by advancing to a later round isn't trivial;
		// archer techs are cheap (100/150). Buy the first.
		const first = room.buyTech("p0", "archer", "archerFireArrows");
		expect(first.ok || first.code === "insufficientFunds").toBe(true);
		if (first.ok) {
			const tech = room.snapshotFor(0).own.tech;
			expect(tech.purchasedTechs).toContain("archerFireArrows");
			// escalated price recorded for archer
			expect(tech.techPriceByUnit.archer).toBeGreaterThan(0);
			// buying it again is rejected
			expect(room.buyTech("p0", "archer", "archerFireArrows")).toMatchObject({
				ok: false,
				code: "techAlreadyOwned",
			});
		}
	});
});

describe("MatchRoom — hidden planning", () => {
	test("a seat's own plan is not visible to the opponent until reveal", () => {
		const room = newRoom();
		startMatch(room);
		expect(room.buySquad("p0", "footman", 0, 10, "north").ok).toBe(true);
		// From seat 1's view, opponent (seat 0) shows only revealed cards (none yet
		// this round beyond nothing revealed → empty until a plan-lock happens).
		const oppView = room.snapshotFor(1).opponent!;
		expect(oppView.cards.some((c) => c.unitId === "footman")).toBe(false);
	});

	test("both ready triggers plan-lock and captures the reveal", () => {
		const room = newRoom();
		startMatch(room);
		expect(room.buySquad("p0", "footman", 0, 10, "north").ok).toBe(true);
		expect(room.buySquad("p1", "archer", 0, 30, "north").ok).toBe(true);
		expect(room.setReady("p0", true, 0).ok).toBe(true);
		expect(room.currentPhase).toBe("planning");
		expect(room.setReady("p1", true, 0).ok).toBe(true);
		expect(room.currentPhase).toBe("battle");
		// after battle the reveal exists
		const armies = room.revealArmies();
		expect(
			armies
				.find((a) => a.seat === 0)!
				.cards.some((c) => c.unitId === "footman"),
		).toBe(true);
	});
});

describe("MatchRoom — battle stub & round result", () => {
	function planLockWithArmies(room: MatchRoom): void {
		startMatch(room);
		// seat 0 buys more value than seat 1 → seat 0 should win the stub battle
		expect(room.buySquad("p0", "footman", 0, 10, "north").ok).toBe(true);
		expect(room.buySquad("p0", "archer", 20, 10, "north").ok).toBe(true);
		expect(room.buySquad("p1", "footman", 0, 30, "north").ok).toBe(true);
		room.setReady("p0", true, 0);
		room.setReady("p1", true, 0);
		expect(room.currentPhase).toBe("battle");
	}

	test("higher invested value wins and the loser takes HP damage", () => {
		const room = newRoom();
		planLockWithArmies(room);
		// both ack → battle resolves
		room.battleAck("p0", 0);
		room.battleAck("p1", 0);
		expect(room.currentPhase).toBe("results");
		const result = room.lastRoundResult()!;
		expect(result.winnerSeat).toBe(0);
		const dmgToLoser = result.hpDamage.find((d) => d.seat === 1)!.amount;
		expect(dmgToLoser).toBeGreaterThan(0);
		expect(result.hpDamage.find((d) => d.seat === 0)!.amount).toBe(0);
	});

	test("results advances to the next planning round when both survive", () => {
		const room = newRoom();
		planLockWithArmies(room);
		room.battleAck("p0", 0);
		room.battleAck("p1", 0);
		expect(room.currentPhase).toBe("results");
		// advance past the results hold
		room.tick(1_000_000);
		expect(room.currentPhase).toBe("planning");
		expect(room.currentRound).toBe(2);
		// last round's plan is now the opponent's visible army
		const oppView = room.snapshotFor(1).opponent!;
		expect(oppView.cards.length).toBeGreaterThan(0);
	});
});

describe("MatchRoom — a full match to HP zero", () => {
	test("repeated rounds drive the loser's HP to zero and end the match", () => {
		const room = newRoom();
		startMatch(room);

		// seat 0 grows its army each round (fresh coords, since cards persist), so
		// its invested-value lead — and the per-round survivor damage — widens and
		// seat 1 bleeds out. seat 1 idles. Rows chosen clear of the back-line
		// buildings; ballista is unlocked once and spammed with the round's income.
		let guard = 0;
		let placed = 0;
		while (room.currentPhase !== "matchEnded" && guard < 200) {
			guard += 1;
			if (room.currentPhase === "planning") {
				room.unlockUnit("p0", "ballista");
				// spend seat 0's whole allowance on new ballistae at fresh spots
				for (let d = 0; d < 3; d++) {
					const row = (placed % 10) * 3;
					const col = 6 + Math.floor(placed / 10) * 5;
					const r = room.buySquad("p0", "ballista", row, col, "north");
					if (r.ok) placed += 1;
				}
				room.setReady("p0", true, 0);
				room.setReady("p1", true, 0);
			} else if (room.currentPhase === "battle") {
				room.battleAck("p0", 0);
				room.battleAck("p1", 0);
			} else if (room.currentPhase === "results") {
				room.tick(1_000_000);
			}
		}

		expect(room.currentPhase).toBe("matchEnded");
		expect(room.winner).toBe(0);
		const finalHp = room.finalHp();
		expect(finalHp.find((h) => h.seat === 1)!.hp).toBe(0);
		expect(finalHp.find((h) => h.seat === 0)!.hp).toBeGreaterThan(0);
	});

	test("plays entirely via deadline ticks with no explicit acks", () => {
		const room = newRoom();
		room.join("p0", "Alice", 0);
		room.join("p1", "Bob", 0);
		// never pick a commander → commanderPick deadline auto-picks
		let now = 0;
		let guard = 0;
		while (room.currentPhase !== "matchEnded" && guard < 500) {
			guard += 1;
			now += 200_000; // advance well past any deadline
			if (room.currentPhase === "planning") {
				// give seat 0 an edge each round
				room.buySquad("p0", "footman", 0, 10, "north");
			}
			room.tick(now);
		}
		// With no purchases from seat 1 and seat 0 buying, seat 0 wins eventually.
		expect(room.currentPhase).toBe("matchEnded");
	});
});

describe("MatchRoom — reconnect", () => {
	test("reconnecting with a resume token restores the seat and its state", () => {
		const room = newRoom();
		startMatch(room);
		expect(room.buySquad("p0", "footman", 0, 10, "north").ok).toBe(true);
		const token = `tok-m-0`; // deterministic token for seat 0
		expect(room.seatByResumeToken(token)).toBe(0 as Seat);

		room.disconnect("p0");
		expect(room.snapshotFor(0).own.connected).toBe(false);

		const rejoin = room.join("p0b", "Alice", 0, token);
		expect(rejoin).not.toBeNull();
		expect(rejoin!.seat).toBe(0 as Seat);
		const own = room.snapshotFor(0).own;
		expect(own.connected).toBe(true);
		// state retained across reconnect
		expect(own.cards.some((c) => c.unitId === "footman")).toBe(true);
	});
});
