using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SegaLauncher;

public sealed record ReqResult(bool Ok, string? Message = null);

public static class RequestClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };
    public const long MaxBytes = 500L * 1024 * 1024;

    public static async Task<ReqResult> SubmitAsync(string lic, string source, string notes, string zipPath)
    {
        var fi = new FileInfo(zipPath);
        if (!fi.Exists) return new ReqResult(false, "File not found.");
        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return new ReqResult(false, "Must be a .zip.");
        if (fi.Length > MaxBytes) return new ReqResult(false, "Max 500 MB.");

        var machine = License.MachineId();
        try
        {
            // 1) init — returns presigned PUT URL and object key (no token)
            var initResp = await Http.PostAsJsonAsync($"{AppConfig.VercelBaseUrl}/api/request/init",
                new { lic, machine, filename = fi.Name, size = fi.Length });
            if ((int)initResp.StatusCode == 429) return new ReqResult(false, "Slow down — 1 request per 5 min.");
            if (!initResp.IsSuccessStatusCode) return new ReqResult(false, "Couldn't start the request.");
            var init = await initResp.Content.ReadFromJsonAsync<InitResp>();
            if (init is null || !init.ok) return new ReqResult(false, "Couldn't start the request.");

            // 2) PUT the zip directly to R2 using the presigned URL.
            //    Auth is embedded in the query string — no Authorization header needed.
            using (var content = new StreamContent(File.OpenRead(zipPath)))
            {
                content.Headers.ContentType = new("application/zip");
                using var put = new HttpRequestMessage(HttpMethod.Put, init.uploadUrl) { Content = content };
                var putResp = await Http.SendAsync(put);
                if (!putResp.IsSuccessStatusCode) return new ReqResult(false, "Upload failed.");
            }

            // 3) finalize — pass the object key so the server can verify ownership
            var finResp = await Http.PostAsJsonAsync($"{AppConfig.VercelBaseUrl}/api/request/finalize",
                new { lic, machine, key = init.key, source, notes });
            if (!finResp.IsSuccessStatusCode) return new ReqResult(false, "Couldn't finalize the request.");
            return new ReqResult(true);
        }
        catch (HttpRequestException) { return new ReqResult(false, "Network error — try again."); }
    }

    private sealed record InitResp(bool ok, string uploadUrl, string key);
}
