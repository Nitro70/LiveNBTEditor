using System.Text.Json;

namespace LiveNBT.Protocol;

/// <summary>Parses the `registry` op reply value (raw JSON object).</summary>
public static class RegistryReply
{
    public static (List<string> Items, List<string> Enchantments) Parse(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        return (Ids(doc.RootElement, "items"), Ids(doc.RootElement, "enchantments"));
    }

    private static List<string> Ids(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
    }
}
