import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyLicense } from "../../lib/tokens.js";
import { presignGet, objectExists } from "../../lib/storage.js";

export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });
  const { lic, machine, key, source, notes } = req.body ?? {};
  if (!lic || !machine || !key) return res.status(400).json({ ok: false, reason: "bad_request" });

  const check = await verifyLicense(String(lic), String(machine));
  if (!check.ok) return res.status(401).json({ ok: false, reason: "expired" });
  // The key is client-supplied here, so a prefix check alone is not enough: validate the FULL
  // shape, bound to the authenticated uid, so a crafted key (path traversal / extra segments)
  // can't reference another user's object. uid is a numeric Discord snowflake from the signed JWT.
  const keyRe = new RegExp(`^requests/${check.uid}/\\d+-[A-Za-z0-9._-]+$`);
  if (!keyRe.test(String(key)))
    return res.status(403).json({ ok: false, reason: "not_owner" });

  const obj = await objectExists(String(key));
  if (!obj.exists) return res.status(400).json({ ok: false, reason: "no_object" });

  const link = await presignGet(String(key), 604800);
  try {
    await fetch(process.env.STAFF_WEBHOOK_URL!, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        content:
          `**New source request**\n` +
          `by <@${check.uid}>\n` +
          `source: **${String(source ?? "(unnamed)").slice(0, 120)}**\n` +
          `notes: ${String(notes ?? "").slice(0, 500) || "—"}\n` +
          `size: ${(obj.size / 1048576).toFixed(1)} MB\n` +
          `download (7d): ${link}`,
        allowed_mentions: { parse: [] },
      }),
    });
  } catch { /* object is stored; webhook is best-effort */ }
  return res.status(200).json({ ok: true });
}
