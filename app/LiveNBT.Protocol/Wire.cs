using System.Text;
using System.Text.Json;

namespace LiveNBT.Protocol;

public static class Wire
{
    // NBT depth 512 needs ~2 JSON levels per NBT level plus the envelope.
    private static readonly JsonDocumentOptions DocOptions = new() { MaxDepth = 1100 };

    public static string BuildRequest(long id, string op, string? root = null, string? path = null,
        NbtNode? value = null, string? token = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = 1100 }))
        {
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("op", op);
            if (root is not null) w.WriteString("root", root);
            if (path is not null) w.WriteString("path", path);
            if (value is not null)
            {
                w.WritePropertyName("value");
                NbtNodeJson.Write(value, w);
            }
            if (token is not null) w.WriteString("token", token);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ServerMessage Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"malformed server frame: {e.Message}");
        }
        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("server frame must be a JSON object");
            NbtNode? value = null;
            string? rawValue = null;
            if (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Object)
            {
                rawValue = v.GetRawText();
                if (v.TryGetProperty("t", out _))
                {
                    value = NbtNodeJson.Parse(v);   // claims to be a node: parse strictly
                }
                // else: non-node value (e.g. roots reply) — caller reads RawValue
            }
            return new ServerMessage
            {
                Op = root.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null,
                Id = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                    ? (id.TryGetInt64(out long idVal) ? idVal : (long?)null)
                    : null,
                Ok = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True,
                Error = root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String ? err.GetString() : null,
                Value = value,
                RawValue = rawValue,
                Root = root.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null,
                Path = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
            };
        }
    }
}
