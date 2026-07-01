package dev.nitro.livenbt.net;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class ClientPresenceTest {

    @Test void noneConnectedByDefault() {
        assertFalse(new ClientPresence().anyConnected());
    }

    @Test void tracksConnectAndDisconnect() {
        ClientPresence p = new ClientPresence();
        p.onConnect();
        assertTrue(p.anyConnected());
        p.onConnect();                 // a second editor
        p.onDisconnect();
        assertTrue(p.anyConnected());  // still one attached
        p.onDisconnect();
        assertFalse(p.anyConnected());
    }

    @Test void disconnectNeverGoesNegative() {
        ClientPresence p = new ClientPresence();
        p.onDisconnect();              // spurious close with no matching connect
        assertFalse(p.anyConnected());
        p.onConnect();
        assertTrue(p.anyConnected());  // a real connect still registers
    }

    @Test void resetClears() {
        ClientPresence p = new ClientPresence();
        p.onConnect();
        p.onConnect();
        p.reset();
        assertFalse(p.anyConnected());
    }
}
