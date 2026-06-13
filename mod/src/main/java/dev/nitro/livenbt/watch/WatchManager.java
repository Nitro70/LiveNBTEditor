package dev.nitro.livenbt.watch;

import com.google.gson.JsonElement;
import com.google.gson.JsonNull;
import dev.nitro.livenbt.path.NbtPath;
import dev.nitro.livenbt.protocol.NbtJson;
import dev.nitro.livenbt.protocol.Replies;
import dev.nitro.livenbt.roots.RootAdapter;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.Tag;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/** Watch subscriptions. All methods run on the server thread (via the op queue / tick handler). */
public final class WatchManager {
    private static final Logger LOG = LoggerFactory.getLogger("livenbt");

    public interface Sender {
        void send(String json);
        boolean isOpen();
    }

    @FunctionalInterface
    public interface Resolver {
        RootAdapter resolve(String rootId) throws Exception;
    }

    private static final class Watch {
        final Sender sender; final String root; final String path;
        JsonElement last; // null = never sent
        Watch(Sender sender, String root, String path) {
            this.sender = sender; this.root = root; this.path = path;
        }
    }

    private final List<Watch> watches = new ArrayList<>();

    /** @return true if a new watch was registered (false = duplicate). */
    public boolean add(Sender sender, String root, String path) {
        if (find(sender, root, path) != null) return false;
        watches.add(new Watch(sender, root, path));
        return true;
    }

    /** @return true if a watch was removed. */
    public boolean remove(Sender sender, String root, String path) {
        return watches.remove(find(sender, root, path));
    }

    public void removeAll(Sender sender) {
        watches.removeIf(w -> w.sender == sender);
    }

    private Watch find(Sender sender, String root, String path) {
        for (Watch w : watches) {
            if (w.sender == sender && w.root.equals(root) && w.path.equals(path)) return w;
        }
        return null;
    }

    /** Evaluate all watches; push only values that changed since the last push. */
    public void sample(Resolver resolver) {
        watches.removeIf(w -> !w.sender.isOpen());
        Map<String, Optional<CompoundTag>> snapshots = new HashMap<>();
        for (Watch w : watches) {
            try {
                CompoundTag snap = snapshots.computeIfAbsent(w.root, id -> {
                    try {
                        return Optional.of(resolver.resolve(id).snapshot());
                    } catch (Exception e) {
                        LOG.debug("watch sample: root {} unavailable: {}", id, e.toString());
                        return Optional.empty();
                    }
                }).orElse(null);
                JsonElement now = JsonNull.INSTANCE;
                if (snap != null) {
                    try {
                        Tag t = NbtPath.get(snap, w.path);
                        if (t != null) now = NbtJson.toJson(t);
                    } catch (NbtPath.PathException ignored) {
                        // bad path counts as "gone"
                    }
                }
                if (!Objects.equals(now, w.last)) {
                    w.last = now;
                    // a throwing sender is dying and will be pruned next sample
                    w.sender.send(Replies.update(w.root, w.path, now.isJsonNull() ? null : now));
                }
            } catch (Exception e) {
                LOG.warn("watch push failed for {} {}: {}", w.root, w.path, e.toString());
            }
        }
    }
}
