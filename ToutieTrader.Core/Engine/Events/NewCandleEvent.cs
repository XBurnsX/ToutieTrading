using ToutieTrader.Core.Models;

namespace ToutieTrader.Core.Engine.Events;

public sealed record NewCandleEvent(Candle Candle);
