using System.Windows;

namespace PacsCdTransfer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DiagLog.Write("MainWindow: constructor start");
        InitializeComponent();
        DiagLog.Write("MainWindow: InitializeComponent done");
        AetLabel.Text = $"AET: {App.Settings.LocalAeTitle}  ·  Port: {App.Settings.LocalPort}";
        UserLabel.Text = App.CurrentUser is { } u ? $"{u.Username}{(u.IsAdmin ? " (Yönetici)" : string.Empty)}" : string.Empty;
        DiagLog.Write("MainWindow: constructor end");
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
