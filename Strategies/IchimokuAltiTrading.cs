using System;
using System.Collections.Generic;
using System.Linq;
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

public sealed class IchimokuAltiTrading : IStrategy
{
    // ─── Identité ─────────────────────────────────────────────────────────────

    public string Name => "Ichimoku AltiTrading";

    private string Mode     => GetStr("Mode",     "IntraDay");
    private string ExitType => GetStr("ExitType", "PivotKijun");
    private string TpLevel  => GetStr("TpLevel",  "R1S1");
    private string SlType   => GetStr("SlType",   "BougieLow");

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

    public List<string> RequiredTimeframes
    {
        get
        {
            var tfs = new List<string> { Timeframe, ContextTf, TrendTf, "D" };
            if (Mode == "ScalpingGourmand")
                tfs.Add("H1");
            return tfs.Distinct().ToList();
        }
    }
    public List<string> Indicators         => new() { "Ichimoku" };

    // ─── Dropdowns ────────────────────────────────────────────────────────────

    public Dictionary<string, string[]> SettingChoices => new()
    {
        ["Mode"]     = new[] { "IntraDay", "Scalping", "ScalpingGourmand" },
        ["ExitType"] = new[] { "PivotKijun", "Pivot", "Kijun" },
        ["TpLevel"]  = new[] { "R1S1", "R2S2" },
        ["SlType"]   = new[] { "BougieLow", "SwingN", "Swing5" },
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
            "MaxDailyDrawdown", "MinRiskReward", "MaxTradesPerSymbolPerDay", "ReentryCooldownMinutes",
        },
        ["Stop Loss / Take Profit"] = new[]
        {
            "ExitType", "TpLevel", "SlType", "SwingLookbackBars", "KijunInverseMinR", "KijunExitMinR",
            "SlBuf_Fx",  "SlMin_Fx",
            "SlBuf_Jpy", "SlMin_Jpy",
            "SlBuf_Mid", "SlMin_Mid",
            "SlBuf_Idx", "SlMin_Idx",
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
    // Le % de risk est UN setting GLOBAL du bot (SettingsPage) — JAMAIS ici.

    public int     MaxSimultaneousTrades   => 1;
    public int     MaxTradesPerSymbolPerDay => GetInt("MaxTradesPerSymbolPerDay", 2);
    public int     ReentryCooldownMinutes  => GetInt("ReentryCooldownMinutes", 30);
    public decimal MaxDailyDrawdownPercent => GetDecimal("MaxDailyDrawdown", 3.0m);

    // ─── Helpers settings ─────────────────────────────────────────────────────

    private string  GetStr(string k, string  d) => Settings.TryGetValue(k, out var v) ? v?.ToString() ?? d : d;
    private decimal GetDecimal(string k, decimal d) => Settings.TryGetValue(k, out var v) ? Convert.ToDecimal(v) : d;
    private int     GetInt(string k, int d)     => Settings.TryGetValue(k, out var v) ? Convert.ToInt32(v) : d;
    private bool    GetBool(string k, bool d)   => Settings.TryGetValue(k, out var v) ? v is bool b ? b : d : d;

    // ─── Stop Loss ────────────────────────────────────────────────────────────
    //
    // Logique :
    //   • SwingN = BUY: Low exact, SELL: High exact sur N bougies. Aucun buffer, aucun minimum.
    //   • Ancrage = bougie signal (BUY: Low, SELL: High) + buffer en unités natives
    //   • Distance minimale imposée (minStopDistance) pour éviter les SL trop serrés
    //     qui font exploser le lot size sur indices / petites bougies.
    //   • Buffer & minStopDistance s'appliquent seulement au mode BougieLow.
    //
    // Profil par défaut (Buffer / MinDistance) :
    //   Close ≥ 5000  → Indices majeurs (US30, NAS100…)  →  1.0   / 10.0
    //   Close ≥ 500   → Indices moyens / XAU             →  0.5   /  4.0
    //   Close ≥ 50    → JPY pairs (USDJPY, EURJPY…)      →  0.02  /  0.08
    //   sinon         → Forex 5-digit standard            →  0.0002 / 0.0008
    //
    // Close vs Entry — limitation connue et acceptée :
    //   Le SL est calculé sur iv.Close (bougie signal N). L'entrée réelle = Open
    //   de la bougie N+1 (± spread). Sur les marchés liquides (FX, indices majeurs),
    //   l'écart est généralement < 1–2 pips → impact négligeable sur le lot sizing.
    //   Sur les marchés à gaps (weekend, news majeure), la distance réelle peut
    //   dépasser le calcul → risque légèrement sous-estimé. Acceptable par design.

    public StopLossRule StopLoss => new StopLossRule
    {
        Type          = StopLossType.Custom,
        CustomCompute = (iv, dir) =>
        {
            var (buffer, minDist) = GetStopProfile(iv.Close);

            if (SlType == "SwingN" || SlType == "Swing5")
                return dir == "BUY"
                    ? LowestRecentLow(iv, GetInt("SwingLookbackBars", 10))
                    : HighestRecentHigh(iv, GetInt("SwingLookbackBars", 10));

            double anchor = dir == "BUY" ? iv.Low - buffer : iv.High + buffer;

            double baseDistance = Math.Abs(iv.Close - anchor);

            double finalDistance = Math.Max(baseDistance, minDist);

            return dir == "BUY"
                ? iv.Close - finalDistance
                : iv.Close + finalDistance;
        },
    };

    /// <summary>
    /// Retourne (Buffer, MinDistance) en unités de prix natives selon la magnitude du prix.
    /// Les valeurs par défaut sont cohérentes avec les 4 classes d'actifs MT5 standard.
    /// Heuristique sur iv.Close — pas besoin du symbole (la signature de CustomCompute reste (iv, dir)).
    /// </summary>
    private (double Buffer, double MinDistance) GetStopProfile(double close)
    {
        if (close >= 5000) return (
            (double)GetDecimal("SlBuf_Idx",  1.0m),
            (double)GetDecimal("SlMin_Idx", 10.0m));   // US30, NAS100, GER40, JP225
        if (close >= 500)  return (
            (double)GetDecimal("SlBuf_Mid",  0.5m),
            (double)GetDecimal("SlMin_Mid",  4.0m));   // XAUUSD, US500, UK100
        if (close >= 50)   return (
            (double)GetDecimal("SlBuf_Jpy",  0.02m),
            (double)GetDecimal("SlMin_Jpy",  0.08m));  // USDJPY, EURJPY, GBPJPY
        return (
            (double)GetDecimal("SlBuf_Fx",   0.0002m),
            (double)GetDecimal("SlMin_Fx",   0.0008m));// EURUSD, GBPUSD, etc.
    }

    // ─── Take Profit ──────────────────────────────────────────────────────────

    private static double LowestRecentLow(IndicatorValues iv, int bars)
    {
        int count = Math.Max(1, bars);
        var lows = iv.RecentLows is { Count: > 0 } ? iv.RecentLows : new[] { iv.Low5 };
        int start = Math.Max(0, lows.Count - count);
        double low = lows[start];
        for (int i = start + 1; i < lows.Count; i++)
            if (lows[i] < low) low = lows[i];
        return low;
    }

    private static double HighestRecentHigh(IndicatorValues iv, int bars)
    {
        int count = Math.Max(1, bars);
        var highs = iv.RecentHighs is { Count: > 0 } ? iv.RecentHighs : new[] { iv.High5 };
        int start = Math.Max(0, highs.Count - count);
        double high = highs[start];
        for (int i = start + 1; i < highs.Count; i++)
            if (highs[i] > high) high = highs[i];
        return high;
    }

    public TakeProfitRule TakeProfit => new TakeProfitRule
    {
        Type          = TakeProfitType.Custom,
        CustomCompute = (iv, dir, entry, sl) =>
        {
            // Kijun only → TP désactivé (sortie sur cassure Kijun uniquement).
            // Valeurs universelles : 1e9 pour BUY (jamais atteint, aucun actif n'y monte),
            // -1.0 pour SELL (prix toujours positifs → candle.Low <= -1 impossible).
            if (ExitType == "Kijun")
                return dir == "BUY" ? 1e9 : -1.0;

            // Exit2xRR → priorité sur les pivots
            if (GetBool("Exit2xRR", false))
            {
                double dist = Math.Abs(entry - sl);
                return dir == "BUY" ? entry + dist * 2.0 : entry - dist * 2.0;
            }

            bool agressif = TpLevel == "R2S2";
            double minRiskReward = Math.Max(2.0, (double)GetDecimal("MinRiskReward", 2.0m));
            double target = NextPivotTarget(iv, dir, entry, sl, agressif, minRiskReward);

            // Fallback pivot pas encore disponible
            if (target == 0)
            {
                double dist = Math.Abs(entry - sl);
                return dir == "BUY" ? entry + dist * minRiskReward : entry - dist * minRiskReward;
            }
            return target;
        },
    };

    private static double NextPivotTarget(
        IndicatorValues iv,
        string dir,
        double entry,
        double sl,
        bool agressif,
        double minRiskReward)
    {
        double risk = Math.Abs(entry - sl);
        if (risk <= 0) return 0;

        var levels = new[]
        {
            iv.PivotS2,
            iv.PivotS1,
            iv.PivotPP,
            iv.PivotR1,
            iv.PivotR2,
        }
        .Where(v => v > 0)
        .Distinct()
        .OrderBy(v => v)
        .ToArray();

        if (levels.Length == 0) return 0;

        if (dir == "BUY")
        {
            var candidates = levels
                .Where(v => v > entry && (v - entry) >= risk * minRiskReward)
                .ToArray();

            if (candidates.Length == 0) return 0;
            return agressif && candidates.Length > 1 ? candidates[1] : candidates[0];
        }

        var shortCandidates = levels
            .Where(v => v < entry && (entry - v) >= risk * minRiskReward)
            .OrderByDescending(v => v)
            .ToArray();

        if (shortCandidates.Length == 0) return 0;
        return agressif && shortCandidates.Length > 1 ? shortCandidates[1] : shortCandidates[0];
    }

    // ─── Settings ─────────────────────────────────────────────────────────────

    public Dictionary<string, object> Settings { get; } = new()
    {
        // Mode & Timeframe
        ["Mode"]             = "IntraDay",

        // Risque
        ["MaxDailyDrawdown"] = 3.0m,
        ["MinRiskReward"]    = 2.0m,
        ["MaxTradesPerSymbolPerDay"] = 2,
        ["ReentryCooldownMinutes"] = 30,

        // Stop Loss / Take Profit
        ["ExitType"]         = "PivotKijun",
        ["TpLevel"]          = "R1S1",
        ["SlType"]           = "BougieLow",  // BougieLow = Low/High bougie signal | SwingN = Low/High le plus extreme des N dernieres bougies
        ["SwingLookbackBars"] = 10,
        ["KijunInverseMinR"] = 0.5m,   // multiplicateur × buffer natif (GetStopProfile) que le close doit dépasser la Kijun inverse
        ["KijunExitMinR"]    = 2.0m,   // Kijun reverse ferme seulement si le trade a atteint au moins ce multiple du risque

        // Profils SL natifs — (Buffer, MinDistance) par classe d'actif (en unités de prix brutes)
        // Tier Forex  : Close < 50    → EURUSD, GBPUSD, AUDUSD…
        ["SlBuf_Fx"]  = 0.0002m, ["SlMin_Fx"]  = 0.0008m,
        // Tier JPY    : 50 ≤ Close < 500 → USDJPY, EURJPY, GBPJPY…
        ["SlBuf_Jpy"] = 0.02m,   ["SlMin_Jpy"] = 0.08m,
        // Tier Mid    : 500 ≤ Close < 5000 → XAUUSD, US500, UK100…
        ["SlBuf_Mid"] = 0.5m,    ["SlMin_Mid"] = 4.0m,
        // Tier Idx    : Close ≥ 5000  → US30, NAS100, GER40, JP225…
        ["SlBuf_Idx"] = 1.0m,    ["SlMin_Idx"] = 10.0m,

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

            double minR = (double)GetDecimal("KijunInverseMinR", 0m);
            double minExitR = (double)GetDecimal("KijunExitMinR", 2.0m);

            // Seuil = KijunInverseMinR × buffer natif de la classe d'actif.
            // Ex: minR=0.5, Forex buffer=0.0002 → seuil = 0.0001 (1 pip environ).
            // Même logique pour indices/JPY/XAU — aucun pip hardcodé.
            return new List<StrategyCondition>
            {
                new StrategyCondition
                {
                    Label               = "Kijun reverse",
                    Timeframe           = Timeframe,
                    ApplicableDirection = "BUY",
                    Expression          = (iv, _) =>
                    {
                        double threshold = minR * GetStopProfile(iv.Close).Buffer;
                        return iv.Close < iv.Kijun - threshold;
                    },
                    TradeExpression     = (iv, _, trade) =>
                    {
                        double threshold = minR * GetStopProfile(iv.Close).Buffer;
                        return iv.Close < iv.Kijun - threshold &&
                               HasReachedMinimumR(trade, iv.Close, minExitR);
                    },
                },
                new StrategyCondition
                {
                    Label               = "Kijun reverse",
                    Timeframe           = Timeframe,
                    ApplicableDirection = "SELL",
                    Expression          = (iv, _) =>
                    {
                        double threshold = minR * GetStopProfile(iv.Close).Buffer;
                        return iv.Close > iv.Kijun + threshold;
                    },
                    TradeExpression     = (iv, _, trade) =>
                    {
                        double threshold = minR * GetStopProfile(iv.Close).Buffer;
                        return iv.Close > iv.Kijun + threshold &&
                               HasReachedMinimumR(trade, iv.Close, minExitR);
                    },
                },
            };
        }
    }

    public List<StrategyCondition> OptionalExitConditions => new();

    private static bool HasReachedMinimumR(TradeRecord trade, double currentPrice, double minR)
    {
        if (!trade.EntryPrice.HasValue || !trade.Sl.HasValue) return false;

        double risk = Math.Abs(trade.EntryPrice.Value - trade.Sl.Value);
        if (risk <= 0) return false;

        double favorableMove = trade.Direction == "BUY"
            ? currentPrice - trade.EntryPrice.Value
            : trade.EntryPrice.Value - currentPrice;

        return favorableMove >= risk * minR;
    }

    // ─── Helper horaire ───────────────────────────────────────────────────────

    private bool IsActiveHour(IndicatorValues iv)
    {
        if (GetBool("Trading24h", false)) return true;
        int h = iv.CandleTime.Hour;
        return h >= GetInt("HoraireDebut", 3) && h < GetInt("HoraireFin", 17);
    }
}
