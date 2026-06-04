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
