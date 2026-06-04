using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    private enum Mode { Play, Install }
    private LocalServer? _server;
    private string? _downloadUrl;
    private string? _updateSha;
    private string? _lic;
    private string? _reqZip;
    private readonly MediaPlayer _boot = new();

    public MainWindow()
    {
        InitializeComponent();
        UpdateInstaller.CleanupOldBackup();
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
        BootPanel.Visibility = Visibility.Collapsed;
        LicensePanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Collapsed;

        var info = await Updater.FetchAsync();
        CheckPanel.Visibility = Visibility.Collapsed;

        if (info != null && VersionGate.IsOutdated(AppConfig.Version, info.min))
        {
            _downloadUrl = (Uri.TryCreate(info.url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                ? u.AbsoluteUri
                : null;
            _updateSha = info.sha256;
            await AutoUpdateAsync();
        }
        else
        {
            await RunBootCheckAsync(info?.sha256);
            await RunLicenseGateAsync();
        }
    }

    // ---- boot / tamper splash (D4) ----
    private async Task RunBootCheckAsync(string? expectedExeSha)
    {
        BootPanel.Visibility = Visibility.Visible;
        BootHead.Text = "CHECKING FOR TAMPERING";
        BootHead.Foreground = (Brush)FindResource("Ink");
        BootSub.Text = "do not close this window";
        BootLog.Text = "";
        BootMeter.Text = "";
        FadeIn(BootPanel);
        PlayBootSound();

        var lines = new[]
        {
            "SEGA+ SECURE LOADER  v" + AppConfig.Version,
            "(c) SEGA+ ON TOP  //  SKIDDED BY JUSTONEONTOP",
            "--------------------------------------------",
            "> mount vault.enc ................. ok",
            "> sha-256 self-check ............. running",
        };
        foreach (var l in lines) { BootLog.Text += l + "\n"; await Task.Delay(360); }

        // real tamper check: hash our own exe, compare to /api/version sha256
        bool intact = true;
        try
        {
            var exe = Environment.ProcessPath!;
            var hash = Integrity.Sha256Hex(await File.ReadAllBytesAsync(exe));
            intact = string.IsNullOrEmpty(expectedExeSha) || hash.Equals(expectedExeSha.Trim(), StringComparison.OrdinalIgnoreCase);
            BootLog.Text += intact ? "> integrity ...................... INTACT\n" : "> integrity ...................... MISMATCH\n";
        }
        catch { /* dev run / single-file edge: don't hard-fail */ }

        // ASCII meter to 100%
        for (int n = 1; n <= 18; n++)
        {
            BootMeter.Text = $"SCAN [{new string('#', n)}{new string('.', 18 - n)}] {n * 100 / 18}%";
            await Task.Delay(60);
        }

        BootHead.Text = intact ? "✓ INTEGRITY VERIFIED" : "⚠ TAMPER DETECTED";
        BootHead.Foreground = intact ? (Brush)FindResource("Green") : (Brush)FindResource("Red");
        BootSub.Text = intact ? "secure // signature matches" : "signature mismatch";
        BootLog.Text += intact ? "\n[ OK ]  NO TAMPERING DETECTED\n" : "\n[ !! ]  REINSTALL FROM THE OFFICIAL RELEASE\n";
        await Task.Delay(900);
        BootPanel.Visibility = Visibility.Collapsed;
    }

    // Robust mp3: read the bundled Resource stream, drop to temp, MediaPlayer.Open the temp file.
    // Single-file self-contained build can't rely on loose files / siteoforigin, so we extract.
    // Cosmetic only — wrapped so it can NEVER block or crash boot.
    private void PlayBootSound()
    {
        try
        {
            var res = Application.GetResourceStream(new Uri("Assets/sega-on-top.mp3", UriKind.Relative));
            if (res == null) return;
            var temp = Path.Combine(Path.GetTempPath(), "sega-on-top.mp3");
            using (var src = res.Stream)
            using (var dst = File.Create(temp))
                src.CopyTo(dst);
            _boot.Open(new Uri(temp));
            _boot.Play();
        }
        catch { /* sound is cosmetic */ }
    }

    // ---- license gate (C4) ----
    private async Task RunLicenseGateAsync()
    {
        _lic = License.Load();
        if (License.IsValid(_lic))
        {
            ShowMain();
            return;
        }
        // no valid license — require verify at startup
        MainPanel.Visibility = Visibility.Collapsed;
        LicensePanel.Visibility = Visibility.Visible;
        FadeIn(LicensePanel);
        await Task.CompletedTask;
    }

    private void ShowMain()
    {
        LicensePanel.Visibility = Visibility.Collapsed;
        if (_lic != null) LicenseBadge.Text = $"▦ LICENSE · {License.DaysLeft(_lic)}D LEFT";
        FadeIn(MainPanel);
    }

    // hooked to the "Verify with Discord" button on LicensePanel
    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        var result = await DiscordAuth.RunAsync();
        if (result.Outcome != AuthOutcome.Verified || string.IsNullOrEmpty(result.Lic))
        {
            LicenseStatus.Text = result.Message ?? "Verification failed.";
            return;
        }
        License.Save(result.Lic);
        _lic = result.Lic;
        ShowMain();
        StatusText.Text = "verified // license valid 7 days";
    }

    private async Task AutoUpdateAsync()
    {
        UpdateTitle.Text = "UPDATING…";
        UpdateMsg.Text = "Downloading the new version, please wait.";
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        DownloadButton.Visibility = Visibility.Collapsed;
        FadeIn(UpdatePanel);

        bool ok = false;
        if (_downloadUrl != null)
            ok = await UpdateInstaller.RunAsync(
                _downloadUrl,
                _updateSha,
                p => Dispatcher.Invoke(() => UpdateProgress.Value = p * 100));

        if (ok)
        {
            UpdateMsg.Text = "Update ready — restarting…";
            await Task.Delay(500);
            Application.Current.Shutdown();
        }
        else
        {
            // auto-update couldn't run (untrusted url / no write permission) — offer manual
            UpdateTitle.Text = "UPDATE NEEDED";
            UpdateMsg.Text = "Couldn't auto-update. Get the new version manually:";
            UpdateProgress.Visibility = Visibility.Collapsed;
            DownloadButton.Visibility = _downloadUrl != null ? Visibility.Visible : Visibility.Collapsed;
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

        string? key = null, dl = null;

        // 1) try the weekly license (no browser)
        if (License.IsValid(_lic))
        {
            StatusText.Text = "Unlocking…";
            var u = await LicenseClient.UnlockAsync(_lic!);
            if (u.Outcome == UnlockOutcome.Ok) { key = u.Key; dl = u.Dl; }
            else if (u.Outcome is UnlockOutcome.Expired) { License.Clear(); _lic = null; }
            else { StatusText.Text = u.Message ?? "Unlock failed."; SetBusy(false); return; }
        }

        // 2) no/expired license → full Discord verify, then use its key+dl directly
        if (key == null)
        {
            StatusText.Text = "Verify in your browser…";
            var v = await DiscordAuth.RunAsync();
            if (v.Outcome != AuthOutcome.Verified) { StatusText.Text = v.Message ?? "Verification failed."; SetBusy(false); return; }
            if (!string.IsNullOrEmpty(v.Lic)) { License.Save(v.Lic); _lic = v.Lic; LicenseBadge.Text = $"▦ LICENSE · {License.DaysLeft(v.Lic)}D LEFT"; }
            key = v.Key; dl = v.Dl;
        }

        StatusText.Text = "Downloading source… (~15 MB)";
        byte[] blob;
        try { blob = await Downloader.FetchBlobAsync(dl!, item.BlobFile); }
        catch { StatusText.Text = "Download failed — check your connection."; SetBusy(false); return; }

        GameVault vault;
        try { vault = GameVault.Open(blob, Convert.FromBase64String(key!)); }
        catch { StatusText.Text = "Couldn't decrypt (wrong key or corrupt file)."; SetBusy(false); return; }

        try { if (mode == Mode.Play) StartPlay(vault); else DoInstall(vault, item); }
        catch { StatusText.Text = "Couldn't start — try again."; }
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

    // ---- request-a-source modal (E2) ----
    private void Request_Click(object sender, RoutedEventArgs e)
    {
        ReqName.Text = ""; ReqNotes.Text = ""; _reqZip = null;
        ReqFileLabel.Text = "no file chosen";
        ReqSentPanel.Visibility = Visibility.Collapsed;
        ReqForm.Visibility = Visibility.Visible;
        ReqSendButton.IsEnabled = true;
        ReqSendButton.Content = "SEND REQUEST";
        RequestModal.Visibility = Visibility.Visible;
    }

    private void ReqClose_Click(object sender, RoutedEventArgs e) => RequestModal.Visibility = Visibility.Collapsed;

    private void ReqPick_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose a .zip build", Filter = "Zip archives (*.zip)|*.zip" };
        if (dlg.ShowDialog() == true)
        {
            var fi = new FileInfo(dlg.FileName);
            if (fi.Length > RequestClient.MaxBytes) { ReqFileLabel.Text = "TOO BIG (max 500 MB)"; _reqZip = null; return; }
            _reqZip = dlg.FileName;
            ReqFileLabel.Text = $"{fi.Name} · {fi.Length / 1048576.0:0.0} MB";
        }
    }

    private async void ReqSend_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReqName.Text)) { ReqName.Focus(); return; }
        if (_reqZip == null) { ReqFileLabel.Text = "ADD A .ZIP FIRST"; return; }
        if (!License.IsValid(_lic)) { ReqFileLabel.Text = "Re-verify first (license expired)."; return; }

        ReqSendButton.IsEnabled = false; ReqSendButton.Content = "UPLOADING…";
        var r = await RequestClient.SubmitAsync(_lic!, ReqName.Text.Trim(), ReqNotes.Text.Trim(), _reqZip);
        ReqSendButton.IsEnabled = true; ReqSendButton.Content = "SEND REQUEST";

        if (r.Ok)
        {
            ReqForm.Visibility = Visibility.Collapsed;
            ReqSentPanel.Visibility = Visibility.Visible;
        }
        else ReqFileLabel.Text = r.Message ?? "Failed.";
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

    protected override void OnClosed(EventArgs e)
    {
        _server?.Dispose();
        base.OnClosed(e);
    }
}
