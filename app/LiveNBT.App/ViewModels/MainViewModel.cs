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
    private readonly UndoJournal _journal = new();
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
                _journal.Clear(); // undo history references the old server's paths
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
            _journal.Clear();
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
            _journal.Clear();   // a different tree — old undo entries point into the previous root
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
        NbtNode before = node.Node;   // pre-edit snapshot for undo
        string root = node.Root, path = node.Path;
        try
        {
            ServerMessage reply = await _client.RequestAsync("set", node.Root, node.Path, value);
            if (reply.Ok)
            {
                node.IsEditing = false;
                node.ApplyUpdate(value);
                node.Flash = "ok";
                _journal.Record(new UndoEntry($"set {path} = {normalized}",
                    () => SendSetAsync(root, path, before),
                    () => SendSetAsync(root, path, value)));
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

    public async Task<bool> DeleteAsync(NodeViewModel node)
    {
        // snapshots for undo, captured before the server mutates anything
        string root = node.Root, path = node.Path;
        bool listElement = node.Parent?.Type is NbtType.List or NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray;
        NbtNode before = node.Node;
        NbtNode? parentBefore = listElement ? node.Parent!.Node : null;
        string parentPath = ParentPath(path);

        if (!await SendDeleteAsync(root, path)) return false;
        // a deleted list element shifts its siblings, so undo restores the whole parent list
        // (the protocol has no insert-at-index); compound keys restore in place with a set
        _journal.Record(listElement
            ? new UndoEntry($"delete {path}",
                () => SendSetAsync(root, parentPath, parentBefore!),
                () => SendDeleteAsync(root, path))
            : new UndoEntry($"delete {path}",
                () => SendSetAsync(root, path, before),
                () => SendDeleteAsync(root, path)));
        Status = $"Deleted {path}";
        return true;
    }

    public async Task AddAsync(NodeViewModel container, string path, NbtNode value)
    {
        string root = container.Root;
        bool listAppend = path == container.Path;   // a list append targets the container itself
        // list undo restores the whole pre-add container snapshot — index-based deletes could hit
        // the wrong element if the game mutates the list between the add and the undo
        NbtNode containerBefore = container.Node;

        if (!await SendAddAsync(root, path, value)) return;
        _journal.Record(listAppend
            ? new UndoEntry($"add to {path}",
                () => SendSetAsync(root, path, containerBefore),
                () => SendAddAsync(root, path, value))
            : new UndoEntry($"add {path}",
                () => SendDeleteAsync(root, path),
                () => SendAddAsync(root, path, value)));
        Status = listAppend ? $"Added to {path}" : $"Added {path}";
    }

    /// <summary>Compound keys become path segments, so names the path syntax can't address are
    /// rejected before anything is sent ('.' and '[' are separators, ']' closes an index).</summary>
    internal static bool IsPathSafeName(string name) =>
        name.Length > 0 && !name.Contains('.') && !name.Contains('[') && !name.Contains(']');

    // ===== op primitives: send + targeted refresh (the tree merges in place, so expansion,
    // selection and scroll survive structural edits — no whole-tree rebuild) =====

    /// <summary>Parent path of a path segment: "a.b.c" → "a.b", "a[3]" → "a", top-level → "".</summary>
    internal static string ParentPath(string path)
    {
        int cut = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
        return cut <= 0 ? "" : path[..cut];
    }

    private async Task<bool> SendSetAsync(string root, string path, NbtNode value)
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("set", root, path, value);
            if (!reply.Ok) { Status = reply.Error ?? "set failed"; return false; }
            await RefreshSubtreeAsync(root, ParentPath(path));   // parent: a set may have created the key
            return true;
        }
        catch (Exception e) { Status = $"set failed: {e.Message}"; return false; }
    }

    private async Task<bool> SendAddAsync(string root, string path, NbtNode value)
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("add", root, path, value);
            if (!reply.Ok) { Status = reply.Error ?? "add failed"; return false; }
            await RefreshSubtreeAsync(root, ParentPath(path));
            return true;
        }
        catch (Exception e) { Status = $"add failed: {e.Message}"; return false; }
    }

    private async Task<bool> SendDeleteAsync(string root, string path)
    {
        try
        {
            ServerMessage reply = await _client.RequestAsync("delete", root, path);
            if (!reply.Ok) { Status = reply.Error ?? "delete failed"; return false; }
            await RefreshSubtreeAsync(root, ParentPath(path));
            return true;
        }
        catch (Exception e) { Status = $"delete failed: {e.Message}"; return false; }
    }

    /// <summary>Re-fetch one subtree and merge it in place (whole tree when path is "").</summary>
    private async Task RefreshSubtreeAsync(string root, string path)
    {
        if (TreeRoot is not { } tree || tree.Root != root) return;
        if (path.Length == 0) { await RefreshTreeAsync(); return; }
        try
        {
            ServerMessage reply = await _client.RequestAsync("get", root, path);
            if (reply.Ok && reply.Value is not null) tree.FindByPath(path)?.ApplyUpdate(reply.Value);
        }
        catch { /* best-effort; the 2 s poll will reconcile */ }
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

    // ===== ease-of-use editing ops (NBT Studio parity) — all built on the primitives above =====

    /// <summary>Collision-free child name: base, else base1, base2… (empty base → tag1…).</summary>
    internal static string AutoName(string baseName, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.Ordinal);
        if (baseName.Length > 0 && !set.Contains(baseName)) return baseName;
        string stem = baseName.Length == 0 ? "tag" : baseName;
        for (int i = 1; ; i++)
            if (!set.Contains(stem + i)) return stem + i;
    }

    public async Task DuplicateAsync(NodeViewModel node)
    {
        if (!node.CanDuplicate || node.Parent is not { } parent) return;
        string root = node.Root;
        NbtNode copy = node.Node;
        if (parent.Type == NbtType.Compound)
        {
            if (!IsPathSafeName(node.Name)) { Status = $"'{node.Name}' contains path characters (. [ ]) — can't duplicate it by path"; return; }
            string name = AutoName(node.Name, (parent.Node.Children ?? []).Select(c => c.Key));
            string path = parent.Path.Length == 0 ? name : $"{parent.Path}.{name}";
            // add, not set: the server errors if the key appeared since the last poll,
            // instead of silently overwriting it
            if (!await SendAddAsync(root, path, copy)) return;
            _journal.Record(new UndoEntry($"duplicate {node.Path} → {name}",
                () => SendDeleteAsync(root, path),
                () => SendAddAsync(root, path, copy)));
            Status = $"Duplicated as {name}";
        }
        else   // list element: append a copy; undo restores the pre-add list snapshot
        {
            NbtNode parentBefore = parent.Node;
            if (!await SendAddAsync(root, parent.Path, copy)) return;
            _journal.Record(new UndoEntry($"duplicate {node.Path}",
                () => SendSetAsync(root, parent.Path, parentBefore),
                () => SendAddAsync(root, parent.Path, copy)));
            Status = $"Duplicated {node.Path}";
        }
    }

    /// <summary>Add one parsed tag into a container (the "Add as SNBT" flow). Unnamed tags get an
    /// automatic name in compounds; list appends ignore the name.</summary>
    public async Task<bool> AddParsedAsync(NodeViewModel container, string name, NbtNode value)
    {
        if (!container.CanAddChild) { Status = "Adding works on player compounds and lists"; return false; }
        string root = container.Root;
        if (container.Type == NbtType.Compound)
        {
            if (name.Length > 0 && !IsPathSafeName(name))
            {
                Status = $"'{name}' contains path characters (. [ ]) — pick a name without them";
                return false;
            }
            string finalName = AutoName(name.Length > 0 ? name : NbtTypes.ToWire(value.Type),
                                        (container.Node.Children ?? []).Select(c => c.Key));
            string path = container.Path.Length == 0 ? finalName : $"{container.Path}.{finalName}";
            if (!await SendAddAsync(root, path, value)) return false;   // add: errors if the key raced into existence
            _journal.Record(new UndoEntry($"add {path}",
                () => SendDeleteAsync(root, path),
                () => SendAddAsync(root, path, value)));
            Status = $"Added {finalName}";
        }
        else
        {
            NbtNode containerBefore = container.Node;
            if (!await SendAddAsync(root, container.Path, value)) return false;
            _journal.Record(new UndoEntry($"add to {container.Path}",
                () => SendSetAsync(root, container.Path, containerBefore),
                () => SendAddAsync(root, container.Path, value)));
            Status = $"Added to {container.Path}";
        }
        return true;
    }

    /// <summary>Paste clipboard SNBT into a container. Tries the whole text as ONE tag first
    /// (handles pretty-printed multi-line SNBT), then falls back to one-tag-per-line — the same
    /// contract NBT Studio uses, so its clipboard round-trips. All pasted tags undo as one step.</summary>
    public async Task PasteSnbtAsync(NodeViewModel container, string text)
    {
        if (!container.CanAddChild) { Status = "Paste works on player compounds and lists"; return; }
        text = text.Trim();
        if (text.Length == 0) { Status = "Clipboard is empty"; return; }

        var tags = new List<(string Name, NbtNode Value)>();
        int skipped = 0;
        if (SnbtParser.TryParseNamed(text, out string name1, out NbtNode? value1, out _))
        {
            tags.Add((name1, value1!));
        }
        else
        {
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (SnbtParser.TryParseNamed(line, out string n, out NbtNode? v, out _)) tags.Add((n, v!));
                else skipped++;
            }
        }
        if (tags.Count == 0) { Status = "Clipboard has no parseable SNBT"; return; }

        string root = container.Root, containerPath = container.Path;
        bool isCompound = container.Type == NbtType.Compound;
        var takenNames = new HashSet<string>((container.Node.Children ?? []).Select(c => c.Key), StringComparer.Ordinal);
        NbtNode containerBefore = container.Node;   // list undo restores this snapshot wholesale
        var done = new List<(string Path, NbtNode Value)>();

        foreach (var (tagName, value) in tags)
        {
            if (isCompound)
            {
                if (tagName.Length > 0 && !IsPathSafeName(tagName)) { skipped++; continue; }   // '.'/'[' keys aren't path-addressable
                string n = AutoName(tagName.Length > 0 ? tagName : NbtTypes.ToWire(value.Type), takenNames);
                takenNames.Add(n);
                string path = containerPath.Length == 0 ? n : $"{containerPath}.{n}";
                if (!await SendAddAsync(root, path, value)) break;   // add: errors instead of overwriting a raced-in key
                done.Add((path, value));
            }
            else
            {
                // a list element has no name; a "name:value" line here would silently lose the name
                // (e.g. unquoted minecraft:stone → "stone"), so skip it and let the user quote it
                if (tagName.Length > 0) { skipped++; continue; }
                if (!await SendAddAsync(root, containerPath, value)) break;
                done.Add((containerPath, value));
            }
        }
        if (done.Count == 0)
        {
            if (skipped > 0) Status = $"Nothing pasted — {skipped} line(s) unparseable or not path-addressable";
            return;
        }

        _journal.Record(isCompound
            ? new UndoEntry($"paste {done.Count} tag(s)",
                async () =>
                {
                    bool ok = true;
                    for (int i = done.Count - 1; i >= 0; i--) ok &= await SendDeleteAsync(root, done[i].Path);
                    return ok;
                },
                async () =>
                {
                    bool ok = true;
                    foreach (var (path, value) in done) ok &= await SendAddAsync(root, path, value);
                    return ok;
                })
            : new UndoEntry($"paste {done.Count} element(s)",
                () => SendSetAsync(root, containerPath, containerBefore),
                async () =>
                {
                    bool ok = true;
                    foreach (var (_, value) in done) ok &= await SendAddAsync(root, containerPath, value);
                    return ok;
                }));
        Status = $"Pasted {done.Count} tag(s)" +
                 (skipped > 0 ? $", {skipped} skipped (unparseable or un-addressable name)" : "") +
                 (done.Count < tags.Count - skipped ? " — stopped at a server error" : "");
    }

    /// <summary>Rename a compound key: copy to the new name, then delete the old one (rolled back
    /// if the delete fails, so a half-rename never leaves a duplicate). The key moves to the end
    /// of the compound — cosmetic only, vanilla compounds don't guarantee order.</summary>
    public async Task<bool> RenameAsync(NodeViewModel node, string newName)
    {
        if (!node.CanRename || node.Parent is not { } parent) return false;
        newName = newName.Trim();
        if (newName.Length == 0) { Status = "Name can't be empty"; return false; }
        if (!IsPathSafeName(newName)) { Status = "Names with path characters (. [ ]) can't be addressed — pick another"; return false; }
        if (!IsPathSafeName(node.Name)) { Status = $"'{node.Name}' contains path characters (. [ ]) — can't rename it by path"; return false; }
        if (newName == node.Name) return true;
        if (parent.Node.Child(newName) is not null) { Status = $"'{newName}' already exists here"; return false; }

        string root = node.Root, oldPath = node.Path;
        string newPath = parent.Path.Length == 0 ? newName : $"{parent.Path}.{newName}";
        NbtNode value = node.Node;

        // copy-then-delete with rollback, in both directions: add (not set) so a key that raced
        // into existence at the destination aborts cleanly, and a failed delete never leaves both
        async Task<bool> MoveKey(string from, string to)
        {
            if (!await SendAddAsync(root, to, value)) return false;
            if (!await SendDeleteAsync(root, from))
            {
                await SendDeleteAsync(root, to);
                Status = "Rename failed — rolled back";
                return false;
            }
            return true;
        }

        if (!await MoveKey(oldPath, newPath)) return false;
        _journal.Record(new UndoEntry($"rename {oldPath} → {newName}",
            () => MoveKey(newPath, oldPath),
            () => MoveKey(oldPath, newPath)));
        Status = $"Renamed to {newName}";
        return true;
    }

    /// <summary>Move a list/array element up or down by rewriting the parent container with one
    /// atomic set. Returns the element's new index, or -1 when nothing moved.</summary>
    public async Task<int> MoveElementAsync(NodeViewModel node, int delta)
    {
        if (!node.CanMove || node.Parent is not { } parent || parent.Node.Items is not { } items) return -1;
        int i = node.IndexInParent, j = i + delta;
        if (i < 0 || j < 0 || j >= items.Count) return -1;

        NbtNode before = parent.Node;
        var swapped = new List<NbtNode>(items);
        (swapped[i], swapped[j]) = (swapped[j], swapped[i]);
        var after = new NbtNode(parent.Type) { Items = swapped };
        string root = node.Root, parentPath = parent.Path;

        if (!await SendSetAsync(root, parentPath, after)) return -1;
        _journal.Record(new UndoEntry($"move {parentPath}[{i}] → [{j}]",
            () => SendSetAsync(root, parentPath, before),
            () => SendSetAsync(root, parentPath, after)));
        Status = $"Moved to [{j}]";
        return j;
    }

    /// <summary>Replace a whole subtree (the Edit-as-SNBT commit) — one atomic server-side set.</summary>
    public async Task<bool> SetSubtreeAsync(NodeViewModel node, NbtNode value)
    {
        NbtNode before = node.Node;
        string root = node.Root, path = node.Path;
        if (!await SendSetAsync(root, path, value)) return false;
        _journal.Record(new UndoEntry($"edit {path}",
            () => SendSetAsync(root, path, before),
            () => SendSetAsync(root, path, value)));
        Status = $"Applied {path}";
        return true;
    }

    public async Task UndoAsync()
    {
        if (!IsConnected) { Status = "Not connected — undo will be available again after reconnecting"; return; }
        if (!_journal.CanUndo) { Status = "Nothing to undo"; return; }
        string? desc = await _journal.TryUndoAsync();
        Status = desc is null ? $"Undo failed — {Status}" : $"Undid: {desc}";
    }

    public async Task RedoAsync()
    {
        if (!IsConnected) { Status = "Not connected — redo will be available again after reconnecting"; return; }
        if (!_journal.CanRedo) { Status = "Nothing to redo"; return; }
        string? desc = await _journal.TryRedoAsync();
        Status = desc is null ? $"Redo failed — {Status}" : $"Redid: {desc}";
    }

    public async Task RemoveWatchAsync(WatchItemViewModel watch)
    {
        try
        {
            await _client.RequestAsync("unwatch", watch.Root, watch.Path);
            Watches.Remove(watch);
        }
        catch (Exception e) { Status = $"unwatch failed: {e.Message}"; }
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
