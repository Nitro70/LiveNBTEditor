using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;

namespace LiveNBT.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TreeRoot))
                Tree.ItemsSource = _vm.TreeRoot is null ? null : new[] { _vm.TreeRoot };
        };
        _pollTimer.Tick += async (_, _) => await _vm.RefreshTreeAsync();
        _pollTimer.Start();
    }

    private async void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        => await _vm.DisconnectAsync();

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
        try
        {
            // v1: profiles are edited as JSON in %APPDATA%\LiveNBT\profiles.json
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveNBT");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, "profiles.json");
            if (!System.IO.File.Exists(file))
                System.IO.File.WriteAllText(file,
                    """[{"Name":"Singleplayer","Host":"127.0.0.1","Port":25599,"Token":"PASTE-TOKEN-HERE"}]""");
            System.Diagnostics.Process.Start("notepad.exe", file);
            MessageBox.Show("Edit profiles.json, save, then click Profiles… again to reload.", "LiveNBT");
            _vm.ReloadProfiles();
        }
        catch (Exception ex) { _vm.Status = $"profiles failed: {ex.Message}"; }
    }
}
