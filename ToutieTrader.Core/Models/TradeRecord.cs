namespace ToutieTrader.Core.Models;

/// <summary>
/// Enregistrement complet d'un trade — schéma exact de DuckDB #2 et DB Replay.
/// Toutes les DateTimeOffset = heure Québec offset-aware (America/Toronto).
/// Nullable = non encore rempli (trade en cours ou rejeté).
/// </summary>
public sealed class TradeRecord
{
    public Guid    Id               { get; init; } = Guid.NewGuid();
    public string  Symbol           { get; set; }  = string.Empty;
    public string  StrategyName     { get; set; }  = string.Empty;
    public string  StrategySettings { get; set; }  = "{}";   // JSON snapshot des Settings au moment du trade
    public string  Direction        { get; set; }  = string.Empty;  // "BUY" | "SELL"

    public DateTimeOffset? EntryTime   { get; set; }
    public double?         EntryPrice  { get; set; }
    public double?         Sl          { get; set; }
    public double?         Tp          { get; set; }

    public DateTimeOffset? ExitTime    { get; set; }
    public double?         ExitPrice   { get; set; }
    public double?         ProfitLoss  { get; set; }
    public double?         RiskDollars { get; set; }
    public double?         LotSize     { get; set; }

    public long?   TicketId      { get; set; }
    public string  CorrelationId { get; set; } = string.Empty;

    /// <summary>"TP" | "SL" | "ForceExit:[label]" | "OptionalExit:[label]"</summary>
    public string? ExitReason    { get; set; }

    /// <summary>JSON array des labels de conditions remplies au moment du signal.</summary>
    public string? ConditionsMet { get; set; }

    /// <summary>null si succès, message d'erreur si ordre rejeté ou exception.</summary>
    public string? ErrorLog      { get; set; }
}
