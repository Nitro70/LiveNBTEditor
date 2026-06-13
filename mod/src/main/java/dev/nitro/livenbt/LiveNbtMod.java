package dev.nitro.livenbt;

import dev.nitro.livenbt.net.LiveNbtServer;
import dev.nitro.livenbt.ops.OpQueue;
import dev.nitro.livenbt.roots.RootRegistry;
import dev.nitro.livenbt.watch.WatchManager;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.loader.api.FabricLoader;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class LiveNbtMod implements ModInitializer {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");
    private static final int WATCH_INTERVAL_TICKS = 4;

    private final OpQueue queue = new OpQueue();
    private final WatchManager watches = new WatchManager();
    private LiveNbtServer ws;
    private RootRegistry registry;
    private int tickCounter;

    @Override
    public void onInitialize() {
        ServerLifecycleEvents.SERVER_STARTED.register(server -> {
            try {
                if (ws != null) { try { ws.stop(0); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); } ws = null; }
                LiveNbtConfig cfg = LiveNbtConfig.loadOrCreate(
                        FabricLoader.getInstance().getConfigDir().resolve("livenbt.json"));
                registry = new RootRegistry(server);
                ws = new LiveNbtServer(cfg, queue, watches, registry);
                ws.start();
            } catch (Exception e) {
                LOG.error("LiveNBT failed to start — live editing disabled for this session", e);
            }
        });

        ServerLifecycleEvents.SERVER_STOPPING.register(server -> {
            if (ws != null) {
                try { ws.stop(1000); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
                ws = null;
            }
            registry = null;
        });

        ServerTickEvents.START_SERVER_TICK.register(server -> {
            queue.drainAll();
            if (++tickCounter % WATCH_INTERVAL_TICKS == 0 && registry != null) {
                try {
                    watches.sample(registry::resolve);
                } catch (Exception e) {
                    LOG.error("watch sampler failed", e);
                }
            }
        });
    }
}
