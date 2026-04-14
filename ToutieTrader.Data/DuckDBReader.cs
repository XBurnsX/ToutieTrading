using DuckDB.NET.Data;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;

namespace ToutieTrader.Data;

/// <summary>
/// Accès en lecture seule à DuckDB #1 (candles historiques).
/// Utilisé exclusivement par le Replay — jamais tout charger en mémoire.
/// </summary>
public sealed class DuckDBReader
{
    private readonly string _connectionString;

    public DuckDBReader(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};";
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne les bougies entre deux dates (bornes incluses).
    /// Timestamps renvoyées en heure Québec offset-aware.
    /// </summary>
    public List<Candle> GetCandles(
        string symbol, string timeframe,
        DateTimeOffset from, DateTimeOffset to)
    {
        using var con = OpenRead();
        using var cmd = con.CreateCommand();

        cmd.CommandText = """
            SELECT time, open, high, low, close, volume
            FROM candles
            WHERE symbol = $symbol
              AND timeframe = $timeframe
              AND time >= $from
              AND time <= $to
            ORDER BY time
            """;

        cmd.Parameters.Add(new DuckDBParameter("symbol",    symbol));
        cmd.Parameters.Add(new DuckDBParameter("timeframe", timeframe));
        cmd.Parameters.Add(new DuckDBParameter("from",      from.UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to",        to.UtcDateTime));

        return ReadCandles(cmd, symbol, timeframe);
    }

    /// <summary>
    /// Retourne un chunk de N bougies à partir d'une date.
    /// Utilisé par le buffer adaptatif du Replay (x1=60 / x2=120 / x4=240 / x8=480).
    /// </summary>
    public List<Candle> GetCandlesChunk(
        string symbol, string timeframe,
        DateTimeOffset from, int count)
    {
        using var con = OpenRead();
        using var cmd = con.CreateCommand();

        cmd.CommandText = """
            SELECT time, open, high, low, close, volume
            FROM candles
            WHERE symbol = $symbol
              AND timeframe = $timeframe
              AND time >= $from
            ORDER BY time
            LIMIT $count
            """;

        cmd.Parameters.Add(new DuckDBParameter("symbol",    symbol));
        cmd.Parameters.Add(new DuckDBParameter("timeframe", timeframe));
        cmd.Parameters.Add(new DuckDBParameter("from",      from.UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("count",     count));

        return ReadCandles(cmd, symbol, timeframe);
    }

    /// <summary>
    /// Retourne la dernière bougie disponible pour un symbole/TF.
    /// Utile pour savoir où commence l'historique d'un symbole.
    /// </summary>
    public Candle? GetLatestCandle(string symbol, string timeframe)
    {
        using var con = OpenRead();
        using var cmd = con.CreateCommand();

        cmd.CommandText = """
            SELECT time, open, high, low, close, volume
            FROM candles
            WHERE symbol = $symbol AND timeframe = $timeframe
            ORDER BY time DESC
            LIMIT 1
            """;

        cmd.Parameters.Add(new DuckDBParameter("symbol",    symbol));
        cmd.Parameters.Add(new DuckDBParameter("timeframe", timeframe));

        return ReadCandles(cmd, symbol, timeframe).FirstOrDefault();
    }

    /// <summary>
    /// Retourne la première bougie disponible pour un symbole/TF.
    /// </summary>
    public Candle? GetEarliestCandle(string symbol, string timeframe)
    {
        using var con = OpenRead();
        using var cmd = con.CreateCommand();

        cmd.CommandText = """
            SELECT time, open, high, low, close, volume
            FROM candles
            WHERE symbol = $symbol AND timeframe = $timeframe
            ORDER BY time ASC
            LIMIT 1
            """;

        cmd.Parameters.Add(new DuckDBParameter("symbol",    symbol));
        cmd.Parameters.Add(new DuckDBParameter("timeframe", timeframe));

        return ReadCandles(cmd, symbol, timeframe).FirstOrDefault();
    }

    /// <summary>
    /// Liste tous les symboles disponibles dans DuckDB #1.
    /// Utilisé pour peupler le dropdown symbole dans la page Replay.
    /// </summary>
    public List<string> GetAvailableSymbols()
    {
        using var con = OpenRead();
        using var cmd = con.CreateCommand();

        cmd.CommandText = "SELECT DISTINCT symbol FROM candles ORDER BY symbol";

        using var reader = cmd.ExecuteReader();
        var symbols = new List<string>();

        while (reader.Read())
            symbols.Add(reader.GetString(0));

        return symbols;
    }

    /// <summary>
    /// Compte de bougies par symbole/TF — utile pour la validation.
    /// </summary>
    public Dictionary<(string Symbol, string Timeframe), int> GetCandleCounts()
    {
        using var con = OpenRead();
        using var cmd = con.CreateCommand();

        cmd.CommandText = """
            SELECT symbol, timeframe, COUNT(*) AS n
            FROM candles
            GROUP BY symbol, timeframe
            ORDER BY symbol, timeframe
            """;

        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<(string, string), int>();

        while (reader.Read())
            result[(reader.GetString(0), reader.GetString(1))] = reader.GetInt32(2);

        return result;
    }

    // ─── Privé ────────────────────────────────────────────────────────────────

    private DuckDBConnection OpenRead()
    {
        var con = new DuckDBConnection(_connectionString);
        con.Open();
        return con;
    }

    private static List<Candle> ReadCandles(DuckDBCommand cmd, string symbol, string timeframe)
    {
        using var reader = cmd.ExecuteReader();
        var candles = new List<Candle>();

        while (reader.Read())
        {
            // DuckDB retourne TIMESTAMPTZ comme DateTime UTC
            var utc = reader.GetDateTime(0);
            var time = TimeZoneHelper.ToQuebec(utc);

            candles.Add(new Candle
            {
                Symbol    = symbol,
                Timeframe = timeframe,
                Time      = time,
                Open      = reader.GetDouble(1),
                High      = reader.GetDouble(2),
                Low       = reader.GetDouble(3),
                Close     = reader.GetDouble(4),
                Volume    = reader.GetInt64(5),
            });
        }

        return candles;
    }
}
