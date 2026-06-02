import { readFileSync, writeFileSync } from "node:fs";
import JavaScriptObfuscator from "javascript-obfuscator";

const src = readFileSync("scripts/watermark.src.js", "utf8");
const out = JavaScriptObfuscator.obfuscate(src, {
  compact: true,
  controlFlowFlattening: true,
  stringArray: true,
  stringArrayEncoding: ["base64"],
}).getObfuscatedCode();
writeFileSync("game-src/_app/skid-watermark.js", out);
console.log("obfuscated -> game-src/_app/skid-watermark.js");
