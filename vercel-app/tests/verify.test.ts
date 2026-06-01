import { describe, it, expect, vi, afterEach } from "vitest";
import { verifyMembership } from "../lib/discord.js";

const ENV = {
  clientId: "cid",
  clientSecret: "csecret",
  botToken: "bot",
  guildId: "999",
  redirectUri: "http://127.0.0.1:51789/callback",
};

afterEach(() => vi.restoreAllMocks());

function mockFetchSequence(responses: Array<{ status: number; body: any }>) {
  const fn = vi.fn();
  responses.forEach((r) =>
    fn.mockResolvedValueOnce({
      ok: r.status >= 200 && r.status < 300,
      status: r.status,
      json: async () => r.body,
    }),
  );
  vi.stubGlobal("fetch", fn);
  return fn;
}

describe("verifyMembership", () => {
  it("returns member result for a SEGA+ member", async () => {
    mockFetchSequence([
      { status: 200, body: { access_token: "atok" } },           // token exchange
      { status: 200, body: { id: "42", username: "neo" } },       // /users/@me
      { status: 200, body: { user: { id: "42" } } },              // guild member (200 = member)
    ]);
    const res = await verifyMembership("authcode", "verifier", ENV);
    expect(res).toEqual({ ok: true, user: { id: "42", name: "neo" } });
  });

  it("returns not_member when the bot lookup 404s", async () => {
    mockFetchSequence([
      { status: 200, body: { access_token: "atok" } },
      { status: 200, body: { id: "7", username: "rando" } },
      { status: 404, body: { message: "Unknown Member" } },
    ]);
    const res = await verifyMembership("authcode", "verifier", ENV);
    expect(res).toEqual({ ok: false, reason: "not_member" });
  });

  it("returns discord_error when token exchange fails", async () => {
    mockFetchSequence([{ status: 400, body: { error: "invalid_grant" } }]);
    const res = await verifyMembership("badcode", "verifier", ENV);
    expect(res).toEqual({ ok: false, reason: "discord_error" });
  });
});
