using System.Collections.ObjectModel;
using LiveNBT.App.Inventory;
using LiveNBT.App.Services;
using LiveNBT.Protocol;

namespace LiveNBT.App.ViewModels;

public sealed class InventoryViewModel : ViewModelBase
{
    private readonly IServerSession _session;
    private readonly string _root;
    private SlotItem? _selectedSlot;
    private string _status = "";

    public InventoryViewModel(IServerSession session, string root,
        IReadOnlyList<string> itemIds, IReadOnlyList<string> enchantmentIds)
    {
        _session = session;
        _root = root;
        ItemIds = itemIds;
        EnchantmentIds = enchantmentIds;
        FilteredItemIds = itemIds.Take(200).ToList();
        FilteredEnchantIds = enchantmentIds.Take(200).ToList();

        foreach (int n in InventoryRegions.SlotOrder) Slots.Add(new SlotItem(n));
        Hotbar = Group(InventoryRegions.Hotbar);
        Main = Group(InventoryRegions.Main);
        Armor = Group(InventoryRegions.Armor);
        Offhand = Group(InventoryRegions.Offhand);
    }

    public ObservableCollection<SlotItem> Slots { get; } = [];
    public IReadOnlyList<SlotItem> Hotbar { get; }
    public IReadOnlyList<SlotItem> Main { get; }
    public IReadOnlyList<SlotItem> Armor { get; }
    public IReadOnlyList<SlotItem> Offhand { get; }

    public IReadOnlyList<string> ItemIds { get; }
    public IReadOnlyList<string> EnchantmentIds { get; }
    public List<string> FilteredItemIds { get; private set; }
    public List<string> FilteredEnchantIds { get; private set; }

    public SlotItem? SelectedSlot { get => _selectedSlot; set => Set(ref _selectedSlot, value); }
    public string Status { get => _status; set => Set(ref _status, value); }

    private List<SlotItem> Group(int[] numbers) => numbers.Select(n => Slots.First(s => s.SlotNumber == n)).ToList();

    public void SetItemQuery(string query)
    {
        FilteredItemIds = InventoryFilter.Filter(ItemIds, query);
        Raise(nameof(FilteredItemIds));
    }

    public void SetEnchantQuery(string query)
    {
        FilteredEnchantIds = InventoryFilter.Filter(EnchantmentIds, query);
        Raise(nameof(FilteredEnchantIds));
    }

    public async Task LoadAsync() => await Reload(skipSelected: false, successStatus: "Loaded inventory");

    /// <summary>Background refresh — leaves the slot the user is editing untouched, and stays silent
    /// so the 2s poll doesn't overwrite the last Apply/Clear message.</summary>
    public async Task RefreshAsync() => await Reload(skipSelected: true, successStatus: null);

    private async Task Reload(bool skipSelected, string? successStatus)
    {
        try
        {
            ServerMessage reply = await _session.RequestAsync("get", _root, "");
            if (!reply.Ok || reply.Value is null) { Status = $"load failed: {reply.Error}"; return; }
            foreach (SlotItem slot in Slots)
            {
                if (skipSelected && ReferenceEquals(slot, SelectedSlot)) continue;
                slot.LoadFrom(reply.Value.Child(slot.SlotNumber.ToString()));
            }
            if (successStatus is not null) Status = successStatus;
        }
        catch (Exception e) { Status = $"load failed: {e.Message}"; }
    }

    public async Task ApplyAsync(SlotItem slot)
    {
        try
        {
            NbtNode? node = slot.BuildItemNode();
            ServerMessage reply = node is null
                ? await _session.RequestAsync("delete", _root, $"slot.{slot.SlotNumber}")
                : await _session.RequestAsync("set", _root, $"slot.{slot.SlotNumber}", node);
            Status = reply.Ok ? $"Applied slot {slot.SlotNumber}" : (reply.Error ?? "edit rejected");
        }
        catch (Exception e) { Status = $"apply failed: {e.Message}"; }
    }

    public async Task ClearAsync(SlotItem slot)
    {
        try
        {
            ServerMessage reply = await _session.RequestAsync("delete", _root, $"slot.{slot.SlotNumber}");
            if (reply.Ok) slot.LoadFrom(null);
            Status = reply.Ok ? $"Cleared slot {slot.SlotNumber}" : (reply.Error ?? "clear rejected");
        }
        catch (Exception e) { Status = $"clear failed: {e.Message}"; }
    }
}
