namespace ToutieTrader.Core.Models;

/// <summary>
/// Signal de trade émis par Strategy.Evaluate() et transmis à RiskEngine → TradeManager.
/// correlation_id = UUID v4 généré par ExecutionManager avant envoi à MT5.
/// </summary>
public sealed record TradeSignal
{
    public string       Direction      { get; init; } = string.Empty;  // "BUY" | "SELL"
    public string       Symbol         { get; init; } = string.Empty;
    public double       Sl             { get; init; }
    public double       Tp             { get; init; }
    public double       EntryPrice     { get; init; }
    public double       LotSize        { get; init; }
    public string       CorrelationId  { get; init; } = string.Empty;
    public List<string> ConditionsMet  { get; init; } = [];           // Labels des conditions vraies
}
