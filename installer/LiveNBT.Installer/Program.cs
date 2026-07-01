using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveNBT.Installer;

/// <summary>
/// "Painless" LiveNBT setup for Windows. Deploys the embedded agent jar, wires the
/// <c>-javaagent</c> argument into the Minecraft launcher's 26.x profiles, seeds a config with a
/// fresh access token, and (if the desktop app is shipped alongside) installs it with a shortcut.
/// Run with <c>--dry-run</c> to preview every change without writing anything.
/// </summary>
internal static class Program
{
    private const string AgentResource = "LiveNBT.Installer.Resources.livenbt-agent.jar";
    private const string AgentFileName = "livenbt-agent.jar";
    private const int DefaultPort = 25599;

    // Mojang's stock profile JVM args — used only when a profile has none of its own, so we don't
    // strip the launcher's default heap/GC settings.
    private const string DefaultJvmArgs =
        "-Xmx2G -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 " +
        "-XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M";

    private static bool _dryRun;

    private static int Main(string[] args)
    {
        _dryRun = args.Contains("--dry-run");
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected console */ }

        Console.WriteLine("LiveNBT Setup");
        Console.WriteLine("=============");
        Console.WriteLine("Installs the LiveNBT agent and wires up Minecraft's -javaagent for you.");
        if (_dryRun) Console.WriteLine("(dry run — nothing will be written)");
        Console.WriteLine();

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string installDir = Path.Combine(appData, "LiveNBT");
            string jarPath = Path.Combine(installDir, AgentFileName);

            InstallAgentJar(installDir, jarPath);
            string token = EnsureConfig(appData, out string mcDir);
            int patched = PatchLauncherProfiles(mcDir, jarPath);
            WriteAppProfile(installDir, token);
            InstallApp(installDir);

            Console.WriteLine();
            Console.WriteLine("Done — LiveNBT is installed.");
            Console.WriteLine($"  Agent:   {jarPath}");
            Console.WriteLine($"  Config:  {Path.Combine(mcDir, "config", "livenbt.json")}");
            Console.WriteLine($"  Token:   {token}");
            Console.WriteLine($"  Profiles wired with -javaagent: {patched}");
            Console.WriteLine();
            if (patched == 0)
            {
                Console.WriteLine("No Minecraft 26.x profile was found to wire up automatically. Add this to your");
                Console.WriteLine("profile's JVM arguments by hand (Installations > Edit > More Options):");
                Console.WriteLine($"  -javaagent:\"{jarPath}\" -Dnet.bytebuddy.experimental=true");
            }
            else
            {
                Console.WriteLine("Next: launch Minecraft on your 26.2 profile and open a world, then run the LiveNBT");
                Console.WriteLine("app and click Connect.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Console.WriteLine("Install failed: " + e.Message);
            Pause();
            return 1;
        }

        Pause();
        return 0;
    }

    private static void InstallAgentJar(string installDir, string jarPath)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(AgentResource);
        if (s is null)
        {
            if (_dryRun) { Console.WriteLine("  = (dry run) agent jar not embedded in this build — skipping"); return; }
            throw new InvalidOperationException("agent jar is not embedded in this build — run installer/build.ps1 to bundle it.");
        }
        if (!_dryRun)
        {
            Directory.CreateDirectory(installDir);
            using FileStream fs = File.Create(jarPath);
            s.CopyTo(fs);
        }
        Console.WriteLine($"  + agent jar -> {jarPath}");
    }

    /// <summary>Reuses an existing config token if present, otherwise writes a fresh one. Returns the token.</summary>
    private static string EnsureConfig(string appData, out string mcDir)
    {
        mcDir = Path.Combine(appData, ".minecraft");
        string configDir = Path.Combine(mcDir, "config");
        string cfg = Path.Combine(configDir, "livenbt.json");

        if (File.Exists(cfg))
        {
            try
            {
                string? existing = JsonNode.Parse(File.ReadAllText(cfg))?["token"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(existing)) { Console.WriteLine("  = reusing existing config token"); return existing!; }
            }
            catch { /* unreadable — fall through and rewrite */ }
        }

        string token = RandomToken();
        var obj = new JsonObject { ["bind"] = "127.0.0.1", ["port"] = DefaultPort, ["token"] = token };
        if (!_dryRun)
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(cfg, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("  + wrote config with a fresh access token (bind 127.0.0.1)");
        return token;
    }

    /// <summary>Adds the -javaagent arg to every 26.x launcher profile that doesn't already have it.
    /// Backs the file up first and preserves each profile's existing args. Returns the count patched.</summary>
    private static int PatchLauncherProfiles(string mcDir, string jarPath)
    {
        string file = Path.Combine(mcDir, "launcher_profiles.json");
        if (!File.Exists(file))
        {
            Console.WriteLine($"  ! no launcher_profiles.json under {mcDir} — skipping (patch a profile manually)");
            return 0;
        }

        string agentArgs = $"-javaagent:\"{jarPath}\" -Dnet.bytebuddy.experimental=true";
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(file)); }
        catch (Exception e) { Console.WriteLine("  ! launcher_profiles.json is unreadable, skipping: " + e.Message); return 0; }

        if (root?["profiles"] is not JsonObject profiles) { Console.WriteLine("  ! launcher_profiles.json has no profiles — skipping"); return 0; }

        int patched = 0;
        foreach ((string key, JsonNode? node) in profiles)
        {
            if (node is not JsonObject profile) continue;
            string? version = profile["lastVersionId"]?.GetValue<string>();
            if (version is null || !version.Contains("26.")) continue;         // 26.x is the unobfuscated line this agent targets
            string existing = profile["javaArgs"]?.GetValue<string>() ?? "";
            if (existing.Contains(AgentFileName)) { continue; }                // already wired
            profile["javaArgs"] = existing.Length == 0 ? $"{DefaultJvmArgs} {agentArgs}" : $"{existing} {agentArgs}";
            patched++;
            Console.WriteLine($"  + wired profile \"{profile["name"]?.GetValue<string>() ?? key}\" (version {version})");
        }

        if (patched > 0 && !_dryRun)
        {
            string backup = file + ".livenbt-backup";
            if (!File.Exists(backup)) File.Copy(file, backup);
            File.WriteAllText(file, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        return patched;
    }

    /// <summary>Seeds the desktop app's connection profile with the matching token (never clobbers an existing one).</summary>
    private static void WriteAppProfile(string installDir, string token)
    {
        string file = Path.Combine(installDir, "profiles.json");
        if (File.Exists(file)) return;
        var profiles = new JsonArray(new JsonObject
        {
            ["Name"] = "Singleplayer", ["Host"] = "127.0.0.1", ["Port"] = DefaultPort, ["Token"] = token,
        });
        if (!_dryRun)
        {
            Directory.CreateDirectory(installDir);
            File.WriteAllText(file, profiles.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("  + seeded the app's connection profile");
    }

    /// <summary>If the release shipped the desktop app in ".\app" beside this exe, copy it in and make a shortcut.</summary>
    private static void InstallApp(string installDir)
    {
        string src = Path.Combine(AppContext.BaseDirectory, "app");
        if (!Directory.Exists(src)) { Console.WriteLine("  = desktop app not bundled beside the installer — download it separately"); return; }
        string dst = Path.Combine(installDir, "app");
        if (!_dryRun) CopyDirectory(src, dst);
        Console.WriteLine($"  + installed the desktop app -> {dst}");

        string exe = Path.Combine(dst, "LiveNBT.App.exe");
        if (!_dryRun && File.Exists(exe)) CreateStartMenuShortcut(exe);
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), overwrite: true);
    }

    private static void CreateStartMenuShortcut(string targetExe)
    {
        try
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LiveNBT.lnk");
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
            shortcut.Description = "LiveNBT — live Minecraft NBT editor";
            shortcut.Save();
            Console.WriteLine("  + Start-menu shortcut created");
        }
        catch { /* a shortcut is a nice-to-have, not worth failing the install */ }
    }

    private static string RandomToken()
    {
        byte[] bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void Pause()
    {
        if (_dryRun || Console.IsInputRedirected) return;
        Console.WriteLine();
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
    }
}
