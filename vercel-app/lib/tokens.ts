import { SignJWT, jwtVerify } from "jose";

// HMAC secret = the 32-byte GAME_KEY (base64). Reused so there's no extra env var.
// atob is available in both the Node and Edge runtimes.
function secret(): Uint8Array {
  const bin = atob(process.env.GAME_KEY ?? "");
  const a = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i);
  return a;
}

export async function signDownload(sub: string): Promise<string> {
  return await new SignJWT({ scope: "download" })
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime("2m")
    .sign(secret());
}

export async function verifyDownload(token: string): Promise<boolean> {
  try {
    const { payload } = await jwtVerify(token, secret());
    return payload.scope === "download";
  } catch {
    return false;
  }
}
