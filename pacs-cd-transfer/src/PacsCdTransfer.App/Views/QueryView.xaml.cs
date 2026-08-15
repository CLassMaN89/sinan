using System.IO;
using System.Windows;
using System.Windows.Controls;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;

namespace PacsCdTransfer.App.Views;

public partial class QueryView : UserControl
{
    public QueryView()
    {
        InitializeComponent();
        RefreshSources();
    }

    public void RefreshSources()
    {
        var previous = (SourceCombo.SelectedItem as PacsNode)?.AeTitle;
        SourceCombo.ItemsSource = App.Settings.QuerySources;
        var toSelect = App.Settings.QuerySources.FirstOrDefault(n => n.AeTitle == previous)
                       ?? App.Settings.QuerySources.FirstOrDefault(n => n.IsDefault)
                       ?? App.Settings.QuerySources.FirstOrDefault();
        SourceCombo.SelectedItem = toSelect;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (SourceCombo.SelectedItem is not PacsNode source)
        {
            StatusText.Text = "Bir sorgu kaynağı seçin.";
            return;
        }

        StatusText.Text = "Sorgulanıyor…";
        try
        {
            var network = new DicomNetworkService(App.Settings.LocalAeTitle);
            var results = await network.FindStudiesAsync(
                source,
                patientName: string.IsNullOrWhiteSpace(PatientNameBox.Text) ? null : PatientNameBox.Text.Trim(),
                patientId: string.IsNullOrWhiteSpace(PatientIdBox.Text) ? null : PatientIdBox.Text.Trim(),
                accessionNumber: string.IsNullOrWhiteSpace(AccessionBox.Text) ? null : AccessionBox.Text.Trim());

            ResultsList.ItemsSource = results;
            StatusText.Text = $"{results.Count} tetkik bulundu.";
            App.Log.Log(true, "PACS Sorgu — C-FIND", $"{source} — {results.Count} sonuç.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Sorgu hatası: " + ex.Message;
            App.Log.Log(false, "PACS Sorgu — C-FIND", $"{source} — {ex.Message}");
        }
    }

    private async void Retrieve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DicomStudyRecord study }) return;
        if (SourceCombo.SelectedItem is not PacsNode source) return;

        var button = (Button)sender;
        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = "İndiriliyor…";

        try
        {
            var network = new DicomNetworkService(App.Settings.LocalAeTitle);
            var storageRoot = Path.IsPathRooted(App.Settings.TempStorageFolder)
                ? App.Settings.TempStorageFolder
                : Path.Combine(AppContext.BaseDirectory, App.Settings.TempStorageFolder);

            var outcome = source.RetrieveMode == RetrieveMode.CMove
                ? await network.MoveStudyAsync(source, study.StudyInstanceUid, App.Settings.LocalAeTitle)
                : await network.GetStudyAsync(source, study.StudyInstanceUid, storageRoot);

            button.Content = outcome.Success ? "İndirildi" : "Hata";
            App.Log.Log(outcome.Success, "PACS Getir",
                $"{study.PatientName} ← {source} ({source.RetrieveMode}) — {outcome.SentCount} görüntü." +
                (outcome.Success ? string.Empty : $" Hata: {outcome.ErrorMessage}"));
        }
        catch (Exception ex)
        {
            button.Content = "Hata";
            App.Log.Log(false, "PACS Getir", $"{study.PatientName} ← {source} — {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
            if ((string)button.Content == "Getir") button.Content = originalContent;
        }
    }
}
