import { loadEmbeddedCatalog } from "@core/catalog/embedded";
import {
	PROTOCOL_VERSION,
	type Seat,
	type ServerMessageV2,
	parseClientMessageV2,
} from "@core/types";
import type { Server, ServerWebSocket } from "bun";
import { type ServerCatalog, indexCatalog } from "./catalog";
import { type CommandOutcome, MatchRoom } from "./matchRoom";

/**
 * Match server — protocol V2 over WebSocket, driving the MatchRoom reducer
 * (design doc §8). The interim Bun implementation (C1); the C# BattleServer
 * (M5) ports the same reducer + protocol. Battle resolution is the stub until
 * the sim lands (M4/M5), so `battleStarted.hasBattleLog` is false and no
 * `battleLog` is emitted.
 *
 * Deadlines: a single interval drives every room's `tick(now)`, so the phase
 * machine advances even when a client never sends `setReady`/`battleAck`.
 */

export type MatchSocketData = {
	playerId: string;
	roomId: string | null;
	seat: Seat | null;
};

type MatchSocket = ServerWebSocket<MatchSocketData>;

export type MatchServer = {
	server: Server<MatchSocketData>;
	rooms: ReadonlyMap<string, MatchRoom>;
	catalog: ServerCatalog;
	stop: () => void;
};

export const DEFAULT_MATCH_PORT = 7778;

export function matchPortFromEnv(): number {
	const raw = process.env.MATCH_PORT;
	if (raw === undefined || raw === "") return DEFAULT_MATCH_PORT;
	const parsed = Number(raw);
	if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) {
		throw new Error(
			`invalid MATCH_PORT ${JSON.stringify(raw)} — expected an integer between 1 and 65535`,
		);
	}
	return parsed;
}

export function createMatchServer(
	options: {
		port?: number;
		catalog?: ServerCatalog;
		catalogJson?: string;
		/** Injected clock for deterministic tests (defaults to Date.now). */
		now?: () => number;
		/** Interval ms for the deadline ticker (0 disables it, for tests). */
		tickMs?: number;
	} = {},
): MatchServer {
	const { catalog, canonicalJson } = (() => {
		if (options.catalog && options.catalogJson) {
			return { catalog: options.catalog, canonicalJson: options.catalogJson };
		}
		const embedded = loadEmbeddedCatalog();
		return {
			catalog: options.catalog ?? indexCatalog(embedded.catalog, embedded.hash),
			canonicalJson: options.catalogJson ?? embedded.canonicalJson,
		};
	})();

	const now = options.now ?? (() => Date.now());
	const rooms = new Map<string, MatchRoom>();
	// playerId → socket, so a seat's private view goes only to its own client.
	const socketsByPlayer = new Map<string, MatchSocket>();
	let tokenCounter = 0;

	const topic = (roomId: string) => `match:${roomId}`;

	const send = (ws: MatchSocket, message: ServerMessageV2) =>
		ws.send(JSON.stringify(message));

	const sendToSeat = (roomId: string, seat: Seat, message: ServerMessageV2) => {
		const room = rooms.get(roomId);
		if (!room) return;
		// find the player occupying that seat
		for (const [playerId, sock] of socketsByPlayer) {
			if (sock.data.roomId === roomId && sock.data.seat === seat) {
				send(sock, message);
			}
			void playerId;
		}
	};

	/** Send each seat its own private phase snapshot. */
	const broadcastPhase = (roomId: string) => {
		const room = rooms.get(roomId);
		if (!room) return;
		for (const seat of [0, 1] as Seat[]) {
			sendToSeat(roomId, seat, {
				type: "phase",
				match: room.snapshotFor(seat),
			});
		}
	};

	// Track phase per room to detect transitions the ticker causes.
	const lastPhase = new Map<string, string>();

	const reactToTransitions = (roomId: string) => {
		const room = rooms.get(roomId);
		if (!room) return;
		const phase = room.currentPhase;
		const prev = lastPhase.get(roomId);
		if (phase === prev) return;
		lastPhase.set(roomId, phase);

		if (phase === "battle") {
			// Reveal + battleStarted (stub: no battleLog).
			const reveal: ServerMessageV2 = {
				type: "revealSnapshot",
				round: room.currentRound,
				armies: room.revealArmies(),
			};
			server.publish(topic(roomId), JSON.stringify(reveal));
			const started: ServerMessageV2 = {
				type: "battleStarted",
				round: room.currentRound,
				startAtServerMs: now(),
				hasBattleLog: false,
			};
			server.publish(topic(roomId), JSON.stringify(started));
		} else if (phase === "results") {
			const result = room.lastRoundResult();
			if (result) {
				const msg: ServerMessageV2 = {
					type: "roundResult",
					round: result.round,
					winnerSeat: result.winnerSeat,
					hpDamage: result.hpDamage,
					hp: result.hp,
					summary: describeResult(result.winnerSeat),
				};
				server.publish(topic(roomId), JSON.stringify(msg));
			}
		} else if (phase === "matchEnded") {
			const msg: ServerMessageV2 = {
				type: "matchEnded",
				winnerSeat: room.winner,
				finalHp: room.finalHp(),
			};
			server.publish(topic(roomId), JSON.stringify(msg));
		}
		broadcastPhase(roomId);
	};

	const server = Bun.serve<MatchSocketData>({
		port: options.port ?? matchPortFromEnv(),
		fetch(req, server) {
			const url = new URL(req.url);
			if (url.pathname === "/health") {
				let players = 0;
				for (const s of socketsByPlayer.values())
					if (s.data.roomId) players += 1;
				return Response.json({
					status: "ok",
					protocolVersion: PROTOCOL_VERSION,
					rooms: rooms.size,
					players,
					catalogHash: catalog.hash,
					board: catalog.board,
				});
			}
			if (url.pathname === "/ws") {
				const upgraded = server.upgrade(req, {
					data: { playerId: crypto.randomUUID(), roomId: null, seat: null },
				});
				if (upgraded) return undefined;
				return new Response("expected a WebSocket upgrade", { status: 400 });
			}
			return new Response("boardgame match-server — connect via /ws", {
				status: url.pathname === "/" ? 200 : 404,
			});
		},
		websocket: {
			message(ws: MatchSocket, raw) {
				if (typeof raw !== "string") {
					send(ws, {
						type: "error",
						code: "badMessage",
						message: "expected text frame",
					});
					return;
				}
				const parsed = parseClientMessageV2(raw);
				if (!parsed.ok) {
					send(ws, {
						type: "error",
						code: "badMessage",
						message: parsed.error,
					});
					return;
				}
				const msg = parsed.message;

				if (msg.type === "ping") {
					send(ws, { type: "pong" });
					return;
				}

				if (msg.type === "join") {
					handleJoin(ws, msg.roomId, msg.playerName, msg.resumeToken);
					return;
				}

				const roomId = ws.data.roomId;
				const room = roomId !== null ? rooms.get(roomId) : undefined;
				if (roomId === null || !room) {
					send(ws, { type: "error", code: "notJoined", message: "join first" });
					return;
				}
				const playerId = ws.data.playerId;

				if (msg.type === "battleAck") {
					room.battleAck(playerId, now());
					reactToTransitions(roomId);
					return;
				}

				// Planning / commander commands all carry a cmdId.
				const outcome = applyCommand(room, playerId, msg, now());
				if (!outcome.ok) {
					send(ws, {
						type: "cmdRejected",
						cmdId: msg.cmdId,
						code: outcome.code,
						message: outcome.message,
					});
					return;
				}
				const seat = ws.data.seat;
				if (seat !== null) {
					send(ws, {
						type: "cmdAccepted",
						cmdId: msg.cmdId,
						match: room.snapshotFor(seat),
					});
				}
				reactToTransitions(roomId);
			},
			close(ws: MatchSocket) {
				socketsByPlayer.delete(ws.data.playerId);
				const roomId = ws.data.roomId;
				if (roomId === null) return;
				const room = rooms.get(roomId);
				if (!room) return;
				room.disconnect(ws.data.playerId);
				if (room.isEmpty) {
					rooms.delete(roomId);
					lastPhase.delete(roomId);
				} else {
					broadcastPhase(roomId);
				}
			},
		},
	});

	function handleJoin(
		ws: MatchSocket,
		roomId: string,
		playerName: string,
		resumeToken?: string,
	): void {
		if (ws.data.roomId !== null) {
			send(ws, {
				type: "error",
				code: "alreadyJoined",
				message: "already joined",
			});
			return;
		}
		let room = rooms.get(roomId);
		if (!room) {
			room = new MatchRoom(
				roomId,
				catalog,
				() => `rt-${roomId}-${tokenCounter++}`,
			);
			rooms.set(roomId, room);
			lastPhase.set(roomId, room.currentPhase);
		}
		const seated = room.join(ws.data.playerId, playerName, now(), resumeToken);
		if (!seated) {
			send(ws, { type: "error", code: "roomFull", message: "room is full" });
			return;
		}
		ws.data.roomId = roomId;
		ws.data.seat = seated.seat;
		socketsByPlayer.set(ws.data.playerId, ws);
		ws.subscribe(topic(roomId));

		send(ws, {
			type: "welcome",
			seat: seated.seat,
			resumeToken: seated.resumeToken,
			catalogJson: canonicalJson,
			catalogHash: catalog.hash,
			matchConfig: room.matchConfig(),
			match: room.snapshotFor(seated.seat),
		});
		reactToTransitions(roomId);
	}

	// Deadline ticker: advance every room's phase machine on schedule.
	let ticker: ReturnType<typeof setInterval> | null = null;
	const tickMs = options.tickMs ?? 250;
	if (tickMs > 0) {
		ticker = setInterval(() => {
			const t = now();
			for (const [roomId, room] of rooms) {
				if (room.tick(t)) reactToTransitions(roomId);
			}
		}, tickMs);
	}

	return {
		server,
		rooms,
		catalog,
		stop: () => {
			if (ticker) clearInterval(ticker);
			server.stop(true);
		},
	};
}

function applyCommand(
	room: MatchRoom,
	playerId: string,
	msg: Extract<
		ReturnType<typeof parseClientMessageV2>,
		{ ok: true }
	>["message"],
	now: number,
): CommandOutcome {
	switch (msg.type) {
		case "pickCommander":
			return room.pickCommander(playerId, msg.commanderId, now);
		case "buySquad":
			return room.buySquad(
				playerId,
				msg.unitId,
				msg.anchor.row,
				msg.anchor.col,
				msg.orientation,
			);
		case "moveSquad":
			return room.moveSquad(
				playerId,
				msg.cardId,
				msg.anchor.row,
				msg.anchor.col,
				msg.orientation,
			);
		case "sellSquad":
			return room.sellSquad(playerId, msg.cardId);
		case "unlockUnit":
			return room.unlockUnit(playerId, msg.unitId);
		case "buyTech":
			return room.buyTech(playerId, msg.unitId, msg.techId);
		case "buyLevel":
			return room.buyLevel(playerId, msg.cardId);
		case "setReady":
			return room.setReady(playerId, msg.ready, now);
		default:
			return {
				ok: false,
				code: "badMessage",
				message: "not a planning command",
			};
	}
}

function describeResult(winnerSeat: Seat | null): string {
	if (winnerSeat === null) return "draw — both sides take survivor damage";
	return `seat ${winnerSeat} wins the round`;
}
