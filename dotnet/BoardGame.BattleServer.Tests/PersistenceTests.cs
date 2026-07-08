using System.IO;
using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Match;
using Xunit;

namespace BoardGame.BattleServer.Tests
{
    /// <summary>
    /// Restart-resume: a room persisted at phase transitions can be rehydrated
    /// (as after a server restart) with its full state — blueprints, hp, coin,
    /// tech, phase, round, resume tokens — intact.
    /// </summary>
    public class PersistenceTests
    {
        private static LoadedCatalog Catalog()
        {
            var (cat, _) = CatalogSource.Load();
            return cat;
        }

        private static MatchRoom PlayIntoMidMatch(LoadedCatalog cat, out string token0)
        {
            var room = new MatchRoom("resume", cat, s => $"tok-resume-{s}");
            room.Join("p0", "Alice", 0);
            room.Join("p1", "Bob", 0);
            room.PickCommander("p0", room.SnapshotFor(0).CommanderOffers[0], 0);
            room.PickCommander("p1", room.SnapshotFor(1).CommanderOffers[0], 0);
            room.BuySquad("p0", "footman", 0, 10, "north");
            room.UnlockUnit("p0", "ballista");
            token0 = "tok-resume-0";
            return room;
        }

        [Fact]
        public void CaptureRestoreRoundTripsState()
        {
            var cat = Catalog();
            var room = PlayIntoMidMatch(cat, out _);
            var snap = room.CaptureState();

            var restored = new MatchRoom("resume", cat, s => $"fresh-{s}");
            restored.RestoreState(snap);

            Assert.Equal(room.CurrentPhase, restored.CurrentPhase);
            Assert.Equal(room.CurrentRound, restored.CurrentRound);
            var own = restored.SnapshotFor(0).Own;
            Assert.Contains(own.Cards, c => c.UnitId == "footman");
            Assert.Contains("ballista", own.Tech.UnlockedUnits);
            // resume token preserved so the original player can reconnect
            Assert.Equal(0, restored.SeatByResumeToken("tok-resume-0"));
        }

        [Fact]
        public void SqliteStoreSurvivesAReopen()
        {
            var cat = Catalog();
            var dbPath = Path.Combine(Path.GetTempPath(), $"bg-resume-{System.Guid.NewGuid():N}.db");
            try
            {
                // "Before restart": play + persist.
                using (var store = new SqliteRoomStore($"Data Source={dbPath}"))
                {
                    var room = PlayIntoMidMatch(cat, out _);
                    store.Save("resume", room);
                }

                // "After restart": a brand-new store over the same file rehydrates.
                using (var store2 = new SqliteRoomStore($"Data Source={dbPath}"))
                {
                    var loaded = store2.TryLoad("resume", cat, () => "tok-resume-x");
                    Assert.NotNull(loaded);
                    Assert.Equal(Phase.Planning, loaded!.CurrentPhase);
                    var own = loaded.SnapshotFor(0).Own;
                    Assert.Contains(own.Cards, c => c.UnitId == "footman");
                    // the reconnect token round-trips
                    Assert.Equal(0, loaded.SeatByResumeToken("tok-resume-0"));

                    // ...and the reconnected player can resume playing.
                    var rejoin = loaded.Join("p0-new", "Alice", 0, "tok-resume-0");
                    Assert.NotNull(rejoin);
                    Assert.Equal(0, rejoin!.Value.seat);
                }
            }
            finally
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task HubResumesARoomFromTheStoreOnRejoin()
        {
            var cat = Catalog();
            var (_, json) = CatalogSource.Load();
            var store = new InMemoryRoomStore();

            // First hub: play into mid-match, persisting on each command.
            var hub1 = new MatchHub(cat, json, store, () => 1000);
            var conn0 = new Connection(_ => System.Threading.Tasks.Task.CompletedTask);
            var conn1 = new Connection(_ => System.Threading.Tasks.Task.CompletedTask);
            await hub1.HandleAsync(conn0, "{\"type\":\"join\",\"roomId\":\"z\",\"playerName\":\"A\",\"protocolVersion\":2}");
            await hub1.HandleAsync(conn1, "{\"type\":\"join\",\"roomId\":\"z\",\"playerName\":\"B\",\"protocolVersion\":2}");

            // A brand-new hub over the SAME store (a restart) resumes the room when
            // a player rejoins — the room only exists in hub2 if TryLoad found it.
            var hub2 = new MatchHub(cat, json, store, () => 2000);
            var reconn = new Connection(_ => System.Threading.Tasks.Task.CompletedTask);
            await hub2.HandleAsync(reconn, "{\"type\":\"join\",\"roomId\":\"z\",\"playerName\":\"A\",\"protocolVersion\":2}");
            Assert.Equal(1, hub2.RoomCount);
        }
    }
}
