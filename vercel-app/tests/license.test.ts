import { describe, it, expect, beforeAll } from "vitest";
import { signLicense, verifyLicense } from "../lib/tokens.js";

beforeAll(() => { process.env.LICENSE_SECRET = "test-secret-test-secret-test-1234"; });

describe("license token", () => {
  it("round-trips uid + machine", async () => {
    const tok = await signLicense("user123", "machineABC");
    const r = await verifyLicense(tok, "machineABC");
    expect(r).toEqual({ ok: true, uid: "user123" });
  });

  it("rejects a wrong machine id", async () => {
    const tok = await signLicense("user123", "machineABC");
    expect(await verifyLicense(tok, "OTHER")).toEqual({ ok: false });
  });

  it("rejects garbage", async () => {
    expect(await verifyLicense("not.a.jwt", "machineABC")).toEqual({ ok: false });
  });

  it("rejects a token signed with a different secret", async () => {
    process.env.LICENSE_SECRET = "secret-AAAA-secret-AAAA-secret-12";
    const tok = await signLicense("user123", "machineABC");
    process.env.LICENSE_SECRET = "secret-BBBB-secret-BBBB-secret-34";
    expect(await verifyLicense(tok, "machineABC")).toEqual({ ok: false });
    process.env.LICENSE_SECRET = "test-secret-test-secret-test-1234"; // restore for other tests
  });
});
