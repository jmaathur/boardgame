import {
	type ClientMessage,
	type ServerMessage,
	parseServerMessage,
} from "@core/types";
import { afterEach, describe, expect, test } from "bun:test";
import { type GameServer, createGameServer } from "./server";

/**
 * Runtime verification of the TWO-CLIENT SHARED BOARD feature
 * (docs/two-client-shared-board-plan.md §5) at the wire-protocol level.
 *
 * The Unity clients cannot be driven headlessly, but the whole point of the
 * feature is that the board is a *view* of server-authoritative state: two
 * clients in one room place units on one board, every placement is broadcast to
 * everyone, ownership is carried by `ownerId`, and a departing player's units
 * are pruned. This test exercises exactly that path with two real WebSocket
 * clients against the real server, so each acceptance criterion the Unity
 * BoardManager relies on is proven end-to-end here — everything except the
 * pixels, which a human verifies by running two Editor instances (§4).
 */

const servers: GameServer[] = [];

function startServer(): GameServer {
	const gs = createGameServer({ port: 0 });
	servers.push(gs);
	return gs;
}

afterEach(() => {
	for (const gs of servers.splice(0)) gs.stop();
});

/** Promise-based WebSocket client — the same shape a Unity GameServerClient drives. */
class Client {
	private readonly ws: WebSocket;
	private readonly queue: ServerMessage[] = [];
	private readonly waiters: Array<(m: ServerMessage) => void> = [];

	private constructor(ws: WebSocket) {
		this.ws = ws;
		ws.onmessage = (event) => {
			const parsed = parseServerMessage(String(event.data));
			if (!parsed.ok)
				throw new Error(`unparseable server message: ${parsed.error}`);
			const waiter = this.waiters.shift();
			if (waiter) waiter(parsed.message);
			else this.queue.push(parsed.message);
		};
	}

	static async connect(gs: GameServer): Promise<Client> {
		const ws = new WebSocket(`ws://localhost:${gs.server.port}/ws`);
		await new Promise<void>((resolve, reject) => {
			ws.onopen = () => resolve();
			ws.onerror = () => reject(new Error("failed to connect"));
		});
		return new Client(ws);
	}

	send(message: ClientMessage): void {
		this.ws.send(JSON.stringify(message));
	}

	next(timeoutMs = 2000): Promise<ServerMessage> {
		const queued = this.queue.shift();
		if (queued) return Promise.resolve(queued);
		return new Promise((resolve, reject) => {
			const timer = setTimeout(
				() => reject(new Error("timed out waiting for a server message")),
				timeoutMs,
			);
			this.waiters.push((message) => {
				clearTimeout(timer);
				resolve(message);
			});
		});
	}

	/** Await the next `state` broadcast, skipping any interleaved messages. */
	async nextState(timeoutMs = 2000): Promise<
		Extract<ServerMessage, { type: "state" }>
	> {
		const deadline = Date.now() + timeoutMs;
		for (;;) {
			const remaining = Math.max(1, deadline - Date.now());
			const message = await this.next(remaining);
			if (message.type === "state") return message;
		}
	}

	async expectNoMessage(windowMs = 150): Promise<void> {
		await new Promise((resolve) => setTimeout(resolve, windowMs));
		expect(this.queue).toHaveLength(0);
	}

	close(): void {
		this.ws.close();
	}
}

/** Join a room and return the client plus the playerId the server assigned. */
async function join(
	gs: GameServer,
	roomId: string,
	playerName: string,
): Promise<{ client: Client; playerId: string }> {
	const client = await Client.connect(gs);
	client.send({ type: "join", roomId, playerName });
	const welcome = await client.next();
	if (welcome.type !== "welcome") {
		throw new Error(`expected welcome, got ${welcome.type}`);
	}
	return { client, playerId: welcome.playerId };
}

describe("two-client shared board (acceptance criteria §5)", () => {
	test("both clients join the same room and see the identical unit set", async () => {
		const gs = startServer();

		// A joins an empty room.
		const a = await join(gs, "lobby", "A");
		// B joins the same room; the join notifies A with a fresh state and B's
		// own welcome carries the same authoritative state.
		const b = await Client.connect(gs);
		b.send({ type: "join", roomId: "lobby", playerName: "B" });

		const bWelcome = await b.next();
		if (bWelcome.type !== "welcome") {
			throw new Error(`expected welcome for B, got ${bWelcome.type}`);
		}
		const aState = await a.client.nextState();

		// A's broadcast and B's welcome describe the same board + players.
		expect(aState.state.board).toEqual(bWelcome.state.board);
		expect(aState.state.units).toEqual(bWelcome.state.units); // both empty, identically
		expect(aState.state.players.map((p) => p.name).sort()).toEqual(["A", "B"]);

		a.client.close();
		b.close();
	});

	test("A places a unit → B receives it tinted as the opponent (correct ownerId), and vice versa", async () => {
		const gs = startServer();
		const a = await join(gs, "lobby", "A");
		const b = await join(gs, "lobby", "B");
		// Drain the join-notification state A received when B joined.
		await a.client.nextState();

		// A places a footman. Both clients get the broadcast.
		a.client.send({ type: "placeUnit", unitType: "footman", row: 4, col: 4 });
		const aSaw = await a.client.nextState();
		const bSaw = await b.client.nextState();

		// Same authoritative state on both screens — no divergence.
		expect(aSaw.state.units).toEqual(bSaw.state.units);
		expect(aSaw.state.units).toHaveLength(1);
		const footman = aSaw.state.units[0];
		expect(footman.unitType).toBe("footman");
		// The unit is owned by A: A tints it "mine", B tints it "theirs".
		expect(footman.ownerId).toBe(a.playerId);
		expect(footman.ownerId).not.toBe(b.playerId);

		// Now B places one somewhere non-overlapping; symmetric result.
		b.client.send({ type: "placeUnit", unitType: "archer", row: 10, col: 40 });
		const aSaw2 = await a.client.nextState();
		const bSaw2 = await b.client.nextState();
		expect(aSaw2.state.units).toEqual(bSaw2.state.units);
		expect(aSaw2.state.units).toHaveLength(2);
		const archer = aSaw2.state.units.find((u) => u.unitType === "archer");
		expect(archer?.ownerId).toBe(b.playerId);

		a.client.close();
		b.client.close();
	});

	test("the placer's unit appears only after the server confirms (no pre-spawn), and a rejected placement changes nothing on either client", async () => {
		const gs = startServer();
		const a = await join(gs, "lobby", "A");
		const b = await join(gs, "lobby", "B");
		await a.client.nextState(); // drain B's join notification

		// Valid placement: the unit exists only once the broadcast arrives. The
		// server assigns the id (BoardManager keys its render diff off it), so
		// there is nothing the client could have spawned ahead of this.
		a.client.send({ type: "placeUnit", unitType: "footman", row: 4, col: 4 });
		const confirmed = await a.client.nextState();
		expect(confirmed.state.units).toHaveLength(1);
		expect(confirmed.state.units[0].id).toBeTruthy();
		await b.client.nextState(); // B also gets it

		// Rejected placement: overlap the just-placed footman's footprint.
		a.client.send({ type: "placeUnit", unitType: "footman", row: 4, col: 4 });
		const rejection = await a.client.next();
		expect(rejection.type).toBe("error");
		if (rejection.type === "error") {
			expect(rejection.code).toBe("tileOccupied");
		}
		// No state change reaches anyone — the board is unchanged on both screens.
		await a.client.expectNoMessage();
		await b.client.expectNoMessage();

		// Off the catalog board (32x48) but within the wire schema's coordinate
		// range: the frame parses, then the Room rejects it as out-of-bounds.
		// (BoardManager can't even produce such a click — it builds its grid from
		// the 32x48 the server reports — but the server enforces it regardless.)
		a.client.send({ type: "placeUnit", unitType: "footman", row: 30, col: 47 });
		const oob = await a.client.next();
		expect(oob.type).toBe("error");
		if (oob.type === "error") expect(oob.code).toBe("outOfBounds");
		await a.client.expectNoMessage();
		await b.client.expectNoMessage();

		a.client.close();
		b.client.close();
	});

	test("disconnecting one client removes its units from the other's board", async () => {
		const gs = startServer();
		const a = await join(gs, "lobby", "A");
		const b = await join(gs, "lobby", "B");
		await a.client.nextState(); // drain B's join notification

		// A and B each place a unit.
		a.client.send({ type: "placeUnit", unitType: "footman", row: 4, col: 4 });
		await a.client.nextState();
		await b.client.nextState();
		b.client.send({ type: "placeUnit", unitType: "archer", row: 10, col: 40 });
		await a.client.nextState();
		const beforeLeave = await b.client.nextState();
		expect(beforeLeave.state.units).toHaveLength(2);

		// A leaves. The server prunes A's units and broadcasts to B.
		a.client.close();
		const afterLeave = await b.client.nextState();
		expect(afterLeave.state.units).toHaveLength(1);
		expect(afterLeave.state.units[0].ownerId).toBe(b.playerId);
		expect(afterLeave.state.players.map((p) => p.name)).toEqual(["B"]);

		b.client.close();
	});
});
