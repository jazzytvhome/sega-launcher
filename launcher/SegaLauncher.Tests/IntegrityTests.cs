using System.Text;
using Xunit;
using SegaLauncher;

public class IntegrityTests
{
    [Fact]
    public void Empty_Expected_Hash_Skips_Check()
    {
        Assert.True(Integrity.BlobMatches(Encoding.UTF8.GetBytes("anything"), ""));
    }

    [Fact]
    public void Matching_Passes_Tampered_Fails()
    {
        var data = Encoding.UTF8.GetBytes("hello sega");
        var hash = Integrity.Sha256Hex(data);

        Assert.True(Integrity.BlobMatches(data, hash));
        Assert.True(Integrity.BlobMatches(data, hash.ToUpperInvariant())); // case-insensitive
        Assert.False(Integrity.BlobMatches(data, "deadbeef"));
        Assert.False(Integrity.BlobMatches(Encoding.UTF8.GetBytes("tampered"), hash));
    }
}
