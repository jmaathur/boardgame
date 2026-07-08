using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BoardGame.BattleServer.Tests
{
    /// <summary>
    /// Ported conformance suite over a REAL socket (Kestrel on an ephemeral port
    /// + a real ClientWebSocket — not TestHost, whose in-memory transport
    /// deadlocks on cross-connection broadcasts). The client sees protocol V2
    /// exactly as against the Bun server, except battleLog now appears — proving
    /// the .NET cutover is a server swap, not a client migration.
    /// </summary>
    public class MatchServerIntegrationTests
    {
        /// <summary>Boots the real Program host on 127.0.0.1:0 and returns its base URL.</summary>
        private static async Task<(WebApplication app, string baseUrl)> StartHost()
        {
            // Build the same app the production Program builds, but bind an
            // ephemeral loopback port. We replicate Program's wiring here because
            // Program.Main runs app.Run() (blocking); this gives us StartAsync.
            var (catalog, catalogJson) = CatalogSource.Load();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var hub = new MatchHub(catalog, catalogJson, new InMemoryRoomStore());
            builder.Services.AddSingleton(hub);

            var app = builder.Build();
            app.UseWebSockets();
            app.MapGet("/health", () => Results.Content(
                Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    status = "ok",
                    protocolVersion = BoardGame.Core.Match.ProtocolV2.Version,
                    catalogHash = hub.CatalogHash,
                }), "application/json"));
            WebSocketEndpoint.Map(app, hub);

            await app.StartAsync();
            var addr = app.Urls.First();
            return (app, addr);
        }

        // A background-reader client: a receive loop drains every frame into a
        // queue, so Until() never misses a message due to read ordering and
        // surfaces an unexpected cmdRejected instead of hanging on it.
        private sealed class Ws : IDisposable
        {
            private readonly ClientWebSocket _ws = new();
            private readonly System.Collections.Concurrent.ConcurrentQueue<JObject> _queue = new();

            public async Task Connect(string baseUrl)
            {
                var uri = new Uri(baseUrl.Replace("http://", "ws://") + "/ws");
                await _ws.ConnectAsync(uri, CancellationToken.None);
                _ = ReceiveLoop();
            }

            private async Task ReceiveLoop()
            {
                var buffer = new byte[64 * 1024];
                try
                {
                    while (_ws.State == WebSocketState.Open)
                    {
                        using var ms = new MemoryStream();
                        WebSocketReceiveResult r;
                        do { r = await _ws.ReceiveAsync(buffer, CancellationToken.None); ms.Write(buffer, 0, r.Count); }
                        while (!r.EndOfMessage);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        _queue.Enqueue(JObject.Parse(Encoding.UTF8.GetString(ms.ToArray())));
                    }
                }
                catch { /* closed */ }
            }

            public async Task Send(object msg)
            {
                var text = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
                await _ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            public async Task<JObject> Until(string type, int timeoutMs = 30000)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    if (_queue.TryDequeue(out var m))
                    {
                        var t = (string?)m["type"];
                        if (t == type) return m;
                        // A cmdRejected we weren't waiting for means a test-setup bug
                        // (e.g. an invalid placement) — fail fast instead of hanging.
                        if (t == "cmdRejected" && type != "cmdRejected")
                            throw new Xunit.Sdk.XunitException($"unexpected cmdRejected: {m["code"]} {m["message"]}");
                        continue;
                    }
                    await Task.Delay(5);
                }
                throw new TimeoutException($"never saw {type}");
            }

            public void Dispose() => _ws.Dispose();
        }

        [Fact]
        public async Task HealthAdvertisesProtocolV2()
        {
            var (app, baseUrl) = await StartHost();
            try
            {
                using var http = new HttpClient();
                var body = await http.GetStringAsync(baseUrl + "/health");
                var json = JObject.Parse(body);
                Assert.Equal("ok", (string?)json["status"]);
                Assert.Equal(2, (int)json["protocolVersion"]!);
                Assert.False(string.IsNullOrEmpty((string?)json["catalogHash"]));
            }
            finally { await app.StopAsync(); }
        }

        [Fact]
        public async Task JoinReturnsWelcomeWithCatalogBytes()
        {
            var (app, baseUrl) = await StartHost();
            try
            {
                using var a = new Ws();
                await a.Connect(baseUrl);
                await a.Send(new { type = "join", roomId = "r1", playerName = "Alice", protocolVersion = 2 });
                var welcome = await a.Until("welcome");
                Assert.Equal(0, (int)welcome["seat"]!);
                Assert.True(((string)welcome["catalogJson"]!).Length > 100);
                Assert.Equal(32, (int)welcome["matchConfig"]!["board"]!["w"]!);
            }
            finally { await app.StopAsync(); }
        }

        [Fact]
        public async Task FullRoundProducesARealBattleLog()
        {
            var (app, baseUrl) = await StartHost();
            try
            {
                using var a = new Ws();
                using var b = new Ws();
                // Join sequentially (await each welcome) so seat assignment is
                // deterministic — a = seat 0, b = seat 1 — regardless of thread
                // scheduling. Each then buys within ITS OWN half.
                await a.Connect(baseUrl);
                await a.Send(new { type = "join", roomId = "r2", playerName = "Alice", protocolVersion = 2 });
                var aWelcome = await a.Until("welcome");
                int aSeat = (int)aWelcome["seat"]!;

                await b.Connect(baseUrl);
                await b.Send(new { type = "join", roomId = "r2", playerName = "Bob", protocolVersion = 2 });
                var bWelcome = await b.Until("welcome");
                int bSeat = (int)bWelcome["seat"]!;

                var aPhase = await a.Until("phase");
                var bPhase = await b.Until("phase");
                Assert.Equal("commanderPick", (string?)aPhase["match"]!["phase"]);

                await a.Send(new { type = "pickCommander", cmdId = "a1", commanderId = (string)aPhase["match"]!["commanderOffers"]![0]! });
                await b.Send(new { type = "pickCommander", cmdId = "b1", commanderId = (string)bPhase["match"]!["commanderOffers"]![0]! });
                await a.Until("phase"); // planning

                // Buy in each seat's own half: seat 0 near col 10, seat 1 near col 30.
                int aCol = aSeat == 0 ? 10 : 30;
                int bCol = bSeat == 0 ? 10 : 30;
                await a.Send(new { type = "buySquad", cmdId = "a2", unitId = "archer", anchor = new { row = 0, col = aCol }, orientation = "north" });
                await b.Send(new { type = "buySquad", cmdId = "b2", unitId = "footman", anchor = new { row = 0, col = bCol }, orientation = "north" });
                await a.Until("cmdAccepted");
                await b.Until("cmdAccepted");

                await a.Send(new { type = "setReady", cmdId = "a3", ready = true });
                await b.Send(new { type = "setReady", cmdId = "b3", ready = true });

                var started = await a.Until("battleStarted");
                Assert.True((bool)started["hasBattleLog"]!); // THE cutover signal
                var log = await a.Until("battleLog");
                Assert.NotNull(log["log"]);

                await a.Send(new { type = "battleAck" });
                await b.Send(new { type = "battleAck" });
                var result = await a.Until("roundResult");
                Assert.Equal(2, ((JArray)result["hp"]!).Count);
            }
            finally { await app.StopAsync(); }
        }

        [Fact]
        public async Task RejectedCommandReturnsCmdRejected()
        {
            var (app, baseUrl) = await StartHost();
            try
            {
                using var a = new Ws();
                using var b = new Ws();
                await a.Connect(baseUrl);
                await a.Send(new { type = "join", roomId = "r3", playerName = "Alice", protocolVersion = 2 });
                await a.Until("welcome");
                await b.Connect(baseUrl);
                await b.Send(new { type = "join", roomId = "r3", playerName = "Bob", protocolVersion = 2 });
                await b.Until("welcome");
                await a.Until("phase");
                await a.Send(new { type = "pickCommander", cmdId = "x", commanderId = "notReal" });
                var rej = await a.Until("cmdRejected");
                Assert.Equal("unknownCommander", (string?)rej["code"]);
            }
            finally { await app.StopAsync(); }
        }
    }
}
