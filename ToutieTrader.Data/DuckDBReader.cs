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
    /// Vérifie rapidement si la DB couvre déjà la range demandée pour CE symbol
    /// sur TOUS les timeframes fournis. Utilisé pour skip ensure_candles_range
    /// quand les données sont déjà en cache local.
    ///
    /// Retourne true seulement si pour CHAQUE tf : MIN(time) <= from ET MAX(time) >= to.
    /// Retourne false dès qu'un TF manque ou ne couvre pas la range.
    /// </summary>
    public bool HasFullCoverage(
        string symbol, IEnumerable<string> timeframes,
        DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            using var con = OpenRead();
            foreach (var tf in timeframes)
            {
                using var cmd = con.CreateCommand();
                // On compte les candles qui tombent DANS la range demandée.
                // Pas de comparaison stricte sur MIN/MAX — les TFs hauts (H4/D) ne
                // s'alignent jamais pile aux bornes choisies par l'utilisateur, donc
                // exiger dbMin <= from causerait toujours un faux miss de cache.
                cmd.CommandText = """
                    SELECT COUNT(*)
                    FROM candles
                    WHERE symbol = $symbol
                      AND timeframe = $tf
                      AND time >= $from
                      AND time <= $to
                    """;
                cmd.Parameters.Add(new DuckDBParameter("symbol", symbol));
                cmd.Parameters.Add(new DuckDBParameter("tf",     tf));
                cmd.Parameters.Add(new DuckDBParameter("from",   from.UtcDateTime));
                cmd.Parameters.Add(new DuckDBParameter("to",     to.UtcDateTime));

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return false;
                long count = r.GetInt64(0);
                // Au moins 1 candle dans la range → on considère que ce TF est cached.
                // (Si l'utilisateur veut vraiment un fetch complet il peut toujours le
                // forcer manuellement via le bridge.)
                if (count == 0) return false;
            }
            return true;
        }
        catch
        {
            // DB locked / pas accessible → on retourne false, le caller fera fallback.
            return false;
        }
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

    /// <summary>
    /// Charge tous les TFs d'un symbole en une seule connexion DuckDB.
    /// queries = liste de (timeframe, from, to) — from inclut déjà le warmup indicateurs.
    /// </summary>
    public Dictionary<string, List<Candle>> GetCandlesForSymbol(
        string symbol,
        IEnumerable<(string Tf, DateTimeOffset From, DateTimeOffset To)> queries)
    {
        using var con    = OpenRead();
        var       result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tf, from, to) in queries)
        {
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
            cmd.Parameters.Add(new DuckDBParameter("timeframe", tf));
            cmd.Parameters.Add(new DuckDBParameter("from",      from.UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("to",        to.UtcDateTime));

            var candles = ReadCandles(cmd, symbol, tf);
            if (candles.Count > 0) result[tf] = candles;
        }

        return result;
    }

    // ─── Ticks ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne les ticks bruts MT5 (bid/ask) entre deux dates pour un symbole.
    /// Utilisé par le Replay en "Mode Tick" pour détection précise SL/TP intra-bougie.
    /// Bornes incluses. Timestamps retournées en heure Québec offset-aware.
    /// Retourne liste vide si la table `ticks` n'existe pas encore.
    /// </summary>
    public List<Tick> GetTicksInRange(
        string symbol, DateTimeOffset from, DateTimeOffset to)
    {
        using var con = OpenRead();

        // Si la table n'existe pas encore (jamais de fetch ticks), retourne vide
        using (var checkCmd = con.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'ticks'";
            var exists = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0L);
            if (exists == 0) return [];
        }

        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT time, bid, ask, last, volume, flags
            FROM ticks
            WHERE symbol = $symbol
              AND time >= $from
              AND time <= $to
            ORDER BY time
            """;

        cmd.Parameters.Add(new DuckDBParameter("symbol", symbol));
        cmd.Parameters.Add(new DuckDBParameter("from",   from.UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to",     to.UtcDateTime));

        using var reader = cmd.ExecuteReader();
        var ticks = new List<Tick>();

        while (reader.Read())
        {
            var utc  = reader.GetDateTime(0);
            var time = TimeZoneHelper.ToQuebec(utc);

            ticks.Add(new Tick
            {
                Symbol = symbol,
                Time   = time,
                Bid    = reader.GetDouble(1),
                Ask    = reader.GetDouble(2),
                Last   = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                Volume = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                Flags  = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
            });
        }

        return ticks;
    }

    // ─── Privé ────────────────────────────────────────────────────────────────

    private DuckDBConnection OpenRead()
    {
        // Retry jusqu'à 20× (5s entre chaque) si la DB est verrouillée par le bridge Python.
        // 20 × 5s = 100s de patience — suffisant même si ensure_candles_range prend ~3 minutes.
        // Note : le vrai fix est HttpClient.Timeout = Infinite dans MT5ApiClient, ce qui
        // garantit que Python a fermé sa connexion avant que LoadAllSymbolsAllTfsAsync démarre.
        // Ce retry reste comme filet de sécurité.
        const int MaxAttempts  = 20;
        const int RetryDelayMs = 5_000;

        for (int i = 0; i < MaxAttempts; i++)
        {
            try
            {
                var con = new DuckDBConnection(_connectionString);
                con.Open();
                return con;
            }
            catch (Exception ex) when (i < MaxAttempts - 1 &&
                (ex.Message.Contains("used by another process") ||
                 ex.Message.Contains("Cannot open file") ||
                 ex.Message.Contains("lock")))
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
        // Dernière tentative — laisse l'exception remonter normalement
        var last = new DuckDBConnection(_connectionString);
        last.Open();
        return last;
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
