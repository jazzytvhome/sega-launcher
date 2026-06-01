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
