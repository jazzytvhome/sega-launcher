using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SegaLauncher;

/// <summary>
/// Self-update: download the new exe, swap the running one for it, relaunch.
/// On Windows you can rename a running exe (just not delete it), so we move the
/// current exe aside, drop the new one in its place, start it, and exit.
/// </summary>
public static class UpdateInstaller
{
    // Only auto-execute an update from a real https github.com link.
    public static bool IsTrustedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Delete a leftover .old backup from a previous update (call at startup).</summary>
    public static void CleanupOldBackup()
    {
        try
        {
            var cur = Environment.ProcessPath;
            if (cur == null) return;
            var bak = cur + ".old";
            if (File.Exists(bak)) File.Delete(bak);
        }
        catch { /* still locked, or gone — ignore */ }
    }

    /// <summary>
    /// Download from <paramref name="url"/>, swap the running exe, relaunch.
    /// Returns false and changes nothing if the URL is untrusted or the swap fails.
    /// </summary>
    public static async Task<bool> RunAsync(string url, Action<double>? onProgress = null)
    {
        if (!IsTrustedUrl(url)) return false;

        var current = Environment.ProcessPath;
        if (current == null) return false;
        var dir = Path.GetDirectoryName(current)!;
        var tmp = Path.Combine(dir, "update.download.tmp");

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1L;
                await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var dst = File.Create(tmp);
                var buffer = new byte[81920];
                long readTotal = 0;
                int n;
                while ((n = await src.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                    readTotal += n;
                    if (total > 0) onProgress?.Invoke((double)readTotal / total);
                }
            }

            // sanity: a Windows exe starts with "MZ"
            using (var fs = File.OpenRead(tmp))
            {
                if (fs.Length < 2 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                {
                    File.Delete(tmp);
                    return false;
                }
            }

            var backup = current + ".old";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(current, backup);   // rename the running exe (allowed on Windows)
            File.Move(tmp, current);      // put the new exe in its place

            Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
            return true;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }
}
