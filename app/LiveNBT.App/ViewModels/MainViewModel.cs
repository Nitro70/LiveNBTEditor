using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LiveNBT.App.Inventory;
using LiveNBT.App.Services;
using LiveNBT.Protocol;

namespace LiveNBT.App.ViewModels;

public sealed class MainViewModel : ViewModelBase, IServerSession
{
    private readonly WsClient _client = new();
    private readonly ProfileStore _profileStore = new();
    private string _status = "Disconnected";
    private bool _isConnected;
    private Profile? _selectedProfile;
    private string? _selectedRoot;
    private NodeViewModel? _treeRoot;
    private string _filterText = "";
    private bool _refreshingTree;
    private bool _userDisconnected;
    private bool _reconnecting;
    private bool _autoReconnect = true;
    private DispatcherTimer? _reconnectTimer;

    public MainViewModel()
    {
        Profiles = new ObservableCollection<Profile>(_profileStore.Load());
        _client.UpdateReceived += msg => OnUi(() => OnUpdate(msg));
        _client.Closed += reason => OnUi(() =>
        {
            IsConnected = false;
            Status = $"Disconnected: {reason}";
            MaybeScheduleReconnect();
        });
        _client.ProtocolNotice += notice => OnUi(() => Status = notice);
    }

    /// <summary>Retry the last profile every few seconds after an unexpected drop (e.g. world closed).</summary>
    public bool AutoReconnect
    {
        get => _autoReconnect;
        set { if (Set(ref _autoReconnect, value) && !value) _reconnectTimer?.Stop(); }
    }

    private void MaybeScheduleReconnect()
    {
        if (!AutoReconnect || _userDisconnected || SelectedProfile is null) return;
        _reconnectTimer ??= CreateReconnectTimer();
        if (!_reconnectTimer.IsEnabled)
        {
            Status = "Connection lost — reconnecting…";
            _reconnectTimer.Start();
        }
    }

    private DispatcherTimer CreateReconnectTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (_, _) =>
        {
            if (IsConnected || _userDisconnected || !AutoReconnect) { timer.Stop(); return; }
            if (_reconnecting) return;   // a slow attempt is still in flight
            _reconnecting = true;
            try { await ConnectAsync(silentRetry: true); }
            finally { _reconnecting = false; }
            if (IsConnected) timer.Stop();
        };
        return timer;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public ObservableCollection<Profile> Profiles { get; }
    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<WatchItemViewModel> Watches { get; } = [];

    public Profile? SelectedProfile { get => _selectedProfile; set => Set(ref _selectedProfile, value); }
    public string? SelectedRoot { get => _selectedRoot; set => Set(ref _selectedRoot, value); }
    public NodeViewModel? TreeRoot { get => _treeRoot; private set => Set(ref _treeRoot, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsConnected { get => _isConnected; set => Set(ref _isConnected, value); }
    public string FilterText { get => _filterText; set => Set(ref _filterText, value); }
    // Default to the full bundled vanilla lists so the dropdowns work immediately, even before a
    // connection; a live registry reply overrides these when it returns something (see LoadRegistryAsync).
    public IReadOnlyList<string> ItemIds { get; private set; } = BundledRegistry.Items;
    public IReadOnlyList<string> EnchantmentIds { get; private set; } = BundledRegistry.Enchantments;

    public void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _profileStore.Load()) Profiles.Add(p);
    }

    public void SaveProfiles() => _profileStore.Save(Profiles.ToList());

    private bool _connectBusy;
    /// <summary>host:port of the last successful connect — a switch to a DIFFERENT server must not
    /// keep showing (and silently re-polling) the previous server's tree.</summary>
    private string? _connectedEndpoint;

    public async Task ConnectAsync(bool silentRetry = false)
    {
        if (SelectedProfile is not { } p) { Status = "Pick a profile first"; return; }
        if (_connectBusy) return;   // an attempt is already in flight; a second click must not join it
        _connectBusy = true;
        _userDisconnected = false;
        try
        {
            Status = $"Connecting to {p.Host}:{p.Port}…";
            await _client.ConnectAsync(p.Host, p.Port, p.Token);
            string endpoint = $"{p.Host}:{p.Port}";
            if (_connectedEndpoint is not null && _connectedEndpoint != endpoint)
            {
                TreeRoot = null;   // different server: drop the old tree (reconnects to the SAME one keep it)
                Roots.Clear();
            }
            _connectedEndpoint = endpoint;
            Watches.Clear();
            IsConnected = true;
            Status = $"Connected to {p.Host}:{p.Port}";
            await RefreshRootsAsync();
            await LoadRegistryAsync();
            // one less click in singleplayer: auto-open the lone player right away. On a
            // multi-player (dedicated) server, don't load someone arbitrary — let the user pick.
            int playerCount = Roots.Count(r => r.StartsWith("player:"));
            if (TreeRoot is null && SelectedRoot is not null && playerCount <= 1) await LoadRootAsync();
            else if (TreeRoot is null && playerCount > 1) Status = $"Connected — {playerCount} players online, pick one and Load";
        }
        catch (Exception e)
        {
            // the failed attempt already tore down whatever connection existed (WsClient disposes
            // before reconnecting), so reflect reality — otherwise the dot stays green forever
            IsConnected = _client.IsConnected;
            Status = silentRetry ? "Reconnecting…" : $"Connect failed: {e.Message}";
        }
        finally
        {
            _connectBusy = false;
        }
    }

    /// <summary>One-click "arg-less" path: find a running Minecraft, load the agent into it via
    /// Dynamic Attach (no JVM args), then connect using the token the agent reports.</summary>
    public async Task AttachAndConnectAsync()
    {
        if (_connectBusy) return;
        MinecraftTarget? target = AttachService.FindMinecraft();
        if (target is null)
        {
            Status = "No running Minecraft found — launch the game and open a world, then try again.";
            return;
        }
        Status = $"Attaching to Minecraft (pid {target.Pid})…";
        AttachResult r = await AttachService.AttachAsync(target);
        if (!r.Ok || r.Token is null) { Status = r.Message; return; }

        // upsert a profile carrying the agent's own token, select it, and connect
        var profile = new Profile("Attached (this PC)", r.Host!, r.Port, r.Token);
        Profile? existing = Profiles.FirstOrDefault(p => p.Name == profile.Name);
        if (existing is not null) Profiles[Profiles.IndexOf(existing)] = profile;
        else Profiles.Add(profile);
        SelectedProfile = profile;

        for (int i = 0; i < 4 && !IsConnected; i++)   // the agent's WS server binds a beat after attach
        {
            if (i > 0) await Task.Delay(500);
            await ConnectAsync(silentRetry: i > 0);
        }
        // don't clobber ConnectAsync's "N players online, pick one and Load" guidance (LAN worlds)
        bool awaitingPick = TreeRoot is null && Roots.Count(r => r.StartsWith("player:")) > 1;
        if (IsConnected && !awaitingPick) Status = "Attached and connected — no launch arguments needed.";
    }

    public async Task DisconnectAsync()
    {
        _userDisconnected = true;
        _reconnectTimer?.Stop();
        try
        {
            await _client.DisposeAsync();
            IsConnected = false;
            TreeRoot = null;
            Roots.Clear();
            Watches.Clear();
            Status = "Disconnected";
        }
        catch (Exception e) { Status = $"disconnect: {e.Message}"; }
    }

    public async Task RefreshRootsAsync()
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("roots");
            if (!reply.Ok || reply.RawValue is null) { Status = $"roots failed: {reply.Error}"; return; }
            string? previous = SelectedRoot;   // Roots.Clear() nulls the ComboBox selection
            Roots.Clear();
            using var doc = JsonDocument.Parse(reply.RawValue);
            foreach (var pl in doc.RootElement.GetProperty("players").EnumerateArray())
                Roots.Add($"player:{pl.GetString()}");
            foreach (var w in doc.RootElement.GetProperty("worlds").EnumerateArray())
                Roots.Add($"world:{w.GetString()}");
            SelectedRoot = Roots.FirstOrDefault(r => r == previous) ?? Roots.FirstOrDefault();
        }
        catch (Exception e)
        {
            Status = $"roots failed: {e.Message}";
        }
    }

    public Task<ServerMessage> RequestAsync(string op, string? root = null, string? path = null, NbtNode? value = null)
        => _client.RequestAsync(op, root, path, value);

    public async Task LoadRegistryAsync()
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("registry");
            if (!reply.Ok || reply.RawValue is null) return;   // older mod without the op: keep bundled lists
            (var items, var ench) = RegistryReply.Parse(reply.RawValue);
            if (items.Count > 0) ItemIds = items;              // let the live server override; else keep bundled
            if (ench.Count > 0) EnchantmentIds = ench;
        }
        catch (Exception e) { Status = $"registry unavailable: {e.Message}"; }   // non-fatal: dropdowns stay empty
    }

    public async Task LoadRootAsync()
    {
        if (SelectedRoot is not { } root) return;
        if (TreeRoot is { } existing && existing.Root == root)
        {
            await RefreshTreeAsync();
            Status = $"Refreshed {root}";
            return;
        }
        try
        {
            ServerMessage reply = await _client.RequestAsync("get", root, "");
            if (!reply.Ok || reply.Value is null) { Status = $"load failed: {reply.Error}"; return; }
            TreeRoot = new NodeViewModel(root, "", reply.Value, null, root, OnNodeExpanded);
            TreeRoot.IsExpanded = true;
            Status = $"Loaded {root}";
        }
        catch (Exception e)
        {
            Status = $"load failed: {e.Message}";
        }
    }

    private void OnNodeExpanded(NodeViewModel node) => _ = RefreshNodeAsync(node);

    /// <summary>Re-fetch one subtree on expand and merge it in place (best-effort, silent).</summary>
    private async Task RefreshNodeAsync(NodeViewModel node)
    {
        if (!IsConnected || node.Path.Length == 0) return; // whole-tree handled by RefreshTreeAsync
        try
        {
            ServerMessage reply = await _client.RequestAsync("get", node.Root, node.Path);
            if (reply.Ok && reply.Value is not null) node.ApplyUpdate(reply.Value);
        }
        catch { /* expand-refresh is best-effort; ignore transient failures */ }
    }

    /// <summary>Re-fetch the whole loaded root and merge it into the existing tree in place,
    /// preserving expansion. Used by the auto-poll timer and same-root Load.</summary>
    public async Task RefreshTreeAsync()
    {
        if (!IsConnected || _refreshingTree || TreeRoot is not { } tree) return;
        _refreshingTree = true;
        try
        {
            ServerMessage reply = await _client.RequestAsync("get", tree.Root, "");
            if (reply.Ok && reply.Value is not null) tree.ApplyUpdate(reply.Value);
        }
        catch { /* best-effort; the poll will retry */ }
        finally { _refreshingTree = false; }
    }

    /// <summary>Commit an inline edit. Returns true when the server accepted it.</summary>
    public async Task<bool> CommitEditAsync(NodeViewModel node, string input)
    {
        if (!ValueParser.TryParse(node.Type, input, out string normalized, out string error))
        {
            Status = error;
            node.Flash = "error";
            return false;
        }
        var value = new NbtNode(node.Type) { Scalar = normalized };
        try
        {
            ServerMessage reply = await _client.RequestAsync("set", node.Root, node.Path, value);
            if (reply.Ok)
            {
                node.IsEditing = false;
                node.ApplyUpdate(value);
                node.Flash = "ok";
            }
            else
            {
                Status = reply.Error ?? "edit rejected";
                node.Flash = "error";
            }
            return reply.Ok;
        }
        catch (Exception e)
        {
            Status = $"edit failed: {e.Message}";
            node.Flash = "error";
            return false;
        }
    }

    public async Task DeleteAsync(NodeViewModel node)
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("delete", node.Root, node.Path);
            if (!reply.Ok) { Status = reply.Error ?? "delete failed"; return; }
            await LoadRootAsync(); // simplest consistent refresh after structural change
        }
        catch (Exception e)
        {
            Status = $"delete failed: {e.Message}";
        }
    }

    public async Task AddAsync(string root, string path, NbtNode value)
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("add", root, path, value);
            if (!reply.Ok) { Status = reply.Error ?? "add failed"; return; }
            await LoadRootAsync();
        }
        catch (Exception e)
        {
            Status = $"add failed: {e.Message}";
        }
    }

    public async Task ToggleWatchAsync(NodeViewModel node)
    {
        try
        {
            WatchItemViewModel? existing = Watches.FirstOrDefault(w => w.Root == node.Root && w.Path == node.Path);
            if (existing is not null)
            {
                await _client.RequestAsync("unwatch", node.Root, node.Path);
                Watches.Remove(existing);
                return;
            }
            ServerMessage reply = await _client.RequestAsync("watch", node.Root, node.Path);
            if (reply.Ok) Watches.Add(new WatchItemViewModel(node.Root, node.Path));
            else Status = reply.Error ?? "watch failed";
        }
        catch (Exception e)
        {
            Status = $"watch failed: {e.Message}";
        }
    }

    private void OnUpdate(ServerMessage msg)
    {
        if (msg.Root is null || msg.Path is null) return;
        foreach (var watch in Watches)
            if (watch.Root == msg.Root && watch.Path == msg.Path)
                watch.ApplyUpdate(msg.Value);
        if (msg.Value is not null && TreeRoot is { } tree && tree.Root == msg.Root)
            tree.FindByPath(msg.Path)?.ApplyUpdate(msg.Value);
    }
}
