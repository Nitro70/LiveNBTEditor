package dev.nitro.livenbt.net;

import java.util.concurrent.atomic.AtomicInteger;

/**
 * Counts authenticated LiveNBT clients currently connected. The java-agent weaves
 * {@code Gui.isPausing()} to return false while {@link #anyConnected()} is true, so singleplayer
 * never pauses while the editor is attached — edits apply and sync live in-game without the user
 * having to Open to LAN. Touched from WS threads (connect/disconnect) and the client render thread
 * (the woven read), so it's an {@link AtomicInteger}.
 */
public final class ClientPresence {
    private final AtomicInteger connected = new AtomicInteger();

    /** A client authenticated. */
    public void onConnect() { connected.incrementAndGet(); }

    /** An authenticated client's connection closed. Never goes below zero. */
    public void onDisconnect() { connected.updateAndGet(n -> n > 0 ? n - 1 : 0); }

    /** True while at least one authenticated client is connected. */
    public boolean anyConnected() { return connected.get() > 0; }

    /** Clear the count (e.g. on server stop, when all sessions are gone anyway). */
    public void reset() { connected.set(0); }
}
