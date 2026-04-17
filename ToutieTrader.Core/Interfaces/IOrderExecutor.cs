using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Interfaces;

/// <summary>
/// Exécuteur d'ordres abstrait — même interface pour Live (MT5) et Replay (simulation).
/// ExecutionManager ne sait pas lequel est utilisé.
/// </summary>
public interface IOrderExecutor
{
    /// <summary>Envoie un ordre. Retourne (ticketId, fillPrice, fillTime).</summary>
    Task<(long TicketId, double FillPrice, DateTimeOffset FillTime)>
        SendOrderAsync(TradeSignal signal, CancellationToken ct);

    /// <summary>Ferme un ordre. Retourne (closePrice, closeTime).</summary>
    Task<(double ClosePrice, DateTimeOffset CloseTime)>
        CloseOrderAsync(long ticketId, string symbol, CancellationToken ct);

    /// <summary>Modifie le stop loss d'une position ouverte. Retourne le SL accepte.</summary>
    Task<double> ModifyStopLossAsync(long ticketId, string symbol, double stopLoss, CancellationToken ct);

    bool IsReplay { get; }
}
