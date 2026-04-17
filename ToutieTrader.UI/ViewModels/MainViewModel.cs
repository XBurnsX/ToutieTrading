using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;
using ToutieTrader.Data;

namespace ToutieTrader.UI.ViewModels;

/// <summary>
/// ViewModel central partagé par toutes les pages.
/// Expose : statut connexion, état bot live/replay, stratégies chargées, compte MT5.
///
/// Règles d'état des boutons (step 21) :
///   CanStartTrading  = Python ✓ + MT5 ✓ + !IsReplayRunning
///   CanStartReplay   = !IsLiveRunning + StrategySelected + dates valides
///   CanPauseReplay   = IsReplayRunning
///   CanResetReplay   = !IsReplayRunning (doit Pause d'abord)
///   CanChangeStrategy = !IsLiveRunning
///   CanEditSettings   = !HasOpenLiveTrade
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly MT5ApiClient _mt5;

    // ── Connexion ─────────────────────────────────────────────────────────────

    private bool _isPythonOk;
    private bool _isMt5Ok;
    private string _connectionError = string.Empty;

    public bool IsPythonOk
    {
        get => _isPythonOk;
        private set { if (_isPythonOk != value) { _isPythonOk = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartTrading)); } }
    }

    public bool IsMt5Ok
    {
        get => _isMt5Ok;
        private set { if (_isMt5Ok != value) { _isMt5Ok = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartTrading)); } }
    }

    public string ConnectionError
    {
        get => _connectionError;
        private set { _connectionError = value; OnPropertyChanged(); }
    }

    // ── Compte MT5 ───────────────────────────────────────────────────────────

    private AccountInfo? _account;
    public AccountInfo? Account
    {
        get => _account;
        private set { _account = value; OnPropertyChanged(); }
    }

    // ── État bot ─────────────────────────────────────────────────────────────

    private bool _isLiveRunning;
    private bool _isReplayRunning;
    private bool _hasOpenLiveTrade;

    public bool IsLiveRunning
    {
        get => _isLiveRunning;
        set
        {
            if (_isLiveRunning == value) return;
            _isLiveRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartTrading));
            OnPropertyChanged(nameof(CanStartReplay));
            OnPropertyChanged(nameof(CanChangeStrategy));
        }
    }

    public bool IsReplayRunning
    {
        get => _isReplayRunning;
        set
        {
            if (_isReplayRunning == value) return;
            _isReplayRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartTrading));
            OnPropertyChanged(nameof(CanStartReplay));
            OnPropertyChanged(nameof(CanPauseReplay));
            OnPropertyChanged(nameof(CanResetReplay));
        }
    }

    public bool HasOpenLiveTrade
    {
        get => _hasOpenLiveTrade;
        set
        {
            if (_hasOpenLiveTrade == value) return;
            _hasOpenLiveTrade = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSettings));
        }
    }

    // ── Computed states (step 21) ─────────────────────────────────────────────

    public bool CanStartTrading  => IsPythonOk && IsMt5Ok && !IsReplayRunning && !IsLiveRunning
                                 && SelectedStrategy != null;
    public bool CanStartReplay   => !IsLiveRunning && SelectedStrategy != null && AreReplayDatesValid;
    public bool CanPauseReplay   => IsReplayRunning;
    public bool CanResetReplay   => !IsReplayRunning;
    public bool CanChangeStrategy => !IsLiveRunning;
    public bool CanEditSettings   => !HasOpenLiveTrade;

    // ── Stratégies ───────────────────────────────────────────────────────────

    private List<IStrategy> _strategies = [];
    public List<IStrategy> Strategies
    {
        get => _strategies;
        set { _strategies = value; OnPropertyChanged(); }
    }

    private IStrategy? _selectedStrategy;
    public IStrategy? SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            _selectedStrategy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartReplay));
            OnPropertyChanged(nameof(CanStartTrading));
        }
    }

    // ── Replay dates ──────────────────────────────────────────────────────────

    private DateTime _replayFrom = DateTime.Today.AddMonths(-3);
    private DateTime _replayTo   = DateTime.Today;

    public DateTime ReplayFrom
    {
        get => _replayFrom;
        set { _replayFrom = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreReplayDatesValid)); OnPropertyChanged(nameof(CanStartReplay)); }
    }

    public DateTime ReplayTo
    {
        get => _replayTo;
        set { _replayTo = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreReplayDatesValid)); OnPropertyChanged(nameof(CanStartReplay)); }
    }

    public bool AreReplayDatesValid => ReplayFrom < ReplayTo;

    // ── Replay capital ────────────────────────────────────────────────────────

    private string _replayCapital = "10000";
    public string ReplayCapital
    {
        get => _replayCapital;
        set { _replayCapital = value; OnPropertyChanged(); }
    }

    // ── Settings GLOBAUX du bot (SettingsPage — JAMAIS dans une Strategy) ────

    /// <summary>% du capital risqué par trade. Appliqué à TOUTES les strategies.</summary>
    private decimal _globalRiskPercent = 1.0m;
    public decimal GlobalRiskPercent
    {
        get => _globalRiskPercent;
        set { _globalRiskPercent = value; OnPropertyChanged(); }
    }

    /// <summary>Commission $ par lot par côté. IC Markets ≈ 3.5 USD/lot/side.</summary>
    private decimal _commissionPerLotPerSide = 3.5m;
    public decimal CommissionPerLotPerSide
    {
        get => _commissionPerLotPerSide;
        set { _commissionPerLotPerSide = value; OnPropertyChanged(); }
    }

    // ── Statut affiché ────────────────────────────────────────────────────────

    private string _botStatus = "Inactif";
    public string BotStatus
    {
        get => _botStatus;
        set { _botStatus = value; OnPropertyChanged(); }
    }

    private string _lastAction = "—";
    public string LastAction
    {
        get => _lastAction;
        set { _lastAction = value; OnPropertyChanged(); }
    }

    private double _liveCapital;
    public double LiveCapital
    {
        get => _liveCapital;
        set { if (Math.Abs(_liveCapital - value) < 0.000001) return; _liveCapital = value; OnPropertyChanged(); }
    }

    private int _liveWins;
    public int LiveWins
    {
        get => _liveWins;
        set { if (_liveWins == value) return; _liveWins = value; OnPropertyChanged(); }
    }

    private int _liveLosses;
    public int LiveLosses
    {
        get => _liveLosses;
        set { if (_liveLosses == value) return; _liveLosses = value; OnPropertyChanged(); }
    }

    private double _liveDrawdown;
    public double LiveDrawdown
    {
        get => _liveDrawdown;
        set { if (Math.Abs(_liveDrawdown - value) < 0.000001) return; _liveDrawdown = value; OnPropertyChanged(); }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    public MainViewModel(MT5ApiClient mt5)
    {
        _mt5 = mt5;
        _mt5.OnStatusChanged += OnStatusChanged;
    }

    // ── Polling MT5 ──────────────────────────────────────────────────────────

    private void OnStatusChanged(ConnectionStatus status)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsPythonOk     = status.PythonOk;
            IsMt5Ok        = status.Mt5Ok;
            ConnectionError = string.Empty;
        });
    }

    public void ReportConnectionError(string error)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectionError = error;
        });
    }

    /// <summary>Rafraîchit les données de compte. Appelé par AccueilPage.</summary>
    public async Task RefreshAccountAsync()
    {
        try
        {
            var info = await _mt5.GetAccountInfoAsync();
            Account = info;
            ConnectionError = string.Empty;
        }
        catch (Exception ex)
        {
            ConnectionError = $"Compte MT5: {ex.Message}";
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
