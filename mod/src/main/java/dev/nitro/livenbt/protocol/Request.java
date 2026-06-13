package dev.nitro.livenbt.protocol;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParseException;
import com.google.gson.JsonParser;

/** A parsed client request. Missing root/path default to ""; value/token may be null. */
public record Request(long id, String op, String root, String path, JsonElement value, String token) {

    private static final int MAX_REQUEST_CHARS = 1 << 20; // 1 MiB of text — far above any legitimate edit

    public static Request parse(String json) {
        if (json == null) throw new IllegalArgumentException("request is null");
        if (json.length() > MAX_REQUEST_CHARS) throw new IllegalArgumentException("request too large");
        JsonObject o;
        try {
            JsonElement el = JsonParser.parseString(json);
            if (!el.isJsonObject()) throw new IllegalArgumentException("request must be a JSON object");
            o = el.getAsJsonObject();
        } catch (JsonParseException e) {
            throw new IllegalArgumentException("malformed JSON: " + e.getMessage());
        }
        String op = str(o, "op", null);
        if (op == null) throw new IllegalArgumentException("request missing 'op'");
        JsonElement idEl = o.get("id");
        long id = idEl != null && idEl.isJsonPrimitive() && idEl.getAsJsonPrimitive().isNumber()
                ? idEl.getAsLong() : -1;
        return new Request(
                id,
                op,
                str(o, "root", ""),
                str(o, "path", ""),
                o.get("value"),
                str(o, "token", null)
        );
    }

    /** String field accessor: absent -> fallback; present but not a JSON string -> error. */
    private static String str(JsonObject o, String field, String fallback) {
        JsonElement el = o.get(field);
        if (el == null) return fallback;
        if (!el.isJsonPrimitive() || !el.getAsJsonPrimitive().isString())
            throw new IllegalArgumentException("'" + field + "' must be a string");
        return el.getAsString();
    }
}
