using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Orchestrateur principal — lit la Strategy et coordonne le moteur.
///
/// Règle absolue : StrategyRunner ne connaît RIEN de la stratégie spécifique.
///   • La Strategy DÉCLARE (SL/TP, conditions, settings)
///   • StrategyEvaluator ÉVALUE et DÉLÈGUE aux engines (RiskEngine, TradeManager…)
///   • StrategyRunner ORCHESTRE (source de bougies, warm-up, boucle)
///
/// Identique Live et Replay — seuls la source (ICandleSource) et l'exécuteur
/// (IOrderExecutor, injecté dans ExecutionManager via TradeManager) changent.
///
/// Replay : source = DuckDB | executor = SimulatedOrderExecutor
/// Live   : source = MT5CandleSource | executor = MT5OrderExecutor
/// </summary>
public sealed class StrategyRunner
{
    private readonly EventBus          _bus;
    private readonly IndicatorEngine   _indicators;
    private readonly SwingPointEngine  _swings;
    private readonly TrendEngine       _trend;
    private readonly RiskEngine        _risk;
    private readonly TradeManager      _trades;
    private readonly Func<string, SymbolMeta?> _metaProvider;

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
        EventBus          bus,
        IndicatorEngine   indicators,
        SwingPointEngine  swings,
        TrendEngine       trend,
        RiskEngine        risk,
        TradeManager      trades,
        Func<string, SymbolMeta?> metaProvider)
    {
        _bus          = bus;
        _indicators   = indicators;
        _swings       = swings;
        _trend        = trend;
        _risk         = risk;
        _trades       = trades;
        _metaProvider = metaProvider;
    }

    // ─── Run Replay ───────────────────────────────────────────────────────────

    public async Task RunReplayAsync(
        ICandleSource source,
        IStrategy     strategy,
        string        symbol,
        double        startingCapital,
        decimal       riskPercent,    // GLOBAL — vient de SettingsPage
        int           speed,          // 1 | 2 | 4 | 8
        CancellationToken ct)
    {
        IsRunning = true;

        int bufferSize = BufferAhead.GetValueOrDefault(speed, 60);
        int delayMs    = SpeedDelays.GetValueOrDefault(speed, 1000);

        double capital         = startingCapital;
        double dailyDrawdown   = 0;

        try
        {
            foreach (var tf in strategy.RequiredTimeframes)
            {
                while (!ct.IsCancellationRequested)
                {
                    var chunk = await source.GetNextChunkAsync(symbol, tf, bufferSize, ct);
                    if (chunk is null || chunk.Count == 0) break;

                    foreach (var candle in chunk)
                    {
                        if (ct.IsCancellationRequested) break;
                        await ProcessCandleAsync(candle, strategy, capital, riskPercent, dailyDrawdown, ct);
                        _bus.Publish(new NewCandleEvent(candle));
                        if (speed > 0)
                            await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { IsRunning = false; }
    }

    // ─── Run Live ─────────────────────────────────────────────────────────────

    public async Task RunLiveAsync(
        ICandleSource source,
        IStrategy     strategy,
        string        symbol,
        double        capital,
        decimal       riskPercent,    // GLOBAL — vient de SettingsPage
        CancellationToken ct)
    {
        IsRunning = true;
        double dailyDrawdown = 0;

        try
        {
            // Warm-up : charger l'historique récent pour les indicateurs
            foreach (var tf in strategy.RequiredTimeframes)
            {
                var warmup = await source.GetNextChunkAsync(symbol, tf, 300, ct);
                if (warmup is not null)
                    foreach (var c in warmup)
                        WarmUpEngines(c);
            }

            // Boucle live : attend une nouvelle bougie close à chaque appel
            while (!ct.IsCancellationRequested)
            {
                var chunk = await source.GetNextChunkAsync(
                    symbol, strategy.Timeframe, 1, ct);

                if (chunk is null || chunk.Count == 0)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                var candle = chunk[0];
                await ProcessCandleAsync(candle, strategy, capital, riskPercent, dailyDrawdown, ct);
                _bus.Publish(new NewCandleEvent(candle));
            }
        }
        catch (OperationCanceledException) { }
        finally { IsRunning = false; }
    }

    // ─── Traitement d'une bougie (commun Replay + Live) ───────────────────────

    private async Task ProcessCandleAsync(
        Candle    candle,
        IStrategy strategy,
        double    capital,
        decimal   riskPercent,
        double    dailyDrawdown,
        CancellationToken ct)
    {
        // Alimentation engines — TOUTES les bougies (warmup indicateurs multi-TF)
        _indicators.ProcessCandle(candle);
        _swings    .ProcessCandle(candle);
        _trend     .ProcessCandle(candle);

        // Évaluation Strategy seulement sur le TF principal
        if (candle.Timeframe != strategy.Timeframe) return;

        var iv = _indicators.GetIndicators(candle.Symbol, candle.Timeframe);
        if (iv is null) return;  // warm-up pas terminé

        // Délègue TOUT à StrategyEvaluator — source unique, identique Live et Replay
        await StrategyEvaluator.EvaluateAsync(
            candle, iv, strategy,
            _indicators, _trend, _trades, _risk,
            capital, dailyDrawdown, riskPercent,
            _metaProvider, ct);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void WarmUpEngines(Candle c)
    {
        _indicators.ProcessCandle(c);
        _swings    .ProcessCandle(c);
        _trend     .ProcessCandle(c);
    }
}
