using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Calcule en continu les indicateurs sur tous les TF, toutes les paires.
/// Calcul incrémental — jamais recalculer tout l'historique à chaque bougie.
/// La Strategy lit, ne recalcule jamais.
///
/// Indicateurs : Ichimoku complet, MACD, EMA50, EMA200.
/// Minimum 52 bougies pour Ichimoku. EMA commence dès la 1ère bougie (imprécis < 200, acceptable).
/// </summary>
public sealed class IndicatorEngine
{
    private readonly EventBus _bus;
    private readonly Dictionary<(string Symbol, string Timeframe), IndicatorState> _states = new();

    public IndicatorEngine(EventBus bus) => _bus = bus;

    // ─── API publique ─────────────────────────────────────────────────────────

    public void ProcessCandle(Candle candle)
    {
        var key = (candle.Symbol, candle.Timeframe);
        if (!_states.TryGetValue(key, out var state))
        {
            state = new IndicatorState();
            _states[key] = state;
        }

        state.Update(candle);

        if (state.Current is not null)
            _bus.Publish(new IndicatorsUpdatedEvent(candle.Symbol, candle.Timeframe, state.Current));
    }

    /// <summary>Valeurs courantes pour un symbole/TF. Null si pas assez de bougies.</summary>
    public IndicatorValues? GetIndicators(string symbol, string timeframe)
        => _states.TryGetValue((symbol, timeframe), out var s) ? s.Current : null;

    /// <summary>Valeurs de la bougie précédente (pour les Strategies qui en ont besoin).</summary>
    public IndicatorValues? GetPreviousIndicators(string symbol, string timeframe)
        => _states.TryGetValue((symbol, timeframe), out var s) ? s.Previous : null;

    public void Reset() => _states.Clear();

    // ─── État par (symbol, timeframe) ─────────────────────────────────────────

    private sealed class IndicatorState
    {
        private const int BufferSize  = 350;  // assez pour 52+26 Ichimoku + EMA200 warm-up
        private const int MinCandles  = 52;   // minimum pour Ichimoku complet

        private readonly Queue<Candle> _buf = new();

        // EMA state (null = pas encore initialisé)
        private double? _ema12, _ema26, _ema50, _ema200, _signal9;

        public IndicatorValues? Current  { get; private set; }
        public IndicatorValues? Previous { get; private set; }

        public void Update(Candle candle)
        {
            _buf.Enqueue(candle);
            if (_buf.Count > BufferSize) _buf.Dequeue();

            var arr = _buf.ToArray();
            int n   = arr.Length;

            // Mise à jour EMA incrémentale (toujours, même avant MinCandles)
            double close = candle.Close;
            UpdateEma(ref _ema12,  close, 12);
            UpdateEma(ref _ema26,  close, 26);
            UpdateEma(ref _ema50,  close, 50);
            UpdateEma(ref _ema200, close, 200);

            double macdLine = (_ema12 ?? close) - (_ema26 ?? close);
            UpdateEma(ref _signal9, macdLine, 9);

            if (n < MinCandles) return;  // pas assez de bougies pour Ichimoku

            Previous = Current;
            Current  = Calculate(arr, n, macdLine);
        }

        private IndicatorValues Calculate(Candle[] arr, int n, double macdLine)
        {
            int last = n - 1;

            // ── Ichimoku ──────────────────────────────────────────────────────

            double tenkan = Midpoint(arr, n - 9,  n);
            double kijun  = Midpoint(arr, n - 26, n);

            // Cloud actuel = calculé il y a 26 bougies, affiché maintenant
            double senkouA = 0, senkouB = 0;
            if (n >= 26 + 9)
            {
                int t26 = n - 26;
                double tenkan26 = Midpoint(arr, t26 - 9,  t26);
                double kijun26  = Midpoint(arr, t26 - 26, t26);
                senkouA = (tenkan26 + kijun26) / 2.0;
            }
            if (n >= 26 + 52)
            {
                int t26 = n - 26;
                senkouB = Midpoint(arr, t26 - 52, t26);
            }

            // Cloud futur = calculé maintenant, affiché dans 26 bougies
            double senkouA26 = (tenkan + kijun) / 2.0;
            double senkouB26 = Midpoint(arr, n - 52, n);

            // Chikou = close actuel (tracé 26 bougies en arrière sur le chart)
            double chikou = arr[last].Close;

            return new IndicatorValues
            {
                Tenkan    = tenkan,
                Kijun     = kijun,
                SenkouA   = senkouA,
                SenkouB   = senkouB,
                SenkouA26 = senkouA26,
                SenkouB26 = senkouB26,
                Chikou    = chikou,

                MacdLine   = macdLine,
                SignalLine  = _signal9  ?? 0,
                Histogram   = macdLine - (_signal9 ?? 0),

                Ema50  = _ema50  ?? 0,
                Ema200 = _ema200 ?? 0,

                Close = arr[last].Close,
                Open  = arr[last].Open,
                High  = arr[last].High,
                Low   = arr[last].Low,
            };
        }

        // Midpoint Ichimoku = (highest high + lowest low) / 2 sur [start, end[
        private static double Midpoint(Candle[] arr, int start, int end)
        {
            start = Math.Max(0, start);
            end   = Math.Min(arr.Length, end);
            if (start >= end) return arr[^1].Close;

            double high = arr[start].High;
            double low  = arr[start].Low;
            for (int i = start + 1; i < end; i++)
            {
                if (arr[i].High > high) high = arr[i].High;
                if (arr[i].Low  < low)  low  = arr[i].Low;
            }
            return (high + low) / 2.0;
        }

        private static void UpdateEma(ref double? ema, double value, int period)
        {
            if (ema is null) { ema = value; return; }
            double alpha = 2.0 / (period + 1);
            ema = value * alpha + ema.Value * (1.0 - alpha);
        }
    }
}
