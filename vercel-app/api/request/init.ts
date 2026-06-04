import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyLicense } from "../../lib/tokens.js";
import { presignPut, latestUserObjectTs } from "../../lib/storage.js";

const MAX_BYTES = 500 * 1024 * 1024;
const RATE_MS = 5 * 60 * 1000;

export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });
  const { lic, machine, filename, size } = req.body ?? {};
  if (!lic || !machine || !filename || typeof size !== "number")
    return res.status(400).json({ ok: false, reason: "bad_request" });

  const check = await verifyLicense(String(lic), String(machine));
  if (!check.ok) return res.status(401).json({ ok: false, reason: "expired" });
  if (!/\.zip$/i.test(String(filename))) return res.status(400).json({ ok: false, reason: "not_zip" });
  if (size <= 0 || size > MAX_BYTES) return res.status(400).json({ ok: false, reason: "too_big" });

  const last = await latestUserObjectTs(check.uid);
  if (Date.now() - last < RATE_MS) return res.status(429).json({ ok: false, reason: "rate_limited" });

  const ts = Date.now();
  const safe = String(filename).replace(/[^a-zA-Z0-9._-]/g, "_");
  const key = `requests/${check.uid}/${ts}-${safe}`;
  const uploadUrl = await presignPut(key, 600);
  return res.status(200).json({ ok: true, uploadUrl, key });
}
