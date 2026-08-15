using System.Windows;
using System.Windows.Controls;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;

namespace PacsCdTransfer.App.Views;

public partial class SettingsWindow : Window
{
    private readonly DicomNetworkService _network = new(App.Settings.LocalAeTitle);

    public SettingsWindow()
    {
        InitializeComponent();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = App.Settings;
        LocalAeBox.Text = s.LocalAeTitle;
        LocalPortBox.Text = s.LocalPort.ToString();
        StorageFolderBox.Text = s.TempStorageFolder;
        MaxBatchBox.Text = s.MaxBatchSeries.ToString();

        DestList.ItemsSource = s.SendDestinations;
        SrcList.ItemsSource = s.QuerySources;
        UserList.ItemsSource = s.Users.Select(u =>
            $"{u.Username}{(u.IsAdmin ? " (Yönetici)" : string.Empty)} — CD:{(u.CanTransferCd ? "✓" : "✗")} Sorgu:{(u.CanQueryRetrieve ? "✓" : "✗")}").ToList();
        LogList.ItemsSource = App.Log.Entries.Select(e => $"[{e.Timestamp:dd.MM.yyyy HH:mm}] {(e.Success ? "OK" : "HATA")} — {e.Title}: {e.Message}").ToList();
    }

    private void AddDest_Click(object sender, RoutedEventArgs e)
    {
        var ae = NewDestAe.Text.Trim();
        var host = NewDestHost.Text.Trim();
        if (string.IsNullOrEmpty(ae) || string.IsNullOrEmpty(host)) return;
        if (!int.TryParse(NewDestPort.Text.Trim(), out var port)) port = 104;

        App.Settings.SendDestinations.Add(new PacsNode
        {
            AeTitle = ae,
            Host = host,
            Port = port,
            IsDefault = App.Settings.SendDestinations.Count == 0
        });
        NewDestAe.Clear(); NewDestHost.Clear(); NewDestPort.Text = "104";
        RefreshLists();
    }

    private void RemoveDest_Click(object sender, RoutedEventArgs e)
    {
        if (DestList.SelectedItem is PacsNode node && App.Settings.SendDestinations.Count > 1)
        {
            App.Settings.SendDestinations.Remove(node);
            RefreshLists();
        }
    }

    private void AddSrc_Click(object sender, RoutedEventArgs e)
    {
        var ae = NewSrcAe.Text.Trim();
        var host = NewSrcHost.Text.Trim();
        if (string.IsNullOrEmpty(ae) || string.IsNullOrEmpty(host)) return;
        if (!int.TryParse(NewSrcPort.Text.Trim(), out var port)) port = 104;

        App.Settings.QuerySources.Add(new PacsNode
        {
            AeTitle = ae,
            Host = host,
            Port = port,
            IsDefault = App.Settings.QuerySources.Count == 0,
            RetrieveMode = NewSrcMode.SelectedIndex == 1 ? RetrieveMode.CGet : RetrieveMode.CMove
        });
        NewSrcAe.Clear(); NewSrcHost.Clear(); NewSrcPort.Text = "104";
        RefreshLists();
    }

    private void RemoveSrc_Click(object sender, RoutedEventArgs e)
    {
        if (SrcList.SelectedItem is PacsNode node && App.Settings.QuerySources.Count > 1)
        {
            App.Settings.QuerySources.Remove(node);
            RefreshLists();
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (DestList.SelectedItem is not PacsNode node)
        {
            TestResultText.Text = "Test edilecek bir hedef seçin.";
            return;
        }
        TestResultText.Text = "Test ediliyor…";
        var ok = await _network.EchoAsync(node);
        TestResultText.Text = ok ? $"✓ {node} ile bağlantı başarılı." : $"✕ {node} yanıt vermedi.";
        App.Log.Log(ok, "Bağlantı Testi — C-ECHO", ok ? $"{node} başarılı." : $"{node} yanıt vermedi.");
        LogList.ItemsSource = App.Log.Entries.Select(en => $"[{en.Timestamp:dd.MM.yyyy HH:mm}] {(en.Success ? "OK" : "HATA")} — {en.Title}: {en.Message}").ToList();
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        if (App.CurrentUser?.IsAdmin != true) { MessageBox.Show("Yalnızca yönetici kullanıcı ekleyebilir."); return; }

        var name = NewUserName.Text.Trim();
        var pass = NewUserPassword.Password;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pass)) return;

        App.Settings.Users.Add(new UserAccount
        {
            Username = name,
            PasswordHash = PasswordHasher.Hash(pass),
            IsAdmin = false,
            CanTransferCd = NewUserCanTransfer.IsChecked == true,
            CanQueryRetrieve = NewUserCanQuery.IsChecked == true
        });
        NewUserName.Clear(); NewUserPassword.Clear();
        RefreshLists();
    }

    private void RemoveUser_Click(object sender, RoutedEventArgs e)
    {
        if (App.CurrentUser?.IsAdmin != true) { MessageBox.Show("Yalnızca yönetici kullanıcı silebilir."); return; }
        var index = UserList.SelectedIndex;
        if (index < 0 || index >= App.Settings.Users.Count) return;
        var user = App.Settings.Users[index];
        if (user.IsAdmin) return;
        App.Settings.Users.Remove(user);
        RefreshLists();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        App.Log.Clear();
        RefreshLists();
    }

    private void RefreshLists() => LoadFromSettings();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.LocalAeTitle = LocalAeBox.Text.Trim();
        if (int.TryParse(LocalPortBox.Text.Trim(), out var port)) App.Settings.LocalPort = port;
        App.Settings.TempStorageFolder = StorageFolderBox.Text.Trim();
        if (int.TryParse(MaxBatchBox.Text.Trim(), out var maxBatch)) App.Settings.MaxBatchSeries = maxBatch;

        App.SaveSettings();
        SavedText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
