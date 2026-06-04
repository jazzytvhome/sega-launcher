# SEGA+ Launcher — GUI + Weekly License + Request-a-Source — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reskin the SEGA+ Launcher (scene/cracktro look), add a server-signed 7-day license so users verify once a week instead of every Play, and add a members-only "Request a Source" flow that uploads a `.zip` straight to a private GitHub repo and posts the link to a staff webhook.

**Architecture:** Existing WPF (.NET 10) launcher + existing Vercel TypeScript backend. The launcher keeps all its OAuth/decrypt/local-server/auto-update logic; only XAML/styles, a license gate, and a request client are added. The backend gains a license-token issuer (reusing `jose`), an `/api/unlock` endpoint (reuses the bot membership check, no browser), and a two-step direct-to-GitHub upload (`/api/request/init` + `/finalize`) using a short-lived GitHub App token.

**Tech Stack:** C# / WPF / .NET 10, MaterialDesign replaced by hand-rolled scene XAML + bundled fonts (Anton, VT323); TypeScript / `@vercel/node` / `jose`; vitest; xUnit (`launcher/SegaLauncher.Tests`).

**Spec:** `docs/superpowers/specs/2026-06-04-sega-launcher-gui-license-design.md`
**Visual source of truth:** `docs/mockups/launcher-gui.html` (boot → verify → main + request modal).

---

## File structure

**Backend (`vercel-app/`)**
- `lib/tokens.ts` (modify) — add `signLicense` / `verifyLicense`.
- `lib/discord.ts` (modify) — add `checkMember(uid, env)` (bot lookup by id, no OAuth).
- `lib/github.ts` (create) — GitHub App → scoped installation token; draft/publish release helpers.
- `api/verify.ts` (modify) — also return `lic`.
- `api/unlock.ts` (create) — `{lic, machine}` → key + dl.
- `api/request/init.ts` (create) — validate + rate-limit + draft release + scoped token.
- `api/request/finalize.ts` (create) — publish release + post webhook link.
- `tests/` (create files) — vitest for tokens + license.

**Launcher (`launcher/SegaLauncher/`)**
- `License.cs` (create) — token file (DPAPI), expiry/days-left, machine id.
- `LicenseClient.cs` (create) — POST `/api/unlock`.
- `RequestClient.cs` (create) — init → PUT zip to GitHub → finalize.
- `DiscordAuth.cs` (modify) — capture `lic`.
- `MainWindow.xaml` + `.cs` (modify) — boot/license gate, request modal, scene skin.
- `App.xaml` (modify) — scene styles, font merges.
- `SegaLauncher.csproj` (modify) — bundle fonts + mp3.
- `Assets/` (create) — `Anton.ttf`, `VT323.ttf`, `sega-on-top.mp3`.
- `launcher/SegaLauncher.Tests/` (add files) — License tests.

---

# PHASE A — Backend: license issuing + unlock

### Task A1: License token sign/verify (`lib/tokens.ts`)

**Files:**
- Modify: `vercel-app/lib/tokens.ts`
- Test: `vercel-app/tests/license.test.ts`

- [ ] **Step 1: Write the failing test**

```ts
// vercel-app/tests/license.test.ts
import { describe, it, expect, beforeAll } from "vitest";
import { signLicense, verifyLicense } from "../lib/tokens.js";

beforeAll(() => { process.env.LICENSE_SECRET = "test-secret-test-secret-test-1234"; });

describe("license token", () => {
  it("round-trips uid + machine", async () => {
    const tok = await signLicense("user123", "machineABC");
    const r = await verifyLicense(tok, "machineABC");
    expect(r).toEqual({ ok: true, uid: "user123" });
  });

  it("rejects a wrong machine id", async () => {
    const tok = await signLicense("user123", "machineABC");
    expect(await verifyLicense(tok, "OTHER")).toEqual({ ok: false });
  });

  it("rejects garbage", async () => {
    expect(await verifyLicense("not.a.jwt", "machineABC")).toEqual({ ok: false });
  });
});
```

- [ ] **Step 2: Run it, confirm it fails**

Run: `cd vercel-app && npx vitest run tests/license.test.ts`
Expected: FAIL — `signLicense`/`verifyLicense` not exported.

- [ ] **Step 3: Implement in `lib/tokens.ts`**

Append:

```ts
// --- weekly license token (separate secret from the download/game key) ---
function licenseSecret(): Uint8Array {
  return new TextEncoder().encode(process.env.LICENSE_SECRET ?? "");
}

const LICENSE_DAYS = Number(process.env.LICENSE_DAYS ?? "7");

export async function signLicense(uid: string, machine: string): Promise<string> {
  return await new SignJWT({ scope: "license", mid: machine })
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(uid)
    .setIssuedAt()
    .setExpirationTime(`${LICENSE_DAYS}d`)
    .sign(licenseSecret());
}

export type LicenseCheck = { ok: true; uid: string } | { ok: false };

export async function verifyLicense(token: string, machine: string): Promise<LicenseCheck> {
  try {
    const { payload } = await jwtVerify(token, licenseSecret());
    if (payload.scope !== "license" || payload.mid !== machine || !payload.sub) return { ok: false };
    return { ok: true, uid: String(payload.sub) };
  } catch {
    return { ok: false };
  }
}
```

- [ ] **Step 4: Run it, confirm it passes**

Run: `cd vercel-app && npx vitest run tests/license.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add vercel-app/lib/tokens.ts vercel-app/tests/license.test.ts
git commit -m "feat(backend): signLicense/verifyLicense (7-day, machine-bound)"
```

---

### Task A2: Membership check by uid (`lib/discord.ts`)

**Files:**
- Modify: `vercel-app/lib/discord.ts`

- [ ] **Step 1: Add `checkMember` (no test — thin wrapper over the Discord API; covered by integration)**

Append to `lib/discord.ts`:

```ts
// Re-check membership/blacklist by user id (used by /api/unlock — no OAuth, no browser).
export async function checkMember(uid: string, env: DiscordEnv): Promise<VerifyResult> {
  if (env.blacklist?.includes(uid)) return { ok: false, reason: "blacklisted" };
  const memberRes = await fetch(`${API}/guilds/${env.guildId}/members/${uid}`, {
    headers: { Authorization: `Bot ${env.botToken}` },
  });
  if (memberRes.status === 404) return { ok: false, reason: "not_member" };
  if (!memberRes.ok) return { ok: false, reason: "discord_error" };
  const m = (await memberRes.json()) as { user?: { username?: string } };
  return { ok: true, user: { id: uid, name: m.user?.username ?? "member" } };
}
```

- [ ] **Step 2: Build-check**

Run: `cd vercel-app && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add vercel-app/lib/discord.ts
git commit -m "feat(backend): checkMember(uid) bot lookup for unlock"
```

---

### Task A3: `api/verify.ts` also issues a license

**Files:**
- Modify: `vercel-app/api/verify.ts`

- [ ] **Step 1: Accept `machine`, return `lic`**

Change the body destructure and the success return:

```ts
// near the top, replace the body line:
const { code, code_verifier, machine } = req.body ?? {};
if (!code || !code_verifier || !machine)
  return res.status(400).json({ ok: false, reason: "bad_request" });
```

```ts
// add the import at the top:
import { signDownload, signLicense } from "../lib/tokens.js";
```

```ts
// replace the final success return:
const dl = await signDownload(result.user.id);
const lic = await signLicense(result.user.id, String(machine));
return res.status(200).json({ ok: true, key, dl, lic });
```

- [ ] **Step 2: Build-check**

Run: `cd vercel-app && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add vercel-app/api/verify.ts
git commit -m "feat(backend): verify returns a 7-day license token"
```

---

### Task A4: `api/unlock.ts`

**Files:**
- Create: `vercel-app/api/unlock.ts`

- [ ] **Step 1: Implement the endpoint**

```ts
// vercel-app/api/unlock.ts
import type { VercelRequest, VercelResponse } from "@vercel/node";
import { checkMember, type DiscordEnv } from "../lib/discord.js";
import { signDownload, verifyLicense } from "../lib/tokens.js";

// During the 7-day license: hand back key + dl WITHOUT a browser OAuth, but still
// re-check live membership/blacklist so a kicked user is cut off on their next Play.
export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });

  const { lic, machine } = req.body ?? {};
  if (!lic || !machine) return res.status(400).json({ ok: false, reason: "bad_request" });

  const check = await verifyLicense(String(lic), String(machine));
  if (!check.ok) return res.status(401).json({ ok: false, reason: "expired" });

  const env: DiscordEnv = {
    clientId: process.env.DISCORD_CLIENT_ID!,
    clientSecret: process.env.DISCORD_CLIENT_SECRET!,
    botToken: process.env.DISCORD_BOT_TOKEN!,
    guildId: process.env.SEGA_GUILD_ID!,
    redirectUri: "http://127.0.0.1:51789/callback",
    blacklist: (process.env.BLACKLIST ?? "").split(",").map((s) => s.trim()).filter(Boolean),
  };

  const member = await checkMember(check.uid, env);
  if (!member.ok) {
    const status = member.reason === "discord_error" ? 502 : 403;
    return res.status(status).json(member);
  }

  const key = process.env.GAME_KEY;
  if (!key) return res.status(500).json({ ok: false, reason: "server_misconfig" });

  const dl = await signDownload(check.uid);
  return res.status(200).json({ ok: true, key, dl });
}
```

- [ ] **Step 2: Build-check**

Run: `cd vercel-app && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add vercel-app/api/unlock.ts
git commit -m "feat(backend): /api/unlock issues key via license token (no OAuth)"
```

---

# PHASE B — Backend: request-a-source (direct-to-GitHub)

### Task B1: GitHub App helper (`lib/github.ts`)

**Files:**
- Create: `vercel-app/lib/github.ts`

GitHub App private key (PKCS8) lives in `GH_APP_PRIVATE_KEY`; `jose` signs the App JWT.

- [ ] **Step 1: Implement**

```ts
// vercel-app/lib/github.ts
import { SignJWT, importPKCS8 } from "jose";

const GH = "https://api.github.com";

// Mint a short-lived installation token scoped to ONE repo, contents:write only.
export async function scopedToken(repo: string): Promise<string> {
  const pk = await importPKCS8(process.env.GH_APP_PRIVATE_KEY!.replace(/\\n/g, "\n"), "RS256");
  const now = Math.floor(Date.now() / 1000);
  const appJwt = await new SignJWT({})
    .setProtectedHeader({ alg: "RS256" })
    .setIssuer(process.env.GH_APP_ID!)
    .setIssuedAt(now - 30)
    .setExpirationTime(now + 540) // 9 min — App JWT max is 10
    .sign(pk);

  const res = await fetch(`${GH}/app/installations/${process.env.GH_APP_INSTALL_ID}/access_tokens`, {
    method: "POST",
    headers: { Authorization: `Bearer ${appJwt}`, Accept: "application/vnd.github+json" },
    body: JSON.stringify({ repositories: [repo.split("/")[1]], permissions: { contents: "write" } }),
  });
  if (!res.ok) throw new Error(`gh token ${res.status}`);
  return ((await res.json()) as { token: string }).token;
}

export async function ghJson(token: string, path: string, init?: RequestInit) {
  const res = await fetch(`${GH}${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${token}`, Accept: "application/vnd.github+json", ...(init?.headers ?? {}) },
  });
  if (!res.ok) throw new Error(`gh ${path} ${res.status}`);
  return res.json();
}

// Most recent release tagged req-<uid>-* (used for rate-limiting + idempotency).
export async function latestUserReleaseTs(token: string, repo: string, uid: string): Promise<number> {
  const list = (await ghJson(token, `/repos/${repo}/releases?per_page=20`)) as Array<{ tag_name: string }>;
  let newest = 0;
  for (const r of list) {
    const m = r.tag_name.match(new RegExp(`^req-${uid}-(\\d+)$`));
    if (m) newest = Math.max(newest, Number(m[1]));
  }
  return newest;
}

export async function createDraftRelease(token: string, repo: string, tag: string, name: string) {
  const r = (await ghJson(token, `/repos/${repo}/releases`, {
    method: "POST",
    body: JSON.stringify({ tag_name: tag, name, draft: true, body: "Pending source request." }),
  })) as { id: number; upload_url: string };
  return { id: r.id, uploadBase: r.upload_url.replace(/\{.*$/, "") };
}

export async function publishRelease(token: string, repo: string, id: number) {
  const r = (await ghJson(token, `/repos/${repo}/releases/${id}`, {
    method: "PATCH", body: JSON.stringify({ draft: false }),
  })) as { assets: Array<{ browser_download_url: string }>; html_url: string };
  return { asset: r.assets[0]?.browser_download_url ?? r.html_url, page: r.html_url };
}
```

- [ ] **Step 2: Build-check**

Run: `cd vercel-app && npx tsc --noEmit`
Expected: no errors. (Fix the obvious typo if tsc flags the `createDraftRelease` return — it must be `{ id: r.id, uploadBase: ... }`.)

- [ ] **Step 3: Commit**

```bash
git add vercel-app/lib/github.ts
git commit -m "feat(backend): GitHub App scoped-token + release helpers"
```

---

### Task B2: `api/request/init.ts`

**Files:**
- Create: `vercel-app/api/request/init.ts`

- [ ] **Step 1: Implement**

```ts
// vercel-app/api/request/init.ts
import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyLicense } from "../../lib/tokens.js";
import { scopedToken, latestUserReleaseTs, createDraftRelease } from "../../lib/github.js";

const MAX_BYTES = 500 * 1024 * 1024; // 500 MB
const RATE_MS = 5 * 60 * 1000;       // 1 request / 5 min

export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });
  const { lic, machine, filename, size } = req.body ?? {};
  if (!lic || !machine || !filename || typeof size !== "number")
    return res.status(400).json({ ok: false, reason: "bad_request" });

  const check = await verifyLicense(String(lic), String(machine)); // members-only
  if (!check.ok) return res.status(401).json({ ok: false, reason: "expired" });
  if (!/\.zip$/i.test(String(filename))) return res.status(400).json({ ok: false, reason: "not_zip" });
  if (size <= 0 || size > MAX_BYTES) return res.status(400).json({ ok: false, reason: "too_big" });

  const repo = process.env.GH_REQUESTS_REPO!; // e.g. "jazzytvhome/sega-requests"
  const token = await scopedToken(repo);

  const last = await latestUserReleaseTs(token, repo, check.uid);
  if (Date.now() - last < RATE_MS) return res.status(429).json({ ok: false, reason: "rate_limited" });

  const ts = Date.now();
  const tag = `req-${check.uid}-${ts}`;
  const { id, uploadBase } = await createDraftRelease(token, repo, tag, `request ${check.uid}`);
  const assetName = `${ts}-${String(filename).replace(/[^a-zA-Z0-9._-]/g, "_")}`;
  const uploadUrl = `${uploadBase}?name=${encodeURIComponent(assetName)}`;

  // token is repo-scoped, contents:write only, ~9 min TTL.
  return res.status(200).json({ ok: true, uploadUrl, token, releaseId: id, assetName });
}
```

- [ ] **Step 2: Build-check**

Run: `cd vercel-app && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add vercel-app/api/request/init.ts
git commit -m "feat(backend): request/init — validate, rate-limit, draft release + scoped token"
```

---

### Task B3: `api/request/finalize.ts`

**Files:**
- Create: `vercel-app/api/request/finalize.ts`

- [ ] **Step 1: Implement**

```ts
// vercel-app/api/request/finalize.ts
import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyLicense } from "../../lib/tokens.js";
import { scopedToken, publishRelease } from "../../lib/github.js";

export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });
  const { lic, machine, releaseId, source, notes } = req.body ?? {};
  if (!lic || !machine || !releaseId)
    return res.status(400).json({ ok: false, reason: "bad_request" });

  const check = await verifyLicense(String(lic), String(machine));
  if (!check.ok) return res.status(401).json({ ok: false, reason: "expired" });

  const repo = process.env.GH_REQUESTS_REPO!;
  const token = await scopedToken(repo);
  const { asset, page } = await publishRelease(token, repo, Number(releaseId));

  // Relay only the LINK to staff — webhook URL stays server-side.
  await fetch(process.env.STAFF_WEBHOOK_URL!, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      content:
        `**New source request**\n` +
        `by <@${check.uid}>\n` +
        `source: **${String(source ?? "(unnamed)").slice(0, 120)}**\n` +
        `notes: ${String(notes ?? "").slice(0, 500) || "—"}\n` +
        `download: ${asset}\n(${page})`,
    }),
  });

  return res.status(200).json({ ok: true });
}
```

- [ ] **Step 2: Build-check**

Run: `cd vercel-app && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit + record env**

Add to `vercel-app/.env.example`:
```
LICENSE_SECRET=
LICENSE_DAYS=7
GH_APP_ID=
GH_APP_INSTALL_ID=
GH_APP_PRIVATE_KEY=
GH_REQUESTS_REPO=jazzytvhome/sega-requests
STAFF_WEBHOOK_URL=
```

```bash
git add vercel-app/api/request/finalize.ts vercel-app/.env.example
git commit -m "feat(backend): request/finalize — publish release + post link to staff webhook"
```

---

# PHASE C — Launcher: license gate

### Task C1: `License.cs` + tests

**Files:**
- Create: `launcher/SegaLauncher/License.cs`
- Test: `launcher/SegaLauncher.Tests/LicenseTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// launcher/SegaLauncher.Tests/LicenseTests.cs
using System;
using SegaLauncher;
using Xunit;

public class LicenseTests
{
    // a JWT-shaped token whose payload is {"sub":"u","mid":"m","exp":<far future>}
    static string TokenWithExp(long exp)
    {
        string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var head = B64("{\"alg\":\"HS256\"}");
        var body = B64($"{{\"sub\":\"u\",\"mid\":\"m\",\"exp\":{exp}}}");
        return $"{head}.{body}.sig";
    }

    [Fact]
    public void ExpiryOf_parses_exp()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds();
        var got = License.ExpiryOf(TokenWithExp(exp));
        Assert.NotNull(got);
        Assert.Equal(exp, got!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void IsValid_false_when_expired()
    {
        var past = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        Assert.False(License.IsValid(TokenWithExp(past)));
    }

    [Fact]
    public void IsValid_true_when_future()
    {
        var future = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
        Assert.True(License.IsValid(TokenWithExp(future)));
    }

    [Fact]
    public void MachineId_is_stable_and_nonempty()
    {
        Assert.Equal(License.MachineId(), License.MachineId());
        Assert.False(string.IsNullOrWhiteSpace(License.MachineId()));
    }
}
```

- [ ] **Step 2: Run, confirm fail**

Run: `cd launcher/SegaLauncher.Tests && dotnet test --filter LicenseTests`
Expected: FAIL — `License` not found.

- [ ] **Step 3: Implement `License.cs`**

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SegaLauncher;

/// <summary>
/// Local weekly-license storage. The token is signed server-side; the launcher only
/// reads the expiry to decide whether to re-verify, and trusts the server otherwise.
/// </summary>
public static class License
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SegaPlusLauncher");
    private static string Path_ => Path.Combine(Dir, "license.tok");

    public static void Save(string token)
    {
        Directory.CreateDirectory(Dir);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path_, enc);
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            var dec = ProtectedData.Unprotect(File.ReadAllBytes(Path_), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return null; }
    }

    public static void Clear() { try { File.Delete(Path_); } catch { } }

    public static DateTimeOffset? ExpiryOf(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var json = Encoding.UTF8.GetString(FromB64Url(parts[1]));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exp", out var exp)) return null;
            return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
        }
        catch { return null; }
    }

    public static bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var exp = ExpiryOf(token);
        return exp != null && exp.Value > DateTimeOffset.UtcNow;
    }

    public static int DaysLeft(string token)
    {
        var exp = ExpiryOf(token);
        if (exp == null) return 0;
        return Math.Max(0, (int)Math.Ceiling((exp.Value - DateTimeOffset.UtcNow).TotalDays));
    }

    /// <summary>Stable per-machine id: SHA-256 of MachineGuid + username.</summary>
    public static string MachineId()
    {
        string guid = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = k?.GetValue("MachineGuid") as string ?? "";
        }
        catch { }
        var raw = guid + "|" + Environment.UserName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
```

- [ ] **Step 4: Run, confirm pass**

Run: `cd launcher/SegaLauncher.Tests && dotnet test --filter LicenseTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add launcher/SegaLauncher/License.cs launcher/SegaLauncher.Tests/LicenseTests.cs
git commit -m "feat(launcher): License — DPAPI token store, expiry, machine id"
```

---

### Task C2: `DiscordAuth` captures `lic`; send `machine`

**Files:**
- Modify: `launcher/SegaLauncher/DiscordAuth.cs`

- [ ] **Step 1: Add `Lic` to the result + send machine**

In `AuthResult`, add a field:

```csharp
public sealed record AuthResult(AuthOutcome Outcome, string? Key = null, string? Dl = null, string? Lic = null, string? Message = null);
```

In the POST body, include the machine id:

```csharp
var resp = await Http.PostAsJsonAsync(
    $"{AppConfig.VercelBaseUrl}/api/verify",
    new { code, code_verifier = pkce.Verifier, machine = License.MachineId() }, ct);
```

Update the success return and the `VerifyResponse` record:

```csharp
return new AuthResult(AuthOutcome.Verified, body.key, body.dl, body.lic);
```
```csharp
private sealed record VerifyResponse(bool ok, string? key, string? dl, string? lic, string? reason);
```

- [ ] **Step 2: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds.

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/DiscordAuth.cs
git commit -m "feat(launcher): verify captures license token + sends machine id"
```

---

### Task C3: `LicenseClient.cs` (unlock)

**Files:**
- Create: `launcher/SegaLauncher/LicenseClient.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SegaLauncher;

public enum UnlockOutcome { Ok, Expired, NotMember, Blacklisted, ServerError }
public sealed record UnlockResult(UnlockOutcome Outcome, string? Key = null, string? Dl = null, string? Message = null);

public static class LicenseClient
{
    private static readonly HttpClient Http = new();

    public static async Task<UnlockResult> UnlockAsync(string lic)
    {
        try
        {
            var resp = await Http.PostAsJsonAsync($"{AppConfig.VercelBaseUrl}/api/unlock",
                new { lic, machine = License.MachineId() });

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new UnlockResult(UnlockOutcome.Expired);
            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                var fb = await resp.Content.ReadFromJsonAsync<Resp>();
                return new UnlockResult(fb?.reason == "blacklisted" ? UnlockOutcome.Blacklisted : UnlockOutcome.NotMember,
                    Message: fb?.reason == "blacklisted" ? "This account is blacklisted." : "Members only — re-verify needed.");
            }
            if (!resp.IsSuccessStatusCode)
                return new UnlockResult(UnlockOutcome.ServerError, Message: "Server issue — try again.");

            var body = await resp.Content.ReadFromJsonAsync<Resp>();
            if (body is null || !body.ok || string.IsNullOrEmpty(body.key) || string.IsNullOrEmpty(body.dl))
                return new UnlockResult(UnlockOutcome.ServerError, Message: "Unexpected server response.");
            return new UnlockResult(UnlockOutcome.Ok, body.key, body.dl);
        }
        catch (HttpRequestException)
        {
            return new UnlockResult(UnlockOutcome.ServerError, Message: "Can't reach the server.");
        }
    }

    private sealed record Resp(bool ok, string? key, string? dl, string? reason);
}
```

- [ ] **Step 2: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds.

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/LicenseClient.cs
git commit -m "feat(launcher): LicenseClient.UnlockAsync (key via license token)"
```

---

### Task C4: Wire the gate + unlock-vs-verify into `MainWindow.xaml.cs`

**Files:**
- Modify: `launcher/SegaLauncher/MainWindow.xaml.cs`
- Modify: `launcher/SegaLauncher/MainWindow.xaml` (add `LicensePanel` + a license badge `LicenseBadge` — see Phase D for the skinned version; for now add plain placeholders so code compiles)

This changes the trust model: verify at startup if no valid license; each Play/Install tries `unlock` first and only falls back to Discord when the license is gone.

- [ ] **Step 1: After the update check, run the license gate**

In `RunUpdateCheckAsync`, replace the `else { FadeIn(MainPanel); }` branch with:

```csharp
else
{
    await RunLicenseGateAsync();
}
```

Add the gate method:

```csharp
private string? _lic;

private async Task RunLicenseGateAsync()
{
    _lic = License.Load();
    if (License.IsValid(_lic))
    {
        ShowMain();
        return;
    }
    // no valid license — require verify at startup
    LicensePanel.Visibility = Visibility.Visible;
    MainPanel.Visibility = Visibility.Collapsed;
    FadeIn(LicensePanel);
}

private void ShowMain()
{
    LicensePanel.Visibility = Visibility.Collapsed;
    if (_lic != null) LicenseBadge.Text = $"LICENSE · {License.DaysLeft(_lic)}D LEFT";
    FadeIn(MainPanel);
}

// hooked to the "Verify with Discord" button on LicensePanel
private async void Verify_Click(object sender, RoutedEventArgs e)
{
    var result = await DiscordAuth.RunAsync();
    if (result.Outcome != AuthOutcome.Verified || string.IsNullOrEmpty(result.Lic))
    {
        StatusText.Text = result.Message ?? "Verification failed.";
        return;
    }
    License.Save(result.Lic);
    _lic = result.Lic;
    ShowMain();
}
```

- [ ] **Step 2: Rewrite `DoFlowAsync` to unlock first, fall back to verify**

```csharp
private async Task DoFlowAsync(Mode mode)
{
    if (SourceList.SelectedItem is not SourceItem item) return;
    SetBusy(true);

    string? key = null, dl = null;

    // 1) try the weekly license (no browser)
    if (License.IsValid(_lic))
    {
        StatusText.Text = "Unlocking…";
        var u = await LicenseClient.UnlockAsync(_lic!);
        if (u.Outcome == UnlockOutcome.Ok) { key = u.Key; dl = u.Dl; }
        else if (u.Outcome is UnlockOutcome.Expired) { License.Clear(); _lic = null; }
        else { StatusText.Text = u.Message ?? "Unlock failed."; SetBusy(false); return; }
    }

    // 2) no/!expired license → full Discord verify, then use its key+dl directly
    if (key == null)
    {
        StatusText.Text = "Verify in your browser…";
        var v = await DiscordAuth.RunAsync();
        if (v.Outcome != AuthOutcome.Verified) { StatusText.Text = v.Message ?? "Verification failed."; SetBusy(false); return; }
        if (!string.IsNullOrEmpty(v.Lic)) { License.Save(v.Lic); _lic = v.Lic; LicenseBadge.Text = $"LICENSE · {License.DaysLeft(v.Lic)}D LEFT"; }
        key = v.Key; dl = v.Dl;
    }

    StatusText.Text = "Downloading source… (~15 MB)";
    byte[] blob;
    try { blob = await Downloader.FetchBlobAsync(dl!, item.BlobFile); }
    catch { StatusText.Text = "Download failed — check your connection."; SetBusy(false); return; }

    GameVault vault;
    try { vault = GameVault.Open(blob, Convert.FromBase64String(key!)); }
    catch { StatusText.Text = "Couldn't decrypt (wrong key or corrupt file)."; SetBusy(false); return; }

    if (mode == Mode.Play) StartPlay(vault); else DoInstall(vault, item);
    SetBusy(false);
}
```

- [ ] **Step 3: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds (after Phase-D XAML adds `LicensePanel`, `LicenseBadge`, `Verify_Click`). If building before Phase D, add minimal placeholders to `MainWindow.xaml` first.

- [ ] **Step 4: Commit**

```bash
git add launcher/SegaLauncher/MainWindow.xaml.cs launcher/SegaLauncher/MainWindow.xaml
git commit -m "feat(launcher): startup license gate + unlock-first Play/Install flow"
```

---

# PHASE D — Launcher: scene GUI + boot/tamper splash

### Task D1: Bundle fonts + mp3

**Files:**
- Create: `launcher/SegaLauncher/Assets/Anton.ttf`, `VT323.ttf`, `sega-on-top.mp3`
- Modify: `launcher/SegaLauncher/SegaLauncher.csproj`

- [ ] **Step 1: Add the asset files**

Download Anton + VT323 `.ttf` from Google Fonts into `Assets/`. Copy the existing
`ttsMP3.com_VoiceText_2026-6-4_7-49-18.mp3` (repo root) to `Assets/sega-on-top.mp3`.

```bash
copy "ttsMP3.com_VoiceText_2026-6-4_7-49-18.mp3" "launcher\SegaLauncher\Assets\sega-on-top.mp3"
```

- [ ] **Step 2: Register them in the csproj**

Inside the `<Project>`:

```xml
  <ItemGroup>
    <Resource Include="Assets\Anton.ttf" />
    <Resource Include="Assets\VT323.ttf" />
    <Content Include="Assets\sega-on-top.mp3" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds; assets embedded.

- [ ] **Step 4: Commit**

```bash
git add launcher/SegaLauncher/Assets launcher/SegaLauncher/SegaLauncher.csproj
git commit -m "chore(launcher): bundle Anton/VT323 fonts + boot mp3"
```

---

### Task D2: Scene styles in `App.xaml`

**Files:**
- Modify: `launcher/SegaLauncher/App.xaml`

Port the look from `docs/mockups/launcher-gui.html` (`:root` palette + button/panel styles).

- [ ] **Step 1: Define resources**

Replace the existing `Application.Resources` styles with scene equivalents:
- `SolidColorBrush` keys: `Bg #FF0A0B11`, `Panel #FF0F111C`, `Ink #FFECE6D6`, `Blue #FF3A5BFF`, `Red #FFFF3146`, `Amber #FFFFC23A`, `Green #FF57F5A3`, `Edge #FF23273F`, `Dim #FF6B6F8C`.
- `FontFamily` keys: `Display = pack://application:,,,/Assets/#Anton`, `Term = pack://application:,,,/Assets/#VT323`.
- A brutalist `Button` style `SceneButton` (flat fill, `4,4` `DropShadowEffect` with `ShadowDepth=4 BlurRadius=0 Color=Black`, invert on `IsMouseOver`, press offset on `IsPressed`). Note: `BlurRadius=0` keeps it a hard offset shadow — **no bloom**.
- Variants `SceneButton.Blue`, `.Red`, `.Ghost` via `BasedOn`.

Keep the exact color hexes from the mockup. Reference the mockup's `.btn`, `.btn:hover`, `.btn:active` rules for the trigger values.

- [ ] **Step 2: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds; fonts resolve (no XAML resource errors).

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/App.xaml
git commit -m "feat(launcher): scene/cracktro styles + bundled fonts"
```

---

### Task D3: Rebuild `MainWindow.xaml` (boot, gate, main, request modal)

**Files:**
- Modify: `launcher/SegaLauncher/MainWindow.xaml`

Translate `docs/mockups/launcher-gui.html` to XAML. Keep the **borderless window + custom title bar** (existing `TitleBar_MouseDown`, `Min_Click`, `Close_Click`). Use a CRT-scanline overlay via a tiled `ImageBrush` or a `Rectangle` with an `OpacityMask` (a 1×3px striped `DrawingBrush`).

**Required named elements (the code-behind depends on these exact names):**
- Existing/keep: `CheckPanel`, `UpdatePanel`, `UpdateTitle`, `UpdateMsg`, `UpdateProgress`, `DownloadButton`, `MainPanel`, `SourceList`, `SourceName`, `DescText`, `PlayButton`, `InstallButton`, `StatusText`, `VersionLabel`.
- New: `BootPanel`, `BootHead`, `BootSub`, `BootLog`, `BootMeter`, `LicensePanel` (with a `Verify_Click` button), `LicenseBadge`, and the request modal: `RequestModal`, `ReqName`, `ReqNotes`, `ReqFileLabel`, `ReqSendButton`, `ReqSentPanel`.

**Structure (panels stacked in one Grid, toggled by Visibility — mirrors the existing pattern):**
1. `CheckPanel` (update check spinner) — unchanged.
2. `UpdatePanel` — unchanged.
3. `BootPanel` — terminal: `BootHead` ("CHECKING FOR TAMPERING"), `BootSub`, `BootLog` (a `TextBlock` in a dark box, `Term` font), `BootMeter`.
4. `LicensePanel` — NFO box: "MEMBERS ONLY" stamp, "VERIFY TO ENTER", a `SceneButton.Blue` bound to `Verify_Click`.
5. `MainPanel` — left rail `SourceList` (release list), hero card, `LicenseBadge`, `PlayButton`/`InstallButton`, a `+ REQUEST A SOURCE` button (`Request_Click`), marquee `TextBlock` animated via a `Storyboard` (`DoubleAnimation` on a `TranslateTransform.X`, `RepeatBehavior=Forever`).
6. `RequestModal` — overlay `Grid` (collapsed) with `ReqName`, `ReqNotes`, a file-pick button (`ReqPick_Click`) + `ReqFileLabel`, `ReqSendButton` (`ReqSend_Click`), and `ReqSentPanel`.

- [ ] **Step 1: Author the XAML** following the mockup (colors, fonts, hard shadows, scanline overlay). No colored glows.

- [ ] **Step 2: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds; all named elements resolve against the code-behind.

- [ ] **Step 3: Smoke-run**

Run: `cd launcher/SegaLauncher && dotnet run`
Expected: window opens; update check → boot → (verify if no license) → main. Verify the marquee scrolls and buttons have hard (non-glowing) shadows.

- [ ] **Step 4: Commit**

```bash
git add launcher/SegaLauncher/MainWindow.xaml
git commit -m "feat(launcher): scene-skinned boot/verify/main + request modal"
```

---

### Task D4: Boot splash logic + real tamper check + mp3

**Files:**
- Modify: `launcher/SegaLauncher/MainWindow.xaml.cs`
- Reuse: `launcher/SegaLauncher/Integrity.cs`, `launcher/SegaLauncher/Updater.cs`

The boot screen runs after the update check, before the license gate. It plays the mp3, animates a spinner + ASCII meter, and does a **real** integrity check: re-hash the running exe and compare to `/api/version`'s `sha256` (already fetched by `Updater`).

- [ ] **Step 1: Add the boot sequence**

In `RunUpdateCheckAsync`, the non-outdated branch becomes:

```csharp
else
{
    await RunBootCheckAsync(info?.sha256);
    await RunLicenseGateAsync();
}
```

Add:

```csharp
using System.Media;            // SoundPlayer (wav) — for mp3 use MediaPlayer
using System.Windows.Media;    // MediaPlayer

private readonly MediaPlayer _boot = new();

private async Task RunBootCheckAsync(string? expectedExeSha)
{
    BootPanel.Visibility = Visibility.Visible;
    FadeIn(BootPanel);
    PlayBootSound();

    var lines = new[]
    {
        "SEGA+ SECURE LOADER  v" + AppConfig.Version,
        "(c) SEGA+ ON TOP  //  SKIDDED BY JUSTONEONTOP",
        "--------------------------------------------",
        "> mount vault.enc ................. ok",
        "> sha-256 self-check ............. running",
    };
    BootLog.Text = "";
    foreach (var l in lines) { BootLog.Text += l + "\n"; await Task.Delay(360); }

    // real tamper check: hash our own exe, compare to /api/version sha256
    bool intact = true;
    try
    {
        var exe = Environment.ProcessPath!;
        var hash = Integrity.Sha256Hex(await File.ReadAllBytesAsync(exe));
        intact = string.IsNullOrEmpty(expectedExeSha) || hash.Equals(expectedExeSha.Trim(), StringComparison.OrdinalIgnoreCase);
        BootLog.Text += intact ? "> integrity ...................... INTACT\n" : "> integrity ...................... MISMATCH\n";
    }
    catch { /* dev run / single-file edge: don't hard-fail */ }

    // ASCII meter to 100%
    for (int n = 1; n <= 18; n++) { BootMeter.Text = $"SCAN [{new string('#', n)}{new string('.', 18 - n)}] {n * 100 / 18}%"; await Task.Delay(60); }

    BootHead.Text = intact ? "✓ INTEGRITY VERIFIED" : "⚠ TAMPER DETECTED";
    BootLog.Text += intact ? "\n[ OK ]  NO TAMPERING DETECTED\n" : "\n[ !! ]  REINSTALL FROM THE OFFICIAL RELEASE\n";
    await Task.Delay(900);
    BootPanel.Visibility = Visibility.Collapsed;
}

private void PlayBootSound()
{
    try
    {
        var uri = new Uri("pack://siteoforigin:,,,/Assets/sega-on-top.mp3");
        _boot.Open(new Uri(AppContext.BaseDirectory + "Assets/sega-on-top.mp3"));
        _boot.Play();
    }
    catch { }
}
```

> Total boot ≈ 5×0.36 + 18×0.06 + 0.9 ≈ **3.8 s** (matches the ~4 s decision).

- [ ] **Step 2: Build + run**

Run: `cd launcher/SegaLauncher && dotnet run`
Expected: boot prints line-by-line, plays "Sega+ on top", meter fills, then hands to verify/main.

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/MainWindow.xaml.cs
git commit -m "feat(launcher): boot splash — real exe-hash tamper check + mp3"
```

---

# PHASE E — Launcher: request-a-source client

### Task E1: `RequestClient.cs`

**Files:**
- Create: `launcher/SegaLauncher/RequestClient.cs`

- [ ] **Step 1: Implement the 3-step upload**

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SegaLauncher;

public sealed record ReqResult(bool Ok, string? Message = null);

public static class RequestClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };
    public const long MaxBytes = 500L * 1024 * 1024;

    public static async Task<ReqResult> SubmitAsync(string lic, string source, string notes, string zipPath)
    {
        var fi = new FileInfo(zipPath);
        if (!fi.Exists) return new ReqResult(false, "File not found.");
        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return new ReqResult(false, "Must be a .zip.");
        if (fi.Length > MaxBytes) return new ReqResult(false, "Max 500 MB.");

        var machine = License.MachineId();
        try
        {
            // 1) init
            var initResp = await Http.PostAsJsonAsync($"{AppConfig.VercelBaseUrl}/api/request/init",
                new { lic, machine, filename = fi.Name, size = fi.Length });
            if ((int)initResp.StatusCode == 429) return new ReqResult(false, "Slow down — 1 request per 5 min.");
            if (!initResp.IsSuccessStatusCode) return new ReqResult(false, "Couldn't start the request.");
            var init = await initResp.Content.ReadFromJsonAsync<InitResp>();
            if (init is null || !init.ok) return new ReqResult(false, "Couldn't start the request.");

            // 2) PUT the zip straight to GitHub with the scoped token
            using (var content = new StreamContent(File.OpenRead(zipPath)))
            {
                content.Headers.ContentType = new("application/zip");
                using var put = new HttpRequestMessage(HttpMethod.Post, init.uploadUrl) { Content = content };
                put.Headers.Add("Authorization", $"Bearer {init.token}");
                put.Headers.Add("Accept", "application/vnd.github+json");
                var putResp = await Http.SendAsync(put);
                if (!putResp.IsSuccessStatusCode) return new ReqResult(false, "Upload failed.");
            }

            // 3) finalize
            var finResp = await Http.PostAsJsonAsync($"{AppConfig.VercelBaseUrl}/api/request/finalize",
                new { lic, machine, releaseId = init.releaseId, source, notes });
            if (!finResp.IsSuccessStatusCode) return new ReqResult(false, "Couldn't finalize the request.");
            return new ReqResult(true);
        }
        catch (HttpRequestException) { return new ReqResult(false, "Network error — try again."); }
    }

    private sealed record InitResp(bool ok, string uploadUrl, string token, long releaseId, string assetName);
}
```

> Note: GitHub release-asset upload uses **POST** to the upload URL (not PUT). The init endpoint returns the full `uploadUrl` already including `?name=`.

- [ ] **Step 2: Build**

Run: `cd launcher/SegaLauncher && dotnet build`
Expected: builds.

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/RequestClient.cs
git commit -m "feat(launcher): RequestClient — init → upload zip → finalize"
```

---

### Task E2: Wire the request modal in `MainWindow.xaml.cs`

**Files:**
- Modify: `launcher/SegaLauncher/MainWindow.xaml.cs`

- [ ] **Step 1: Add handlers**

```csharp
using Microsoft.Win32; // OpenFileDialog

private string? _reqZip;

private void Request_Click(object sender, RoutedEventArgs e)
{
    ReqName.Text = ""; ReqNotes.Text = ""; _reqZip = null;
    ReqFileLabel.Text = "no file chosen";
    ReqSentPanel.Visibility = Visibility.Collapsed;
    RequestModal.Visibility = Visibility.Visible;
}

private void ReqClose_Click(object sender, RoutedEventArgs e) => RequestModal.Visibility = Visibility.Collapsed;

private void ReqPick_Click(object sender, RoutedEventArgs e)
{
    var dlg = new OpenFileDialog { Title = "Choose a .zip build", Filter = "Zip archives (*.zip)|*.zip" };
    if (dlg.ShowDialog() == true)
    {
        var fi = new System.IO.FileInfo(dlg.FileName);
        if (fi.Length > RequestClient.MaxBytes) { ReqFileLabel.Text = "TOO BIG (max 500 MB)"; _reqZip = null; return; }
        _reqZip = dlg.FileName;
        ReqFileLabel.Text = $"{fi.Name} · {fi.Length / 1048576.0:0.0} MB";
    }
}

private async void ReqSend_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrWhiteSpace(ReqName.Text)) { ReqName.Focus(); return; }
    if (_reqZip == null) { ReqFileLabel.Text = "ADD A .ZIP FIRST"; return; }
    if (!License.IsValid(_lic)) { ReqFileLabel.Text = "Re-verify first (license expired)."; return; }

    ReqSendButton.IsEnabled = false; ReqSendButton.Content = "UPLOADING…";
    var r = await RequestClient.SubmitAsync(_lic!, ReqName.Text.Trim(), ReqNotes.Text.Trim(), _reqZip);
    ReqSendButton.IsEnabled = true; ReqSendButton.Content = "SEND REQUEST";

    if (r.Ok) ReqSentPanel.Visibility = Visibility.Visible;
    else ReqFileLabel.Text = r.Message ?? "Failed.";
}
```

- [ ] **Step 2: Build + run, exercise the modal**

Run: `cd launcher/SegaLauncher && dotnet run`
Expected: `+ REQUEST A SOURCE` opens the modal; picking a non-zip is blocked by the dialog filter; sending without a name/zip is blocked. (Full upload needs the live backend.)

- [ ] **Step 3: Commit**

```bash
git add launcher/SegaLauncher/MainWindow.xaml.cs
git commit -m "feat(launcher): wire request-a-source modal to RequestClient"
```

---

# PHASE F — Verify, obfuscation, release

### Task F1: Full test pass + obfuscation guard

- [ ] **Step 1: Run all tests**

Run: `cd launcher/SegaLauncher.Tests && dotnet test` → expect existing 28 + 4 new = **32 pass**.
Run: `cd vercel-app && npx vitest run` → expect license tests pass.

- [ ] **Step 2: Confirm Obfuscar leaves XAML/JSON-safe types alone**

Open `launcher/SegaLauncher/Obfuscar.xml`; confirm the existing skip rules cover the new
`License`, `LicenseClient`, `RequestClient` JSON DTOs (records used by `System.Text.Json` must
keep member names). Add `SkipType`/`SkipField` entries for the new `*Resp`/`InitResp` records if
they're renamed. Third-party assemblies are untouched by default.

- [ ] **Step 3: Obfuscated build smoke**

Run: `cd "C:\Users\jazzy\Downloads\slow roads"; .\package.ps1 -Key "<GAME_KEY>"`
Expected: builds the single-file exe; it launches; boot + verify + a Play round-trip works.

- [ ] **Step 4: Commit any Obfuscar.xml change**

```bash
git add launcher/SegaLauncher/Obfuscar.xml
git commit -m "chore(launcher): keep new JSON DTO members un-obfuscated"
```

---

### Task F2: Backend env + deploy (user-run)

The user runs Vercel deploys (see `sega-launcher-deploy-constraint`). Provide the steps.

- [ ] **Step 1: Create the GitHub App + requests repo**

- New private repo `jazzytvhome/sega-requests`.
- New GitHub App (owner: jazzytvhome): permission **Repository contents: Read & write**; install it on **only** `sega-requests`. Note the App ID, the Installation ID, generate a private key (`.pem`).

- [ ] **Step 2: Set Vercel env (put commands in a `.ps1`, never paste multi-token CLI lines)**

Create `vercel-app/set-license-env.ps1`:
```powershell
vercel env add LICENSE_SECRET production
vercel env add LICENSE_DAYS production
vercel env add GH_APP_ID production
vercel env add GH_APP_INSTALL_ID production
vercel env add GH_APP_PRIVATE_KEY production
vercel env add GH_REQUESTS_REPO production
vercel env add STAFF_WEBHOOK_URL production
```
(Run it, paste each value when prompted. `GH_APP_PRIVATE_KEY` = the PEM with literal `\n` escaped, matching the `.replace(/\\n/g,"\n")` in `lib/github.ts`.)

- [ ] **Step 3: Deploy + verify**

User runs `cd vercel-app; .\deploy.ps1`. Then:
```bash
curl -s -X POST https://vercel-app-seven-lake.vercel.app/api/unlock -H "Content-Type: application/json" -d "{}"
```
Expected: `400 {"ok":false,"reason":"bad_request"}` (endpoint live).

- [ ] **Step 4: End-to-end manual check**

Fresh launcher → verify with Discord → confirm `license.tok` written → close + reopen →
goes straight to main (no browser) → Play works via unlock → Request a small `.zip` → confirm
it lands in `sega-requests` releases and the staff webhook gets the link.

- [ ] **Step 5: Bump version + release** (per `sega-launcher-release-loop`)

Bump `AppConfig.Version` → 1.0.4, `package.ps1 -Key …`, `gh release create v1.0.4 …`, set
`LATEST_VERSION`/`LATEST_SHA256`, user deploys, verify `/api/version`.

---

## Self-review notes

- **Spec coverage:** GUI skin (D2/D3), boot+mp3+real tamper check (D1/D4), 7-day license sign/verify (A1), machine-bound + DPAPI (C1), verify issues lic (A3), unlock path (A4/C3/C4), members-only + rate-limit (B2), direct-to-GitHub 500 MB upload (B1–B3, E1), webhook-link-only (B3), tests (A1/C1/F1). All spec sections map to a task.
- **Type consistency:** `AuthResult` gains `Lic`; `UnlockResult`/`UnlockOutcome`, `ReqResult`, `InitResp` defined once and reused. Server `verifyLicense` returns `{ok,uid}` consumed identically by unlock/init/finalize. Named XAML elements listed once in D3 and consumed by C4/D4/E2.
- **Known follow-ups:** the GitHub asset upload verb is **POST** to the `upload_url` host (`uploads.github.com`); `lib/github.ts` returns `uploadBase` from `release.upload_url`, so `init` hands the launcher the correct host automatically.
