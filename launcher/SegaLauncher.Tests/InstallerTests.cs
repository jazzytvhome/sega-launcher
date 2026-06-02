using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using SegaLauncher;

public class InstallerTests
{
    private static GameVault OpenVector() =>
        GameVault.Open(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "vector.enc")),
            Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="));

    [Fact]
    public void Writes_Files_And_Nested_Paths_To_Disk()
    {
        var target = Path.Combine(Path.GetTempPath(), "sega-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Installer.WriteTo(OpenVector().Files, target);

            Assert.Equal("SEGA+ ON TOP", File.ReadAllText(Path.Combine(target, "hello.txt")));
            Assert.Equal("nested-ok", File.ReadAllText(Path.Combine(target, "_app", "inner.txt")));
            Assert.True(File.Exists(Path.Combine(target, "index.html")));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    [Fact]
    public void Refuses_Path_Traversal()
    {
        var target = Path.Combine(Path.GetTempPath(), "sega-trav-" + Guid.NewGuid().ToString("N"));
        var evil = new Dictionary<string, byte[]> { ["../escape.txt"] = Encoding.UTF8.GetBytes("nope") };
        try
        {
            Assert.ThrowsAny<Exception>(() => Installer.WriteTo(evil, target));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }
}
