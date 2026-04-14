namespace ToutieTrader.Core.Models;

public enum SwingPointType
{
    HH,  // Higher High
    HL,  // Higher Low
    LH,  // Lower High
    LL   // Lower Low
}

/// <summary>
/// Un point swing détecté par SwingPointEngine sur un TF/symbole donné.
/// Time = heure Québec offset-aware.
/// </summary>
public sealed class SwingPoint
{
    public SwingPointType Type  { get; init; }
    public double         Price { get; init; }
    public DateTimeOffset Time  { get; init; }
}
