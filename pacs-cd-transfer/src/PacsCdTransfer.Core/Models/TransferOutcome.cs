namespace PacsCdTransfer.Core.Models;

public sealed class TransferOutcome
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int SentCount { get; init; }
    public int FailedCount { get; init; }

    public static TransferOutcome Ok(int sent) => new() { Success = true, SentCount = sent };
    public static TransferOutcome Fail(string message, int sent = 0, int failed = 0) =>
        new() { Success = false, ErrorMessage = message, SentCount = sent, FailedCount = failed };
}

public sealed class ConnectionLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public required bool Success { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
}
