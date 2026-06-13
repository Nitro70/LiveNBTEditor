package dev.nitro.livenbt.roots;

import dev.nitro.livenbt.path.NbtPath;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.Tag;

/** A live-editable NBT root. All methods MUST be called on the server thread. */
public interface RootAdapter {
    /** Current state as a fresh CompoundTag (never a live reference). */
    CompoundTag snapshot() throws RootException;

    void set(String path, Tag value) throws RootException, NbtPath.PathException;

    void add(String path, Tag value) throws RootException, NbtPath.PathException;

    void delete(String path) throws RootException, NbtPath.PathException;
}
