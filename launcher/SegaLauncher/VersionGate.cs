using System;

namespace SegaLauncher;

/// <summary>Pure version comparison. Fail-open: unparseable input never blocks.</summary>
public static class VersionGate
{
    public static bool IsOutdated(string? current, string? min)
    {
        if (!Version.TryParse(current, out var c)) return false;
        if (!Version.TryParse(min, out var m)) return false;
        return c < m;
    }
}
