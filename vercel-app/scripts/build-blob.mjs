import { readFileSync, writeFileSync, readdirSync, statSync, mkdirSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { packContainer, encryptBlob } from "./lib/pack.mjs";

const SRC = "game-src";
const OUT = "dist/game.enc";

const keyB64 = process.env.GAME_KEY;
if (!keyB64) {
  console.error(
    "GAME_KEY env var not set. Generate one:\n" +
      "  npm run keygen\n" +
      "Then set it for this shell (PowerShell):\n" +
      "  $env:GAME_KEY = '<base64-from-keygen>'",
  );
  process.exit(1);
}
const key = Buffer.from(keyB64, "base64");
if (key.length !== 32) {
  console.error(`GAME_KEY must decode to 32 bytes (got ${key.length}).`);
  process.exit(1);
}

function walk(dir, base, out) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full, base, out);
    else out.push({ path: relative(base, full).split(sep).join("/"), content: readFileSync(full) });
  }
}

const entries = [];
walk(SRC, SRC, entries);

const container = packContainer(entries);
const blob = encryptBlob(container, key);

mkdirSync("dist", { recursive: true });
writeFileSync(OUT, blob);
console.log(`Built ${OUT}: ${entries.length} files, ${(blob.length / 1048576).toFixed(2)} MB`);
