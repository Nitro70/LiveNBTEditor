package dev.nitro.livenbt.roots;

import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.Tag;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.ProblemReporter;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.level.storage.TagValueOutput;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Live player inventory as a virtual 41-slot compound. Edits replace one slot in
 * the Inventory list (NBT slot-number space) and reload the entity.
 */
public final class InventoryRoot implements RootAdapter {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");
    private final MinecraftServer server;
    private final String playerName;

    public InventoryRoot(MinecraftServer server, String playerName) {
        this.server = server;
        this.playerName = playerName;
    }

    private ServerPlayer player() throws RootException {
        ServerPlayer p = server.getPlayerList().getPlayerByName(playerName);
        if (p == null) throw new RootException("player not online: " + playerName);
        return p;
    }

    private CompoundTag fullSnapshot(ServerPlayer p) {
        TagValueOutput out = TagValueOutput.createWithContext(ProblemReporter.DISCARDING, server.registryAccess());
        p.saveWithoutId(out);
        return out.buildResult();
    }

    private static ListTag inventoryOf(CompoundTag full) {
        return full.get("Inventory") instanceof ListTag l ? l : new ListTag();
    }

    @Override
    public CompoundTag snapshot() throws RootException {
        return InventorySlots.toVirtual(inventoryOf(fullSnapshot(player())));
    }

    @Override
    public void set(String path, Tag value) throws RootException {
        int slot = parseSlot(path);
        if (!(value instanceof CompoundTag item))
            throw new RootException("inventory slot value must be a compound: " + path);
        applyInventory(slot, item);
    }

    @Override
    public void delete(String path) throws RootException {
        applyInventory(parseSlot(path), new CompoundTag());   // empty compound = clear
    }

    @Override
    public void add(String path, Tag value) throws RootException {
        throw new RootException("inventory roots do not support add (use set on a slot)");
    }

    private void applyInventory(int slot, CompoundTag item) throws RootException {
        ServerPlayer p = player();
        CompoundTag before = fullSnapshot(p);
        CompoundTag edited = before.copy();
        edited.put("Inventory", InventorySlots.withSlot(inventoryOf(edited), slot, item));
        try {
            p.load(TagValueInput.create(ProblemReporter.DISCARDING, server.registryAccess(), edited));
        } catch (Exception e) {
            try {
                p.load(TagValueInput.create(ProblemReporter.DISCARDING, server.registryAccess(), before));
            } catch (Exception restoreFailure) {
                e.addSuppressed(restoreFailure);
                LOG.error("RESTORE FAILED for player {} — inventory may be inconsistent", playerName, e);
                throw new RootException("inventory edit failed AND restore failed for " + playerName + ": " + e, e);
            }
            LOG.warn("inventory edit rejected for {} (player restored)", playerName, e);
            throw new RootException("inventory edit rejected (player restored): " + e, e);
        }
    }

    private static int parseSlot(String path) throws RootException {
        if (!path.startsWith("slot.")) throw new RootException("expected slot.<n>, got: " + path);
        int slot;
        try {
            slot = Integer.parseInt(path.substring("slot.".length()));
        } catch (NumberFormatException e) {
            throw new RootException("bad slot number in path: " + path);
        }
        for (int s : InventorySlots.SLOTS) if (s == slot) return slot;
        throw new RootException("not a player inventory slot: " + slot);
    }
}
