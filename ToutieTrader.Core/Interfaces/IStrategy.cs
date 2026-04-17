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
///   - Le % de risk est UN setting GLOBAL du bot (SettingsPage) — JAMAIS dans une Strategy.
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
    // Le % de risk est UN setting GLOBAL (SettingsPage), jamais ici.

    /// <summary>Nombre max de trades ouverts simultanément toutes paires confondues.</summary>
    int MaxSimultaneousTrades { get; }

    /// <summary>Nombre max d'entrees par symbole par journee. -1 = illimite.</summary>
    int MaxTradesPerSymbolPerDay => -1;

    /// <summary>Minutes minimum avant de reprendre le meme symbole dans la meme direction. 0 = desactive.</summary>
    int ReentryCooldownMinutes => 0;

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
    /// Le % de risk N'EST PAS un setting de strategy — c'est un setting global du bot.
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
