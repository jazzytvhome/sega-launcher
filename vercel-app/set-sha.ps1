# Adds LATEST_SHA256 to Vercel (production) and redeploys.
# Run from vercel-app:  .\set-sha.ps1
$sha = "1bf2cad0282ff53989df2012d54a451377a41bb19c01478f7b79bdf533b6dd7c"

Write-Host "Removing any existing LATEST_SHA256 (ignore 'not found')..." -ForegroundColor Cyan
try { npx vercel env rm LATEST_SHA256 production --yes } catch {}

Write-Host "Adding LATEST_SHA256..." -ForegroundColor Cyan
$sha | npx vercel env add LATEST_SHA256 production

Write-Host "Redeploying to production..." -ForegroundColor Cyan
npx vercel --prod

Write-Host "Done. Tell Claude to re-check /api/version." -ForegroundColor Green
