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

    // Real transfer history for this session — backs the home screen's "Son Aktarılanlar"
    // KPI cards and table, which used to show the mockup's hardcoded demo numbers.
    private sealed record TransferRecord(string PatientName, string Modality, string Destination, DateTime Timestamp, bool Success, long Bytes);
    private readonly List<TransferRecord> _transferHistory = new();

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
                "getRecentTransfers" => HandleGetRecentTransfers(),
                "getAppSettings" => HandleGetAppSettings(),
                "saveAppSettings" => HandleSaveAppSettings(request.Payload),
                "getLocalIp" => HandleGetLocalIp(),
                "getUsers" => HandleGetUsers(),
                "addUser" => HandleAddUser(request.Payload),
                "removeUser" => HandleRemoveUser(request.Payload),
                "addDestination" => HandleAddDestination(request.Payload),
                "removeDestination" => HandleRemoveDestination(request.Payload),
                "setDefaultDestination" => HandleSetDefaultDestination(request.Payload),
                "addSource" => HandleAddSource(request.Payload),
                "removeSource" => HandleRemoveSource(request.Payload),
                "setDefaultSource" => HandleSetDefaultSource(request.Payload),
                "getLog" => HandleGetLog(),
                "clearLog" => HandleClearLog(),
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

            var bytes = filteredStudy.Series.SelectMany(s => s.FilePaths)
                .Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
            _transferHistory.Insert(0, new TransferRecord(study.PatientName, study.Modality ?? "", destination.AeTitle, DateTime.Now, failed == 0, bytes));

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

    private object HandleGetRecentTransfers()
    {
        var today = DateTime.Today;
        var sentToday = _transferHistory.Count(t => t.Success && t.Timestamp.Date == today);
        var failedToday = _transferHistory.Count(t => !t.Success && t.Timestamp.Date == today);
        var totalBytes = _transferHistory.Where(t => t.Success).Sum(t => t.Bytes);

        return new
        {
            ok = true,
            sentToday,
            failedToday,
            totalDataFormatted = FormatBytes(totalBytes),
            rows = _transferHistory.Take(10).Select(t => new
            {
                patientName = t.PatientName,
                modality = t.Modality,
                destination = t.Destination,
                time = t.Timestamp.ToString("dd.MM.yyyy HH:mm"),
                success = t.Success
            }).ToList()
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824.0).ToString("0.0") + " GB";
        if (bytes >= 1_048_576) return (bytes / 1_048_576.0).ToString("0.0") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        return bytes + " B";
    }

    private static object HandleGetAppSettings() => new
    {
        ok = true,
        localAeTitle = App.Settings.LocalAeTitle,
        localPort = App.Settings.LocalPort,
        storageFolder = App.Settings.TempStorageFolder,
        maxBatchSeries = App.Settings.MaxBatchSeries
    };

    private static object HandleSaveAppSettings(JsonElement payload)
    {
        if (payload.TryGetProperty("localAeTitle", out var ae) && !string.IsNullOrWhiteSpace(ae.GetString()))
            App.Settings.LocalAeTitle = ae.GetString()!;
        if (payload.TryGetProperty("localPort", out var port) && port.TryGetInt32(out var portNum) && portNum > 0)
            App.Settings.LocalPort = portNum;
        if (payload.TryGetProperty("storageFolder", out var sf) && !string.IsNullOrWhiteSpace(sf.GetString()))
            App.Settings.TempStorageFolder = sf.GetString()!;
        if (payload.TryGetProperty("maxBatchSeries", out var mb) && mb.TryGetInt32(out var mbNum) && mbNum > 0)
            App.Settings.MaxBatchSeries = mbNum;

        App.SaveSettings();
        return new
        {
            ok = true,
            note = "Yerel AE/Port değişikliği, DICOM sunucusunun yeniden başlatılması için uygulamanın kapatılıp açılmasını gerektirir."
        };
    }

    private static object HandleGetLocalIp()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
            return new { ok = true, ip = endPoint?.Address.ToString() ?? "127.0.0.1" };
        }
        catch
        {
            return new { ok = true, ip = "127.0.0.1" };
        }
    }

    private static object HandleGetUsers() => new
    {
        ok = true,
        users = App.Settings.Users.Select(u => new
        {
            username = u.Username,
            isAdmin = u.IsAdmin,
            canTransferCd = u.CanTransferCd,
            canQueryRetrieve = u.CanQueryRetrieve
        }).ToList()
    };

    private static object HandleAddUser(JsonElement payload)
    {
        var username = payload.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = payload.TryGetProperty("password", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new { ok = false, error = "Kullanıcı adı ve şifre gerekli." };

        if (App.Settings.Users.Any(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            return new { ok = false, error = "Bu kullanıcı adı zaten kullanılıyor." };

        var canTransfer = payload.TryGetProperty("canTransferCd", out var ct) && ct.ValueKind == JsonValueKind.True;
        var canQuery = payload.TryGetProperty("canQueryRetrieve", out var cq) && cq.ValueKind == JsonValueKind.True;

        App.Settings.Users.Add(new UserAccount
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = false,
            CanTransferCd = canTransfer,
            CanQueryRetrieve = canQuery
        });
        App.SaveSettings();
        return new { ok = true };
    }

    private static object HandleRemoveUser(JsonElement payload)
    {
        var username = payload.TryGetProperty("username", out var u) ? u.GetString() : null;
        var user = App.Settings.Users.FirstOrDefault(x => x.Username == username);
        if (user is null) return new { ok = false, error = "Kullanıcı bulunamadı." };
        if (user.IsAdmin) return new { ok = false, error = "Yönetici hesabı silinemez." };

        App.Settings.Users.Remove(user);
        App.SaveSettings();
        return new { ok = true };
    }

    private static object HandleAddDestination(JsonElement payload) => AddNode(payload, App.Settings.SendDestinations);
    private static object HandleAddSource(JsonElement payload) => AddNode(payload, App.Settings.QuerySources, readRetrieveMode: true);

    private static object AddNode(JsonElement payload, List<PacsNode> list, bool readRetrieveMode = false)
    {
        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var host = payload.TryGetProperty("host", out var h) ? h.GetString() : null;
        var portOk = payload.TryGetProperty("port", out var p) && p.TryGetInt32(out var port);
        if (string.IsNullOrWhiteSpace(ae) || string.IsNullOrWhiteSpace(host) || !portOk)
            return new { ok = false, error = "AE Title, Host ve Port gerekli." };

        var mode = RetrieveMode.CMove;
        if (readRetrieveMode && payload.TryGetProperty("retrieveMode", out var rm) && rm.GetString() == "cget")
            mode = RetrieveMode.CGet;

        list.Add(new PacsNode
        {
            AeTitle = ae,
            Host = host,
            Port = port,
            IsDefault = list.Count == 0,
            RetrieveMode = mode
        });
        App.SaveSettings();
        return new { ok = true };
    }

    private static object HandleRemoveDestination(JsonElement payload) => RemoveNode(payload, App.Settings.SendDestinations);
    private static object HandleRemoveSource(JsonElement payload) => RemoveNode(payload, App.Settings.QuerySources);

    private static object RemoveNode(JsonElement payload, List<PacsNode> list)
    {
        if (list.Count <= 1) return new { ok = false, error = "En az bir kayıt kalmalı." };
        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var node = list.FirstOrDefault(n => n.AeTitle == ae);
        if (node is null) return new { ok = false, error = "Kayıt bulunamadı." };

        var wasDefault = node.IsDefault;
        list.Remove(node);
        if (wasDefault && list.Count > 0) list[0].IsDefault = true;
        App.SaveSettings();
        return new { ok = true };
    }

    private static object HandleSetDefaultDestination(JsonElement payload) => SetDefaultNode(payload, App.Settings.SendDestinations);
    private static object HandleSetDefaultSource(JsonElement payload) => SetDefaultNode(payload, App.Settings.QuerySources);

    private static object SetDefaultNode(JsonElement payload, List<PacsNode> list)
    {
        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var node = list.FirstOrDefault(n => n.AeTitle == ae);
        if (node is null) return new { ok = false, error = "Kayıt bulunamadı." };

        foreach (var n in list) n.IsDefault = false;
        node.IsDefault = true;
        App.SaveSettings();
        return new { ok = true };
    }

    private static object HandleGetLog() => new
    {
        ok = true,
        entries = App.Log.Entries.Select(e => new
        {
            success = e.Success,
            title = e.Title,
            message = e.Message,
            time = e.Timestamp.ToString("dd.MM.yyyy HH:mm")
        }).ToList()
    };

    private static object HandleClearLog()
    {
        App.Log.Clear();
        return new { ok = true };
    }

    private static PacsNode? ReadNode(JsonElement payload)
    {
        var aeTitle = payload.TryGetProperty("aeTitle", out var ae) ? ae.GetString() : null;
        if (string.IsNullOrEmpty(aeTitle)) return null;

        return App.Settings.SendDestinations.FirstOrDefault(n => n.AeTitle == aeTitle)
               ?? App.Settings.QuerySources.FirstOrDefault(n => n.AeTitle == aeTitle);
    }
}
