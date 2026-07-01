using LiveNBT.Protocol;

namespace LiveNBT.App.Services;

/// <summary>Narrow server-request surface so view models can be tested without a socket.</summary>
public interface IServerSession
{
    Task<ServerMessage> RequestAsync(string op, string? root = null, string? path = null, NbtNode? value = null);
}
