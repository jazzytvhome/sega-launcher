// vercel-app/api/request/finalize.ts
import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyLicense } from "../../lib/tokens.js";
import { scopedToken, publishRelease, getRelease } from "../../lib/github.js";

export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "method" });
  const { lic, machine, releaseId, source, notes } = req.body ?? {};
  if (!lic || !machine || !releaseId)
    return res.status(400).json({ ok: false, reason: "bad_request" });

  const check = await verifyLicense(String(lic), String(machine));
  if (!check.ok) return res.status(401).json({ ok: false, reason: "expired" });

  const repo = process.env.GH_REQUESTS_REPO!;
  const token = await scopedToken(repo);

  // Verify the release belongs to this caller and has a completed upload.
  const rel = await getRelease(token, repo, Number(releaseId));
  if (!rel.tag_name.startsWith(`req-${check.uid}-`))
    return res.status(403).json({ ok: false, reason: "not_owner" });
  if (rel.assets.length === 0)
    return res.status(400).json({ ok: false, reason: "no_asset" });

  const { asset, page } = await publishRelease(token, repo, Number(releaseId));

  // Relay only the LINK to staff — webhook URL stays server-side.
  // Wrap in try/catch so a webhook failure never blocks the response.
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
          `download: ${asset}\n(${page})`,
        allowed_mentions: { parse: [] },
      }),
    });
  } catch { /* release is live; webhook is best-effort */ }

  return res.status(200).json({ ok: true });
}
