using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;
using ToutieTrader.UI.Services;
using ToutieTrader.UI.ViewModels;
using ToutieTrader.UI.Windows;

namespace ToutieTrader.UI.Pages;

/// <summary>
/// Page Replay complète — Phase 08.
///
/// Architecture :
///   1. WebView2 charge chart.html via virtual host (https://toutie.local/)
///   2. ReplayService tourne sur un Task background (pas le thread UI)
///   3. Chaque callback ReplayService dispatche vers le Dispatcher UI
///   4. Le chart reçoit des messages JSON via PostWebMessageAsString
/// </summary>
public partial class ReplayPage : Page
{
    private readonly MainViewModel   _vm;
    private readonly ReplayService?  _replay;
    private readonly Services.AppSettings? _settings;

    private int _speed = 1;
    private CancellationTokenSource? _replayCts;
    private CancellationTokenSource? _histCts;
    private bool _chartReady;
    private bool _replayScrollEnabled;
    private bool _replayPaused;
    // Bloque le flush de la queue pendant le rechargement de l'historique mid-replay
    private bool _historyPending;

    // Queue de messages chart — flush toutes les 50ms pour ne pas saturer le Dispatcher
    private readonly ConcurrentQueue<string> _chartQueue = new();
    private readonly DispatcherTimer _chartTimer;

    // Trade list — bound au DataGrid
    private readonly List<TradeRecord> _trades = [];

    public ReplayPage(MainViewModel vm, ReplayService? replay = null, Services.AppSettings? settings = null)
    {
        InitializeComponent();
        _vm       = vm;
        _replay   = replay;
        _settings = settings;

        // Timer UI : vide la queue vers le chart toutes les 50ms
        _chartTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _chartTimer.Tick += (_, _) => FlushChartQueue();
        _chartTimer.Start();

        DpFrom.SelectedDate = vm.ReplayFrom;
        DpTo.SelectedDate   = vm.ReplayTo;
        TxtCapital.Text     = vm.ReplayCapital;

        DpFrom.DateSelected += (_, d) => _vm.ReplayFrom = d;
        DpTo.DateSelected   += (_, d) => _vm.ReplayTo   = d;

        // Sauvegarder le capital dès que l'utilisateur le modifie
        TxtCapital.TextChanged += (_, _) =>
        {
            _vm.ReplayCapital = TxtCapital.Text;  // déclenche auto-save via App.xaml.cs
        };

        // Peupler le dropdown symbole
        if (_replay != null)
        {
            foreach (var sym in _replay.AvailableSymbols)
                CmbSymbol.Items.Add(sym);

            // Restaurer le symbole sauvegardé, sinon premier de la liste
            if (!string.IsNullOrEmpty(settings?.ReplaySymbol) &&
                CmbSymbol.Items.Contains(settings.ReplaySymbol))
                CmbSymbol.SelectedItem = settings.ReplaySymbol;
            else if (CmbSymbol.Items.Count > 0)
                CmbSymbol.SelectedIndex = 0;
        }

        // Peupler le dropdown strategy
        foreach (var s in vm.Strategies)
            CmbStrategy.Items.Add(s.Name);

        // Synchroniser avec la strategy déjà sélectionnée dans le ViewModel
        if (vm.SelectedStrategy != null)
            CmbStrategy.SelectedItem = vm.SelectedStrategy.Name;
        else if (CmbStrategy.Items.Count > 0)
            CmbStrategy.SelectedIndex = 0;

        CmbStrategy.SelectionChanged += CmbStrategy_SelectionChanged;

        // ── Restaurer TF + vitesse depuis les settings sauvegardés ────────────
        if (settings != null)
        {
            // Timeframe
            foreach (ComboBoxItem item in CmbTimeframe.Items)
            {
                if (item.Content?.ToString() == settings.ReplayTimeframe)
                {
                    CmbTimeframe.SelectedItem = item;
                    break;
                }
            }

            // Vitesse
            _speed = settings.ReplaySpeed;
            var speedMap = new Dictionary<int, RadioButton>
            {
                { 1,  SpeedX1  },
                { 2,  SpeedX2  },
                { 4,  SpeedX4  },
                { 8,  SpeedX8  },
                { 16, SpeedX16 },
                { 32, SpeedX32 },
            };
            if (speedMap.TryGetValue(settings.ReplaySpeed, out var rb))
                rb.IsChecked = true;
        }

        // Suivre les changements de strategy depuis la page Strategy
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedStrategy) && vm.SelectedStrategy != null)
            {
                CmbStrategy.SelectionChanged -= CmbStrategy_SelectionChanged;
                CmbStrategy.SelectedItem = vm.SelectedStrategy.Name;
                CmbStrategy.SelectionChanged += CmbStrategy_SelectionChanged;
            }
            if (e.PropertyName == nameof(MainViewModel.Strategies))
            {
                CmbStrategy.Items.Clear();
                foreach (var s in vm.Strategies)
                    CmbStrategy.Items.Add(s.Name);
                if (vm.SelectedStrategy != null)
                    CmbStrategy.SelectedItem = vm.SelectedStrategy.Name;
            }
        };

        // Abonnements callbacks ReplayService
        if (_replay != null)
        {
            _replay.OnCandleProcessed     += OnCandleProcessed;
            _replay.OnProgress            += OnProgress;
            _replay.OnTradeOpened         += OnTradeOpened;
            _replay.OnTradeClosed         += OnTradeClosed;
            _replay.OnStatsUpdate         += OnStatsUpdate;
            _replay.OnConditionsEvaluated += OnConditionsEvaluated;
            _replay.OnTrendsUpdate        += OnTrendsUpdate;
            _replay.OnLoadingStatus       += OnLoadingStatus;
        }

        // Changer symbole ou TF : routé selon l'état du replay
        CmbSymbol.SelectionChanged    += CmbSymbol_SelectionChanged;
        CmbTimeframe.SelectionChanged += CmbTimeframe_SelectionChanged;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CanStartReplay)
                                or nameof(MainViewModel.CanPauseReplay)
                                or nameof(MainViewModel.IsReplayRunning))
                UpdateButtonStates();
        };


        Loaded += async (_, _) =>
        {
            ReplayLogger.Log($"ReplayPage Loaded — _replay={(_replay == null ? "NULL" : "OK")}, log path: {ReplayLogger.LogPath}");
            await InitChartAsync();
            ReplayLogger.Log($"InitChartAsync done, _chartReady={_chartReady}");

            // Si candles.db est vide (première utilisation ou wipe récent),
            // on fetch la watchlist MT5 pour populer CmbSymbol — sinon l'utilisateur
            // ne peut rien sélectionner et Start est bloqué.
            ReplayLogger.Log($"Before watchlist fetch: CmbSymbol.Items.Count={CmbSymbol.Items.Count}, AvailableSymbols.Count={_replay?.AvailableSymbols.Count ?? -1}");
            if (_replay != null && CmbSymbol.Items.Count == 0)
            {
                ReplayLogger.Log("CmbSymbol empty → calling PopulateSymbolsFromMT5Async");
                var populated = await _replay.PopulateSymbolsFromMT5Async();
                ReplayLogger.Log($"PopulateSymbolsFromMT5Async returned {populated}, AvailableSymbols.Count={_replay.AvailableSymbols.Count}");
                if (populated)
                {
                    CmbSymbol.Items.Clear();
                    foreach (var sym in _replay.AvailableSymbols)
                        CmbSymbol.Items.Add(sym);

                    // Restaurer le symbole sauvegardé, sinon premier de la liste
                    if (!string.IsNullOrEmpty(_settings?.ReplaySymbol) &&
                        CmbSymbol.Items.Contains(_settings.ReplaySymbol))
                        CmbSymbol.SelectedItem = _settings.ReplaySymbol;
                    else if (CmbSymbol.Items.Count > 0)
                        CmbSymbol.SelectedIndex = 0;

                    ReplayLogger.Log($"CmbSymbol populated: {CmbSymbol.Items.Count} items, SelectedIndex={CmbSymbol.SelectedIndex}");
                }
                else
                {
                    ReplayLogger.Log("WARNING: PopulateSymbolsFromMT5Async returned false — dropdown stays empty, Start will silently fail");
                }
            }
            else
            {
                ReplayLogger.Log($"Skipped watchlist fetch: _replay={(_replay == null ? "NULL" : "OK")}, items={CmbSymbol.Items.Count}");
            }

            // Chart volontairement vide jusqu'au clic sur Start — pas de preload.
            // L'utilisateur doit avoir le temps de configurer ses settings sans
            // qu'aucune donnée ne défile ou ne s'affiche.
        };
        UpdateButtonStates();
    }

    // ── Initialisation WebView2 ───────────────────────────────────────────────

    private async Task InitChartAsync()
    {
        try
        {
            await Chart.EnsureCoreWebView2Async();

            string chartDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart");

            Chart.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "toutie.local", chartDir,
                CoreWebView2HostResourceAccessKind.Allow);

            Chart.Source = new Uri("https://toutie.local/chart.html");
            _chartReady  = true;
        }
        catch (Exception ex)
        {
            // WebView2 runtime non installé — afficher un message
            Dispatcher.Invoke(() =>
            {
                var tb = new TextBlock
                {
                    Text       = $"WebView2 runtime requis (Edge WebView2 Evergreen).\n{ex.Message}",
                    Foreground = System.Windows.Media.Brushes.OrangeRed,
                    FontSize   = 12,
                    Margin     = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap,
                };
                // Remplacer le WebView2 par le message d'erreur
                var grid = (Grid)Content;
                Grid.SetRow(tb, 1);
                grid.Children.Remove(Chart);
                grid.Children.Add(tb);
            });
        }
    }

    // ── Callbacks ReplayService (background thread → Dispatcher) ─────────────

    private void OnCandleProcessed(ToutieTrader.Core.Models.Candle candle,
                                   ToutieTrader.Core.Models.IndicatorValues? iv,
                                   ToutieTrader.Core.Models.IndicatorValues? dailyPivotIv,
                                   ToutieTrader.Core.Models.IndicatorValues? h1PivotIv,
                                   DateTimeOffset? futureCandleTime)
    {
        if (!_chartReady) return;
        // Le service ne nous envoie de bougies que pour (displaySymbol, displayTf) —
        // mais on filtre par sécurité au cas où la queue contiendrait encore des
        // bougies post-switch.
        if (_replay != null &&
            (candle.Symbol != _replay.LiveDisplaySymbol || candle.Timeframe != _replay.LiveDisplayTimeframe))
            return;

        // Shift to "fake UTC" Québec — TradingView v3.8.0 affiche Unix en UTC par défaut,
        // en faisant passer l'heure Québec wall-clock comme UTC on obtient l'affichage correct.
        long ts = TimeZoneHelper.ToChartUnixSeconds(candle.Time);

        // Timestamps réels des bougies ±26 — évite les gaps weekend dans la time scale TradingView.
        // L'arithmétique (ts ± 26*tfSeconds) atterrit sur des samedis/dimanches sans données forex,
        // ce qui insère des "slots" vides dans la time scale et crée des sauts visuels entre batches.
        long futureTs = futureCandleTime.HasValue ? TimeZoneHelper.ToChartUnixSeconds(futureCandleTime.Value) : 0;
        long chikouTs = (iv != null && iv.ChikouCandleTime != default) ? TimeZoneHelper.ToChartUnixSeconds(iv.ChikouCandleTime) : 0;

        // ── Log chaque bougie envoyée au chart ──────────────────────────────
        // Écrit dans logs/chart_candles.log — colonne : TF | QC time | chartUnix | O | H | L | C | Dir
        string dir = candle.Close >= candle.Open ? "UP" : "DN";
        Services.ChartCandleLogger.Log(
            candle.Timeframe,
            candle.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            ts,
            candle.Open, candle.High, candle.Low, candle.Close,
            dir);

        // Enqueue dans la queue — le timer flushe toutes les 50ms
        _chartQueue.Enqueue(JsonSerializer.Serialize(new
        {
            type = "candle",
            data = new { time = ts, open = candle.Open, high = candle.High, low = candle.Low, close = candle.Close }
        }));

        if (iv != null)
        {
            var dailyPivots = dailyPivotIv ?? iv;
            _chartQueue.Enqueue(JsonSerializer.Serialize(new
            {
                type = "indicator",
                data = new
                {
                    time       = ts,
                    tenkan     = iv.Tenkan,
                    kijun      = iv.Kijun,
                    senkouA    = iv.SenkouA,
                    senkouB    = iv.SenkouB,
                    chikouTime = chikouTs,
                    chikou     = iv.Chikou,
                    futureTime = futureTs,
                    senkouA26  = iv.SenkouA26,
                    pivotPP    = dailyPivots.PivotPP,
                    pivotR1    = dailyPivots.PivotR1,
                    pivotR2    = dailyPivots.PivotR2,
                    pivotS1    = dailyPivots.PivotS1,
                    pivotS2    = dailyPivots.PivotS2,
                    pivotH1PP  = h1PivotIv?.PivotPP ?? 0,
                    pivotH1R1  = h1PivotIv?.PivotR1 ?? 0,
                    pivotH1R2  = h1PivotIv?.PivotR2 ?? 0,
                    pivotH1S1  = h1PivotIv?.PivotS1 ?? 0,
                    pivotH1S2  = h1PivotIv?.PivotS2 ?? 0,
                    senkouB26  = iv.SenkouB26,
                }
            }));
        }
    }

    // ── Flush queue → WebView (appelé par le DispatcherTimer toutes les 50ms) ──
    // Chaque message est envoyé INDIVIDUELLEMENT — aucun batch JSON susceptible d'être malformé.
    // Max 500 messages par tick pour ne pas bloquer le thread UI.
    private void FlushChartQueue()
    {
        if (!_chartReady) { while (_chartQueue.TryDequeue(out _)) { } return; }
        // Pendant le rechargement de l'historique mid-replay : laisser la queue s'accumuler
        // (les bougies du nouveau TF seront flushées APRÈS la réception du historyset)
        if (_historyPending) return;

        const int MaxPerFlush = 500;
        int  count = 0;
        bool any   = false;

        while (count < MaxPerFlush && _chartQueue.TryDequeue(out var msg))
        {
            Chart.CoreWebView2.PostWebMessageAsString(msg);
            any = true;
            count++;
        }

        if (!any) return;

        // Redessiner le nuage une seule fois après le flush
        Chart.CoreWebView2.PostWebMessageAsString("{\"type\":\"redrawCloud\"}");

        // Scroll vers la dernière bougie uniquement pendant un replay actif
        if (_replayScrollEnabled)
            Chart.CoreWebView2.PostWebMessageAsString("{\"type\":\"scroll\"}");
    }

    private void OnProgress(double pct, DateTimeOffset currentTime)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ProgressBar.Value    = pct;
            TxtProgressDate.Text = currentTime.ToString("yyyy-MM-dd HH:mm");
        });
    }

    /// <summary>
    /// Callback ReplayService.OnLoadingStatus — affiche l'overlay de chargement
    /// au-dessus du chart pendant le fetch MT5 + load DuckDB.
    /// String vide = cacher l'overlay (replay démarre).
    /// </summary>
    private void OnLoadingStatus(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (string.IsNullOrEmpty(message))
            {
                LoadingOverlay.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                LoadingText.Text          = message;
                LoadingOverlay.Visibility = System.Windows.Visibility.Visible;
            }
        });
    }

    private void OnTradeOpened(TradeRecord trade)
    {
        if (!_chartReady) return;

        long ts  = trade.EntryTime.HasValue ? TimeZoneHelper.ToChartUnixSeconds(trade.EntryTime.Value) : 0;
        var side = trade.Direction == "BUY" ? "buy" : "sell";

        var markerMsg = JsonSerializer.Serialize(new
        {
            type = "marker",
            data = new { time = ts, side, action = "entry", text = trade.Direction }
        });

        // Marker dessiné uniquement si le trade concerne le symbole actuellement affiché
        bool isDisplaySymbol = _replay != null && trade.Symbol == _replay.LiveDisplaySymbol;

        Dispatcher.InvokeAsync(() =>
        {
            _trades.Insert(0, trade);
            GridReplayTrades.ItemsSource = null;
            GridReplayTrades.ItemsSource = _trades;

            if (_chartReady && isDisplaySymbol)
                Chart.CoreWebView2.PostWebMessageAsString(markerMsg);
        });
    }

    private void OnTradeClosed(TradeRecord trade)
    {
        if (!_chartReady) return;

        long ts  = trade.ExitTime.HasValue ? TimeZoneHelper.ToChartUnixSeconds(trade.ExitTime.Value) : 0;
        var side = trade.Direction == "BUY" ? "buy" : "sell";

        var markerMsg = JsonSerializer.Serialize(new
        {
            type = "marker",
            data = new { time = ts, side, action = "exit", text = FormatExitReason(trade.ExitReason) }
        });

        // Marker dessiné uniquement si le trade concerne le symbole actuellement affiché
        bool isDisplaySymbol = _replay != null && trade.Symbol == _replay.LiveDisplaySymbol;

        Dispatcher.InvokeAsync(() =>
        {
            // Rafraîchir la liste pour afficher ExitPrice/ProfitLoss mis à jour
            GridReplayTrades.ItemsSource = null;
            GridReplayTrades.ItemsSource = _trades;

            if (_chartReady && isDisplaySymbol)
                Chart.CoreWebView2.PostWebMessageAsString(markerMsg);
        });
    }

    private void OnStatsUpdate(double capital, int wins, int losses, double drawdown)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtCapitalCurrent.Text = $"${capital:N2}";
            TxtWins.Text           = wins.ToString();
            TxtLosses.Text         = losses.ToString();
            TxtDrawdown.Text       = $"{drawdown:N1} %";
        });
    }

    // ── HUD Trends (overlay chart top-right) ──────────────────────────────────

    private void OnTrendsUpdate(Dictionary<string, TrendDirection> trends)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ApplyTrend(HudM1,  trends, "M1");
            ApplyTrend(HudM5,  trends, "M5");
            ApplyTrend(HudM15, trends, "M15");
            ApplyTrend(HudH1,  trends, "H1");
            ApplyTrend(HudH4,  trends, "H4");
            ApplyTrend(HudD1,  trends, "D");
        });
    }

    private void ApplyTrend(TextBlock tb, Dictionary<string, TrendDirection> trends, string tf)
    {
        if (!trends.TryGetValue(tf, out var dir))
        {
            tb.Text       = "—";
            tb.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            return;
        }
        switch (dir)
        {
            case TrendDirection.Bull:
                tb.Text       = "Bullish";
                tb.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                break;
            case TrendDirection.Bear:
                tb.Text       = "Bearish";
                tb.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                break;
            default:
                tb.Text       = "Range";
                tb.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
                break;
        }
    }

    private void ResetTrendHud()
    {
        HudM1.Text = HudM5.Text = HudM15.Text = HudH1.Text = HudH4.Text = HudD1.Text = "—";
        var muted = (System.Windows.Media.Brush)FindResource("MutedBrush");
        HudM1.Foreground = HudM5.Foreground = HudM15.Foreground =
            HudH1.Foreground = HudH4.Foreground = HudD1.Foreground = muted;
    }

    private void OnConditionsEvaluated(
        long ts,
        List<(string Label, bool Passed)> longResults,
        List<(string Label, bool Passed)> shortResults)
    {
        if (!_chartReady) return;

        var msg = JsonSerializer.Serialize(new
        {
            type = "conditions",
            data = new
            {
                time  = ts,
                @long  = longResults. Select(r => new { l = r.Label, p = r.Passed }),
                @short = shortResults.Select(r => new { l = r.Label, p = r.Passed }),
            }
        });

        _chartQueue.Enqueue(msg);
    }

    // ── Boutons ───────────────────────────────────────────────────────────────

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        ReplayLogger.Log("========== BtnStart_Click ==========");
        ReplayLogger.Log($"CanStartReplay={_vm.CanStartReplay} | IsLiveRunning={_vm.IsLiveRunning} | " +
                         $"SelectedStrategy={_vm.SelectedStrategy?.Name ?? "NULL"} | " +
                         $"AreReplayDatesValid={_vm.AreReplayDatesValid} | " +
                         $"From={_vm.ReplayFrom:yyyy-MM-dd} | To={_vm.ReplayTo:yyyy-MM-dd}");

        if (!_vm.CanStartReplay)
        {
            ReplayLogger.Log("EARLY RETURN: _vm.CanStartReplay is false");
            return;
        }
        if (_replay == null)
        {
            ReplayLogger.Log("EARLY RETURN: _replay is null");
            return;
        }

        ReplayLogger.Log($"TxtCapital.Text='{TxtCapital.Text}'");
        if (!double.TryParse(TxtCapital.Text, out double capital) || capital <= 0)
        {
            ReplayLogger.Log("EARLY RETURN: invalid capital");
            TxtCapital.BorderBrush = System.Windows.Media.Brushes.Red;
            return;
        }
        TxtCapital.ClearValue(TextBox.BorderBrushProperty);

        string symbol    = CmbSymbol.SelectedItem?.ToString() ?? "";
        string timeframe = (CmbTimeframe.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H1";
        ReplayLogger.Log($"symbol='{symbol}' (CmbSymbol.Items={CmbSymbol.Items.Count}, SelectedIndex={CmbSymbol.SelectedIndex}) | timeframe='{timeframe}'");

        if (string.IsNullOrEmpty(symbol))
        {
            ReplayLogger.Log("EARLY RETURN: symbol empty — CmbSymbol has no selection");
            return;
        }
        ReplayLogger.Log("All guards passed → launching ReplayService.StartAsync in background Task");

        _vm.ReplayCapital = TxtCapital.Text;
        _histCts?.Cancel();                     // annuler un chargement d'historique en cours
        _replayCts           = new CancellationTokenSource();
        _replayPaused        = false;
        _vm.IsReplayRunning  = true;
        _replayScrollEnabled = true;
        BtnPause.Content     = "⏸ Pause";

        // Règle absolue du bot : dates saisies = heure Québec (America/Toronto).
        // On construit des DateTimeOffset avec l'offset Québec du jour (gère DST EST/EDT).
        var from = ToQuebecOffset(_vm.ReplayFrom);
        var to   = ToQuebecOffset(_vm.ReplayTo.AddDays(1).AddSeconds(-1));

        var cfg = new ReplayConfig
        {
            Symbol                  = symbol,
            Timeframe               = timeframe,
            From                    = from,
            To                      = to,
            StartingCapital         = capital,
            Speed                   = _speed,
            Strategy                = _vm.SelectedStrategy,
            IsPaused                = () => _replayPaused,
            UseTicks                = ChkTickMode.IsChecked == true,
            RiskPercent             = _vm.GlobalRiskPercent,
            CommissionPerLotPerSide = _vm.CommissionPerLotPerSide,
        };

        // Activer le log chart_candles.log pour ce replay
        Services.ChartCandleLogger.Reset(symbol, timeframe,
            from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
        ReplayLogger.Log($"ChartCandleLogger reset → {Services.ChartCandleLogger.LogPath}");

        // Vider la queue AVANT reset — les messages résiduels du history load
        // (LoadChartAsync n'a aucun délai) seraient flushés par le timer après le reset
        // et feraient apparaître des dizaines de bougies/seconde au lieu de 1.
        while (_chartQueue.TryDequeue(out _)) { }

        // Réinitialiser chart et stats
        if (_chartReady)
            Chart.CoreWebView2.PostWebMessageAsString("{\"type\":\"reset\"}");
        ResetStats();
        ResetTrendHud();
        _trades.Clear();
        GridReplayTrades.ItemsSource = null;

        // Wipe DB replay (session précédente) avant de démarrer — clean slate.
        _ = _replay.WipeReplayTradesAsync();

        _ = Task.Run(async () =>
            {
                ReplayLogger.Log("Task.Run → calling _replay.StartAsync");
                try
                {
                    await _replay.StartAsync(cfg, _replayCts.Token);
                    ReplayLogger.Log("StartAsync completed normally");
                }
                catch (OperationCanceledException)
                {
                    ReplayLogger.Log("StartAsync cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    ReplayLogger.LogException("StartAsync", ex);
                    var msg = ex.Message;
                    _ = Dispatcher.InvokeAsync(() =>
                        _vm.ReportConnectionError($"[Replay] {msg}"));
                }
            })
            .ContinueWith(_ =>
            {
                ReplayLogger.Log("ContinueWith → IsReplayRunning = false");
                return Dispatcher.InvokeAsync(() =>
                {
                    _vm.IsReplayRunning  = false;
                    _replayScrollEnabled = false;
                });
            },
                TaskContinuationOptions.None);
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        _replayPaused = !_replayPaused;

        if (_replayPaused)
        {
            BtnPause.Content     = "▶ Reprendre";
            _replayScrollEnabled = false;
        }
        else
        {
            BtnPause.Content     = "⏸ Pause";
            _replayScrollEnabled = true;
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _replayPaused        = false;   // débloque le loop avant l'annulation
        _replayCts?.Cancel();
        _vm.IsReplayRunning  = false;
        _replayScrollEnabled = false;
        BtnPause.Content     = "⏸ Pause";
        ProgressBar.Value    = 0;
        TxtProgressDate.Text = "—";

        // Wipe la DB replay_trades + la liste UI — session terminée.
        _ = _replay?.WipeReplayTradesAsync();
        GridReplayTrades.ItemsSource = null;
        _trades.Clear();
        GridReplayTrades.ItemsSource = _trades;
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReplayRunning) return;

        ProgressBar.Value    = 0;
        TxtProgressDate.Text = "—";
        ResetStats();
        ResetTrendHud();

        _trades.Clear();
        GridReplayTrades.ItemsSource = null;

        if (_chartReady)
            Chart.CoreWebView2.PostWebMessageAsString("{\"type\":\"reset\"}");
    }

    // ── Trade Popup (double-clic sur la liste) ────────────────────────────────

    private void GridReplayTrades_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridReplayTrades.SelectedItem is not TradeRecord trade) return;

        // Popup = toujours le TF de la stratégie (M1 pour ScalpingGourmand, M5 pour Scalping, M15 pour IntraDay)
        // Le bot entre sur le TF de la stratégie — c'est ce que le popup doit montrer.
        string tf = _vm.SelectedStrategy?.Timeframe
            ?? (!string.IsNullOrEmpty(_replay?.LiveDisplayTimeframe)
                ? _replay!.LiveDisplayTimeframe
                : (CmbTimeframe.SelectedItem as ComboBoxItem)?.Content?.ToString()
                   ?? CmbTimeframe.SelectedItem?.ToString()
                   ?? "M1");

        // Digits : on tente la meta du symbole (cache via _replay) sinon défaut 5
        int digits = 5;
        try
        {
            var meta = _replay?.TryGetCachedMeta(trade.Symbol);
            if (meta != null) digits = meta.Digits;
        }
        catch { /* fallback digits=5 */ }

        new TradePopupWindow(trade, _replay, tf, digits, _vm.SelectedStrategy)
        {
            Owner = Window.GetWindow(this),
        }.Show();
    }

    // ── Date / vitesse ────────────────────────────────────────────────────────

    private void Speed_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int s))
        {
            _speed = s;
            if (_settings != null) { _settings.ReplaySpeed = s; _settings.Save(); }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateButtonStates()
    {
        bool running = _vm.IsReplayRunning;

        BtnStart.IsEnabled     = _vm.CanStartReplay && !running;
        BtnPause.Visibility    = running ? Visibility.Visible : Visibility.Collapsed;
        BtnStop.IsEnabled      = running;
        BtnReset.IsEnabled     = _vm.CanResetReplay;
        DpFrom.IsEnabled       = !running;
        DpTo.IsEnabled         = !running;
        TxtCapital.IsEnabled   = !running;
        // Symbol et Timeframe restent actifs pendant le replay :
        //   - Tous les symboles × tous les TFs sont chargés en mémoire au Start
        //   - Changement = switch d'affichage instantané, le backtest continue
    }

    private void CmbStrategy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbStrategy.SelectedItem is string name)
            _vm.SelectedStrategy = _vm.Strategies.FirstOrDefault(s => s.Name == name);
    }

    // ── Handlers Symbol / Timeframe ───────────────────────────────────────────
    //
    // Hors replay : ne RIEN faire — le chart reste vide jusqu'au clic sur Start.
    //               L'utilisateur peut librement changer Symbol/TF pour configurer
    //               sa session sans déclencher de chargement d'historique.
    //
    // En replay    : switch d'AFFICHAGE uniquement — le backtest multi-symbole
    //                continue en parallèle. Aucun stop, aucune fermeture de trade.
    //                Tous les symboles × tous les TFs sont déjà en mémoire.

    private void CmbSymbol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string newSymbol = CmbSymbol.SelectedItem?.ToString() ?? "";
        if (!string.IsNullOrEmpty(newSymbol) && _settings != null)
        {
            _settings.ReplaySymbol = newSymbol;
            _settings.Save();
        }

        if (!_vm.IsReplayRunning || _replay == null || !_chartReady) return;
        if (string.IsNullOrEmpty(newSymbol)) return;
        if (newSymbol == _replay.LiveDisplaySymbol) return;

        if (!_replay.TrySwitchDisplaySymbol(newSymbol)) return;
        RebuildDisplayFromMemory(newSymbol, _replay.LiveDisplayTimeframe);
    }

    private void CmbTimeframe_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string newTf = (CmbTimeframe.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H1";
        if (!string.IsNullOrEmpty(newTf) && _settings != null)
        {
            _settings.ReplayTimeframe = newTf;
            _settings.Save();
        }

        if (!_vm.IsReplayRunning || _replay == null || !_chartReady) return;
        if (newTf == _replay.LiveDisplayTimeframe) return;

        if (!_replay.TrySwitchDisplayTf(newTf)) return;
        RebuildDisplayFromMemory(_replay.LiveDisplaySymbol, newTf);
    }

    /// <summary>
    /// Reconstruit le chart depuis la mémoire (données déjà chargées par le service)
    /// pour un nouveau (symbole, TF). Utilisé pour les switches d'affichage mid-replay.
    /// Le backtest ne s'arrête JAMAIS — seul l'affichage change.
    /// </summary>
    private void RebuildDisplayFromMemory(string symbol, string tf)
    {
        if (_replay == null) return;

        _historyPending = true;
        while (_chartQueue.TryDequeue(out _)) { }
        Chart.CoreWebView2.PostWebMessageAsString("{\"type\":\"reset\"}");

        // Redessiner les markers des trades existants pour ce symbole
        RedrawMarkersForSymbol(symbol);

        _histCts?.Cancel();
        _histCts     = new CancellationTokenSource();
        var thisCts  = _histCts;
        var ct       = thisCts.Token;
        var from     = new DateTimeOffset(DateTime.SpecifyKind(_vm.ReplayFrom, DateTimeKind.Unspecified), TimeSpan.Zero);
        var upTo     = _replay.CurrentReplayTime;
        var strategy = _vm.SelectedStrategy;

        _ = Task.Run(() =>
        {
            var json = _replay.BuildHistoryJsonUpTo(symbol, tf, from, upTo, strategy, ct);

            Dispatcher.InvokeAsync(() =>
            {
                _historyPending = false;    // toujours libérer le flush, même si annulé
                if (json == null || ct.IsCancellationRequested || _histCts != thisCts) return;
                Chart.CoreWebView2.PostWebMessageAsString(json);
                Chart.CoreWebView2.PostWebMessageAsString("{\"type\":\"fit\"}");
                // Repose les markers après le historyset pour qu'ils ne soient pas écrasés
                RedrawMarkersForSymbol(symbol);
            });
        });
    }

    /// <summary>
    /// Repose les markers des trades ouverts/fermés appartenant au symbole affiché.
    /// Appelé après un reset chart ou un switch symbole.
    /// </summary>
    private void RedrawMarkersForSymbol(string symbol)
    {
        if (!_chartReady) return;
        foreach (var t in _trades)
        {
            if (t.Symbol != symbol) continue;

            if (t.EntryTime.HasValue)
            {
                var entryMsg = JsonSerializer.Serialize(new
                {
                    type = "marker",
                    data = new
                    {
                        time   = TimeZoneHelper.ToChartUnixSeconds(t.EntryTime.Value),
                        side   = t.Direction == "BUY" ? "buy" : "sell",
                        action = "entry",
                        text   = t.Direction
                    }
                });
                Chart.CoreWebView2.PostWebMessageAsString(entryMsg);
            }

            if (t.ExitTime.HasValue)
            {
                var exitMsg = JsonSerializer.Serialize(new
                {
                    type = "marker",
                    data = new
                    {
                        time   = TimeZoneHelper.ToChartUnixSeconds(t.ExitTime.Value),
                        side   = t.Direction == "BUY" ? "buy" : "sell",
                        action = "exit",
                        text   = FormatExitReason(t.ExitReason)
                    }
                });
                Chart.CoreWebView2.PostWebMessageAsString(exitMsg);
            }
        }
    }

    private void ResetStats()
    {
        TxtCapitalCurrent.Text = "—";
        TxtWins.Text           = "0";
        TxtLosses.Text         = "0";
        TxtDrawdown.Text       = "0 %";
    }

    private static string FormatExitReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "X";
        return reason switch
        {
            "ForceExit:Kijun reverse" => "Sortie Kijun reverse",
            _ when reason.StartsWith("ForceExit:", StringComparison.OrdinalIgnoreCase)
                => "Sortie forcee - " + reason["ForceExit:".Length..],
            _ when reason.StartsWith("OptionalExit:", StringComparison.OrdinalIgnoreCase)
                => "Sortie optionnelle - " + reason["OptionalExit:".Length..],
            _ => reason,
        };
    }

    /// <summary>
    /// Convertit un DateTime (généralement Kind=Unspecified d'un DatePicker) en DateTimeOffset
    /// traité comme heure Québec (America/Toronto). Gère DST automatiquement.
    /// Exemple : "1 janvier 2024" → 2024-01-01T00:00:00-05:00 (EST).
    /// </summary>
    private static DateTimeOffset ToQuebecOffset(DateTime dt)
    {
        var unspec = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        var offset = ToutieTrader.Core.Utils.TimeZoneHelper.QuebecTz.GetUtcOffset(unspec);
        return new DateTimeOffset(unspec, offset);
    }
}
