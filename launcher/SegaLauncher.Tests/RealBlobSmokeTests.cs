using System;
using System.IO;
using System.Text;
using Xunit;
using SegaLauncher;

// Opt-in smoke test against the REAL game.enc. No-op unless both env vars are set:
//   SEGA_SMOKE_BLOB = full path to game.enc
//   SEGA_SMOKE_KEY  = the base64 GAME_KEY it was built with
// Run: $env:SEGA_SMOKE_BLOB="...\dist\game.enc"; $env:SEGA_SMOKE_KEY="..."; dotnet test
public class RealBlobSmokeTests
{
    [Fact]
    public void Real_Blob_Decrypts_And_Installs_Clean_Source()
    {
        var blobPath = Environment.GetEnvironmentVariable("SEGA_SMOKE_BLOB");
        var keyB64 = Environment.GetEnvironmentVariable("SEGA_SMOKE_KEY");
        if (string.IsNullOrEmpty(blobPath) || string.IsNullOrEmpty(keyB64))
            return; // not configured — skip silently

        var vault = GameVault.Open(File.ReadAllBytes(blobPath), Convert.FromBase64String(keyB64));

        Assert.True(vault.Files.ContainsKey("index.html"), "index.html present");
        var indexHtml = Encoding.UTF8.GetString(vault.Files["index.html"]);
        Assert.Contains("skid-mark", indexHtml); // watermark survived

        // The installed source must NOT contain the SEGA+ gate (Discord/Vercel/launcher).
        foreach (var kv in vault.Files)
        {
            var text = TryText(kv.Value);
            Assert.DoesNotContain("DiscordAuth", text);
            Assert.DoesNotContain("vercel-app-seven-lake", text);
            Assert.DoesNotContain("api/verify", text);
        }

        // Install to a temp folder and confirm files land on disk.
        var target = Path.Combine(Path.GetTempPath(), "sega-real-" + Guid.NewGuid().ToString("N"));
        try
        {
            Installer.WriteTo(vault.Files, target);
            Assert.True(File.Exists(Path.Combine(target, "index.html")));
            Assert.True(File.Exists(Path.Combine(target, "alea.min.js")));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    private static string TryText(byte[] bytes)
    {
        // Only scan smallish text-ish files; skip big binaries (images/audio/models).
        if (bytes.Length > 2_000_000) return "";
        return Encoding.UTF8.GetString(bytes);
    }
}
