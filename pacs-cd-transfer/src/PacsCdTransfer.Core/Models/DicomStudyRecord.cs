namespace PacsCdTransfer.Core.Models;

/// <summary>
/// A study-level record as returned by a C-FIND query or read from a CD.
/// FileNumber/AccessionNumber are catalog references only — the mockup's rule
/// "Dosya No ve Accession No PACS'e gönderilmez" is enforced at the network layer,
/// not by omitting them here.
/// </summary>
public sealed class DicomStudyRecord
{
    public required string PatientName { get; set; }
    public required string PatientId { get; set; }
    public string? FileNumber { get; set; }
    public string? AccessionNumber { get; set; }
    public string? TcKimlikNo { get; set; }
    public required string StudyInstanceUid { get; set; }
    public DateTime? StudyDate { get; set; }
    public string? Modality { get; set; }
    public string? InstitutionName { get; set; }
    public int SeriesCount { get; set; }
    public List<DicomSeriesRecord> Series { get; set; } = new();
}

public sealed class DicomSeriesRecord
{
    public required string SeriesInstanceUid { get; set; }
    public string? SeriesDescription { get; set; }
    public string? Modality { get; set; }
    public int InstanceCount { get; set; }
    public List<string> FilePaths { get; set; } = new();
}
