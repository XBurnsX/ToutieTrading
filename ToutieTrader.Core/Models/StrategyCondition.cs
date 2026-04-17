namespace ToutieTrader.Core.Models;

/// <summary>
/// Une condition déclarative d'une Strategy.
/// Évaluée par le moteur à chaque bougie — jamais par la Strategy elle-même.
///
/// Expression reçoit les valeurs calculées par IndicatorEngine et TrendEngine,
/// retourne true si la condition est remplie.
/// </summary>
public sealed class StrategyCondition
{
    /// <summary>Affiché dans le tooltip hover chart et le popup Trade.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Timeframe sur lequel évaluer cette condition (ex: "H1", "M15").</summary>
    public string Timeframe { get; init; } = string.Empty;

    /// <summary>
    /// Lambda d'évaluation.
    /// Paramètres : (valeurs indicateurs, état tendance) → bool.
    /// Les champs PrevClose/PrevKijun/High26 etc. de IndicatorValues donnent accès
    /// aux bougies précédentes sans nécessiter un état privé dans la Strategy.
    /// </summary>
    public Func<IndicatorValues, TrendState, bool> Expression { get; init; } = (_, _) => false;

    /// <summary>
    /// Variante pour les sorties qui ont besoin du trade ouvert (entry, SL, direction).
    /// Si null, le moteur utilise Expression. La logique reste déclarée par la Strategy ;
    /// le moteur ne fait qu'évaluer la condition fournie.
    /// </summary>
    public Func<IndicatorValues, TrendState, TradeRecord, bool>? TradeExpression { get; init; } = null;

    /// <summary>
    /// Direction à laquelle cette condition s'applique.
    /// null = toutes directions | "BUY" = long seulement | "SELL" = short seulement.
    /// Utilisé principalement dans ForceExitConditions et OptionalExitConditions
    /// pour éviter qu'une condition de sortie BUY ne ferme une position SELL (et vice-versa).
    /// </summary>
    public string? ApplicableDirection { get; init; } = null;
}
