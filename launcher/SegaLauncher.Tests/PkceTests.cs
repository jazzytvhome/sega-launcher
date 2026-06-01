using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using SegaLauncher;

public class PkceTests
{
    [Fact]
    public void Challenge_Is_Base64Url_Sha256_Of_Verifier()
    {
        var pkce = Pkce.Create();

        // verifier is URL-safe and of reasonable length
        Assert.InRange(pkce.Verifier.Length, 43, 128);
        Assert.DoesNotContain('+', pkce.Verifier);
        Assert.DoesNotContain('/', pkce.Verifier);
        Assert.DoesNotContain('=', pkce.Verifier);

        // challenge == base64url(SHA256(ASCII(verifier)))
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(pkce.Verifier));
        var expected = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.Equal(expected, pkce.Challenge);
    }

    [Fact]
    public void Create_Produces_Unique_Verifiers()
    {
        Assert.NotEqual(Pkce.Create().Verifier, Pkce.Create().Verifier);
    }
}
