# SEGA+ Discord-Gated Launcher — Design

**Date:** 2026-06-01
**Status:** Design — awaiting user review before implementation plan

## Purpose & context

The modded Slow Roads build is shared **privately** inside the SEGA+ Discord server.
Two goals:

1. **Gate it to SEGA+ members.** Only people in the SEGA+ Discord (an apply-only
   server) can reach and play the build.
2. **Make it look trustworthy, not sketchy.** Members have been asking "is this a
   virus?" — the launcher should *reassure*, not alarm.

A fading on-load watermark — **"SKIDDED BY JUSTONEONTOP" / "SEGA+ ON TOP"** — is a
scene tag/credit, **not** an ownership claim. Full credit for the game goes to
slowroads.io (Anslo). This project is private and not publicly advertised.

> **Note on the source material:** the build embeds slowroads.io's copyrighted
> bundle. Hosting it (even behind a member gate) is the user's accepted call;
> distribution stays private and credited.

## Non-goals (YAGNI)

- No role-based tiers — membership in SEGA+ is the only requirement (it's apply-only).
- No accounts/passwords of our own — Discord identity *is* the login.
- No anti-debug / packing / heavy obfuscation on the launcher (see AV decision).
- No DRM fantasy: we accept that a *verified member* could still copy assets once
  served. We protect against *unverified* people getting the files at all.

## Key decisions (resolved during brainstorming)

| Decision | Choice |
|---|---|
| Backend host | **Vercel** (serverless `/api` + Edge Middleware + static game files) |
| Discord check | App gets user identity via OAuth (`identify`); **bot** in SEGA+ authoritatively confirms membership server-side |
| Where the game lives | **Hosted on Vercel, gated server-side** — unverified clients never receive game files |
| Unlock mechanism | Short-lived signed **unlock token** → exchanged for an HttpOnly **session cookie** → Edge Middleware gates every asset |
| Desktop OAuth handoff | **Loopback + PKCE** (no client secret in the app) |
| Verify rule | Member of SEGA+ only |
| Launcher obfuscation | **Clean / unpacked** (minimize AV false positives — the whole point is "it's safe") |
| Game-JS obfuscation | Obfuscate **our added glue** (`javascript-obfuscator`); leave/lightly-touch the third-party SvelteKit chunks (aggressive obfuscation can break them) |
| Watermark | Fading splash on load-in; lives in the gated game's `index.html` |
| Old typed-password gate | **Removed** — the app + cookie replaces it |

## Architecture

```
┌─────────────────────┐     1. PKCE authorize  ┌──────────────────────┐
│  SEGA+ Launcher     │  ───────────────────►  │  Discord OAuth        │
│  (C# / WPF .exe)    │  ◄───────────────────  │  user authorizes      │
│  clean / unpacked   │     2. code (loopback) └──────────────────────┘
└─────────┬───────────┘
          │ 3. POST {code, code_verifier}
          ▼
┌──────────────────────────────────────────────┐  4. bot: member?  ┌──────────────┐
│  Vercel                                        │ ─────────────────►│ Discord REST │
│  • POST /api/verify  exchange code, get user,  │   GET guild member│ (Bot token)  │
│      ask bot, sign unlock token                │ ◄─────────────────└──────────────┘
│  • GET  /api/unlock  validate token,           │
│      set HttpOnly session cookie, redirect /   │
│  • middleware.ts     gate every game asset     │
│  • /denied           "launch via SEGA+ app"    │
│  • static: the game build (the dump)           │
└─────────┬──────────────────────────────────────┘
          │ 5. launcher opens default browser → /api/unlock?token=…
          ▼
┌─────────────────────┐
│  Browser: the game  │  ← watermark splash: SKIDDED BY JUSTONEONTOP / SEGA+ ON TOP
└─────────────────────┘
```

## Components

### 1. Discord application + bot (setup, mostly config)

- Create a Discord application (Developer Portal). Record **Client ID** + **Client Secret**.
- OAuth2 → Redirects: register the **exact** loopback URI the launcher uses, e.g.
  `http://127.0.0.1:51789/callback` (Discord matches redirect URIs exactly, including
  port + path, so the launcher binds this fixed port).
- Add a **Bot** to the app. Record the **Bot Token**. Invite the bot to SEGA+
  (scope `bot`, no special permissions needed — it only needs to *be a member* to
  read other members). Enable the **Server Members Intent** in the portal to avoid
  any permission ambiguity on the member-lookup endpoint.
- Record the **SEGA+ Guild ID**.

### 2. Vercel backend

**Runtime note:** use **`jose`** (Web-Crypto based) for JWT sign/verify so the same
code works in both serverless functions *and* Edge Middleware (Edge can't use
Node's `jsonwebtoken`).

**Env vars (Vercel project settings — never in the app or repo):**
`DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`, `DISCORD_BOT_TOKEN`,
`SEGA_GUILD_ID`, `JWT_SECRET`.

**`POST /api/verify`** — body `{ code, code_verifier }`:
1. Exchange code at `https://discord.com/api/oauth2/token`
   (`grant_type=authorization_code`, client id/secret, `redirect_uri`, `code_verifier`).
2. `GET /users/@me` (Bearer user token) → `user.id`, username, avatar.
3. **Bot membership check:** `GET /guilds/{SEGA_GUILD_ID}/members/{user.id}`
   with `Authorization: Bot {DISCORD_BOT_TOKEN}`. `200` = member, `404` = not.
4. If member → sign **unlock token** (JWT: `{ sub: userId, name, scope:"unlock" }`,
   **exp 5 min**) and return `{ ok:true, token, user }`.
5. If not → `{ ok:false, reason:"not_member" }`. Surface Discord/config errors
   distinctly (`reason:"discord_error"`).

**`GET /api/unlock?token=…`**:
1. Verify the unlock JWT (signature, `scope:"unlock"`, not expired).
2. On success → mint a **session JWT** (`scope:"session"`, **exp 6 h**) and set
   `Set-Cookie: sega_session=<jwt>; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=21600`,
   then `302` → `/`.
3. On failure → `302` → `/denied`.

**`middleware.ts` (Edge Middleware)** — `matcher` covers everything **except**
`/api/*`, `/denied`, and the handful of static assets the denied page needs:
- Read `sega_session` cookie → verify JWT (signature + exp).
- Valid → `next()` (serve the game asset).
- Invalid/absent → rewrite to `/denied`.

**`/denied`** — small branded static page: "You need to launch via the **SEGA+
Launcher**, and be a member of SEGA+." Links: how to get the launcher / how to apply.

**Static game files** — the build (currently `modded/`) deployed as Vercel static
output. Gated by middleware. The watermark splash lives in its `index.html`
(carried over from the earlier work; typed-password gate removed).

### 3. C# / WPF launcher ("SEGA+ Launcher")

- **UI:** single branded window — title/logo, status line, **"Verify with Discord"**
  button, small "Full credit: slowroads.io" footer, optional VirusTotal link.
- **Verify flow (on click):**
  1. Generate PKCE `code_verifier` (random) + `code_challenge = base64url(SHA256(verifier))`,
     and a random `state`.
  2. Start a one-shot `HttpListener` on `http://127.0.0.1:51789/callback/`.
  3. Open the **system browser** to Discord authorize:
     `…/oauth2/authorize?client_id=…&response_type=code&redirect_uri=http://127.0.0.1:51789/callback&scope=identify&state=…&code_challenge=…&code_challenge_method=S256`.
  4. Catch the redirect, validate `state`, read `code`; respond to the browser with
     a tiny "Verified — you can close this tab and return to the launcher" page.
  5. `POST` `{ code, code_verifier }` to `https://<project>.vercel.app/api/verify`.
  6. **ok=true** → open default browser to
     `https://<project>.vercel.app/api/unlock?token=<token>` (cookie set → game loads);
     launcher shows "Verified! Launching…".
  7. **not_member** → show "You must be a SEGA+ member — apply at <link>."
- **Stores nothing sensitive** (no tokens persisted). The embedded config is only the
  public Vercel base URL + Discord Client ID + the fixed port.
- **Clean build:** no packer/anti-debug. Optional symbol-rename only if desired later.

### 4. Obfuscation

- **Launcher:** none (AV-friendly). Ship as a normal Release build.
- **Our added JS** (watermark loader, the `/denied` page glue, any unlock helper):
  `javascript-obfuscator`, since it's tiny and breakage is easy to catch.
- **Third-party SvelteKit chunks:** **do not** blind-obfuscate (already minified;
  high risk of breaking the game). If desired, a *tested, reversible* light pass only,
  validated by launching the game after.

### 5. Watermark splash (carried over)

Fading centered splash on load-in inside the gated game's `index.html`:
**SKIDDED BY JUSTONEONTOP** (large) / **SEGA+ ON TOP** (accent). Fades in, holds ~3s,
fades out, removes itself (`pointer-events:none` so it never blocks driving). The
earlier typed-password overlay is removed.

## Data flow (happy path)

1. User runs launcher → clicks Verify.
2. Browser → Discord → user authorizes → loopback receives `code`.
3. Launcher → `POST /api/verify` → Vercel exchanges code, identifies user, bot
   confirms SEGA+ membership → returns 5-min unlock token.
4. Launcher opens browser → `GET /api/unlock?token=…` → Vercel sets 6-h session
   cookie → redirect to `/`.
5. Edge Middleware sees valid cookie → serves the game. Watermark splash plays.

## Error handling

| Situation | Behavior |
|---|---|
| User cancels / closes Discord page | Loopback times out → launcher resets to "Cancelled". |
| Port 51789 busy (another instance) | Launcher: "Close other SEGA+ Launcher window and retry." |
| Can't reach Vercel | Launcher: "Can't reach the server — try again later." |
| Not a SEGA+ member (`404`) | Launcher: "Members only — apply at SEGA+." |
| Discord/bot misconfig (bot not in guild, bad token) | `/api/verify` → `discord_error`; launcher: "Server config issue, contact admin." |
| Discord rate limit (`429`) | Respect `Retry-After`; launcher shows "Busy, retrying…". |
| Session cookie expired (after 6h) | Middleware → `/denied` → "Re-open the launcher." |
| Direct hit to game URL w/o cookie | Middleware → `/denied`. |

## Testing strategy

- **JWT (jose):** unit tests — sign/verify round-trip, expiry rejection, wrong-secret
  rejection, scope mismatch.
- **`/api/verify`:** mock Discord token + `users/@me` + guild-member endpoints —
  cover member (200), non-member (404), token-exchange failure, bot error.
- **Middleware:** valid cookie → pass; missing/expired/tampered → `/denied`.
- **Launcher:** PKCE correctness (`challenge == base64url(SHA256(verifier))`);
  state-mismatch rejection. Manual e2e for the browser handoff.
- **End-to-end (manual):** member account unlocks and plays; non-member blocked;
  direct game URL without cookie → `/denied`; cookie expiry forces re-launch.

## Open items to confirm at plan time

- Final Vercel project domain (placeholder `https://<project>.vercel.app`).
- Exact loopback port (default `51789`).
- Session lifetime (default 6 h) and unlock-token lifetime (default 5 min).
- VirusTotal link: include in launcher UI? (recommended for the "is it safe" goal.)
