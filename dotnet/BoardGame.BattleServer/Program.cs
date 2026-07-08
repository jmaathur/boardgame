// Production battle server host. M0: a bare /health liveness probe so the
// solution builds and the deploy path exists end-to-end. WebSocket rooms, the
// match loop, the sim burst, and SQLite persistence are wired in at M5.
using BoardGame.Core;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    schemaVersion = EngineInfo.SchemaVersion,
}));

app.Run();

// Exposed so an M5 WebApplicationFactory-based integration test can boot the
// same host in-process.
public partial class Program { }
