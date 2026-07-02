package dev.nitro.livenbt.attach;

import com.sun.tools.attach.VirtualMachine;

/**
 * Standalone launcher for the "arg-less" flow: attach to an already-running JVM by pid and load an
 * agent jar into it via the JVM Dynamic Attach API — no {@code -javaagent} argument, no launcher
 * profile edits. The desktop app invokes this with Minecraft's OWN bundled Java (which ships the
 * {@code jdk.attach} module), so nothing external is required.
 *
 * <pre>java -cp livenbt-agent.jar dev.nitro.livenbt.attach.SelfAttach &lt;pid&gt; &lt;agent-jar-path&gt;</pre>
 *
 * Exit codes: 0 attached, 1 attach/load failed, 2 bad arguments.
 */
public final class SelfAttach {
    private SelfAttach() {}

    public static void main(String[] args) {
        if (args.length < 2) {
            System.err.println("usage: SelfAttach <pid> <agent-jar-path>");
            System.exit(2);
            return;
        }
        String pid = args[0];
        String agentJar = args[1];
        try {
            VirtualMachine vm = VirtualMachine.attach(pid);
            try {
                vm.loadAgent(agentJar);
            } finally {
                vm.detach();
            }
            System.out.println("LiveNBT: attached to pid " + pid);
        } catch (Exception e) {
            System.err.println("LiveNBT: attach failed: " + e.getMessage());
            System.exit(1);
        }
    }
}
