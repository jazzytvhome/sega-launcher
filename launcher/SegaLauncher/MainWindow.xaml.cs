using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceCombo.ItemsSource = Catalog.Items;
        SourceCombo.SelectedIndex = 0;
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DescText.Text = (SourceCombo.SelectedItem as SourceItem)?.Description ?? "";
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (SourceCombo.SelectedItem is not SourceItem item) return;

        InstallButton.IsEnabled = false;
        StatusText.Text = "Checking your SEGA+ membership… authorize in your browser.";

        var result = await DiscordAuth.RunAsync();
        if (result.Outcome != AuthOutcome.Verified)
        {
            StatusText.Text = result.Message ?? "Verification failed.";
            InstallButton.IsEnabled = true;
            return;
        }

        // Ask where to install.
        var dlg = new OpenFolderDialog { Title = $"Choose where to install {item.Name}" };
        if (dlg.ShowDialog() != true)
        {
            StatusText.Text = "Verified — install cancelled (no folder chosen).";
            InstallButton.IsEnabled = true;
            return;
        }
        var target = Path.Combine(dlg.FolderName, item.Id);

        StatusText.Text = "Verified! Decrypting & installing…";
        if (!TryInstall(item, result.Key!, target, out var error))
        {
            StatusText.Text = error;
            InstallButton.IsEnabled = true;
            return;
        }

        StatusText.Text = $"Installed to:\n{target}";
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"")); } catch { }
        InstallButton.IsEnabled = true;
    }

    private static bool TryInstall(SourceItem item, string keyBase64, string target, out string error)
    {
        error = "";
        byte[] key;
        try { key = Convert.FromBase64String(keyBase64); }
        catch { error = "Server returned a malformed key."; return false; }

        var blobPath = Path.Combine(AppContext.BaseDirectory, item.BlobFile);
        if (!File.Exists(blobPath))
        {
            error = $"{item.BlobFile} not found next to the launcher. Keep them in the same folder.";
            return false;
        }

        try
        {
            var vault = GameVault.Open(File.ReadAllBytes(blobPath), key);
            Installer.WriteTo(vault.Files, target);
            return true;
        }
        catch
        {
            error = "Install failed (wrong key, corrupt file, or no write permission).";
            return false;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
