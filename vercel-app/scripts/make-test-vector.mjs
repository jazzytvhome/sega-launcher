import { writeFileSync, mkdirSync } from "node:fs";
import { packContainer, encryptBlob } from "./lib/pack.mjs";

// Deterministic key so the C# test can hardcode it. (Test-only — NOT the real GAME_KEY.)
const KEY_B64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="; // bytes 0x00..0x1F
const key = Buffer.from(KEY_B64, "base64");

const entries = [
  { path: "hello.txt", content: Buffer.from("SEGA+ ON TOP", "utf8") },
  { path: "_app/inner.txt", content: Buffer.from("nested-ok", "utf8") },
];

const blob = encryptBlob(packContainer(entries), key);

const outDir = "../launcher/SegaLauncher.Tests/fixtures";
mkdirSync(outDir, { recursive: true });
writeFileSync(`${outDir}/vector.enc`, blob);
console.log(`Wrote ${outDir}/vector.enc (key b64 = ${KEY_B64})`);
