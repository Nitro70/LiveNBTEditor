using System.IO;
using System.Reflection;

namespace LiveNBT.App.Inventory;

/// <summary>
/// The full vanilla 26.2 item + enchantment id lists, embedded in the app so the search dropdowns
/// are populated with the whole game even before (or without) a live <c>registry</c> fetch. A
/// connected server's real registry still takes precedence when it returns something (see
/// <c>MainViewModel.LoadRegistryAsync</c>), so modded/other-version servers aren't limited to these.
/// </summary>
public static class BundledRegistry
{
    public static IReadOnlyList<string> Items { get; } = Load("items.txt");
    public static IReadOnlyList<string> Enchantments { get; } = Load("enchantments.txt");

    private static IReadOnlyList<string> Load(string file)
    {
        var asm = Assembly.GetExecutingAssembly();
        using Stream? s = asm.GetManifestResourceStream($"LiveNBT.App.Resources.{file}");
        if (s is null) return [];
        using var reader = new StreamReader(s);
        var list = new List<string>();
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            string id = line.Trim();
            if (id.Length > 0) list.Add(id);
        }
        return list;
    }
}
