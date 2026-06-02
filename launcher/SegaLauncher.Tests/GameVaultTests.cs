using System;
using System.IO;
using System.Text;
using Xunit;
using SegaLauncher;

public class GameVaultTests
{
    // Matches the deterministic key in vercel-app/scripts/make-test-vector.mjs (bytes 0x00..0x1F).
    private const string VectorKeyB64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private static byte[] VectorBlob() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "vector.enc"));

    [Fact]
    public void Decrypts_Node_Built_Vector()
    {
        var vault = GameVault.Open(VectorBlob(), Convert.FromBase64String(VectorKeyB64));

        Assert.Equal("SEGA+ ON TOP", Encoding.UTF8.GetString(vault.Files["hello.txt"]));
        Assert.Equal("nested-ok", Encoding.UTF8.GetString(vault.Files["_app/inner.txt"]));
    }

    [Fact]
    public void Wrong_Key_Throws()
    {
        Assert.ThrowsAny<Exception>(() => GameVault.Open(VectorBlob(), new byte[32]));
    }
}
