namespace ToutieTrader.Core.Models;

/// <summary>
/// Valeurs de tous les indicateurs calcules par IndicatorEngine pour une bougie donnee.
/// La Strategy lit ces valeurs; elle ne recalcule jamais.
/// </summary>
public sealed class IndicatorValues
{
    // Heure de la bougie. Toutes les heures sont en heure Quebec.
    public DateTimeOffset CandleTime { get; init; }

    // Ichimoku
    public double Tenkan    { get; init; }
    public double Kijun     { get; init; }
    public double SenkouA   { get; init; }
    public double SenkouB   { get; init; }
    public double SenkouA26 { get; init; }
    public double SenkouB26 { get; init; }
    public double Chikou    { get; init; }
    public DateTimeOffset ChikouCandleTime { get; init; }

    // MACD
    public double MacdLine   { get; init; }
    public double SignalLine { get; init; }
    public double Histogram  { get; init; }

    // EMA
    public double Ema50  { get; init; }
    public double Ema200 { get; init; }

    // ATR(14)
    public double Atr14 { get; init; }

    // Prix bougie courante
    public double Close { get; init; }
    public double Open  { get; init; }
    public double High  { get; init; }
    public double Low   { get; init; }

    // Bougie precedente [-1]
    public double PrevClose  { get; init; }
    public double PrevOpen   { get; init; }
    public double PrevHigh   { get; init; }
    public double PrevLow    { get; init; }
    public double PrevKijun  { get; init; }
    public double PrevTenkan { get; init; }

    // Historique [-26] pour verification Chikou libre
    public double High26    { get; init; }
    public double Low26     { get; init; }
    public double Kijun26   { get; init; }
    public double Tenkan26  { get; init; }

    // Swing historique pour SL dynamique
    public double Low5  { get; init; }
    public double High5 { get; init; }
    public IReadOnlyList<double> RecentLows  { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> RecentHighs { get; init; } = Array.Empty<double>();

    // Points pivots
    public double PivotPP { get; init; }
    public double PivotR1 { get; init; }
    public double PivotR2 { get; init; }
    public double PivotS1 { get; init; }
    public double PivotS2 { get; init; }
}
