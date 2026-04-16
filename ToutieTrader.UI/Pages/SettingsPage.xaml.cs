using System.Windows;
using System.Windows.Controls;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI.Pages;

public partial class SettingsPage : Page
{
    private readonly MainViewModel _vm;
    private bool _suppressChange;

    public SettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _suppressChange = true;
        TxtRiskPercent.Text = "1.0";
        _suppressChange = false;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CanEditSettings))
                UpdateLockState();
        };

        UpdateLockState();
    }

    private void UpdateLockState()
    {
        bool canEdit = _vm.CanEditSettings;
        TxtRiskPercent.IsEnabled          = canEdit;
        TxtSettingsLocked.Visibility      = canEdit ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TxtRiskPercent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChange) return;
        // Valeur utilisée par la Strategy via Settings["RiskPercent"]
        // Le parsing est validé silencieusement — mauvais format = ignoré
        if (decimal.TryParse(TxtRiskPercent.Text, out var val) && val > 0 && val <= 10)
        {
            // TODO : propager au StrategyRunner si nécessaire
        }
    }
}
