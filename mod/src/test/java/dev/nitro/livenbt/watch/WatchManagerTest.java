package dev.nitro.livenbt.watch;

import dev.nitro.livenbt.roots.RootAdapter;
import dev.nitro.livenbt.roots.RootException;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.Tag;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class WatchManagerTest {

    /** Adapter over a mutable CompoundTag; set/add/delete unused here. */
    private static final class FakeRoot implements RootAdapter {
        CompoundTag data = new CompoundTag();
        boolean gone = false;
        @Override public CompoundTag snapshot() throws RootException {
            if (gone) throw new RootException("gone");
            return data.copy();
        }
        @Override public void set(String p, Tag v) {}
        @Override public void add(String p, Tag v) {}
        @Override public void delete(String p) {}
    }

    private static final class FakeSender implements WatchManager.Sender {
        final List<String> sent = new ArrayList<>();
        boolean open = true;
        @Override public void send(String json) { sent.add(json); }
        @Override public boolean isOpen() { return open; }
    }

    @Test
    void sendsInitialValueThenOnlyChanges() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "hp");

        wm.sample(id -> root);
        assertEquals(1, sender.sent.size());            // initial push
        assertTrue(sender.sent.get(0).contains("\"v\":20"));

        wm.sample(id -> root);
        assertEquals(1, sender.sent.size());            // unchanged -> no push

        root.data.putInt("hp", 15);
        wm.sample(id -> root);
        assertEquals(2, sender.sent.size());            // changed -> push
        assertTrue(sender.sent.get(1).contains("\"v\":15"));
    }

    @Test
    void missingPathOrRootPushesNullOnce() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "hp");
        wm.sample(id -> root);

        root.gone = true;
        wm.sample(id -> root);
        assertEquals(2, sender.sent.size());
        assertTrue(sender.sent.get(1).contains("\"value\":null"));
        wm.sample(id -> root);
        assertEquals(2, sender.sent.size());            // still gone -> no repeat
    }

    @Test
    void unwatchAndClosedSendersAreRemoved() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "hp");
        wm.remove(sender, "player:Bob", "hp");
        wm.sample(id -> root);
        assertEquals(0, sender.sent.size());

        wm.add(sender, "player:Bob", "hp");
        sender.open = false;
        wm.sample(id -> root);                          // pruned before evaluation
        root.data.putInt("hp", 1);
        wm.sample(id -> root);
        assertEquals(0, sender.sent.size());
    }

    @Test
    void duplicateAddIsIgnored() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "hp");
        wm.add(sender, "player:Bob", "hp");
        wm.sample(id -> root);
        assertEquals(1, sender.sent.size());            // one watch, one push
    }

    @Test
    void removeAllDropsEverythingForSender() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "hp");
        wm.add(sender, "player:Bob", "");
        wm.removeAll(sender);
        wm.sample(id -> root);
        assertEquals(0, sender.sent.size());
    }

    @Test
    void twoWatchesShareOneSnapshotAndFailingRootResolvesOnce() {
        final int[] resolves = {0};
        FakeRoot root = new FakeRoot();
        root.gone = true;
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "hp");
        wm.add(sender, "player:Bob", "xp");
        wm.sample(id -> { resolves[0]++; return root; });
        assertEquals(1, resolves[0]);                  // one resolve per root per sample
        assertEquals(2, sender.sent.size());           // both watches got a null push
    }

    @Test
    void missingPathOnLiveRootPushesNullOnce() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(sender, "player:Bob", "nope");
        wm.sample(id -> root);
        assertEquals(1, sender.sent.size());
        assertTrue(sender.sent.get(0).contains("\"value\":null"));
        wm.sample(id -> root);
        assertEquals(1, sender.sent.size());
    }

    @Test
    void addAndRemoveReportWhetherTheyChangedAnything() {
        FakeRoot root = new FakeRoot();
        FakeSender sender = new FakeSender();
        WatchManager wm = new WatchManager();
        assertTrue(wm.add(sender, "player:Bob", "hp"));
        assertFalse(wm.add(sender, "player:Bob", "hp"));   // duplicate
        assertTrue(wm.remove(sender, "player:Bob", "hp"));
        assertFalse(wm.remove(sender, "player:Bob", "hp")); // already gone
    }

    @Test
    void throwingSenderDoesNotKillTheSample() {
        FakeRoot root = new FakeRoot();
        root.data.putInt("hp", 20);
        WatchManager.Sender thrower = new WatchManager.Sender() {
            @Override public void send(String json) { throw new RuntimeException("socket died"); }
            @Override public boolean isOpen() { return true; }
        };
        FakeSender good = new FakeSender();
        WatchManager wm = new WatchManager();
        wm.add(thrower, "player:Bob", "hp");
        wm.add(good, "player:Bob", "hp");
        wm.sample(id -> root);                          // must not throw
        assertEquals(1, good.sent.size());              // good sender still served
    }
}
