using System.IO;
using System.Text.Json;

namespace LiveNBT.App.Services;

public sealed record Profile(string Name, string Host, int Port, string Token);

public sealed class ProfileStore(string? directory = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _file = Path.Combine(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveNBT"),
        "profiles.json");

    /// <summary>Missing or unreadable file loads as an empty list. A corrupt file is
    /// preserved as profiles.json.bak so a later Save can't silently destroy it.</summary>
    public List<Profile> Load()
    {
        try
        {
            if (!File.Exists(_file)) return [];
            return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(_file)) ?? [];
        }
        catch (JsonException)
        {
            try { File.Move(_file, _file + ".bak", overwrite: true); } catch (IOException) { }
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void Save(List<Profile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        // write-then-rename so a crash mid-write can't truncate the real file
        string tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profiles, Options));
        File.Move(tmp, _file, overwrite: true);
    }
}
