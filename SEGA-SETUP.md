# SEGA+ Launcher — finish-up checklist (encrypted distribution)

Everything is built and on the **`sega-launcher`** git branch. You distribute
**the launcher + `game.enc`**; the plaintext game and the AES key stay private.

> **The golden rule:** the `GAME_KEY` you put in Vercel **must be the exact same key**
> you build `game.enc` with. The launcher fetches that key from Vercel (only for SEGA+
> members) and uses it to decrypt the blob. Different keys → "couldn't decrypt".

---

## 1. Discord app + bot

1. <https://discord.com/developers/applications> → **New Application**.
2. **OAuth2** → copy **Client ID** + **Client Secret**.
3. **OAuth2 → Redirects** → add EXACTLY `http://127.0.0.1:51789/callback` → Save.
4. **Bot** → Add Bot → copy **Bot Token** → enable **Server Members Intent**.
5. Invite the bot to **SEGA+** (OAuth2 URL Generator, scope `bot`, no extra perms).
6. Developer Mode on → right-click SEGA+ icon → **Copy Server ID**.

## 2. Generate the game key

```powershell
cd "C:\Users\jazzy\Downloads\slow roads\vercel-app"
npm run keygen        # prints a base64 key — this is GAME_KEY. Save it.
```

## 3. Deploy backend + env vars (NO JWT_SECRET anymore)

Fill in your secrets once, then let the helper push them all and deploy:

```powershell
cd "C:\Users\jazzy\Downloads\slow roads\vercel-app"
copy .env.example .env          # then edit .env and paste in your 5 values
npx vercel login                # interactive — complete it in Google Chrome
.\deploy.ps1                     # pushes all env vars + deploys, scope: jazzytvhome
# (preview first with:  .\deploy.ps1 -DryRun )
```

`deploy.ps1` reads `.env`, pushes `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`,
`DISCORD_BOT_TOKEN`, `SEGA_GUILD_ID`, `GAME_KEY` to Vercel **production** under the
**jazzytvhome** scope, then deploys. Note the production URL it prints — that goes in
`AppConfig.cs` (step 5).

The only endpoint is `POST /api/verify`: it checks SEGA+ membership and, on success,
returns `GAME_KEY`. Nothing else is served — the game is NOT hosted.

> `.env` is gitignored — your real secrets never get committed. Only `.env.example`
> (the empty template) is in the repo.

## Shortcut: steps 4-6 in one command

Once your key exists and `AppConfig.cs` is set (step 5 below), you can do the whole
build+publish+zip in one go:

```powershell
cd "C:\Users\jazzy\Downloads\slow roads"
.\package.ps1 -Key "<your GAME_KEY>"
# -> SEGA-Plus-Launcher.zip  (launcher + game.enc + READ ME + source), ~15.6 MB, fits Discord
# add -SelfContained for a no-runtime-needed 125 MB exe (host it externally)
```

The manual steps below are still here if you want to run them individually.

## 4. Build the encrypted blob (with the SAME key)

```powershell
cd "C:\Users\jazzy\Downloads\slow roads\vercel-app"
$env:GAME_KEY = "<paste the same key from step 2>"
npm run obfuscate     # refresh the obfuscated watermark (optional but recommended)
npm run build:blob    # -> dist/game.enc  (~15.5 MB)
```

## 5. Point the launcher at your values + publish the .exe

Edit `launcher/SegaLauncher/AppConfig.cs`:
- `DiscordClientId` → your real Client ID
- `VercelBaseUrl`   → your real production domain (e.g. `https://your-project.vercel.app`)

Then publish a self-contained .exe (so users don't need .NET installed):

```powershell
cd "C:\Users\jazzy\Downloads\slow roads\launcher\SegaLauncher"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# output: bin\Release\net10.0-windows\win-x64\publish\SEGA+ Launcher.exe
```

(Smaller alternative if your users already have the .NET 10 runtime: drop
`--self-contained true` for a ~MB-sized framework-dependent build.)

## 6. Package for Discord

Put these two files together (folder or zip) and post in SEGA+:
- `SEGA+ Launcher.exe`   (from step 5)
- `game.enc`             (from step 4, `vercel-app/dist/game.enc`)

They must sit in the **same folder** — the launcher looks for `game.enc` next to itself.
Optionally also include the `launcher/` source and a **VirusTotal** link (the launcher
is unobfuscated specifically so scans come back clean).

## 7. Test (the part only you can do — needs real Discord)

- [ ] **Member:** run the .exe → Verify → authorize with an account in SEGA+ →
      "Decrypting…" → browser opens `http://127.0.0.1:<port>` → game plays + watermark.
- [ ] **Non-member:** same with an account NOT in SEGA+ → "Members only", no decrypt.
- [ ] **No blob:** move `game.enc` away → "game.enc not found next to the launcher".
- [ ] **Keep-open:** closing the launcher stops the local server (game stops loading new assets).

> **If a key ever leaks:** generate a new `GAME_KEY` (step 2), update the Vercel env
> var (step 3), rebuild `game.enc` (step 4), and re-post. Old blobs/keys stop working.

---

## What's already done (branch `sega-launcher`)

- **Backend** (`vercel-app/`): `POST /api/verify` returns the key on SEGA+ membership;
  `lib/discord.ts` (bot check). **3 unit tests pass**, tsc clean. Game is NOT hosted
  (plaintext lives in `vercel-app/game-src/`, excluded from deploys).
- **Blob pipeline** (`vercel-app/scripts/`): `build:blob` (AES-256-GCM + per-file deflate),
  `keygen`, `make:vector` (cross-language test fixture).
- **Launcher** (`launcher/SegaLauncher/`): Discord PKCE → key → `GameVault` (in-memory
  AES-GCM decrypt) → `LocalServer` (serves over localhost, plaintext never on disk).
  **5 unit tests pass** incl. a Node→C# crypto interop test. Builds clean (net10).
- **Honest limits:** a verified member can still dump the running game or leak the key;
  the gate stops *non-members getting the key*. Re-key on leak (above).

To merge when happy: `git checkout main && git merge sega-launcher`.
