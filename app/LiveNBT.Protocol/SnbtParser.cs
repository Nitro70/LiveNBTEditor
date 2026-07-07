using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LiveNBT.Protocol;

/// <summary>Error thrown by <see cref="SnbtParser.Parse"/>. Position is a zero-based character index.</summary>
public sealed class SnbtParseException(string message, int position)
    : FormatException($"{message} at position {position}")
{
    public int Position { get; } = position;
}

/// <summary>
/// SNBT text -> NbtNode; the exact inverse of <see cref="SnbtWriter"/>, whitespace tolerant.
/// Scalars are stored the way the wire format keeps them: integers normalized via integer
/// parsing (never floating point), float/double kept as the original text with the suffix
/// stripped, so long/double exactness is preserved end to end.
/// Follows vanilla SNBT rules for unquoted tokens: an unquoted token that is not a valid
/// number (e.g. leading zeros, out-of-int-range bare integer, "1e5" without a dot) falls
/// back to an unquoted string. Suffixed numbers that are out of range are an error instead,
/// since silently turning "999b" into a string would be a footgun for live editing.
/// </summary>
public static partial class SnbtParser
{
    private const int MaxDepth = 512;

    [GeneratedRegex("^[-+]?(0|[1-9][0-9]*)$")]
    private static partial Regex IntToken();

    /// Core of a suffixed float/double: digits with optional dot and exponent ("1", "1.5", ".5", "1e5").
    [GeneratedRegex(@"^[-+]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][-+]?[0-9]+)?$")]
    private static partial Regex RealToken();

    /// A bare (unsuffixed) decimal: the dot is required, matching vanilla.
    [GeneratedRegex(@"^[-+]?([0-9]+\.[0-9]*|\.[0-9]+)([eE][-+]?[0-9]+)?$")]
    private static partial Regex BareDecimalToken();

    /// <summary>Parses a single unnamed SNBT value; trailing non-whitespace content is an error.</summary>
    /// <exception cref="SnbtParseException">On any syntax or range error, with character position.</exception>
    public static NbtNode Parse(string text)
    {
        var c = new Cursor(text);
        var node = c.ParseValue(0);
        c.ExpectEnd();
        return node;
    }

    /// <summary>Non-throwing variant of <see cref="Parse"/>.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out NbtNode? node, [NotNullWhen(false)] out string? error)
    {
        try
        {
            node = Parse(text);
            error = null;
            return true;
        }
        catch (SnbtParseException e)
        {
            node = null;
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses NBT Studio-style "name:value" (name may be quoted); falls back to a plain
    /// unnamed value, in which case <paramref name="name"/> is "".
    /// </summary>
    public static bool TryParseNamed(string text, out string name, [NotNullWhen(true)] out NbtNode? node, [NotNullWhen(false)] out string? error)
    {
        name = "";
        try
        {
            var c = new Cursor(text);
            if (c.TryReadNamePrefix(out string prefix))
            {
                var value = c.ParseValue(0);
                c.ExpectEnd();
                name = prefix;
                node = value;
                error = null;
                return true;
            }
            node = Parse(text);
            error = null;
            return true;
        }
        catch (SnbtParseException e)
        {
            node = null;
            error = e.Message;
            return false;
        }
    }

    private sealed class Cursor(string text)
    {
        private readonly string _s = text;
        private int _pos;

        private bool AtEnd => _pos >= _s.Length;

        private SnbtParseException Err(string message, int? pos = null) => new(message, pos ?? _pos);

        private void SkipWs()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }

        private bool TryConsume(char c)
        {
            if (AtEnd || _s[_pos] != c) return false;
            _pos++;
            return true;
        }

        public void ExpectEnd()
        {
            SkipWs();
            if (!AtEnd) throw Err($"unexpected trailing content '{Snip()}'");
        }

        private string Snip()
        {
            string rest = _s[_pos..];
            return rest.Length <= 12 ? rest : rest[..12] + "…";
        }

        /// If the input starts with `name :`, consumes it and returns true; otherwise rewinds.
        public bool TryReadNamePrefix(out string name)
        {
            name = "";
            int save = _pos;
            SkipWs();
            if (AtEnd) { _pos = save; return false; }
            string candidate;
            if (_s[_pos] is '"' or '\'')
            {
                try { candidate = ReadQuoted(); }
                catch (SnbtParseException) { _pos = save; return false; } // unnamed parse reports the real error
            }
            else
            {
                candidate = ReadUnquotedToken();
                if (candidate.Length == 0) { _pos = save; return false; }
            }
            SkipWs();
            if (!TryConsume(':')) { _pos = save; return false; }
            name = candidate;
            return true;
        }

        public NbtNode ParseValue(int depth)
        {
            if (depth > MaxDepth) throw Err("nesting too deep");
            SkipWs();
            if (AtEnd) throw Err("expected value");
            return _s[_pos] switch
            {
                '{' => ParseCompound(depth),
                '[' => ParseListOrArray(depth),
                '"' or '\'' => new NbtNode(NbtType.String) { Scalar = ReadQuoted() },
                _ => ParseScalarToken(),
            };
        }

        private NbtNode ParseCompound(int depth)
        {
            _pos++; // '{'
            var node = new NbtNode(NbtType.Compound) { Children = [] };
            var seen = new HashSet<string>(StringComparer.Ordinal);   // O(1) dup detection, not an O(n) scan per key
            SkipWs();
            if (TryConsume('}')) return node;
            while (true)
            {
                SkipWs();
                int keyPos = _pos;
                string key = ReadKey();
                if (!seen.Add(key)) throw Err($"duplicate key '{key}'", keyPos);
                SkipWs();
                if (!TryConsume(':'))
                    throw Err(AtEnd ? "unterminated compound (expected ':')" : $"expected ':' after key but found '{_s[_pos]}'");
                node.Children.Add(new(key, ParseValue(depth + 1)));
                SkipWs();
                if (TryConsume(',')) continue;
                if (TryConsume('}')) return node;
                throw Err(AtEnd ? "unterminated compound (expected ',' or '}')" : $"expected ',' or '}}' but found '{_s[_pos]}'");
            }
        }

        private string ReadKey()
        {
            if (AtEnd) throw Err("expected key");
            if (_s[_pos] is '"' or '\'') return ReadQuoted();
            string token = ReadUnquotedToken();
            if (token.Length == 0) throw Err($"expected key but found '{_s[_pos]}'");
            return token;
        }

        private NbtNode ParseListOrArray(int depth)
        {
            _pos++; // '['
            int save = _pos;
            SkipWs();
            if (!AtEnd && char.IsAsciiLetter(_s[_pos]))
            {
                int letterPos = _pos;
                char letter = _s[_pos++];
                SkipWs();
                if (TryConsume(';'))
                    return letter switch
                    {
                        'B' => ParseArray(NbtType.ByteArray),
                        'I' => ParseArray(NbtType.IntArray),
                        'L' => ParseArray(NbtType.LongArray),
                        _ => throw Err($"unknown array type '{letter};' (expected B;, I; or L;)", letterPos),
                    };
                _pos = save; // plain list starting with an unquoted string
            }
            else
            {
                _pos = save;
            }
            return ParseList(depth);
        }

        private NbtNode ParseList(int depth)
        {
            var node = new NbtNode(NbtType.List) { Items = [] };
            SkipWs();
            if (TryConsume(']')) return node;
            while (true)
            {
                SkipWs();
                int elemPos = _pos;
                var item = ParseValue(depth + 1);
                if (node.Items.Count > 0 && item.Type != node.Items[0].Type)
                    throw Err($"mixed list: expected {NbtTypes.ToWire(node.Items[0].Type)}, found {NbtTypes.ToWire(item.Type)}", elemPos);
                node.Items.Add(item);
                SkipWs();
                if (TryConsume(',')) continue;
                if (TryConsume(']')) return node;
                throw Err(AtEnd ? "unterminated list (expected ',' or ']')" : $"expected ',' or ']' but found '{_s[_pos]}'");
            }
        }

        private NbtNode ParseArray(NbtType arrayType)
        {
            var node = new NbtNode(arrayType) { Items = [] };
            SkipWs();
            if (TryConsume(']')) return node;
            while (true)
            {
                SkipWs();
                int elemPos = _pos;
                if (AtEnd) throw Err("unterminated array (expected value)");
                string token = ReadUnquotedToken();
                if (token.Length == 0) throw Err($"expected {NbtTypes.ToWire(NbtTypes.ArrayElementType(arrayType))} but found '{_s[_pos]}'");
                node.Items.Add(ArrayElement(arrayType, token, elemPos));
                SkipWs();
                if (TryConsume(',')) continue;
                if (TryConsume(']')) return node;
                throw Err(AtEnd ? "unterminated array (expected ',' or ']')" : $"expected ',' or ']' but found '{_s[_pos]}'");
            }
        }

        /// Array elements may carry the matching suffix or omit it: [B;1b,2] and [L;1l,2] are both fine.
        private NbtNode ArrayElement(NbtType arrayType, string token, int pos)
        {
            NbtType elemType = NbtTypes.ArrayElementType(arrayType);
            string wire = NbtTypes.ToWire(elemType);
            if (elemType == NbtType.Byte && token is "true" or "false")
                return new NbtNode(NbtType.Byte) { Scalar = token == "true" ? "1" : "0" };
            string core = token;
            if (char.IsAsciiLetter(token[^1]))
            {
                bool suffixOk = elemType switch
                {
                    NbtType.Byte => token[^1] is 'b' or 'B',
                    NbtType.Long => token[^1] is 'l' or 'L',
                    _ => false,
                };
                if (!suffixOk) throw Err($"expected {wire} element but found '{token}'", pos);
                core = token[..^1];
            }
            if (!IntToken().IsMatch(core)) throw Err($"expected {wire} element but found '{token}'", pos);
            (long min, long max) = elemType switch
            {
                NbtType.Byte => ((long)sbyte.MinValue, (long)sbyte.MaxValue),
                NbtType.Int => (int.MinValue, int.MaxValue),
                _ => (long.MinValue, long.MaxValue),
            };
            if (!long.TryParse(core, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n) || n < min || n > max)
                throw Err($"{wire} out of range: {token}", pos);
            return new NbtNode(elemType) { Scalar = n.ToString(CultureInfo.InvariantCulture) };
        }

        private NbtNode ParseScalarToken()
        {
            int start = _pos;
            string token = ReadUnquotedToken();
            if (token.Length == 0) throw Err($"unexpected character '{_s[_pos]}'");
            return Classify(token, start);
        }

        private NbtNode Classify(string token, int pos)
        {
            if (token == "true") return new NbtNode(NbtType.Byte) { Scalar = "1" };
            if (token == "false") return new NbtNode(NbtType.Byte) { Scalar = "0" };

            string core = token[..^1];
            switch (token[^1])
            {
                case 'b' or 'B' when IntToken().IsMatch(core):
                    return IntScalar(NbtType.Byte, core, sbyte.MinValue, sbyte.MaxValue, pos);
                case 's' or 'S' when IntToken().IsMatch(core):
                    return IntScalar(NbtType.Short, core, short.MinValue, short.MaxValue, pos);
                case 'l' or 'L' when IntToken().IsMatch(core):
                    return IntScalar(NbtType.Long, core, long.MinValue, long.MaxValue, pos);
                case 'f' or 'F' when RealToken().IsMatch(core):
                    return RealScalar(NbtType.Float, core, pos);
                case 'd' or 'D' when RealToken().IsMatch(core):
                    return RealScalar(NbtType.Double, core, pos);
            }

            if (IntToken().IsMatch(token))
            {
                // bare integer: int if it fits, otherwise vanilla falls back to an unquoted string
                if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)
                    && n is >= int.MinValue and <= int.MaxValue)
                    return new NbtNode(NbtType.Int) { Scalar = n.ToString(CultureInfo.InvariantCulture) };
                return new NbtNode(NbtType.String) { Scalar = token };
            }
            if (BareDecimalToken().IsMatch(token))
                return RealScalar(NbtType.Double, token, pos);
            return new NbtNode(NbtType.String) { Scalar = token };
        }

        private NbtNode IntScalar(NbtType type, string digits, long min, long max, int pos)
        {
            if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n) || n < min || n > max)
                throw Err($"{NbtTypes.ToWire(type)} out of range: {digits}", pos);
            return new NbtNode(type) { Scalar = n.ToString(CultureInfo.InvariantCulture) };
        }

        /// Keeps the original text (suffix already stripped) so doubles never pass through floating point.
        private NbtNode RealScalar(NbtType type, string core, int pos)
        {
            string wire = NbtTypes.ToWire(type);
            if (type == NbtType.Float)
            {
                if (!float.TryParse(core, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    throw Err($"invalid {wire} '{core}'", pos);
                if (!float.IsFinite(f)) throw Err($"{wire} out of range: {core}", pos);
            }
            else
            {
                if (!double.TryParse(core, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Err($"invalid {wire} '{core}'", pos);
                if (!double.IsFinite(d)) throw Err($"{wire} out of range: {core}", pos);
            }
            return new NbtNode(type) { Scalar = core };
        }

        private static bool IsUnquotedChar(char c) =>
            c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or '-' or '.' or '+';

        private string ReadUnquotedToken()
        {
            int start = _pos;
            while (_pos < _s.Length && IsUnquotedChar(_s[_pos])) _pos++;
            return _s[start.._pos];
        }

        private string ReadQuoted()
        {
            int start = _pos;
            char quote = _s[_pos++];
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Err("unterminated string", start);
                char c = _s[_pos++];
                if (c == quote) return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (AtEnd) throw Err("unterminated escape", _pos - 1);
                char e = _s[_pos++];
                switch (e)
                {
                    case '\\' or '"' or '\'': sb.Append(e); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append(ReadHex4(_pos - 2)); break;
                    default: throw Err($"invalid escape '\\{e}'", _pos - 2);
                }
            }
        }

        private char ReadHex4(int escapeStart)
        {
            if (_pos + 4 > _s.Length) throw Err("incomplete \\u escape", escapeStart);
            if (!ushort.TryParse(_s.AsSpan(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                throw Err($"invalid \\u escape '\\u{_s.AsSpan(_pos, 4)}'", escapeStart);
            _pos += 4;
            return (char)code;
        }
    }
}
