using FellowOakDicom;
using PacsCdTransfer.Core.Models;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// Detects inserted optical discs and reads the DICOM studies on them — via DICOMDIR when
/// present (most hospital CD burners write one), falling back to a recursive file scan
/// for discs that don't.
/// </summary>
public sealed class CdImportService
{
    /// <summary>Root paths of every optical drive that currently has a disc in it.</summary>
    public static IEnumerable<string> GetReadyOpticalDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.CDRom && d.IsReady)
            .Select(d => d.RootDirectory.FullName);

    public async Task<List<DicomStudyRecord>> ReadDiscAsync(string driveRoot, CancellationToken ct = default)
    {
        var dicomDirPath = Path.Combine(driveRoot, "DICOMDIR");
        if (File.Exists(dicomDirPath))
        {
            var fromIndex = await TryReadDicomDirAsync(dicomDirPath, ct);
            if (fromIndex.Count > 0) return fromIndex;
        }

        return await ScanFilesAsync(driveRoot, ct);
    }

    private static async Task<List<DicomStudyRecord>> TryReadDicomDirAsync(string dicomDirPath, CancellationToken ct)
    {
        var studies = new Dictionary<string, DicomStudyRecord>();
        try
        {
            var dicomDir = await DicomFile.OpenAsync(dicomDirPath);
            var root = Path.GetDirectoryName(dicomDirPath)!;

            foreach (var item in dicomDir.Dataset.GetSequence(DicomTag.DirectoryRecordSequence))
            {
                var recordType = item.GetSingleValueOrDefault(DicomTag.DirectoryRecordType, string.Empty);
                if (recordType != "IMAGE") continue;

                var refPath = item.GetValues<string>(DicomTag.ReferencedFileID);
                if (refPath.Length == 0) continue;

                var fullPath = Path.Combine(new[] { root }.Concat(refPath).ToArray());
                if (!File.Exists(fullPath)) continue;

                ct.ThrowIfCancellationRequested();
                AddFileToStudyMap(studies, fullPath);
            }
        }
        catch
        {
            return new List<DicomStudyRecord>();
        }

        return studies.Values.ToList();
    }

    private static async Task<List<DicomStudyRecord>> ScanFilesAsync(string driveRoot, CancellationToken ct)
    {
        var studies = new Dictionary<string, DicomStudyRecord>();
        foreach (var path in Directory.EnumerateFiles(driveRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (!LooksLikeDicom(path)) continue;
            AddFileToStudyMap(studies, path);
        }
        return await Task.FromResult(studies.Values.ToList());
    }

    private static bool LooksLikeDicom(string path)
    {
        try
        {
            return DicomFile.HasValidHeader(path);
        }
        catch
        {
            return false;
        }
    }

    private static void AddFileToStudyMap(Dictionary<string, DicomStudyRecord> studies, string filePath)
    {
        DicomFile file;
        try
        {
            file = DicomFile.Open(filePath);
        }
        catch
        {
            return;
        }

        var ds = file.Dataset;
        var studyUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);
        if (string.IsNullOrEmpty(studyUid)) return;

        if (!studies.TryGetValue(studyUid, out var study))
        {
            var patientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty);
            var otherPatientId = ds.GetSingleValueOrDefault(DicomTag.OtherPatientIDsRETIRED, string.Empty);

            // Many Turkish hospital systems put the TC Kimlik No directly in PatientID (or
            // OtherPatientIDs) — if it's actually a valid TC number, pre-fill it so staff
            // don't have to retype it; otherwise leave it blank for manual entry, since CDs
            // often carry a hospital file number here instead (which is not a TC Kimlik No).
            string? tcKimlikNo = null;
            if (Validation.TcKimlikValidator.IsValid(patientId)) tcKimlikNo = patientId;
            else if (Validation.TcKimlikValidator.IsValid(otherPatientId)) tcKimlikNo = otherPatientId;

            study = new DicomStudyRecord
            {
                PatientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, "Bilinmiyor"),
                PatientId = patientId,
                AccessionNumber = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty),
                TcKimlikNo = tcKimlikNo,
                StudyInstanceUid = studyUid,
                StudyDate = ds.TryGetSingleValue<DateTime>(DicomTag.StudyDate, out var date) ? date : null,
                Modality = ds.GetSingleValueOrDefault(DicomTag.Modality, string.Empty),
                InstitutionName = ds.GetSingleValueOrDefault(DicomTag.InstitutionName, string.Empty)
            };
            studies[studyUid] = study;
        }

        var seriesUid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty);
        var series = study.Series.FirstOrDefault(s => s.SeriesInstanceUid == seriesUid);
        if (series is null)
        {
            series = new DicomSeriesRecord
            {
                SeriesInstanceUid = seriesUid,
                SeriesDescription = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, string.Empty),
                Modality = ds.GetSingleValueOrDefault(DicomTag.Modality, string.Empty)
            };
            study.Series.Add(series);
            study.SeriesCount = study.Series.Count;
        }

        series.FilePaths.Add(filePath);
        series.InstanceCount = series.FilePaths.Count;
    }
}
