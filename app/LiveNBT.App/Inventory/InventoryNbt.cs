using System.Globalization;
using LiveNBT.Protocol;

namespace LiveNBT.App.Inventory;

/// <summary>Tiny NbtNode constructors/readers for building item compounds.</summary>
internal static class InventoryNbt
{
    public static NbtNode Str(string s) => new(NbtType.String) { Scalar = s };
    public static NbtNode Int(int i) => new(NbtType.Int) { Scalar = i.ToString(CultureInfo.InvariantCulture) };

    public static NbtNode Compound(List<KeyValuePair<string, NbtNode>> children)
        => new(NbtType.Compound) { Children = children };

    public static int? ReadInt(NbtNode? n) =>
        n is not null && int.TryParse(n.Scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
}
