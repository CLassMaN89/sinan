using FellowOakDicom.Network;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// Owns the lifetime of the local C-STORE SCP that C-MOVE and C-GET results land on.
/// The app starts one of these at launch, on the port configured in Dicom Bilgileri.
/// </summary>
public sealed class DicomServerHost : IDisposable
{
    private IDicomServer? _server;

    public int Port { get; }

    public DicomServerHost(int port, string aeTitle, string storageRoot)
    {
        Port = port;
        DicomStoreScp.LocalAeTitle = aeTitle;
        DicomStoreScp.StorageRoot = storageRoot;
    }

    public void Start()
    {
        if (_server is not null) return;
        _server = DicomServerFactory.Create<DicomStoreScp>(Port);
    }

    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }
}
