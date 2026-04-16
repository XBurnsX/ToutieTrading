using System.IO;

namespace ToutieTrader.UI.Services;

/// <summary>
/// Logger file-based pour debug le flow de replay.
/// Écrit dans C:\Users\XBurnsX\ToutieTrading\logs\replay.log
/// Thread-safe, append-only. Reset au démarrage de l'app.
/// </summary>
public static class ReplayLogger
{
    private static readonly string _logPath;
    private static readonly object _lock = new();

    static ReplayLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ToutieTrading", "logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "replay.log");

        // Reset au démarrage — on écrase le log précédent
        try
        {
            File.WriteAllText(_logPath,
                $"=== ReplayLogger started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { /* si le fichier est locked, on continue silencieusement */ }
    }

    public static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
        lock (_lock)
        {
            try { File.AppendAllText(_logPath, line); }
            catch { /* ne jamais throw depuis un logger */ }
        }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"EXCEPTION in {context}: {ex.GetType().Name}: {ex.Message}");
        Log($"  StackTrace: {ex.StackTrace}");
        if (ex.InnerException != null)
            Log($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
    }

    public static string LogPath => _logPath;
}
