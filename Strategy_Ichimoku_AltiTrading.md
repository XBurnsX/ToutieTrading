# ToutieTrader — Stratégie Ichimoku DayTrader PRO (Alti Trading)
## Extraction des règles du cours

---

## 🔑 LES 4 CONDITIONS OBLIGATOIRES POUR OUVRIR UNE POSITION

Chaque condition doit être validée (✅) **avant** d'entrer en position. Si une seule manque = **pas de trade**.

| # | Condition | Description |
|---|-----------|-------------|
| 1 | **Tendance de fond** | Ouvrir **toujours** dans le sens de la tendance de fond. Ne **jamais** être à contre-tendance. |
| 2 | **Nuage Kumo aligné** | Le nuage Kumo doit être configuré dans le **même sens** que la tendance de fond (haussier pour achat, baissier pour vente). |
| 3 | **Cassure franche de la Kijun** | Le prix doit **franchement** casser la Kijun **à la clôture** de la bougie (pas juste une mèche). |
| 4 | **Clôture hors du nuage** | La clôture de la bougie de signal doit se faire **en dehors** du nuage Kumo (au-dessus pour achat, en dessous pour vente). |

---

## 📈 POSITION À L'ACHAT (BUY)

### Conditions d'ouverture
- ✅ **Tendance haussière** (confirmée par le contexte / timeframe supérieur)
- ✅ **Nuage Kumo haussier** (Senkou Span A > Senkou Span B → nuage vert/bleu)
- ✅ **Prix casse franchement la Kijun à la hausse** en clôture de bougie
- ✅ **La clôture de la bougie se fait AU-DESSUS du nuage** (hors du Kumo)
- ✅ **Chikou Span libre** de tout obstacle (pas de bougies/nuage qui bloquent son chemin)
- ✅ **Tenkan SOUS la Kijun** au moment de l'entrée (le prix a pullback, Tenkan a croisé sous la Kijun, puis le prix recasse la Kijun à la hausse MAIS la Tenkan est encore en dessous → on entre tôt avant le TK cross retour)

### Analyse du graphique (Screenshot Achat)
- **Entrée** marquée en bas du mouvement : le prix était sous la Kijun, puis une bougie casse franchement au-dessus de la Kijun ET clôture au-dessus du nuage
- **Sortie** marquée en haut : le prix atteint un sommet et le trade est clôturé
- **Faux signal** identifié : le prix touche/traverse brièvement la Kijun mais **ne clôture pas franchement au-dessus** ou la bougie reste **dans le nuage** → on n'entre PAS

### Pourquoi c'est un faux signal (achat) ?
Le prix a peut-être mèché au-dessus de la Kijun mais :
- La **clôture** est restée en dessous ou au niveau de la Kijun (pas de cassure franche)
- OU la clôture s'est faite **à l'intérieur du nuage** (condition 4 non respectée)
- OU le Chikou Span était bloqué par des bougies/obstacles

---

## 📉 POSITION À LA VENTE (SELL)

### Conditions d'ouverture
- ✅ **Tendance baissière** (confirmée par le contexte / timeframe supérieur)
- ✅ **Nuage Kumo baissier** (Senkou Span A < Senkou Span B → nuage orange/rouge)
- ✅ **Prix casse franchement la Kijun à la baisse** en clôture de bougie
- ✅ **La clôture de la bougie se fait EN DESSOUS du nuage** (hors du Kumo)
- ✅ **Chikou Span libre** de tout obstacle
- ✅ **Tenkan AU-DESSUS de la Kijun** au moment de l'entrée (inverse de l'achat : le prix a pullback vers le haut, Tenkan a croisé au-dessus de la Kijun, puis le prix recasse la Kijun à la baisse MAIS la Tenkan est encore au-dessus → entrée tôt)

### Analyse du graphique (Screenshot Vente)
- **Faux signaux** (cercles orange) : plusieurs fois le prix touche ou traverse la Kijun mais ne satisfait pas toutes les conditions. Soit :
  - La clôture reste dans le nuage
  - La cassure n'est pas "franche" (petite bougie, mèche qui revient)
  - Le nuage n'est pas encore clairement baissier à ce moment
- **Entrée** (label vert) : le prix casse franchement sous la Kijun, la bougie clôture **sous le nuage**, le nuage est baissier → TOUTES les conditions sont réunies → grosse chute qui suit

### Pourquoi les faux signaux (vente) ?
Les cercles oranges montrent des zones où :
1. Le prix **oscillait autour de la Kijun** sans cassure nette → le corps de la bougie ne clôture pas clairement en dessous
2. Le **nuage était encore en transition** (pas clairement baissier, ou le prix clôturait DANS le nuage)
3. La Kijun était plate/horizontale ce qui indique un range → pas de momentum directionnel clair

---

## 🔄 SITUATION 2 : CONTINUATION DE TENDANCE (Rebond sur Kijun)

### Principe
Dans une tendance établie, le prix **revient tester la Kijun** puis **rebondit** dans le sens de la tendance de fond. C'est un signal de **continuation**, pas de retournement.

### Comment ça marche (exemple baissier du screenshot)
- La tendance de fond est **baissière** (confirmée par la ligne rouge descendante)
- La **Kijun** est identifiée (label vert en haut à gauche)
- **Signal 1** : Le prix remonte vers la Kijun, la touche, puis repart à la baisse → continuation
- **Signal 2** : Idem, prix pull back vers la Kijun, rebondit, repart vers le bas
- **Signal 3** : Même pattern, la Kijun agit comme résistance dynamique
- **Signal 4** : Plus bas dans la tendance, le prix reteste encore la Kijun et continue de baisser

### Règles pour la continuation
- La tendance de fond doit être **clairement établie** (pas un range)
- Le prix vient **toucher ou s'approcher de la Kijun** sans la casser franchement
- La Kijun agit comme **support dynamique** (tendance haussière) ou **résistance dynamique** (tendance baissière)
- On entre au **rebond** sur la Kijun dans le sens de la tendance

---

## ⚠️ RÈGLES ADDITIONNELLES (verbales, pas sur les slides)

### Chikou Span libre
- Le **Chikou Span** (lagging line, décalée de 26 périodes en arrière) doit être **libre de TOUT obstacle**
- "Libre" = aucune ligne, bougie, nuage, Tenkan, Kijun ou quoi que ce soit qui pourrait **bloquer** le mouvement du Chikou dans le sens du trade
- Si le Chikou est bloqué par N'IMPORTE QUEL élément → le signal est **faible** ou **invalide**

#### 🔍 Analyse du Chikou sur chaque screenshot

**Screenshot VENTE (13635) :**
- **Faux signaux (cercles orange)** : Le Chikou (ligne bleue) est **empêtré dans les bougies** 26 périodes en arrière. Bougies au-dessus et en dessous → COINCÉ → ❌ invalide
- **Entrée valide (label vert)** : Le Chikou projeté 26 périodes en arrière est **en dessous des bougies** → espace ouvert → LIBRE de descendre → ✅ valide
- **Après l'entrée** : Le Chikou chute librement, bien sous les anciennes bougies → confirme la force

**Screenshot ACHAT (13634) :**
- **Entrée valide (label vert, en bas)** : Le Chikou est sous les bougies historiques avec espace libre au-dessus → peut monter → ✅ valide
- **Faux signal (orange, à droite)** : Le Chikou tombe dans une **zone encombrée de bougies** au sommet → bloqué → ❌ invalide

**Screenshot CONTINUATION (13633) — Signaux 1 à 4 :**
- **Signal 1** : Chikou encore proche des bougies, zone congestionnée → **borderline**, plus faible
- **Signal 2** : Prix assez chuté → Chikou sous les anciennes bougies → espace libre → ✅
- **Signal 3** : Similaire au 2, Chikou a de l'espace → ✅
- **Signal 4** : Chikou **clairement en dessous** de tout → très libre → ✅ signal le plus fort
- **Observation :** Plus la tendance avance, plus le Chikou se dégage → signaux de continuation de plus en plus forts

**Screenshot PIÈGE #1 — Tenkan (13637) :**
- **Gauche (faux signaux)** : Phase de correction → Chikou rebondit dans les bougies → empêtré → ❌
- **Droit (signal valide)** : Chikou casse sous les bougies historiques → espace libre → ✅

**Screenshot PIÈGE #2 — Dans le nuage (13642) :**
- **2 Faux signaux** : Prix coincé dans le nuage → Chikou aussi dans zone encombrée (bougies + nuage) → ❌ doublement bloqué

**Screenshot PIÈGE #3 — Contre-tendance (13638) :**
- **Faux signaux d'achat** : Tendance baissière. Pour acheter, Chikou doit être libre VERS LE HAUT mais les bougies descendantes le bloquent → ❌
- **Signal de vente valide** : Chikou au-dessus des anciennes bougies (prix a chuté), espace libre en dessous → ✅

**Screenshot PIÈGE #5 — News (13641) :**
- **Faux signal news** : Spike violent déplace le Chikou artificiellement → ❌
- **Signal valide (après)** : Volatilité calmée, Chikou repositionné proprement → ✅

#### 📐 Règles Chikou déduites

| Règle | Description |
|-------|-------------|
| **Direction** | Chikou libre dans la direction du trade (au-dessus pour BUY, en dessous pour SELL) |
| **Congestion = danger** | Chikou empêtré dans bougies/nuage/lignes → PAS DE TRADE |
| **Plus libre = plus fort** | Plus d'espace = signal plus fort (Signal 4 > Signal 1 en continuation) |
| **Correction/range** | Pendant les corrections le Chikou est presque toujours coincé → ne pas trader |
| **Post-news** | Spikes déplacent le Chikou artificiellement → attendre la stabilisation |

#### 💻 Logique pour le bot MQL5
```
// Chikou = Close actuel projeté 26 périodes en arrière
chikou_value = Close[0]

// BUY : Chikou AU-DESSUS de tout à position -26
SI chikou_value > High[26] ET
   chikou_value > max(SSA[26], SSB[26]) ET
   chikou_value > Kijun[26] ET
   chikou_value > Tenkan[26]
→ Chikou libre ✅

// SELL : Chikou EN DESSOUS de tout à position -26
SI chikou_value < Low[26] ET
   chikou_value < min(SSA[26], SSB[26]) ET
   chikou_value < Kijun[26] ET
   chikou_value < Tenkan[26]
→ Chikou libre ✅
```

### Position Tenkan vs Kijun (CONFIRMÉ)
- **À L'ACHAT** : La Tenkan doit être **EN DESSOUS** de la Kijun au moment de l'entrée
  - Le prix descend → Tenkan croise sous la Kijun (pullback)
  - Le prix remonte et casse franchement la Kijun à la hausse
  - MAIS la Tenkan est **encore sous la Kijun** (elle n'a pas encore rattrapé)
  - = On entre **tôt** dans le mouvement, avant que la Tenkan recroise au-dessus
- **À LA VENTE** : La Tenkan doit être **AU-DESSUS** de la Kijun au moment de l'entrée
  - Le prix monte → Tenkan croise au-dessus de la Kijun (pullback)
  - Le prix redescend et casse franchement la Kijun à la baisse
  - MAIS la Tenkan est **encore au-dessus** de la Kijun
  - = Même logique, entrée tôt dans la continuation baissière

---

---

## 🚨 LES 5 PIÈGES À ÉVITER

### Piège #1 : Se méfier de la Tenkan
**Règle : N'ouvrir une position QUE SI la Tenkan est DÉJÀ cassée par le prix.**
**Status : RÈGLE DURE pour l'instant. Ajustable plus tard après testing.**

La Tenkan indique la tendance **court terme** du prix. Sa cassure par le prix doit être le **premier critère de validation**, AVANT la cassure de la Kijun.

**Séquence obligatoire :**
1. Le prix casse d'abord la **Tenkan** dans le sens du trade
2. PUIS le prix casse la **Kijun** franchement en clôture
3. On entre seulement quand les DEUX sont cassées

**Effet élastique de la Tenkan :**
- Si la Tenkan est **visuellement trop éloignée** du prix, le prix a besoin de "respirer" (pullback)
- Pas de seuil en pips → c'est un jugement **visuel** : si c'est clairement étiré, c'est trop loin
- **Double red flag** : si la Tenkan est trop loin ET dans la mauvaise position (ex: en dessous de la Kijun pour un SELL), c'est doublement invalide — la Tenkan devrait être AU-DESSUS de la Kijun pour un sell
- Règle pour le bot : pas de seuil fixe codable pour l'instant, potentiellement utiliser un ratio Tenkan-prix / ATR plus tard

**Analyse du graphique (Piège #1) :**
- **Graphique gauche** : "Phase de correction du marché" — les cercles orange montrent des "Faux signaux de vente" où le prix oscille entre la Tenkan et la Kijun. Le prix n'a pas clairement cassé la Tenkan en premier, donc les signaux de vente sont invalides.
- **Graphique droit** : "Signal de vente valide" — le prix a d'abord cassé la Tenkan vers le bas, PUIS cassé la Kijun franchement → entrée valide, grosse chute qui suit.

**Pourquoi c'est des faux signaux :**
- Le prix touche/traverse la Kijun mais la Tenkan n'est pas encore cassée
- OU la Tenkan était en dessous de la Kijun pour un SELL (mauvaise config — pour un sell, Tenkan doit être AU-DESSUS de la Kijun)
- OU l'effet élastique : la Tenkan est trop loin, le prix corrige au lieu de continuer

---

### Piège #2 : Ouvrir une position dans le nuage
**Règle : Ne JAMAIS ouvrir de position quand le prix est DANS le nuage Kumo.**

- Le prix n'arrive pas à casser la Kijun ET sortir du nuage en cassant la SSA (Senkou Span A)
- Quand le prix est dans le nuage = les traders sont **indécis sur la tendance**
- Le nuage agit comme zone de **no-trade** / zone neutre

**Analyse du graphique (Piège #2) :**
- **2 Faux signaux** (cercles orange) : le prix essaie de monter, casse peut-être la Kijun, mais reste **coincé dans le nuage**
- Première tentative : le prix monte mais ne sort pas du nuage par le haut (ne casse pas la SSA)
- Deuxième tentative : le prix **longe la SSA** sans jamais clôturer au-dessus → échec, le prix retombe
- Le nuage = mur. Tant que la clôture est DANS le nuage, on ne trade pas.

**Pour le bot MQL5 :**
```
// Le prix doit être HORS du nuage
kumo_top = max(SSA, SSB)     // la ligne la plus haute du nuage
kumo_bottom = min(SSA, SSB)  // la ligne la plus basse du nuage

SI prix_close > kumo_top → au-dessus du nuage → BUY seulement ✅
SI prix_close < kumo_bottom → en dessous du nuage → SELL seulement ✅
SI prix_close entre kumo_bottom et kumo_top → DANS le nuage ❌ PAS DE TRADE
```

---

### Piège #3 : Ouvrir une position à contre-tendance
**Règle : Ne JAMAIS ouvrir à contre-tendance. Rester patient et attendre le prochain signal dans le sens de la tendance.**

- La tendance est déterminée par l'alignement de : **Nuage Kumo + Kijun + Tenkan**
- Si les 3 pointent à la baisse → tendance clairement baissière → SEULEMENT des SELL
- Si les 3 pointent à la hausse → tendance clairement haussière → SEULEMENT des BUY
- Un pullback temporaire ≠ retournement de tendance

**Analyse du graphique (Piège #3) :**
- La tendance est clairement **baissière** : Nuage Kumo + Kijun + Tenkan tous orientés vers le bas
- **Faux signal d'achat #1** (orange, haut gauche) : le prix fait un petit rebond vers le haut. Ça ressemble à un signal d'achat mais c'est juste une **pause dans la tendance baissière**. Le Kumo est baissier, la Kijun descend → on n'achète PAS.
- **Faux signal d'achat #2** (orange, milieu bas) : même chose, le prix tente de remonter dans la correction mais le contexte reste 100% baissier.
- **Signal de vente valide** (vert) : le prix casse la Kijun vers le bas dans le sens de la tendance → toutes les conditions sont alignées → SELL valide, le prix dégringole.

**Leçon clé :** Quand tu vois un signal d'achat mais que le Kumo + Kijun + Tenkan sont tous baissiers = c'est un **piège**. Reste patient, attends le prochain SELL.

---

### Piège #4 : Attention aux horaires de trading
**Règle : Choisir une plage horaire dynamique. Éviter les heures mortes.**

Le marché est stagnant en dehors des heures actives et les signaux Ichimoku deviennent peu fiables.

**Horaires FRANCE → QUÉBEC (UTC-6) :**

| Catégorie | Heure France | Heure Québec (EST) | Action |
|-----------|-------------|-------------------|--------|
| ❌ **ÉVITER** | Avant 08:00 | Avant 02:00 | Pas de trade |
| ⚠️ **VIGILANT** | 08:00 - 09:00 | 02:00 - 03:00 | Prudence, marché s'ouvre |
| ✅ **DYNAMIQUE** | 09:00 - 12:00 | 03:00 - 06:00 | Meilleure fenêtre |
| ❌ **ÉVITER** | 12:00 - 14:00 | 06:00 - 08:00 | Pause déjeuner Europe |
| ✅ **DYNAMIQUE** | 14:00 - 18:00 | 08:00 - 12:00 | Session US + Europe overlap |
| ⚠️ **VIGILANT** | 18:00 - 19:00 | 12:00 - 13:00 | Fin de session Europe |
| ❌ **ÉVITER** | Après 19:00 | Après 13:00 | Marché stagnant |

**Pour le bot MQL5 :**
```
Fenêtres de trading autorisées (heure Québec EST) :
  - 03:00 - 06:00 (session Europe active)
  - 08:00 - 12:00 (overlap US/Europe)
Fenêtres de prudence (réduire la taille ou filtrer) :
  - 02:00 - 03:00
  - 12:00 - 13:00
Bloqué :
  - 00:00 - 02:00
  - 06:00 - 08:00
  - 13:00 - 00:00
```

---

### Piège #5 : Attention aux news économiques
**Règle : Rester EN DEHORS du marché lors des news importantes pouvant influencer les prix.**

Les news créent des mouvements **inattendus** et violents qui peuvent déclencher de faux signaux Ichimoku.

**Site à consulter :** forexfactory.com (calendrier économique)

**Analyse du graphique (Piège #5) :**
- **Zone rose (rectangle pointillé)** : sortie d'une news importante → le prix fait un spike violent vers le haut puis crash vers le bas
- **"Faux signal acheteur lors de la sortie de la news"** : la bougie massive vers le haut casse tout (Tenkan, Kijun, nuage) mais c'est un mouvement de news, pas un vrai signal technique
- **"Signal d'achat valide"** (vert) : APRÈS que la volatilité de la news se calme, un vrai signal technique se forme → entrée valide
- Le bot devrait idéalement vérifier le calendrier économique ou au minimum ne pas trader X minutes avant/après les news high-impact

**Pour le bot MQL5 :**
```
Option 1 : Filtrer manuellement via forexfactory.com (mode semi-auto)
Option 2 : Intégrer un calendrier économique via API
Option 3 : Détecter les spikes de volatilité anormaux et ignorer les signaux pendant ces périodes
```

---

## 🛡️ STOP LOSS (SL)

### Règle principale
**TOUJOURS placer le SL sur un niveau technique** pour laisser suffisamment d'espace à la position pour respirer.

| Configuration | Description | Résultat |
|--------------|-------------|---------|
| ❌ **Piège 1 : SL trop serré** | SL entre l'ouverture et le niveau technique | Se fait stopper par le bruit du marché, réduit la probabilité de gagner |
| ❌ **Piège 2 : SL trop large** | SL bien en dessous du niveau technique | Augmente inutilement le risque en cas de retournement |
| ✅ **Bonne configuration** | SL placé SUR le niveau technique | Laisse le trade respirer, protège si le niveau casse |

### Placement du SL en dynamique HAUSSIÈRE (BUY)
- Le SL se place juste **en dessous de la bougie qui a cassé la Kijun** (le Low de cette bougie)
- Ça évite la **chasse aux stops** tout en maximisant les profits
- Le niveau technique ici = le **Low de la bougie de breakout**

```
// BUY : SL = Low de la bougie de cassure - buffer
SL_buy = Low[bougie_cassure] - buffer_pips
```

### Placement du SL en dynamique BAISSIÈRE (SELL)
- Le SL se place juste **au-dessus de la bougie qui a cassé la Kijun** (le High de cette bougie)
- Même logique inversée

```
// SELL : SL = High de la bougie de cassure + buffer
SL_sell = High[bougie_cassure] + buffer_pips
```

### ⚠️ Attention au piège de la Kijun
- Le prix peut venir **tourner autour de la Kijun** après la cassure
- Si le SL est placé **trop proche de la Kijun** (ex: juste en dessous de la Kijun au lieu du Low de la bougie), le prix peut le faire sauter avant de repartir dans le bon sens
- **Mauvais placement** = SL à la Kijun → le prix oscille autour et te sort
- **Bon placement** = SL au Low/High de la bougie de cassure → plus loin, laisse respirer

### Pour le bot MQL5
```
// Identifier la bougie de cassure (la bougie qui a clôturé au-delà de la Kijun)
int breakout_bar = 1;  // la bougie précédente qui vient de clôturer

// BUY
double SL = iLow(Symbol(), PERIOD_CURRENT, breakout_bar) - buffer * Point();

// SELL
double SL = iHigh(Symbol(), PERIOD_CURRENT, breakout_bar) + buffer * Point();

// buffer = quelques pips pour éviter la chasse aux stops
// Suggestion : buffer = spread * 2 ou un nombre fixe selon le TF
```

### Résumé SL + TP + Trailing

| Phase | BUY | SELL |
|-------|-----|------|
| **SL initial** | Low de la bougie de cassure Kijun | High de la bougie de cassure Kijun |
| **TP initial** | R1 (PP journalier) | S1 (PP journalier) |
| **Quand R1/S1 atteint** | Remonter SL proche de R1, TP → R2 | Descendre SL proche de S1, TP → S2 |
| **Sortie alternative** | Cassure inverse Kijun | Cassure inverse Kijun |

### Breakeven — Réduire les pertes

**Astuce 1 — Breakeven :** Dès que le prix est suffisamment en profit, remonter le SL au **niveau du prix d'ouverture** → élimine la perte potentielle. Le trade devient "gratuit".

**Astuce 2 — Trailing progressif :** Continuer à monter le SL progressivement pour sécuriser les gains au fur et à mesure que le prix avance.

**⚠️ Toujours laisser suffisamment d'espace** pour que la position puisse respirer (sur un niveau technique) et éviter de toucher le SL trop tôt.

**Quand activer le Breakeven ?**
Dès que le prix a gagné **au moins la distance entre l'ouverture et le SL initial** (ratio 1:1).

Exemple : si l'ouverture est à 100 et le SL à 95 (= 5 pips de risque), on active le breakeven quand le prix atteint 105 (= 5 pips de gain = même distance que le risque).

**Pour le bot MQL5 :**
```
// Calcul de la distance de risque initiale
risk_distance = abs(entry_price - initial_SL)

// BUY : activer breakeven quand le prix monte de risk_distance
SI Bid >= entry_price + risk_distance :
   modifier SL → entry_price + spread  // breakeven (+ spread pour couvrir les frais)

// SELL : activer breakeven quand le prix descend de risk_distance
SI Ask <= entry_price - risk_distance :
   modifier SL → entry_price - spread  // breakeven

// Ensuite : trailing progressif sur niveaux PP (R1 → R2 pour BUY, S1 → S2 pour SELL)
```

### Séquence complète du SL dans un trade

```
1. ENTRÉE
   SL = Low/High bougie de cassure (+ buffer)
   TP = R1/S1

2. PRIX ATTEINT 1:1 (distance = risque initial)
   → SL → Breakeven (prix d'ouverture)

3. PRIX ATTEINT R1/S1
   → SL → proche de R1/S1 (lock profit)
   → TP → R2/S2

4. PRIX ATTEINT R2/S2 OU cassure inverse Kijun
   → CLOSE position
```

---

## 💰 MONEY MANAGEMENT

### Les 3 règles de base
1. **Se fixer des règles précises** pour gérer le risque du capital
2. **Calculer le risque AVANT d'ouvrir** une position et éviter l'effet de levier excessif
3. **TOUJOURS utiliser un Stop Loss**

### Paramètres de trading rentable (vs "trading casino")

| Paramètre | ❌ Trading Casino | ✅ Trading Rentable | Paramètre bot |
|-----------|-----------------|-------------------|---------------|
| **Objectif de gains / session** | > 5% (trop gourmand) | 0.5%, 1% ou 2% par session | `target_pct = 1.0` |
| **Perte max / session** | Pas de limite (5-10%) | Max 2-3% du capital | `max_loss_pct = 2.0` |
| **Trades perdants consécutifs** | Pas de limite | 3 à 8 selon la confiance | `max_consecutive_losses = 5` |
| **Risque par trade** | > 5% sans protection | 0.125% à 2% du capital | `risk_per_trade_pct = 1.0` |
| **Risk:Reward** | < 0.5:1 ou > 3:1 | Entre 1:1 et 3:1 | `min_rr = 1.0` |
| **Règles de session** | Impulsif, pas d'analyse | Strictes, apprendre des erreurs | Logging + review |

### Compound : réinvestir 100% des gains
- Réinvestir **100% des gains** → intérêts composés
- 1% par mois, capital 1000$ → après 12 mois = 1126.83$ (+12.68%)
- 1% par jour, capital 1000$ → après 1 an = 12032$ (+1103%)
- Le bot calcule la taille de position basée sur le **capital actuel** (pas le capital initial)

### Pour le bot MQL5
```
// Paramètres Money Management (inputs configurables)
input double risk_per_trade_pct = 1.0;     // % du capital risqué par trade
input double max_loss_session_pct = 2.0;   // % max de perte par session
input double target_session_pct = 1.0;     // % objectif par session
input int max_consecutive_losses = 5;      // trades perdants consécutifs avant pause
input double min_risk_reward = 1.0;        // ratio R:R minimum

// Calcul taille de position
double capital = AccountBalance();
double risk_amount = capital * risk_per_trade_pct / 100.0;
double sl_distance = abs(entry_price - stop_loss);
double lot_size = risk_amount / (sl_distance / Point() * tick_value);

// Vérification R:R avant d'entrer
double tp_distance = abs(take_profit - entry_price);
double rr_ratio = tp_distance / sl_distance;
SI rr_ratio < min_risk_reward → NE PAS ENTRER (R:R insuffisant)

// Contrôles de session
SI perte_session >= max_loss_session_pct → ARRÊTER pour la session
SI gains_session >= target_session_pct → ARRÊTER (optionnel, objectif atteint)
SI trades_perdants_consecutifs >= max_consecutive_losses → PAUSE
```

---

## 🚪 TECHNIQUES DE SORTIE DE POSITION

### Technique #1 : L'objectif de gains (Take Profit fixe)

**Comment :** Clôturer la position selon un objectif de gains précis via un Take Profit.

Le TP est défini soit en **+X pips**, soit via un **ratio Risk:Reward** (ex: 1:2, 1:3) basé sur le Money Management.

**Graphique :** Signal d'achat en bas → Ouverture (ligne bleue pointillée) → le prix monte → Take Profit (ligne cyan pointillée en haut) = objectif de gains atteint → position clôturée automatiquement.

| | |
|---|---|
| **Avantage** | Sécurise les gains dès que l'objectif du Money Management est atteint |
| **Inconvénient** | Risque de clôturer trop tôt si la tendance continue → on n'optimise pas les gains |

**Pour le bot :** Facile à coder → TP fixe en pips ou ratio R:R par rapport au SL.

---

### Technique #2 : Cassure inverse de la Kijun

**Comment :** Attendre une **cassure inverse de la Kijun** pour sortir. Le prix recasse la Kijun dans le sens opposé au trade.

**Option alternative :** On peut aussi sortir sur cassure de la **Tenkan**, mais attention c'est parfois juste une **simple correction** (pas un vrai retournement).

**Graphique :** Signal d'achat en bas → Ouverture → le prix monte longtemps → Signal de sortie (label rouge) = le prix casse la Kijun vers le bas → on sort.

| | |
|---|---|
| **Avantage** | On ride la tendance **jusqu'au bout** → maximise les gains |
| **Inconvénient** | On peut perdre des gains si le marché corrige après avoir touché un niveau technique |

**Pour le bot :** Surveiller en continu si le prix recasse la Kijun dans le sens inverse.
```
// Sortie BUY : prix casse Kijun vers le bas
SI Close[0] < Kijun[0] → FERMER position BUY

// Sortie SELL : prix casse Kijun vers le haut  
SI Close[0] > Kijun[0] → FERMER position SELL

// Option Tenkan (plus agressive, plus de faux signaux de sortie)
SI Close[0] < Tenkan[0] → FERMER BUY (mais risque de sortir sur une correction)
```

---

### Quelle technique choisir ?

| Critère | Technique #1 (TP fixe) | Technique #2 (Cassure Kijun) |
|---------|----------------------|--------------------------|
| Style | Conservateur | Agressif / trend-follower |
| Gains max | Limités au TP | Illimités (ride la tendance) |
| Risque | Peu de drawback après TP | Peut rendre des gains sur correction |
| Bot | Simple à coder | Nécessite surveillance continue |
| Recommandé pour | Marchés choppy, petits TF | Tendances fortes, grands TF |

---

### Technique #3 : Sortie selon les Points Pivots

#### C'est quoi les Points Pivots ?
Les PP mettent en avant automatiquement des **niveaux psychologiquement importants**. L'indicateur repose sur **5 niveaux** : un Pivot central, deux Supports (S1, S2) et deux Résistances (R1, R2). Ils sont calculés à partir des données de la période précédente et créent des **paliers de validation** connus à l'avance.

#### Comment les utiliser ?
- **Au-dessus du Point Pivot** → tendance haussière → **Achat** à privilégier
- **En-dessous du Point Pivot** → tendance baissière → **Vente** à privilégier
- Les niveaux S1/S2/R1/R2 servent de **cibles de Take Profit** et de zones de rebond

#### Points Pivots selon les Timeframes
| Timeframe de trading | Points Pivots à utiliser |
|---------------------|------------------------|
| **M1** | PP H1 + PP Journaliers |
| **M5, M15, H1** | PP Journaliers uniquement |

#### Comparaison : Sans vs Avec Points Pivots
- **Sans PP :** Signal → prix descend → pas d'objectif clair → sortie vague sur alerte ou cassure Kijun
- **Avec PP :** Signal → prix descend → "Objectif S1 atteint" → sortie propre avec profit défini à l'avance

#### Attention au rebond sur les Points Pivots
- Le prix peut **rebondir** sur un niveau PP au lieu de le casser
- Exemple du cours : prix atteint S1 mais ne le casse pas → **rebond immédiat**
- La vente n'était pas valide car la **pente de la Tenkan était haussière** (confirme la règle pente Tenkan)
- **Leçon :** Quand le prix approche un niveau PP, laisser le temps au prix de se stabiliser

#### Points Pivots comme CONFIRMATION d'entrée (⚠️ PAS un filtre!)
- Les PP peuvent donner un **boost de confiance** qu'un trade est bon
- Exemple : prix descend → rebondit sur un Pivot → recasse la Kijun à la hausse → bon indice que ça repart
- Rebond PP **+ cassure Kijun** = double confirmation → plus de confiance
- **⚠️ IMPORTANT : C'est un BONUS, PAS une condition d'entrée. Ne PAS bloquer un trade valide Ichimoku juste parce que le PP ne confirme pas. Le but c'est pas d'avoir 1 trade par mois!**

#### Trailing Stop Loss sur Points Pivots (à confirmer)
- Possibilité de remonter le SL progressivement en suivant les niveaux PP
- Ex BUY : SL sous S1 → prix passe R1 → remonter SL au PP → prix passe R2 → SL à R1
- **Status : à confirmer** si c'est la méthode recommandée

#### Pour le bot MQL5
```
// Calcul des Points Pivots Journaliers
PP = (High_prev + Low_prev + Close_prev) / 3
R1 = (2 * PP) - Low_prev
R2 = PP + (High_prev - Low_prev)
S1 = (2 * PP) - High_prev
S2 = PP - (High_prev - Low_prev)

// TP sur niveaux PP
SELL → TP = S1 (conservateur) ou S2 (agressif)
BUY  → TP = R1 (conservateur) ou R2 (agressif)

// PP comme CONFIRMATION (log seulement, NE BLOQUE PAS le trade)
SI prix > PP ET signal BUY → log "PP confirme" (bonus confiance)
SI prix < PP ET signal SELL → log "PP confirme" (bonus confiance)
SI prix < PP ET signal BUY → log "PP ne confirme pas" (trade quand même si Ichimoku valide!)

// Confluence rebond PP (info seulement)
SI prix rebondit sur S1/S2 ET cassure Kijun haussière → log "double confirmation BUY"
SI prix proche S1/S2 ET signal SELL → log "attention rebond possible"
```

---

### Quelle technique choisir ? (FINAL)

| Critère | T1 (TP fixe) | T2 (Cassure Kijun) | T3 (Points Pivots) |
|---------|-------------|-------------------|-------------------|
| Style | Conservateur | Trend-follower | Structuré |
| Cible | Pips ou R:R fixe | Ride la tendance | S1/S2/R1/R2 |
| Avantage | Simple, sécurise | Maximise gains | Niveaux connus, psychologiques |
| Inconvénient | Coupe trop tôt | Peut rendre gains | Rebond possible |
| Bot | Très simple | Surveillance continue | Calcul PP + logique TP |

**Stratégie hybride recommandée pour le bot :**
- TP sur le prochain niveau PP (S1/R1) comme objectif principal
- Trailing SL sur la Kijun pour protéger les gains
- Si le prix casse le niveau PP → nouveau TP au prochain niveau (S2/R2)

---

## 📊 EXEMPLES CONCRETS DU COURS

### Exemple #1 — Trade de l'espace ✅ GAGNANT
- ✅ Cassure franche de la Kijun (Tenkan cassée **en même temps**)
- ✅ Les deux droites (Tenkan + Kijun) sont en tendance haussière
- ✅ Prix au-dessus du nuage Kumo
- ✅ Sortie sur cassure inverse de la Kijun

**Analyse Chikou :** Le Chikou (bleu) est libre au-dessus des bougies historiques → espace dégagé → ✅

**Nuance clé :** La Tenkan était AU-DESSUS de la Kijun (normalement on veut en dessous pour un BUY), MAIS les deux ont été cassées **en même temps** par le prix → signal valide quand même. C'est une **exception** à la règle Tenkan/Kijun.

---

### Exemple #2 — Trade de l'espace ✅ GAGNANT
- ✅ Ouverture sur cassure de la Kijun (Tenkan cassée **en même temps**)

**Même pattern que Exemple #1 :** cassure simultanée des deux lignes = bon signal même si la Tenkan n'est pas dans la position "idéale".

---

### Exemple #3 — Trade gagnant ✅ GAGNANT
- ✅ Ouverture sur cassure de la Kijun (Tenkan **déjà cassée un peu avant**)
- ✅ Prix au-dessus du nuage Kumo
- ✅ Sortie sur cassure inverse de la Kijun

**Différence vs Ex 1-2 :** Ici la Tenkan a été cassée **un peu avant** la Kijun (pas en même temps). C'est le scénario classique : Tenkan cassée d'abord → puis Kijun cassée → entrée.

---

### Exemple #4 — Trade à éviter ❌ PERDANT
- ❌ Entrée sur cassure de Kijun, SAUF que la cassure est **très faible et pas nette**
- ❌ La **pente de la Tenkan est baissière** (pour un achat !)

**Pourquoi c'est un mauvais trade :**
1. La cassure de la Kijun n'est pas "franche" → le prix passe à peine au-dessus, pas de momentum
2. La Tenkan pointe vers le BAS alors qu'on essaie d'acheter → contradiction avec la tendance court terme
3. Résultat : le prix retombe et le trade est perdant

**Nouvelle règle extraite :** La **pente/direction de la Tenkan** doit être dans le sens du trade. Pour un BUY, la Tenkan doit pointer vers le haut. Pour un SELL, la Tenkan doit pointer vers le bas.

**Pour le bot :** Vérifier que `Tenkan[0] > Tenkan[1]` pour un BUY (pente haussière) et `Tenkan[0] < Tenkan[1]` pour un SELL (pente baissière).

---

### Exemple #5 — Trade à éviter ❌ PERDANT
- ❌ Achat à éviter car le prix a cassé la Kijun et la Tenkan **DANS LE NUAGE**
- ❌ La direction n'était pas claire, réduisant les chances de réussir
- ❌ Sortie sur cassure inverse de la Kijun

**Pourquoi c'est un mauvais trade :**
1. La cassure s'est faite **à l'intérieur du nuage Kumo** → condition #4 violée
2. Quand le prix est dans le nuage = indécision totale → pas de direction claire
3. Même si le trade a monté un peu, il retombe → le nuage agissait comme résistance

**Confirmation :** C'est exactement le **Piège #2** (ne pas ouvrir dans le nuage) illustré avec un vrai trade.

---

### Exemple #6 — Trade à éviter ❌ PERDANT
- ❌ Cassure **non franche** de la Kijun → c'est plus un **rebond** qu'une cassure
- ❌ Sortie sur cassure inverse
- ❌ 2ème signal "prudent" confirmé seulement par la 2ème bougie (trop incertain)

**Pourquoi c'est un mauvais trade :**
1. Le prix touche la Kijun mais ne la traverse pas clairement → c'est un **rebond**, pas une cassure
2. Le premier "signal à éviter" (orange) : le prix oscille autour de la Kijun sans cassure nette
3. Le "signal prudent" plus tard : même le 2ème essai nécessite une 2ème bougie pour confirmer → trop hésitant

**Pour le bot :** Définir une distance minimum entre le Close et la Kijun pour que la cassure soit "franche". Possiblement : `abs(Close - Kijun) > X pips` ou `> facteur * ATR`.

---

### 📐 Règles supplémentaires extraites des exemples

#### EXCEPTION Tenkan/Kijun : Cassure simultanée
- Si le prix casse la **Tenkan ET la Kijun en même temps** (même bougie), le signal est **valide** même si la Tenkan n'est pas dans la position "idéale" (ex: Tenkan au-dessus de la Kijun pour un BUY)
- C'est un signal de **fort momentum** → les deux lignes sont balayées d'un coup

#### NOUVELLE RÈGLE : Pente de la Tenkan
- **BUY :** La Tenkan doit pointer vers le **HAUT** (pente haussière)
- **SELL :** La Tenkan doit pointer vers le **BAS** (pente baissière)
- Si la pente de la Tenkan est contre le trade → signal faible → éviter
- Pour le bot : `Tenkan[0] > Tenkan[1]` (BUY) ou `Tenkan[0] < Tenkan[1]` (SELL)

#### Cassure "franche" vs "rebond"
- Une cassure franche = le corps de la bougie traverse clairement la Kijun avec du momentum
- Un rebond = le prix touche/effleure la Kijun mais ne la traverse pas nettement
- Pour le bot : ajouter un seuil minimum de distance `Close - Kijun` pour filtrer les faux breakouts

---

## 📊 EXEMPLES SORTIE POINTS PIVOTS

### Exemple PP #1 — BUY, sortie R2 conseillée ✅
- ✅ Ouverture : cassure Kijun + Tenkan, au-dessus du nuage
- ✅ Sortie possible à R1 (premier objectif) OU R2 (deuxième objectif) OU cassure inverse Kijun
- ✅ **Sortie R2 conseillée** car la pente de la Kijun ET de la Tenkan sont haussières → le momentum est fort → on hold pour R2

**Leçon :** Quand les pentes Kijun+Tenkan sont fortement dans le sens du trade → ne pas sortir à R1, viser R2.

---

### Exemple PP #2 — BUY, faux signal + sortie R2 ✅
- ❌ **Faux signal** d'abord : le prix n'a pas cassé la Tenkan → on n'entre pas (confirme Piège #1)
- ✅ Vrai signal ensuite : cassure Kijun + Tenkan à la sortie du nuage, au-dessus du PP Journalier
- ✅ **Sortie R1 déconseillée** car tous les indicateurs (pente Kijun + Tenkan) étaient haussiers → on tient pour R2
- ✅ Sortie finale sur cassure inverse Kijun ou R2

**Leçon :** Même logique que Ex #1 — indicateurs haussiers = pas de sortie prématurée à R1.

---

### Exemple PP #3 — BUY, sortie R1 conseillée ✅
- ✅ Ouverture : cassure Kijun + Tenkan, sortie du nuage
- ✅ Sortie sur R1 et/ou R2
- ✅ **Sortie R1 conseillée** car la bougie à R1 montre une **indécision** (doji, mèches, petit corps)

**Leçon :** Quand la bougie au niveau R1 montre de l'indécision → sortir à R1, ne pas risquer de tenir pour R2. Le marché hésite = prendre ses profits.

---

### Exemple PP #4 — SELL, sortie S1 ✅
- ✅ Ouverture : cassure Kijun, après cassure Tenkan
- ✅ Cassure sous le nuage Kumo ET sous le Point Pivot
- ✅ **Sortie sur objectif S1 atteint** → profit encaissé

**Leçon :** Exemple classique de SELL avec TP sur S1. Le PP agit comme confirmation (prix sous le PP = biais vendeur).

---

### Exemple PP #5 — SELL, stabilisation puis S1 ✅
- ✅ Ouverture : cassure Kijun + Tenkan, sortie du nuage
- ✅ Cassure franche, mais **stabilisation/consolidation** avant d'atteindre S1
- ✅ **Sortie sur S1 atteint** (après la stabilisation)

**Leçon :** Le prix ne va pas toujours en ligne droite vers le TP. Une phase de stabilisation après la cassure est normale → ne pas paniquer, laisser le trade travailler.

---

### 📐 Stratégie de sortie PP : Système de paliers (trailing par niveaux)

**Le principe :** On ne choisit PAS entre R1 et R2. On fait les DEUX en séquence.

**BUY :**
1. Entrée → TP initial = **R1**
2. Prix atteint R1 → **remonter le SL proche de R1** (lock les gains) + nouveau TP = **R2**
3. Si le prix continue vers R2 → profit max
4. Si le prix corrige après R1 → le SL proche de R1 se déclenche → on sort quand même en **profit**

**SELL :**
1. Entrée → TP initial = **S1**
2. Prix atteint S1 → **descendre le SL proche de S1** (lock les gains) + nouveau TP = **S2**
3. Si le prix continue vers S2 → profit max
4. Si le prix corrige après S1 → le SL proche de S1 se déclenche → on sort en **profit**

**Résultat :** On capture toujours au minimum le mouvement jusqu'à R1/S1, et si le momentum est là on ride jusqu'à R2/S2 sans risque.

**Quand sortir direct à R1/S1 sans viser R2/S2 :**
- Bougie d'**indécision** au niveau R1/S1 (doji, mèches longues)
- Les pentes Kijun/Tenkan s'aplatissent ou changent de direction
- Fin de session de trading (approche des heures mortes)

**Pour le bot MQL5 :**
```
// Phase 1 : TP initial sur premier niveau PP
BUY  → TP = R1, SL = selon stratégie SL
SELL → TP = S1, SL = selon stratégie SL

// Phase 2 : quand R1/S1 atteint
SI prix >= R1 (BUY) :
   SL = R1 - buffer_pips    // lock profit au niveau R1
   TP = R2                   // nouveau objectif
   
SI prix <= S1 (SELL) :
   SL = S1 + buffer_pips    // lock profit au niveau S1
   TP = S2                   // nouveau objectif

// Phase 3 : résultat
SI prix atteint R2/S2 → close en profit max
SI prix revient et touche SL (proche R1/S1) → close en profit sécurisé
```

---

## ⏱️ TIMEFRAMES & CONVERGENCE MULTI-UT

### Les 3 modes de trading (à configurer dans le bot)

| Mode | UT COURTE (Gestion position) | UT MOYENNE (Visibilité) | UT LONGUE (Tendance de fond) |
|------|------------------------------|------------------------|------------------------------|
| **Scalping Gourmand** | M1 | M5 | M15 |
| **Scalping** | M5 | M15 | H1 |
| **Intraday Rapide** | M15 | H1 | H4 |

- **UT COURTE** = le timeframe sur lequel on gère la position (entrée, sortie, SL)
- **UT MOYENNE** = visibilité du contexte immédiat
- **UT LONGUE** = tendance de fond (détermine le biais directionnel)

### Convergence de tendance sur 3 UT

Pour trouver des "trades de l'espace" (les meilleurs), il faut chercher la **convergence** de la tendance sur les 3 unités de temps.

#### Convergence BAISSIÈRE (exemple M15/H1/H4)

| UT-0 (M15) | UT+1 (H1) | UT+2 (H4) | Conclusion | Action bot |
|------------|-----------|-----------|------------|------------|
| BAISSIÈRE | NEUTRE | NEUTRE | 🟡 Faiblement marquée | Trade OK mais prudent |
| BAISSIÈRE | BAISSIÈRE | NEUTRE | 🟠 Moyennement marquée | Trade OK |
| BAISSIÈRE | NEUTRE | BAISSIÈRE | 🟠 Moyennement marquée | Trade OK |
| ⭐ BAISSIÈRE | BAISSIÈRE | BAISSIÈRE | 🟢 **Fortement marquée** | **MEILLEUR signal** |
| BAISSIÈRE | NEUTRE | HAUSSIÈRE | 🔴 **À ÉVITER** | Retournement potentiel |
| BAISSIÈRE | HAUSSIÈRE | HAUSSIÈRE | 🔴 **À ÉVITER** | Consolidation, tendance de fond haussière |

#### Convergence HAUSSIÈRE (exemple M15/H1/H4)

| UT-0 (M15) | UT+1 (H1) | UT+2 (H4) | Conclusion | Action bot |
|------------|-----------|-----------|------------|------------|
| HAUSSIÈRE | NEUTRE | NEUTRE | 🟡 Faiblement marquée | Trade OK mais prudent |
| HAUSSIÈRE | HAUSSIÈRE | NEUTRE | 🟠 Moyennement marquée | Trade OK |
| HAUSSIÈRE | NEUTRE | HAUSSIÈRE | 🟠 Moyennement marquée | Trade OK |
| ⭐ HAUSSIÈRE | HAUSSIÈRE | HAUSSIÈRE | 🟢 **Fortement marquée** | **MEILLEUR signal** |
| HAUSSIÈRE | NEUTRE | BAISSIÈRE | 🔴 **À ÉVITER** | Retournement potentiel |
| HAUSSIÈRE | BAISSIÈRE | BAISSIÈRE | 🔴 **À ÉVITER** | Consolidation, tendance de fond baissière |

### Règles de convergence pour le bot

- **🟢 3/3 UT alignées** → signal fort, position full size, viser R2/S2
- **🟠 2/3 UT alignées** → signal moyen, position normale, viser R1/S1
- **🟡 1/3 UT alignée** → signal faible, position réduite ou prudence accrue
- **🔴 UT-0 vs UT+2 en contradiction** → **FORTE PRUDENCE** → taille réduite, le cours dit "à éviter" mais c'est un avertissement, pas un blocage absolu

### Pour le bot MQL5
```
// Configuration des modes (paramètre input)
enum TradingMode { SCALPING_GOURMAND, SCALPING, INTRADAY_RAPIDE };

// Timeframes par mode
SI mode == SCALPING_GOURMAND → UT0=M1,  UT1=M5,  UT2=M15
SI mode == SCALPING          → UT0=M5,  UT1=M15, UT2=H1
SI mode == INTRADAY_RAPIDE   → UT0=M15, UT1=H1,  UT2=H4

// Déterminer la tendance sur chaque UT
// Tendance = position prix vs Kumo + direction Kijun + direction Tenkan
fonction getTrend(timeframe):
  kumo_top = max(SSA, SSB)
  kumo_bottom = min(SSA, SSB)
  SI prix > kumo_top → HAUSSIÈRE
  SI prix < kumo_bottom → BAISSIÈRE
  SI prix entre kumo_bottom et kumo_top → NEUTRE (dans le nuage)

// Convergence
trend_UT0 = getTrend(UT0)
trend_UT1 = getTrend(UT1)
trend_UT2 = getTrend(UT2)

// Scoring
SI trend_UT0 == trend_UT1 == trend_UT2 → convergence = FORTE (3/3)
SI trend_UT0 == trend_UT1 OU trend_UT0 == trend_UT2 → convergence = MOYENNE (2/3)
SI trend_UT0 seul → convergence = FAIBLE (1/3)

// Contradiction UT0 vs UT2
SI trend_UT0 == BAISSIÈRE ET trend_UT2 == HAUSSIÈRE → ⚠️ FORTE PRUDENCE (taille réduite)
SI trend_UT0 == HAUSSIÈRE ET trend_UT2 == BAISSIÈRE → ⚠️ FORTE PRUDENCE (taille réduite)
// Note : le cours dit "situation à éviter" mais c'est un ATTENTION, pas un blocage absolu
```

### 🔄 Workflow complet du bot (exemple DAX Intraday Rapide)

Le cours montre un walkthrough étape par étape. **Le bot doit reproduire cette séquence.**

#### Étape 1 : Analyser l'UT LONGUE (H4) — Tendance de fond
- Déterminer la tendance : prix vs Kumo, position Kijun/Tenkan, Chikou
- Identifier les niveaux PP sur cette UT (R1-R4, S1-S4, PP)
- Ex DAX : tendance baissière confirmée par Chikou + droite de tendance + prix sous nuage/Kijun/Tenkan

#### Étape 2 : Analyser l'UT MOYENNE (H1) — Visibilité + objectifs
- Confirmer la tendance (doit être alignée ou neutre, PAS en contradiction avec UT longue)
- Identifier les niveaux PP **journaliers** et leur position vs le prix
- Ex DAX : tendance baissière confirmée, prix sous le PP journalier → objectif = S1. S1 atteint "avec une précision chirurgicale"

#### Étape 3 : Analyser l'UT COURTE (M15) — Signaux d'entrée
- Appliquer les 11 conditions Ichimoku d'entrée
- Les signaux sur UT courte dans le sens de la convergence donnent de **très bons résultats**
- Ex DAX : 4 signaux détectés, **3 gagnants à 100%, 1 perdant de quelques points**
- Le prix vient **tester la SSB** (bord du nuage) comme résistance avant de repartir

**Nouveau pattern : test de la SSB**
- Dans une tendance baissière, le prix remonte tester la SSB du nuage puis repart à la baisse
- La SSB agit comme résistance dynamique
- Pour le bot : un signal de vente près de la SSB est renforcé

#### Étape 4 : Sortie de position
- Signal 1 → sortie sur **S2 ou S3** (PP levels) OU **cassure inverse Kijun**
- Signal 2 → sortie sur **cassure inverse Kijun**
- Chaque signal a **plusieurs options de sortie** (paliers PP + Kijun)

#### Étape 5 : Fin de phase + retournement potentiel
- Signal 3 arrive **en fin de tendance** → objectif S1 atteint mais le prix consolide
- "Après 3 signaux, fin de phase baissière potentielle"
- Sur H4 : le prix **rebondit sur un support** et **repart dans le nuage Kumo**
- Le retournement est à **confirmer à la sortie du nuage**

**Règle fin de phase :** Quand le prix re-entre dans le Kumo sur l'UT longue → ⚠️ **ATTENTION**, réduire la taille ou être plus sélectif, mais **PAS un blocage total**. Des trades peuvent encore fonctionner.

#### Faux signal identifié
- Un signal de vente a été **invalidé** car le prix a cassé en sens inverse **la bougie suivante**
- Pour le bot : possibilité d'ajouter une confirmation sur la bougie suivante (mais attention, ça retarde l'entrée)

### Architecture bot multi-UT

**Le bot DOIT analyser chaque UT séparément :**
```
// Boucle principale
1. Charger données H4 → getTrend(H4) + calculer PP(H4)
2. Charger données H1 → getTrend(H1) + calculer PP_journalier
3. Vérifier convergence (pas de contradiction UT0 vs UT2)
4. SI convergence OK :
   a. Charger données M15 → chercher signal Ichimoku (11 conditions)
   b. SI signal trouvé :
      - TP = prochain niveau PP identifié sur H1 (S1/R1)
      - SL = selon stratégie SL
      - Entrer le trade
5. Monitorer H4 en continu :
   SI prix re-entre dans le Kumo H4 → ⚠️ WARNING (réduire taille, pas bloquer)
   SI prix sort du Kumo H4 dans nouveau sens → nouveau biais
```

### 🔄 Workflow Scalping Gourmand (exemple DAX M1/M5/M15)

**Différence clé vs Intraday:** En Scalping Gourmand, le M1 utilise les PP **H1 ET Journaliers** (deux couches de PP).

#### Étape 1 : UT LONGUE (M15) — Tendance de fond
- Tendance baissière : prix sous nuage, sous Kijun et Tenkan
- Prix sous le PP Journalier → objectif = **S1 journalier**
- Les niveaux PP sont identifiés : R1, PP, S1

#### Étape 2 : UT MOYENNE (M5) — Visibilité
- Tendance toujours baissière, confirmée par droite de tendance
- Prix sous nuage, sous Kijun et Tenkan (même constat)
- Prix toujours sous PP journalier → objectif S1 toujours valide

#### Étape 3 : UT COURTE (M1) — Signaux d'entrée
- 3 signaux de vente détectés → **3 signaux 100% gagnants**
- Les PP H1 sont visibles en plus des PP journaliers (double couche)
- **Pull back** après rebond sur S3 H1 qui confirme la tendance baissière
- Le rebond sur un niveau PP H1 + continuation = confirmation de la force baissière

**Différence PP M1 vs M5/M15 :**
```
M1  → PP H1 (niveaux intraday fins) + PP Journaliers (niveaux macro)
M5  → PP Journaliers uniquement
M15 → PP Journaliers uniquement
H1  → PP Journaliers uniquement
```

#### Ensuite : Retournement sur la tendance de fond (M15)
- Le prix n'arrive PAS à casser le **S2 journalier** malgré plusieurs tentatives
- Le prix **rebondit franchement** sur S2 → repart à la hausse
- Il casse tous les niveaux Ichimoku vers le haut en direction du **R2 journalier**
- Sur M15 un nouveau **signal d'achat** apparaît → retournement confirmé

**Leçon retournement :** Quand le prix échoue à casser un niveau PP majeur (S2/R2 journalier) après plusieurs tentatives → forte probabilité de retournement. Le bot devrait détecter les tentatives ratées sur les niveaux PP.

```
// Détection retournement PP (optionnel, bonus)
SI prix touche S2_journalier >= 2 fois SANS clôturer en dessous
   ET signal Ichimoku BUY apparaît
→ log "Retournement probable sur S2, BUY renforcé"
```

---

## 📋 CHECKLIST FINALE MISE À JOUR (MQL5)

### SELL Signal Checklist
```
[ ] Horaire dans une fenêtre de trading autorisée
[ ] Pas de news high-impact en cours
[ ] Convergence multi-UT : UT0 baissière (⚠️ si contradiction UT0 vs UT2 = prudence, pas blocage)
[ ] Tendance baissière (Kumo + Kijun + Tenkan orientés baissier)
[ ] Nuage Kumo baissier (SSA < SSB)
[ ] Pente Tenkan baissière (Tenkan[0] < Tenkan[1])
[ ] Prix a DÉJÀ cassé la Tenkan vers le bas (OU cassure simultanée TK+KJ)
[ ] Tenkan pas trop éloignée du prix (pas d'effet élastique)
[ ] Tenkan AU-DESSUS de la Kijun (OU cassure simultanée = exception)
[ ] Prix casse Kijun à la baisse EN CLÔTURE (cassure FRANCHE, pas un rebond)
[ ] Clôture de la bougie SOUS le nuage (PAS dans le Kumo)
[ ] Chikou Span libre en dessous (pas d'obstacle)
→ Si tout ✅ = SELL (taille position selon convergence : 3/3=full, 2/3=normal, 1/3=réduit)
```

### BUY Signal Checklist
```
[ ] Horaire dans une fenêtre de trading autorisée
[ ] Pas de news high-impact en cours
[ ] Convergence multi-UT : UT0 haussière (⚠️ si contradiction UT0 vs UT2 = prudence, pas blocage)
[ ] Tendance haussière (Kumo + Kijun + Tenkan orientés haussier)
[ ] Nuage Kumo haussier (SSA > SSB)
[ ] Pente Tenkan haussière (Tenkan[0] > Tenkan[1])
[ ] Prix a DÉJÀ cassé la Tenkan vers le haut (OU cassure simultanée TK+KJ)
[ ] Tenkan pas trop éloignée du prix (pas d'effet élastique)
[ ] Tenkan EN DESSOUS de la Kijun (OU cassure simultanée = exception)
[ ] Prix casse Kijun à la hausse EN CLÔTURE (cassure FRANCHE, pas un rebond)
[ ] Clôture de la bougie AU-DESSUS du nuage (PAS dans le Kumo)
[ ] Chikou Span libre au-dessus (pas d'obstacle)
→ Si tout ✅ = BUY (taille position selon convergence : 3/3=full, 2/3=normal, 1/3=réduit)
```

---

## 📋 PLAN DE TRADING OFFICIEL (résumé du cours)

### À faire AVANT chaque session
1. Choisir les actifs sur lesquels on est spécialisé
2. Déterminer la tendance de fond
3. Repérer les supports et résistances (Points Pivots)
4. Vérifier la présence de nouvelles économiques ou non
5. Décider si c'est le bon moment de trader l'actif choisi

### Configuration du plan
| Paramètre | Valeur |
|-----------|--------|
| Stratégie | DayTrader PRO |
| Indicateurs | Ichimoku + Points Pivots |
| UT principale | M15 |
| Séances | 3h (de 9h à 12h France = 3h à 6h Québec) |
| Sens | Toujours en tendance |
| Stop Loss | Oui à chaque position |

### 🟢 4 RÈGLES D'OUVERTURE — ACHAT

| Niveau | Règle | Description |
|--------|-------|-------------|
| **Règle 1** (débutant) | Cassure Kijun + Tenkan | Cassure de la Kijun au-dessus de la Tenkan et au-dessus du nuage Kumo |
| **Règle 2** (avancé) | Entrée sur mèche | Entrée sur mèche lors de la cassure de la Kijun au-dessus du nuage Kumo |
| **Règle 3** (avancé) | Rebond Kijun (continuation) | Rebond du prix sur la Kijun dans une tendance haussière marquée |
| **Règle 4** (avancé) | Cassure Tenkan + nuage | Cassure de la Tenkan au-dessus du nuage Kumo (**attention après 3 cassures**) |

### 🔴 4 RÈGLES D'OUVERTURE — VENTE

| Niveau | Règle | Description |
|--------|-------|-------------|
| **Règle 1** (débutant) | Cassure Kijun + Tenkan | Cassure de la Kijun en-dessous de la Tenkan et en-dessous du nuage Kumo |
| **Règle 2** (avancé) | Entrée sur mèche | Entrée sur mèche lors de la cassure de la Kijun en-dessous du nuage Kumo |
| **Règle 3** (avancé) | Rebond Kijun (continuation) | Rebond du prix sur la Kijun dans une tendance baissière marquée |
| **Règle 4** (avancé) | Cassure Tenkan + nuage | Cassure de la Tenkan en-dessous du nuage Kumo (**attention après 3 cassures**) |

### Règles de clôture
- **Type d'objectif :** 3 options — selon le Risk:Reward, selon un niveau technique (PP), ou selon un signal inverse de la stratégie
- **Fermeture en perte :** Quand le prix touche le Stop Loss

### Mapping des règles dans notre documentation

| Règle du plan | Ce qu'on a documenté |
|--------------|---------------------|
| Règle 1 | ✅ Nos 11 conditions d'entrée (cassure Kijun + Tenkan + nuage) |
| Règle 2 | ⚠️ **NOUVEAU** : entrée sur la mèche de la bougie de cassure (pas la clôture) — plus agressif |
| Règle 3 | ✅ Situation 2 : continuation de tendance (rebond Kijun) |
| Règle 4 | ⚠️ **NOUVEAU** : cassure de la Tenkan seule près du nuage — attention après 3 cassures |

### Pour le bot MQL5
```
// Règle 1 : Cassure Kijun = LA BASE (toujours active, non désactivable)
// C'est notre checklist de 11 conditions

// Règles avancées (toggles on/off)
input bool rule2_wick_entry     = false;  // Signal avancé 1 : Entrée sur mèche
input bool rule3_kijun_bounce   = false;  // Signal avancé 2 : Rebond Kijun
input bool rule4_tenkan_break   = false;  // Signal avancé 3 : Cassure Tenkan
```

---

## 🎯 SIGNAUX D'ENTRÉE AVANCÉS (détails)

### Signal avancé 1 : Mèche lors de la cassure de la Kijun
**Toggle : `rule2_wick_entry = false`**

**Principe :** Entrer sur la **mèche** de la bougie qui casse la Kijun (sans attendre la clôture). Plus agressif, entre plus tôt.

**Conditions :**
- Chikou doit être **libre**
- La tendance doit être **claire**
- La **mèche doit être 2 à 3 fois plus grande** que le corps de la bougie (ratio mèche/corps ≥ 2)

**Graphique :** Signal de vente → la mèche de la bougie casse la Kijun → entrée immédiate → sortie sur cassure inverse. Un faux signal de vente est aussi montré plus tard.

```
// Détection : ratio mèche vs corps
body = abs(Close - Open)
SI direction == SELL :
   wick = Open - Low  // mèche basse
SI direction == BUY :
   wick = High - Open  // mèche haute

SI wick >= body * 2 ET wick traverse Kijun → signal mèche valide
```

---

### Signal avancé 2 : Rebond Kijun dans une tendance
**Toggle : `rule3_kijun_bounce = false`**

**Principe :** Entrer sur un **rebond** du prix sur la Kijun pendant une tendance établie (= continuation).

**Conditions :**
- Chikou doit être **libre**
- La tendance doit être **claire**
- La **correction** du prix jusqu'à la Kijun **ne doit PAS remettre en question la tendance** de l'actif (juste un pullback, pas un retournement)

**Graphique :** Tendance haussière → le prix corrige et touche/approche la Kijun → rebond → entrée BUY → sortie sur cassure inverse Kijun.

```
// Détection : prix touche la Kijun en tendance
SI tendance == HAUSSIÈRE ET Low[1] <= Kijun[1] ET Close[1] > Kijun[1]
   → rebond détecté, signal BUY

SI tendance == BAISSIÈRE ET High[1] >= Kijun[1] ET Close[1] < Kijun[1]
   → rebond détecté, signal SELL
```

---

### Signal avancé 3 : Cassure de la Tenkan
**Toggle : `rule4_tenkan_break = false`**

**Principe :** Entrer sur une cassure de la **Tenkan seule** (pas la Kijun), en dehors du nuage Kumo.

**Conditions :**
- Chikou doit être **libre**
- La tendance doit être **claire**
- Ce signal est à prendre **en début de tendance** (Phase 1)
- **Plus risqué après 3 signaux Tenkan** → après 3 cassures Tenkan dans la même direction, laisser le cours respirer

**⚠️ Le piège : après 3 cassures Tenkan**
- Le graphique montre : Signal 1 ✅, Signal 2 ✅, Signal 3 ✅ (tous gagnants)
- Mais Signal 4 (après le 3ème) est **PERDANT**
- Après 3 signaux Tenkan, la tendance est souvent en fin de phase → prudence

```
// Compteur de cassures Tenkan par tendance
int tenkan_break_count = 0;

SI prix casse Tenkan ET rule4_tenkan_break == true :
   tenkan_break_count++
   
   SI tenkan_break_count <= 3 → signal OK
   SI tenkan_break_count > 3  → ⚠️ WARNING "3+ cassures Tenkan, risqué"
   
// Reset le compteur quand la tendance change
SI changement_tendance → tenkan_break_count = 0
```

---

### Optimisation : Entrée anticipée (10-15 secondes avant clôture)
**Toggle : `early_entry = false`**

**Principe :** Quand une grosse bougie est en train de casser la Kijun et qu'il est clair qu'elle va clôturer au-delà, entrer **10-15 secondes avant la clôture** de la bougie pour obtenir un meilleur prix d'entrée.

**Gain :** 2 à 5 pips/points supplémentaires par trade.

**Pourquoi :** En attendant la clôture complète, la bougie a déjà bougé et on entre au prix de clôture (moins favorable). En entrant 10-15s avant, on entre plus haut (SELL) ou plus bas (BUY).

```
input bool early_entry = false;  // OFF par défaut
input int early_entry_seconds = 12;  // secondes avant clôture

// Vérifier si on est proche de la clôture de la bougie
SI early_entry == true :
   seconds_to_close = period_seconds - (TimeCurrent() % period_seconds)
   
   SI seconds_to_close <= early_entry_seconds :
      // Vérifier si la bougie COURANTE est en train de casser la Kijun
      SI conditions_ichimoku_ok_sur_bougie_courante :
         → ENTRER maintenant (pas attendre la clôture)
```

---

### Optimisation : Sortie optimisée (éviter de sortir trop tôt)
**Toggle : `optimized_exit = false`**

**Principe :** Au lieu de sortir automatiquement au premier TP (PP journalier), le bot peut utiliser les **PP horaires** et la **cassure de Tenkan** comme points de sortie alternatifs pour capturer plus de mouvement. Laisser courir ses gains peut transformer un trade normal en **trade de l'espace**.

**Points de sortie possibles (du plus tôt au plus tard) :**
- Sortie sur PP journalier (S1/R1) → sortie classique
- Sortie sur PP H1 suivant → un peu plus loin, quelques points de plus
- Sortie sur cassure de Tenkan → le prix a fini son momentum
- Sortie sur prochain PP H1 encore → encore plus de profit

**Le graphique montre :** Chaque "Sortie optimisée" est un point où on AURAIT PU sortir avec un meilleur prix que la sortie classique. Ce sont des options, pas des obligations.

**Pour le bot :** Quand `optimized_exit = true`, au lieu de fermer au premier PP, le bot utilise la **cassure de Tenkan** comme critère de sortie principal, avec les niveaux PP H1 comme repères visuels dans le journal.

```
input bool optimized_exit = false;    // OFF par défaut

SI optimized_exit == false :
   // Sortie classique : fermer 100% au PP journalier (S1/R1)
   
SI optimized_exit == true :
   // Sortie sur cassure inverse de Tenkan OU prochain PP H1
   // Le SL trailing suit les niveaux PP H1 atteints
   // Objectif : laisser courir le trade le plus longtemps possible
```

---

### Optimisation : Technique "Sortie 2X" (sortie partielle)
**Toggle : `exit_2x = false`**

**Problème :** Quand on ferme 100% au premier TP et que la tendance continue → gros manque à gagner.

**Solution :** Fermer en **2 fois** :
- **Sortie 1** : fermer une partie au premier signal de sortie (PP level)
- **Sortie 2** : fermer le reste au deuxième signal (prochain PP ou cassure Tenkan)

**2 configurations :**

| Config | Sortie 1 | Sortie 2 |
|--------|---------|---------|
| **Conservative** | 70% | 30% restant |
| **Agressive** | 50% | 50% restant |

```
input bool exit_2x = false;           // OFF par défaut
input int exit_2x_first_pct = 70;     // % à fermer au premier signal de sortie

SI exit_2x == true :
   // Sortie 1 : fermer exit_2x_first_pct% au premier TP (PP)
   // SL du reste → remonté au niveau de Sortie 1
   // Sortie 2 : fermer le reste au prochain signal (PP suivant ou cassure Tenkan)
```

---

## 🔀 TRADING SANS CONVERGENCE (divergence des UT) — OPTION AVANCÉE

**Toggle dans le bot : `allow_divergence = false` par défaut**

### C'est quoi ?
Quand l'UT courte (M1) donne un signal dans un sens, mais les UT supérieures (M5, M15) sont dans l'autre sens. Exemple : signal d'achat sur M1, mais tendance baissière sur M5 et M15.

### 2 situations de divergence

**Situation 1 — Divergence par pullback :**
- L'UT longue (M15) fait un pullback vers un niveau technique (ex: R1 journalier)
- Au début de ce pullback, l'UT courte (M1) donne un signal de vente
- C'est un trade **court** qui profite du pullback temporaire → TP limité
- Le risque : le pullback peut se terminer vite et le prix repart dans la tendance de fond

**Situation 2 — Divergence par retournement :**
- L'UT longue (M15) touche un niveau technique majeur → changement de tendance après une phase de range
- L'UT courte (M1) donne un signal aligné avec le **nouveau** sens
- C'est un trade de retournement → potentiel plus grand mais incertain

### Règles en cas de divergence
- ✅ **Plus prudent** — le prix peut rapidement repartir dans l'autre direction
- ✅ **Bonne lecture des UT supérieures** — comprendre le prochain objectif majeur du marché
- ✅ **Objectif de gains limité** — le potentiel est réduit dans un pullback
- ✅ **Réduire le risque par trade** — certitude plus faible
- ✅ **SL obligatoire** — retournement peut arriver très vite

### Pour le bot MQL5
```
input bool allow_divergence = false;  // OFF par défaut

SI allow_divergence == true :
   // Accepter les signaux même si UT supérieures divergent
   // MAIS appliquer des ajustements :
   risk_multiplier = 0.5            // moitié du risque normal
   tp_target = R1/S1 seulement     // pas de R2/S2, objectif limité
   breakeven_asap = true            // breakeven encore plus tôt
```

---

## 📦 TRADING EN RANGE — OPTION AVANCÉE

**Toggle dans le bot : `allow_range_trading = false` par défaut**

### 3 règles pour trader en range

**Règle 1 — Identifier les niveaux techniques :**
- Résistance = borne haute du range
- Support = borne basse du range
- Le prix oscille entre les deux

**Règle 2 — Identifier la tendance de fond à long terme :**
- Le range va finir par casser → dans quel sens?
- Si tendance de fond haussière → privilégier les BUY dans le range (rebond sur support)
- Si tendance de fond baissière → privilégier les SELL dans le range (rebond sur résistance)

**Règle 3 — SL et TP en range :**
- BUY en range (fond haussier) : TP juste **sous** la résistance, SL juste **sous** le support
- SELL en range (fond baissier) : TP juste **au-dessus** du support, SL juste **au-dessus** de la résistance

### Types de range

| Type | Description | Action |
|------|-------------|--------|
| ❌ **Faible amplitude** | Prix rebondit trop vite entre les bornes, bougies serrées | **Dangereux** — pas de trade |
| ✅ **Forte amplitude** | Prix navigue tranquillement entre les bornes, espace pour respirer | **Acceptable** — peut trader |

### Adapter les paramètres en range

| Paramètre | Configuration range |
|-----------|-------------------|
| **Risque par trade** | **Très faible** — limiter l'exposition |
| **Pertes consécutives** | Max **3 trades perdants** puis arrêter |
| **Stop Loss** | Sur la borne opposée, placer rapidement et suivre de près |
| **Breakeven** | ❌ **NE PAS utiliser** — ça réduit les chances de succès car le prix oscille naturellement |

### Pour le bot MQL5
```
input bool allow_range_trading = false;  // OFF par défaut

SI allow_range_trading == true :
   // Détecter le range : prix dans le nuage Kumo + Kijun plate
   // Identifier support/résistance (PP ou swing high/low)
   
   risk_multiplier = 0.25              // risque très faible
   max_consecutive_losses_range = 3    // max 3 pertes puis stop
   use_breakeven = false               // PAS de breakeven en range
   
   // BUY : rebond sur support, TP = résistance - buffer
   // SELL : rebond sur résistance, TP = support + buffer
   // SL = au-delà de la borne opposée
```

---

## 📈 PHASES DE TENDANCE & ADAPTATION

### Tendance vs Range
| | Tendance (Haussière/Baissière) | Range |
|---|---|---|
| Probabilité de gains | **Élevée / importante** | Faible / limitée |
| Complexité | **Faible** | Élevée |
| Pilotage | **Plus agressif** | Prudent |

**Le bot trade en TENDANCE.** Le range est à éviter (Piège #2 — prix dans le nuage = range).

### Les 5 phases d'une tendance

| Phase | Nom | Style de trading | Description |
|-------|-----|-----------------|-------------|
| **Phase 0** | Range | ❌ Pas de trade | Prix oscille sans direction, dans le nuage |
| **Phase 1** | Début de tendance | 🟢 **Agressif** | Breakout, cassure du nuage, premiers signaux forts |
| **Phase 2** | Milieu de tendance | 🟢 **Calme** | Tendance établie, continuations, rebonds Kijun |
| **Phase 3** | Fin de tendance | 🟠 **Prudent** | Momentum faiblit, approche résistance, attention |
| **Phase 4** | Consolidation / nouvelle tendance | ⚠️ Attendre | Soit consolidation (range), soit retournement |

### Exemple sur graphique Ichimoku
- **Phase 0** : Prix empêtré dans le nuage, Kijun plate, Chikou coincé → range
- **Phase 1** : Prix casse le nuage, grosses bougies, Chikou se libère → premiers trades
- **Phase 2** : Prix au-dessus du nuage, Kijun/Tenkan montent, rebonds réguliers → continuation
- **Phase 3** : Prix s'aplatit, Kijun commence à se stabiliser, approche de résistance → prudence
- **Phase 4** : Prix re-entre dans le nuage ou retourne → arrêter cette direction

### Adapter les paramètres selon la phase

| Paramètre | Configuration |
|-----------|--------------|
| **Risque par trade** | Peut être **plus élevé** quand la tendance est marquée (Phase 1-2) |
| **Stop Loss** | Doit **toujours** être placé, autour de la Kijun (règle inchangée) |
| **Breakeven** | Utiliser **dès que possible** pour éliminer la perte latente |

### Pour le bot MQL5
```
// Le scoring convergence (3/3, 2/3, 1/3) sert déjà de proxy pour la phase
// 3/3 convergence = probablement Phase 1-2 (tendance forte) → risque normal/élevé
// 2/3 convergence = probablement Phase 2-3 → risque normal
// 1/3 convergence = probablement Phase 3-4 → risque réduit

// Ajustement risque selon convergence
SI convergence == FORTE (3/3) → risk_multiplier = 1.5  // plus agressif
SI convergence == MOYENNE (2/3) → risk_multiplier = 1.0  // normal
SI convergence == FAIBLE (1/3) → risk_multiplier = 0.5  // prudent

lot_size = base_lot_size * risk_multiplier;
```

---

## 📓 JOURNAL DE TRADING AUTOMATISÉ

Le bot doit journaliser chaque trade avec des screenshots automatiques du graphique.

### Screenshots automatiques
- **À l'entrée :** screenshot du graphique au moment de l'ouverture du trade
- **À la sortie :** screenshot quand le trade se ferme (TP, SL, breakeven, ou cassure Kijun)

### Structure de dossiers
```
/ToutieTrader_Journal/
  /2026-04/                          ← mois
    /2026-04-09/                     ← jour
      /trade_001_SELL_EURUSD/        ← trade#_direction_paire
        entry.png                    ← screenshot à l'entrée
        exit.png                     ← screenshot à la sortie
        trade.json                   ← données du trade
      /trade_002_BUY_GBPUSD/
        entry.png
        exit.png
        trade.json
```

### Données à logger (trade.json)
```json
{
  "trade_id": 1,
  "pair": "EURUSD",
  "direction": "SELL",
  "mode": "INTRADAY_RAPIDE",
  "entry_time": "2026-04-09 09:15:00",
  "entry_price": 1.0850,
  "exit_time": "2026-04-09 11:30:00",
  "exit_price": 1.0820,
  "exit_reason": "TP_S1",
  "sl_initial": 1.0875,
  "tp_initial": 1.0810,
  "breakeven_activated": true,
  "profit_pips": 30,
  "profit_pct": 0.8,
  "risk_reward": 1.5,
  "convergence_score": "3/3",
  "pp_confirmation": true,
  "rules_checked": {
    "trend": true,
    "kumo": true,
    "tenkan_slope": true,
    "tenkan_broken": true,
    "tenkan_position": true,
    "kijun_break": true,
    "outside_cloud": true,
    "chikou_free": true,
    "time_ok": true,
    "no_news": true
  }
}
```

### Pour le bot MQL5
```
// Screenshot à l'entrée
string folder = "ToutieTrader_Journal\\"
              + TimeToString(TimeCurrent(), TIME_DATE) + "\\"
              + "trade_" + IntegerToString(trade_count, 3, '0')
              + "_" + direction + "_" + Symbol();
              
ChartScreenShot(0, folder + "\\entry.png", 1920, 1080);

// Screenshot à la sortie (dans OnTrade ou quand position ferme)
ChartScreenShot(0, folder + "\\exit.png", 1920, 1080);

// Sauvegarder trade.json
int file = FileOpen(folder + "\\trade.json", FILE_WRITE|FILE_TXT);
FileWriteString(file, trade_data_json);
FileClose(file);
```

---

## 🔧 FRAMEWORK D'OPTIMISATION (Strategy Tester MT5)

### Philosophie
L'optimizer ne teste PAS juste "combien" (des seuils numériques). Il teste aussi **"comment la règle agit"** (blocage, réduction de taille, warning, ignoré). C'est ce qui le rend puissant.

### 🔒 NOYAU VERROUILLÉ (jamais modifié par l'optimizer)
Ces règles sont l'ADN de la stratégie Ichimoku. L'optimizer ne peut PAS les désactiver :
- Sens de tendance (on trade dans le sens du trend)
- Nuage aligné (SSA vs SSB dans le bon sens)
- Cassure Kijun (le trigger de base)
- Hors du nuage (pas de trade dans le Kumo)
- Logique Chikou (libre dans la direction du trade)
- Pas de contre-tendance

### 🔓 INTERPRÉTATION OPTIMISABLE
Pour chaque règle ci-dessous, l'optimizer peut tester :
- **Le seuil numérique** (en ratio relatif, jamais en pips fixes)
- **Le mode d'action** (blocage / réduction de taille / warning / ignoré)

---

### OPT-1 : Tenkan trop loin (effet élastique)

**Paramètre numérique — distance relative :**
| Variable | Quoi | Valeurs à tester |
|----------|------|-----------------|
| `elastic_distance` | Distance prix ↔ Tenkan en multiple d'ATR(14) | 0.5, 0.75, 1.0, 1.5, 2.0, 3.0 |
| `elastic_alt_metric` | Mesure alternative | distance en % de la hauteur du nuage, en multiple de la taille moyenne des bougies, en % du swing récent |

**Paramètre de mode — comment la règle agit :**
| Mode | Comportement |
|------|-------------|
| `elastic_mode = "block"` | Bloque le trade si Tenkan trop loin |
| `elastic_mode = "reduce"` | Réduit le risque proportionnellement |
| `elastic_mode = "warning"` | Log un warning mais trade quand même |
| `elastic_mode = "off"` | Règle complètement ignorée |
| `elastic_mode = "conditional"` | Bloque seulement si convergence multi-UT < 2/3 |

**Combinaisons à tester :**
- off (ignorer complètement)
- warning à 1.5×ATR (log mais trade)
- reduce à 1.0×ATR (réduire taille de 50%)
- block à 2.0×ATR (bloquer)
- conditional à 1.5×ATR (bloquer seulement si convergence faible)

---

### OPT-2 : Distance Tenkan trop loin — métriques relatives

**Pourquoi pas en pips :** 10 pips sur EURUSD ≠ 10 pips sur GBPJPY. Faut normaliser.

**Métriques à tester :**

| Métrique | Formule | Pourquoi |
|----------|---------|----------|
| **ATR ratio** | `dist(Prix, Tenkan) / ATR(14)` | Normalise par la volatilité récente |
| **Body ratio** | `dist(Prix, Tenkan) / avg_body(20)` | Normalise par la taille moyenne des bougies |
| **Cloud ratio** | `dist(Prix, Tenkan) / abs(SSA - SSB)` | Normalise par la hauteur du nuage |
| **Swing ratio** | `dist(Prix, Tenkan) / dernier_swing_size` | Normalise par la structure récente |

L'optimizer teste quelle métrique + quel seuil donne le meilleur résultat sur l'historique.

---

### OPT-3 : Position Tenkan vs Kijun

**Rappel :** BUY = Tenkan sous Kijun (pullback), SELL = Tenkan au-dessus

**Paramètre de mode :**
| Mode | Comportement |
|------|-------------|
| `tk_position_mode = "strict"` | Obligatoire (Tenkan dans la bonne position, sinon skip) |
| `tk_position_mode = "allow_simultaneous"` | Obligatoire SAUF si cassure simultanée TK+KJ |
| `tk_position_mode = "soft"` | Réduit la taille si pas en bonne position |
| `tk_position_mode = "off"` | Complètement ignoré |

**Combinaisons :**
- strict : seuls les trades "classiques" passent (moins de trades, plus fiables?)
- allow_simultaneous : les trades à fort momentum passent aussi (plus de trades)
- soft : tous les trades passent mais les "mauvaise position" sont plus petits
- off : on ignore cette règle → plus de trades mais potentiellement plus de pertes

---

### OPT-4 : Cassure Kijun — qualité de la cassure

**Pas en pips. En ratio relatif à la bougie et à l'ATR.**

**Paramètres numériques :**
| Variable | Quoi | Valeurs à tester |
|----------|------|-----------------|
| `kijun_break_atr` | Distance Close-Kijun en multiple d'ATR | 0.05, 0.1, 0.2, 0.3, 0.5 |
| `kijun_break_body_pct` | % du corps de la bougie qui a traversé la Kijun | 10%, 30%, 50%, 70% |
| `kijun_break_candle_pct` | Clôture dans le top/bottom X% de la bougie | 10%, 25%, 40% |

**Explication `kijun_break_body_pct` :**
- La bougie ouvre à 1.0840, Kijun à 1.0835, bougie clôture à 1.0820
- Corps = 1.0840 - 1.0820 = 20 pips
- Partie sous la Kijun = 1.0835 - 1.0820 = 15 pips
- % traversé = 15/20 = 75% → cassure très franche
- Si clôture à 1.0833 → seulement 2/7 = 28% → cassure faible

**Paramètre de mode :**
| Mode | Comportement |
|------|-------------|
| `kijun_quality_mode = "block"` | Bloque si cassure trop faible |
| `kijun_quality_mode = "reduce"` | Réduit la taille proportionnellement à la qualité |
| `kijun_quality_mode = "off"` | Toute cassure est acceptée (Close au-delà de Kijun suffit) |

---

### OPT-5 : Chikou libre — niveau de liberté

**Pas juste libre/pas libre. Degré de liberté.**

**Paramètres :**
| Variable | Quoi | Valeurs à tester |
|----------|------|-----------------|
| `chikou_check_scope` | Quoi vérifier | bougies seul, bougies+nuage, bougies+nuage+kijun+tenkan (tout) |
| `chikou_margin_atr` | Marge minimale en ATR | 0, 0.1, 0.3, 0.5 |
| `chikou_bars_check` | Combien de barres autour de -26 vérifier | 1 (juste [26]), 3 ([25-27]), 5 ([24-28]) |

**Explication `chikou_check_scope` :**
- `"candles_only"` : Chikou > High[26] (ou < Low[26]) suffit
- `"candles_cloud"` : + aussi au-dessus/en dessous du nuage à [26]
- `"all"` : + aussi au-dessus/en dessous de Kijun[26] et Tenkan[26]

**Explication `chikou_bars_check` :**
- `1` : on regarde seulement la barre exacte [26] → rapide mais peut rater un obstacle à [25] ou [27]
- `3` : on regarde [25], [26], [27] → le Chikou doit être libre sur les 3
- `5` : [24] à [28] → plus conservateur, le chemin est vraiment dégagé

**Paramètre de mode :**
| Mode | Comportement |
|------|-------------|
| `chikou_mode = "strict"` | Bloque si Chikou pas libre selon le scope choisi |
| `chikou_mode = "soft"` | Réduit la taille si pas 100% libre |

---

### OPT-6 : Clôture hors du nuage — degré de sortie

**Pas juste "hors du nuage". Combien hors du nuage.**

**Paramètres :**
| Variable | Quoi | Valeurs à tester |
|----------|------|-----------------|
| `cloud_exit_margin_atr` | Distance minimum Close-bord du nuage en ATR | 0 (juste hors), 0.05, 0.1, 0.2, 0.3 |
| `cloud_exit_body_pct` | % du corps de la bougie qui doit être hors du nuage | 50%, 75%, 90%, 100% |

**Explication :**
- `cloud_exit_margin_atr = 0` : la clôture est à 0.00001 au-dessus du nuage → accepté (très loose)
- `cloud_exit_margin_atr = 0.1` : la clôture doit être au moins 0.1×ATR au-dessus/en dessous du nuage
- `cloud_exit_body_pct = 100%` : tout le corps doit être hors du nuage (très strict)
- `cloud_exit_body_pct = 50%` : au moins la moitié du corps hors du nuage (plus tolérant)

**Paramètre de mode :**
| Mode | Comportement |
|------|-------------|
| `cloud_exit_mode = "strict"` | Bloque si pas assez loin du nuage |
| `cloud_exit_mode = "reduce"` | Réduit la taille selon la distance |
| `cloud_exit_mode = "basic"` | Close hors du nuage suffit (pas de marge) |

---

### OPT-7 : Convergence multi-UT — rôle exact

**Paramètre de mode :**
| Mode | Quoi | Comportement |
|------|------|-------------|
| `convergence_mode = "3/3_required"` | 3/3 obligatoire | Bloque si < 3/3 |
| `convergence_mode = "2/3_minimum"` | 2/3 minimum | Bloque si < 2/3 |
| `convergence_mode = "size_scaling"` | Taille selon score | 3/3=150%, 2/3=100%, 1/3=50%, contradiction=25% |
| `convergence_mode = "no_contradiction"` | Contradiction bloque | Bloque seulement si UT0 vs UT2 en opposition |
| `convergence_mode = "info_only"` | Info seulement | Log le score mais ne change rien |
| `convergence_mode = "off"` | Désactivé | Ignore complètement les autres UT |

---

### OPT-8 : Pente Tenkan — seuil minimum

**Le problème :** `Tenkan[0] > Tenkan[1]` est vrai même si la différence est 0.000001. C'est techniquement "haussier" mais en réalité c'est plat.

**Paramètres :**
| Variable | Quoi | Valeurs à tester |
|----------|------|-----------------|
| `tenkan_slope_min_atr` | Pente minimum en multiple d'ATR | 0 (tout accepter), 0.01, 0.02, 0.05, 0.1 |

**Paramètre de mode :**
| Mode | Comportement |
|------|-------------|
| `tenkan_slope_mode = "block"` | Bloque si pente trop faible |
| `tenkan_slope_mode = "reduce"` | Réduit la taille si pente faible |
| `tenkan_slope_mode = "off"` | Toute pente dans le bon sens est acceptée |

---

## 📊 RÉSUMÉ DES PARAMÈTRES D'OPTIMISATION

### A. Paramètres numériques relatifs (jamais en pips)

| # | Paramètre | Type | Valeurs à tester |
|---|-----------|------|-----------------|
| 1 | `elastic_distance` | ATR ratio | 0.5, 0.75, 1.0, 1.5, 2.0, 3.0 |
| 2 | `kijun_break_atr` | ATR ratio | 0.05, 0.1, 0.2, 0.3, 0.5 |
| 3 | `kijun_break_body_pct` | % du corps | 10, 30, 50, 70 |
| 4 | `chikou_margin_atr` | ATR ratio | 0, 0.1, 0.3, 0.5 |
| 5 | `chikou_bars_check` | nb barres | 1, 3, 5 |
| 6 | `cloud_exit_margin_atr` | ATR ratio | 0, 0.05, 0.1, 0.2, 0.3 |
| 7 | `tenkan_slope_min_atr` | ATR ratio | 0, 0.01, 0.02, 0.05, 0.1 |

### B. Paramètres de mode logique

| # | Paramètre | Modes possibles |
|---|-----------|----------------|
| 1 | `elastic_mode` | block, reduce, warning, off, conditional |
| 2 | `tk_position_mode` | strict, allow_simultaneous, soft, off |
| 3 | `kijun_quality_mode` | block, reduce, off |
| 4 | `chikou_mode` | strict, soft |
| 5 | `chikou_check_scope` | candles_only, candles_cloud, all |
| 6 | `cloud_exit_mode` | strict, reduce, basic |
| 7 | `convergence_mode` | 3/3_required, 2/3_minimum, size_scaling, no_contradiction, info_only, off |
| 8 | `tenkan_slope_mode` | block, reduce, off |

### C. Paramètres d'action (ce que fait le "reduce")

| Grade de réduction | Impact sur la taille |
|-------------------|---------------------|
| Warning seulement | Log, pas de changement |
| Réduction légère | Taille × 0.75 |
| Réduction moyenne | Taille × 0.50 |
| Réduction forte | Taille × 0.25 |
| Blocage | Taille × 0 = pas de trade |

---

## ⚠️ CE QUE L'OPTIMIZER NE DOIT PAS FAIRE

1. **Pas désactiver le noyau :** les 6 règles verrouillées restent ON
2. **Pas utiliser de pips fixes :** tout est en ratio relatif (ATR, %, body)
3. **Pas overfitter :** tester sur une période, valider sur une autre (walk-forward)
4. **Pas trop de combinaisons :** commencer avec les modes + 2-3 valeurs par seuil, pas tout d'un coup

---

## 🎯 STRATÉGIE D'OPTIMISATION RECOMMANDÉE

### Phase 1 : Modes d'abord (trouver le bon comportement)
- Fixer tous les seuils numériques à des valeurs "neutres" (moyennes)
- Tester chaque paramètre de mode : block vs reduce vs off
- Identifier pour chaque règle quel mode performe le mieux

### Phase 2 : Seuils ensuite (affiner les chiffres)
- Une fois les modes fixés, optimiser les seuils numériques un par un
- Tester 3-5 valeurs par seuil
- Garder le meilleur

### Phase 3 : Validation (walk-forward)
- Optimiser sur 1 an de data
- Valider sur 6 mois différents
- Si les résultats tiennent → les paramètres sont robustes
- Si ça marche seulement sur la période d'optimisation → overfitting, recommencer

---

## ❓ ÉLÉMENTS RESTANTS

- [ ] **Paires** : quelles paires forex spécifiques ? (le cours mentionne DAX)
- [ ] **Durée buffer news** : minutes avant/après une news high-impact
- [ ] **IC Markets server timezone** : vérifier quelle TZ le serveur MT5 utilise
- [ ] **Buffer SL** : combien de pips (ou ATR ratio) de buffer au Low/High de la bougie de cassure

---

*Dernière mise à jour : extraction des screenshots 13631-13709 + framework d'optimisation*
*Source : Alti Trading — DayTrader PRO (cours Ichimoku)*
