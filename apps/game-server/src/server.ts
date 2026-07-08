import { type ServerMessage, parseClientMessage } from "@core/types";
import type { Server, ServerWebSocket } from "bun";
import { Room } from "./room";

export type SocketData = {
	playerId: string;
	roomId: string | null;
};

type GameSocket = ServerWebSocket<SocketData>;

export type GameServer = {
	server: Server<SocketData>;
	rooms: ReadonlyMap<string, Room>;
	stop: () => void;
};

function send(ws: GameSocket, message: ServerMessage): void {
	ws.send(JSON.stringify(message));
}

export const DEFAULT_PORT = 7777;

/**
 * Resolves the listen port from the PORT env var, failing loudly on garbage
 * (Number("abc") is NaN and Number("") is 0 — Bun.serve silently binds a
 * random ephemeral port for both instead of erroring).
 */
export function portFromEnv(): number {
	const raw = process.env.PORT;
	if (raw === undefined || raw === "") return DEFAULT_PORT;
	const parsed = Number(raw);
	if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) {
		throw new Error(
			`invalid PORT ${JSON.stringify(raw)} — expected an integer between 1 and 65535`,
		);
	}
	return parsed;
}

/**
 * Starts the authoritative game server.
 *
 * HTTP:
 * - GET /health — liveness probe with room/player counts.
 * - GET /ws     — WebSocket upgrade; clients then speak the @core/types
 *                 protocol (join first, then placeUnit/moveUnit/ping).
 *
 * Pass `port: 0` to bind an ephemeral port (used by tests).
 */
export function createGameServer(options: { port?: number } = {}): GameServer {
	const rooms = new Map<string, Room>();

	const topic = (roomId: string) => `room:${roomId}`;

	const broadcastState = (roomId: string) => {
		const room = rooms.get(roomId);
		if (!room) return;
		const message: ServerMessage = { type: "state", state: room.getState() };
		server.publish(topic(roomId), JSON.stringify(message));
	};

	const server = Bun.serve<SocketData>({
		port: options.port ?? portFromEnv(),
		fetch(req, server) {
			const url = new URL(req.url);
			if (url.pathname === "/health") {
				let players = 0;
				for (const room of rooms.values())
					players += room.getState().players.length;
				return Response.json({ status: "ok", rooms: rooms.size, players });
			}
			if (url.pathname === "/ws") {
				const upgraded = server.upgrade(req, {
					data: { playerId: crypto.randomUUID(), roomId: null },
				});
				if (upgraded) return undefined;
				return new Response("expected a WebSocket upgrade", { status: 400 });
			}
			return new Response("boardgame game-server — connect via /ws", {
				status: url.pathname === "/" ? 200 : 404,
			});
		},
		websocket: {
			message(ws: GameSocket, raw) {
				if (typeof raw !== "string") {
					send(ws, {
						type: "error",
						code: "badMessage",
						message: "expected a JSON text frame",
					});
					return;
				}
				const parsed = parseClientMessage(raw);
				if (!parsed.ok) {
					send(ws, {
						type: "error",
						code: "badMessage",
						message: parsed.error,
					});
					return;
				}
				const message = parsed.message;

				if (message.type === "ping") {
					send(ws, { type: "pong" });
					return;
				}

				if (message.type === "join") {
					if (ws.data.roomId !== null) {
						send(ws, {
							type: "error",
							code: "alreadyJoined",
							message: `already joined room ${ws.data.roomId}`,
						});
						return;
					}
					let room = rooms.get(message.roomId);
					if (!room) {
						room = new Room(message.roomId);
						rooms.set(message.roomId, room);
					}
					room.addPlayer(ws.data.playerId, message.playerName);
					ws.data.roomId = message.roomId;
					ws.subscribe(topic(message.roomId));
					send(ws, {
						type: "welcome",
						playerId: ws.data.playerId,
						roomId: message.roomId,
						state: room.getState(),
					});
					// Notify everyone already in the room (excludes this socket).
					ws.publish(
						topic(message.roomId),
						JSON.stringify({ type: "state", state: room.getState() }),
					);
					return;
				}

				// Everything below requires having joined a room.
				const roomId = ws.data.roomId;
				const room = roomId !== null ? rooms.get(roomId) : undefined;
				if (roomId === null || !room) {
					send(ws, {
						type: "error",
						code: "notJoined",
						message: "join a room before sending game actions",
					});
					return;
				}

				const result =
					message.type === "placeUnit"
						? room.placeUnit(
								ws.data.playerId,
								message.unitType,
								message.row,
								message.col,
							)
						: room.moveUnit(
								ws.data.playerId,
								message.unitId,
								message.row,
								message.col,
							);

				if (!result.ok) {
					send(ws, {
						type: "error",
						code: result.code,
						message: result.message,
					});
					return;
				}
				broadcastState(roomId);
			},
			close(ws: GameSocket) {
				const roomId = ws.data.roomId;
				if (roomId === null) return;
				const room = rooms.get(roomId);
				if (!room) return;
				room.removePlayer(ws.data.playerId);
				if (room.isEmpty) {
					rooms.delete(roomId);
				} else {
					broadcastState(roomId);
				}
			},
		},
	});

	return {
		server,
		rooms,
		stop: () => server.stop(true),
	};
}
