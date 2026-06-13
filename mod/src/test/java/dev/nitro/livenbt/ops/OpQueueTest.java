package dev.nitro.livenbt.ops;

import org.junit.jupiter.api.Test;
import java.util.ArrayList;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class OpQueueTest {
    @Test void drainsInOrderAndSurvivesThrowingOps() {
        OpQueue q = new OpQueue();
        List<Integer> ran = new ArrayList<>();
        q.submit(() -> ran.add(1));
        q.submit(() -> { throw new RuntimeException("boom"); });
        q.submit(() -> ran.add(3));
        q.drainAll();
        assertEquals(List.of(1, 3), ran);
        q.drainAll(); // empty drain is a no-op
        assertEquals(List.of(1, 3), ran);
    }

    @Test
    void drainIsBoundedPerCall() {
        OpQueue q = new OpQueue();
        int[] ran = {0};
        for (int i = 0; i < 1500; i++) q.submit(() -> ran[0]++);
        q.drainAll();
        assertEquals(1024, ran[0]);
        q.drainAll();
        assertEquals(1500, ran[0]);
    }
}
