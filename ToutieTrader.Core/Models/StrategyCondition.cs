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
    /// Bougie précédente = champ privé dans la Strategy si nécessaire.
    /// </summary>
    public Func<IndicatorValues, TrendState, bool> Expression { get; init; } = (_, _) => false;
}
