using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Boucle principale — ordre fixe et déterministe à chaque bougie :
///
///   Source → IndicatorEngine → SwingPointEngine → TrendEngine
///         → Strategy.Evaluate() → RiskEngine → TradeManager → ExecutionManager
///
/// Replay : source = ICandleSource (DuckDB), vitesse x1/x2/x4/x8
/// Live   : source = ICandleSource (MT5ApiClient), attend close de chaque bougie
///
/// Le moteur ne sait pas quelle source est utilisée — découplage complet.
/// </summary>
public sealed class StrategyRunner
{
    private readonly EventBus        _bus;
    private readonly IndicatorEngine _indicators;
    private readonly SwingPointEngine _swings;
    private readonly TrendEngine     _trend;
    private readonly RiskEngine      _risk;
    private readonly TradeManager    _trades;

    // Vitesses Replay : délai entre bougies en ms
    private static readonly Dictionary<int, int> SpeedDelays = new()
    {
        [1] = 1000,
        [2] = 500,
        [4] = 250,
        [8] = 125,
    };

    // Buffers adaptatifs Replay (bougies ahead)
    private static readonly Dictionary<int, int> BufferAhead = new()
    {
        [1] = 60,
        [2] = 120,
        [4] = 240,
        [8] = 480,
    };

    public bool IsRunning { get; private set; }

    public StrategyRunner(
        EventBus bus,
        IndicatorEngine indicators,
        SwingPointEngine swings,
        TrendEngine trend,
        RiskEngine risk,
        TradeManager trades)
    {
        _bus        = bus;
        _indicators = indicators;
        _swings     = swings;
        _trend      = trend;
        _risk       = risk;
        _trades     = trades;
    }

    // ─── Run Replay ───────────────────────────────────────────────────────────

    public async Task RunReplayAsync(
        ICandleSource source,
        IStrategy     strategy,
        double        startingCapital,
        int           speed,           // 1 | 2 | 4 | 8
        CancellationToken ct)
    {
        IsRunning = true;

        int bufferSize = BufferAhead.GetValueOrDefault(speed, 60);
        int delayMs    = SpeedDelays.GetValueOrDefault(speed, 1000);

        double capital          = startingCapital;
        double dailyDrawdown    = 0;
        int    openTradesCount  = 0;

        try
        {
            foreach (var tf in strategy.RequiredTimeframes)
            {
                while (!ct.IsCancellationRequested)
                {
                    var chunk = await source.GetNextChunkAsync(strategy.Symbol(), tf, bufferSize, ct);
                    if (chunk is null || chunk.Count == 0) break;

                    foreach (var candle in chunk)
                    {
                        if (ct.IsCancellationRequested) break;

                        await ProcessCandleAsync(candle, strategy, capital, dailyDrawdown, openTradesCount, ct);
                        _bus.Publish(new NewCandleEvent(candle));

                        openTradesCount = _trades.GetOpenTrades().Count;

                        if (speed > 0)
                            await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* Pause/Stop normal */ }
        finally { IsRunning = false; }
    }

    // ─── Run Live ─────────────────────────────────────────────────────────────

    public async Task RunLiveAsync(
        ICandleSource source,
        IStrategy     strategy,
        double        capital,
        CancellationToken ct)
    {
        IsRunning = true;
        double dailyDrawdown   = 0;
        int    openTradesCount = 0;

        try
        {
            // Warm-up : charger l'historique récent pour les indicateurs
            foreach (var tf in strategy.RequiredTimeframes)
            {
                var warmup = await source.GetNextChunkAsync(strategy.Symbol(), tf, 300, ct);
                if (warmup is not null)
                    foreach (var c in warmup)
                        WarmUpEngines(c);
            }

            // Boucle live : attend une nouvelle bougie close à chaque tick
            while (!ct.IsCancellationRequested)
            {
                var chunk = await source.GetNextChunkAsync(
                    strategy.Symbol(), strategy.Timeframe, 1, ct);

                if (chunk is null || chunk.Count == 0)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                var candle = chunk[0];
                await ProcessCandleAsync(candle, strategy, capital, dailyDrawdown, openTradesCount, ct);
                _bus.Publish(new NewCandleEvent(candle));
                openTradesCount = _trades.GetOpenTrades().Count;
            }
        }
        catch (OperationCanceledException) { /* Stop normal */ }
        finally { IsRunning = false; }
    }

    // ─── Traitement d'une bougie (commun Replay + Live) ───────────────────────

    private async Task ProcessCandleAsync(
        Candle    candle,
        IStrategy strategy,
        double    capital,
        double    dailyDrawdown,
        int       openTradesCount,
        CancellationToken ct)
    {
        // Ordre fixe et déterministe
        _indicators.ProcessCandle(candle);
        _swings.ProcessCandle(candle);
        _trend.ProcessCandle(candle);

        // Évaluation Strategy seulement sur le TF principal
        if (candle.Timeframe != strategy.Timeframe) return;

        var indicatorValues = _indicators.GetIndicators(candle.Symbol, candle.Timeframe);
        var trendState      = _trend.GetTrend(candle.Symbol, candle.Timeframe)
                           ?? new TrendState { Timeframe = candle.Timeframe, Trend = TrendDirection.Range };

        if (indicatorValues is null) return;  // warm-up pas terminé

        // Évaluation des conditions d'entrée
        var longSignal  = EvaluateEntry(strategy.LongConditions,  indicatorValues, trendState, strategy, "BUY",  candle);
        var shortSignal = EvaluateEntry(strategy.ShortConditions, indicatorValues, trendState, strategy, "SELL", candle);

        var signal = longSignal ?? shortSignal;

        if (signal is not null)
        {
            // RiskEngine
            var riskResult = _risk.Calculate(
                capital, (double)strategy.RiskPercent,
                candle.Close, signal.Sl,
                candle.Symbol, strategy,
                openTradesCount, dailyDrawdown);

            if (riskResult is not null)
            {
                signal = signal with { LotSize = riskResult.LotSize };
                _trades.QueueSignal(signal, strategy);
            }
        }

        // TradeManager : exécute signaux en attente + vérifie SL/TP/exits
        await _trades.ProcessCandleAsync(candle, ct);
    }

    // ─── Évaluation conditions ────────────────────────────────────────────────

    private TradeSignal? EvaluateEntry(
        List<StrategyCondition> conditions,
        IndicatorValues         indicators,
        TrendState              trend,
        IStrategy               strategy,
        string                  direction,
        Candle                  candle)
    {
        if (conditions.Count == 0) return null;

        var metLabels = new List<string>();

        foreach (var cond in conditions)
        {
            var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
            var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trend;

            if (!cond.Expression(condIndicators, condTrend))
                return null;  // TOUTES doivent être vraies

            metLabels.Add(cond.Label);
        }

        // Calculer SL et TP via la règle déclarée dans la Strategy
        double sl = CalculateSl(strategy.StopLoss, direction, indicators, candle.Symbol, candle.Timeframe);
        double tp = CalculateTp(strategy.TakeProfit, direction, candle.Close, sl);

        return new TradeSignal
        {
            Symbol        = candle.Symbol,
            Direction     = direction,
            EntryPrice    = candle.Close,
            Sl            = sl,
            Tp            = tp,
            CorrelationId = Guid.NewGuid().ToString(),
            ConditionsMet = metLabels,
        };
    }

    // ─── Calcul SL / TP ───────────────────────────────────────────────────────

    private double CalculateSl(
        StopLossRule rule, string direction,
        IndicatorValues iv, string symbol, string timeframe)
    {
        double pipSize = symbol.Contains("JPY") ? 0.01 : 0.0001;
        double buffer  = rule.BufferPips * pipSize;

        return rule.Type switch
        {
            StopLossType.BelowCloud => Math.Min(iv.SenkouA, iv.SenkouB) - buffer,
            StopLossType.AboveCloud => Math.Max(iv.SenkouA, iv.SenkouB) + buffer,
            StopLossType.SwingLow   => GetLastSwingPrice(symbol, timeframe, "low")  - buffer,
            StopLossType.SwingHigh  => GetLastSwingPrice(symbol, timeframe, "high") + buffer,
            StopLossType.Fixed      => direction == "BUY"
                                        ? iv.Close - rule.BufferPips * pipSize
                                        : iv.Close + rule.BufferPips * pipSize,
            _ => iv.Close
        };
    }

    private double CalculateTp(TakeProfitRule rule, string direction, double entryPrice, double sl)
    {
        double slDistance = Math.Abs(entryPrice - sl);

        return rule.Type switch
        {
            TakeProfitType.RiskRatio => direction == "BUY"
                ? entryPrice + slDistance * rule.Ratio
                : entryPrice - slDistance * rule.Ratio,
            TakeProfitType.Fixed =>
                direction == "BUY"
                ? entryPrice + rule.Ratio   // Ratio = pips dans ce cas
                : entryPrice - rule.Ratio,
            _ => entryPrice
        };
    }

    private double GetLastSwingPrice(string symbol, string timeframe, string type)
    {
        var point = type == "low"
            ? _swings.GetLastSwingLow(symbol, timeframe)
            : _swings.GetLastSwingHigh(symbol, timeframe);
        return point?.Price ?? 0;
    }

    private void WarmUpEngines(Candle c)
    {
        _indicators.ProcessCandle(c);
        _swings.ProcessCandle(c);
        _trend.ProcessCandle(c);
    }
}

// Extension helper pour accéder au symbole principal d'une Strategy
file static class StrategyExtensions
{
    public static string Symbol(this IStrategy s)
        => s.RequiredTimeframes.Count > 0 ? "" : "";  // Fourni par le StrategyRunner context
}
