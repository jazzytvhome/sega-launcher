import { next } from "@vercel/edge";
import { verifyDownload } from "./lib/tokens.js";

// Gate ONLY the encrypted blob: it can be fetched only with a valid, short-lived
// download token from /api/verify (sent as "Authorization: Bearer <token>").
export const config = {
  matcher: ["/game.enc", "/eaglercraftx.enc"],
};

export default async function middleware(request: Request): Promise<Response> {
  const auth = request.headers.get("authorization") ?? "";
  const m = auth.match(/^Bearer\s+(.+)$/i);
  if (m && (await verifyDownload(m[1]))) return next();
  return new Response("forbidden", { status: 403 });
}
