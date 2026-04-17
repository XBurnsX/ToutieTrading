using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Calcule le lot size et vérifie les limites de risk.
/// Zéro calcul de risk dans la Strategy — tout passe ici.
///
/// Formule UNIVERSELLE (FX, indices, métaux, crypto — tout MT5) :
///   risk_dollars   = capital × (risk_percent / 100)
///   moneyPerLot    = (slDistance / TradeTickSize) × TradeTickValue   ← 1 lot, perte au SL
///   lot_size       = risk_dollars / moneyPerLot
///   lot_size       = NormalizeVolume(lot_size)                        ← clamp + step rounding
///
/// Aucun pip hardcodé. Toutes les métadonnées viennent de SymbolMeta (mt5.symbol_info()).
/// Le % de risk vient TOUJOURS de SettingsPage (GLOBAL) — JAMAIS d'une Strategy.
/// </summary>
public sealed class RiskEngine
{
    /// <summary>
    /// Calcule lot_size et risk_dollars en dollars du compte.
    /// Retourne null si :
    ///   - MaxSimultaneousTrades dépassé
    ///   - MaxDailyDrawdown dépassé
    ///   - SL distance nulle ou meta invalide
    ///   - lot calculé < volume_min du broker (capital trop petit pour ce SL)
    /// </summary>
    public RiskResult? Calculate(
        double     capital,
        double     riskPercent,            // setting GLOBAL — vient de SettingsPage
        double     entryPrice,
        double     slPrice,
        SymbolMeta meta,
        IStrategy  strategy,
        int        openTradesCount,
        double     dailyDrawdownPercent,
        double     estimatedRoundTripFeesPerLot = 0.0)
    {
        // Vérifications stratégie
        if (openTradesCount >= strategy.MaxSimultaneousTrades) return null;
        if (dailyDrawdownPercent >= (double)strategy.MaxDailyDrawdownPercent) return null;

        if (meta.TradeTickSize <= 0 || meta.TradeTickValue <= 0) return null;

        double slDistance = Math.Abs(entryPrice - slPrice);
        if (slDistance <= 0) return null;

        double riskDollars = capital * (riskPercent / 100.0);
        if (riskDollars <= 0) return null;

        // Valeur monétaire d'une perte au SL pour 1 lot (universel : FX, indices, métaux…)
        double moneyPerLotAtSl = meta.MoneyPerLot(slDistance) + Math.Max(0, estimatedRoundTripFeesPerLot);
        if (moneyPerLotAtSl <= 0) return null;

        double rawLot = riskDollars / moneyPerLotAtSl;

        // Si le capital est trop petit pour respecter volume_min sans excéder le risk
        // demandé, on REFUSE le trade (jamais clamper vers le haut au volume_min, ce qui
        // exploserait le risk réel — bug historique).
        if (rawLot < meta.VolumeMin)
            return null;

        // Inverse : si le risk demandé exige PLUS que volume_max, on REFUSE aussi.
        // Sinon NormalizeVolume clampe silencieusement à volume_max et les commissions
        // (calculées sur lotSize réel) explosent — typique sur indices cross-currency
        // (JP225 par ex.) où chaque lot génère très peu de $/point.
        if (rawLot > meta.VolumeMax)
            return null;

        double lotSize = meta.NormalizeVolume(rawLot);
        if (lotSize <= 0) return null;

        double actualRiskDollars = moneyPerLotAtSl * lotSize;

        return new RiskResult
        {
            LotSize     = lotSize,
            RiskDollars = Math.Round(actualRiskDollars, 2),
        };
    }
}

public sealed class RiskResult
{
    public double LotSize     { get; init; }
    public double RiskDollars { get; init; }
}
