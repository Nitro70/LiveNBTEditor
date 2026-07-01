package dev.nitro.livenbt.agent;

import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;
import net.minecraft.server.MinecraftServer;

import java.lang.instrument.Instrumentation;

import static net.bytebuddy.matcher.ElementMatchers.named;

/**
 * {@code -javaagent} entrypoint. Weaves the vanilla server lifecycle methods to drive
 * {@link LiveNbtHooks}. No mod loader involved.
 *
 * <p>Singleplayer's {@code IntegratedServer} OVERRIDES {@code tickServer} (verified via javap on
 * the 26.2 jar), so we match all three server types; {@code Advice.on(named("tickServer"))} only
 * takes on the type that declares it. The advice bodies are kept trivial — a single public call into
 * {@link LiveNbtHooks} — because inlined advice may not access private members of this class; all
 * state (including the start-once flag) therefore lives in {@code LiveNbtHooks}.
 *
 * <p>We also weave the client HUD's {@code Gui.isPausing()}: while an editor is attached it is forced
 * to return false, which drives {@code Minecraft.pause} false so singleplayer keeps ticking (edits
 * apply and sync live in-game) instead of pausing when you tab to the app — the automatic equivalent
 * of Open to LAN. {@code isPausing} is only consulted by the pause computation, so this is surgical.
 * On a dedicated server the {@code Gui} class never loads, so that matcher simply never fires.
 */
public final class LiveNbtAgent {

    private LiveNbtAgent() {}

    public static void premain(String args, Instrumentation inst) {
        new AgentBuilder.Default()
                .with(AgentBuilder.RedefinitionStrategy.RETRANSFORMATION)
                .type(named("net.minecraft.server.MinecraftServer")
                        .or(named("net.minecraft.client.server.IntegratedServer"))
                        .or(named("net.minecraft.server.dedicated.DedicatedServer"))
                        .or(named("net.minecraft.client.gui.Gui")))
                .transform((builder, td, cl, module, pd) -> builder
                        .visit(Advice.to(TickAdvice.class).on(named("tickServer")))
                        .visit(Advice.to(StopAdvice.class).on(named("stopServer")))
                        .visit(Advice.to(KeepAwakeAdvice.class).on(named("isPausing"))))
                .installOn(inst);
    }

    /** Woven into {@code tickServer}: delegates to the hook (which starts LiveNBT on the first tick). */
    public static class TickAdvice {
        @Advice.OnMethodEnter
        static void enter(@Advice.This Object server) {
            LiveNbtHooks.onServerTick((MinecraftServer) server);
        }
    }

    /** Woven into {@code stopServer}: delegates to the hook (which also re-arms start for the next world). */
    public static class StopAdvice {
        @Advice.OnMethodEnter
        static void enter(@Advice.This Object server) {
            LiveNbtHooks.onServerStopping((MinecraftServer) server);
        }
    }

    /**
     * Woven into {@code Gui.isPausing()}: while an editor is attached, override a {@code true} result
     * to {@code false} so the game doesn't pause. Leaves the real result alone when no client is
     * connected, so normal pausing is untouched.
     */
    public static class KeepAwakeAdvice {
        @Advice.OnMethodExit
        static void exit(@Advice.Return(readOnly = false) boolean pausing) {
            if (pausing && LiveNbtHooks.keepAwake()) {
                pausing = false;
            }
        }
    }
}
