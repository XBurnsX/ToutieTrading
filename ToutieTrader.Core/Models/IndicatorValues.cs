namespace ToutieTrader.Core.Models;

/// <summary>
/// Valeurs de tous les indicateurs calculés par IndicatorEngine pour une bougie donnée.
/// La Strategy lit ces valeurs — elle ne recalcule jamais.
/// </summary>
public sealed class IndicatorValues
{
    // ── Ichimoku ──────────────────────────────────────────────────────────────
    public double Tenkan    { get; init; }   // Tenkan-sen (conversion line)
    public double Kijun     { get; init; }   // Kijun-sen  (base line)
    public double SenkouA   { get; init; }   // Senkou Span A (bougie courante)
    public double SenkouB   { get; init; }   // Senkou Span B (bougie courante)
    public double SenkouA26 { get; init; }   // Senkou Span A projeté +26 (cloud futur)
    public double SenkouB26 { get; init; }   // Senkou Span B projeté +26 (cloud futur)
    public double Chikou    { get; init; }   // Chikou Span (lagging span)

    // ── MACD ──────────────────────────────────────────────────────────────────
    public double MacdLine   { get; init; }
    public double SignalLine { get; init; }
    public double Histogram  { get; init; }

    // ── EMA ───────────────────────────────────────────────────────────────────
    public double Ema50  { get; init; }
    public double Ema200 { get; init; }

    // ── Prix bougie courante ───────────────────────────────────────────────────
    public double Close { get; init; }
    public double Open  { get; init; }
    public double High  { get; init; }
    public double Low   { get; init; }
}
