using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Calcule en continu les indicateurs sur tous les TF, toutes les paires.
/// Calcul incrémental — jamais recalculer tout l'historique à chaque bougie.
/// La Strategy lit, ne recalcule jamais.
///
/// Indicateurs : Ichimoku complet, MACD, EMA50, EMA200, ATR14, Pivots journaliers.
/// Minimum 52 bougies pour Ichimoku. EMA commence dès la 1ère bougie (imprécis < 200, acceptable).
/// Champs Previous* et *26 permettent aux Strategies de détecter cassures et Chikou libre.
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
        private const int BufferSize = 350;  // assez pour 52+26 Ichimoku + EMA200 warm-up
        private const int MinCandles = 52;   // minimum pour Ichimoku complet

        private readonly Queue<Candle> _buf = new();

        // ── EMA state (null = pas encore initialisé) ──────────────────────────
        private double? _ema12, _ema26, _ema50, _ema200, _signal9;

        // ── ATR(14) ───────────────────────────────────────────────────────────
        private double? _atr14;
        private double? _prevCandleClose;

        // ── Points Pivots journaliers ─────────────────────────────────────────
        private double _dayHigh  = 0;
        private double _dayLow   = double.MaxValue;
        private double _dayClose = 0;
        private int    _currentDay = -1;   // DayOfYear*10000+Year, -1 = pas encore init

        // Pivots du JOUR PRÉCÉDENT (utilisés pendant la session courante)
        private double _pivotPP = 0, _pivotR1 = 0, _pivotR2 = 0, _pivotS1 = 0, _pivotS2 = 0;

        public IndicatorValues? Current  { get; private set; }
        public IndicatorValues? Previous { get; private set; }

        public void Update(Candle candle)
        {
            _buf.Enqueue(candle);
            if (_buf.Count > BufferSize) _buf.Dequeue();

            var arr = _buf.ToArray();
            int n   = arr.Length;

            // ── Mise à jour ATR incrémentale ──────────────────────────────────
            double tr = _prevCandleClose.HasValue
                ? Math.Max(candle.High - candle.Low,
                  Math.Max(Math.Abs(candle.High - _prevCandleClose.Value),
                           Math.Abs(candle.Low  - _prevCandleClose.Value)))
                : candle.High - candle.Low;
            UpdateEma(ref _atr14, tr, 14);
            _prevCandleClose = candle.Close;

            // ── Mise à jour EMA incrémentale ──────────────────────────────────
            double close = candle.Close;
            UpdateEma(ref _ema12,  close, 12);
            UpdateEma(ref _ema26,  close, 26);
            UpdateEma(ref _ema50,  close, 50);
            UpdateEma(ref _ema200, close, 200);

            double macdLine = (_ema12 ?? close) - (_ema26 ?? close);
            UpdateEma(ref _signal9, macdLine, 9);

            // ── Pivots journaliers ────────────────────────────────────────────
            UpdateDailyPivots(candle);

            if (n < MinCandles) return;  // pas assez de bougies pour Ichimoku

            Previous = Current;
            Current  = Calculate(arr, n, macdLine, candle);
        }

        // ── Pivots : roll au changement de jour ───────────────────────────────

        private void UpdateDailyPivots(Candle candle)
        {
            int day = candle.Time.Year * 10000 + candle.Time.DayOfYear;

            if (_currentDay == -1)
            {
                // Premier candle : initialiser
                _currentDay = day;
                _dayHigh    = candle.High;
                _dayLow     = candle.Low;
            }
            else if (day != _currentDay)
            {
                // Nouveau jour : calculer pivots du jour précédent
                if (_dayLow < _dayHigh)  // sécurité
                {
                    _pivotPP = (_dayHigh + _dayLow + _dayClose) / 3.0;
                    _pivotR1 = 2.0 * _pivotPP - _dayLow;
                    _pivotR2 = _pivotPP + (_dayHigh - _dayLow);
                    _pivotS1 = 2.0 * _pivotPP - _dayHigh;
                    _pivotS2 = _pivotPP - (_dayHigh - _dayLow);
                }

                _currentDay = day;
                _dayHigh    = candle.High;
                _dayLow     = candle.Low;
            }
            else
            {
                if (candle.High > _dayHigh) _dayHigh = candle.High;
                if (candle.Low  < _dayLow)  _dayLow  = candle.Low;
            }

            _dayClose = candle.Close;
        }

        // ── Calcul principal ──────────────────────────────────────────────────

        private IndicatorValues Calculate(Candle[] arr, int n, double macdLine, Candle currentCandle)
        {
            int last = n - 1;

            // ── Ichimoku ──────────────────────────────────────────────────────

            double tenkan = Midpoint(arr, n - 9,  n);
            double kijun  = Midpoint(arr, n - 26, n);

            // Cloud actuel = calculé il y a 26 bougies, affiché maintenant
            double senkouA = 0, senkouB = 0;
            double tenkan26 = 0, kijun26 = 0;
            if (n >= 26 + 9)
            {
                int t26 = n - 26;
                tenkan26 = Midpoint(arr, t26 - 9,  t26);
                kijun26  = Midpoint(arr, t26 - 26, t26);
                senkouA  = (tenkan26 + kijun26) / 2.0;
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
            // Timestamp réel de la bougie à -26 (n >= 52 garanti ici, arr[n-27] est toujours valide)
            var chikouCandleTime = arr[n - 27].Time;

            // ── Historique -26 pour Chikou libre ──────────────────────────────
            // Bar à -26 = arr[n-27] (0-indexed, n-1 = bar actuel)
            double high26 = 0, low26 = 0;
            if (n >= 27)
            {
                high26 = arr[n - 27].High;
                low26  = arr[n - 27].Low;
            }

            // ── Valeurs bougie précédente ─────────────────────────────────────
            double prevClose = 0, prevOpen = 0, prevHigh = 0, prevLow = 0;
            double prevKijun = 0, prevTenkan = 0;
            if (n >= 2)
            {
                prevClose  = arr[n - 2].Close;
                prevOpen   = arr[n - 2].Open;
                prevHigh   = arr[n - 2].High;
                prevLow    = arr[n - 2].Low;
                // Tenkan/Kijun de la bougie précédente
                prevTenkan = Midpoint(arr, n - 10, n - 1);
                prevKijun  = Midpoint(arr, n - 27, n - 1);
            }

            return new IndicatorValues
            {
                CandleTime = currentCandle.Time,

                Tenkan    = tenkan,
                Kijun     = kijun,
                SenkouA   = senkouA,
                SenkouB   = senkouB,
                SenkouA26 = senkouA26,
                SenkouB26 = senkouB26,
                Chikou           = chikou,
                ChikouCandleTime = chikouCandleTime,

                MacdLine   = macdLine,
                SignalLine  = _signal9 ?? 0,
                Histogram   = macdLine - (_signal9 ?? 0),

                Ema50  = _ema50  ?? 0,
                Ema200 = _ema200 ?? 0,

                Atr14 = _atr14 ?? 0,

                Close = arr[last].Close,
                Open  = arr[last].Open,
                High  = arr[last].High,
                Low   = arr[last].Low,

                PrevClose  = prevClose,
                PrevOpen   = prevOpen,
                PrevHigh   = prevHigh,
                PrevLow    = prevLow,
                PrevKijun  = prevKijun,
                PrevTenkan = prevTenkan,

                High26   = high26,
                Low26    = low26,
                Kijun26  = kijun26,
                Tenkan26 = tenkan26,

                PivotPP = _pivotPP,
                PivotR1 = _pivotR1,
                PivotR2 = _pivotR2,
                PivotS1 = _pivotS1,
                PivotS2 = _pivotS2,
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
