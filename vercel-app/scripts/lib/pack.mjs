import { deflateRawSync } from "node:zlib";
import { createCipheriv, randomBytes } from "node:crypto";

// Container format (plaintext, before encryption):
//   "SEGA1" (5 bytes ascii) | entryCount (u32 LE)
//   per entry: pathLen (u16 LE) | path (utf8) | compLen (u32 LE) | deflateRaw(content)
// deflateRaw matches C# DeflateStream (raw deflate, no zlib header) — zero deps both sides.
export function packContainer(entries) {
  const chunks = [];
  const header = Buffer.alloc(9);
  header.write("SEGA1", 0, "ascii");
  header.writeUInt32LE(entries.length, 5);
  chunks.push(header);
  for (const e of entries) {
    const comp = deflateRawSync(e.content, { level: 9 });
    const pathBuf = Buffer.from(e.path, "utf8");
    const meta = Buffer.alloc(2 + pathBuf.length + 4);
    meta.writeUInt16LE(pathBuf.length, 0);
    pathBuf.copy(meta, 2);
    meta.writeUInt32LE(comp.length, 2 + pathBuf.length);
    chunks.push(meta, comp);
  }
  return Buffer.concat(chunks);
}

// Encrypted blob format: nonce (12) | tag (16) | ciphertext  (AES-256-GCM)
export function encryptBlob(container, key) {
  const nonce = randomBytes(12);
  const cipher = createCipheriv("aes-256-gcm", key, nonce);
  const ct = Buffer.concat([cipher.update(container), cipher.final()]);
  const tag = cipher.getAuthTag();
  return Buffer.concat([nonce, tag, ct]);
}
