using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
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

    public MainViewModel()
    {
        Profiles = new ObservableCollection<Profile>(_profileStore.Load());
        _client.UpdateReceived += msg => OnUi(() => OnUpdate(msg));
        _client.Closed += reason => OnUi(() =>
        {
            IsConnected = false;
            Status = $"Disconnected: {reason}";
        });
        _client.ProtocolNotice += notice => OnUi(() => Status = notice);
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

    public async Task ConnectAsync()
    {
        if (SelectedProfile is not { } p) { Status = "Pick a profile first"; return; }
        try
        {
            Status = $"Connecting to {p.Host}:{p.Port}…";
            await _client.ConnectAsync(p.Host, p.Port, p.Token);
            Watches.Clear();
            IsConnected = true;
            Status = $"Connected to {p.Host}:{p.Port}";
            await RefreshRootsAsync();
            await LoadRegistryAsync();
        }
        catch (Exception e)
        {
            Status = $"Connect failed: {e.Message}";
        }
    }

    public async Task DisconnectAsync()
    {
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
            Roots.Clear();
            using var doc = JsonDocument.Parse(reply.RawValue);
            foreach (var pl in doc.RootElement.GetProperty("players").EnumerateArray())
                Roots.Add($"player:{pl.GetString()}");
            foreach (var w in doc.RootElement.GetProperty("worlds").EnumerateArray())
                Roots.Add($"world:{w.GetString()}");
            SelectedRoot ??= Roots.FirstOrDefault();
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
