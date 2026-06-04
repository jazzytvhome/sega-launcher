using System;
using SegaLauncher;
using Xunit;

public class LicenseTests
{
    // a JWT-shaped token whose payload is {"sub":"u","mid":"m","exp":<far future>}
    static string TokenWithExp(long exp)
    {
        string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var head = B64("{\"alg\":\"HS256\"}");
        var body = B64($"{{\"sub\":\"u\",\"mid\":\"m\",\"exp\":{exp}}}");
        return $"{head}.{body}.sig";
    }

    [Fact]
    public void ExpiryOf_parses_exp()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds();
        var got = License.ExpiryOf(TokenWithExp(exp));
        Assert.NotNull(got);
        Assert.Equal(exp, got!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void IsValid_false_when_expired()
    {
        var past = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        Assert.False(License.IsValid(TokenWithExp(past)));
    }

    [Fact]
    public void IsValid_true_when_future()
    {
        var future = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
        Assert.True(License.IsValid(TokenWithExp(future)));
    }

    [Fact]
    public void MachineId_is_stable_and_nonempty()
    {
        Assert.Equal(License.MachineId(), License.MachineId());
        Assert.False(string.IsNullOrWhiteSpace(License.MachineId()));
    }
}
