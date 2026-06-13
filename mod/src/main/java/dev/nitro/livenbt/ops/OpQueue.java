package dev.nitro.livenbt.ops;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.ConcurrentLinkedQueue;

/** Hands ops from the WebSocket thread to the server thread. */
public final class OpQueue {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");
    private final ConcurrentLinkedQueue<Runnable> queue = new ConcurrentLinkedQueue<>();

    /** Any thread. */
    public void submit(Runnable op) { queue.add(op); }

    /** Upper bound on ops applied per tick; the rest stay queued for the next tick. */
    private static final int MAX_OPS_PER_TICK = 1024;

    /** Server thread, once per tick. A throwing op never kills the drain. */
    public void drainAll() {
        Runnable op;
        int drained = 0;
        while (drained < MAX_OPS_PER_TICK && (op = queue.poll()) != null) {
            drained++;
            try {
                op.run();
            } catch (Throwable t) {
                LOG.error("op failed", t);
            }
        }
        if (drained == MAX_OPS_PER_TICK && !queue.isEmpty()) {
            LOG.warn("op queue backlogged; deferring remaining ops to next tick");
        }
    }
}
