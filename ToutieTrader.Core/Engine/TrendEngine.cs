using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Calcule Bull/Bear/Range sur tous les TF, toutes les paires.
/// Définition unique, globale, NON modifiable par une Strategy.
///
/// Bull  = EMA50 + EMA200 montent + structure HH/HL + prix au-dessus du cloud Ichimoku
/// Bear  = EMA50 + EMA200 descendent + structure LL/LH + prix en-dessous du cloud
/// Range = EMA50 ou EMA200 plate (slope sous seuil) + compression LH/HL
///
/// "Au-dessus du cloud" = close > max(SenkouA, SenkouB)
/// "En-dessous du cloud" = close < min(SenkouA, SenkouB)
///
/// EMA50/EMA200 utilisées pour la slope — potentiellement configurable via Settings plus tard.
/// Seuil slope "plate" hardcodé ici.
/// </summary>
public sealed class TrendEngine
{
    private readonly EventBus        _bus;
    private readonly IndicatorEngine _indicators;
    private readonly SwingPointEngine _swings;

    // Seuil slope hardcodé — EMA considérée "plate" si variation relative < 0.005%
    private const double FlatSlopeThreshold = 0.00005;

    private readonly Dictionary<(string, string), TrendState>      _states   = new();
    private readonly Dictionary<(string, string), IndicatorValues> _previous = new();

    public TrendEngine(EventBus bus, IndicatorEngine indicators, SwingPointEngine swings)
    {
        _bus        = bus;
        _indicators = indicators;
        _swings     = swings;
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    public void ProcessCandle(Candle candle)
    {
        var key  = (candle.Symbol, candle.Timeframe);
        var curr = _indicators.GetIndicators(candle.Symbol, candle.Timeframe);
        var prev = _indicators.GetPreviousIndicators(candle.Symbol, candle.Timeframe);

        if (curr is null) return;  // pas encore assez de bougies

        var trend  = Evaluate(curr, prev, candle.Symbol, candle.Timeframe);
        var state  = new TrendState { Timeframe = candle.Timeframe, Trend = trend };
        _states[key] = state;

        _bus.Publish(new TrendUpdatedEvent(candle.Symbol, candle.Timeframe, state));
    }

    public TrendState? GetTrend(string symbol, string timeframe)
        => _states.TryGetValue((symbol, timeframe), out var s) ? s : null;

    public void Reset() => _states.Clear();

    // ─── Logique Bull/Bear/Range ──────────────────────────────────────────────

    private TrendDirection Evaluate(
        IndicatorValues curr, IndicatorValues? prev,
        string symbol, string timeframe)
    {
        // ── Slopes EMA ────────────────────────────────────────────────────────
        bool ema50Rising   = prev is not null && IsRising(prev.Ema50,  curr.Ema50);
        bool ema50Falling  = prev is not null && IsFalling(prev.Ema50, curr.Ema50);
        bool ema200Rising  = prev is not null && IsRising(prev.Ema200,  curr.Ema200);
        bool ema200Falling = prev is not null && IsFalling(prev.Ema200, curr.Ema200);
        bool ema50Flat     = !ema50Rising  && !ema50Falling;
        bool ema200Flat    = !ema200Rising && !ema200Falling;

        // ── Prix vs Cloud Ichimoku ────────────────────────────────────────────
        double cloudTop    = Math.Max(curr.SenkouA, curr.SenkouB);
        double cloudBottom = Math.Min(curr.SenkouA, curr.SenkouB);
        bool aboveCloud    = curr.Close > cloudTop;
        bool belowCloud    = curr.Close < cloudBottom;

        // ── Structure Swing Points ────────────────────────────────────────────
        var recentSwings   = _swings.GetSwingPoints(symbol, timeframe, 6);
        bool bullStructure = HasBullStructure(recentSwings);
        bool bearStructure = HasBearStructure(recentSwings);

        // ── Décision ─────────────────────────────────────────────────────────

        if (ema50Rising && ema200Rising && aboveCloud && bullStructure)
            return TrendDirection.Bull;

        if (ema50Falling && ema200Falling && belowCloud && bearStructure)
            return TrendDirection.Bear;

        // Range : EMA plate ou compression LH/HL
        if (ema50Flat || ema200Flat)
            return TrendDirection.Range;

        // Sinon : pas de signal clair → Range par défaut
        return TrendDirection.Range;
    }

    // ── Helpers slope ──────────────────────────────────────────────────────────

    private static bool IsRising(double prev, double curr)
    {
        if (prev == 0) return false;
        return (curr - prev) / prev > FlatSlopeThreshold;
    }

    private static bool IsFalling(double prev, double curr)
    {
        if (prev == 0) return false;
        return (prev - curr) / prev > FlatSlopeThreshold;
    }

    // ── Helpers structure swing ────────────────────────────────────────────────

    /// <summary>Bull = au moins un HH et un HL dans les derniers swings.</summary>
    private static bool HasBullStructure(List<SwingPoint> swings)
        => swings.Any(s => s.Type == SwingPointType.HH)
        && swings.Any(s => s.Type == SwingPointType.HL);

    /// <summary>Bear = au moins un LL et un LH dans les derniers swings.</summary>
    private static bool HasBearStructure(List<SwingPoint> swings)
        => swings.Any(s => s.Type == SwingPointType.LL)
        && swings.Any(s => s.Type == SwingPointType.LH);
}
