package dev.nitro.livenbt.roots;

import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.Tag;

/** Pure slot<->Inventory-list surgery in NBT save-format space. No game access. */
public final class InventorySlots {
    private InventorySlots() {}

    /** The 41 player slot numbers: 0-8 hotbar, 9-35 main, 36-39 armor, 40 offhand. */
    public static final int[] SLOTS;
    static {
        int[] s = new int[41];
        for (int n = 0; n <= 40; n++) s[n] = n;   // 0-35 hotbar+main, 36-39 armor, 40 offhand
        SLOTS = s;
    }

    /** Each slot number -> its item compound (Slot key stripped), or an empty compound if vacant. */
    public static CompoundTag toVirtual(ListTag inventory) {
        CompoundTag out = new CompoundTag();
        for (int slot : SLOTS) out.put(Integer.toString(slot), new CompoundTag());
        for (Tag t : inventory) {
            if (!(t instanceof CompoundTag entry)) continue;
            String key = Integer.toString(slotOf(entry));
            if (!out.keySet().contains(key)) continue;     // ignore slots outside the 41 we expose
            if (out.getCompound(key).map(c -> !c.isEmpty()).orElse(false)) continue; // keep first on duplicate
            CompoundTag copy = entry.copy();
            copy.remove("Slot");
            out.put(key, copy);
        }
        return out;
    }

    /**
     * A copy of {@code inventory} with {@code slot} set to {@code item} (cleared if item is empty).
     * {@code slot} must be one of {@link #SLOTS}.
     */
    public static ListTag withSlot(ListTag inventory, int slot, CompoundTag item) {
        boolean known = false;
        for (int s : SLOTS) if (s == slot) { known = true; break; }
        if (!known) throw new IllegalArgumentException("not an inventory slot: " + slot);
        ListTag out = new ListTag();
        for (Tag t : inventory) {
            if (t instanceof CompoundTag e && slotOf(e) == slot) continue;   // drop the old occupant
            out.add(t.copy());
        }
        if (!item.keySet().isEmpty()) {
            CompoundTag entry = item.copy();
            entry.putByte("Slot", (byte) slot);
            out.add(entry);
        }
        return out;
    }

    private static int slotOf(CompoundTag entry) {
        Tag s = entry.get("Slot");
        return s == null ? Integer.MIN_VALUE : s.asByte().map(Byte::intValue).orElse(Integer.MIN_VALUE);
    }
}
