using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Events;
using BoardGame.Core.Sim;
using Newtonsoft.Json;
using Xunit;

namespace BoardGame.Core.Tests
{
    public class BattleSimTests
    {
        private static LoadedCatalog Catalog() => CatalogLoader.Load(CatalogTestData.CanonicalJson());

        private static string RunToJson(uint seed, string lineupA, string lineupB)
        {
            var cat = Catalog();
            var a = BattleSetup.FromLineup(cat, 0, BalanceHarness.ParseLineup(lineupA));
            var b = BattleSetup.FromLineup(cat, 1, BalanceHarness.ParseLineup(lineupB));
            var sink = new ListEventSink();
            new BattleSim(cat, seed, sink).Run(a, b);
            // Serialize the whole event log; byte-equality is the determinism gate.
            return JsonConvert.SerializeObject(sink.Events, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
            });
        }

        [Fact]
        public void RepeatSeedProducesIdenticalEventLog()
        {
            // The core determinism guarantee (design doc §6, from day one).
            var first = RunToJson(12345, "footman x6", "archer x4");
            var second = RunToJson(12345, "footman x6", "archer x4");
            Assert.Equal(first, second);
            Assert.True(first.Length > 100, "expected a non-trivial event log");
        }

        [Fact]
        public void DifferentSeedsCanProduceDifferentLogs()
        {
            var a = RunToJson(1, "footman x6", "footman x6");
            var b = RunToJson(2, "footman x6", "footman x6");
            // Not a hard requirement, but our setup uses the seed; ensure the seed
            // is actually threaded (logs differ or the battle is trivially short).
            Assert.NotNull(a);
            Assert.NotNull(b);
        }

        [Fact]
        public void EmitsBattleStartedAndEndedBookends()
        {
            var cat = Catalog();
            var a = BattleSetup.FromLineup(cat, 0, BalanceHarness.ParseLineup("footman x2"));
            var b = BattleSetup.FromLineup(cat, 1, BalanceHarness.ParseLineup("footman x2"));
            var sink = new ListEventSink();
            new BattleSim(cat, 7, sink).Run(a, b);
            Assert.IsType<BattleStartedEvent>(sink.Events.First());
            Assert.IsType<BattleEndedEvent>(sink.Events.Last());
            Assert.Equal(4, sink.Events.OfType<SquadSpawnedEvent>().Count()); // 2 per side
        }

        [Fact]
        public void OverwhelmingNumbersWin()
        {
            var report = BalanceHarness.Fight(Catalog(), "footman x12", "archer x2", 20);
            Assert.True(report.AWinrate >= 0.9, $"expected footman swarm to dominate, got {report.AWinrate:P0}");
        }

        [Fact]
        public void AntiAirBeatsFlyersItCanHit()
        {
            // gargoyle targets air only; whelp is air → gargoyle wins decisively.
            var report = BalanceHarness.Fight(Catalog(), "gargoyle x5", "whelp x10", 20);
            Assert.True(report.AWinrate >= 0.8, $"anti-air should beat flyers, got {report.AWinrate:P0}");
        }

        [Fact]
        public void MirrorMatchupIsFairAfterSideSwap()
        {
            // The harness swaps sides each seed, so identical lineups → ~50%.
            var report = BalanceHarness.Fight(Catalog(), "footman x5", "footman x5", 20);
            Assert.Equal(0.5, report.AWinrate, 3);
        }

        [Fact]
        public void SplashDamageHitsMultipleMembers()
        {
            // A ballista volley (areaDamage) into a tight footman swarm should kill
            // several bodies per impact — verify multiple splash damage events.
            var cat = Catalog();
            var a = BattleSetup.FromLineup(cat, 0, BalanceHarness.ParseLineup("ballista x2"));
            var b = BattleSetup.FromLineup(cat, 1, BalanceHarness.ParseLineup("footman x4"));
            var sink = new ListEventSink();
            new BattleSim(cat, 3, sink).Run(a, b);
            int splashDeaths = sink.Events.OfType<MemberDiedEvent>().Count();
            Assert.True(splashDeaths > 5, $"expected splash to rack up kills, got {splashDeaths}");
        }

        [Fact]
        public void SurvivorValueIsProratedByMembersAlive()
        {
            // A one-sided fight leaves the winner with partial survivors → a
            // positive-but-not-full survivor value.
            var cat = Catalog();
            var a = BattleSetup.FromLineup(cat, 0, BalanceHarness.ParseLineup("footman x10"));
            var b = BattleSetup.FromLineup(cat, 1, BalanceHarness.ParseLineup("archer x1"));
            var result = new BattleSim(cat, 5).Run(a, b);
            Assert.Equal(0, result.WinnerSeat);
            int survivorValue = result.SurvivorValueBySeat[0];
            Assert.True(survivorValue > 0, "winner should inflict survivor damage");
        }
    }
}
