using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BoardGame.Core.Catalog;
using BoardGame.Core.Sim;

namespace BoardGame.Core.Tests
{
    /// <summary>
    /// Headless balance harness — runs a lineup A vs lineup B over N seeds and
    /// reports winrate / battle length / survivor tables. Also the backend for
    /// Forge's balance tab. Uses the NullEventSink so it is fast.
    /// </summary>
    public static class BalanceHarness
    {
        public sealed class Report
        {
            public int Seeds;
            public int AWins;
            public int BWins;
            public int Draws;
            public double AWinrate => Seeds == 0 ? 0 : (double)AWins / Seeds;
            public double AvgDurationTicks;
            public double AvgMembersAliveA;
            public double AvgMembersAliveB;
        }

        /// <summary>Parse "base.footman x5" / "footman*5" / "footman:5" into (id,count).</summary>
        public static List<(string unitId, int count)> ParseLineup(string spec)
        {
            var entries = new List<(string, int)>();
            foreach (var raw in spec.Split(new[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                // strip an optional "pack." prefix
                int dot = token.IndexOf('.');
                // only treat as pack prefix if the left side looks like a pack id
                // (letters only) — avoid eating decimals (there are none here).
                if (dot > 0 && token.Substring(0, dot).All(char.IsLetter) &&
                    !token.Substring(0, dot).Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    // keep only if the remainder still has a unit id
                    var after = token.Substring(dot + 1);
                    if (after.Length > 0) token = after;
                }
                int count = 1;
                string id = token;
                foreach (var sep in new[] { 'x', '*', ':' })
                {
                    int idx = token.IndexOf(sep);
                    if (idx > 0 && int.TryParse(token.Substring(idx + 1).Trim(), out var c))
                    {
                        id = token.Substring(0, idx).Trim();
                        count = Math.Max(1, c);
                        break;
                    }
                }
                entries.Add((id, count));
            }
            return entries;
        }

        public static Report Fight(LoadedCatalog catalog, string lineupA, string lineupB, int seeds)
        {
            var a = ParseLineup(lineupA);
            var b = ParseLineup(lineupB);
            var report = new Report { Seeds = seeds };
            double totalDuration = 0, aliveA = 0, aliveB = 0;
            for (uint i = 0; i < seeds; i++)
            {
                uint seed = i + 1;
                // Swap which lineup takes seat 0 on alternating seeds so any
                // residual seat/first-mover bias cancels — the balance signal is
                // the LINEUP matchup, not the seat. A is seat 0 on even i.
                bool aIsSeat0 = (i % 2) == 0;
                var seat0 = aIsSeat0 ? a : b;
                var seat1 = aIsSeat0 ? b : a;
                var army0 = BattleSetup.FromLineup(catalog, 0, seat0);
                var army1 = BattleSetup.FromLineup(catalog, 1, seat1);
                var result = new BattleSim(catalog, seed).Run(army0, army1);

                // Translate seat winner → lineup winner.
                int aSeat = aIsSeat0 ? 0 : 1;
                if (result.WinnerSeat == aSeat) report.AWins++;
                else if (result.WinnerSeat == (1 - aSeat)) report.BWins++;
                else report.Draws++;
                totalDuration += result.DurationTicks;
                aliveA += result.MembersAliveBySeat.GetValueOrDefault(aSeat);
                aliveB += result.MembersAliveBySeat.GetValueOrDefault(1 - aSeat);
            }
            report.AvgDurationTicks = seeds == 0 ? 0 : totalDuration / seeds;
            report.AvgMembersAliveA = seeds == 0 ? 0 : aliveA / seeds;
            report.AvgMembersAliveB = seeds == 0 ? 0 : aliveB / seeds;
            return report;
        }

        public static string FormatReport(string lineupA, string lineupB, Report r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  A: {lineupA}");
            sb.AppendLine($"  B: {lineupB}");
            sb.AppendLine($"  seeds: {r.Seeds}");
            sb.AppendLine($"  A winrate: {r.AWinrate:P1}  (A {r.AWins} / B {r.BWins} / draw {r.Draws})");
            sb.AppendLine($"  avg length: {r.AvgDurationTicks / BattleSim.TickRate:F1}s ({r.AvgDurationTicks:F0} ticks)");
            sb.AppendLine($"  avg survivors: A {r.AvgMembersAliveA:F1}  B {r.AvgMembersAliveB:F1}");
            return sb.ToString();
        }
    }
}
