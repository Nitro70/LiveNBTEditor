using System.Text;
using System.Text.RegularExpressions;

namespace LiveNBT.Protocol;

/// <summary>NbtNode -> SNBT (the /give-style text format) for copy-to-clipboard.</summary>
public static partial class SnbtWriter
{
    [GeneratedRegex("^[A-Za-z0-9._+-]+$")]
    private static partial Regex PlainKey();

    public static string Write(NbtNode node)
    {
        var sb = new StringBuilder();
        Append(node, sb);
        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="indented"/> selects NBT Studio-style pretty printing: 2-space indent,
    /// opening brace on the same line as its key, one compound/list entry per line, arrays and
    /// empty containers inline, "\n" line endings. <c>indented: false</c> is identical to
    /// <see cref="Write(NbtNode)"/>. Both forms parse back via <see cref="SnbtParser"/>.
    /// </summary>
    public static string Write(NbtNode node, bool indented)
    {
        if (!indented) return Write(node);
        var sb = new StringBuilder();
        AppendIndented(node, sb, 0);
        return sb.ToString();
    }

    /// <summary>Convenience alias for <see cref="Write(NbtNode, bool)"/> with <c>indented: true</c>.</summary>
    public static string WriteIndented(NbtNode node) => Write(node, indented: true);

    private static void AppendIndented(NbtNode node, StringBuilder sb, int level)
    {
        switch (node.Type)
        {
            case NbtType.Compound:
                var children = node.Children;
                if (children is null || children.Count == 0) { sb.Append("{}"); break; }
                sb.Append("{\n");
                for (int i = 0; i < children.Count; i++)
                {
                    var (name, child) = children[i];
                    Indent(sb, level + 1);
                    if (PlainKey().IsMatch(name)) sb.Append(name);
                    else AppendQuoted(name, sb);
                    sb.Append(": ");
                    AppendIndented(child, sb, level + 1);
                    if (i < children.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, level);
                sb.Append('}');
                break;
            case NbtType.List:
                var items = node.Items;
                if (items is null || items.Count == 0) { sb.Append("[]"); break; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    Indent(sb, level + 1);
                    AppendIndented(items[i], sb, level + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, level);
                sb.Append(']');
                break;
            case NbtType.ByteArray: AppendArrayInline(node.Items, sb, 'B', "b"); break;
            case NbtType.IntArray: AppendArrayInline(node.Items, sb, 'I', null); break;
            case NbtType.LongArray: AppendArrayInline(node.Items, sb, 'L', "L"); break;
            default: Append(node, sb); break; // scalars and strings: same as compact
        }
    }

    private static void AppendArrayInline(List<NbtNode>? items, StringBuilder sb, char prefix, string? suffix)
    {
        sb.Append('[').Append(prefix).Append(';');
        bool first = true;
        foreach (var item in items ?? [])
        {
            sb.Append(first ? " " : ", ");
            first = false;
            sb.Append(item.Scalar).Append(suffix);
        }
        sb.Append(']');
    }

    private static void Indent(StringBuilder sb, int level) => sb.Append(' ', level * 2);

    private static void Append(NbtNode node, StringBuilder sb)
    {
        switch (node.Type)
        {
            case NbtType.Byte: sb.Append(node.Scalar).Append('b'); break;
            case NbtType.Short: sb.Append(node.Scalar).Append('s'); break;
            case NbtType.Int: sb.Append(node.Scalar); break;
            case NbtType.Long: sb.Append(node.Scalar).Append('L'); break;
            case NbtType.Float: sb.Append(node.Scalar).Append('f'); break;
            case NbtType.Double: sb.Append(node.Scalar).Append('d'); break;
            case NbtType.String: AppendQuoted(node.Scalar ?? "", sb); break;
            case NbtType.List:
                sb.Append('[');
                AppendJoined(node.Items, sb);
                sb.Append(']');
                break;
            case NbtType.Compound:
                sb.Append('{');
                bool first = true;
                foreach (var (name, child) in node.Children ?? [])
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (PlainKey().IsMatch(name)) sb.Append(name);
                    else AppendQuoted(name, sb);
                    sb.Append(':');
                    Append(child, sb);
                }
                sb.Append('}');
                break;
            case NbtType.ByteArray:
                sb.Append("[B;");
                AppendJoined(node.Items, sb, suffix: "b");
                sb.Append(']');
                break;
            case NbtType.IntArray:
                sb.Append("[I;");
                AppendJoined(node.Items, sb);
                sb.Append(']');
                break;
            case NbtType.LongArray:
                sb.Append("[L;");
                AppendJoined(node.Items, sb, suffix: "L");
                sb.Append(']');
                break;
        }
    }

    private static void AppendJoined(List<NbtNode>? items, StringBuilder sb, string? suffix = null)
    {
        bool first = true;
        foreach (var item in items ?? [])
        {
            if (!first) sb.Append(',');
            first = false;
            if (suffix is null) Append(item, sb);
            else sb.Append(item.Scalar).Append(suffix);
        }
    }

    private static void AppendQuoted(string text, StringBuilder sb)
    {
        sb.Append('"')
          .Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                      .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"))
          .Append('"');
    }
}
