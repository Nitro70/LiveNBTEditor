namespace LiveNBT.Protocol;

public enum NbtType
{
    Byte, Short, Int, Long, Float, Double, String,
    List, Compound, ByteArray, IntArray, LongArray
}

public static class NbtTypes
{
    private static readonly (NbtType Type, string Wire)[] Map =
    {
        (NbtType.Byte, "byte"), (NbtType.Short, "short"), (NbtType.Int, "int"),
        (NbtType.Long, "long"), (NbtType.Float, "float"), (NbtType.Double, "double"),
        (NbtType.String, "string"), (NbtType.List, "list"), (NbtType.Compound, "compound"),
        (NbtType.ByteArray, "byte_array"), (NbtType.IntArray, "int_array"), (NbtType.LongArray, "long_array"),
    };

    public static string ToWire(NbtType type) => Map.First(m => m.Type == type).Wire;

    public static NbtType FromWire(string wire)
    {
        foreach (var m in Map)
            if (m.Wire == wire) return m.Type;
        throw new FormatException($"unknown NBT node type: {wire}");
    }

    /// True for types whose value is a single editable text (everything except list/compound/arrays).
    public static bool IsScalar(NbtType type) =>
        type is not (NbtType.List or NbtType.Compound or NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray);

    /// Element type for the array containers.
    public static NbtType ArrayElementType(NbtType type) => type switch
    {
        NbtType.ByteArray => NbtType.Byte,
        NbtType.IntArray => NbtType.Int,
        NbtType.LongArray => NbtType.Long,
        _ => throw new ArgumentException($"not an array type: {type}"),
    };
}
