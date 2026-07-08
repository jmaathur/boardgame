import {
	PROTOCOL_VERSION,
	type ClientMessageV2,
	type ServerMessageV2,
	parseServerMessageV2,
} from "@core/types";
import { afterEach, describe, expect, test } from "bun:test";
import { type MatchServer, createMatchServer } from "./matchServer";

const servers: MatchServer[] = [];

function startServer(): MatchServer {
	// tickMs 0 disables the deadline ticker so tests drive phases explicitly.
	const gs = createMatchServer({ port: 0, tickMs: 0, now: () => 1000 });
	servers.push(gs);
	return gs;
}

afterEach(() => {
	for (const gs of servers.splice(0)) gs.stop();
});

class Client {
	private readonly ws: WebSocket;
	private readonly queue: ServerMessageV2[] = [];
	private readonly waiters: Array<(m: ServerMessageV2) => void> = [];

	private constructor(ws: WebSocket) {
		this.ws = ws;
		ws.onmessage = (e) => {
			const parsed = parseServerMessageV2(String(e.data));
			if (!parsed.ok) throw new Error(`unparseable: ${parsed.error}`);
			const w = this.waiters.shift();
			if (w) w(parsed.message);
			else this.queue.push(parsed.message);
		};
	}

	static async connect(gs: MatchServer): Promise<Client> {
		const ws = new WebSocket(`ws://localhost:${gs.server.port}/ws`);
		await new Promise<void>((resolve, reject) => {
			ws.onopen = () => resolve();
			ws.onerror = () => reject(new Error("connect failed"));
		});
		return new Client(ws);
	}

	send(m: ClientMessageV2): void {
		this.ws.send(JSON.stringify(m));
	}

	next(timeoutMs = 2000): Promise<ServerMessageV2> {
		const q = this.queue.shift();
		if (q) return Promise.resolve(q);
		return new Promise((resolve, reject) => {
			const t = setTimeout(() => reject(new Error("timeout")), timeoutMs);
			this.waiters.push((m) => {
				clearTimeout(t);
				resolve(m);
			});
		});
	}

	/** Drain until a message of the given type arrives (skips others). */
	async until(
		type: ServerMessageV2["type"],
		timeoutMs = 2000,
	): Promise<ServerMessageV2> {
		const deadline = Date.now() + timeoutMs;
		while (Date.now() < deadline) {
			const m = await this.next(timeoutMs);
			if (m.type === type) return m;
		}
		throw new Error(`never saw ${type}`);
	}

	close(): void {
		this.ws.close();
	}
}

async function join(
	gs: MatchServer,
	room: string,
	name: string,
): Promise<{ client: Client; welcome: ServerMessageV2 }> {
	const client = await Client.connect(gs);
	client.send({
		type: "join",
		roomId: room,
		playerName: name,
		protocolVersion: PROTOCOL_VERSION,
	});
	const welcome = await client.next();
	return { client, welcome };
}

describe("match server — protocol v2 transport", () => {
	test("GET /health advertises protocol v2 and the catalog hash", async () => {
		const gs = startServer();
		const health = (await fetch(
			`http://localhost:${gs.server.port}/health`,
		).then((r) => r.json())) as {
			status: string;
			protocolVersion: number;
			catalogHash: string;
		};
		expect(health.status).toBe("ok");
		expect(health.protocolVersion).toBe(PROTOCOL_VERSION);
		expect(health.catalogHash).toBe(gs.catalog.hash);
	});

	test("join returns a welcome with seat, catalog bytes, and match config", async () => {
		const gs = startServer();
		const { welcome } = await join(gs, "r", "Alice");
		expect(welcome.type).toBe("welcome");
		if (welcome.type === "welcome") {
			expect(welcome.seat).toBe(0);
			expect(welcome.catalogHash).toBe(gs.catalog.hash);
			// welcome carries the EXACT catalog bytes
			expect(welcome.catalogJson.length).toBeGreaterThan(100);
			expect(welcome.matchConfig.board).toEqual({ w: 32, h: 48 });
		}
	});

	test("two joins reach commanderPick and a pick is accepted", async () => {
		const gs = startServer();
		const { client: a } = await join(gs, "r", "Alice");
		const { client: b } = await join(gs, "r", "Bob");

		// both should receive a phase update landing in commanderPick
		const aPhase = await a.until("phase");
		expect(aPhase.type === "phase" && aPhase.match.phase).toBe("commanderPick");

		// Alice picks her first offer
		const offers = aPhase.type === "phase" ? aPhase.match.commanderOffers : [];
		a.send({ type: "pickCommander", cmdId: "c1", commanderId: offers[0] });
		const accepted = await a.until("cmdAccepted");
		expect(accepted.type).toBe("cmdAccepted");

		void b;
	});

	test("a full round: pick, buy, ready, reveal, battleStarted, roundResult", async () => {
		const gs = startServer();
		const { client: a } = await join(gs, "r", "Alice");
		const { client: b } = await join(gs, "r", "Bob");

		const aPhase = await a.until("phase");
		const bPhase = await b.until("phase");
		const aOffers = aPhase.type === "phase" ? aPhase.match.commanderOffers : [];
		const bOffers = bPhase.type === "phase" ? bPhase.match.commanderOffers : [];

		a.send({ type: "pickCommander", cmdId: "a1", commanderId: aOffers[0] });
		b.send({ type: "pickCommander", cmdId: "b1", commanderId: bOffers[0] });

		// both land in planning
		const aPlan = await a.until("phase");
		expect(aPlan.type === "phase" && aPlan.match.phase).toBe("planning");

		// Alice out-invests: buy an archer in her half
		a.send({
			type: "buySquad",
			cmdId: "a2",
			unitId: "archer",
			anchor: { row: 0, col: 10 },
			orientation: "north",
		});
		b.send({
			type: "buySquad",
			cmdId: "b2",
			unitId: "footman",
			anchor: { row: 0, col: 30 },
			orientation: "north",
		});
		await a.until("cmdAccepted");
		await b.until("cmdAccepted");

		// both ready → plan-lock → battle
		a.send({ type: "setReady", cmdId: "a3", ready: true });
		b.send({ type: "setReady", cmdId: "b3", ready: true });

		// Alice should see a reveal + battleStarted
		const reveal = await a.until("revealSnapshot");
		expect(reveal.type).toBe("revealSnapshot");
		const started = await a.until("battleStarted");
		expect(started.type === "battleStarted" && started.hasBattleLog).toBe(
			false,
		);

		// ack the battle → roundResult
		a.send({ type: "battleAck" });
		b.send({ type: "battleAck" });
		const result = await a.until("roundResult");
		expect(result.type).toBe("roundResult");
		if (result.type === "roundResult") {
			// Alice (seat 0) invested more (archer 100 vs footman 100 — but she also
			// has commander/starting differences); at minimum a winner or a draw is
			// reported and HP is present.
			expect(result.hp.length).toBe(2);
		}
	});

	test("hidden planning: opponent view never leaks the current plan", async () => {
		const gs = startServer();
		const { client: a } = await join(gs, "r", "Alice");
		const { client: b } = await join(gs, "r", "Bob");
		const aPhase = await a.until("phase");
		const bPhase = await b.until("phase");
		a.send({
			type: "pickCommander",
			cmdId: "a1",
			commanderId:
				aPhase.type === "phase" ? aPhase.match.commanderOffers[0] : "",
		});
		b.send({
			type: "pickCommander",
			cmdId: "b1",
			commanderId:
				bPhase.type === "phase" ? bPhase.match.commanderOffers[0] : "",
		});
		await a.until("phase"); // planning

		a.send({
			type: "buySquad",
			cmdId: "a2",
			unitId: "archer",
			anchor: { row: 0, col: 10 },
			orientation: "north",
		});
		await a.until("cmdAccepted");

		// Bob asks for nothing; his view of Alice must not contain the archer.
		b.send({ type: "ping" });
		// Trigger a fresh phase snapshot for Bob by having Bob act.
		b.send({
			type: "buySquad",
			cmdId: "b2",
			unitId: "footman",
			anchor: { row: 0, col: 30 },
			orientation: "north",
		});
		const bAccept = await b.until("cmdAccepted");
		if (bAccept.type === "cmdAccepted") {
			const opp = bAccept.match.opponent;
			expect(opp?.cards.some((c) => c.unitId === "archer")).toBe(false);
		}
	});

	test("a rejected command returns cmdRejected to the sender", async () => {
		const gs = startServer();
		const { client: a } = await join(gs, "r", "Alice");
		await join(gs, "r", "Bob");
		const aPhase = await a.until("phase");
		// pickCommander with an unoffered id
		a.send({ type: "pickCommander", cmdId: "x", commanderId: "notReal" });
		const rej = await a.until("cmdRejected");
		expect(rej.type === "cmdRejected" && rej.code).toBe("unknownCommander");
		void aPhase;
	});
});
