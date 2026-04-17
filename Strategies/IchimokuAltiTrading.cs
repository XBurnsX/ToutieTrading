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
        ["Protection"] = new[]
        {
            "ProtectTradeEnabled", "ProtectAtR", "ProtectExtraR",
        },
        ["Fenêtre horaire"] = new[]
        {
            "Trading24h", "HoraireDebut", "HoraireFin",
        },
        ["Qualité du signal"] = new[]
        {
            "KijunBreakoutPct", "CloudBreakoutPct", "TenkanSlopePct",
            "TenkanSlopeLookback", "KijunFlatMaxBars",
            "AtrMinPct", "AtrMaxPct", "CloudThicknessMinPct",
            "DistanceFromCloudMinPct", "DistanceFromCloudMaxPct",
            "DistanceFromKijunMinPct", "DistanceFromKijunMaxPct",
            "WickEntry", "TenkanBreak", "TenkanPartielle",
            "RequirePriceCloud", "RequireCloudDirection",
            "ChikouVsPrice", "ChikouVsKijun", "ChikouVsTenkan",
            "EarlyEntry", "EarlyEntrySeconds", "KijunBounce",
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

    // ─── Optimisation ──────────────────────────────────────────────────────────

    public Dictionary<string, SettingRange> OptimizableRanges => new()
    {
        // Risque
        ["MinRiskReward"]     = new(1.0m,    4.0m,   0.5m),
        ["MaxDailyDrawdown"]  = new(1.0m,    5.0m,   0.5m),
        ["MaxTradesPerSymbolPerDay"] = new(1m, 5m, 1m),
        ["ReentryCooldownMinutes"]   = new(0m, 90m, 15m),

        // Kijun
        ["KijunInverseMinR"]  = new(0.0m,    2.0m,   0.25m),
        ["KijunExitMinR"]     = new(0.5m,    3.0m,   0.5m),
        ["KijunBreakoutPct"]  = new(0.0m,    0.5m,   0.1m),
        ["CloudBreakoutPct"]  = new(0.0m,    0.5m,   0.1m),
        ["TenkanSlopePct"]    = new(0.0m,    0.5m,   0.1m),
        ["TenkanSlopeLookback"] = new(1m,    5m,     1m),
        ["KijunFlatMaxBars"]  = new(0m,      12m,    3m),
        ["AtrMinPct"]         = new(0.0m,    0.3m,   0.05m),
        ["AtrMaxPct"]         = new(0.0m,    1.5m,   0.25m),
        ["CloudThicknessMinPct"] = new(0.0m, 0.5m,   0.1m),
        ["DistanceFromCloudMinPct"] = new(0.0m, 0.5m, 0.1m),
        ["DistanceFromCloudMaxPct"] = new(0.0m, 2.0m, 0.25m),
        ["DistanceFromKijunMinPct"] = new(0.0m, 0.5m, 0.1m),
        ["DistanceFromKijunMaxPct"] = new(0.0m, 2.0m, 0.25m),

        // Stop Loss
        ["SwingLookbackBars"] = new(5m,      20m,    5m),
        ["SlBuf_Fx"]          = new(0.0001m, 0.0005m, 0.0001m),
        ["SlMin_Fx"]          = new(0.0005m, 0.0020m, 0.0005m),
        ["SlBuf_Jpy"]         = new(0.01m,   0.05m,  0.01m),
        ["SlMin_Jpy"]         = new(0.05m,   0.20m,  0.05m),
        ["SlBuf_Mid"]         = new(0.25m,   1.0m,   0.25m),
        ["SlMin_Mid"]         = new(2.0m,    8.0m,   2.0m),
        ["SlBuf_Idx"]         = new(0.5m,    3.0m,   0.5m),
        ["SlMin_Idx"]         = new(5.0m,    20.0m,  5.0m),

        // Breakeven
        ["BreakevenRatio"]    = new(0.5m,    2.0m,   0.5m),

        // Protection
        ["ProtectAtR"]        = new(0.5m,    2.0m,   0.25m),
        ["ProtectExtraR"]     = new(0.0m,    0.5m,   0.1m),

        // Fenêtre horaire
        ["HoraireDebut"]      = new(0m,      10m,    1m),
        ["HoraireFin"]        = new(14m,     22m,    1m),
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

    public List<StopLossProtectionRule> StopLossProtections
    {
        get
        {
            bool protectFeesEnabled = GetBool("ProtectTradeEnabled", true);
            double protectAtR = Math.Max(0.1, (double)GetDecimal("ProtectAtR", 1.0m));
            double extraR = Math.Max(0.0, (double)GetDecimal("ProtectExtraR", 0.0m));
            bool breakevenActive = GetBool("BreakevenActive", false);
            double breakevenAtR = Math.Max(0.1, (double)GetDecimal("BreakevenRatio", 1.0m));
            bool optimizedExit = GetBool("OptimizedExit", false);

            var rules = new List<StopLossProtectionRule>();

            if (protectFeesEnabled)
            {
                rules.Add(new StopLossProtectionRule
                {
                    Label = "Protect fees",
                    Timeframe = Timeframe,
                    ComputeStopLoss = (_, _, trade, ctx) =>
                    {
                        if (ctx.CurrentR < protectAtR)
                            return null;

                        double extra = ctx.RiskDistance * extraR;
                        return trade.Direction == "BUY"
                            ? ctx.FeesCoveredStop + extra
                            : ctx.FeesCoveredStop - extra;
                    },
                });
            }

            if (breakevenActive)
            {
                rules.Add(new StopLossProtectionRule
                {
                    Label = "Breakeven fees",
                    Timeframe = Timeframe,
                    ComputeStopLoss = (_, _, trade, ctx) =>
                    {
                        if (ctx.CurrentR < breakevenAtR)
                            return null;

                        return ctx.FeesCoveredStop;
                    },
                });
            }

            if (optimizedExit)
            {
                rules.Add(new StopLossProtectionRule
                {
                    Label = "Trail previous candle",
                    Timeframe = Timeframe,
                    ComputeStopLoss = (iv, _, trade, ctx) =>
                    {
                        if (ctx.CurrentR < 2.0)
                            return null;

                        return trade.Direction == "BUY"
                            ? Math.Max(ctx.FeesCoveredStop, iv.PrevLow)
                            : Math.Min(ctx.FeesCoveredStop, iv.PrevHigh);
                    },
                });
            }

            return rules;
        }
    }

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

        // Protection du trade ouvert
        ["ProtectTradeEnabled"] = true,
        ["ProtectAtR"]          = 1.0m,
        ["ProtectExtraR"]       = 0.0m,

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
        ["CloudBreakoutPct"] = 0m,     // % du range (H-L) exige au-dessus/en-dessous du nuage
        ["TenkanSlopePct"]   = 0m,     // pente Tenkan minimum en % du range de bougie
        ["TenkanSlopeLookback"] = 1,   // nombre de bougies pour mesurer la pente Tenkan
        ["KijunFlatMaxBars"] = 0,      // 0 = desactive; sinon rejette si Kijun flat trop longtemps
        ["AtrMinPct"]        = 0m,     // 0 = desactive; ATR14 min en % du prix
        ["AtrMaxPct"]        = 0m,     // 0 = desactive; ATR14 max en % du prix
        ["CloudThicknessMinPct"] = 0m, // 0 = desactive; epaisseur min du nuage futur +26 en % du prix
        ["DistanceFromCloudMinPct"] = 0m, // 0 = desactive; distance min prix-nuage courant en % du prix
        ["DistanceFromCloudMaxPct"] = 0m, // 0 = desactive; distance max prix-nuage courant en % du prix
        ["DistanceFromKijunMinPct"] = 0m, // 0 = desactive; distance min close-Kijun en % du prix
        ["DistanceFromKijunMaxPct"] = 0m, // 0 = desactive; distance max close-Kijun en % du prix
        ["WickEntry"]        = false,  // autoriser entrée sur la mèche (High/Low) plutôt que clôture
        ["TenkanBreak"]      = false,  // exiger cassure franche de la Tenkan (Close > Tenkan strict)
        ["TenkanPartielle"]  = false,  // accepter cassure Tenkan partielle (précédente bougie)
        ["RequirePriceCloud"] = true,  // exiger prix hors nuage
        ["RequireCloudDirection"] = true, // exiger nuage futur +26 dans le sens du trade
        ["ChikouVsPrice"]    = true,   // Chikou libre vs prix 26 périodes
        ["ChikouVsKijun"]    = true,   // Chikou au-dessus/en-dessous Kijun26
        ["ChikouVsTenkan"]   = true,   // Chikou au-dessus/en-dessous Tenkan26
        ["EarlyEntry"]       = false,  // entrer avant la fermeture de la bougie signal
        ["EarlyEntrySeconds"] = 10,    // secondes avant le close de bougie quand EarlyEntry=true
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

                bool bounce = GetBool("KijunBounce", false) &&
                              (iv.Low <= iv.Kijun || iv.PrevLow <= iv.PrevKijun) &&
                              iv.Close > iv.Kijun + minGap &&
                              iv.Close > iv.Open;

                return cassure || bounce;
            },
        },
        new StrategyCondition
        {
            Label      = "H3 Prix au-dessus du nuage",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                if (!GetBool("RequirePriceCloud", true)) return true;
                if (iv.SenkouA <= 0 || iv.SenkouB <= 0) return false;
                double pct = (double)GetDecimal("CloudBreakoutPct", 0m) / 100.0;
                double minGap = pct * (iv.High - iv.Low);
                return iv.Close > Math.Max(iv.SenkouA, iv.SenkouB) + minGap;
            },
        },
        new StrategyCondition
        {
            Label      = "H4 Nuage haussier",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
                !GetBool("RequireCloudDirection", true) ||
                (iv.SenkouA26 > 0 && iv.SenkouB26 > 0 && iv.SenkouA26 > iv.SenkouB26),
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
                iv.High26 > 0 &&
                (!GetBool("ChikouVsPrice", true)  || iv.Chikou > iv.High26) &&
                (!GetBool("ChikouVsKijun", true)  || iv.Chikou > iv.Kijun26) &&
                (!GetBool("ChikouVsTenkan", true) || iv.Chikou > iv.Tenkan26),
        },
        new StrategyCondition
        {
            Label      = "H7 Pente Tenkan haussière",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                double pct = (double)GetDecimal("TenkanSlopePct", 0m) / 100.0;
                bool tenkanOk = IsTenkanSlopeOk(iv, bullish: true, pct);
                bool macdOk = !GetBool("Divergence", false) ||
                              (iv.MacdLine > iv.SignalLine && iv.Histogram > 0);
                return tenkanOk && macdOk;
            },
        },
        new StrategyCondition
        {
            Label      = "H8 Filtres structure haussiers",
            Timeframe  = Timeframe,
            Expression = (iv, _) => PassesStructureFilters(iv, bullish: true),
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

                bool bounce = GetBool("KijunBounce", false) &&
                              (iv.High >= iv.Kijun || iv.PrevHigh >= iv.PrevKijun) &&
                              iv.Close < iv.Kijun - minGap &&
                              iv.Close < iv.Open;

                return cassure || bounce;
            },
        },
        new StrategyCondition
        {
            Label      = "H3 Prix en-dessous du nuage",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                if (!GetBool("RequirePriceCloud", true)) return true;
                if (iv.SenkouA <= 0 || iv.SenkouB <= 0) return false;
                double pct = (double)GetDecimal("CloudBreakoutPct", 0m) / 100.0;
                double minGap = pct * (iv.High - iv.Low);
                return iv.Close < Math.Min(iv.SenkouA, iv.SenkouB) - minGap;
            },
        },
        new StrategyCondition
        {
            Label      = "H4 Nuage baissier",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
                !GetBool("RequireCloudDirection", true) ||
                (iv.SenkouA26 > 0 && iv.SenkouB26 > 0 && iv.SenkouA26 < iv.SenkouB26),
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
                iv.Low26 > 0 &&
                (!GetBool("ChikouVsPrice", true)  || iv.Chikou < iv.Low26) &&
                (!GetBool("ChikouVsKijun", true)  || iv.Chikou < iv.Kijun26) &&
                (!GetBool("ChikouVsTenkan", true) || iv.Chikou < iv.Tenkan26),
        },
        new StrategyCondition
        {
            Label      = "H7 Pente Tenkan baissière",
            Timeframe  = Timeframe,
            Expression = (iv, _) =>
            {
                double pct = (double)GetDecimal("TenkanSlopePct", 0m) / 100.0;
                bool tenkanOk = IsTenkanSlopeOk(iv, bullish: false, pct);
                bool macdOk = !GetBool("Divergence", false) ||
                              (iv.MacdLine < iv.SignalLine && iv.Histogram < 0);
                return tenkanOk && macdOk;
            },
        },
        new StrategyCondition
        {
            Label      = "H8 Filtres structure baissiers",
            Timeframe  = Timeframe,
            Expression = (iv, _) => PassesStructureFilters(iv, bullish: false),
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

    private bool IsTenkanSlopeOk(IndicatorValues iv, bool bullish, double pct)
    {
        int lookback = Math.Max(1, GetInt("TenkanSlopeLookback", 1));
        double referenceTenkan = TenkanAtLookback(iv, lookback);
        double minSlope = pct * Math.Max(0, iv.High - iv.Low) * lookback;

        return bullish
            ? iv.Tenkan - referenceTenkan >= minSlope
            : referenceTenkan - iv.Tenkan >= minSlope;
    }

    private bool PassesStructureFilters(IndicatorValues iv, bool bullish)
    {
        if (!PassesAtrFilter(iv)) return false;
        if (!PassesCloudThicknessFilter(iv)) return false;
        if (!PassesDistanceFromCloudFilter(iv, bullish)) return false;
        if (!PassesDistanceFromKijunFilter(iv, bullish)) return false;
        if (!PassesKijunFlatFilter(iv)) return false;
        return true;
    }

    private bool PassesAtrFilter(IndicatorValues iv)
    {
        if (iv.Close <= 0) return true;

        double atrPct = iv.Atr14 > 0 ? iv.Atr14 / iv.Close * 100.0 : 0.0;
        double minPct = Math.Max(0.0, (double)GetDecimal("AtrMinPct", 0m));
        double maxPct = Math.Max(0.0, (double)GetDecimal("AtrMaxPct", 0m));

        if (minPct > 0 && atrPct < minPct) return false;
        if (maxPct > 0 && atrPct > maxPct) return false;
        return true;
    }

    private bool PassesCloudThicknessFilter(IndicatorValues iv)
    {
        double minPct = Math.Max(0.0, (double)GetDecimal("CloudThicknessMinPct", 0m));
        if (minPct <= 0 || iv.Close <= 0) return true;

        if (iv.SenkouA26 <= 0 || iv.SenkouB26 <= 0) return false;
        double cloudPct = Math.Abs(iv.SenkouA26 - iv.SenkouB26) / iv.Close * 100.0;
        return cloudPct >= minPct;
    }

    private bool PassesDistanceFromCloudFilter(IndicatorValues iv, bool bullish)
    {
        double minPct = Math.Max(0.0, (double)GetDecimal("DistanceFromCloudMinPct", 0m));
        double maxPct = Math.Max(0.0, (double)GetDecimal("DistanceFromCloudMaxPct", 0m));
        if (minPct <= 0 && maxPct <= 0) return true;
        if (iv.Close <= 0 || iv.SenkouA <= 0 || iv.SenkouB <= 0) return false;

        double top = Math.Max(iv.SenkouA, iv.SenkouB);
        double bottom = Math.Min(iv.SenkouA, iv.SenkouB);
        double distance = bullish
            ? iv.Close - top
            : bottom - iv.Close;

        if (distance <= 0) return false;

        double distancePct = distance / iv.Close * 100.0;
        if (minPct > 0 && distancePct < minPct) return false;
        if (maxPct > 0 && distancePct > maxPct) return false;
        return true;
    }

    private bool PassesDistanceFromKijunFilter(IndicatorValues iv, bool bullish)
    {
        if (iv.Close <= 0 || iv.Kijun <= 0) return true;

        double distancePct = Math.Abs(iv.Close - iv.Kijun) / iv.Close * 100.0;
        double minPct = Math.Max(0.0, (double)GetDecimal("DistanceFromKijunMinPct", 0m));
        double maxPct = Math.Max(0.0, (double)GetDecimal("DistanceFromKijunMaxPct", 0m));

        bool correctSide = bullish ? iv.Close > iv.Kijun : iv.Close < iv.Kijun;
        if (!correctSide) return false;
        if (minPct > 0 && distancePct < minPct) return false;
        if (maxPct > 0 && distancePct > maxPct) return false;
        return true;
    }

    private bool PassesKijunFlatFilter(IndicatorValues iv)
    {
        int maxFlatBars = GetInt("KijunFlatMaxBars", 0);
        if (maxFlatBars <= 0) return true;

        int flatBars = CountFlatKijunBars(iv, maxFlatBars + 1);
        return flatBars <= maxFlatBars;
    }

    private static double TenkanAtLookback(IndicatorValues iv, int lookback)
        => MidpointAtLookback(iv.RecentHighs, iv.RecentLows, 9, lookback, iv.PrevTenkan > 0 ? iv.PrevTenkan : iv.Tenkan);

    private static double KijunAtLookback(IndicatorValues iv, int lookback)
        => MidpointAtLookback(iv.RecentHighs, iv.RecentLows, 26, lookback, iv.PrevKijun > 0 ? iv.PrevKijun : iv.Kijun);

    private static double MidpointAtLookback(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        int period,
        int lookback,
        double fallback)
    {
        if (highs.Count == 0 || lows.Count == 0) return fallback;

        int count = Math.Min(highs.Count, lows.Count);
        int end = count - 1 - Math.Max(0, lookback);
        if (end < 0) return fallback;

        int start = Math.Max(0, end - period + 1);
        double high = highs[start];
        double low = lows[start];
        for (int i = start + 1; i <= end; i++)
        {
            if (highs[i] > high) high = highs[i];
            if (lows[i] < low) low = lows[i];
        }

        return (high + low) / 2.0;
    }

    private static int CountFlatKijunBars(IndicatorValues iv, int maxScan)
    {
        if (iv.Kijun <= 0 || iv.Close <= 0) return 0;

        double tolerance = Math.Max(iv.Close * 0.000001, 0.0000001);
        int flat = 0;
        double current = iv.Kijun;
        int scan = Math.Max(1, maxScan);

        for (int lookback = 1; lookback <= scan; lookback++)
        {
            double previous = KijunAtLookback(iv, lookback);
            if (Math.Abs(current - previous) > tolerance)
                break;
            flat++;
            current = previous;
        }

        return flat;
    }

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
