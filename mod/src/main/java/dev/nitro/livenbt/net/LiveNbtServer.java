package dev.nitro.livenbt.net;

import dev.nitro.livenbt.LiveNbtConfig;
import dev.nitro.livenbt.ops.OpQueue;
import dev.nitro.livenbt.path.NbtPath;
import dev.nitro.livenbt.protocol.NbtJson;
import dev.nitro.livenbt.protocol.Replies;
import dev.nitro.livenbt.protocol.Request;
import dev.nitro.livenbt.roots.RootAdapter;
import dev.nitro.livenbt.roots.RootRegistry;
import dev.nitro.livenbt.roots.RootSnapshots;
import dev.nitro.livenbt.watch.WatchManager;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.Tag;
import org.java_websocket.WebSocket;
import org.java_websocket.drafts.Draft;
import org.java_websocket.drafts.Draft_6455;
import org.java_websocket.extensions.IExtension;
import org.java_websocket.handshake.ClientHandshake;
import org.java_websocket.protocols.IProtocol;
import org.java_websocket.protocols.Protocol;
import org.java_websocket.server.WebSocketServer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Collections;
import java.util.List;

/**
 * The embedded WebSocket endpoint. onMessage runs on a WS thread: it parses,
 * authenticates, and queues; all game access happens inside queued ops that the
 * server tick drains.
 */
public final class LiveNbtServer extends WebSocketServer {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");
    /** Hard ceiling on a single inbound frame, enforced by the websocket draft (pre-parse OOM guard). */
    private static final int MAX_FRAME_BYTES = 4 * 1024 * 1024;
    /** Per-connection watch budget. */
    private static final int MAX_WATCHES_PER_SESSION = 64;

    /** Per-connection state, stored as the WebSocket attachment. */
    static final class Session implements WatchManager.Sender {
        final WebSocket conn;
        volatile boolean authed;
        int watchCount; // server thread only
        Session(WebSocket conn) { this.conn = conn; }
        @Override public void send(String json) { if (conn.isOpen()) conn.send(json); }
        @Override public boolean isOpen() { return conn.isOpen(); }
    }

    private final LiveNbtConfig config;
    private final OpQueue queue;
    private final WatchManager watches;
    private final RootRegistry registry;
    private final RootSnapshots snapshots;
    private final ClientPresence presence;

    public LiveNbtServer(LiveNbtConfig config, OpQueue queue, WatchManager watches,
                         RootRegistry registry, RootSnapshots snapshots, ClientPresence presence) {
        // Protocol("") accepts clients that send no Sec-WebSocket-Protocol header (the
        // normal case — python websockets, C# ClientWebSocket). An empty protocol list
        // makes Draft_6455 reject every standard handshake with HTTP 404.
        super(new InetSocketAddress(config.bind(), config.port()),
                List.<Draft>of(new Draft_6455(Collections.<IExtension>emptyList(),
                        Collections.<IProtocol>singletonList(new Protocol("")), MAX_FRAME_BYTES)));
        this.config = config;
        this.queue = queue;
        this.watches = watches;
        this.registry = registry;
        this.snapshots = snapshots;
        this.presence = presence;
        setReuseAddr(true);
    }

    @Override
    public void onStart() {
        LOG.info("LiveNBT listening on {}:{}", config.bind(), config.port());
    }

    @Override
    public void onOpen(WebSocket conn, ClientHandshake handshake) {
        conn.setAttachment(new Session(conn));
        conn.send(Replies.hello());
    }

    @Override
    public void onClose(WebSocket conn, int code, String reason, boolean remote) {
        Session s = conn.getAttachment();
        if (s != null) {
            if (s.authed) presence.onDisconnect();   // stop keeping the game awake once the last client leaves
            queue.submit(() -> watches.removeAll(s));
        }
    }

    @Override
    public void onError(WebSocket conn, Exception ex) {
        if (conn == null) {
            LOG.error("LiveNBT server error (likely failed to bind {}:{} — is the port in use?) — live editing disabled",
                    config.bind(), config.port(), ex);
        } else {
            LOG.error("websocket error", ex);
        }
    }

    @Override
    public void onMessage(WebSocket conn, String message) {
        Session session = conn.getAttachment();
        Request req;
        try {
            req = Request.parse(message);
        } catch (IllegalArgumentException e) {
            session.send(Replies.error(-1, e.getMessage()));
            return;
        }
        if (req.op().equals("auth")) {
            if (req.token() != null && constantTimeEquals(req.token(), config.token())) {
                if (!session.authed) {   // count each client once, even if it re-sends auth
                    session.authed = true;
                    presence.onConnect();   // start keeping the game awake while this client is attached
                }
                session.send(Replies.ok(req.id()));
            } else {
                session.send(Replies.error(req.id(), "bad token"));
            }
            return;
        }
        if (!session.authed) {
            session.send(Replies.error(req.id(), "not authenticated"));
            return;
        }
        // roots + registry are pure reads served from a server-thread snapshot, so answer them
        // straight off the WS thread. Routing them through the tick queue would make them time out
        // whenever the integrated server is paused — which is exactly when the app is being used.
        switch (req.op()) {
            case "roots" -> session.send(Replies.ok(req.id(), snapshots.roots()));
            case "registry" -> session.send(Replies.ok(req.id(), snapshots.registries()));
            default -> queue.submit(() -> handleOnServerThread(session, req));
        }
    }

    /** Server thread. Replies are built here and sent via the (thread-safe) conn.send. */
    private void handleOnServerThread(Session session, Request req) {
        try {
            switch (req.op()) {
                // "roots" and "registry" are handled off-queue in onMessage (server-thread snapshot).
                case "get" -> {
                    RootAdapter adapter = registry.resolve(req.root());
                    CompoundTag snap = adapter.snapshot();
                    Tag t = NbtPath.get(snap, req.path());
                    if (t == null) session.send(Replies.error(req.id(), "no such path: " + req.path()));
                    else session.send(Replies.ok(req.id(), NbtJson.toJson(t)));
                }
                case "set", "add" -> {
                    if (req.value() == null || !req.value().isJsonObject()) {
                        session.send(Replies.error(req.id(), "missing 'value'"));
                        return;
                    }
                    Tag value = NbtJson.fromJson(req.value().getAsJsonObject());
                    RootAdapter adapter = registry.resolve(req.root());
                    if (req.op().equals("set")) adapter.set(req.path(), value);
                    else adapter.add(req.path(), value);
                    session.send(Replies.ok(req.id()));
                }
                case "delete" -> {
                    registry.resolve(req.root()).delete(req.path());
                    session.send(Replies.ok(req.id()));
                }
                case "watch" -> {
                    if (session.watchCount >= MAX_WATCHES_PER_SESSION) {
                        session.send(Replies.error(req.id(), "too many watches (max " + MAX_WATCHES_PER_SESSION + ")"));
                        return;
                    }
                    registry.resolve(req.root()); // syntax-validates the root id (existence is checked per sample)
                    if (watches.add(session, req.root(), req.path())) session.watchCount++;
                    session.send(Replies.ok(req.id()));
                }
                case "unwatch" -> {
                    if (watches.remove(session, req.root(), req.path()) && session.watchCount > 0) session.watchCount--;
                    session.send(Replies.ok(req.id()));
                }
                default -> session.send(Replies.error(req.id(), "unknown op: " + req.op()));
            }
        } catch (Exception e) {
            session.send(Replies.error(req.id(), e.getMessage() == null ? e.toString() : e.getMessage()));
        }
    }

    private static boolean constantTimeEquals(String a, String b) {
        return MessageDigest.isEqual(a.getBytes(StandardCharsets.UTF_8), b.getBytes(StandardCharsets.UTF_8));
    }
}
