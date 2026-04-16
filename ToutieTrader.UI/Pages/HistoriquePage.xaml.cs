using System.Windows.Controls;
using System.Windows.Input;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI.Pages;

public partial class HistoriquePage : Page
{
    private readonly MainViewModel _vm;

    public HistoriquePage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        // TODO phase 09 : charger les trades depuis TradeRepository
    }

    private void GridTrades_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // TODO phase 09 : ouvrir Popup Trade
    }
}
