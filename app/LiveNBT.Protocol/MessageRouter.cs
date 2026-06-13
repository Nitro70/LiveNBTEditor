using System.Collections.Concurrent;

namespace LiveNBT.Protocol;

/// <summary>Correlates replies to requests by id and fans out pushes. Thread-safe.
/// A router is single-connection — create a fresh MessageRouter per connect.</summary>
public sealed class MessageRouter
{
    private long _nextId;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<ServerMessage>> _pending = new();
    private Exception? _dead;

    /// <summary>Raised synchronously on the OnIncoming caller's thread; handlers must marshal to the UI thread and must not throw.</summary>
    public event Action<ServerMessage>? UpdateReceived;
    /// <summary>Raised synchronously on the OnIncoming caller's thread; handlers must marshal to the UI thread and must not throw.</summary>
    public event Action<ServerMessage>? HelloReceived;
    /// <summary>Raised when a reply arrives with an id that has no pending request (e.g. server error for a timed-out request). Handlers must not throw.</summary>
    public event Action<ServerMessage>? UnmatchedReply;

    public (long Id, Task<ServerMessage> Reply) NewRequest()
    {
        long id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<ServerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        Exception? dead = Volatile.Read(ref _dead);
        if (dead is not null && _pending.TryRemove(id, out _)) tcs.TrySetException(dead);
        return (id, tcs.Task);
    }

    public void OnIncoming(string json)
    {
        ServerMessage msg = Wire.Parse(json);
        if (msg.Id is long id)
        {
            if (_pending.TryRemove(id, out var tcs)) tcs.TrySetResult(msg);
            else UnmatchedReply?.Invoke(msg);
            return;
        }
        switch (msg.Op)
        {
            case "update": UpdateReceived?.Invoke(msg); break;
            case "hello": HelloReceived?.Invoke(msg); break;
        }
    }

    /// <summary>Drop a pending request whose caller gave up (e.g. timed out) so it can't leak.</summary>
    public void Forget(long id) => _pending.TryRemove(id, out _);

    public void FailAll(Exception reason)
    {
        Volatile.Write(ref _dead, reason);
        foreach (var key in _pending.Keys.ToArray())
            if (_pending.TryRemove(key, out var tcs)) tcs.TrySetException(reason);
    }
}
