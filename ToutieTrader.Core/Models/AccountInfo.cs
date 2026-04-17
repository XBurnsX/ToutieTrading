namespace ToutieTrader.Core.Models;

/// <summary>
/// Informations du compte MT5 retournées par GET /account.
/// </summary>
public sealed class AccountInfo
{
    public double DrawdownPercent { get; init; }
    public double Balance         { get; init; }
    public double Equity          { get; init; }
    public string Currency        { get; init; } = string.Empty;
    public double Profit          { get; init; }
    public double Margin          { get; init; }
    public double FreeMargin      { get; init; }
    public double MarginLevel     { get; init; }
    public long   Login           { get; init; }
    public string Server          { get; init; } = string.Empty;
}
