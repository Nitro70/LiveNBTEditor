using System.Text.Json;
using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class NbtNodeJsonTests
{
    private const string Sample = """
        {"t":"compound","v":{
            "b":{"t":"byte","v":-2},
            "l":{"t":"long","v":"9223372036854775807"},
            "d":{"t":"double","v":"0.1"},
            "f":{"t":"float","v":1.5},
            "s":{"t":"string","v":"hi"},
            "pos":{"t":"list","v":[{"t":"double","v":"10.5"},{"t":"double","v":"64.0"}]},
            "la":{"t":"long_array","v":["-9223372036854775808","7"]},
            "ia":{"t":"int_array","v":[4,5]}
        }}
        """;

    [Fact]
    public void ParsesEveryType()
    {
        NbtNode node = NbtNodeJson.Parse(JsonDocument.Parse(Sample).RootElement);
        Assert.Equal(NbtType.Compound, node.Type);
        Assert.Equal("-2", node.Child("b")!.Scalar);
        Assert.Equal(NbtType.Long, node.Child("l")!.Type);
        Assert.Equal("9223372036854775807", node.Child("l")!.Scalar);
        Assert.Equal("0.1", node.Child("d")!.Scalar);
        Assert.Equal(2, node.Child("pos")!.Items!.Count);
        Assert.Equal(NbtType.LongArray, node.Child("la")!.Type);
        Assert.Equal("-9223372036854775808", node.Child("la")!.Items![0].Scalar);
        Assert.Equal("4", node.Child("ia")!.Items![0].Scalar);
    }

    [Fact]
    public void RoundTripsExactly()
    {
        NbtNode node = NbtNodeJson.Parse(JsonDocument.Parse(Sample).RootElement);
        string json = NbtNodeJson.ToJsonString(node);
        NbtNode again = NbtNodeJson.Parse(JsonDocument.Parse(json).RootElement);
        // long precision survives a full round trip
        Assert.Equal("9223372036854775807", again.Child("l")!.Scalar);
        Assert.Equal(NbtNodeJson.ToJsonString(node), NbtNodeJson.ToJsonString(again));
    }

    [Fact]
    public void WriterEmitsCorrectWireTypes()
    {
        var node = new NbtNode(NbtType.Long) { Scalar = "42" };
        Assert.Equal("""{"t":"long","v":"42"}""", NbtNodeJson.ToJsonString(node));
        var i = new NbtNode(NbtType.Int) { Scalar = "42" };
        Assert.Equal("""{"t":"int","v":42}""", NbtNodeJson.ToJsonString(i));
    }

    [Fact]
    public void RejectsUnknownType()
    {
        Assert.Throws<FormatException>(() =>
            NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"nope","v":1}""").RootElement));
    }

    [Fact]
    public void WireNamesMapBothWays()
    {
        Assert.Equal(NbtType.ByteArray, NbtTypes.FromWire("byte_array"));
        Assert.Equal("byte_array", NbtTypes.ToWire(NbtType.ByteArray));
    }

    [Fact]
    public void RejectsOutOfRangeAndWrongKindScalars()
    {
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"byte","v":300}""").RootElement));
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"short","v":70000}""").RootElement));
        Assert.Equal("5", NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"int","v":"5"}""").RootElement).Scalar);
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"byte","v":true}""").RootElement));
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"byte_array","v":[1,300]}""").RootElement));
    }

    [Fact]
    public void RejectsMalformedTypeFieldWithFormatException()
    {
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":123,"v":1}""").RootElement));
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"list","v":5}""").RootElement));
        Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse("""{"t":null,"v":1}""").RootElement));
    }

    [Fact]
    public void ErrorMessagesAreTruncated()
    {
        string huge = """{"t":"nope","v":""" + "\"" + new string('x', 100_000) + "\"}";
        // unknown type throws before echoing v; use missing-v shape to exercise the echo path
        string hugeNode = """{"x":""" + "\"" + new string('x', 100_000) + "\"}";
        var e = Assert.Throws<FormatException>(() => NbtNodeJson.Parse(JsonDocument.Parse(hugeNode).RootElement));
        Assert.True(e.Message.Length < 400, $"message length {e.Message.Length}");
    }

    [Fact]
    public void WriteFailuresAreFormatExceptionsWithContext()
    {
        var bad = new NbtNode(NbtType.Byte) { Scalar = "garbage" };
        var e = Assert.Throws<FormatException>(() => NbtNodeJson.ToJsonString(bad));
        Assert.Contains("byte", e.Message);
        var nullStr = new NbtNode(NbtType.String);
        Assert.Throws<FormatException>(() => NbtNodeJson.ToJsonString(nullStr));
    }

    [Fact]
    public void IntArrayStillParsesNumbersAndLongArrayStrings()
    {
        var n = NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"int_array","v":[4,5]}""").RootElement);
        Assert.Equal("4", n.Items![0].Scalar);
        var la = NbtNodeJson.Parse(JsonDocument.Parse("""{"t":"long_array","v":["7"]}""").RootElement);
        Assert.Equal("7", la.Items![0].Scalar);
    }
}
