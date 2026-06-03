using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SegaLauncher;

/// <summary>Downloads the encrypted blob from Vercel, gated by the 2-min download token.</summary>
public static class Downloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static async Task<byte[]> FetchBlobAsync(string dlToken, string blobFile)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{AppConfig.VercelBaseUrl}/{blobFile}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dlToken);
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }
}
