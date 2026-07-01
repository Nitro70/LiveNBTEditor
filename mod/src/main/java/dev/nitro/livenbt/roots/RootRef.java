package dev.nitro.livenbt.roots;

public record RootRef(Kind kind, String name) {
    public enum Kind { PLAYER, WORLD, INVENTORY }

    public static RootRef parse(String rootId) throws RootException {
        if (rootId == null) throw new RootException("root id is null");
        if (rootId.startsWith("player:") && rootId.length() > 7)
            return new RootRef(Kind.PLAYER, rootId.substring(7));
        if (rootId.startsWith("world:") && rootId.length() > 6)
            return new RootRef(Kind.WORLD, rootId.substring(6));
        if (rootId.startsWith("inventory:") && rootId.length() > 10)
            return new RootRef(Kind.INVENTORY, rootId.substring(10));
        throw new RootException("unknown root (expected player:<name>, world:<dimension> or inventory:<name>): " + rootId);
    }
}
