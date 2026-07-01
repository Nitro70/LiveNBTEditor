using System.Collections.ObjectModel;
using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;
using static LiveNBT.App.Inventory.InventoryNbt;

namespace LiveNBT.App.Inventory;

public sealed record Enchantment(string Id, int Level);

/// <summary>One slot's editable item state. Raises change notifications so the grid + editor repaint.</summary>
public sealed class SlotItem(int slotNumber) : ViewModelBase
{
    public const string EnchComponent = "minecraft:enchantments";

    private string _itemId = "";
    private int _count = 1;

    public int SlotNumber { get; } = slotNumber;
    public bool IsEmpty => _itemId.Length == 0;
    public string ItemId { get => _itemId; set { if (Set(ref _itemId, value)) Raise(nameof(IsEmpty)); } }
    public int Count { get => _count; set => Set(ref _count, value); }
    public ObservableCollection<Enchantment> Enchantments { get; } = [];

    /// <summary>Components other than enchantments, preserved verbatim across edits.</summary>
    private NbtNode? _otherComponents;

    public void LoadFrom(NbtNode? itemNode)
    {
        Enchantments.Clear();
        _otherComponents = null;
        NbtNode? id = itemNode?.Child("id");
        if (itemNode is null || id is null)
        {
            ItemId = "";
            Count = 1;
            return;
        }
        ItemId = id.Scalar ?? "";
        Count = ReadInt(itemNode.Child("count")) ?? 1;

        NbtNode? components = itemNode.Child("components");
        if (components is { Children: { } comps })
        {
            var kept = new List<KeyValuePair<string, NbtNode>>();
            foreach (var (key, value) in comps)
            {
                if (key == EnchComponent) ReadEnchantments(value);
                else kept.Add(new(key, value));
            }
            if (kept.Count > 0) _otherComponents = Compound(kept);
        }
    }

    private void ReadEnchantments(NbtNode enchComponent)
    {
        // assumed 26.1.2 shape: { levels: { "<id>": <int> } } — confirmed in Task 14
        NbtNode levels = enchComponent.Child("levels") ?? enchComponent;   // tolerate a flat map too
        if (levels.Children is null) return;
        foreach (var (id, lvl) in levels.Children)
            Enchantments.Add(new Enchantment(id, ReadInt(lvl) ?? 1));
    }

    /// <summary>The item compound to `set`, or null when the slot is empty (caller `delete`s).</summary>
    public NbtNode? BuildItemNode()
    {
        if (IsEmpty) return null;
        var children = new List<KeyValuePair<string, NbtNode>>
        {
            new("id", Str(ItemId)),
            new("count", Int(Math.Clamp(Count, 1, 99))),
        };
        NbtNode? components = BuildComponents();
        if (components is not null) children.Add(new("components", components));
        return Compound(children);
    }

    private NbtNode? BuildComponents()
    {
        var comps = new List<KeyValuePair<string, NbtNode>>();
        if (_otherComponents?.Children is { } other) comps.AddRange(other);
        if (Enchantments.Count > 0) comps.Add(new(EnchComponent, BuildEnchantmentsComponent()));
        return comps.Count > 0 ? Compound(comps) : null;
    }

    /// <summary>The single place the enchantments component shape lives (see Task 14).</summary>
    public NbtNode BuildEnchantmentsComponent()
    {
        // 26.1.2: the enchantments component is a flat { "<id>": <level> } map; level codec is intRange(1,255).
        return Compound(Enchantments.Select(e =>
            new KeyValuePair<string, NbtNode>(e.Id, Int(Math.Clamp(e.Level, 1, 255)))).ToList());
    }
}
