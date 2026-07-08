import {
	type ClientMessage,
	type ServerMessage,
	parseServerMessage,
} from "@core/types";
import { afterEach, describe, expect, test } from "bun:test";
import {
	DEFAULT_PORT,
	type GameServer,
	createGameServer,
	portFromEnv,
} from "./server";

const servers: GameServer[] = [];

function startServer(): GameServer {
	const gs = createGameServer({ port: 0 });
	servers.push(gs);
	return gs;
}

afterEach(() => {
	for (const gs of servers.splice(0)) gs.stop();
});

/** Small promise-based WebSocket client for driving the server in tests. */
class TestClient {
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

	static async connect(gs: GameServer): Promise<TestClient> {
		const ws = new WebSocket(`ws://localhost:${gs.server.port}/ws`);
		await new Promise<void>((resolve, reject) => {
			ws.onopen = () => resolve();
			ws.onerror = () => reject(new Error("failed to connect"));
		});
		return new TestClient(ws);
	}

	send(message: ClientMessage): void {
		this.ws.send(JSON.stringify(message));
	}

	sendRaw(raw: string): void {
		this.ws.send(raw);
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

	async expectNoMessage(windowMs = 150): Promise<void> {
		await new Promise((resolve) => setTimeout(resolve, windowMs));
		expect(this.queue).toHaveLength(0);
	}

	close(): void {
		this.ws.close();
	}
}

async function joinedClient(
	gs: GameServer,
	roomId: string,
	playerName: string,
): Promise<{ client: TestClient; playerId: string }> {
	const client = await TestClient.connect(gs);
	client.send({ type: "join", roomId, playerName });
	const welcome = await client.next();
	if (welcome.type !== "welcome") {
		throw new Error(`expected welcome, got ${welcome.type}`);
	}
	return { client, playerId: welcome.playerId };
}

describe("portFromEnv", () => {
	function withPort(value: string | undefined, fn: () => void): void {
		const saved = process.env.PORT;
		if (value === undefined) delete process.env.PORT;
		else process.env.PORT = value;
		try {
			fn();
		} finally {
			if (saved === undefined) delete process.env.PORT;
			else process.env.PORT = saved;
		}
	}

	test("falls back to the default when PORT is unset or empty", () => {
		withPort(undefined, () => expect(portFromEnv()).toBe(DEFAULT_PORT));
		withPort("", () => expect(portFromEnv()).toBe(DEFAULT_PORT));
	});

	test("parses a valid PORT", () => {
		withPort("8080", () => expect(portFromEnv()).toBe(8080));
	});

	test("rejects garbage instead of silently binding a random port", () => {
		for (const bad of ["abc", "0", "-1", "70000", "12.5"]) {
			withPort(bad, () => expect(() => portFromEnv()).toThrow("invalid PORT"));
		}
	});
});

describe("game server", () => {
	test("GET /health reports rooms and players", async () => {
		const gs = startServer();
		const before = await (
			await fetch(`http://localhost:${gs.server.port}/health`)
		).json();
		expect(before).toEqual({ status: "ok", rooms: 0, players: 0 });

		await joinedClient(gs, "lobby", "Jeev");
		const after = await (
			await fetch(`http://localhost:${gs.server.port}/health`)
		).json();
		expect(after).toEqual({ status: "ok", rooms: 1, players: 1 });
	});

	test("join returns a welcome carrying the authoritative state", async () => {
		const gs = startServer();
		const client = await TestClient.connect(gs);
		client.send({ type: "join", roomId: "lobby", playerName: "Jeev" });
		const welcome = await client.next();
		expect(welcome.type).toBe("welcome");
		if (welcome.type === "welcome") {
			expect(welcome.roomId).toBe("lobby");
			expect(welcome.state.board).toEqual({ width: 72, height: 60 });
			expect(welcome.state.players).toHaveLength(1);
			expect(welcome.state.units).toHaveLength(0);
		}
	});

	test("a second join in the same room notifies the first player", async () => {
		const gs = startServer();
		const { client: first } = await joinedClient(gs, "lobby", "One");
		await joinedClient(gs, "lobby", "Two");
		const update = await first.next();
		expect(update.type).toBe("state");
		if (update.type === "state") {
			expect(update.state.players.map((p) => p.name).sort()).toEqual([
				"One",
				"Two",
			]);
		}
	});

	test("placing a unit broadcasts the new state to everyone in the room", async () => {
		const gs = startServer();
		const { client: first, playerId } = await joinedClient(gs, "lobby", "One");
		const { client: second } = await joinedClient(gs, "lobby", "Two");
		await first.next(); // state from Two's join

		first.send({ type: "placeUnit", unitType: "archer", row: 10, col: 12 });
		for (const client of [first, second]) {
			const update = await client.next();
			expect(update.type).toBe("state");
			if (update.type === "state") {
				expect(update.state.units).toHaveLength(1);
				expect(update.state.units[0]).toMatchObject({
					ownerId: playerId,
					unitType: "archer",
					row: 10,
					col: 12,
				});
			}
		}
	});

	test("rejected actions error only to the sender", async () => {
		const gs = startServer();
		const { client: first } = await joinedClient(gs, "lobby", "One");
		const { client: second } = await joinedClient(gs, "lobby", "Two");
		await first.next(); // state from Two's join

		first.send({ type: "placeUnit", unitType: "archer", row: 3, col: 3 });
		await first.next(); // state broadcast
		await second.next(); // state broadcast

		second.send({ type: "placeUnit", unitType: "whelp", row: 3, col: 3 });
		const error = await second.next();
		expect(error).toMatchObject({ type: "error", code: "tileOccupied" });
		await first.expectNoMessage();
	});

	test("game actions before joining are rejected", async () => {
		const gs = startServer();
		const client = await TestClient.connect(gs);
		client.send({ type: "placeUnit", unitType: "archer", row: 0, col: 0 });
		const error = await client.next();
		expect(error).toMatchObject({ type: "error", code: "notJoined" });
	});

	test("joining twice is rejected", async () => {
		const gs = startServer();
		const { client } = await joinedClient(gs, "lobby", "One");
		client.send({ type: "join", roomId: "other", playerName: "One" });
		const error = await client.next();
		expect(error).toMatchObject({ type: "error", code: "alreadyJoined" });
	});

	test("malformed frames get a badMessage error", async () => {
		const gs = startServer();
		const client = await TestClient.connect(gs);
		client.sendRaw("this is not json");
		const error = await client.next();
		expect(error).toMatchObject({ type: "error", code: "badMessage" });
	});

	test("ping gets a pong", async () => {
		const gs = startServer();
		const { client } = await joinedClient(gs, "lobby", "One");
		client.send({ type: "ping" });
		const pong = await client.next();
		expect(pong.type).toBe("pong");
	});

	test("disconnecting removes the player and their units from the room", async () => {
		const gs = startServer();
		const { client: first } = await joinedClient(gs, "lobby", "One");
		const { client: second } = await joinedClient(gs, "lobby", "Two");
		await first.next(); // state from Two's join

		second.send({ type: "placeUnit", unitType: "whelp", row: 7, col: 8 });
		await first.next(); // state broadcast
		await second.next(); // state broadcast

		second.close();
		const update = await first.next();
		expect(update.type).toBe("state");
		if (update.type === "state") {
			expect(update.state.players.map((p) => p.name)).toEqual(["One"]);
			expect(update.state.units).toHaveLength(0);
		}
	});

	test("rooms are torn down when the last player leaves", async () => {
		const gs = startServer();
		const { client } = await joinedClient(gs, "solo", "One");
		expect(gs.rooms.size).toBe(1);
		client.close();
		await new Promise((resolve) => setTimeout(resolve, 100));
		expect(gs.rooms.size).toBe(0);
	});

	test("rooms are isolated from each other", async () => {
		const gs = startServer();
		const { client: a } = await joinedClient(gs, "room-a", "Alice");
		const { client: b } = await joinedClient(gs, "room-b", "Bob");
		a.send({ type: "placeUnit", unitType: "archer", row: 0, col: 0 });
		await a.next(); // state broadcast in room-a
		await b.expectNoMessage();
	});
});
