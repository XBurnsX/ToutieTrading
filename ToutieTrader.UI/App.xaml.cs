using System.IO;
using System.Windows;
using ToutieTrader.Core.Engine;
using ToutieTrader.Data;
using ToutieTrader.UI.Services;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI;

/// <summary>
/// Composition root — instancie tous les services et crée le MainWindow.
/// </summary>
public partial class App : Application
{
    private MT5ApiClient? _mt5;
    private TradeRepository? _tradeRepo;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // ── MT5 Client ────────────────────────────────────────────────────────
        _mt5 = new MT5ApiClient("http://127.0.0.1:8000");

        // ── Settings persistants ─────────────────────────────────────────────
        var settings = AppSettings.Load();

        // ── ViewModel ─────────────────────────────────────────────────────────
        var vm = new MainViewModel(_mt5);

        // Appliquer les paramètres sauvegardés au ViewModel
        vm.ReplayFrom              = settings.ReplayFrom;
        vm.ReplayTo                = settings.ReplayTo;
        vm.ReplayCapital           = settings.ReplayCapital;
        vm.GlobalRiskPercent       = settings.GlobalRiskPercent;
        vm.CommissionPerLotPerSide = settings.CommissionPerLotPerSide;

        // Auto-sauvegarder les propriétés VM à chaque changement
        vm.PropertyChanged += (_, pe) =>
        {
            switch (pe.PropertyName)
            {
                case nameof(MainViewModel.ReplayFrom):
                    settings.ReplayFrom = vm.ReplayFrom; settings.Save(); break;
                case nameof(MainViewModel.ReplayTo):
                    settings.ReplayTo = vm.ReplayTo; settings.Save(); break;
                case nameof(MainViewModel.ReplayCapital):
                    settings.ReplayCapital = vm.ReplayCapital; settings.Save(); break;
                case nameof(MainViewModel.GlobalRiskPercent):
                    settings.GlobalRiskPercent = vm.GlobalRiskPercent; settings.Save(); break;
                case nameof(MainViewModel.CommissionPerLotPerSide):
                    settings.CommissionPerLotPerSide = vm.CommissionPerLotPerSide; settings.Save(); break;
            }
        };

        // ── StrategyLoader : dossier /Strategies/ à côté de l'exe ────────────
        string strategiesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Strategies");

        var loader = new StrategyLoader(strategiesPath);

        loader.OnCompilationError += (file, errors) =>
        {
            // Log dans un fichier lisible à côté de l'exe
            string log = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "strategy_errors.log");
            System.IO.File.AppendAllText(log,
                $"[{DateTime.Now:HH:mm:ss}] {file}:\n{errors}\n---\n");

            Current.Dispatcher.Invoke(() =>
                vm.ReportConnectionError($"[Strategy] {file}: {errors.Split('\n')[0]}"));
        };

        loader.OnStrategyLoaded += _ => { };

        var strategies = loader.LoadAll();
        vm.Strategies       = strategies;
        vm.SelectedStrategy = strategies.Count > 0 ? strategies[0] : null;

        // ── DuckDB Replay ─────────────────────────────────────────────────────
        ReplayService? replayService = null;
        LiveService?   liveService   = null;
        try
        {
            string candlesDb = ResolveCandlesDb();
            Services.ReplayLogger.Log($"App: ResolveCandlesDb → {candlesDb} (exists={File.Exists(candlesDb)})");
            string liveDb    = Path.Combine(Path.GetDirectoryName(candlesDb)!, "trades.db");
            string replayDb  = Path.Combine(Path.GetDirectoryName(candlesDb)!, "replay_trades.db");

            Services.ReplayLogger.Log("App: new DuckDBReader…");
            var duckReader = new DuckDBReader(candlesDb);
            Services.ReplayLogger.Log("App: new TradeRepository…");
            var tradeRepo  = new TradeRepository(liveDb, replayDb);
            _tradeRepo = tradeRepo;
            Services.ReplayLogger.Log("App: new ReplayService…");
            replayService  = new ReplayService(duckReader, tradeRepo, _mt5);
            Services.ReplayLogger.Log("App: ReplayService OK");
            liveService    = new LiveService(_mt5, tradeRepo);
        }
        catch (Exception ex)
        {
            Services.ReplayLogger.LogException("App DuckDB init", ex);
        }

        // ── Démarrer le polling connexion ─────────────────────────────────────
        Services.ReplayLogger.Log("App: StartPolling…");
        _mt5.StartPolling();
        Services.ReplayLogger.Log("App: StartPolling OK");

        // ── Ouvrir la fenêtre principale ──────────────────────────────────────
        Services.ReplayLogger.Log("App: new MainWindow…");
        var window = new MainWindow(vm, replayService, liveService, settings);
        Services.ReplayLogger.Log("App: MainWindow OK, calling Show()…");
        MainWindow = window;
        window.Show();
        Services.ReplayLogger.Log("App: Show() returned");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Wipe la DB replay_trades — éphémère par design.
        try { _tradeRepo?.WipeReplayAsync().GetAwaiter().GetResult(); } catch { }

        _mt5?.StopPolling();
        _mt5?.Dispose();
        base.OnExit(e);
    }

    // ── Localisation de candles.db ────────────────────────────────────────────

    /// <summary>
    /// Cherche candles.db depuis le répertoire de l'exe vers la racine.
    /// Ordre : ./data/candles.db → ../data/candles.db → ../../data/candles.db …
    /// Si introuvable, retourne le chemin par défaut (sera créé par le collecteur).
    /// </summary>
    private static string ResolveCandlesDb()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

        // Remonter jusqu'à 5 niveaux
        for (int i = 0; i < 5; i++)
        {
            string candidate = Path.Combine(dir.FullName, "data", "candles.db");
            if (File.Exists(candidate))
                return candidate;

            if (dir.Parent is null) break;
            dir = dir.Parent;
        }

        // Fallback : data/ à côté de l'exe (sera créé par le collecteur Python)
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "candles.db");
    }
}
