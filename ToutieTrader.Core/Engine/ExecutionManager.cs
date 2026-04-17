using System.Text.Json;
using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Exécute les ordres via IOrderExecutor (Live → MT5 | Replay → simulation).
/// Génère les correlation_id UUID v4.
/// Anti double-envoi : correlation_id déjà envoyé → ignoré.
/// Partial fills non supportés. Rejet → loggé, pas de retry.
///
/// P&L universel via SymbolMeta (FX, indices, métaux, crypto) :
///   priceDiff_$ = (priceDiff / TradeTickSize) × TradeTickValue × LotSize
///   commission  = LotSize × CommissionPerLotPerSide × 2  (entrée + sortie)
///   pnl         = priceDiff_$ − commission
/// </summary>
public sealed class ExecutionManager
{
    private readonly EventBus       _bus;
    private readonly IOrderExecutor _executor;
    private readonly RiskEngine     _risk;
    private readonly Func<string, SymbolMeta?> _metaProvider;
    private readonly double _commissionPerLotPerSide;

    // Sauvegarde des trades : délégué injecté depuis l'extérieur (évite dépendance sur Data)
    private readonly Func<TradeRecord, Task> _saveTrade;

    // Suivi des correlation_id déjà envoyés (anti double-envoi)
    private readonly HashSet<string> _sentCorrelationIds = new();

    public ExecutionManager(
        EventBus       bus,
        IOrderExecutor executor,
        RiskEngine     risk,
        Func<TradeRecord, Task> saveTrade,
        Func<string, SymbolMeta?> metaProvider,
        double commissionPerLotPerSide)
    {
        _bus       = bus;
        _executor  = executor;
        _risk      = risk;
        _saveTrade = saveTrade;
        _metaProvider = metaProvider;
        _commissionPerLotPerSide = commissionPerLotPerSide;
    }

    // ─── Entrée ───────────────────────────────────────────────────────────────

    public async Task<TradeRecord?> ExecuteEntryAsync(
        TradeSignal signal,
        IStrategy   strategy,
        DateTimeOffset candleTime,
        double      spreadAtEntry,   // spread en prix au moment de l'entrée (Ask - Bid)
        CancellationToken ct)
    {
        // Anti double-envoi
        if (_sentCorrelationIds.Contains(signal.CorrelationId))
            return null;

        _sentCorrelationIds.Add(signal.CorrelationId);

        // Snapshot Settings pour DB
        string settingsJson = JsonSerializer.Serialize(
            strategy.Settings.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? ""));

        var record = new TradeRecord
        {
            Symbol           = signal.Symbol,
            StrategyName     = strategy.Name,
            StrategySettings = settingsJson,
            Direction        = signal.Direction,
            Sl               = signal.Sl,
            Tp               = signal.Tp,
            TpReason         = signal.TpReason,
            RiskDollars      = signal.RiskDollars > 0 ? signal.RiskDollars : null,
            CorrelationId    = signal.CorrelationId,
            ConditionsMet    = JsonSerializer.Serialize(signal.ConditionsMet),
        };

        try
        {
            var (ticketId, fillPrice, fillTime) =
                await _executor.SendOrderAsync(signal, ct);

            record.TicketId   = ticketId;
            record.EntryPrice = fillPrice;
            // Replay : candleTime = Open de la vraie bougie d'entrée (déjà timezone-aware)
            // Live   : fillTime   = heure du fill broker, convertie en heure Québec
            record.EntryTime  = _executor.IsReplay ? candleTime : TimeZoneHelper.ToQuebec(fillTime);
            record.LotSize    = signal.LotSize;

            // Coût du spread à l'entrée — converti en $ via SymbolMeta universel.
            // (priceDiff_$ = (priceDiff / TickSize) × TickValue × LotSize)
            // On stocke seulement la partie spread ici ; la commission s'ajoute à la sortie.
            var meta = _metaProvider(record.Symbol);
            if (meta is not null && meta.TradeTickSize > 0 && meta.TradeTickValue > 0
                && spreadAtEntry > 0 && signal.LotSize > 0)
            {
                double spreadCost = meta.MoneyPerLot(spreadAtEntry) * signal.LotSize;
                record.Fees = Math.Round(spreadCost, 2);
            }
            else
            {
                record.Fees = 0;
            }
        }
        catch (Exception ex)
        {
            record.ErrorLog = ex.Message;
            await _saveTrade(record);
            return null;
        }

        await _saveTrade(record);
        _bus.Publish(new TradeSignalEvent(signal));
        return record;
    }

    // ─── Sortie ───────────────────────────────────────────────────────────────

    public async Task ExecuteExitAsync(
        TradeRecord record,
        Candle      candle,
        string      exitReason,
        CancellationToken ct,
        double?         explicitClosePrice = null,
        DateTimeOffset? explicitCloseTime  = null)
    {
        double closePrice;
        DateTimeOffset closeTime;

        try
        {
            if (record.TicketId.HasValue && !_executor.IsReplay)
            {
                // Live : fermeture réelle via le broker
                (closePrice, closeTime) =
                    await _executor.CloseOrderAsync(record.TicketId.Value, record.Symbol, ct);
            }
            else if (explicitClosePrice.HasValue)
            {
                // Replay Mode Tick : prix précis du tick qui a déclenché SL/TP
                closePrice = explicitClosePrice.Value;
                closeTime  = explicitCloseTime ?? candle.Time;
            }
            else
            {
                // Replay / simulation candle-based : prix exact du SL, TP, ou close de bougie
                closePrice = exitReason == "SL" ? record.Sl!.Value
                           : exitReason == "TP" ? record.Tp!.Value
                           : candle.Close;
                closeTime = candle.Time;
            }
        }
        catch (Exception ex)
        {
            record.ErrorLog = ex.Message;
            await _saveTrade(record);
            return;
        }

        record.ExitTime   = TimeZoneHelper.ToQuebec(closeTime);
        record.ExitPrice  = closePrice;
        record.ExitReason = exitReason == "TP" && !string.IsNullOrWhiteSpace(record.TpReason)
            ? record.TpReason
            : FormatExitReason(exitReason);
        record.ProfitLoss = CalculatePnl(record, closePrice);

        // Frais totaux $ = spread déjà stocké à l'entrée + commission round-trip.
        // Commission appliquée UNIQUEMENT sur les symboles facturés (FX + Métaux chez IC Markets).
        // Indices / énergies / crypto = spread-only, pas de commission style ECN.
        if (record.LotSize.HasValue && _commissionPerLotPerSide > 0)
        {
            var meta = _metaProvider(record.Symbol);
            if (meta is not null && meta.ChargesCommission)
            {
                double commission = record.LotSize.Value * _commissionPerLotPerSide * 2.0;
                record.Fees = Math.Round((record.Fees ?? 0) + commission, 2);
            }
        }

        await _saveTrade(record);
        _bus.Publish(new TradeClosedEvent(record));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// P&L universel : (priceDiff / tickSize) × tickValue × lot − commission round-trip.
    /// Si la meta du symbole n'est pas disponible (cas de fallback), tombe à 0
    /// plutôt que d'inventer un prix → mieux qu'un nombre faux.
    /// </summary>
    public async Task<bool> ModifyStopLossAsync(
        TradeRecord record,
        double newStopLoss,
        CancellationToken ct)
    {
        if (!record.TicketId.HasValue && !_executor.IsReplay)
            return false;

        try
        {
            double acceptedSl = newStopLoss;
            if (record.TicketId.HasValue && !_executor.IsReplay)
                acceptedSl = await _executor.ModifyStopLossAsync(
                    record.TicketId.Value,
                    record.Symbol,
                    newStopLoss,
                    ct).ConfigureAwait(false);

            record.Sl = acceptedSl;
            await _saveTrade(record).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            record.ErrorLog = $"Modify SL failed: {ex.Message}";
            await _saveTrade(record).ConfigureAwait(false);
            return false;
        }
    }

    private double CalculatePnl(TradeRecord record, double closePrice)
    {
        if (record.EntryPrice is null || record.LotSize is null)
            return 0;

        var meta = _metaProvider(record.Symbol);
        if (meta is null || meta.TradeTickSize <= 0 || meta.TradeTickValue <= 0)
            return 0;

        double priceDiff = record.Direction == "BUY"
            ? closePrice - record.EntryPrice.Value
            : record.EntryPrice.Value - closePrice;

        double gross      = meta.MoneyPerLot(priceDiff) * record.LotSize.Value;
        double commission = meta.ChargesCommission
            ? record.LotSize.Value * _commissionPerLotPerSide * 2.0
            : 0.0;

        return Math.Round(gross - commission, 2);
    }

    private static string FormatExitReason(string exitReason)
        => exitReason switch
        {
            "SL" => "SL",
            "TP" => "TP",
            "ForceExit:Kijun reverse" => "Sortie Kijun reverse",
            _ when exitReason.StartsWith("ForceExit:", StringComparison.OrdinalIgnoreCase)
                => "Sortie forcee - " + exitReason["ForceExit:".Length..],
            _ when exitReason.StartsWith("OptionalExit:", StringComparison.OrdinalIgnoreCase)
                => "Sortie optionnelle - " + exitReason["OptionalExit:".Length..],
            _ => exitReason,
        };
}
