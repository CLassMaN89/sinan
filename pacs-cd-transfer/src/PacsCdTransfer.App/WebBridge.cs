using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;
using PacsCdTransfer.Core.Validation;

namespace PacsCdTransfer.App;

/// <summary>
/// JS ↔ C# bridge for the WebView2-hosted mockup. JS calls <c>nativeCall(action, payload)</c>
/// (see app.html), which posts <c>{id, action, payload}</c> here; we dispatch to the real
/// backend and post <c>{id, result}</c> back so the pixel-identical UI is driven by real
/// DICOM/CD operations instead of the mockup's demo timers.
/// </summary>
public sealed class WebBridge
{
    private readonly AuthService _auth = new();
    private readonly CdImportService _cdImport = new();

    // Studies scanned this session, keyed by StudyInstanceUID — sendStudy/retrieve reference
    // these by key instead of round-tripping full file-path lists through JSON every call.
    private readonly Dictionary<string, DicomStudyRecord> _scannedStudies = new();
    private readonly Dictionary<string, DicomStudyRecord> _foundStudies = new();

    private sealed record BridgeRequest(int Id, string Action, JsonElement Payload);
    private sealed record BridgeResponse(int Id, object? Result);

    public async Task<string> HandleAsync(string requestJson)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(requestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return "{}";
        }

        if (request is null) return "{}";

        object? result;
        try
        {
            result = request.Action switch
            {
                "login" => HandleLogin(request.Payload),
                "scanDrives" => HandleScanDrives(),
                "pickFolder" => HandlePickFolder(),
                "readSource" => await HandleReadSourceAsync(request.Payload),
                "getDestinations" => HandleGetDestinations(),
                "getSources" => HandleGetSources(),
                "testConnection" => await HandleTestConnectionAsync(request.Payload),
                "sendStudy" => await HandleSendStudyAsync(request.Payload),
                "findStudies" => await HandleFindStudiesAsync(request.Payload),
                "retrieveStudy" => await HandleRetrieveStudyAsync(request.Payload),
                _ => new { ok = false, error = "unknown action: " + request.Action }
            };
        }
        catch (Exception ex)
        {
            result = new { ok = false, error = ex.Message };
        }

        var response = new BridgeResponse(request.Id, result);
        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private object HandleLogin(JsonElement payload)
    {
        var username = payload.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
        var password = payload.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";

        var user = _auth.TryLogin(App.Settings, username, password);
        if (user is null) return new { ok = false };

        App.CurrentUser = user;
        return new { ok = true, username = user.Username, isAdmin = user.IsAdmin };
    }

    private static object HandleScanDrives()
    {
        var drives = CdImportService.GetReadyOpticalDrives().ToList();
        return new { ok = true, drives };
    }

    private static object HandlePickFolder()
    {
        // Microsoft.Win32.OpenFolderDialog — native Windows folder picker, .NET 8+.
        var dialog = new OpenFolderDialog { Title = "Klasör Seçin" };
        var chosen = dialog.ShowDialog() == true;
        return new { ok = chosen, path = chosen ? dialog.FolderName : null };
    }

    private async Task<object> HandleReadSourceAsync(JsonElement payload)
    {
        var path = payload.TryGetProperty("path", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return new { ok = false, error = "Geçerli bir klasör/sürücü yolu değil." };

        var studies = await _cdImport.ReadDiscAsync(path);
        _scannedStudies.Clear();
        foreach (var s in studies) _scannedStudies[s.StudyInstanceUid] = s;

        return new
        {
            ok = true,
            studies = studies.Select(ToJsonStudy).ToList()
        };
    }

    private static object ToJsonStudy(DicomStudyRecord s) => new
    {
        studyUid = s.StudyInstanceUid,
        patientName = s.PatientName,
        patientId = s.PatientId,
        accessionNumber = s.AccessionNumber ?? "",
        tcKimlikNo = s.TcKimlikNo ?? "",
        modality = s.Modality ?? "",
        institutionName = s.InstitutionName ?? "",
        studyDate = s.StudyDate?.ToString("dd.MM.yyyy") ?? "",
        seriesCount = s.SeriesCount,
        instanceCount = s.Series.Sum(sr => sr.InstanceCount),
        series = s.Series.Select(sr => new
        {
            seriesUid = sr.SeriesInstanceUid,
            description = sr.SeriesDescription ?? "",
            modality = sr.Modality ?? "",
            instanceCount = sr.InstanceCount
        }).ToList()
    };

    private static object HandleGetDestinations() => new
    {
        ok = true,
        destinations = App.Settings.SendDestinations.Select(ToJsonNode).ToList()
    };

    private static object HandleGetSources() => new
    {
        ok = true,
        sources = App.Settings.QuerySources.Select(ToJsonNode).ToList()
    };

    private static object ToJsonNode(PacsNode n) => new
    {
        aeTitle = n.AeTitle,
        host = n.Host,
        port = n.Port,
        isDefault = n.IsDefault,
        retrieveMode = n.RetrieveMode == RetrieveMode.CGet ? "cget" : "cmove"
    };

    private async Task<object> HandleTestConnectionAsync(JsonElement payload)
    {
        var node = ReadNode(payload);
        if (node is null) return new { ok = false, error = "Hedef bulunamadı." };

        var network = new DicomNetworkService(App.Settings.LocalAeTitle);
        var ok = await network.EchoAsync(node);
        App.Log.Log(ok, "Bağlantı Testi — C-ECHO", ok ? $"{node} başarılı." : $"{node} yanıt vermedi.");
        return new { ok };
    }

    private async Task<object> HandleSendStudyAsync(JsonElement payload)
    {
        var studyUid = payload.TryGetProperty("studyUid", out var su) ? su.GetString() : null;
        var seriesUids = payload.TryGetProperty("seriesUids", out var suArr) && suArr.ValueKind == JsonValueKind.Array
            ? suArr.EnumerateArray().Select(x => x.GetString()!).ToHashSet()
            : null;
        var patientName = payload.TryGetProperty("patientName", out var pn) ? pn.GetString() : null;
        var tcKimlikNo = payload.TryGetProperty("tcKimlikNo", out var tc) ? tc.GetString() : null;

        if (studyUid is null || !_scannedStudies.TryGetValue(studyUid, out var study))
            return new { ok = false, error = "Bu tetkik CD/klasör taramasında bulunamadı." };

        var destination = ReadNode(payload);
        if (destination is null) return new { ok = false, error = "Gönderilecek hedef seçilmedi." };

        study.PatientName = patientName ?? study.PatientName;
        study.TcKimlikNo = tcKimlikNo ?? study.TcKimlikNo;

        if (!TcKimlikValidator.HasValidFormat(study.TcKimlikNo))
            return new { ok = false, error = "TC Kimlik No 11 haneli değil." };

        var filteredStudy = seriesUids is null
            ? study
            : new DicomStudyRecord
            {
                PatientName = study.PatientName,
                PatientId = study.PatientId,
                StudyInstanceUid = study.StudyInstanceUid,
                TcKimlikNo = study.TcKimlikNo,
                Series = study.Series.Where(s => seriesUids.Contains(s.SeriesInstanceUid)).ToList()
            };

        var network = new DicomNetworkService(App.Settings.LocalAeTitle);
        var orchestrator = new TransferOrchestrator(network, App.Settings.MaxBatchSeries);
        try
        {
            var outcomes = await orchestrator.SendStudiesAsync(destination, new[] { filteredStudy });
            var sent = outcomes.Sum(o => o.SentCount);
            var failed = outcomes.Sum(o => o.FailedCount);
            App.Log.Log(failed == 0, "PACS'e Gönderim", $"{study.PatientName} → {destination} — {sent} gönderildi, {failed} hata.");
            return new { ok = failed == 0, sentCount = sent, failedCount = failed };
        }
        catch (TcValidationException ex)
        {
            return new { ok = false, error = "TC Kimlik No geçersiz: " + string.Join(", ", ex.InvalidPatientNames) };
        }
    }

    private async Task<object> HandleFindStudiesAsync(JsonElement payload)
    {
        var source = ReadNode(payload);
        if (source is null) return new { ok = false, error = "Sorgu kaynağı seçilmedi." };

        string? Get(string key) => payload.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString())
            : null;

        var network = new DicomNetworkService(App.Settings.LocalAeTitle);
        try
        {
            var results = await network.FindStudiesAsync(
                source,
                patientName: Get("patientName"),
                patientId: Get("patientId"),
                accessionNumber: Get("accessionNumber"),
                studyDateRange: Get("dateRange"));

            _foundStudies.Clear();
            foreach (var r in results) _foundStudies[r.StudyInstanceUid] = r;

            App.Log.Log(true, "PACS Sorgu — C-FIND", $"{source} — {results.Count} sonuç.");
            return new { ok = true, studies = results.Select(ToJsonStudy).ToList() };
        }
        catch (Exception ex)
        {
            App.Log.Log(false, "PACS Sorgu — C-FIND", $"{source} — {ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    private async Task<object> HandleRetrieveStudyAsync(JsonElement payload)
    {
        var source = ReadNode(payload);
        var studyUid = payload.TryGetProperty("studyUid", out var su) ? su.GetString() : null;
        if (source is null || studyUid is null || !_foundStudies.TryGetValue(studyUid, out var study))
            return new { ok = false, error = "Tetkik bulunamadı." };

        var network = new DicomNetworkService(App.Settings.LocalAeTitle);
        var storageRoot = Path.IsPathRooted(App.Settings.TempStorageFolder)
            ? App.Settings.TempStorageFolder
            : Path.Combine(AppContext.BaseDirectory, App.Settings.TempStorageFolder);

        try
        {
            var outcome = source.RetrieveMode == RetrieveMode.CMove
                ? await network.MoveStudyAsync(source, studyUid, App.Settings.LocalAeTitle)
                : await network.GetStudyAsync(source, studyUid, storageRoot);

            App.Log.Log(outcome.Success, "PACS Getir",
                $"{study.PatientName} ← {source} ({source.RetrieveMode}) — {outcome.SentCount} görüntü." +
                (outcome.Success ? "" : $" Hata: {outcome.ErrorMessage}"));

            return new { ok = outcome.Success, count = outcome.SentCount, error = outcome.ErrorMessage };
        }
        catch (Exception ex)
        {
            App.Log.Log(false, "PACS Getir", $"{study.PatientName} ← {source} — {ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    private static PacsNode? ReadNode(JsonElement payload)
    {
        var aeTitle = payload.TryGetProperty("aeTitle", out var ae) ? ae.GetString() : null;
        if (string.IsNullOrEmpty(aeTitle)) return null;

        return App.Settings.SendDestinations.FirstOrDefault(n => n.AeTitle == aeTitle)
               ?? App.Settings.QuerySources.FirstOrDefault(n => n.AeTitle == aeTitle);
    }
}
