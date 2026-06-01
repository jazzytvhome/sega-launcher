import { next, rewrite } from "@vercel/edge";
import { verifyToken } from "./lib/tokens.js";

// Run on everything EXCEPT the API, the denied page, and the public unlock page
// (the unlock page must be reachable without a session cookie — it's what sets it).
export const config = {
  matcher: ["/((?!api/|denied|unlock|favicon|robots).*)"],
};

export default async function middleware(request: Request): Promise<Response> {
  const cookie = request.headers.get("cookie") ?? "";
  const match = cookie.match(/(?:^|;\s*)sega_session=([^;]+)/);
  const token = match?.[1];

  if (token) {
    try {
      const claims = await verifyToken(token, process.env.JWT_SECRET!);
      if (claims.scope === "session") return next();
    } catch {
      /* fall through to denied */
    }
  }
  return rewrite(new URL("/denied", request.url));
}
