# QDE-017 — Premier test de conditionnement order flow : le Delta par barre prédit-il le rendement forward de MES ?

**Date :** 2026-09-01
**Mode :** lecture seule. **Production modifiée : NON.**
**Statut :** outillage construit et vérifié ; **Phase 2 (analyse) en attente du CSV d'export** — à produire par l'utilisateur dans ATAS (§4).

---

## 1. Contexte

QDE-016 a tranché la Phase 0 : les données order-flow par barre (`Bid`, `Ask`, `Delta`, `MaxDelta`, `MinDelta`, footprint) sont **réelles, cohérentes et complètes sur ~126 jours de MES M5** (`bid + ask == volume` exact jusqu'à la barre la plus ancienne chargée, aucune dégradation avec l'âge). L'axe order flow est donc un projet de semaines : l'échantillon existe déjà, un export one-shot suffit.

QDE-017 est le **premier test de la question de fond** : une variable d'état order-flow au moment de la clôture d'une barre conditionne-t-elle le rendement des H barres suivantes, suffisamment pour battre le coût round-trip (~0,77 pt) ? Méthodologie strictement calquée sur QDE-014 (segmentation par tercile, IC 95 %, split train/OOS purgé) pour rester directement comparable.

---

## 2. Hypothèses

- **Primaire :** le **Delta signé** de la barre (`Delta / Volume`) prédit le signe du rendement des H barres suivantes — soit en continuation (pression acheteuse → hausse), soit en réversion (pression acheteuse épuisée → baisse). Concrètement : la règle « prendre la position dans le sens de `sign(Delta)`, sortir H barres plus tard » a-t-elle une espérance **nette de coût robustement positive** (IC excluant 0) ?
- **Secondaires (exploratoires) :**
  - `|Delta| / Volume` (intensité du déséquilibre, non signé) → prédit-il l'**amplitude** `|fwdMove|` (utile pour un jeu de volatilité) ?
  - `(MaxDelta − MinDelta) / Volume` (amplitude d'excursion intra-barre du delta) → idem.
  - `Betweens / Volume` (part des trades au mid) → régime de liquidité.

---

## 3. Méthodologie et outillage

### 3.1 Données

Export one-shot de **toutes les barres MES M5 chargées** (~24 500, ~126 jours) via l'indicateur ATAS jetable **`OrderFlowExport`**. Colonnes : `timestamp,open,high,low,close,volume,bid,ask,delta,maxDelta,minDelta,oi,ticks,betweens`.

### 3.2 Nettoyage (`OrderFlowCsvBarSource.ReadClean`)

Retire : la dernière ligne (barre en cours de formation) ; les barres `volume ≤ 0` (slots de session vides) ; toute barre dont le timestamp n'est pas strictement croissant (dédup / anti-désordre, même discipline que `HistoricalSeries.TryCreate`).

### 3.3 Re-contrôle qualité sur le corpus complet

QDE-016 n'a spot-checké que 6 barres. La sonde recalcule sur les ~24 500 : % de barres où `bid + ask == volume`, où `delta == ask − bid`, où `betweens == 0`, où `oi == 0`. Confirme (ou infirme) que la cohérence tient partout.

### 3.4 Rendements forward, conscients des gaps

Intervalle modal de barre détecté depuis la médiane des écarts. Pour l'horizon H, une barre `i` n'est retenue que si `ts[i+H] − ts[i] ≤ H × intervalle × 1,6` — la fenêtre ne franchit aucune coupure de session (week-end / pause CME / férié, cf. QDE-016 Phase 0). `fwdMove_i = close[i+H] − close[i]` en points.

Horizons testés : **H ∈ {1, 3, 6, 12} barres M5** (≈ 5 / 15 / 30 / 60 min).

### 3.5 Tests

| Test | Contenu |
|---|---|
| **1 — Règle directionnelle Delta** | Position dans le sens `sign(Delta/Volume)`, sortie à H barres. Espérance **brute** et **nette de coût** (`− 0,77 pt`) par trade, en points ; taux de réussite (fraction où `sign(Delta) == sign(fwdMove)`) vs 50 % ; IC 95 % ; split train/OOS (60/40, purge 20). |
| **2 — Terciles de feature** | Pour chaque feature : moyenne de `fwdMove` signé (features signées) ou `|fwdMove|` (features non signées) par tercile, IC 95 %, train/OOS. Recherche de monotonicité. |
| **3 — Pearson(feature, fwdMove)** | Corrélation linéaire par feature et par H, avec bande bruit blanc ±1,96/√n. |
| **Baseline** | `fwdMove` moyen inconditionnel (drift), `|fwdMove|` moyen, espérance nette « toujours long » / « toujours short ». |

Coût : `RoundTripCostPts = 0,77` (base MES, identique à QDE-012+/QDE-014). Un pari directionnel = un aller-retour, quel que soit le sens.

**Critère « établi avec confiance » (réservé à l'hypothèse primaire) :** la règle directionnelle Delta a une espérance **nette robustement positive** (IC excluant 0) à au moins un horizon, **ET** cela tient en OOS. Sinon → classification explicite « pas de signal order-flow directionnel exploitable à cette granularité », les features secondaires étant rapportées comme exploratoires uniquement.

### 3.6 Fichiers livrés

| Fichier | Rôle | Emplacement |
|---|---|---|
| `OrderFlowExport.cs` | Indicateur ATAS jetable — export one-shot | `C:\Users\rnbch\OneDrive\Bureau\OrderFlowDepthProbe\` (**hors dépôt IQIA**, même projet que `OrderFlowDepthProbe`, `net8.0-windows`) |
| `OrderFlowCsvBarSource.cs` | Lecteur de recherche : CSV → `OrderFlowBar[]` + `ToHistoricalSeries()` remplissant `HistoricalBar.BidVolume/AskVolume/Delta/OpenInterest` (slots déjà présents, `IHistoricalBarSource`/`HistoricalSeries.Create` publics → **zéro modification de production**) | `IQIAIndicator/Tests/Research/OrderFlowFeasibility/` |
| `OrderFlowConditioningPilotTests.cs` | Sonde d'analyse (Tests 1–3 + baseline). **Skip** propre tant que le CSV n'existe pas. | `IQIAIndicator/Tests/Research/OrderFlowFeasibility/` |

État : le projet ATAS compile (`net8.0-windows`, 0 erreur) et le DLL (`OrderFlowDepthProbe.dll`, 2 indicateurs) est déployé dans `%APPDATA%\ATAS\Indicators\`. Le projet Tests compile (0 erreur). La sonde d'analyse **Skip** correctement (« No orderflow_export_*.csv … »).

---

## 4. Ce qui reste manuel (nécessite ATAS + flux réel)

1. **Fermer puis rouvrir ATAS** (recharge le DLL à 2 indicateurs).
2. Graphique **MES M5, flux RÉEL** (pas SIM / pas Replay), **« Nombre de jours à charger » au maximum possible**, rechargé. *(Pousser plus haut que les 126 jours de QDE-016 si le flux le permet — plus de barres = meilleure puissance.)*
3. Ajouter l'indicateur **`OrderFlowExport`** au graphique. Il s'exécute une fois.
4. Récupérer le CSV : **`%LOCALAPPDATA%\IQIA\orderflow\orderflow_export_<horodatage>.csv`** (+ `.done.txt`).
5. Me le signaler → je lance `OrderFlowConditioningPilotTests` et complète les §5–§8.

---

## 5. Phase 2 — Résultats

**Corpus :** CSV `orderflow_export_20260901_185621.csv` — 67 238 lignes brutes, **67 236 barres propres** après `ReadClean` (2 lignes retirées : barre en formation + 1). Fenêtre **2025-09-18 → 2026-09-01 (~349 jours calendaires, ~8× le book H1 720 j de QDE-012 en nombre de barres)**. Intervalle modal 5 min, 99,6 % de pas contigus.

**Re-contrôle qualité sur les 67 236 barres :** `bid + ask == volume` **100,0 %** ; `delta == ask − bid` **100,0 %** ; `betweens == 0` **100,0 %** ; `oi == 0` **100,0 %**. Le spot-check 6 barres de QDE-016 tient sur l'intégralité du corpus.

### 5.1 Test 1 — hypothèse primaire : `sign(Delta)` prédit la direction

Règle « position dans le sens `sign(Delta/Volume)`, sortie H barres plus tard », coût round-trip 0,77 pt.

| H | n | hit rate (vs 0,500) | espérance brute (pt, IC 95 %) | **espérance NETTE (pt, IC 95 %)** | verdict | TRAIN / OOS (net) |
|---|---|---|---|---|---|---|
| 1 (5 min) | 66 834 | **0,468** | −0,015 ± 0,028 | **−0,785 ± 0,028** | net− robuste | −0,746 / −0,843 |
| 3 (15 min) | 66 339 | 0,477 | −0,026 ± 0,048 | **−0,796 ± 0,048** | net− robuste | −0,758 / −0,853 |
| 6 (30 min) | 65 601 | 0,485 | −0,024 ± 0,068 | **−0,794 ± 0,068** | net− robuste | −0,775 / −0,820 |
| 12 (60 min) | 64 127 | 0,489 | −0,028 ± 0,097 | **−0,798 ± 0,097** | net− robuste | −0,757 / −0,857 |

`sign(Delta)` prédit la direction **moins bien qu'un pile ou face** (0,47–0,49) à tous les horizons. Brut indistinguable de 0 ; net = exactement −1 aller-retour, robuste, TRAIN et OOS. Inverser la règle ne sauve rien (brut symétrique ~0 < coût).

### 5.2 Test 2 — terciles de `Delta/Volume`, cible = fwdMove **signé**

Pente faiblement négative (tercile haut de pression acheteuse → fwdMove légèrement négatif), mais **non robuste** (IC englobent 0 partout) et **TRAIN/OOS de signes opposés** (ex. H=1 tercile haut : TRAIN +0,018 / OOS −0,092). Aucun signal directionnel.

### 5.3 Test 2/3 — features non signées → `|fwdMove|` (volatilité forward)

| feature | Pearson(feature, \|fwdMove\|) | bande bruit blanc | stabilité |
|---|---|---|---|
| `deltaRatio` (signé) vs fwdMove signé | ≈ **−0,002** | ±0,008 | dans le bruit — pas de relation linéaire directionnelle |
| `\|Delta\|/Volume` vs \|fwdMove\| | ≈ **−0,167** | ±0,008 | identique aux 4 horizons |
| `(MaxDelta − MinDelta)/Volume` vs \|fwdMove\| | ≈ **−0,274** | ±0,008 | identique aux 4 horizons |

*(Bug cosmétique de la sonde : le format `:+0.0000` affiche `-+0.XXXX` pour un négatif — le `+` est un littéral. Valeurs réelles ci-dessus.)*

`(MaxDelta − MinDelta)/Volume` par tercile, cible = `|fwdMove|` :

| H | tercile bas | tercile haut | ratio bas/haut | TRAIN vs OOS |
|---|---|---|---|---|
| 1 | 3,33 ± 0,05 pt | 1,40 ± 0,02 pt | **2,4×** | ≈ stables |
| 3 | 5,71 ± 0,08 | 2,47 ± 0,04 | 2,3× | ≈ stables |
| 6 | 8,09 ± 0,12 | 3,58 ± 0,06 | 2,3× | ≈ stables |
| 12 | 11,48 ± 0,16 | 5,20 ± 0,08 | 2,2× | ≈ stables |

Monotone, IC serrés, TRAIN ≈ OOS à chaque horizon. `|Delta|/Volume` : même sens, effet plus faible (r ≈ −0,17). `betweensRatio` : dégénéré (`betweens == 0` partout → terciles s'effondrent).

**Sens = inverse :** flux très unilatéral / forte excursion de delta → heure suivante plus **calme** (impulsion épuisée) ; flux équilibré malgré un gros volume → mouvement suivant plus **grand** (compression / indécision qui se résout). Microstructure documentée.

---

## 6. Établi avec confiance

1. **Hypothèse primaire RÉFUTÉE.** `sign(Delta)` par barre n'a **aucun pouvoir prédictif directionnel** sur MES M5 aux horizons 5–60 min : hit rate 0,468–0,489 (< 0,500), espérance brute indistinguable de 0, espérance nette = −1 aller-retour robuste (IC excluant 0) à tous les horizons, TRAIN et OOS. Corpus de 64 000–67 000 paris — puissance statistique maximale. Résultat cohérent avec l'ensemble de l'investigation : pas d'edge directionnel net de coût sur MES intraday.
2. **Qualité des données order-flow ATAS confirmée** sur 67 236 barres : `bid + ask == volume` et `delta == ask − bid` exacts à 100 %.

**QDE-017 ne confirme aucun edge directionnel.**

---

## 7. Exploratoire / non concluant

**Une relation prédictive robuste existe — mais de MAGNITUDE, pas de direction :**

- `(MaxDelta − MinDelta)/Volume` (amplitude d'excursion intra-barre du delta, normalisée) prédit `|fwdMove|` avec **Pearson r ≈ −0,274** (n ≈ 65 000, bande ±0,008 → massivement significatif), **monotone en terciles (2,2–2,4× entre bas et haut), robuste et TRAIN/OOS-stable aux 4 horizons.** `|Delta|/Volume` : même relation, r ≈ −0,17. Sens inverse (flux unilatéral → marché plus calme ensuite).
- **C'est la première relation prédictive robuste, stable hors échantillon et économiquement large de toute l'investigation QDE-012 → QDE-017.**
- **Mais ce n'est PAS un edge traçable en l'état :** c'est un prédicteur de volatilité future, pas de direction. Le monétiser exigerait (a) une structure optionnelle (pas d'historique d'options MES/ES accessible — QDE-013), ou (b) un signal directionnel à conditionner (aucun ne survit — QDE-012 à 015). Génératrice d'hypothèse uniquement.

---

## 8. Recommandation

**L'hypothèse order-flow directionnelle au niveau agrégé-par-barre M5 est close négativement.** Combinée à QDE-012–015 (prix) : ni le prix ni le Delta par barre ne portent d'edge directionnel net de coût sur MES intraday.

Options, par valeur décroissante :

1. **QDE-018 — conditionnement footprint détaillé.** L'export ne contient que les agrégats par barre. Le footprint complet (`IndicatorCandle.GetAllPriceLevels()` → volume/bid/ask par niveau de prix) porte une structure directionnelle que le Delta agrégé masque : absorption (gros volume à un prix sans mouvement), imbalances diagonales empilées, divergence delta/prix aux extrêmes de barre, position du POC vs clôture. Un **dernier** test directionnel avec ces features, sur le même corpus (l'export footprint est ~40 lignes de plus dans `OrderFlowExport`), avant de fermer définitivement l'axe order flow. Coût : ~1 semaine.
2. **QDE-018 alt — la trouvaille de volatilité comme entrée du Risk Engine.** `deltaRange` fournit une estimation *forward* de la volatilité (r ≈ −0,27 avec le mouvement réel de l'heure suivante) là où la production dimensionne le SL sur un `2×σ` *backward*. Question étroite mais concrète : un SL/TP conditionné par `deltaRange` réduit-il la variance des résultats du book existant ? **Changement de Risk Engine → nécessite un lot dédié avec son propre protocole**, hors du périmètre « recherche d'edge ».
3. **Arrêt de l'axe recherche d'edge.** Acter que MES intraday (M5–H1, ~2 ans de données prix + ~1 an d'order flow) n'a pas de structure directionnelle exploitable après coûts, et que la seule relation robuste trouvée (imbalance → volatilité) n'a pas de voie de monétisation avec l'outillage actuel.

**Ne PAS :** conclure à un edge à partir de la relation de volatilité sans voie de monétisation ; ajuster un paramètre de production ; modifier le Risk Engine hors d'un lot dédié.

---

## 9. Interdictions respectées

- **Aucune modification de production.** Dépôt IQIA : 2 fichiers ajoutés sous `Tests/Research/OrderFlowFeasibility/` (lecteur + sonde). Hors dépôt : `OrderFlowExport.cs` dans le projet jetable `OrderFlowDepthProbe` (`.gitignore *`, jamais dans la solution, jamais référencé par la production).
- **`HistoricalBar`, `HistoricalSeries`, `CsvHistoricalBarSource`, `ScientificDatasetCollector` intouchés.** Le lecteur utilise les slots order-flow **déjà présents** dans `HistoricalBar` et les API publiques `HistoricalSeries.Create` / `IHistoricalBarSource`.
- **Aucune calibration, aucune Decision Rule, statut SIGNAL_ONLY inchangé.**

---

## 10. FINAL OUTPUT

**STATUS :** Analyse terminée sur 67 236 barres MES M5 (~349 jours, ~8× le book H1 de QDE-012). **Hypothèse primaire RÉFUTÉE** : `sign(Delta)` par barre a un hit rate 0,468–0,489 (< 0,500) et une espérance nette de −1 aller-retour robuste à tous les horizons 5–60 min. Aucun edge directionnel dans le Delta agrégé par barre. **Trouvaille exploratoire robuste (non traçable en l'état)** : `(MaxDelta−MinDelta)/Volume` prédit la volatilité forward `|fwdMove|` avec Pearson r ≈ −0,27 (sens inverse), monotone en terciles (2,2–2,4×), stable OOS aux 4 horizons — première relation prédictive robuste de toute l'investigation, mais de magnitude et non de direction.

**PRODUCTION MODIFIED :** NO. Dépôt : `Tests/Research/OrderFlowFeasibility/OrderFlowCsvBarSource.cs` + `OrderFlowConditioningPilotTests.cs`. Hors dépôt : `OrderFlowExport.cs` (2ᵉ indicateur du DLL jetable). `HistoricalBar`/`HistoricalSeries`/`CsvHistoricalBarSource`/`ScientificDatasetCollector` intouchés.

**TESTS :** `OrderFlowConditioningPilotTests` — Passed (67 236 barres ; data quality 100 % ; Tests 1–3 + baseline aux 4 horizons). Bug cosmétique de format sur la ligne Pearson (valeurs récupérables, cf. §5.3).

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-017_OrderFlow_Conditioning_Pilot.md`.

**NEXT ACTION :** Décision. Option 1 — **QDE-018 : conditionnement footprint détaillé** (`GetAllPriceLevels`, ~40 lignes de plus dans `OrderFlowExport`) — dernier test directionnel avec absorption / imbalances diagonales / divergence delta-prix / POC, avant fermeture de l'axe. Option 2 — la trouvaille de volatilité comme entrée forward du Risk Engine (lot dédié, hors « recherche d'edge »). Option 3 — arrêt de l'axe recherche d'edge (MES intraday sans structure directionnelle exploitable, prix comme order flow).
