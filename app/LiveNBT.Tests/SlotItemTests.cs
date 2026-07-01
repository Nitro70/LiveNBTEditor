using System.Globalization;
using LiveNBT.App.Inventory;
using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class SlotItemTests
{
    private static NbtNode Str(string s) => new(NbtType.String) { Scalar = s };
    private static NbtNode Int(int i) => new(NbtType.Int) { Scalar = i.ToString(CultureInfo.InvariantCulture) };

    private static NbtNode ItemNode(string id, int count, NbtNode? components = null)
    {
        var children = new List<KeyValuePair<string, NbtNode>>
        {
            new("id", Str(id)),
            new("count", Int(count)),
        };
        if (components is not null) children.Add(new("components", components));
        return new NbtNode(NbtType.Compound) { Children = children };
    }

    private static NbtNode Enchantments(params (string Id, int Level)[] entries)
    {
        var map = new NbtNode(NbtType.Compound)
        {
            Children = entries.Select(e => new KeyValuePair<string, NbtNode>(e.Id, Int(e.Level))).ToList(),
        };
        return new NbtNode(NbtType.Compound)
        {
            Children = [new("minecraft:enchantments", map)],
        };
    }

    [Fact]
    public void EmptyCompoundLoadsAsEmptySlot()
    {
        var s = new SlotItem(0);
        s.LoadFrom(new NbtNode(NbtType.Compound) { Children = [] });
        Assert.True(s.IsEmpty);
        Assert.Null(s.BuildItemNode());
    }

    [Fact]
    public void LoadsIdCountAndEnchantments()
    {
        var s = new SlotItem(0);
        s.LoadFrom(ItemNode("minecraft:diamond_sword", 1, Enchantments(("minecraft:sharpness", 5))));
        Assert.False(s.IsEmpty);
        Assert.Equal("minecraft:diamond_sword", s.ItemId);
        Assert.Equal(1, s.Count);
        Assert.Single(s.Enchantments);
        Assert.Equal("minecraft:sharpness", s.Enchantments[0].Id);
        Assert.Equal(5, s.Enchantments[0].Level);
    }

    [Fact]
    public void CountClampsTo99()
    {
        var s = new SlotItem(0);
        s.LoadFrom(ItemNode("minecraft:stone", 1));
        s.Count = 999;
        Assert.Equal("99", s.BuildItemNode()!.Child("count")!.Scalar);
    }

    [Fact]
    public void BuildPreservesNonEnchantmentComponents()
    {
        var components = new NbtNode(NbtType.Compound) { Children = [new("minecraft:damage", Int(42))] };
        var s = new SlotItem(0);
        s.LoadFrom(ItemNode("minecraft:diamond_pickaxe", 1, components));
        s.Enchantments.Add(new Enchantment("minecraft:efficiency", 100));

        var built = s.BuildItemNode()!.Child("components")!;
        Assert.NotNull(built.Child("minecraft:damage"));        // preserved
        Assert.NotNull(built.Child("minecraft:enchantments"));  // added
    }

    [Fact]
    public void RemovingAllEnchantmentsDropsTheComponent()
    {
        var s = new SlotItem(0);
        s.LoadFrom(ItemNode("minecraft:diamond_sword", 1, Enchantments(("minecraft:sharpness", 5))));
        s.Enchantments.Clear();
        Assert.Null(s.BuildItemNode()!.Child("components"));    // only component was enchantments -> dropped
    }

    [Fact]
    public void SettingItemIdMakesSlotNonEmpty()
    {
        var s = new SlotItem(0);
        Assert.True(s.IsEmpty);
        s.ItemId = "minecraft:stone";
        Assert.False(s.IsEmpty);
        Assert.NotNull(s.BuildItemNode());
    }
}
