# LiveNBT installer

`LiveNBT-Setup.exe` is a self-contained Windows installer that makes LiveNBT painless — no editing
JVM arguments by hand.

## What it does

1. Copies the agent jar to `%APPDATA%\LiveNBT\livenbt-agent.jar`.
2. Adds `-javaagent:"…livenbt-agent.jar" -Dnet.bytebuddy.experimental=true` to every **Minecraft 26.x**
   profile in `launcher_profiles.json` that doesn't already have it (backing the file up first, and
   preserving each profile's existing JVM args).
3. Writes `%APPDATA%\.minecraft\config\livenbt.json` with a fresh random access token (or reuses an
   existing one), bound to `127.0.0.1`.
4. Seeds the desktop app's connection profile (`%APPDATA%\LiveNBT\profiles.json`) with the matching token.
5. If the desktop app was shipped beside the installer (`.\app\`), installs it and adds a Start-menu shortcut.

Run `LiveNBT-Setup.exe --dry-run` to preview every change without writing anything.

## Building the release

From this directory:

```powershell
.\build.ps1
```

That builds the agent jar, embeds it, publishes the self-contained desktop app and installer, and
stages everything under `dist\`:

```
dist\LiveNBT-Setup.exe     <- users run this
dist\app\LiveNBT.App.exe   <- bundled desktop app
```

Zip the contents of `dist\` and attach it to the GitHub release.

> The embedded jar (`LiveNBT.Installer\Resources\livenbt-agent.jar`) and `dist\` are build artifacts
> and are git-ignored; `build.ps1` regenerates them.
