using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Match;
using Xunit;

namespace BoardGame.Core.Tests
{
    /// <summary>
    /// C# port of apps/game-server/src/matchRoom.test.ts — confirms the ported
    /// reducer behaves identically to the Bun spec (the M5 conformance suite),
    /// now with REAL simulated battles at plan-lock.
    /// </summary>
    public class MatchRoomTests
    {
        private static LoadedCatalog Catalog() => CatalogLoader.Load(CatalogTestData.CanonicalJson());

        private static MatchRoom NewRoom(string id = "m")
            => new MatchRoom(id, Catalog(), seat => $"tok-{id}-{seat}");

        private static void StartMatch(MatchRoom room, long now = 0)
        {
            room.Join("p0", "Alice", now);
            room.Join("p1", "Bob", now);
            Assert.Equal(Phase.CommanderPick, room.CurrentPhase);
            var off0 = room.SnapshotFor(0).CommanderOffers[0];
            var off1 = room.SnapshotFor(1).CommanderOffers[0];
            Assert.True(room.PickCommander("p0", off0, now).Ok);
            Assert.True(room.PickCommander("p1", off1, now).Ok);
            Assert.Equal(Phase.Planning, room.CurrentPhase);
            Assert.Equal(1, room.CurrentRound);
        }

        [Fact]
        public void StaysInLobbyUntilBothConnect()
        {
            var room = NewRoom();
            room.Join("p0", "Alice", 0);
            Assert.Equal(Phase.Lobby, room.CurrentPhase);
            room.Join("p1", "Bob", 0);
            Assert.Equal(Phase.CommanderPick, room.CurrentPhase);
        }

        [Fact]
        public void RejectsUnofferedCommander()
        {
            var room = NewRoom();
            room.Join("p0", "Alice", 0);
            room.Join("p1", "Bob", 0);
            var r = room.PickCommander("p0", "notReal", 0);
            Assert.False(r.Ok);
            Assert.Equal("unknownCommander", r.Code);
        }

        [Fact]
        public void ThirdPlayerCannotJoin()
        {
            var room = NewRoom();
            room.Join("p0", "Alice", 0);
            room.Join("p1", "Bob", 0);
            Assert.Null(room.Join("p2", "Carol", 0));
        }

        [Fact]
        public void StartingArmyAndIncomeMaterialize()
        {
            var room = NewRoom();
            StartMatch(room);
            var own = room.SnapshotFor(0).Own;
            Assert.True(own.Cards.Count >= 2);
            Assert.Contains(own.Cards, c => c.UnitId == "cathedral");
            Assert.True(own.Coin >= 200);
        }

        [Fact]
        public void BuySquadSpendsAndPlaces()
        {
            var room = NewRoom();
            StartMatch(room);
            var before = room.SnapshotFor(0).Own;
            var r = room.BuySquad("p0", "footman", 0, 10, "north");
            Assert.True(r.Ok, r.Message);
            var after = room.SnapshotFor(0).Own;
            Assert.Equal(before.Coin - 100, after.Coin);
            Assert.Equal(before.DeploysRemaining - 1, after.DeploysRemaining);
            Assert.Contains(after.Cards, c => c.UnitId == "footman");
        }

        [Fact]
        public void RejectsPlacementOutsideOwnHalf()
        {
            var room = NewRoom();
            StartMatch(room);
            var r = room.BuySquad("p0", "footman", 2, 40, "north");
            Assert.False(r.Ok);
            Assert.Equal("outsideOwnHalf", r.Code);
        }

        [Fact]
        public void UnlockGatesLockedUnits()
        {
            var room = NewRoom();
            StartMatch(room);
            Assert.Equal("notUnlocked", room.BuySquad("p0", "ballista", 0, 10, "north").Code);
            Assert.True(room.UnlockUnit("p0", "ballista").Ok);
            var r = room.BuySquad("p0", "ballista", 0, 10, "north");
            Assert.True(r.Ok || r.Code == "insufficientFunds");
        }

        [Fact]
        public void HiddenPlanningDoesNotLeakTheOpponentPlan()
        {
            var room = NewRoom();
            StartMatch(room);
            room.BuySquad("p0", "footman", 0, 10, "north");
            var oppView = room.SnapshotFor(1).Opponent!;
            Assert.DoesNotContain(oppView.Cards, c => c.UnitId == "footman");
        }

        [Fact]
        public void BothReadyLocksPlanAndRunsARealBattle()
        {
            var room = NewRoom();
            StartMatch(room);
            room.BuySquad("p0", "footman", 0, 10, "north");
            room.BuySquad("p1", "archer", 0, 30, "north");
            room.SetReady("p0", true, 0);
            room.SetReady("p1", true, 0);
            Assert.Equal(Phase.Battle, room.CurrentPhase);
            // M5: a real battle log exists (the cutover's only visible change).
            Assert.NotNull(room.LastBattleLog);
        }

        [Fact]
        public void FullMatchReachesHpZero()
        {
            var room = NewRoom();
            StartMatch(room);
            int placed = 0;
            int guard = 0;
            while (room.CurrentPhase != Phase.MatchEnded && guard < 300)
            {
                guard++;
                if (room.CurrentPhase == Phase.Planning)
                {
                    room.UnlockUnit("p0", "ballista");
                    for (int d = 0; d < 3; d++)
                    {
                        int row = (placed % 10) * 3;
                        int col = 6 + placed / 10 * 5;
                        if (room.BuySquad("p0", "ballista", row, col, "north").Ok) placed++;
                    }
                    room.SetReady("p0", true, 0);
                    room.SetReady("p1", true, 0);
                }
                else if (room.CurrentPhase == Phase.Battle)
                {
                    room.BattleAck("p0", 0);
                    room.BattleAck("p1", 0);
                }
                else if (room.CurrentPhase == Phase.Results)
                {
                    room.Tick(1_000_000);
                }
            }
            Assert.Equal(Phase.MatchEnded, room.CurrentPhase);
            Assert.Equal(0, room.Winner);
            Assert.Equal(0, room.FinalHp().First(h => h.seat == 1).hp);
        }

        [Fact]
        public void ReconnectRestoresSeatState()
        {
            var room = NewRoom();
            StartMatch(room);
            room.BuySquad("p0", "footman", 0, 10, "north");
            var token = "tok-m-0";
            Assert.Equal(0, room.SeatByResumeToken(token));
            room.Disconnect("p0");
            Assert.False(room.SnapshotFor(0).Own.Connected);
            var rejoin = room.Join("p0b", "Alice", 0, token);
            Assert.NotNull(rejoin);
            Assert.Equal(0, rejoin!.Value.seat);
            Assert.True(room.SnapshotFor(0).Own.Connected);
            Assert.Contains(room.SnapshotFor(0).Own.Cards, c => c.UnitId == "footman");
        }
    }
}
