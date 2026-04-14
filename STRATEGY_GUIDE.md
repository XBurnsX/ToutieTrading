# TOUTIETRADER — GUIDE DE CRÉATION DE STRATEGY

> Ce fichier est la référence complète pour créer une Strategy compatible avec ToutieTrader.
> Toute Strategy doit respecter ce contrat à 100%. Aucune supposition permise.

---

## ⚠️ RÈGLES ABSOLUES

- La Strategy **DÉCLARE** ses conditions. Le moteur les **LIT** et les **ÉVALUE**.
- La Strategy ne calcule **JAMAIS** les valeurs des indicateurs elle-même.
- La Strategy ne calcule **JAMAIS** le risk, le lot size, ou la taille de position.
- La Strategy ne lit **JAMAIS** directement DuckDB ou MT5.
- La Strategy ne **SURCHARGE JAMAIS** un comportement du moteur.
- **Zéro méthode ShouldExit()** — supprimée définitivement. Toutes les sorties via listes de conditions.
- TOUT ce que la Strategy expose dans `Settings` apparaît automatiquement dans la page Strategy de l'UI.
- Les heures sont gérées par le moteur — la Strategy ne manipule jamais les fuseaux horaires.
- `Settings["RiskPercent"]` surcharge `RiskPercent` si les deux sont présents — Settings gagnent toujours.
- Types valides dans Settings : **bool** (toggle), **decimal** (textfield), **int** (textfield). Rien d'autre.

---

## DÉPLOIEMENT — PLUG & PLAY

1. Placer le fichier `.cs` dans `/Strategies/`
2. Redémarrer le bot
3. Le bot compile le `.cs` automatiquement via **Roslyn** (Microsoft.CodeAnalysis)
4. La Strategy apparaît dans le dropdown de la page Strategy
5. Zéro Visual Studio requis. Zéro recompilation du bot. Zéro config.

Si le `.cs` contient une erreur → message clair dans l'UI, le bot continue sans cette Strategy.
Si le fichier est retiré → la Strategy disparaît au prochain redémarrage.
Fonctionne même après installation via `setup.exe` — les `.cs` restent dans `/Strategies/`.

---

## RÈGLES DE NOMMAGE

- Nom du fichier : `NomDeLaStrategieStrategy.cs`
- Namespace : `ToutieTrader.Strategies`
- Dossier : `/Strategies/`
- Classe : implémente `IStrategy` (namespace `ToutieTrader.Core.Interfaces`)

---

## CE QUE LE MOTEUR FOURNIT À LA STRATEGY

Le moteur calcule TOUT. La Strategy lit, ne recalcule jamais.

### IndicatorValues (par symbol + timeframe)
```csharp
decimal Tenkan          // Tenkan-sen Ichimoku
decimal Kijun           // Kijun-sen Ichimoku
decimal SenkouA         // Senkou Span A (cloud)
decimal SenkouB         // Senkou Span B (cloud)
decimal SenkouA_26      // Senkou Span A futur (+26 bougies)
decimal SenkouB_26      // Senkou Span B futur (+26 bougies)
decimal Chikou          // Chikou Span
decimal MacdLine        // Ligne MACD
decimal SignalLine      // Ligne Signal MACD
decimal Histogram       // Histogramme MACD
decimal Ema50           // EMA 50
decimal Ema200          // EMA 200
decimal Close           // Close de la bougie courante
decimal Open            // Open de la bougie courante
decimal High            // High de la bougie courante
decimal Low             // Low de la bougie courante
```

### TrendState (par symbol + timeframe)
```csharp
Trend Value  // Bull | Bear | Range
// Défini par le TrendEngine global — non modifiable par la Strategy
// Bull  = EMA50 + EMA200 montent + HH/HL + prix au-dessus cloud
// Bear  = EMA50 + EMA200 descendent + LL/LH + prix sous cloud
// Range = EMA50 ou EMA200 plate + compression LH/HL
```

> ⚠️ Une Strategy PEUT ignorer TrendState — elle l'inclut juste pas dans ses conditions.
> Une Strategy NE PEUT PAS modifier la définition Bull/Bear/Range.
> Une Strategy NE PEUT PAS demander des indicateurs non fournis par IndicatorEngine.

---

## STRUCTURE D'UNE STRATEGYCONDITION

```csharp
new StrategyCondition
{
    Label = "Prix sous le cloud",   // Affiché dans tooltip hover + popup trade
    Timeframe = "H1",               // TF sur lequel évaluer — doit être dans RequiredTimeframes
    Expression = (ind, trend) => ind.Close < Math.Min(ind.SenkouA, ind.SenkouB)
    // ind = IndicatorValues pour ce TF
    // trend = TrendState pour ce TF
    // Retourne bool
}
```

**Évaluation :** sur la bougie **courante**.
**Bougie précédente :** si nécessaire, stocker l'état précédent via un champ privé dans la Strategy.

---

## TEMPLATE COMPLET

```csharp
using ToutieTrader.Core.Interfaces;
using ToutieTrader.Core.Models;

namespace ToutieTrader.Strategies
{
    public class NomDeLaStrategieStrategy : IStrategy
    {
        // ─────────────────────────────────────────
        // IDENTITÉ
        // ─────────────────────────────────────────

        public string Name => "Nom de la Stratégie";

        // TF affiché dans le chart du Replay
        // Valeurs valides : "M1" | "M5" | "M15" | "H1" | "H4" | "D"
        public string Timeframe => "H1";

        // TF que le moteur doit calculer pour cette Strategy
        // Doit inclure Timeframe + tous les TF utilisés dans les conditions
        public List<string> RequiredTimeframes => new() { "M5", "M15", "H1" };

        // Indicateurs affichés sur le chart
        // Valeurs valides UNIQUEMENT : "Ichimoku" | "MACD" | "EMA50" | "EMA200"
        public List<string> Indicators => new() { "Ichimoku", "EMA50", "EMA200" };

        // ─────────────────────────────────────────
        // GESTION DU RISQUE
        // ─────────────────────────────────────────

        // Valeur par défaut — surchargée par Settings["RiskPercent"] si présent
        public decimal RiskPercent => 1.0m;

        public int MaxSimultaneousTrades => 3;

        public decimal MaxDailyDrawdownPercent => 5.0m;

        // ─────────────────────────────────────────
        // STOP LOSS
        // Type : AboveCloud | BelowCloud | SwingHigh | SwingLow | Fixed
        // BufferPips : pips de buffer au-dessus/dessous du niveau calculé
        // ─────────────────────────────────────────
        public StopLossRule StopLoss => new()
        {
            Type = StopLossType.AboveCloud,
            BufferPips = 2
        };

        // ─────────────────────────────────────────
        // TAKE PROFIT
        // Type : RiskRatio | Fixed
        // Ratio : multiplicateur du SL en pips (ex: 2.0 = 2:1)
        // ─────────────────────────────────────────
        public TakeProfitRule TakeProfit => new()
        {
            Type = TakeProfitType.RiskRatio,
            Ratio = 2.0m
        };

        // ─────────────────────────────────────────
        // CONDITIONS D'ENTRÉE LONG
        // TOUTES doivent être vraies pour entrer LONG
        // Si pas de LONG → retourner new List<StrategyCondition>()
        // ─────────────────────────────────────────
        public List<StrategyCondition> LongConditions => new()
        {
            new()
            {
                Label = "Description de la condition",
                Timeframe = "H1",
                Expression = (ind, trend) => /* votre logique */ true
            },
            // Ajouter autant de conditions que nécessaire...
        };

        // ─────────────────────────────────────────
        // CONDITIONS D'ENTRÉE SHORT
        // TOUTES doivent être vraies pour entrer SHORT
        // Si pas de SHORT → retourner new List<StrategyCondition>()
        // ─────────────────────────────────────────
        public List<StrategyCondition> ShortConditions => new()
        {
            new()
            {
                Label = "Description de la condition",
                Timeframe = "H1",
                Expression = (ind, trend) => /* votre logique */ true
            },
        };

        // ─────────────────────────────────────────
        // CONDITIONS DE SORTIE FORCÉE
        // UNE condition vraie = fermeture immédiate du trade
        // exit_reason loggé : "ForceExit:[Label]"
        // Si aucune → retourner new List<StrategyCondition>()
        // ─────────────────────────────────────────
        public List<StrategyCondition> ForceExitConditions => new()
        {
            new()
            {
                Label = "Description de la sortie forcée",
                Timeframe = "H1",
                Expression = (ind, trend) => /* votre logique */ false
            },
        };

        // ─────────────────────────────────────────
        // CONDITIONS DE SORTIE OPTIONNELLE
        // UNE condition vraie + activée dans Settings = fermeture
        // exit_reason loggé : "OptionalExit:[Label]"
        // L'activation/désactivation se fait via Settings (bool)
        // Si aucune → retourner new List<StrategyCondition>()
        // ─────────────────────────────────────────
        public List<StrategyCondition> OptionalExitConditions => new()
        {
            new()
            {
                Label = "Description de la sortie optionnelle",
                Timeframe = "H1",
                Expression = (ind, trend) =>
                    (bool)Settings["SortieOptionnelle"] &&
                    /* votre logique */ false
            },
        };

        // ─────────────────────────────────────────
        // SETTINGS — Affichés dans la page Strategy de l'UI
        // Types valides UNIQUEMENT : bool | decimal | int
        //   bool    → toggle on/off
        //   decimal → textfield numérique
        //   int     → textfield entier
        // Settings["RiskPercent"] surcharge RiskPercent si présent
        // ─────────────────────────────────────────
        public Dictionary<string, object> Settings => new()
        {
            { "SortieOptionnelle", false },  // bool
            { "RiskPercent",       1.0m  },  // decimal — surcharge RiskPercent
            { "MaxTrades",         3     },  // int
            // Ajouter autant d'options que nécessaire...
        };
    }
}
```

---

## COMMENT LE MOTEUR UTILISE LA STRATEGY

1. **Chargement** — StrategyLoader scanne `/Strategies/`, compile via Roslyn, charge tout ce qui implémente `IStrategy`. Apparaît dans le dropdown.

2. **À chaque bougie** — StrategyRunner dans l'ordre :
   - Récupère `IndicatorValues` et `TrendState` pour chaque TF dans `RequiredTimeframes`
   - Évalue chaque condition dans `LongConditions` et `ShortConditions`
   - Si **TOUTES** les conditions d'un sens sont vraies → génère un `TradeSignal`
   - Entrée = **open de la bougie suivante** (jamais sur la bougie du signal)

3. **Gestion des trades ouverts** — À chaque bougie :
   - Vérifie SL et TP
   - Évalue `ForceExitConditions` → UNE vraie = ferme, `exit_reason = "ForceExit:[label]"`
   - Évalue `OptionalExitConditions` → UNE vraie + activée dans Settings = ferme, `exit_reason = "OptionalExit:[label]"`

4. **Hover tooltip** — Label + résultat (✓/✗) de chaque condition affiché pour la bougie survolée.

5. **Popup trade** — `conditions_met` (labels des conditions remplies) + `exit_reason` sauvegardés en DB et affichés dans la popup.

---

## OPTIMIZABLESETTINGS — VERSION CORRIGÉE (OBLIGATOIRE)

**Une Strategy est unique. Elle n'existe pas en double.**
L'IA doit générer **UNE seule** Strategy `.cs` qui contient :
- `Settings` → valeurs actives utilisées en live + replay manuel
- `OptimizableSettings` → plages utilisées par l'Optimizer

> **"Une Strategy est unique. L'Optimizer ne crée pas une autre Strategy, il remplace dynamiquement les valeurs de Settings selon OptimizableSettings."**

---

### ⚠️ RÈGLE ABSOLUE

Chaque paramètre défini dans `Settings` **DOIT** avoir exactement **UNE** entrée correspondante dans `OptimizableSettings`.

- ❌ Aucune duplication de clé
- ❌ Aucune valeur définie deux fois
- ❌ Aucune séparation en deux versions de Strategy
- ❌ Un paramètre dans `Settings` absent de `OptimizableSettings`

---

### Relation entre les deux

```
Settings["RiskPercent"]            = 1.0        → valeur actuelle utilisée
OptimizableSettings["RiskPercent"] = RangeSetting(0.5 → 2.0, step 0.25) → comment cette valeur peut varier
```

---

### Comportement du système

- **Mode Live / Replay manuel** → utilise `Settings` uniquement
- **Mode Optimizer** → remplace dynamiquement les valeurs de `Settings` par les combinaisons générées depuis `OptimizableSettings`

---

### Structure correcte

```csharp
public Dictionary<string, object> Settings => new()
{
    { "RiskPercent", 1.0m },
    { "BufferPips",  2m   },
    { "TpRatio",     2.0m },
    { "MaxTrades",   3    }
};

public Dictionary<string, OptimizableSetting> OptimizableSettings => new()
{
    { "RiskPercent", new RangeSetting(0.5m, 2.0m, 0.25m) },
    { "BufferPips",  new RangeSetting(1m,   5m,   1m)    },
    { "TpRatio",     new RangeSetting(1.0m, 3.0m, 0.5m)  },
    { "MaxTrades",   new RangeSetting(1m,   4m,   1m)    }
};
```

### Cas particulier — paramètre non optimisable

```csharp
{ "SomeParam", new FixedSetting(10m) }
```

---

### Règles OptimizableSettings
- **Tout ce qui est dans `Settings`** doit avoir un équivalent dans `OptimizableSettings`
- **Tout paramètre logique** de la Strategy (seuils, distances, compteurs de bougies) doit être dans `OptimizableSettings`
- Si un paramètre n'a pas de sens à optimiser → utiliser `FixedSetting`
- L'Optimizer génère les combinaisons brute force sur tous les `RangeSetting`
- Minimum 30 trades par combinaison sinon rejetée
- Toujours définir une période in-sample ET out-of-sample

---

## QUESTIONNAIRE OPTIMIZER — À POSER EN PLUS

Quand l'IA crée une Strategy, poser aussi ces questions pour préparer l'OptimizableSettings :

- Quels paramètres numériques dans les conditions pourraient varier? (ex: nombre de bougies, distance en pips, seuil)
- Pour chaque paramètre : quelle est la plage réaliste (min / max) et le pas logique?
- Y'a-t-il des paramètres qui n'ont aucun sens à optimiser (garder fixe)?

---



Quand un utilisateur décrit sa Strategy, poser **TOUTES** ces questions avant d'écrire une ligne de code. Ne rien supposer.

---

### 1. IDENTITÉ
- Quel est le nom de la Strategy?
- C'est une Strategy SHORT seulement, LONG seulement, ou les deux?
- Sur quel timeframe principal on trade? (M1 / M5 / M15 / H1 / H4 / D)
- Est-ce que la Strategy utilise d'autres timeframes pour des filtres? (ex: EMA H1 pour filtrer un trade M5)

---

### 2. CONDITIONS D'ENTRÉE
Pour chaque condition mentionnée, préciser :
- Sur quel timeframe cette condition est évaluée?
- Est-ce que la condition doit être vraie sur la bougie courante ou sur la bougie précédente?
- Est-ce que TOUTES les conditions doivent être vraies en même temps, ou juste certaines?
- Y'a-t-il des conditions supplémentaires non mentionnées? (filtres de tendance, horaires, paires spécifiques)

---

### 3. STOP LOSS
- Comment le SL est calculé? (au-dessus/dessous du cloud, swing high/low, distance fixe en pips)
- Y'a-t-il un buffer de pips? (ex: 2 pips au-dessus du cloud)
- Le SL est fixe après l'entrée ou trailing?

---

### 4. TAKE PROFIT
- Comment le TP est calculé? (ratio 2:1, niveau fixe, autre)
- Si ratio — c'est par rapport au SL en pips?
- Y'a-t-il plusieurs targets? (ex: fermer 50% à 1:1 et laisser courir le reste)

---

### 5. SORTIES
- Y'a-t-il des conditions de sortie FORCÉE pour le SHORT? (ex: chikou retraverse le prix vers le haut)
- Y'a-t-il des conditions de sortie FORCÉE pour le LONG? (ex: chikou retraverse le prix vers le bas)
- Y'a-t-il des conditions de sortie OPTIONNELLE pour le SHORT? (ex: tenkan recroise kijun vers le haut)
- Y'a-t-il des conditions de sortie OPTIONNELLE pour le LONG? (ex: tenkan recroise kijun vers le bas)
- Si sortie optionnelle — activée par défaut ou désactivée?

---

### 6. GESTION DU RISQUE
- Quel % du capital risquer par trade?
- Combien de trades simultanés max sur toutes les paires?
- Y'a-t-il un drawdown journalier max avant que le bot arrête de trader?

---

### 7. SETTINGS EXPOSÉS DANS L'UI
Quelles options l'utilisateur devrait pouvoir ajuster dans la page Strategy sans modifier le code?
(ex: activer/désactiver la sortie optionnelle, changer le % risk, changer le ratio TP, etc.)

---

### EXEMPLE — Questions posées pour une Strategy décrite partiellement

Utilisateur dit :
> SHORT → prix close en dessous du cloud, nuage 26 rouge, chikou traversé nuage, tenkan sous kijun. SL au-dessus du cloud. TP 2:1.

L'IA demande :
1. Ces conditions sont évaluées sur quel TF?
2. La Strategy trade aussi en LONG? Si oui, conditions LONG = inverse exact du SHORT ou différentes?
3. Y'a-t-il des filtres de tendance sur un TF supérieur?
4. SL au-dessus du cloud : Senkou A ou Senkou B (le plus haut)? Buffer de combien de pips?
5. Pour le LONG : SL en dessous du cloud (Senkou A ou B, le plus bas)?
6. TP 2:1 : par rapport à la distance SL en pips?
7. Sortie forcée SHORT? (ex: chikou retraverse le prix vers le haut)
8. Sortie forcée LONG? (ex: chikou retraverse le prix vers le bas)
9. Sortie optionnelle SHORT? (ex: tenkan recroise kijun vers le haut)
10. Sortie optionnelle LONG? (ex: tenkan recroise kijun vers le bas)
11. % risk par trade?
12. Max trades simultanés?
13. Drawdown journalier max?
14. Quelles options dans l'UI sans toucher au code?
