namespace LiveNBT.Protocol;

/// <summary>
/// In-memory NBT value. Scalars keep their value as an invariant-culture string
/// (longs/doubles never pass through floating point). Lists and arrays use Items;
/// compounds use Children (ordered).
/// </summary>
public sealed class NbtNode(NbtType type)
{
    public NbtType Type { get; } = type;
    public string? Scalar { get; set; }
    public List<NbtNode>? Items { get; set; }
    public List<KeyValuePair<string, NbtNode>>? Children { get; set; }

    public NbtNode? Child(string name)
    {
        if (Children is null) return null;
        foreach (var kv in Children)
            if (kv.Key == name) return kv.Value;
        return null;
    }
}
