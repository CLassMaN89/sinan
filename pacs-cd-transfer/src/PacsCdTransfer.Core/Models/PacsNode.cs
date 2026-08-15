namespace PacsCdTransfer.Core.Models;

public enum RetrieveMode
{
    CMove,
    CGet
}

/// <summary>
/// A configured remote PACS: either a send destination or a query/retrieve source.
/// The same node can be used for both roles depending on which list it's added to.
/// </summary>
public sealed class PacsNode
{
    public required string AeTitle { get; set; }
    public required string Host { get; set; }
    public required int Port { get; set; }
    public bool IsDefault { get; set; }
    public RetrieveMode RetrieveMode { get; set; } = RetrieveMode.CMove;

    public override string ToString() => $"{AeTitle} ({Host}:{Port})";
}
