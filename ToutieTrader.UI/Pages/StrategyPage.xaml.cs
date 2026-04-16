using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.UI.ViewModels;

namespace ToutieTrader.UI.Pages;

public partial class StrategyPage : Page
{
    private readonly MainViewModel _vm;

    // ── Persistence ───────────────────────────────────────────────────────────

    private static readonly string SettingsFile =
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategy_settings.json");

    private static Dictionary<string, Dictionary<string, JsonElement>>? _cache;

    private static Dictionary<string, Dictionary<string, JsonElement>> Cache
    {
        get
        {
            if (_cache != null) return _cache;
            try
            {
                _cache = System.IO.File.Exists(SettingsFile)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(
                          System.IO.File.ReadAllText(SettingsFile)) ?? new()
                    : new();
            }
            catch { _cache = new(); }
            return _cache;
        }
    }

    private static void ApplySaved(IStrategy strategy)
    {
        if (!Cache.TryGetValue(strategy.Name, out var saved)) return;
        foreach (var (key, elem) in saved)
        {
            if (!strategy.Settings.TryGetValue(key, out var def)) continue;
            try
            {
                strategy.Settings[key] = def switch
                {
                    bool    => elem.GetBoolean(),
                    decimal => elem.GetDecimal(),
                    int     => elem.GetInt32(),
                    string  => elem.GetString() ?? (string)def,
                    _       => def
                };
            }
            catch { }
        }
    }

    private static void PersistNow(IStrategy strategy)
    {
        Cache[strategy.Name] = strategy.Settings.ToDictionary(
            kvp => kvp.Key,
            kvp => JsonSerializer.SerializeToElement(kvp.Value));
        try
        {
            System.IO.File.WriteAllText(SettingsFile,
                JsonSerializer.Serialize(Cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ── Constructeur ──────────────────────────────────────────────────────────

    public StrategyPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Strategies))
                PopulateDropdown();
            if (e.PropertyName == nameof(MainViewModel.CanChangeStrategy))
                UpdateLockState();
        };

        PopulateDropdown();
        UpdateLockState();
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    private void PopulateDropdown()
    {
        CmbStrategy.Items.Clear();

        if (_vm.Strategies.Count == 0)
        {
            TxtNoStrategy.Visibility = Visibility.Visible;
            PanelSettings.Visibility = Visibility.Collapsed;
            return;
        }

        TxtNoStrategy.Visibility = Visibility.Collapsed;

        foreach (var s in _vm.Strategies)
            CmbStrategy.Items.Add(s.Name);

        if (_vm.SelectedStrategy is null && _vm.Strategies.Count > 0)
        {
            CmbStrategy.SelectedIndex = 0;
            _vm.SelectedStrategy      = _vm.Strategies[0];
        }
        else if (_vm.SelectedStrategy is not null)
        {
            int idx = _vm.Strategies.FindIndex(s => s.Name == _vm.SelectedStrategy.Name);
            CmbStrategy.SelectedIndex = idx >= 0 ? idx : 0;
        }

        RenderSettings(_vm.SelectedStrategy);
    }

    private void CmbStrategy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbStrategy.SelectedIndex < 0 || CmbStrategy.SelectedIndex >= _vm.Strategies.Count)
            return;

        var strategy = _vm.Strategies[CmbStrategy.SelectedIndex];
        _vm.SelectedStrategy = strategy;
        RenderSettings(strategy);
    }

    // ── Settings dynamiques ───────────────────────────────────────────────────

    private void RenderSettings(IStrategy? strategy)
    {
        SettingsContainer.Children.Clear();

        if (strategy is null || strategy.Settings.Count == 0)
        {
            PanelSettings.Visibility = Visibility.Collapsed;
            return;
        }

        ApplySaved(strategy);
        PanelSettings.Visibility = Visibility.Visible;

        var sections = strategy.SettingSections;

        if (sections.Count == 0)
        {
            // Affichage plat — pas de sections définies
            foreach (var (key, value) in strategy.Settings)
                AddRow(strategy, key, value);
            return;
        }

        // Affichage par sections
        var rendered = new HashSet<string>();

        foreach (var (section, keys) in sections)
        {
            SettingsContainer.Children.Add(BuildSectionHeader(section));

            foreach (var key in keys)
            {
                if (!strategy.Settings.TryGetValue(key, out var value)) continue;
                AddRow(strategy, key, value);
                rendered.Add(key);
            }
        }

        // Settings restants non assignés à une section
        foreach (var (key, value) in strategy.Settings)
        {
            if (!rendered.Contains(key))
                AddRow(strategy, key, value);
        }
    }

    private void AddRow(IStrategy strategy, string key, object value)
    {
        var row = BuildSettingRow(strategy, key, value);
        if (row is not null)
            SettingsContainer.Children.Add(row);
    }

    // ── Header de section ─────────────────────────────────────────────────────

    private static UIElement BuildSectionHeader(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 8) };

        panel.Children.Add(new TextBlock
        {
            Text       = title.ToUpperInvariant(),
            FontSize   = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x72)),
            Margin     = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new Rectangle
        {
            Height = 1,
            Fill   = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x38)),
        });

        return panel;
    }

    // ── Ligne de setting ──────────────────────────────────────────────────────

    private static UIElement? BuildSettingRow(IStrategy strategy, string key, object value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        grid.Children.Add(new TextBlock
        {
            Text              = FormatLabel(key),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 12,
        });

        UIElement? control = BuildControl(strategy, key, value);
        if (control is null) return null;

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement? BuildControl(IStrategy strategy, string key, object value)
    {
        // ── Dropdown si choices définis ────────────────────────────────────
        if (value is string sChoice && strategy.SettingChoices.TryGetValue(key, out var opts))
        {
            var cmb = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var o in opts) cmb.Items.Add(o);
            cmb.SelectedItem = sChoice;
            cmb.SelectionChanged += (_, _) =>
            {
                if (cmb.SelectedItem is string s) { strategy.Settings[key] = s; PersistNow(strategy); }
            };
            return cmb;
        }

        switch (value)
        {
            case bool bVal:
            {
                var cb = new CheckBox { IsChecked = bVal, VerticalAlignment = VerticalAlignment.Center };
                cb.Checked   += (_, _) => { strategy.Settings[key] = true;  PersistNow(strategy); };
                cb.Unchecked += (_, _) => { strategy.Settings[key] = false; PersistNow(strategy); };
                return cb;
            }
            case decimal dVal:
            {
                var tb = new TextBox { Text = dVal.ToString("G"), Width = 120, HorizontalAlignment = HorizontalAlignment.Right };
                tb.LostFocus += (_, _) =>
                {
                    if (decimal.TryParse(tb.Text, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var d))
                    { strategy.Settings[key] = d; PersistNow(strategy); }
                };
                return tb;
            }
            case int iVal:
            {
                var tb = new TextBox { Text = iVal.ToString(), Width = 120, HorizontalAlignment = HorizontalAlignment.Right };
                tb.LostFocus += (_, _) =>
                {
                    if (int.TryParse(tb.Text, out var i)) { strategy.Settings[key] = i; PersistNow(strategy); }
                };
                return tb;
            }
            case string sVal:
            {
                var tb = new TextBox { Text = sVal, Width = 160, HorizontalAlignment = HorizontalAlignment.Right };
                tb.LostFocus += (_, _) => { strategy.Settings[key] = tb.Text; PersistNow(strategy); };
                return tb;
            }
        }

        return null;
    }

    private static string FormatLabel(string key)
        => System.Text.RegularExpressions.Regex.Replace(key, @"(?<=[a-z])(?=[A-Z])", " ");

    // ── Lock ──────────────────────────────────────────────────────────────────

    private void UpdateLockState()
    {
        bool can = _vm.CanChangeStrategy;
        CmbStrategy.IsEnabled        = can;
        TxtStrategyLocked.Visibility = can ? Visibility.Collapsed : Visibility.Visible;
    }
}
