# package.ps1 - one-command distribution builder for the SEGA+ Launcher.
# Builds game.enc, publishes the launcher, and zips a ready-to-post package.
#
#   -Key <base64>     GAME_KEY to encrypt game.enc with (else uses $env:GAME_KEY).
#                     MUST match the GAME_KEY in your Vercel env.
#                     Generate one: cd vercel-app; npm run keygen
#   -SelfContained    ~125 MB exe, no runtime needed by users (too big for Discord;
#                     host externally). Default is ~222 KB framework-dependent.
#   -IncludeSource    Bundle the launcher source too (OFF by default - shipping the
#                     launcher source makes the SEGA+ gate easier to reverse).
#
# Examples:
#   $env:GAME_KEY = "<base64>"; .\package.ps1
#   .\package.ps1 -Key "<base64>" -SelfContained

param(
  [string]$Key = $env:GAME_KEY,
  [switch]$SelfContained,
  [switch]$IncludeSource
)

$ErrorActionPreference = "Stop"
$root     = $PSScriptRoot
$vercel   = Join-Path $root "vercel-app"
$launcher = Join-Path $root "launcher\SegaLauncher"
$staging  = Join-Path $root "dist-package"
$zipPath  = Join-Path $root "SEGA-Plus-Launcher.zip"

if (-not $Key) {
  Write-Error "No GAME_KEY. Pass -Key <base64> or set `$env:GAME_KEY. Generate: cd vercel-app; npm run keygen"
}

# --- AppConfig sanity check ------------------------------------------------
$cfg = Get-Content (Join-Path $launcher "AppConfig.cs") -Raw
if ($cfg -match "REPLACE_WITH_DISCORD_CLIENT_ID") {
  Write-Warning "AppConfig.cs still has the placeholder Discord Client ID - the launcher won't verify until you set it."
}
if ($cfg -match "sega-roads\.vercel\.app") {
  Write-Warning "AppConfig.cs still points at the placeholder Vercel URL (sega-roads.vercel.app) - set your real domain."
}

# --- 1. Build the encrypted blob ------------------------------------------
Write-Host "==> Building game.enc..." -ForegroundColor Cyan
Push-Location $vercel
try {
  $env:GAME_KEY = $Key
  npm run obfuscate
  npm run build:blob
} finally { Pop-Location }
$blob = Join-Path $vercel "dist\game.enc"
if (-not (Test-Path $blob)) { Write-Error "game.enc was not produced." }

# --- 2. Publish the launcher ----------------------------------------------
Write-Host "==> Publishing launcher..." -ForegroundColor Cyan
$pub = Join-Path $launcher "bin\package-publish"
if (Test-Path $pub) { Remove-Item -Recurse -Force $pub }
Push-Location $launcher
try {
  if ($SelfContained) {
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $pub
  } else {
    dotnet publish -c Release -o $pub
  }
} finally { Pop-Location }

# --- 3. Assemble staging folder -------------------------------------------
Write-Host "==> Assembling package..." -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null

Get-ChildItem $pub -File | Where-Object { $_.Extension -ne ".pdb" } | Copy-Item -Destination $staging
Copy-Item $blob -Destination $staging

if ($SelfContained) {
  $runtimeNote = "This is the self-contained build - no .NET runtime needed."
} else {
  $runtimeNote = "This build needs the .NET 10 Desktop Runtime (x64), a free official Microsoft component: https://dotnet.microsoft.com/download/dotnet/10.0"
}

$readme = @"
SEGA+ Installer - source code for members

HOW TO INSTALL
  1. Keep "SEGA+ Launcher.exe" and "game.enc" in the SAME folder.
  2. Run "SEGA+ Launcher.exe".
  3. Pick a source code from the list, then click "Verify with Discord & Install".
     Authorize in your browser - you must be a member of the SEGA+ Discord server.
  4. Choose a folder. The source code installs there and the folder opens.

REQUIREMENTS
  - Windows 10/11 (x64).
  - $runtimeNote

IS THIS A VIRUS?
  No. All the launcher does: open Discord to check your SEGA+ membership, then
  decrypt and write the source code to a folder you choose. Nothing else is
  installed; nothing is sent anywhere except Discord's own login. Scan it if you
  like (link in the launcher).

CREDITS
  Slow Roads by slowroads.io (Anslo) - full credit to the original. Private,
  non-commercial, shared within SEGA+.
"@
Set-Content -Path (Join-Path $staging "READ ME.txt") -Value $readme -Encoding UTF8

if ($IncludeSource) {
  $srcDest = Join-Path $staging "source\SegaLauncher"
  New-Item -ItemType Directory -Force $srcDest | Out-Null
  Get-ChildItem $launcher -File | Where-Object { @(".cs", ".xaml", ".csproj") -contains $_.Extension } | Copy-Item -Destination $srcDest
}

# --- 4. Zip ----------------------------------------------------------------
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath
$mb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Done -> $zipPath ($mb MB)" -ForegroundColor Green
Write-Host "Post this zip in SEGA+. Reminder: the GAME_KEY you used must match Vercel's." -ForegroundColor Green
