using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LiveNBT.App.Services;

/// <summary>A running Minecraft we can attach to: its pid, its own java.exe, and its game directory.</summary>
public sealed record MinecraftTarget(int Pid, string JavaExe, string GameDir);

public sealed record AttachResult(bool Ok, string Message, string? Host = null, int Port = 0, string? Token = null);

/// <summary>
/// "Arg-less" setup: find a running Minecraft, load the LiveNBT agent into it via the JVM Dynamic
/// Attach API — launched by Minecraft's OWN bundled Java (every Mojang runtime ships the
/// <c>jdk.attach</c> module), so nothing external is needed — then read the token the agent uses so
/// the app can connect. No <c>-javaagent</c> argument, no launcher edits, no installer.
/// </summary>
public static class AttachService
{
    private const string AgentResource = "LiveNBT.App.Resources.livenbt-agent.jar";
    private const string AgentFileName = "livenbt-agent.jar";
    private const string AttacherMain = "dev.nitro.livenbt.attach.SelfAttach";

    /// <summary>Locate a running Minecraft client, or null if none is running.</summary>
    public static MinecraftTarget? FindMinecraft()
    {
        foreach ((int pid, string exe, string cmd) in QueryJavaProcesses())
        {
            if (!LooksLikeMinecraft(cmd)) continue;
            string binDir = Path.GetDirectoryName(exe) ?? "";
            string javaExe = Path.Combine(binDir, "java.exe");   // attach needs java.exe, process is javaw.exe
            if (!File.Exists(javaExe)) javaExe = exe;
            return new MinecraftTarget(pid, javaExe, ParseGameDir(cmd));
        }
        return null;
    }

    private static IEnumerable<(int Pid, string Exe, string Cmd)> QueryJavaProcesses()
    {
        var results = new List<(int, string, string)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'javaw.exe' OR Name = 'java.exe'");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                int pid = Convert.ToInt32(mo["ProcessId"] ?? 0);
                string exe = mo["ExecutablePath"] as string ?? "";
                string cmd = mo["CommandLine"] as string ?? "";
                if (pid != 0 && exe.Length > 0) results.Add((pid, exe, cmd));
            }
        }
        catch { /* WMI unavailable — nothing to attach to */ }
        return results;
    }

    private static bool LooksLikeMinecraft(string cmd) =>
        cmd.Contains("net.minecraft.client.main.Main", StringComparison.OrdinalIgnoreCase)
        || (cmd.Contains("--gameDir", StringComparison.OrdinalIgnoreCase) && cmd.Contains("--assetIndex", StringComparison.OrdinalIgnoreCase))
        || cmd.Contains("-Dminecraft.launcher", StringComparison.OrdinalIgnoreCase);

    private static string DefaultGameDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    private static string ParseGameDir(string cmd)
    {
        List<string> tokens = Tokenize(cmd);
        for (int i = 0; i < tokens.Count - 1; i++)
            if (tokens[i] == "--gameDir") return tokens[i + 1];
        return DefaultGameDir;
    }

    /// <summary>Split a command line on spaces, respecting double-quoted runs (paths may contain spaces).</summary>
    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in s)
        {
            if (c == '"') quoted = !quoted;
            else if (c == ' ' && !quoted) { if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>Load the agent into the target and return the host/port/token to connect with.</summary>
    public static async Task<AttachResult> AttachAsync(MinecraftTarget target)
    {
        string agentJar;
        try { agentJar = PlaceWhereTargetCanRead(EnsureAgentJar(), target.GameDir); }
        catch (Exception e) { return new AttachResult(false, "Agent jar not available: " + e.Message); }

        var psi = new ProcessStartInfo
        {
            FileName = target.JavaExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--add-modules");   // make jdk.attach resolvable from the classpath launcher
        psi.ArgumentList.Add("jdk.attach");
        psi.ArgumentList.Add("-cp");
        psi.ArgumentList.Add(agentJar);
        psi.ArgumentList.Add(AttacherMain);
        psi.ArgumentList.Add(target.Pid.ToString());
        psi.ArgumentList.Add(agentJar);

        try
        {
            using var p = Process.Start(psi)!;
            Task<string> outTask = p.StandardOutput.ReadToEndAsync();
            Task<string> errTask = p.StandardError.ReadToEndAsync();
            await Task.WhenAll(outTask, errTask);
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
            {
                string err = errTask.Result.Trim();
                return new AttachResult(false, "Attach failed: " + (err.Length > 0 ? err : outTask.Result.Trim()));
            }
        }
        catch (Exception e)
        {
            return new AttachResult(false, "Could not launch the attacher: " + e.Message);
        }

        // the agent creates/reads <gameDir>/config/livenbt.json on its first server tick; poll for it
        for (int i = 0; i < 24; i++)
        {
            if (ReadConfig(target.GameDir) is { } cfg)
                return new AttachResult(true, "Attached to Minecraft", "127.0.0.1", cfg.Port, cfg.Token);
            await Task.Delay(250);
        }
        return new AttachResult(true, "Attached — but no token appeared. Open a world, then use Profiles ▸ Detect.");
    }

    private static (string Token, int Port)? ReadConfig(string gameDir)
    {
        try
        {
            string file = Path.Combine(gameDir, "config", "livenbt.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            string? token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            int port = doc.RootElement.TryGetProperty("port", out var p) && p.TryGetInt32(out int pv) ? pv : 25599;
            return string.IsNullOrWhiteSpace(token) ? null : (token!, port);
        }
        catch { return null; }
    }

    /// <summary>
    /// Copy the agent jar somewhere the TARGET game process can actually read it. The MS Store
    /// launcher runs Minecraft's Java sandboxed away from arbitrary paths like %APPDATA%\LiveNBT, so
    /// <c>loadAgent</c> reports "jar not found" — but the game can always read its own game directory
    /// (verified in-game), and ProgramData is a world-readable fallback. Returns the path to pass to
    /// the attacher (also fine for the -cp launcher, which runs unsandboxed).
    /// </summary>
    private static string PlaceWhereTargetCanRead(string src, string gameDir)
    {
        string commonData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LiveNBT");
        foreach (string dir in new[] { gameDir, commonData })
        {
            try
            {
                Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir, AgentFileName);
                if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length)
                    File.Copy(src, dst, overwrite: true);
                return dst;
            }
            catch { /* not writable — try the next location */ }
        }
        return src;   // last resort; works when the game isn't sandboxed
    }

    /// <summary>Materialize the agent jar (embedded → %APPDATA%\LiveNBT, or a jar already there / beside the exe).</summary>
    private static string EnsureAgentJar()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveNBT");
        string dst = Path.Combine(dir, AgentFileName);

        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(AgentResource);
        if (s is not null)
        {
            Directory.CreateDirectory(dir);
            if (!File.Exists(dst) || new FileInfo(dst).Length != s.Length)
            {
                using FileStream fs = File.Create(dst);
                s.CopyTo(fs);
            }
            return dst;
        }
        if (File.Exists(dst)) return dst;                                       // installer / a previous attach wrote it
        string beside = Path.Combine(AppContext.BaseDirectory, AgentFileName);
        if (File.Exists(beside)) return beside;
        throw new FileNotFoundException("livenbt-agent.jar is not bundled with this build");
    }
}
