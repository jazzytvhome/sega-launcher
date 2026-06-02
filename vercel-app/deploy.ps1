# deploy.ps1 - push env vars from .env to Vercel and deploy to production.
#
# FIRST: log in to Vercel (interactive - complete it in Google Chrome):
#     npx vercel login
#
# Then:
#     .\deploy.ps1                 # uses .env, scope jazzytvhome
#     .\deploy.ps1 -DryRun         # print what it would do, run nothing
#     .\deploy.ps1 -Scope other    # different Vercel team/user
#
# Run this from the vercel-app folder.

param(
  [string]$EnvFile = (Join-Path $PSScriptRoot ".env"),
  [string]$Scope = "jazzytvhome",
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$keys = @("DISCORD_CLIENT_ID", "DISCORD_CLIENT_SECRET", "DISCORD_BOT_TOKEN", "SEGA_GUILD_ID", "GAME_KEY")

if (-not (Test-Path $EnvFile)) {
  Write-Error "No $EnvFile found. Copy .env.example to .env and fill it in."
}

# --- parse .env (KEY=VALUE, ignore blanks/comments) ---
$vals = @{}
foreach ($line in Get-Content $EnvFile) {
  $t = $line.Trim()
  if ($t -and -not $t.StartsWith("#") -and $t.Contains("=")) {
    $i = $t.IndexOf("=")
    $vals[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
  }
}

$missing = $keys | Where-Object { -not $vals[$_] }
if ($missing) { Write-Error "Missing values in ${EnvFile}: $($missing -join ', ')" }

Write-Host "Vercel scope: $Scope" -ForegroundColor Cyan
Write-Host "(If this fails with an auth error, run 'npx vercel login' and complete it in Google Chrome.)" -ForegroundColor Yellow

if ($DryRun) {
  Write-Host "[dry run] npx vercel link --yes --scope $Scope"
  foreach ($k in $keys) {
    Write-Host "[dry run] npx vercel env rm $k production --yes --scope $Scope   (ignored if absent)"
    Write-Host "[dry run] <value of $k> | npx vercel env add $k production --scope $Scope"
  }
  Write-Host "[dry run] npx vercel deploy --prod --scope $Scope"
  return
}

# --- link the project (idempotent) ---
npx vercel link --yes --scope $Scope

# --- push each var to production (remove-then-add so re-runs update cleanly) ---
foreach ($k in $keys) {
  Write-Host "==> $k" -ForegroundColor Cyan
  try { npx vercel env rm $k production --yes --scope $Scope 2>$null | Out-Null } catch {}
  $vals[$k] | npx vercel env add $k production --scope $Scope
}

# --- deploy ---
npx vercel deploy --prod --scope $Scope

Write-Host ""
Write-Host "Done. Copy the production URL above into launcher\SegaLauncher\AppConfig.cs" -ForegroundColor Green
Write-Host "(VercelBaseUrl), then run ..\package.ps1 -Key '<your GAME_KEY>'." -ForegroundColor Green
