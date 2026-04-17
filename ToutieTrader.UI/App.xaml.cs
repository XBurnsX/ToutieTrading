using System.IO;
using System.Windows;
using ToutieTrader.Core.Engine;
using ToutieTrader.Data;
using ToutieTrader.UI.Services;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI;

/// <summary>
/// Composition root.
/// Phase 1 (synchrone, immédiate) : MT5 client + settings + ViewModel → fenêtre visible.
/// Phase 2 (background, Task.Run) : Roslyn compilation + DuckDB → injection dans les pages.
/// </summary>
public partial class App : Application
{
    private MT5ApiClient?     _mt5;
    private TradeRepository?  _tradeRepo;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // ── Handlers globaux d'exception : plus jamais de crash silencieux ──
        string crashLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
        void LogAndShow(string origin, Exception ex)
        {
            try
            {
                File.AppendAllText(crashLog,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origin}\n{ex}\n---\n");
            }
            catch { /* rien à faire */ }
            try
            {
                Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show(
                        $"Erreur non gérée ({origin}) :\n\n{ex.Message}\n\nDétails dans crash.log",
                        "ToutieTrader — crash",
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
            catch { /* dispatcher peut être mort */ }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogAndShow("UI thread", args.Exception);
            args.Handled = true;   // empêche la fermeture de l'app
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogAndShow("AppDomain", ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogAndShow("Task", args.Exception);
            args.SetObserved();
        };

        // ── Phase 1 : rapide, sur le thread UI ───────────────────────────────
        _mt5 = new MT5ApiClient("http://127.0.0.1:8000");
        var settings = AppSettings.Load();
        var vm       = new MainViewModel(_mt5);

        vm.ReplayFrom              = settings.ReplayFrom;
        vm.ReplayTo                = settings.ReplayTo;
        vm.ReplayCapital           = settings.ReplayCapital;
        vm.GlobalRiskPercent       = settings.GlobalRiskPercent;
        vm.CommissionPerLotPerSide = settings.CommissionPerLotPerSide;

        vm.PropertyChanged += (_, pe) =>
        {
            switch (pe.PropertyName)
            {
                case nameof(MainViewModel.ReplayFrom):              settings.ReplayFrom              = vm.ReplayFrom;              settings.Save(); break;
                case nameof(MainViewModel.ReplayTo):                settings.ReplayTo                = vm.ReplayTo;                settings.Save(); break;
                case nameof(MainViewModel.ReplayCapital):           settings.ReplayCapital           = vm.ReplayCapital;           settings.Save(); break;
                case nameof(MainViewModel.GlobalRiskPercent):       settings.GlobalRiskPercent       = vm.GlobalRiskPercent;       settings.Save(); break;
                case nameof(MainViewModel.CommissionPerLotPerSide): settings.CommissionPerLotPerSide = vm.CommissionPerLotPerSide; settings.Save(); break;
            }
        };

        string strategiesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Strategies");
        var loader = new StrategyLoader(strategiesPath);

        loader.OnCompilationError += (file, errors) =>
        {
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategy_errors.log");
            File.AppendAllText(log, $"[{DateTime.Now:HH:mm:ss}] {file}:\n{errors}\n---\n");
            Current.Dispatcher.Invoke(() =>
                vm.ReportConnectionError($"[Strategy] {file}: {errors.Split('\n')[0]}"));
        };

        loader.OnStrategyLoaded += _ => { };

        _mt5.StartPolling();

        // Fenêtre visible immédiatement (services null — les pages gèrent le cas null)
        var window = new MainWindow(vm, null, null, settings, null);
        MainWindow = window;
        window.Show();

        // ── Phase 2 : background — Roslyn + DuckDB ───────────────────────────
        _ = Task.Run(() =>
        {
            // Compilation Roslyn (lent : lecture de tous les assemblies + emit)
            ReplayLogger.Log("BG: StrategyLoader.LoadAll start");
            var strategies = loader.LoadAll();
            ReplayLogger.Log($"BG: StrategyLoader.LoadAll done ({strategies.Count} strategies)");

            Current.Dispatcher.Invoke(() =>
            {
                vm.Strategies       = strategies;
                vm.SelectedStrategy = strategies.Count > 0 ? strategies[0] : null;
            });

            // DuckDB + services
            ReplayService?    replayService = null;
            LiveService?      liveService   = null;
            TradeRepository?  tradeRepo     = null;
            try
            {
                string candlesDb = ResolveCandlesDb();
                ReplayLogger.Log($"BG: ResolveCandlesDb → {candlesDb} (exists={File.Exists(candlesDb)})");
                string liveDb   = Path.Combine(Path.GetDirectoryName(candlesDb)!, "trades.db");
                string replayDb = Path.Combine(Path.GetDirectoryName(candlesDb)!, "replay_trades.db");

                ReplayLogger.Log("BG: new DuckDBReader…");
                var duckReader = new DuckDBReader(candlesDb);
                ReplayLogger.Log("BG: new TradeRepository…");
                tradeRepo  = new TradeRepository(liveDb, replayDb);
                ReplayLogger.Log("BG: new ReplayService…");
                replayService = new ReplayService(duckReader, tradeRepo, _mt5);
                ReplayLogger.Log("BG: new LiveService…");
                liveService   = new LiveService(_mt5, tradeRepo);
                ReplayLogger.Log("BG: services OK");
            }
            catch (Exception ex)
            {
                ReplayLogger.LogException("BG DuckDB init", ex);
            }

            _tradeRepo = tradeRepo;

            // Injecter les services dans la fenêtre déjà affichée
            Current.Dispatcher.Invoke(() =>
                window.InitServices(replayService, liveService, tradeRepo, _mt5));
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tradeRepo?.WipeReplayAsync().GetAwaiter().GetResult(); } catch { }
        _mt5?.StopPolling();
        _mt5?.Dispose();
        base.OnExit(e);
    }

    private static string ResolveCandlesDb()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int i = 0; i < 5; i++)
        {
            string candidate = Path.Combine(dir.FullName, "data", "candles.db");
            if (File.Exists(candidate)) return candidate;
            if (dir.Parent is null) break;
            dir = dir.Parent;
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "candles.db");
    }
}
