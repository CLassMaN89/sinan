using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;
using PacsCdTransfer.Core.Validation;

namespace PacsCdTransfer.Web;

public sealed class WebBridge
{
    private readonly AuthService _auth = new();
    private readonly CdImportService _cdImport = new();
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore;
    private readonly ConnectionLogService _log;
    private UserAccount? _currentUser;

    // Studies scanned this session, keyed by StudyInstanceUID — sendStudy/retrieve reference
    // these by key instead of round-tripping full file-path lists through JSON every call.
    private readonly Dictionary<string, DicomStudyRecord> _scannedStudies = new();
    private readonly Dictionary<string, DicomStudyRecord> _foundStudies = new();

    public WebBridge(AppSettings settings, ConnectionLogService log)
    {
        _settings = settings;
        _settingsStore = new AppSettingsStore();
        _log = log;
    }

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
                "updateDestination" => HandleUpdateDestination(request.Payload),
                "addSource" => HandleAddSource(request.Payload),
                "removeSource" => HandleRemoveSource(request.Payload),
                "setDefaultSource" => HandleSetDefaultSource(request.Payload),
                "updateSource" => HandleUpdateSource(request.Payload),
                "getLog" => HandleGetLog(),
                "clearLog" => HandleClearLog(),
                "getStudyThumbnails" => HandleGetStudyThumbnails(request.Payload),
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

        var user = _auth.TryLogin(_settings, username, password);
        if (user is null) return new { ok = false };

        _currentUser = user;
        return new { ok = true, username = user.Username, isAdmin = user.IsAdmin };
    }

    private object HandleScanDrives()
    {
        var drives = CdImportService.GetReadyOpticalDrives().ToList();
        return new { ok = true, drives };
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

    private object ToJsonStudy(DicomStudyRecord s) => new
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

    private object HandleGetDestinations() => new
    {
        ok = true,
        destinations = _settings.SendDestinations.Select(ToJsonNode).ToList()
    };

    private object HandleGetSources() => new
    {
        ok = true,
        sources = _settings.QuerySources.Select(ToJsonNode).ToList()
    };

    private object ToJsonNode(PacsNode n) => new
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

        var network = new DicomNetworkService(_settings.LocalAeTitle);
        var ok = await network.EchoAsync(node);
        _log.Log(ok, "Bağlantı Testi — C-ECHO", ok ? $"{node} başarılı." : $"{node} yanıt vermedi.");
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

        if (filteredStudy.Series.Count == 0)
            return new { ok = false, error = "Seçili serilere ait dosya bulunamadı — gönderim yapılmadı." };
        if (filteredStudy.Series.All(s => s.FilePaths.Count == 0))
            return new { ok = false, error = "Seçili serilerde diskte dosya bulunamadı — gönderim yapılmadı." };

        var network = new DicomNetworkService(_settings.LocalAeTitle);
        var orchestrator = new TransferOrchestrator(network, _settings.MaxBatchSeries);
        try
        {
            var outcomes = await orchestrator.SendStudiesAsync(destination, new[] { filteredStudy });
            var sent = outcomes.Sum(o => o.SentCount);
            var failed = outcomes.Sum(o => o.FailedCount);

            // A 0-sent/0-failed outcome is not a success — it means nothing actually went out
            // (e.g. every attempted C-STORE association silently produced no result). Treating
            // it as ok:true was the root cause of "says sent, nothing arrives, no error shown".
            if (sent == 0)
            {
                _log.Log(false, "PACS'e Gönderim", $"{study.PatientName} → {destination} — 0 görüntü gönderildi (bağlantı kurulamamış olabilir).");
                return new { ok = false, error = "Hiçbir görüntü gönderilemedi — hedefe bağlanılamadı." };
            }

            _log.Log(failed == 0, "PACS'e Gönderim", $"{study.PatientName} → {destination} — {sent} gönderildi, {failed} hata.");

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

        var network = new DicomNetworkService(_settings.LocalAeTitle);
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

            _log.Log(true, "PACS Sorgu — C-FIND", $"{source} — {results.Count} sonuç.");
            return new { ok = true, studies = results.Select(ToJsonStudy).ToList() };
        }
        catch (Exception ex)
        {
            _log.Log(false, "PACS Sorgu — C-FIND", $"{source} — {ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    private async Task<object> HandleRetrieveStudyAsync(JsonElement payload)
    {
        var source = ReadNode(payload);
        var studyUid = payload.TryGetProperty("studyUid", out var su) ? su.GetString() : null;
        if (source is null || studyUid is null || !_foundStudies.TryGetValue(studyUid, out var study))
            return new { ok = false, error = "Tetkik bulunamadı." };

        var network = new DicomNetworkService(_settings.LocalAeTitle);
        var storageRoot = Path.IsPathRooted(_settings.TempStorageFolder)
            ? _settings.TempStorageFolder
            : Path.Combine(AppContext.BaseDirectory, _settings.TempStorageFolder);

        try
        {
            var outcome = source.RetrieveMode == RetrieveMode.CMove
                ? await network.MoveStudyAsync(source, studyUid, _settings.LocalAeTitle)
                : await network.GetStudyAsync(source, studyUid, storageRoot);

            _log.Log(outcome.Success, "PACS Getir",
                $"{study.PatientName} ← {source} ({source.RetrieveMode}) — {outcome.SentCount} görüntü." +
                (outcome.Success ? "" : $" Hata: {outcome.ErrorMessage}"));

            return new { ok = outcome.Success, count = outcome.SentCount, error = outcome.ErrorMessage };
        }
        catch (Exception ex)
        {
            _log.Log(false, "PACS Getir", $"{study.PatientName} ← {source} — {ex.Message}");
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

    private object HandleGetAppSettings() => new
    {
        ok = true,
        localAeTitle = _settings.LocalAeTitle,
        localPort = _settings.LocalPort,
        storageFolder = _settings.TempStorageFolder,
        maxBatchSeries = _settings.MaxBatchSeries
    };

    private object HandleSaveAppSettings(JsonElement payload)
    {
        if (payload.TryGetProperty("localAeTitle", out var ae) && !string.IsNullOrWhiteSpace(ae.GetString()))
            _settings.LocalAeTitle = ae.GetString()!;
        if (payload.TryGetProperty("localPort", out var port) && port.TryGetInt32(out var portNum) && portNum > 0)
            _settings.LocalPort = portNum;
        if (payload.TryGetProperty("storageFolder", out var sf) && !string.IsNullOrWhiteSpace(sf.GetString()))
            _settings.TempStorageFolder = sf.GetString()!;
        if (payload.TryGetProperty("maxBatchSeries", out var mb) && mb.TryGetInt32(out var mbNum) && mbNum > 0)
            _settings.MaxBatchSeries = mbNum;

        _settingsStore.Save(_settings);
        return new
        {
            ok = true,
            note = "Yerel AE/Port değişikliği, DICOM sunucusunun yeniden başlatılması için uygulamanın kapatılıp açılmasını gerektirir."
        };
    }

    private object HandleGetLocalIp()
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

    private object HandleGetUsers() => new
    {
        ok = true,
        users = _settings.Users.Select(u => new
        {
            username = u.Username,
            isAdmin = u.IsAdmin,
            canTransferCd = u.CanTransferCd,
            canQueryRetrieve = u.CanQueryRetrieve
        }).ToList()
    };

    private object HandleAddUser(JsonElement payload)
    {
        var username = payload.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = payload.TryGetProperty("password", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new { ok = false, error = "Kullanıcı adı ve şifre gerekli." };

        if (_settings.Users.Any(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            return new { ok = false, error = "Bu kullanıcı adı zaten kullanılıyor." };

        var canTransfer = payload.TryGetProperty("canTransferCd", out var ct) && ct.ValueKind == JsonValueKind.True;
        var canQuery = payload.TryGetProperty("canQueryRetrieve", out var cq) && cq.ValueKind == JsonValueKind.True;

        _settings.Users.Add(new UserAccount
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = false,
            CanTransferCd = canTransfer,
            CanQueryRetrieve = canQuery
        });
        _settingsStore.Save(_settings);
        return new { ok = true };
    }

    private object HandleRemoveUser(JsonElement payload)
    {
        var username = payload.TryGetProperty("username", out var u) ? u.GetString() : null;
        var user = _settings.Users.FirstOrDefault(x => x.Username == username);
        if (user is null) return new { ok = false, error = "Kullanıcı bulunamadı." };
        if (user.IsAdmin) return new { ok = false, error = "Yönetici hesabı silinemez." };

        _settings.Users.Remove(user);
        _settingsStore.Save(_settings);
        return new { ok = true };
    }

    private object HandleAddDestination(JsonElement payload) => AddNode(payload, _settings.SendDestinations);
    private object HandleAddSource(JsonElement payload) => AddNode(payload, _settings.QuerySources, readRetrieveMode: true);

    private object AddNode(JsonElement payload, List<PacsNode> list, bool readRetrieveMode = false)
    {
        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var host = payload.TryGetProperty("host", out var h) ? h.GetString() : null;
        var port = 0;
        var portOk = payload.TryGetProperty("port", out var p) && p.TryGetInt32(out port);
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
        _settingsStore.Save(_settings);
        return new { ok = true };
    }

    private object HandleRemoveDestination(JsonElement payload) => RemoveNode(payload, _settings.SendDestinations);
    private object HandleRemoveSource(JsonElement payload) => RemoveNode(payload, _settings.QuerySources);

    private object RemoveNode(JsonElement payload, List<PacsNode> list)
    {
        if (list.Count <= 1) return new { ok = false, error = "En az bir kayıt kalmalı." };
        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var node = list.FirstOrDefault(n => n.AeTitle == ae);
        if (node is null) return new { ok = false, error = "Kayıt bulunamadı." };

        var wasDefault = node.IsDefault;
        list.Remove(node);
        if (wasDefault && list.Count > 0) list[0].IsDefault = true;
        _settingsStore.Save(_settings);
        return new { ok = true };
    }

    private object HandleSetDefaultDestination(JsonElement payload) => SetDefaultNode(payload, _settings.SendDestinations);
    private object HandleSetDefaultSource(JsonElement payload) => SetDefaultNode(payload, _settings.QuerySources);

    private object SetDefaultNode(JsonElement payload, List<PacsNode> list)
    {
        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var node = list.FirstOrDefault(n => n.AeTitle == ae);
        if (node is null) return new { ok = false, error = "Kayıt bulunamadı." };

        foreach (var n in list) n.IsDefault = false;
        node.IsDefault = true;
        _settingsStore.Save(_settings);
        return new { ok = true };
    }

    private object HandleUpdateDestination(JsonElement payload) => UpdateNode(payload, _settings.SendDestinations);
    private object HandleUpdateSource(JsonElement payload) => UpdateNode(payload, _settings.QuerySources);

    // Fixes a bug where the inline pencil-edit only updated the on-screen row: Test/Send
    // would silently keep using the OLD saved host/port, making a "wrong port" test look
    // successful because it was never actually testing the port shown on screen.
    private object UpdateNode(JsonElement payload, List<PacsNode> list)
    {
        var originalAe = payload.TryGetProperty("originalAeTitle", out var oa) ? oa.GetString() : null;
        var node = list.FirstOrDefault(n => n.AeTitle == originalAe);
        if (node is null) return new { ok = false, error = "Kayıt bulunamadı." };

        var ae = payload.TryGetProperty("aeTitle", out var a) ? a.GetString() : null;
        var host = payload.TryGetProperty("host", out var h) ? h.GetString() : null;
        var port = 0;
        var portOk = payload.TryGetProperty("port", out var p) && p.TryGetInt32(out port);
        if (string.IsNullOrWhiteSpace(ae) || string.IsNullOrWhiteSpace(host) || !portOk)
            return new { ok = false, error = "AE Title, Host ve Port gerekli." };

        if (ae != originalAe && list.Any(n => n.AeTitle == ae))
            return new { ok = false, error = "Bu AE Title zaten kullanılıyor." };

        node.AeTitle = ae;
        node.Host = host;
        node.Port = port;
        _settingsStore.Save(_settings);
        return new { ok = true };
    }

    private object HandleGetLog() => new
    {
        ok = true,
        entries = _log.Entries.Select(e => new
        {
            success = e.Success,
            title = e.Title,
            message = e.Message,
            time = e.Timestamp.ToString("dd.MM.yyyy HH:mm")
        }).ToList()
    };

    private object HandleClearLog()
    {
        _log.Clear();
        return new { ok = true };
    }

    // Real per-series thumbnails rendered from actual DICOM pixel data — replaces the
    // mockup's decorative CSS discs. Looks in two places: the in-session scan cache (CD/folder
    // import, has FilePaths already) and, for studies pulled via Sorgu/Getir, the on-disk
    // storageRoot/{studyUid}/{seriesUid}/ folder that C-GET/C-MOVE receive into.
    private object HandleGetStudyThumbnails(JsonElement payload)
    {
        var studyUid = payload.TryGetProperty("studyUid", out var su) ? su.GetString() : null;
        if (string.IsNullOrEmpty(studyUid)) return new { ok = false, error = "studyUid gerekli." };

        var storageRoot = Path.IsPathRooted(_settings.TempStorageFolder)
            ? _settings.TempStorageFolder
            : Path.Combine(AppContext.BaseDirectory, _settings.TempStorageFolder);

        IEnumerable<(string SeriesUid, string? FirstFile)> seriesFiles;
        if (_scannedStudies.TryGetValue(studyUid, out var scanned))
        {
            seriesFiles = scanned.Series.Select(s => (s.SeriesInstanceUid, s.FilePaths.FirstOrDefault()));
        }
        else if (_foundStudies.TryGetValue(studyUid, out var found))
        {
            seriesFiles = found.Series.Select(s =>
            {
                var dir = Path.Combine(storageRoot, studyUid, s.SeriesInstanceUid);
                var first = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.dcm").FirstOrDefault() : null;
                return (s.SeriesInstanceUid, first);
            });
        }
        else
        {
            return new { ok = false, error = "Tetkik bulunamadı." };
        }

        var thumbnails = new Dictionary<string, string>();
        foreach (var (seriesUid, firstFile) in seriesFiles)
        {
            if (firstFile is null) continue;
            var dataUri = DicomThumbnailService.RenderThumbnailDataUri(firstFile);
            if (dataUri is not null) thumbnails[seriesUid] = dataUri;
        }

        return new { ok = true, thumbnails };
    }

    private PacsNode? ReadNode(JsonElement payload)
    {
        var aeTitle = payload.TryGetProperty("aeTitle", out var ae) ? ae.GetString() : null;
        if (string.IsNullOrEmpty(aeTitle)) return null;

        return _settings.SendDestinations.FirstOrDefault(n => n.AeTitle == aeTitle)
               ?? _settings.QuerySources.FirstOrDefault(n => n.AeTitle == aeTitle);
    }
}
