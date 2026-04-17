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

    /// <summary>% du capital risqué par trade — vient du SettingsPage GLOBAL du bot.</summary>
    public required decimal RiskPercent    { get; init; }

    /// <summary>
    /// Commission $ par lot par côté (entrée + sortie comptés séparément).
    /// IC Markets ≈ 3.5 USD/lot/side. Vient du SettingsPage GLOBAL du bot.
    /// </summary>
    public required decimal CommissionPerLotPerSide { get; init; }

    /// <summary>
    /// Mode Tick : si true, le Replay charge les ticks MT5 réels pour le symbole
    /// affiché et vérifie SL/TP tick-par-tick (précision intra-bougie).
    /// Si false (défaut), SL/TP vérifiés sur OHLC de bougie (comportement V1).
    /// </summary>
    public bool UseTicks                   { get; init; }
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
    private readonly SymbolMetaCache?  _metaCache;

    // ── Callbacks UI ──────────────────────────────────────────────────────────
    public event Action<Candle, IndicatorValues?, IndicatorValues?, IndicatorValues?, DateTimeOffset?>? OnCandleProcessed;
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

    // Streaming ticks : protège les List<Tick> pendant que le BG append des jours
    private readonly object _ticksLock = new();
    // Combien de jours de ticks ont été chargés en mémoire (0 = rien, 1 = day 1 OK, etc.)
    private volatile int _tickLoadedDayCount;

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

    /// <summary>
    /// Expose le DuckDBReader (lecture seule) pour la popup détail de trade
    /// qui doit charger les bougies autour de l'entrée/sortie pour les afficher.
    /// </summary>
    public DuckDBReader Reader => _reader;

    /// <summary>
    /// Lookup synchrone d'une SymbolMeta déjà cachée (null sinon).
    /// Utilisé par la popup trade pour connaître Digits/etc. sans appel réseau.
    /// </summary>
    public SymbolMeta? TryGetCachedMeta(string symbol)
        => _metaCache?.TryGetCached(symbol);

    public ReplayService(
        DuckDBReader reader,
        TradeRepository? tradeRepo = null,
        MT5ApiClient? mt5 = null,
        SymbolMetaCache? metaCache = null)
    {
        _reader    = reader;
        _tradeRepo = tradeRepo;
        _mt5       = mt5;
        _metaCache = metaCache ?? (mt5 != null ? new SymbolMetaCache(mt5) : null);
        RefreshSymbols();
    }

    /// <summary>
    /// Wipe la DB replay_trades.db. Appelé au stop replay et à la fermeture
    /// de l'app — la DB replay est éphémère par design.
    /// </summary>
    public Task WipeReplayTradesAsync()
        => _tradeRepo?.WipeReplayAsync() ?? Task.CompletedTask;

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
        if (snapshot == null || !snapshot.TryGetValue(symbol, out _)) return null;
        return BuildHistoryJsonFromSnapshot(symbol, tf, from, upTo, strategy, ct, snapshot);
    }

    /// <summary>
    /// Même rendu que BuildHistoryJsonUpTo mais charge les bougies depuis DuckDB.
    /// Utilisé par la Page Historique où les données live ne sont pas chargées en mémoire
    /// (aucun replay actif). Charge uniquement le TF du chart + H1 + H4 + D pour les pivots.
    /// </summary>
    public string? BuildHistoryJsonFromDb(
        string symbol, string tf, DateTimeOffset from, DateTimeOffset upTo,
        IStrategy? strategy, CancellationToken ct)
    {
        // TFs minimum : chart TF + H1 + H4 + D pour les pivots Ichimoku
        var tfsNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tf, "H1", "H4", "D" };
        var loadFrom  = from.AddDays(-60);   // 60 jours de warmup D + Ichimoku
        var queries   = tfsNeeded.Select(t => (t, loadFrom, upTo)).ToList();

        try
        {
            var batch = _reader.GetAllCandlesBatch([symbol], queries);
            if (!batch.TryGetValue(symbol, out var tfMap) || !tfMap.ContainsKey(tf))
                return null;
            return BuildHistoryJsonFromSnapshot(symbol, tf, from, upTo, strategy, ct, batch);
        }
        catch (Exception ex)
        {
            ReplayLogger.LogException("BuildHistoryJsonFromDb", ex);
            return null;
        }
    }

    // ── Rendu commun (mémoire OU DuckDB) ─────────────────────────────────────

    private string? BuildHistoryJsonFromSnapshot(
        string symbol, string tf, DateTimeOffset from, DateTimeOffset upTo,
        IStrategy? strategy, CancellationToken ct,
        Dictionary<string, Dictionary<string, List<Candle>>> snapshot)
    {
        if (!snapshot.TryGetValue(symbol, out var tfMap)) return null;
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
        var bPivotPP      = new List<object>();
        var bPivotR1      = new List<object>();
        var bPivotR2      = new List<object>();
        var bPivotS1      = new List<object>();
        var bPivotS2      = new List<object>();
        var bPivotH1PP    = new List<object>();
        var bPivotH1R1    = new List<object>();
        var bPivotH1R2    = new List<object>();
        var bPivotH1S1    = new List<object>();
        var bPivotH1S2    = new List<object>();
        bool showH1Pivots = ShouldShowH1Pivots(strategy);

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

                var dailyPivotIv = BuildPivotIndicators(snapshot, symbol, "D", candle.Time);
                if (dailyPivotIv is { PivotPP: > 0 })
                    AddPivotPoint(ts, dailyPivotIv, bPivotPP, bPivotR1, bPivotR2, bPivotS1, bPivotS2);

                if (showH1Pivots)
                {
                    var h1PivotIv = BuildPivotIndicators(snapshot, symbol, "H1", candle.Time);
                    if (h1PivotIv is { PivotPP: > 0 })
                        AddPivotPoint(ts, h1PivotIv, bPivotH1PP, bPivotH1R1, bPivotH1R2, bPivotH1S1, bPivotH1S2);
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
            pivotPP      = bPivotPP,
            pivotR1      = bPivotR1,
            pivotR2      = bPivotR2,
            pivotS1      = bPivotS1,
            pivotS2      = bPivotS2,
            pivotH1PP    = bPivotH1PP,
            pivotH1R1    = bPivotH1R1,
            pivotH1R2    = bPivotH1R2,
            pivotH1S1    = bPivotH1S1,
            pivotH1S2    = bPivotH1S2,
        });
    }

    // ── Main Loop ──────────────────────────────────────────────────────────────

    public async Task StartAsync(ReplayConfig cfg, CancellationToken ct)
    {
        ReplayLogger.Log("========== StartAsync BEGIN ==========");
        ReplayLogger.Log($"Config: Symbol={cfg.Symbol}, TF={cfg.Timeframe}, From={cfg.From}, To={cfg.To}, Capital={cfg.StartingCapital}, Speed={cfg.Speed}, Strategy={cfg.Strategy?.Name ?? "NULL"}");
        IsRunning = true;

        // ── Lazy fetch MT5 → DuckDB ───────────────────────────────────────────
        // FAST PATH : si la DB locale couvre déjà [From, To] pour CE symbol sur les TFs
        // que la Strategy utilise vraiment (RequiredTimeframes, pas AllTimeframes), on
        // skip complètement l'appel MT5 (qui itère 36 symbols × 6 TFs et peut prendre
        // plusieurs minutes même quand tout est en cache).
        var requiredTfs = cfg.Strategy?.RequiredTimeframes is { Count: > 0 } reqs
            ? reqs
            : new List<string> { cfg.Timeframe };

        OnLoadingStatus?.Invoke($"Vérification cache local ({AvailableSymbols.Count} symboles)…");
        bool hasCache = _reader.HasFullCoverage(AvailableSymbols, requiredTfs, cfg.From, cfg.To);

        if (hasCache)
        {
            ReplayLogger.Log($"FAST PATH: DB locale couvre déjà {AvailableSymbols.Count} symbols × [{string.Join(",", requiredTfs)}] sur la range — skip ensure_candles_range");
            OnLoadingStatus?.Invoke("Cache local OK — chargement DuckDB…");
        }
        else if (_mt5 != null)
        {
            try
            {
                ReplayLogger.Log("Starting MT5 EnsureCandlesRangeAsync...");
                OnLoadingStatus?.Invoke("Téléchargement MT5 en cours…");

                // Lance le download et le polling de progrès en parallèle.
                // Le endpoint Python /ensure_candles_range tourne dans un thread pool
                // (def, pas async def) → l'event loop Python reste libre pour répondre
                // aux requêtes /download_progress toutes les ~1s.
                var downloadTask = _mt5.EnsureCandlesRangeAsync(cfg.From, cfg.To, AllTimeframes, ct);

                using var pollCts = new CancellationTokenSource();
                var mt5Client     = _mt5;   // capture pour la lambda
                var loadingStatus = OnLoadingStatus; // capture event
                var pollTask      = Task.Run(async () =>
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                    while (await timer.WaitForNextTickAsync(pollCts.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            var (current, index, total) =
                                await mt5Client.GetDownloadProgressAsync(pollCts.Token).ConfigureAwait(false);
                            if (total > 0 && !string.IsNullOrEmpty(current))
                                loadingStatus?.Invoke($"Téléchargement MT5 — {current} ({index}/{total})…");
                        }
                        catch { /* réseau ou annulation — silencieux */ }
                    }
                }, pollCts.Token);

                var result = await downloadTask.ConfigureAwait(false);
                pollCts.Cancel();
                try { await pollTask.ConfigureAwait(false); } catch (OperationCanceledException) { }

                ReplayLogger.Log($"EnsureCandlesRange done: {result.TotalSymbols} symbols, {result.TotalInserted} inserted, {result.TotalCached} cached, {result.ElapsedSec:F1}s");
                OnLoadingStatus?.Invoke(
                    $"MT5 → DuckDB : {result.TotalSymbols} symboles, " +
                    $"{result.TotalInserted:N0} nouvelles bougies, " +
                    $"{result.TotalCached:N0} en cache ({result.ElapsedSec:F1}s) — chargement DuckDB…");

                RefreshSymbols();
                ReplayLogger.Log($"After RefreshSymbols: {AvailableSymbols.Count} symbols");
            }
            catch (Exception ex)
            {
                ReplayLogger.LogException("EnsureCandlesRangeAsync", ex);
                OnLoadingStatus?.Invoke($"Erreur MT5 : {ex.Message} — lecture DB locale…");
            }
        }
        else
        {
            ReplayLogger.Log("_mt5 is null — skipping lazy fetch");
            OnLoadingStatus?.Invoke("Chargement DuckDB…");
        }

        // ── Mode Tick : streaming load (jour 1 sync, reste en background) ─────
        // Voir bloc plus bas après allBySymbolAndTf — on a besoin de allTicksBySymbol
        // pour passer en référence au BG task. Le fetch + load du jour 1 est fait
        // juste après LoadAllSymbolsAllTfsAsync.

        // Charger TOUS les symboles × TOUS les TFs en parallèle
        Dictionary<string, Dictionary<string, List<Candle>>> allBySymbolAndTf;
        try
        {
            ReplayLogger.Log("Loading all symbols/TFs from DuckDB...");
            OnLoadingStatus?.Invoke($"DuckDB — {AvailableSymbols.Count} symboles × {AllTimeframes.Length} TFs (1 connexion, {AllTimeframes.Length} requêtes)…");
            allBySymbolAndTf = await LoadAllSymbolsAllTfsAsync(cfg.From, cfg.To, ct);
            ReplayLogger.Log($"Loaded {allBySymbolAndTf.Count} symbols from DuckDB");

            int totalCandles = allBySymbolAndTf.Values.Sum(t => t.Values.Sum(c => c.Count));
            OnLoadingStatus?.Invoke($"DuckDB OK — {allBySymbolAndTf.Count} symboles, {totalCandles:N0} bougies…");
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

        // ── Pré-charger SymbolMeta pour tous les symboles tradés ──
        // Indispensable : RiskEngine et ExecutionManager les lisent SYNCHRONEMENT
        // (lookup cache TryGetCached). Sans preload, lot=0 et P&L=0.
        if (_metaCache != null)
        {
            OnLoadingStatus?.Invoke("Chargement métadonnées MT5…");
            try
            {
                await _metaCache.PreloadAsync(allBySymbolAndTf.Keys, ct).ConfigureAwait(false);
                ReplayLogger.Log($"SymbolMeta preloaded for {allBySymbolAndTf.Count} symbols");
            }
            catch (Exception ex)
            {
                ReplayLogger.LogException("SymbolMeta preload", ex);
            }
        }
        else
        {
            ReplayLogger.Log("WARNING: SymbolMetaCache is NULL — lot sizing & P&L seront à 0 (pas de MT5).");
        }

        ReplayLogger.Log("All data loaded → starting replay loop");
        OnLoadingStatus?.Invoke("");   // Clear le message → replay démarre

        // Merge toutes les bougies de tous les symboles et TFs, triées par temps global
        var allCandles = allBySymbolAndTf.Values
            .SelectMany(tfMap => tfMap.Values.SelectMany(c => c))
            .OrderBy(c => c.Time)
            .ToList();

        if (allCandles.Count == 0) { IsRunning = false; return; }

        // ── Mode Tick : streaming jour-par-jour ────────────────────────────────
        // Jour 1 sync (replay démarre vite) ; jours 2..N en background pendant que
        // le replay joue. Le replay attend si une bougie dépasse l'horizon chargé.
        var allTicksBySymbol      = new Dictionary<string, List<Tick>>(StringComparer.OrdinalIgnoreCase);
        var tickCursorBySymbol    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var displayCursorBySymbol = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _tickLoadedDayCount       = 0;

        int totalTickDays = 0;
        if (cfg.UseTicks)
        {
            var tickSymbols  = AvailableSymbols.ToArray();
            totalTickDays    = (int)Math.Ceiling((cfg.To - cfg.From).TotalDays);
            if (totalTickDays < 1) totalTickDays = 1;

            ReplayLogger.Log($"Mode Tick streaming: {tickSymbols.Length} symbols × {totalTickDays} days");

            // Jour 1 sync
            await LoadTicksForDayAsync(0, cfg, tickSymbols,
                allTicksBySymbol, tickCursorBySymbol, displayCursorBySymbol, ct)
                .ConfigureAwait(false);

            // Jours 2..N en background — fire-and-forget (catch interne)
            if (totalTickDays > 1)
            {
                var bgSymbols = tickSymbols;
                var bgCfg     = cfg;
                _ = Task.Run(async () =>
                {
                    for (int day = 1; day < totalTickDays; day++)
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            await LoadTicksForDayAsync(day, bgCfg, bgSymbols,
                                allTicksBySymbol, tickCursorBySymbol, displayCursorBySymbol, ct)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ReplayLogger.LogException($"BG LoadTicksForDay({day})", ex);
                        }
                    }
                    ReplayLogger.Log($"BG tick streaming complete: {_tickLoadedDayCount}/{totalTickDays} days");
                }, ct);
            }
        }

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
        bool showH1Pivots = ShouldShowH1Pivots(cfg.Strategy);

        var engine   = new IndicatorEngine(new EventBus());
        var swings   = new SwingPointEngine();
        var trendEng = new TrendEngine(new EventBus(), engine, swings);
        // ── Moteur de trade — identique Live/Replay ──────────────────────────────
        double capital       = cfg.StartingCapital;
        double peak          = cfg.StartingCapital;
        int    wins          = 0;
        int    losses        = 0;
        double dailyDrawdown = 0;
        DateOnly? currentRiskDay = null;
        double dayPeak = cfg.StartingCapital;

        var replayBus = new EventBus();
        var riskEng   = new RiskEngine();
        var simExec   = new SimulatedOrderExecutor();

        Func<TradeRecord, Task> saveTrade = record =>
        {
            if (record.ErrorLog != null) return Task.CompletedTask;
            if (!record.ExitPrice.HasValue)
            {
                OnTradeOpened?.Invoke(record);
            }
            else
            {
                double pl = record.ProfitLoss ?? 0;
                capital  += pl;
                if (capital > peak) peak = capital;
                if (capital > dayPeak) dayPeak = capital;
                if (pl >= 0) wins++; else losses++;
                OnTradeClosed?.Invoke(record);
                if (_tradeRepo != null)
                    return _tradeRepo.SaveTradeAsync(record, isReplay: true);
            }
            return Task.CompletedTask;
        };

        // Provider de métadonnées MT5 (lazy-cache via SymbolMetaCache, lookup synchrone après preload)
        Func<string, SymbolMeta?> metaProvider = sym => _metaCache?.TryGetCached(sym);

        var execMgr  = new ExecutionManager(
            replayBus, simExec, riskEng, saveTrade,
            metaProvider, (double)cfg.CommissionPerLotPerSide);
        var tradeMgr = new TradeManager(
            replayBus, engine, trendEng, execMgr, riskEng,
            metaProvider, (double)cfg.CommissionPerLotPerSide);

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
                // TF d'évaluation = TF propre de la stratégie (M15 pour IntraDay, etc.)
                // PAS le TF d'affichage sélectionné dans le dropdown UI.
                string stratTf    = cfg.Strategy?.Timeframe ?? cfg.Timeframe;
                bool isStrategyTf = candle.Timeframe == stratTf;

                // ── Warmup : nourrir les engines + envoyer les display au chart ──
                // Les bougies warmup du (symbol, TF) affiché sont envoyées au chart
                // SANS délai pour que Ichimoku soit déjà visible au démarrage du replay.
                // Les bougies des autres symbols/TFs alimentent les engines en silence.
                if (candle.Time < cfg.From)
                {
                    if (isDisplay)
                    {
                        var warmIv = engine.GetIndicators(candle.Symbol, candle.Timeframe);
                        var warmDailyPivotIv = BuildPivotIndicators(allBySymbolAndTf, candle.Symbol, "D", candle.Time);
                        var warmH1PivotIv = showH1Pivots
                            ? BuildPivotIndicators(allBySymbolAndTf, candle.Symbol, "H1", candle.Time)
                            : null;
                        var warmFt = ComputeFutureTime(allBySymbolAndTf, candle, curIdx);
                        OnCandleProcessed?.Invoke(candle, warmIv, warmDailyPivotIv, warmH1PivotIv, warmFt);
                    }
                    continue;
                }

                Interlocked.Exchange(ref _currentReplayTimestamp, candle.Time.ToUnixTimeSeconds());

                // ── Mode Tick streaming : attendre que le BG ait chargé ce jour ──
                var riskDay = DateOnly.FromDateTime(candle.Time.DateTime);
                if (currentRiskDay != riskDay)
                {
                    currentRiskDay = riskDay;
                    dayPeak = capital;
                }
                if (capital > dayPeak) dayPeak = capital;
                dailyDrawdown = dayPeak > 0
                    ? Math.Max(0, (dayPeak - capital) / dayPeak * 100.0)
                    : 0;

                if (cfg.UseTicks && totalTickDays > 0)
                {
                    int neededDay = (int)Math.Floor((candle.Time - cfg.From).TotalDays) + 1;
                    if (neededDay > totalTickDays) neededDay = totalTickDays;
                    bool warned = false;
                    while (_tickLoadedDayCount < neededDay && !ct.IsCancellationRequested)
                    {
                        if (!warned)
                        {
                            OnLoadingStatus?.Invoke($"Attente ticks jour {neededDay}/{totalTickDays} (chargé {_tickLoadedDayCount})…");
                            warned = true;
                        }
                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                    if (warned) OnLoadingStatus?.Invoke("");
                }

                var iv = engine.GetIndicators(candle.Symbol, candle.Timeframe);

                // ── Évaluation stratégie ──────────────────────────────────────
                // La stratégie tourne sur CHAQUE symbole (bougies du TF cfg.Timeframe).
                // Elle peut ouvrir/fermer des trades sur n'importe quel symbole.
                if (isStrategyTf && cfg.Strategy != null && iv != null)
                {
                    // Mode Tick : slice les ticks pour cette bougie (par symbole)
                    // Lock car le BG task peut faire AddRange sur la List<Tick> en parallèle.
                    IReadOnlyList<Tick>? candleTicks = null;
                    if (cfg.UseTicks)
                    {
                        lock (_ticksLock)
                        {
                            if (allTicksBySymbol.TryGetValue(candle.Symbol, out var symTicks))
                            {
                                var candleEnd = candle.Time.AddSeconds(TfSeconds(candle.Timeframe));
                                int cur = tickCursorBySymbol[candle.Symbol];
                                while (cur < symTicks.Count && symTicks[cur].Time < candle.Time)
                                    cur++;
                                tickCursorBySymbol[candle.Symbol] = cur;
                                var slice = new List<Tick>();
                                int scan  = cur;
                                while (scan < symTicks.Count && symTicks[scan].Time < candleEnd)
                                {
                                    slice.Add(symTicks[scan]);
                                    scan++;
                                }
                                candleTicks = slice;
                            }
                        }
                    }

                    // Délègue à StrategyEvaluator — source unique, identique Live
                    // condCb : on émet les conditions sur le TF STRATÉGIE du symbole affiché
                    // (pas le TF display — la stratégie tourne sur M1, pas H1 ou M15)
                    bool isCondDisplay = candle.Symbol == displaySym;
                    Action<long, List<(string, bool)>, List<(string, bool)>>? condCb = isCondDisplay
                        ? (ts, l, s) => OnConditionsEvaluated?.Invoke(ts, l, s)
                        : null;

                    await StrategyEvaluator.EvaluateAsync(
                        candle, iv, cfg.Strategy, engine, trendEng,
                        tradeMgr, riskEng, capital, dailyDrawdown,
                        cfg.RiskPercent, metaProvider, ct,
                        conditionsCallback: condCb,
                        ticks: candleTicks);
                }

                // ── Envoi au chart : bougies du symbole+TF display ─────────────
                bool tickAnimated = false;
                if (isDisplay)
                {
                    var ft = ComputeFutureTime(allBySymbolAndTf, candle, curIdx);
                    var dailyPivotIv = BuildPivotIndicators(allBySymbolAndTf, candle.Symbol, "D", candle.Time);
                    var h1PivotIv = showH1Pivots
                        ? BuildPivotIndicators(allBySymbolAndTf, candle.Symbol, "H1", candle.Time)
                        : null;

                    // Mode Tick : animer la bougie tick-par-tick (OHLC progressifs)
                    // Snapshot sous lock (le BG task peut AddRange en parallèle), puis
                    // on itère hors lock car y'a des await Task.Delay.
                    if (cfg.UseTicks)
                    {
                        List<Tick>? snapshot = null;
                        lock (_ticksLock)
                        {
                            if (allTicksBySymbol.TryGetValue(candle.Symbol, out var dispTicks))
                            {
                                var candleEnd = candle.Time.AddSeconds(TfSeconds(candle.Timeframe));
                                int dispCur = displayCursorBySymbol[candle.Symbol];
                                while (dispCur < dispTicks.Count && dispTicks[dispCur].Time < candle.Time)
                                    dispCur++;
                                displayCursorBySymbol[candle.Symbol] = dispCur;
                                int sliceStart = dispCur;
                                int sliceEnd   = sliceStart;
                                while (sliceEnd < dispTicks.Count && dispTicks[sliceEnd].Time < candleEnd)
                                    sliceEnd++;
                                if (sliceEnd > sliceStart)
                                {
                                    snapshot = new List<Tick>(sliceEnd - sliceStart);
                                    for (int i = sliceStart; i < sliceEnd; i++)
                                        snapshot.Add(dispTicks[i]);
                                }
                            }
                        }

                        if (snapshot is { Count: > 0 })
                        {
                            double tickOpen  = snapshot[0].Bid;
                            double tickHigh  = tickOpen;
                            double tickLow   = tickOpen;
                            double tickClose = tickOpen;
                            int    perTickDelay = Math.Max(0, delayMs / snapshot.Count);

                            for (int i = 0; i < snapshot.Count; i++)
                            {
                                if (ct.IsCancellationRequested) break;

                                var t = snapshot[i];
                                if (t.Bid > tickHigh) tickHigh = t.Bid;
                                if (t.Bid < tickLow)  tickLow  = t.Bid;
                                tickClose = t.Bid;

                                var partial = new Candle
                                {
                                    Symbol    = candle.Symbol,
                                    Timeframe = candle.Timeframe,
                                    Time      = candle.Time,
                                    Open      = tickOpen,
                                    High      = tickHigh,
                                    Low       = tickLow,
                                    Close     = tickClose,
                                    Volume    = candle.Volume,
                                };
                                OnCandleProcessed?.Invoke(partial, iv, dailyPivotIv, h1PivotIv, ft);

                                if (perTickDelay > 0)
                                    await Task.Delay(perTickDelay, ct).ConfigureAwait(false);
                            }
                            // Bougie finale (vraies valeurs OHLC du DB)
                            OnCandleProcessed?.Invoke(candle, iv, dailyPivotIv, h1PivotIv, ft);
                            tickAnimated = true;
                        }
                    }

                    if (!tickAnimated)
                        OnCandleProcessed?.Invoke(candle, iv, dailyPivotIv, h1PivotIv, ft);

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
                    double dd = peak > 0 ? Math.Max(0, (peak - capital) / peak * 100.0) : 0;
                    OnStatsUpdate?.Invoke(capital, wins, losses, dd);
                }

                // Pause
                while (cfg.IsPaused?.Invoke() == true && !ct.IsCancellationRequested)
                    await Task.Delay(50, ct).ConfigureAwait(false);

                // ── Delay visuel : seulement sur la bougie display ────────────
                // Le rythme visuel suit _liveDisplayTf (qui peut changer dynamiquement).
                // Les bougies non-display passent à pleine vitesse (alimentation des engines).
                // En Mode Tick, le delay est déjà appliqué par tick (perTickDelay) → skip.
                if (isDisplay && delayMs > 0 && !tickAnimated)
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

    private static bool ShouldShowH1Pivots(IStrategy? strategy)
        => strategy?.Settings.TryGetValue("Mode", out var mode) == true &&
           string.Equals(mode?.ToString(), "ScalpingGourmand", StringComparison.OrdinalIgnoreCase);

    private static IndicatorValues? BuildPivotIndicators(
        Dictionary<string, Dictionary<string, List<Candle>>> all,
        string symbol,
        string pivotTf,
        DateTimeOffset time)
    {
        if (!all.TryGetValue(symbol, out var tfMap)) return null;
        if (!tfMap.TryGetValue(pivotTf, out var candles) || candles.Count == 0) return null;

        var source = FindPreviousCandle(candles, time);
        if (source is null) return null;

        double pp = (source.High + source.Low + source.Close) / 3.0;
        if (pp <= 0) return null;

        double range = source.High - source.Low;
        return new IndicatorValues
        {
            PivotPP = pp,
            PivotR1 = 2.0 * pp - source.Low,
            PivotR2 = pp + range,
            PivotS1 = 2.0 * pp - source.High,
            PivotS2 = pp - range,
        };
    }

    private static Candle? FindPreviousCandle(IReadOnlyList<Candle> candles, DateTimeOffset time)
    {
        int lo = 0;
        int hi = candles.Count - 1;
        int best = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (candles[mid].Time < time)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best >= 0 ? candles[best] : null;
    }

    private static void AddPivotPoint(
        long ts,
        IndicatorValues pivot,
        List<object> pp,
        List<object> r1,
        List<object> r2,
        List<object> s1,
        List<object> s2)
    {
        pp.Add(new { time = ts, value = pivot.PivotPP });
        r1.Add(new { time = ts, value = pivot.PivotR1 });
        r2.Add(new { time = ts, value = pivot.PivotR2 });
        s1.Add(new { time = ts, value = pivot.PivotS1 });
        s2.Add(new { time = ts, value = pivot.PivotS2 });
    }

    /// <summary>
    /// Charge les ticks d'UN jour pour tous les symbols (Python fetch + DuckDB read +
    /// merge dans la mémoire partagée sous _ticksLock). Incrémente _tickLoadedDayCount
    /// quand le jour est OK. Utilisé en sync pour day 0 et en background pour day 1..N.
    /// </summary>
    private async Task LoadTicksForDayAsync(
        int dayIndex,
        ReplayConfig cfg,
        string[] symbols,
        Dictionary<string, List<Tick>> allTicksBySymbol,
        Dictionary<string, int>        tickCursorBySymbol,
        Dictionary<string, int>        displayCursorBySymbol,
        CancellationToken ct)
    {
        var dayFrom = cfg.From.AddDays(dayIndex);
        var dayTo   = dayFrom.AddDays(1);
        if (dayTo > cfg.To) dayTo = cfg.To;
        if (dayFrom >= cfg.To) { _tickLoadedDayCount = dayIndex + 1; return; }

        // 1. Python : ensure_ticks_range pour ce jour (idempotent — cache hit si déjà fait)
        if (_mt5 != null)
        {
            try
            {
                var r = await _mt5.EnsureTicksRangeAsync(dayFrom, dayTo, symbols, ct)
                    .ConfigureAwait(false);
                ReplayLogger.Log($"Ticks day {dayIndex+1}: {r.TotalInserted:N0} new, {r.TotalCached:N0} cached, {r.ElapsedSec:F1}s");
            }
            catch (Exception ex)
            {
                ReplayLogger.LogException($"EnsureTicksRangeAsync(day {dayIndex+1})", ex);
                // On continue — peut-être que des ticks sont déjà en DB
            }
        }

        // 2. DuckDB : lire ce jour pour tous les symbols et append en mémoire
        long appended = 0;
        foreach (var sym in symbols)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var t = _reader.GetTicksInRange(sym, dayFrom, dayTo);
                if (t.Count == 0) continue;

                lock (_ticksLock)
                {
                    if (!allTicksBySymbol.TryGetValue(sym, out var list))
                    {
                        allTicksBySymbol[sym]      = t;
                        tickCursorBySymbol[sym]    = 0;
                        displayCursorBySymbol[sym] = 0;
                    }
                    else
                    {
                        list.AddRange(t);
                    }
                }
                appended += t.Count;
            }
            catch (Exception ex)
            {
                ReplayLogger.LogException($"GetTicksInRange({sym}, day {dayIndex+1})", ex);
            }
        }

        // 3. Marquer ce jour comme chargé (le replay loop débloquera la wait)
        _tickLoadedDayCount = dayIndex + 1;
        ReplayLogger.Log($"Day {dayIndex+1} loaded in memory: +{appended:N0} ticks (horizon={_tickLoadedDayCount})");
    }

    /// <summary>
    /// Charge tous les symboles × tous les TFs standards en une seule connexion DuckDB.
    /// 1 requête par TF (6 total) au lieu de N connexions concurrentes (cause du gel 39s).
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, List<Candle>>>>
        LoadAllSymbolsAllTfsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var symbols = AvailableSymbols.ToList();
        if (symbols.Count == 0)
            return new Dictionary<string, Dictionary<string, List<Candle>>>(StringComparer.OrdinalIgnoreCase);

        ReplayLogger.Log($"LoadAllSymbolsAllTfsAsync: {symbols.Count} symbols × {AllTimeframes.Length} TFs, from={from}, to={to}");

        var tfQueries = AllTimeframes
            .Select(tf => (Tf: tf, From: from.AddSeconds(-100 * TfSeconds(tf)), To: to))
            .ToList();

        // Connexion unique, 1 requête par TF — élimine la contention DuckDB
        return await Task.Run(() =>
        {
            if (ct.IsCancellationRequested)
                return new Dictionary<string, Dictionary<string, List<Candle>>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                return _reader.GetAllCandlesBatch(
                    symbols,
                    tfQueries,
                    progressCallback: (tf, n) =>
                    {
                        ReplayLogger.Log($"  Loaded TF={tf}: {n:N0} rows");
                        OnLoadingStatus?.Invoke($"DuckDB {tf} chargé — {n:N0} bougies…");
                    });
            }
            catch (Exception ex)
            {
                ReplayLogger.Log($"GetAllCandlesBatch FAILED: {ex.GetType().Name}: {ex.Message}");
                return new Dictionary<string, Dictionary<string, List<Candle>>>(StringComparer.OrdinalIgnoreCase);
            }
        }, ct).ConfigureAwait(false);
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
    // Déléguée à StrategyEvaluator (ToutieTrader.Core.Engine) — source unique Live + Replay.

    // ── Interne ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Exécuteur simulé pour le Replay.
    /// SendOrderAsync : fill au prix signal.EntryPrice (= Open bougie suivante, injecté par TradeManager).
    /// IsReplay = true : ExecutionManager utilise le chemin simulation (SL/TP/Close de bougie)
    ///            sans jamais appeler CloseOrderAsync.
    /// </summary>
    private sealed class SimulatedOrderExecutor : IOrderExecutor
    {
        public bool IsReplay => true;

        public Task<(long TicketId, double FillPrice, DateTimeOffset FillTime)>
            SendOrderAsync(TradeSignal signal, CancellationToken ct)
            => Task.FromResult((0L, signal.EntryPrice, DateTimeOffset.UtcNow));

        public Task<(double ClosePrice, DateTimeOffset CloseTime)>
            CloseOrderAsync(long ticketId, string symbol, CancellationToken ct)
            => throw new NotSupportedException("SimulatedOrderExecutor: CloseOrderAsync non appelé en Replay");
    }
}
