import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyToken, signToken } from "../lib/tokens.js";

// The unlock token arrives in the POST body (never the URL/query), so it can't
// leak via request logs or the Referer header. The static /unlock page reads the
// token from the URL fragment and POSTs it here; we trade it for a session cookie.
export default async function handler(req: VercelRequest, res: VercelResponse) {
  if (req.method !== "POST") return res.status(405).json({ ok: false });

  const token = (req.body?.token as string) ?? "";
  const secret = process.env.JWT_SECRET!;
  try {
    const claims = await verifyToken(token, secret);
    if (claims.scope !== "unlock") throw new Error("wrong scope");

    const session = await signToken(
      { sub: claims.sub, name: claims.name, scope: "session" },
      "6h",
      secret,
    );
    res.setHeader(
      "Set-Cookie",
      `sega_session=${session}; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=21600`,
    );
    res.setHeader("Referrer-Policy", "no-referrer");
    return res.status(200).json({ ok: true });
  } catch {
    return res.status(401).json({ ok: false });
  }
}
