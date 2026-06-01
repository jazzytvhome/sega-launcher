# SEGA+ Discord-Gated Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate the hosted Slow Roads build to SEGA+ Discord members via a clean C# launcher that verifies membership through Vercel + a Discord bot, unlocking the game behind an Edge-Middleware cookie gate.

**Architecture:** A C# WPF launcher runs a loopback+PKCE Discord OAuth flow, POSTs the code to a Vercel serverless function that exchanges it and asks the SEGA+ bot whether the user is a member. On success Vercel issues a 5-minute signed unlock token; the launcher opens the browser to `/api/unlock`, which trades it for a 6-hour HttpOnly session cookie. Vercel Edge Middleware checks that cookie on every game asset, so unverified clients never receive the files.

**Tech Stack:** Vercel (framework-less: `api/*.ts` Node functions + `middleware.ts` Edge + `public/` static), TypeScript, `jose` (Edge-compatible JWT), Discord OAuth2 + Bot REST, C#/.NET 10 WPF, xUnit, `javascript-obfuscator`.

---

> **Post-implementation amendments (2026-06-01):**
> 1. **TFM:** launcher targets `net10.0` / `net10.0-windows` (only the .NET 10 SDK was installed; WPF ships in-box). Tasks 10/12 csproj use `net10.0`.
> 2. **Security hardening (from automated review):** the unlock token is **no longer passed in the query string**. `/api/unlock` is now **POST** (token in body). The launcher opens **`/unlock#token=…`** (fragment, never sent to the server); a static `public/unlock.html` reads the fragment and POSTs it, then `location.replace("/")`. Middleware matcher excludes `unlock`. This keeps the token out of logs/history/Referer. The committed code is the source of truth where Task 4 / Task 11 code blocks below differ.

---

## Repository layout (target)

```
slow roads/
├─ vercel-app/                 # NEW — the deployed Vercel project
│  ├─ api/
│  │  ├─ verify.ts             # POST: exchange code, bot membership check, sign unlock token
│  │  └─ unlock.ts             # GET: validate unlock token, set session cookie, redirect /
│  ├─ lib/
│  │  └─ tokens.ts             # jose sign/verify helpers (shared by api + middleware)
│  ├─ public/                  # the game build (copied from ../modded), gated by middleware
│  │  └─ index.html            # watermark splash, NO typed-password gate
│  ├─ denied.html              # static "launch via SEGA+ app" page (public, ungated)
│  ├─ middleware.ts            # Edge: cookie gate on all game assets
│  ├─ tests/
│  │  ├─ tokens.test.ts
│  │  └─ verify.test.ts
│  ├─ package.json
│  ├─ tsconfig.json
│  ├─ vitest.config.ts
│  └─ vercel.json
├─ launcher/                   # NEW — the C# WPF launcher
│  ├─ SegaLauncher/            # WPF app project
│  │  ├─ Pkce.cs               # PKCE verifier/challenge helper
│  │  ├─ DiscordAuth.cs        # loopback listener + browser open + verify call
│  │  ├─ MainWindow.xaml(.cs)  # branded UI
│  │  ├─ AppConfig.cs          # public config (Vercel base URL, client id, port)
│  │  └─ SegaLauncher.csproj
│  └─ SegaLauncher.Tests/      # xUnit tests
│     ├─ PkceTests.cs
│     └─ SegaLauncher.Tests.csproj
└─ docs/superpowers/...        # spec + this plan
```

**Contracts pinned once (every component must match these exactly):**
- Loopback redirect URI: `http://127.0.0.1:51789/callback` (exact, registered in Discord portal)
- OAuth scope: `identify`
- Unlock token: HS256, claims `{ sub: userId, name, scope: "unlock" }`, exp **5m**
- Session token: HS256, claims `{ sub: userId, name, scope: "session" }`, exp **6h**
- Cookie: `sega_session`, `HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=21600`
- Vercel base URL placeholder: `https://sega-roads.vercel.app` (replace once the project exists)

---

## Task 0: Initialize repo + scaffold folders

**Files:**
- Create: `.gitignore`

- [ ] **Step 1: Initialize git (the folder is not yet a repo)**

Run:
```bash
cd "C:/Users/jazzy/Downloads/slow roads"
git init
```
Expected: `Initialized empty Git repository`.

- [ ] **Step 2: Create `.gitignore`**

```gitignore
node_modules/
.vercel/
dist/
bin/
obj/
*.user
.env
.env.local
__pycache__/
dump/
```

- [ ] **Step 3: Commit the existing tree as baseline**

```bash
git add -A
git commit -m "chore: baseline before SEGA+ launcher work"
```

---

## Task 1: Vercel project scaffold

**Files:**
- Create: `vercel-app/package.json`
- Create: `vercel-app/tsconfig.json`
- Create: `vercel-app/vercel.json`
- Create: `vercel-app/vitest.config.ts`

- [ ] **Step 1: Create `vercel-app/package.json`**

```json
{
  "name": "sega-roads",
  "private": true,
  "type": "module",
  "scripts": {
    "test": "vitest run",
    "dev": "vercel dev"
  },
  "dependencies": {
    "jose": "^5.9.6"
  },
  "devDependencies": {
    "@vercel/edge": "^1.2.1",
    "@vercel/node": "^3.2.24",
    "typescript": "^5.6.3",
    "vitest": "^2.1.8"
  }
}
```

- [ ] **Step 2: Create `vercel-app/tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "types": ["node"]
  },
  "include": ["api", "lib", "middleware.ts", "tests"]
}
```

- [ ] **Step 3: Create `vercel-app/vercel.json`**

```json
{
  "$schema": "https://openapi.vercel.sh/vercel.json",
  "cleanUrls": true,
  "trailingSlash": false
}
```

- [ ] **Step 4: Create `vercel-app/vitest.config.ts`**

```ts
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: { environment: "node", include: ["tests/**/*.test.ts"] },
});
```

- [ ] **Step 5: Install dependencies**

Run:
```bash
cd "C:/Users/jazzy/Downloads/slow roads/vercel-app"
npm install
```
Expected: `node_modules/` created, no errors.

- [ ] **Step 6: Commit**

```bash
git add vercel-app/package.json vercel-app/tsconfig.json vercel-app/vercel.json vercel-app/vitest.config.ts
git commit -m "chore: scaffold vercel project"
```

---

## Task 2: Token helpers (TDD)

**Files:**
- Test: `vercel-app/tests/tokens.test.ts`
- Create: `vercel-app/lib/tokens.ts`

- [ ] **Step 1: Write the failing test**

`vercel-app/tests/tokens.test.ts`:
```ts
import { describe, it, expect } from "vitest";
import { signToken, verifyToken } from "../lib/tokens.js";

const SECRET = "test-secret-test-secret-test-secret-32";

describe("tokens", () => {
  it("round-trips an unlock token", async () => {
    const jwt = await signToken({ sub: "123", name: "neo", scope: "unlock" }, "5m", SECRET);
    const claims = await verifyToken(jwt, SECRET);
    expect(claims.sub).toBe("123");
    expect(claims.scope).toBe("unlock");
  });

  it("rejects a token signed with a different secret", async () => {
    const jwt = await signToken({ sub: "123", name: "neo", scope: "session" }, "6h", SECRET);
    await expect(verifyToken(jwt, "another-secret-another-secret-32xx")).rejects.toThrow();
  });

  it("rejects an expired token", async () => {
    const jwt = await signToken({ sub: "1", name: "x", scope: "unlock" }, "0s", SECRET);
    await new Promise((r) => setTimeout(r, 1100));
    await expect(verifyToken(jwt, SECRET)).rejects.toThrow();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Users/jazzy/Downloads/slow roads/vercel-app" && npm test`
Expected: FAIL — cannot find module `../lib/tokens.js`.

- [ ] **Step 3: Write minimal implementation**

`vercel-app/lib/tokens.ts`:
```ts
import { SignJWT, jwtVerify, type JWTPayload } from "jose";

export type Scope = "unlock" | "session";
export interface TokenClaims extends JWTPayload {
  sub: string;
  name: string;
  scope: Scope;
}

function key(secret: string): Uint8Array {
  return new TextEncoder().encode(secret);
}

export async function signToken(
  claims: { sub: string; name: string; scope: Scope },
  expiresIn: string,
  secret: string,
): Promise<string> {
  return await new SignJWT({ name: claims.name, scope: claims.scope })
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(claims.sub)
    .setIssuedAt()
    .setExpirationTime(expiresIn)
    .sign(key(secret));
}

export async function verifyToken(token: string, secret: string): Promise<TokenClaims> {
  const { payload } = await jwtVerify(token, key(secret));
  return payload as TokenClaims;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add vercel-app/lib/tokens.ts vercel-app/tests/tokens.test.ts
git commit -m "feat(vercel): jose token sign/verify helpers"
```

---

## Task 3: `/api/verify` — exchange code + bot membership check (TDD)

**Files:**
- Test: `vercel-app/tests/verify.test.ts`
- Create: `vercel-app/lib/discord.ts`
- Create: `vercel-app/api/verify.ts`

The membership logic lives in a pure, testable function in `lib/discord.ts`; `api/verify.ts` is the thin HTTP wrapper. We test the pure function with `fetch` mocked.

- [ ] **Step 1: Write the failing test**

`vercel-app/tests/verify.test.ts`:
```ts
import { describe, it, expect, vi, afterEach } from "vitest";
import { verifyMembership } from "../lib/discord.js";

const ENV = {
  clientId: "cid",
  clientSecret: "csecret",
  botToken: "bot",
  guildId: "999",
  redirectUri: "http://127.0.0.1:51789/callback",
};

afterEach(() => vi.restoreAllMocks());

function mockFetchSequence(responses: Array<{ status: number; body: any }>) {
  const fn = vi.fn();
  responses.forEach((r) =>
    fn.mockResolvedValueOnce({
      ok: r.status >= 200 && r.status < 300,
      status: r.status,
      json: async () => r.body,
    }),
  );
  vi.stubGlobal("fetch", fn);
  return fn;
}

describe("verifyMembership", () => {
  it("returns member result for a SEGA+ member", async () => {
    mockFetchSequence([
      { status: 200, body: { access_token: "atok" } },           // token exchange
      { status: 200, body: { id: "42", username: "neo" } },       // /users/@me
      { status: 200, body: { user: { id: "42" } } },              // guild member (200 = member)
    ]);
    const res = await verifyMembership("authcode", "verifier", ENV);
    expect(res).toEqual({ ok: true, user: { id: "42", name: "neo" } });
  });

  it("returns not_member when the bot lookup 404s", async () => {
    mockFetchSequence([
      { status: 200, body: { access_token: "atok" } },
      { status: 200, body: { id: "7", username: "rando" } },
      { status: 404, body: { message: "Unknown Member" } },
    ]);
    const res = await verifyMembership("authcode", "verifier", ENV);
    expect(res).toEqual({ ok: false, reason: "not_member" });
  });

  it("returns discord_error when token exchange fails", async () => {
    mockFetchSequence([{ status: 400, body: { error: "invalid_grant" } }]);
    const res = await verifyMembership("badcode", "verifier", ENV);
    expect(res).toEqual({ ok: false, reason: "discord_error" });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `../lib/discord.js`.

- [ ] **Step 3: Write minimal implementation**

`vercel-app/lib/discord.ts`:
```ts
export interface DiscordEnv {
  clientId: string;
  clientSecret: string;
  botToken: string;
  guildId: string;
  redirectUri: string;
}

export type VerifyResult =
  | { ok: true; user: { id: string; name: string } }
  | { ok: false; reason: "not_member" | "discord_error" };

const API = "https://discord.com/api";

export async function verifyMembership(
  code: string,
  codeVerifier: string,
  env: DiscordEnv,
): Promise<VerifyResult> {
  // 1. Exchange the authorization code for a user access token (PKCE).
  const tokenRes = await fetch(`${API}/oauth2/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      client_id: env.clientId,
      client_secret: env.clientSecret,
      grant_type: "authorization_code",
      code,
      redirect_uri: env.redirectUri,
      code_verifier: codeVerifier,
    }),
  });
  if (!tokenRes.ok) return { ok: false, reason: "discord_error" };
  const { access_token } = (await tokenRes.json()) as { access_token: string };

  // 2. Identify the user.
  const meRes = await fetch(`${API}/users/@me`, {
    headers: { Authorization: `Bearer ${access_token}` },
  });
  if (!meRes.ok) return { ok: false, reason: "discord_error" };
  const me = (await meRes.json()) as { id: string; username: string };

  // 3. Ask the bot whether the user is a member of SEGA+.
  const memberRes = await fetch(`${API}/guilds/${env.guildId}/members/${me.id}`, {
    headers: { Authorization: `Bot ${env.botToken}` },
  });
  if (memberRes.status === 404) return { ok: false, reason: "not_member" };
  if (!memberRes.ok) return { ok: false, reason: "discord_error" };

  return { ok: true, user: { id: me.id, name: me.username } };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS (all verify + token tests pass).

- [ ] **Step 5: Write the HTTP wrapper `api/verify.ts`**

`vercel-app/api/verify.ts`:
```ts
import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyMembership, type DiscordEnv } from "../lib/discord.js";
import { signToken } from "../lib/tokens.js";

const REDIRECT_URI = "http://127.0.0.1:51789/callback";

export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });

  const { code, code_verifier } = req.body ?? {};
  if (!code || !code_verifier)
    return res.status(400).json({ ok: false, reason: "bad_request" });

  const env: DiscordEnv = {
    clientId: process.env.DISCORD_CLIENT_ID!,
    clientSecret: process.env.DISCORD_CLIENT_SECRET!,
    botToken: process.env.DISCORD_BOT_TOKEN!,
    guildId: process.env.SEGA_GUILD_ID!,
    redirectUri: REDIRECT_URI,
  };

  const result = await verifyMembership(code, code_verifier, env);
  if (!result.ok) return res.status(result.reason === "not_member" ? 403 : 502).json(result);

  const token = await signToken(
    { sub: result.user.id, name: result.user.name, scope: "unlock" },
    "5m",
    process.env.JWT_SECRET!,
  );
  return res.status(200).json({ ok: true, token, user: result.user });
}
```

- [ ] **Step 6: Run tests again (wrapper has no new unit test; ensure nothing broke)**

Run: `npm test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add vercel-app/lib/discord.ts vercel-app/api/verify.ts vercel-app/tests/verify.test.ts
git commit -m "feat(vercel): /api/verify with bot membership check"
```

---

## Task 4: `/api/unlock` — token → session cookie

**Files:**
- Create: `vercel-app/api/unlock.ts`

- [ ] **Step 1: Write `api/unlock.ts`**

`vercel-app/api/unlock.ts`:
```ts
import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyToken, signToken } from "../lib/tokens.js";

export default async function handler(req: VercelRequest, res: VercelResponse) {
  const token = (req.query.token as string) ?? "";
  const secret = process.env.JWT_SECRET!;
  try {
    const claims = await verifyToken(token, secret);
    if (claims.scope !== "unlock") throw new Error("wrong scope");

    const session = await signToken(
      { sub: claims.sub, name: claims.name, scope: "session" },
      "6h",
      secret,
    );
    res.setHeader(
      "Set-Cookie",
      `sega_session=${session}; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=21600`,
    );
    res.statusCode = 302;
    res.setHeader("Location", "/");
    return res.end();
  } catch {
    res.statusCode = 302;
    res.setHeader("Location", "/denied");
    return res.end();
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add vercel-app/api/unlock.ts
git commit -m "feat(vercel): /api/unlock sets session cookie"
```

---

## Task 5: Edge Middleware — the cookie gate

**Files:**
- Create: `vercel-app/middleware.ts`

- [ ] **Step 1: Write `middleware.ts`**

`vercel-app/middleware.ts`:
```ts
import { next, rewrite } from "@vercel/edge";
import { verifyToken } from "./lib/tokens.js";

// Run on everything EXCEPT the API, the denied page, and the denied page's own assets.
export const config = {
  matcher: ["/((?!api/|denied|favicon|robots).*)"],
};

export default async function middleware(request: Request): Promise<Response> {
  const cookie = request.headers.get("cookie") ?? "";
  const match = cookie.match(/(?:^|;\s*)sega_session=([^;]+)/);
  const token = match?.[1];

  if (token) {
    try {
      const claims = await verifyToken(token, process.env.JWT_SECRET!);
      if (claims.scope === "session") return next();
    } catch {
      /* fall through to denied */
    }
  }
  return rewrite(new URL("/denied", request.url));
}
```

- [ ] **Step 2: Commit**

```bash
git add vercel-app/middleware.ts
git commit -m "feat(vercel): edge middleware gates game assets by session cookie"
```

---

## Task 6: `/denied` page (branded, ungated)

**Files:**
- Create: `vercel-app/denied.html`

- [ ] **Step 1: Write `denied.html`**

`vercel-app/denied.html`:
```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>SEGA+ — locked</title>
  <style>
    html,body{height:100%;margin:0}
    body{display:flex;flex-direction:column;align-items:center;justify-content:center;
      background:#222;color:#F4F2ED;font-family:system-ui,Helvetica,sans-serif;text-align:center;gap:1rem;padding:2rem}
    h1{letter-spacing:.12em;font-size:1.6rem;margin:0}
    .accent{color:#ff992b}
    p{opacity:.85;max-width:32rem;line-height:1.5}
    a{color:#ff992b}
    .credit{position:fixed;bottom:1rem;font-size:.75rem;opacity:.5}
  </style>
</head>
<body>
  <h1>SEGA+ <span class="accent">members only</span></h1>
  <p>This build is gated. Open the <strong>SEGA+ Launcher</strong> and verify with
     Discord to play. If verification fails, make sure you're a member of the
     SEGA+ server (apply-only).</p>
  <p>Not a member yet? Ask in SEGA+ about applying.</p>
  <div class="credit">Game by slowroads.io (Anslo) — all credit to the original.</div>
</body>
</html>
```

- [ ] **Step 2: Commit**

```bash
git add vercel-app/denied.html
git commit -m "feat(vercel): branded /denied page"
```

---

## Task 7: Place the game build + watermark (no typed gate)

**Files:**
- Create: `vercel-app/public/` (copied from `modded/`)
- Modify: `vercel-app/public/index.html`

- [ ] **Step 1: Copy the modded build into the Vercel public folder**

Run (PowerShell):
```powershell
Copy-Item -Recurse -Force "C:/Users/jazzy/Downloads/slow roads/modded/*" "C:/Users/jazzy/Downloads/slow roads/vercel-app/public/"
```
Expected: `vercel-app/public/index.html`, `_app/`, `fonts/`, etc. exist.

- [ ] **Step 2: In `vercel-app/public/index.html`, REMOVE the typed-password gate block but KEEP the watermark**

Delete the `#skid-gate` markup and its password `<script>` logic (the part that reads `SKID_PASSWORD`, the input, button, `tryPass`). Keep only the watermark. Replace the entire injected `<!-- ===== SKID GATE + WATERMARK (injected) ===== --> … <!-- ===== /SKID GATE + WATERMARK ===== -->` block with this watermark-only block:

```html
		<!-- ===== SKID WATERMARK (hosted build) ===== -->
		<style>
			#skid-mark{position:fixed;inset:0;z-index:2147483645;display:flex;
				flex-direction:column;align-items:center;justify-content:center;
				pointer-events:none;background:rgba(34,34,34,.55);
				font-family:Space,Helvetica,sans-serif;color:#F4F2ED;text-align:center;
				opacity:0;transition:opacity .8s ease;}
			#skid-mark.show{opacity:1;}
			#skid-mark .big{font-size:2.6rem;font-weight:700;letter-spacing:.06em;
				text-shadow:0 2px 14px rgba(0,0,0,.6);}
			#skid-mark .sub{font-size:1.4rem;font-weight:600;letter-spacing:.22em;margin-top:.4rem;
				color:#ff992b;text-shadow:0 2px 14px rgba(0,0,0,.6);}
		</style>
		<div id="skid-mark">
			<div class="big">SKIDDED BY JUSTONEONTOP</div>
			<div class="sub">SEGA+ ON TOP</div>
		</div>
		<script>
			(function(){
				var mark = document.getElementById("skid-mark");
				mark.classList.add("show");
				setTimeout(function(){ mark.classList.remove("show"); }, 3000);
				setTimeout(function(){ mark.remove(); }, 4000);
			})();
		</script>
		<!-- ===== /SKID WATERMARK ===== -->
```

- [ ] **Step 3: Commit**

```bash
git add vercel-app/public
git commit -m "feat(vercel): host game build with watermark, no typed gate"
```

---

## Task 8: Remove the now-obsolete typed-password gate from local builds

The local `.bat` play is dev-only and superseded by the server gate. Per the spec, remove the typed-password overlay from `modded/index.html` and `normal/index.html`, keeping the watermark.

**Files:**
- Modify: `modded/index.html`
- Modify: `normal/index.html`

- [ ] **Step 1: In both files, replace the injected gate+watermark block with the watermark-only block from Task 7 Step 2**

Apply the identical edit (delete `#skid-gate` + password script, keep `#skid-mark` + its show/hide script) to `modded/index.html` and `normal/index.html`.

- [ ] **Step 2: Commit**

```bash
git add "modded/index.html" "normal/index.html"
git commit -m "refactor: drop local typed-password gate (server gate supersedes)"
```

---

## Task 9: Deploy to Vercel + configure env vars (manual setup)

This task is operator setup; there's no unit test. Do it in order — the redirect URI must match the launcher byte-for-byte.

- [ ] **Step 1: Create the Discord application**

In <https://discord.com/developers/applications>: New Application → name it (e.g. "SEGA+ Roads"). Copy **Client ID** and **Client Secret** (OAuth2 page).

- [ ] **Step 2: Register the redirect URI**

OAuth2 → Redirects → add exactly: `http://127.0.0.1:51789/callback` → Save.

- [ ] **Step 3: Create + invite the bot**

Bot page → Add Bot → copy **Bot Token**. Enable **Server Members Intent**. Use OAuth2 URL Generator with scope `bot` (no extra permissions) to invite it to **SEGA+**.

- [ ] **Step 4: Get the SEGA+ Guild ID**

In Discord (Developer Mode on) → right-click the SEGA+ server icon → Copy Server ID.

- [ ] **Step 5: Create the Vercel project + deploy**

Run:
```bash
cd "C:/Users/jazzy/Downloads/slow roads/vercel-app"
npx vercel
```
Follow prompts. Note the production domain (replace the `sega-roads.vercel.app` placeholder everywhere if different).

- [ ] **Step 6: Set environment variables**

Run (repeat for each, or set in the Vercel dashboard → Settings → Environment Variables):
```bash
npx vercel env add DISCORD_CLIENT_ID
npx vercel env add DISCORD_CLIENT_SECRET
npx vercel env add DISCORD_BOT_TOKEN
npx vercel env add SEGA_GUILD_ID
npx vercel env add JWT_SECRET
```
For `JWT_SECRET` use a long random string (e.g. `openssl rand -base64 48`).

- [ ] **Step 7: Redeploy to pick up env vars + verify the gate**

Run: `npx vercel --prod`
Then visit the production URL in a browser with no cookie. Expected: you see `/denied`, NOT the game.

---

## Task 10: Launcher — PKCE helper (TDD)

**Files:**
- Create: `launcher/SegaLauncher.Tests/SegaLauncher.Tests.csproj`
- Test: `launcher/SegaLauncher.Tests/PkceTests.cs`
- Create: `launcher/SegaLauncher/Pkce.cs`

- [ ] **Step 1: Create the test project + reference**

`launcher/SegaLauncher.Tests/SegaLauncher.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="../SegaLauncher/Pkce.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing test**

`launcher/SegaLauncher.Tests/PkceTests.cs`:
```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using SegaLauncher;

public class PkceTests
{
    [Fact]
    public void Challenge_Is_Base64Url_Sha256_Of_Verifier()
    {
        var pkce = Pkce.Create();

        // verifier is URL-safe and of reasonable length
        Assert.InRange(pkce.Verifier.Length, 43, 128);
        Assert.DoesNotContain('+', pkce.Verifier);
        Assert.DoesNotContain('/', pkce.Verifier);
        Assert.DoesNotContain('=', pkce.Verifier);

        // challenge == base64url(SHA256(ASCII(verifier)))
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(pkce.Verifier));
        var expected = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.Equal(expected, pkce.Challenge);
    }

    [Fact]
    public void Create_Produces_Unique_Verifiers()
    {
        Assert.NotEqual(Pkce.Create().Verifier, Pkce.Create().Verifier);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run:
```bash
cd "C:/Users/jazzy/Downloads/slow roads/launcher/SegaLauncher.Tests"
dotnet test
```
Expected: FAIL — `Pkce` does not exist.

- [ ] **Step 4: Write `Pkce.cs`**

`launcher/SegaLauncher/Pkce.cs`:
```csharp
using System;
using System.Security.Cryptography;
using System.Text;

namespace SegaLauncher;

public readonly record struct PkcePair(string Verifier, string Challenge);

public static class Pkce
{
    public static PkcePair Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return new PkcePair(verifier, Base64Url(hash));
    }

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (2 passed).

- [ ] **Step 6: Commit**

```bash
git add launcher/SegaLauncher/Pkce.cs launcher/SegaLauncher.Tests/
git commit -m "feat(launcher): PKCE helper with tests"
```

---

## Task 11: Launcher — config + Discord auth flow

**Files:**
- Create: `launcher/SegaLauncher/AppConfig.cs`
- Create: `launcher/SegaLauncher/DiscordAuth.cs`

- [ ] **Step 1: Write `AppConfig.cs` (public, non-secret config)**

`launcher/SegaLauncher/AppConfig.cs`:
```csharp
namespace SegaLauncher;

public static class AppConfig
{
    // PUBLIC values only. No secrets ever live in the launcher.
    public const string DiscordClientId = "REPLACE_WITH_DISCORD_CLIENT_ID";
    public const string VercelBaseUrl   = "https://sega-roads.vercel.app";
    public const int    LoopbackPort    = 51789;

    public static string RedirectUri => $"http://127.0.0.1:{LoopbackPort}/callback";
}
```

- [ ] **Step 2: Write `DiscordAuth.cs`**

`launcher/SegaLauncher/DiscordAuth.cs`:
```csharp
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SegaLauncher;

public enum AuthOutcome { Verified, NotMember, Cancelled, ServerError, PortBusy }

public sealed record AuthResult(AuthOutcome Outcome, string? UnlockToken = null, string? Message = null);

public static class DiscordAuth
{
    private static readonly HttpClient Http = new();

    public static async Task<AuthResult> RunAsync(CancellationToken ct = default)
    {
        var pkce = Pkce.Create();
        var state = Pkce.NewState();

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{AppConfig.LoopbackPort}/callback/");
        try { listener.Start(); }
        catch (HttpListenerException) { return new AuthResult(AuthOutcome.PortBusy,
            Message: "Close any other SEGA+ Launcher window and try again."); }

        // Open the system browser to Discord's authorize page.
        var authUrl =
            "https://discord.com/api/oauth2/authorize" +
            $"?client_id={AppConfig.DiscordClientId}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(AppConfig.RedirectUri)}" +
            "&scope=identify" +
            $"&state={state}" +
            $"&code_challenge={pkce.Challenge}&code_challenge_method=S256";
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        // Wait for Discord to redirect back to the loopback listener (with a timeout).
        var getContext = listener.GetContextAsync();
        var completed = await Task.WhenAny(getContext, Task.Delay(TimeSpan.FromMinutes(3), ct));
        if (completed != getContext)
            return new AuthResult(AuthOutcome.Cancelled, Message: "Verification timed out.");

        var ctx = await getContext;
        var query = ctx.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];
        await WriteBrowserPageAsync(ctx.Response,
            "Verified — you can close this tab and return to the SEGA+ Launcher.");

        if (returnedState != state || string.IsNullOrEmpty(code))
            return new AuthResult(AuthOutcome.Cancelled, Message: "Verification cancelled.");

        // Hand the code to Vercel, which exchanges it and asks the bot about membership.
        try
        {
            var resp = await Http.PostAsJsonAsync(
                $"{AppConfig.VercelBaseUrl}/api/verify",
                new { code, code_verifier = pkce.Verifier }, ct);

            if (resp.StatusCode == HttpStatusCode.Forbidden)
                return new AuthResult(AuthOutcome.NotMember,
                    Message: "Members only — make sure you're in the SEGA+ server.");
            if (!resp.IsSuccessStatusCode)
                return new AuthResult(AuthOutcome.ServerError,
                    Message: "Server config issue — contact a SEGA+ admin.");

            var body = await resp.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken: ct);
            if (body is null || !body.ok || string.IsNullOrEmpty(body.token))
                return new AuthResult(AuthOutcome.ServerError, Message: "Unexpected server response.");

            return new AuthResult(AuthOutcome.Verified, body.token);
        }
        catch (HttpRequestException)
        {
            return new AuthResult(AuthOutcome.ServerError,
                Message: "Can't reach the server — try again later.");
        }
    }

    public static void LaunchGame(string unlockToken)
    {
        var url = $"{AppConfig.VercelBaseUrl}/api/unlock?token={Uri.EscapeDataString(unlockToken)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static async Task WriteBrowserPageAsync(HttpListenerResponse res, string message)
    {
        var html = Encoding.UTF8.GetBytes(
            $"<html><body style='background:#222;color:#eee;font-family:sans-serif;" +
            $"display:flex;align-items:center;justify-content:center;height:100vh'>" +
            $"<h2>{message}</h2></body></html>");
        res.ContentType = "text/html";
        res.ContentLength64 = html.Length;
        await res.OutputStream.WriteAsync(html);
        res.OutputStream.Close();
    }

    private sealed record VerifyResponse(bool ok, string? token);
}
```

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/AppConfig.cs launcher/SegaLauncher/DiscordAuth.cs
git commit -m "feat(launcher): loopback PKCE auth flow + verify call"
```

---

## Task 12: Launcher — WPF UI + project file

**Files:**
- Create: `launcher/SegaLauncher/SegaLauncher.csproj`
- Create: `launcher/SegaLauncher/App.xaml` + `App.xaml.cs`
- Create: `launcher/SegaLauncher/MainWindow.xaml` + `MainWindow.xaml.cs`

- [ ] **Step 1: Write `SegaLauncher.csproj`**

`launcher/SegaLauncher/SegaLauncher.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <AssemblyName>SEGA+ Launcher</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Write `App.xaml` and `App.xaml.cs`**

`launcher/SegaLauncher/App.xaml`:
```xml
<Application x:Class="SegaLauncher.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml" />
```

`launcher/SegaLauncher/App.xaml.cs`:
```csharp
using System.Windows;
namespace SegaLauncher;
public partial class App : Application { }
```

- [ ] **Step 3: Write `MainWindow.xaml`**

`launcher/SegaLauncher/MainWindow.xaml`:
```xml
<Window x:Class="SegaLauncher.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="SEGA+ Launcher" Height="380" Width="520"
        WindowStartupLocation="CenterScreen" Background="#222"
        ResizeMode="CanMinimize">
    <Grid Margin="32">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0" Text="SEGA+ ON TOP" Foreground="#ff992b"
                   FontSize="26" FontWeight="Bold" HorizontalAlignment="Center"/>
        <TextBlock Grid.Row="1" Text="Verify your SEGA+ membership to play."
                   Foreground="#F4F2ED" FontSize="14" Opacity="0.85"
                   HorizontalAlignment="Center" Margin="0,8,0,0"/>
        <TextBlock x:Name="StatusText" Grid.Row="2" Text="" Foreground="#F4F2ED"
                   FontSize="14" TextWrapping="Wrap" TextAlignment="Center"
                   VerticalAlignment="Center"/>
        <Button x:Name="VerifyButton" Grid.Row="3" Content="Verify with Discord"
                Height="44" FontSize="15" FontWeight="SemiBold" Cursor="Hand"
                Background="#5865F2" Foreground="White" BorderThickness="0"
                Click="VerifyButton_Click"/>
        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,12,0,0">
            <TextBlock Text="Game by slowroads.io — all credit to the original.  "
                       Foreground="#7e7c76" FontSize="11"/>
            <TextBlock>
                <Hyperlink NavigateUri="https://www.virustotal.com/"
                           RequestNavigate="Hyperlink_RequestNavigate"
                           Foreground="#7e7c76">scan this app</Hyperlink>
            </TextBlock>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 4: Write `MainWindow.xaml.cs`**

`launcher/SegaLauncher/MainWindow.xaml.cs`:
```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        VerifyButton.IsEnabled = false;
        StatusText.Text = "Opening Discord… authorize in your browser, then come back.";

        var result = await DiscordAuth.RunAsync();
        switch (result.Outcome)
        {
            case AuthOutcome.Verified:
                StatusText.Text = "Verified! Launching the game…";
                DiscordAuth.LaunchGame(result.UnlockToken!);
                break;
            default:
                StatusText.Text = result.Message ?? "Verification failed.";
                break;
        }
        VerifyButton.IsEnabled = true;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
```

- [ ] **Step 5: Build the launcher (sanity)**

Run:
```bash
cd "C:/Users/jazzy/Downloads/slow roads/launcher/SegaLauncher"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add launcher/SegaLauncher/
git commit -m "feat(launcher): WPF UI wired to auth flow"
```

---

## Task 13: Obfuscate added JS (build step)

Only our small added glue is obfuscated; the SvelteKit chunks are left intact (already minified; obfuscating them risks breaking the game).

**Files:**
- Modify: `vercel-app/public/index.html` (replace inline watermark `<script>` with an obfuscated external file)
- Create: `vercel-app/scripts/obfuscate.mjs`
- Create: `vercel-app/public/_app/skid-watermark.js` (generated output, committed)

- [ ] **Step 1: Add the obfuscator dev dependency**

Run:
```bash
cd "C:/Users/jazzy/Downloads/slow roads/vercel-app"
npm install -D javascript-obfuscator
```

- [ ] **Step 2: Move the watermark JS to a source file**

`vercel-app/scripts/watermark.src.js`:
```js
(function () {
  var mark = document.getElementById("skid-mark");
  if (!mark) return;
  mark.classList.add("show");
  setTimeout(function () { mark.classList.remove("show"); }, 3000);
  setTimeout(function () { mark.remove(); }, 4000);
})();
```

- [ ] **Step 3: Write the obfuscation script**

`vercel-app/scripts/obfuscate.mjs`:
```js
import { readFileSync, writeFileSync } from "node:fs";
import JavaScriptObfuscator from "javascript-obfuscator";

const src = readFileSync("scripts/watermark.src.js", "utf8");
const out = JavaScriptObfuscator.obfuscate(src, {
  compact: true,
  controlFlowFlattening: true,
  stringArray: true,
  stringArrayEncoding: ["base64"],
}).getObfuscatedCode();
writeFileSync("public/_app/skid-watermark.js", out);
console.log("obfuscated -> public/_app/skid-watermark.js");
```

- [ ] **Step 4: Add an npm script and run it**

In `vercel-app/package.json` add to `"scripts"`: `"obfuscate": "node scripts/obfuscate.mjs"`. Then:
```bash
npm run obfuscate
```
Expected: `public/_app/skid-watermark.js` written.

- [ ] **Step 5: Replace the inline watermark script in `public/index.html`**

Remove the inline `<script>(function(){ var mark = … })();</script>` from the watermark block and replace with:
```html
		<script src="/_app/skid-watermark.js"></script>
```
(Keep the `#skid-mark` markup + `<style>`.)

- [ ] **Step 6: Verify the game still loads locally**

Run: `cd "C:/Users/jazzy/Downloads/slow roads/vercel-app" && npx vercel dev`
Visit the local URL; manually set a session cookie or temporarily bypass middleware for this check, and confirm the watermark fades in/out and the game runs.

- [ ] **Step 7: Commit**

```bash
git add vercel-app/scripts vercel-app/public/_app/skid-watermark.js vercel-app/public/index.html vercel-app/package.json
git commit -m "feat(vercel): obfuscate watermark glue as external script"
```

---

## Task 14: End-to-end verification (manual)

- [ ] **Step 1: Set the launcher's real config**

Edit `launcher/SegaLauncher/AppConfig.cs`: set `DiscordClientId` to the real Client ID and `VercelBaseUrl` to the real production domain. Rebuild: `dotnet build`.

- [ ] **Step 2: Member happy path**

Run the launcher (`dotnet run` in `launcher/SegaLauncher`), click Verify, authorize in Discord with an account that IS in SEGA+.
Expected: browser opens → game loads → watermark splash plays.

- [ ] **Step 3: Non-member path**

Repeat with an account NOT in SEGA+ (or temporarily change `SEGA_GUILD_ID` to a guild you're not in).
Expected: launcher shows "Members only…", browser does not reach the game.

- [ ] **Step 4: Direct-URL path**

In a fresh browser (no cookie), open the production game URL directly.
Expected: redirected/rewritten to `/denied` — no game assets served.

- [ ] **Step 5: Expiry path**

After ≥6 hours (or temporarily lower session exp to `"30s"` in `unlock.ts`, redeploy, and wait), refresh the game.
Expected: `/denied` — must re-launch.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "chore: finalize SEGA+ launcher config + e2e verified"
```

---

## Self-review notes (author)

- **Spec coverage:** Discord app/bot (Task 9), Vercel verify (Task 3), unlock cookie (Task 4), edge gate (Task 5), denied page (Task 6), hosted game + watermark (Task 7), old gate removal (Task 8), PKCE launcher (Tasks 10–12), obfuscation of added JS + clean launcher (Task 13 / no launcher obfuscation per AV decision), error states (covered in `DiscordAuth` outcomes + `/denied`), testing (Tasks 2,3,10 unit; Task 14 e2e). All spec sections mapped.
- **Membership-only:** no role checks anywhere — matches spec.
- **Contract consistency:** redirect URI, scopes, token claims/scopes, cookie name, and lifetimes match across `tokens.ts`, `verify.ts`, `unlock.ts`, `middleware.ts`, and `DiscordAuth.cs`.
- **Launcher obfuscation:** intentionally omitted (AV-friendliness decision).
