using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Détecte HH/HL/LH/LL sur chaque TF en continu.
/// N bougies de contexte = configurable par Strategy via Settings["SwingLookback"].
/// Détection avec un lag de N bougies (nécessaire en temps réel).
/// </summary>
public sealed class SwingPointEngine
{
    private readonly Dictionary<(string, string), SwingState> _states = new();

    /// <summary>
    /// Nombre de bougies de contexte de chaque côté pour valider un swing.
    /// Peut être mis à jour par la Strategy via Settings.
    /// </summary>
    public int LookbackN { get; set; } = 5;

    // ─── API publique ─────────────────────────────────────────────────────────

    public void ProcessCandle(Candle candle)
    {
        var key = (candle.Symbol, candle.Timeframe);
        if (!_states.TryGetValue(key, out var state))
        {
            state = new SwingState();
            _states[key] = state;
        }
        state.Update(candle, LookbackN);
    }

    /// <summary>Retourne les derniers N swing points détectés pour un symbole/TF.</summary>
    public List<SwingPoint> GetSwingPoints(string symbol, string timeframe, int count = 20)
        => _states.TryGetValue((symbol, timeframe), out var s) ? s.GetLast(count) : [];

    /// <summary>Dernier swing high détecté.</summary>
    public SwingPoint? GetLastSwingHigh(string symbol, string timeframe)
        => _states.TryGetValue((symbol, timeframe), out var s)
            ? s.GetLast(50).LastOrDefault(p => p.Type is SwingPointType.HH or SwingPointType.LH)
            : null;

    /// <summary>Dernier swing low détecté.</summary>
    public SwingPoint? GetLastSwingLow(string symbol, string timeframe)
        => _states.TryGetValue((symbol, timeframe), out var s)
            ? s.GetLast(50).LastOrDefault(p => p.Type is SwingPointType.HL or SwingPointType.LL)
            : null;

    public void Reset() => _states.Clear();

    // ─── État par (symbol, timeframe) ─────────────────────────────────────────

    private sealed class SwingState
    {
        private const int BufferSize = 200;
        private readonly List<Candle>     _buf    = new();
        private readonly List<SwingPoint> _swings = new();

        // Dernier swing high/low confirmé (pour HH/HL/LH/LL)
        private double? _lastSwingHigh;
        private double? _lastSwingLow;

        public void Update(Candle candle, int n)
        {
            _buf.Add(candle);
            if (_buf.Count > BufferSize)
                _buf.RemoveAt(0);

            // On peut confirmer le swing du point T-N maintenant qu'on a N bougies après
            int candidateIdx = _buf.Count - 1 - n;
            if (candidateIdx < n) return;  // pas encore assez de bougies avant le candidat

            var candidate = _buf[candidateIdx];

            // ── Détection swing high ──────────────────────────────────────────
            bool isSwingHigh = true;
            for (int i = candidateIdx - n; i < candidateIdx + n; i++)
            {
                if (i == candidateIdx || i < 0 || i >= _buf.Count) continue;
                if (_buf[i].High >= candidate.High) { isSwingHigh = false; break; }
            }

            if (isSwingHigh)
            {
                var type = _lastSwingHigh is null || candidate.High > _lastSwingHigh.Value
                    ? SwingPointType.HH
                    : SwingPointType.LH;

                _swings.Add(new SwingPoint { Type = type, Price = candidate.High, Time = candidate.Time });
                _lastSwingHigh = candidate.High;
            }

            // ── Détection swing low ───────────────────────────────────────────
            bool isSwingLow = true;
            for (int i = candidateIdx - n; i < candidateIdx + n; i++)
            {
                if (i == candidateIdx || i < 0 || i >= _buf.Count) continue;
                if (_buf[i].Low <= candidate.Low) { isSwingLow = false; break; }
            }

            if (isSwingLow)
            {
                var type = _lastSwingLow is null || candidate.Low > _lastSwingLow.Value
                    ? SwingPointType.HL
                    : SwingPointType.LL;

                _swings.Add(new SwingPoint { Type = type, Price = candidate.Low, Time = candidate.Time });
                _lastSwingLow = candidate.Low;
            }

            // Garder max 200 swing points en mémoire
            if (_swings.Count > 200)
                _swings.RemoveAt(0);
        }

        public List<SwingPoint> GetLast(int count)
            => _swings.Count <= count
                ? [.. _swings]
                : _swings.GetRange(_swings.Count - count, count);
    }
}
