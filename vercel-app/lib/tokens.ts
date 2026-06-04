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

// --- weekly license token (separate secret from the download/game key) ---
function licenseSecret(): Uint8Array {
  const s = process.env.LICENSE_SECRET;
  if (!s) throw new Error("LICENSE_SECRET not set");
  return new TextEncoder().encode(s);
}

const LICENSE_DAYS = Number(process.env.LICENSE_DAYS ?? "7");

export async function signLicense(uid: string, machine: string): Promise<string> {
  return await new SignJWT({ scope: "license", mid: machine })
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(uid)
    .setIssuedAt()
    .setExpirationTime(`${LICENSE_DAYS}d`)
    .sign(licenseSecret());
}

export type LicenseCheck = { ok: true; uid: string } | { ok: false };

export async function verifyLicense(token: string, machine: string): Promise<LicenseCheck> {
  try {
    const { payload } = await jwtVerify(token, licenseSecret());
    if (payload.scope !== "license" || payload.mid !== machine || !payload.sub) return { ok: false };
    return { ok: true, uid: String(payload.sub) };
  } catch {
    return { ok: false };
  }
}
