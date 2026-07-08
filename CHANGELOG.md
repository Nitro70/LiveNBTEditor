# Changelog

## v0.4.1

Bug fix for the live refresh. App-only; agent unchanged from v0.3.0.

### Fixed
- The 2-second live refresh no longer collapses an expanded compound (an inventory item,
  `components`, attributes, etc.) when the game re-serializes it with reordered or newly-added
  keys. Compound children are now matched by name rather than position, so the tree updates in
  place and keeps your expansion and selection.

## v0.4.0

The ease-of-use release: the editing workflow from NBT Studio, adapted for live editing.
Everything is additive — existing flows are untouched — and everything works against the running
game (no files, no saving). App-only — the agent is unchanged from v0.3.0, so dedicated-server
operators don't need to redeploy.

### Added
- **Keyboard-first editing** (tree must have focus): `Enter`/`Ctrl+E` edit value, `F2` rename,
  `Del` delete (selection stays near the hole), `Space` expand/collapse, `Ctrl+Space` expand-all
  (capped), `Ctrl+Up` jump to parent, `Alt+Up`/`Alt+Down` reorder list elements.
- **Clipboard**: `Ctrl+C` copy as SNBT (`name:value` for compound keys — round-trips with
  NBT Studio), `Ctrl+Shift+C` copy path, `Ctrl+X` cut, `Ctrl+V` paste SNBT — single tags,
  pretty-printed subtrees, or one-tag-per-line batches; name collisions auto-rename.
- **Undo / Redo** — `Ctrl+Z` / `Ctrl+Shift+Z` (or `Ctrl+Y`) for every edit made in the app
  (set, add, delete, paste, duplicate, rename, reorder). Live-editor semantics: undo restores
  your pre-edit snapshot; if the game changed the value meanwhile, the snapshot still wins.
- **Duplicate** (`Ctrl+D`) with automatic `Name1`, `Name2`… naming.
- **Edit as SNBT** (`Ctrl+Shift+E`) — whole-subtree text editing in a multiline dialog
  (also the way to edit multiline strings); type-checked so a set can't change a tag's type.
  Plus **Add as SNBT** on containers.
- **Deep Find** (`Ctrl+Shift+F`) — search names and values across the whole loaded tree
  (substring or regex), Find Next/Prev wraps, Find All lists results; click to jump. The old
  `Ctrl+F` top-level filter is unchanged.
- **Quick add** submenu — insert a typed tag with an automatic name in one click, no dialog.
- **Byte/Int/Long arrays** can now be created in the Add Tag dialog (optionally comma-seeded).
- **Colored type chips** (numeric/string/container/array) and hover tooltips for long strings
  and array previews.
- **Ctrl+mouse-wheel tree zoom**, persisted across runs.
- Watches panel: **✕ to unwatch**, **double-click to jump** to the tag in the tree.
- **In-app Help** (`F1` or the ❔ Help button) — how to connect, how editing works, and the full
  shortcut reference without leaving the app.
- Context menu items grey out when the server would reject the operation (world roots are
  structurally read-only, inventories don't support add, only compound keys rename).

### Changed
- Adding or deleting tags now refreshes just the affected branch — the tree no longer collapses
  after structural edits; expansion, selection and scroll survive.
- "Copy as SNBT" on a compound key now prefixes the key name (`Health:20.0f`) so pastes into
  NBT Studio and LiveNBT land with the right name.

## v0.3.0

The dedicated-server release: run the same agent inside a **Linux dedicated server** and edit it
remotely from the Windows app.

### Added
- **Dedicated server support (Linux)** — load `livenbt-agent.jar` into a vanilla dedicated server
  with `-javaagent` (or dynamic attach); the app connects by IP and lists **every online player**
  and world. Full setup guide in the README, including firewall/SSH-tunnel guidance and systemd
  working-directory notes.
- The agent logs its config file location on startup, with a hint when it's bound to loopback only —
  server operators can find the token and `bind` setting without hunting.
- Remote-server hint in the Profiles dialog.

### Fixed
- **Tick hook fired twice per tick on dedicated servers** (and on unpaused singleplayer): the advice
  was woven into both `MinecraftServer` and its subclass overrides, which call `super`. It is now
  woven only where each method actually needs it (tick → the two concrete overrides, stop → the base
  class), verified against the 26.2 client and server jars.
- IPv6 hosts (e.g. `::1`) are now bracketed correctly in the WebSocket URL.
- On servers with multiple players online, connect no longer auto-loads an arbitrary player's NBT —
  the status bar shows the online count and lets you pick.
- A non-numeric port in the Profiles dialog is rejected with a message instead of silently saving
  as 25599.

## v0.2.0

The standalone, no-setup release.

### Added
- **⚡ Attach to Minecraft (arg-less, one click):** load LiveNBT into a stock, already-running
  vanilla Minecraft with **no `-javaagent` argument and no installer** — the app finds the game and
  loads the agent live via the JVM's Dynamic Attach API, using Minecraft's own bundled Java. Reads
  the access token and connects automatically.
- **Windows installer (optional, antivirus-friendly):** auto-adds the `-javaagent` argument to your
  launcher profile so the agent loads every launch. Doesn't inject at runtime, so it won't trip AV.
- **Full vanilla 26.2 registry bundled in the app** — every item + enchantment id is searchable
  even before you connect.
- **Professional dark theme** (grey + dark blue) across every window, with dark title bars.
- **Profiles dialog** with **"Detect from this PC's game"** — reads the token from your game config,
  no copy-pasting. Replaces the old edit-JSON-in-notepad flow.
- **Ease of access:** auto-connect on launch, auto-reconnect on drop, auto-load your player after
  connecting, window size/position memory, and hotkeys (F5 refresh, Ctrl+F filter, Ctrl+I inventory).
- **Live in-game edits:** the agent keeps the integrated server ticking while the app is connected,
  so changes apply and show up immediately without opening to LAN.

### Fixed
- Item/enchantment search suggestions (a binding bug had left them silently non-functional).
- Connect race and "stuck connected" state; the selected root is preserved when refreshing roots.
- Changing an existing enchantment's level now works; empty inventory slots no longer show a phantom count.
- Enchant level box fits 3 digits (255); default window fits the full toolbar.
- Installer: space-safe unquoted `-javaagent`, wires the stock *Latest Release/Snapshot* profiles,
  atomic launcher file writes, and refuses to patch while the launcher is open.
- Agent: surfaces weave failures in the log, drains queued edits on world reload, and fixes a
  keep-awake counter race.

### Notes
- **Vanilla Minecraft Java 26.2 only** — LiveNBT relies on 26.x shipping unobfuscated.
- The **⚡ Attach** path uses runtime code injection, which strict antivirus (e.g. Bitdefender) may
  flag; add an exception for it, or use the **installer** path, which doesn't inject.
- The downloadable `.exe` is **unsigned**, so Windows SmartScreen may warn on first run
  ("More info" → "Run anyway"). Code signing is planned.

## v0.1.0
- Initial release: live NBT tree editor + inventory editor for Minecraft Java (server-side mod + WPF app).
