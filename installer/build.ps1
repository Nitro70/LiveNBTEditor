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
Push-Location (Join-Path $repo "mod")
try { & .\gradlew.bat shadowJar --console=plain } finally { Pop-Location }
$jar = Get-ChildItem (Join-Path $repo "mod\build\libs") -Filter "livenbt-agent-*.jar" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $jar) { throw "agent jar not found under mod/build/libs" }

Write-Host "==> Embedding the agent jar into the installer" -ForegroundColor Cyan
$res = Join-Path $here "LiveNBT.Installer\Resources"
New-Item -ItemType Directory -Force $res | Out-Null
Copy-Item $jar.FullName (Join-Path $res "livenbt-agent.jar") -Force

Write-Host "==> Publishing the desktop app (self-contained, no .NET prereq)" -ForegroundColor Cyan
dotnet publish (Join-Path $repo "app\LiveNBT.App\LiveNBT.App.csproj") -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $dist "app")

Write-Host "==> Publishing the installer (self-contained, single file)" -ForegroundColor Cyan
dotnet publish (Join-Path $here "LiveNBT.Installer\LiveNBT.Installer.csproj") -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -o $dist

Write-Host ""
Write-Host "Release staged in: $dist" -ForegroundColor Green
Write-Host "  LiveNBT-Setup.exe    <- users run this"
Write-Host "  app\LiveNBT.App.exe  <- bundled desktop app"
