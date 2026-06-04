using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SegaLauncher;

public enum UnlockOutcome { Ok, Expired, NotMember, Blacklisted, ServerError }
public sealed record UnlockResult(UnlockOutcome Outcome, string? Key = null, string? Dl = null, string? Message = null);

public static class LicenseClient
{
    private static readonly HttpClient Http = new();

    public static async Task<UnlockResult> UnlockAsync(string lic)
    {
        try
        {
            var resp = await Http.PostAsJsonAsync($"{AppConfig.VercelBaseUrl}/api/unlock",
                new { lic, machine = License.MachineId() });

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new UnlockResult(UnlockOutcome.Expired);
            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                var fb = await resp.Content.ReadFromJsonAsync<Resp>();
                return new UnlockResult(fb?.reason == "blacklisted" ? UnlockOutcome.Blacklisted : UnlockOutcome.NotMember,
                    Message: fb?.reason == "blacklisted" ? "This account is blacklisted." : "Members only — re-verify needed.");
            }
            if (!resp.IsSuccessStatusCode)
                return new UnlockResult(UnlockOutcome.ServerError, Message: "Server issue — try again.");

            var body = await resp.Content.ReadFromJsonAsync<Resp>();
            if (body is null || !body.ok || string.IsNullOrEmpty(body.key) || string.IsNullOrEmpty(body.dl))
                return new UnlockResult(UnlockOutcome.ServerError, Message: "Unexpected server response.");
            return new UnlockResult(UnlockOutcome.Ok, body.key, body.dl);
        }
        catch (HttpRequestException)
        {
            return new UnlockResult(UnlockOutcome.ServerError, Message: "Can't reach the server.");
        }
    }

    private sealed record Resp(bool ok, string? key, string? dl, string? reason);
}
