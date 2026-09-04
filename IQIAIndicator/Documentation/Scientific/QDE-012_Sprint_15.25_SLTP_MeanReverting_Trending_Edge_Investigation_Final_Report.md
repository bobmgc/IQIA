# QDE-012 Sprint 15.25 — Investigation SL / TP / Edge : MeanReverting & Trending — Rapport final

**Date :** 2026-08-31
**Mode :** lecture seule / documentation. **Production modifiée : NON.**
**Périmètre :** MES (Micro E-mini S&P 500), timeframes M5 / M15 / H1, données Yahoo Finance continues (`MES=F`, `ES=F`).
**Sondes :** 8 fichiers de recherche isolés dans `IQIAIndicator/Tests/Research/StopLossContractAudit/` — aucune ne touche un type de production. Toutes conservées comme preuve vérifiable.

---

## 1. Résumé exécutif

### Symptôme de départ

L'audit fonctionnel du 2026-08-29 (QDE-012_Audit_2026-08-29) avait établi que le système ne tradait rien tant qu'aucun Stop Loss live n'existait. Une fois le SL câblé (`VolatilityStopLossModel`, distance = 2 × σ), l'observation initiale était : **« le stop placé selon la volatilité semble trop large »**, et la majorité des plans MeanReverting se retrouvaient bloqués sur un ratio reward/risk (R:R) très faible (~0,38 médian).

### Fil des hypothèses testées et éliminées, dans l'ordre

| # | Hypothèse | Verdict |
|---|---|---|
| a | Le contrat SL/TP/sizing contient un bug ou un plafond mal réglé | **Éliminée.** Formules propres, aucun cap, aucun rejet de sizing anormal. Le stop médian (17 ticks / 21 $) n'est pas « trop large » dans l'absolu ; il est large *relativement à une cible minuscule*. |
| b | Un seuil `MinRiskReward` bien choisi rend le book MeanReverting rentable | **Éliminée.** Aucune valeur dans [0 ; 1,5) ne rend le book net-positif après coûts réalistes. Zone brute-positive 0,5–1,0 → **−2,83 $/trade** net. |
| c | Le résultat est un artefact de l'instrument / de la fenêtre | **Éliminée.** ES=F (cross-instrument) et split temporel train/OOS : mêmes signes, mêmes ordres de grandeur, tous négatifs après coûts. |
| d | La cible (équilibre Kalman) est trop proche ; la calculer sur H1 l'élargit | **Éliminée.** Cible ×4 (médiane 1,3 → 5,2 pt), win rate ÷1,55 (74 % → 48 %), **expectancy inchangée** (−0,21 R vs −0,20 R). Gain × probabilité s'auto-compensent exactement. |
| e | Une cadence d'entrée plus lente (pipeline complet M15/H1) donne un edge | **Éliminée.** R:R médian reste **~0,38 invariant d'échelle** à M5, M15 et H1. Sur l'échantillon le plus puissant jamais mesuré (H1 natif, 720 jours, 2 565 trades) : expectancy **brute −0,031 R avec IC excluant 0** (négative *avant* coûts). |
| f | Filtrer les entrées sur un seuil de déviation `\|dev\|/σ ≥ x` (entrer « loin » comme un vrai signal mean-reversion) isole un edge | **Éliminée.** Aucun x ∈ {0,5 … 3,0} ne rend l'expectancy nette robustement positive. Le filtre fait passer le book de « robustement perdant » à « statistiquement neutre », jamais positif. |
| g | Trending (l'autre jambe) a un edge sur lequel se replier | **Non concluant / fragile.** M5 : brut **−0,24 R, IC excluant 0** (pire que MeanReverting). H1 natif : +0,14 R net global mais entièrement porté par la moitié OOS (+0,42 R, n=105) alors que TRAIN est négatif, sur 23 jours de signaux en 720. Signe qui s'inverse M5↔H1. |
| h | **Question de fond : MES a-t-il une dépendance sérielle exploitable dans ses rendements, indépendamment de tout modèle ?** | **NON.** Seule structure robuste : bid-ask bounce à M5 (non traçable, 6× trop petit). À M15 et H1, les rendements sont **statistiquement une marche aléatoire** (aucun ratio de variance significatif à aucun horizon, n jusqu'à 10 821). |

### Conclusion finale

**MES intraday sur M5–H1, sur ces fenêtres, ne contient pas de dépendance sérielle linéaire ni non-linéaire simple qui soit à la fois statistiquement robuste et économiquement significative après coûts.** Les huit vérifications convergent vers cette explication unique : ce n'est pas la conception d'un signal (seuil, cible, cadence, R:R, placement du stop) qui est en cause — c'est qu'il n'y a pas de structure prévisible dans la série de prix elle-même à ces échelles à exploiter.

### Implication pour la roadmap

- **NE PAS** construire l'ancre VWAP pour MeanReverting : le test de ratio de variance, agnostique de toute ancre, ne voit **aucune réversion multi-barres à aucun horizon** — prior fort qu'une ancre différente échouerait aussi.
- **NE PAS** calibrer `TimeSeriesMomentumModel` : aucune structure momentum (VR > 1 / autocorrélation positive) n'existe à calibrer.
- Toute piste légitime restante nécessite une **nouvelle source de données** (order flow, cross-asset, structure calendaire/événementielle, niveaux implicites options) ou un **horizon franchement plus long** (daily+), non testés ici — pas un ajustement du système actuel.

---

## 2. Chronologie complète des investigations

Toutes les mesures « coût base » utilisent le même modèle round-trip MES, quantité 1 : **spread 1 tick (1,25 $) + slippage 0,25 tick/jambe (0,625 $) + commission ~1,24 $ + frais exchange/reg ~0,74 $ = ~3,855 $** (≈ 0,77 point). Appliqué en post-traitement via `PositionCostCalculator` (Lot 14.7, inchangé) : `NetPnL = GrossPnL − TotalCost`. « R » = multiples de risque = P&L / (distance stop × PointValue × quantité), avec PointValue MES = 5 $/point.

---

### 2.a — Contrat SL / TP / Position Sizing

**Sonde :** `StopLossContractAuditTests.cs` → `Output/signals.csv`
**Hypothèse :** un bug ou un plafond mal réglé du contrat SL/TP/sizing explique le stop « trop large ».
**Méthode :** lecture du code de production + exécution du pipeline signal sur MES M5 59 j, capture par signal directionnel de la distance SL, distance TP, `CurrentVolatility`, régime de volatilité, R:R, et taille de position calculée avant arrondi.

#### Formules réelles (code de production, inchangé)

| Élément | Formule | Fichier | Calibré ? |
|---|---|---|---|
| **Stop Loss** | `distance = k × CurrentVolatility`, `k = 2.0`, prix de stop arrondi au tick **vers l'entrée** | `Engine/Risk/VolatilityStopLossModel.cs:37,76-96` | **NON CALIBRÉ** (« 2 écarts-types », convention, jamais optimisé) |
| `CurrentVolatility` (MeanReverting) | écart-type (population) des **20** dernières différences de clôture 1-barre, en points | `Engine/ScientificModels/Context/VolatilityModel.cs:43,204-209` | fenêtre 20 = convention |
| `CurrentVolatility` (Trending) | écart-type échantillon des **60** derniers log-retours × dernier prix | `Engine/ScientificModels/Trend/TimeSeriesMomentumModel.cs:79,159-164` | NON CALIBRÉ |
| **Take Profit (MeanReverting)** | `TP = EstimatedEquilibrium` (moyenne Kalman), utilisé seulement si du côté profitable — **niveau structurel, indépendant du SL** | `Engine/TradePlan/TradePlanBuilder.cs:68-84` | — |
| **Take Profit (Trending)** | `TP = Entry ± R × \|Entry − StopLoss\|`, `R = 2.0` codé en dur au point d'appel | `Engine/TradePlan/TradePlanBuilder.cs:90-108` ; `BacktestEngine.cs:390-394` ; `IQIAIndicator.cs:603-606` | NON CALIBRÉ |
| **Position sizing** | `contrats = floor(riskBudget / (stopPts × PointValue))`, arrondi au pas inférieur `QuantityStep`, clampé `[MinQuantity, MaxQuantity]`/`MaxPositionSize` | `Engine/Risk/RiskEngine.cs:179-215` (+ copie simplifiée `TradePlanBuilder.cs:110-146`) | — |

**Absence de cap :** recherche exhaustive dans `VolatilityStopLossModel`, `RiskEngine`, `TradePlanBuilder`, `PositionRiskEvaluator`, `RiskDistanceConfiguration`, `BacktestRiskConfiguration` — **aucun cap absolu (points/ticks/$) ni relatif (% prix, multiple ATR, fraction budget)** sur la distance de stop. Les seuls garde-fous : arrondi tick vers l'entrée (ne peut que *réduire* la distance) ; résultat dégénéré → `null` ; en aval, rejet si le budget ne couvre pas 1 `QuantityStep`.

**Comportement du sizing < 1 contrat :** `RiskEngine` (`RiskEngine.cs:190-199`) → `RiskRejectionReason.POSITION_SIZE_INVALID` ou `QUANTITY_LIMIT`, statut `REJECTED`. `TradePlanBuilder` (`TradePlanBuilder.cs:132-141`) → `positionSize` reste `null`, statut `SIGNAL_ONLY`. **Jamais d'arrondi à 1, jamais de trade sur une taille non résolue.**

#### Chiffres mesurés (MES M5, 59 j, 2 497 signaux directionnels : 2 294 MeanReverting + 203 Trending)

**Distribution de la distance de stop (points / ticks / $ par contrat) :**

| | min | p50 | p90 | p99 | max | moyenne |
|---|---|---|---|---|---|---|
| points | 1,25 | 4,25 | 9,75 | 18,00 | 19,50 | 5,22 |
| ticks | 5 | 17 | 39 | 72 | 78 | 20,9 |
| $ / contrat | 6,25 | 21,25 | 48,75 | 90,00 | 97,50 | 26,1 |

Par régime : MeanReverting p50 4,25 / p90 9,50 / p99 13,27 / max 19,50 ; Trending p50 6,25 / p90 18,00 (σ 60 barres). `CurrentVolatility` brute max = 9,84 pt → le « plafond » apparent à 19,5 pt n'est **pas** un cap code, c'est `2 × max(σ)` sur une fenêtre calme (2 signaux seulement à 19,5).

**Vérification de la formule :** `corr(distanceSL, CurrentVolatility) = 0,9997` ; `moyenne(distanceSL / CurrentVolatility) = 1,94` (≤ 2 à cause de l'arrondi). Stop = 2σ quasi-déterministe, sans cap ni plancher au-delà d'un tick.

**Stops « anormalement larges » :** au-delà du p90 (9,75 pt) : 227 / 2 497 = **9,1 %** ; au-delà de 2× la médiane : 14,0 % ; au-delà de 3× : 2,4 %. **84 % des stops extrêmes sont sur des barres `VolatilityRegime = HIGH`** (191/227), 0 sur LOW/MEDIUM.

**Sizing < 1 contrat :** `contrats_bruts < 1` sur **0 / 2 497 = 0,0 %** à budget 1 % (250 $) *et* 2 % (500 $). Minimum de contrats bruts = 2,56 (1 %). Le risque $/contrat (6–98 $, max 0,39 % de l'équité) est trop petit devant le budget pour que le sizing bloque jamais.

**R:R :** médiane 0,365 ; **88,1 % des signaux ont R:R < 1,0** (96 % pour MeanReverting) ; Trending R:R = 2,000 par construction.

**Verdict 2.a :** le contrat est propre. Le stop n'est pas « trop large » dans l'absolu et ne bloque jamais le sizing. Le déséquilibre est du côté de la **cible** (équilibre Kalman souvent minuscule), pas du stop. Le vrai goulot est le R:R.

---

### 2.b — MeanReverting : expectancy par tranche de R:R (coûts, IC, split train/OOS)

**Sondes :** `MeanRevertingExpectancyAuditTests.cs` (brut, coûts désactivés) → `Output/mr_trades.csv`, `summary.txt` ; puis `MinRiskRewardThresholdValidationTests.cs` (coûts réalistes) → `Output/threshold_validation.txt`
**Hypothèse :** un seuil `MinRiskReward` (candidat ~0,5, suggéré par les données brutes) rend le book MeanReverting rentable.

**Phase 0 (configuration production) :** `MinRiskReward` est **désactivé (null) partout** — `IQIAIndicator.cs:264` (`= 0.0` → null via `RiskPolicyFactory`), et chaque scénario de backtest passe `null`. Résultat : **la totalité des 2 294 signaux directionnels MeanReverting atteignent `PLAN_READY` → 2 294 trades `Closed`** (0 `SIGNAL_ONLY`, 0 `PLAN_REJECTED`).

#### Brut (coûts désactivés), durée de détention médiane = 0 barre

| tranche R:R | % volume | expectancy brute $/trade |
|---|---|---|
| globale | 100 % | **−0,114** |
| R:R < 0,5 | 71 % | **−0,52** |
| 0,5 ≤ R:R < 1,0 | 25 % | **+1,14** |

#### Coûts réalistes — MES M5, 59 j (n = 2 293)

Scénarios : optimiste ~1,87 $ RT, **base ~3,86 $ RT**, pessimiste ~4,48 $ RT.

| tranche | n | win rate | gain moy. brut | perte moy. brute | exp brute $/tr | **exp base $/tr** | exp brute R | **exp base R** |
|---|---|---|---|---|---|---|---|---|
| R:R < 0,5 | 1 622 | 0,800 | +5,51 | −24,59 | −0,52 | **−4,37** | −0,020 | −0,226 |
| 0,5 ≤ R:R < 1,0 | 576 | 0,625 | +15,63 | −23,31 | **+1,03** | **−2,83** | +0,060 | −0,154 |
| R:R ≥ 1,0 | 95 | 0,505 | +24,28 | −26,46 | −0,82 | **−4,68** | +0,090 | −0,115 |

Sorties : TakeProfit 8,16 $ brut → **+4,31 $ net base** ; StopLoss −25,34 → **−29,19** ; Ambiguous −18,54 → −22,40 ; TimeHorizon −10,91 → −14,76.

**Cumulatif « R:R ≥ x » (coût base) — négatif pour tout x de 0,0 à 1,4 :**

| x | n | exp base $/tr (IC 95 %) | exp base R/tr (IC 95 %) |
|---|---|---|---|
| 0,0 | 2 293 | −4,00 ± 0,72 | −0,203 ± 0,026 |
| 0,4 | 943 | −2,92 ± 1,38 | −0,153 ± 0,051 |
| 0,5 | 671 | −3,09 ± 1,75 | −0,148 ± 0,064 |
| 0,9 | 144 | −3,35 ± 4,42 | −0,066 ± 0,167 (le moins négatif) |
| 1,0 | 95 | −4,68 ± 5,78 | −0,115 ± 0,216 |
| 1,4 | 12 | −7,23 ± 21,65 | −0,298 ± 0,752 |

**Grille fine 0,1 (coût base, n ≥ 30) :** toutes les bandes < 0,5 sont **robustement net-négatives** (IC entièrement < 0). La meilleure, [0,5–0,6), donne **−1,55 $ ± 2,81** (chevauche 0, point estimate négatif). Aucune bande n'est net-positive robuste.

**Simulation du gate `MinRiskReward` → `PLAN_REJECTED` :** seuil 1,0 → 88,1 % rejetés ; 1,5 → 91,7 % ; 2,0 → 91,8 %.

**Split temporel MES (train bar < 6 441, purge 100, OOS bar ≥ 6 641) :**

| | R:R < 0,5 | 0,5 ≤ R:R < 1,0 | R:R ≥ 1,0 |
|---|---|---|---|
| TRAIN (n=1 394) exp base $/tr | −5,21 | −2,46 | −1,94 |
| OOS (n=873) exp base $/tr | −3,04 | −3,43 | **−9,55** |

**Verdict 2.b :** le seuil candidat ~0,5 ne survit pas. Aucune valeur de `MinRiskReward` dans [0 ; 1,5) ne rend le book net-positif. Le déséquilibre est structurel : edge brut ≤ +0,02 R (bande 0,5–1,0) contre coût round-trip ~0,05–0,10 R pour un stop de largeur médiane.

---

### 2.c — MeanReverting : stabilité cross-instrument (ES=F)

**Sonde :** `MinRiskRewardThresholdValidationTests.cs` (section ES) → `Output/threshold_validation.txt`
**Hypothèse :** le résultat MES est un artefact de l'instrument ou de la fenêtre.
**Méthode :** `RunFullBacktest` sur `ES=F` M5 59 j (même indice, price-équivalent, PointValue 50 $), mêmes tranches R:R, coût base scalé par PointValue, expectancy exprimée en R (comparable à MES).

| tranche | n | win rate | **exp brute R** | **exp base R** |
|---|---|---|---|---|
| R:R < 0,5 | 1 569 | 0,791 | −0,032 | **−0,139** |
| 0,5 ≤ R:R < 1,0 | 578 | 0,592 | +0,006 | **−0,108** |
| R:R ≥ 1,0 | 105 | 0,514 | +0,099 | **−0,014** |

Même forme que MES (de moins en moins négatif quand R:R monte), **négatif partout après coûts**. Cumulatif « R:R ≥ 1,0 » = −0,014 R (n=105, IC ±0,204) — le plus proche de l'équilibre observé, mais négatif et n petit. Grille fine : aucune bande n ≥ 30 robustement positive.

**Verdict 2.c :** pas un artefact d'instrument. ES=F reproduit MES : négatif après coûts à toutes les tranches.

---

### 2.d — MeanReverting : cible multi-timeframe (équilibre H1, entrée M5 inchangée)

**Sonde :** `MultiTimeframeEquilibriumAuditTests.cs` → `Output/mtf_equilibrium.txt`
**Hypothèse :** la cible (équilibre Kalman calculé sur 20 barres M5 = ~100 min) est trop proche du coût ; la recalculer sur H1 (20 h) l'élargit sans élargir le risque.

**Phase 0 anti-look-ahead :** `KalmanFilterModel` est **sans état** (`KalmanFilterModel.cs:27-269`) — recalculé de zéro sur les 20 dernières clôtures à chaque appel, aucun état incrémental. La substitution « 20 clôtures M5 → 20 dernières clôtures H1 complètes » est donc sans contamination. Règle H1 sûre : un bucket `[H, H+1h)` est utilisable à la clôture de la barre M5 k **si et seulement si** `H + 1h ≤ bars[k].Timestamp + 5min` (l'heure entière est passée). Self-check : re-simuler la cible M5 via le moteur d'exécution du probe reproduit le P&L de `RunFullBacktest` à **0,0000 $** près.

**Résultats (MES M5, 59 j, coût base) :**

| | M5 (cible actuelle) | H1 (cible testée) |
|---|---|---|
| signaux/jour | 55,6 (cadence inchangée — confirmé) | idem |
| consistance cible (équilibre du bon côté de l'entrée) | **100 %** (par construction : le signal *est* « le prix a dévié de l'équilibre M5 ») | **72,3 %** (632/2 280 deviennent `SIGNAL_ONLY`) |
| distance TP médiane / p90 | 1,31 / 4,22 pt | **5,24 / 14,63 pt** (×4) |
| R:R médian | 0,328 | **1,209** |
| % R:R ≥ 0,5 | 29,2 % | 79,3 % |

**Sorties (cible H1) :** TakeProfit 704 (43 %) net **+16,50 $** ; StopLoss 675 (41 %) net **−27,80 $** (SL inchangé) ; TimeHorizon 251 (15 %) net +4,42 ; Ambiguous 18 net −24,62.

**Expectancy nette de coût base par tranche R:R (cible H1) :**

| tranche | n | win rate | exp brute $/tr | **exp nette $/tr** | **exp nette R (IC 95 %)** |
|---|---|---|---|---|---|
| R:R < 0,5 | 341 | 0,484 | −1,57 | −5,42 | −0,220 ± 0,058 |
| 0,5 ≤ R:R < 1,0 | 368 | 0,606 | −0,18 | −4,04 | −0,112 ± 0,085 |
| 1,0 ≤ R:R < 2,0 | 506 | 0,466 | **+3,07** | **−0,79** | −0,083 ± 0,097 |
| R:R ≥ 2,0 | 433 | 0,286 | −2,50 | −6,36 | −0,439 ± 0,117 |

**Cumulatif « R:R ≥ x » (cible H1, coût base) — négatif pour tout x de 0,0 à 2,4** ; le moins négatif vers x ≈ 0,8 (−0,222 R), se dégradant au-delà de x = 1,0.

**Book entier :** cible M5 −4,00 $/trade (−0,203 R, n≈2 293) ; cible H1 **−3,94 $/trade (−0,211 R, n=1 648 tradeable)** — **statistiquement inchangé**.

**Verdict 2.d :** la distance à la cible n'est pas le facteur limitant. L'élargir ×4 multiplie les gains par ~4 (+16,50 $ vs +4,31 $) mais divise le win rate par ~1,55 (74 % → 48 %) : effet net nul. Le win rate reste **mécanique** (durée = 0 persiste), pas directionnel.

---

### 2.e — MeanReverting : pipeline complet M15 / H1 (entrée + régime recalculés)

**Sonde :** `SlowTimeframePipelineAuditTests.cs` → `Output/slow_timeframe_pipeline.txt`
**Hypothèse :** le signal d'entrée lui-même (« déviation par rapport à l'équilibre ») a un edge à une cadence plus lente, où le bruit de microstructure M5 pèse moins.
**Méthode :** `RunFullBacktest` (inchangé) sur barres M5 ré-agrégées en M15 et H1, plus **H1 natif 720 jours** (pull `1h` via `HttpYahooChartClient` interne). Aucune constante de fenêtre modifiée.

**Phase 0 — fenêtres exprimées en nombre de barres (non ajustées, documentées) :** contexte scientifique 500 clôtures ; `RegimeEngine._priceBuffer` = 128 (plafond dur de l'historique de régime) ; ADF/KPSS 60, DFA/Bai-Perron 128, Half-Life/VR/CUSUM/Hurst/Volatility-evidence 30, Kalman/VolatilityModel 20 ; FusionStateManager EMA α = 0,20, hystérésis 0,03 ; horizon d'exécution 10. À H1, ces durées valent ~12× celles de M5 (stop = 2σ sur 20 h, régime décidé sur ≤ 128 h).

**Phase 3 — look-ahead :** mécanisme d'exécution (fill à `bars[k+1].Open`, surveillance SL/TP sur `bars[k+1 … k+1+Horizon]`) indépendant du timeframe, reste causal. « Durée = 0 barre » = résolution dans la 1ʳᵉ barre après l'entrée : ~5 min (M5), ~15 min (M15), **jusqu'à ~1 h (H1)**.

#### MeanReverting — comparaison directe

| métrique | **M5** (59 j) | **M15** (59 j) | **H1 agrégé** (59 j) | **H1 natif** (720 j) |
|---|---|---|---|---|
| signaux/jour | ~39 | 17,1 | 9,2 | 8,3 |
| n trades Closed | 2 293 | 700 | 210 | **2 565** |
| SL médiane / p90 (pt) | 4,25 / 9,75 | 7,5 / 16,3 | 24,0 / 36,3 | 19,8 / 38,1 |
| TP médiane / p90 (pt) | 1,3 / 4,2 | 2,25 / 7,6 | 7,0 / 19,6 | 5,4 / 19,0 |
| **R:R médian** | **0,33** | **0,38** | **0,39** | **0,38** |
| durée détention médiane | 0 | 0 | 1 | **0** |
| **% durée == 0** | ~50 % | **53,9 %** | 48,6 % | **52,6 %** |
| win rate brut | 0,74 | 0,730 | 0,710 | 0,722 |
| seuil d'équilibre 1/(1+R:R) | ~0,75 | 0,724 | 0,719 | 0,723 |
| écart au seuil | −0,01 | +0,006 | −0,009 | **−0,001** |
| **exp BRUTE (R, IC 95 %)** | ~−0,02 | −0,026 ± 0,046 | −0,049 ± 0,083 | **−0,031 ± 0,024 (exclut 0)** |
| **exp NETTE (R, IC 95 %)** | −0,20 | −0,138 ± 0,046 | −0,087 ± 0,083 | **−0,076 ± 0,024** |

Sorties (H1 natif) : TakeProfit 70,6 %, StopLoss 21,2 %, TimeHorizon 5,1 %, Ambiguous 3,2 % — structure identique à M5/M15.

**Split temporel MeanReverting :**

| | M15 TRAIN (385) | M15 OOS (312) | H1 natif TRAIN (1 553) | H1 natif OOS (1 012) |
|---|---|---|---|---|
| exp brute R (IC) | +0,011 ± 0,063 | −0,070 ± 0,066 | **−0,055 ± 0,031 (exclut 0)** | +0,007 ± 0,036 (chevauche 0) |
| exp nette R (IC) | −0,087 ± 0,063 | **−0,200 ± 0,068** | **−0,105 ± 0,031** | −0,032 ± 0,036 |

**Verdict 2.e :** MeanReverting reste sans edge net de coûts aux deux nouvelles cadences. Le R:R médian ~0,38 est **invariant d'échelle** — le déséquilibre (cible minuscule car le signal se déclenche sur petites déviations ; stop = 2σ) se reproduit identiquement. Réajuster les fenêtres ne changerait pas cette géométrie. **La piste « cadence » est épuisée.**

---

### 2.f — MeanReverting : filtre par seuil de déviation `|dev|/σ` (post-hoc)

**Sonde :** `MeanRevertingDeviationThresholdAuditTests.cs` → `Output/deviation_threshold.txt`
**Hypothèse :** ne garder que les entrées où le prix a suffisamment dévié de l'équilibre (`|dev|/σ ≥ x`, x ∈ {0,5 … 3,0}) isole un vrai signal mean-reversion « manuel de texte ».

**Phase 0 :** la production **n'a aucun seuil de magnitude** — `EntryTriggerBuilder.DetermineDirection` (`EntryTriggerBuilder.cs:236-254`) émet un candidat directionnel MeanReverting sur `sign(DynamicZScore)` seul (`zScore != 0`), gaté uniquement par la porte d'ambiguïté régime (`AmbiguityGateThreshold = 0.95`, explicitement **Provisional**) et un plancher `_scientificConfidence ≥ 0.70`. `DynamicZScore = (prix − moyenne Kalman) / InnovationStd` (InnovationStd de Kalman, *pas* `CurrentVolatility` qui pilote le SL).

**Phase 1 — distribution de la déviation-à-l'entrée en σ** (contrôle : `mean(dev/σ) = 0,761 ≈ mean(2·R:R) = 0,767` ; `SL/σ ≈ 1,986`) :

| | H1 natif (n=2 565) | M5 (n=2 307) |
|---|---|---|
| p25 | 0,26 | 0,29 |
| **médiane** | **0,57** | **0,64** |
| p75 | 1,07 | 1,05 |
| p90 | 1,68 | 1,54 |
| % ≥ 2σ | **6,6 %** | **3,3 %** |

La moitié des signaux se déclenchent sous 0,6σ. Confirmation directe : sans seuil, le signal se déclenche sur des écarts modestes, pas des extrêmes.

**Phase 2 — filtre `|dev|/σ ≥ x` sur H1 natif (coût base) :**

| x | n | %conservé | méd R:R | winRate | **exp nette R/tr (IC 95 %)** | verdict |
|---|---|---|---|---|---|---|
| 0,0 | 2 565 | 100 % | 0,29 | 0,649 | **−0,076 ± 0,024** | net− robuste |
| 0,5 | 1 406 | 55 % | 0,51 | 0,629 | −0,049 ± 0,039 | chevauche 0 |
| 1,0 | 723 | 28 % | 0,74 | 0,537 | −0,062 ± 0,063 | chevauche 0 |
| 1,5 | 354 | 14 % | 1,00 | 0,517 | −0,011 ± 0,100 | chevauche 0 |
| 2,0 | 169 | 6,6 % | 1,22 | 0,515 | +0,041 ± 0,155 | chevauche 0 |
| 2,5 | 69 | 2,7 % | 1,43 | 0,449 | −0,057 ± 0,247 | chevauche 0 |
| 3,0 | 24 | 0,9 % | 1,62 | 0,500 | +0,007 ± 0,395 | n<30 |

**Train/OOS (H1 natif) :** aucun x où TRAIN *et* OOS sont robustement positifs (à x=2,0 : TRAIN +0,030 ± 0,201, OOS +0,060 ± 0,244 — non concluant). Grille fine dev/σ **non monotone** : bande [1,25–1,50) robustement *négative* entre des voisines qui chevauchent 0 — signature de bruit, pas de signal. M5 : robustement négatif jusqu'à x=1,5 ; OOS négatif à *chaque* x.

**Verdict 2.f :** aucun seuil de déviation ne rend MeanReverting robustement net-positif. Le filtre fait passer le book de « robustement perdant » à « statistiquement neutre » — jamais positif. Même les entrées ≥ 2,5–3σ (les plus « manuel de texte ») ne montrent aucun edge. **Le problème n'est pas le seuil ; c'est la cible** (moyenne Kalman adaptative sur 20 barres, qui suit le prix de trop près pour être un ancrage de réversion).

---

### 2.g — Trending : expectancy nette (fragile, non concluant)

**Sondes :** `TrendingExpectancyAuditTests.cs` (M5) → `Output/trending_expectancy.txt` ; section Trending de `SlowTimeframePipelineAuditTests.cs` (M15, H1)
**Hypothèse :** Trending (TP = 2× distance SL, R:R fixe 2,0) a un edge sur lequel se concentrer pendant que MeanReverting est en pause.

**Phase 0 :** R:R = 2,0 pour tous les trades Trending → seuil d'équilibre théorique 1/(1+2,0) = **33,3 %**.

| métrique | **M5** (n=221) | **M15** (n=53) | **H1 natif** (n=209) |
|---|---|---|---|
| jours avec signal Trending | (peu) | 6 | **23** / 720 cal |
| durée détention médiane / % ==0 | — | 10 / 7,5 % | **9 / 8,6 %** (pas d'artefact durée=0) |
| win rate brut | 0,312 | 0,453 | 0,502 |
| écart au seuil 33,3 % | −0,021 | +0,119 | +0,169 |
| sorties TP / SL / TimeHorizon | 11 / 50 / 39 % | 13 / 38 / 49 % | 17 / 38 / 45 % |
| **exp BRUTE (R, IC 95 %)** | **−0,241 ± 0,133 (exclut 0)** | +0,018 ± 0,284 | **+0,181 ± 0,156** |
| **exp NETTE (R, IC 95 %)** | −0,376 ± 0,134 | −0,055 ± 0,284 | **+0,141 ± 0,157** |

**Split temporel (H1 natif, seul échantillon exploitable) :**

| | TRAIN (104) | OOS (105) |
|---|---|---|
| win rate brut | 0,423 | 0,581 |
| exp brute R (IC) | −0,103 ± 0,190 (point **négatif**) | +0,461 ± 0,236 |
| **exp nette R (IC)** | −0,144 ± 0,191 | **+0,423 ± 0,236 → IC [+0,187 ; +0,659], exclut 0** |

**Verdict 2.g :** résultat **non robuste**. Le +0,14 R net global H1 est entièrement porté par la moitié OOS (~2026) ; TRAIN (~2025) est négatif. Signe inversé entre M5 (−0,24 R brut, IC excluant 0) et H1 (+0,18 R). 209 trades sur 23 jours seulement en 720. Impossible de conclure à un edge. Caveat : `TimeSeriesMomentumModel` est très récent (P0-2, 2026-08-30) et jamais calibré.

---

### 2.h — Étude de dépendance sérielle MES (la conclusion de fond)

**Sonde :** `MesSerialDependenceAuditTests.cs` → `Output/serial_dependence.txt`
**Hypothèse :** avant d'investir dans un nouveau modèle, MES a-t-il une dépendance sérielle exploitable dans ses rendements à M5/M15/H1, indépendamment de toute construction de signal ?
**Méthode :** log-retours, barres consécutives contiguës uniquement (écart ≤ 1,5× l'intervalle → élimine sauts overnight/week-end/pause CME). ACF avec IC Bartlett 95 %. Ratio de variance Lo-MacKinlay via `VarianceRatioStatistics.Compute` **de production, réutilisé sans modification**. Runs test Wald-Wolfowitz sur le signe des rendements.

#### Autocorrélation (IC Bartlett 95 %)

| TF | n | lag 1 | lag 2 | lag 3 | lag 5 | lag 10 | lag 20 |
|---|---|---|---|---|---|---|---|
| **M5** (59 j) | 11 175 | **−0,040** ✔ réversion | **+0,035** ✔ momentum | +0,013 | +0,006 | +0,006 | +0,015 |
| **M15** (59 j) | 3 729 | **+0,038** ✔ momentum | +0,004 | −0,024 | −0,017 | +0,030 | +0,015 |
| **H1 natif** (720 j) | 10 821 | **−0,037** ✔ réversion | −0,001 | −0,012 | −0,008 | +0,031 (limite) | −0,025 (limite) |

✔ = |ρ| > bande. Le M5 lag-1 négatif + lag-2 positif = signature du **bid-ask bounce** (modèle de Roll). Signe qui s'inverse entre M5 (−) et M15 (+).

#### Ratio de variance Lo-MacKinlay (VR < 1 → réversion, VR > 1 → momentum)

| TF | VR(2) | VR(3) | VR(5) | VR(10) | VR(20) |
|---|---|---|---|---|---|
| **M5** FULL | 0,960, p=**0,018** ✔ | 0,971, p=0,23 | 0,997, p=0,93 | 1,024, p=0,66 | 0,984, p=0,84 |
| **M15** FULL | 1,039, p=0,25 | 1,055, p=0,27 | 1,031, p=0,65 | 0,969, p=0,75 | 0,905, p=0,46 |
| **H1** FULL | 0,964, p=0,15 | 0,951, p=0,22 | 0,933, p=0,28 | 0,912, p=0,34 | 0,890, p=0,36 |

**Seule significativité : M5 VR(2)** (horizon 10 min → microstructure). À M15 et H1, **aucun VR significatif à aucun horizon** sur jusqu'à 10 821 observations → rendements ≈ marche aléatoire aux horizons traçables.

#### Runs test (signe des rendements)

| TF | Z | p | verdict |
|---|---|---|---|
| **M5** | +3,52 | **0,0004** ✔ | alternance de signe (bid-ask bounce). Tient en TRAIN (p=0,04) et OOS (p=0,002) |
| **M15** | +1,46 | 0,14 | non significatif |
| **H1** | −0,20 | 0,84 | non significatif |

#### Significativité économique

| TF | ρ₁ | mouvement prédictible barre suivante | coût RT | **ratio mvt/coût** | edge en R | coût en R |
|---|---|---|---|---|---|---|
| M5 | −0,040 | 0,122 pt | 0,77 pt | **0,16** | 0,020 R | 0,126 R |
| M15 | +0,038 | 0,204 pt | 0,77 pt | **0,27** | 0,019 R | 0,073 R |
| H1 | −0,037 | 0,497 pt | 0,77 pt | **0,65** | 0,018 R | 0,028 R |

À toutes les cadences, la composante prédictible du prochain mouvement est **une fraction d'un seul coût round-trip** (0,16× à 0,65×). Part de variance réellement prédictible : ρ₁² ≈ 0,14 %.

#### Stabilité train/OOS

| TF, mesure | FULL | TRAIN (60 %) | OOS (40 %) | stable ? |
|---|---|---|---|---|
| M5 lag-1 | −0,040 ✔ | −0,024 (non sig) | −0,089 ✔ | signe oui, magnitude ×2–4 |
| M5 VR(2) | p=0,018 ✔ | p=0,26 (non sig) | p=0,0003 ✔✔ | non — invisible en TRAIN |
| **M15 lag-1** | +0,038 ✔ | +0,058 ✔ | **−0,024 (signe INVERSÉ, non sig)** | **non — faux positif** |
| H1 lag-1 | −0,037 ✔ | −0,043 ✔ | −0,021 (non sig) | signe oui, OOS non sig, magnitude ÷2 |
| H1 VR (tous q) | non sig | non sig | non sig | — |

**Verdict 2.h :** MES n'a aucune dépendance sérielle à la fois (a) significative, (b) de signe stable entre timeframes, (c) de signe stable train→OOS, ET (d) économiquement plus grande que le coût. La seule structure robuste (M5 lag-1 + VR(2) + runs) est le **bid-ask bounce** : non traçable (le capturer = payer le spread qui le crée), ~6× trop petit, absent à M15/H1.

---

## 3. Synthèse des causes profondes

**Pourquoi chaque piste a échoué — et pourquoi elles convergent :**

1. **Le contrat SL/TP/sizing est correct (2.a).** Le stop 2σ n'est pas « trop large » ; il est large *relativement à une cible minuscule*. Le sizing ne bloque jamais. Aucun cap manquant.

2. **Le R:R faible (~0,38) n'est pas un paramètre mal réglé — c'est la géométrie inhérente du signal (2.b, 2.e).** Le signal MeanReverting se déclenche sur `sign(DynamicZScore)` dès que le prix quitte l'équilibre Kalman (aucun seuil, 2.f). L'équilibre étant une moyenne adaptative sur 20 barres, il « poursuit » le prix : au moment où une déviation est mesurée, la cible (= cette déviation) est petite, tandis que le stop = 2σ est large. Ce rapport ~0,38 est **invariant d'échelle** : il se reproduit identiquement à M5, M15 et H1.

3. **Le win rate de ~74 % est mécanique, pas directionnel (2.d, 2.e).** Une cible très proche est touchée par le bruit intra-barre avant le stop — d'où « durée de détention = 0 » sur ~53 % des trades à *toutes* les cadences (y compris H1, où « 0 barre » vaut jusqu'à 1 h). Élargir la cible (2.d) fait chuter le win rate exactement dans la proportion inverse du gain de taille → expectancy inchangée.

4. **Filtrer sur la magnitude de la déviation ne crée pas d'information (2.f).** Isoler les entrées ≥ 2–3σ retire la masse robustement perdante mais laisse un résidu *statistiquement neutre* — le prix ne revient pas plus vers l'équilibre Kalman après une grande déviation qu'après une petite, net de coûts.

5. **Trending partage le problème (2.g).** Pas d'edge brut sur M5 (−0,24 R, IC excluant 0), résultat H1 fragile et non reproductible (porté par une seule moitié OOS sur 23 jours de signaux).

6. **La cause commune est dans les données, pas dans les modèles (2.h).** L'étude de dépendance sérielle, indépendante de tout modèle IQIA, montre que les rendements MES à M15 et H1 sont **statistiquement une marche aléatoire** (aucun ratio de variance significatif à aucun horizon). La seule structure existante — le bid-ask bounce à M5 — n'est ni traçable ni économiquement suffisante. **Tout modèle directionnel fondé sur le seul historique prix/rendement construit une prédiction là où il n'y a rien à prédire.** Chaque piste des sections 2.a–2.g a échoué parce qu'elle essayait d'extraire un edge d'une série qui n'en contient pas à ces échelles.

---

## 4. Ce qui est maintenant établi avec un haut niveau de confiance

Huit vérifications indépendantes, convergentes :

1. Le contrat SL/TP/sizing est propre : SL = 2σ sans cap, TP structurel (équilibre Kalman) ou 2R, sizing `floor(budget/riskParUnit)` qui rejette (jamais n'arrondit) sous 1 contrat. **Le sizing ne bloque jamais** pour MES à 25 000 $ (0 % des signaux sous 1 contrat).
2. Le book MeanReverting M5 **n'a pas d'edge net de coûts réalistes** : expectancy globale −0,20 R nette (−4,00 $/trade), négative sur toutes les tranches de R:R, y compris la seule brute-positive (0,5–1,0 : +1,03 $ brut → −2,83 $ net).
3. **Aucune valeur de `MinRiskReward` dans [0 ; 1,5)** ne rend le book net-positif. Le candidat ~0,5 échoue.
4. Le résultat **n'est pas un artefact d'instrument** : ES=F reproduit MES (négatif après coûts à toutes les tranches).
5. **Élargir la cible seule ne change rien** : cible H1 ×4, win rate ÷1,55, expectancy nette du book identique (−0,211 R vs −0,203 R).
6. Le R:R médian est **~0,38 invariant d'échelle** (M5, M15, H1). Sur l'échantillon le plus puissant (H1 natif, 2 565 trades, 720 jours), l'expectancy **brute** MeanReverting est **−0,031 R avec IC excluant 0** — négative avant même les coûts.
7. **Aucun seuil de déviation `|dev|/σ` ≤ 3σ** ne rend MeanReverting robustement net-positif ; le filtre atteint au mieux la neutralité statistique.
8. Les rendements MES à **M15 et H1 sont statistiquement une marche aléatoire** : aucun ratio de variance Lo-MacKinlay significatif à aucun horizon q ∈ {2,3,5,10,20}, runs test non significatif. La seule dépendance robuste (M5 bid-ask bounce) est non traçable et ~6× sous le coût.

**Conséquence factuelle :** sur MES intraday M5–H1, sur les fenêtres testées (M5/M15 : 59 jours ; H1 : 720 jours), aucun modèle directionnel keyé sur le seul historique prix/rendement — quels que soient le seuil d'entrée, la cible, la cadence, le R:R ou le placement du stop — ne produit un edge net des coûts futures retail.

---

## 5. Ce qui reste incertain / non testé

- **Trending sous une forme correctement calibrée avec un échantillon plus large.** Le seul résultat positif (H1 natif OOS : +0,42 R net, IC excluant 0, n=105) est fragile : moitié OOS uniquement, 23 jours de signaux, signe inversé vs M5, modèle non calibré. Un vrai lot de calibration walk-forward pour `TimeSeriesMomentumModel` (lookbacks, TStatScale, seuils de régime, `MinMomentumConfidence`) sur un panier multi-instruments et un historique plus long serait nécessaire pour distinguer un edge d'une chance de petit échantillon. **Non fait ici.**
- **Tout horizon daily+.** L'étude de dépendance sérielle s'arrête à H1 (limite pratique : Yahoo `1h` ≈ 720 j ; `5m`/`15m` ≈ 60 j). Le momentum time-series de la littérature (Moskowitz et al. 2012) opère à l'échelle mensuelle, pas horaire. Non testé.
- **Toute source d'information hors de la série de prix :** order flow / footprint, données cross-asset (VIX, taux, corrélations sectorielles), structure calendaire et événementielle (ouvertures de séance, annonces macro), niveaux implicites options (gamma, max pain, murs de strikes). Aucune n'a été évaluée.
- **Régimes de marché non couverts par la fenêtre.** H1 natif = 2024-09 à 2026-08, marché globalement haussier de l'indice. Un régime de forte volatilité / krach pourrait modifier les conclusions (dans un sens ou l'autre).

---

## 6. Recommandations explicites

1. **NE PAS construire l'ancre VWAP pour MeanReverting.** Le test de ratio de variance Lo-MacKinlay, **agnostique de toute ancre**, ne détecte aucune réversion multi-barres (q ≥ 3) à aucun timeframe entre M5 et H1. Une ancre VWAP est simplement une autre cible de réversion ; s'il n'y a pas de réversion dans les rendements à exploiter, le choix de l'ancre est indifférent. Prior fort d'échec — ne pas investir avant d'avoir une raison indépendante de croire que la réversion existe à une échelle donnée.

2. **NE PAS calibrer `TimeSeriesMomentumModel` en l'état.** Aucune structure momentum (VR > 1 ou autocorrélation positive robuste) n'a été trouvée à calibrer entre M5 et H1. La calibration optimiserait des paramètres autour d'un signal qui n'a pas de fondement statistique à ces cadences. Exception : si l'on décide d'explorer l'horizon daily+ (voir §5), un TSMOM y a un fondement littéraire — mais c'est un projet distinct avec sa propre acquisition de données.

3. **NE PAS ajuster davantage le système actuel** (seuils d'entrée, multiplicateurs de stop, R:R minimum, fenêtres de modèle, poids de fusion). Six analyses convergentes montrent que le goulot n'est aucun de ces paramètres.

4. **Les pistes légitimes restantes nécessitent une nouvelle source de données, pas un réglage :**
   - order flow / cross-asset / calendaire / options-implied à l'échelle intraday ;
   - ou un modèle directionnel à horizon daily+ avec sa propre acquisition d'historique long.
   Chacune est un nouveau projet de recherche, pas une itération sur MeanReverting/Trending.

5. **Maintenir le système en `SIGNAL_ONLY` / non-trading pour la production live** tant qu'aucune de ces pistes n'a produit un edge validé OOS + après coûts. Ne pas activer `MinRiskReward` ni aucun autre gate dans l'espoir d'un redressement — aucun ne redresse le book.

---

## 7. Index des fichiers produits (2026-08-30 → 2026-08-31)

### Sondes — `IQIAIndicator/Tests/Research/StopLossContractAudit/` (8 fichiers, read-only, aucune modification de production)

| Fichier | Section | Sortie |
|---|---|---|
| `StopLossContractAuditTests.cs` | 2.a | `Output/signals.csv` |
| `MeanRevertingExpectancyAuditTests.cs` | 2.b (brut) | `Output/mr_trades.csv`, `Output/summary.txt` |
| `MinRiskRewardThresholdValidationTests.cs` | 2.b (coûts), 2.c (ES=F) | `Output/threshold_validation.txt` |
| `MultiTimeframeEquilibriumAuditTests.cs` | 2.d | `Output/mtf_equilibrium.txt` |
| `SlowTimeframePipelineAuditTests.cs` | 2.e (MeanReverting), 2.g (Trending M15/H1) | `Output/slow_timeframe_pipeline.txt` |
| `MeanRevertingDeviationThresholdAuditTests.cs` | 2.f | `Output/deviation_threshold.txt` |
| `TrendingExpectancyAuditTests.cs` | 2.g (M5) | `Output/trending_expectancy.txt` |
| `MesSerialDependenceAuditTests.cs` | 2.h | `Output/serial_dependence.txt` |

Note technique : les sondes 2.e / 2.f / 2.h utilisent les classes `internal` `HttpYahooChartClient`, `YahooChartParser`, `YahooSymbolMap`, `VarianceRatioStatistics` **sans les modifier**, via `InternalsVisibleTo("IQIAIndicator.Tests")` déjà présent (`Properties/AssemblyInfo.cs:3`). Aucun type de production n'a été touché.

### Fichiers mémoire créés

| Fichier | Contenu |
|---|---|
| `project_iqia_stoploss_takeprofit_sizing_contract.md` | Carte du contrat SL/TP/sizing + mesures empiriques (section 2.a) |
| `project_iqia_minriskreward_threshold_invalidated.md` | 6 vérifications MeanReverting (sections 2.b–2.f), avec les 3 follow-ups (cible H1, pipeline M15/H1, seuil de déviation) |
| `project_iqia_trending_book_no_edge.md` | Trending sans edge M5, résultat H1 fragile (section 2.g) |
| `project_iqia_mes_no_serial_dependence.md` | Étude de dépendance sérielle, la conclusion de fond (section 2.h) |

### Rappel

**Aucun fichier de production n'a été modifié.** Les sondes et leurs sorties sont conservées comme preuve vérifiable derrière chaque chiffre de ce rapport. Suppression éventuelle (recherche uniquement) : `git clean -fd IQIAIndicator/Tests/Research/StopLossContractAudit/`.

---

## 8. FINAL OUTPUT

**STATUS:** Investigation terminée. Huit vérifications indépendantes convergent : sur MES intraday M5–H1 (fenêtres 59 j pour M5/M15, 720 j pour H1), il n'existe pas de dépendance sérielle exploitable après coûts. MeanReverting n'a pas d'edge net (établi avec un haut niveau de confiance) ; Trending est non concluant (résultat OOS-only fragile). Le goulot n'est ni le contrat SL/TP, ni le R:R, ni le placement du stop, ni la cadence, ni le seuil d'entrée — c'est l'absence de structure prévisible dans la série de prix elle-même à ces échelles.

**PRODUCTION MODIFIED:** NO. Aucune ligne de code de production modifiée. 8 sondes de recherche ajoutées sous `Tests/Research/StopLossContractAudit/` ; classes `internal` de production réutilisées sans modification via `InternalsVisibleTo` existant.

**TESTS:** Les 8 sondes s'exécutent en Passed (ou Skipped si Yahoo indisponible). Aucune assertion de comportement de production ajoutée ou modifiée. Auto-vérification de la sonde 2.d : re-simulation de la cible M5 reproduit `RunFullBacktest` à 0,0000 $ près.

**DOCUMENTATION:** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_SLTP_MeanReverting_Trending_Edge_Investigation_Final_Report.md`. Quatre fichiers mémoire créés (voir §7).

**NEXT ACTION:** Décision de roadmap requise. Options mutuellement exclusives :
1. **Arrêter les itérations sur MeanReverting/Trending intraday** et ouvrir un projet de recherche sur une nouvelle source de données (order flow, cross-asset, calendaire, options-implied) — recommandé.
2. **Explorer l'horizon daily+** avec une acquisition d'historique long dédiée (TSMOM mensuel a un fondement littéraire ; non testé ici).
3. **Lot de calibration Trending** walk-forward multi-instruments pour trancher le résultat OOS fragile de la section 2.g — plus faible priorité (l'étude 2.h suggère un prior défavorable même à cadence horaire).

Ne PAS : construire l'ancre VWAP, calibrer `TimeSeriesMomentumModel` à cadence intraday, ajuster un paramètre du système actuel, ou activer un gate de risque dans l'espoir d'un redressement.
