using System.IO;
using System.Windows;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;

namespace PacsCdTransfer.App;

public partial class App : Application
{
    public static AppSettingsStore SettingsStore { get; private set; } = null!;
    public static AppSettings Settings { get; set; } = null!;
    public static DicomServerHost? ServerHost { get; private set; }
    public static UserAccount? CurrentUser { get; set; }
    public static ConnectionLogService Log { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DiagLog.Write("OnStartup begin");

        // Surface any crash as a message box instead of the process silently vanishing —
        // WPF's default unhandled-exception behavior gives no visible feedback at all.
        DispatcherUnhandledException += (_, args) =>
        {
            DiagLog.Write("DispatcherUnhandledException: " + args.Exception);
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n\n{args.Exception}",
                "PACS CD Transfer — Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            DiagLog.Write("AppDomain.UnhandledException: " + args.ExceptionObject);
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n\n{args.ExceptionObject}",
                "PACS CD Transfer — Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            RunStartup();
        }
        catch (Exception ex)
        {
            DiagLog.Write("RunStartup threw: " + ex);
            MessageBox.Show(
                $"Uygulama başlatılırken hata oluştu:\n\n{ex}",
                "PACS CD Transfer — Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void RunStartup()
    {
        DiagLog.Write("RunStartup: loading settings");
        SettingsStore = new AppSettingsStore();
        Settings = SettingsStore.Load();
        DiagLog.Write($"RunStartup: settings loaded, {Settings.Users.Count} user(s), config at {SettingsStore.ConfigPath}");

        var storageRoot = Path.IsPathRooted(Settings.TempStorageFolder)
            ? Settings.TempStorageFolder
            : Path.Combine(AppContext.BaseDirectory, Settings.TempStorageFolder);
        Directory.CreateDirectory(storageRoot);
        DiagLog.Write("RunStartup: storage root ready at " + storageRoot);

        ServerHost = new DicomServerHost(Settings.LocalPort, Settings.LocalAeTitle, storageRoot);
        try
        {
            ServerHost.Start();
            DiagLog.Write($"RunStartup: DICOM SCP listening on port {Settings.LocalPort}");
        }
        catch (Exception ex)
        {
            DiagLog.Write("RunStartup: DICOM SCP failed to start: " + ex);
            MessageBox.Show(
                $"Yerel DICOM sunucusu {Settings.LocalPort} portunda başlatılamadı: {ex.Message}\n" +
                "Ayarlar > Dicom Bilgileri'nden farklı bir port deneyin.",
                "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DiagLog.Write("RunStartup: showing login window");
        var login = new Views.LoginWindow();
        var ok = login.ShowDialog();
        DiagLog.Write("RunStartup: login window closed, DialogResult=" + ok);
        if (ok != true)
        {
            DiagLog.Write("RunStartup: shutting down (login not confirmed)");
            Shutdown();
            return;
        }

        DiagLog.Write("RunStartup: constructing MainWindow");
        var main = new Views.MainWindow();
        MainWindow = main;
        DiagLog.Write("RunStartup: showing MainWindow");
        main.Show();
        DiagLog.Write("RunStartup: MainWindow.Show() returned");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ServerHost?.Dispose();
        base.OnExit(e);
    }

    public static void SaveSettings() => SettingsStore.Save(Settings);
}
