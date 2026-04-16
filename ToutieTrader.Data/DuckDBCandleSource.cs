using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Data;

/// <summary>
/// ICandleSource backed by DuckDB — utilisé exclusivement par le Replay.
/// Maintient un curseur par (symbol, timeframe) pour les requêtes en chunks.
/// </summary>
public sealed class DuckDBCandleSource : ICandleSource
{
    private readonly DuckDBReader _reader;
    private readonly Dictionary<(string, string), DateTimeOffset> _cursors = new();

    private DateTimeOffset _from = DateTimeOffset.MinValue;
    private DateTimeOffset _to   = DateTimeOffset.MaxValue;

    public DuckDBCandleSource(DuckDBReader reader) => _reader = reader;

    /// <summary>Réinitialise les curseurs et définit la plage de dates pour la session.</summary>
    public void Reset(DateTimeOffset from, DateTimeOffset to)
    {
        _from = from;
        _to   = to;
        _cursors.Clear();
    }

    /// <summary>
    /// Retourne le prochain chunk de bougies dans la plage configurée.
    /// Retourne null quand toutes les bougies ont été consommées.
    /// </summary>
    public Task<List<Candle>?> GetNextChunkAsync(
        string symbol, string timeframe, int count, CancellationToken ct)
    {
        var key = (symbol, timeframe);
        if (!_cursors.TryGetValue(key, out var cursor))
            cursor = _from;

        if (cursor > _to)
            return Task.FromResult<List<Candle>?>(null);

        var raw      = _reader.GetCandlesChunk(symbol, timeframe, cursor, count);
        var filtered = raw.Where(c => c.Time <= _to).ToList();

        if (filtered.Count == 0)
            return Task.FromResult<List<Candle>?>(null);

        // Avancer le curseur après la dernière bougie lue
        _cursors[key] = filtered[^1].Time.AddSeconds(1);

        return Task.FromResult<List<Candle>?>(filtered);
    }

    public List<string> GetAvailableSymbols() => _reader.GetAvailableSymbols();
}
