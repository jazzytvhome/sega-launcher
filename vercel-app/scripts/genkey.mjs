import { randomBytes } from "node:crypto";
// 32-byte AES-256 key, base64. Use as GAME_KEY (Vercel env + local build shell).
console.log(randomBytes(32).toString("base64"));
