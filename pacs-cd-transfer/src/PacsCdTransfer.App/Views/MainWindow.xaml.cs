using System.Windows;

namespace PacsCdTransfer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AetLabel.Text = $"AET: {App.Settings.LocalAeTitle}  ·  Port: {App.Settings.LocalPort}";
        UserLabel.Text = App.CurrentUser is { } u ? $"{u.Username}{(u.IsAdmin ? " (Yönetici)" : string.Empty)}" : string.Empty;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = this };
        settingsWindow.ShowDialog();
        AetLabel.Text = $"AET: {App.Settings.LocalAeTitle}  ·  Port: {App.Settings.LocalPort}";
        CdTransferTab.RefreshDestinations();
        QueryTab.RefreshSources();
    }
}
