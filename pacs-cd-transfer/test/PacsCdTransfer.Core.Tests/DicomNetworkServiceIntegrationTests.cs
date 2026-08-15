using FellowOakDicom;
using FellowOakDicom.Network;
using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Services;
using Xunit;

namespace PacsCdTransfer.Core.Tests;

/// <summary>
/// Real network round-trips over localhost TCP against <see cref="TestScp"/> — not mocks.
/// Validates that DicomNetworkService actually speaks correct DICOM upper-layer protocol.
/// </summary>
public sealed class DicomNetworkServiceIntegrationTests : IDisposable
{
    private readonly IDicomServer _server;
    private readonly int _port;
    private readonly PacsNode _node;
    private readonly DicomNetworkService _sut;

    public DicomNetworkServiceIntegrationTests()
    {
        _port = GetFreeTcpPort();
        TestScp.StorageRoot = Path.Combine(Path.GetTempPath(), "PacsCdTransfer.Tests", Guid.NewGuid().ToString());
        TestScp.SeededStudies.Clear();
        _server = DicomServerFactory.Create<TestScp>(_port);
        _node = new PacsNode { AeTitle = "TEST_SCP", Host = "127.0.0.1", Port = _port };
        _sut = new DicomNetworkService("TEST_SCU");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Echo_Succeeds_Against_Real_Scp()
    {
        var ok = await _sut.EchoAsync(_node);
        Assert.True(ok);
    }

    [Fact]
    public async Task SendFiles_ActuallyDeliversTheFile_ToTheScp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "send-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var dataset = new DicomDataset
            {
                { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
                { DicomTag.SOPInstanceUID, DicomUID.Generate().UID },
                { DicomTag.StudyInstanceUID, DicomUID.Generate().UID },
                { DicomTag.SeriesInstanceUID, DicomUID.Generate().UID },
                { DicomTag.PatientName, "GONDERIM^TEST" },
                { DicomTag.Modality, "CT" }
            };
            var path = Path.Combine(dir, "test.dcm");
            new DicomFile(dataset).Save(path);

            var outcome = await _sut.SendFilesAsync(_node, new[] { path });

            Assert.True(outcome.Success);
            Assert.Equal(1, outcome.SentCount);
            Assert.Contains(TestScp.ReceivedFilePaths, p => File.Exists(p));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task FindStudies_ReturnsSeededRecord_MatchingPatientName()
    {
        var studyUid = DicomUID.Generate().UID;
        TestScp.SeededStudies.Add(new DicomDataset
        {
            { DicomTag.PatientName, "SORGU^TEST" },
            { DicomTag.PatientID, "999" },
            { DicomTag.StudyInstanceUID, studyUid },
            { DicomTag.ModalitiesInStudy, "CT" }
        });

        var results = await _sut.FindStudiesAsync(_node, patientName: "SORGU^TEST");

        var record = Assert.Single(results);
        Assert.Equal(studyUid, record.StudyInstanceUid);
        Assert.Equal("999", record.PatientId);
    }

    [Fact]
    public async Task FindStudies_ReturnsNothing_ForNonMatchingPatientName()
    {
        TestScp.SeededStudies.Add(new DicomDataset
        {
            { DicomTag.PatientName, "BASKA^HASTA" },
            { DicomTag.PatientID, "111" },
            { DicomTag.StudyInstanceUID, DicomUID.Generate().UID }
        });

        var results = await _sut.FindStudiesAsync(_node, patientName: "OLMAYAN^HASTA");

        Assert.Empty(results);
    }

    [Fact]
    public async Task MoveStudy_DeliversFile_ToOurOwnRealStoreScp()
    {
        // The remote PACS (TestScp) opens a *new* association back to our own production
        // DicomStoreScp — this is the exact real-world C-MOVE flow, not a stub.
        var receiverPort = GetFreeTcpPort();
        var receiverRoot = Path.Combine(Path.GetTempPath(), "move-receiver-" + Guid.NewGuid());
        using var receiverHost = new DicomServerHost(receiverPort, "SK_CD_IMPORT", receiverRoot);
        receiverHost.Start();
        TestScp.KnownDestinations["SK_CD_IMPORT"] = ("127.0.0.1", receiverPort);

        var studyUid = DicomUID.Generate().UID;
        var sourceDir = Path.Combine(Path.GetTempPath(), "move-source-" + Guid.NewGuid());
        Directory.CreateDirectory(sourceDir);
        try
        {
            var dataset = new DicomDataset
            {
                { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
                { DicomTag.SOPInstanceUID, DicomUID.Generate().UID },
                { DicomTag.StudyInstanceUID, studyUid },
                { DicomTag.SeriesInstanceUID, DicomUID.Generate().UID },
                { DicomTag.PatientName, "MOVE^TEST" },
                { DicomTag.Modality, "CT" }
            };
            var path = Path.Combine(sourceDir, "move-test.dcm");
            new DicomFile(dataset).Save(path);
            TestScp.MoveableFilesByStudyUid[studyUid] = new List<string> { path };

            var outcome = await _sut.MoveStudyAsync(_node, studyUid, "SK_CD_IMPORT");

            Assert.True(outcome.Success);
            Assert.Equal(1, outcome.SentCount);

            var received = Directory.EnumerateFiles(receiverRoot, "*.dcm", SearchOption.AllDirectories).ToList();
            Assert.Single(received);
        }
        finally
        {
            Directory.Delete(sourceDir, true);
            if (Directory.Exists(receiverRoot)) Directory.Delete(receiverRoot, true);
        }
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(TestScp.StorageRoot))
            Directory.Delete(TestScp.StorageRoot, true);
    }
}
