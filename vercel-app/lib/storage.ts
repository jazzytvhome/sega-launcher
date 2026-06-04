import { AwsClient } from "aws4fetch";

function client(): AwsClient {
  return new AwsClient({
    accessKeyId: process.env.R2_ACCESS_KEY_ID!,
    secretAccessKey: process.env.R2_SECRET_ACCESS_KEY!,
    service: "s3",
    region: "auto",
  });
}
// Defense-in-depth: every keyed operation goes through this. Object keys are server-built as
// `requests/<uid>/<ts>-<sanitized>`; reject anything else (path traversal, empty segments, etc.)
// so a key can never be coerced outside that shape even if a future caller forgets to validate.
function assertSafeKey(key: string): void {
  if (key.includes("..") || key.includes("//") || !/^requests\/\d+\/\d+-[A-Za-z0-9._-]+$/.test(key))
    throw new Error("unsafe object key");
}
function objectUrl(key: string): string {
  assertSafeKey(key);
  return `https://${process.env.R2_ACCOUNT_ID}.r2.cloudflarestorage.com/${process.env.R2_BUCKET}/${key}`;
}

// One-object, PUT-only, short-lived upload URL. No content-type is signed so the
// client can PUT raw bytes with no special headers.
export async function presignPut(key: string, expiresSec = 600): Promise<string> {
  const url = new URL(objectUrl(key));
  url.searchParams.set("X-Amz-Expires", String(expiresSec));
  const signed = await client().sign(url.toString(), { method: "PUT", aws: { signQuery: true } });
  return signed.url;
}

// Short-lived signed download link for staff (max 7 days for SigV4).
export async function presignGet(key: string, expiresSec = 604800): Promise<string> {
  const url = new URL(objectUrl(key));
  url.searchParams.set("X-Amz-Expires", String(expiresSec));
  const signed = await client().sign(url.toString(), { method: "GET", aws: { signQuery: true } });
  return signed.url;
}

export async function objectExists(key: string): Promise<{ exists: boolean; size: number }> {
  const res = await client().fetch(objectUrl(key), { method: "HEAD" });
  if (res.status === 200) return { exists: true, size: Number(res.headers.get("content-length") ?? 0) };
  return { exists: false, size: 0 };
}

// Best-effort rate-limit state: newest <ts> among this user's already-uploaded objects.
export async function latestUserObjectTs(uid: string): Promise<number> {
  const url = new URL(`https://${process.env.R2_ACCOUNT_ID}.r2.cloudflarestorage.com/${process.env.R2_BUCKET}/`);
  url.searchParams.set("list-type", "2");
  url.searchParams.set("prefix", `requests/${uid}/`);
  const res = await client().fetch(url.toString(), { method: "GET" });
  if (!res.ok) return 0;
  const xml = await res.text();
  let newest = 0;
  for (const m of xml.matchAll(new RegExp(`requests/${uid}/(\\d+)-`, "g"))) newest = Math.max(newest, Number(m[1]));
  return newest;
}
