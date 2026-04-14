using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine.Events;

public sealed record IndicatorsUpdatedEvent(string Symbol, string Timeframe, IndicatorValues Values);
