package dev.nitro.livenbt.roots;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class RootRefTest {
    @Test void parsesPlayerAndWorld() throws Exception {
        assertEquals(new RootRef(RootRef.Kind.PLAYER, "Nitro70"), RootRef.parse("player:Nitro70"));
        assertEquals(new RootRef(RootRef.Kind.WORLD, "minecraft:overworld"), RootRef.parse("world:minecraft:overworld"));
    }
    @Test void rejectsUnknown() {
        assertThrows(RootException.class, () -> RootRef.parse("item:hand"));
        assertThrows(RootException.class, () -> RootRef.parse("player:"));
        assertThrows(RootException.class, () -> RootRef.parse(""));
        assertThrows(RootException.class, () -> RootRef.parse(null));
    }

    @Test void parsesInventory() throws Exception {
        assertEquals(new RootRef(RootRef.Kind.INVENTORY, "Nitro70"), RootRef.parse("inventory:Nitro70"));
    }
    @Test void rejectsEmptyInventory() {
        assertThrows(RootException.class, () -> RootRef.parse("inventory:"));
    }
}
