namespace ToutieTrader.Core.Models;

public enum TakeProfitType
{
    RiskRatio,  // TP = distance SL × Ratio (ex: Ratio=2 → RR 1:2)
    Fixed       // TP en pips fixes
}

/// <summary>
/// Règle de take-profit déclarée par la Strategy.
/// Le calcul est fait par RiskEngine — jamais dans la Strategy.
/// </summary>
public sealed class TakeProfitRule
{
    public TakeProfitType Type  { get; init; }
    public double         Ratio { get; init; }  // Ratio RR si RiskRatio, pips si Fixed
}
