# QDE-019 — Conditionnement par la structure footprint : test terminal de l'axe order flow

**Date :** 2026-09-01
**Mode :** lecture seule. **Production modifiée : NON.** Aucun changement de Risk Engine / Decision Rule ; statut SIGNAL_ONLY inchangé.
**Sondes :** `IQIAIndicator/Tests/Research/OrderFlowFeasibility/FootprintCsvBarSource.cs` (lecteur) + `FootprintConditioningPilotTests.cs` (analyse). Export : `OrderFlowFootprintExport` (3ᵉ indicateur du DLL jetable hors dépôt). Sortie : `Output/footprint_conditioning_pilot.txt`.

---

## 1. Contexte

QDE-017 a réfuté l'hypothèse order-flow directionnelle sur les **agrégats par barre** (`Delta`) et QDE-018 a montré que la trouvaille de volatilité associée (`deltaRange`) n'est pas incrémentale sur le `σ` backward de production. Restait un seul contenu order-flow non testé : le **footprint par niveau de prix** (`IndicatorCandle.GetAllPriceLevels()` → volume/bid/ask à chaque prix), qui porte une structure que les agrégats masquent — absorption, imbalances diagonales empilées, divergence delta/prix aux extrêmes de barre, position du POC, forme de la distribution.

QDE-019 est le **test terminal** de l'axe order flow : la structure footprint à la barre de signal MeanReverting prédit-elle la direction du rendement des H barres suivantes, net de coût ?

---

## 2. Méthodologie

- **Données :** `orderflow_footprint_20260901_202931.csv` — **67 254 barres MES M5 propres** (2025-09-18 → 2026-09-01, ~349 jours), 18 colonnes : OHLCV + delta + 10 features footprint calculées à l'export depuis la surface footprint ATAS de chaque barre complète (donc causales) :
  - **signées (hypothèses directionnelles) :** `pocOffTicks` (POC au-dessus/dessous de la clôture), `pocVsClosePos` (POC plus haut/bas que la clôture, dans le range), `pocPosCentred` (POC moitié haute/basse), `netStackedImb` (imbalances d'achat empilées en haut − imbalances de vente empilées en bas, ratio 3×), `aggSkewTopBot` (volume ask dans la moitié haute − volume bid dans la moitié basse, normalisé), `buyVsSellPos` (position du plus fort achat net − position du plus fort vente net), `posDeltaVsClose` (plus fort achat net au-dessus/dessous de la clôture) ;
  - **non signées (absorption/structure → magnitude) :** `pocConc` (vol au POC / vol), `top3Conc` (vol des 3 plus gros niveaux / vol), `fpLevels` (nombre de niveaux de prix), `totalStacked` (total des imbalances empilées).
- **Book :** `RunFullBacktest` inchangé (warmup 128, horizon 10, coût round-trip ~0,77 pt) sur la série reconstruite depuis le CSV → **13 556 trades MeanReverting**.
- **Cible :** `fwdMove_H` = `close[k+H] − entry` (entry = open barre k+1), H ∈ {1, 3, 6, 12} barres M5, fenêtre conscients des gaps de session.
- **Tests (par horizon) :** (1) règle directionnelle « suivre `sign(feature)` », espérance brute et nette de coût, hit rate vs 0,500, IC 95 %, split TRAIN 60 % / OOS 40 % (purge 40) ; (2) moyenne de la cible par tercile (features signées → `fwdMove` signé ; non signées → `|fwdMove|`) ; (3) Pearson(feature, cible), bande ±1,96/√n.
- **Sanity footprint :** `fpLevels` médian 15 ; `pocConc` médian 0,154 ; `stackedBuyTop` moyen 0,75 ; `stackedSellBot` moyen 0,76 ; `aggSkew` médian +0,031.

---

## 3. Résultats

### 3.1 Test directionnel — 7 features footprint signées (net de coût)

| feature | hit rate (min–max sur H) | espérance brute (pt) | **espérance NETTE (pt)** | verdict (tous H) |
|---|---|---|---|---|
| `pocOffTicks` = `pocVsClosePos` | 0,480 – 0,501 | −0,001 à +0,043 | **−0,727 à −0,768** | net− robuste |
| `pocPosCentred` | 0,472 – 0,496 | −0,039 à +0,077 | **−0,694 à −0,809** | net− robuste |
| `netStackedImb` | **0,466 – 0,483** | −0,183 à +0,058 | **−0,712 à −0,953** | net− robuste |
| `aggSkewTopBot` | 0,473 – 0,489 | −0,053 à +0,038 | **−0,732 à −0,825** | net− robuste |
| `buyVsSellPos` | 0,474 – 0,506 | −0,017 à +0,042 | **−0,728 à −0,787** | net− robuste |
| `posDeltaVsClose` | 0,475 – 0,501 | −0,040 à +0,017 | **−0,753 à −0,810** | net− robuste |

**Aucune feature footprint signée n'a de pouvoir prédictif directionnel.** Hit rate ≤ ~0,50 partout, espérance brute indistinguable de 0 (IC englobent 0), espérance nette = −1 aller-retour, **robustement négative à tous les horizons × toutes les features × TRAIN et OOS**. Inverser une règle ne sauve rien (brut symétrique ~0 < coût). Résultat identique au Delta par barre (QDE-017) et au prix seul (QDE-012→015).

### 3.2 Pearson — features signées vs `fwdMove` signé

Toutes entre **−0,008 et +0,029** à tous les horizons (bande ±0,017). Seul `pocOffTicks` effleure la bande (r ≈ +0,023–0,029) — « POC au-dessus de la clôture → mouvement légèrement positif » (continuation faible) — mais :
- économiquement nul : la règle directionnelle dessus fait quand même hit 0,48–0,50, net −0,73 pt ;
- tercile non monotone / TRAIN-OOS instable (H=1 : bas −0,013, mid −0,120, haut +0,044 — le mid est *plus* négatif que le bas).
Blip statistique attendu sur 4 horizons × 7 features × 3 partitions.

### 3.3 Terciles — features signées → `fwdMove` signé

Points estimate ±0,1–0,3 pt, IC ±0,1–0,4, aucun gradient monotone stable, TRAIN/OOS fréquemment de signes opposés (ex. `buyVsSellPos` H=12 : bas +0,30 / mid −0,32 / haut +0,15 — forme en V, bruit). Rien.

### 3.4 Features non signées → `|fwdMove|` (volatilité, exploratoire — 3ᵉ apparition)

| feature | Pearson vs `|fwdMove|` (tous H) | terciles bas→haut (H=1) | ratio | stabilité |
|---|---|---|---|---|
| **`fpLevels`** | **+0,37 à +0,39** | 1,13 → 3,59 pt | **3,2×** | monotone, robuste, TRAIN ≈ OOS aux 4 H |
| **`top3Conc`** | **−0,32 à −0,35** | 3,51 → 1,12 pt | 3,1× | idem |
| **`pocConc`** | −0,29 à −0,32 | 3,50 → 1,13 pt | 3,1× | idem |
| `totalStacked` | +0,11 à +0,13 | 1,95 → 2,74 pt | faible | monotone, plus bruité |

**`fpLevels` (r ≈ +0,38) est le prédicteur univarié le plus fort de toute l'investigation QDE-012 → QDE-019.**

**MAIS — comme `deltaRange` en QDE-018 — c'est un ré-emballage de l'amplitude de barre réalisée.** `fpLevels` ≈ `(High − Low) / tick` = le range de la barre en ticks ; `pocConc`/`top3Conc` en sont l'inverse (barre serrée → peu de niveaux, volume concentré). Une barre à grand range a beaucoup de niveaux footprint ET est une barre haute-volatilité ET (persistance de volatilité) précède des mouvements plus grands. Le `σ` backward de production (`VolatilityModel.CurrentVolatility`, écart-type des 20 derniers rendements) capture déjà cette information. QDE-018 l'a démontré de façon décisive sur `deltaRange` (r −0,17 brut → corrélation partielle −0,04/−0,07 après contrôle du `σ`, R² incrémental 0,004) ; `fpLevels`/`pocConc` sont des fonctions encore plus directes du range → le contrôle serait une formalité au résultat prévisible.

---

## 4. Établi avec confiance

1. **Aucune structure footprint par barre n'a de pouvoir prédictif directionnel sur MES M5.** Les 7 features footprint signées (position du POC, POC vs clôture, imbalances diagonales empilées, skew d'agression, position achat vs vente, achat vs clôture) : hit rate ≤ ~0,50, espérance brute ≈ 0, espérance nette = −1 aller-retour robuste à tous les horizons 5–60 min. Corpus de 13 556 trades — puissance maximale.
2. **La relation de volatilité réapparaît une 3ᵉ fois et se renforce** (`fpLevels` r ≈ +0,38 avec `|fwdMove|`), mais reste un **proxy de l'amplitude de barre réalisée**, non incrémental sur le `σ` backward de production (établi par le contrôle de QDE-018 sur l'analogue `deltaRange`).

**QDE-019 ne confirme aucun edge.**

---

## 5. Exploratoire / non concluant

Rien de nouveau au-delà de la relation de volatilité, déjà classée non traçable (QDE-017) et non incrémentale (QDE-018).

---

## 6. Clôture de l'axe order flow et de l'investigation QDE-012 → QDE-019

**L'axe order flow est entièrement clos :**

| Lot | Résultat |
|---|---|
| QDE-016 | Backfill order-flow exploitable confirmé (~349 j MES M5). |
| QDE-017 | Delta agrégé par barre — **pas d'edge directionnel** ; découverte de la relation `deltaRange` → volatilité forward (exploratoire). |
| QDE-018 | La relation `deltaRange` **n'est pas incrémentale** sur le `σ` backward de production. |
| QDE-019 | Structure footprint par barre — **pas d'edge directionnel** ; la relation de volatilité réapparaît (proxy de range). |

**Conclusion de l'investigation complète (QDE-012 → QDE-019) : MES intraday (M5–H1) n'a AUCUNE structure directionnelle exploitable après coûts.**
- Prix seul : 7 vérifications convergentes (tranches R:R, split temporel, cross-instrument ES, cible multi-timeframe H1, pipeline complet M15/H1, seuil de déviation, dépendance sérielle brute). MeanReverting sans edge ; Trending artefact de régime.
- Order flow agrégé par barre (Delta) : pas d'edge directionnel.
- Structure footprint par barre : pas d'edge directionnel.
- Conditionnement cross-asset (VIX/taux/DXY) : n'améliore rien.

**Le seul signal prédictif robuste, stable hors échantillon et économiquement large jamais trouvé** est la persistance de volatilité réalisée (amplitude de barre → magnitude du prochain mouvement, r ≈ 0,3–0,4). Il est déjà capté par le système (`CurrentVolatility` pilote le stop), n'a pas de contenu directionnel, et n'a **aucune voie de monétisation** dans l'outillage actuel (pas d'historique d'options pour jouer la volatilité, aucun signal directionnel survivant à conditionner).

---

## 7. Recommandation

**Arrêt de l'axe recherche d'edge.** Le système reste en **SIGNAL_ONLY** — c'est le statut honnête : il ne trade pas parce qu'il n'a pas d'edge démontrable, et l'investigation exhaustive (8 lots, ~2 ans de données prix, ~1 an d'order flow, prix + Delta + footprint + cross-asset) confirme qu'il n'y en a pas à cette échelle avec ces sources.

Toute reprise future exigerait :
- une **source de données réellement différente** de ce qui est accessible gratuitement — order flow multi-instruments profond, données options historiques (payant : CBOE DataShop / ORATS), données alternatives ; ou
- un **marché / instrument / horizon fondamentalement autre** — daily+ (où le momentum time-series a un fondement littéraire), d'autres classes d'actifs, d'autres régimes de liquidité.

Chacune est un nouveau projet avec sa propre acquisition de données, pas une itération sur le système actuel.

**Ne PAS :** relancer un lot MeanReverting/Trending/order-flow fondé sur les données actuelles ; activer un gate de risque ou modifier le Risk Engine dans l'espoir d'un redressement ; sortir du statut SIGNAL_ONLY sans un edge validé OOS + après coûts issu d'une nouvelle source.

---

## 8. Interdictions respectées

- **Aucune modification de production.** Dépôt IQIA : 2 fichiers ajoutés sous `Tests/Research/OrderFlowFeasibility/` (lecteur + sonde). Hors dépôt : `OrderFlowFootprintExport.cs` dans le projet jetable `OrderFlowDepthProbe` (`.gitignore *`). `HistoricalBar`/`HistoricalSeries`/`CsvHistoricalBarSource`/`ScientificDatasetCollector`/`VolatilityStopLossModel`/`RiskEngine` : intouchés.
- **Aucune calibration, aucune Decision Rule modifiée, statut SIGNAL_ONLY inchangé.**

---

## 9. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-019_Footprint_Conditioning_Terminal.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/FootprintCsvBarSource.cs` | Lecteur CSV footprint, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/FootprintConditioningPilotTests.cs` | Sonde d'analyse, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/Output/footprint_conditioning_pilot.txt` | Sortie mesurée (preuve §3) | Recherche uniquement |
| `C:\Users\rnbch\OneDrive\Bureau\OrderFlowDepthProbe\OrderFlowFootprintExport.cs` *(hors dépôt)* | 3ᵉ indicateur jetable du DLL | **Jamais** |

Nettoyage complet de l'investigation order flow : `git clean -fd IQIAIndicator/Tests/Research/OrderFlowFeasibility/` ; supprimer `C:\Users\rnbch\OneDrive\Bureau\OrderFlowDepthProbe\` et `%APPDATA%\ATAS\Indicators\OrderFlowDepthProbe.dll`.

---

## 10. FINAL OUTPUT

**STATUS :** Test terminal. **Aucun edge directionnel dans la structure footprint par barre** sur MES M5 : les 7 features signées ont un hit rate ≤ ~0,50 et une espérance nette de −1 aller-retour robuste à tous les horizons (13 556 trades). La relation de volatilité réapparaît (`fpLevels` r ≈ +0,38 — le plus fort de l'investigation) mais reste un proxy de range non incrémental sur le `σ` de production. **L'axe order flow est clos ; l'investigation QDE-012 → QDE-019 est close : MES intraday M5–H1 n'a pas de structure directionnelle exploitable après coûts.**

**PRODUCTION MODIFIED :** NO. 2 fichiers de recherche dans le dépôt, 1 indicateur jetable hors dépôt. Aucun type de production touché.

**TESTS :** `FootprintConditioningPilotTests` — Passed (13 556 trades, 7 features × 4 horizons, terciles + Pearson + règle directionnelle).

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-019_Footprint_Conditioning_Terminal.md`.

**NEXT ACTION :** Arrêt de l'axe recherche d'edge. Système en SIGNAL_ONLY. Toute reprise = nouveau projet avec une source de données ou un marché/horizon fondamentalement différent, pas une itération sur le système actuel.
