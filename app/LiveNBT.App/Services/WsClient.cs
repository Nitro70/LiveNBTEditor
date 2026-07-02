using System.IO;
using System.Net.WebSockets;
using System.Text;
using LiveNBT.Protocol;

namespace LiveNBT.App.Services;

/// <summary>
/// One LiveNBT server connection. Create, ConnectAsync, RequestAsync, DisposeAsync.
/// Events are raised on the receive-loop thread — UI consumers must marshal.
/// IsConnected reflects the socket only; it is true during the hello/auth handshake.
/// </summary>
public sealed class WsClient : IAsyncDisposable
{
    private MessageRouter? _router;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private volatile bool _disposing;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    /// <summary>Serializes ConnectAsync: overlapping connects used to dispose each other's sockets
    /// through the shared fields (double-click on Connect broke both attempts).</summary>
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private const int MaxMessageBytes = 32 * 1024 * 1024;

    public event Action<ServerMessage>? UpdateReceived;
    public event Action<string?>? Closed;
    public event Action<string>? ProtocolNotice;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string host, int port, string token, CancellationToken ct = default)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            await DisposeAsync();

            var router = new MessageRouter();           // fresh router per connection
            router.UpdateReceived += msg => UpdateReceived?.Invoke(msg);
            // a late reply to a timed-out (forgotten) request is only news when it carries an error
            router.UnmatchedReply += msg =>
            {
                if (msg.Error is not null) ProtocolNotice?.Invoke($"server error: {msg.Error}");
            };
            var helloTcs = new TaskCompletionSource<ServerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            router.HelloReceived += msg => helloTcs.TrySetResult(msg);
            // fault the hello wait the moment the connection dies, instead of sitting out the 5s timeout
            Action<string?> onClosed = reason =>
                helloTcs.TrySetException(new InvalidOperationException(reason ?? "connection lost"));
            Closed += onClosed;
            _router = router;

            try
            {
                _socket = new ClientWebSocket();
                _cts = new CancellationTokenSource();
                await _socket.ConnectAsync(new Uri($"ws://{host}:{port}/"), ct);
                _receiveLoop = Task.Run(() => ReceiveLoopAsync(_socket, router, _cts.Token), CancellationToken.None);

                await helloTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                ServerMessage authReply = await RequestAsync("auth", token: token).WaitAsync(TimeSpan.FromSeconds(5), ct);
                if (!authReply.Ok) throw new InvalidOperationException($"auth failed: {authReply.Error}");
            }
            catch
            {
                // the connect lock guarantees these fields are still THIS attempt's resources
                await DisposeAsync();
                throw;
            }
            finally
            {
                Closed -= onClosed;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>Throws TimeoutException if the server doesn't reply within 30s (e.g. singleplayer paused).</summary>
    public async Task<ServerMessage> RequestAsync(string op, string? root = null, string? path = null,
        NbtNode? value = null, string? token = null)
    {
        ClientWebSocket socket = _socket ?? throw new InvalidOperationException("not connected");
        MessageRouter router = _router ?? throw new InvalidOperationException("not connected");
        var (id, reply) = router.NewRequest();
        string json = Wire.BuildRequest(id, op, root, path, value, token);
        await _sendLock.WaitAsync();
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
        try
        {
            return await reply.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            router.Forget(id); // caller gave up — don't leave the pending entry to leak
            throw;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, MessageRouter router, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Fail(router, "closed by server");
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        Fail(router, "message too large");
                        return;
                    }
                } while (!result.EndOfMessage);
                router.OnIncoming(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            }
            Fail(router, "connection ended");
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or FormatException)
        {
            // FormatException = malformed frame from the server; treat as fatal for this connection
            Fail(router, e.Message);
        }
        catch (Exception e)
        {
            Fail(router, "receive loop error: " + e.Message);
        }
        finally
        {
            // never leave a half-dead socket open (e.g. after a malformed or oversized frame):
            // the server would keep pushing watch updates into a buffer nobody drains
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) socket.Abort();
            }
            catch { /* already torn down */ }
        }
    }

    private void Fail(MessageRouter router, string? reason)
    {
        router.FailAll(new InvalidOperationException(reason ?? "connection lost"));
        if (!_disposing) Closed?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception) { /* already gone */ }
        }
        _cts?.Cancel();
        _socket?.Dispose();
        _socket = null;
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch { /* loop exceptions already routed via Fail */ }
            _receiveLoop = null;
        }
        _router?.FailAll(new InvalidOperationException("disposed"));
        _router = null;
        _cts?.Dispose();
        _cts = null;
        _disposing = false;
    }
}
