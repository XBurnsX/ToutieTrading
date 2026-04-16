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

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // ── MT5 Client ────────────────────────────────────────────────────────
        _mt5 = new MT5ApiClient("http://127.0.0.1:8000");

        // ── ViewModel ─────────────────────────────────────────────────────────
        var vm = new MainViewModel(_mt5);

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
        try
        {
            string candlesDb = ResolveCandlesDb();
            Services.ReplayLogger.Log($"App: ResolveCandlesDb → {candlesDb} (exists={File.Exists(candlesDb)})");
            string liveDb    = Path.Combine(Path.GetDirectoryName(candlesDb)!, "trades.db");
            string replayDb  = Path.Combine(Path.GetDirectoryName(candlesDb)!, "replay_trades.db");

            var duckReader = new DuckDBReader(candlesDb);
            var tradeRepo  = new TradeRepository(liveDb, replayDb);
            replayService  = new ReplayService(duckReader, tradeRepo, _mt5);
        }
        catch (Exception ex)
        {
            Services.ReplayLogger.LogException("App DuckDB init", ex);
        }

        // ── Démarrer le polling connexion ─────────────────────────────────────
        _mt5.StartPolling();

        // ── Ouvrir la fenêtre principale ──────────────────────────────────────
        var window = new MainWindow(vm, replayService);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
