package dev.nitro.livenbt;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.*;

class LiveNbtConfigTest {

    @Test
    void createsDefaultConfigWithRandomToken(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("livenbt.json");
        LiveNbtConfig cfg = LiveNbtConfig.loadOrCreate(file);
        assertEquals("127.0.0.1", cfg.bind());
        assertEquals(25599, cfg.port());
        assertEquals(32, cfg.token().length());
        assertTrue(Files.exists(file));
        // second create gets a different token; loading the same file does not
        assertEquals(cfg.token(), LiveNbtConfig.loadOrCreate(file).token());
        assertNotEquals(cfg.token(), LiveNbtConfig.loadOrCreate(dir.resolve("other.json")).token());
    }

    @Test
    void loadsExistingConfig(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("livenbt.json");
        Files.writeString(file, "{\"bind\":\"0.0.0.0\",\"port\":1234,\"token\":\"abc\"}");
        LiveNbtConfig cfg = LiveNbtConfig.loadOrCreate(file);
        assertEquals("0.0.0.0", cfg.bind());
        assertEquals(1234, cfg.port());
        assertEquals("abc", cfg.token());
    }

    @Test
    void rejectsMalformedConfigWithHelpfulMessage(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("livenbt.json");
        Files.writeString(file, "{not json!");
        IllegalArgumentException e = assertThrows(IllegalArgumentException.class, () -> LiveNbtConfig.loadOrCreate(file));
        assertTrue(e.getMessage().contains("livenbt.json"));
        assertTrue(e.getMessage().contains("regenerate"));
    }

    @Test
    void rejectsConfigWithMissingFields(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("livenbt.json");
        Files.writeString(file, "{\"port\":25599}");
        assertThrows(IllegalArgumentException.class, () -> LiveNbtConfig.loadOrCreate(file));
    }

    @Test
    void generatedTokenIsLowercaseHex(@TempDir Path dir) {
        LiveNbtConfig cfg = LiveNbtConfig.loadOrCreate(dir.resolve("livenbt.json"));
        assertTrue(cfg.token().matches("[0-9a-f]{32}"));
    }
}
