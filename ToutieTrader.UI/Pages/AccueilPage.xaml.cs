using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI.Pages;

public partial class AccueilPage : Page
{
    private readonly MainViewModel _vm;

    public AccueilPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Account))
                UpdateAccount();
            if (e.PropertyName is nameof(MainViewModel.BotStatus) or nameof(MainViewModel.IsLiveRunning))
                UpdateBotStatus();
            if (e.PropertyName == nameof(MainViewModel.LastAction))
                TxtLastAction.Text = vm.LastAction;
        };

        UpdateAccount();
        UpdateBotStatus();
    }

    private void UpdateAccount()
    {
        var a = _vm.Account;
        if (a is null)
        {
            TxtBalance.Text  = "—";
            TxtEquity.Text   = "—";
            TxtDrawdown.Text = "—";
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
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        await _vm.RefreshAccountAsync();
        BtnRefresh.IsEnabled = true;
    }
}
