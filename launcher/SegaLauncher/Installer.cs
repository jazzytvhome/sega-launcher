using System;
using System.Collections.Generic;
using System.IO;

namespace SegaLauncher;

/// <summary>
/// Writes a decrypted file map to disk (the "install"). Guards against path
/// traversal so a malformed blob can't write outside the chosen folder.
/// </summary>
public static class Installer
{
    public static void WriteTo(IReadOnlyDictionary<string, byte[]> files, string targetDir)
    {
        var root = Path.GetFullPath(targetDir);
        Directory.CreateDirectory(root);

        foreach (var kv in files)
        {
            var rel = kv.Key.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, rel));

            if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"refusing to write outside target: {kv.Key}");

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, kv.Value);
        }
    }
}
