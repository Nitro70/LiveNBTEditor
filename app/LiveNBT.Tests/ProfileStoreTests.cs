using System.IO;
using LiveNBT.App.Services;
using Xunit;

namespace LiveNBT.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void RoundTripsProfiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new ProfileStore(dir);
            Assert.Empty(store.Load());
            var profiles = new List<Profile>
            {
                new("Singleplayer", "127.0.0.1", 25599, "tok1"),
                new("My Server", "192.168.1.50", 25599, "tok2"),
            };
            store.Save(profiles);
            Assert.Equal(profiles, new ProfileStore(dir).Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CorruptFileLoadsAsEmptyAndIsRescued()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "profiles.json"), "{not json");
            Assert.Empty(new ProfileStore(dir).Load());
            // the corrupt original survives as .bak so a later Save can't destroy it
            Assert.True(File.Exists(Path.Combine(dir, "profiles.json.bak")));
            Assert.False(File.Exists(Path.Combine(dir, "profiles.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
