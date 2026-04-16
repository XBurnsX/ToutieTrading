using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ToutieTrader.Core.Models;

namespace ToutieTrader.UI.Windows;

/// <summary>
/// Popup de détail d'un trade replay (double-clic depuis la liste).
/// Affiche : Symbol, Direction, Prix d'entrée/sortie, SL, TP, P/L, dates, conditions.
/// </summary>
public partial class TradePopupWindow : Window
{
    public TradePopupWindow(TradeRecord trade)
    {
        InitializeComponent();

        // ── En-tête ───────────────────────────────────────────────────────────
        TxtSymbol.Text = trade.Symbol;

        bool isBuy = trade.Direction == "BUY";
        BadgeDir.Background = isBuy
            ? new SolidColorBrush(Color.FromRgb(0, 100, 40))
            : new SolidColorBrush(Color.FromRgb(100, 20, 20));
        TxtDirection.Text = trade.Direction;

        if (trade.ProfitLoss.HasValue)
        {
            double pl = trade.ProfitLoss.Value;
            TxtProfitLoss.Text       = $"{(pl >= 0 ? "+" : "")}{pl:N2} $";
            TxtProfitLoss.Foreground = pl >= 0
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush");
        }

        // ── Prix ──────────────────────────────────────────────────────────────
        TxtEntry.Text = trade.EntryPrice?.ToString("N5") ?? "—";
        TxtExit.Text  = trade.ExitPrice?.ToString("N5")  ?? "—";
        TxtSl.Text    = trade.Sl?.ToString("N5")         ?? "—";
        TxtTp.Text    = trade.Tp?.ToString("N5")         ?? "—";

        // ── Dates ─────────────────────────────────────────────────────────────
        TxtEntryTime.Text = trade.EntryTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
        TxtExitTime.Text  = trade.ExitTime?.ToString("yyyy-MM-dd HH:mm")  ?? "—";

        // ── Conditions ────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(trade.ConditionsMet))
        {
            try
            {
                var labels = JsonSerializer.Deserialize<List<string>>(trade.ConditionsMet);
                ListConditions.ItemsSource = labels ?? [];
            }
            catch
            {
                ListConditions.ItemsSource = new[] { trade.ConditionsMet };
            }
        }

        TxtExitReason.Text = trade.ExitReason ?? "—";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
