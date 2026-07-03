package dev.nitro.livenbt.boot;

import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Path;

/**
 * The one LiveNBT class visible to EVERY classloader — the agent injects just this class into the
 * bootstrap loader, and the woven advice calls only here.
 *
 * <p><b>Why (found by running the real 26.2 dedicated server):</b> the dedicated server's bundler
 * loads all game classes in an isolated {@link URLClassLoader} whose parent chain skips the system
 * classpath — where {@code -javaagent} puts the agent jar. Inlined advice executes as part of the
 * woven class, so a direct reference to the hooks threw {@code NoClassDefFoundError} there (the
 * client never hit this because its game classes live ON the system classpath). The fix: on the
 * first tick this class re-loads the hook stack in a child of the <i>game's own</i> loader — from
 * there the Minecraft classes (via parent) and the agent jar (via the child URL) are both visible.
 * On the client the parent-first child simply resolves the already-visible system-classpath copy,
 * preserving the old behavior exactly.
 *
 * <p>This class may reference {@code java.*} only: it is defined by the bootstrap loader, which
 * cannot see anything else.
 */
public final class LiveNbtBoot {

    /** Absolute path to the agent jar; set by premain/agentmain before anything is woven. */
    public static volatile String agentJar;

    private static volatile MethodHandle tick;      // (Object)void — also the "initialized" guard
    private static volatile MethodHandle stop;      // (Object)void
    private static volatile MethodHandle keepAwake; // ()boolean
    private static volatile boolean failed;

    private LiveNbtBoot() {}

    /** Woven into the concrete servers' {@code tickServer}; initializes the hooks on first call. */
    public static void onTick(Object server) {
        MethodHandle h = tick;
        if (h == null) {
            init(server);
            h = tick;
            if (h == null) return;
        }
        try {
            h.invoke(server);
        } catch (Throwable t) {
            fail(t);
        }
    }

    /** Woven into {@code MinecraftServer.stopServer}. A server that never ticked has nothing to stop. */
    public static void onStop(Object server) {
        MethodHandle h = stop;
        if (h == null) return;
        try {
            h.invoke(server);
        } catch (Throwable t) {
            fail(t);
        }
    }

    /** Woven into the client HUD's {@code Gui.isPausing}; false until the hooks are up. */
    public static boolean keepAwake() {
        MethodHandle h = keepAwake;
        if (h == null) return false;
        try {
            return (boolean) h.invoke();
        } catch (Throwable t) {
            fail(t);
            return false;
        }
    }

    private static synchronized void init(Object server) {
        if (tick != null || failed) return;
        try {
            String jar = agentJar;
            if (jar == null) throw new IllegalStateException("agent jar path was never set");
            ClassLoader gameLoader = server.getClass().getClassLoader();
            ClassLoader cl = new URLClassLoader(new URL[]{Path.of(jar).toUri().toURL()}, gameLoader);
            Class<?> hooks = Class.forName("dev.nitro.livenbt.agent.LiveNbtHooks", true, cl);
            MethodHandles.Lookup lookup = MethodHandles.publicLookup();
            MethodHandle s = lookup.findStatic(hooks, "onServerStopping", MethodType.methodType(void.class, Object.class));
            MethodHandle k = lookup.findStatic(hooks, "keepAwake", MethodType.methodType(boolean.class));
            MethodHandle t = lookup.findStatic(hooks, "onServerTick", MethodType.methodType(void.class, Object.class));
            stop = s;
            keepAwake = k;
            tick = t;   // last: publishing tick marks initialization complete
        } catch (Throwable t) {
            failed = true;
            System.err.println("LiveNBT failed to initialize — live editing disabled for this session");
            t.printStackTrace();
        }
    }

    private static void fail(Throwable t) {
        if (!failed) {
            failed = true;
            System.err.println("LiveNBT hook failure — live editing disabled");
            t.printStackTrace();
        }
        tick = null;
        stop = null;
        keepAwake = null;
    }
}
