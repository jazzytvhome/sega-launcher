# Slow Roads — Multiplayer + Wide-Easy Mods Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 6 value-patch mods to the modded slowroads.io copy AND add multiplayer (ghost cars + chat) via PlayFab + Photon with guest/registered identity and km tracking for registered users.

**Architecture:** Mods are in-place edits to existing minified chunks under `modded/_app/immutable/chunks/`. Multiplayer is a new set of client-side scripts under `modded/_app/multiplayer/`, loaded via `<script>` tags appended to `modded/index.html`, plus a small hook injected into the main Svelte scene (`modded/_app/immutable/nodes/3.cdda88cc.js`) that exposes the local car's state and a per-frame ghost-render callback. All cloud services are managed — no server to host.

**Tech Stack:** Vanilla JavaScript (ES2020+, no build step, no bundler). PlayFab Client SDK (vendored locally). Photon Realtime LoadBalancing JS SDK (vendored locally). Three.js (already present in the slowroads bundle, accessed through the exposed scene). Python `http.server` for local hosting (unchanged from existing setup).

---

## Pre-flight notes

**File structure after this plan:**

```
modded/
├── index.html                                         [MODIFIED — +5 lines]
└── _app/
    ├── immutable/
    │   ├── chunks/
    │   │   ├── conifers.3f25b06c.js                   [MODIFIED — mods 1,2,3,6]
    │   │   └── DevMidlineGenerator.33e318ed.js        [MODIFIED — mods 4,5]
    │   └── nodes/
    │       └── 3.cdda88cc.js                          [MODIFIED — multiplayer hook]
    └── multiplayer/                                   [NEW DIRECTORY]
        ├── playfab.min.js                             [NEW — vendored, ~150KB]
        ├── photon-loadbalancing.js                    [NEW — vendored, ~120KB]
        ├── mp-config.js                               [NEW — ~15 LOC]
        ├── mp-client.js                               [NEW — ~400 LOC]
        └── mp-ui.css                                  [NEW — ~80 LOC]
```

**Verification model.** The project is NOT a git repository and has no test framework. Each task ends with a **Browser checkpoint** that loads `http://localhost:8002/` and confirms the behavior visually. This replaces the standard "run tests + git commit" steps from the TDD pattern.

**Server.** Throughout the plan we assume `play_modded.bat` is running (which starts `python -m http.server 8002` in `modded/`). Python's `http.server` does not cache, so hard refresh (`Ctrl+Shift+R`) picks up file changes immediately.

**Browser DevTools convention.** Some verification steps say "open DevTools → Console". Use `F12` to open. The Console tab shows `console.log`/`console.error` from our scripts.

**Implementation order.** Part 1 (Tasks 1-6) is independent of Part 2 and can be done first — they are pure value patches with no shared state. Part 2 (Tasks 7-21) depends on no Part 1 task, but several Part 2 tasks depend on earlier Part 2 tasks (vendoring SDKs before writing code that calls them, etc.).

**Rollback.** Any single mod-task can be reverted by copying the corresponding line(s) from `normal/_app/immutable/chunks/<same-file>`. The multiplayer integration can be disabled by removing the script tags from `modded/index.html` — the new files in `modded/_app/multiplayer/` will be inert.

---

## Part 1 — Wide-Easy Mods

### Task 1: Raise `speedFactor` UI cap from 2 to 10

**Files:**
- Modify: `modded/_app/immutable/chunks/conifers.3f25b06c.js` (line ~17746)

The engine accepts `speedFactor` up to `1e6` (see L17684 where `Yi(e, Dt.vehicle[i].min, 1e6)` is the speedFactor-specific clamp). Only the UI was limiting it. Mod #1 raises the visible slider max to 10.

- [ ] **Step 1: Confirm the current line.** Read `modded/_app/immutable/chunks/conifers.3f25b06c.js` lines 17740-17750 and confirm the `speedFactor` block looks like:
```js
      speedFactor: {
        default: Ze.speedFactor,
        readable: "Speed factor",
        desc: "Applies a multiplier to the speed. Settings above 1 do not apply to the bike.",
        type: fe.Range,
        min: .2,
        max: 2,
        step: .1
      },
```

- [ ] **Step 2: Apply the edit.** Use Edit on the file with:

`old_string`:
```
      speedFactor: {
        default: Ze.speedFactor,
        readable: "Speed factor",
        desc: "Applies a multiplier to the speed. Settings above 1 do not apply to the bike.",
        type: fe.Range,
        min: .2,
        max: 2,
        step: .1
      },
```
`new_string`:
```
      speedFactor: {
        default: Ze.speedFactor,
        readable: "Speed factor",
        desc: "Applies a multiplier to the speed. Settings above 1 do not apply to the bike. [MODDED: max raised from 2 to 10]",
        type: fe.Range,
        min: .2,
        max: 10,
        step: .1
      },
```

- [ ] **Step 3: Browser checkpoint.** Hard refresh `http://localhost:8002/` (`Ctrl+Shift+R`). Open in-game Settings → Vehicle → "Speed factor". Slider should range up to 10. Description should end with `[MODDED: max raised from 2 to 10]`.

---

### Task 2: Raise `softBrakeForce` default from 0 to 0.3

**Files:**
- Modify: `modded/_app/immutable/chunks/conifers.3f25b06c.js` (line ~17672)

The `softBrakeForce` setting already exists ("One-pedal driving"). Vanilla default is 0 (off). Mod #2 sets default to 0.3 so the game ships with gentle auto-braking on accelerator release.

- [ ] **Step 1: Confirm the current line.** Read lines 17670-17675 of the same file. Expected:
```js
    autodriveMode: Hg.FULL,
    autodriveSpeedFactor: .8,
    softBrakeForce: 0,
    speedFactor: 1,
    gripFactor: 1.5,
```

- [ ] **Step 2: Apply the edit.**

`old_string`: `    softBrakeForce: 0,`
`new_string`: `    softBrakeForce: .3,`

(Use `replace_all: false` — there is only one occurrence in this defaults block.)

- [ ] **Step 3: Browser checkpoint.** Hard refresh. Open Settings → Vehicle → "One-pedal driving". Slider value should default to 0.3 on a fresh load. (To verify a fresh load: also clear localStorage via DevTools → Application → Local Storage → right-click localhost:8002 → Clear, then refresh.)

---

### Task 3: Raise `autodriveSpeedFactor` UI cap from 1 to 2

**Files:**
- Modify: `modded/_app/immutable/chunks/conifers.3f25b06c.js` (line ~17772)

Autodrive's speed-as-fraction-of-top-speed is capped at 1 in the UI. Mod #3 allows up to 2 — autodrive can exceed nominal top speed.

- [ ] **Step 1: Confirm the current line.** Read lines 17766-17774. Expected:
```js
      autodriveSpeedFactor: {
        default: Ze.autodriveSpeedFactor,
        readable: "Autodrive speed",
        desc: "How fast the autodrive should drive as a proportion of the vehicle's top speed",
        type: fe.Range,
        min: .1,
        max: 1,
        step: .1
      },
```

- [ ] **Step 2: Apply the edit.**

`old_string`:
```
      autodriveSpeedFactor: {
        default: Ze.autodriveSpeedFactor,
        readable: "Autodrive speed",
        desc: "How fast the autodrive should drive as a proportion of the vehicle's top speed",
        type: fe.Range,
        min: .1,
        max: 1,
        step: .1
      },
```
`new_string`:
```
      autodriveSpeedFactor: {
        default: Ze.autodriveSpeedFactor,
        readable: "Autodrive speed",
        desc: "How fast the autodrive should drive as a proportion of the vehicle's top speed. [MODDED: max raised from 1 to 2]",
        type: fe.Range,
        min: .1,
        max: 2,
        step: .1
      },
```

- [ ] **Step 3: Browser checkpoint.** Hard refresh. Settings → Vehicle → "Autodrive speed". Slider should range to 2.

---

### Task 4: Unlock Driftmas scene

**Files:**
- Modify: `modded/_app/immutable/chunks/DevMidlineGenerator.33e318ed.js` (line ~3666)

Driftmas (winter) is gated behind a localStorage flag. Mod #4 unlocks it unconditionally.

- [ ] **Step 1: Confirm the current line.** Read lines 3664-3668. Expected:
```js
  qa = new fe(!!localStorage.getItem("enableDriftmasScene")),
  iA = new fe(!0),
  Td = new fe(!1);
qa.subscribe(l => {
  l && localStorage.setItem("enableDriftmasScene", !0)
});
```

- [ ] **Step 2: Apply the edit.**

`old_string`: `  qa = new fe(!!localStorage.getItem("enableDriftmasScene")),`
`new_string`: `  qa = new fe(!0), /* MODDED: Driftmas always unlocked */`

- [ ] **Step 3: Browser checkpoint.** Hard refresh, clear localStorage as in Task 2 to simulate a fresh user. Settings → World → Scene. Driftmas should be selectable without any prior interaction.

---

### Task 5: Expose hidden "Dev" road style

**Files:**
- Modify: `modded/_app/immutable/chunks/DevMidlineGenerator.33e318ed.js` (line ~3689)

The `tn` enum (L3681-3687) has 5 road styles, but the label array `ha` only exposes 4. Index 4 is `Dev` — gated out of the UI. Mod #5 adds the label.

- [ ] **Step 1: Confirm the current line.** Read lines 3685-3693. Expected:
```js
  tn = {
    Straight: 0,
    Casual: 1,
    Normal: 2,
    Winding: 3,
    Dev: 4
  },
  Fd = tn.Dev,
  ha = ["STRAIGHT", "CASUAL", "NORMAL", "WINDING"],
```

- [ ] **Step 2: Apply the edit.**

`old_string`: `  ha = ["STRAIGHT", "CASUAL", "NORMAL", "WINDING"],`
`new_string`: `  ha = ["STRAIGHT", "CASUAL", "NORMAL", "WINDING", "DEV"], /* MODDED: expose Dev road style */`

- [ ] **Step 3: Browser checkpoint.** Hard refresh. Settings → World → Road style. A fifth option "DEV" should appear. Select it. The road should generate (the underlying generator already supports index 4). If the game errors, revert this single edit and report — likely the validator in the World registry (L3737) rejects values >= 4. In that case, an additional patch will be needed in that validator function (`l == "roadStyle"` branch — increase the modulo length or remove the bounds check). This contingency is acceptable to leave for follow-up since road style index 4 is documented in the README as "hidden" — it may not be release-quality.

---

### Task 6: Set Ultra graphics defaults ("realistic mode")

**Files:**
- Modify: `modded/_app/immutable/chunks/conifers.3f25b06c.js` (lines ~17895-17910)

Per the design: realistic mode = just graphics. Bake the Ultra tier into the defaults. User can still lower in Graphics settings if FPS suffers.

- [ ] **Step 1: Confirm the current lines.** Read lines 17893-17910. Expected:
```js
  kg = [.5, .75, 1, 1.5, 2],
  Gg = [-1, 30, 45, 60, 90, 120, 144, 160, 240],
  hn = {
    viewDistance: js || Ks.value ? hl.Low : hl.High,
    detail: js || Ks.value ? ul.Medium : ul.High,
    snowEffects: !0,
    renderScale: 2,
```

`js || Ks.value` is the touchscreen-detection branch (low defaults on mobile, high on desktop). We only raise the desktop side.

- [ ] **Step 2: Apply the edit.**

`old_string`:
```
  hn = {
    viewDistance: js || Ks.value ? hl.Low : hl.High,
    detail: js || Ks.value ? ul.Medium : ul.High,
    snowEffects: !0,
    renderScale: 2,
```
`new_string`:
```
  hn = {
    viewDistance: js || Ks.value ? hl.Low : hl.Ultra, /* MODDED: Ultra default for desktop */
    detail: js || Ks.value ? ul.Medium : ul.Ultra,   /* MODDED: Ultra default for desktop */
    snowEffects: !0,
    renderScale: 3, /* MODDED: 150% default for desktop (kg[3]) */
```

- [ ] **Step 3: Browser checkpoint.** Hard refresh, clear localStorage (fresh-user simulation). Settings → Graphics. View distance and Detail should default to "Ultra". Render scale should default to "150%". Drive briefly — grass should be visibly denser and extend further than vanilla. If FPS tanks badly on your machine, that's expected; the user can lower these.

---

**End of Part 1.** At this point the six wide-easy mods are live. If you stopped here you'd have a stable modded build. Part 2 begins multiplayer.

---

## Part 2 — Multiplayer Foundations

### Task 7: Create the multiplayer directory and placeholder config

**Files:**
- Create: `modded/_app/multiplayer/` (directory)
- Create: `modded/_app/multiplayer/mp-config.js`

- [ ] **Step 1: Create the directory.** Run (Windows PowerShell):
```
mkdir "C:\Users\jazzy\Downloads\slow roads\modded\_app\multiplayer"
```

- [ ] **Step 2: Create `mp-config.js`** with placeholder values:

```js
// === Slow Roads multiplayer config ===
// Fill in your own values after creating PlayFab + Photon accounts.
// See README.md "Multiplayer setup" section for walkthrough.

window.MP_CONFIG = {
  // PlayFab Title ID (5-char alphanumeric, from PlayFab Game Manager → Title settings)
  titleId: "PASTE_YOUR_PLAYFAB_TITLE_ID",

  // Photon Realtime App ID (UUID, from Photon dashboard → Applications → your Realtime app)
  photonAppId: "PASTE_YOUR_PHOTON_APP_ID",

  // Photon region code: "us", "usw", "eu", "asia", "jp", "au", "sa", "in", "ru", "rue", "cae", "kr", "tr", "za"
  photonRegion: "us"
};
```

- [ ] **Step 3: Browser checkpoint.** Hard refresh `http://localhost:8002/`. The file isn't loaded yet so nothing should change. Just verify the file exists at the expected path with the expected contents.

---

### Task 8: Vendor the PlayFab Client SDK

**Files:**
- Create: `modded/_app/multiplayer/playfab.min.js` (downloaded from PlayFab CDN)

- [ ] **Step 1: Download the SDK.** From a browser or PowerShell:
```
Invoke-WebRequest -Uri "https://download.playfab.com/PlayFabClientApi.js" -OutFile "C:\Users\jazzy\Downloads\slow roads\modded\_app\multiplayer\playfab.min.js"
```

If the download fails or returns HTML, alternative source: https://github.com/PlayFab/JavaScriptSDK (clone, then copy `PlayFabClientApi.js` from the `PlayFabSdk/` folder).

- [ ] **Step 2: Verify the file.** Check the file is >100KB and contains the string `PlayFabClientSDK` (run Grep on the file for that token; it should match many times). If the file is HTML or empty, the download failed — try the alternative source.

- [ ] **Step 3: Note the version.** Open the first ~20 lines of the file. There's usually a version banner like `// Generated SDK Version: 1.x.x`. Note the version in your head — if the API in later tasks doesn't match (very unlikely; PlayFab Client SDK API is highly stable), this version number is what to consult against PlayFab docs.

- [ ] **Step 4: Browser checkpoint.** None yet — file is unwired.

---

### Task 9: Vendor the Photon Realtime LoadBalancing JS SDK

**Files:**
- Create: `modded/_app/multiplayer/photon-loadbalancing.js`

The Photon JS SDK is distributed as a ZIP from the Photon dashboard or website. We need just the LoadBalancing build (not Chat or Voice).

- [ ] **Step 1: Download the SDK ZIP.** Open https://www.photonengine.com/sdks#realtime-javascript-sdk in a browser. Download the JavaScript SDK ZIP (no Photon account required for download).

- [ ] **Step 2: Extract.** Inside the ZIP find `lib/photon-loadbalancing.js` (or similar — folder layout varies by SDK version; look for the file named `photon-loadbalancing.js` or `Photon-Javascript_SDK.js` that's ~120KB).

- [ ] **Step 3: Copy into place.** Copy the extracted file to:
```
C:\Users\jazzy\Downloads\slow roads\modded\_app\multiplayer\photon-loadbalancing.js
```

- [ ] **Step 4: Verify the file.** Grep the file for `LoadBalancingClient` — should match dozens of times. Grep for `connectToRegionMaster` — should match (this is the entry-point function we'll call).

- [ ] **Step 5: Browser checkpoint.** None yet — file unwired.

---

### Task 10: Write `mp-ui.css`

**Files:**
- Create: `modded/_app/multiplayer/mp-ui.css`

Styles for the auth panel (top-right), chat overlay (bottom-left), ghost name tags (world-projected).

- [ ] **Step 1: Create the file** with this exact content:

```css
/* === Slow Roads multiplayer UI styles === */

/* Auth panel — top-right floating box */
#mp-auth-panel {
  position: fixed;
  top: 12px;
  right: 12px;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.6);
  color: #fff;
  font: 12px/1.4 system-ui, sans-serif;
  padding: 8px 10px;
  border-radius: 6px;
  display: flex;
  gap: 6px;
  align-items: center;
  pointer-events: auto;
}
#mp-auth-panel button {
  font: inherit;
  background: #2a7;
  color: #fff;
  border: 0;
  border-radius: 4px;
  padding: 4px 8px;
  cursor: pointer;
}
#mp-auth-panel button:hover { background: #3b8; }

/* Chat overlay — bottom-left */
#mp-chat-wrap {
  position: fixed;
  bottom: 12px;
  left: 12px;
  z-index: 9999;
  width: 280px;
  pointer-events: none;
}
#mp-chat-log {
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: 160px;
  overflow: hidden;
  margin-bottom: 4px;
}
.mp-chat-line {
  background: rgba(0, 0, 0, 0.55);
  color: #fff;
  font: 12px/1.4 system-ui, sans-serif;
  padding: 3px 7px;
  border-radius: 4px;
  transition: opacity 1.5s linear;
}
.mp-chat-faded { opacity: 0; }
#mp-chat-input {
  width: 100%;
  box-sizing: border-box;
  font: 12px/1.4 system-ui, sans-serif;
  padding: 4px 7px;
  background: rgba(0, 0, 0, 0.7);
  color: #fff;
  border: 1px solid #5a5;
  border-radius: 4px;
  pointer-events: auto;
}

/* Ghost car name tags — world-projected, position set per frame by JS */
.mp-ghost-tag {
  position: fixed;
  z-index: 9998;
  transform: translate(-50%, -100%);
  background: rgba(0, 0, 0, 0.5);
  color: #fff;
  font: 11px/1.2 system-ui, sans-serif;
  padding: 2px 5px;
  border-radius: 3px;
  pointer-events: none;
  white-space: nowrap;
}
```

- [ ] **Step 2: Browser checkpoint.** None yet — CSS unwired.

---

### Task 11: Inject hooks into `nodes/3.cdda88cc.js` + add script tags to `index.html`

**Files:**
- Modify: `modded/index.html`
- Modify: `modded/_app/immutable/nodes/3.cdda88cc.js`

This task has two parts: (a) load the multiplayer scripts/styles in `index.html`, (b) expose the local car's state and a ghost-render callback hook in `nodes/3.cdda88cc.js`.

#### Part (a) — `index.html`

- [ ] **Step 1: Read `modded/index.html`** to find the end of `<body>`. The file is small (~30 lines).

- [ ] **Step 2: Add the script tags.** Insert just before `</body>`:
```html
    <link rel="stylesheet" href="/_app/multiplayer/mp-ui.css">
    <script src="/_app/multiplayer/playfab.min.js"></script>
    <script src="/_app/multiplayer/photon-loadbalancing.js"></script>
    <script src="/_app/multiplayer/mp-config.js"></script>
    <script src="/_app/multiplayer/mp-client.js"></script>
```

Use Edit with `old_string: "</body>"` and `new_string` = the above five lines + `\n  </body>`. (If `</body>` appears multiple times, narrow `old_string` to include more context.)

- [ ] **Step 3: Browser checkpoint (HTML side).** Hard refresh. Open DevTools → Console. You should see an error like `Uncaught ReferenceError: ... not defined` or a 404 for `mp-client.js` — that's expected at this point because we haven't created `mp-client.js` yet (Task 13). The important thing is: `playfab.min.js`, `photon-loadbalancing.js`, `mp-config.js`, and `mp-ui.css` should load with HTTP 200 (visible in DevTools → Network tab). If any of those four returns 404, the path is wrong — recheck.

#### Part (b) — `nodes/3.cdda88cc.js`

The minified scene file is huge. We need to find where the per-frame render loop runs, and where the local car state is updated for the HUD. Strategy: grep for known anchors.

- [ ] **Step 4: Locate the render loop.** Grep `modded/_app/immutable/nodes/3.cdda88cc.js` for `requestAnimationFrame` — Three.js / Svelte render loops typically register a frame callback via this API. Note the line number of the first match.

- [ ] **Step 5: Locate the HUD speed update.** Grep the same file for the speed-string formatter we already saw: `Fg[ji.units]` (this is from `L_` in conifers.js, called by the HUD). Or grep for `setSpeed` or `setOdometer`. Note a line near where the per-frame HUD update appears to happen.

- [ ] **Step 6: Identify the car-state source.** Near the HUD update site, look at what object the speed/position/heading are read from. It will likely be a variable named something like `v`, `car`, `vehicle`, or a longer minified identifier. Note this variable name. Also note the variable holding the Three.js `Scene` and `Camera` (they will be used by the ghost render).

- [ ] **Step 7: Apply the hook.** Inject this code immediately after the per-frame HUD update site (use the Edit tool, with `old_string` containing enough surrounding context to be unique). The block to insert:

```js
;try{
  /* MODDED: multiplayer hook — expose car state for mp-client.js */
  if(typeof window!=="undefined"){
    /* Adjust `<CAR_VAR>` and `<SCENE_VAR>`/`<CAMERA_VAR>` to the actual minified names identified above. */
    var __c=<CAR_VAR>;
    if(__c){
      window.__slowRoadsMP_car={
        x: __c.position?.x ?? __c.pos?.x ?? 0,
        y: __c.position?.y ?? __c.pos?.y ?? 0,
        z: __c.position?.z ?? __c.pos?.z ?? 0,
        h: __c.heading ?? __c.rotation?.y ?? __c.rot?.y ?? 0,
        s: __c.steering ?? __c.steer ?? 0,
        v: __c.speed ?? __c.vel ?? 0,
        b: !!(__c.brake ?? __c.braking),
        totalMeters: __c.totalDistance ?? __c.odom ?? 0,
        THREE: (typeof THREE!=="undefined"?THREE:(window.THREE||null))
      };
    }
    if(typeof window.__slowRoadsMP_renderGhosts==="function"){
      try{ window.__slowRoadsMP_renderGhosts(<SCENE_VAR>, <CAMERA_VAR>, 1/60); }catch(e){ console.error("[MP] renderGhosts threw:",e); }
    }
  }
}catch(e){ console.error("[MP] hook threw:",e); }
```

**IMPORTANT:** Replace `<CAR_VAR>`, `<SCENE_VAR>`, `<CAMERA_VAR>` with the actual minified identifiers you found in Steps 5-6. If you cannot positively identify them on first pass, fall back to: inject just the `try{}catch{}` skeleton with `__c = null` and `<SCENE_VAR>=null, <CAMERA_VAR>=null` — that lets you verify the injection point works (no errors thrown), then iterate on identifier discovery in a follow-up. The point is to get the hook *running every frame* first, then refine what data it reads.

- [ ] **Step 8: Browser checkpoint.** Hard refresh. DevTools → Console: should see no error mentioning "MP" or "renderGhosts". DevTools → Console, type `window.__slowRoadsMP_car` and press Enter — should print an object with `x`, `y`, `z` keys (possibly with 0 values if you haven't started driving yet, or `null`s if identifier mapping is off — that's the signal to refine Step 7). Drive briefly, then re-evaluate `window.__slowRoadsMP_car` — `x`/`z` should change.

If the values are stuck at 0 or undefined, iterate on Step 7's variable mapping. Once `window.__slowRoadsMP_car` reflects real driving, this task is complete.

---

## Part 3 — Multiplayer Client (`mp-client.js`)

The remaining tasks build up `mp-client.js` incrementally — one logical section per task. Each task is additive: the file gets longer, not rewritten.

### Task 12: Create `mp-client.js` skeleton (auth + UI bootstrap)

**Files:**
- Create: `modded/_app/multiplayer/mp-client.js`

This task lands the file with: config check, IIFE wrapper, STATE object, auth panel rendering, PlayFab login functions (guest + email register + email sign-in + sign-out). No Photon yet. No ghost rendering. No chat. No stats.

- [ ] **Step 1: Create the file** with this exact content:

```js
// === Slow Roads multiplayer client ===
// Reads:  window.MP_CONFIG, window.PlayFabClientSDK, window.Photon, window.__slowRoadsMP_car
// Writes: window.__slowRoadsMP_renderGhosts (called by nodes/3 per frame)
//
// Loaded as a classic <script> after mp-config.js, playfab.min.js, photon-loadbalancing.js.

(function() {
  'use strict';

  // ---- Config check ---------------------------------------------------------
  const CFG = window.MP_CONFIG;
  if (!CFG) {
    console.error('[MP] mp-config.js did not load.');
    return;
  }

  // ---- State ----------------------------------------------------------------
  const STATE = {
    auth: 'none',                 // 'none' | 'guest' | 'registered'
    playerId: null,
    displayName: null,
    sessionTicket: null,
    photonClient: null,
    currentSeed: null,
    ghosts: new Map(),            // actorNr -> { x,y,z,h,s,v,b, lastUpdateMs, name, mesh, tag }
    chatLastSentMs: 0,
    lastTotalMeters: 0,
    lastStatUploadMs: 0,
  };

  // ---- Bootstrap ------------------------------------------------------------
  function init() {
    if (!CFG.titleId || CFG.titleId === 'PASTE_YOUR_PLAYFAB_TITLE_ID') {
      console.warn('[MP] mp-config.js not configured — multiplayer in stub mode.');
      renderAuthPanel(/*stub*/ true);
      return;
    }
    if (typeof PlayFabClientSDK === 'undefined') {
      console.error('[MP] PlayFabClientSDK not found — playfab.min.js failed to load.');
      return;
    }
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }

  // ---- Auth -----------------------------------------------------------------
  function loginAsGuest() {
    let id = localStorage.getItem('slow-roads-mp-guest-id');
    if (!id) {
      id = (crypto.randomUUID ? crypto.randomUUID() : 'g' + Date.now() + Math.random().toString(36).slice(2));
      localStorage.setItem('slow-roads-mp-guest-id', id);
    }
    PlayFabClientSDK.LoginWithCustomID({
      TitleId: CFG.titleId,
      CustomId: id,
      CreateAccount: true,
    }, function(result, error) {
      if (error) { console.error('[MP] guest login failed:', error); alert('Guest login failed: ' + (error.errorMessage || error)); return; }
      STATE.auth = 'guest';
      STATE.playerId = result.data.PlayFabId;
      STATE.sessionTicket = result.data.SessionTicket;
      STATE.displayName = 'Guest-' + id.replace(/-/g, '').slice(0, 4).toUpperCase();
      console.log('[MP] guest login OK, playfabId=' + STATE.playerId);
      renderAuthPanel(false);
    });
  }

  function signIn(email, password) {
    PlayFabClientSDK.LoginWithEmailAddress({
      TitleId: CFG.titleId,
      Email: email,
      Password: password,
      InfoRequestParameters: { GetPlayerProfile: true, ProfileConstraints: { ShowDisplayName: true } },
    }, function(result, error) {
      if (error) { alert('Sign in failed: ' + (error.errorMessage || error)); return; }
      STATE.auth = 'registered';
      STATE.playerId = result.data.PlayFabId;
      STATE.sessionTicket = result.data.SessionTicket;
      STATE.displayName =
        (result.data.InfoResultPayload &&
         result.data.InfoResultPayload.PlayerProfile &&
         result.data.InfoResultPayload.PlayerProfile.DisplayName) || 'Player';
      console.log('[MP] sign-in OK as ' + STATE.displayName);
      renderAuthPanel(false);
    });
  }

  function registerAccount(email, password, displayName) {
    PlayFabClientSDK.RegisterPlayFabUser({
      TitleId: CFG.titleId,
      Email: email,
      Password: password,
      DisplayName: displayName,
      RequireBothUsernameAndEmail: false,
    }, function(result, error) {
      if (error) { alert('Register failed: ' + (error.errorMessage || error)); return; }
      STATE.auth = 'registered';
      STATE.playerId = result.data.PlayFabId;
      STATE.sessionTicket = result.data.SessionTicket;
      STATE.displayName = displayName;
      console.log('[MP] register OK as ' + displayName);
      renderAuthPanel(false);
    });
  }

  function signOut() {
    STATE.auth = 'none';
    STATE.playerId = STATE.sessionTicket = STATE.displayName = null;
    renderAuthPanel(false);
    console.log('[MP] signed out');
  }

  // ---- UI: auth panel -------------------------------------------------------
  // All DOM constructed via createElement + textContent (no innerHTML) so any
  // user-controlled string (display name) is naturally text, never markup.
  function promptSignIn() {
    const email = prompt('Email:'); if (!email) return;
    const password = prompt('Password:'); if (!password) return;
    signIn(email, password);
  }

  function promptRegister() {
    const email = prompt('Email:'); if (!email) return;
    const password = prompt('Password (min 6 chars):'); if (!password) return;
    const name = prompt('Display name:'); if (!name) return;
    registerAccount(email, password, name);
  }

  function makeButton(id, label, handler) {
    const b = document.createElement('button');
    b.id = id;
    b.textContent = label;
    b.onclick = handler;
    return b;
  }

  function renderAuthPanel(stubMode) {
    let panel = document.getElementById('mp-auth-panel');
    if (!panel) {
      panel = document.createElement('div');
      panel.id = 'mp-auth-panel';
      document.body.appendChild(panel);
    }
    while (panel.firstChild) panel.removeChild(panel.firstChild);
    if (stubMode) {
      panel.textContent = 'MP not configured (edit mp-config.js)';
      return;
    }
    if (STATE.auth === 'none') {
      panel.appendChild(makeButton('mp-btn-guest', 'Continue as Guest', loginAsGuest));
      panel.appendChild(makeButton('mp-btn-signin', 'Sign in', promptSignIn));
      panel.appendChild(makeButton('mp-btn-register', 'Create account', promptRegister));
    } else if (STATE.auth === 'guest') {
      const label = document.createElement('span');
      label.textContent = 'Driving as ' + STATE.displayName;
      panel.appendChild(label);
      panel.appendChild(makeButton('mp-btn-signin', 'Sign in to track km', promptSignIn));
    } else {
      const label = document.createElement('span');
      label.appendChild(document.createTextNode(STATE.displayName + ' · '));
      const km = document.createElement('span');
      km.id = 'mp-km-display';
      km.textContent = '…';
      label.appendChild(km);
      panel.appendChild(label);
      panel.appendChild(makeButton('mp-btn-signout', 'Sign out', signOut));
    }
  }

  // ---- Boot -----------------------------------------------------------------
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  // ---- Exports for later tasks ----------------------------------------------
  // (Task 13 will attach Photon stuff. Task 14 will attach broadcast.
  //  Task 15 ghost render. Task 16 chat. Task 17 km stats.)

})();
```

- [ ] **Step 2: Browser checkpoint.** Hard refresh `http://localhost:8002/`. With `mp-config.js` still containing the placeholder `PASTE_YOUR_PLAYFAB_TITLE_ID`, the auth panel in the top-right corner should display: `MP not configured (edit mp-config.js)`. No console errors.

- [ ] **Step 3: Temporarily set a real Title ID for testing.** If the user has already provided a PlayFab Title ID, edit `mp-config.js` to use it. Reload. Buttons `[Continue as Guest]` `[Sign in]` `[Create account]` should now appear. Click `[Continue as Guest]` — within ~1s the panel should update to `Driving as Guest-XXXX`. If the user has NOT yet provided a Title ID, skip this step; Task 20 will revisit.

---

### Task 13: Add Photon room join (seed = room name)

**Files:**
- Modify: `modded/_app/multiplayer/mp-client.js`

Adds Photon connection after a successful PlayFab login, joins a room named after the current seed, and reconnects on seed change.

- [ ] **Step 1: Add Photon connect/disconnect functions.** Insert this block in `mp-client.js` just before the `// ---- UI: auth panel ----` section:

```js
  // ---- Room (Photon) --------------------------------------------------------
  function connectPhotonForCurrentSeed() {
    const seed = readCurrentSeed();
    if (!seed) {
      console.log('[MP] no seed yet, will retry');
      setTimeout(connectPhotonForCurrentSeed, 1000);
      return;
    }
    if (STATE.photonClient && STATE.currentSeed === seed) return;
    if (STATE.photonClient) {
      try { STATE.photonClient.disconnect(); } catch (_) {}
      STATE.photonClient = null;
    }
    STATE.currentSeed = seed;
    if (typeof Photon === 'undefined' || !Photon.LoadBalancing) {
      console.error('[MP] Photon SDK not loaded.');
      return;
    }
    const client = new Photon.LoadBalancing.LoadBalancingClient(
      Photon.ConnectionProtocol.Wss,
      CFG.photonAppId,
      '1.0'
    );
    client.myActor().setName(STATE.displayName);
    client.onError = function(errorCode, msg) {
      console.error('[MP] Photon error', errorCode, msg);
    };
    client.onStateChange = function(state) {
      const S = Photon.LoadBalancing.LoadBalancingClient.State;
      if (state === S.JoinedLobby) {
        console.log('[MP] joined lobby, joining room "' + seed + '"');
        client.joinRoom(seed, {
          createIfNotExists: true,
          createRoomOptions: { maxPlayers: 10, customGameProperties: { seed: seed } }
        });
      } else if (state === S.Joined) {
        console.log('[MP] joined room "' + seed + '"');
      }
    };
    client.onEvent = function(code, content, actorNr) {
      if (actorNr === client.myActor().actorNr) return;
      // Task 14 will dispatch position events (code=1).
      // Task 16 will dispatch chat events (code=2).
    };
    client.onActorJoin = function(actor) {
      console.log('[MP] actor joined:', actor.actorNr, actor.getName());
    };
    client.onActorLeave = function(actor) {
      console.log('[MP] actor left:', actor.actorNr);
      // Task 15 will remove ghost mesh here.
    };
    // PlayFab CustomAuth: Photon validates the PlayFab session ticket server-side.
    client.setCustomAuthentication(
      Photon.LoadBalancing.Constants.CustomAuthenticationType.Custom,
      'username=' + encodeURIComponent(STATE.playerId) + '&token=' + encodeURIComponent(STATE.sessionTicket)
    );
    client.connectToRegionMaster(CFG.photonRegion);
    STATE.photonClient = client;
  }

  function readCurrentSeed() {
    // Strategy 1: URL hash format is "#<location-code><road-style>-<seed>" or "#<location-code><road-style>-<seed>@<distance>"
    if (location.hash && location.hash.length > 3) {
      const m = location.hash.match(/^#?[A-Z]\d-(.+?)(?:@|$)/i);
      if (m && m[1]) return m[1];
    }
    // Strategy 2: query the in-page seed input (validated max length 24, validation in DevMidlineGenerator).
    const candidates = document.querySelectorAll('input[type="text"]');
    for (const el of candidates) {
      if (el.maxLength === 24 && el.value) return el.value;
    }
    return null;
  }
```

- [ ] **Step 2: Wire Photon connect into auth success paths.** Edit each of the three login callbacks (`loginAsGuest`, `signIn`, `registerAccount`) to call `connectPhotonForCurrentSeed();` immediately after `renderAuthPanel(false);`. Use Edit with `replace_all: false` for each, narrowing `old_string` to include enough surrounding context to disambiguate. For example, for `loginAsGuest`:

`old_string`:
```
      STATE.displayName = 'Guest-' + id.replace(/-/g, '').slice(0, 4).toUpperCase();
      console.log('[MP] guest login OK, playfabId=' + STATE.playerId);
      renderAuthPanel(false);
    });
```
`new_string`:
```
      STATE.displayName = 'Guest-' + id.replace(/-/g, '').slice(0, 4).toUpperCase();
      console.log('[MP] guest login OK, playfabId=' + STATE.playerId);
      renderAuthPanel(false);
      connectPhotonForCurrentSeed();
    });
```

Repeat with the same pattern for `signIn` and `registerAccount` (their `renderAuthPanel(false)` lines).

- [ ] **Step 3: Wire Photon disconnect into `signOut`.** Edit:

`old_string`:
```
  function signOut() {
    STATE.auth = 'none';
    STATE.playerId = STATE.sessionTicket = STATE.displayName = null;
    renderAuthPanel(false);
    console.log('[MP] signed out');
  }
```
`new_string`:
```
  function signOut() {
    if (STATE.photonClient) {
      try { STATE.photonClient.disconnect(); } catch (_) {}
      STATE.photonClient = null;
    }
    STATE.currentSeed = null;
    STATE.auth = 'none';
    STATE.playerId = STATE.sessionTicket = STATE.displayName = null;
    renderAuthPanel(false);
    console.log('[MP] signed out');
  }
```

- [ ] **Step 4: Browser checkpoint.** Requires real PlayFab Title ID + real Photon AppId in `mp-config.js`, AND Photon add-on linked in PlayFab dashboard (see Task 20). With those set:
  1. Hard refresh.
  2. Click `[Continue as Guest]`.
  3. Console should log: `[MP] guest login OK, playfabId=...`, then `[MP] joined lobby...`, then `[MP] joined room "..."`.
  4. If you see Photon errors about auth: the PlayFab-Photon link in the dashboard isn't set up — defer this checkpoint to after Task 20.

If credentials are still placeholders, Step 4 verification is deferred; verify only that no NEW console errors appear during page load (the existing "MP not configured" warning is expected).

---

### Task 14: Add 20Hz position broadcast

**Files:**
- Modify: `modded/_app/multiplayer/mp-client.js`

Broadcasts the local car state to the Photon room 20 times per second.

- [ ] **Step 1: Add the broadcast function and ghost-update receiver.** Insert this block in `mp-client.js` just before the `// ---- UI: auth panel ----` section (so it sits after the Room block from Task 13):

```js
  // ---- Position broadcast ---------------------------------------------------
  function broadcastPosition() {
    const c = window.__slowRoadsMP_car;
    if (!c || !STATE.photonClient || !STATE.photonClient.isInRoom || !STATE.photonClient.isInRoom()) return;
    STATE.photonClient.raiseEvent(1, {
      x: c.x, y: c.y, z: c.z,
      h: c.h, s: c.s, v: c.v,
      b: !!c.b
    });
  }

  function onPositionEvent(actorNr, payload, client) {
    let g = STATE.ghosts.get(actorNr);
    if (!g) {
      const actor = (client.myRoomActors && client.myRoomActors()) ? client.myRoomActors()[actorNr] : null;
      g = {
        name: actor ? actor.getName() : ('Player-' + actorNr),
        mesh: null, tag: null,
        lastUpdateMs: 0,
        x: 0, y: 0, z: 0, h: 0, s: 0, v: 0, b: false
      };
      STATE.ghosts.set(actorNr, g);
    }
    g.x = payload.x; g.y = payload.y; g.z = payload.z;
    g.h = payload.h; g.s = payload.s; g.v = payload.v; g.b = !!payload.b;
    g.lastUpdateMs = performance.now();
  }
```

- [ ] **Step 2: Wire `onPositionEvent` into the Photon event handler.** Edit the `client.onEvent = function(code, content, actorNr) {` block from Task 13:

`old_string`:
```
    client.onEvent = function(code, content, actorNr) {
      if (actorNr === client.myActor().actorNr) return;
      // Task 14 will dispatch position events (code=1).
      // Task 16 will dispatch chat events (code=2).
    };
```
`new_string`:
```
    client.onEvent = function(code, content, actorNr) {
      if (actorNr === client.myActor().actorNr) return;
      if (code === 1) onPositionEvent(actorNr, content, client);
      // Task 16 will dispatch chat events (code=2).
    };
```

- [ ] **Step 3: Start the broadcast interval at init.** Edit the `init()` function:

`old_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```
`new_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    setInterval(broadcastPosition, 50); // 20 Hz
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```

- [ ] **Step 4: Browser checkpoint.** Requires two browsers (use one normal + one incognito so they don't share localStorage).
  1. In Browser A, log in as Guest, type seed `mptest`, drive.
  2. In Browser B, log in as Guest, type seed `mptest`, drive.
  3. In each browser's DevTools Console, evaluate: `(function(){ const m=window.__SR_MP_DEBUG||{}; return STATE_dump_unavailable_in_v1; })()` — this won't work because STATE is closed over the IIFE. Instead, just verify by intuition: there should be NO console errors. Photon network panel (DevTools → Network → WS) should show a WebSocket connection with ongoing message traffic.

(Note: STATE is private to the IIFE by design. If a debugger view is wanted later, expose a `window.__SR_MP_DEBUG = STATE` line at the end of the IIFE — but only in dev.)

---

### Task 15: Add ghost car rendering

**Files:**
- Modify: `modded/_app/multiplayer/mp-client.js`

Renders received ghosts as semi-transparent box meshes with floating name tags. Per-frame callback wired into the slowroads render loop via the hook from Task 11.

- [ ] **Step 1: Add the renderGhosts function and helpers.** Insert this block in `mp-client.js` just before the `// ---- UI: auth panel ----` section:

```js
  // ---- Ghost car rendering --------------------------------------------------
  function renderGhosts(scene, camera, dt) {
    if (!scene || !camera || STATE.ghosts.size === 0) return;
    const THREE = (window.__slowRoadsMP_car && window.__slowRoadsMP_car.THREE) || window.THREE || null;
    if (!THREE) return; // can't render without THREE; tag positions also require it
    const now = performance.now();
    for (const [actorNr, g] of STATE.ghosts) {
      if (!g.mesh) {
        g.mesh = createGhostMesh(THREE, actorNr);
        if (g.mesh) scene.add(g.mesh);
        g.tag = createGhostTag(g.name);
      }
      if (!g.mesh) continue;
      // Dead-reckoning: extrapolate by velocity for smoothness; cap at 0.5s old.
      const ageS = Math.min((now - g.lastUpdateMs) / 1000, 0.5);
      const dx = Math.sin(g.h) * g.v * ageS;
      const dz = Math.cos(g.h) * g.v * ageS;
      g.mesh.position.set(g.x + dx, g.y, g.z + dz);
      g.mesh.rotation.y = g.h;
      updateGhostTagPosition(THREE, g.tag, g.mesh.position, camera);
    }
    // GC ghosts whose actor has gone silent for >10s (defensive — onActorLeave should also catch).
    for (const [actorNr, g] of Array.from(STATE.ghosts)) {
      if (now - g.lastUpdateMs > 10000) removeGhost(actorNr);
    }
  }

  function createGhostMesh(THREE, actorNr) {
    try {
      const geo = new THREE.BoxGeometry(1.7, 1.4, 4.2);
      const hue = ((actorNr * 137) % 360) / 360;
      const color = new THREE.Color().setHSL(hue, 0.7, 0.5);
      const mat = new THREE.MeshLambertMaterial({ color: color, transparent: true, opacity: 0.6 });
      const mesh = new THREE.Mesh(geo, mat);
      return mesh;
    } catch (e) {
      console.error('[MP] createGhostMesh failed:', e);
      return null;
    }
  }

  function createGhostTag(name) {
    const div = document.createElement('div');
    div.className = 'mp-ghost-tag';
    div.textContent = name;
    document.body.appendChild(div);
    return div;
  }

  function updateGhostTagPosition(THREE, tag, worldPos, camera) {
    if (!tag) return;
    const v = new THREE.Vector3(worldPos.x, worldPos.y + 2, worldPos.z);
    v.project(camera);
    const w = window.innerWidth, h = window.innerHeight;
    const sx = (v.x + 1) / 2 * w;
    const sy = (-v.y + 1) / 2 * h;
    const onScreen = v.z > -1 && v.z < 1 && sx > 0 && sx < w && sy > 0 && sy < h;
    tag.style.display = onScreen ? 'block' : 'none';
    if (onScreen) {
      tag.style.left = sx + 'px';
      tag.style.top = sy + 'px';
    }
  }

  function removeGhost(actorNr) {
    const g = STATE.ghosts.get(actorNr);
    if (!g) return;
    if (g.mesh && g.mesh.parent) g.mesh.parent.remove(g.mesh);
    if (g.tag && g.tag.remove) g.tag.remove();
    STATE.ghosts.delete(actorNr);
  }
```

- [ ] **Step 2: Wire `removeGhost` into the actor-leave handler.** Edit the `client.onActorLeave` from Task 13:

`old_string`:
```
    client.onActorLeave = function(actor) {
      console.log('[MP] actor left:', actor.actorNr);
      // Task 15 will remove ghost mesh here.
    };
```
`new_string`:
```
    client.onActorLeave = function(actor) {
      console.log('[MP] actor left:', actor.actorNr);
      removeGhost(actor.actorNr);
    };
```

- [ ] **Step 3: Export `renderGhosts` to the global hook.** Edit `init()`:

`old_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    setInterval(broadcastPosition, 50); // 20 Hz
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```
`new_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    setInterval(broadcastPosition, 50); // 20 Hz
    window.__slowRoadsMP_renderGhosts = renderGhosts;
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```

- [ ] **Step 4: Browser checkpoint.** Requires two browsers, real credentials, and the hook from Task 11 confirmed working.
  1. A and B both log in as Guest, type seed `mptest`, drive.
  2. A should see a semi-transparent colored box (~car-sized) wherever B currently is, with B's display name floating above. Same in reverse.
  3. If A sees no ghost at all, but Photon logs say A joined the room: probably the hook in Task 11 is not passing the right `scene`/`camera` variables. Re-check Step 7 of Task 11.
  4. If A sees a ghost but it's in the wrong place / not moving: the `__slowRoadsMP_car` fields aren't right (likely `position.x/y/z` mapping). Re-check.

---

### Task 16: Add chat UI + Photon event protocol

**Files:**
- Modify: `modded/_app/multiplayer/mp-client.js`

Adds T-to-type chat. Messages broadcast as Photon event code 2.

- [ ] **Step 1: Add chat helpers and key handler.** Insert this block in `mp-client.js` just before the `// ---- UI: auth panel ----` section:

```js
  // ---- Chat -----------------------------------------------------------------
  function ensureChatUi() {
    if (document.getElementById('mp-chat-log')) return;
    const wrap = document.createElement('div');
    wrap.id = 'mp-chat-wrap';
    const log = document.createElement('div');
    log.id = 'mp-chat-log';
    wrap.appendChild(log);
    const inp = document.createElement('input');
    inp.id = 'mp-chat-input';
    inp.type = 'text';
    inp.maxLength = 200;
    inp.placeholder = 'Press Enter to send, Esc to cancel';
    inp.style.display = 'none';
    wrap.appendChild(inp);
    document.body.appendChild(wrap);
    inp.addEventListener('keydown', function(e) {
      if (e.key === 'Enter') {
        e.preventDefault();
        sendChat(inp.value);
        inp.value = '';
        inp.style.display = 'none';
        inp.blur();
      } else if (e.key === 'Escape') {
        e.preventDefault();
        inp.value = '';
        inp.style.display = 'none';
        inp.blur();
      }
      // Stop propagation so the game's own keybindings don't react while typing.
      e.stopPropagation();
    });
  }

  function handleGlobalKey(e) {
    if (e.key === 't' || e.key === 'T') {
      const inp = document.getElementById('mp-chat-input');
      if (inp && document.activeElement !== inp) {
        e.preventDefault();
        inp.style.display = 'block';
        inp.focus();
      }
    }
  }

  function sendChat(msg) {
    if (!msg || !msg.trim()) return;
    msg = msg.trim().slice(0, 200);
    const now = Date.now();
    if (now - STATE.chatLastSentMs < 2000) { showChatMessage('SYSTEM', '(slow down)'); return; }
    if (!STATE.photonClient || !STATE.photonClient.isInRoom || !STATE.photonClient.isInRoom()) {
      showChatMessage('SYSTEM', '(not in a room)'); return;
    }
    STATE.photonClient.raiseEvent(2, { msg: msg, ts: now });
    STATE.chatLastSentMs = now;
    showChatMessage(STATE.displayName, msg);
  }

  function onChatEvent(actorNr, payload, client) {
    const actor = (client.myRoomActors && client.myRoomActors()) ? client.myRoomActors()[actorNr] : null;
    const name = actor ? actor.getName() : ('Player-' + actorNr);
    showChatMessage(name, String(payload && payload.msg || '').slice(0, 200));
  }

  function showChatMessage(who, msg) {
    ensureChatUi();
    const log = document.getElementById('mp-chat-log');
    if (!log) return;
    const line = document.createElement('div');
    line.className = 'mp-chat-line';
    line.textContent = who + ': ' + msg;
    log.appendChild(line);
    while (log.children.length > 5) log.removeChild(log.firstChild);
    setTimeout(function() { line.classList.add('mp-chat-faded'); }, 10000);
    setTimeout(function() { if (line.parentNode) line.parentNode.removeChild(line); }, 12000);
  }
```

- [ ] **Step 2: Wire chat dispatch into Photon event handler.** Edit:

`old_string`:
```
    client.onEvent = function(code, content, actorNr) {
      if (actorNr === client.myActor().actorNr) return;
      if (code === 1) onPositionEvent(actorNr, content, client);
      // Task 16 will dispatch chat events (code=2).
    };
```
`new_string`:
```
    client.onEvent = function(code, content, actorNr) {
      if (actorNr === client.myActor().actorNr) return;
      if (code === 1) onPositionEvent(actorNr, content, client);
      else if (code === 2) onChatEvent(actorNr, content, client);
    };
```

- [ ] **Step 3: Install global T key handler at init.** Edit:

`old_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    setInterval(broadcastPosition, 50); // 20 Hz
    window.__slowRoadsMP_renderGhosts = renderGhosts;
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```
`new_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    ensureChatUi();
    document.addEventListener('keydown', handleGlobalKey);
    setInterval(broadcastPosition, 50); // 20 Hz
    window.__slowRoadsMP_renderGhosts = renderGhosts;
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```

- [ ] **Step 4: Browser checkpoint.** Two browsers, real credentials.
  1. Both join seed `mptest`.
  2. Press **T** in A. The chat input at bottom-left should focus.
  3. Type "hello" and press Enter. A sees `Guest-XXXX: hello` in their chat log.
  4. B should see the same line appear within ~200ms.
  5. Press T again in A within 2 seconds, send another message. A sees `SYSTEM: (slow down)` and the message is NOT sent.

If pressing T doesn't focus the input, the game might be capturing the keystroke first. Check `e.stopPropagation()` is in place (Step 1) and that the game's own keybindings don't override it. If still problematic, try **/** or **;** as the chat key — pick one the game doesn't bind.

---

### Task 17: Add km stat tracking (registered users only)

**Files:**
- Modify: `modded/_app/multiplayer/mp-client.js`

For registered users, upload accumulated meters to PlayFab Player Statistic `total_km_driven` every 60 seconds. Fetch + display on login and every 5 minutes.

- [ ] **Step 1: Add the stat upload + fetch functions.** Insert this block in `mp-client.js` just before the `// ---- UI: auth panel ----` section:

```js
  // ---- km stat tracking (registered users only) ----------------------------
  function uploadStatsIfDue() {
    if (STATE.auth !== 'registered') return;
    if (Date.now() - STATE.lastStatUploadMs < 60000) return;
    const c = window.__slowRoadsMP_car;
    if (!c || typeof c.totalMeters !== 'number') return;
    const delta = c.totalMeters - STATE.lastTotalMeters;
    if (delta < 1) return;
    PlayFabClientSDK.UpdatePlayerStatistics({
      Statistics: [{ StatisticName: 'total_km_driven', Value: Math.floor(delta) }],
    }, function(result, error) {
      if (error) {
        console.warn('[MP] stat upload failed:', error.errorMessage || error);
        return;
      }
      STATE.lastTotalMeters = c.totalMeters;
      STATE.lastStatUploadMs = Date.now();
      fetchKmStat(); // refresh the display after a successful upload
    });
  }

  function fetchKmStat() {
    if (STATE.auth !== 'registered') return;
    PlayFabClientSDK.GetPlayerStatistics({
      StatisticNames: ['total_km_driven']
    }, function(result, error) {
      if (error) { console.warn('[MP] stat fetch failed:', error.errorMessage || error); return; }
      const stats = (result.data && result.data.Statistics) || [];
      const stat = stats.find(function(s) { return s.StatisticName === 'total_km_driven'; });
      const meters = stat ? stat.Value : 0;
      const km = (meters / 1000).toFixed(1);
      const el = document.getElementById('mp-km-display');
      if (el) el.textContent = km + ' km';
    });
  }
```

- [ ] **Step 2: Start the upload-check interval and seed the fetch on sign-in.** Edit `init()`:

`old_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    ensureChatUi();
    document.addEventListener('keydown', handleGlobalKey);
    setInterval(broadcastPosition, 50); // 20 Hz
    window.__slowRoadsMP_renderGhosts = renderGhosts;
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```
`new_string`:
```
    PlayFab.settings.titleId = CFG.titleId;
    renderAuthPanel(false);
    ensureChatUi();
    document.addEventListener('keydown', handleGlobalKey);
    setInterval(broadcastPosition, 50);          // 20 Hz
    setInterval(uploadStatsIfDue, 5000);         // check every 5s; uploads when 60s elapsed
    setInterval(function() { fetchKmStat(); }, 5 * 60 * 1000); // refresh display every 5 min
    window.__slowRoadsMP_renderGhosts = renderGhosts;
    console.log('[MP] init complete, titleId=' + CFG.titleId);
  }
```

- [ ] **Step 3: Trigger an initial fetch on sign-in.** Edit `signIn` and `registerAccount` to call `fetchKmStat();` after their `renderAuthPanel(false); connectPhotonForCurrentSeed();` lines.

For `signIn`:

`old_string`:
```
      console.log('[MP] sign-in OK as ' + STATE.displayName);
      renderAuthPanel(false);
      connectPhotonForCurrentSeed();
    });
```
`new_string`:
```
      console.log('[MP] sign-in OK as ' + STATE.displayName);
      renderAuthPanel(false);
      connectPhotonForCurrentSeed();
      fetchKmStat();
    });
```

For `registerAccount`:

`old_string`:
```
      console.log('[MP] register OK as ' + displayName);
      renderAuthPanel(false);
      connectPhotonForCurrentSeed();
    });
```
`new_string`:
```
      console.log('[MP] register OK as ' + displayName);
      renderAuthPanel(false);
      connectPhotonForCurrentSeed();
      fetchKmStat();
    });
```

- [ ] **Step 4: Reset lastTotalMeters on signOut.** Edit:

`old_string`:
```
  function signOut() {
    if (STATE.photonClient) {
      try { STATE.photonClient.disconnect(); } catch (_) {}
      STATE.photonClient = null;
    }
    STATE.currentSeed = null;
    STATE.auth = 'none';
    STATE.playerId = STATE.sessionTicket = STATE.displayName = null;
    renderAuthPanel(false);
    console.log('[MP] signed out');
  }
```
`new_string`:
```
  function signOut() {
    if (STATE.photonClient) {
      try { STATE.photonClient.disconnect(); } catch (_) {}
      STATE.photonClient = null;
    }
    STATE.currentSeed = null;
    STATE.auth = 'none';
    STATE.playerId = STATE.sessionTicket = STATE.displayName = null;
    STATE.lastTotalMeters = 0;
    STATE.lastStatUploadMs = 0;
    renderAuthPanel(false);
    console.log('[MP] signed out');
  }
```

- [ ] **Step 5: Browser checkpoint.** Requires a real PlayFab account (not guest).
  1. Hard refresh, click `[Create account]`, fill in email/password/display name (or `[Sign in]` if account exists).
  2. Auth panel should show `<DisplayName> · 0.0 km` (or whatever the existing total is).
  3. Drive for at least 60 seconds.
  4. Console should log a successful upload after ~60s — no error.
  5. The km display should refresh shortly after.
  6. Sign out, sign back in: km display should still reflect cumulative total (persisted in PlayFab).

If you see `Statistic name 'total_km_driven' not configured`, the PlayFab dashboard task in Task 20 has not been completed yet.

---

## Part 4 — Wire-Up & Test

### Task 18: Update `README.md` to document multiplayer + new mods

**Files:**
- Modify: `README.md`

Add a "Multiplayer setup" section. Extend the "What's modded" table.

- [ ] **Step 1: Update the "What's modded" table.** Edit `README.md`:

`old_string`:
```
| `steerAssist`       | 0.8             | **1.0**        | 1           | **2**      | Steering assist (auto-counter-steers under hard cornering). |
| `gripFactor`        | 1               | **1.5**        | 2           | **3**      | Lateral tyre grip multiplier. Higher = harder to slide off. |
| `autodriveMode`     | `FULL`          | unchanged      | n/a         | n/a        | "Full auto" = autodrive does both steering and speed. **Already on by default in vanilla.** |
| `autodriveSpeedFactor` | 0.8         | unchanged      | 1           | unchanged  | How fast the autodrive drives, as a fraction of top speed. |
| World seed          | 323             | unchanged      | n/a         | n/a        | Hardcoded in `IsStaticRoute.ed7acde0.js` line 67. The game also has an in-UI seed input under the "World" settings panel — change it there for "world generator" use. |
```
`new_string`:
```
| `steerAssist`       | 0.8             | **1.0**        | 1           | **2**      | Steering assist (auto-counter-steers under hard cornering). |
| `gripFactor`        | 1               | **1.5**        | 2           | **3**      | Lateral tyre grip multiplier. Higher = harder to slide off. |
| `autodriveMode`     | `FULL`          | unchanged      | n/a         | n/a        | "Full auto" = autodrive does both steering and speed. **Already on by default in vanilla.** |
| `autodriveSpeedFactor` | 0.8         | unchanged      | 1           | **2**      | How fast the autodrive drives, as a fraction of top speed. v2 raises cap. |
| `speedFactor`       | 1.0             | unchanged      | 2           | **10**     | Visible cap raised; engine internally allows up to 1e6. |
| `softBrakeForce`    | 0               | **0.3**        | 1           | unchanged  | Default one-pedal driving (gentle brake on accelerator release). |
| Graphics `viewDistance` | High        | **Ultra**      | Ultra       | unchanged  | "Realistic mode" — default to Ultra on desktop. |
| Graphics `detail`   | High            | **Ultra**      | Ultra       | unchanged  | "Realistic mode" — default to Ultra on desktop. Drives grass density. |
| Graphics `renderScale` | 100%         | **150%**       | 200%        | unchanged  | "Realistic mode" — supersample for sharper image. |
| Driftmas scene      | localStorage-gated | **always unlocked** | n/a    | n/a        | Patched in `DevMidlineGenerator.33e318ed.js` L3666. |
| Dev road style      | hidden          | **visible**    | n/a         | n/a        | Added "DEV" to the road-style label array (`ha`) in `DevMidlineGenerator.33e318ed.js` L3689. |
| World seed          | 323             | unchanged      | n/a         | n/a        | Hardcoded in `IsStaticRoute.ed7acde0.js` line 67. The game also has an in-UI seed input under the "World" settings panel — change it there for "world generator" use. |
```

- [ ] **Step 2: Replace the "Multiplayer — the honest assessment (deferred)" section** with a real "Multiplayer setup" section. Edit:

`old_string`: the full block from `## Multiplayer — the honest assessment (deferred)` through `Doable but it's a real project - not a one-shot script. Pick this up in a fresh session with this README as context.` (copy the entire current section as your `old_string`).

`new_string`:
```
## Multiplayer setup

The modded build includes a ghost-cars + chat multiplayer layer built on
[PlayFab](https://playfab.com/) (identity, statistics) and [Photon Realtime](https://www.photonengine.com/realtime)
(room messaging). Both services are free for small-scale personal use.

### One-time account setup

1. **PlayFab.** Sign up at https://developer.playfab.com (free, no card). Create a Title. From Game Manager → Title settings, copy the 5-character **Title ID**.
2. **Photon.** Sign up at https://dashboard.photonengine.com (free up to 20 concurrent users, no card). Create a **Realtime** application. Copy the **App ID** (a UUID).
3. **Link them in PlayFab.** PlayFab Game Manager → Add-ons → Photon. Paste your Photon AppId and your Photon **Secret** (from Photon dashboard → app → "Secret"). This sets up the trust relationship so Photon will validate PlayFab session tickets.
4. **Create the km statistic.** PlayFab Game Manager → Settings → Statistics → New statistic. Name: `total_km_driven`. Aggregation: Sum. Save.

### Configure the modded build

Edit `modded/_app/multiplayer/mp-config.js`:

```js
window.MP_CONFIG = {
  titleId: "XXXXX",                  // your PlayFab Title ID
  photonAppId: "...uuid...",          // your Photon App ID
  photonRegion: "us"                  // closest Photon region: us, usw, eu, asia, jp, au, sa, in, ru, rue, cae, kr, tr, za
};
```

### Playing

Launch `play_modded.bat`. The auth panel appears in the top-right corner:

- **Continue as Guest** — instant join, no signup, no km tracking, identity is per-browser only.
- **Sign in** / **Create account** — email + password. km tracked persistently across devices.

Type a seed in Settings → World → Seed. **Anyone else who types the same seed joins your room** — the seed string IS the room name. Want a private room? Pick a seed nobody will guess.

Other players appear as colored, semi-transparent box cars with their display name floating above. **There is no collision** — ghosts pass through each other (intentional — this is a relaxing driving game, not a derby).

Press **T** to open chat. Enter to send, Esc to cancel. Last 5 messages shown, fade after 10 seconds.

### Limits

- 10 players per room (room is full at 10).
- 20Hz position updates (smooth via dead-reckoning between packets).
- 200-char messages, 1 per 2 seconds per sender.
- No collision, no voice, no friends list, no race timing — see `docs/superpowers/specs/2026-05-30-multiplayer-and-mods-design.md` Section 2.10 for the full v1 YAGNI list.

### Re-applying after a slowroads re-dump

If the upstream slowroads.io updates and you re-dump, the chunk filenames will re-hash. The multiplayer files under `modded/_app/multiplayer/` are independent and survive. The two patches that must be re-applied:

1. The script tags in `modded/index.html` (re-add them just before `</body>`).
2. The hook in `modded/_app/immutable/nodes/3.<new-hash>.js` (find the per-frame render section as documented in `docs/superpowers/plans/2026-05-30-multiplayer-and-mods.md` Task 11).
```

- [ ] **Step 3: Browser checkpoint (sanity).** Open the README in a markdown viewer or rendered preview. Verify the tables and code blocks render correctly. No actual gameplay change in this task.

---

### Task 19: Collect user credentials; populate `mp-config.js`

**Files:**
- Modify: `modded/_app/multiplayer/mp-config.js`

This is a coordination task with the user. The implementation agent does not need to "code" anything — just guide the user through what they need to provide, then paste their values into the config file.

- [ ] **Step 1: Ask the user for these values in a single message:**
  1. PlayFab Title ID (5 chars, e.g. `A1B2C`).
  2. Photon App ID (UUID, e.g. `12345678-1234-1234-1234-123456789abc`).
  3. Preferred Photon region (e.g. `us`).
  4. Confirmation that they completed the one-time dashboard setup from README "Multiplayer setup → One-time account setup", specifically: (a) created the `total_km_driven` statistic in PlayFab, (b) pasted Photon AppId + Secret into PlayFab Game Manager → Add-ons → Photon.

  **Do NOT ask for the PlayFab secret key.** It stays on their machine in the PlayFab dashboard only.

- [ ] **Step 2: Edit `mp-config.js`** to replace the three placeholder values with the user's actual values. Use Edit with the placeholder strings as `old_string`.

- [ ] **Step 3: Browser checkpoint.** Hard refresh. Auth panel should now show the three buttons (`[Continue as Guest]` etc.) instead of `MP not configured`. Console should log `[MP] init complete, titleId=<their ID>`. Click guest → should see successful PlayFab login + Photon room join in the console (Task 13 Step 4 verification, now actually runnable).

---

### Task 20: End-to-end test (two browsers, same seed)

**Files:** none modified

Final integration test exercising every feature.

- [ ] **Step 1: Open two browser windows** that don't share localStorage. Options: Firefox + Chrome, or Chrome + Chrome-incognito, or two different Chrome profiles. Both navigate to `http://localhost:8002/`.

- [ ] **Step 2: Browser A — guest.**
  - Click `[Continue as Guest]`.
  - Confirm panel says `Driving as Guest-XXXX`.
  - Open Settings → World → Seed. Enter `e2etest`. Apply.
  - Drive.

- [ ] **Step 3: Browser B — registered.**
  - Click `[Create account]`. Email: `e2etest@example.com`, password: `password123`, display name: `TestDriver`.
  - Confirm panel says `TestDriver · 0.0 km`.
  - Open Settings → World → Seed. Enter `e2etest`. Apply.
  - Drive.

- [ ] **Step 4: Verify ghost rendering (both directions).**
  - In Browser A, look around. A semi-transparent box car labeled `TestDriver` should be visible somewhere on the road, moving in real time.
  - In Browser B, look around. A semi-transparent box car labeled `Guest-XXXX` should be visible.
  - Each should follow the other within 1 second of input.

- [ ] **Step 5: Verify chat (both directions).**
  - In A, press T, type `hi from A`, Enter. A sees `Guest-XXXX: hi from A`.
  - B sees the same line within ~500ms.
  - In B, press T, type `hi from B`, Enter. Same in reverse.
  - Try sending a second message in A within 2s — expect `SYSTEM: (slow down)`.

- [ ] **Step 6: Verify km tracking.**
  - In B, drive continuously for at least 90 seconds (covering some real distance).
  - After 60s, the console should log a successful stat upload.
  - The km display in B's auth panel should update to a non-zero value.
  - Close B's browser, reopen, sign in again with the same credentials. The km value should be restored (persisted in PlayFab).

- [ ] **Step 7: Verify graceful degradation.**
  - In A, sign out. Photon disconnects (visible in console).
  - In B, the ghost car for A should disappear within ~10s (the `onActorLeave` handler kicks in immediately if A's disconnect is clean; otherwise the staleness GC catches it).

- [ ] **Step 8: Verify Part 1 mods still work** (multiplayer didn't break anything):
  - Settings → Vehicle → Speed factor goes to 10. ✓
  - Settings → Vehicle → One-pedal driving starts at 0.3. ✓
  - Settings → Vehicle → Autodrive speed goes to 2. ✓
  - Settings → World → Scene includes Driftmas. ✓
  - Settings → World → Road style includes DEV. ✓
  - Settings → Graphics defaults are Ultra/Ultra/150%. ✓

If all 8 steps pass, the plan is complete.

---

## Self-review checklist (run after writing this plan)

- [x] **Spec coverage.** Each section of `2026-05-30-multiplayer-and-mods-design.md` is implemented by a task:
  - Spec Part 1 mods 1-6 → Plan Tasks 1-6 ✓
  - Spec 2.1 architecture → Plan Tasks 7-17 implement the diagram ✓
  - Spec 2.2 files added/touched → Plan Tasks 7-11 create the listed files ✓
  - Spec 2.3 identity model → Plan Task 12 (auth) + Task 17 (km display in registered case) ✓
  - Spec 2.4 room model → Plan Task 13 ✓
  - Spec 2.5 real-time state sync → Plan Task 14 (broadcast) + Task 15 (ghost render) ✓
  - Spec 2.6 chat protocol → Plan Task 16 ✓
  - Spec 2.7 persistent km tracking → Plan Task 17 ✓
  - Spec 2.8 hook into nodes/3 → Plan Task 11 Part (b) ✓
  - Spec 2.9 user dashboard setup → Plan Task 19 + README in Task 18 ✓
  - Spec 2.10 YAGNI list → Explicitly carried into README in Task 18, no tasks for them (correct, that's the point) ✓
  - Spec Part 3 build sequence → Plan task ordering follows it ✓
  - Spec Part 3.2 E2E test → Plan Task 20 ✓
  - Spec Part 3.3 README updates → Plan Task 18 ✓

- [x] **Placeholder scan.** Searched for "TBD", "TODO", "implement later", "add appropriate" — none present. Each task has runnable code or concrete steps.

- [x] **Type consistency.** Method names used in later tasks match earlier tasks: `connectPhotonForCurrentSeed`, `readCurrentSeed`, `broadcastPosition`, `onPositionEvent`, `onChatEvent`, `renderGhosts`, `createGhostMesh`, `createGhostTag`, `updateGhostTagPosition`, `removeGhost`, `sendChat`, `showChatMessage`, `ensureChatUi`, `handleGlobalKey`, `uploadStatsIfDue`, `fetchKmStat`, `loginAsGuest`, `signIn`, `registerAccount`, `signOut`, `renderAuthPanel`, `promptSignIn`, `promptRegister`, `makeButton`, `init` — all defined exactly once, used consistently.

- [x] **No `innerHTML` with user-controlled data.** All DOM construction in `mp-client.js` uses `createElement` + `textContent` / `appendChild`. The chat log lines (Task 16's `showChatMessage`) already used `textContent`. The auth panel (Task 12) was rewritten away from `innerHTML` after a PostToolUse security hook flagged the original. No `escapeHtml` helper is needed because no user-controlled string is ever interpreted as markup.

- [x] **No reference to undefined symbols.** `PlayFabClientSDK`, `PlayFab.settings`, `Photon.LoadBalancing.LoadBalancingClient`, `Photon.ConnectionProtocol.Wss`, `Photon.LoadBalancing.Constants.CustomAuthenticationType.Custom` — all part of the vendored SDKs (Tasks 8, 9). `window.__slowRoadsMP_car`, `window.__slowRoadsMP_renderGhosts` — defined by the hook in Task 11 and assigned/consumed by `mp-client.js`. `window.MP_CONFIG` — defined in `mp-config.js` (Task 7).

- [x] **Implementation risks acknowledged where relevant.** Task 5 notes the road-style validator may need an additional patch. Task 11 Part (b) Steps 7-8 explicitly call out the identifier-mapping refinement loop. Task 16 Step 4 notes alternative chat-key choice if T is captured by game.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-30-multiplayer-and-mods.md`.

Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between tasks, fast iteration. Best for catching mistakes early in a long plan.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

Which approach?
