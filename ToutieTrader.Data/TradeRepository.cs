using DuckDB.NET.Data;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;

namespace ToutieTrader.Data;

/// <summary>
/// Accès lecture/écriture à DuckDB #2 (trades live) et DB Replay.
/// - isReplay=false → trades.db (persistant)
/// - isReplay=true  → replay_trades.db (wiped à chaque session)
/// </summary>
public sealed class TradeRepository
{
    private readonly string _liveCs;
    private readonly string _replayCs;

    // Mutex pour éviter les écritures concurrentes sur le même fichier DuckDB
    private readonly SemaphoreSlim _liveLock   = new(1, 1);
    private readonly SemaphoreSlim _replayLock = new(1, 1);

    public TradeRepository(string liveDbPath, string replayDbPath)
    {
        _liveCs   = $"Data Source={liveDbPath};";
        _replayCs = $"Data Source={replayDbPath};";

        // Migration : assure-toi que les colonnes ajoutées après création existent.
        // ALTER TABLE ADD COLUMN IF NOT EXISTS — DuckDB-supported.
        EnsureSchema(_liveCs);
        EnsureSchema(_replayCs);
    }

    private static void EnsureSchema(string cs)
    {
        try
        {
            using var con = Open(cs);
            using var cmd = con.CreateCommand();
            cmd.CommandText = "ALTER TABLE trades ADD COLUMN IF NOT EXISTS fees DOUBLE";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Si la table n'existe pas encore (DB non initialisée), on ignore — sera créée
            // par tools/create_trade_databases.py avec le schéma à jour.
        }
    }

    /// <summary>
    /// Vide tous les trades de la DB replay. Appelé au stop replay et à la fermeture
    /// de l'app — la DB replay est éphémère, on ne garde rien d'une session à l'autre.
    /// </summary>
    public async Task WipeReplayAsync()
    {
        await _replayLock.WaitAsync();
        try
        {
            using var con = Open(_replayCs);
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM trades";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Si la table n'existe pas, rien à wiper.
        }
        finally
        {
            _replayLock.Release();
        }
    }

    // ─── Écriture ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Insère un nouveau trade.
    /// Si le trade existe déjà (même Id), met à jour tous les champs.
    /// </summary>
    public async Task SaveTradeAsync(TradeRecord trade, bool isReplay)
    {
        var sem = isReplay ? _replayLock : _liveLock;
        var cs  = isReplay ? _replayCs   : _liveCs;

        await sem.WaitAsync();
        try
        {
            using var con = Open(cs);
            using var cmd = con.CreateCommand();

            cmd.CommandText = """
                INSERT INTO trades (
                    id, symbol, strategy_name, strategy_settings, direction,
                    entry_time, entry_price, sl, tp,
                    exit_time, exit_price, profit_loss, risk_dollars, lot_size,
                    ticket_id, correlation_id, exit_reason, conditions_met, error_log, fees
                ) VALUES (
                    $id, $symbol, $strategy_name, $strategy_settings, $direction,
                    $entry_time, $entry_price, $sl, $tp,
                    $exit_time, $exit_price, $profit_loss, $risk_dollars, $lot_size,
                    $ticket_id, $correlation_id, $exit_reason, $conditions_met, $error_log, $fees
                )
                ON CONFLICT (id) DO UPDATE SET
                    exit_time       = excluded.exit_time,
                    exit_price      = excluded.exit_price,
                    profit_loss     = excluded.profit_loss,
                    risk_dollars    = excluded.risk_dollars,
                    lot_size        = excluded.lot_size,
                    ticket_id       = excluded.ticket_id,
                    exit_reason     = excluded.exit_reason,
                    conditions_met  = excluded.conditions_met,
                    error_log       = excluded.error_log,
                    fees            = excluded.fees
                """;

            AddTradeParams(cmd, trade);
            cmd.ExecuteNonQuery();
        }
        finally { sem.Release(); }
    }

    // ─── Lecture ──────────────────────────────────────────────────────────────

    /// <summary>Retourne tous les trades (live ou replay).</summary>
    public List<TradeRecord> GetAllTrades(bool isReplay)
    {
        var cs = isReplay ? _replayCs : _liveCs;
        using var con = Open(cs);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM trades ORDER BY entry_time DESC";
        return ReadTrades(cmd);
    }

    /// <summary>
    /// Retourne les trades sans exit_time — utilisé au redémarrage
    /// pour resynchroniser les positions ouvertes depuis MT5.
    /// </summary>
    public List<TradeRecord> GetOpenTrades()
    {
        using var con = Open(_liveCs);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM trades WHERE exit_time IS NULL ORDER BY entry_time";
        return ReadTrades(cmd);
    }

    /// <summary>
    /// Vide DB Replay (Reset Replay ou fermeture de l'app).
    /// </summary>
    public async Task WipeReplayTradesAsync()
    {
        await _replayLock.WaitAsync();
        try
        {
            using var con = Open(_replayCs);
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM trades";
            cmd.ExecuteNonQuery();
        }
        finally { _replayLock.Release(); }
    }

    // ─── Privé ────────────────────────────────────────────────────────────────

    private static DuckDBConnection Open(string cs)
    {
        var con = new DuckDBConnection(cs);
        con.Open();
        return con;
    }

    private static void AddTradeParams(DuckDBCommand cmd, TradeRecord t)
    {
        cmd.Parameters.Add(new DuckDBParameter("id",                t.Id.ToString()));
        cmd.Parameters.Add(new DuckDBParameter("symbol",            t.Symbol));
        cmd.Parameters.Add(new DuckDBParameter("strategy_name",     t.StrategyName));
        cmd.Parameters.Add(new DuckDBParameter("strategy_settings", t.StrategySettings));
        cmd.Parameters.Add(new DuckDBParameter("direction",         t.Direction));
        cmd.Parameters.Add(new DuckDBParameter("entry_time",        (object?)t.EntryTime?.UtcDateTime ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("entry_price",       (object?)t.EntryPrice  ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("sl",                (object?)t.Sl          ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("tp",                (object?)t.Tp          ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("exit_time",         (object?)t.ExitTime?.UtcDateTime ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("exit_price",        (object?)t.ExitPrice   ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("profit_loss",       (object?)t.ProfitLoss  ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("risk_dollars",      (object?)t.RiskDollars ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("lot_size",          (object?)t.LotSize     ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("ticket_id",         (object?)t.TicketId    ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("correlation_id",    t.CorrelationId));
        cmd.Parameters.Add(new DuckDBParameter("exit_reason",       (object?)t.ExitReason    ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("conditions_met",    (object?)t.ConditionsMet ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("error_log",         (object?)t.ErrorLog      ?? DBNull.Value));
        cmd.Parameters.Add(new DuckDBParameter("fees",              (object?)t.Fees          ?? DBNull.Value));
    }

    private static List<TradeRecord> ReadTrades(DuckDBCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var trades = new List<TradeRecord>();

        while (r.Read())
        {
            trades.Add(new TradeRecord
            {
                Id               = Guid.Parse(r.GetString(r.GetOrdinal("id"))),
                Symbol           = r.GetString(r.GetOrdinal("symbol")),
                StrategyName     = r.GetString(r.GetOrdinal("strategy_name")),
                StrategySettings = r.GetString(r.GetOrdinal("strategy_settings")),
                Direction        = r.GetString(r.GetOrdinal("direction")),
                EntryTime        = ReadNullableTs(r, "entry_time"),
                EntryPrice       = ReadNullableDouble(r, "entry_price"),
                Sl               = ReadNullableDouble(r, "sl"),
                Tp               = ReadNullableDouble(r, "tp"),
                ExitTime         = ReadNullableTs(r, "exit_time"),
                ExitPrice        = ReadNullableDouble(r, "exit_price"),
                ProfitLoss       = ReadNullableDouble(r, "profit_loss"),
                RiskDollars      = ReadNullableDouble(r, "risk_dollars"),
                LotSize          = ReadNullableDouble(r, "lot_size"),
                TicketId         = ReadNullableLong(r, "ticket_id"),
                CorrelationId    = r.GetString(r.GetOrdinal("correlation_id")),
                ExitReason       = ReadNullableString(r, "exit_reason"),
                ConditionsMet    = ReadNullableString(r, "conditions_met"),
                ErrorLog         = ReadNullableString(r, "error_log"),
                Fees             = ReadNullableDouble(r, "fees"),
            });
        }

        return trades;
    }

    // ─── Helpers lecture nullable ──────────────────────────────────────────────

    private static DateTimeOffset? ReadNullableTs(DuckDB.NET.Data.DuckDBDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        if (r.IsDBNull(ord)) return null;
        return TimeZoneHelper.ToQuebec(r.GetDateTime(ord));
    }

    private static double? ReadNullableDouble(DuckDB.NET.Data.DuckDBDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDouble(ord);
    }

    private static long? ReadNullableLong(DuckDB.NET.Data.DuckDBDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt64(ord);
    }

    private static string? ReadNullableString(DuckDB.NET.Data.DuckDBDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }
}
