import { describe, it, expect, beforeAll } from "vitest";
import { signDownload, verifyDownload } from "../lib/tokens.js";

beforeAll(() => {
  // 32 zero-ish bytes, base64 — stands in for GAME_KEY during tests.
  process.env.GAME_KEY = Buffer.from(new Uint8Array(32).fill(7)).toString("base64");
});

describe("download tokens", () => {
  it("round-trips a valid token", async () => {
    const t = await signDownload("user-1");
    expect(await verifyDownload(t)).toBe(true);
  });

  it("rejects garbage", async () => {
    expect(await verifyDownload("not.a.real.token")).toBe(false);
  });
});
