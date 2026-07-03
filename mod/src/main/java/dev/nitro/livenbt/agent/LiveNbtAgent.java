package dev.nitro.livenbt.agent;

import dev.nitro.livenbt.boot.LiveNbtBoot;
import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;

import java.io.IOException;
import java.lang.instrument.Instrumentation;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import java.util.jar.JarOutputStream;
import java.util.zip.ZipEntry;

import static net.bytebuddy.matcher.ElementMatchers.named;

/**
 * {@code -javaagent} entrypoint. Weaves the vanilla server lifecycle methods to drive
 * {@link LiveNbtHooks}. No mod loader involved. Works in the client (integrated server) and in a
 * dedicated server — the same jar, {@code -javaagent} or Dynamic Attach either way.
 *
 * <p><b>Weave placement (javap-verified on the 26.2 client AND server jars):</b> both concrete
 * servers override {@code tickServer} — {@code IntegratedServer} calls super only while unpaused,
 * {@code DedicatedServer} always calls super. Weaving the base class too would therefore fire the
 * tick advice twice per tick whenever super runs, so the tick advice goes on the two
 * <i>overrides</i> only: each is the run loop's entry point and fires exactly once per iteration —
 * including while singleplayer is paused, and while a dedicated server is empty-paused
 * ({@code pause-when-empty-seconds}: 26.2's {@code tickServer} early-returns after
 * {@code tickConnection()}, but it is still invoked, so queued edits keep draining with nobody
 * online). {@code stopServer} is the mirror image: both overrides unconditionally call super, so
 * the stop advice goes on the base {@code MinecraftServer} only and fires exactly once.
 *
 * <p><b>Classloaders:</b> the advice bodies call only {@link LiveNbtBoot}, which {@link #install}
 * injects into the <i>bootstrap</i> loader so it resolves from every classloader. That matters on
 * the dedicated server, whose bundler loads the game in an isolated loader that cannot see the
 * system classpath (a direct advice→hooks reference threw {@code NoClassDefFoundError} there —
 * caught by running the real server, not by review). {@code LiveNbtBoot} then loads
 * {@link LiveNbtHooks} in a child of the game's own loader on the first tick. All advice suppresses
 * throwables: a LiveNBT failure must degrade to "editing disabled", never crash the game.
 *
 * <p>We also weave the client HUD's {@code Gui.isPausing()}: while an editor is attached it is
 * forced to return false, which drives {@code Minecraft.pause} false so singleplayer keeps ticking
 * (edits apply and sync live in-game) instead of pausing when you tab to the app — the automatic
 * equivalent of Open to LAN. {@code isPausing} is only consulted by the pause computation, so this
 * is surgical. On a dedicated server the {@code Gui} class never loads and the matcher never fires.
 */
public final class LiveNbtAgent {

    /** Install once, whether we arrive via {@code -javaagent} (premain) or Dynamic Attach (agentmain). */
    private static final AtomicBoolean INSTALLED = new AtomicBoolean();

    private LiveNbtAgent() {}

    /** {@code -javaagent} path: JVM start, target classes not yet loaded. */
    public static void premain(String args, Instrumentation inst) {
        install(inst);
    }

    /**
     * Dynamic Attach path: the app loads this agent into an already-running Minecraft (no JVM args).
     * The matched classes are already loaded, so ByteBuddy RETRANSFORMS them — our advice only
     * modifies method bodies ({@code @Advice.OnMethodEnter/Exit}), which is retransform-safe.
     */
    public static void agentmain(String args, Instrumentation inst) {
        install(inst);
    }

    private static void install(Instrumentation inst) {
        if (!INSTALLED.compareAndSet(false, true)) return;   // a second attach must not double-weave
        Path jar = ownJar();
        try {
            injectBootClass(inst, jar);   // BEFORE the first LiveNbtBoot reference resolves below
        } catch (Exception e) {
            // client still works (its game classes share the system classpath); dedicated won't
            System.err.println("LiveNBT: bootstrap injection failed — dedicated-server support disabled: " + e);
        }
        LiveNbtBoot.agentJar = jar.toString();
        new AgentBuilder.Default()
                .with(AgentBuilder.RedefinitionStrategy.RETRANSFORMATION)
                // weave failures are otherwise swallowed silently — surface them in the game log
                .with(AgentBuilder.Listener.StreamWriting.toSystemError().withErrorsOnly())
                // tick: the two concrete overrides only — the base would double-fire via super
                .type(named("net.minecraft.client.server.IntegratedServer")
                        .or(named("net.minecraft.server.dedicated.DedicatedServer")))
                .transform((builder, td, cl, module, pd) -> builder
                        .visit(Advice.to(TickAdvice.class).on(named("tickServer"))))
                // stop: the base only — both overrides unconditionally call super
                .type(named("net.minecraft.server.MinecraftServer"))
                .transform((builder, td, cl, module, pd) -> builder
                        .visit(Advice.to(StopAdvice.class).on(named("stopServer"))))
                .type(named("net.minecraft.client.gui.Gui"))
                .transform((builder, td, cl, module, pd) -> builder
                        .visit(Advice.to(KeepAwakeAdvice.class).on(named("isPausing"))))
                .installOn(inst);
    }

    /**
     * Copies just {@code LiveNbtBoot.class} out of the agent jar into a minimal temp jar and appends
     * it to the bootstrap search. Only that one class may go to bootstrap: appending the whole jar
     * would let parent-first delegation resolve the entire hook stack in the bootstrap loader, where
     * the Minecraft classes are invisible.
     */
    private static void injectBootClass(Instrumentation inst, Path agentJar) throws IOException {
        String entry = "dev/nitro/livenbt/boot/LiveNbtBoot.class";
        Path bootJar = Files.createTempFile("livenbt-boot", ".jar");
        try (JarFile self = new JarFile(agentJar.toFile());
             JarOutputStream out = new JarOutputStream(Files.newOutputStream(bootJar))) {
            ZipEntry cls = self.getEntry(entry);
            if (cls == null) throw new IOException(entry + " missing from " + agentJar);
            out.putNextEntry(new JarEntry(entry));
            self.getInputStream(cls).transferTo(out);
            out.closeEntry();
        }
        inst.appendToBootstrapClassLoaderSearch(new JarFile(bootJar.toFile()));
    }

    private static Path ownJar() {
        try {
            return Path.of(LiveNbtAgent.class.getProtectionDomain().getCodeSource().getLocation().toURI());
        } catch (Exception e) {
            throw new IllegalStateException("cannot locate the LiveNBT agent jar", e);
        }
    }

    /** Woven into {@code tickServer}: delegates to the boot bridge (which starts LiveNBT on the first tick). */
    public static class TickAdvice {
        @Advice.OnMethodEnter(suppress = Throwable.class)
        static void enter(@Advice.This Object server) {
            LiveNbtBoot.onTick(server);
        }
    }

    /** Woven into {@code stopServer}: delegates to the boot bridge (which also re-arms start for the next world). */
    public static class StopAdvice {
        @Advice.OnMethodEnter(suppress = Throwable.class)
        static void enter(@Advice.This Object server) {
            LiveNbtBoot.onStop(server);
        }
    }

    /**
     * Woven into {@code Gui.isPausing()}: while an editor is attached, override a {@code true} result
     * to {@code false} so the game doesn't pause. Leaves the real result alone when no client is
     * connected, so normal pausing is untouched.
     */
    public static class KeepAwakeAdvice {
        @Advice.OnMethodExit(suppress = Throwable.class)
        static void exit(@Advice.Return(readOnly = false) boolean pausing) {
            if (pausing && LiveNbtBoot.keepAwake()) {
                pausing = false;
            }
        }
    }
}
