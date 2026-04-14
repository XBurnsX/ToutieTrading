namespace ToutieTrader.Core.Models;

/// <summary>
/// Une bougie OHLCV. Time = heure Québec offset-aware (America/Toronto).
/// </summary>
public sealed class Candle
{
    public string         Symbol    { get; init; } = string.Empty;
    public string         Timeframe { get; init; } = string.Empty;
    public DateTimeOffset Time      { get; init; }
    public double         Open      { get; init; }
    public double         High      { get; init; }
    public double         Low       { get; init; }
    public double         Close     { get; init; }
    public long           Volume    { get; init; }
}
