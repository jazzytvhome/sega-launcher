namespace SegaLauncher;

public static class AppConfig
{
    // PUBLIC values only. No secrets ever live in the launcher.
    public const string DiscordClientId = "1511302943276793917";
    public const string VercelBaseUrl   = "https://vercel-app-seven-lake.vercel.app";
    public const int    LoopbackPort    = 51789;

    // Bump this when you ship an update, and set MIN_VERSION in Vercel to match
    // so older launchers show "update needed".
    public const string Version = "1.0.2";

    // Auto-update will only download+run an exe from THIS GitHub repo's releases.
    public const string UpdateRepoPathPrefix = "/jazzytvhome/sega-launcher/releases/";

    public static string RedirectUri => $"http://127.0.0.1:{LoopbackPort}/callback";
}
