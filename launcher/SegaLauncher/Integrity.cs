using System;
using System.Security.Cryptography;

namespace SegaLauncher;

/// <summary>
/// Tamper detection: confirms game.enc is the exact file that shipped with this
/// launcher build. Detect-and-refuse only — never destructive.
/// </summary>
public static class Integrity
{
    public static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// True if the blob matches the expected hash. Dev builds (empty expected hash)
    /// always pass so local `dotnet run` works.
    /// </summary>
    public static bool BlobMatches(byte[] blob, string expectedSha256Hex)
    {
        if (string.IsNullOrEmpty(expectedSha256Hex)) return true; // dev mode
        return Sha256Hex(blob).Equals(expectedSha256Hex.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
