using System;
using System.Collections.Generic;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

public sealed class IchimokuAltiTrading : IStrategy
{
    // ─── Identité ─────────────────────────────────────────────────────────────

    public string Name => "Ichimoku AltiTrading";

    private string Mode     => GetStr("Mode",     "IntraDay");
    private string ExitType => GetStr("ExitType", "PivotKijun");
    private string TpLevel  => GetStr("TpLevel",  "R1S1");

    public string Timeframe => Mode switch
    {
        "ScalpingGourmand" => "M1",
        "Scalping"         => "M5",
        _                  => "M15",
    };
    private string ContextTf => Mode switch
    {
        "ScalpingGourmand" => "M5",
        "Scalping"         => "M15",
        _                  => "H1",
    };
    private string TrendTf => Mode switch
    {
        "ScalpingGourmand" => "M15",
        "Scalping"         => "H1",
        _                  => "H4",
    };

    public List<string> RequiredTimeframes => new() { Timeframe, ContextTf, TrendTf };
    public List<string> Indicators         => new() { "Ichimoku" };

    // ─── Dropdowns ────────────────────────────────────────────────────────────

    public Dictionary<string, string[]> SettingChoices => new()
    {
        ["Mode"]     = new[] { "IntraDay", "Scalping", "ScalpingGourmand" },
        ["ExitType"] = new[] { "PivotKijun", "Pivot", "Kijun" },
        ["TpLevel"]  = new[] { "R1S1", "R2S2" },
    };

    // ─── Sections UI ──────────────────────────────────────────────────────────

    public Dictionary<string, string[]> SettingSections => new()
    {
        ["Mode & Timeframe"] = new[]
        {
            "Mode",
        },
        ["Risque"] = new[]
        {
            "RiskPercent", "MaxDailyDrawdown", "MinRiskReward",
        },
        ["Stop Loss / Take Profit"] = new[]
        {
            "ExitType", "TpLevel", "SlBufferPips", "KijunInverseMinR",
        },
        ["Fenêtre horaire"] = new[]
        {
            "Trading24h", "HoraireDebut", "HoraireFin",
        },
        ["Qualité du signal"] = new[]
        {
            "KijunBreakoutPct", "WickEntry", "TenkanBreak",
            "TenkanPartielle", "EarlyEntry", "KijunBounce",
        },
        ["Options avancées séparées"] = new[]
        {
            "RangeTrading", "Divergence", "Exit2xRR", "OptimizedExit",
        },
        ["Breakeven"] = new[]
        {
            "BreakevenActive", "BreakevenRatio",
        },
    };

    // ─── Risk ─────────────────────────────────────────────────────────────────

    public decimal RiskPercent             => GetDecimal("RiskPercent",      1.0m);
    public int     MaxSimultaneousTrades   => 1;
    public decimal MaxDailyDrawdownPercent => GetDecimal("MaxDailyDrawdown", 3.0m);

    // ─── Helpers settings ─────────────────────────────────────────────────────

    private string  GetStr(string k, string  d) => Settings.TryGetValue(k, out var v) ? v?.ToString() ?? d : d;
    private decimal GetDecimal(string k, decimal d) => Settings.TryGetValue(k, out var v) ? Convert.ToDecimal(v) : d;
    private int     GetInt(string k, int d)     => Settings.TryGetValue(k, out var v) ? Convert.ToInt32(v) : d;
    private bool    GetBool(string k, bool d)   => Settings.TryGetValue(k, out var v) ? v is bool b ? b : d : d;

    // ─── Stop Loss ────────────────────────────────────────────────────────────

    public StopLossRule StopLoss => new StopLossRule
    {
        Type          = StopLossType.Custom,
        CustomCompute = (iv, dir) =>
        {
            double pip    = iv.Close > 50 ? 0.01 : 0.0001;
            double buffer = (double)GetDecimal("SlBufferPips", 2m) * pip;
            return dir == "BUY" ? iv.Low - buffer : iv.High + buffer;
        },
    };

    // ─── Take Profit ──────────────────────────────────────────────────────────

    public TakeProfitRule TakeProfit => new TakeProfitRule
    {
        Type          = TakeProfitType.Custom,
        CustomCompute = (iv, dir, entry, sl) =>
        {
            // Kijun only → TP désactivé (sortie sur cassure Kijun uniquement)
            if (ExitType == "Kijun")
            {
                double pip = iv.Close > 50 ? 0.01 : 0.0001;
                return dir == "BUY" ? entry + 99999 * pip : entry - 99999 * pip;
            }

            // Exit2xRR → priorité sur les pivots
            if (GetBool("Exit2xRR", false))
            {
                double dist = Math.Abs(entry - sl);
                return dir == "BUY" ? entry + dist * 2.0 : entry - dist * 2.0;
            }

            bool agressif = TpLevel == "R2S2";
            double target = dir == "BUY"
                ? (agressif ? iv.PivotR2 : iv.PivotR1)
                : (agressif ? iv.PivotS2 : iv.PivotS1);

            // Fallback pivot pas encore disponible
            if (target == 0)
            {
                double dist = Math.Abs(entry - sl);
                return dir == "BUY" ? entry + dist * 1.5 : entry - dist * 1.5;
            }
            return target;
        },
    };

    // ─── Settings ─────────────────────────────────────────────────────────────

    public Dictionary<string, object> Settings { get; } = new()
    {
        // Mode & Timeframe
        ["Mode"]             = "IntraDay",

        // Risque
        ["RiskPercent"]      = 1.0m,
        ["MaxDailyDrawdown"] = 3.0m,
        ["MinRiskReward"]    = 1.0m,

        // Stop Loss / Take Profit
        ["ExitType"]         = "PivotKijun",
        ["TpLevel"]          = "R1S1",
        ["SlBufferPips"]     = 2m,
        ["KijunInverseMinR"] = 0.5m,   // pips min que le close doit dépasser la Kijun inverse

        // Fenêtre horaire
        ["Trading24h"]       = false,
        ["HoraireDebut"]     = 3,
        ["HoraireFin"]       = 17,

        // Qualité du signal
        ["KijunBreakoutPct"] = 0m,     // % du range (H-L) que le close doit dépasser la Kijun
        ["WickEntry"]        = false,  // autoriser entrée sur la mèche (High/Low) plutôt que clôture
        ["TenkanBreak"]      = false,  // exiger cassure franche de la Tenkan (Close > Tenkan strict)
        ["TenkanPartielle"]  = false,  // accepter cassure Tenkan partielle (précédente bougie)
        ["EarlyEntry"]       = false,  // entrer dès la cassure Tenkan sans attendre la Kijun
        ["KijunBounce"]      = false,  // signal de continuation : rebond sur Kijun dans tendance

        // Options avancées séparées
        ["RangeTrading"]     = false,  // autoriser les trades quand TrendState = Range
        ["Divergence"]       = false,  // filtre MACD divergence comme confirmation
        ["Exit2xRR"]         = false,  // sortir à exactement 2× le risque initial
        ["OptimizedExit"]    = false,  // sortie optimisée (scale-out progressif)

        // Breakeven
        ["BreakevenActive"]  = false,
        ["BreakevenRatio"]   = 1.0m,
    };

    // ─── Conditions LONG (HARD) ───────────────────────────────────────────────

    public List<StrategyCondition> LongConditions => new()
    {
        new StrategyCondition
        {
            Label      = "H1 Horaire actif",
            Timeframe  = Timeframe,
            Expression = (iv, _) => IsActiveHour(iv),
        },
        new StrategyCondition
        {
            Label      = "H2 Cassure Kijun haussière",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                double pip    = iv.Close > 50 ? 0.01 : 0.0001;
                double pct    = (double)GetDecimal("KijunBreakoutPct", 0m) / 100.0;
                double minGap = pct * (iv.High - iv.Low);

                bool cassure = GetBool("WickEntry", false)
                    ? iv.High    > iv.Kijun + minGap && iv.PrevHigh  <= iv.PrevKijun
                    : iv.Close   > iv.Kijun + minGap && iv.PrevClose <= iv.PrevKijun;

                return cassure;
            },
        },
        new StrategyCondition
        {
            Label      = "H3 Prix au-dessus du nuage",
            Timeframe  = Timeframe,
            Expression = (iv, _) => iv.Close > Math.Max(iv.SenkouA, iv.SenkouB),
        },
        new StrategyCondition
        {
            Label      = "H4 Nuage haussier",
            Timeframe  = Timeframe,
            Expression = (iv, _) => iv.SenkouA > iv.SenkouB,
        },
        new StrategyCondition
        {
            Label      = "H5 Tenkan franchie",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                bool strict = GetBool("TenkanBreak", false) && !GetBool("TenkanPartielle", false);
                return strict
                    ? iv.Close > iv.Tenkan                                 // cassure franche requise
                    : iv.Close > iv.Tenkan || iv.PrevClose > iv.PrevTenkan; // partielle acceptée
            },
        },
        new StrategyCondition
        {
            Label      = "H6 Chikou libre",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
                iv.High26 > 0                                        &&
                iv.Chikou > iv.High26                                &&
                iv.Chikou > Math.Max(iv.SenkouA, iv.SenkouB)         &&
                iv.Chikou > iv.Kijun26                               &&
                iv.Chikou > iv.Tenkan26,
        },
        new StrategyCondition
        {
            Label      = "H7 Pente Tenkan haussière",
            Timeframe  = Timeframe,
            Expression = (iv, _) => iv.Tenkan >= iv.PrevTenkan,
        },
        new StrategyCondition
        {
            Label      = "Contexte haussier",
            Timeframe  = ContextTf,
            Expression = (_, trend) => trend.Trend == TrendDirection.Bull
                || (GetBool("RangeTrading", false) && trend.Trend == TrendDirection.Range),
        },
        new StrategyCondition
        {
            Label      = "Tendance de fond haussière",
            Timeframe  = TrendTf,
            Expression = (_, trend) => trend.Trend == TrendDirection.Bull
                || (GetBool("RangeTrading", false) && trend.Trend == TrendDirection.Range),
        },
    };

    // ─── Conditions SHORT (HARD) ──────────────────────────────────────────────

    public List<StrategyCondition> ShortConditions => new()
    {
        new StrategyCondition
        {
            Label      = "H1 Horaire actif",
            Timeframe  = Timeframe,
            Expression = (iv, _) => IsActiveHour(iv),
        },
        new StrategyCondition
        {
            Label      = "H2 Cassure Kijun baissière",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                double pip    = iv.Close > 50 ? 0.01 : 0.0001;
                double pct    = (double)GetDecimal("KijunBreakoutPct", 0m) / 100.0;
                double minGap = pct * (iv.High - iv.Low);

                bool cassure = GetBool("WickEntry", false)
                    ? iv.Low     < iv.Kijun - minGap && iv.PrevLow  >= iv.PrevKijun
                    : iv.Close   < iv.Kijun - minGap && iv.PrevClose >= iv.PrevKijun;

                return cassure;
            },
        },
        new StrategyCondition
        {
            Label      = "H3 Prix en-dessous du nuage",
            Timeframe  = Timeframe,
            Expression = (iv, _) => iv.Close < Math.Min(iv.SenkouA, iv.SenkouB),
        },
        new StrategyCondition
        {
            Label      = "H4 Nuage baissier",
            Timeframe  = Timeframe,
            Expression = (iv, _) => iv.SenkouA < iv.SenkouB,
        },
        new StrategyCondition
        {
            Label      = "H5 Tenkan franchie",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                bool strict = GetBool("TenkanBreak", false) && !GetBool("TenkanPartielle", false);
                return strict
                    ? iv.Close < iv.Tenkan
                    : iv.Close < iv.Tenkan || iv.PrevClose < iv.PrevTenkan;
            },
        },
        new StrategyCondition
        {
            Label      = "H6 Chikou libre",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
                iv.Low26  > 0                                        &&
                iv.Chikou < iv.Low26                                 &&
                iv.Chikou < Math.Min(iv.SenkouA, iv.SenkouB)         &&
                iv.Chikou < iv.Kijun26                               &&
                iv.Chikou < iv.Tenkan26,
        },
        new StrategyCondition
        {
            Label      = "H7 Pente Tenkan baissière",
            Timeframe  = Timeframe,
            Expression = (iv, _) => iv.Tenkan <= iv.PrevTenkan,
        },
        new StrategyCondition
        {
            Label      = "Contexte baissier",
            Timeframe  = ContextTf,
            Expression = (_, trend) => trend.Trend == TrendDirection.Bear
                || (GetBool("RangeTrading", false) && trend.Trend == TrendDirection.Range),
        },
        new StrategyCondition
        {
            Label      = "Tendance de fond baissière",
            Timeframe  = TrendTf,
            Expression = (_, trend) => trend.Trend == TrendDirection.Bear
                || (GetBool("RangeTrading", false) && trend.Trend == TrendDirection.Range),
        },
    };

    // ─── Sorties forcées ──────────────────────────────────────────────────────

    public List<StrategyCondition> ForceExitConditions
    {
        get
        {
            if (ExitType == "Pivot")
                return new List<StrategyCondition>();

            double pip    = 0.0001;  // approximation initiale, affinée dans l'expression
            double minR   = (double)GetDecimal("KijunInverseMinR", 0m);

            return new List<StrategyCondition>
            {
                new StrategyCondition
                {
                    Label               = "Kijun reverse",
                    Timeframe           = Timeframe,
                    ApplicableDirection = "BUY",
                    Expression          = (iv, _) =>
                    {
                        double p = iv.Close > 50 ? 0.01 : 0.0001;
                        return iv.Close < iv.Kijun - minR * p;
                    },
                },
                new StrategyCondition
                {
                    Label               = "Kijun reverse",
                    Timeframe           = Timeframe,
                    ApplicableDirection = "SELL",
                    Expression          = (iv, _) =>
                    {
                        double p = iv.Close > 50 ? 0.01 : 0.0001;
                        return iv.Close > iv.Kijun + minR * p;
                    },
                },
            };
        }
    }

    public List<StrategyCondition> OptionalExitConditions => new();

    // ─── Helper horaire ───────────────────────────────────────────────────────

    private bool IsActiveHour(IndicatorValues iv)
    {
        if (GetBool("Trading24h", false)) return true;
        int h = iv.CandleTime.Hour;
        return h >= GetInt("HoraireDebut", 3) && h < GetInt("HoraireFin", 17);
    }
}
