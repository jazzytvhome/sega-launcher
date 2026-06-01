# SEGA+ Launcher — finish-up checklist

Everything is built and on the **`sega-launcher`** git branch. Two things need *you*
(they involve secrets / live accounts I can't and shouldn't do):

## 1. Discord app + bot (Task 9)

1. <https://discord.com/developers/applications> → **New Application**.
2. **OAuth2** page → copy **Client ID** and **Client Secret**.
3. **OAuth2 → Redirects** → add EXACTLY: `http://127.0.0.1:51789/callback` → Save.
4. **Bot** page → Add Bot → copy **Bot Token** → enable **Server Members Intent**.
5. Invite the bot to **SEGA+** (OAuth2 URL Generator, scope `bot`, no extra perms).
6. In Discord (Developer Mode on) → right-click the SEGA+ icon → **Copy Server ID**.

## 2. Deploy + env vars (Task 9)

```powershell
cd "C:\Users\jazzy\Downloads\slow roads\vercel-app"
npx vercel            # first deploy; note the production domain
npx vercel env add DISCORD_CLIENT_ID
npx vercel env add DISCORD_CLIENT_SECRET
npx vercel env add DISCORD_BOT_TOKEN
npx vercel env add SEGA_GUILD_ID
npx vercel env add JWT_SECRET        # use a long random string
npx vercel --prod                    # redeploy so env vars take effect
```

Generate a `JWT_SECRET`, e.g.: `node -e "console.log(require('crypto').randomBytes(48).toString('base64'))"`

## 3. Point the launcher at your real values

Edit `launcher/SegaLauncher/AppConfig.cs`:
- `DiscordClientId` → your real Client ID
- `VercelBaseUrl`   → your real production domain (if not `https://sega-roads.vercel.app`)

If your Vercel domain differs, the registered redirect URI (step 3 above) stays the
same — it's a loopback address, unrelated to the Vercel domain. But the `REDIRECT_URI`
constant in `vercel-app/api/verify.ts` must match the launcher's exactly (both are
`http://127.0.0.1:51789/callback` by default — only change if you change the port).

Then build the launcher:
```powershell
cd "C:\Users\jazzy\Downloads\slow roads\launcher\SegaLauncher"
dotnet build -c Release
# or run it directly:
dotnet run
```

## 4. End-to-end test (Task 14)

- [ ] **Member:** run launcher → Verify → authorize with an account in SEGA+ → game loads + watermark plays.
- [ ] **Non-member:** same with an account NOT in SEGA+ → "Members only", no game.
- [ ] **Direct URL:** open the prod game URL in a fresh browser (no cookie) → `/denied`.
- [ ] **Expiry:** after 6 h (or temporarily set session exp low in `api/unlock.ts`) → `/denied`, must relaunch.

## What's already done (on branch `sega-launcher`)

- Vercel backend: `api/verify.ts`, `api/unlock.ts` (POST), `middleware.ts` gate, `denied.html`, `unlock.html`, `lib/tokens.ts` + `lib/discord.ts` — **6 unit tests passing**, typechecks clean.
- Game hosted in `vercel-app/public/` with the fading **SKIDDED BY JUSTONEONTOP / SEGA+ ON TOP** watermark (obfuscated glue); typed-password gate removed.
- C# WPF launcher in `launcher/SegaLauncher/` — **PKCE tests passing**, builds clean (net10).
- Security: unlock token travels in the URL fragment + POST (never in logs/Referer).

To merge when you're happy: `git checkout main && git merge sega-launcher`.
