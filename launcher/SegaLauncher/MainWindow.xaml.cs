using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    private LocalServer? _server;

    public MainWindow() => InitializeComponent();

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        VerifyButton.IsEnabled = false;
        StatusText.Text = "Opening Discord… authorize in your browser, then come back.";

        var result = await DiscordAuth.RunAsync();
        if (result.Outcome != AuthOutcome.Verified)
        {
            StatusText.Text = result.Message ?? "Verification failed.";
            VerifyButton.IsEnabled = true;
            return;
        }

        StatusText.Text = "Verified! Decrypting game…";
        if (!TryStartGame(result.Key!, out var error))
        {
            StatusText.Text = error;
            VerifyButton.IsEnabled = true;
            return;
        }

        Process.Start(new ProcessStartInfo(_server!.BaseUrl) { UseShellExecute = true });
        StatusText.Text = "Playing! Keep this launcher open while you play —\nclosing it stops the game.";
        // Button stays disabled: the server is live for this session.
    }

    private bool TryStartGame(string keyBase64, out string error)
    {
        error = "";
        byte[] key;
        try { key = Convert.FromBase64String(keyBase64); }
        catch { error = "Server returned a malformed key."; return false; }

        var blobPath = Path.Combine(AppContext.BaseDirectory, "game.enc");
        if (!File.Exists(blobPath))
        {
            error = "game.enc not found next to the launcher. Keep them in the same folder.";
            return false;
        }

        GameVault vault;
        try
        {
            var blob = File.ReadAllBytes(blobPath);
            vault = GameVault.Open(blob, key);
        }
        catch
        {
            error = "Couldn't decrypt the game (wrong key or corrupt game.enc).";
            return false;
        }

        _server = new LocalServer(vault.Files, LocalServer.FreePort());
        _server.Start();
        return true;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _server?.Dispose();
        base.OnClosed(e);
    }
}
