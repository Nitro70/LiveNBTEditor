package dev.nitro.livenbt.roots;

import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class InventorySlotsTest {

    private static CompoundTag item(int slot, String id, int count) {
        CompoundTag c = new CompoundTag();
        c.putByte("Slot", (byte) slot);
        c.putString("id", id);
        c.putInt("count", count);
        return c;
    }

    @Test void hasAll41SlotNumbers() {
        assertEquals(41, InventorySlots.SLOTS.length);
        assertEquals(0, InventorySlots.SLOTS[0]);
        assertEquals(35, InventorySlots.SLOTS[35]);
        assertEquals(36, InventorySlots.SLOTS[36]);
        assertEquals(39, InventorySlots.SLOTS[39]);
        assertEquals(40, InventorySlots.SLOTS[40]);
    }

    @Test void toVirtualIncludesEverySlotWithEmptiesAsEmptyCompound() {
        ListTag inv = new ListTag();
        inv.add(item(0, "minecraft:diamond_sword", 1));
        CompoundTag v = InventorySlots.toVirtual(inv);

        assertEquals(41, v.keySet().size());
        CompoundTag s0 = v.getCompound("0").orElseThrow();
        assertEquals("minecraft:diamond_sword", s0.getString("id").orElseThrow());
        assertFalse(s0.contains("Slot"));
        assertTrue(v.getCompound("5").orElseThrow().isEmpty());
        assertTrue(v.getCompound("40").orElseThrow().isEmpty());
    }

    @Test void withSlotReplacesAnExistingSlot() {
        ListTag inv = new ListTag();
        inv.add(item(0, "minecraft:stone", 1));
        CompoundTag newItem = new CompoundTag();
        newItem.putString("id", "minecraft:dirt");
        newItem.putInt("count", 64);

        ListTag out = InventorySlots.withSlot(inv, 0, newItem);
        assertEquals(1, out.size());
        CompoundTag e = out.getCompound(0).orElseThrow();
        assertEquals("minecraft:dirt", e.getString("id").orElseThrow());
        assertEquals(0, e.getByte("Slot").orElseThrow().byteValue());
    }

    @Test void withSlotEmptyCompoundClearsTheSlot() {
        ListTag inv = new ListTag();
        inv.add(item(0, "minecraft:stone", 1));
        inv.add(item(1, "minecraft:dirt", 1));
        ListTag out = InventorySlots.withSlot(inv, 0, new CompoundTag());
        assertEquals(1, out.size());
        assertEquals(1, out.getCompound(0).orElseThrow().getByte("Slot").orElseThrow().byteValue());
    }

    @Test void withSlotAddsANewSlot() {
        ListTag out = InventorySlots.withSlot(new ListTag(), 8, item(8, "minecraft:torch", 5));
        assertEquals(1, out.size());
        assertEquals(8, out.getCompound(0).orElseThrow().getByte("Slot").orElseThrow().byteValue());
    }

    @Test void toVirtualKeepsFirstOnDuplicate() {
        ListTag inv = new ListTag();
        inv.add(item(0, "minecraft:stone", 1));
        inv.add(item(0, "minecraft:dirt", 1));   // duplicate slot
        CompoundTag v = InventorySlots.toVirtual(inv);
        assertEquals("minecraft:stone", v.getCompound("0").orElseThrow().getString("id").orElseThrow());
    }
}
