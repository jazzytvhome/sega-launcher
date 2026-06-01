import { describe, it, expect } from "vitest";
import { signToken, verifyToken } from "../lib/tokens.js";

const SECRET = "test-secret-test-secret-test-secret-32";

describe("tokens", () => {
  it("round-trips an unlock token", async () => {
    const jwt = await signToken({ sub: "123", name: "neo", scope: "unlock" }, "5m", SECRET);
    const claims = await verifyToken(jwt, SECRET);
    expect(claims.sub).toBe("123");
    expect(claims.scope).toBe("unlock");
  });

  it("rejects a token signed with a different secret", async () => {
    const jwt = await signToken({ sub: "123", name: "neo", scope: "session" }, "6h", SECRET);
    await expect(verifyToken(jwt, "another-secret-another-secret-32xx")).rejects.toThrow();
  });

  it("rejects an expired token", async () => {
    const jwt = await signToken({ sub: "1", name: "x", scope: "unlock" }, "0s", SECRET);
    await new Promise((r) => setTimeout(r, 1100));
    await expect(verifyToken(jwt, SECRET)).rejects.toThrow();
  });
});
