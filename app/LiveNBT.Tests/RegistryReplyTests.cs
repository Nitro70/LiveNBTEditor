using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class RegistryReplyTests
{
    [Fact]
    public void ParsesItemsAndEnchantments()
    {
        var (items, ench) = RegistryReply.Parse(
            """{"items":["minecraft:stone","minecraft:diamond_sword"],"enchantments":["minecraft:sharpness"]}""");
        Assert.Equal(["minecraft:stone", "minecraft:diamond_sword"], items);
        Assert.Equal(["minecraft:sharpness"], ench);
    }

    [Fact]
    public void MissingArraysYieldEmpty()
    {
        var (items, ench) = RegistryReply.Parse("""{"items":[]}""");
        Assert.Empty(items);
        Assert.Empty(ench);
    }
}
