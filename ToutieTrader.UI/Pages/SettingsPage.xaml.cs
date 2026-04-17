using System.Globalization;
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
        TxtRiskPercent.Text = _vm.GlobalRiskPercent.ToString(CultureInfo.InvariantCulture);
        TxtCommission.Text  = _vm.CommissionPerLotPerSide.ToString(CultureInfo.InvariantCulture);
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
        TxtRiskPercent.IsEnabled       = canEdit;
        TxtCommission.IsEnabled        = canEdit;
        TxtSettingsLocked.Visibility   = canEdit ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TxtRiskPercent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChange) return;
        if (decimal.TryParse(TxtRiskPercent.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
            && val > 0 && val <= 10)
        {
            _vm.GlobalRiskPercent = val;
        }
    }

    private void TxtCommission_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChange) return;
        if (decimal.TryParse(TxtCommission.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
            && val >= 0 && val <= 100)
        {
            _vm.CommissionPerLotPerSide = val;
        }
    }

}
