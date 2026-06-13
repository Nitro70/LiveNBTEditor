package dev.nitro.livenbt.roots;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.ByteTag;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.IntTag;
import net.minecraft.nbt.StringTag;
import net.minecraft.nbt.Tag;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.Difficulty;
import net.minecraft.world.clock.WorldClock;
import net.minecraft.world.level.border.WorldBorder;
import net.minecraft.world.level.gamerules.GameRule;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.saveddata.WeatherData;
import net.minecraft.world.level.storage.LevelData;
import net.minecraft.world.level.storage.ServerLevelData;

import java.util.Locale;
import java.util.Optional;

/**
 * Virtual NBT tree over live world state. Every leaf maps to a real
 * getter/setter; nothing here is read from or written to level.dat.
 */
public final class WorldRoot implements RootAdapter {
    private final MinecraftServer server;
    private final String dimensionId;

    public WorldRoot(MinecraftServer server, String dimensionId) {
        this.server = server;
        this.dimensionId = dimensionId;
    }

    private ServerLevel level() throws RootException {
        for (ServerLevel l : server.getAllLevels()) {
            if (l.dimension().identifier().toString().equals(dimensionId)) return l;
        }
        throw new RootException("no such dimension: " + dimensionId);
    }

    @Override
    public CompoundTag snapshot() throws RootException {
        ServerLevel level = level();
        CompoundTag root = new CompoundTag();

        CompoundTag rules = new CompoundTag();
        GameRules gr = level.getGameRules();
        gr.availableRules().forEach(rule -> rules.put(rule.id(), ruleValue(gr, rule)));
        root.put("gamerules", rules);

        CompoundTag time = new CompoundTag();
        time.putLong("gameTime", level.getLevelData().getGameTime());
        CompoundTag clocks = new CompoundTag();
        server.registryAccess().lookupOrThrow(Registries.WORLD_CLOCK).listElements().forEach(ref -> {
            CompoundTag c = new CompoundTag();
            c.putLong("totalTicks", level.clockManager().getTotalTicks(ref));
            clocks.put(ref.key().identifier().toString(), c);
        });
        time.put("clocks", clocks);
        root.put("time", time);

        WeatherData w = level.getWeatherData();
        CompoundTag weather = new CompoundTag();
        weather.putInt("clearWeatherTime", w.getClearWeatherTime());
        weather.putBoolean("raining", w.isRaining());
        weather.putInt("rainTime", w.getRainTime());
        weather.putBoolean("thundering", w.isThundering());
        weather.putInt("thunderTime", w.getThunderTime());
        root.put("weather", weather);

        LevelData.RespawnData rd = level.getRespawnData();
        CompoundTag spawn = new CompoundTag();
        spawn.putInt("x", rd.pos().getX());
        spawn.putInt("y", rd.pos().getY());
        spawn.putInt("z", rd.pos().getZ());
        spawn.putFloat("yaw", rd.yaw());
        spawn.putFloat("pitch", rd.pitch());
        root.put("spawn", spawn);

        WorldBorder b = level.getWorldBorder();
        CompoundTag border = new CompoundTag();
        border.putDouble("centerX", b.getCenterX());
        border.putDouble("centerZ", b.getCenterZ());
        border.putDouble("size", b.getSize());
        border.putDouble("damagePerBlock", b.getDamagePerBlock());
        border.putInt("warningBlocks", b.getWarningBlocks());
        border.putInt("warningTime", b.getWarningTime());
        border.putDouble("safeZone", b.getSafeZone());
        root.put("worldborder", border);

        root.putString("difficulty", level.getLevelData().getDifficulty().name().toLowerCase(Locale.ROOT));
        root.putBoolean("difficultyLocked", level.getLevelData().isDifficultyLocked());
        return root;
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private static Tag ruleValue(GameRules gr, GameRule<?> rule) {
        Object v = gr.get((GameRule) rule);
        if (v instanceof Boolean bool) return ByteTag.valueOf(bool ? (byte) 1 : (byte) 0);
        if (v instanceof Integer i) return IntTag.valueOf(i);
        return StringTag.valueOf(((GameRule<Object>) rule).serialize(v));
    }

    @Override
    public void set(String path, Tag value) throws RootException {
        ServerLevel level = level();
        if (path.startsWith("gamerules.")) { setRule(level, path.substring(10), value); return; }
        switch (path) {
            // game time is server-global; non-overworld levels wrap it in DerivedLevelData
            // whose setGameTime is a no-op, so always write through the overworld's data
            case "time.gameTime" -> ((ServerLevelData) server.overworld().getLevelData()).setGameTime(requireLong(value, path));
            case "weather.clearWeatherTime" -> editWeather(level, w -> w.setClearWeatherTime(requireInt(value, path)));
            case "weather.raining" -> editWeather(level, w -> w.setRaining(requireBool(value, path)));
            case "weather.rainTime" -> editWeather(level, w -> w.setRainTime(requireInt(value, path)));
            case "weather.thundering" -> editWeather(level, w -> w.setThundering(requireBool(value, path)));
            case "weather.thunderTime" -> editWeather(level, w -> w.setThunderTime(requireInt(value, path)));
            case "spawn.x", "spawn.y", "spawn.z", "spawn.yaw", "spawn.pitch" -> setSpawn(level, path, value);
            case "worldborder.centerX" -> level.getWorldBorder().setCenter(requireDouble(value, path), level.getWorldBorder().getCenterZ());
            case "worldborder.centerZ" -> level.getWorldBorder().setCenter(level.getWorldBorder().getCenterX(), requireDouble(value, path));
            case "worldborder.size" -> level.getWorldBorder().setSize(requireDouble(value, path));
            case "worldborder.damagePerBlock" -> level.getWorldBorder().setDamagePerBlock(requireDouble(value, path));
            case "worldborder.warningBlocks" -> level.getWorldBorder().setWarningBlocks(requireInt(value, path));
            case "worldborder.warningTime" -> level.getWorldBorder().setWarningTime(requireInt(value, path));
            case "worldborder.safeZone" -> level.getWorldBorder().setSafeZone(requireDouble(value, path));
            case "difficulty" -> {
                Difficulty d = Difficulty.byName(requireString(value, path));
                if (d == null) throw new RootException("unknown difficulty (peaceful|easy|normal|hard)");
                server.setDifficulty(d, true);
            }
            case "difficultyLocked" -> server.setDifficultyLocked(requireBool(value, path));
            default -> {
                if (path.startsWith("time.clocks.") && path.endsWith(".totalTicks")
                        && path.length() > "time.clocks.".length() + ".totalTicks".length()) {
                    setClock(level, path.substring("time.clocks.".length(), path.length() - ".totalTicks".length()),
                            requireLong(value, path));
                    return;
                }
                throw new RootException("path is not editable on world roots: " + path);
            }
        }
    }

    @Override
    public void add(String path, Tag value) throws RootException {
        throw new RootException("world roots do not support add");
    }

    @Override
    public void delete(String path) throws RootException {
        throw new RootException("world roots do not support delete");
    }

    @FunctionalInterface
    private interface WeatherEdit { void apply(WeatherData w) throws RootException; }

    private static void editWeather(ServerLevel level, WeatherEdit edit) throws RootException {
        WeatherData w = level.getWeatherData();
        edit.apply(w);
        w.setDirty();
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private void setRule(ServerLevel level, String ruleId, Tag value) throws RootException {
        GameRules gr = level.getGameRules();
        GameRule<?> rule = gr.availableRules()
                .filter(r -> r.id().equals(ruleId))
                .findFirst()
                .orElseThrow(() -> new RootException("unknown gamerule: " + ruleId));
        String s;
        if (rule.valueClass() == Boolean.class) {
            s = String.valueOf(requireBool(value, "gamerules." + ruleId));
        } else if (rule.valueClass() == Integer.class) {
            s = String.valueOf(requireInt(value, "gamerules." + ruleId));
        } else {
            s = requireString(value, "gamerules." + ruleId);
        }
        Optional<?> parsed = rule.deserialize(s).result();
        if (parsed.isEmpty()) throw new RootException("invalid value '" + s + "' for gamerule " + ruleId);
        gr.set((GameRule) rule, parsed.get(), server);
    }

    private void setClock(ServerLevel level, String clockId, long ticks) throws RootException {
        Holder<WorldClock> clock = server.registryAccess().lookupOrThrow(Registries.WORLD_CLOCK)
                .listElements()
                .filter(ref -> ref.key().identifier().toString().equals(clockId))
                .findFirst()
                .map(ref -> (Holder<WorldClock>) ref)
                .orElseThrow(() -> new RootException("unknown clock: " + clockId));
        level.clockManager().setTotalTicks(clock, ticks);
    }

    private void setSpawn(ServerLevel level, String path, Tag value) throws RootException {
        LevelData.RespawnData rd = level.getRespawnData();
        BlockPos pos = rd.pos();
        float yaw = rd.yaw(), pitch = rd.pitch();
        switch (path) {
            case "spawn.x" -> pos = new BlockPos(requireInt(value, path), pos.getY(), pos.getZ());
            case "spawn.y" -> pos = new BlockPos(pos.getX(), requireInt(value, path), pos.getZ());
            case "spawn.z" -> pos = new BlockPos(pos.getX(), pos.getY(), requireInt(value, path));
            case "spawn.yaw" -> yaw = requireFloat(value, path);
            case "spawn.pitch" -> pitch = requireFloat(value, path);
        }
        level.setRespawnData(LevelData.RespawnData.of(rd.dimension(), pos, yaw, pitch));
    }

    // ---- value coercion helpers ----

    private static int requireInt(Tag v, String path) throws RootException {
        return v.asInt().orElseThrow(() -> typeError("int", path));
    }

    private static long requireLong(Tag v, String path) throws RootException {
        return v.asLong().orElseThrow(() -> typeError("long", path));
    }

    private static float requireFloat(Tag v, String path) throws RootException {
        float f = v.asFloat().orElseThrow(() -> typeError("float", path));
        if (!Float.isFinite(f)) throw typeError("finite float", path);
        return f;
    }

    private static double requireDouble(Tag v, String path) throws RootException {
        double d = v.asDouble().orElseThrow(() -> typeError("double", path));
        if (!Double.isFinite(d)) throw typeError("finite double", path);
        return d;
    }

    private static boolean requireBool(Tag v, String path) throws RootException {
        return v.asBoolean().orElseThrow(() -> typeError("byte 0/1", path));
    }

    private static String requireString(Tag v, String path) throws RootException {
        return v.asString().orElseThrow(() -> typeError("string", path));
    }

    private static RootException typeError(String expected, String path) {
        return new RootException("expected " + expected + " for " + path);
    }
}
