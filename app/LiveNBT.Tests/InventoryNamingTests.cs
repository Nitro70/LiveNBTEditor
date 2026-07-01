using LiveNBT.App.Inventory;
using Xunit;

namespace LiveNBT.Tests;

public class InventoryNamingTests
{
    [Theory]
    [InlineData("minecraft:diamond_sword", "Diamond Sword")]
    [InlineData("minecraft:stone", "Stone")]
    [InlineData("dirt", "Dirt")]
    [InlineData("mymod:cool_gadget_v2", "Cool Gadget V2")]
    public void PrettifiesIds(string id, string expected)
        => Assert.Equal(expected, InventoryNaming.Prettify(id));
}
