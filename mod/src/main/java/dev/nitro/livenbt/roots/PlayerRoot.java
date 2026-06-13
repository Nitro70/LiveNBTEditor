package dev.nitro.livenbt.roots;

import dev.nitro.livenbt.path.NbtPath;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.Tag;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.ProblemReporter;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.level.storage.TagValueOutput;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.Set;

/**
 * Live player data. Reads serialize the living entity; generic writes
 * snapshot -> path-edit -> reload; hot fields use direct game APIs instead
 * (reload has side effects for them).
 */
public final class PlayerRoot implements RootAdapter {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");
    private final MinecraftServer server;
    private final String playerName;

    public PlayerRoot(MinecraftServer server, String playerName) {
        this.server = server;
        this.playerName = playerName;
    }

    private ServerPlayer player() throws RootException {
        ServerPlayer p = server.getPlayerList().getPlayerByName(playerName);
        if (p == null) throw new RootException("player not online: " + playerName);
        return p;
    }

    @Override
    public CompoundTag snapshot() throws RootException {
        return snapshotOf(player());
    }

    private CompoundTag snapshotOf(ServerPlayer p) {
        TagValueOutput out = TagValueOutput.createWithContext(ProblemReporter.DISCARDING, server.registryAccess());
        p.saveWithoutId(out);
        return out.buildResult();
    }

    @Override
    public void set(String path, Tag value) throws RootException, NbtPath.PathException {
        ServerPlayer p = player();
        if (applyFastPath(p, path, value)) return;
        genericEdit(p, tag -> NbtPath.set(tag, path, value));
    }

    @Override
    public void add(String path, Tag value) throws RootException, NbtPath.PathException {
        genericEdit(player(), tag -> NbtPath.add(tag, path, value));
    }

    @Override
    public void delete(String path) throws RootException, NbtPath.PathException {
        genericEdit(player(), tag -> NbtPath.delete(tag, path));
    }

    @FunctionalInterface
    private interface Edit { void apply(CompoundTag tag) throws NbtPath.PathException; }

    private void genericEdit(ServerPlayer p, Edit edit) throws RootException, NbtPath.PathException {
        CompoundTag before = snapshotOf(p);
        CompoundTag edited = before.copy();
        edit.apply(edited);
        try {
            p.load(TagValueInput.create(ProblemReporter.DISCARDING, server.registryAccess(), edited));
        } catch (Exception e) {
            // restore pre-edit state; never leave a half-loaded player
            try {
                p.load(TagValueInput.create(ProblemReporter.DISCARDING, server.registryAccess(), before));
            } catch (Exception restoreFailure) {
                e.addSuppressed(restoreFailure);
                LOG.error("RESTORE FAILED for player {} — entity may be in an inconsistent state", playerName, e);
                throw new RootException("edit failed AND restore failed for " + playerName
                        + " — player may be in an inconsistent state: " + e, e);
            }
            LOG.warn("entity reload rejected for {} (player restored)", playerName, e);
            throw new RootException("edit rejected by entity reload (player restored): " + e, e);
        }
    }

    /** Returns true if the path was handled by a dedicated game API. */
    private boolean applyFastPath(ServerPlayer p, String path, Tag value) throws RootException {
        switch (path) {
            case "Health" -> { p.setHealth(requireFloat(value, path)); return true; }
            case "foodLevel" -> { p.getFoodData().setFoodLevel(requireInt(value, path)); return true; }
            case "XpLevel" -> { p.setExperienceLevels(requireInt(value, path)); return true; }
            case "Pos" -> {
                double[] xyz = requireDoubles(value, 3, path);
                p.teleportTo(xyz[0], xyz[1], xyz[2]);
                return true;
            }
            case "Pos[0]", "Pos[1]", "Pos[2]" -> {
                int axis = path.charAt(4) - '0';
                double[] xyz = {p.getX(), p.getY(), p.getZ()};
                xyz[axis] = requireDouble(value, path);
                p.teleportTo(xyz[0], xyz[1], xyz[2]);
                return true;
            }
            case "Rotation" -> {
                double[] rot = requireDoubles(value, 2, path);
                p.teleportTo((ServerLevel) p.level(), p.getX(), p.getY(), p.getZ(),
                        Set.of(), (float) rot[0], (float) rot[1], false);
                return true;
            }
            default -> {
                if (path.startsWith("abilities.")) return applyAbility(p, path.substring(10), value, path);
                return false;
            }
        }
    }

    private boolean applyAbility(ServerPlayer p, String key, Tag value, String path) throws RootException {
        var ab = p.getAbilities();
        switch (key) {
            case "invulnerable" -> ab.invulnerable = requireBool(value, path);
            case "flying" -> ab.flying = requireBool(value, path);
            case "mayfly" -> ab.mayfly = requireBool(value, path);
            case "instabuild" -> ab.instabuild = requireBool(value, path);
            case "mayBuild" -> ab.mayBuild = requireBool(value, path);
            case "flySpeed" -> ab.setFlyingSpeed(requireFloat(value, path));
            case "walkSpeed" -> ab.setWalkingSpeed(requireFloat(value, path));
            default -> { return false; } // unknown ability key: fall through to generic edit
        }
        p.onUpdateAbilities();
        return true;
    }

    // ---- value coercion helpers ----

    private static float requireFloat(Tag v, String path) throws RootException {
        float f = v.asFloat().orElseThrow(() -> typeError("float", path));
        if (!Float.isFinite(f)) throw typeError("finite float", path);
        return f;
    }

    private static int requireInt(Tag v, String path) throws RootException {
        return v.asInt().orElseThrow(() -> typeError("int", path));
    }

    private static double requireDouble(Tag v, String path) throws RootException {
        double d = v.asDouble().orElseThrow(() -> typeError("double", path));
        if (!Double.isFinite(d)) throw typeError("finite double", path);
        return d;
    }

    private static boolean requireBool(Tag v, String path) throws RootException {
        return v.asBoolean().orElseThrow(() -> typeError("byte 0/1", path));
    }

    private static double[] requireDoubles(Tag v, int n, String path) throws RootException {
        if (!(v instanceof ListTag list) || list.size() != n) throw typeError("list of " + n + " numbers", path);
        double[] out = new double[n];
        for (int i = 0; i < n; i++) {
            out[i] = list.get(i).asDouble().orElseThrow(() -> typeError("list of " + n + " numbers", path));
            if (!Double.isFinite(out[i])) throw typeError("list of " + n + " finite numbers", path);
        }
        return out;
    }

    private static RootException typeError(String expected, String path) {
        return new RootException("expected " + expected + " for " + path);
    }
}
