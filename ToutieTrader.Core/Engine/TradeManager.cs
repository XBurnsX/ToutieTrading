using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Gère le cycle de vie des trades ouverts.
///
/// Règles absolues :
///   - Entrée = open de la bougie SUIVANT le signal (jamais sur la bougie du signal)
///   - SL et TP vérifiés à chaque bougie
///   - ForceExitConditions → UNE vraie = ferme immédiatement
///   - OptionalExitConditions → UNE vraie + activée dans Settings = ferme
///   - Une position par paire par direction. Zéro hedging.
/// </summary>
public sealed class TradeManager
{
    private readonly EventBus        _bus;
    private readonly IndicatorEngine _indicators;
    private readonly TrendEngine     _trend;
    private readonly ExecutionManager _execution;
    private readonly RiskEngine _risk;
    private readonly Func<string, SymbolMeta?> _metaProvider;
    private readonly double _commissionPerLotPerSide;

    // Signal en attente → sera exécuté à l'open de la prochaine bougie
    private readonly Dictionary<string, (TradeSignal Signal, IStrategy Strategy)> _pendingSignals = new();

    // Trades ouverts en mémoire : correlationId → (record, strategy)
    private readonly Dictionary<string, (TradeRecord Record, IStrategy Strategy)> _openTrades = new();
    private readonly Dictionary<(string Symbol, DateOnly Day), int> _entriesBySymbolDay = new();
    private readonly Dictionary<(string Symbol, string Direction), DateTimeOffset> _lastEntryBySymbolDirection = new();
    private readonly Dictionary<(string Symbol, string Direction), DateTimeOffset> _lastExitBySymbolDirection = new();
    private readonly HashSet<string> _countedEntryIds = new(StringComparer.OrdinalIgnoreCase);

    public TradeManager(
        EventBus bus,
        IndicatorEngine indicators,
        TrendEngine trend,
        ExecutionManager execution,
        RiskEngine risk,
        Func<string, SymbolMeta?> metaProvider,
        double commissionPerLotPerSide)
    {
        _bus        = bus;
        _indicators = indicators;
        _trend      = trend;
        _execution  = execution;
        _risk       = risk;
        _metaProvider = metaProvider;
        _commissionPerLotPerSide = commissionPerLotPerSide;
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>Appelé quand une Strategy émet un signal. Mis en attente jusqu'à la prochaine bougie.</summary>
    public void QueueSignal(TradeSignal signal, IStrategy strategy)
    {
        // Une seule position ouverte total, et un seul signal en attente.
        if (_openTrades.Count > 0 || _pendingSignals.Count > 0)
            return;

        string key = $"{signal.Symbol}:{signal.Direction}";
        _pendingSignals[key] = (signal, strategy);
    }

    /// <summary>
    /// Appelé au début de chaque bougie.
    /// Exécute les signaux en attente (entrée = open de cette bougie).
    /// Puis vérifie SL/TP et conditions de sortie sur les trades ouverts.
    /// </summary>
    public Task ProcessCandleAsync(Candle candle, CancellationToken ct)
        => ProcessCandleAsync(candle, ticks: null, ct);

    /// <summary>
    /// Variante "Mode Tick" pour le Replay : ticks fournis = SL/TP vérifiés tick-par-tick
    /// (précision intra-bougie). Conditions ForceExit/OptionalExit restent évaluées au close
    /// de la bougie (basées sur indicateurs).
    /// Si ticks == null ou vide → fallback candle-based (comportement Live).
    /// </summary>
    public async Task ProcessCandleAsync(Candle candle, IReadOnlyList<Tick>? ticks, CancellationToken ct)
    {
        // 1. Exécuter les signaux en attente sur l'open de cette bougie
        //    Mode Tick : on prend le premier tick (bid/ask réels au moment de l'entrée).
        //    Mode Candle : on utilise candle.Open + spread courant SymbolMeta.
        var firstTick = ticks is { Count: > 0 } ? ticks[0] : null;
        await ExecutePendingSignalsAsync(candle, firstTick, ct);

        // 2. Vérifier les trades ouverts
        if (ticks is { Count: > 0 })
            await CheckOpenTradesTicksAsync(candle, ticks, ct);
        else
            await CheckOpenTradesAsync(candle, ct);
    }

    /// <summary>Reprend les trades ouverts au redémarrage (depuis DB).</summary>
    public void RestoreOpenTrades(List<(TradeRecord Record, IStrategy Strategy)> trades)
    {
        foreach (var (record, strategy) in trades)
        {
            _openTrades[record.CorrelationId] = (record, strategy);
            RegisterEntry(record);
        }
    }

    public void SeedTradeCounts(IEnumerable<TradeRecord> trades)
    {
        foreach (var trade in trades)
        {
            RegisterEntry(trade);
            RegisterExit(trade);
        }
    }

    public List<TradeRecord> GetOpenTrades()
        => _openTrades.Values.Select(v => v.Record).ToList();

    // ─── Exécution des signaux en attente ─────────────────────────────────────

    private async Task ExecutePendingSignalsAsync(Candle candle, Tick? firstTick, CancellationToken ct)
    {
        var toExecute = _pendingSignals
            .Where(kv => kv.Key.StartsWith(candle.Symbol + ":"))
            .ToList();

        foreach (var (key, (signal, strategy)) in toExecute)
        {
            _pendingSignals.Remove(key);

            // Entrée RÉALISTE :
            //   BUY  → on paye l'Ask  (Open + spread/2 fallback)
            //   SELL → on reçoit le Bid (Open − spread/2 fallback)
            // Mode Tick → on a bid/ask exacts du premier tick de la bougie d'entrée.
            // Mode Candle → on applique le spread courant SymbolMeta autour de candle.Open.
            double entryPrice;
            double spreadAtEntry;
            if (firstTick is not null && (firstTick.Bid > 0 || firstTick.Ask > 0))
            {
                entryPrice = signal.Direction == "BUY" ? firstTick.Ask : firstTick.Bid;
                if (entryPrice <= 0) entryPrice = candle.Open;
                spreadAtEntry = (firstTick.Ask > 0 && firstTick.Bid > 0)
                    ? Math.Max(0, firstTick.Ask - firstTick.Bid)
                    : 0;
            }
            else
            {
                var meta = _metaProvider(candle.Symbol);
                double fullSpread = (meta is not null && meta.Spread > 0)
                    ? meta.SpreadPrice
                    : 0.0;
                double halfSpread = fullSpread / 2.0;
                entryPrice = signal.Direction == "BUY"
                    ? candle.Open + halfSpread
                    : candle.Open - halfSpread;
                spreadAtEntry = fullSpread;
            }

            var metaForRisk = _metaProvider(candle.Symbol);
            if (metaForRisk is null)
                continue;

            if (HasReachedMaxTradesPerSymbolDay(candle.Symbol, candle.Time, strategy))
                continue;

            if (IsInReentryCooldown(candle.Symbol, signal.Direction, candle.Time, strategy))
                continue;

            var signalWithEntry = signal with { EntryPrice = entryPrice };
            if (!IsStopLossOnRiskSide(signalWithEntry))
                continue;

            signalWithEntry = EnsureMinimumTakeProfit(signalWithEntry, strategy);

            if (signalWithEntry.RiskCapital > 0 && signalWithEntry.RiskPercent > 0)
            {
                double estimatedFeesPerLot = EstimateRoundTripFeesPerLot(metaForRisk, spreadAtEntry);
                var recalculatedRisk = _risk.Calculate(
                    signalWithEntry.RiskCapital,
                    signalWithEntry.RiskPercent,
                    entryPrice,
                    signalWithEntry.Sl,
                    metaForRisk,
                    strategy,
                    _openTrades.Count,
                    signalWithEntry.DailyDrawdownPercent,
                    estimatedFeesPerLot);

                if (recalculatedRisk is null)
                    continue;

                signalWithEntry = signalWithEntry with
                {
                    LotSize     = recalculatedRisk.LotSize,
                    RiskDollars = recalculatedRisk.RiskDollars,
                };
            }
            else if (signalWithEntry.LotSize <= 0)
            {
                continue;
            }

            var record = await _execution.ExecuteEntryAsync(signalWithEntry, strategy, candle.Time, spreadAtEntry, ct);
            if (record is not null)
            {
                _openTrades[record.CorrelationId] = (record, strategy);
                RegisterEntry(record);
            }
        }
    }

    // ─── Vérification des trades ouverts ──────────────────────────────────────

    private async Task CheckOpenTradesAsync(Candle candle, CancellationToken ct)
    {
        var toClose = new List<(string CorrelationId, string Reason)>();

        foreach (var (corrId, (record, strategy)) in _openTrades)
        {
            if (record.Symbol != candle.Symbol) continue;

            var indicators = _indicators.GetIndicators(candle.Symbol, record.StrategyName != "" ? candle.Timeframe : strategy.Timeframe);
            var trendState = _trend.GetTrend(candle.Symbol, strategy.Timeframe)
                          ?? new TrendState { Timeframe = strategy.Timeframe, Trend = TrendDirection.Range };

            if (indicators is null) continue;

            // ── SL hit ────────────────────────────────────────────────────────
            if (record.Sl.HasValue)
            {
                bool slHit = record.Direction == "BUY"
                    ? candle.Low  <= record.Sl.Value
                    : candle.High >= record.Sl.Value;
                if (slHit) { toClose.Add((corrId, "SL")); continue; }
            }

            // ── TP hit ────────────────────────────────────────────────────────
            if (record.Tp.HasValue)
            {
                bool tpHit = record.Direction == "BUY"
                    ? candle.High >= record.Tp.Value
                    : candle.Low  <= record.Tp.Value;
                if (tpHit) { toClose.Add((corrId, "TP")); continue; }
            }

            // ── ForceExitConditions (UNE vraie = ferme) ───────────────────────
            foreach (var cond in strategy.ForceExitConditions)
            {
                // Ignorer si la condition ne s'applique pas à la direction de ce trade
                if (cond.ApplicableDirection != null && cond.ApplicableDirection != record.Direction)
                    continue;

                var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
                var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trendState;

                if (EvaluateCondition(cond, condIndicators, condTrend, record))
                {
                    toClose.Add((corrId, $"ForceExit:{cond.Label}"));
                    break;
                }
            }
            if (toClose.Any(c => c.CorrelationId == corrId)) continue;

            // ── OptionalExitConditions (UNE vraie + activée dans Settings) ────
            foreach (var cond in strategy.OptionalExitConditions)
            {
                if (!IsOptionalExitEnabled(strategy, cond.Label)) continue;

                // Ignorer si la condition ne s'applique pas à la direction de ce trade
                if (cond.ApplicableDirection != null && cond.ApplicableDirection != record.Direction)
                    continue;

                var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
                var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trendState;

                if (EvaluateCondition(cond, condIndicators, condTrend, record))
                {
                    toClose.Add((corrId, $"OptionalExit:{cond.Label}"));
                    break;
                }
            }

            if (!toClose.Any(c => c.CorrelationId == corrId))
                await ApplyStopLossProtectionsAsync(
                    record,
                    strategy,
                    candle,
                    indicators,
                    trendState,
                    ct).ConfigureAwait(false);
        }

        // Fermer les trades
        foreach (var (corrId, reason) in toClose)
        {
            if (!_openTrades.TryGetValue(corrId, out var pair)) continue;
            await _execution.ExecuteExitAsync(pair.Record, candle, reason, ct);
            RegisterExit(pair.Record);
            _openTrades.Remove(corrId);
        }
    }

    // ─── Vérification tick-par-tick (Replay Mode Tick) ────────────────────────

    private async Task CheckOpenTradesTicksAsync(
        Candle candle, IReadOnlyList<Tick> ticks, CancellationToken ct)
    {
        // 1. Pour les trades sur ce symbole, itérer les ticks chrono pour SL/TP précis.
        //    BUY  : SL hit si bid <= sl  | TP hit si bid >= tp  | exit at bid
        //    SELL : SL hit si ask >= sl  | TP hit si ask <= tp  | exit at ask
        var tickHits = new List<(string CorrelationId, string Reason, double Price, DateTimeOffset Time)>();

        foreach (var (corrId, (record, _)) in _openTrades)
        {
            if (record.Symbol != candle.Symbol) continue;
            if (!record.Sl.HasValue && !record.Tp.HasValue) continue;

            foreach (var t in ticks)
            {
                if (record.Direction == "BUY")
                {
                    if (record.Sl.HasValue && t.Bid <= record.Sl.Value)
                    { tickHits.Add((corrId, "SL", t.Bid, t.Time)); break; }
                    if (record.Tp.HasValue && t.Bid >= record.Tp.Value)
                    { tickHits.Add((corrId, "TP", t.Bid, t.Time)); break; }
                }
                else // SELL
                {
                    if (record.Sl.HasValue && t.Ask >= record.Sl.Value)
                    { tickHits.Add((corrId, "SL", t.Ask, t.Time)); break; }
                    if (record.Tp.HasValue && t.Ask <= record.Tp.Value)
                    { tickHits.Add((corrId, "TP", t.Ask, t.Time)); break; }
                }
            }
        }

        // Fermer les trades touchés sur tick avec prix précis
        foreach (var (corrId, reason, price, time) in tickHits)
        {
            if (!_openTrades.TryGetValue(corrId, out var pair)) continue;
            await _execution.ExecuteExitAsync(pair.Record, candle, reason, ct,
                explicitClosePrice: price, explicitCloseTime: time);
            RegisterExit(pair.Record);
            _openTrades.Remove(corrId);
        }

        // 2. Conditions ForceExit / OptionalExit — évaluées au close de la bougie
        //    (basées sur indicateurs, pas sur ticks). Mêmes règles que le path candle.
        await CheckExitConditionsAsync(candle, ct);
    }

    private async Task CheckExitConditionsAsync(Candle candle, CancellationToken ct)
    {
        var toClose = new List<(string CorrelationId, string Reason)>();

        foreach (var (corrId, (record, strategy)) in _openTrades)
        {
            if (record.Symbol != candle.Symbol) continue;

            var indicators = _indicators.GetIndicators(candle.Symbol, record.StrategyName != "" ? candle.Timeframe : strategy.Timeframe);
            var trendState = _trend.GetTrend(candle.Symbol, strategy.Timeframe)
                          ?? new TrendState { Timeframe = strategy.Timeframe, Trend = TrendDirection.Range };

            if (indicators is null) continue;

            foreach (var cond in strategy.ForceExitConditions)
            {
                if (cond.ApplicableDirection != null && cond.ApplicableDirection != record.Direction)
                    continue;

                var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
                var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trendState;

                if (EvaluateCondition(cond, condIndicators, condTrend, record))
                {
                    toClose.Add((corrId, $"ForceExit:{cond.Label}"));
                    break;
                }
            }
            if (toClose.Any(c => c.CorrelationId == corrId)) continue;

            foreach (var cond in strategy.OptionalExitConditions)
            {
                if (!IsOptionalExitEnabled(strategy, cond.Label)) continue;
                if (cond.ApplicableDirection != null && cond.ApplicableDirection != record.Direction)
                    continue;

                var condIndicators = _indicators.GetIndicators(candle.Symbol, cond.Timeframe) ?? indicators;
                var condTrend      = _trend.GetTrend(candle.Symbol, cond.Timeframe) ?? trendState;

                if (EvaluateCondition(cond, condIndicators, condTrend, record))
                {
                    toClose.Add((corrId, $"OptionalExit:{cond.Label}"));
                    break;
                }
            }

            if (!toClose.Any(c => c.CorrelationId == corrId))
                await ApplyStopLossProtectionsAsync(
                    record,
                    strategy,
                    candle,
                    indicators,
                    trendState,
                    ct).ConfigureAwait(false);
        }

        foreach (var (corrId, reason) in toClose)
        {
            if (!_openTrades.TryGetValue(corrId, out var pair)) continue;
            await _execution.ExecuteExitAsync(pair.Record, candle, reason, ct);
            RegisterExit(pair.Record);
            _openTrades.Remove(corrId);
        }
    }

    private static bool IsOptionalExitEnabled(IStrategy strategy, string label)
    {
        string settingKey = $"Exit_{label.Replace(" ", "_")}";
        return strategy.Settings.TryGetValue(settingKey, out var val) && val is true;
    }

    private static bool EvaluateCondition(
        StrategyCondition cond,
        IndicatorValues indicators,
        TrendState trend,
        TradeRecord record)
        => cond.TradeExpression?.Invoke(indicators, trend, record)
           ?? cond.Expression(indicators, trend);

    private async Task ApplyStopLossProtectionsAsync(
        TradeRecord record,
        IStrategy strategy,
        Candle candle,
        IndicatorValues fallbackIndicators,
        TrendState fallbackTrend,
        CancellationToken ct)
    {
        if (strategy.StopLossProtections.Count == 0) return;
        if (!record.EntryPrice.HasValue || !record.Sl.HasValue || !record.LotSize.HasValue)
            return;

        var meta = _metaProvider(record.Symbol);
        if (meta is null) return;

        foreach (var rule in strategy.StopLossProtections)
        {
            if (rule.ApplicableDirection is not null &&
                rule.ApplicableDirection != record.Direction)
                continue;

            var indicators = _indicators.GetIndicators(candle.Symbol, rule.Timeframe) ?? fallbackIndicators;
            var trend = _trend.GetTrend(candle.Symbol, rule.Timeframe) ?? fallbackTrend;
            double currentPrice = indicators.Close > 0 ? indicators.Close : candle.Close;
            double riskDistance = Math.Abs(record.EntryPrice.Value - record.Sl.Value);
            if (riskDistance <= 0) continue;

            var context = new StopLossProtectionContext
            {
                CurrentPrice = currentPrice,
                CurrentR = CurrentRiskReward(record, currentPrice),
                RiskDistance = riskDistance,
                FeesCoveredStop = CalculateFeesCoveredStop(record, meta),
                Meta = meta,
            };

            var candidate = rule.ComputeStopLoss(indicators, trend, record, context);
            if (!candidate.HasValue) continue;

            double newSl = RoundPrice(candidate.Value, meta);
            if (!IsImprovedStopLoss(record, newSl)) continue;
            if (!CanPlaceStopLoss(record, newSl, currentPrice, meta)) continue;

            await _execution.ModifyStopLossAsync(record, newSl, ct).ConfigureAwait(false);
        }
    }

    private double CalculateFeesCoveredStop(TradeRecord record, SymbolMeta meta)
    {
        if (!record.EntryPrice.HasValue || !record.LotSize.HasValue)
            return 0;

        double totalFees = Math.Max(0, record.Fees ?? 0);
        if (meta.ChargesCommission && _commissionPerLotPerSide > 0)
            totalFees += record.LotSize.Value * _commissionPerLotPerSide * 2.0;

        double moneyPerPriceForTrade = Math.Abs(meta.MoneyPerLot(1.0)) * record.LotSize.Value;
        if (moneyPerPriceForTrade <= 0)
            return record.EntryPrice.Value;

        double offset = totalFees / moneyPerPriceForTrade;
        double roundBuffer = meta.Point > 0 ? meta.Point : 0;

        return record.Direction == "BUY"
            ? record.EntryPrice.Value + offset + roundBuffer
            : record.EntryPrice.Value - offset - roundBuffer;
    }

    private static double CurrentRiskReward(TradeRecord record, double currentPrice)
    {
        if (!record.EntryPrice.HasValue || !record.Sl.HasValue) return 0;
        double risk = Math.Abs(record.EntryPrice.Value - record.Sl.Value);
        if (risk <= 0) return 0;

        double favorable = record.Direction == "BUY"
            ? currentPrice - record.EntryPrice.Value
            : record.EntryPrice.Value - currentPrice;

        return favorable / risk;
    }

    private static bool IsImprovedStopLoss(TradeRecord record, double newSl)
    {
        if (!record.Sl.HasValue || !record.EntryPrice.HasValue) return false;

        return record.Direction == "BUY"
            ? newSl > record.Sl.Value && newSl > record.EntryPrice.Value
            : newSl < record.Sl.Value && newSl < record.EntryPrice.Value;
    }

    private static bool CanPlaceStopLoss(
        TradeRecord record,
        double newSl,
        double currentPrice,
        SymbolMeta meta)
    {
        double gap = meta.Point > 0 ? meta.Point * 2.0 : 0;
        return record.Direction == "BUY"
            ? newSl < currentPrice - gap
            : newSl > currentPrice + gap;
    }

    private static double RoundPrice(double price, SymbolMeta meta)
        => meta.Digits >= 0 ? Math.Round(price, meta.Digits) : price;

    private double EstimateRoundTripFeesPerLot(SymbolMeta meta, double spreadAtEntry)
    {
        double spreadCost = spreadAtEntry > 0 ? Math.Abs(meta.MoneyPerLot(spreadAtEntry)) : 0.0;
        double commission = meta.ChargesCommission ? _commissionPerLotPerSide * 2.0 : 0.0;
        return spreadCost + commission;
    }

    private static bool IsStopLossOnRiskSide(TradeSignal signal)
        => signal.Direction == "BUY"
            ? signal.Sl < signal.EntryPrice
            : signal.Sl > signal.EntryPrice;

    private static TradeSignal EnsureMinimumTakeProfit(TradeSignal signal, IStrategy strategy)
    {
        if (IsTakeProfitDisabled(signal))
            return signal;

        double risk = Math.Abs(signal.EntryPrice - signal.Sl);
        if (risk <= 0) return signal;

        double minRiskReward = GetMinimumRiskReward(strategy);
        double minReward = risk * minRiskReward;
        double currentReward = signal.Direction == "BUY"
            ? signal.Tp - signal.EntryPrice
            : signal.EntryPrice - signal.Tp;

        if (currentReward >= minReward)
            return signal;

        double tp = signal.Direction == "BUY"
            ? signal.EntryPrice + minReward
            : signal.EntryPrice - minReward;

        return signal with
        {
            Tp       = tp,
            TpReason = FormatRiskRewardTpReason(minRiskReward),
        };
    }

    private static bool IsTakeProfitDisabled(TradeSignal signal)
        => signal.Direction == "BUY"
            ? signal.Tp >= 100_000_000
            : signal.Tp <= 0;

    private static double GetMinimumRiskReward(IStrategy strategy)
    {
        double minRiskReward = 2.0;
        if (strategy.Settings.TryGetValue("MinRiskReward", out var value) && value is not null)
        {
            try { minRiskReward = Convert.ToDouble(value); }
            catch { minRiskReward = 2.0; }
        }

        return Math.Max(2.0, minRiskReward);
    }

    private static string FormatRiskRewardTpReason(double rr)
        => Math.Abs(rr - 2.0) <= 0.05 ? "TP - sorti 2:1" : $"TP - sorti {rr:0.##}:1";

    private bool HasReachedMaxTradesPerSymbolDay(string symbol, DateTimeOffset entryTime, IStrategy strategy)
    {
        int max = strategy.MaxTradesPerSymbolPerDay;
        if (max < 0) return false;

        var key = (symbol, DateOnly.FromDateTime(TimeZoneHelper.ToQuebec(entryTime).Date));
        return _entriesBySymbolDay.TryGetValue(key, out int count) && count >= max;
    }

    private bool IsInReentryCooldown(
        string symbol,
        string direction,
        DateTimeOffset candidateEntryTime,
        IStrategy strategy)
    {
        int minutes = strategy.ReentryCooldownMinutes;
        if (minutes <= 0) return false;

        var key = (symbol, direction);
        var candidate = TimeZoneHelper.ToQuebec(candidateEntryTime);
        var cooldown = TimeSpan.FromMinutes(minutes);

        if (_lastEntryBySymbolDirection.TryGetValue(key, out var lastEntry) &&
            candidate - TimeZoneHelper.ToQuebec(lastEntry) < cooldown)
            return true;

        if (_lastExitBySymbolDirection.TryGetValue(key, out var lastExit) &&
            candidate - TimeZoneHelper.ToQuebec(lastExit) < cooldown)
            return true;

        return false;
    }

    private void RegisterEntry(TradeRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Symbol) || !record.EntryTime.HasValue)
            return;

        TrackLatest(
            _lastEntryBySymbolDirection,
            (record.Symbol, record.Direction),
            record.EntryTime.Value);

        string id = string.IsNullOrWhiteSpace(record.CorrelationId)
            ? $"{record.Symbol}|{record.Direction}|{record.EntryTime.Value:O}|{record.EntryPrice}"
            : record.CorrelationId;

        if (!_countedEntryIds.Add(id))
            return;

        var key = (record.Symbol, DateOnly.FromDateTime(TimeZoneHelper.ToQuebec(record.EntryTime.Value).Date));
        _entriesBySymbolDay[key] = _entriesBySymbolDay.TryGetValue(key, out int count)
            ? count + 1
            : 1;
    }

    private void RegisterExit(TradeRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Symbol) || !record.ExitTime.HasValue)
            return;

        TrackLatest(
            _lastExitBySymbolDirection,
            (record.Symbol, record.Direction),
            record.ExitTime.Value);
    }

    private static void TrackLatest(
        Dictionary<(string Symbol, string Direction), DateTimeOffset> map,
        (string Symbol, string Direction) key,
        DateTimeOffset time)
    {
        var qc = TimeZoneHelper.ToQuebec(time);
        if (!map.TryGetValue(key, out var existing) || qc > TimeZoneHelper.ToQuebec(existing))
            map[key] = qc;
    }

}
