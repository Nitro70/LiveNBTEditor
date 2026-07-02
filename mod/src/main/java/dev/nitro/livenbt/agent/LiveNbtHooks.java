package dev.nitro.livenbt.agent;

import dev.nitro.livenbt.LiveNbtConfig;
import dev.nitro.livenbt.net.LiveNbtServer;
import dev.nitro.livenbt.net.ClientPresence;
import dev.nitro.livenbt.ops.OpQueue;
import dev.nitro.livenbt.roots.RootRegistry;
import dev.nitro.livenbt.roots.RootSnapshots;
import dev.nitro.livenbt.watch.WatchManager;
import net.minecraft.server.MinecraftServer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Loader-agnostic LiveNBT lifecycle, driven by the java-agent ({@link LiveNbtAgent}). All
 * server-thread state lives here so the woven advice stays a trivial public call — inlined advice
 * may not access private members of the agent class. Nothing here depends on a mod loader.
 */
public final class LiveNbtHooks {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");
    private static final int WATCH_INTERVAL_TICKS = 4;

    private static final OpQueue queue = new OpQueue();
    private static final WatchManager watches = new WatchManager();
    /** Read-only listings served off the tick queue (so they work while the server is paused). */
    private static final RootSnapshots snapshots = new RootSnapshots();
    /** Tracks connected editors; while any are attached the agent keeps singleplayer from pausing. */
    private static final ClientPresence presence = new ClientPresence();
    /** tickServer fires every tick; this gates onServerStarted to exactly once per server (reset on stop). */
    private static final AtomicBoolean started = new AtomicBoolean(false);
    private static LiveNbtServer ws;
    private static RootRegistry registry;
    private static int tickCounter;

    private LiveNbtHooks() {}

    /** Server has finished starting (world loaded). Starts the embedded WebSocket server. */
    public static synchronized void onServerStarted(MinecraftServer server) {
        try {
            stopWs(0);   // defensive: never leak a prior server
            LiveNbtConfig cfg = LiveNbtConfig.loadOrCreate(configFile());
            registry = new RootRegistry(server);
            // Prime the snapshots on the server thread before the socket accepts anyone, so the very
            // first roots/registry request already has real data. Registries are frozen after world
            // load, so they never need re-sampling; roots are refreshed each tick below.
            snapshots.setRegistries(registry.listRegistries());
            snapshots.setRoots(registry.listRoots());
            ws = new LiveNbtServer(cfg, queue, watches, registry, snapshots, presence);
            ws.start();
        } catch (Exception e) {
            LOG.error("LiveNBT failed to start — live editing disabled for this session", e);
        }
    }

    /** Start of a server tick: on the first tick start LiveNBT, then drain ops + periodically sample watches. */
    public static void onServerTick(MinecraftServer server) {
        if (started.compareAndSet(false, true)) {
            onServerStarted(server);
        }
        queue.drainAll();
        if (++tickCounter % WATCH_INTERVAL_TICKS == 0 && registry != null) {
            try {
                snapshots.setRoots(registry.listRoots());   // keep the off-queue roots listing fresh
                watches.sample(registry::resolve);
            } catch (Exception e) {
                LOG.error("watch sampler failed", e);
            }
        }
    }

    /** Server is stopping (worlds still valid). Stops the WebSocket server and re-arms start for the next world. */
    public static synchronized void onServerStopping(MinecraftServer server) {
        stopWs(1000);
        // run stragglers (queued edits + the watch removals stopWs just enqueued) while this world's
        // server is still valid — nothing may survive the reload and execute against the next world
        queue.drainAll();
        presence.reset();     // no clients across a world reload; let the next world pause normally until one attaches
        registry = null;
        started.set(false);   // re-arm: the next world's first tick restarts LiveNBT (same JVM)
    }

    /**
     * True while an editor is attached. The agent weaves {@code Gui.isPausing()} to return false in
     * that case, so singleplayer keeps ticking (edits apply and sync live in-game) instead of
     * pausing when you tab to the app or open the menu — the automatic equivalent of Open to LAN.
     * Public because the woven advice may only call public members of this class.
     */
    public static boolean keepAwake() {
        return presence.anyConnected();
    }

    private static void stopWs(int timeoutMs) {
        if (ws != null) {
            try {
                ws.stop(timeoutMs);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
            ws = null;
        }
    }

    /** {@code -Dlivenbt.config=<path>} override, else {@code <gamedir>/config/livenbt.json} (user.dir). */
    private static Path configFile() {
        String override = System.getProperty("livenbt.config");
        if (override != null && !override.isBlank()) {
            Path p = Path.of(override);
            return Files.isDirectory(p) ? p.resolve("livenbt.json") : p;
        }
        return Path.of(System.getProperty("user.dir"), "config", "livenbt.json");
    }
}
