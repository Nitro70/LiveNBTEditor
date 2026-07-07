using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LiveNBT.App.Services;
using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;

namespace LiveNBT.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        DataContext = _vm;
        _settings = _settingsStore.Load();
        ApplyWindowBounds();
        if (_settings.TreeFontSize is >= 8 and <= 32) Tree.FontSize = _settings.TreeFontSize;
        _vm.AutoReconnect = _settings.AutoReconnect;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TreeRoot))
                Tree.ItemsSource = _vm.TreeRoot is null ? null : new[] { _vm.TreeRoot };
        };
        _pollTimer.Tick += async (_, _) => await _vm.RefreshTreeAsync();
        _pollTimer.Start();
        Loaded += async (_, _) =>
        {
            Profile? last = _vm.Profiles.FirstOrDefault(p => p.Name == _settings.LastProfile)
                            ?? _vm.Profiles.FirstOrDefault();
            if (last is not null) _vm.SelectedProfile = last;
            if (_settings.AutoConnect && _vm.SelectedProfile is not null) await _vm.ConnectAsync();
        };
    }

    /// <summary>Restore the saved size/position, but never place the window off every screen.</summary>
    private void ApplyWindowBounds()
    {
        if (_settings.WindowWidth >= 400 && _settings.WindowHeight >= 300)
        {
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }
        double vLeft = SystemParameters.VirtualScreenLeft, vTop = SystemParameters.VirtualScreenTop;
        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop) &&
            _settings.WindowLeft >= vLeft - 8 && _settings.WindowTop >= vTop - 8 &&
            _settings.WindowLeft < vLeft + SystemParameters.VirtualScreenWidth - 60 &&
            _settings.WindowTop < vTop + SystemParameters.VirtualScreenHeight - 60)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveSettings()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.LastProfile = _vm.SelectedProfile?.Name;
        _settings.AutoReconnect = _vm.AutoReconnect;
        try { _settingsStore.Save(_settings); } catch { /* settings are never worth blocking exit */ }
    }

    private async void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        await _vm.DisconnectAsync();
    }

    private async void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await _vm.RefreshTreeAsync();
            if (_vm.IsConnected) _vm.Status = "Refreshed";
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            FilterBox.Focus();
            FilterBox.SelectAll();
            return;
        }
        if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnOpenInventory(sender, new RoutedEventArgs());
            return;
        }
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            OnOpenHelp(sender, new RoutedEventArgs());
            return;
        }

        // ===== ease-of-use hotkeys (NBT Studio parity). The guard below is load-bearing:
        // PreviewKeyDown fires before TextBox KeyDown, so never intercept while typing
        // (inline editor Enter/Esc, filter box, dialog boxes). =====
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        ModifierKeys mods = Keyboard.Modifiers;
        // window-wide: undo / redo / deep find
        if (e.Key == Key.Z && mods == ModifierKeys.Control)
        {
            e.Handled = true;
            await _vm.UndoAsync();
            return;
        }
        if ((e.Key == Key.Z && mods == (ModifierKeys.Control | ModifierKeys.Shift)) ||
            (e.Key == Key.Y && mods == ModifierKeys.Control))
        {
            e.Handled = true;
            await _vm.RedoAsync();
            return;
        }
        if (e.Key == Key.F && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            OpenFindWindow();
            return;
        }

        // the rest act on the selected tree row, and only while the tree owns keyboard focus —
        // otherwise Space/Enter/Del would be stolen from buttons and combo boxes
        if (!Tree.IsKeyboardFocusWithin || SelectedNode is not { } node) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;   // Alt combos arrive as Key.System

        switch (key, mods)
        {
            case (Key.Enter, ModifierKeys.None):
            case (Key.E, ModifierKeys.Control):
                e.Handled = true;
                if (node.CanEdit) node.IsEditing = true;
                break;
            case (Key.E, ModifierKeys.Control | ModifierKeys.Shift):
                e.Handled = true;
                OpenEditSnbt(node);
                break;
            case (Key.F2, ModifierKeys.None):
                e.Handled = true;
                OpenRename(node);
                break;
            case (Key.Delete, ModifierKeys.None):
                e.Handled = true;
                await DeleteWithSelectionAsync(node, confirm: true);
                break;
            case (Key.C, ModifierKeys.Control):
                e.Handled = true;
                CopyNodeSnbt(node);
                break;
            case (Key.C, ModifierKeys.Control | ModifierKeys.Shift):
                e.Handled = true;
                CopyNodePath(node);
                break;
            case (Key.X, ModifierKeys.Control):
                e.Handled = true;
                await CutNodeAsync(node);
                break;
            case (Key.V, ModifierKeys.Control):
                e.Handled = true;
                await PasteIntoAsync(node);
                break;
            case (Key.D, ModifierKeys.Control):
                e.Handled = true;
                await _vm.DuplicateAsync(node);
                break;
            case (Key.Space, ModifierKeys.None):
                e.Handled = true;
                if (node.Children is not null) node.IsExpanded = !node.IsExpanded;
                break;
            case (Key.Space, ModifierKeys.Control):
                e.Handled = true;
                ExpandAllUnder(node);
                break;
            case (Key.Up, ModifierKeys.Control):
                e.Handled = true;
                if (node.Parent is { } parent) SelectNode(parent);
                break;
            case (Key.Up, ModifierKeys.Alt):
                e.Handled = true;
                await MoveSelectedAsync(node, -1);
                break;
            case (Key.Down, ModifierKeys.Alt):
                e.Handled = true;
                await MoveSelectedAsync(node, +1);
                break;
        }
    }

    private NodeViewModel? SelectedNode => Tree.SelectedItem as NodeViewModel;

    private async void OnAttach(object sender, RoutedEventArgs e) => await _vm.AttachAndConnectAsync();
    private async void OnConnect(object sender, RoutedEventArgs e) => await _vm.ConnectAsync();
    private async void OnDisconnect(object sender, RoutedEventArgs e) => await _vm.DisconnectAsync();
    private async void OnLoadRoot(object sender, RoutedEventArgs e) => await _vm.LoadRootAsync();
    private async void OnRefreshRoots(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshRootsAsync();
        await _vm.LoadRegistryAsync();
    }

    // MenuItems inherit the placement target's DataContext, so one cast covers
    // both direct senders and context-menu items.
    private static NodeViewModel? NodeOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as NodeViewModel;

    private void OnNodeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && NodeOf(sender) is { CanEdit: true } node)
        {
            node.IsEditing = true;
            e.Handled = true;
        }
    }

    private void OnEditMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { CanEdit: true } node) node.IsEditing = true;
    }

    private void OnEditorIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box &&
            box.DataContext is NodeViewModel { IsEditing: true } node)
        {
            box.Text = node.ValueText;
            box.SelectAll();
            box.Focus();
        }
    }

    private async void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not NodeViewModel node) return;
        if (e.Key == Key.Enter)
        {
            bool ok = await _vm.CommitEditAsync(node, box.Text);
            if (ok) node.IsEditing = false;
            ScheduleFlashClear(node);
        }
        else if (e.Key == Key.Escape)
        {
            node.IsEditing = false;
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: NodeViewModel node }) node.IsEditing = false;
    }

    private static void ScheduleFlashClear(NodeViewModel node)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) => { node.Flash = ""; timer.Stop(); };
        timer.Start();
    }

    private async void OnWatchMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await _vm.ToggleWatchAsync(node);
    }

    private async void OnDeleteMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await DeleteWithSelectionAsync(node, confirm: true);
    }

    private async void OnAddMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not { } node) return;
        if (node.Type is not (NbtType.Compound or NbtType.List))
        {
            _vm.Status = "Add works on compounds and lists";
            return;
        }
        var dialog = new AddTagWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            if (node.Type == NbtType.Compound && result.Name.Length == 0)
            {
                _vm.Status = "compound entries need a name";
                return;
            }
            if (node.Type == NbtType.List && result.Name.Length > 0)
            {
                _vm.Status = "list entries don't take a name";
                return;
            }
            if (result.Name.Length > 0 && !MainViewModel.IsPathSafeName(result.Name))
            {
                _vm.Status = "Names with path characters (. [ ]) can't be addressed — pick another";
                return;
            }
            string path = result.Name.Length == 0
                ? node.Path
                : node.Path.Length == 0 ? result.Name : $"{node.Path}.{result.Name}";
            await _vm.AddAsync(node, path, result.Value);
        }
    }

    private void OnCopyPathMenu(object sender, RoutedEventArgs e)
    {
        try
        {
            if (NodeOf(sender) is { } node) Clipboard.SetText(node.Path);
        }
        catch (Exception ex) { _vm.Status = $"copy failed: {ex.Message}"; }
    }

    private void OnCopySnbtMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) CopyNodeSnbt(node);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        // v1: expands top-level branches whose name matches; collapses the rest
        if (_vm.TreeRoot is not { Children: { } kids }) return;
        string filter = _vm.FilterText.Trim();
        foreach (var child in kids)
            child.IsExpanded = filter.Length > 0 &&
                child.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private async void OnOpenInventory(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsConnected) { _vm.Status = "Connect first"; return; }

        var players = _vm.Roots.Where(r => r.StartsWith("player:")).ToList();
        string? playerRoot = _vm.SelectedRoot is { } sel && sel.StartsWith("player:") ? sel
            : players.Count == 1 ? players[0] : null;
        if (playerRoot is null)
        {
            _vm.Status = "Select a player in the Roots dropdown, then click Inventory";
            return;
        }

        string invRoot = "inventory:" + playerRoot["player:".Length..];
        if (_vm.ItemIds.Count == 0) await _vm.LoadRegistryAsync();
        var invVm = new ViewModels.InventoryViewModel(_vm, invRoot, _vm.ItemIds, _vm.EnchantmentIds);
        new InventoryWindow(invVm) { Owner = this }.Show();
    }

    private void OnEditProfiles(object sender, RoutedEventArgs e)
    {
        string? selectedName = _vm.SelectedProfile?.Name;
        var dialog = new ProfilesWindow(_vm, _settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { _settingsStore.Save(_settings); } catch { /* saved again on exit */ }
        // the dialog rebuilt the Profiles collection — restore a sensible selection
        _vm.SelectedProfile = _vm.Profiles.FirstOrDefault(p => p.Name == selectedName)
                              ?? _vm.Profiles.FirstOrDefault();
    }

    // ================= ease-of-use update: clipboard / structure / navigation =================

    /// <summary>Clipboard form NBT Studio round-trips: compound children as name:value, else bare.</summary>
    private static string NamedSnbt(NodeViewModel node)
    {
        string snbt = SnbtWriter.Write(node.Node);
        if (node.Parent?.Type != NbtType.Compound || node.Name.Length == 0) return snbt;
        bool plain = node.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '-');
        string name = plain ? node.Name
            : "\"" + node.Name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return name + ":" + snbt;
    }

    private void CopyNodeSnbt(NodeViewModel node)
    {
        try
        {
            Clipboard.SetText(NamedSnbt(node));
            _vm.Status = $"Copied {(node.Path.Length == 0 ? node.Root : node.Path)}";
        }
        catch (Exception ex) { _vm.Status = $"copy failed: {ex.Message}"; }
    }

    private void CopyNodePath(NodeViewModel node)
    {
        try
        {
            Clipboard.SetText(node.Path);
            _vm.Status = "Copied path";
        }
        catch (Exception ex) { _vm.Status = $"copy failed: {ex.Message}"; }
    }

    private async Task CutNodeAsync(NodeViewModel node)
    {
        if (!node.CanDelete) { _vm.Status = "Can't cut here"; return; }
        try { Clipboard.SetText(NamedSnbt(node)); }
        catch (Exception ex) { _vm.Status = $"cut failed: {ex.Message}"; return; }
        string name = node.Name;
        // no confirm on cut (matches NBT Studio) — the data is on the clipboard
        if (await DeleteWithSelectionAsync(node, confirm: false))
            _vm.Status = $"Cut {name} (on clipboard)";
    }

    private async Task PasteIntoAsync(NodeViewModel node)
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch (Exception ex) { _vm.Status = $"paste failed: {ex.Message}"; return; }
        await _vm.PasteSnbtAsync(node, text);
    }

    /// <summary>Delete, then keep the selection near the hole (next sibling → clamped → parent).</summary>
    private async Task<bool> DeleteWithSelectionAsync(NodeViewModel node, bool confirm)
    {
        if (!node.CanDelete) { _vm.Status = "Can't delete here"; return false; }
        if (confirm && MessageBox.Show($"Delete {node.Path}?", "LiveNBT", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return false;
        NodeViewModel? parent = node.Parent;
        int index = node.IndexInParent;
        if (!await _vm.DeleteAsync(node)) return false;
        if (parent?.Children is { Count: > 0 } kids) SelectNode(kids[Math.Clamp(index, 0, kids.Count - 1)]);
        else if (parent is not null) SelectNode(parent);
        return true;
    }

    private async Task MoveSelectedAsync(NodeViewModel node, int delta)
    {
        NodeViewModel? parent = node.Parent;
        int j = await _vm.MoveElementAsync(node, delta);
        if (j >= 0 && parent?.Children is { } kids && j < kids.Count) SelectNode(kids[j]);
    }

    /// <summary>Bulk expand, capped — silent expansion (no per-node refresh), the poll keeps it fresh.</summary>
    private void ExpandAllUnder(NodeViewModel node)
    {
        const int cap = 400;
        int expanded = 0;
        var queue = new Queue<NodeViewModel>();
        queue.Enqueue(node);
        while (queue.Count > 0 && expanded < cap)
        {
            NodeViewModel n = queue.Dequeue();
            if (n.Children is null) continue;
            if (!n.IsExpanded) { n.ExpandSilently(); expanded++; }
            foreach (var child in n.Children) queue.Enqueue(child);
        }
        _vm.Status = queue.Count > 0
            ? $"Expanded {expanded} branches (capped — expand deeper branches manually)"
            : $"Expanded {expanded} branches";
    }

    // ----- selection / navigation plumbing -----

    private void SelectNode(NodeViewModel node)
    {
        node.IsSelected = true;
        // container generation is async; bring it into view once layout has caught up
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ContainerFor(node) is { } item)
            {
                item.BringIntoView();
                item.Focus();
            }
        });
    }

    /// <summary>Walk the ItemContainerGenerator chain root→node to find the TreeViewItem.</summary>
    private TreeViewItem? ContainerFor(NodeViewModel node)
    {
        var chain = new List<NodeViewModel>();
        for (NodeViewModel? n = node; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        ItemsControl parent = Tree;
        foreach (var vm in chain)
        {
            parent.UpdateLayout();
            if (parent.ItemContainerGenerator.ContainerFromItem(vm) is not TreeViewItem item) return null;
            parent = item;
        }
        return parent as TreeViewItem;
    }

    /// <summary>Expand ancestors down to a data path, then select it (find results, watch jumps).</summary>
    private void JumpToPath(string root, string path)
    {
        if (_vm.TreeRoot is not { } tree || tree.Root != root)
        {
            _vm.Status = $"Load {root} first to jump to results";
            return;
        }
        NodeViewModel current = tree;
        tree.IsExpanded = true;
        while (current.Path != path && current.Children is not null)
        {
            NodeViewModel? next = current.Children.FirstOrDefault(c =>
                path == c.Path ||
                path.StartsWith(c.Path + ".", StringComparison.Ordinal) ||
                path.StartsWith(c.Path + "[", StringComparison.Ordinal));
            if (next is null) break;
            if (next.Path != path) next.IsExpanded = true;   // materializes children
            current = next;
        }
        if (current.Path == path) SelectNode(current);
        else _vm.Status = $"Path not found (it may have changed): {path}";
    }

    // ----- new context-menu handlers -----

    private async void OnQuickAddMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not { } node || (sender as MenuItem)?.Tag is not string wire) return;
        NbtType type = NbtTypes.FromWire(wire);
        NbtNode value = type switch
        {
            NbtType.Compound => new NbtNode(type) { Children = [] },
            NbtType.List => new NbtNode(type) { Items = [] },
            NbtType.String => new NbtNode(type) { Scalar = "" },
            _ => new NbtNode(type) { Scalar = "0" },
        };
        await _vm.AddParsedAsync(node, "", value);
    }

    private void OnEditSnbtMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) OpenEditSnbt(node);
    }

    private void OnAddSnbtMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) OpenAddSnbt(node);
    }

    private async void OnPasteMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await PasteIntoAsync(node);
    }

    private async void OnDuplicateMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await _vm.DuplicateAsync(node);
    }

    private void OnRenameMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) OpenRename(node);
    }

    private async void OnMoveUpMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await MoveSelectedAsync(node, -1);
    }

    private async void OnMoveDownMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await MoveSelectedAsync(node, +1);
    }

    private async void OnCutMenu(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) await CutNodeAsync(node);
    }

    // ----- dialogs / windows -----

    private async void OpenEditSnbt(NodeViewModel node)
    {
        if (!node.CanEditSnbt) { _vm.Status = "SNBT editing isn't available here"; return; }
        var dialog = new EditSnbtWindow($"Edit {(node.Name.Length > 0 ? node.Name : node.Root)} as SNBT",
                                        SnbtWriter.WriteIndented(node.Node), node.Type, allowNamed: false)
        { Owner = this };
        if (dialog.ShowDialog() == true && dialog.ParsedValue is { } parsed)
            await _vm.SetSubtreeAsync(node, parsed);
    }

    private async void OpenAddSnbt(NodeViewModel node)
    {
        if (!node.CanAddChild) { _vm.Status = "Adding works on player compounds and lists"; return; }
        var dialog = new EditSnbtWindow($"Add SNBT into {(node.Path.Length > 0 ? node.Path : node.Root)}",
                                        "", requiredType: null, allowNamed: true)
        { Owner = this };
        if (dialog.ShowDialog() == true && dialog.ParsedValue is { } parsed)
            await _vm.AddParsedAsync(node, dialog.ParsedName, parsed);
    }

    private async void OpenRename(NodeViewModel node)
    {
        if (!node.CanRename) { _vm.Status = "Only compound keys on players can be renamed"; return; }
        var dialog = new RenameWindow(node.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _vm.RenameAsync(node, dialog.NewName);
    }

    private HelpWindow? _helpWindow;

    private void OnOpenHelp(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is { IsLoaded: true })
        {
            _helpWindow.Activate();
            return;
        }
        _helpWindow = new HelpWindow { Owner = this };
        _helpWindow.Show();
    }

    private FindWindow? _findWindow;

    private void OpenFindWindow()
    {
        if (_findWindow is { IsLoaded: true })
        {
            _findWindow.Activate();
            _findWindow.FocusQuery();
            return;
        }
        _findWindow = new FindWindow(_vm, _settings, JumpToPath) { Owner = this };
        _findWindow.Show();
    }

    // ----- watches panel -----

    private async void OnRemoveWatch(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WatchItemViewModel watch)
            await _vm.RemoveWatchAsync(watch);
    }

    private void OnWatchRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is WatchItemViewModel watch)
        {
            JumpToPath(watch.Root, watch.Path);
            e.Handled = true;
        }
    }

    // ----- tree zoom (Ctrl+wheel, persisted) -----

    private void OnTreeWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        double size = Math.Clamp(Tree.FontSize + (e.Delta > 0 ? 1 : -1), 8, 32);
        Tree.FontSize = size;
        _settings.TreeFontSize = size;
    }
}
