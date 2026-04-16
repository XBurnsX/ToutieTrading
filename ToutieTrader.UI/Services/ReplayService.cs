using System.Text.Json;
using ToutieTrader.Core.Engine;
using ToutieTrader.Core.Engine.Events;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;
using ToutieTrader.Data;

namespace ToutieTrader.UI.Services;

/// <summary>Configuration d'une session de replay.</summary>
public sealed class ReplayConfig
{
    public required string Symbol          { get; init; }   // Symbole affiché au Start
    public required string Timeframe       { get; init; }   // TF d'évaluation (fixe pendant le replay)
    public required DateTimeOffset From    { get; init; }
    public required DateTimeOffset To      { get; init; }
    public required double StartingCapital { get; init; }
    public required int Speed              { get; init; }
    public IStrategy? Strategy             { get; init; }
    public Func<bool>? IsPaused            { get; init; }
}

/// <summary>
/// Moteur de replay autonome — backtest multi-symbole / multi-timeframe.
///
/// - Charge TOUS les symboles × TOUS les TFs standards en mémoire au Start.
/// - Merge toutes les bougies de tous les symboles/TFs triées par temps.
/// - La stratégie est évaluée sur CHAQUE symbole en parallèle (bougies du TF cfg.Timeframe).
/// - Les trades peuvent s'ouvrir sur n'importe quel symbole simultanément.
/// - Le chart affiche UNIQUEMENT le (symbole, TF) actuellement sélectionné.
/// - Switch Symbol/TF mid-replay = switch d'affichage instantané, pas de stop, pas de reload.
/// </summary>
public sealed class ReplayService
{
    private static readonly Dictionary<int, int> DelayMs = new()
    {
        [1] = 1000, [2] = 500, [4] = 250, [8] = 125, [16] = 62, [32] = 0
    };

    /// <summary>TFs standards — tous chargés en background à chaque session replay.</summary>
    public static readonly string[] AllTimeframes =
        ["M1", "M5", "M15", "H1", "H4", "D"];

    private readonly DuckDBReader      _reader;
    private readonly TradeRepository?  _tradeRepo;
    private readonly MT5ApiClient?     _mt5;

    // ── Callbacks UI ──────────────────────────────────────────────────────────
    public event Action<Candle, IndicatorValues?, DateTimeOffset?>? OnCandleProcessed;
    public event Action<double, DateTimeOffset>?   OnProgress;
    public event Action<TradeRecord>?              OnTradeOpened;
    public event Action<TradeRecord>?              OnTradeClosed;
    public event Action<double, int, int, double>? OnStatsUpdate;

    public event Action<long, List<(string Label, bool Passed)>, List<(string Label, bool Passed)>>?
        OnConditionsEvaluated;

    /// <summary>Snapshot des trends par TF du symbole actuellement affiché (throttle ~4Hz).</summary>
    public event Action<Dictionary<string, TrendDirection>>? OnTrendsUpdate;

    /// <summary>Message de chargement au démarrage du replay (fetch MT5, load DuckDB…).</summary>
    public event Action<string>? OnLoadingStatus;

    // ── State ──────────────────────────────────────────────────────────────────
    public bool   IsRunning        { get; private set; }
    public List<string> AvailableSymbols { get; private set; } = [];

    // Display state — accessible depuis l'UI pendant le replay
    private volatile string _liveDisplayTf = "";
    private volatile string _liveSymbol    = "";
    // symbol -> tf -> candles
    private volatile Dictionary<string, Dictionary<string, List<Candle>>>? _liveAllBySymbolAndTf;
    private long _currentReplayTimestamp;   // Unix seconds, lu/écrit via Interlocked

    /// <summary>Temps courant du replay (bougie en cours). Thread-safe.</summary>
    public DateTimeOffset CurrentReplayTime =>
        DateTimeOffset.FromUnixTimeSeconds(Interlocked.Read(ref _currentReplayTimestamp));

    /// <summary>Symbole actuellement affiché sur le chart. Thread-safe.</summary>
    public string LiveDisplaySymbol => _liveSymbol;

    /// <summary>TF actuellement affiché sur le chart. Thread-safe.</summary>
    public string LiveDisplayTimeframe => _liveDisplayTf;

    public ReplayService(
        DuckDBReader reader,
        TradeRepository? tradeRepo = null,
        MT5ApiClient? mt5 = null)
    {
        _reader    = reader;
        _tradeRepo = tradeRepo;
        _mt5       = mt5;
        RefreshSymbols();
    }

    public void RefreshSymbols()
    {
        try
        {
            AvailableSymbols = _reader.GetAvailableSymbols();
            ReplayLogger.Log($"RefreshSymbols: {AvailableSymbols.Count} symbols from DuckDB");
        }
        catch (Exception ex)
        {
            ReplayLogger.LogException("RefreshSymbols", ex);
            AvailableSymbols = [];
        }
    }

    /// <summary>
    /// Peuple AvailableSymbols depuis la watchlist MT5 (via le Python bridge).
    /// Utilisé au démarrage de ReplayPage quand candles.db est vide — permet
    /// d'avoir le dropdown symbole populé AVANT le premier Start (sinon l'utilisateur
    /// ne peut rien sélectionner et Start est bloqué).
    ///
    /// Ne throw pas — en cas d'erreur, garde la liste actuelle.
    /// Retourne true si des symbols ont été chargés depuis MT5.
    /// </summary>
    public async Task<bool> PopulateSymbolsFromMT5Async(CancellationToken ct = default)
    {
        ReplayLogger.Log($"PopulateSymbolsFromMT5Async: _mt5={(_mt5 == null ? "NULL" : "OK")}");
        if (_mt5 == null) return false;

        try
        {
            ReplayLogger.Log("Calling _mt5.GetWatchlistAsync...");
            var watchlist = await _mt5.GetWatchlistAsync(ct).ConfigureAwait(false);
            ReplayLogger.Log($"GetWatchlistAsync returned {watchlist.Count} entries");
            if (watchlist.Count == 0)
            {
                ReplayLogger.Log("Watchlist empty — returning false. Python bridge not running? MT5 not connected?");
                return false;
            }

            // Fusion : on garde tout ce qui est déjà dans AvailableSymbols (depuis la DB)
            // + on ajoute les canoniques de la watchlist MT5 qui n'y sont pas encore.
            var merged = new HashSet<string>(AvailableSymbols, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in watchlist)
                merged.Add(entry.CanonicalName);

            AvailableSymbols = merged.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            ReplayLogger.Log($"AvailableSymbols now has {AvailableSymbols.Count} entries: {string.Join(", ", AvailableSymbols.Take(10))}...");
            return true;
        }
        catch (Exception ex)
        {
            ReplayLogger.LogException("PopulateSymbolsFromMT5Async", ex);
            return false;
        }
    }

    // ── Switch display (temps réel pendant replay) ────────────────────────────

    /// <summary>
    /// Change le TF affiché pendant un replay actif.
    /// Retourne true si les données de ce TF sont disponibles pour le symbole courant.
    /// </summary>
    public bool TrySwitchDisplayTf(string newTf)
    {
        var snapshot = _liveAllBySymbolAndTf;
        if (snapshot == null) return false;
        if (!snapshot.TryGetValue(_liveSymbol, out var tfMap)) return false;
        if (!tfMap.ContainsKey(newTf)) return false;
        _liveDisplayTf = newTf;
        return true;
    }

    /// <summary>
    /// Change le symbole affiché pendant un replay actif.
    /// Retourne true si les données de ce symbole sont disponibles.
    /// Aucune fermeture de trades — les trades ouverts (tous symboles) continuent.
    /// </summary>
    public bool TrySwitchDisplaySymbol(string newSymbol)
    {
        var snapshot = _liveAllBySymbolAndTf;
        if (snapshot == null || !snapshot.ContainsKey(newSymbol)) return false;
        _liveSymbol = newSymbol;
        return true;
    }

    // ── Reconstruction du chart (historyset) ──────────────────────────────────

    /// <summary>
    /// Construit le JSON historyset pour un (symbol, tf) depuis les données en mémoire,
    /// incluant toutes les bougies jusqu'à upTo. Utilisé pour recharger le chart
    /// instantanément lors d'un switch Symbol/TF mid-replay.
    /// </summary>
    public string? BuildHistoryJsonUpTo(
        string symbol, string tf, DateTimeOffset from, DateTimeOffset upTo,
        IStrategy? strategy, CancellationToken ct)
    {
        var snapshot = _liveAllBySymbolAndTf;
        if (snapshot == null || !snapshot.TryGetValue(symbol, out var tfMap)) return null;
        if (!tfMap.TryGetValue(tf, out var chartCandlesFull) || chartCandlesFull.Count == 0)
            return null;

        // Warm-up : toutes les bougies de CE symbole (tous TFs) jusqu'à upTo
        var allCandles = tfMap.Values
            .SelectMany(c => c)
            .Where(c => c.Time <= upTo)
            .OrderBy(c => c.Time)
            .ToList();

        var engine   = new IndicatorEngine(new EventBus());
        var swings   = new SwingPointEngine();
        var trendEng = new TrendEngine(new EventBus(), engine, swings);

        int chartIdxInFull = -1;   // index dans chartCandlesFull (incrémenté sur chaque bougie du TF)
        var bCandles      = new List<object>(chartCandlesFull.Count);
        var bTenkan       = new List<object>();
        var bKijun        = new List<object>();
        var bSsa          = new List<object>();
        var bSsb          = new List<object>();
        var bSsa26        = new List<object>();
        var bSsb26        = new List<object>();
        var bChikou       = new List<object>();
        var bCloudCurrent = new List<object>();
        var bCloudFuture  = new List<object>();
        var bConditions   = new List<object>();

        foreach (var candle in allCandles)
        {
            if (ct.IsCancellationRequested) return null;

            // Alimentation des engines pour toutes les bougies (warmup + display)
            engine.ProcessCandle(candle);
            swings.ProcessCandle(candle);
            trendEng.ProcessCandle(candle);

            if (candle.Timeframe != tf) continue;

            chartIdxInFull++;   // suit la position réelle dans chartCandlesFull

            // Warmup : on skippe l'émission — le chart démarre à `from`
            if (candle.Time < from) continue;

            var iv = engine.GetIndicators(symbol, tf);
            // Shift to "fake UTC" Quebec — TradingView v3.8.0 affiche Unix en UTC par défaut,
            // en faisant passer l'heure Québec wall-clock comme UTC on obtient l'affichage correct.
            long ts       = TimeZoneHelper.ToChartUnixSeconds(candle.Time);
            long futureTs = chartIdxInFull + 26 < chartCandlesFull.Count
                ? TimeZoneHelper.ToChartUnixSeconds(chartCandlesFull[chartIdxInFull + 26].Time) : 0;
            long chikouTs = (iv != null && iv.ChikouCandleTime != default)
                ? TimeZoneHelper.ToChartUnixSeconds(iv.ChikouCandleTime) : 0;

            bCandles.Add(new { time = ts, open = candle.Open, high = candle.High,
                               low = candle.Low, close = candle.Close });

            if (iv != null)
            {
                if (iv.Tenkan > 0) bTenkan.Add(new { time = ts, value = iv.Tenkan });
                if (iv.Kijun  > 0) bKijun .Add(new { time = ts, value = iv.Kijun  });

                if (iv.Chikou > 0 && chikouTs > 0)
                    bChikou.Add(new { time = chikouTs, value = iv.Chikou });

                if (iv.SenkouA > 0 && iv.SenkouB > 0)
                {
                    bSsa.Add(new { time = ts, value = iv.SenkouA });
                    bSsb.Add(new { time = ts, value = iv.SenkouB });
                    bCloudCurrent.Add(new { time = ts, senkouA = iv.SenkouA, senkouB = iv.SenkouB });
                }

                if (iv.SenkouA26 > 0 && iv.SenkouB26 > 0 && futureTs > 0)
                {
                    bSsa26.Add(new { time = futureTs, value = iv.SenkouA26 });
                    bSsb26.Add(new { time = futureTs, value = iv.SenkouB26 });
                    bCloudFuture.Add(new { time = futureTs, senkouA = iv.SenkouA26, senkouB = iv.SenkouB26 });
                }

                if (strategy != null)
                {
                    var chartTrend = trendEng.GetTrend(symbol, tf)
                        ?? new TrendState { Timeframe = tf, Trend = TrendDirection.Range };
                    var lr = EvalConditions(strategy.LongConditions,  symbol, iv, chartTrend, engine, trendEng, tf);
                    var sr = EvalConditions(strategy.ShortConditions, symbol, iv, chartTrend, engine, trendEng, tf);
                    bConditions.Add(new { time = ts, @long = lr, @short = sr });
                }
            }
        }

        if (ct.IsCancellationRequested || bCandles.Count == 0) return null;

        return JsonSerializer.Serialize(new
        {
            type         = "historyset",
            candles      = bCandles,
            tenkan       = bTenkan,
            kijun        = bKijun,
            ssa          = bSsa,
            ssb          = bSsb,
            ssa26        = bSsa26,
            ssb26        = bSsb26,
            chikou       = bChikou,
            cloudCurrent = bCloudCurrent,
            cloudFuture  = bCloudFuture,
            conditions   = bConditions,
        });
    }

    // ── Main Loop ──────────────────────────────────────────────────────────────

    public async Task StartAsync(ReplayConfig cfg, CancellationToken ct)
    {
        ReplayLogger.Log("========== StartAsync BEGIN ==========");
        ReplayLogger.Log($"Config: Symbol={cfg.Symbol}, TF={cfg.Timeframe}, From={cfg.From}, To={cfg.To}, Capital={cfg.StartingCapital}, Speed={cfg.Speed}, Strategy={cfg.Strategy?.Name ?? "NULL"}");
        IsRunning = true;

        // ── Lazy fetch MT5 → DuckDB ───────────────────────────────────────────
        if (_mt5 != null)
        {
            try
            {
                ReplayLogger.Log("Starting MT5 EnsureCandlesRangeAsync...");
                OnLoadingStatus?.Invoke("Téléchargement des données MT5… (première run : ça peut prendre quelques minutes)");
                var result = await _mt5.EnsureCandlesRangeAsync(cfg.From, cfg.To, AllTimeframes, ct)
                    .ConfigureAwait(false);

                ReplayLogger.Log($"EnsureCandlesRange done: {result.TotalSymbols} symbols, {result.TotalInserted} inserted, {result.TotalCached} cached, {result.ElapsedSec:F1}s");
                OnLoadingStatus?.Invoke(
                    $"MT5 → DuckDB : {result.TotalSymbols} symbols, " +
                    $"{result.TotalInserted:N0} nouvelles bougies, " +
                    $"{result.TotalCached:N0} en cache ({result.ElapsedSec:F1}s)");

                RefreshSymbols();
                ReplayLogger.Log($"After RefreshSymbols: {AvailableSymbols.Count} symbols");
            }
            catch (Exception ex)
            {
                ReplayLogger.LogException("EnsureCandlesRangeAsync", ex);
                OnLoadingStatus?.Invoke($"Erreur MT5 fetch : {ex.Message}. Tentative de lecture DB locale…");
            }
        }
        else
        {
            ReplayLogger.Log("_mt5 is null — skipping lazy fetch");
        }

        OnLoadingStatus?.Invoke("Chargement DuckDB…");

        // Charger TOUS les symboles × TOUS les TFs en parallèle
        Dictionary<string, Dictionary<string, List<Candle>>> allBySymbolAndTf;
        try
        {
            ReplayLogger.Log("Loading all symbols/TFs from DuckDB...");
            allBySymbolAndTf = await LoadAllSymbolsAllTfsAsync(cfg.From, cfg.To, ct);
            ReplayLogger.Log($"Loaded {allBySymbolAndTf.Count} symbols from DuckDB");
            foreach (var kvp in allBySymbolAndTf)
                ReplayLogger.Log($"  {kvp.Key}: {string.Join(", ", kvp.Value.Select(t => $"{t.Key}={t.Value.Count}"))}");
        }
        catch (Exception ex)
        {
            ReplayLogger.LogException("LoadAllSymbolsAllTfsAsync", ex);
            IsRunning = false;
            return;
        }

        if (allBySymbolAndTf.Count == 0)
        {
            ReplayLogger.Log("WARNING: allBySymbolAndTf is EMPTY — no data for this range. Aborting.");
            OnLoadingStatus?.Invoke("Aucune donnée disponible pour cette range.");
            IsRunning = false;
            return;
        }

        ReplayLogger.Log("All data loaded → starting replay loop");
        OnLoadingStatus?.Invoke("");   // Clear le message → replay démarre

        // Merge toutes les bougies de tous les symboles et TFs, triées par temps global
        var allCandles = allBySymbolAndTf.Values
            .SelectMany(tfMap => tfMap.Values.SelectMany(c => c))
            .OrderBy(c => c.Time)
            .ToList();

        if (allCandles.Count == 0) { IsRunning = false; return; }

        // ── Init display state ────────────────────────────────────────────────
        _liveDisplayTf        = cfg.Timeframe;
        _liveSymbol           = cfg.Symbol;
        _liveAllBySymbolAndTf = allBySymbolAndTf;
        Interlocked.Exchange(ref _currentReplayTimestamp, cfg.From.ToUnixTimeSeconds());

        // Index courant par (symbol, tf) — pour projection nuage futur du chart
        var chartIdxBySymTf = new Dictionary<(string, string), int>();

        int  delayMs    = DelayMs.GetValueOrDefault(cfg.Speed, 500);
        long totalRange = Math.Max(1, (cfg.To - cfg.From).Ticks);
        long lastTrendEmitTicks = 0;
        long lastProgressEmitTicks = 0;

        var engine   = new IndicatorEngine(new EventBus());
        var swings   = new SwingPointEngine();
        var trendEng = new TrendEngine(new EventBus(), engine, swings);
        var open     = new List<ReplayTrade>();
        var state    = new ReplayState { Capital = cfg.StartingCapital, Peak = cfg.StartingCapital };

        try
        {
            foreach (var candle in allCandles)
            {
                if (ct.IsCancellationRequested) break;

                // Alimentation des engines — toutes bougies de tous symboles/TFs
                engine.ProcessCandle(candle);
                swings.ProcessCandle(candle);
                trendEng.ProcessCandle(candle);

                // Index pour futureTime (projection nuage)
                var symTfKey = (candle.Symbol, candle.Timeframe);
                if (!chartIdxBySymTf.TryGetValue(symTfKey, out int curIdx))
                    curIdx = 0;
                chartIdxBySymTf[symTfKey] = curIdx + 1;

                // Snapshot état d'affichage — peut changer à chaque bougie (user dropdown)
                string displayTf  = _liveDisplayTf;
                string displaySym = _liveSymbol;
                bool isDisplay    = candle.Symbol == displaySym && candle.Timeframe == displayTf;
                bool isStrategyTf = candle.Timeframe == cfg.Timeframe;

                // ── Warmup : nourrir les engines + envoyer les display au chart ──
                // Les bougies warmup du (symbol, TF) affiché sont envoyées au chart
                // SANS délai pour que Ichimoku soit déjà visible au démarrage du replay.
                // Les bougies des autres symbols/TFs alimentent les engines en silence.
                if (candle.Time < cfg.From)
                {
                    if (isDisplay)
                    {
                        var warmIv = engine.GetIndicators(candle.Symbol, candle.Timeframe);
                        var warmFt = ComputeFutureTime(allBySymbolAndTf, candle, curIdx);
                        OnCandleProcessed?.Invoke(candle, warmIv, warmFt);
                    }
                    continue;
                }

                Interlocked.Exchange(ref _currentReplayTimestamp, candle.Time.ToUnixTimeSeconds());

                var iv = engine.GetIndicators(candle.Symbol, candle.Timeframe);

                // ── Évaluation stratégie ──────────────────────────────────────
                // La stratégie tourne sur CHAQUE symbole (bougies du TF cfg.Timeframe).
                // Elle peut ouvrir/fermer des trades sur n'importe quel symbole.
                if (isStrategyTf && cfg.Strategy != null && iv != null)
                {
                    await EvaluateAsync(candle, iv, cfg.Strategy, engine, trendEng,
                                        open, state, ct, emitConditions: isDisplay);
                }

                // ── Envoi au chart : bougies du symbole+TF display ─────────────
                if (isDisplay)
                {
                    var ft = ComputeFutureTime(allBySymbolAndTf, candle, curIdx);
                    OnCandleProcessed?.Invoke(candle, iv, ft);

                    // HUD trends — throttle 250ms
                    long nowTicks = DateTime.UtcNow.Ticks;
                    if (nowTicks - lastTrendEmitTicks > 2_500_000)   // 250ms en ticks
                    {
                        EmitTrendsUpdate(trendEng, displaySym);
                        lastTrendEmitTicks = nowTicks;
                    }
                }

                // ── Progress bar — basée sur le temps simulé (throttle 200ms) ──
                {
                    long nowTicks = DateTime.UtcNow.Ticks;
                    if (nowTicks - lastProgressEmitTicks > 2_000_000)   // 200ms
                    {
                        long elapsed = (candle.Time - cfg.From).Ticks;
                        double pct   = Math.Clamp((double)elapsed / totalRange * 100.0, 0, 100);
                        OnProgress?.Invoke(pct, candle.Time);
                        lastProgressEmitTicks = nowTicks;
                    }
                }

                // Stats — émission seulement après une bougie affichée (ou quand la stratégie tourne)
                if (isDisplay || isStrategyTf)
                {
                    double dd = state.Peak > 0 ? Math.Max(0, (state.Peak - state.Capital) / state.Peak * 100.0) : 0;
                    OnStatsUpdate?.Invoke(state.Capital, state.Wins, state.Losses, dd);
                }

                // Pause
                while (cfg.IsPaused?.Invoke() == true && !ct.IsCancellationRequested)
                    await Task.Delay(50, ct).ConfigureAwait(false);

                // ── Delay visuel : seulement sur la bougie display ────────────
                // Le rythme visuel suit _liveDisplayTf (qui peut changer dynamiquement).
                // Les bougies non-display passent à pleine vitesse (alimentation des engines).
                if (isDisplay && delayMs > 0)
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }

            // Final progress
            OnProgress?.Invoke(100.0, cfg.To);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _liveAllBySymbolAndTf = null;
            _liveDisplayTf        = "";
            _liveSymbol           = "";
            IsRunning             = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EmitTrendsUpdate(TrendEngine trendEng, string symbol)
    {
        var trends = new Dictionary<string, TrendDirection>(AllTimeframes.Length);
        foreach (var tf in AllTimeframes)
        {
            var t = trendEng.GetTrend(symbol, tf);
            trends[tf] = t?.Trend ?? TrendDirection.Range;
        }
        OnTrendsUpdate?.Invoke(trends);
    }

    private static DateTimeOffset? ComputeFutureTime(
        Dictionary<string, Dictionary<string, List<Candle>>> all,
        Candle candle, int currentIdx)
    {
        if (!all.TryGetValue(candle.Symbol, out var tfMap)) return null;
        if (!tfMap.TryGetValue(candle.Timeframe, out var lst)) return null;
        int target = currentIdx + 26;
        return target < lst.Count ? lst[target].Time : null;
    }

    /// <summary>Charge tous les symboles × tous les TFs standards en parallèle.</summary>
    private async Task<Dictionary<string, Dictionary<string, List<Candle>>>>
        LoadAllSymbolsAllTfsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var result  = new Dictionary<string, Dictionary<string, List<Candle>>>(StringComparer.OrdinalIgnoreCase);
        var symbols = AvailableSymbols.ToList();
        if (symbols.Count == 0) return result;

        // Une Task par symbole — 1 seule connexion DuckDB par symbole pour tous les TFs
        ReplayLogger.Log($"LoadAllSymbolsAllTfsAsync: {symbols.Count} symbols × {AllTimeframes.Length} TFs, from={from}, to={to}");
        var tfQueries = AllTimeframes
            .Select(tf => (Tf: tf, From: from.AddSeconds(-100 * TfSeconds(tf)), To: to))
            .ToList();

        var tasks = symbols.Select(sym => Task.Run(() =>
        {
            if (ct.IsCancellationRequested) return (sym, new Dictionary<string, List<Candle>>());
            try
            {
                var byTf = _reader.GetCandlesForSymbol(sym, tfQueries);
                return (sym, byTf);
            }
            catch (Exception ex)
            {
                ReplayLogger.Log($"GetCandlesForSymbol({sym}) FAILED: {ex.GetType().Name}: {ex.Message}");
                return (sym, new Dictionary<string, List<Candle>>());
            }
        }, ct)).ToList();

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var (sym, byTf) in completed)
        {
            if (byTf.Count > 0) result[sym] = byTf;
        }
        return result;
    }

    /// <summary>Évalue une liste de conditions en utilisant le bon TF pour chaque condition.</summary>
    private static object[] EvalConditions(
        List<StrategyCondition> conditions,
        string symbol, IndicatorValues chartIv, TrendState chartTrend,
        IndicatorEngine engine, TrendEngine trendEng, string chartTf)
    {
        return conditions.Select(c =>
        {
            var condTf    = string.IsNullOrEmpty(c.Timeframe) ? chartTf : c.Timeframe;
            var condIv    = engine.GetIndicators(symbol, condTf) ?? chartIv;
            var condTrend = trendEng.GetTrend(symbol, condTf)
                ?? new TrendState { Timeframe = condTf, Trend = TrendDirection.Range };
            return new { l = c.Label, p = c.Expression(condIv, condTrend) };
        }).ToArray<object>();
    }

    private static long TfSeconds(string tf) => tf switch
    {
        "M1"  => 60,
        "M5"  => 300,
        "M15" => 900,
        "H1"  => 3600,
        "H4"  => 14400,
        "D"   => 86400,
        _     => 3600,
    };

    // ── Strategy Evaluation ───────────────────────────────────────────────────

    private async Task EvaluateAsync(
        Candle candle, IndicatorValues iv, IStrategy strategy,
        IndicatorEngine engine, TrendEngine trendEng,
        List<ReplayTrade> open, ReplayState state,
        CancellationToken ct, bool emitConditions)
    {
        var chartTrend = trendEng.GetTrend(candle.Symbol, candle.Timeframe)
            ?? new TrendState { Timeframe = candle.Timeframe, Trend = TrendDirection.Range };

        // 1. Sorties des trades ouverts — SEULEMENT ceux du symbole courant
        for (int i = open.Count - 1; i >= 0; i--)
        {
            var t = open[i];
            if (t.Record.Symbol != candle.Symbol) continue;

            string? reason = null;
            double  exitPx = candle.Close;

            if      (t.Direction == "BUY"  && candle.Low  <= t.Sl) { reason = "SL"; exitPx = t.Sl; }
            else if (t.Direction == "SELL" && candle.High >= t.Sl) { reason = "SL"; exitPx = t.Sl; }
            else if (t.Direction == "BUY"  && candle.High >= t.Tp) { reason = "TP"; exitPx = t.Tp; }
            else if (t.Direction == "SELL" && candle.Low  <= t.Tp) { reason = "TP"; exitPx = t.Tp; }
            else
            {
                foreach (var cond in strategy.ForceExitConditions)
                {
                    if (cond.ApplicableDirection != null && cond.ApplicableDirection != t.Direction) continue;
                    var condTf    = string.IsNullOrEmpty(cond.Timeframe) ? candle.Timeframe : cond.Timeframe;
                    var condIv    = engine.GetIndicators(candle.Symbol, condTf) ?? iv;
                    var condTrend = trendEng.GetTrend(candle.Symbol, condTf) ?? chartTrend;
                    if (cond.Expression(condIv, condTrend)) { reason = $"ForceExit:{cond.Label}"; break; }
                }
            }

            if (reason == null) continue;

            double pipSize = candle.Symbol.Contains("JPY") ? 0.01 : 0.0001;
            double pips    = t.Direction == "BUY"
                ? (exitPx - t.Entry) / pipSize
                : (t.Entry - exitPx) / pipSize;
            double pl = Math.Round(pips * t.LotSize * 10.0, 2);

            state.Capital += pl;
            if (state.Capital > state.Peak) state.Peak = state.Capital;
            if (pl >= 0) state.Wins++; else state.Losses++;

            t.Record.ExitTime   = candle.Time;
            t.Record.ExitPrice  = exitPx;
            t.Record.ProfitLoss = pl;
            t.Record.ExitReason = reason;

            OnTradeClosed?.Invoke(t.Record);
            if (_tradeRepo != null)
                await _tradeRepo.SaveTradeAsync(t.Record, isReplay: true);
            open.RemoveAt(i);
        }

        // 2. Évaluer conditions — évaluées pour l'ouverture de trade ET pour le HUD chart
        var longResults  = strategy.LongConditions. Select(c => {
            var condTf = string.IsNullOrEmpty(c.Timeframe) ? candle.Timeframe : c.Timeframe;
            var condIv = engine.GetIndicators(candle.Symbol, condTf) ?? iv;
            var condTr = trendEng.GetTrend(candle.Symbol, condTf) ?? chartTrend;
            return (c.Label, c.Expression(condIv, condTr));
        }).ToList();
        var shortResults = strategy.ShortConditions.Select(c => {
            var condTf = string.IsNullOrEmpty(c.Timeframe) ? candle.Timeframe : c.Timeframe;
            var condIv = engine.GetIndicators(candle.Symbol, condTf) ?? iv;
            var condTr = trendEng.GetTrend(candle.Symbol, condTf) ?? chartTrend;
            return (c.Label, c.Expression(condIv, condTr));
        }).ToList();

        // Émission des conditions au tooltip chart UNIQUEMENT si c'est la bougie affichée
        // Timestamp doit matcher celui de la bougie envoyée au chart (fake UTC Québec)
        if (emitConditions)
            OnConditionsEvaluated?.Invoke(TimeZoneHelper.ToChartUnixSeconds(candle.Time), longResults, shortResults);

        // 3. MaxSimultaneousTrades = global (tous symboles confondus)
        if (open.Count >= strategy.MaxSimultaneousTrades) return;

        if (longResults.Count > 0 && longResults.All(r => r.Item2))
            TryOpenTrade("BUY", candle, iv, strategy, open, state.Capital);
        else if (shortResults.Count > 0 && shortResults.All(r => r.Item2))
            TryOpenTrade("SELL", candle, iv, strategy, open, state.Capital);
    }

    private void TryOpenTrade(
        string direction, Candle candle, IndicatorValues iv,
        IStrategy strategy, List<ReplayTrade> open, double capital)
    {
        double pipSize = candle.Symbol.Contains("JPY") ? 0.01 : 0.0001;

        double sl = strategy.StopLoss.Type == StopLossType.Custom
            ? strategy.StopLoss.CustomCompute?.Invoke(iv, direction) ?? iv.Close
            : CalcStdSl(strategy.StopLoss, direction, iv, pipSize);

        double tp = strategy.TakeProfit.Type == TakeProfitType.Custom
            ? strategy.TakeProfit.CustomCompute?.Invoke(iv, direction, candle.Close, sl) ?? candle.Close
            : CalcStdTp(strategy.TakeProfit, direction, candle.Close, sl);

        if (direction == "BUY"  && sl >= candle.Close) return;
        if (direction == "SELL" && sl <= candle.Close) return;

        decimal riskPct = strategy.Settings.TryGetValue("RiskPercent", out var rp)
            ? Convert.ToDecimal(rp) : strategy.RiskPercent;

        double riskDollars = capital * (double)riskPct / 100.0;
        double slPips      = Math.Abs(candle.Close - sl) / pipSize;
        double lotSize     = slPips > 0 ? Math.Round(riskDollars / (slPips * 10.0), 2) : 0.01;
        lotSize = Math.Max(0.01, Math.Min(lotSize, 100.0));

        var record = new TradeRecord
        {
            Symbol           = candle.Symbol,
            StrategyName     = strategy.Name,
            StrategySettings = JsonSerializer.Serialize(strategy.Settings),
            Direction        = direction,
            EntryTime        = candle.Time,
            EntryPrice       = candle.Close,
            Sl               = sl,
            Tp               = tp,
            RiskDollars      = Math.Round(riskDollars, 2),
            LotSize          = lotSize,
            CorrelationId    = Guid.NewGuid().ToString(),
        };

        open.Add(new ReplayTrade
        {
            Record    = record,
            Direction = direction,
            Entry     = candle.Close,
            Sl        = sl,
            Tp        = tp,
            LotSize   = lotSize,
        });

        OnTradeOpened?.Invoke(record);
    }

    // ── SL/TP Standards ───────────────────────────────────────────────────────

    private static double CalcStdSl(StopLossRule r, string dir, IndicatorValues iv, double pip)
    {
        double buf = r.BufferPips * pip;
        return r.Type switch
        {
            StopLossType.BelowCloud => Math.Min(iv.SenkouA, iv.SenkouB) - buf,
            StopLossType.AboveCloud => Math.Max(iv.SenkouA, iv.SenkouB) + buf,
            StopLossType.Fixed      => dir == "BUY" ? iv.Close - r.BufferPips * pip
                                                    : iv.Close + r.BufferPips * pip,
            _ => dir == "BUY" ? iv.Low - buf : iv.High + buf,
        };
    }

    private static double CalcStdTp(TakeProfitRule r, string dir, double entry, double sl)
    {
        double dist = Math.Abs(entry - sl);
        return dir == "BUY" ? entry + dist * r.Ratio : entry - dist * r.Ratio;
    }

    // ── Interne ───────────────────────────────────────────────────────────────

    private sealed class ReplayTrade
    {
        public required TradeRecord Record    { get; init; }
        public required string      Direction { get; init; }
        public required double      Entry     { get; init; }
        public required double      Sl        { get; init; }
        public required double      Tp        { get; init; }
        public required double      LotSize   { get; init; }
    }

    private sealed class ReplayState
    {
        public double Capital { get; set; }
        public double Peak    { get; set; }
        public int    Wins    { get; set; }
        public int    Losses  { get; set; }
    }
}
