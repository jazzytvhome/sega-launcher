using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SegaLauncher;

public enum AuthOutcome { Verified, NotMember, Cancelled, ServerError, PortBusy }

public sealed record AuthResult(AuthOutcome Outcome, string? Key = null, string? Message = null);

public static class DiscordAuth
{
    private static readonly HttpClient Http = new();

    public static async Task<AuthResult> RunAsync(CancellationToken ct = default)
    {
        var pkce = Pkce.Create();
        var state = Pkce.NewState();

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{AppConfig.LoopbackPort}/callback/");
        try { listener.Start(); }
        catch (HttpListenerException) { return new AuthResult(AuthOutcome.PortBusy,
            Message: "Close any other SEGA+ Launcher window and try again."); }

        // Open the system browser to Discord's authorize page.
        var authUrl =
            "https://discord.com/api/oauth2/authorize" +
            $"?client_id={AppConfig.DiscordClientId}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(AppConfig.RedirectUri)}" +
            "&scope=identify" +
            $"&state={state}" +
            $"&code_challenge={pkce.Challenge}&code_challenge_method=S256";
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        // Wait for Discord to redirect back to the loopback listener (with a timeout).
        var getContext = listener.GetContextAsync();
        var completed = await Task.WhenAny(getContext, Task.Delay(TimeSpan.FromMinutes(3), ct));
        if (completed != getContext)
            return new AuthResult(AuthOutcome.Cancelled, Message: "Verification timed out.");

        var ctx = await getContext;
        var query = ctx.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];
        await WriteBrowserPageAsync(ctx.Response,
            "Verified — you can close this tab and return to the SEGA+ Launcher.");

        if (returnedState != state || string.IsNullOrEmpty(code))
            return new AuthResult(AuthOutcome.Cancelled, Message: "Verification cancelled.");

        // Hand the code to Vercel, which exchanges it and asks the bot about membership.
        try
        {
            var resp = await Http.PostAsJsonAsync(
                $"{AppConfig.VercelBaseUrl}/api/verify",
                new { code, code_verifier = pkce.Verifier }, ct);

            if (resp.StatusCode == HttpStatusCode.Forbidden)
                return new AuthResult(AuthOutcome.NotMember,
                    Message: "Members only — make sure you're in the SEGA+ server.");
            if (!resp.IsSuccessStatusCode)
                return new AuthResult(AuthOutcome.ServerError,
                    Message: "Server config issue — contact a SEGA+ admin.");

            var body = await resp.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken: ct);
            if (body is null || !body.ok || string.IsNullOrEmpty(body.key))
                return new AuthResult(AuthOutcome.ServerError, Message: "Unexpected server response.");

            return new AuthResult(AuthOutcome.Verified, body.key);
        }
        catch (HttpRequestException)
        {
            return new AuthResult(AuthOutcome.ServerError,
                Message: "Can't reach the server — try again later.");
        }
    }

    private static async Task WriteBrowserPageAsync(HttpListenerResponse res, string message)
    {
        var html = Encoding.UTF8.GetBytes(
            $"<html><body style='background:#222;color:#eee;font-family:sans-serif;" +
            $"display:flex;align-items:center;justify-content:center;height:100vh'>" +
            $"<h2>{message}</h2></body></html>");
        res.ContentType = "text/html";
        res.ContentLength64 = html.Length;
        await res.OutputStream.WriteAsync(html);
        res.OutputStream.Close();
    }

    private sealed record VerifyResponse(bool ok, string? key);
}
