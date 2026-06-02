namespace SegaLauncher;

public static class AppConfig
{
    // PUBLIC values only. No secrets ever live in the launcher.
    public const string DiscordClientId = "1511302943276793917";
    public const string VercelBaseUrl   = "https://vercel-app-seven-lake.vercel.app";
    public const int    LoopbackPort    = 51789;

    public static string RedirectUri => $"http://127.0.0.1:{LoopbackPort}/callback";
}
