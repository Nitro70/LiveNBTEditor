using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveNBT.Protocol;

public static class NbtNodeJson
{
    private static string Brief(object? o)
    {
        string s = o?.ToString() ?? "null";
        return s.Length > 200 ? s[..200] + "…" : s;
    }

    private static long RangedLong(JsonElement e, long min, long max, string type)
    {
        if (e.ValueKind != JsonValueKind.Number && e.ValueKind != JsonValueKind.String)
            throw new FormatException($"expected number for {type}, got {Brief(e)}");
        if (!long.TryParse(e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText(),
                System.Globalization.NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
            throw new FormatException($"bad value for {type}: {Brief(e)}");
        if (n < min || n > max) throw new FormatException($"value {n} out of range for {type}");
        return n;
    }

    public static NbtNode Parse(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object ||
            !el.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String ||
            !el.TryGetProperty("v", out var v))
            throw new FormatException($"node needs string 't' and 'v': {Brief(el)}");

        NbtType type = NbtTypes.FromWire(t.GetString() ?? "");
        var node = new NbtNode(type);
        try
        {
            switch (type)
            {
                case NbtType.Byte:
                    node.Scalar = RangedLong(v, sbyte.MinValue, sbyte.MaxValue, "byte").ToString(CultureInfo.InvariantCulture);
                    break;
                case NbtType.Short:
                    node.Scalar = RangedLong(v, short.MinValue, short.MaxValue, "short").ToString(CultureInfo.InvariantCulture);
                    break;
                case NbtType.Int:
                    node.Scalar = RangedLong(v, int.MinValue, int.MaxValue, "int").ToString(CultureInfo.InvariantCulture);
                    break;
                case NbtType.Long:
                    node.Scalar = RangedLong(v, long.MinValue, long.MaxValue, "long").ToString(CultureInfo.InvariantCulture);
                    break;
                case NbtType.Float:
                    if (v.ValueKind != JsonValueKind.Number)
                        throw new FormatException($"expected number for float, got {Brief(v)}");
                    node.Scalar = v.GetRawText();
                    break;
                case NbtType.Double:
                    if (v.ValueKind != JsonValueKind.Number && v.ValueKind != JsonValueKind.String)
                        throw new FormatException($"expected number for double, got {Brief(v)}");
                    node.Scalar = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                    break;
                case NbtType.String:
                    node.Scalar = v.GetString() ?? throw new FormatException("string node needs a string value");
                    break;
                case NbtType.List:
                    node.Items = v.EnumerateArray().Select(Parse).ToList();
                    break;
                case NbtType.Compound:
                    node.Children = v.EnumerateObject()
                        .Select(p => new KeyValuePair<string, NbtNode>(p.Name, Parse(p.Value)))
                        .ToList();
                    break;
                case NbtType.ByteArray:
                    node.Items = v.EnumerateArray().Select(e => new NbtNode(NbtType.Byte)
                    {
                        Scalar = RangedLong(e, sbyte.MinValue, sbyte.MaxValue, "byte").ToString(CultureInfo.InvariantCulture),
                    }).ToList();
                    break;
                case NbtType.IntArray:
                    node.Items = v.EnumerateArray().Select(e => new NbtNode(NbtType.Int)
                    {
                        Scalar = RangedLong(e, int.MinValue, int.MaxValue, "int").ToString(CultureInfo.InvariantCulture),
                    }).ToList();
                    break;
                case NbtType.LongArray:
                    node.Items = v.EnumerateArray().Select(e => new NbtNode(NbtType.Long)
                    {
                        Scalar = RangedLong(e, long.MinValue, long.MaxValue, "long").ToString(CultureInfo.InvariantCulture),
                    }).ToList();
                    break;
            }
        }
        catch (InvalidOperationException e)   // wrong JSON kind for container ops
        {
            throw new FormatException($"bad value for {NbtTypes.ToWire(type)}: {Brief(v)}", e);
        }
        return node;
    }

    public static string ToJsonString(NbtNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = 1100 }))
        {
            Write(node, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(NbtNode node, Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("t", NbtTypes.ToWire(node.Type));
        w.WritePropertyName("v");
        try
        {
            switch (node.Type)
            {
                case NbtType.Byte:
                    w.WriteNumberValue(sbyte.Parse(node.Scalar!, CultureInfo.InvariantCulture)); break;
                case NbtType.Short:
                    w.WriteNumberValue(short.Parse(node.Scalar!, CultureInfo.InvariantCulture)); break;
                case NbtType.Int:
                    w.WriteNumberValue(int.Parse(node.Scalar!, CultureInfo.InvariantCulture)); break;
                case NbtType.Float:
                    w.WriteNumberValue(float.Parse(node.Scalar!, CultureInfo.InvariantCulture)); break;
                case NbtType.Long:
                    w.WriteStringValue(node.Scalar ?? throw new FormatException($"missing scalar for {NbtTypes.ToWire(node.Type)}")); break;
                case NbtType.Double:
                    w.WriteStringValue(node.Scalar ?? throw new FormatException($"missing scalar for {NbtTypes.ToWire(node.Type)}")); break;
                case NbtType.String:
                    w.WriteStringValue(node.Scalar ?? throw new FormatException($"missing scalar for {NbtTypes.ToWire(node.Type)}")); break;
                case NbtType.List:
                    w.WriteStartArray();
                    foreach (var item in node.Items ?? []) Write(item, w);
                    w.WriteEndArray();
                    break;
                case NbtType.Compound:
                    w.WriteStartObject();
                    foreach (var (name, child) in node.Children ?? [])
                    {
                        w.WritePropertyName(name);
                        Write(child, w);
                    }
                    w.WriteEndObject();
                    break;
                case NbtType.ByteArray or NbtType.IntArray:
                    w.WriteStartArray();
                    foreach (var item in node.Items ?? [])
                        w.WriteNumberValue(long.Parse(item.Scalar!, CultureInfo.InvariantCulture));
                    w.WriteEndArray();
                    break;
                case NbtType.LongArray:
                    w.WriteStartArray();
                    foreach (var item in node.Items ?? []) w.WriteStringValue(item.Scalar!);
                    w.WriteEndArray();
                    break;
            }
        }
        catch (Exception e) when (e is OverflowException or FormatException or ArgumentNullException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException($"bad value for {NbtTypes.ToWire(node.Type)}: {Brief(node.Scalar)}", e);
        }
        w.WriteEndObject();
    }
}
