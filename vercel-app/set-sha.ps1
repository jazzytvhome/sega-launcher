# Adds LATEST_SHA256 to Vercel (production) and redeploys.
# Run from vercel-app:  .\set-sha.ps1
$sha = "b098c908f5579ef77683f9272ac5aa8b4c50d095ee1c889cc1b28eac7fc8a2bc"

Write-Host "Removing any existing LATEST_SHA256 (ignore 'not found')..." -ForegroundColor Cyan
try { npx vercel env rm LATEST_SHA256 production --yes } catch {}

Write-Host "Adding LATEST_SHA256..." -ForegroundColor Cyan
$sha | npx vercel env add LATEST_SHA256 production

Write-Host "Redeploying to production..." -ForegroundColor Cyan
npx vercel --prod

Write-Host "Done. Tell Claude to re-check /api/version." -ForegroundColor Green
