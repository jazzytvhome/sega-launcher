using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    private enum Mode { Play, Install }
    private LocalServer? _server;
    private string? _downloadUrl;

    public MainWindow()
    {
        InitializeComponent();
        SourceList.ItemsSource = Catalog.Items;
        SourceList.SelectedIndex = 0;
        VersionLabel.Text = "v" + AppConfig.Version;
    }

    // ---- window chrome ----
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- update gate (runs first) ----
    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RunUpdateCheckAsync();

    private async Task RunUpdateCheckAsync()
    {
        CheckPanel.Visibility = Visibility.Visible;
        UpdatePanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Collapsed;

        var info = await Updater.FetchAsync();
        CheckPanel.Visibility = Visibility.Collapsed;

        if (info != null && VersionGate.IsOutdated(AppConfig.Version, info.min))
        {
            _downloadUrl = (Uri.TryCreate(info.url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                ? u.AbsoluteUri
                : null;
            DownloadButton.Visibility = _downloadUrl != null ? Visibility.Visible : Visibility.Collapsed;
            FadeIn(UpdatePanel);
        }
        else
        {
            FadeIn(MainPanel);
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadUrl != null)
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
    }

    private async void Recheck_Click(object sender, RoutedEventArgs e) => await RunUpdateCheckAsync();

    // ---- source selection ----
    private void SourceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceList.SelectedItem is SourceItem item)
        {
            SourceName.Text = item.Name;
            DescText.Text = item.Description;
        }
    }

    // ---- actions ----
    private async void Play_Click(object sender, RoutedEventArgs e) => await DoFlowAsync(Mode.Play);
    private async void Install_Click(object sender, RoutedEventArgs e) => await DoFlowAsync(Mode.Install);

    private async Task DoFlowAsync(Mode mode)
    {
        if (SourceList.SelectedItem is not SourceItem item) return;

        SetBusy(true);
        StatusText.Text = "Checking your SEGA+ membership… authorize in your browser.";

        var result = await DiscordAuth.RunAsync();
        if (result.Outcome != AuthOutcome.Verified)
        {
            StatusText.Text = result.Message ?? "Verification failed.";
            SetBusy(false);
            return;
        }

        // download the encrypted blob from Vercel (gated by the 2-min token)
        StatusText.Text = "Verified! Downloading source… (~15 MB)";
        byte[] blob;
        try
        {
            blob = await Downloader.FetchBlobAsync(result.Dl!, item.BlobFile);
        }
        catch
        {
            StatusText.Text = "Download failed — check your connection and try again.";
            SetBusy(false);
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
            SetBusy(false);
            return;
        }

        if (mode == Mode.Play) StartPlay(vault);
        else DoInstall(vault, item);

        SetBusy(false);
    }

    private void StartPlay(GameVault vault)
    {
        _server?.Dispose();
        _server = new LocalServer(vault.Files, LocalServer.FreePort());
        _server.Start();
        Process.Start(new ProcessStartInfo(_server.BaseUrl) { UseShellExecute = true });
        StatusText.Text = "Playing! Keep this launcher open while you play —\nclosing it stops the game.";
    }

    private void DoInstall(GameVault vault, SourceItem item)
    {
        var dlg = new OpenFolderDialog { Title = $"Choose where to install {item.Name}" };
        if (dlg.ShowDialog() != true)
        {
            StatusText.Text = "Verified — install cancelled (no folder chosen).";
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
            return;
        }
        StatusText.Text = $"Installed to:\n{target}";
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"")); } catch { }
    }

    private void SetBusy(bool busy)
    {
        PlayButton.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy;
    }

    // reveal a panel with a quick fade + upward slide
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
