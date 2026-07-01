using LiveNBT.App.Inventory;
using Xunit;

namespace LiveNBT.Tests;

public class InventoryFilterTests
{
    private static readonly string[] Ids =
        ["minecraft:stone", "minecraft:diamond_sword", "minecraft:diamond", "minecraft:oak_log"];

    [Fact]
    public void EmptyQueryReturnsAllUpToLimit()
    {
        Assert.Equal(4, InventoryFilter.Filter(Ids, "").Count);
        Assert.Equal(2, InventoryFilter.Filter(Ids, "", limit: 2).Count);
    }

    [Fact]
    public void MatchesIdSubstringCaseInsensitive()
    {
        var r = InventoryFilter.Filter(Ids, "DIAMOND");
        Assert.Equal(["minecraft:diamond_sword", "minecraft:diamond"], r);
    }

    [Fact]
    public void MatchesPrettifiedName()
    {
        Assert.Contains("minecraft:oak_log", InventoryFilter.Filter(Ids, "oak log"));
    }
}
