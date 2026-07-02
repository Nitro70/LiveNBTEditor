using System.Windows;
using System.Windows.Threading;
using LiveNBT.App.Inventory;
using LiveNBT.App.ViewModels;

namespace LiveNBT.App;

public partial class InventoryWindow : Window
{
    private readonly InventoryViewModel _vm;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _closed;
    private bool _refreshBusy;

    public InventoryWindow(InventoryViewModel vm)
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        _vm = vm;
        DataContext = vm;
        _poll.Tick += async (_, _) =>
        {
            if (_refreshBusy || LiveRefreshBox.IsChecked != true) return;   // never pile up slow refreshes
            _refreshBusy = true;
            try { await _vm.RefreshAsync(); }
            finally { _refreshBusy = false; }
        };
        // don't start the poll if the window was closed while the initial load was still in flight
        Loaded += async (_, _) => { await _vm.LoadAsync(); if (!_closed) _poll.Start(); };
        Closed += (_, _) => { _closed = true; _poll.Stop(); };
    }

    private void OnSlotClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SlotItem slot) _vm.SelectedSlot = slot;
    }

    private void OnAddEnchant(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSlot is { } slot) AddEnchantFromBox(slot);
    }

    /// <summary>Add whatever is in the enchant box (or update the level of an existing entry) and
    /// clear it. Shared by the Add button and Apply, so a typed-but-not-"Added" enchant still applies.</summary>
    private void AddEnchantFromBox(SlotItem slot)
    {
        string id = EnchBox.Text.Trim();
        if (id.Length == 0) return;
        int level = int.TryParse(EnchLevelBox.Text, out int l) ? l : 1;
        Enchantment? existing = slot.Enchantments.FirstOrDefault(
            en => string.Equals(en.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            slot.Enchantments.Add(new Enchantment(id, level));
        else if (existing.Level != level)
            slot.Enchantments[slot.Enchantments.IndexOf(existing)] = existing with { Level = level };
        EnchBox.Text = "";
    }

    private void OnRemoveEnchant(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Enchantment ench && _vm.SelectedSlot is { } slot)
            slot.Enchantments.Remove(ench);
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSlot is not { } slot) return;
        AddEnchantFromBox(slot);   // also apply an enchant the user typed but didn't click "Add" for
        await _vm.ApplyAsync(slot);
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSlot is { } slot) await _vm.ClearAsync(slot);
    }
}
