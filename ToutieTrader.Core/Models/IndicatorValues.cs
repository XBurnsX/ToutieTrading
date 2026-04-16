namespace ToutieTrader.Core.Models;

/// <summary>
/// Valeurs de tous les indicateurs calculés par IndicatorEngine pour une bougie donnée.
/// La Strategy lit ces valeurs — elle ne recalcule jamais.
/// </summary>
public sealed class IndicatorValues
{
    // ── Heure de la bougie ────────────────────────────────────────────────────
    public DateTimeOffset CandleTime { get; init; }   // Pour les filtres horaires

    // ── Ichimoku ──────────────────────────────────────────────────────────────
    public double Tenkan    { get; init; }   // Tenkan-sen (conversion line)
    public double Kijun     { get; init; }   // Kijun-sen  (base line)
    public double SenkouA   { get; init; }   // Senkou Span A (bougie courante)
    public double SenkouB   { get; init; }   // Senkou Span B (bougie courante)
    public double SenkouA26 { get; init; }   // Senkou Span A projeté +26 (cloud futur)
    public double SenkouB26 { get; init; }   // Senkou Span B projeté +26 (cloud futur)
    public double Chikou    { get; init; }   // Chikou Span = Close actuel (tracé -26 sur chart)
    public DateTimeOffset ChikouCandleTime { get; init; }   // Timestamp réel de la bougie à -26 (pour éviter les gaps weekend)

    // ── MACD ──────────────────────────────────────────────────────────────────
    public double MacdLine   { get; init; }
    public double SignalLine { get; init; }
    public double Histogram  { get; init; }

    // ── EMA ───────────────────────────────────────────────────────────────────
    public double Ema50  { get; init; }
    public double Ema200 { get; init; }

    // ── ATR(14) ───────────────────────────────────────────────────────────────
    public double Atr14 { get; init; }

    // ── Prix bougie courante ───────────────────────────────────────────────────
    public double Close { get; init; }
    public double Open  { get; init; }
    public double High  { get; init; }
    public double Low   { get; init; }

    // ── Bougie précédente [-1] (pour cassures, pentes) ────────────────────────
    public double PrevClose  { get; init; }
    public double PrevOpen   { get; init; }
    public double PrevHigh   { get; init; }
    public double PrevLow    { get; init; }
    public double PrevKijun  { get; init; }
    public double PrevTenkan { get; init; }

    // ── Historique [-26] pour vérification Chikou libre ───────────────────────
    public double High26    { get; init; }   // High de la bougie à -26 périodes
    public double Low26     { get; init; }   // Low  de la bougie à -26 périodes
    public double Kijun26   { get; init; }   // Kijun à -26 périodes
    public double Tenkan26  { get; init; }   // Tenkan à -26 périodes

    // ── Points Pivots journaliers ─────────────────────────────────────────────
    public double PivotPP { get; init; }   // PP  = (H+L+C) / 3
    public double PivotR1 { get; init; }   // R1  = 2×PP − L
    public double PivotR2 { get; init; }   // R2  = PP + (H−L)
    public double PivotS1 { get; init; }   // S1  = 2×PP − H
    public double PivotS2 { get; init; }   // S2  = PP − (H−L)
}
