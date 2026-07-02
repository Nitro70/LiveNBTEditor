using System.Diagnostics;
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
            string appDir = Path.Combine(appData, "LiveNBT");     // desktop app + its profiles
            string agentDir = ChooseAgentDir(appDir);             // MUST be space-free (launcher arg splitting)
            string jarPath = Path.Combine(agentDir, AgentFileName);

            InstallAgentJar(agentDir, jarPath);
            (string token, int port) = EnsureConfig(appData, out string mcDir);
            int patched = PatchLauncherProfiles(mcDir, jarPath);
            WriteAppProfile(appDir, token, port);
            InstallApp(appDir);

            Console.WriteLine();
            Console.WriteLine("Done — LiveNBT is installed.");
            Console.WriteLine($"  Agent:   {jarPath}");
            Console.WriteLine($"  Config:  {Path.Combine(mcDir, "config", "livenbt.json")}");
            Console.WriteLine($"  Token:   {token}");
            Console.WriteLine($"  Profiles wired with -javaagent: {patched}");
            Console.WriteLine();
            if (patched == 0)
            {
                Console.WriteLine("No profile was wired automatically. Add this to your profile's JVM arguments by");
                Console.WriteLine("hand (Installations > Edit > More Options):");
                Console.WriteLine($"  -javaagent:{jarPath} -Dnet.bytebuddy.experimental=true");
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

    /// <summary>The vanilla launcher splits a profile's javaArgs on spaces with NO quote handling,
    /// so the agent jar must live at a space-free path or the game will not start.</summary>
    private static string ChooseAgentDir(string preferred)
    {
        if (!preferred.Contains(' ')) return preferred;
        string fallback = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "LiveNBT");
        try
        {
            if (!_dryRun) Directory.CreateDirectory(fallback);
            Console.WriteLine($"  ! \"{preferred}\" contains a space, which the Minecraft launcher can't pass to the JVM —");
            Console.WriteLine($"    installing the agent to {fallback} instead");
            return fallback;
        }
        catch
        {
            Console.WriteLine($"  ! WARNING: \"{preferred}\" contains a space and {fallback} is not writable.");
            Console.WriteLine("    The launcher cannot pass a spaced -javaagent path — Minecraft will NOT start with");
            Console.WriteLine("    this profile until you move the jar to a space-free folder and fix the JVM argument.");
            return preferred;
        }
    }

    private static void InstallAgentJar(string agentDir, string jarPath)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(AgentResource);
        if (s is null)
        {
            if (_dryRun) { Console.WriteLine("  = (dry run) agent jar not embedded in this build — skipping"); return; }
            throw new InvalidOperationException("agent jar is not embedded in this build — run installer/build.ps1 to bundle it.");
        }
        if (!_dryRun)
        {
            Directory.CreateDirectory(agentDir);
            using FileStream fs = File.Create(jarPath);
            s.CopyTo(fs);
        }
        Console.WriteLine($"  + agent jar -> {jarPath}");
    }

    /// <summary>Reuses an existing config token (and port) when present; otherwise writes a fresh
    /// token, preserving any custom bind/port it can still read. Returns the effective token+port.</summary>
    private static (string Token, int Port) EnsureConfig(string appData, out string mcDir)
    {
        mcDir = Path.Combine(appData, ".minecraft");
        string configDir = Path.Combine(mcDir, "config");
        string cfg = Path.Combine(configDir, "livenbt.json");

        string bind = "127.0.0.1";
        int port = DefaultPort;
        if (File.Exists(cfg))
        {
            try
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(cfg));
                string? existingToken = root?["token"]?.GetValue<string>();
                if (root?["port"] is JsonValue pv && pv.TryGetValue(out int p)) port = p;
                if (root?["bind"] is JsonValue bv && bv.TryGetValue(out string? b) && !string.IsNullOrWhiteSpace(b)) bind = b!;
                if (!string.IsNullOrWhiteSpace(existingToken))
                {
                    Console.WriteLine($"  = reusing existing config token (port {port})");
                    return (existingToken!, port);
                }
            }
            catch { /* unreadable — fall through and rewrite, keeping defaults */ }
        }

        string token = RandomToken();
        var obj = new JsonObject { ["bind"] = bind, ["port"] = port, ["token"] = token };
        if (!_dryRun)
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(cfg, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"  + wrote config with a fresh access token (bind {bind}, port {port})");
        return (token, port);
    }

    /// <summary>26.x targeting: the id must START with 26. (or embed -26. for loader profiles), not
    /// merely contain "26." (which false-matched e.g. loader 0.26.0). The launcher's stock profiles
    /// carry the symbolic ids latest-release/latest-snapshot — currently 26.x, so wire those too.</summary>
    private static bool IsTargetProfile(JsonObject profile)
    {
        string? type = profile["type"]?.GetValue<string>();
        if (type is "latest-release" or "latest-snapshot") return true;
        string? version = profile["lastVersionId"]?.GetValue<string>();
        if (version is null) return false;
        return version.StartsWith("26.", StringComparison.Ordinal)
               || version.Contains("-26.", StringComparison.Ordinal);
    }

    /// <summary>Adds the -javaagent arg to every targeted launcher profile that doesn't already have
    /// it. Refuses while the launcher is open (it would rewrite the file from memory and silently
    /// undo the patch), backs up first, writes atomically, and preserves each profile's own args.</summary>
    private static int PatchLauncherProfiles(string mcDir, string jarPath)
    {
        string file = Path.Combine(mcDir, "launcher_profiles.json");
        if (!File.Exists(file))
        {
            Console.WriteLine($"  ! no launcher_profiles.json under {mcDir} — skipping (patch a profile manually)");
            return 0;
        }

        if (Process.GetProcessesByName("MinecraftLauncher").Length > 0 ||
            Process.GetProcessesByName("Minecraft Launcher").Length > 0 ||
            Process.GetProcessesByName("Minecraft").Length > 0)
        {
            Console.WriteLine("  ! the Minecraft launcher is running — it would overwrite this change from memory.");
            Console.WriteLine("    Close the launcher and run this installer again to wire the -javaagent argument.");
            return 0;
        }

        string agentArgs = $"-javaagent:{jarPath} -Dnet.bytebuddy.experimental=true";
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(file)); }
        catch (Exception e) { Console.WriteLine("  ! launcher_profiles.json is unreadable, skipping: " + e.Message); return 0; }

        if (root?["profiles"] is not JsonObject profiles) { Console.WriteLine("  ! launcher_profiles.json has no profiles — skipping"); return 0; }

        int patched = 0;
        foreach ((string key, JsonNode? node) in profiles)
        {
            if (node is not JsonObject profile || !IsTargetProfile(profile)) continue;
            string existing = profile["javaArgs"]?.GetValue<string>() ?? "";
            if (existing.Contains(AgentFileName)) continue;                // already wired
            profile["javaArgs"] = existing.Length == 0 ? $"{DefaultJvmArgs} {agentArgs}" : $"{existing} {agentArgs}";
            patched++;
            string version = profile["lastVersionId"]?.GetValue<string>() ?? profile["type"]?.GetValue<string>() ?? "?";
            string name = profile["name"]?.GetValue<string>() is { Length: > 0 } n ? n
                : profile["type"]?.GetValue<string>() ?? key;   // stock profiles have an empty name
            Console.WriteLine($"  + wired profile \"{name}\" (version {version})");
        }

        if (patched > 0 && !_dryRun)
        {
            string backup = file + ".livenbt-backup";
            if (!File.Exists(backup)) File.Copy(file, backup);
            // write-then-rename: a crash mid-write must never leave the launcher's file truncated
            string tmp = file + ".livenbt-tmp";
            File.WriteAllText(tmp, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, file, overwrite: true);
        }
        return patched;
    }

    /// <summary>Seeds or UPDATES the desktop app's local connection profile so it always carries the
    /// token/port the game config actually uses (a re-run after a .minecraft wipe used to leave the
    /// app holding a stale token). Other profiles in the file are preserved untouched.</summary>
    private static void WriteAppProfile(string appDir, string token, int port)
    {
        string file = Path.Combine(appDir, "profiles.json");
        JsonArray profiles;
        try
        {
            profiles = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonArray ?? [] : [];
        }
        catch
        {
            profiles = [];
        }

        JsonObject? local = profiles.OfType<JsonObject>().FirstOrDefault(p =>
            p["Name"]?.GetValue<string>() == "Singleplayer" || p["Host"]?.GetValue<string>() == "127.0.0.1");
        bool created = local is null;
        if (local is null)
        {
            local = new JsonObject { ["Name"] = "Singleplayer" };
            profiles.Add(local);
        }
        local["Host"] = "127.0.0.1";
        local["Port"] = port;
        local["Token"] = token;

        if (!_dryRun)
        {
            Directory.CreateDirectory(appDir);
            File.WriteAllText(file, profiles.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(created ? "  + seeded the app's connection profile" : "  + updated the app's connection profile (token/port)");
    }

    /// <summary>If the release shipped the desktop app in ".\app" beside this exe, copy it in and make a shortcut.</summary>
    private static void InstallApp(string appDir)
    {
        string src = Path.Combine(AppContext.BaseDirectory, "app");
        if (!Directory.Exists(src)) { Console.WriteLine("  = desktop app not bundled beside the installer — download it separately"); return; }
        string dst = Path.Combine(appDir, "app");
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
