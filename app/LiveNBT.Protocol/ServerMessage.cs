namespace LiveNBT.Protocol;

/// <summary>One parsed server frame: a reply (Id set) or a push (Op set).</summary>
public sealed record ServerMessage
{
    public string? Op { get; init; }
    public long? Id { get; init; }
    public bool Ok { get; init; }
    public string? Error { get; init; }
    /// <summary>Typed node value. Null when the frame carried no value, a JSON-null value
    /// ("gone" semantics on updates), or a non-node value (see RawValue).</summary>
    public NbtNode? Value { get; init; }
    /// <summary>Raw JSON of the value field — for non-node values like the roots reply.</summary>
    public string? RawValue { get; init; }
    public string? Root { get; init; }
    public string? Path { get; init; }
}
