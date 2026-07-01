using System.Windows;
using System.Windows.Threading;
using LiveNBT.App.Inventory;
using LiveNBT.App.ViewModels;

namespace LiveNBT.App;

public partial class InventoryWindow : Window
{
    private readonly InventoryViewModel _vm;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };

    public InventoryWindow(InventoryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _poll.Tick += async (_, _) => { if (LiveRefreshBox.IsChecked == true) await _vm.RefreshAsync(); };
        Loaded += async (_, _) => { await _vm.LoadAsync(); _poll.Start(); };
        Closed += (_, _) => _poll.Stop();
    }

    private void OnSlotClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SlotItem slot) _vm.SelectedSlot = slot;
    }

    private void OnAddEnchant(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSlot is { } slot) AddEnchantFromBox(slot);
    }

    /// <summary>Add whatever is in the enchant box (deduped by id) and clear it. Shared by the Add
    /// button and Apply, so a typed-but-not-"Added" enchant still gets applied.</summary>
    private void AddEnchantFromBox(SlotItem slot)
    {
        string id = EnchBox.Text.Trim();
        if (id.Length == 0) return;
        int level = int.TryParse(EnchLevelBox.Text, out int l) ? l : 1;
        if (!slot.Enchantments.Any(en => string.Equals(en.Id, id, StringComparison.OrdinalIgnoreCase)))
            slot.Enchantments.Add(new Enchantment(id, level));
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
