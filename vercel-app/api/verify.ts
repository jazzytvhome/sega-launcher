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
