package dev.nitro.livenbt.path;

import net.minecraft.nbt.*;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class NbtPathTest {

    private static CompoundTag sample() {
        CompoundTag root = new CompoundTag();
        CompoundTag abilities = new CompoundTag();
        abilities.putByte("mayfly", (byte) 0);
        root.put("abilities", abilities);
        ListTag inv = new ListTag();
        CompoundTag slot = new CompoundTag();
        slot.putInt("count", 64);
        inv.add(slot);
        root.put("Inventory", inv);
        return root;
    }

    @Test void getNested() throws Exception {
        assertEquals(new ByteTag((byte) 0), NbtPath.get(sample(), "abilities.mayfly"));
    }

    @Test void getThroughList() throws Exception {
        assertEquals(new IntTag(64), NbtPath.get(sample(), "Inventory[0].count"));
    }

    @Test void getMissingReturnsNull() throws Exception {
        assertNull(NbtPath.get(sample(), "abilities.nope"));
        assertNull(NbtPath.get(sample(), "Inventory[5]"));
    }

    @Test void getEmptyPathReturnsRoot() throws Exception {
        CompoundTag r = sample();
        assertSame(r, NbtPath.get(r, ""));
    }

    @Test void setReplacesValue() throws Exception {
        CompoundTag r = sample();
        NbtPath.set(r, "abilities.mayfly", new ByteTag((byte) 1));
        assertEquals(new ByteTag((byte) 1), NbtPath.get(r, "abilities.mayfly"));
    }

    @Test void setCreatesNewKey() throws Exception {
        CompoundTag r = sample();
        NbtPath.set(r, "abilities.flySpeed", new FloatTag(0.1f));
        assertEquals(new FloatTag(0.1f), NbtPath.get(r, "abilities.flySpeed"));
    }

    @Test void setListElementAndAppend() throws Exception {
        CompoundTag r = sample();
        NbtPath.set(r, "Inventory[0].count", new IntTag(1));
        assertEquals(new IntTag(1), NbtPath.get(r, "Inventory[0].count"));
        // index == size appends
        CompoundTag extra = new CompoundTag();
        NbtPath.set(r, "Inventory[1]", extra);
        assertEquals(2, ((ListTag) NbtPath.get(r, "Inventory")).size());
    }

    @Test void setOutOfRangeFails() {
        var e = assertThrows(NbtPath.PathException.class,
                () -> NbtPath.set(sample(), "Inventory[9]", new CompoundTag()));
        assertTrue(e.getMessage().contains("Inventory"));
    }

    @Test void setMissingParentFails() {
        assertThrows(NbtPath.PathException.class,
                () -> NbtPath.set(sample(), "no.such.parent", new IntTag(1)));
    }

    @Test void setRootFails() {
        assertThrows(NbtPath.PathException.class, () -> NbtPath.set(sample(), "", new IntTag(1)));
    }

    @Test void addAppendsToList() throws Exception {
        CompoundTag r = sample();
        NbtPath.add(r, "Inventory", new CompoundTag());
        assertEquals(2, ((ListTag) NbtPath.get(r, "Inventory")).size());
    }

    @Test void addNewCompoundKey() throws Exception {
        CompoundTag r = sample();
        NbtPath.add(r, "abilities.flying", new ByteTag((byte) 1));
        assertEquals(new ByteTag((byte) 1), NbtPath.get(r, "abilities.flying"));
    }

    @Test void addExistingKeyFails() {
        assertThrows(NbtPath.PathException.class,
                () -> NbtPath.add(sample(), "abilities.mayfly", new ByteTag((byte) 1)));
    }

    @Test void deleteKeyAndElement() throws Exception {
        CompoundTag r = sample();
        NbtPath.delete(r, "abilities.mayfly");
        assertNull(NbtPath.get(r, "abilities.mayfly"));
        NbtPath.delete(r, "Inventory[0]");
        assertEquals(0, ((ListTag) NbtPath.get(r, "Inventory")).size());
    }

    @Test void deleteMissingFails() {
        assertThrows(NbtPath.PathException.class, () -> NbtPath.delete(sample(), "abilities.nope"));
    }

    @Test void parseErrors() {
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "a..b"));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "a[x]"));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "a[1"));
    }

    @Test void typeMismatchFails() {
        // abilities is a compound, not a list
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "abilities[0]"));
    }

    @Test void nullInputsFailCleanly() {
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), null));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.set(sample(), "abilities.mayfly", null));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.add(sample(), "Inventory", null));
    }

    @Test void strictGrammarRejectsAmbiguousSpellings() {
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "Inventory[0]count"));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "abilities.[0]"));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "a."));
        assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), "Inventory[-1]"));
    }

    @Test void longHostilePathErrorIsTruncated() {
        String huge = "x".repeat(10_000) + "[";
        var e = assertThrows(NbtPath.PathException.class, () -> NbtPath.get(sample(), huge));
        assertTrue(e.getMessage().length() < 400);
    }

    @Test void deleteListIndexOutOfRangeFails() {
        assertThrows(NbtPath.PathException.class, () -> NbtPath.delete(sample(), "Inventory[5]"));
    }
}
