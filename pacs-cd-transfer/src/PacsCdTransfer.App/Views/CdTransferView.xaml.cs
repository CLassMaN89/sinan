using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;
using PacsCdTransfer.Core.Validation;

namespace PacsCdTransfer.App.Views;

public partial class CdTransferView : UserControl
{
    private readonly CdImportService _cdImport = new();
    private readonly ObservableCollection<DicomStudyRecord> _studies = new();
    private readonly ObservableCollection<DicomStudyRecord> _worklist = new();
    private DicomStudyRecord? _selected;

    public CdTransferView()
    {
        InitializeComponent();
        StudyList.ItemsSource = _studies;
        WorklistBox.ItemsSource = _worklist;
        RefreshDestinations();
    }

    public void RefreshDestinations()
    {
        var previous = (DestCombo.SelectedItem as PacsNode)?.AeTitle;
        DestCombo.ItemsSource = App.Settings.SendDestinations;
        var toSelect = App.Settings.SendDestinations.FirstOrDefault(n => n.AeTitle == previous)
                       ?? App.Settings.SendDestinations.FirstOrDefault(n => n.IsDefault)
                       ?? App.Settings.SendDestinations.FirstOrDefault();
        DestCombo.SelectedItem = toSelect;
    }

    private void ScanDrives_Click(object sender, RoutedEventArgs e)
    {
        DriveList.ItemsSource = CdImportService.GetReadyOpticalDrives().ToList();
        if (DriveList.Items.Count == 0)
            StatusText.Text = "Hazır durumda CD/DVD sürücüsü bulunamadı.";
    }

    private void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ReadDiscButton.IsEnabled = DriveList.SelectedItem is not null;
    }

    private async void ReadDisc_Click(object sender, RoutedEventArgs e)
    {
        if (DriveList.SelectedItem is not string drive) return;
        StatusText.Text = "CD okunuyor…";
        ReadDiscButton.IsEnabled = false;
        try
        {
            var studies = await _cdImport.ReadDiscAsync(drive);
            _studies.Clear();
            foreach (var s in studies) _studies.Add(s);
            StatusText.Text = $"CD okundu — {studies.Count} tetkik bulundu.";
            App.Log.Log(true, "CD Okuma", $"{drive} sürücüsünden {studies.Count} tetkik okundu.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "CD okunamadı: " + ex.Message;
            App.Log.Log(false, "CD Okuma", $"{drive} sürücüsü okunamadı: {ex.Message}");
        }
        finally
        {
            ReadDiscButton.IsEnabled = true;
        }
    }

    private void StudyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = StudyList.SelectedItem as DicomStudyRecord;
        if (_selected is null) return;

        PatientNameBox.Text = _selected.PatientName;
        TcBox.Text = _selected.TcKimlikNo ?? string.Empty;
        SeriesList.ItemsSource = _selected.Series.Select(s => $"{s.Modality} — {s.SeriesDescription} ({s.InstanceCount} görüntü)").ToList();

        if (!_worklist.Contains(_selected)) _worklist.Add(_selected);
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "Önce bir hasta seçin."; return; }
        if (DestCombo.SelectedItem is not PacsNode destination) { StatusText.Text = "Bir hedef seçin."; return; }

        _selected.PatientName = PatientNameBox.Text.Trim();
        _selected.TcKimlikNo = TcBox.Text.Trim();

        if (!TcKimlikValidator.HasValidFormat(_selected.TcKimlikNo))
        {
            TcError.Visibility = Visibility.Visible;
            StatusText.Text = "TC Kimlik No 11 haneli değil — gönderim engellendi.";
            return;
        }
        TcError.Visibility = Visibility.Collapsed;

        StatusText.Text = "Gönderiliyor…";
        try
        {
            var network = new DicomNetworkService(App.Settings.LocalAeTitle);
            var orchestrator = new TransferOrchestrator(network, App.Settings.MaxBatchSeries);
            var outcomes = await orchestrator.SendStudiesAsync(destination, new[] { _selected });

            var sent = outcomes.Sum(o => o.SentCount);
            var failed = outcomes.Sum(o => o.FailedCount);
            StatusText.Text = failed == 0
                ? $"Gönderildi — {sent} görüntü {destination.AeTitle} adresine iletildi."
                : $"Kısmi hata — {sent} gönderildi, {failed} başarısız.";

            App.Log.Log(failed == 0, "PACS'e Gönderim",
                $"{_selected.PatientName} → {destination} — {sent} gönderildi, {failed} hata.");
        }
        catch (TcValidationException ex)
        {
            StatusText.Text = "TC Kimlik No geçersiz: " + string.Join(", ", ex.InvalidPatientNames);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Gönderim hatası: " + ex.Message;
            App.Log.Log(false, "PACS'e Gönderim", $"{destination} — {ex.Message}");
        }
    }
}
