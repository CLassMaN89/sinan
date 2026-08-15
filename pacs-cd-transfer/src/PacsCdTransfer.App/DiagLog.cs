namespace PacsCdTransfer.App;

/// <summary>
/// Temporary forensic logging for the "login does nothing, no error" report — writes a
/// plain-text trail next to the exe so we can see exactly which step the app reaches
/// without needing a debugger attached on the user's machine.
/// </summary>
internal static class DiagLog
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "startup-log.txt");

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        }
        catch
        {
            // Logging must never be the thing that crashes the app.
        }
    }
}
