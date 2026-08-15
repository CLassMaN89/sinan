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

        // Surface any crash as a message box instead of the process silently vanishing —
        // WPF's default unhandled-exception behavior gives no visible feedback at all.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n\n{args.Exception}",
                "PACS CD Transfer — Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
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
            MessageBox.Show(
                $"Uygulama başlatılırken hata oluştu:\n\n{ex}",
                "PACS CD Transfer — Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void RunStartup()
    {
        SettingsStore = new AppSettingsStore();
        Settings = SettingsStore.Load();

        var storageRoot = Path.IsPathRooted(Settings.TempStorageFolder)
            ? Settings.TempStorageFolder
            : Path.Combine(AppContext.BaseDirectory, Settings.TempStorageFolder);
        Directory.CreateDirectory(storageRoot);

        ServerHost = new DicomServerHost(Settings.LocalPort, Settings.LocalAeTitle, storageRoot);
        try
        {
            ServerHost.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Yerel DICOM sunucusu {Settings.LocalPort} portunda başlatılamadı: {ex.Message}\n" +
                "Ayarlar > Dicom Bilgileri'nden farklı bir port deneyin.",
                "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var login = new Views.LoginWindow();
        var ok = login.ShowDialog();
        if (ok != true)
        {
            Shutdown();
            return;
        }

        var main = new Views.MainWindow();
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ServerHost?.Dispose();
        base.OnExit(e);
    }

    public static void SaveSettings() => SettingsStore.Save(Settings);
}
