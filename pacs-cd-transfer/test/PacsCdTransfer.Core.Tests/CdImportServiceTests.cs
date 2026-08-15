using FellowOakDicom;
using PacsCdTransfer.Core.Services;
using Xunit;

namespace PacsCdTransfer.Core.Tests;

public class CdImportServiceTests
{
    private static string WriteTestFile(string dir, string studyUid, string seriesUid, string sopUid, string patientName, string modality)
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
            { DicomTag.SOPInstanceUID, sopUid },
            { DicomTag.StudyInstanceUID, studyUid },
            { DicomTag.SeriesInstanceUID, seriesUid },
            { DicomTag.PatientName, patientName },
            { DicomTag.PatientID, "123456" },
            { DicomTag.Modality, modality },
            { DicomTag.SeriesDescription, "Test Series" },
            { DicomTag.InstitutionName, "Test Hospital" }
        };
        var file = new DicomFile(dataset);
        var path = Path.Combine(dir, $"{sopUid}.dcm");
        file.Save(path);
        return path;
    }

    [Fact]
    public async Task ScanFilesAsync_GroupsInstancesByStudyAndSeries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-import-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var studyUid = DicomUID.Generate().UID;
            var series1 = DicomUID.Generate().UID;
            var series2 = DicomUID.Generate().UID;

            WriteTestFile(dir, studyUid, series1, DicomUID.Generate().UID, "TEST^PATIENT", "CT");
            WriteTestFile(dir, studyUid, series1, DicomUID.Generate().UID, "TEST^PATIENT", "CT");
            WriteTestFile(dir, studyUid, series2, DicomUID.Generate().UID, "TEST^PATIENT", "CT");

            var service = new CdImportService();
            var studies = await service.ReadDiscAsync(dir);

            var study = Assert.Single(studies);
            Assert.Equal("TEST^PATIENT", study.PatientName);
            Assert.Equal(2, study.SeriesCount);
            Assert.Equal(3, study.Series.Sum(s => s.InstanceCount));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ScanFilesAsync_IgnoresNonDicomFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-import-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "not dicom");
            WriteTestFile(dir, DicomUID.Generate().UID, DicomUID.Generate().UID, DicomUID.Generate().UID, "A^B", "MR");

            var service = new CdImportService();
            var studies = await service.ReadDiscAsync(dir);

            Assert.Single(studies);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
