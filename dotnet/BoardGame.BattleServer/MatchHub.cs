using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Match;

namespace BoardGame.BattleServer
{
    /// <summary>A live connection to one seat of one room.</summary>
    public sealed class Connection
    {
        public string PlayerId { get; } = Guid.NewGuid().ToString();
        public string? RoomId;
        public int? Seat;
        public Func<string, System.Threading.Tasks.Task> Send { get; }
        public Connection(Func<string, System.Threading.Tasks.Task> send) { Send = send; }
    }

    /// <summary>
    /// Owns the room registry and routes protocol-V2 messages to the ported C#
    /// MatchRoom, mirroring apps/game-server/src/matchServer.ts. Rooms are driven
    /// by a deadline ticker (Tick) and by client acks; each seat gets its own
    /// private snapshot. At plan-lock the MatchRoom runs the Core sim and this hub
    /// ships battleStarted + battleLog + roundResult. Persistence is delegated to
    /// an IRoomStore so a restart can resume mid-match.
    /// </summary>
    public sealed class MatchHub
    {
        private readonly LoadedCatalog _catalog;
        private readonly string _catalogJson;
        private readonly IRoomStore _store;
        private readonly Func<long> _now;

        private readonly ConcurrentDictionary<string, MatchRoom> _rooms = new();
        private readonly ConcurrentDictionary<string, string> _lastPhase = new();
        // roomId → seat → connection
        private readonly ConcurrentDictionary<string, Connection?[]> _seatConns = new();
        // Per-room lock: all mutation of a room's state (commands + ticks) is
        // serialized so two connections' commands can never interleave (the
        // single-threaded-per-room actor model — no PlanLock double-fire race).
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _roomLocks = new();
        private int _tokenCounter;

        private SemaphoreSlim LockFor(string roomId)
            => _roomLocks.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));

        public MatchHub(LoadedCatalog catalog, string catalogJson, IRoomStore store, Func<long>? now = null)
        {
            _catalog = catalog;
            _catalogJson = catalogJson;
            _store = store;
            _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public string CatalogHash => _catalog.Hash;
        public int RoomCount => _rooms.Count;

        public async System.Threading.Tasks.Task HandleAsync(Connection conn, string raw)
        {
            var msg = ClientMessageV2.Parse(raw);
            if (msg == null) { await conn.Send(J(ServerMsg.Error("badMessage", "unparseable"))); return; }

            switch (msg)
            {
                case PingV2Msg:
                    await conn.Send(J(ServerMsg.Pong()));
                    return;
                case JoinV2 join:
                    await HandleJoin(conn, join);
                    return;
            }

            if (conn.RoomId == null || !_rooms.TryGetValue(conn.RoomId, out var room))
            {
                await conn.Send(J(ServerMsg.Error("notJoined", "join first")));
                return;
            }

            // Serialize all state mutation for this room (single-threaded actor).
            var gate = LockFor(conn.RoomId);
            await gate.WaitAsync();
            try
            {
                if (msg is BattleAckMsg)
                {
                    room.BattleAck(conn.PlayerId, _now());
                    await ReactToTransitions(conn.RoomId, room);
                    return;
                }

                var (outcome, cmdId) = ApplyCommand(room, conn.PlayerId, msg);
                if (!outcome.Ok)
                {
                    await conn.Send(J(ServerMsg.CmdRejected(cmdId, outcome.Code, outcome.Message)));
                    return;
                }
                if (conn.Seat is int seat)
                    await conn.Send(J(ServerMsg.CmdAccepted(cmdId, room.SnapshotFor(seat))));
                _store.Save(conn.RoomId, room);
                await ReactToTransitions(conn.RoomId, room);
            }
            finally { gate.Release(); }
        }

        private async System.Threading.Tasks.Task HandleJoin(Connection conn, JoinV2 join)
        {
            if (conn.RoomId != null) { await conn.Send(J(ServerMsg.Error("alreadyJoined", "already joined"))); return; }
            var room = _rooms.GetOrAdd(join.RoomId, id =>
            {
                var loaded = _store.TryLoad(id, _catalog, () => $"rt-{id}-{_tokenCounter++}");
                var r = loaded ?? new MatchRoom(id, _catalog, _ => $"rt-{id}-{_tokenCounter++}");
                _lastPhase[id] = r.CurrentPhase.ToString();
                _seatConns[id] = new Connection?[2];
                return r;
            });

            var gate = LockFor(join.RoomId);
            await gate.WaitAsync();
            try
            {
                var seated = room.Join(conn.PlayerId, join.PlayerName, _now(), join.ResumeToken);
                if (seated == null) { await conn.Send(J(ServerMsg.Error("roomFull", "room is full"))); return; }

                conn.RoomId = join.RoomId;
                conn.Seat = seated.Value.seat;
                _seatConns[join.RoomId][seated.Value.seat] = conn;

                await conn.Send(J(ServerMsg.Welcome(
                    seated.Value.seat, seated.Value.resumeToken, _catalogJson, _catalog.Hash,
                    room.MatchConfig(), room.SnapshotFor(seated.Value.seat))));
                _store.Save(join.RoomId, room);
                await ReactToTransitions(join.RoomId, room);
            }
            finally { gate.Release(); }
        }

        /// <summary>Drive every room's phase machine (called by the host ticker).</summary>
        public async System.Threading.Tasks.Task TickAll()
        {
            long t = _now();
            foreach (var (roomId, room) in _rooms.Select(kv => (kv.Key, kv.Value)).ToList())
            {
                var gate = LockFor(roomId);
                await gate.WaitAsync();
                try
                {
                    if (room.Tick(t))
                    {
                        _store.Save(roomId, room);
                        await ReactToTransitions(roomId, room);
                    }
                }
                finally { gate.Release(); }
            }
        }

        private async System.Threading.Tasks.Task ReactToTransitions(string roomId, MatchRoom room)
        {
            var phase = room.CurrentPhase.ToString();
            if (_lastPhase.TryGetValue(roomId, out var prev) && prev == phase) return;
            _lastPhase[roomId] = phase;

            if (room.CurrentPhase == Phase.Battle)
            {
                var armies = room.RevealArmies().Select(a => (object)new { seat = a.seat, cards = a.cards });
                await Broadcast(roomId, ServerMsg.RevealSnapshot(room.CurrentRound, armies));
                await Broadcast(roomId, ServerMsg.BattleStarted(room.CurrentRound, _now(), room.LastBattleLog != null));
                if (room.LastBattleLog != null)
                    await Broadcast(roomId, ServerMsg.BattleLog(room.LastBattleRound, room.LastBattleLog));
            }
            else if (room.CurrentPhase == Phase.Results)
            {
                var r = room.LastRoundResult();
                if (r != null)
                {
                    var hpDamage = r.HpDamage.Select(d => (object)new { seat = d.seat, amount = d.amount });
                    var hp = r.Hp.Select(h => (object)new { seat = h.seat, hp = h.hp });
                    await Broadcast(roomId, ServerMsg.RoundResult(r.Round, r.WinnerSeat, hpDamage, hp,
                        r.WinnerSeat == null ? "draw" : $"seat {r.WinnerSeat} wins"));
                }
            }
            else if (room.CurrentPhase == Phase.MatchEnded)
            {
                var finalHp = room.FinalHp().Select(h => (object)new { seat = h.seat, hp = h.hp });
                await Broadcast(roomId, ServerMsg.MatchEnded(room.Winner, finalHp));
            }
            await BroadcastPhase(roomId, room);
        }

        private async System.Threading.Tasks.Task BroadcastPhase(string roomId, MatchRoom room)
        {
            for (int seat = 0; seat < 2; seat++)
            {
                var conn = _seatConns[roomId][seat];
                if (conn != null) await conn.Send(J(ServerMsg.Phase(room.SnapshotFor(seat))));
            }
        }

        private async System.Threading.Tasks.Task Broadcast(string roomId, object message)
        {
            var text = J(message);
            foreach (var conn in _seatConns[roomId])
                if (conn != null) await conn.Send(text);
        }

        public void Disconnect(Connection conn)
        {
            if (conn.RoomId == null) return;
            if (_rooms.TryGetValue(conn.RoomId, out var room))
            {
                room.Disconnect(conn.PlayerId);
                if (conn.Seat is int seat && _seatConns.TryGetValue(conn.RoomId, out var conns))
                    conns[seat] = null;
                if (room.IsEmpty)
                {
                    _rooms.TryRemove(conn.RoomId, out _);
                    _lastPhase.TryRemove(conn.RoomId, out _);
                    _seatConns.TryRemove(conn.RoomId, out _);
                }
            }
        }

        private static (CommandOutcome, string cmdId) ApplyCommand(MatchRoom room, string playerId, ClientMessageV2 msg)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return msg switch
            {
                PickCommanderMsg m => (room.PickCommander(playerId, m.CommanderId, now), m.CmdId),
                BuySquadMsg m => (room.BuySquad(playerId, m.UnitId, m.Anchor.Row, m.Anchor.Col, m.Orientation), m.CmdId),
                MoveSquadMsg m => (room.MoveSquad(playerId, m.CardId, m.Anchor.Row, m.Anchor.Col, m.Orientation), m.CmdId),
                SellSquadMsg m => (room.SellSquad(playerId, m.CardId), m.CmdId),
                UnlockUnitMsg m => (room.UnlockUnit(playerId, m.UnitId), m.CmdId),
                BuyTechMsg m => (room.BuyTech(playerId, m.UnitId, m.TechId), m.CmdId),
                BuyLevelMsg m => (room.BuyLevel(playerId, m.CardId), m.CmdId),
                SetReadyMsg m => (room.SetReady(playerId, m.Ready, now), m.CmdId),
                _ => (CommandOutcome.Fail("badMessage", "not a command"), ""),
            };
        }

        private static string J(object o) => Newtonsoft.Json.JsonConvert.SerializeObject(o);
    }
}
