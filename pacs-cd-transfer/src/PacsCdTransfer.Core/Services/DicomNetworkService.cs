using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using PacsCdTransfer.Core.Models;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// C-ECHO / C-FIND / C-MOVE / C-GET / C-STORE against a configured <see cref="PacsNode"/>.
/// One instance per call keeps this stateless and safe to use from the UI thread's async handlers.
/// </summary>
public sealed class DicomNetworkService
{
    private readonly string _callingAe;

    public DicomNetworkService(string callingAeTitle)
    {
        _callingAe = callingAeTitle;
    }

    public async Task<bool> EchoAsync(PacsNode node, CancellationToken ct = default)
    {
        var client = DicomClientFactory.Create(node.Host, node.Port, false, _callingAe, node.AeTitle);
        var ok = false;
        var request = new DicomCEchoRequest
        {
            OnResponseReceived = (_, response) => ok = response.Status == DicomStatus.Success
        };
        await client.AddRequestAsync(request);
        await client.SendAsync(ct);
        return ok;
    }

    /// <summary>Study-level C-FIND. Any of the query fields may be null/empty to mean "don't care".</summary>
    public async Task<List<DicomStudyRecord>> FindStudiesAsync(
        PacsNode node,
        string? patientName = null,
        string? patientId = null,
        string? accessionNumber = null,
        string? studyDateRange = null,
        string[]? modalities = null,
        CancellationToken ct = default)
    {
        var results = new List<DicomStudyRecord>();
        var client = DicomClientFactory.Create(node.Host, node.Port, false, _callingAe, node.AeTitle);

        var request = DicomCFindRequest.CreateStudyQuery(
            patientId: patientId,
            patientName: patientName,
            accession: accessionNumber,
            modalitiesInStudy: modalities is { Length: > 0 } ? string.Join("\\", modalities) : null);

        // DICOM date-range query syntax: "YYYYMMDD-YYYYMMDD" (either side may be omitted for an open range).
        if (!string.IsNullOrWhiteSpace(studyDateRange))
            request.Dataset.AddOrUpdate(DicomTag.StudyDate, studyDateRange);

        request.OnResponseReceived = (_, response) =>
        {
            if (response.Status != DicomStatus.Pending || response.Dataset is null) return;

            var ds = response.Dataset;
            results.Add(new DicomStudyRecord
            {
                PatientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty),
                PatientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty),
                AccessionNumber = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty),
                StudyInstanceUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty),
                StudyDate = ds.TryGetSingleValue<DateTime>(DicomTag.StudyDate, out var date) ? date : null,
                Modality = ds.GetSingleValueOrDefault(DicomTag.ModalitiesInStudy, string.Empty),
                InstitutionName = ds.GetSingleValueOrDefault(DicomTag.InstitutionName, string.Empty),
                SeriesCount = ds.TryGetSingleValue<int>(DicomTag.NumberOfStudyRelatedSeries, out var n) ? n : 0
            });
        };

        await client.AddRequestAsync(request);
        await client.SendAsync(ct);
        return results;
    }

    /// <summary>Retrieve a study via C-MOVE to <paramref name="destinationAe"/> — normally our own local SCP.</summary>
    public async Task<TransferOutcome> MoveStudyAsync(PacsNode node, string studyInstanceUid, string destinationAe, CancellationToken ct = default)
    {
        var client = DicomClientFactory.Create(node.Host, node.Port, false, _callingAe, node.AeTitle);
        var completed = 0;
        var failed = 0;
        string? error = null;

        var request = new DicomCMoveRequest(destinationAe, studyInstanceUid)
        {
            OnResponseReceived = (_, response) =>
            {
                completed = response.Completed;
                failed = response.Failures;
                if (response.Status.State == DicomState.Failure)
                    error = response.Status.Description;
            }
        };

        await client.AddRequestAsync(request);
        await client.SendAsync(ct);

        return error is null
            ? TransferOutcome.Ok(completed)
            : TransferOutcome.Fail(error, completed, failed);
    }

    /// <summary>Retrieve a study via C-GET — images stream back on the same association, no separate SCP needed.</summary>
    public async Task<TransferOutcome> GetStudyAsync(PacsNode node, string studyInstanceUid, string storageRoot, CancellationToken ct = default)
    {
        var client = DicomClientFactory.Create(node.Host, node.Port, false, _callingAe, node.AeTitle);
        client.OnCStoreRequest = async request =>
        {
            Directory.CreateDirectory(storageRoot);
            var studyUid = request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, studyInstanceUid);
            var seriesUid = request.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "unknown_series");
            var dir = Path.Combine(storageRoot, studyUid, seriesUid);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{request.SOPInstanceUID.UID}.dcm");
            await request.File.SaveAsync(path);
            return new DicomCStoreResponse(request, DicomStatus.Success);
        };

        var completed = 0;
        var failed = 0;
        string? error = null;

        var request = new DicomCGetRequest(studyInstanceUid)
        {
            OnResponseReceived = (_, response) =>
            {
                completed = response.Completed;
                failed = response.Failures;
                if (response.Status.State == DicomState.Failure)
                    error = response.Status.Description;
            }
        };
        client.AdditionalPresentationContexts.AddRange(
            DicomPresentationContext.GetScpRolePresentationContextsFromStorageUids(
                DicomStorageCategory.Image,
                DicomTransferSyntax.ImplicitVRLittleEndian, DicomTransferSyntax.ExplicitVRLittleEndian));

        await client.AddRequestAsync(request);
        await client.SendAsync(ct);

        return error is null
            ? TransferOutcome.Ok(completed)
            : TransferOutcome.Fail(error, completed, failed);
    }

    /// <summary>Send local DICOM files (from a CD or a prior C-MOVE/C-GET) to a destination via C-STORE.</summary>
    public async Task<TransferOutcome> SendFilesAsync(PacsNode node, IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var client = DicomClientFactory.Create(node.Host, node.Port, false, _callingAe, node.AeTitle);
        var sent = 0;
        var failed = 0;
        string? lastError = null;

        foreach (var path in filePaths)
        {
            var request = new DicomCStoreRequest(path)
            {
                OnResponseReceived = (_, response) =>
                {
                    if (response.Status == DicomStatus.Success) sent++;
                    else { failed++; lastError = response.Status.Description; }
                }
            };
            await client.AddRequestAsync(request);
        }

        await client.SendAsync(ct);

        return failed == 0
            ? TransferOutcome.Ok(sent)
            : TransferOutcome.Fail(lastError ?? "Bir veya daha fazla görüntü gönderilemedi.", sent, failed);
    }
}
