using PacsCdTransfer.Core.Models;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// In-memory, session-scoped log of PACS connection successes/failures — surfaced in the
/// mockup's Bağlantı Günlüğü pane so staff can see *why* a send or query failed.
/// </summary>
public sealed class ConnectionLogService
{
    private readonly List<ConnectionLogEntry> _entries = new();
    public IReadOnlyList<ConnectionLogEntry> Entries => _entries;

    public event Action? Changed;

    public void Log(bool success, string title, string message)
    {
        _entries.Insert(0, new ConnectionLogEntry { Success = success, Title = title, Message = message });
        Changed?.Invoke();
    }

    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke();
    }
}
