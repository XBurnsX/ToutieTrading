using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Gère le cycle de vie des trades ouverts.
///
/// Règles absolues :
///   - Entrée = open de la bougie SUIVANT le signal (jamais sur la bougie du signal)
///   - SL et TP vérifiés à chaque bougie
///   - ForceExitConditions → UNE vraie = ferme immédiatement
///   - OptionalExitConditions → UNE vraie + activée dans Settings = ferme
///   - Une position par paire par direction. Zéro hedging.
/// </summary>
public sealed class TradeManager
{
    private readonly EventBus        _bus;
    private readonly IndicatorEngine _indicators;
    private readonly TrendEngine     _trend;
    private readonly ExecutionManager _execution;

    // Signal en attente → sera exécuté à l'open de la prochaine bougie
    private readonly Dictionary<string, (TradeSignal Signal, IStrategy Strategy)> _pendingSignals = new();

    // Trades ouverts en mémoire : correlationId → (record, strategy)
    private readonly Dictionary<string, (TradeRecord Record, IStrategy Strategy)> _openTrades = new();

    public TradeManager(
        EventBus bus,
        IndicatorEngine indicators,
        TrendEngine trend,
        ExecutionManager execution)
    {
        _bus        = bus;
        _indicators = indicators;
        _trend      = trend;
        _execution  = execution;
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>Appelé quand une Strategy émet un signal. Mis en attente jusqu'à la prochaine bougie.</summary>
    public void QueueSignal(TradeSignal signal, IStrategy strategy)
    {
        // Une position par paire par direction — zéro hedging
        string key = $"{signal.Symbol}:{signal.Direction}";
        if (_openTrades.Values.Any(t =>
                t.Record.Symbol    == signal.Symbol &&
                t.Record.Direction == signal.Direction))
            return;

        _pendingSignals[key] = (signal, strategy);
    }

    /// <summary>
    /// Appelé au début de chaque bougie.
    /// Exécute les signaux en attente (entrée = open de cette bougie).
    /// Puis vérifie SL/TP et conditions de sortie sur les trades ouverts.
    /// </summary>
    public async Task ProcessCandleAsync(Candle candle, CancellationToken ct)
    {
        // 1. Exécuter les signaux en attente sur l'open de cette bougie
        await ExecutePendingSignalsAsync(candle, ct);

        // 2. Vérifier les trades ouverts
        await CheckOpenTradesAsync(candle, ct);
    }

    /// <summary>Reprend les trades ouverts au redémarrage (depuis DB).</summary>
    public void RestoreOpenTrades(List<(TradeRecord Record, IStrategy Strategy)> trades)
    {
        foreach (var (record, strategy) in trades)
            _openTrades[record.CorrelationId] = (record, strategy);
    }

    public List<TradeRecord> GetOpenTrades()
        => _openTrades.Values.Select(v => v.Record).ToList();

    // ─── Exécution des signaux en attente ─────────────────────────────────────

    private async Task ExecutePendingSignalsAsync(Candle candle, CancellationToken ct)
    {
        var toExecute = _pendingSignals
            .Where(kv => kv.Key.StartsWith(candle.Symbol + ":"))
            .ToList();

        foreach (var (key, (signal, strategy)) in toExecute)
        {
            _pendingSignals.Remove(key);

            // Entrée au prix open de cette bougie
            var signalWithEntry = signal with { EntryPrice = candle.Open };

            var record = await _execution.ExecuteEntryAsync(signalWithEntry, strategy, candle.Time, ct);
            if (record is not null)
                _openTrades[record.CorrelationId] = (record, strategy);
        }
    }

    // ─── Vérification des trades ouverts ──────────────────────────────────────

    private async Task CheckOpenTradesAsync(Candle candle, CancellationToken ct)
    {
        var toClose = new List<(string CorrelationId, string Reason)>();

        foreach (var (corrId, (record, strategy)) in _openTrades)
        {
            if (record.Symbol != candle.Symbol) continue;

            var indicators = _indicators.GetIndicators(candle.Symbol, record.StrategyName != "" ? candle.Timeframe : strategy.Timeframe);
            var trendState = _trend.GetTrend(candle.Symbol, strategy.Timeframe)
                          ?? new TrendState { Timeframe = strategy.Timeframe, Trend = TrendDirection.Range };

            if (indicators is null) continue;

            // ── SL hit ────────────────────────────────────────────────────────
            if (record.Sl.HasValue)
            {
                bool slHit = record.Direction == "BUY"
                    ? candle.Low  <= record.Sl.Value
                    : candle.High >= record.Sl.Value;
                if (slHit) { toClose.Add((corrId, "SL")); continue; }
            }

            // ── TP hit ────────────────────────────────────────────────────────
            if (record.Tp.HasValue)
            {
                bool tpHit = record.Direction == "BUY"
                    ? candle.High >= record.Tp.Value
                    : candle.Low  <= record.Tp.Value;
                if (tpHit) { toClose.Add((corrId, "TP")); continue; }
            }

            // ── ForceExitConditions (UNE vraie = ferme) ───────────────────────
            foreach (var cond in strategy.ForceExitConditions)
            {
                var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
                var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trendState;

                if (cond.Expression(condIndicators, condTrend))
                {
                    toClose.Add((corrId, $"ForceExit:{cond.Label}"));
                    break;
                }
            }
            if (toClose.Any(c => c.CorrelationId == corrId)) continue;

            // ── OptionalExitConditions (UNE vraie + activée dans Settings) ────
            foreach (var cond in strategy.OptionalExitConditions)
            {
                if (!IsOptionalExitEnabled(strategy, cond.Label)) continue;

                var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
                var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trendState;

                if (cond.Expression(condIndicators, condTrend))
                {
                    toClose.Add((corrId, $"OptionalExit:{cond.Label}"));
                    break;
                }
            }
        }

        // Fermer les trades
        foreach (var (corrId, reason) in toClose)
        {
            if (!_openTrades.TryGetValue(corrId, out var pair)) continue;
            await _execution.ExecuteExitAsync(pair.Record, candle, reason, ct);
            _openTrades.Remove(corrId);
        }
    }

    private static bool IsOptionalExitEnabled(IStrategy strategy, string label)
    {
        string settingKey = $"Exit_{label.Replace(" ", "_")}";
        return strategy.Settings.TryGetValue(settingKey, out var val) && val is true;
    }
}
