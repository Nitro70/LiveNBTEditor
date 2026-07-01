# LiveNBT

**Edit Minecraft NBT in the *running* game — no file editing, no world reload, no mod loader.**

LiveNBT is a two-part tool for **vanilla Minecraft Java 26.2**:

- a **Java agent** (`-javaagent`, ByteBuddy) injected into Minecraft that runs a small WebSocket server *inside* the integrated (or dedicated) server, and
- a **Windows desktop editor** (WPF) that shows the live game's player, world, and inventory NBT as a tree you can edit, with changes applying **instantly** in-game.

Traditional NBT editors work on `.dat` files on disk, so you have to quit the world, edit, and reload. LiveNBT talks to the running server instead: when you change a value, the agent applies it to the live game object on the next server tick. Set `Health` and your hearts drop immediately; flip `weather.raining` and it starts raining; watch your `Pos` stream as you walk.

It works on **26.2 specifically** because the 26.x client and server ship **unobfuscated** (official Mojang names). That means the agent can hook the game by its real class and method names — **no mappings, no Fabric/Forge, no mod loader of any kind.** You run stock vanilla Minecraft with one extra JVM flag.

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
- **Windows** — for the desktop editor app.
- To build from source: **Java 21+** (agent) and the **.NET SDK** (app).

---

## Install (recommended — via the installer)

1. Download the **LiveNBT installer** from [**Releases**](../../releases/latest) and run it.
2. Point it at your Minecraft launcher profile. The installer:
   - drops `livenbt-agent.jar` into place, and
   - **adds the `-javaagent` argument to that launcher profile's JVM arguments automatically** — you never edit JVM args by hand.
3. Launch Minecraft with that profile and open a world.
4. Run the desktop app, and **Connect** to `127.0.0.1:25599` using the token from `.minecraft/config/livenbt.json` (auto-created on first run).

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
- `token` — 32 random hex chars, generated on first run. **Treat it like a password** — anyone with the token and network access to the port can edit your world. Token comparison is constant-time.

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
