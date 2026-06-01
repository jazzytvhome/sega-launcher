import type { VercelRequest, VercelResponse } from "@vercel/node";
import { verifyToken, signToken } from "../lib/tokens.js";

export default async function handler(req: VercelRequest, res: VercelResponse) {
  const token = (req.query.token as string) ?? "";
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
    res.statusCode = 302;
    res.setHeader("Location", "/");
    return res.end();
  } catch {
    res.statusCode = 302;
    res.setHeader("Location", "/denied");
    return res.end();
  }
}
