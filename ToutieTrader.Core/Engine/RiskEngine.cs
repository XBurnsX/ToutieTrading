using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Calcule le lot size et vérifie les limites de risk.
/// Zéro calcul de risk dans la Strategy — tout passe ici.
///
/// Formule :
///   risk_dollars = capital × (risk_percent / 100)
///   lot_size     = risk_dollars / (sl_pips × pip_value_per_lot)
///
/// pip_value_per_lot = 10 USD pour les paires XXX/USD (approximation V1).
/// </summary>
public sealed class RiskEngine
{
    private const double PipValuePerLot = 10.0;   // $10 / pip / lot standard (approximation V1)
    private const double MinLotSize     = 0.01;
    private const double MaxLotSize     = 100.0;

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>
    /// Calcule lot_size et risk_dollars.
    /// Retourne null si MaxSimultaneousTrades ou MaxDailyDrawdownPercent dépassés.
    /// </summary>
    public RiskResult? Calculate(
        double     capital,
        double     riskPercent,
        double     entryPrice,
        double     slPrice,
        string     symbol,
        IStrategy  strategy,
        int        openTradesCount,
        double     dailyDrawdownPercent)
    {
        // Vérification MaxSimultaneousTrades
        if (openTradesCount >= strategy.MaxSimultaneousTrades)
            return null;

        // Vérification MaxDailyDrawdown
        if (dailyDrawdownPercent >= (double)strategy.MaxDailyDrawdownPercent)
            return null;

        // RiskPercent : Settings["RiskPercent"] surcharge la propriété si présent
        double effectiveRiskPct = riskPercent;
        if (strategy.Settings.TryGetValue("RiskPercent", out var settingRisk))
            effectiveRiskPct = Convert.ToDouble(settingRisk);

        double riskDollars = capital * (effectiveRiskPct / 100.0);

        // Distance SL en pips
        double pipSize = GetPipSize(symbol);
        double slPips  = Math.Abs(entryPrice - slPrice) / pipSize;

        if (slPips <= 0) return null;

        double lotSize = riskDollars / (slPips * PipValuePerLot);
        lotSize = Math.Round(lotSize, 2);
        lotSize = Math.Clamp(lotSize, MinLotSize, MaxLotSize);

        return new RiskResult
        {
            LotSize     = lotSize,
            RiskDollars = Math.Round(riskDollars, 2),
        };
    }

    // ─── Pip size par symbole ─────────────────────────────────────────────────

    /// <summary>
    /// Taille d'un pip selon le symbole.
    /// JPY pairs = 0.01, tout le reste = 0.0001.
    /// </summary>
    private static double GetPipSize(string symbol)
        => symbol.Contains("JPY", StringComparison.OrdinalIgnoreCase) ? 0.01 : 0.0001;
}

public sealed class RiskResult
{
    public double LotSize     { get; init; }
    public double RiskDollars { get; init; }
}
