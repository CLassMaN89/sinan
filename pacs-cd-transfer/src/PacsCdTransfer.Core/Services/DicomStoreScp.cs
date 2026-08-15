using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// Local C-STORE receiver. Required to actually complete a C-MOVE (the remote PACS
/// opens a new association back to us and pushes the images here) and used as the
/// storage target for C-GET as well. Files land under <see cref="StorageRoot"/>.
/// </summary>
public sealed class DicomStoreScp : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCStoreProvider
{
    public static string StorageRoot { get; set; } = Path.Combine(Path.GetTempPath(), "PacsCdTransfer", "Incoming");
    public static string LocalAeTitle { get; set; } = "SK_CD_IMPORT";

    public event Action<string>? FileReceived;

    public DicomStoreScp(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, log, dependencies)
    {
    }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
        {
            pc.AcceptTransferSyntaxes(pc.GetTransferSyntaxes().ToArray());
        }
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }

    public void OnConnectionClosed(Exception? exception) { }

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        Directory.CreateDirectory(StorageRoot);
        var studyUid = request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "unknown_study");
        var seriesUid = request.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "unknown_series");
        var sopUid = request.SOPInstanceUID.UID;

        var dir = Path.Combine(StorageRoot, studyUid, seriesUid);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{sopUid}.dcm");
        await request.File.SaveAsync(path);
        FileReceived?.Invoke(path);

        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e) => Task.CompletedTask;

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request) =>
        Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
}
