using System;
using System.Collections.Concurrent;
using BoardGame.Core.Catalog;
using BoardGame.Core.Match;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BoardGame.BattleServer
{
    /// <summary>Persists room state at phase transitions so a restart resumes.</summary>
    public interface IRoomStore
    {
        void Save(string roomId, MatchRoom room);
        MatchRoom? TryLoad(string roomId, LoadedCatalog catalog, Func<string> tokenGen);
    }

    /// <summary>In-memory store for tests (still exercises capture/restore).</summary>
    public sealed class InMemoryRoomStore : IRoomStore
    {
        private readonly ConcurrentDictionary<string, string> _rows = new();

        public void Save(string roomId, MatchRoom room)
            => _rows[roomId] = JsonConvert.SerializeObject(room.CaptureState());

        public MatchRoom? TryLoad(string roomId, LoadedCatalog catalog, Func<string> tokenGen)
        {
            if (!_rows.TryGetValue(roomId, out var json)) return null;
            return Rehydrate(json, catalog, tokenGen);
        }

        internal static MatchRoom? Rehydrate(string json, LoadedCatalog catalog, Func<string> tokenGen)
        {
            var snap = JsonConvert.DeserializeObject<MatchRoomSnapshot>(json);
            if (snap == null) return null;
            var room = new MatchRoom(snap.Id, catalog, _ => tokenGen());
            room.RestoreState(snap);
            return room;
        }
    }

    /// <summary>
    /// SQLite-backed store: one row per room (the full snapshot as JSON), upserted
    /// at every phase transition / accepted command — restart-safe. A separate
    /// archive table records finished matches.
    /// </summary>
    public sealed class SqliteRoomStore : IRoomStore, IDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly object _lock = new();

        public SqliteRoomStore(string connectionString)
        {
            _conn = new SqliteConnection(connectionString);
            _conn.Open();
            Exec("CREATE TABLE IF NOT EXISTS rooms (roomId TEXT PRIMARY KEY, snapshot TEXT NOT NULL, updatedAt INTEGER NOT NULL);");
            Exec("CREATE TABLE IF NOT EXISTS matches (roomId TEXT, snapshot TEXT NOT NULL, endedAt INTEGER NOT NULL);");
        }

        public void Save(string roomId, MatchRoom room)
        {
            var json = JsonConvert.SerializeObject(room.CaptureState());
            lock (_lock)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "INSERT INTO rooms(roomId, snapshot, updatedAt) VALUES ($id, $snap, $ts) " +
                                  "ON CONFLICT(roomId) DO UPDATE SET snapshot=$snap, updatedAt=$ts;";
                cmd.Parameters.AddWithValue("$id", roomId);
                cmd.Parameters.AddWithValue("$snap", json);
                cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cmd.ExecuteNonQuery();

                if (room.CurrentPhase == Phase.MatchEnded)
                {
                    using var arch = _conn.CreateCommand();
                    arch.CommandText = "INSERT INTO matches(roomId, snapshot, endedAt) VALUES ($id, $snap, $ts);";
                    arch.Parameters.AddWithValue("$id", roomId);
                    arch.Parameters.AddWithValue("$snap", json);
                    arch.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    arch.ExecuteNonQuery();
                }
            }
        }

        public MatchRoom? TryLoad(string roomId, LoadedCatalog catalog, Func<string> tokenGen)
        {
            lock (_lock)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT snapshot FROM rooms WHERE roomId = $id;";
                cmd.Parameters.AddWithValue("$id", roomId);
                var json = cmd.ExecuteScalar() as string;
                if (json == null) return null;
                return InMemoryRoomStore.Rehydrate(json, catalog, tokenGen);
            }
        }

        private void Exec(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _conn.Dispose();
    }
}
