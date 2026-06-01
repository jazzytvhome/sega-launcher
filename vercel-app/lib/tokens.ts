import { SignJWT, jwtVerify, type JWTPayload } from "jose";

export type Scope = "unlock" | "session";
export interface TokenClaims extends JWTPayload {
  sub: string;
  name: string;
  scope: Scope;
}

function key(secret: string): Uint8Array {
  return new TextEncoder().encode(secret);
}

export async function signToken(
  claims: { sub: string; name: string; scope: Scope },
  expiresIn: string,
  secret: string,
): Promise<string> {
  return await new SignJWT({ name: claims.name, scope: claims.scope })
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(claims.sub)
    .setIssuedAt()
    .setExpirationTime(expiresIn)
    .sign(key(secret));
}

export async function verifyToken(token: string, secret: string): Promise<TokenClaims> {
  const { payload } = await jwtVerify(token, key(secret));
  return payload as TokenClaims;
}
