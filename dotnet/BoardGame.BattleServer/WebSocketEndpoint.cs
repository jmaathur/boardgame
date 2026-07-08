using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BoardGame.BattleServer
{
    /// <summary>
    /// The /ws protocol-V2 endpoint, shared by Program (production) and the
    /// integration tests (a real Kestrel host on an ephemeral port), so both
    /// exercise identical wiring.
    /// </summary>
    public static class WebSocketEndpoint
    {
        public static void Map(WebApplication app, MatchHub hub)
        {
            app.Map("/ws", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                var sendLock = new SemaphoreSlim(1, 1);
                var conn = new Connection(async text =>
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await sendLock.WaitAsync();
                    try
                    {
                        if (ws.State == WebSocketState.Open)
                            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    finally { sendLock.Release(); }
                });

                var buffer = new byte[16 * 1024];
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        using var ms = new MemoryStream();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                                break;
                            }
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close) break;
                        var raw = Encoding.UTF8.GetString(ms.ToArray());
                        if (raw.Length == 0) continue;
                        await hub.HandleAsync(conn, raw);
                    }
                }
                catch (WebSocketException) { /* client dropped */ }
                finally { hub.Disconnect(conn); }
            });
        }
    }
}
