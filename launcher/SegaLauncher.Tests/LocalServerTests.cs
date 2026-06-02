using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using SegaLauncher;

public class LocalServerTests
{
    private static GameVault OpenVector() =>
        GameVault.Open(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "vector.enc")),
            Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="));

    [Fact]
    public async Task Serves_Root_As_Index_Nested_And_404()
    {
        using var server = new LocalServer(OpenVector().Files, LocalServer.FreePort());
        server.Start();
        using var http = new HttpClient();

        var root = await http.GetStringAsync(server.BaseUrl);
        Assert.Contains("root", root);

        var nested = await http.GetStringAsync(server.BaseUrl + "_app/inner.txt");
        Assert.Equal("nested-ok", nested);

        var missing = await http.GetAsync(server.BaseUrl + "nope.txt");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
