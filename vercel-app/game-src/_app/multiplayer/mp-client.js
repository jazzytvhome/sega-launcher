// === Slow Roads multiplayer client ===
// PlayFab (identity, statistics) + Photon Realtime (room messaging) glue.
//
// Loaded as a classic <script> after mp-config.js, playfab.min.js, photon-loadbalancing.js.
//
// Globals read:
//   window.MP_CONFIG                — {titleId, photonAppId, photonRegion}
//   window.PlayFabClientSDK         — PlayFab API surface (provided by playfab.min.js)
//   window.PlayFab.settings.titleId — set during init
//   window.Photon                   — Photon namespace (provided by photon-loadbalancing.js)
//
// Globals written:
//   window.__SR_MP_tick — called by nodes/3 per frame as __SR_MP_tick(H, Ae, dt, t, Be)
//     H  = live vehicle (position, heading, speed, velocity, motionDir, rotation, container)
//     Ae = live camera (position, matrixWorld, aspect, fov)
//     dt = delta time (s)
//     t  = sim time
//     Be = odometer (Be.totalDist + Be.sr1Distance = total meters)

(function() {
  'use strict';

  // ---- Config check ---------------------------------------------------------
  const CFG = window.MP_CONFIG;
  if (!CFG) {
    console.error('[MP] mp-config.js did not load.');
    return;
  }
  const CONFIGURED = CFG.titleId && CFG.titleId !== 'PASTE_YOUR_PLAYFAB_TITLE_ID'
                  && CFG.photonAppId && CFG.photonAppId !== 'PASTE_YOUR_PHOTON_APP_ID';

  // ---- State ----------------------------------------------------------------
  const STATE = {
    auth: 'none',                 // 'none' | 'guest' | 'registered' | 'solo'
    playerId: null,
    displayName: null,
    sessionTicket: null,
    photonClient: null,
    currentSeed: null,
    ghosts: new Map(),            // actorNr -> {x,y,z,h,v,b,lastUpdateMs,name,mesh,tag}
    chatLastSentMs: 0,
    lastTotalMeters: 0,
    lastStatUploadMs: 0,
    lastBroadcastMs: 0,
    sceneRef: null,               // discovered via H.container.parent on first tick
    THREE_ref: null,              // discovered via H.position.constructor.prototype chain
  };

  // ---- Bootstrap ------------------------------------------------------------
  function init() {
    renderAuthPanel();
    ensureChatUi();
    document.addEventListener('keydown', handleGlobalKey);

    if (!CONFIGURED) {
      console.warn('[MP] mp-config.js not configured — edit it with your PlayFab Title ID + Photon App ID.');
      return;
    }
    if (typeof PlayFabClientSDK === 'undefined') {
      console.error('[MP] PlayFabClientSDK not found — playfab.min.js failed to load.');
      return;
    }
    if (typeof Photon === 'undefined' || !Photon.LoadBalancing) {
      console.error('[MP] Photon SDK not found — photon-loadbalancing.js failed to load.');
      return;
    }
    PlayFab.settings.titleId = CFG.titleId;

    // Install the per-frame hook called by nodes/3's vehicle updateLive.
    window.__SR_MP_tick = onFrame;

    // Stat uploads (registered users only). Checks every 5s, uploads if 60s elapsed.
    setInterval(uploadStatsIfDue, 5000);

    console.log('[MP] init complete, titleId=' + CFG.titleId);

    // Show the startup gate so the user must pick an identity (or single-player) before playing.
    showStartupGate();
  }

  // ---- Startup gate (blocking modal) ----------------------------------------
  function showStartupGate() {
    if (document.getElementById('mp-startup-gate')) return;
    const overlay = document.createElement('div');
    overlay.id = 'mp-startup-gate';

    const inner = document.createElement('div');
    inner.className = 'mp-startup-inner';

    const title = document.createElement('h2');
    title.textContent = 'Slow Roads — Modded';
    inner.appendChild(title);

    const sub = document.createElement('p');
    sub.textContent = 'Pick how you want to play. You can change this later (refresh to reset).';
    inner.appendChild(sub);

    const btnSignIn = makeButton('mp-gate-signin', 'Sign in (play online, track km)', promptSignIn);
    inner.appendChild(btnSignIn);

    const btnRegister = makeButton('mp-gate-register', 'Create account (play online, track km)', promptRegister);
    inner.appendChild(btnRegister);

    const btnGuest = makeButton('mp-gate-guest', 'Continue as Guest (play online, no tracking)', loginAsGuest);
    inner.appendChild(btnGuest);

    const btnSolo = makeButton('mp-gate-solo', 'Single Player Only (no multiplayer)', selectSoloMode);
    btnSolo.classList.add('secondary');
    inner.appendChild(btnSolo);

    overlay.appendChild(inner);
    document.body.appendChild(overlay);
  }

  function hideStartupGate() {
    const o = document.getElementById('mp-startup-gate');
    if (o && o.parentNode) o.parentNode.removeChild(o);
  }

  // ---- Per-frame callback (driven by the game's vehicle updateLive) ---------
  // Signature: onFrame(H, Ae, dt, simT, Be, World)
  //   H     — vehicle (position/heading/speed/velocity)
  //   Ae    — camera
  //   dt    — delta time (s)
  //   simT  — sim time
  //   Be    — odometer (Be.totalDist + Be.sr1Distance = total meters)
  //   World — World settings registry (World.seed is the current room key)
  function onFrame(H, Ae, dt, simT, Be, World) {
    const now = performance.now();

    // Lazy-grab Vector3 constructor from a live H field (we need it to project ghost positions).
    if (!STATE.Vector3Ctor && H && H.position && H.position.constructor) {
      STATE.Vector3Ctor = H.position.constructor;
    }

    // Mark that the game is now running (vehicle updateLive only fires once a world is loaded).
    if (!STATE.gameLive) {
      STATE.gameLive = true;
      console.log('[MP] game live — hook receiving frames');
    }

    // Reconnect Photon when seed changes (or on first valid seed if we deferred).
    if (World && typeof World.seed !== 'undefined' && World.seed) {
      const seedStr = String(World.seed);
      if (STATE.pendingPhoton && STATE.currentSeed !== seedStr) {
        STATE.currentSeed = seedStr;
        connectPhotonWithCurrentSeed(seedStr);
      }
    }

    // Broadcast at 20 Hz max (don't flood Photon at game framerate).
    if (STATE.photonClient && STATE.photonClient.isInRoom && STATE.photonClient.isInRoom() &&
        now - STATE.lastBroadcastMs >= 50) {
      broadcastPosition(H);
      STATE.lastBroadcastMs = now;
    }

    // Render ghosts (no-op if alone).
    if (STATE.ghosts.size > 0) {
      renderGhosts(Ae, now);
    }

    // Stat accumulation (registered users only). Track delta since last upload.
    if (STATE.auth === 'registered' && Be) {
      const totalM = (Be.totalDist || 0) + (Be.sr1Distance || 0);
      if (STATE.lastTotalMeters > 0 && totalM > STATE.lastTotalMeters) {
        STATE._pendingMeters = (STATE._pendingMeters || 0) + (totalM - STATE.lastTotalMeters);
      }
      STATE.lastTotalMeters = totalM;
    }
  }

  // ---- Auth -----------------------------------------------------------------
  // Format PlayFab errors with field-level detail (the errorDetails object).
  function formatPlayFabError(error) {
    if (!error) return 'unknown error';
    let msg = error.errorMessage || String(error);
    if (error.errorDetails && typeof error.errorDetails === 'object') {
      const fields = [];
      for (const k of Object.keys(error.errorDetails)) {
        const v = error.errorDetails[k];
        fields.push('  ' + k + ': ' + (Array.isArray(v) ? v.join('; ') : v));
      }
      if (fields.length) msg += '\n' + fields.join('\n');
    }
    return msg;
  }

  function loginAsGuest() {
    let id = localStorage.getItem('slow-roads-mp-guest-id');
    if (!id) {
      id = (crypto.randomUUID ? crypto.randomUUID() : 'g' + Date.now() + Math.random().toString(36).slice(2));
      localStorage.setItem('slow-roads-mp-guest-id', id);
    }
    PlayFabClientSDK.LoginWithCustomID({
      TitleId: CFG.titleId, CustomId: id, CreateAccount: true,
    }, function(result, error) {
      if (error) { alert('Guest login failed:\n' + formatPlayFabError(error)); console.error('[MP] guest login', error); return; }
      STATE.auth = 'guest';
      STATE.playerId = result.data.PlayFabId;
      STATE.sessionTicket = result.data.SessionTicket;
      STATE.displayName = 'Guest-' + id.replace(/-/g, '').slice(0, 4).toUpperCase();
      console.log('[MP] guest login OK, playfabId=' + STATE.playerId);
      hideStartupGate();
      renderAuthPanel();
      connectPhotonForCurrentSeed();
    });
  }

  function signIn(email, password) {
    PlayFabClientSDK.LoginWithEmailAddress({
      TitleId: CFG.titleId, Email: email, Password: password,
      InfoRequestParameters: { GetPlayerProfile: true, ProfileConstraints: { ShowDisplayName: true } },
    }, function(result, error) {
      if (error) { alert('Sign in failed:\n' + formatPlayFabError(error)); console.error('[MP] sign-in', error); return; }
      STATE.auth = 'registered';
      STATE.playerId = result.data.PlayFabId;
      STATE.sessionTicket = result.data.SessionTicket;
      STATE.displayName =
        (result.data.InfoResultPayload && result.data.InfoResultPayload.PlayerProfile &&
         result.data.InfoResultPayload.PlayerProfile.DisplayName) || 'Player';
      console.log('[MP] sign-in OK as ' + STATE.displayName);
      hideStartupGate();
      renderAuthPanel();
      connectPhotonForCurrentSeed();
      fetchKmStat();
    });
  }

  function registerAccount(email, password, displayName) {
    PlayFabClientSDK.RegisterPlayFabUser({
      TitleId: CFG.titleId, Email: email, Password: password, DisplayName: displayName,
      RequireBothUsernameAndEmail: false,
    }, function(result, error) {
      if (error) { alert('Register failed:\n' + formatPlayFabError(error)); console.error('[MP] register', error); return; }
      STATE.auth = 'registered';
      STATE.playerId = result.data.PlayFabId;
      STATE.sessionTicket = result.data.SessionTicket;
      STATE.displayName = displayName;
      console.log('[MP] register OK as ' + displayName);
      hideStartupGate();
      renderAuthPanel();
      connectPhotonForCurrentSeed();
      fetchKmStat();
    });
  }

  function selectSoloMode() {
    STATE.auth = 'solo';
    console.log('[MP] single-player mode selected — multiplayer disabled');
    hideStartupGate();
    renderAuthPanel();
  }

  function signOut() {
    if (STATE.photonClient) {
      try { STATE.photonClient.disconnect(); } catch (_) {}
      STATE.photonClient = null;
    }
    for (const actorNr of Array.from(STATE.ghosts.keys())) removeGhost(actorNr);
    STATE.currentSeed = null;
    STATE.auth = 'none';
    STATE.playerId = STATE.sessionTicket = STATE.displayName = null;
    STATE.lastTotalMeters = 0;
    STATE.lastStatUploadMs = 0;
    renderAuthPanel();
    console.log('[MP] signed out');
  }

  // ---- Photon room (seed → room) -------------------------------------------
  // The two-step PlayFab → Photon auth handshake:
  //   1. After PlayFab login, call GetPhotonAuthenticationToken to exchange the
  //      PlayFab session for a Photon-specific token.
  //   2. Pass that token to Photon CustomAuth. Photon's CustomAuth URL
  //      (`https://<TitleID>.playfabapi.com/photon/authenticate`) validates it.
  //
  // Connection is deferred until the game's vehicle updateLive starts firing —
  // at which point onFrame receives the live World registry and reads the
  // current seed directly (no DOM scraping). See onFrame above.
  function connectPhotonForCurrentSeed() {
    // Defer until onFrame sees a live seed. See STATE.pendingPhoton check there.
    STATE.pendingPhoton = true;
    setStatus('Waiting for world to load…');
    console.log('[MP] Photon connection deferred — waiting for game world to load.');
  }

  function connectPhotonWithCurrentSeed(seed) {
    STATE.pendingPhoton = false;
    setStatus('Getting Photon token from PlayFab…');
    PlayFabClientSDK.GetPhotonAuthenticationToken({
      PhotonApplicationId: CFG.photonAppId,
    }, function(result, error) {
      if (error) {
        const msg = formatPlayFabError(error);
        console.error('[MP] GetPhotonAuthenticationToken failed:', msg,
          '— check PlayFab dashboard → Add-ons → Photon has your Photon AppId+Secret saved.');
        setStatus('PlayFab→Photon token failed (see console)');
        return;
      }
      const photonToken = result.data && result.data.PhotonCustomAuthenticationToken;
      if (!photonToken) {
        console.error('[MP] PlayFab returned no PhotonCustomAuthenticationToken');
        setStatus('Token missing (see console)');
        return;
      }
      connectPhotonWithToken(seed, photonToken);
    });
  }

  function connectPhotonWithToken(seed, photonToken) {
    if (STATE.photonClient) { try { STATE.photonClient.disconnect(); } catch (_) {} }
    STATE.currentSeed = seed;
    setStatus('Connecting to Photon…');

    const client = new Photon.LoadBalancing.LoadBalancingClient(
      Photon.ConnectionProtocol.Wss, CFG.photonAppId, '1.0'
    );
    client.myActor().setName(STATE.displayName);
    client.onError = function(errorCode, msg) {
      console.error('[MP] Photon error', errorCode, msg);
      setStatus('Photon error ' + errorCode + ' (console)');
    };
    client.onStateChange = function(state) {
      const S = Photon.LoadBalancing.LoadBalancingClient.State;
      const stateNames = ['Uninitialized', 'Error', 'ConnectingToMasterserver', 'ConnectedToMaster',
                          'JoinedLobby', 'DisconnectingFromMasterserver', 'ConnectingToGameserver',
                          'ConnectedToGameserver', 'Joined', 'Disconnecting', 'Disconnected',
                          'ConnectingToNameServer', 'ConnectedToNameServer', 'DisconnectingFromNameServer'];
      console.log('[MP] Photon state →', stateNames[state] || state);
      if (state === S.JoinedLobby) {
        setStatus('Joining room "' + seed + '"…');
        console.log('[MP] joined lobby, joining room "' + seed + '"');
        client.joinRoom(seed, {
          createIfNotExists: true,
          createRoomOptions: { maxPlayers: 10, customGameProperties: { seed: seed } }
        });
      } else if (state === S.Joined) {
        const others = countOthers(client);
        setStatus('In room "' + seed + '" · ' + (others + 1) + ' player' + ((others + 1) === 1 ? '' : 's'));
        console.log('[MP] joined room "' + seed + '" — ' + (others + 1) + ' player(s) including you');
      } else if (state === S.Disconnected) {
        setStatus('Disconnected');
      }
    };
    client.onEvent = function(code, content, actorNr) {
      if (actorNr === client.myActor().actorNr) return;
      if (code === 1) onPositionEvent(actorNr, content, client);
      else if (code === 2) onChatEvent(actorNr, content, client);
    };
    client.onActorJoin = function(actor) {
      console.log('[MP] actor joined:', actor.actorNr, actor.getName());
      const others = countOthers(client);
      setStatus('In room "' + STATE.currentSeed + '" · ' + (others + 1) + ' players');
    };
    client.onActorLeave = function(actor) {
      console.log('[MP] actor left:', actor.actorNr);
      removeGhost(actor.actorNr);
      const others = countOthers(client);
      setStatus('In room "' + STATE.currentSeed + '" · ' + (others + 1) + ' player' + ((others + 1) === 1 ? '' : 's'));
    };
    client.setCustomAuthentication(
      Photon.LoadBalancing.Constants.CustomAuthenticationType.Custom,
      'username=' + encodeURIComponent(STATE.playerId) + '&token=' + encodeURIComponent(photonToken)
    );
    client.connectToRegionMaster(CFG.photonRegion);
    STATE.photonClient = client;
  }

  function countOthers(client) {
    try {
      const actors = client.myRoomActors ? client.myRoomActors() : {};
      let n = 0;
      for (const k in actors) if (parseInt(k, 10) !== client.myActor().actorNr) n++;
      return n;
    } catch (_) { return 0; }
  }

  function setStatus(text) {
    STATE._statusText = text;
    const el = document.getElementById('mp-status-line');
    if (el) el.textContent = text;
  }

  // ---- Position broadcast ---------------------------------------------------
  function broadcastPosition(H) {
    if (!H || !H.position) return;
    STATE.photonClient.raiseEvent(1, {
      x: H.position.x, y: H.position.y, z: H.position.z,
      h: H.heading || 0,
      v: H.speed || 0,
      b: !!(H.brake || H.braking),
    });
  }

  function onPositionEvent(actorNr, payload, client) {
    let g = STATE.ghosts.get(actorNr);
    if (!g) {
      const actor = (client.myRoomActors && client.myRoomActors()) ? client.myRoomActors()[actorNr] : null;
      g = {
        name: actor ? actor.getName() : ('Player-' + actorNr),
        mesh: null, tag: null, lastUpdateMs: 0,
        x: 0, y: 0, z: 0, h: 0, v: 0, b: false,
      };
      STATE.ghosts.set(actorNr, g);
    }
    g.x = payload.x; g.y = payload.y; g.z = payload.z;
    g.h = payload.h; g.v = payload.v; g.b = !!payload.b;
    g.lastUpdateMs = performance.now();
  }

  // ---- Ghost rendering (2D HTML overlay markers — v1) -----------------------
  // We project each ghost's world position to screen space via the live camera
  // and render an HTML marker with a name tag. This is the v1 approach because
  // creating in-scene Three.js meshes would require reflecting the THREE
  // namespace out of the minified bundle, which is brittle. Markers are honest
  // and ship now; v2 can upgrade to 3D ghost meshes.
  function renderGhosts(camera, nowMs) {
    const Vec3 = STATE.Vector3Ctor;
    if (!Vec3 || !camera) return;
    const w = window.innerWidth, h = window.innerHeight;
    for (const [actorNr, g] of STATE.ghosts) {
      if (!g.marker) g.marker = createGhostMarker(g.name, actorNr);
      // Dead-reckon by velocity to smooth 20Hz → display refresh.
      const ageS = Math.min((nowMs - g.lastUpdateMs) / 1000, 0.5);
      const dx = Math.sin(g.h) * g.v * ageS;
      const dz = Math.cos(g.h) * g.v * ageS;
      const wx = g.x + dx, wy = g.y, wz = g.z + dz;
      // Project to screen
      const v = new Vec3(wx, wy + 1.5, wz);
      if (typeof v.project !== 'function') continue;
      v.project(camera);
      const sx = (v.x + 1) * 0.5 * w;
      const sy = (-v.y + 1) * 0.5 * h;
      const onScreen = v.z > -1 && v.z < 1 && sx > 0 && sx < w && sy > 0 && sy < h;
      if (onScreen) {
        g.marker.style.display = 'block';
        g.marker.style.left = sx + 'px';
        g.marker.style.top = sy + 'px';
      } else {
        g.marker.style.display = 'none';
      }
    }
    // GC ghosts whose actor went silent for >10s (defensive — onActorLeave should catch first).
    for (const [actorNr, g] of Array.from(STATE.ghosts)) {
      if (nowMs - g.lastUpdateMs > 10000) removeGhost(actorNr);
    }
  }

  function createGhostMarker(name, actorNr) {
    const hue = ((actorNr * 137) % 360);
    const wrap = document.createElement('div');
    wrap.className = 'mp-ghost-marker';
    wrap.style.borderColor = 'hsl(' + hue + ',70%,50%)';
    const tag = document.createElement('div');
    tag.className = 'mp-ghost-tag';
    tag.textContent = name;
    tag.style.background = 'hsla(' + hue + ',70%,30%,0.85)';
    wrap.appendChild(tag);
    document.body.appendChild(wrap);
    return wrap;
  }

  function removeGhost(actorNr) {
    const g = STATE.ghosts.get(actorNr);
    if (!g) return;
    if (g.marker && g.marker.parentNode) g.marker.parentNode.removeChild(g.marker);
    STATE.ghosts.delete(actorNr);
  }

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
      e.stopPropagation();
    });
  }

  function handleGlobalKey(e) {
    if (e.key === 't' || e.key === 'T') {
      const inp = document.getElementById('mp-chat-input');
      if (inp && document.activeElement !== inp && document.activeElement.tagName !== 'INPUT') {
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
    showChatMessage(name, String((payload && payload.msg) || '').slice(0, 200));
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

  // ---- km stat tracking (registered users only) ----------------------------
  // onFrame accumulates the running delta into STATE._pendingMeters every frame
  // (see Per-frame callback section). This timer uploads accumulated meters every 60s.
  function uploadStatsIfDue() {
    if (STATE.auth !== 'registered') return;
    if (Date.now() - STATE.lastStatUploadMs < 60000) return;
    const delta = STATE._pendingMeters || 0;
    if (delta < 1) return;
    PlayFabClientSDK.UpdatePlayerStatistics({
      Statistics: [{ StatisticName: 'total_km_driven', Value: Math.floor(delta) }],
    }, function(result, error) {
      if (error) { console.warn('[MP] stat upload failed:', error.errorMessage || error); return; }
      STATE._pendingMeters = 0;
      STATE.lastStatUploadMs = Date.now();
      fetchKmStat();
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

  // ---- UI: auth panel -------------------------------------------------------
  // All DOM constructed via createElement + textContent (no innerHTML) so user-controlled
  // strings (display name) can never be interpreted as markup.
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

  function renderAuthPanel() {
    let panel = document.getElementById('mp-auth-panel');
    if (!panel) {
      panel = document.createElement('div');
      panel.id = 'mp-auth-panel';
      document.body.appendChild(panel);
    }
    while (panel.firstChild) panel.removeChild(panel.firstChild);
    if (!CONFIGURED) {
      panel.textContent = 'MP not configured (edit mp-config.js)';
      return;
    }
    if (STATE.auth === 'solo') {
      // Single-player mode: hide the panel entirely so the game UI is uncluttered.
      panel.style.display = 'none';
      return;
    }
    panel.style.display = '';
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
    // Status line — shows connection state so user knows what's happening without DevTools.
    const status = document.createElement('div');
    status.id = 'mp-status-line';
    status.textContent = STATE._statusText || 'Not connected';
    panel.appendChild(status);
  }

  // ---- Boot -----------------------------------------------------------------
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
