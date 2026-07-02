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
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            FilterBox.Focus();
            FilterBox.SelectAll();
        }
        else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnOpenInventory(sender, new RoutedEventArgs());
        }
    }

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
        if (NodeOf(sender) is { } node &&
            MessageBox.Show($"Delete {node.Path}?", "LiveNBT", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await _vm.DeleteAsync(node);
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
            string path = result.Name.Length == 0
                ? node.Path
                : node.Path.Length == 0 ? result.Name : $"{node.Path}.{result.Name}";
            await _vm.AddAsync(node.Root, path, result.Value);
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
        try
        {
            if (NodeOf(sender) is { } node) Clipboard.SetText(SnbtWriter.Write(node.Node));
        }
        catch (Exception ex) { _vm.Status = $"copy failed: {ex.Message}"; }
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
}
