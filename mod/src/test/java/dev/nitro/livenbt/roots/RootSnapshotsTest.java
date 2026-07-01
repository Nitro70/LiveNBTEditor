package dev.nitro.livenbt.roots;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class RootSnapshotsTest {

    @Test void defaultsAreWellFormedEmptyShapes() {
        RootSnapshots s = new RootSnapshots();
        // A request that races startup must still get valid JSON, never null.
        assertTrue(s.roots().getAsJsonArray("players").isEmpty());
        assertTrue(s.roots().getAsJsonArray("worlds").isEmpty());
        assertTrue(s.registries().getAsJsonArray("items").isEmpty());
        assertTrue(s.registries().getAsJsonArray("enchantments").isEmpty());
    }

    @Test void servesTheLastPublishedSnapshot() {
        RootSnapshots s = new RootSnapshots();
        JsonObject roots = new JsonObject();
        JsonArray players = new JsonArray();
        players.add("nitro700");
        roots.add("players", players);
        s.setRoots(roots);
        assertEquals("nitro700", s.roots().getAsJsonArray("players").get(0).getAsString());
    }

    @Test void publishingReplacesRatherThanMutates() {
        RootSnapshots s = new RootSnapshots();
        JsonObject first = s.registries();
        JsonObject next = new JsonObject();
        next.add("enchantments", new JsonArray());
        s.setRegistries(next);
        // The previously handed-out reference is untouched (readers may still be serializing it).
        assertTrue(first.getAsJsonArray("enchantments").isEmpty());
        assertSame(next, s.registries());
    }
}
