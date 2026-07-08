// Entry point for the balance-harness CLI (dotnet run --project
// BoardGame.Core.Tests -- fight "base.footman x5" vs "base.ballista x2"
// --seeds 100). The real harness lands in M4; until then this is a stub so
// the Exe-typed test project has a Main. `dotnet test` ignores this and drives
// the xUnit runner instead.
using BoardGame.Core;

if (args.Length == 0)
{
    Console.WriteLine($"BoardGame balance harness (engine schemaVersion {EngineInfo.SchemaVersion}).");
    Console.WriteLine("Usage: fight \"<lineupA>\" vs \"<lineupB>\" --seeds <N>");
    Console.WriteLine("The battle simulation arrives in M4; no fights to run yet.");
    return 0;
}

Console.Error.WriteLine("balance harness not implemented until M4");
return 1;
