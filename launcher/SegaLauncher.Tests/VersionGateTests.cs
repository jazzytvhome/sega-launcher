using Xunit;
using SegaLauncher;

public class VersionGateTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]   // older than required
    [InlineData("1.0.0", "1.0.0", false)]  // equal
    [InlineData("1.2.0", "1.0.0", false)]  // newer than required
    [InlineData("1.0.0", "2.0.0", true)]
    public void Compares_Versions(string current, string min, bool outdated)
    {
        Assert.Equal(outdated, VersionGate.IsOutdated(current, min));
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData("notaversion", "1.0.0")]
    [InlineData("1.0.0", "")]
    public void Fails_Open_On_Bad_Input(string? current, string? min)
    {
        // unknown/garbage input must never block the user
        Assert.False(VersionGate.IsOutdated(current, min));
    }
}
