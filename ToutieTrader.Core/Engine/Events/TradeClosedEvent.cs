using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine.Events;

public sealed record TradeClosedEvent(TradeRecord Trade);
