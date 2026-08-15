using PacsCdTransfer.Core.Models;
using PacsCdTransfer.Core.Validation;

namespace PacsCdTransfer.Core.Services;

public sealed class TcValidationException : Exception
{
    public IReadOnlyList<string> InvalidPatientNames { get; }
    public TcValidationException(IReadOnlyList<string> invalidPatientNames)
        : base("Bir veya daha fazla hastanın TC Kimlik No alanı geçersiz.")
    {
        InvalidPatientNames = invalidPatientNames;
    }
}

/// <summary>
/// Sends one or more studies to a PACS destination, mirroring the mockup's rules:
/// TC Kimlik No must be valid before anything is sent (whole batch is blocked, not
/// partially sent), and series are grouped so no more than MaxBatchSeries go out
/// in a single C-STORE burst.
/// </summary>
public sealed class TransferOrchestrator
{
    private readonly DicomNetworkService _network;
    private readonly int _maxBatchSeries;

    public TransferOrchestrator(DicomNetworkService network, int maxBatchSeries)
    {
        _network = network;
        _maxBatchSeries = Math.Max(1, maxBatchSeries);
    }

    public async Task<IReadOnlyList<TransferOutcome>> SendStudiesAsync(
        PacsNode destination,
        IReadOnlyList<DicomStudyRecord> studies,
        CancellationToken ct = default)
    {
        var invalid = studies
            .Where(s => !TcKimlikValidator.HasValidFormat(s.TcKimlikNo))
            .Select(s => s.PatientName)
            .ToList();
        if (invalid.Count > 0)
            throw new TcValidationException(invalid);

        var outcomes = new List<TransferOutcome>();
        foreach (var study in studies)
        {
            // Batch by SERIES (as the "Aynı Anda Gönderilecek Maksimum Seri" setting name
            // promises), not by individual image count — chunking by raw file count would cut
            // a single series across multiple C-STORE bursts and mislabel the setting's unit.
            foreach (var seriesBatch in Chunk(study.Series, _maxBatchSeries))
            {
                ct.ThrowIfCancellationRequested();
                var batchFiles = seriesBatch.SelectMany(s => s.FilePaths).ToList();
                if (batchFiles.Count == 0) continue;
                outcomes.Add(await _network.SendFilesAsync(destination, batchFiles, ct));
            }
        }
        return outcomes;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
