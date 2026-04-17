namespace ToutieTrader.Core.Models;

public enum StopLossType
{
    AboveCloud,  // SL au-dessus du cloud Ichimoku (pour SHORT)
    BelowCloud,  // SL en-dessous du cloud Ichimoku (pour LONG)
    SwingHigh,   // SL au dernier swing high
    SwingLow,    // SL au dernier swing low
    Fixed,       // SL en pips fixes
    Custom       // SL calculé par la lambda CustomCompute de la Strategy
}

/// <summary>
/// Règle de stop-loss déclarée par la Strategy.
/// Le calcul est fait par StrategyRunner — jamais dans la Strategy.
/// </summary>
public sealed class StopLossRule
{
    public StopLossType Type       { get; init; }
    public double       BufferPips { get; init; }  // Pips de marge ajoutés au SL calculé

    /// <summary>
    /// Calcul custom du SL. Requis si Type == Custom, ignoré sinon.
    /// Paramètres : (valeurs indicateurs, direction "BUY"|"SELL") → prix SL absolu.
    /// </summary>
    public Func<IndicatorValues, string, double>? CustomCompute { get; init; }
}
