package dev.nitro.livenbt.protocol;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import net.minecraft.nbt.*;

/** Typed JSON <-> NBT codec. Longs, doubles and long-array elements travel as strings. */
public final class NbtJson {
    private NbtJson() {}

    public static JsonObject toJson(Tag tag) {
        JsonObject o = new JsonObject();
        switch (tag.getId()) {
            case Tag.TAG_BYTE ->   { o.addProperty("t", "byte");   o.addProperty("v", tag.asByte().orElseThrow()); }
            case Tag.TAG_SHORT ->  { o.addProperty("t", "short");  o.addProperty("v", tag.asShort().orElseThrow()); }
            case Tag.TAG_INT ->    { o.addProperty("t", "int");    o.addProperty("v", tag.asInt().orElseThrow()); }
            case Tag.TAG_LONG ->   { o.addProperty("t", "long");   o.addProperty("v", Long.toString(tag.asLong().orElseThrow())); }
            case Tag.TAG_FLOAT -> {
                float f = tag.asFloat().orElseThrow();
                o.addProperty("t", "float");
                o.addProperty("v", Float.isFinite(f) ? f : 0.0f);   // protocol forbids non-finite; substitute
            }
            case Tag.TAG_DOUBLE -> { o.addProperty("t", "double"); o.addProperty("v", Double.toString(tag.asDouble().orElseThrow())); }
            case Tag.TAG_STRING -> { o.addProperty("t", "string"); o.addProperty("v", tag.asString().orElseThrow()); }
            case Tag.TAG_BYTE_ARRAY -> {
                JsonArray a = new JsonArray();
                for (byte b : tag.asByteArray().orElseThrow()) a.add(b);
                o.addProperty("t", "byte_array"); o.add("v", a);
            }
            case Tag.TAG_INT_ARRAY -> {
                JsonArray a = new JsonArray();
                for (int i : tag.asIntArray().orElseThrow()) a.add(i);
                o.addProperty("t", "int_array"); o.add("v", a);
            }
            case Tag.TAG_LONG_ARRAY -> {
                JsonArray a = new JsonArray();
                for (long l : tag.asLongArray().orElseThrow()) a.add(Long.toString(l));
                o.addProperty("t", "long_array"); o.add("v", a);
            }
            case Tag.TAG_LIST -> {
                JsonArray a = new JsonArray();
                for (Tag el : tag.asList().orElseThrow()) a.add(toJson(el));
                o.addProperty("t", "list"); o.add("v", a);
            }
            case Tag.TAG_COMPOUND -> {
                CompoundTag c = tag.asCompound().orElseThrow();
                JsonObject m = new JsonObject();
                for (String k : c.keySet()) m.add(k, toJson(c.get(k)));
                o.addProperty("t", "compound"); o.add("v", m);
            }
            default -> throw new IllegalArgumentException("unsupported NBT tag id: " + tag.getId());
        }
        return o;
    }

    public static Tag fromJson(JsonObject o) {
        JsonElement t = o.get("t");
        JsonElement v = o.get("v");
        if (t == null || !t.isJsonPrimitive() || !t.getAsJsonPrimitive().isString() || v == null)
            throw new IllegalArgumentException("node needs string 't' and 'v' fields: " + brief(o));
        String type = t.getAsString();
        try {
            return switch (type) {
                case "byte"  -> ByteTag.valueOf((byte)  asRangedLong(v, Byte.MIN_VALUE,    Byte.MAX_VALUE,    "byte"));
                case "short" -> ShortTag.valueOf((short) asRangedLong(v, Short.MIN_VALUE,   Short.MAX_VALUE,   "short"));
                case "int"   -> IntTag.valueOf((int)    asRangedLong(v, Integer.MIN_VALUE,  Integer.MAX_VALUE, "int"));
                case "long"  -> LongTag.valueOf(         asRangedLong(v, Long.MIN_VALUE,    Long.MAX_VALUE,    "long"));
                case "float" -> FloatTag.valueOf(v.getAsFloat());
                case "double" -> DoubleTag.valueOf(v.getAsDouble());
                case "string" -> StringTag.valueOf(v.getAsString());
                case "byte_array" -> {
                    JsonArray a = v.getAsJsonArray();
                    byte[] out = new byte[a.size()];
                    for (int i = 0; i < a.size(); i++) out[i] = (byte) asRangedLong(a.get(i), Byte.MIN_VALUE, Byte.MAX_VALUE, "byte");
                    yield new ByteArrayTag(out);
                }
                case "int_array" -> {
                    JsonArray a = v.getAsJsonArray();
                    int[] out = new int[a.size()];
                    for (int i = 0; i < a.size(); i++) out[i] = (int) asRangedLong(a.get(i), Integer.MIN_VALUE, Integer.MAX_VALUE, "int");
                    yield new IntArrayTag(out);
                }
                case "long_array" -> {
                    JsonArray a = v.getAsJsonArray();
                    long[] out = new long[a.size()];
                    for (int i = 0; i < a.size(); i++) out[i] = asRangedLong(a.get(i), Long.MIN_VALUE, Long.MAX_VALUE, "long");
                    yield new LongArrayTag(out);
                }
                case "list" -> {
                    ListTag list = new ListTag();
                    for (JsonElement el : v.getAsJsonArray()) list.add(fromJson(el.getAsJsonObject()));
                    yield list;
                }
                case "compound" -> {
                    CompoundTag c = new CompoundTag();
                    for (var e : v.getAsJsonObject().entrySet()) c.put(e.getKey(), fromJson(e.getValue().getAsJsonObject()));
                    yield c;
                }
                default -> throw new IllegalArgumentException("unknown NBT node type: " + type);
            };
        } catch (IllegalArgumentException e) {
            throw e;
        } catch (RuntimeException e) {
            throw new IllegalArgumentException("bad value for type '" + type + "': " + brief(v), e);
        }
    }

    private static long asRangedLong(JsonElement v, long min, long max, String type) {
        long n = v.getAsJsonPrimitive().isString() ? Long.parseLong(v.getAsString()) : v.getAsLong();
        if (n < min || n > max) throw new IllegalArgumentException("value " + n + " out of range for " + type);
        return n;
    }

    private static String brief(JsonElement el) {
        String s = String.valueOf(el);
        return s.length() > 200 ? s.substring(0, 200) + "…" : s;
    }
}
