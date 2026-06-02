using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    private enum Mode { Play, Install }
    private Mode _mode;
    private LocalServer? _server;
    private string? _downloadUrl;

    public MainWindow()
    {
        InitializeComponent();
        SourceCombo.ItemsSource = Catalog.Items;
        SourceCombo.SelectedIndex = 0;
    }

    // ---- update gate (runs first, before anything else) ----
    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RunUpdateCheckAsync();

    private async Task RunUpdateCheckAsync()
    {
        CheckPanel.Visibility = Visibility.Visible;
        UpdatePanel.Visibility = Visibility.Collapsed;
        ModePanel.Visibility = Visibility.Collapsed;
        WorkPanel.Visibility = Visibility.Collapsed;

        var info = await Updater.FetchAsync();
        CheckPanel.Visibility = Visibility.Collapsed;

        if (info != null && VersionGate.IsOutdated(AppConfig.Version, info.min))
        {
            // Only accept an absolute https link from the server — never file://, cmd:, etc.
            _downloadUrl = (Uri.TryCreate(info.url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                ? u.AbsoluteUri
                : null;
            DownloadButton.Visibility = _downloadUrl != null ? Visibility.Visible : Visibility.Collapsed;
            FadeIn(UpdatePanel);
        }
        else
        {
            FadeIn(ModePanel);
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        // _downloadUrl is only ever set to a validated https URL (see RunUpdateCheckAsync).
        if (_downloadUrl != null)
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
    }

    private async void Recheck_Click(object sender, RoutedEventArgs e) => await RunUpdateCheckAsync();

    // Reveal a panel with a quick fade + upward slide.
    private static void FadeIn(FrameworkElement el)
    {
        el.Visibility = Visibility.Visible;
        var tt = new TranslateTransform(0, 12);
        el.RenderTransform = tt;
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ---- navigation ----
    private void PlayMode_Click(object sender, RoutedEventArgs e) => EnterWork(Mode.Play);
    private void InstallMode_Click(object sender, RoutedEventArgs e) => EnterWork(Mode.Install);

    private void EnterWork(Mode mode)
    {
        _mode = mode;
        WorkHeading.Text = mode == Mode.Play ? "Play" : "Install source code";
        ActionButton.Content = mode == Mode.Play ? "Verify with Discord & Play" : "Verify with Discord & Install";
        ActionButton.IsEnabled = true;
        StatusText.Text = "";
        ModePanel.Visibility = Visibility.Collapsed;
        FadeIn(WorkPanel);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _server?.Dispose();
        _server = null;
        WorkPanel.Visibility = Visibility.Collapsed;
        FadeIn(ModePanel);
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DescText.Text = (SourceCombo.SelectedItem as SourceItem)?.Description ?? "";
    }

    // ---- the action ----
    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (SourceCombo.SelectedItem is not SourceItem item) return;

        ActionButton.IsEnabled = false;
        StatusText.Text = "Checking your SEGA+ membership… authorize in your browser.";

        var result = await DiscordAuth.RunAsync();
        if (result.Outcome != AuthOutcome.Verified)
        {
            StatusText.Text = result.Message ?? "Verification failed.";
            ActionButton.IsEnabled = true;
            return;
        }

        // Load blob + tamper check (detect-and-refuse, never destructive).
        var blobPath = Path.Combine(AppContext.BaseDirectory, item.BlobFile);
        if (!File.Exists(blobPath))
        {
            StatusText.Text = $"{item.BlobFile} not found next to the launcher.";
            ActionButton.IsEnabled = true;
            return;
        }
        var blob = File.ReadAllBytes(blobPath);
        if (!Integrity.BlobMatches(blob, BuildInfo.ExpectedBlobSha256))
        {
            StatusText.Text = "Tamper detected: this file doesn't match the launcher.\nRe-download the official package.";
            ActionButton.IsEnabled = true;
            return;
        }

        GameVault vault;
        try
        {
            vault = GameVault.Open(blob, Convert.FromBase64String(result.Key!));
        }
        catch
        {
            StatusText.Text = "Couldn't decrypt (wrong key or corrupt file).";
            ActionButton.IsEnabled = true;
            return;
        }

        if (_mode == Mode.Play) StartPlay(vault);
        else DoInstall(vault, item);
    }

    private void StartPlay(GameVault vault)
    {
        _server?.Dispose();
        _server = new LocalServer(vault.Files, LocalServer.FreePort());
        _server.Start();
        Process.Start(new ProcessStartInfo(_server.BaseUrl) { UseShellExecute = true });
        StatusText.Text = "Playing! Keep this launcher open while you play —\nclosing it stops the game.";
        // ActionButton stays disabled: the server is live this session.
    }

    private void DoInstall(GameVault vault, SourceItem item)
    {
        var dlg = new OpenFolderDialog { Title = $"Choose where to install {item.Name}" };
        if (dlg.ShowDialog() != true)
        {
            StatusText.Text = "Verified — install cancelled (no folder chosen).";
            ActionButton.IsEnabled = true;
            return;
        }
        var target = Path.Combine(dlg.FolderName, item.Id);
        try
        {
            Installer.WriteTo(vault.Files, target);
        }
        catch
        {
            StatusText.Text = "Install failed (no write permission to that folder?).";
            ActionButton.IsEnabled = true;
            return;
        }
        StatusText.Text = $"Installed to:\n{target}";
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"")); } catch { }
        ActionButton.IsEnabled = true;
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
