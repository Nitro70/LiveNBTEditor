package dev.nitro.livenbt.path;

import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.Tag;

import java.util.ArrayList;
import java.util.List;

/**
 * Minimal NBT path engine: dotted names + [index]. v1 limitation: names cannot
 * contain '.' or '[' (no quoting support).
 */
public final class NbtPath {
    private NbtPath() {}

    public static final class PathException extends Exception {
        public PathException(String message) { super(message); }
    }

    private static String brief(String path) {
        return path != null && path.length() > 200 ? path.substring(0, 200) + "…" : String.valueOf(path);
    }

    /** Steps are String (compound key) or Integer (list index). */
    static List<Object> parse(String path) throws PathException {
        if (path == null) throw new PathException("path is null");
        List<Object> steps = new ArrayList<>();
        int i = 0, n = path.length();
        boolean expectName = true;
        while (i < n) {
            char c = path.charAt(i);
            if (c == '[') {
                if (expectName && !steps.isEmpty())
                    throw new PathException("unexpected '[' after '.' in path: " + brief(path));
                int close = path.indexOf(']', i);
                if (close < 0) throw new PathException("unclosed '[' in path: " + brief(path));
                String num = path.substring(i + 1, close);
                int idx;
                try { idx = Integer.parseInt(num); } catch (NumberFormatException e) {
                    throw new PathException("bad list index '" + num + "' in path: " + brief(path));
                }
                if (idx < 0) throw new PathException("negative index in path: " + brief(path));
                steps.add(idx);
                i = close + 1;
                expectName = false;
            } else if (c == '.') {
                if (expectName) throw new PathException("empty name in path: " + brief(path));
                i++;
                expectName = true;
            } else {
                if (!expectName && !steps.isEmpty() && steps.getLast() instanceof Integer)
                    throw new PathException("missing '.' before name in path: " + brief(path));
                int end = i;
                while (end < n && path.charAt(end) != '.' && path.charAt(end) != '[') end++;
                steps.add(path.substring(i, end));
                i = end;
                expectName = false;
            }
        }
        if (expectName && !steps.isEmpty()) throw new PathException("path ends with '.': " + brief(path));
        return steps;
    }

    /** Returns the tag at path, or null if any step is missing. Root itself for "". */
    public static Tag get(CompoundTag root, String path) throws PathException {
        Tag current = root;
        for (Object step : parse(path)) {
            current = step(current, step, path);
            if (current == null) return null;
        }
        return current;
    }

    public static void set(CompoundTag root, String path, Tag value) throws PathException {
        if (value == null) throw new PathException("value is null");
        Parent p = resolveParent(root, path);
        if (p.container instanceof CompoundTag c && p.last instanceof String name) {
            c.put(name, value);
        } else if (p.container instanceof ListTag list && p.last instanceof Integer idx) {
            if (idx < list.size()) list.set(idx, value);
            else if (idx == list.size()) list.add(value);
            else throw new PathException("index " + idx + " out of range (size " + list.size() + ") in path: " + brief(path));
        } else {
            throw new PathException("cannot set " + describe(p.last) + " on " + typeName(p.container) + " in path: " + brief(path));
        }
    }

    public static void add(CompoundTag root, String path, Tag value) throws PathException {
        if (value == null) throw new PathException("value is null");
        Tag target = get(root, path);
        if (target instanceof ListTag list) {
            list.add(value);
            return;
        }
        if (target != null) throw new PathException("path exists and is not a list (use set): " + brief(path));
        Parent p = resolveParent(root, path);
        if (p.container instanceof CompoundTag c && p.last instanceof String name) {
            c.put(name, value);
        } else {
            throw new PathException("cannot add at path: " + brief(path));
        }
    }

    public static void delete(CompoundTag root, String path) throws PathException {
        Parent p = resolveParent(root, path);
        if (p.container instanceof CompoundTag c && p.last instanceof String name) {
            if (!c.contains(name)) throw new PathException("no such key: " + brief(path));
            c.remove(name);
        } else if (p.container instanceof ListTag list && p.last instanceof Integer idx) {
            if (idx >= list.size()) throw new PathException("index " + idx + " out of range (size " + list.size() + ") in path: " + brief(path));
            list.remove((int) idx);
        } else {
            throw new PathException("cannot delete at path: " + brief(path));
        }
    }

    private record Parent(Tag container, Object last) {}

    private static Parent resolveParent(CompoundTag root, String path) throws PathException {
        List<Object> steps = parse(path);
        if (steps.isEmpty()) throw new PathException("cannot modify the root itself");
        Tag current = root;
        for (int i = 0; i < steps.size() - 1; i++) {
            current = step(current, steps.get(i), path);
            if (current == null) throw new PathException("missing parent at step '" + describe(steps.get(i)) + "' in path: " + brief(path));
        }
        return new Parent(current, steps.getLast());
    }

    /** One navigation step; null if a key/index is absent; PathException on container-type mismatch. */
    private static Tag step(Tag container, Object step, String path) throws PathException {
        if (step instanceof String name) {
            if (!(container instanceof CompoundTag c))
                throw new PathException("expected compound at '" + name + "' but found " + typeName(container) + " in path: " + brief(path));
            return c.get(name);
        }
        int idx = (Integer) step;
        if (!(container instanceof ListTag list))
            throw new PathException("expected list at [" + idx + "] but found " + typeName(container) + " in path: " + brief(path));
        return idx < list.size() ? list.get(idx) : null;
    }

    private static String describe(Object step) {
        return step instanceof Integer i ? "[" + i + "]" : "'" + step + "'";
    }

    private static String typeName(Tag tag) {
        return tag == null ? "nothing" : tag.getType().getName();
    }
}
