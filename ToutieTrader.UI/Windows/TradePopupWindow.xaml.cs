using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;
using ToutieTrader.UI.Services;

namespace ToutieTrader.UI.Windows;

/// <summary>
/// Popup de détail d'un trade replay (double-clic depuis la liste).
/// Chart Ichimoku : via ReplayService.BuildHistoryJsonUpTo (données déjà calculées).
/// </summary>
public partial class TradePopupWindow : Window
{
    private readonly TradeRecord     _trade;
    private readonly ReplayService?  _replay;
    private readonly string          _timeframe;
    private readonly int             _digits;
    private readonly IStrategy?      _strategy;

    public TradePopupWindow(
        TradeRecord    trade,
        ReplayService? replay    = null,
        string         timeframe = "M1",
        int            digits    = 5,
        IStrategy?     strategy  = null)
    {
        InitializeComponent();

        _trade     = trade;
        _replay    = replay;
        _timeframe = string.IsNullOrWhiteSpace(timeframe) ? "M1" : timeframe;
        _digits    = digits is >= 0 and <= 8 ? digits : 5;
        _strategy  = strategy;

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

        string fmt = "N" + _digits;
        TxtEntry.Text = trade.EntryPrice?.ToString(fmt) ?? "—";
        TxtExit.Text  = trade.ExitPrice?.ToString(fmt)  ?? "—";
        TxtSl.Text    = trade.Sl?.ToString(fmt)         ?? "—";
        TxtTp.Text    = trade.Tp?.ToString(fmt)         ?? "—";

        TxtEntryTime.Text = trade.EntryTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
        TxtExitTime.Text  = trade.ExitTime?.ToString("yyyy-MM-dd HH:mm")  ?? "—";

        if (!string.IsNullOrEmpty(trade.ConditionsMet))
        {
            try
            {
                var labels = JsonSerializer.Deserialize<List<string>>(trade.ConditionsMet);
                ListConditions.ItemsSource = labels ?? [];
            }
            catch { ListConditions.ItemsSource = new[] { trade.ConditionsMet }; }
        }

        TxtExitReason.Text = FormatExitReason(trade.ExitReason);

        Loaded += async (_, _) => await InitChartAsync();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Chart ─────────────────────────────────────────────────────────────────

    private async Task InitChartAsync()
    {
        if (_replay == null || _trade.EntryTime == null) return;

        try
        {
            await Chart.EnsureCoreWebView2Async();

            string chartDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart");
            Chart.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "toutie.local", chartDir,
                CoreWebView2HostResourceAccessKind.Allow);

            Chart.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess) PushTradeDataToChart();
            };

            Chart.Source = new Uri("https://toutie.local/trade_chart.html");
        }
        catch (Exception ex)
        {
            ReplayLogger.LogException("TradePopupWindow.InitChartAsync", ex);
        }
    }

    private void PushTradeDataToChart()
    {
        if (_replay == null || _trade.EntryTime == null) return;

        try
        {
            long tfSec = TfSeconds(_timeframe);
            var  entry = _trade.EntryTime.Value;
            var  exit  = _trade.ExitTime ?? entry.AddSeconds(tfSec * 40);
            var  from  = entry.AddSeconds(-tfSec * 80);
            var  to    = exit .AddSeconds( tfSec * 40);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Tente d'abord les données en mémoire (Replay actif).
            // Si null (historique live ou replay terminé) → charge depuis DuckDB.
            var historyJson = _replay.BuildHistoryJsonUpTo(
                                  _trade.Symbol, _timeframe, from, to, _strategy, cts.Token)
                           ?? _replay.BuildHistoryJsonFromDb(
                                  _trade.Symbol, _timeframe, from, to, _strategy, cts.Token);

            if (historyJson == null) return;

            // 1. Envoyer le historyset (candles + Ichimoku déjà calculé)
            Chart.CoreWebView2.PostWebMessageAsString(historyJson);

            // 2. Superposer les lignes et markers du trade
            string fmt = "N" + _digits;
            var lines = new List<object>();
            if (_trade.EntryPrice.HasValue)
                lines.Add(new { price = _trade.EntryPrice.Value, color = "#A0A0B8", title = "ENTRY " + _trade.EntryPrice.Value.ToString(fmt), dashed = false });
            if (_trade.Sl.HasValue)
                lines.Add(new { price = _trade.Sl.Value,         color = "#E8002D", title = "SL "    + _trade.Sl   .Value.ToString(fmt),       dashed = true  });
            if (_trade.Tp.HasValue)
                lines.Add(new { price = _trade.Tp.Value,         color = "#00C85A", title = "TP "    + _trade.Tp   .Value.ToString(fmt),       dashed = true  });
            if (_trade.ExitPrice.HasValue)
                lines.Add(new { price = _trade.ExitPrice.Value,  color = "#FFA500", title = "EXIT "  + _trade.ExitPrice.Value.ToString(fmt),   dashed = false });

            var markers = new List<object>();
            if (_trade.EntryPrice.HasValue && _trade.EntryTime.HasValue)
                markers.Add(new
                {
                    time     = TimeZoneHelper.ToChartUnixSeconds(_trade.EntryTime.Value),
                    position = _trade.Direction == "BUY" ? "belowBar" : "aboveBar",
                    color    = _trade.Direction == "BUY" ? "#00C85A"  : "#E8002D",
                    shape    = _trade.Direction == "BUY" ? "arrowUp"  : "arrowDown",
                    text     = _trade.Direction,
                });
            if (_trade.ExitPrice.HasValue && _trade.ExitTime.HasValue)
                markers.Add(new
                {
                    time     = TimeZoneHelper.ToChartUnixSeconds(_trade.ExitTime.Value),
                    position = "aboveBar",
                    color    = "#FFA500",
                    shape    = "circle",
                    text     = FormatExitReason(_trade.ExitReason),
                });

            var overlay = JsonSerializer.Serialize(new
            {
                type    = "tradeOverlay",
                digits  = _digits,
                lines   = lines,
                markers = markers,
            });
            Chart.CoreWebView2.PostWebMessageAsString(overlay);
        }
        catch (Exception ex)
        {
            ReplayLogger.LogException("TradePopupWindow.PushTradeDataToChart", ex);
        }
    }

    private static long TfSeconds(string tf) => tf switch
    {
        "M1"  => 60,
        "M5"  => 300,
        "M15" => 900,
        "M30" => 1800,
        "H1"  => 3600,
        "H4"  => 14400,
        "D"   => 86400,
        _     => 60,
    };

    private static string FormatExitReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "—";
        return reason switch
        {
            "TP" => "TP",
            "SL" => "SL",
            "ForceExit:Kijun reverse" => "Sortie Kijun reverse",
            _ when reason.StartsWith("ForceExit:", StringComparison.OrdinalIgnoreCase)
                => "Sortie forcee - " + reason["ForceExit:".Length..],
            _ when reason.StartsWith("OptionalExit:", StringComparison.OrdinalIgnoreCase)
                => "Sortie optionnelle - " + reason["OptionalExit:".Length..],
            _ => reason,
        };
    }
}
