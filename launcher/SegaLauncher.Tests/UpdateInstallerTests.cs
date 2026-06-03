using Xunit;
using SegaLauncher;

public class UpdateInstallerTests
{
    [Theory]
    [InlineData("https://github.com/jazzytvhome/sega-launcher/releases/latest/download/SEGA-Launcher.exe", true)]
    [InlineData("https://github.com/jazzytvhome/sega-launcher/releases/download/v1.0.2/SEGA-Launcher.exe", true)]
    [InlineData("https://github.com/attacker/repo/releases/download/v1/malware.exe", false)] // wrong repo
    [InlineData("https://github.com/jazzytvhome/sega-launcher/blob/main/evil.exe", false)]   // not /releases/
    [InlineData("http://github.com/jazzytvhome/sega-launcher/releases/x.exe", false)]        // not https
    [InlineData("https://evil.com/jazzytvhome/sega-launcher/releases/x.exe", false)]         // wrong host
    [InlineData("https://raw.githubusercontent.com/jazzytvhome/sega-launcher/x.exe", false)] // wrong host
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Trusts_This_Repos_GitHub_Releases(string? url, bool trusted)
    {
        Assert.Equal(trusted, UpdateInstaller.IsTrustedUrl(url));
    }
}
