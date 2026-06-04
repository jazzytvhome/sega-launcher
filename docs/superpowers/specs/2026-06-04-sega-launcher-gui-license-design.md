# SEGA+ Launcher — New GUI, Weekly License & Request-a-Source — Design

**Date:** 2026-06-04
**Status:** Draft for review
**Builds on:** `2026-06-01-sega-plus-discord-launcher-design.md` (the existing WPF launcher)

---

## 1. Goal

Three changes to the existing SEGA+ Launcher (WPF / .NET 10):

1. **New GUI** — a distinctive "scene / cracktro + 16-bit SEGA" skin, replacing the generic
   Material-cyan look. Same window, same code-behind wiring; the XAML and styles are rebuilt.
2. **Verify-once, weekly license** — verify with Discord at startup, then hold a **server-signed
   7-day license**; re-verify only after it expires. The AES game key still never persists.
3. **Request-a-Source** — members can upload a `.zip` build and request it be added. The file is
   stored in a separate GitHub repo and a link is posted to a staff Discord webhook (the webhook
   is never exposed to clients).

Visual reference: `docs/mockups/launcher-gui.html` (boot → verify → main, plus the request modal).

---

## 2. Aesthetic direction

- **Concept:** 90s warez-scene cracktro / NFO loader crossed with 16-bit SEGA. Justified by the
  project's own `SKIDDED BY JUSTONEONTOP / SEGA+ ON TOP` watermark.
- **Type:** Anton (heavy display) + VT323 (CRT terminal). Bundled as `.ttf` in the WPF app.
- **Color:** cream `#ece6d6` on near-black `#0a0b11`; SEGA electric-blue `#3a5bff`, hot-red
  `#ff3146`, amber `#ffc23a`, terminal-green `#57f5a3`.
- **No bloom.** Depth comes from hard flat offset shadows (`5px 5px 0 #000`) and CRT scanline +
  vignette texture — never colored glows.
- **Motion:** CRT power-on per screen, a terminal boot log that prints line-by-line with a spinner
  and an ASCII scan meter, a scrolling marquee credit, brutalist button press-displace.

---

## 3. Screen flow

```
launch
  -> UPDATE CHECK (existing Updater -> /api/version; auto-update if outdated)
  -> BOOT / TAMPER SPLASH  ("CHECKING FOR TAMPERING": exe re-hash vs /api/version sha256,
                            spinner + ASCII meter, plays sega+-on-top.mp3)
  -> LICENSE GATE
        valid local license? --no--> VERIFY (Discord OAuth -> /api/verify -> {key,dl,lic})
        |                                       -> save lic.tok (DPAPI, machine-bound)
        +--yes--------------------------------------------------+
                                                                v
  -> MAIN  (release list, hero card, license badge "N days left", Play/Install, Request-a-Source)
        Play/Install:
           valid license -> /api/unlock {lic, machine} -> {key, dl}  (no browser)
                            403 (expired/kicked/blacklisted) -> fall back to VERIFY, retry
           -> download game.enc (dl token) -> decrypt in memory -> Play (LocalServer) | Install
```

The boot splash plays `ttsMP3.com_VoiceText_2026-6-4_7-49-18.mp3` ("Sega+ on top") once at start.

---

## 4. Weekly license

### Token
`lic` is a minimal HS256 JWT signed server-side with `LICENSE_SECRET`:
```
payload = { uid, machine, iat, exp = iat + LICENSE_DAYS(7)*86400 }
lic     = base64url(payload) + "." + base64url(HMAC_SHA256(payload, LICENSE_SECRET))
```
- The launcher never holds `LICENSE_SECRET`, so it **cannot forge or extend** the token — editing
  `exp` breaks the signature and the server rejects it.
- `machine` = hash of Windows `MachineGuid` + username, so a copied `lic.tok` **won't work on
  another PC**.

### Storage
- File: `%LOCALAPPDATA%\SegaPlusLauncher\license.tok`, **DPAPI-encrypted at rest** (machine scope).
- The launcher reads `exp` only for display ("N days left") and to decide whether to re-verify;
  trust is always re-established server-side.

### Enforcement
- **Startup gate:** no token / expired / wrong machine → show VERIFY screen.
- **Per Play/Install:** `/api/unlock` validates signature + expiry + machine, then **re-checks
  membership and blacklist via the bot** (cheap, no browser). A kicked/blacklisted user is cut off
  on their **next Play**, not 7 days later. The license only removes the *browser OAuth*, not the
  per-Play server round-trip — so the encrypted blob stays as protected as today.

---

## 5. Request-a-Source

**Decision:** the zip uploads **directly to GitHub Releases** so it bypasses Vercel's ~4.5 MB body
limit. Cap = **500 MB** (GitHub allows up to 2 GB/asset; 500 MB is the enforced UI/server limit).
The file never passes through Vercel and the webhook only ever carries a link.

### Client (launcher) — three calls
1. `POST /api/request/init` `{ source, notes, lic, filename, size }` → gets
   `{ uploadUrl, token, releaseId, assetName }`.
2. `PUT <uploadUrl>` the raw zip bytes **directly to GitHub** with the short-lived `token`
   (`Content-Type: application/zip`). No Vercel involved → no body limit.
3. `POST /api/request/finalize` `{ releaseId, lic }` → server publishes + posts to webhook → `{ ok }`.

The launcher validates `.zip` and `size ≤ 500 MB` before step 1. Identity comes from `lic`, never
client-supplied fields.

### Server (Vercel)
- `api/request/init.js`:
  1. Validate `lic` (current member — **members-only**).
  2. Rate-limit: **1 request per user per 5 minutes** (429 otherwise).
  3. Reject non-`.zip` / `size > 500 MB`.
  4. Mint a **GitHub App installation token** scoped to **only the requests repo** with
     **`contents: write` only**, short TTL (~1 h), via the App private key in env.
  5. Create a **draft release** in the requests repo (tag `req-<uid>-<ts>`); return its asset
     `uploadUrl` + the scoped token + `releaseId`.
- `api/request/finalize.js`:
  1. Re-validate `lic`; confirm the release has the asset.
  2. **Publish** the release; read the asset's browser_download_url.
  3. POST to the staff Discord webhook (`STAFF_WEBHOOK_URL`, server-only): requester, source,
     notes, and the **GitHub link**. Return `{ ok }`.

### Security note (the one tradeoff)
A short-lived (~1 h), single-repo, `contents:write`-only token briefly lives in the **compiled,
obfuscated** launcher during the upload. That is far less exposed than a webhook URL in client code,
is scoped so it can do nothing but write to the requests repo, and expires on its own. The webhook
and the GitHub App private key stay **server-only**.

---

## 6. Components

### Backend (Vercel) — `vercel-app/`
| File | Change |
|------|--------|
| `lib/license.js` | **new** — `sign(payload)` / `verify(token)` HMAC helpers |
| `api/verify.js` | after membership check, also issue `lic`; return `{ ok, key, dl, lic }` |
| `api/unlock.js` | **new** — `{ lic, machine }` → validate + bot recheck → `{ key, dl }` / 403 |
| `api/request/init.js` | **new** — validate lic + rate-limit → mint scoped GH App token + draft release → `{uploadUrl, token, releaseId}` |
| `api/request/finalize.js` | **new** — publish release → post GitHub link to webhook |
| `lib/github.js` | **new** — GitHub App JWT → scoped installation token helper |
| env | **new:** `LICENSE_SECRET`, `LICENSE_DAYS=7`, `GH_APP_ID`, `GH_APP_PRIVATE_KEY`, `GH_APP_INSTALL_ID`, `GH_REQUESTS_REPO`, `STAFF_WEBHOOK_URL` |

### Launcher (C#) — `launcher/SegaLauncher/`
| File | Change |
|------|--------|
| `License.cs` | **new** — load/save `lic.tok` (DPAPI), read exp/days-left, machine fingerprint. No network. |
| `LicenseClient.cs` | **new** — POST `/api/unlock`; returns key+dl or an outcome |
| `RequestClient.cs` | **new** — 3-step: init → direct `PUT` zip to GitHub → finalize |
| `DiscordAuth.cs` | capture new `lic` field; otherwise unchanged |
| `Integrity.cs` | reuse for the boot tamper check (re-hash exe vs `/api/version` sha256) |
| `MainWindow.xaml` | rebuilt with the scene skin; reuse named elements; add `BootPanel`, `LicensePanel`, request modal |
| `MainWindow.xaml.cs` | add boot/license gate; rewire `DoFlowAsync` (unlock-vs-verify); request modal handlers |
| `App.xaml` | replace Material styles with the scene styles; merge bundled fonts |
| `SegaLauncher.csproj` | bundle `Anton.ttf`, `VT323.ttf`, and `sega+-on-top.mp3` as resources |
| `Obfuscar.xml` | keep XAML-referenced styles/types un-renamed (existing rules cover it) |

### Sound
`ttsMP3.com_VoiceText_2026-6-4_7-49-18.mp3` ("Sega+ on top") → bundled, played via WPF `MediaPlayer`
on boot. (HTML mockup needs a click to arm audio; the real app has no such restriction.)

---

## 7. Testing

- Keep the existing 28 tests passing.
- `License`: token parse, expiry math, days-left, machine fingerprint stability, DPAPI round-trip.
- Gate logic: valid license → unlock path; missing/expired/wrong-machine → verify path.
- `RequestClient`: rejects non-zip / oversize before sending; builds correct multipart.
- Server validation (`/api/unlock`, `/api/request`) is integration-level (manual/curl).

---

## 8. Decisions (all resolved)

1. **Zip size cap** — direct-to-GitHub-Releases upload, **500 MB** cap.
2. **Request access** — **members-only** (valid `lic` required) + **1 request / 5 min** rate-limit.
3. **Boot tamper check** — **real** exe-hash-vs-`/api/version` `sha256` check (via `Integrity.cs`),
   not just animation. Mismatch blocks launch / forces re-download.
4. **Splash length** — **~4 s**.

---

## 9. Out of scope (YAGNI)

- Large-file / chunked uploads, a request-status UI, license transfer between machines, offline
  play during the license week (each Play still needs the server for the key — by design).
