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
