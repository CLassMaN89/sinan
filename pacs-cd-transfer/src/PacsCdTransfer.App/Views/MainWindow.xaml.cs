using System.Windows;

namespace PacsCdTransfer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DiagLog.Write("MainWindow: constructor start");
        InitializeComponent();
        DiagLog.Write("MainWindow: InitializeComponent done");

        // Fixed, non-resizable window (ResizeMode=CanMinimize in XAML) — but the fixed size
        // itself scales down proportionally on smaller screens so it never opens larger than
        // the available work area (e.g. a 1366x768 laptop) instead of getting clipped.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width * 0.92);
        Height = Math.Min(Height, workArea.Height * 0.92);

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

    private void NavCdTransfer_Checked(object sender, RoutedEventArgs e)
    {
        if (CdTransferTab is null || QueryTab is null) return;
        CdTransferTab.Visibility = Visibility.Visible;
        QueryTab.Visibility = Visibility.Collapsed;
    }

    private void NavQuery_Checked(object sender, RoutedEventArgs e)
    {
        if (CdTransferTab is null || QueryTab is null) return;
        CdTransferTab.Visibility = Visibility.Collapsed;
        QueryTab.Visibility = Visibility.Visible;
    }
}
