namespace PacsCdTransfer.Core.Models;

/// <summary>
/// Everything configured in the mockup's Ayarlar modal — persisted next to the exe so the
/// portable build carries its own config (no per-machine install state, no registry).
/// </summary>
public sealed class AppSettings
{
    public string LocalAeTitle { get; set; } = "SK_CD_IMPORT";
    public int LocalPort { get; set; } = 11112;
    public string TempStorageFolder { get; set; } = "PacsCD_Temp";
    public bool AutoCleanTempFiles { get; set; } = true;
    public int MaxBatchSeries { get; set; } = 10;

    public List<PacsNode> SendDestinations { get; set; } = new();
    public List<PacsNode> QuerySources { get; set; } = new();
    public List<UserAccount> Users { get; set; } = new();
}
