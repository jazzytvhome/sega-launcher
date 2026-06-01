# Slow Roads — v2 Design: Wide-Easy Mods + Multiplayer

**Date:** 2026-05-30
**Status:** Awaiting user approval before implementation planning.
**Scope:** Two parallel deliverables — (1) six low-effort value-patch mods, and (2) a multiplayer layer built on PlayFab + Photon (ghost cars + chat + guest/registered identity).

---

## Part 1 — Six Wide-Easy Mods

All six are pure value patches on top of the existing `modded/` build. They follow the same philosophy as the four mods already shipped: tweak existing knobs, raise UI clamps on existing sliders.

| # | Mod | File | Patch location | Change |
|---|---|---|---|---|
| 1 | `speedFactor` UI cap raise | `modded/_app/immutable/chunks/conifers.3f25b06c.js` | L17746 | `max: 2` → `max: 10`. (Engine internal cap is `1e6`, see L17684 — UI was the only thing clamping.) |
| 2 | `softBrakeForce` default raise | `modded/_app/immutable/chunks/conifers.3f25b06c.js` | L17672 | `softBrakeForce: 0` → `softBrakeForce: .3`. Default one-pedal driving. |
| 3 | `autodriveSpeedFactor` UI cap raise | `modded/_app/immutable/chunks/conifers.3f25b06c.js` | L17772 | `max: 1` → `max: 2`. Autodrive can exceed nominal top speed. |
| 4 | Unlock Driftmas scene | `modded/_app/immutable/chunks/DevMidlineGenerator.33e318ed.js` | L3666 | `new fe(!!localStorage.getItem("enableDriftmasScene"))` → `new fe(!0)`. Driftmas always available in the Scene picker. |
| 5 | Expose "Dev" road style | `modded/_app/immutable/chunks/DevMidlineGenerator.33e318ed.js` | L3689 | `ha = ["STRAIGHT","CASUAL","NORMAL","WINDING"]` → `ha = ["STRAIGHT","CASUAL","NORMAL","WINDING","DEV"]`. The Dev road style is already present in the `tn` enum (index 4) and is referenced as `Fd = tn.Dev` — exposing it just adds the label. |
| 6 | Realistic graphics defaults | `modded/_app/immutable/chunks/conifers.3f25b06c.js` | L17896-17899 | Change graphics defaults `hn`: `viewDistance: hl.High` → `hl.Ultra`, `detail: ul.High` → `ul.Ultra`, `renderScale: 2` → `3` (150%). User can still lower them in the Graphics panel if FPS suffers. |

**Rollback:** each line is independently revertable by copying the corresponding line from `normal/`. The `[MODDED: ...]` desc-string convention already used in the existing mods (e.g. L17725, L17752) will be extended where the change affects a slider's UI text.

**Verification:** hard refresh `http://localhost:8002/`. For each mod:
1. (#1) Open Vehicle → Speed factor; slider goes to 10.
2. (#2) Open Vehicle → One-pedal driving; slider starts at 0.3.
3. (#3) Open Vehicle → Autodrive speed; slider goes to 2.
4. (#4) Open World → Scene picker; Driftmas selectable without touching localStorage.
5. (#5) Open World → Road style; "DEV" appears as a fifth option.
6. (#6) Open Graphics; View distance and Detail default to Ultra, Render scale to 150%.

---

## Part 2 — Multiplayer (Ghost Cars + Chat)

### 2.1 Architecture

```
PlayFab (managed)              Photon Realtime (managed)
  ├─ Identity                    ├─ Room (keyed by seed string)
  ├─ Player profile               ├─ ~20 Hz position broadcast
  ├─ Statistics (km tracking)    └─ Chat events
  └─ Issues session ticket for
     Photon CustomAuth handshake

       ▲                              ▲
       │                              │
       └──────────────┬───────────────┘
                      │
              ┌───────┴───────┐
              │  modded/      │
              │  browser      │
              │  client       │
              │               │
              │  - mp-client  │  ← new glue file
              │  - hook in    │  ← small edit to nodes/3
              │    main scene │
              └───────────────┘
```

All cloud services are managed — no self-hosted server. Modded build only adds client-side code.

### 2.2 Files added or touched in `modded/`

| Path | Action | Purpose | Approx LOC |
|---|---|---|---|
| `modded/_app/multiplayer/playfab.min.js` | NEW (vendored) | PlayFab Client SDK, downloaded once and committed to repo | ~150 KB |
| `modded/_app/multiplayer/photon-loadbalancing.js` | NEW (vendored) | Photon Realtime JavaScript SDK, LoadBalancing client | ~120 KB |
| `modded/_app/multiplayer/mp-config.js` | NEW | Holds `{titleId, photonAppId, photonRegion}` — user fills in. Loaded before mp-client | ~15 |
| `modded/_app/multiplayer/mp-client.js` | NEW | All glue: auth flow, room join, position broadcast, ghost renderer, chat, km stat updates | ~400 |
| `modded/_app/multiplayer/mp-ui.css` | NEW | Chat overlay, ghost name tags, guest/sign-in panel styles | ~80 |
| `modded/index.html` | EDIT | Append four `<script>` and one `<link>` tag at end of `<body>` | +5 lines |
| `modded/_app/immutable/nodes/3.cdda88cc.js` | EDIT (minimal) | Add window-scope hook: expose own-car state read + ghost-render callback registration | +~10 lines |

Total new code (excluding vendored SDKs): ~510 LOC + a 15-line edit to the existing bundle.

### 2.3 Identity model — guest vs. registered

The main menu gains a small auth panel (top-right). Two paths, both ending in a PlayFab session ticket Photon can validate:

| Path | PlayFab call | Persistence | km tracked? |
|---|---|---|---|
| **Continue as Guest** | `LoginWithCustomID({ CustomId: localStorageUUID, CreateAccount: true })` | Per-browser. Clearing localStorage = new guest. | No |
| **Sign in / Create account** | `LoginWithEmailAddress` or `RegisterPlayFabUser` | Cross-device. PlayFab DisplayName settable. | Yes — via PlayFab Player Statistics |

The UUID for guest mode is generated with `crypto.randomUUID()` and stored as `localStorage["slow-roads-mp-guest-id"]`. Display name for guests is `Guest-XXXX` where XXXX is the first 4 chars of the UUID, uppercased.

Auth panel UI:
- Not logged in: `[Continue as Guest]  [Sign in]  [Create account]`
- Guest: `Driving as Guest-4F2A  [Sign in to track km]`
- Registered: `Driving as <DisplayName> · <X> km total  [Sign out]`

### 2.4 Room model

The existing seed text field IS the multiplayer room name. After PlayFab login succeeds and the seed is locked in (game loaded), `mp-client.js` calls `photonClient.connectToRegionMaster(region)` then `joinOrCreateRoom(seed)`.

Two players who type the same seed end up in the same procedural world AND the same Photon room — for free, no matchmaking. Want a private session? Pick a seed nobody will guess.

When the seed changes (user types a new seed and reloads world), the client leaves the current room and joins the new one.

### 2.5 Real-time state sync

**Outbound (per player → room) at 20 Hz** via `photonClient.raiseEvent(code=1, payload)`:

```js
{
  x: Number,    // world position X (meters)
  y: Number,    // world position Y (meters, elevation)
  z: Number,    // world position Z (meters)
  h: Number,    // heading (radians, 0 = north)
  s: Number,    // steering input (-1..1)
  v: Number,    // speed (m/s) — used by receivers for dead-reckoning
  b: Boolean    // brake lights on
}
```

~30 bytes serialized per packet. 20 Hz × ~10 players max realistic = 6 kbps per client per peer. Well within free-tier Photon limits.

**Inbound:** each `actorNr` (Photon player ID) is tracked in a local `Map<actorNr, GhostState>`. When a packet arrives, GhostState is updated. Each render frame, ghosts are interpolated/extrapolated based on `lastUpdateTime + v * dt` toward the last-known position (dead-reckoning) to smooth over 20Hz → 60fps rendering and packet loss.

**Ghost rendering:** ghost cars use the existing vehicle mesh from the slowroads bundle, instantiated as additional Three.js `Mesh` objects in the scene (NOT `InstancedMesh` — we want per-ghost material override for transparency + per-ghost name tag). Material gets `transparent: true, opacity: 0.6` and a slight color shift (per-player hash → hue) so multiple ghosts are distinguishable.

A floating HTML name tag (CSS-positioned via projected world coords) renders above each ghost, showing the player's display name and current speed.

When the room has only the local player, the ghost-render path is a no-op — zero visual cost when alone.

### 2.6 Chat protocol

Photon room events, separate code (`code=2`) so the high-frequency position handler doesn't have to inspect chat payloads:

```js
{ msg: String, ts: Number }   // ts = client-sent timestamp, for ordering
```

UI:
- Press **T** to focus chat input (bottom-left, 250px wide).
- Enter to send; Esc or click-outside to cancel.
- Last 5 messages displayed; each fades out after 10 seconds.
- Format: `<DisplayName>: <msg>` with a per-player color matching their ghost car.
- Client-side cooldown of 1 message per 2 seconds (UI shows "(slow down)" if exceeded).
- Max 200 chars per message, client-enforced; longer payloads dropped by receivers.

No moderation in v1.

### 2.7 Persistent km tracking (registered users only)

PlayFab Player Statistic `total_km_driven` (integer, in meters to avoid float drift on PlayFab's side).

`mp-client.js` keeps a running `metersSinceLastUpload` counter, incremented per frame from the slowroads odometer delta. Every 60 seconds (while signed in as a real user, not guest), calls:

```js
PlayFab.ClientApi.UpdatePlayerStatistics({
  Statistics: [{ StatisticName: "total_km_driven", Value: Math.floor(metersSinceLastUpload) }]
})
```

PlayFab's statistic update accumulates. After upload, the local counter resets.

Fetched once on login (`GetPlayerStatistics`) to display total in the auth panel; refreshed every 5 minutes thereafter.

Trust note: the client sends honest numbers. Anti-cheat via PlayFab CloudScript (server-side validation) is out of scope for v1.

### 2.8 Hook into `nodes/3.cdda88cc.js`

The slowroads main scene needs two tiny exposures for `mp-client.js` to interoperate:

1. **Own-car state read.** First step of the implementation is to locate the struct that the on-screen HUD reads each frame for `position`/`heading`/`speed`/`steeringAngle`/`brakeInput` — that struct (or its fields) gets assigned to `window.__slowRoadsMP_car` next to the existing HUD-update site. Grep target: the HUD's speed-display update call. If the relevant fields live in distinct objects rather than one struct, the hook becomes a per-frame `window.__slowRoadsMP_car = { x: a.x, y: a.y, z: a.z, h: b.heading, ... }` literal — cheap, no algorithmic dependency.
2. **Ghost render callback registration.** Inside the existing render/animate loop, after the local car is rendered, call `window.__slowRoadsMP_renderGhosts && window.__slowRoadsMP_renderGhosts(scene, camera, dt)` so the multiplayer module gets a chance to update ghost meshes.

Both hooks are designed to be no-ops when `mp-client.js` is not loaded, so the bundle keeps working standalone.

If a future slowroads update re-hashes the chunk filename, the patch needs to be reapplied (the surrounding code is unlikely to change). README will document the grep target.

### 2.9 What you (user) need to set up

To be requested *only after the code is ready to consume them*:

1. **PlayFab Title ID** (5-char alphanumeric — from PlayFab Game Manager → Title settings → URL).
2. **Photon AppId** (UUID — from Photon dashboard → Applications → your Realtime app → "App ID").
3. **Photon Region** (e.g. `us`, `eu`, `asia`).
4. **One-time PlayFab dashboard config:**
   - Game Manager → Settings → API Features → enable "Allow client to start games" (or whatever name the current dashboard uses for the equivalent).
   - Game Manager → Settings → Statistics → create `total_km_driven` (integer, aggregation: sum).
   - Game Manager → Add-ons → Photon → paste your Photon AppId and the Photon Secret. This sets up the trust relationship so Photon validates PlayFab tickets.

**Title secret keys never leave your machine** — I will not ask for them.

### 2.10 Explicit YAGNI (v1 does NOT include)

- Voice chat
- Friends list / friend invites
- Persistent rooms (Photon rooms die when last player leaves; world is recreated identically from the seed next time)
- Collision / physics sync between cars (ghosts are visual only, pass through each other)
- Race timing / leaderboards
- Anti-cheat / server-side validation
- Moderation tools (mute, block, kick)
- Voice / video / spectator mode
- Mobile-friendly chat input

All are reasonable v2 additions on top of this foundation.

---

## Part 3 — Build & Verification

### 3.1 Build sequence
1. Apply Part 1 mods (independent, can be done in any order, each verifiable in isolation).
2. Vendor PlayFab + Photon SDKs into `modded/_app/multiplayer/`.
3. Write `mp-config.js` with placeholder values.
4. Write `mp-ui.css`.
5. Write `mp-client.js` (largest file — built incrementally: auth → room → broadcast → ghosts → chat → km stats).
6. Patch `modded/index.html` to load the new files.
7. Patch `modded/_app/immutable/nodes/3.cdda88cc.js` with the two hooks.
8. Collect user's PlayFab Title ID and Photon AppId; populate `mp-config.js`.
9. End-to-end test with two browsers.

### 3.2 E2E test plan

Open Browser A and Browser B (different profiles or one normal + one incognito so they don't share localStorage):
- A: click `[Continue as Guest]`. Type seed `mptest`. Drive.
- B: click `[Create account]`, fill email/password/display name; future visits would use `[Sign in]`. Type seed `mptest`. Drive.

Expected:
- Each browser sees one ghost car (the other player).
- Ghost car follows the other player's path with sub-second latency.
- Pressing T in A and sending "hi" makes "Guest-XXXX: hi" appear in B within ~200ms.
- After 60s of driving in B, B's auth panel shows km > 0; refreshing browser preserves it.
- A's auth panel shows nothing about km (guest, not tracked).

### 3.3 README updates
- New "Multiplayer setup" section: PlayFab + Photon account creation walkthrough, `mp-config.js` fields, room/seed semantics.
- Extend the "What's modded" table with the 6 new value patches.
- Note in "Re-dumping" section: after a re-dump, mp-client glue + hooks must be reapplied to the new bundle.

---

## Open questions / known risks

| Risk | Mitigation |
|---|---|
| The exact own-car state object in `nodes/3.cdda88cc.js` isn't yet identified | Implementation phase begins with reading the rendering loop. If it turns out the state lives in a closed-over variable not on `window`, we'll need to find one accessible reference (likely the HUD-update path, since the HUD already reads pos/speed). Plan B: inject the assignment inline near the HUD update. |
| Photon JS SDK download URL stability | Lock vendored SDK files to a specific version, document the version in `mp-client.js` header comment. |
| PlayFab Photon add-on UI may differ from doc above (Microsoft updates dashboards frequently) | Provide a "if the dashboard layout differs, search for 'Photon' in Add-ons" fallback in README. |
| Ghost car visual using vehicle mesh: the mesh may be tightly coupled to the local player's physics state | If decoupling is hard, fall back to a simple low-poly proxy mesh (just a box or imported low-poly car) for ghosts. Less pretty but unblocks. |

---

## Approval

**Once you approve this spec**, I will:
1. Invoke the `superpowers:writing-plans` skill to produce a step-by-step implementation plan with checkpoints.
2. Begin implementation in order: Part 1 mods → vendor SDKs → glue files → hooks → request your credentials → E2E test → update README.

If anything in this spec is wrong or missing, tell me what to change and I'll revise before we plan.
