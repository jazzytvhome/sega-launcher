using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using SegaLauncher;

// Opt-in smoke test against the REAL game.enc. No-op unless both env vars are set:
//   SEGA_SMOKE_BLOB = full path to game.enc
//   SEGA_SMOKE_KEY  = the base64 GAME_KEY it was built with
// Run: $env:SEGA_SMOKE_BLOB="...\dist\game.enc"; $env:SEGA_SMOKE_KEY="..."; dotnet test
public class RealBlobSmokeTests
{
    [Fact]
    public async Task Real_Blob_Decrypts_And_Serves_The_Game()
    {
        var blobPath = Environment.GetEnvironmentVariable("SEGA_SMOKE_BLOB");
        var keyB64 = Environment.GetEnvironmentVariable("SEGA_SMOKE_KEY");
        if (string.IsNullOrEmpty(blobPath) || string.IsNullOrEmpty(keyB64))
            return; // not configured — skip silently

        var vault = GameVault.Open(File.ReadAllBytes(blobPath), Convert.FromBase64String(keyB64));

        Assert.True(vault.Files.ContainsKey("index.html"), "index.html present");
        var indexHtml = Encoding.UTF8.GetString(vault.Files["index.html"]);
        Assert.Contains("skid-mark", indexHtml); // the watermark survived encryption

        using var server = new LocalServer(vault.Files, LocalServer.FreePort());
        server.Start();
        using var http = new HttpClient();

        var served = await http.GetStringAsync(server.BaseUrl);
        Assert.Contains("<html", served);
        Assert.Contains("skid-mark", served);

        // a real asset referenced by the page loads
        var alea = await http.GetAsync(server.BaseUrl + "alea.min.js");
        Assert.Equal(HttpStatusCode.OK, alea.StatusCode);
    }
}
