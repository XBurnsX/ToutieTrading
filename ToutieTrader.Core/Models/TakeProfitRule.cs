namespace ToutieTrader.Core.Models;

public enum TakeProfitType
{
    RiskRatio,  // TP = distance SL × Ratio (ex: Ratio=2 → RR 1:2)
    Fixed,      // TP en pips fixes
    Custom      // TP calculé par la lambda CustomCompute de la Strategy
}

/// <summary>
/// Règle de take-profit déclarée par la Strategy.
/// Le calcul est fait par StrategyRunner — jamais dans la Strategy.
///
/// Pour un TP non couvert par les types prédéfinis, utiliser Type = Custom
/// et fournir une lambda CustomCompute(iv, direction, entryPrice, sl) → prix TP.
/// Aucune modification du Core requise.
/// </summary>
public sealed class TakeProfitRule
{
    public TakeProfitType Type  { get; init; }
    public double         Ratio { get; init; }  // Ratio RR si RiskRatio, pips si Fixed

    /// <summary>
    /// Calcul custom du TP. Requis si Type == Custom, ignoré sinon.
    /// Paramètres : (valeurs indicateurs, direction "BUY"|"SELL", entryPrice, sl) → prix TP absolu.
    /// </summary>
    public Func<IndicatorValues, string, double, double, double>? CustomCompute { get; init; }
}
