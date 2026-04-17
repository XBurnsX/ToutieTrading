using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToutieTrader.Core.Models;
using ToutieTrader.Data;
using ToutieTrader.UI.Services;
using ToutieTrader.UI.ViewModels;
using ToutieTrader.UI.Windows;

namespace ToutieTrader.UI.Pages;

/// <summary>
/// Page Historique — Phase 09.
/// Affiche tous les trades live (ou replay) depuis DuckDB avec filtres et stats.
/// Double-clic → TradePopupWindow (panneau info complet avec chart DuckDB).
/// </summary>
public partial class HistoriquePage : Page
{
    private readonly MainViewModel    _vm;
    private          TradeRepository? _repo;
    private          ReplayService?   _replay;

    private List<TradeRecord> _allTrades = [];
    private bool _filtersInitialized;

    public HistoriquePage(MainViewModel vm, TradeRepository? repo = null, ReplayService? replay = null)
    {
        InitializeComponent();
        _vm     = vm;
        _repo   = repo;
        _replay = replay;

        // Init plage de dates par défaut : 3 derniers mois
        DpFrom.SelectedDate = DateTime.Today.AddMonths(-3);
        DpTo.SelectedDate   = DateTime.Today;

        DpFrom.DateSelected += (_, _) => { if (_filtersInitialized) ApplyFilters(); };
        DpTo.DateSelected   += (_, _) => { if (_filtersInitialized) ApplyFilters(); };

        Loaded += (_, _) =>
        {
            _filtersInitialized = true;
            LoadAndApply();
        };

        // Rechargement à chaque fois que la page redevient visible (navigation Frame)
        IsVisibleChanged += (_, e) =>
        {
            if (_filtersInitialized && (bool)e.NewValue)
                LoadAndApply();
        };
    }

    // ── Injection tardive des services ───────────────────────────────────────

    public void InitServices(TradeRepository? repo, ReplayService? replay)
    {
        if (_repo != null) return;   // déjà injecté
        _repo   = repo;
        _replay = replay;
        if (_filtersInitialized) LoadAndApply();
    }

    // ── Chargement depuis DuckDB ──────────────────────────────────────────────

    private void LoadAndApply()
    {
        if (_repo == null)
        {
            TxtSubtitle.Text = "Aucune base de données";
            GridTrades.ItemsSource = null;
            ResetStats();
            return;
        }

        try
        {
            _allTrades = _repo.GetAllTrades(isReplay: false);
        }
        catch (Exception ex)
        {
            TxtSubtitle.Text = $"Erreur chargement : {ex.Message}";
            return;
        }

        // Reconstruire le dropdown symboles sans déclencher de filtrage intermédiaire
        string? prevSym = (CmbSymbol.SelectedItem as ComboBoxItem)?.Content?.ToString();
        CmbSymbol.SelectionChanged -= Filter_Changed;
        CmbSymbol.Items.Clear();
        CmbSymbol.Items.Add(new ComboBoxItem { Content = "Tout", IsSelected = true });
        foreach (var sym in _allTrades.Select(t => t.Symbol).Distinct().OrderBy(s => s))
            CmbSymbol.Items.Add(new ComboBoxItem { Content = sym });

        CmbSymbol.SelectedIndex = 0;
        if (prevSym is not null && prevSym != "Tout")
        {
            foreach (ComboBoxItem item in CmbSymbol.Items)
            {
                if (item.Content?.ToString() == prevSym)
                {
                    CmbSymbol.SelectedItem = item;
                    break;
                }
            }
        }
        CmbSymbol.SelectionChanged += Filter_Changed;

        ApplyFilters();
    }

    // ── Filtrage ──────────────────────────────────────────────────────────────

    private void ApplyFilters()
    {
        var filtered = _allTrades.AsEnumerable();

        // Symbole
        string sym = (CmbSymbol.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tout";
        if (sym != "Tout")
            filtered = filtered.Where(t => t.Symbol == sym);

        // Direction
        string dir = (CmbDirection.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tout";
        if (dir != "Tout")
            filtered = filtered.Where(t => t.Direction == dir);

        // Raison sortie
        string reason = (CmbReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tout";
        filtered = reason switch
        {
            "TP"         => filtered.Where(t => t.ExitReason == "TP"),
            "SL"         => filtered.Where(t => t.ExitReason == "SL"),
            "Force Exit" => filtered.Where(t =>
                t.ExitReason?.StartsWith("ForceExit:",    StringComparison.OrdinalIgnoreCase) == true ||
                t.ExitReason?.StartsWith("OptionalExit:", StringComparison.OrdinalIgnoreCase) == true),
            "Ouvert"     => filtered.Where(t => t.ExitTime == null),
            _            => filtered,
        };

        // Date range
        if (DpFrom.SelectedDate is DateTime df)
            filtered = filtered.Where(t => t.EntryTime == null || t.EntryTime.Value.Date >= df.Date);
        if (DpTo.SelectedDate is DateTime dt)
            filtered = filtered.Where(t => t.EntryTime == null || t.EntryTime.Value.Date <= dt.Date);

        var result = filtered.ToList();

        GridTrades.ItemsSource = result.Select(t => new TradeRowVM(t)).ToList();

        UpdateStats(result);

        TxtSubtitle.Text = $"{result.Count} trade{(result.Count != 1 ? "s" : "")} live";
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    private void UpdateStats(List<TradeRecord> trades)
    {
        var closed = trades.Where(t => t.ProfitLoss.HasValue).ToList();
        int wins   = closed.Count(t => t.ProfitLoss!.Value > 0);
        int losses = closed.Count(t => t.ProfitLoss!.Value < 0);

        double totalPnl = closed.Sum(t => t.ProfitLoss!.Value);
        double avgPnl   = closed.Count > 0 ? totalPnl / closed.Count : 0.0;
        double? best    = closed.Count > 0 ? closed.Max(t => t.ProfitLoss) : null;
        double? worst   = closed.Count > 0 ? closed.Min(t => t.ProfitLoss) : null;
        double winRate  = (wins + losses) > 0 ? (double)wins / (wins + losses) * 100.0 : 0.0;

        StatTrades.Text  = trades.Count.ToString();
        StatWins.Text    = wins.ToString();
        StatLosses.Text  = losses.ToString();
        StatWinRate.Text = (wins + losses) > 0 ? $"{winRate:N1}%" : "—";

        if (closed.Count > 0)
        {
            StatPnlTotal.Text       = (totalPnl >= 0 ? "+" : "") + $"{totalPnl:N2} $";
            StatPnlTotal.Foreground = totalPnl >= 0
                ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("DangerBrush");

            StatPnlAvg.Text       = (avgPnl >= 0 ? "+" : "") + $"{avgPnl:N2} $";
            StatPnlAvg.Foreground = avgPnl >= 0
                ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("DangerBrush");
        }
        else
        {
            StatPnlTotal.Text       = "—";
            StatPnlTotal.Foreground = (Brush)FindResource("MutedBrush");
            StatPnlAvg.Text         = "—";
            StatPnlAvg.Foreground   = (Brush)FindResource("MutedBrush");
        }

        StatWinRate.Foreground = winRate switch
        {
            >= 60.0 => (Brush)FindResource("SuccessBrush"),
            >= 40.0 => (Brush)FindResource("TextBrush"),
            _       => wins + losses > 0 ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("MutedBrush"),
        };

        StatBest.Text  = best.HasValue  ? $"+{best.Value:N2} $"  : "—";
        StatWorst.Text = worst.HasValue ? $"{worst.Value:N2} $"  : "—";
    }

    private void ResetStats()
    {
        StatTrades.Text = StatWins.Text = StatLosses.Text = "0";
        StatWinRate.Text = StatPnlTotal.Text = StatPnlAvg.Text = "—";
        StatBest.Text = StatWorst.Text = "—";
    }

    // ── Handlers filtre / boutons ─────────────────────────────────────────────

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_filtersInitialized) return;
        ApplyFilters();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => LoadAndApply();

    private void BtnResetFilters_Click(object sender, RoutedEventArgs e)
    {
        CmbSymbol.SelectedIndex    = 0;
        CmbDirection.SelectedIndex = 0;
        CmbReason.SelectedIndex    = 0;
        DpFrom.SelectedDate        = DateTime.Today.AddMonths(-3);
        DpTo.SelectedDate          = DateTime.Today;
        ApplyFilters();
    }

    // ── Export CSV ────────────────────────────────────────────────────────────

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (GridTrades.ItemsSource is not IEnumerable<TradeRowVM> items) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = "Exporter les trades",
            Filter   = "CSV (*.csv)|*.csv",
            FileName = $"trades_{DateTime.Now:yyyyMMdd_HHmm}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Symbole,Strategy,Direction,Lot,Entree,Sortie,SL,TP,PL,Frais,Raison,DateEntree,DateSortie");
            foreach (var r in items)
            {
                sb.AppendLine(string.Join(",",
                    r.Symbol, r.Strategy, r.Direction, r.LotSize,
                    r.EntryPrice, r.ExitPrice, r.Sl, r.Tp,
                    r.RawProfitLoss?.ToString("N2") ?? "",
                    r.RawFees?.ToString("N2") ?? "",
                    $"\"{r.ExitReason}\"",
                    r.EntryTime, r.ExitTime));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Exporté : {dlg.FileName}", "Export CSV",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur export : {ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Double-clic → popup détail ────────────────────────────────────────────

    private void GridTrades_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridTrades.SelectedItem is not TradeRowVM row) return;

        var popup = new TradePopupWindow(
            row.Source,
            replay:    _replay,
            timeframe: "M15",
            digits:    5);
        popup.Owner = Window.GetWindow(this);
        popup.ShowDialog();
    }


    // ── TradeRowVM — wrapper d'affichage pour le DataGrid ─────────────────────

    private sealed class TradeRowVM
    {
        public TradeRecord Source { get; }
        public TradeRowVM(TradeRecord t) => Source = t;

        // ── Colonnes texte ──────────────────────────────────────────────────
        public string Symbol      => Source.Symbol;
        public string Strategy    => Source.StrategyName;
        public string Direction   => Source.Direction;
        public string LotSize     => Source.LotSize?.ToString("0.##") ?? "—";
        public string EntryPrice  => Source.EntryPrice?.ToString("N5") ?? "—";
        public string ExitPrice   => Source.ExitPrice?.ToString("N5")  ?? "—";
        public string Sl          => Source.Sl?.ToString("N5")         ?? "—";
        public string Tp          => Source.Tp?.ToString("N5")         ?? "—";
        public string Fees        => Source.Fees?.ToString("N2")       ?? "—";
        public string EntryTime   => Source.EntryTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
        public string ExitTime    => Source.ExitTime ?.ToString("yyyy-MM-dd HH:mm") ?? "—";

        public string ExitReason =>
            Source.ExitTime == null ? "Ouvert" :
            Source.ExitReason switch
            {
                null or "" => "—",
                "TP"       => "TP",
                "SL"       => "SL",
                var r when r.StartsWith("ForceExit:",    StringComparison.OrdinalIgnoreCase)
                    => "Sortie " + r["ForceExit:".Length..],
                var r when r.StartsWith("OptionalExit:", StringComparison.OrdinalIgnoreCase)
                    => "Sortie " + r["OptionalExit:".Length..],
                var r => r,
            };

        public string ProfitLoss =>
            Source.ProfitLoss.HasValue
                ? (Source.ProfitLoss.Value >= 0 ? "+" : "") + $"{Source.ProfitLoss.Value:N2} $"
                : "—";

        // ── Props pour tri et coloration ────────────────────────────────────
        public double? RawProfitLoss => Source.ProfitLoss;
        public double? RawFees       => Source.Fees;
        public bool    IsProfit      => Source.ProfitLoss.HasValue && Source.ProfitLoss.Value > 0;
        public bool    IsLoss        => Source.ProfitLoss.HasValue && Source.ProfitLoss.Value < 0;
        public bool    IsOpen        => !Source.ExitTime.HasValue;
    }
}
