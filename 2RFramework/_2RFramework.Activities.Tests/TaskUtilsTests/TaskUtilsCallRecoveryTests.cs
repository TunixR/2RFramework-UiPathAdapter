using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using ThreadingTask = System.Threading.Tasks.Task;


// Namespace chosen to match test assembly name hint (InternalsVisibleTo attribute)
namespace _2RFramework.Activities.Tests.TaskUtilsTests
{
    /// <summary>
    /// Tests for ThreadingTaskUtils.CallRecoveryAPIAsync.
    /// These tests spin up an in-process WebSocket server to validate:
    /// 1. WebSocket handshake and client connection.
    /// 2. Initial JSON message sent by the client.
    /// 3. Screenshot binary payload sent after a "screenshot" request.
    /// 4. Final returned JObject when a "done" message is sent by the server.
    ///
    /// NOTE:
    /// - No modifications are made to ThreadingTaskUtils; dependency injection is not available.
    /// - Screenshot capture may return empty bytes in headless environments; test is tolerant and reports this.
    /// - This file is a skeleton: adjust timeouts / assertions as needed for your environment.
    /// </summary>
    public class ThreadingTaskUtilsCallRecoveryTests
    {
        [Fact(Timeout = 30000)]
        public async ThreadingTask CallRecoveryAPIAsync_FullInteraction_SendsInitialMessage_Screenshot_AndReturnsDone()
        {
            // Arrange
            var port = GetFreeTcpPort();
            var httpPrefix = $"http://localhost:{port}/ws/";
            var wsClientUri = httpPrefix; // ThreadingTaskUtils will internally convert http:// to ws://

            using var server = new TestWebSocketServer(httpPrefix);
            await server.StartAsync();

            var initialPayload = new
            {
                test = "value",
                number = 42
            };

            // Act
            var callTask = Utilities.TaskUtils.CallRecoveryAPIAsync(initialPayload, wsClientUri, null);

            // Server sequence (runs concurrently):
            // 1. Accept connection
            // 2. Receive initial text JSON
            // 3. Send screenshot request {"type":"screenshot"}
            // 4. Receive binary screenshot
            // 5. Send done message {"type":"done","status":"ok","echoInitial":<initialPayload>}
            await server.RunScreenshotInteractionAsync(initialPayload);

            var result = await callTask.ConfigureAwait(false);

            // Assert
            Assert.True(server.HandshakeAccepted, "Server did not accept a WebSocket handshake.");
            Assert.NotNull(server.InitialClientMessageJson);
            Assert.Equal("value", server.InitialClientMessageJson?["test"]?.ToString());
            Assert.Equal("42", server.InitialClientMessageJson?["number"]?.ToString());

            // Screenshot assertions
            Assert.True(server.ScreenshotRequestSent, "Server never sent screenshot request to client.");
            Assert.True(server.ScreenshotBytesReceivedAttempted, "Server never attempted to receive screenshot bytes from client.");
            // If environment cannot capture screenshot (headless), bytes may be empty.
            // We still assert the attempt happened; optionally assert length > 0 when available.

            Assert.True(server.ScreenshotBytes?.Length > 0);
            // Heuristic: PNG header starts with 0x89 0x50 0x4E 0x47
            Assert.True(server.ScreenshotBytes[0] == 0x89 &&
                        server.ScreenshotBytes[1] == 0x50 &&
                        server.ScreenshotBytes[2] == 0x4E &&
                        server.ScreenshotBytes[3] == 0x47,
                "Screenshot bytes do not look like a PNG header.");

            // Result assertions
            Assert.NotNull(result);
            Assert.IsType<JObject>(result);
            var resultJson = (JObject)result;
            Assert.Equal("done", resultJson["type"]?.ToString());
            Assert.Equal("ok", resultJson["status"]?.ToString());
        }

        // Utility: obtain an available TCP port
        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    /// <summary>
    /// Minimal in-process WebSocket test server to interact with ThreadingTaskUtils.CallRecoveryAPIAsync.
    /// </summary>
    internal sealed class TestWebSocketServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private HttpListenerContext? _context;
        private WebSocket? _webSocket;

        public bool HandshakeAccepted { get; private set; }
        public JObject? InitialClientMessageJson { get; private set; }
        public bool ScreenshotRequestSent { get; private set; }
        public bool ScreenshotBytesReceivedAttempted { get; private set; }
        public byte[]? ScreenshotBytes { get; private set; }

        public TestWebSocketServer(string httpPrefix)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(httpPrefix);
        }

        public async ThreadingTask StartAsync()
        {
            _listener.Start();
            // Accept context (non-blocking pattern)
            _ = ThreadingTask.Run(async () =>
            {
                try
                {
                    _context = await _listener.GetContextAsync().ConfigureAwait(false);
                    if (_context.Request.IsWebSocketRequest)
                    {
                        HandshakeAccepted = true;
                        var wsContext = await _context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                        _webSocket = wsContext.WebSocket;
                    }
                    else
                    {
                        _context.Response.StatusCode = 400;
                        _context.Response.Close();
                    }
                }
                catch
                {
                    // Ignored for test scenarios
                }
            }, _cts.Token);
        }

        public async ThreadingTask RunScreenshotInteractionAsync(object initialPayloadEcho)
        {
            // Wait until we have a websocket
            await WaitForWebSocketAsync();

            // 1. Receive initial JSON message from client
            InitialClientMessageJson = await ReceiveJsonAsync();

            // 2. Send screenshot request
            await SendTextAsync(new JObject { ["type"] = "screenshot" }.ToString());
            ScreenshotRequestSent = true;

            // 3. Receive binary screenshot
            ScreenshotBytesReceivedAttempted = true;
            ScreenshotBytes = await ReceiveBinaryAsync();

            // 4. Send done message with echo
            var done = new JObject
            {
                ["type"] = "done",
                ["status"] = "ok",
                ["echoInitial"] = JObject.FromObject(initialPayloadEcho)
            };
            await SendTextAsync(done.ToString());

            // Keep connection briefly to allow client to process
            await ThreadingTask.Delay(250);
            await ThreadingTask.Delay(250);
            await CloseAsync();
        }

        private async ThreadingTask WaitForWebSocketAsync()
        {
            var timeout = TimeSpan.FromSeconds(5);
            var start = DateTime.UtcNow;
            while (_webSocket == null)
            {
                if (DateTime.UtcNow - start > timeout)
                    throw new TimeoutException("WebSocket was not established within timeout.");
                await ThreadingTask.Delay(50);
                await ThreadingTask.Delay(50);
            }
        }

        private async Task<JObject?> ReceiveJsonAsync()
        {
            var text = await ReceiveTextAsync();
            try
            {
                return JObject.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> ReceiveTextAsync()
        {
            if (_webSocket == null) throw new InvalidOperationException("WebSocket not established.");

            var buffer = new byte[8192];
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return string.Empty;

                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            ms.Position = 0;
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private async Task<byte[]?> ReceiveBinaryAsync()
        {
            if (_webSocket == null) throw new InvalidOperationException("WebSocket not established.");
            var buffer = new byte[8192];
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return ms.ToArray();
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    // Unexpected text; ignore for binary capture
                    return ms.ToArray();
                }
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            return ms.ToArray();
        }

        private async ThreadingTask SendTextAsync(string text)
        {
            if (_webSocket == null) throw new InvalidOperationException("WebSocket not established.");
            var bytes = Encoding.UTF8.GetBytes(text);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
        }

        private async ThreadingTask CloseAsync()
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
                _webSocket?.Dispose();
                if (_listener.IsListening) _listener.Stop();
                _listener.Close();
            }
            catch { }
        }
    }
}
