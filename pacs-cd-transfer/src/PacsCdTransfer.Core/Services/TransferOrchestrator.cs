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
            var allFiles = study.Series.SelectMany(s => s.FilePaths).ToList();
            foreach (var batch in Chunk(allFiles, _maxBatchSeries))
            {
                ct.ThrowIfCancellationRequested();
                outcomes.Add(await _network.SendFilesAsync(destination, batch, ct));
            }
        }
        return outcomes;
    }

    private static IEnumerable<List<string>> Chunk(List<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
