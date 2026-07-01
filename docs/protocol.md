# LiveNBT WebSocket Protocol v1

Transport: WebSocket, text frames, one JSON object per frame. Default port 25599.
URL: `ws://<host>:<port>/`

## Typed NBT-node encoding

Every NBT value crosses the wire as `{"t": <type>, "v": <value>}`:

| t | v |
|---|---|
| `byte` `short` `int` | JSON number |
| `long` | **JSON string** (precision) e.g. `"9223372036854775807"` |
| `float` | JSON number |
| `double` | **JSON string** (precision) e.g. `"1.5"` |
| `string` | JSON string |
| `list` | JSON array of nodes |
| `compound` | JSON object: key → node |
| `byte_array` `int_array` | JSON array of numbers |
| `long_array` | JSON array of **strings** |

There is no boolean type — NBT booleans are `byte` 0/1.

Non-finite `float` values (NaN/Infinity) are not representable on the wire — both sides reject them.

## Roots and paths

- Roots: `player:<name>` (online player) and `world:<dimension-id>` (e.g. `world:minecraft:overworld`).
- Paths: dotted names with `[index]` for lists, e.g. `abilities.mayfly`, `Inventory[3].count`.
  Empty path `""` = the whole root.

## Client → server requests

All requests carry a client-chosen numeric `id`; the reply echoes it.

```json
{"id": 1, "op": "auth",    "token": "<hex>"}
{"id": 2, "op": "roots"}
{"id": 3, "op": "get",     "root": "player:Nitro70", "path": ""}
{"id": 4, "op": "set",     "root": "player:Nitro70", "path": "abilities.mayfly", "value": {"t":"byte","v":1}}
{"id": 5, "op": "add",     "root": "player:Nitro70", "path": "Tags", "value": {"t":"string","v":"vip"}}
{"id": 6, "op": "delete",  "root": "player:Nitro70", "path": "Tags[0]"}
{"id": 7, "op": "watch",   "root": "player:Nitro70", "path": "Pos"}
{"id": 8, "op": "unwatch", "root": "player:Nitro70", "path": "Pos"}
{"id": 9, "op": "registry"}
```

Semantics:
- `auth` must be the first successful op; everything else replies `not authenticated` until then.
- `get` → reply `value` is the node at path (error if the path doesn't exist).
- `set` → create-or-replace at path. Parent must exist. List index must be `0..size` (`size` appends).
- `add` → append to the list AT `path`, or create a new compound key (error if key exists — use `set`).
- `delete` → remove compound key or list element (error if missing).
- `watch` → server pushes an initial `update` at the next sample (within 4 ticks, ~200 ms) and then whenever the value changes. Watching a compound watches its whole subtree.
- `registry` → reply `value` is `{"items": [...ids], "enchantments": [...ids]}` — live registry ids for the inventory editor's dropdowns.

## Server → client

Reply (one per request):
```json
{"id": 3, "ok": true, "value": {"t":"compound","v":{...}}}
{"id": 4, "ok": true}
{"id": 4, "ok": false, "error": "no such path: abilities.mayflyy"}
```

Hello (on connect, before auth):
```json
{"op": "hello", "protocol": 1, "authRequired": true}
```

`roots` reply value:
```json
{"id": 2, "ok": true, "value": {"players": ["Nitro70"], "worlds": ["minecraft:overworld", "minecraft:the_nether", "minecraft:the_end"]}}
```

Watch push (no id). `value: null` means the path or root is gone (player logged off, key deleted):
```json
{"op": "update", "root": "player:Nitro70", "path": "Pos", "value": {"t":"list","v":[...]}}
{"op": "update", "root": "player:Nitro70", "path": "Pos", "value": null}
```

## World root tree (26.1.2)

Virtual tree — every leaf maps to a live getter/setter. `add`/`delete` are not
supported on world roots. 26.1 note: vanilla replaced `dayTime` with registry
"world clocks", and weather lives in a `WeatherData` store.

Scope note: gamerules, weather, spawn, gameTime, and difficulty are
**server-global** in vanilla — editing them through any `world:` root affects
every dimension. Only `worldborder.*` is truly per-dimension.

Weather gotcha: while `clearWeatherTime > 0` the game forces `raining`/
`thundering` back to false each tick — set `clearWeatherTime` to 0 first.

```
gamerules.<rule-id>           byte (boolean rules) or int — all registered rules, auto-discovered
time.gameTime                 long (rw)
time.clocks.<clock-id>.totalTicks   long (rw)
weather.clearWeatherTime      int
weather.raining               byte
weather.rainTime              int
weather.thundering            byte
weather.thunderTime           int
spawn.x  spawn.y  spawn.z     int
spawn.yaw  spawn.pitch        float
worldborder.centerX .centerZ  double
worldborder.size              double
worldborder.damagePerBlock    double
worldborder.warningBlocks     int
worldborder.warningTime       int
worldborder.safeZone          double
difficulty                    string (peaceful|easy|normal|hard)
difficultyLocked              byte
```

## Player root fast paths

`set` on these paths uses dedicated game APIs instead of snapshot→reload:
`Pos` (list of 3 doubles → teleport), `Pos[0..2]`, `Rotation` (list of 2 floats),
`Health`, `foodLevel`, `XpLevel`, `abilities.invulnerable|flying|mayfly|instabuild|mayBuild|flySpeed|walkSpeed`.
Everything else round-trips through entity NBT reload.

Note: generic (non-fast-path) player edits round-trip through the entity's NBT
load. Keys the entity loader does not consume (including unknown `abilities.*`
keys) are silently discarded by the game — the op replies `ok` but the value
will not appear in subsequent reads.

## Inventory root

`inventory:<player>` exposes the player's 41 slots as a virtual compound keyed by
NBT slot number: `0`–`8` hotbar, `9`–`35` main, `100`–`103` armor, `-106` offhand.
Every slot is present; an empty slot is an empty compound `{}`.

- `get inventory:<player> ""` → the 41-slot compound. Each occupied slot is an item
  compound `{id, count, components}` (the `Slot` key is stripped from the view).
- `set inventory:<player> slot.<n>` with `{id, count, components?}` replaces slot `<n>`.
  `delete inventory:<player> slot.<n>` (or `set` with `{}`) empties it.
- `add` is unsupported. Edits reload the entity, so an invalid id/components is rejected
  with a readable error and the player is restored unchanged.

## Auth & security

Config `config/livenbt.json` (created on first run): `{"bind": "127.0.0.1", "port": 25599, "token": "<32 hex chars>"}`.
Token compare is constant-time. Default bind is loopback; set `0.0.0.0` for LAN use.
