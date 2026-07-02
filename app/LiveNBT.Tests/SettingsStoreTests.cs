using System.IO;
using LiveNBT.App.Services;
using Xunit;

namespace LiveNBT.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void RoundTripsSettings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "livenbt-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            var s = new AppSettings
            {
                AutoConnect = true,
                AutoReconnect = false,
                LastProfile = "Singleplayer",
                WindowWidth = 1100,
                WindowHeight = 700,
                WindowLeft = 40,
                WindowTop = 60,
                WindowMaximized = true,
            };
            store.Save(s);
            AppSettings loaded = new SettingsStore(dir).Load();
            Assert.True(loaded.AutoConnect);
            Assert.False(loaded.AutoReconnect);
            Assert.Equal("Singleplayer", loaded.LastProfile);
            Assert.Equal(1100, loaded.WindowWidth);
            Assert.True(loaded.WindowMaximized);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingOrCorruptFileLoadsDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "livenbt-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new SettingsStore(dir).Load().AutoReconnect);   // default on, nothing saved yet
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{not json");
            var loaded = new SettingsStore(dir).Load();
            Assert.False(loaded.AutoConnect);                            // defaults, not an exception
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
