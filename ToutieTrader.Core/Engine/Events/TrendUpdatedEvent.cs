using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine.Events;

public sealed record TrendUpdatedEvent(string Symbol, string Timeframe, TrendState State);
