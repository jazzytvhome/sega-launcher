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
