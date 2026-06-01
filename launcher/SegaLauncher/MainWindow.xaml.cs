using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace SegaLauncher;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        VerifyButton.IsEnabled = false;
        StatusText.Text = "Opening Discord… authorize in your browser, then come back.";

        var result = await DiscordAuth.RunAsync();
        switch (result.Outcome)
        {
            case AuthOutcome.Verified:
                StatusText.Text = "Verified! Launching the game…";
                DiscordAuth.LaunchGame(result.UnlockToken!);
                break;
            default:
                StatusText.Text = result.Message ?? "Verification failed.";
                break;
        }
        VerifyButton.IsEnabled = true;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
