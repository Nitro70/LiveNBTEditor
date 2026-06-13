package dev.nitro.livenbt.protocol;

import com.google.gson.JsonElement;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import java.util.Objects;

public final class Replies {
    private Replies() {}

    public static String ok(long id) {
        JsonObject o = new JsonObject();
        o.addProperty("id", id);
        o.addProperty("ok", true);
        return o.toString();
    }

    public static String ok(long id, JsonElement value) {
        Objects.requireNonNull(value, "value");
        JsonObject o = new JsonObject();
        o.addProperty("id", id);
        o.addProperty("ok", true);
        o.add("value", value);
        return o.toString();
    }

    public static String error(long id, String message) {
        if (message == null) message = "internal error";
        JsonObject o = new JsonObject();
        o.addProperty("id", id);
        o.addProperty("ok", false);
        o.addProperty("error", message);
        return o.toString();
    }

    public static String hello() {
        JsonObject o = new JsonObject();
        o.addProperty("op", "hello");
        o.addProperty("protocol", 1);
        o.addProperty("authRequired", true);
        return o.toString();
    }

    public static String update(String root, String path, JsonElement valueOrNull) {
        JsonObject o = new JsonObject();
        o.addProperty("op", "update");
        o.addProperty("root", root);
        o.addProperty("path", path);
        o.add("value", valueOrNull == null ? JsonNull.INSTANCE : valueOrNull);
        return o.toString();
    }
}
