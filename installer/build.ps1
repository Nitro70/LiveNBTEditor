#Requires -Version 5
# Builds the LiveNBT release into installer\dist:
#   LiveNBT-Setup.exe  - self-contained installer, embeds the agent jar
#   app\               - the self-contained desktop app the installer drops in
# Zip the contents of dist\ and attach to the GitHub release.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $PSCommandPath
$repo = (Resolve-Path (Join-Path $here "..")).Path
$dist = Join-Path $here "dist"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "==> Building the agent jar (gradlew shadowJar)" -ForegroundColor Cyan
# drop old jars first so a failed build can never stage a stale one
Remove-Item (Join-Path $repo "mod\build\libs\livenbt-agent-*.jar") -Force -ErrorAction SilentlyContinue
Push-Location (Join-Path $repo "mod")
try {
    & .\gradlew.bat shadowJar --console=plain
    if ($LASTEXITCODE -ne 0) { throw "gradlew shadowJar failed (exit $LASTEXITCODE)" }
} finally { Pop-Location }
$jar = Get-ChildItem (Join-Path $repo "mod\build\libs") -Filter "livenbt-agent-*.jar" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $jar) { throw "agent jar not found under mod/build/libs" }

Write-Host "==> Embedding the agent jar into the installer and the app" -ForegroundColor Cyan
$res = Join-Path $here "LiveNBT.Installer\Resources"
New-Item -ItemType Directory -Force $res | Out-Null
Copy-Item $jar.FullName (Join-Path $res "livenbt-agent.jar") -Force
# the desktop app embeds the same jar so "Attach to Minecraft" is self-contained
$appRes = Join-Path $repo "app\LiveNBT.App\Resources"
New-Item -ItemType Directory -Force $appRes | Out-Null
Copy-Item $jar.FullName (Join-Path $appRes "livenbt-agent.jar") -Force

Write-Host "==> Publishing the desktop app (self-contained, no .NET prereq)" -ForegroundColor Cyan
dotnet publish (Join-Path $repo "app\LiveNBT.App\LiveNBT.App.csproj") -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $dist "app")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) failed (exit $LASTEXITCODE)" }

Write-Host "==> Publishing the installer (self-contained, single file)" -ForegroundColor Cyan
dotnet publish (Join-Path $here "LiveNBT.Installer\LiveNBT.Installer.csproj") -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Release staged in: $dist" -ForegroundColor Green
Write-Host "  LiveNBT-Setup.exe    <- users run this"
Write-Host "  app\LiveNBT.App.exe  <- bundled desktop app"
