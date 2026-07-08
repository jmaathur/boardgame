using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BoardGame.Net
{
    /// <summary>
    /// WebSocket client for the authoritative game server (apps/game-server).
    /// Attach to a GameObject, point <see cref="serverUrl"/> at a running
    /// server (bun run dev in apps/game-server), and subscribe to the events.
    ///
    /// Networking runs on background tasks; all events are raised on the main
    /// thread via a dispatch queue drained in Update().
    /// </summary>
    public class GameServerClient : MonoBehaviour
    {
        [SerializeField] private string serverUrl = "ws://localhost:7777/ws";
        [SerializeField] private string roomId = "lobby";
        [SerializeField] private string playerName = "UnityPlayer";
        [SerializeField] private bool connectOnStart = true;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<WelcomeMessage> WelcomeReceived;
        public event Action<GameStateDto> StateReceived;
        public event Action<ErrorMessage> ErrorReceived;

        public string PlayerId { get; private set; }
        public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;

        // Both fields always change together and belong to the current
        // connection attempt; a finished attempt only clears them if they
        // still reference its own instances (see Connect's finally).
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellation;

        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        private void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        public async void Connect()
        {
            // A cancelled source means the previous connection is tearing
            // down — safe to start a new one; its finally block will not
            // clobber the fields we set below.
            if (cancellation != null && !cancellation.IsCancellationRequested)
            {
                Debug.LogWarning("[GameServerClient] Already connected or connecting.");
                return;
            }

            var socket = new ClientWebSocket();
            var cts = new CancellationTokenSource();
            webSocket = socket;
            cancellation = cts;
            var announcedConnected = false;

            try
            {
                await socket.ConnectAsync(new Uri(serverUrl), cts.Token);
                announcedConnected = true;
                RunOnMainThread(() => Connected?.Invoke());

                SendJson(JsonUtility.ToJson(new JoinMessage { roomId = roomId, playerName = playerName }));
                await ReceiveLoop(socket, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Disconnect() was called — expected.
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameServerClient] Connection failed: {exception.Message}");
            }
            finally
            {
                cts.Cancel();
                socket.Dispose();
                if (ReferenceEquals(webSocket, socket))
                {
                    webSocket = null;
                    cancellation = null;
                }
                if (announcedConnected)
                {
                    RunOnMainThread(() => Disconnected?.Invoke());
                }
            }
        }

        public void Disconnect()
        {
            cancellation?.Cancel();
        }

        public void SendPlaceUnit(string unitType, int row, int col)
        {
            SendJson(JsonUtility.ToJson(new PlaceUnitMessage { unitType = unitType, row = row, col = col }));
        }

        public void SendMoveUnit(string unitId, int row, int col)
        {
            SendJson(JsonUtility.ToJson(new MoveUnitMessage { unitId = unitId, row = row, col = col }));
        }

        public void SendPing()
        {
            SendJson(JsonUtility.ToJson(new PingMessage()));
        }

        private async void SendJson(string json)
        {
            // Capture the current connection: the fields may be cleared by
            // Connect's finally (or replaced by a reconnect) while this method
            // is suspended below.
            var socket = webSocket;
            var cts = cancellation;
            if (socket == null || cts == null || socket.State != WebSocketState.Open)
            {
                Debug.LogWarning("[GameServerClient] Not connected; dropping message.");
                return;
            }

            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
            await sendLock.WaitAsync();
            try
            {
                // The connection may have died while we waited for the lock.
                if (socket.State != WebSocketState.Open || cts.IsCancellationRequested)
                {
                    Debug.LogWarning("[GameServerClient] Connection closed; dropping message.");
                    return;
                }
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException || exception is ObjectDisposedException)
            {
                // Connection torn down mid-send — the message is moot.
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameServerClient] Send failed: {exception.Message}");
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            using (var messageBytes = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    // Accumulate raw bytes and decode once per message: a
                    // multi-byte UTF-8 character can straddle two receives.
                    messageBytes.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    var json = Encoding.UTF8.GetString(messageBytes.GetBuffer(), 0, (int)messageBytes.Length);
                    messageBytes.SetLength(0);
                    RunOnMainThread(() => HandleServerMessage(json));
                }
            }
        }

        private void HandleServerMessage(string json)
        {
            var probe = JsonUtility.FromJson<MessageTypeProbe>(json);
            switch (probe.type)
            {
                case "welcome":
                    var welcome = JsonUtility.FromJson<WelcomeMessage>(json);
                    PlayerId = welcome.playerId;
                    WelcomeReceived?.Invoke(welcome);
                    StateReceived?.Invoke(welcome.state);
                    break;
                case "state":
                    var state = JsonUtility.FromJson<StateMessage>(json);
                    StateReceived?.Invoke(state.state);
                    break;
                case "error":
                    var error = JsonUtility.FromJson<ErrorMessage>(json);
                    Debug.LogWarning($"[GameServerClient] Server error {error.code}: {error.message}");
                    ErrorReceived?.Invoke(error);
                    break;
                case "pong":
                    break;
                default:
                    Debug.LogWarning($"[GameServerClient] Unknown message type: {probe.type}");
                    break;
            }
        }

        private void RunOnMainThread(Action action)
        {
            mainThreadActions.Enqueue(action);
        }
    }
}
