# LiveNBT

**Edit Minecraft NBT in the *running* game — no file editing, no world reload, no mod loader.**

LiveNBT is a two-part tool for **vanilla Minecraft Java 26.2**:

- a **Java agent** (`-javaagent`, ByteBuddy) injected into Minecraft that runs a small WebSocket server *inside* the integrated (or dedicated) server, and
- a **Windows desktop editor** (WPF) that shows the live game's player, world, and inventory NBT as a tree you can edit, with changes applying **instantly** in-game.

Traditional NBT editors work on `.dat` files on disk, so you have to quit the world, edit, and reload. LiveNBT talks to the running server instead: when you change a value, the agent applies it to the live game object on the next server tick. Set `Health` and your hearts drop immediately; flip `weather.raining` and it starts raining; watch your `Pos` stream as you walk.

It works on **26.2 specifically** because the 26.x client and server ship **unobfuscated** (official Mojang names). That means the agent can hook the game by its real class and method names — **no mappings, no Fabric/Forge, no mod loader of any kind.** And with one click the app can load the agent into a **stock, already-running Minecraft** — no launch arguments at all (see below).

> Works in singleplayer (the integrated server) and on dedicated servers you control.

---

## Features

- **Live NBT tree editor** — edit player data (health, food, XP, abilities, position, …) and world data (gamerules, time, weather, spawn, world border, difficulty) as a tree and see it apply at once. Every edit is validated against the real NBT type before it's sent; bad input is rejected with a readable message and never written to your save.
- **Watches** — pin any value (or a whole compound subtree) and watch it update live as the game changes, several times a second.
- **Inventory editor** — edit any of a player's 41 slots (hotbar, main, armor, offhand) with **searchable item and enchantment pickers** backed by the **full bundled 26.2 registry**, so every real item and enchantment id is one search away. Invalid ids/components are rejected and the player is restored unchanged.
- **Edits apply live in-game** — the agent keeps the **integrated server ticking** while the app is connected, so edits take effect immediately even when Minecraft is in the background (no more "tab out and everything freezes"). Path-based edits touch only the field you changed — no whole-file rewrites, no lost updates.
- **Copy as SNBT** — grab any tag in `/data`-style SNBT for use in commands.
- **Safe by default** — the agent binds `127.0.0.1` (loopback) only and requires a per-install auth token before any operation.

---

## How it works

```
┌─────────────────────────┐   WebSocket (JSON, 127.0.0.1:25599)   ┌──────────────────────────┐
│   LiveNBT.App (WPF)      │ ◄───────────────────────────────────► │   livenbt-agent (-javaagent) │
│   tree view, editing,    │   hello → auth → get / set /          │   ByteBuddy hooks +        │
│   inventory + watches    │   add / delete / watch / registry     │   embedded WS server       │
└─────────────────────────┘                                       └────────────┬─────────────┘
                                                                                │  runs on the server thread
                                                                    ┌───────────▼────────────┐
                                                                    │  live game objects       │
                                                                    │  ServerPlayer, levels,   │
                                                                    │  gamerules, weather …    │
                                                                    └─────────────────────────┘
```

- The agent is attached at JVM start via `-javaagent`. Using ByteBuddy it hooks the (unobfuscated) server internals and stands up a small WebSocket server.
- Every request is authenticated, then **queued and applied on the server thread** at the start of the next tick — the editor never touches game state off-thread.
- Edits are **path-based** (`abilities.mayfly`, `Inventory[3].count`), so only the field you changed is touched.
- The full wire protocol is documented in [`docs/protocol.md`](docs/protocol.md).

---

## Requirements

- **Minecraft Java 26.2**, vanilla (no mod loader). LiveNBT relies on 26.x being unobfuscated; other versions are not supported.
- **Windows** — for the desktop editor app. The **agent is plain Java** and runs anywhere the game does, including Linux dedicated servers.
- To build from source: **Java 21+** (agent) and the **.NET SDK** (app).

---

## Instant use — no install, no launch arguments (recommended)

Just run stock vanilla Minecraft normally, then:

1. Open a world.
2. Run the LiveNBT app and click **⚡ Attach to Minecraft**.

That's it. The app finds the running game and loads the agent into it live via the JVM's Dynamic
Attach API — launched by **Minecraft's own bundled Java**, so nothing extra is required — then reads
the access token and connects automatically. No `-javaagent` argument, no launcher edits, no installer.

> How: the agent only rewrites method bodies, which the JVM allows on already-loaded classes
> (retransformation). This is standard, supported instrumentation — not memory hacking.

## Permanent install — auto `-javaagent` (optional)

If you'd rather the agent load every launch (e.g. for a dedicated server, or to skip the Attach
click):

1. Download the **LiveNBT installer** from [**Releases**](../../releases/latest) and run it.
2. It drops `livenbt-agent.jar` into place and **adds the `-javaagent` argument to your Minecraft
   launcher profile automatically** — you never edit JVM args by hand.
3. Launch Minecraft, open a world, run the app, and **Connect**.

---

## Manual install

If you'd rather wire it up yourself:

1. Place `livenbt-agent.jar` somewhere stable (e.g. next to your instance).
2. In your launcher profile's **JVM arguments**, add:
   ```
   -javaagent:<path-to>\livenbt-agent.jar -Dnet.bytebuddy.experimental=true
   ```
   (`-Dnet.bytebuddy.experimental=true` lets ByteBuddy instrument the modern JVM.)
3. Launch Minecraft and **open a world** (this starts the integrated server the agent hooks into).
4. Run the desktop app and **Connect** to `127.0.0.1:25599`, using the `token` from `.minecraft/config/livenbt.json`.

---

## Dedicated server (Linux)

The **same agent jar** runs inside a dedicated server — the app then shows **every online player**
(and their inventories) plus all world data, and connects to the server box by IP. Nothing here is
Windows-specific: the agent is plain Java.

On the server box:

1. Put `livenbt-agent.jar` next to `server.jar` and add the agent to your start command:
   ```sh
   java -javaagent:livenbt-agent.jar -Dnet.bytebuddy.experimental=true -jar server.jar nogui
   ```
2. Start the server once. The agent creates **`config/livenbt.json`** (relative to the server's
   working directory) and logs its location and the listen address. Open it, note the `token`, and
   set `"bind"` to the server's **LAN IP** so remote editors are allowed in:
   ```json
   { "bind": "<server LAN IP, e.g. 192.168.1.50>", "port": 25599, "token": "<32 hex chars>" }
   ```
   (`"0.0.0.0"` binds **every** interface — including a public one on a VPS. Only use it behind a
   firewall.)
3. Restart the server. **If the box is reachable from the internet, this step is not optional:**
   either scope a firewall rule to your LAN
   (`sudo ufw allow from 192.168.0.0/16 to any port 25599 proto tcp`), or — better on any box with
   a public IP — keep `"bind": "127.0.0.1"` and use the SSH tunnel from the security note below.

In the app (on your PC): **Profiles… → Add**, set **Host** to the server's IP, port `25599`, and
paste the `token` — then **Connect**. The roots dropdown lists `player:<name>` for everyone online
and `world:` for every dimension.

Notes:

- **Attach instead of `-javaagent`** also works on Linux if the server is already running
  (`java -cp livenbt-agent.jar dev.nitro.livenbt.attach.SelfAttach <pid> /abs/path/livenbt-agent.jar`),
  but it needs a full JDK, the same user as the server process, and survives only until the next
  restart — prefer the `-javaagent` line.
- **Empty servers are fine** — even when `pause-when-empty-seconds` has paused the world, 26.2 still
  invokes the tick entry point, so queued edits keep applying with nobody online. (Edits made while
  paused live in memory until the next save — autosaves resume once a player joins, and a server
  stop always saves.)
- **systemd**: the config path follows the process working directory, so set `WorkingDirectory=` to
  the server folder (or pin it with `-Dlivenbt.config=/path/to/livenbt.json`).
- **Security**: the socket is plain `ws://` and token-authenticated — treat it as LAN-only. Prefer a
  scoped firewall rule; on an untrusted network keep `bind` at `127.0.0.1` and tunnel instead:
  `ssh -L 25599:127.0.0.1:25599 user@server`, then connect the app to `127.0.0.1`.

---

## Usage

1. **Connect** to your world/server (`127.0.0.1:25599` by default) with your token.
2. **Load** a root:
   - a player — `player:<name>`
   - a dimension — `world:minecraft:overworld`
   - an inventory — `inventory:<name>`
3. **Edit** a value by double-clicking it, typing, and pressing **Enter** — it applies instantly in-game (accepted values flash; rejected ones show a reason in the status bar).
4. **Watch** a value from its right-click menu — it appears in the Watches panel and updates live.
5. Right-click also offers **Add tag**, **Delete**, **Copy path**, and **Copy as SNBT**.
6. In the **inventory** view, use the searchable item and enchantment pickers to build a slot from the bundled 26.2 registry, then apply.

What you can edit:

- **Player** — anything in player NBT. Fast paths for `Pos`, `Rotation`, `Health`, `foodLevel`, `XpLevel`, and `abilities.*`; everything else round-trips through an entity reload.
- **World** — `gamerules.<rule>`, `time.gameTime` and world-clock ticks, `weather.*`, `spawn.*`, `worldborder.*`, `difficulty` / `difficultyLocked`. (Most of these are server-global in vanilla — see [`docs/protocol.md`](docs/protocol.md).)
- **Inventory** — the 41 slots per player (`0`–`8` hotbar, `9`–`35` main, `36`–`39` armor, `40` offhand).

---

## Configuration

Config lives at **`.minecraft/config/livenbt.json`** and is **auto-created on first run**:

```json
{
  "bind": "127.0.0.1",
  "port": 25599,
  "token": "<32 hex chars>"
}
```

- `bind` — loopback (`127.0.0.1`) by default. Set `0.0.0.0` only for LAN/remote use on a network you trust.
- `port` — WebSocket port (default `25599`).
- `token` — 32 random hex chars, generated on first run. **Treat it like a password** — anyone with the token and network access to the port can edit your world. Token comparison is constant-time, and on Linux the file is created owner-only (`0600`).

---

## Build from source

**Agent** (`mod/`) — needs **Java 21+**:
```sh
cd mod
./gradlew shadowJar        # Windows: .\gradlew.bat shadowJar
# → mod/build/libs/livenbt-agent-*.jar
```

**App** (`app/`) — needs the **.NET SDK** (builds the Windows desktop app):
```sh
dotnet build app/LiveNBT.App
```

---

## Scope

LiveNBT is for editing **your own single-player game** or **a server you control**. It's a power tool: editing live NBT can corrupt data if you write nonsense, so back up worlds you care about.

## License

[MIT](LICENSE)
