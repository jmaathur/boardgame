// Production battle server (M5): ASP.NET Core WebSocket host driving the ported
// C# MatchRoom. /health probe, /ws protocol-V2 endpoint, a background deadline
// ticker, and SQLite persistence (BOARDGAME_DB path; falls back to in-memory).
// At plan-lock the MatchRoom runs the Core sim and this host ships battleStarted
// + battleLog + roundResult.
using BoardGame.BattleServer;
using BoardGame.Core;

var builder = WebApplication.CreateBuilder(args);

var (catalog, catalogJson) = CatalogSource.Load();

IRoomStore store;
var dbPath = Environment.GetEnvironmentVariable("BOARDGAME_DB");
if (!string.IsNullOrEmpty(dbPath))
    store = new SqliteRoomStore($"Data Source={dbPath}");
else
    store = new InMemoryRoomStore();

var hub = new MatchHub(catalog, catalogJson, store);
builder.Services.AddSingleton(hub);
builder.Services.AddHostedService(sp => new TickerService(sp.GetRequiredService<MatchHub>()));

var app = builder.Build();
app.UseWebSockets();

// Newtonsoft (not Results.Json/System.Text.Json) — matches the wire serializer
// and avoids a PipeWriter API mismatch when a net8.0 app rolls forward onto a
// newer runtime in the test host.
app.MapGet("/health", () => Results.Content(
    Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        status = "ok",
        protocolVersion = BoardGame.Core.Match.ProtocolV2.Version,
        schemaVersion = EngineInfo.SchemaVersion,
        catalogHash = hub.CatalogHash,
        rooms = hub.RoomCount,
    }),
    "application/json"));

WebSocketEndpoint.Map(app, hub);

app.Run();

// A hosted background service that advances every room's phase deadlines.
sealed class TickerService : BackgroundService
{
    private readonly MatchHub _hub;
    public TickerService(MatchHub hub) => _hub = hub;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await _hub.TickAll(); }
            catch { /* never let a tick crash the loop */ }
        }
    }
}

// Exposed so a WebApplicationFactory-based integration test can boot the host.
public partial class Program { }
