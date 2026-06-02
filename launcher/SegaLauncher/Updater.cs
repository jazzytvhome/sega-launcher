using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SegaLauncher;

public sealed record VersionInfo(string? min, string? latest, string? url);

public static class Updater
{
    private static readonly HttpClient Http = new();

    /// <summary>Fetch the version manifest. Returns null if the server can't be reached.</summary>
    public static async Task<VersionInfo?> FetchAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<VersionInfo>($"{AppConfig.VercelBaseUrl}/api/version");
        }
        catch
        {
            return null; // offline / server down — fail open, the verify step will surface real errors
        }
    }
}
