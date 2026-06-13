package dev.nitro.livenbt;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonParseException;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.SecureRandom;
import java.util.HexFormat;

public record LiveNbtConfig(String bind, int port, String token) {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

    public LiveNbtConfig {
        if (bind == null || bind.isBlank()) throw new IllegalArgumentException("'bind' must be a non-empty address");
        if (port < 1 || port > 65535) throw new IllegalArgumentException("'port' must be 1-65535, got " + port);
        if (token == null || token.isBlank()) throw new IllegalArgumentException("'token' must be non-empty");
    }

    public static LiveNbtConfig loadOrCreate(Path file) {
        try {
            if (Files.exists(file)) {
                try {
                    LiveNbtConfig cfg = GSON.fromJson(Files.readString(file), LiveNbtConfig.class);
                    if (cfg == null) throw new IllegalArgumentException("file is empty");
                    return cfg;
                } catch (RuntimeException e) {
                    throw new IllegalArgumentException(
                            "bad config " + file + " (" + e.getMessage() + ") - delete the file to regenerate defaults", e);
                }
            }
            byte[] raw = new byte[16];
            new SecureRandom().nextBytes(raw);
            LiveNbtConfig cfg = new LiveNbtConfig("127.0.0.1", 25599, HexFormat.of().formatHex(raw));
            if (file.getParent() != null) Files.createDirectories(file.getParent());
            Files.writeString(file, GSON.toJson(cfg));
            return cfg;
        } catch (IOException e) {
            throw new UncheckedIOException("failed to load/create " + file, e);
        }
    }
}
