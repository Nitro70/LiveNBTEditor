package dev.nitro.livenbt.protocol;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class MessagesTest {

    @Test
    void parsesSetRequest() {
        Request r = Request.parse("{\"id\":4,\"op\":\"set\",\"root\":\"player:Bob\",\"path\":\"Health\",\"value\":{\"t\":\"float\",\"v\":10.0}}");
        assertEquals(4, r.id());
        assertEquals("set", r.op());
        assertEquals("player:Bob", r.root());
        assertEquals("Health", r.path());
        assertEquals("float", r.value().getAsJsonObject().get("t").getAsString());
        assertNull(r.token());
    }

    @Test
    void parsesAuthRequest() {
        Request r = Request.parse("{\"id\":0,\"op\":\"auth\",\"token\":\"abc\"}");
        assertEquals("auth", r.op());
        assertEquals("abc", r.token());
        assertEquals("", r.path()); // missing path defaults to ""
    }

    @Test
    void rejectsGarbage() {
        assertThrows(IllegalArgumentException.class, () -> Request.parse("not json"));
        assertThrows(IllegalArgumentException.class, () -> Request.parse("{\"id\":1}")); // no op
        assertThrows(IllegalArgumentException.class, () -> Request.parse("[1,2]"));
    }

    @Test
    void rejectsNullFieldValues() {
        assertThrows(IllegalArgumentException.class,
                () -> Request.parse("{\"id\":1,\"op\":null}"));
        assertThrows(IllegalArgumentException.class,
                () -> Request.parse("{\"id\":1,\"op\":\"get\",\"path\":{\"a\":1}}"));
    }

    @Test
    void buildsReplies() {
        assertEquals("{\"id\":7,\"ok\":true}", Replies.ok(7));
        JsonObject err = JsonParser.parseString(Replies.error(7, "boom")).getAsJsonObject();
        assertFalse(err.get("ok").getAsBoolean());
        assertEquals("boom", err.get("error").getAsString());
        JsonObject hello = JsonParser.parseString(Replies.hello()).getAsJsonObject();
        assertEquals("hello", hello.get("op").getAsString());
        assertEquals(1, hello.get("protocol").getAsInt());
        JsonObject upd = JsonParser.parseString(Replies.update("player:Bob", "Pos", null)).getAsJsonObject();
        assertEquals("update", upd.get("op").getAsString());
        assertTrue(upd.get("value").isJsonNull());
    }

    @Test
    void idEdgeCasesDefaultToMinusOne() {
        assertEquals(-1, Request.parse("{\"op\":\"roots\"}").id());
        assertEquals(-1, Request.parse("{\"id\":\"abc\",\"op\":\"roots\"}").id());
        assertEquals(-1, Request.parse("{\"id\":true,\"op\":\"roots\"}").id());
        assertEquals(-1, Request.parse("{\"id\":{},\"op\":\"roots\"}").id());
    }

    @Test
    void oversizedAndNullRequestsRejected() {
        assertThrows(IllegalArgumentException.class, () -> Request.parse(null));
        String huge = "{\"op\":\"" + "x".repeat(1 << 20) + "\"}";
        IllegalArgumentException e = assertThrows(IllegalArgumentException.class, () -> Request.parse(huge));
        assertEquals("request too large", e.getMessage());
    }

    @Test
    void nullErrorMessageCoalesced() {
        assertTrue(Replies.error(1, null).contains("internal error"));
        assertThrows(NullPointerException.class, () -> Replies.ok(1, null));
    }
}
