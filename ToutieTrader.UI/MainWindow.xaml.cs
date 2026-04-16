using System.Windows;
using System.Windows.Media;
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

    public MainWindow(MainViewModel vm, ReplayService? replayService = null)
    {
        InitializeComponent();
        ViewModel = vm;

        // Pré-instancier les pages — pas de re-création à chaque navigation
        _accueilPage    = new AccueilPage(vm);
        _historiquePage = new HistoriquePage(vm);
        _replayPage     = new ReplayPage(vm, replayService);
        _strategyPage   = new StrategyPage(vm);
        _settingsPage   = new SettingsPage(vm);

        // Réagir aux changements d'état du ViewModel
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsPythonOk) or nameof(MainViewModel.IsMt5Ok))
                UpdateConnectionDots();

            if (e.PropertyName == nameof(MainViewModel.ConnectionError))
                UpdateConnectionError();

            if (e.PropertyName == nameof(MainViewModel.CanStartTrading))
                BtnStartTrading.IsEnabled = vm.CanStartTrading;
        };

        BtnStartTrading.IsEnabled = vm.CanStartTrading;
        UpdateConnectionDots();
        MainFrame.Navigate(_accueilPage);
    }

    // ─── Navigation ───────────────────────────────────────────────────────────

    private void NavAccueil_Checked(object sender, RoutedEventArgs e)
    { if (_accueilPage    != null) MainFrame.Navigate(_accueilPage);    }

    private void NavHistorique_Checked(object sender, RoutedEventArgs e)
    { if (_historiquePage != null) MainFrame.Navigate(_historiquePage); }

    private void NavReplay_Checked(object sender, RoutedEventArgs e)
    { if (_replayPage     != null) MainFrame.Navigate(_replayPage);     }

    private void NavStrategy_Checked(object sender, RoutedEventArgs e)
    { if (_strategyPage   != null) MainFrame.Navigate(_strategyPage);   }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    { if (_settingsPage   != null) MainFrame.Navigate(_settingsPage);   }

    // ─── Start Trading ────────────────────────────────────────────────────────

    private void BtnStartTrading_Click(object sender, RoutedEventArgs e)
    {
        // TODO Phase 10 : brancher StrategyRunner en mode live
        NavReplay.IsChecked = true;
    }

    // ─── Helpers UI ──────────────────────────────────────────────────────────

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
        TxtConnectionError.Text       = ViewModel.ConnectionError;
    }
}
