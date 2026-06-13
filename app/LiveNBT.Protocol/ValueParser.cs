using System.Globalization;

namespace LiveNBT.Protocol;

/// <summary>Validates user-typed text into a normalized invariant scalar string.</summary>
public static class ValueParser
{
    public static bool TryParse(NbtType type, string input, out string normalized, out string error)
    {
        input = input.Trim(); // pasted values commonly carry stray whitespace
        normalized = "";
        error = "";
        const NumberStyles Int = NumberStyles.AllowLeadingSign;
        const NumberStyles Real = NumberStyles.Float;
        var inv = CultureInfo.InvariantCulture;
        switch (type)
        {
            case NbtType.Byte:
                if (input is "true") { normalized = "1"; return true; }
                if (input is "false") { normalized = "0"; return true; }
                if (sbyte.TryParse(input, Int, inv, out var b)) { normalized = b.ToString(inv); return true; }
                error = "expected -128..127 or true/false";
                return false;
            case NbtType.Short:
                if (short.TryParse(input, Int, inv, out var s)) { normalized = s.ToString(inv); return true; }
                error = "expected -32768..32767";
                return false;
            case NbtType.Int:
                if (int.TryParse(input, Int, inv, out var i)) { normalized = i.ToString(inv); return true; }
                error = "expected a 32-bit integer";
                return false;
            case NbtType.Long:
                if (long.TryParse(input, Int, inv, out var l)) { normalized = l.ToString(inv); return true; }
                error = "expected a 64-bit integer";
                return false;
            case NbtType.Float:
                if (float.TryParse(input, Real, inv, out var f) && float.IsFinite(f)) { normalized = f.ToString("R", inv); return true; }
                error = "expected a finite float";
                return false;
            case NbtType.Double:
                if (double.TryParse(input, Real, inv, out var d) && double.IsFinite(d)) { normalized = d.ToString("R", inv); return true; }
                error = "expected a finite double";
                return false;
            case NbtType.String:
                normalized = input;
                return true;
            default:
                error = $"{type} cannot be edited as text";
                return false;
        }
    }
}
