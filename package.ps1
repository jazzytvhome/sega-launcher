# package.ps1 - build the encrypted blob (for Vercel) and the standalone launcher exe.
# The exe is the ONLY thing you hand out; it downloads game.enc from Vercel after a
# SEGA+ verification, so there's no blob to distribute.
#
#   -Key <base64>        GAME_KEY (else $env:GAME_KEY). MUST match Vercel's GAME_KEY.
#   -FrameworkDependent  Tiny multi-file build (needs the .NET 10 Desktop Runtime)
#                        instead of the ~125 MB self-contained single .exe (default).
#
# After this: cd vercel-app; .\deploy.ps1   (publishes game.enc + backend), then hand out the exe.

param(
  [string]$Key = $env:GAME_KEY,
  [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root     = $PSScriptRoot
$vercel   = Join-Path $root "vercel-app"
$launcher = Join-Path $root "launcher\SegaLauncher"

if (-not $Key) {
  Write-Error "No GAME_KEY. Pass -Key <base64> or set `$env:GAME_KEY. Generate: cd vercel-app; npm run keygen"
}

$cfg = Get-Content (Join-Path $launcher "AppConfig.cs") -Raw
if ($cfg -match "REPLACE_WITH_DISCORD_CLIENT_ID") {
  Write-Warning "AppConfig.cs still has the placeholder Discord Client ID."
}
if ($cfg -match "sega-roads\.vercel\.app") {
  Write-Warning "AppConfig.cs still points at the placeholder Vercel URL - set your real domain."
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

# --- 2. Stage the blob for Vercel (served gated by middleware) ------------
$pubBlob = Join-Path $vercel "public\game.enc"
New-Item -ItemType Directory -Force (Split-Path $pubBlob) | Out-Null
Copy-Item $blob $pubBlob -Force
Write-Host "==> Staged blob -> vercel-app/public/game.enc" -ForegroundColor Cyan

# --- 3. Publish the launcher exe ------------------------------------------
# The self-contained build obfuscates the managed assembly via Obfuscar
# (global tool). Make sure it's installed so the build doesn't fail mid-way.
if (-not $FrameworkDependent) {
  $hasObfuscar = Get-Command obfuscar.console -ErrorAction SilentlyContinue
  if (-not $hasObfuscar) {
    Write-Host "==> Installing Obfuscar global tool..." -ForegroundColor Cyan
    dotnet tool install -g Obfuscar.GlobalTool
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
  }
}

Write-Host "==> Publishing launcher exe..." -ForegroundColor Cyan
$pub = Join-Path $launcher "bin\package-publish"
if (Test-Path $pub) { Remove-Item -Recurse -Force $pub }
Push-Location $launcher
try {
  if ($FrameworkDependent) {
    dotnet publish -c Release -o $pub
  } else {
    # -p:Obfuscate=true runs Obfuscar over the managed assembly before the single-file
    # bundle (see SegaLauncher.csproj): renames the logic classes + encrypts strings,
    # while leaving WPF (App/MainWindow) and JSON property names intact.
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Obfuscate=true -o $pub
  }
} finally { Pop-Location }

# --- 4. Deliverable -------------------------------------------------------
$exeName = "SEGA+ Launcher.exe"
Write-Host ""
if ($FrameworkDependent) {
  Write-Host "Framework-dependent build (multi-file) -> $pub" -ForegroundColor Green
  Write-Host "Ship that whole folder (users need the .NET 10 Desktop Runtime)." -ForegroundColor Green
} else {
  $outExe = Join-Path $root $exeName
  Copy-Item (Join-Path $pub $exeName) $outExe -Force
  $mb = [math]::Round((Get-Item $outExe).Length / 1MB, 1)
  $sha = (Get-FileHash $outExe -Algorithm SHA256).Hash.ToLower()
  Write-Host "Standalone exe -> $outExe ($mb MB)" -ForegroundColor Green
  Write-Host "SHA-256: $sha" -ForegroundColor Green
  Write-Host "(set LATEST_SHA256 in .env to this so auto-update accepts it)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "NEXT: cd vercel-app; .\deploy.ps1   (publishes game.enc + backend to Vercel)" -ForegroundColor Yellow
Write-Host "Then hand out the exe. Reminder: the GAME_KEY you used must match Vercel's." -ForegroundColor Yellow
