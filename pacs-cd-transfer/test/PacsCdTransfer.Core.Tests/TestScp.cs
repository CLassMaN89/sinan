using System.Collections.Concurrent;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.Logging;

namespace PacsCdTransfer.Core.Tests;

/// <summary>
/// Minimal real DICOM SCP used only by integration tests: answers C-ECHO, accepts C-STORE,
/// and answers C-FIND against an in-memory list of seeded datasets. This exercises the
/// genuine wire protocol (TCP + PDU negotiation) between fo-dicom's SCU and SCP sides —
/// the same code path a real hospital PACS would drive.
/// </summary>
public sealed class TestScp : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCStoreProvider, IDicomCFindProvider, IDicomCMoveProvider
{
    public static readonly ConcurrentBag<string> ReceivedFilePaths = new();
    public static readonly List<DicomDataset> SeededStudies = new();
    public static string StorageRoot { get; set; } = Path.Combine(Path.GetTempPath(), "PacsCdTransfer.Tests", "scp-storage");

    /// <summary>Files this SCP will push out when it receives a C-MOVE for their study, keyed by StudyInstanceUID.</summary>
    public static readonly Dictionary<string, List<string>> MoveableFilesByStudyUid = new();

    /// <summary>Where a named destination AE actually lives, so C-MOVE can open a real sub-association to it.</summary>
    public static readonly Dictionary<string, (string Host, int Port)> KnownDestinations = new();

    public TestScp(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, log, dependencies)
    {
    }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
            pc.AcceptTransferSyntaxes(pc.GetTransferSyntaxes().ToArray());
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }

    public void OnConnectionClosed(Exception? exception) { }

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request) =>
        Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        Directory.CreateDirectory(StorageRoot);
        var path = Path.Combine(StorageRoot, $"{request.SOPInstanceUID.UID}.dcm");
        await request.File.SaveAsync(path);
        ReceivedFilePaths.Add(path);
        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e) => Task.CompletedTask;

    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        var wantedPatientName = request.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty);

        foreach (var candidate in SeededStudies)
        {
            var candidateName = candidate.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty);
            if (!string.IsNullOrEmpty(wantedPatientName) && candidateName != wantedPatientName)
                continue;

            yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = candidate };
            await Task.Yield();
        }

        yield return new DicomCFindResponse(request, DicomStatus.Success);
    }

    public async IAsyncEnumerable<DicomCMoveResponse> OnCMoveRequestAsync(DicomCMoveRequest request)
    {
        var studyUid = request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);
        var destinationAe = request.DestinationAE;

        if (!MoveableFilesByStudyUid.TryGetValue(studyUid, out var files) ||
            !KnownDestinations.TryGetValue(destinationAe, out var endpoint))
        {
            yield return new DicomCMoveResponse(request, DicomStatus.QueryRetrieveUnableToProcess);
            yield break;
        }

        var client = DicomClientFactory.Create(endpoint.Host, endpoint.Port, false, "TEST_SCP", destinationAe);
        var sent = 0;
        foreach (var path in files)
        {
            var storeRequest = new DicomCStoreRequest(path)
            {
                OnResponseReceived = (_, response) => { if (response.Status == DicomStatus.Success) sent++; }
            };
            await client.AddRequestAsync(storeRequest);
        }
        await client.SendAsync();

        yield return new DicomCMoveResponse(request, DicomStatus.Pending) { Remaining = 0, Completed = sent };
        yield return new DicomCMoveResponse(request, DicomStatus.Success) { Completed = sent };
    }
}
