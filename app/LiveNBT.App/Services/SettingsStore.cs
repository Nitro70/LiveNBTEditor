using System.IO;
using System.Text.Json;

namespace LiveNBT.App.Services;

/// <summary>App-level preferences (not connection profiles — those live in <see cref="ProfileStore"/>).</summary>
public sealed class AppSettings
{
    public bool AutoConnect { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public string? LastProfile { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }
}

/// <summary>%APPDATA%\LiveNBT\settings.json, same defensive load/save pattern as ProfileStore.</summary>
public sealed class SettingsStore(string? directory = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _file = Path.Combine(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveNBT"),
        "settings.json");

    /// <summary>Missing or corrupt file loads as defaults — settings are never worth an error dialog.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_file)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file)) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        string tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, _file, overwrite: true);
    }
}
