using Xunit;
using SegaLauncher;

public class UpdateInstallerTests
{
    [Theory]
    [InlineData("https://github.com/jazzytvhome/sega-launcher/releases/latest/download/SEGA-Launcher.exe", true)]
    [InlineData("https://github.com/anyone/repo/releases/download/v1/app.exe", true)]
    [InlineData("http://github.com/x/y/app.exe", false)]   // not https
    [InlineData("https://evil.com/malware.exe", false)]    // wrong host
    [InlineData("https://raw.githubusercontent.com/x/y/app.exe", false)] // not github.com
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Trusts_Https_GitHub(string? url, bool trusted)
    {
        Assert.Equal(trusted, UpdateInstaller.IsTrustedUrl(url));
    }
}
