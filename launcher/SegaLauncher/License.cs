using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SegaLauncher;

/// <summary>
/// Local weekly-license storage. The token is signed server-side; the launcher only
/// reads the expiry to decide whether to re-verify, and trusts the server otherwise.
/// </summary>
public static class License
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SegaPlusLauncher");
    private static string Path_ => Path.Combine(Dir, "license.tok");

    public static void Save(string token)
    {
        Directory.CreateDirectory(Dir);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path_, enc);
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            var dec = ProtectedData.Unprotect(File.ReadAllBytes(Path_), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return null; }
    }

    public static void Clear() { try { File.Delete(Path_); } catch { } }

    public static DateTimeOffset? ExpiryOf(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var json = Encoding.UTF8.GetString(FromB64Url(parts[1]));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exp", out var exp)) return null;
            return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
        }
        catch { return null; }
    }

    public static bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var exp = ExpiryOf(token);
        return exp != null && exp.Value > DateTimeOffset.UtcNow;
    }

    public static int DaysLeft(string token)
    {
        var exp = ExpiryOf(token);
        if (exp == null) return 0;
        return Math.Max(0, (int)Math.Ceiling((exp.Value - DateTimeOffset.UtcNow).TotalDays));
    }

    /// <summary>Stable per-machine id: SHA-256 of MachineGuid + username.</summary>
    public static string MachineId()
    {
        string guid = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = k?.GetValue("MachineGuid") as string ?? "";
        }
        catch { }
        var raw = guid + "|" + Environment.UserName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
