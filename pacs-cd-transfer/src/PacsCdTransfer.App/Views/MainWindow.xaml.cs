using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace PacsCdTransfer.App.Views;

public partial class MainWindow : Window
{
    private readonly WebBridge _bridge = new();

    public MainWindow()
    {
        DiagLog.Write("MainWindow: constructor start");
        InitializeComponent();
        DiagLog.Write("MainWindow: InitializeComponent done");

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width * 0.92);
        Height = Math.Min(Height, workArea.Height * 0.92);

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(AppContext.BaseDirectory, "WebView2Data");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await Browser.EnsureCoreWebView2Async(env);

            Browser.CoreWebView2.WebMessageReceived += async (_, args) =>
            {
                var json = args.TryGetWebMessageAsString();
                var response = await _bridge.HandleAsync(json);
                Browser.CoreWebView2.PostWebMessageAsJson(response);
            };

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "app.html");
            Browser.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            LoadingText.Visibility = Visibility.Collapsed;
            DiagLog.Write("MainWindow: WebView2 navigated to " + htmlPath);
        }
        catch (Exception ex)
        {
            DiagLog.Write("MainWindow: WebView2 init failed: " + ex);
            LoadingText.Text =
                "Ekran yüklenemedi — Microsoft Edge WebView2 Runtime kurulu olmayabilir.\n" +
                "Windows 11'de ve güncel Windows 10'da genelde hazır gelir.\n\nHata: " + ex.Message;
        }
    }
}
