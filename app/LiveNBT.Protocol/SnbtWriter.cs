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
