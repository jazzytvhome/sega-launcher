# deploy.ps1 - push env vars from .env to Vercel and deploy to production.
#
# FIRST: log in to Vercel (interactive - complete it in Google Chrome):
#     npx vercel login
#
# Then:
#     .\deploy.ps1                 # personal account (default), uses .env
#     .\deploy.ps1 -DryRun         # print what it would do, run nothing
#     .\deploy.ps1 -Scope myteam   # ONLY if deploying under a Vercel TEAM (not a personal account)
#
# Run this from the vercel-app folder.

param(
  [string]$EnvFile = (Join-Path $PSScriptRoot ".env"),
  [string]$Scope = "",          # empty = your personal account (the default). Set only for a team.
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$keys = @("DISCORD_CLIENT_ID", "DISCORD_CLIENT_SECRET", "DISCORD_BOT_TOKEN", "SEGA_GUILD_ID", "GAME_KEY")

# Only pass --scope when a (team) scope is given; a personal account is rejected by --scope.
$scopeArgs = @()
if ($Scope) { $scopeArgs = @("--scope", $Scope) }
$scopeLabel = if ($Scope) { $Scope } else { "(personal account - default)" }

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

if (-not (Test-Path (Join-Path $PSScriptRoot "public\game.enc"))) {
  Write-Warning "public/game.enc is missing - the launcher's download will 404. Run ..\package.ps1 first to stage it."
}

Write-Host "Vercel scope: $scopeLabel" -ForegroundColor Cyan
Write-Host "(If this fails with an auth error, run 'npx vercel login' and complete it in Google Chrome.)" -ForegroundColor Yellow

if ($DryRun) {
  Write-Host "[dry run] npx vercel link --yes $($scopeArgs -join ' ')"
  foreach ($k in $keys) {
    Write-Host "[dry run] npx vercel env rm $k production --yes $($scopeArgs -join ' ')   (ignored if absent)"
    Write-Host "[dry run] <value of $k> | npx vercel env add $k production $($scopeArgs -join ' ')"
  }
  Write-Host "[dry run] npx vercel deploy --prod $($scopeArgs -join ' ')"
  return
}

# --- link the project (idempotent; creates it on first run) ---
npx vercel link --yes @scopeArgs

# --- push each var to production (remove-then-add so re-runs update cleanly) ---
foreach ($k in $keys) {
  Write-Host "==> $k" -ForegroundColor Cyan
  try { npx vercel env rm $k production --yes @scopeArgs 2>$null | Out-Null } catch {}
  $vals[$k] | npx vercel env add $k production @scopeArgs
}

# --- optional vars (pushed only if set in .env) ---
foreach ($k in @("BLACKLIST", "MIN_VERSION", "LATEST_VERSION", "DOWNLOAD_URL")) {
  if ($vals.ContainsKey($k) -and $vals[$k]) {
    Write-Host "==> $k (optional)" -ForegroundColor Cyan
    try { npx vercel env rm $k production --yes @scopeArgs 2>$null | Out-Null } catch {}
    $vals[$k] | npx vercel env add $k production @scopeArgs
  }
}

# --- deploy ---
npx vercel deploy --prod @scopeArgs

Write-Host ""
Write-Host "Done. Copy the production URL above into launcher\SegaLauncher\AppConfig.cs" -ForegroundColor Green
Write-Host "(VercelBaseUrl), then run ..\package.ps1 -Key '<your GAME_KEY>'." -ForegroundColor Green
