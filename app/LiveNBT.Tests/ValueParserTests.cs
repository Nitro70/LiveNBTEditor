using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class ValueParserTests
{
    [Theory]
    [InlineData(NbtType.Byte, "1", "1")]
    [InlineData(NbtType.Byte, "true", "1")]
    [InlineData(NbtType.Byte, "false", "0")]
    [InlineData(NbtType.Byte, "-128", "-128")]
    [InlineData(NbtType.Short, "300", "300")]
    [InlineData(NbtType.Int, "-2147483648", "-2147483648")]
    [InlineData(NbtType.Long, "9223372036854775807", "9223372036854775807")]
    [InlineData(NbtType.Float, "1.5", "1.5")]
    [InlineData(NbtType.Double, "0.1", "0.1")]
    [InlineData(NbtType.String, "anything goes", "anything goes")]
    public void AcceptsValidInput(NbtType type, string input, string expected)
    {
        Assert.True(ValueParser.TryParse(type, input, out string normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(NbtType.Byte, "128")]      // out of range
    [InlineData(NbtType.Byte, "abc")]
    [InlineData(NbtType.Short, "70000")]
    [InlineData(NbtType.Int, "1.5")]
    [InlineData(NbtType.Long, "abc")]
    [InlineData(NbtType.Float, "NaN")]
    [InlineData(NbtType.Double, "")]
    public void RejectsInvalidInput(NbtType type, string input)
    {
        Assert.False(ValueParser.TryParse(type, input, out _, out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ContainerTypesAreNotParseable()
    {
        Assert.False(ValueParser.TryParse(NbtType.Compound, "{}", out _, out _));
    }

    [Theory]
    [InlineData(NbtType.Int, " 42 ", "42")]
    [InlineData(NbtType.Byte, " true ", "1")]
    [InlineData(NbtType.Float, " 1.5 ", "1.5")]
    public void StrayWhitespaceIsTrimmed(NbtType type, string input, string expected)
    {
        Assert.True(ValueParser.TryParse(type, input, out string normalized, out _));
        Assert.Equal(expected, normalized);
    }
}
