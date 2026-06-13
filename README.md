# LiveNBTEditor

**Edit Minecraft NBT in the *running* game — no file editing, no world reload.**

LiveNBTEditor is a two-part tool for Minecraft Java **26.1.2 (Fabric)**:

- a server-side **Fabric mod** that exposes the live game's player and world NBT over a local WebSocket, and
- a **Windows desktop editor** that shows that NBT as a tree you can edit, with changes applying **instantly** in-game.

Traditional NBT editors work on `.dat` files on disk, so you have to quit the world, edit, and reload. LiveNBTEditor talks to the running server instead: when you change a value, the mod applies it to the live game object on the next server tick. Set `Health` and your hearts drop immediately; flip `weather.raining` and it starts raining; watch your `Pos` stream as you walk.

> Works in singleplayer (the integrated server) and on dedicated servers you control.

---

## Features

- **Live editing** — edit player data (health, food, XP, abilities, position, inventory, …) and world data (gamerules, time, weather, spawn, world border, difficulty) and see it apply at once.
- **Live watch panel** — pin any value and watch it update ~5×/second.
- **Auto-refreshing tree** — the editor re-reads what you have open so displayed values stay current as the game changes, with no clicking.
- **Type-safe** — every edit is validated against the real NBT type before it's sent; bad input is rejected with a readable message, never written to your save.
- **Safe by default** — the mod listens on `127.0.0.1` only and requires a per-install auth token. Opt into LAN access explicitly.
- **Copy as SNBT** — right-click any tag to copy it in `/data`-style SNBT for use in commands.

## How it works

```
┌─────────────────────────┐   WebSocket (JSON, port 25599)   ┌──────────────────────────┐
│   LiveNBT.App (WPF)     │ ◄──────────────────────────────► │   livenbt (Fabric mod)   │
│   tree view, editing,   │   hello → auth → get / set /     │   embedded WS server      │
│   live watch panel      │   add / delete / watch / update  │   ops run on server thread│
└─────────────────────────┘                                  └────────────┬─────────────┘
                                                                          │
                                                              ┌───────────▼────────────┐
                                                              │  live game objects      │
                                                              │  ServerPlayer, levels,  │
                                                              │  gamerules, weather …   │
                                                              └─────────────────────────┘
```

- The mod runs a small WebSocket server. Every request is authenticated, then **queued and applied on the server thread** at the start of the next tick — so the editor never touches game state off-thread.
- Edits are **path-based** (`abilities.mayfly`, `Inventory[3].count`), so only the field you changed is touched — no whole-file rewrites, no lost updates from values that change every tick.
- Player edits round-trip through the entity's own NBT load, with fast paths (teleport, set-health, ability sync) for fields where a raw reload would have side effects.
- World values are a curated tree where every leaf maps to a real live getter/setter — nothing is faked.
- The full wire protocol is documented in [`docs/protocol.md`](docs/protocol.md).

---

## Install (compiled — no build tools needed)

Grab the latest [**Release**](../../releases/latest). It contains:

| File | What it is |
| --- | --- |
| `livenbt-<version>.jar` | the Fabric mod |
| `LiveNBT-App-win-x64.zip` | the Windows editor (self-contained — no .NET install required) |

**1. Install the mod**
- You need a **Fabric 26.1.2** profile with **Java 25** and **Fabric API** (any 26.1.2 build).
- Drop `livenbt-<version>.jar` (and `fabric-api`) into that profile's `mods/` folder.
- Launch a world. The log shows `LiveNBT listening on 127.0.0.1:25599`.

**2. Run the editor**
- Unzip `LiveNBT-App-win-x64.zip` and run `LiveNBT.App.exe`.

**3. Connect**
- Click **Profiles…**, paste your token (see below) into the JSON file that opens, save, and click **Profiles…** again to reload.
- Pick the **Singleplayer** profile → **Connect** → choose a root (e.g. `player:<you>`) → **Load**.

Your token is generated on first run and lives in `<your instance>/config/livenbt.json`.

> **Singleplayer pause gotcha:** a singleplayer world pauses when its window loses focus, which freezes the integrated server — so connect/auth work but every edit "hangs" while you're tabbed into the editor. Press **F3 + P** in-game (or set `pauseOnLostFocus:false` in `options.txt`) before switching to the editor. Dedicated servers don't have this issue.

---

## Usage

1. **Connect** to your world/server using a saved profile.
2. **Load** a root: a player (`player:<name>`) or a dimension (`world:minecraft:overworld`).
3. **Edit** a value by double-clicking it, typing, and pressing **Enter** — it applies instantly in-game (green flash = accepted, red = rejected with a reason in the status bar).
4. **Watch** a value via its right-click menu — it appears in the Watches panel and updates live.
5. Right-click also offers **Add tag**, **Delete**, **Copy path**, and **Copy as SNBT**.

What you can edit today:

- **Player** — anything in player NBT. Fast paths for `Pos`, `Rotation`, `Health`, `foodLevel`, `XpLevel`, and `abilities.*`; everything else round-trips through an entity reload.
- **World** — `gamerules.<rule>`, `time.gameTime` and world-clock ticks, `weather.*`, `spawn.*`, `worldborder.*`, `difficulty` / `difficultyLocked`. (These are server-global in vanilla — see the notes in [`docs/protocol.md`](docs/protocol.md).)

---

## Security

- The mod binds **`127.0.0.1` (loopback) by default** and requires a **token** (32 random hex chars, generated on first run) before any operation.
- The token lives in `config/livenbt.json`. **Treat it like a password** — anyone with the token and network access to the port can edit your world.
- For LAN / remote-server use, change `bind` to `0.0.0.0` in that file and connect with the token from another machine. Only do this on a network you trust.
- The editor stores connection profiles (host/port/token) in `%APPDATA%\LiveNBT\profiles.json`.

This is a power tool. Editing live NBT can corrupt data if you write nonsense — back up worlds you care about.

---

## Build from source

**Mod** (`mod/`) — needs **JDK 25**:
```sh
cd mod
./gradlew build        # Windows: .\gradlew.bat build
# → mod/build/libs/livenbt-<version>.jar
```
The build uses [Unimined](https://github.com/unimined/unimined) and pulls Minecraft 26.1.2 automatically. If your JDK 25 is in a non-standard location, point Gradle at it from your **global** `~/.gradle/gradle.properties` (the project file is intentionally machine-agnostic):
```properties
org.gradle.java.installations.paths=/path/to/jdk-25
```

**App** (`app/`) — needs the **.NET SDK 9.0.200+** (for the `.slnx` solution) and builds the Windows-only .NET 8 desktop app:
```sh
cd app
dotnet test                       # 61 unit tests
dotnet run --project LiveNBT.App  # launch the editor
```
To produce the self-contained release exe:
```sh
dotnet publish LiveNBT.App -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

There's also a small protocol smoke test (`tools/smoke.py`, needs `pip install websockets`):
```sh
python tools/smoke.py <token>
```

## Project layout

```
mod/    Fabric mod (Java, Gradle + Unimined, MC 26.1.2)
app/    C# WPF editor — LiveNBT.Protocol (codec + protocol), LiveNBT.App (UI), LiveNBT.Tests
docs/   protocol.md — the WebSocket protocol both sides implement
tools/  smoke.py — protocol smoke test
```

## Compatibility

Built and tested against **Minecraft 26.1.2 / Fabric**. Other versions are not supported (the mod uses version-specific Minecraft internals).

## License

[MIT](LICENSE)
