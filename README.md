# Slow Roads — Local Mirror & Modding Project

A complete capture of [slowroads.io](https://slowroads.io/) (SvelteKit build, May 2026)
running as a local site, plus a `modded/` variant with stronger driving aids,
plus tooling to re-dump, beautify, and continue modding.

---

## TL;DR — how to play

- **Normal (vanilla copy):** double-click `play_normal.bat` → opens
  http://localhost:8001/
- **Modded:** double-click `play_modded.bat` → opens http://localhost:8002/

Different ports, so you can run both simultaneously and compare.
Close the minimised server window (titled "slowroads NORMAL/MODDED — close to
stop") to stop the server.

Requires Python (any 3.10+). Fully offline — no internet needed once the dump
is captured.

---

## Folder layout

| Folder         | What it is                                                              |
|----------------|--------------------------------------------------------------------------|
| `dump/`        | Raw capture, the source of truth. Don't edit. Re-runnable via `dump_site.py`. |
| `normal/`      | Playable copy of `dump/slowroads.io/`, unmodified. Used as a clean baseline. |
| `modded/`      | Playable copy with patches (see "What's modded" below). Edit further here. |
| `pretty/`      | Beautified copy of every `.js` chunk, **for reading only** (not served). Has `READ_ME_FIRST.md` index. |

`normal/` and `modded/` are independent — patching `modded/` won't affect `normal/`,
and you can always restore `modded/` by copying from `normal/`.

---

## Scripts

| File              | What it does                                                                |
|-------------------|-----------------------------------------------------------------------------|
| `dump_site.py`    | Drive a Chromium browser through a URL, save every asset to `dump/`. Stays open so you can drive around in the game and trigger lazy-loaded terrain/audio. Setup: `pip install playwright && playwright install chromium`. |
| `reader.py`       | Walk `dump/`, beautify every `.js` to `pretty/`, write `pretty/READ_ME_FIRST.md` with files ranked by reading priority. Setup: `pip install jsbeautifier`. |
| `play_normal.bat` | Serve `normal/` on http://localhost:8001/. |
| `play_modded.bat` | Serve `modded/` on http://localhost:8002/. |
| `play_wrapper.html` | A single-file CDN-loading wrapper (for hosting the dump on jsDelivr + GitHub). Has two `USER/REPO@main` placeholders to fill in. |

---

## What's modded (current state)

Ten value tweaks across two files. The mods are *value tweaks to existing settings*
— every one of these knobs was already in the game; we just (1) cranked the
defaults and (2) raised the UI clamps so you can push further.

| Setting | Vanilla | Modded | Where |
|---|---|---|---|
| `steerAssist` default | 0.8 | 1.0 | conifers.js L17669 (existing) |
| `gripFactor` default | 1.0 | 1.5 | conifers.js L17674 (existing) |
| `steerAssist` UI max | 1 | 2 | conifers.js L17725-17728 (existing) |
| `gripFactor` UI max | 2 | 3 | conifers.js L17749-17756 (existing) |
| `speedFactor` UI max | 2 | **10** | conifers.js L17746 (NEW v2) |
| `softBrakeForce` default | 0 | **0.3** | conifers.js L17672 (NEW v2) |
| `autodriveSpeedFactor` UI max | 1 | **2** | conifers.js L17772 (NEW v2) |
| Driftmas scene | localStorage-gated | **always unlocked** | DevMidlineGenerator.js (NEW v2) |
| Dev road style | hidden | **visible (`DEV`)** | DevMidlineGenerator.js (NEW v2) |
| Graphics `viewDistance` default | High | **Ultra** | conifers.js L17896 (NEW v2 — "realistic mode") |
| Graphics `detail` default | High | **Ultra** | conifers.js L17897 (NEW v2 — "realistic mode") |
| Graphics `renderScale` default | 100% (idx 2) | **150% (idx 3)** | conifers.js L17899 (NEW v2 — "realistic mode") |

To revert a single mod, copy that specific section back from the corresponding file in `normal/`.
To revert *everything*, delete `modded/` and re-copy `normal/`.

---

## Filenames lie — quick map of where things actually live

SvelteKit chunks are named after the *first symbol* that pulled them in, not
the most important content. Real contents (after beautification in `pretty/`):

| Chunk filename                          | What's actually in it                                              |
|-----------------------------------------|--------------------------------------------------------------------|
| `conifers.3f25b06c.js`                  | Big chunk: includes Three.js, the **Gameplay settings registry** (steerAssist/gripFactor/autodrive/etc.), and tree placement. Most user-facing mod knobs are here. |
| `DevMidlineGenerator.33e318ed.js`       | Road pathfinding algorithm (the heart of the game). Holds the `"World"` settings group + seed validation (no `?` or `!`, max 24 chars). |
| `DriftmasMidlineGenerator.95b0a73b.js`  | Winter/holiday variant of the road generator. Diff against `DevMidlineGenerator` for seasonal overrides. |
| `IsStaticRoute.ed7acde0.js`             | Road-gen config object at the top (`seed: 323, nodeSpacing: 10, feelDist: 4`...). Matches Anslo's Medium-post description of the algorithm. |
| `SettingsManager.b2309b24.js`           | 837 KB — broader settings infrastructure (UI, persistence). |
| `HillsHeightmap.7e1f2030.js`            | Procedural terrain (Perlin-noise-based heightmap). |
| `nodes/3.cdda88cc.js`                   | Main gameplay scene — calls into `H.setAutodrive`, `H.setGripFactor`, etc. Wires UI to systems. Also contains the anti-piracy nag ("Play the original ad-free on slowroads.io"). |

For a guided reading order with previews of each file, see
`pretty/READ_ME_FIRST.md`.

---

## How to add more mods (workflow)

1. **Find the setting.** `grep -ri "settingName"` in `pretty/`. Look for an entry shaped like:
   ```js
   settingName: {
       default: Ze.settingName,
       readable: "Display Name",
       type: fe.Range,
       min: 0, max: 1, step: .1
   }
   ```
2. **Find the default.** Look in the same file for a `Ze` (or similar) settings-defaults object — usually a few thousand lines above the registry. The string `steerAssist: .8` is on L17669, for example.
3. **Patch in `modded/`.** Edit the corresponding file under `modded/_app/immutable/chunks/`. If the file is still minified, beautify it first by copying from `pretty/` (as we did for `conifers.*.js`).
4. **Refresh the browser.** Hard refresh (`Ctrl+Shift+R`) on http://localhost:8002/. Python's `http.server` doesn't cache, so changes appear immediately.
5. **Sanity check.** If the game fails to load, check the browser console for syntax errors (you may have accidentally broken a `,` or `;`). If broken, copy that one file from `normal/`.

### Promising knobs to try next

- `softBrakeForce` (currently 0, max 1): one-pedal driving — release accel and the car gently brakes.
- `wheelRotation` (currently 900°, max 1080°): visual-only wheel turn angle.
- Inside `IsStaticRoute.*.js` (L66+): `maxTurnDelta`, `feelDist`, `feelAng` — change the *shape* of the generated road (sharper curves, wider lookahead).
- The `speedFactor` clamp at L17684 of conifers has a hidden internal max of `1e6` (a million). The UI caps at 2 but the engine allows extreme values; you could raise the visible `max: 2` for `speedFactor` and see what happens at speed 50× normal.

---

## Existing features that look like mods but ship with vanilla

Helpful to know so you don't waste time "implementing" them:

- **Drive For Me / Autodrive** — already exists. In-game settings → Vehicle → "Autodrive mode". Default `Full auto`.
- **Steer assist** — already exists. Vehicle → "Steer assist" (0–1 slider). Modded raises max to 2.
- **Seed / world picker** — already exists. Settings → World → seed text field. Validated to ≤24 chars, no `?` or `!`. Loads as `location.hash` so you can also share URLs like `slowroads.io/#my-seed`.
- **Speed factor**, **grip factor**, **one-pedal driving** — all present as Range sliders. Modded version raises clamps + bumps `gripFactor` default.

---

## Multiplayer (PlayFab + Photon)

The modded build now includes a ghost-cars + chat multiplayer layer built on
[PlayFab](https://playfab.com/) (identity, statistics) and
[Photon Realtime](https://www.photonengine.com/realtime) (room messaging).
Both have generous free tiers — no credit card needed to get started.

### One-time account setup

1. **PlayFab.** Sign up at https://developer.playfab.com. Create a Title. From Game Manager → Title settings, copy the 5-character **Title ID**.
2. **Photon.** Sign up at https://dashboard.photonengine.com. Create a **Realtime** application. Copy the **App ID** (UUID) and the **App Secret**.
3. **Link them in PlayFab.** PlayFab Game Manager → Add-ons → Photon. Paste your Photon AppId and Photon Secret. This sets up the trust handshake so Photon validates PlayFab session tickets server-side.
4. **Create the km statistic.** PlayFab Game Manager → Settings → Statistics → New statistic. Name: `total_km_driven`. Aggregation: Sum. Save.

### Configure the modded build

Edit `modded/_app/multiplayer/mp-config.js`:

```js
window.MP_CONFIG = {
  titleId: "XXXXX",                  // your PlayFab Title ID
  photonAppId: "...uuid...",          // your Photon App ID
  photonRegion: "us"                  // closest Photon region
};
```

### Playing

Launch `play_modded.bat`. The auth panel appears top-right:

- **Continue as Guest** — instant join, no signup, no km tracking, identity per-browser only.
- **Sign in** / **Create account** — email + password. km tracked persistently across devices.

Type a seed in Settings → World → Seed. **Anyone else who types the same seed joins your room** — the seed string IS the room name. Want a private room? Pick a seed nobody will guess.

Other players appear as colored circle markers (with name tag) overlaid where they are in the world. **No collision** — they're visual only.

Press **T** to open chat. Enter to send, Esc to cancel. Last 5 messages shown; fade after 10 seconds.

### Limits

- 10 players per room.
- 20Hz position updates (dead-reckoned between packets).
- 200-char messages, 1 per 2 seconds per sender.
- No voice, no friends list, no race timing, no anti-cheat. See `docs/superpowers/specs/2026-05-30-multiplayer-and-mods-design.md` for the v1 YAGNI list.

### Re-applying after a slowroads re-dump

The multiplayer files under `modded/_app/multiplayer/` (config, glue, SDKs) are independent of any chunk hash and survive re-dumps. Two patches must be re-applied:

1. The five script/link tags in `modded/index.html` (just before `</body>`).
2. The one-line hook injected into `modded/_app/immutable/nodes/3.<new-hash>.js` — find the method `updateLive(e,t){` in the vehicle controller class and insert at the start: `/*MODDED:MP-hook*/try{window.__SR_MP_tick&&window.__SR_MP_tick(H,Ae,e,t,Be)}catch(_){};`

The Photon SDK (`modded/_app/multiplayer/photon-loadbalancing.js`) is also patched — the last 2 lines (CommonJS `module.exports` + Node `require('ws')` WebSocket wrapper) are disabled so the SDK works as a classic browser `<script>`. If you re-download the SDK, re-apply those two comment-outs.

---

## Re-dumping (if slowroads.io updates)

The game ships new builds occasionally. To capture a newer version:

```powershell
cd "C:\Users\jazzy\Downloads\slow roads"
python dump_site.py https://slowroads.io/
```

This **overwrites** `dump/slowroads.io/`. After re-dumping you'll want to:

1. Run `python reader.py` to refresh the beautified `pretty/` tree.
2. Re-copy `dump/slowroads.io/*` into `normal/` and `modded/`.
3. Re-apply the four mod patches to `modded/_app/immutable/chunks/conifers.<NEW_HASH>.js`.
   The filename hash will change but the surrounding code is unlikely to. Grep for `steerAssist: .8` to find it.

---

## Anti-piracy nag

The original site shows a small "Play the original ad-free on slowroads.io"
overlay (text template in `nodes/3.cdda88cc.js` L3300). It seems to render
based on hostname check. Likely it'll appear when playing via localhost too.
If it gets annoying:

1. Grep for the string `"Play the original ad-free on"` in the beautified
   `nodes/3.*.js`.
2. Find the surrounding hostname check (`location.hostname === "slowroads.io"`
   or similar).
3. Either delete the conditional block or invert it.
4. Apply only to `modded/`, leave `normal/` clean.

We have not done this patch yet — try playing first to see if it actually
blocks gameplay or is just a banner you can dismiss.

---

## License & legal note

The dump contains slowroads.io's copyrighted JavaScript, art, audio, and 3D
models. This setup is fine for personal study and local play. Don't
redistribute the dump itself, and don't host the modded version publicly under
the slowroads.io name — Anslo built this solo and it's his work.

The wrapper (`play_wrapper.html`) we set up earlier points at a CDN-hosted
copy; same rule applies — keep it personal.
