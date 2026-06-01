using System;
using System.Security.Cryptography;
using System.Text;

namespace SegaLauncher;

public readonly record struct PkcePair(string Verifier, string Challenge);

public static class Pkce
{
    public static PkcePair Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return new PkcePair(verifier, Base64Url(hash));
    }

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
