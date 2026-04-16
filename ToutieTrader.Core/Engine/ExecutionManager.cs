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
/// </summary>
public sealed class ExecutionManager
{
    private readonly EventBus       _bus;
    private readonly IOrderExecutor _executor;
    private readonly RiskEngine     _risk;

    // Sauvegarde des trades : délégué injecté depuis l'extérieur (évite dépendance sur Data)
    private readonly Func<TradeRecord, Task> _saveTrade;

    // Suivi des correlation_id déjà envoyés (anti double-envoi)
    private readonly HashSet<string> _sentCorrelationIds = new();

    public ExecutionManager(
        EventBus       bus,
        IOrderExecutor executor,
        RiskEngine     risk,
        Func<TradeRecord, Task> saveTrade)
    {
        _bus       = bus;
        _executor  = executor;
        _risk      = risk;
        _saveTrade = saveTrade;
    }

    // ─── Entrée ───────────────────────────────────────────────────────────────

    public async Task<TradeRecord?> ExecuteEntryAsync(
        TradeSignal signal,
        IStrategy   strategy,
        DateTimeOffset candleTime,
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
        CancellationToken ct)
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
            else
            {
                // Replay / simulation : prix exact du SL, TP, ou close de bougie
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
        record.ExitReason = exitReason;
        record.ProfitLoss = CalculatePnl(record, closePrice);

        await _saveTrade(record);
        _bus.Publish(new TradeClosedEvent(record));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static double CalculatePnl(TradeRecord record, double closePrice)
    {
        if (record.EntryPrice is null || record.LotSize is null)
            return 0;

        double priceDiff = record.Direction == "BUY"
            ? closePrice - record.EntryPrice.Value
            : record.EntryPrice.Value - closePrice;

        // Approximation V1 : 1 lot = 100 000 unités, valeur en USD
        return Math.Round(priceDiff * record.LotSize.Value * 100_000, 2);
    }
}
