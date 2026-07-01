package dev.nitro.livenbt.roots;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;

/**
 * Server-thread-maintained snapshots of the two read-only listings the app needs before it can do
 * anything: the available roots (players + worlds) and the item/enchantment registries.
 *
 * <p>These are served to the WebSocket thread <em>directly</em>, bypassing the op queue. The queue
 * only drains on a server tick, so anything routed through it silently times out whenever the
 * integrated server is paused (singleplayer with the app focused / a menu open / not open to LAN) —
 * which is exactly when the user is clicking around in the desktop app. Roots and the registry are
 * pure reads, so we snapshot them on the server thread and let the WS thread hand back the latest
 * snapshot instantly, paused or not.
 *
 * <p>Each setter publishes a brand-new {@link JsonObject}; readers only ever serialize it. The
 * {@code volatile} reference is the entire synchronization: a published object is never mutated.
 */
public final class RootSnapshots {

    private static JsonObject emptyRoots() {
        JsonObject o = new JsonObject();
        o.add("players", new JsonArray());
        o.add("worlds", new JsonArray());
        return o;
    }

    private static JsonObject emptyRegistries() {
        JsonObject o = new JsonObject();
        o.add("items", new JsonArray());
        o.add("enchantments", new JsonArray());
        return o;
    }

    /** Valid-but-empty shapes so a request that races startup gets a well-formed reply, never null. */
    private volatile JsonObject roots = emptyRoots();
    private volatile JsonObject registries = emptyRegistries();

    /** WS thread. */
    public JsonObject roots() { return roots; }

    /** WS thread. */
    public JsonObject registries() { return registries; }

    /** Server thread. */
    public void setRoots(JsonObject roots) { this.roots = roots; }

    /** Server thread. */
    public void setRegistries(JsonObject registries) { this.registries = registries; }
}
