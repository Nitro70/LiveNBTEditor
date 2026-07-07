using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class SnbtWriterIndentedTests
{
    private static NbtNode Compound(params (string Name, NbtNode Node)[] entries) => new(NbtType.Compound)
    {
        Children = entries.Select(e => new KeyValuePair<string, NbtNode>(e.Name, e.Node)).ToList(),
    };

    private static NbtNode Scalar(NbtType type, string value) => new(type) { Scalar = value };

    [Fact]
    public void NonIndentedOverloadMatchesCompactWriter()
    {
        var node = Compound(
            ("a", Scalar(NbtType.Byte, "1")),
            ("b", new NbtNode(NbtType.List) { Items = [Scalar(NbtType.Int, "1"), Scalar(NbtType.Int, "2")] }));
        Assert.Equal(SnbtWriter.Write(node), SnbtWriter.Write(node, indented: false));
    }

    [Fact]
    public void ScalarsAreUnchangedWhenIndented()
    {
        Assert.Equal("1b", SnbtWriter.Write(Scalar(NbtType.Byte, "1"), indented: true));
        Assert.Equal("0.1d", SnbtWriter.Write(Scalar(NbtType.Double, "0.1"), indented: true));
        Assert.Equal("\"a\\nb\"", SnbtWriter.Write(Scalar(NbtType.String, "a\nb"), indented: true));
    }

    [Fact]
    public void IndentsCompoundsOneEntryPerLine()
    {
        var compound = Compound(
            ("mayfly", Scalar(NbtType.Byte, "1")),
            ("weird key", Scalar(NbtType.Int, "2")));
        Assert.Equal("{\n  mayfly: 1b,\n  \"weird key\": 2\n}", SnbtWriter.Write(compound, indented: true));
        Assert.Equal(SnbtWriter.Write(compound, indented: true), SnbtWriter.WriteIndented(compound));
    }

    [Fact]
    public void IndentsNestedContainersTwoSpacesPerLevel()
    {
        var node = Compound(
            ("a", Compound(("b", new NbtNode(NbtType.List) { Items = [Scalar(NbtType.Int, "1"), Scalar(NbtType.Int, "2")] }))),
            ("c", new NbtNode(NbtType.List) { Items = [] }));
        const string expected = """
            {
              a: {
                b: [
                  1,
                  2
                ]
              },
              c: []
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), SnbtWriter.Write(node, indented: true));
    }

    [Fact]
    public void KeepsEmptyContainersInline()
    {
        Assert.Equal("{}", SnbtWriter.Write(new NbtNode(NbtType.Compound), indented: true));
        Assert.Equal("[]", SnbtWriter.Write(new NbtNode(NbtType.List) { Items = [] }, indented: true));
        Assert.Equal("[B;]", SnbtWriter.Write(new NbtNode(NbtType.ByteArray) { Items = [] }, indented: true));
    }

    [Fact]
    public void WritesArraysInlineWithSpaces()
    {
        var bytes = new NbtNode(NbtType.ByteArray) { Items = [Scalar(NbtType.Byte, "1"), Scalar(NbtType.Byte, "2")] };
        Assert.Equal("[B; 1b, 2b]", SnbtWriter.Write(bytes, indented: true));

        var ints = new NbtNode(NbtType.IntArray) { Items = [Scalar(NbtType.Int, "4"), Scalar(NbtType.Int, "5")] };
        Assert.Equal("[I; 4, 5]", SnbtWriter.Write(ints, indented: true));

        var longs = new NbtNode(NbtType.LongArray) { Items = [Scalar(NbtType.Long, "7")] };
        Assert.Equal("[L; 7L]", SnbtWriter.Write(longs, indented: true));

        Assert.Equal("{\n  arr: [B; 1b, 2b]\n}", SnbtWriter.Write(Compound(("arr", bytes)), indented: true));
    }

    [Fact]
    public void IndentedListsOfCompoundsKeepBracesOnEntryLines()
    {
        var node = new NbtNode(NbtType.List)
        {
            Items =
            [
                Compound(("x", Scalar(NbtType.Int, "1"))),
                Compound(),
            ],
        };
        Assert.Equal("[\n  {\n    x: 1\n  },\n  {}\n]", SnbtWriter.Write(node, indented: true));
    }

    [Fact]
    public void IndentedOutputReparsesToTheSameTree()
    {
        var node = Compound(
            ("scalars", Compound(
                ("b", Scalar(NbtType.Byte, "1")),
                ("l", Scalar(NbtType.Long, "9223372036854775807")),
                ("d", Scalar(NbtType.Double, "0.30000000000000004")),
                ("s", Scalar(NbtType.String, "multi\nline \"quoted\"")))),
            ("list", new NbtNode(NbtType.List) { Items = [Scalar(NbtType.Float, "1.5"), Scalar(NbtType.Float, "2.5")] }),
            ("bytes", new NbtNode(NbtType.ByteArray) { Items = [Scalar(NbtType.Byte, "-1"), Scalar(NbtType.Byte, "127")] }));
        string indented = SnbtWriter.Write(node, indented: true);
        Assert.True(SnbtParser.TryParse(indented, out var reparsed, out var error), error);
        Assert.Equal(SnbtWriter.Write(node), SnbtWriter.Write(reparsed!));
    }
}
