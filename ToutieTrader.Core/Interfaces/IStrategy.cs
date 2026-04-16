using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Interfaces;

/// <summary>
/// Contrat verrouillé de toutes les Strategies.
///
/// Règles absolues :
///   - Zéro méthode ShouldExit() — toutes les sorties via listes déclaratives.
///   - La Strategy DÉCLARE — le moteur ÉVALUE. Jamais l'inverse.
///   - Une Strategy NE PEUT PAS demander des indicateurs non fournis par IndicatorEngine.
///   - Une Strategy NE PEUT PAS modifier la logique TrendEngine (Bull/Bear/Range).
///   - Settings types valides : bool | decimal | int uniquement.
///   - Settings["RiskPercent"] surcharge RiskPercent si présent.
/// </summary>
public interface IStrategy
{
    // ── Identité ──────────────────────────────────────────────────────────────

    /// <summary>Nom affiché dans le dropdown Strategy de l'UI.</summary>
    string Name { get; }

    /// <summary>Timeframe principal affiché dans le chart.</summary>
    string Timeframe { get; }

    /// <summary>Tous les TF que le moteur doit calculer pour cette Strategy.</summary>
    List<string> RequiredTimeframes { get; }

    /// <summary>
    /// Indicateurs affichés sur le chart.
    /// Valeurs valides : "Ichimoku" | "MACD" | "EMA50" | "EMA200"
    /// </summary>
    List<string> Indicators { get; }

    // ── Risk ──────────────────────────────────────────────────────────────────

    /// <summary>% du capital risqué par trade. Surchargeable par Settings["RiskPercent"].</summary>
    decimal RiskPercent { get; }

    /// <summary>Nombre max de trades ouverts simultanément toutes paires confondues.</summary>
    int MaxSimultaneousTrades { get; }

    /// <summary>Le bot stoppe si le drawdown dépasse ce % dans la journée.</summary>
    decimal MaxDailyDrawdownPercent { get; }

    // ── SL / TP ───────────────────────────────────────────────────────────────

    StopLossRule   StopLoss   { get; }
    TakeProfitRule TakeProfit { get; }

    // ── Conditions d'entrée ───────────────────────────────────────────────────

    /// <summary>TOUTES doivent être vraies pour entrer LONG.</summary>
    List<StrategyCondition> LongConditions { get; }

    /// <summary>TOUTES doivent être vraies pour entrer SHORT.</summary>
    List<StrategyCondition> ShortConditions { get; }

    // ── Conditions de sortie ──────────────────────────────────────────────────

    /// <summary>UNE seule vraie → fermeture immédiate. exit_reason = "ForceExit:[label]".</summary>
    List<StrategyCondition> ForceExitConditions { get; }

    /// <summary>
    /// UNE seule vraie + activée dans Settings → fermeture.
    /// exit_reason = "OptionalExit:[label]".
    /// </summary>
    List<StrategyCondition> OptionalExitConditions { get; }

    // ── Settings UI ───────────────────────────────────────────────────────────

    /// <summary>
    /// Options configurables depuis la page Strategy de l'UI.
    /// Types valides : bool | decimal | int | string.
    /// Si string → déclarer les options valides dans SettingChoices pour obtenir un dropdown.
    /// Settings["RiskPercent"] surcharge RiskPercent si présent.
    /// </summary>
    Dictionary<string, object> Settings { get; }

    /// <summary>
    /// Options valides pour les settings de type string.
    /// Clé = nom du setting, valeur = tableau des options.
    /// Si présent → ComboBox dans l'UI. Si absent → TextBox.
    /// Implémentation par défaut = aucun dropdown.
    /// </summary>
    Dictionary<string, string[]> SettingChoices => new();

    /// <summary>
    /// Regroupe les settings en sections dans la page Strategy.
    /// Clé = titre de section, valeur = clés des settings dans l'ordre voulu.
    /// Les settings non listés ici apparaissent sans section à la fin.
    /// Implémentation par défaut = affichage plat sans sections.
    /// </summary>
    Dictionary<string, string[]> SettingSections => new();
}
