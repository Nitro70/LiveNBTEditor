using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class SnbtParserTests
{
    private static NbtNode Parse(string text)
    {
        Assert.True(SnbtParser.TryParse(text, out var node, out var error), $"parse failed: {error}");
        return node!;
    }

    // ---------- scalars ----------

    [Theory]
    [InlineData("1b", NbtType.Byte, "1")]
    [InlineData("-128b", NbtType.Byte, "-128")]
    [InlineData("127B", NbtType.Byte, "127")]
    [InlineData("300s", NbtType.Short, "300")]
    [InlineData("-32768S", NbtType.Short, "-32768")]
    [InlineData("42", NbtType.Int, "42")]
    [InlineData("0", NbtType.Int, "0")]
    [InlineData("-2147483648", NbtType.Int, "-2147483648")]
    [InlineData("2147483647", NbtType.Int, "2147483647")]
    [InlineData("7L", NbtType.Long, "7")]
    [InlineData("7l", NbtType.Long, "7")]
    [InlineData("9223372036854775807L", NbtType.Long, "9223372036854775807")]
    [InlineData("-9223372036854775808L", NbtType.Long, "-9223372036854775808")]
    [InlineData("1.5f", NbtType.Float, "1.5")]
    [InlineData("1.5F", NbtType.Float, "1.5")]
    [InlineData("1f", NbtType.Float, "1")]
    [InlineData("0.1d", NbtType.Double, "0.1")]
    [InlineData("0.1D", NbtType.Double, "0.1")]
    [InlineData("2.5", NbtType.Double, "2.5")]
    [InlineData(".5", NbtType.Double, ".5")]
    [InlineData("3.", NbtType.Double, "3.")]
    [InlineData("1.5e3", NbtType.Double, "1.5e3")]
    [InlineData("-1.5E-3d", NbtType.Double, "-1.5E-3")]
    public void ParsesNumericScalars(string text, NbtType type, string scalar)
    {
        var node = Parse(text);
        Assert.Equal(type, node.Type);
        Assert.Equal(scalar, node.Scalar);
    }

    [Theory]
    [InlineData("true", "1")]
    [InlineData("false", "0")]
    public void ParsesBooleansAsBytes(string text, string scalar)
    {
        var node = Parse(text);
        Assert.Equal(NbtType.Byte, node.Type);
        Assert.Equal(scalar, node.Scalar);
    }

    [Fact]
    public void NormalizesIntegerSigns()
    {
        Assert.Equal("5", Parse("+5").Scalar);
        Assert.Equal("5", Parse("+5b").Scalar);
        Assert.Equal("0", Parse("-0").Scalar);
    }

    [Fact]
    public void PreservesLongAndDoubleExactness()
    {
        // longs never pass through floating point; doubles keep the original text verbatim
        Assert.Equal("9223372036854775807", Parse("9223372036854775807L").Scalar);
        Assert.Equal("0.30000000000000004", Parse("0.30000000000000004d").Scalar);
        Assert.Equal("1.0000000000000002", Parse("1.0000000000000002").Scalar);
        Assert.Equal("20.0", Parse("20.0f").Scalar); // trailing zero kept, not re-rendered as "20"
    }

    // vanilla rule: unquoted tokens that are not valid numbers fall back to strings
    [Theory]
    [InlineData("9999999999")] // bare integer out of int range
    [InlineData("007")]        // integer patterns reject leading zeros
    [InlineData("01b")]
    [InlineData("1.5x")]
    [InlineData("--3")]
    [InlineData("1e5")]        // bare decimal requires the dot
    [InlineData("NaNd")]
    [InlineData("3bees")]
    public void NonNumberTokensFallBackToStrings(string text)
    {
        var node = Parse(text);
        Assert.Equal(NbtType.String, node.Type);
        Assert.Equal(text, node.Scalar);
    }

    // ---------- strings ----------

    [Theory]
    [InlineData("\"hi\"", "hi")]
    [InlineData("'hi'", "hi")]
    [InlineData("\"\"", "")]
    [InlineData("''", "")]
    [InlineData("\"a\\\"b\"", "a\"b")]
    [InlineData("'a\\'b'", "a'b")]
    [InlineData("'a\"b'", "a\"b")]
    [InlineData("\"a'b\"", "a'b")]
    [InlineData("\"back\\\\slash\"", "back\\slash")]
    [InlineData("\"a\\nb\\tc\\rd\"", "a\nb\tc\rd")]
    [InlineData("\"\\u0041\\u00e9\"", "Aé")]
    [InlineData("\"héllo wörld ✨\"", "héllo wörld ✨")]
    public void ParsesQuotedStrings(string text, string value)
    {
        var node = Parse(text);
        Assert.Equal(NbtType.String, node.Type);
        Assert.Equal(value, node.Scalar);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("minecraft.stone")]
    [InlineData("a-b.c_d+e")]
    [InlineData("TRUE")] // booleans are case-sensitive
    public void ParsesUnquotedStrings(string text)
    {
        var node = Parse(text);
        Assert.Equal(NbtType.String, node.Type);
        Assert.Equal(text, node.Scalar);
    }

    // ---------- compounds ----------

    [Fact]
    public void ParsesCompounds()
    {
        var node = Parse("{mayfly:1b,\"weird key\":2}");
        Assert.Equal(NbtType.Compound, node.Type);
        Assert.Equal(2, node.Children!.Count);
        Assert.Equal("mayfly", node.Children[0].Key);
        Assert.Equal(NbtType.Byte, node.Children[0].Value.Type);
        Assert.Equal("1", node.Children[0].Value.Scalar);
        Assert.Equal("weird key", node.Children[1].Key);
        Assert.Equal(NbtType.Int, node.Children[1].Value.Type);
        Assert.Equal("2", node.Children[1].Value.Scalar);
    }

    [Fact]
    public void ParsesQuotedKeys()
    {
        var node = Parse("{'a b':1b,\"c\\nd\":2s}");
        Assert.Equal("a b", node.Children![0].Key);
        Assert.Equal("c\nd", node.Children[1].Key);
    }

    [Fact]
    public void ParsesEmptyContainers()
    {
        Assert.Equal(NbtType.Compound, Parse("{}").Type);
        Assert.Empty(Parse("{}").Children!);
        Assert.Equal(NbtType.List, Parse("[]").Type);
        Assert.Empty(Parse("[]").Items!);
        Assert.Equal(NbtType.ByteArray, Parse("[B;]").Type);
        Assert.Empty(Parse("[B;]").Items!);
        Assert.Equal(NbtType.IntArray, Parse("[I;]").Type);
        Assert.Equal(NbtType.LongArray, Parse("[L;]").Type);
    }

    [Fact]
    public void ParsesNestedStructures()
    {
        var node = Parse("{a:{b:[{c:1b}]},d:[[1,2],[3]]}");
        var b = node.Child("a")!.Child("b")!;
        Assert.Equal(NbtType.List, b.Type);
        Assert.Equal("1", b.Items![0].Child("c")!.Scalar);
        var d = node.Child("d")!;
        Assert.Equal(NbtType.List, d.Items![0].Type);
        Assert.Equal("3", d.Items[1].Items![0].Scalar);
    }

    // ---------- lists ----------

    [Fact]
    public void ParsesLists()
    {
        var ints = Parse("[1,2,3]");
        Assert.Equal(NbtType.List, ints.Type);
        Assert.Equal(["1", "2", "3"], ints.Items!.Select(i => i.Scalar));
        Assert.All(ints.Items!, i => Assert.Equal(NbtType.Int, i.Type));

        var doubles = Parse("[1.5d, 2.5d]");
        Assert.All(doubles.Items!, i => Assert.Equal(NbtType.Double, i.Type));

        var strings = Parse("[\"a\", b]");
        Assert.All(strings.Items!, i => Assert.Equal(NbtType.String, i.Type));
    }

    // ---------- arrays ----------

    [Theory]
    [InlineData("[B;1b,2b]", NbtType.ByteArray, "1,2")]
    [InlineData("[B; 1, 2]", NbtType.ByteArray, "1,2")]          // suffixes may be omitted
    [InlineData("[B;1B,-128,127b]", NbtType.ByteArray, "1,-128,127")]
    [InlineData("[B;true,false]", NbtType.ByteArray, "1,0")]
    [InlineData("[I;4,5]", NbtType.IntArray, "4,5")]
    [InlineData("[I; -1, +2]", NbtType.IntArray, "-1,2")]
    [InlineData("[L;1l,2L,3]", NbtType.LongArray, "1,2,3")]
    [InlineData("[L;9223372036854775807L]", NbtType.LongArray, "9223372036854775807")]
    [InlineData("[ B ; 1 , 2 ]", NbtType.ByteArray, "1,2")]      // whitespace tolerant
    public void ParsesArrays(string text, NbtType type, string joined)
    {
        var node = Parse(text);
        Assert.Equal(type, node.Type);
        Assert.Equal(joined, string.Join(",", node.Items!.Select(i => i.Scalar)));
        Assert.All(node.Items!, i => Assert.Equal(NbtTypes.ArrayElementType(type), i.Type));
    }

    [Fact]
    public void ListStartingWithSingleLetterStringIsNotAnArray()
    {
        var node = Parse("[B, C]");
        Assert.Equal(NbtType.List, node.Type);
        Assert.Equal(["B", "C"], node.Items!.Select(i => i.Scalar));
        Assert.All(node.Items!, i => Assert.Equal(NbtType.String, i.Type));
    }

    // ---------- whitespace ----------

    [Fact]
    public void ToleratesWhitespaceEverywhere()
    {
        var node = Parse("  {  a  :  1b  ,  b  : [ 1 , 2 ] , c : { } }  ");
        Assert.Equal("1", node.Child("a")!.Scalar);
        Assert.Equal(2, node.Child("b")!.Items!.Count);
        Assert.Empty(node.Child("c")!.Children!);

        var multiline = Parse("{\n  a: 1b,\n  b: \"x\"\n}");
        Assert.Equal("x", multiline.Child("b")!.Scalar);
    }

    // ---------- TryParseNamed ----------

    [Fact]
    public void TryParseNamedParsesNbtStudioClipboardStrings()
    {
        Assert.True(SnbtParser.TryParseNamed("Health:20.0f", out var name, out var node, out _));
        Assert.Equal("Health", name);
        Assert.Equal(NbtType.Float, node!.Type);
        Assert.Equal("20.0", node.Scalar);
    }

    [Fact]
    public void TryParseNamedAcceptsQuotedNames()
    {
        Assert.True(SnbtParser.TryParseNamed("\"weird name\": {a: 1b}", out var name, out var node, out _));
        Assert.Equal("weird name", name);
        Assert.Equal(NbtType.Compound, node!.Type);

        Assert.True(SnbtParser.TryParseNamed("'single': [1.5d]", out name, out node, out _));
        Assert.Equal("single", name);
        Assert.Equal(NbtType.List, node!.Type);
    }

    [Fact]
    public void TryParseNamedAcceptsNumericNames()
    {
        Assert.True(SnbtParser.TryParseNamed("123:456", out var name, out var node, out _));
        Assert.Equal("123", name);
        Assert.Equal(NbtType.Int, node!.Type);
        Assert.Equal("456", node.Scalar);
    }

    [Fact]
    public void TryParseNamedFallsBackToUnnamedValues()
    {
        Assert.True(SnbtParser.TryParseNamed("20.0f", out var name, out var node, out _));
        Assert.Equal("", name);
        Assert.Equal(NbtType.Float, node!.Type);

        Assert.True(SnbtParser.TryParseNamed("{Health:20.0f}", out name, out node, out _));
        Assert.Equal("", name);
        Assert.Equal(NbtType.Compound, node!.Type);

        Assert.True(SnbtParser.TryParseNamed("\"just a string\"", out name, out node, out _));
        Assert.Equal("", name);
        Assert.Equal("just a string", node!.Scalar);
    }

    [Theory]
    [InlineData("Health:")]         // missing value
    [InlineData("Health: 1b junk")] // trailing junk after named value
    [InlineData("Health: @@")]
    [InlineData("")]
    public void TryParseNamedRejectsBadInput(string text)
    {
        Assert.False(SnbtParser.TryParseNamed(text, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ---------- errors ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{a:1b")]           // truncated compound
    [InlineData("[1,2")]            // truncated list
    [InlineData("[B;1,2")]          // truncated array
    [InlineData("\"unterminated")]
    [InlineData("'unterminated")]
    [InlineData("\"bad\\qescape\"")]
    [InlineData("\"bad\\u12\"")]    // short \u escape
    [InlineData("\"bad\\uzzzz\"")]  // non-hex \u escape
    [InlineData("\"dangling\\")]    // escape at end of input
    [InlineData("1b junk")]         // trailing junk
    [InlineData("{a:1b} extra")]
    [InlineData("@")]
    [InlineData("{a:}")]            // missing value
    [InlineData("{:1}")]            // missing key
    [InlineData("{a 1}")]           // missing colon
    [InlineData("{a:1,}")]          // trailing comma
    [InlineData("{a:1 b:2}")]       // missing comma
    [InlineData("[1,]")]
    [InlineData("[1 2]")]
    [InlineData("[X;1]")]           // wrong array prefix
    [InlineData("[b;1b]")]          // lowercase array prefix
    [InlineData("[B;1s]")]          // wrong element suffix
    [InlineData("[I;1b]")]
    [InlineData("[L;1.5]")]         // non-integer array element
    [InlineData("[B;300]")]         // array element out of range
    [InlineData("[B;\"1\"]")]       // quoted array element
    [InlineData("999b")]            // suffixed number out of range is an error, not a string
    [InlineData("40000s")]
    [InlineData("99999999999999999999L")]
    [InlineData("1.0e999")]         // overflows to infinity
    [InlineData("1.0e999f")]
    [InlineData("[1,2b]")]          // mixed list
    [InlineData("{a:1,a:2}")]       // duplicate key
    public void RejectsMalformedInput(string text)
    {
        Assert.False(SnbtParser.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("position", error);
    }

    [Fact]
    public void StrictParseThrowsWithPosition()
    {
        var ex = Assert.Throws<SnbtParseException>(() => SnbtParser.Parse("1b junk"));
        Assert.Equal(3, ex.Position);
        Assert.Contains("position 3", ex.Message);

        ex = Assert.Throws<SnbtParseException>(() => SnbtParser.Parse("{a:@}"));
        Assert.Equal(3, ex.Position);

        ex = Assert.Throws<SnbtParseException>(() => SnbtParser.Parse("[1, 2b]"));
        Assert.Equal(4, ex.Position);
    }

    [Fact]
    public void RejectsDeeplyNestedInputWithoutOverflowing()
    {
        Assert.False(SnbtParser.TryParse(new string('[', 600), out _, out var error));
        Assert.Contains("deep", error);
        Assert.False(SnbtParser.TryParse(string.Concat(Enumerable.Repeat("{a:", 600)), out _, out error));
        Assert.Contains("deep", error);
    }

    // ---------- round trips ----------

    private static NbtNode BigComposite()
    {
        return new NbtNode(NbtType.Compound)
        {
            Children =
            [
                new("byte", new NbtNode(NbtType.Byte) { Scalar = "-5" }),
                new("short", new NbtNode(NbtType.Short) { Scalar = "300" }),
                new("int", new NbtNode(NbtType.Int) { Scalar = "42" }),
                new("long", new NbtNode(NbtType.Long) { Scalar = "9223372036854775807" }),
                new("float", new NbtNode(NbtType.Float) { Scalar = "1.5" }),
                new("double", new NbtNode(NbtType.Double) { Scalar = "0.30000000000000004" }),
                new("text", new NbtNode(NbtType.String) { Scalar = "quote \" backslash \\ tab\tnewline\nunicode é✨" }),
                new("weird key!", new NbtNode(NbtType.Byte) { Scalar = "1" }),
                new("doubles", new NbtNode(NbtType.List)
                {
                    Items = [new NbtNode(NbtType.Double) { Scalar = "1.5" }, new NbtNode(NbtType.Double) { Scalar = "2.5" }],
                }),
                new("compounds", new NbtNode(NbtType.List)
                {
                    Items =
                    [
                        new NbtNode(NbtType.Compound) { Children = [new("x", new NbtNode(NbtType.Int) { Scalar = "1" })] },
                        new NbtNode(NbtType.Compound) { Children = [] },
                    ],
                }),
                new("emptyList", new NbtNode(NbtType.List) { Items = [] }),
                new("emptyCompound", new NbtNode(NbtType.Compound) { Children = [] }),
                new("bytes", new NbtNode(NbtType.ByteArray)
                {
                    Items = [new NbtNode(NbtType.Byte) { Scalar = "1" }, new NbtNode(NbtType.Byte) { Scalar = "-2" }],
                }),
                new("ints", new NbtNode(NbtType.IntArray)
                {
                    Items = [new NbtNode(NbtType.Int) { Scalar = "4" }, new NbtNode(NbtType.Int) { Scalar = "5" }],
                }),
                new("longs", new NbtNode(NbtType.LongArray)
                {
                    Items = [new NbtNode(NbtType.Long) { Scalar = "9223372036854775807" }],
                }),
                new("emptyBytes", new NbtNode(NbtType.ByteArray) { Items = [] }),
            ],
        };
    }

    [Fact]
    public void RoundTripsWriterOutputThroughParser()
    {
        var original = BigComposite();
        string compact = SnbtWriter.Write(original);
        var reparsed = Parse(compact);
        Assert.Equal(compact, SnbtWriter.Write(reparsed));
    }

    [Fact]
    public void RoundTripsIndentedWriterOutputThroughParser()
    {
        var original = BigComposite();
        var reparsed = Parse(SnbtWriter.Write(original, indented: true));
        Assert.Equal(SnbtWriter.Write(original), SnbtWriter.Write(reparsed));
    }

    [Theory]
    [InlineData("{mayfly:1b,\"weird key\":2}")]
    [InlineData("[1.5d,2.5d]")]
    [InlineData("[B;1b,2b]")]
    [InlineData("[I;4,5]")]
    [InlineData("[L;7L,8L]")]
    [InlineData("{a:{b:{c:[[],[1b]]}}}")]
    [InlineData("\"hi \\\"there\\\"\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[B;]")]
    [InlineData("300s")]
    [InlineData("9223372036854775807L")]
    [InlineData("0.1d")]
    public void WriteOfParseIsIdentityOnCanonicalText(string canonical)
    {
        Assert.Equal(canonical, SnbtWriter.Write(Parse(canonical)));
    }

    [Theory]
    [InlineData("[B; 1, 2]", "[B;1b,2b]")]
    [InlineData("{ a : 1b }", "{a:1b}")]
    [InlineData("'single'", "\"single\"")]
    [InlineData("unquoted", "\"unquoted\"")]
    [InlineData("true", "1b")]
    [InlineData("+5", "5")]
    [InlineData("[7l, 8l]", "[7L,8L]")]
    public void NormalizesToCanonicalFormWhenRewritten(string input, string canonical)
    {
        Assert.Equal(canonical, SnbtWriter.Write(Parse(input)));
    }
}
