using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Interfaces;

/// <summary>
/// Source de bougies abstraite — même interface pour Replay (DuckDB) et Live (MT5).
/// StrategyRunner ne sait pas lequel est utilisé.
/// </summary>
public interface ICandleSource
{
    /// <summary>Retourne le prochain chunk de bougies. Null = fin de données.</summary>
    Task<List<Candle>?> GetNextChunkAsync(string symbol, string timeframe, int count, CancellationToken ct);

    /// <summary>Tous les symboles disponibles dans cette source.</summary>
    List<string> GetAvailableSymbols();
}
