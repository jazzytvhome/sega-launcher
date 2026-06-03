import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyMembership, type DiscordEnv } from "../lib/discord.js";
import { signDownload } from "../lib/tokens.js";

const REDIRECT_URI = "http://127.0.0.1:51789/callback";

// On a confirmed SEGA+ membership, hand back the AES key that decrypts game.enc.
// The key only ever leaves the server for a verified member — that's the gate.
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
    blacklist: (process.env.BLACKLIST ?? "").split(",").map((s) => s.trim()).filter(Boolean),
  };

  const result = await verifyMembership(code, code_verifier, env);
  if (!result.ok) {
    const status = result.reason === "not_member" || result.reason === "blacklisted" ? 403 : 502;
    return res.status(status).json(result);
  }

  const key = process.env.GAME_KEY;
  if (!key) return res.status(500).json({ ok: false, reason: "server_misconfig" });

  // key decrypts game.enc; dl is a 2-min token that authorizes downloading it.
  const dl = await signDownload(result.user.id);
  return res.status(200).json({ ok: true, key, dl });
}
