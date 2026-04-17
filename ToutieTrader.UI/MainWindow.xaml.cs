using System.Windows;
using System.Windows.Media;
using ToutieTrader.Core.Models;
using ToutieTrader.UI.Pages;
using ToutieTrader.UI.Services;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI;

/// <summary>
/// Shell principal : header permanent + nav sidebar + frame de contenu.
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly AccueilPage    _accueilPage;
    private readonly HistoriquePage _historiquePage;
    private readonly ReplayPage     _replayPage;
    private readonly StrategyPage   _strategyPage;
    private readonly SettingsPage   _settingsPage;

    private readonly LiveService?              _live;
    private          CancellationTokenSource?  _liveCts;

    public MainWindow(MainViewModel vm, ReplayService? replayService = null, LiveService? liveService = null, Services.AppSettings? settings = null)
    {
        InitializeComponent();
        ViewModel = vm;

        _live = liveService;
        _accueilPage    = new AccueilPage(vm, ToggleLiveTrading);
        _historiquePage = new HistoriquePage(vm);
        _replayPage     = new ReplayPage(vm, replayService, settings);
        _strategyPage   = new StrategyPage(vm);
        _settingsPage   = new SettingsPage(vm);

        if (_live != null)
        {
            _live.OnStatusUpdate += OnLiveStatusUpdate;
            _live.OnTradeOpened  += OnLiveTradeOpened;
            _live.OnTradeClosed  += OnLiveTradeClosed;
            _live.OnStatsUpdate  += OnLiveStatsUpdate;
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsPythonOk) or nameof(MainViewModel.IsMt5Ok))
                UpdateConnectionDots();

            if (e.PropertyName == nameof(MainViewModel.ConnectionError))
                UpdateConnectionError();

            if (e.PropertyName is nameof(MainViewModel.CanStartTrading) or nameof(MainViewModel.IsLiveRunning))
                UpdateTradingButton();
        };

        UpdateTradingButton();
        UpdateConnectionDots();
        MainFrame.Navigate(_accueilPage);
    }

    private void NavAccueil_Checked(object sender, RoutedEventArgs e)
    { if (_accueilPage != null) MainFrame.Navigate(_accueilPage); }

    private void NavHistorique_Checked(object sender, RoutedEventArgs e)
    { if (_historiquePage != null) MainFrame.Navigate(_historiquePage); }

    private void NavReplay_Checked(object sender, RoutedEventArgs e)
    { if (_replayPage != null) MainFrame.Navigate(_replayPage); }

    private void NavStrategy_Checked(object sender, RoutedEventArgs e)
    { if (_strategyPage != null) MainFrame.Navigate(_strategyPage); }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    { if (_settingsPage != null) MainFrame.Navigate(_settingsPage); }

    private void BtnStartTrading_Click(object sender, RoutedEventArgs e)
        => ToggleLiveTrading();

    private void ToggleLiveTrading()
    {
        if (ViewModel.IsLiveRunning)
        {
            ViewModel.LastAction = "Arret live demande.";
            _liveCts?.Cancel();
            return;
        }

        if (_live == null || !ViewModel.CanStartTrading) return;
        if (ViewModel.SelectedStrategy == null) return;

        _liveCts = new CancellationTokenSource();
        double startingCapital = ViewModel.Account?.Balance ?? 10000.0;
        ViewModel.IsLiveRunning = true;
        ViewModel.LiveCapital = startingCapital;
        ViewModel.LiveWins = 0;
        ViewModel.LiveLosses = 0;
        ViewModel.LiveDrawdown = 0;
        ViewModel.LastAction = "Demarrage live...";
        UpdateTradingButton();

        var cfg = new LiveConfig
        {
            StartingCapital         = startingCapital,
            Strategy                = ViewModel.SelectedStrategy,
            RiskPercent             = ViewModel.GlobalRiskPercent,
            CommissionPerLotPerSide = ViewModel.CommissionPerLotPerSide,
        };

        _ = Task.Run(async () =>
        {
            await _live.StartAsync(cfg, _liveCts.Token);
            Dispatcher.Invoke(() =>
            {
                ViewModel.IsLiveRunning = false;
                ViewModel.HasOpenLiveTrade = false;
                UpdateTradingButton();
                _liveCts = null;
            });
        });
    }

    private void OnLiveStatusUpdate(string msg)
        => Dispatcher.Invoke(() => ViewModel.BotStatus = msg);

    private void OnLiveTradeOpened(TradeRecord trade)
        => Dispatcher.Invoke(() =>
        {
            ViewModel.HasOpenLiveTrade = true;
            ViewModel.LastAction = $"Trade ouvert {trade.Symbol} {trade.Direction} lot {trade.LotSize:0.##}";
        });

    private void OnLiveTradeClosed(TradeRecord trade)
        => Dispatcher.Invoke(() =>
        {
            ViewModel.HasOpenLiveTrade = false;
            ViewModel.LastAction = $"Trade ferme {trade.Symbol} {trade.Direction} P/L {trade.ProfitLoss:0.##} ({trade.ExitReason ?? "-"})";
        });

    private void OnLiveStatsUpdate(double capital, int wins, int losses, double drawdown)
        => Dispatcher.Invoke(() =>
        {
            ViewModel.LiveCapital = capital;
            ViewModel.LiveWins = wins;
            ViewModel.LiveLosses = losses;
            ViewModel.LiveDrawdown = drawdown;
        });

    private void UpdateConnectionDots()
    {
        DotPython.Fill = ViewModel.IsPythonOk
            ? new SolidColorBrush(Color.FromRgb(0, 200, 90))
            : new SolidColorBrush(Color.FromRgb(255, 51, 51));

        DotMt5.Fill = ViewModel.IsMt5Ok
            ? new SolidColorBrush(Color.FromRgb(0, 200, 90))
            : new SolidColorBrush(Color.FromRgb(255, 51, 51));
    }

    private void UpdateConnectionError()
    {
        bool hasError = !string.IsNullOrEmpty(ViewModel.ConnectionError);
        TxtConnectionError.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        TxtConnectionError.Text = ViewModel.ConnectionError;
    }

    private void UpdateTradingButton()
    {
        BtnStartTrading.Content = ViewModel.IsLiveRunning ? "Stop" : "Start Trading";
        BtnStartTrading.IsEnabled = ViewModel.IsLiveRunning || ViewModel.CanStartTrading;
    }
}
