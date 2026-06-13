package dev.nitro.livenbt.protocol;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.nbt.*;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class NbtJsonTest {

    private static CompoundTag sample() {
        CompoundTag t = new CompoundTag();
        t.putByte("b", (byte) -2);
        t.putShort("s", (short) 300);
        t.putInt("i", 123456);
        t.putLong("l", Long.MAX_VALUE);
        t.putFloat("f", 1.5f);
        t.putDouble("d", 0.1d);
        t.putString("str", "hi \"there\"");
        t.putByteArray("ba", new byte[]{1, 2, 3});
        t.putIntArray("ia", new int[]{4, 5});
        t.putLongArray("la", new long[]{Long.MIN_VALUE, 7L});
        ListTag list = new ListTag();
        list.add(new DoubleTag(10.5));
        list.add(new DoubleTag(64.0));
        t.put("pos", list);
        CompoundTag inner = new CompoundTag();
        inner.putBoolean("flag", true);
        t.put("nested", inner);
        return t;
    }

    @Test
    void roundTripPreservesEverything() {
        CompoundTag original = sample();
        Tag back = NbtJson.fromJson(NbtJson.toJson(original));
        assertEquals(original, back);
    }

    @Test
    void longAndDoubleEncodedAsStrings() {
        JsonObject j = NbtJson.toJson(sample());
        JsonObject v = j.getAsJsonObject("v");
        assertEquals("long", v.getAsJsonObject("l").get("t").getAsString());
        assertEquals("9223372036854775807", v.getAsJsonObject("l").get("v").getAsString());
        assertTrue(v.getAsJsonObject("l").get("v").isJsonPrimitive());
        assertTrue(v.getAsJsonObject("l").getAsJsonPrimitive("v").isString());
        assertTrue(v.getAsJsonObject("d").getAsJsonPrimitive("v").isString());
    }

    @Test
    void longArrayElementsAreStrings() {
        JsonObject j = NbtJson.toJson(sample());
        JsonObject la = j.getAsJsonObject("v").getAsJsonObject("la");
        assertEquals("-9223372036854775808", la.getAsJsonArray("v").get(0).getAsString());
        assertTrue(la.getAsJsonArray("v").get(0).getAsJsonPrimitive().isString());
    }

    @Test
    void booleanIsByte() {
        JsonObject j = NbtJson.toJson(sample());
        JsonObject flag = j.getAsJsonObject("v").getAsJsonObject("nested")
                .getAsJsonObject("v").getAsJsonObject("flag");
        assertEquals("byte", flag.get("t").getAsString());
        assertEquals(1, flag.get("v").getAsInt());
    }

    @Test
    void fromJsonAcceptsNumericLongToo() {
        JsonObject j = JsonParser.parseString("{\"t\":\"long\",\"v\":42}").getAsJsonObject();
        assertEquals(new LongTag(42L), NbtJson.fromJson(j));
    }

    @Test
    void fromJsonRejectsUnknownType() {
        JsonObject j = JsonParser.parseString("{\"t\":\"nope\",\"v\":1}").getAsJsonObject();
        IllegalArgumentException e = assertThrows(IllegalArgumentException.class, () -> NbtJson.fromJson(j));
        assertTrue(e.getMessage().contains("nope"));
    }

    @Test
    void fromJsonRejectsMissingFields() {
        JsonObject j = JsonParser.parseString("{\"t\":\"int\"}").getAsJsonObject();
        assertThrows(IllegalArgumentException.class, () -> NbtJson.fromJson(j));
    }

    @Test
    void fromJsonRejectsOutOfRangeNumbers() {
        assertThrows(IllegalArgumentException.class,
                () -> NbtJson.fromJson(JsonParser.parseString("{\"t\":\"byte\",\"v\":300}").getAsJsonObject()));
        assertThrows(IllegalArgumentException.class,
                () -> NbtJson.fromJson(JsonParser.parseString("{\"t\":\"short\",\"v\":70000}").getAsJsonObject()));
        assertThrows(IllegalArgumentException.class,
                () -> NbtJson.fromJson(JsonParser.parseString("{\"t\":\"int\",\"v\":3000000000}").getAsJsonObject()));
        assertThrows(IllegalArgumentException.class,
                () -> NbtJson.fromJson(JsonParser.parseString("{\"t\":\"byte_array\",\"v\":[1,300]}").getAsJsonObject()));
    }

    @Test
    void nonFiniteFloatsAreSubstituted() {
        CompoundTag t = new CompoundTag();
        t.putFloat("bad", Float.NaN);
        JsonObject j = NbtJson.toJson(t);
        assertEquals(0.0f, j.getAsJsonObject("v").getAsJsonObject("bad").get("v").getAsFloat());
        // and the emitted JSON is strictly valid
        assertFalse(j.toString().contains("NaN"));
    }

    @Test
    void fromJsonRejectsMalformedTypeField() {
        assertThrows(IllegalArgumentException.class,
                () -> NbtJson.fromJson(JsonParser.parseString("{\"t\":null,\"v\":1}").getAsJsonObject()));
        assertThrows(IllegalArgumentException.class,
                () -> NbtJson.fromJson(JsonParser.parseString("{\"t\":{},\"v\":1}").getAsJsonObject()));
    }
}
