using System.Globalization;
using System.IO;

namespace ToutieTrader.UI.Services;

/// <summary>
/// Log chaque bougie exactement telle qu'elle est envoyée au chart WebView2.
/// Fichier : logs/chart_candles.log
/// Colonnes : TF | QC-Time | chartUnix | O | H | L | C | Dir
/// Remis à zéro à chaque démarrage du replay (BtnStart_Click).
/// Thread-safe.
/// </summary>
public static class ChartCandleLogger
{
    private static readonly string _logPath;
    private static readonly object _lock = new();
    private static bool _headerWritten;

    static ChartCandleLogger()
    {
        string logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ToutieTrading", "logs");
        Directory.CreateDirectory(logsDir);
        _logPath = Path.Combine(logsDir, "chart_candles.log");
    }

    /// <summary>Efface le log et écrit l'en-tête (appeler au début de chaque replay).</summary>
    public static void Reset(string symbol, string timeframe, string fromDate, string toDate)
    {
        lock (_lock)
        {
            File.WriteAllText(_logPath,
                $"=== Chart Candles Log : {symbol} {timeframe} | {fromDate} → {toDate} ===\n" +
                $"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Columns   : TF | QC-Time (wall-clock) | chartUnix (fake-UTC) | O | H | L | C | Dir\n" +
                new string('-', 110) + "\n");
            _headerWritten = true;
        }
    }

    /// <summary>Logue une bougie envoyée au chart.</summary>
    public static void Log(
        string tf, string qcTime, long chartUnix,
        double open, double high, double low, double close,
        string dir)
    {
        lock (_lock)
        {
            if (!_headerWritten) return;   // Reset() non appelé = log désactivé
            try
            {
                var ic = CultureInfo.InvariantCulture;
                File.AppendAllText(_logPath,
                    $"{tf,4} | {qcTime,19} | {chartUnix,12} | " +
                    $"{open.ToString("F5", ic),10} | {high.ToString("F5", ic),10} | " +
                    $"{low.ToString("F5", ic),10} | {close.ToString("F5", ic),10} | {dir}\n");
            }
            catch { /* Ne jamais crasher l'UI pour un log */ }
        }
    }

    public static string LogPath => _logPath;
}
