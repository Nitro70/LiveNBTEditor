namespace LiveNBT.App.Inventory;

public static class InventoryNaming
{
    /// <summary>"minecraft:diamond_sword" -> "Diamond Sword". Namespace dropped, words title-cased.</summary>
    public static string Prettify(string id)
    {
        int colon = id.IndexOf(':');
        string name = colon >= 0 ? id[(colon + 1)..] : id;
        var words = name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }
}
