namespace ToutieTrader.Core.Models;

public enum TrendDirection
{
    Bull,
    Bear,
    Range
}

/// <summary>
/// Résultat du TrendEngine pour un symbole/TF donné.
/// Calculé par TrendEngine uniquement — jamais modifiable par une Strategy.
/// </summary>
public sealed class TrendState
{
    public string         Timeframe { get; init; } = string.Empty;
    public TrendDirection Trend     { get; init; }
}
