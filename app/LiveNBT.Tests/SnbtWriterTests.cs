using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class SnbtWriterTests
{
    [Fact]
    public void WritesScalarsWithSuffixes()
    {
        Assert.Equal("1b", SnbtWriter.Write(new NbtNode(NbtType.Byte) { Scalar = "1" }));
        Assert.Equal("300s", SnbtWriter.Write(new NbtNode(NbtType.Short) { Scalar = "300" }));
        Assert.Equal("42", SnbtWriter.Write(new NbtNode(NbtType.Int) { Scalar = "42" }));
        Assert.Equal("7L", SnbtWriter.Write(new NbtNode(NbtType.Long) { Scalar = "7" }));
        Assert.Equal("1.5f", SnbtWriter.Write(new NbtNode(NbtType.Float) { Scalar = "1.5" }));
        Assert.Equal("0.1d", SnbtWriter.Write(new NbtNode(NbtType.Double) { Scalar = "0.1" }));
    }

    [Fact]
    public void EscapesStrings()
    {
        Assert.Equal("\"hi \\\"there\\\"\"", SnbtWriter.Write(new NbtNode(NbtType.String) { Scalar = "hi \"there\"" }));
        Assert.Equal("\"a\\nb\\tc\"", SnbtWriter.Write(new NbtNode(NbtType.String) { Scalar = "a\nb\tc" }));
    }

    [Fact]
    public void WritesContainers()
    {
        var compound = new NbtNode(NbtType.Compound)
        {
            Children =
            [
                new("mayfly", new NbtNode(NbtType.Byte) { Scalar = "1" }),
                new("weird key", new NbtNode(NbtType.Int) { Scalar = "2" }),
            ],
        };
        Assert.Equal("{mayfly:1b,\"weird key\":2}", SnbtWriter.Write(compound));

        var list = new NbtNode(NbtType.List)
        {
            Items = [new NbtNode(NbtType.Double) { Scalar = "1.5" }, new NbtNode(NbtType.Double) { Scalar = "2.5" }],
        };
        Assert.Equal("[1.5d,2.5d]", SnbtWriter.Write(list));

        var array = new NbtNode(NbtType.IntArray)
        {
            Items = [new NbtNode(NbtType.Int) { Scalar = "4" }, new NbtNode(NbtType.Int) { Scalar = "5" }],
        };
        Assert.Equal("[I;4,5]", SnbtWriter.Write(array));
    }
}
