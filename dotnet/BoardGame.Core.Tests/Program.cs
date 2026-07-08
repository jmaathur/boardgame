// Balance-harness CLI. Runs a lineup A vs lineup B over N seeds and prints a
// winrate / length / survivor table. Also the backend for Forge's balance tab.
//
//   dotnet run --project BoardGame.Core.Tests -- \
//     fight "footman x24" vs "archer x7" --seeds 100
//
// `dotnet test` ignores this Main and drives the xUnit runner instead.
using BoardGame.Core;
using BoardGame.Core.Catalog;
using BoardGame.Core.Tests;

if (args.Length == 0 || args[0] != "fight")
{
    Console.WriteLine($"BoardGame balance harness (engine schemaVersion {EngineInfo.SchemaVersion}).");
    Console.WriteLine("Usage: fight \"<lineupA>\" vs \"<lineupB>\" [--seeds N]");
    Console.WriteLine("  lineup: comma/plus-separated \"unitId xN\", e.g. \"footman x24, archer x7\"");
    return args.Length == 0 ? 0 : 1;
}

// Parse: fight <A> vs <B> [--seeds N]
var rest = args.Skip(1).ToList();
int vs = rest.FindIndex(a => a == "vs");
if (vs < 0)
{
    Console.Error.WriteLine("expected: fight <A> vs <B> [--seeds N]");
    return 1;
}
int seeds = 100;
int seedsFlag = rest.FindIndex(a => a == "--seeds");
if (seedsFlag >= 0 && seedsFlag + 1 < rest.Count && int.TryParse(rest[seedsFlag + 1], out var s))
{
    seeds = Math.Max(1, s);
}

string lineupA = string.Join(" ", rest.Take(vs)).Trim();
int bEnd = seedsFlag >= 0 ? seedsFlag : rest.Count;
string lineupB = string.Join(" ", rest.Skip(vs + 1).Take(bEnd - (vs + 1))).Trim();

var catalog = CatalogLoader.Load(CatalogTestData.CanonicalJson());
var report = BalanceHarness.Fight(catalog, lineupA, lineupB, seeds);
Console.WriteLine("=== balance report ===");
Console.Write(BalanceHarness.FormatReport(lineupA, lineupB, report));
return 0;
