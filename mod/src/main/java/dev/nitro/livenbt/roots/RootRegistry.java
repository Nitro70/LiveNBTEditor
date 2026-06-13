package dev.nitro.livenbt.roots;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;

/** Resolves root ids to adapters and lists available roots. Server thread only. */
public final class RootRegistry {
    private final MinecraftServer server;

    public RootRegistry(MinecraftServer server) { this.server = server; }

    public RootAdapter resolve(String rootId) throws RootException {
        RootRef ref = RootRef.parse(rootId);
        return switch (ref.kind()) {
            case PLAYER -> new PlayerRoot(server, ref.name());
            case WORLD -> new WorldRoot(server, ref.name());
        };
    }

    public JsonObject listRoots() {
        JsonObject o = new JsonObject();
        JsonArray players = new JsonArray();
        for (ServerPlayer p : server.getPlayerList().getPlayers()) players.add(p.getName().getString());
        JsonArray worlds = new JsonArray();
        for (ServerLevel level : server.getAllLevels()) worlds.add(level.dimension().identifier().toString());
        o.add("players", players);
        o.add("worlds", worlds);
        return o;
    }
}
