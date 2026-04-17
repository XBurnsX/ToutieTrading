using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI.Pages;

public partial class AccueilPage : Page
{
    private readonly MainViewModel _vm;
    private readonly Action? _toggleLive;

    public AccueilPage(MainViewModel vm, Action? toggleLive = null)
    {
        InitializeComponent();
        _vm = vm;
        _toggleLive = toggleLive;

        vm.PropertyChanged += OnViewModelPropertyChanged;

        UpdateAll();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Account))
            UpdateAccount();

        if (e.PropertyName is nameof(MainViewModel.BotStatus)
            or nameof(MainViewModel.IsLiveRunning)
            or nameof(MainViewModel.CanStartTrading))
            UpdateBotStatus();

        if (e.PropertyName is nameof(MainViewModel.IsPythonOk)
            or nameof(MainViewModel.IsMt5Ok)
            or nameof(MainViewModel.SelectedStrategy)
            or nameof(MainViewModel.GlobalRiskPercent)
            or nameof(MainViewModel.CommissionPerLotPerSide)
            or nameof(MainViewModel.HasOpenLiveTrade))
            UpdateLiveConfig();

        if (e.PropertyName is nameof(MainViewModel.LiveCapital)
            or nameof(MainViewModel.LiveWins)
            or nameof(MainViewModel.LiveLosses)
            or nameof(MainViewModel.LiveDrawdown))
            UpdateLiveStats();

        if (e.PropertyName == nameof(MainViewModel.LastAction))
            TxtLastAction.Text = string.IsNullOrWhiteSpace(_vm.LastAction) ? "-" : _vm.LastAction;
    }

    private void UpdateAll()
    {
        UpdateAccount();
        UpdateBotStatus();
        UpdateLiveConfig();
        UpdateLiveStats();
        TxtLastAction.Text = string.IsNullOrWhiteSpace(_vm.LastAction) ? "-" : _vm.LastAction;
    }

    private void UpdateAccount()
    {
        var a = _vm.Account;
        if (a is null)
        {
            TxtBalance.Text  = "-";
            TxtEquity.Text   = "-";
            TxtDrawdown.Text = "-";
            TxtCurrency.Text = "";
            return;
        }

        TxtBalance.Text  = $"{a.Balance:N2}";
        TxtEquity.Text   = $"{a.Equity:N2}";
        TxtDrawdown.Text = $"{a.DrawdownPercent:F2} %";
        TxtCurrency.Text = a.Currency;
    }

    private void UpdateBotStatus()
    {
        bool active = _vm.IsLiveRunning;
        DotBotStatus.Fill = active
            ? new SolidColorBrush(Color.FromRgb(0, 200, 90))
            : new SolidColorBrush(Color.FromRgb(90, 90, 114));

        TxtBotStatus.Text = _vm.BotStatus;
        BtnStartLive.Content = active ? "Stop live" : "Start live";
        BtnStartLive.IsEnabled = active || _vm.CanStartTrading;
    }

    private void UpdateLiveConfig()
    {
        SetConnection(DotPython, TxtPython, _vm.IsPythonOk);
        SetConnection(DotMt5, TxtMt5, _vm.IsMt5Ok);

        TxtStrategy.Text = _vm.SelectedStrategy?.Name ?? "-";
        TxtRisk.Text = $"{_vm.GlobalRiskPercent:0.##} % du capital";
        TxtCommission.Text = $"{_vm.CommissionPerLotPerSide:0.##} $ / lot / side";
        TxtOpenTrade.Text = _vm.HasOpenLiveTrade ? "Oui" : "Non";

        TxtProtection.Text = _vm.SelectedStrategy is null
            ? "Choisir une strategy avant de partir le live."
            : "Risk global + SL/TP de strategy + frais IC Markets + no replay pendant live.";

        UpdateBotStatus();
    }

    private void UpdateLiveStats()
    {
        TxtLiveCapital.Text = _vm.LiveCapital > 0 ? $"{_vm.LiveCapital:N2}" : "-";
        TxtLiveRecord.Text = $"{_vm.LiveWins} / {_vm.LiveLosses}";
        TxtLiveDrawdown.Text = $"{_vm.LiveDrawdown:F2} %";
    }

    private static void SetConnection(System.Windows.Shapes.Ellipse dot, TextBlock text, bool ok)
    {
        dot.Fill = ok
            ? new SolidColorBrush(Color.FromRgb(0, 200, 90))
            : new SolidColorBrush(Color.FromRgb(255, 51, 51));
        text.Text = ok ? "OK" : "Non connecte";
    }

    private void BtnStartLive_Click(object sender, RoutedEventArgs e)
        => _toggleLive?.Invoke();

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        await _vm.RefreshAccountAsync();
        BtnRefresh.IsEnabled = true;
    }
}
