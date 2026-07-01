namespace LiveNBT.App.Inventory;

public static class InventoryFilter
{
    /// <summary>Case-insensitive substring match over the id and its prettified name. Capped at limit.</summary>
    public static List<string> Filter(IEnumerable<string> ids, string query, int limit = 200)
    {
        string q = query.Trim();
        if (q.Length == 0) return ids.Take(limit).ToList();
        return ids.Where(id =>
                id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                InventoryNaming.Prettify(id).Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToList();
    }
}
