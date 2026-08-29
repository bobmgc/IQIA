# QDE-012 — Sprint 15.25 — Lot 14.13 — AmbiguityGateThreshold : Revalidation Mono-Fenêtre Contrainte

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.9 (audit global), Lot 14.10 (binding réel P0-3), Lot 14.11 (protocole), Lot 14.12 (dataset 59 jours)
**Statut** : **IMPLEMENTED**
**Type** : **REVALIDATION / SENSITIVITY ANALYSIS — PAS UNE CALIBRATION FINALE**
**Portée** : sensibilité de `AmbiguityGateThreshold` (production : 0.95) sur MES=F/M5/59 jours. **Aucune valeur de production modifiée. Aucun autre paramètre touché. Aucune sélection de "meilleur seuil".**

---

# 1. EXECUTIVE SUMMARY

Ce lot exécute, via le framework réel `Backtest.Calibration` (Lots 14.9/14.10, inchangé), une grille déterministe de 7 valeurs de `AmbiguityGateThreshold` (0.80 → 0.99, 0.95 inclus) sur le dataset MES=F/M5/59 jours (recette du Lot 14.12, re-téléchargé ce jour — fenêtre glissante, donc non bit-identique à celle du Lot 14.12, voir §4). Chaque configuration est un `CalibrationExperiment` réel, injecté par `CalibrationParameterBinding` (Lot 14.10), jamais un simulateur parallèle ni un recalcul manuel.

**Constat central, mesuré, pas supposé** : sur ce dataset, `AmbiguityScore` (la métrique que ce seuil filtre) est concentré très près de 1.0 — **aucun candidat directionnel n'existe pour θ ∈ {0.80, 0.85, 0.90}** (0 signal, sur les 5 362 candidats MeanReverting disponibles à chaque seuil). Le comportement bascule brutalement entre 0.90 (0%) et 0.99 (93.4% d'acceptation), avec 0.95 — la valeur de production — située **au milieu d'une pente très raide, pas dans un plateau robuste**, pour ce qui concerne la **quantité** de signaux. En revanche, la **qualité** par position (win rate, hit rates, MFE/MAE médians) reste comparativement stable de 0.95 à 0.99 — un plateau existe, mais pour une métrique différente de celle où l'instabilité est la plus visible. Ces deux constats, présentés ensemble, sont le résultat principal de ce lot (§13/§14/§16).

L'analyse économique (coûts désactivés, aucun barème réel disponible — rappel obligatoire du brief) montre un classement **non stable entre TRAIN/VALIDATION/OOS** : TRAIN atteint son P&L le moins négatif à 0.95, tandis que VALIDATION et OOS croissent plutôt vers 0.975 — une divergence directe entre fenêtres qui est elle-même la preuve empirique qu'aucune sélection de seuil n'est scientifiquement défendable sur ce seul dataset (§10/§17).

L'analyse par régime confirme, et explique structurellement pour la première fois pour CE paramètre précis, un fait déjà connu du pipeline : `AmbiguityGateThreshold` n'a d'effet que sur le régime `MeanReverting` — tout autre régime retourne `NO_ACTION` avant même d'atteindre le test d'ambiguïté (`EntryTriggerBuilder.DetermineDirection`, lu directement, §12). Ce n'est donc pas un blocage de jointure de données (le blocage générique D14.11-2 existe toujours pour d'autres analyses) mais une propriété structurelle du code : pour ce paramètre spécifique, l'analyse par régime EST complète avec un seul régime actif, jamais partielle.

Toutes les propriétés de framework exigées (§26 du brief) sont **PASS**, vérifiées par assertions exécutables dans le test de ce lot : Parameter Binding, Behavioural Effect, Determinism, Run Isolation, Fingerprint differentiation, Look-Ahead (regime map invariant au seuil). `EntryTriggerBuilder.AmbiguityGateThreshold` reste `0.95` — **jamais modifié**.

---

# 2. SCIENTIFIC QUESTION

Reprise exacte du brief : le seuil de production 0.95 est-il raisonnable sur ce dataset ? Le comportement est-il sensible au seuil ? Existe-t-il une zone de seuils stable ? Le seuil modifie-t-il la quantité et/ou la qualité des signaux et des métriques économiques ? Son effet dépend-il du régime ? Les résultats sont-ils assez robustes pour justifier un futur lot plus large ?

**Ce lot ne répond PAS à** : "quelle est la valeur optimale de production ?" — question explicitement hors scope (brief §27, règle absolue §RÈGLE SCIENTIFIQUE ABSOLUE).

---

# 3. DATASET

```
Instrument : MES (Yahoo continu, MES=F)
Timeframe  : M5
Provider   : Yahoo(Continuous)
Symbol     : MES
BarCount   : 11 058
Range      : 2026-06-25T22:35:00.0000000Z .. 2026-08-23T22:29:05.0000000Z
Warmup     : 128 barres (leadIn, identique à tous les lots précédents)
```

**Important — dataset NON bit-identique à celui du Lot 14.12** (11 078 barres, 2026-06-25T19:35Z→2026-08-21T20:59:59Z, fingerprint `F7B34C25...`). Yahoo M5 est une fenêtre glissante ancrée sur "maintenant" (Lot 14.12 §23) : ce lot a re-téléchargé la MÊME recette (`YahooHistoricalBarSource.DefaultMaxChunkSpanDays` = 59 jours) le jour suivant (2026-08-24 vs 2026-08-23), produisant un dataset voisin mais distinct — reproductible en MÉTHODE, jamais bit-identique en RÉSULTAT, exactement la discipline déjà établie au Lot 14.12. La couverture MeanReverting mesurée sur TRAIN+VALIDATION de ce dataset (57.7%, §12) est cohérente avec le 58.7% mesuré au Lot 14.12 sur son propre dataset — corroboration croisée, pas une réutilisation du même chiffre.

## Fenêtres

| Fenêtre | Bornes | Barres |
|---|---|---|
| TRAIN | [2026-06-26T09:20:00Z .. 2026-08-05T23:30:00Z) | 7 650 |
| VALIDATION | [2026-08-05T23:30:00Z .. 2026-08-13T23:05:00Z) | 1 639 |
| OOS | [2026-08-13T23:05:00Z .. 2026-08-23T22:29:05Z) | 1 640 — **descriptif uniquement, jamais utilisée pour sélectionner un seuil (§18)** |

Split 70/15/15, identique à la convention déjà établie par `CalibrationYahooIntegrationTests` (Lot 14.9) — pas une nouvelle proportion inventée pour ce lot.

---

# 4. DATASET FINGERPRINT

```
DatasetFingerprint = 98106875F950798A8511523170FCE75261D5B371A5F8B79574E084B4A939B9C4
```

Calculé par `HistoricalSeriesFingerprint.Compute` (Lot 14.2, inchangé), porté par `CalibrationDataset.Fingerprint` (Lot 14.9, inchangé). Chaque `CalibrationExperimentResult` de ce lot porte ce même `DatasetFingerprint` — vérifié par construction (`CalibrationExperimentRunner.BuildSlice` le copie tel quel depuis `experiment.Dataset.Fingerprint`), jamais recalculé indépendamment.

---

# 5. EXPERIMENTAL CONFIGURATION

Toute la configuration hors `AmbiguityGateThreshold` est maintenue strictement constante (brief §1) :

```
Instrument         : MES (TickSize=0.25, TickValue=1.25, PointValue=5, Decimals=2)
RiskPolicy         : MaxRiskPerTradePercent=0.02, tout le reste null (fail-closed, Lot 14.9 convention)
InitialCapital     : 50 000
Measurement        : HorizonBars=10, HitThresholds=[0.001, 0.002]
Execution          : HorizonBars=10, Fill=Open[SignalBarIndex+1] (Lot 14.10 P0-1, inchangé)
PnL                : quantity=1, priceUnitValue=5, currency=USD
Cost                : DISABLED — aucun barème réel sourcé (rappel obligatoire, §10)
Risk (position sizing) : DISABLED — AllowedQuantity uniforme = PnLConfiguration.Quantity (Lot 14.8 convention documentée)
ProtocolVersion     : CalibrationProtocolVersion.Current (Lot 14.9, inchangé)
```

Pipeline réel utilisé sans modification : Yahoo → HistoricalSeries → MarketContext → Regime → Fusion → Decision → Signal → Entry → EntryTrigger → TradePlan → Execution → Measurement → Risk. Chaque configuration est exécutée via `BacktestEngine.RunFullBacktestWithRisk` (appelé UNE fois par configuration à l'intérieur de `CalibrationExperimentRunner.Run`, exactement comme documenté par ce runner depuis le Lot 14.9) — jamais de re-simulation par fenêtre.

---

# 6. PARAMETER GRID

```
{0.80, 0.85, 0.90, 0.925, 0.95, 0.975, 0.99}
```

Grille de l'exemple donné par le brief, retenue telle quelle — justification : pas de partition arbitraire, mais un pas grossier (0.05) sous 0.90 pour couvrir la zone jugée improbable a priori, et un pas fin (0.025) au-dessus de 0.90, là où la littérature interne (Lots 4-9, Lot 14.11 §5) situe la valeur de production. **0.95 est présent**, conformément à l'exigence absolue du brief. Générée par `CalibrationGrid.GenerateParameterSets` (Lot 14.9, inchangé) sur un seul axe — jamais choisie à la main valeur par valeur.

**7 configurations comparées, un seul cycle, aucune itération** (voir §17 pour la discipline anti-data-snooping complète).

---

# 7. BASELINE 0.95

Rappel : 0.95 est la valeur de production actuelle (`EntryTriggerBuilder.AmbiguityGateThreshold`, const, inchangée). Résultats TRAIN/All (7 650 barres) :

```
SignalCount=1570, PositionCount=1569, WinRate=47.86%, MedianReturn=-0.0000333,
MedianMfe=0.000698, MedianMae=0.000690, HitRate(0.001)=34.78%, HitRate(0.002)=12.61%,
GrossPnL=NetPnL=+98.75 (coûts désactivés), MaxDrawdown=-3515.00
```

VALIDATION/All (1 639 barres) : SignalCount=431, WinRate=52.08%, PnL=+1391.25, MaxDrawdown=-477.50.
OOS/All (1 640 barres, descriptif) : SignalCount=343, WinRate=54.23%, PnL=+372.50, MaxDrawdown=-943.75.

0.95 accepte **37.3%** des 5 362 candidats MeanReverting disponibles sur TRAIN+VALIDATION (994 BUY + 1007 SELL sur 5 362) — ni saturé (comme 0.80-0.90, 0%) ni quasi-permissif (comme 0.99, 93.4%) : une position intermédiaire, mesurée, pas choisie.

---

# 8. SIGNAL-LEVEL RESULTS

Candidats = barres MeanReverting sur TRAIN+VALIDATION (5 362, **identique à chaque seuil** — confirme que le seuil ne change jamais QUELS candidats existent, seulement combien sont acceptés — cohérent avec le fait que `Decision.Winner` est calculé en amont de `AmbiguityGateThreshold`, §12).

| θ | Candidats | BUY | SELL | Rejetés par le gate | Taux d'acceptation |
|---|---|---|---|---|---|
| 0.80 | 5 362 | 0 | 0 | 5 362 | 0.00% |
| 0.85 | 5 362 | 0 | 0 | 5 362 | 0.00% |
| 0.90 | 5 362 | 0 | 0 | 5 362 | 0.00% |
| 0.925 | 5 362 | 27 | 17 | 5 318 | 0.82% |
| **0.95** | 5 362 | 994 | 1 007 | 3 361 | **37.32%** |
| 0.975 | 5 362 | 2 126 | 2 302 | 934 | 82.58% |
| 0.99 | 5 362 | 2 394 | 2 613 | 355 | 93.38% |

"Rejetés par le gate" = `EntryTrigger.Assessment.Reason == DECISION_AMBIGUOUS` exactement (lu directement sur `BacktestSignalResult.EntryTrigger`, jamais recalculé — brief §17).

**Constat** : la courbe Acceptance(θ) est un plateau à 0% jusqu'à 0.90, puis une croissance très raide (0% → 93% en 0.065 de seuil), sans signe de saturation complète à 0.99. Une extension de la grille au-delà de 0.99 (ex. 0.995, 0.999) documenterait la queue de cette courbe — non fait ici, hors grille approuvée (§6), noté comme piste pour un futur lot (§20/§21).

---

# 9. MEASUREMENT-LEVEL RESULTS

TRAIN/All, par seuil (positions, win rate, retour médian, MFE/MAE médians, hit rates) :

| θ | Positions | WinRate | MedianReturn | MedianMfe | MedianMae | Hit(0.001) | Hit(0.002) |
|---|---|---|---|---|---|---|---|
| 0.925 | 27 | 40.74% | -0.000296 | 0.000265 | 0.001611 | 18.52% | 11.11% |
| **0.95** | 1 569 | 47.86% | -0.0000333 | 0.000698 | 0.000690 | 34.78% | 12.61% |
| 0.975 | 3 442 | 46.48% | -0.0000665 | 0.000700 | 0.000725 | 35.38% | 12.95% |
| 0.99 | 3 956 | 46.54% | -0.0000663 | 0.000706 | 0.000728 | 35.99% | 12.74% |

**Constat central** : de 0.95 à 0.99, ces métriques PAR POSITION restent dans une bande étroite (win rate 46-48%, hit rates ~35%/~13%, MFE≈MAE≈0.0007) — un plateau réel, malgré le fait que le NOMBRE de positions triple sur le même intervalle (§8/§13). À 0.925 (n=27), les chiffres divergent nettement (win rate 40.7%, MAE 6× le MFE) — mais l'échantillon est trop petit pour toute conclusion (règle §5 du brief, respectée : aucune conclusion forte tirée de n=27).

VALIDATION/All et OOS/All (descriptif) montrent la même stabilité qualitative à partir de 0.95 (win rate 50-55% partout, hit rates comparables) — détail complet dans le log de test (§Reproducibility).

---

# 10. ECONOMIC RESULTS

**RAPPEL OBLIGATOIRE (brief §4) : Real cost schedule unavailable.** `ExecutionCostConfiguration.Disabled()` — les chiffres ci-dessous sont un P&L brut au sens strict (aucun coût, spread, slippage, commission, frais appliqué), donc `GrossPnL == NetPnL` littéralement dans `CalibrationExperimentResult`. Ils ne représentent PAS ce qu'un compte réel aurait gagné/perdu — diagnostic uniquement, jamais une conclusion économique.

| θ | TRAIN PnL | TRAIN MaxDD | VALIDATION PnL | VALIDATION MaxDD | OOS PnL (descriptif) | OOS MaxDD |
|---|---|---|---|---|---|---|
| 0.925 | -65.00 | -311.25 | -32.50 | -110.00 | +105.00 | -145.00 |
| **0.95** | **+98.75** | -3 515.00 | +1 391.25 | -477.50 | +372.50 | -943.75 |
| 0.975 | -2 915.00 | -6 993.75 | +4 331.25 | -930.00 | +872.50 | -2 013.75 |
| 0.99 | -2 651.25 | -7 180.00 | +3 611.25 | -1 517.50 | +701.25 | -2 093.75 |

**Constat, présenté sans conclusion de "meilleur seuil" (règle absolue du brief)** : le classement par PnL diffère entre TRAIN (le moins négatif à 0.95) et VALIDATION/OOS (croissant plutôt vers 0.975). Cette DIVERGENCE ENTRE FENÊTRES est elle-même le résultat scientifique le plus important de cette section — elle démontre concrètement, sur ce dataset, pourquoi choisir un seuil sur la base d'un P&L mono-fenêtre serait non justifié (§17 Data Snooping). Le Drawdown croît mécaniquement avec le nombre de positions (plus de barres exposées ⇒ plus d'occasions de repli cumulatif) — pas nécessairement un signal de risque par position plus élevé.

---

# 11. BUY/SELL ANALYSIS

TRAIN, win rate BUY vs SELL :

| θ | BUY (n) | BUY WinRate | SELL (n) | SELL WinRate |
|---|---|---|---|---|
| 0.925 | 19 | 36.84% | 8 | 50.00% |
| 0.95 | 767 | 49.15% | 803 | 46.63% |
| 0.975 | 1 654 | 49.33% | 1 789 | 43.85% |
| 0.99 | 1 896 | 49.58% | 2 061 | 43.74% |

Asymétrie modérée BUY > SELL en TRAIN/VALIDATION aux seuils élevés (0.975/0.99), jamais extrême (jamais >60/40). **En OOS (descriptif), l'asymétrie s'INVERSE** : SELL nettement plus performant que BUY (ex. θ=0.95 : BUY WinRate=44.10% vs SELL=67.57% ; θ=0.975 : BUY=41.96% vs SELL=63.34%). Au niveau P&L, ce même retournement apparaît (ex. θ=0.975 TRAIN : Buy=+2317.50 / Sell=-5232.50 ; OOS : Buy=-3735.00 / Sell=+4607.50).

**Conclusion méthodologique (brief §6, respectée)** : cette inversion TRAIN/VALIDATION ↔ OOS interdit toute conclusion causale sur une asymétrie structurelle BUY/SELL — c'est un artefact plausible du mono-fenêtre (59 jours), pas une propriété du marché prouvée ici. Aucun seuil n'a été retenu ni écarté sur la base de sa performance BUY ou SELL isolée.

---

# 12. REGIME ANALYSIS

**Constat structurel, vérifié par lecture directe du code, pas seulement par les données** : `EntryTriggerBuilder.DetermineDirection` (Engine/EntryTrigger/EntryTriggerBuilder.cs:159-222) retourne `NO_ACTION` pour tout régime différent de `MeanReverting` **avant même d'atteindre le test `decision.AmbiguityScore >= ambiguityGateThreshold`** (ligne 176-180, suppression reason distincte de `DECISION_AMBIGUOUS`). Conséquence directe, confirmée empiriquement sur les 7 seuils testés : **StructuralBreak, Trending, RandomWalk, StableRange n'apparaissent JAMAIS dans la répartition régime-conditionnelle des candidats directionnels, quel que soit le seuil.**

| θ | Régime | Signaux | BUY | SELL | Positions | MedianReturn | MedianMfe | MedianMae |
|---|---|---|---|---|---|---|---|---|
| 0.925 | MeanReverting | 59 | 36 | 23 | 59 | -0.000129 | 0.000517 | 0.000807 |
| 0.95 | MeanReverting | 2 344 | 1 189 | 1 155 | 2 344 | 0 | 0.000624 | 0.000600 |
| 0.975 | MeanReverting | 5 380 | 2 646 | 2 734 | 5 369 | 0 | 0.000613 | 0.000591 |
| 0.99 | MeanReverting | 6 030 | 2 942 | 3 088 | 6 019 | 0 | 0.000618 | 0.000599 |

(StructuralBreak/Trending/RandomWalk/StableRange : 0 ligne à chaque seuil — omis du tableau, pas absents par erreur.)

**Ce n'est PAS le blocage générique D14.11-2** (jointure `Decision.Winner`↔`MeasurementResult` techniquement absente pour une analyse régime GÉNÉRALE) — ce blocage reste entier pour tout AUTRE paramètre. Pour `AmbiguityGateThreshold` SPÉCIFIQUEMENT, l'analyse par régime est **structurellement complète avec un seul régime actif** : il n'existe tout simplement aucun candidat MeanReverting-absent sur lequel ce seuil pourrait avoir un effet, parce que le code ne le laisse jamais s'exécuter dans ce cas. Conforme au brief §5 : aucune conclusion inventée pour Transitional/Unknown (absents du dataset, Lot 14.12) ni pour StructuralBreak/Trending/RandomWalk/StableRange (présents dans le dataset mais jamais candidats directionnels, quel que soit le seuil — fait différent, documenté séparément).

---

# 13. SENSITIVITY ANALYSIS

**Performance(θ), pas Best(θ)** (méthodologie Lot 14.11 §14, exécutée ici pour la première fois sur données réelles).

## Quantité de signaux (Acceptance Rate, §8)
Signature : **plateau à zéro (θ≤0.90) suivi d'une transition raide et monotone (0.90→0.99)**, sans signe de plateau haut atteint à 0.99 (93.4%, pas 100%). Type de signature (typologie §14 du Lot 14.11) : **MONOTONICITÉ dans la zone active**, **RÉGION MORTE en dessous**.

## Qualité par position (win rate, hit rates, MFE/MAE, §9)
Signature, dans la zone active (θ≥0.95) : **PLATEAU** — win rate 46-48%, hit rates ~35%/~13% quasi constants sur 0.95→0.99, malgré un triplement du nombre de positions sur le même intervalle. C'est la preuve la plus solide de ce lot que la quantité et la qualité répondent différemment au même paramètre.

## Économie (PnL, §10)
Signature : **INSTABILITÉ inter-fenêtre** — le classement change de sens entre TRAIN et VALIDATION/OOS (§10). Pas assez d'invariance pour parler de plateau ni de monotonicité propre — l'échantillon disponible (un seul mono-window de 59 jours) ne permet pas de trancher.

---

# 14. ROBUSTNESS ANALYSIS

Comparaison de voisins directs, méthode explicitement demandée par le brief (§8, exemple 0.90/0.925/0.95) :

| Triplet | SignalCount TRAIN | Verdict |
|---|---|---|
| 0.90 / 0.925 / 0.95 | 0 / 27 / 1 570 | **INSTABLE** — chaque pas multiplie le comptage par un facteur >>10 |
| 0.925 / 0.95 / 0.975 | 27 / 1 570 / 3 443 | **INSTABLE** — le brief cite explicitement ce type de voisinage comme exemple d'instabilité (§8) |
| 0.95 / 0.975 / 0.99 | 1 570 / 3 443 / 3 957 | Croissance continue mais ralentissante — plus proche d'une **MONOTONICITÉ AMORTIE** que d'un plateau strict |

**0.95 n'est PAS dans une région robuste pour la QUANTITÉ de signal** — un voisin à ±0.025 change le comptage TRAIN d'un facteur ≈2 à ≈58×. C'est un résultat honnête, pas confortable, et c'est exactement le type de signature que le brief demande de détecter plutôt que de masquer (§8/§13).

Pour la QUALITÉ par position (§9/§13), le même voisinage (0.925 exclu pour n trop petit ; 0.95/0.975/0.99) EST robuste — win rate et hit rates varient de moins de 2 points de pourcentage sur cet intervalle.

**Conclusion de robustesse, à deux niveaux, jamais réduite à un seul verdict** : ROBUSTE pour la qualité conditionnelle, INSTABLE pour la quantité absolue, au voisinage de 0.95.

---

# 15. EFFECT SIZE

Comparaison explicite θ=0.80 / θ=0.95 / θ=0.99 (brief §9) :

| Métrique | θ=0.80 | θ=0.95 | θ=0.99 | Effet |
|---|---|---|---|---|
| SignalCount (TRAIN) | 0 | 1 570 | 3 957 | Énorme — d'une stratégie inexistante à une stratégie dense |
| PositionCount (TRAIN) | 0 | 1 569 | 3 956 | Idem |
| MedianReturn (TRAIN) | n/a | -0.0000333 | -0.0000663 | Modeste en valeur absolue, doublement en ordre de grandeur |
| MedianMae (TRAIN) | n/a | 0.000690 | 0.000728 | Faible (+5.5%) |
| MedianMfe (TRAIN) | n/a | 0.000698 | 0.000706 | Négligeable (+1.1%) |
| PnL (TRAIN, diagnostic) | n/a | +98.75 | -2 651.25 | Grand, mais dominé par SELL (§11) et non reproduit en VALIDATION/OOS |

**Verdict** : `AmbiguityGateThreshold` est un paramètre **fortement influent sur la quantité** de signaux/positions produits (effet de premier ordre, sans ambiguïté), et un paramètre **d'effet modeste sur la qualité mesurée par position** une fois la zone active atteinte (effet de second ordre). L'effet sur le P&L observé est réel en amplitude mais non stable entre fenêtres, donc non attribuable avec confiance au seuil seul plutôt qu'au bruit d'échantillonnage sur 59 jours.

---

# 16. INSENSITIVE REGIONS

| Région | θ | Statut |
|---|---|---|
| Zone morte | [0.80, 0.90] | **INSENSITIVE** — 0 signal à chaque point testé, identiquement |
| Zone active (quantité) | [0.925, 0.99] | **ACTIVE** — chaque pas change le comptage de façon marquée |
| Zone active (qualité) | [0.95, 0.99] | **INSENSITIVE relative** — win rate/hit rates/MFE/MAE quasi stables malgré le comptage qui triple |

Documentation explicite demandée par le brief §10 : la zone morte n'est PAS un artefact de bug — elle reflète simplement que `decision.AmbiguityScore` ne descend quasiment jamais sous 0.90 sur ce dataset précis (fait mesuré, pas expliqué par ce lot — une investigation de la distribution d'`AmbiguityScore` elle-même serait un travail Decision/Fusion, hors scope, brief §27).

---

# 17. DATA SNOOPING CONTROLS

- **7 configurations comparées**, un seul cycle TRAIN→VALIDATION, **aucune itération** (brief §14).
- **0 configuration sélectionnée comme "gagnante"** — chaque résultat de ce rapport est présenté nu, sans score composite (cohérent avec `CalibrationExperimentResult`, qui n'a délibérément aucun champ `CalibrationScore`, Lot 14.9).
- Dataset choisi UNIQUEMENT parce que c'est la profondeur maximale Yahoo (Lot 14.12 §5/§22) — jamais parce qu'il produirait de meilleurs résultats pour ce paramètre.
- Grille choisie AVANT toute exécution, documentée §6, jamais ajustée après avoir vu un résultat partiel.
- **19 exécutions de pipeline complet** au total dans ce lot (7 grille officielle + 7 diagnostics gate/régime + 1 rebuild déterminisme + 4 séquence isolation A/B/C/B) — chiffre journalisé ici conformément à la discipline anti-data-snooping du Lot 14.11 §13 ("journaliser N systématiquement").
- Aucune correction statistique formelle (Bonferroni etc.) appliquée — non justifiée à ce stade (Lot 14.11 §13, position inchangée), la comparaison reste descriptive.

---

# 18. OOS HANDLING

OOS (1 640 barres, [2026-08-13T23:05Z..2026-08-23T22:29:05Z)) est **affichée dans ce rapport à titre strictement descriptif** (§10, §11) — jamais consultée pour choisir la grille, jamais utilisée pour départager deux seuils, jamais re-exécutée après avoir vu son propre résultat. La divergence de classement PnL entre TRAIN/VALIDATION et OOS (§10) est rapportée précisément PARCE QU'elle illustre pourquoi cette discipline est nécessaire, pas malgré elle. Aucun second cycle n'a été déclenché suite à l'observation d'OOS.

---

# 19. LIMITATIONS

- **Dataset mono-fenêtre, 59 jours** — Walk-Forward multi-période structurellement impossible via Yahoo (Lot 14.12 §14.2, inchangé).
- **Coûts réels non sourcés** — tous les chiffres économiques (§10) sont un P&L diagnostique sans coûts, jamais un résultat de compte réel.
- **StopLoss/Risk non calibrés, non modifiés** — sizing uniforme (`BacktestRiskConfiguration.Disabled()`), le Drawdown rapporté n'intègre aucune protection de risque réelle.
- **Régimes sous-représentés** (Trending, RandomWalk, StableRange — Lot 14.12 §16) restent hors de portée de toute analyse pour CE paramètre spécifiquement PARCE QU'ils ne produisent jamais de candidat directionnel (§12) — pas une limitation de puissance statistique à combler par plus de données, mais une propriété du code actuel.
- **Aucune p-value, aucun intervalle de confiance** n'a été calculé (brief §15, respecté) — toutes les comparaisons ci-dessus sont descriptives.
- **Grille bornée à [0.80, 0.99]** — la courbe d'acceptation n'atteint pas 100% à 0.99 (93.4%) ; son comportement au-delà de 0.99 reste non mesuré par ce lot.
- **Dataset non bit-identique au Lot 14.12** (§3/§4) — toute comparaison chiffre-à-chiffre avec ce lot précédent doit tenir compte de cette différence.

---

# 20. CONCLUSION

Le seuil de production 0.95 se situe, sur ce dataset précis, **dans une zone de forte sensibilité pour la quantité de signaux** (un déplacement de ±0.025 change le comptage TRAIN d'un facteur 2 à 58×) et **dans une zone de stabilité relative pour la qualité par position** (win rate, hit rates, MFE/MAE quasi constants de 0.95 à 0.99). Aucune de ces deux observations ne permet, seule ou combinée, de conclure que 0.95 est "la meilleure" valeur — seulement qu'elle est une valeur RAISONNABLE au sens où elle ne tombe ni dans la zone morte (0.80-0.90, 0 signal) ni dans la zone de saturation quasi-totale (0.99, 93% d'acceptation, coûts implicitement ignorés à cette fréquence). L'analyse économique (§10) montre une instabilité inter-fenêtre qui interdit explicitement toute conclusion de performance. Ce lot est une **REVALIDATION CONTRAINTE**, pas une calibration finale — conforme à la règle scientifique absolue du brief.

---

# 21. HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
    IQIA

CURRENT LOT:
    14.13

LAST COMPLETED:
    14.12

DATASET:
    MES=F
    M5
    59 days (dataset THIS session's own download - 11 058 bars, 2026-06-25T22:35Z..2026-08-23T22:29:05Z,
    fingerprint 98106875F950798A8511523170FCE75261D5B371A5F8B79574E084B4A939B9C4 - NOT bit-identical to
    Lot 14.12's own capture, rolling window, see §3/§4 of this report)

BAR COUNT:
    11058

DATASET LIMIT:
    Yahoo M5 >59 days unavailable (unchanged, Lot 14.12)

REGIME COVERAGE:
    Not re-measured dataset-wide this lot (out of scope - only the regime-conditional breakdown of
    DIRECTIONAL candidates was measured, §12). MeanReverting share among TRAIN+VALIDATION bars = 57.7%
    (5362/9289), consistent with Lot 14.12's 58.7% on its own dataset.

PARAMETER TESTED:
    AmbiguityGateThreshold

PRODUCTION VALUE:
    0.95

CALIBRATION STATUS:
    CONSTRAINED REVALIDATION

PRODUCTION VALUE CHANGED:
    NO

RESULT:
    Dead zone for theta<=0.90 (0 signals identically). Steep, monotonic acceptance-rate transition from
    0.925 (0.82%) to 0.99 (93.38%). Per-position quality metrics (win rate, hit rates, MFE/MAE) stable
    from 0.95 to 0.99 despite signal count tripling over the same range. Economic ranking (PnL) NOT stable
    across TRAIN/VALIDATION/OOS - no threshold selected or implied as superior.

ROBUST REGION:
    Per-position QUALITY metrics: [0.95, 0.99] plateau (win rate 46-48%, hit rates ~35%/~13% throughout).
    Signal QUANTITY: NOT robust in this same range (factor 2x-58x swings between adjacent grid points).

INSTABILITY:
    Signal count around 0.95 (neighbours 0.925/0.975 differ by >10x in both directions - brief's own
    example of an unstable neighbourhood, reproduced empirically here). Economic PnL ranking flips sign
    between TRAIN and VALIDATION/OOS.

REGIME LIMITATIONS:
    AmbiguityGateThreshold structurally affects ONLY the MeanReverting regime - EntryTriggerBuilder
    returns NO_ACTION for every other regime before reaching the ambiguity check (code-level fact, not a
    join-blocker). StructuralBreak/Trending/RandomWalk/StableRange never appear as directional candidates
    at any threshold tested. This is a DIFFERENT finding from the generic D14.11-2 join blocker, which
    still applies to any OTHER parameter's regime-conditional analysis.

BUY/SELL:
    Mild BUY>SELL win-rate asymmetry in TRAIN/VALIDATION at high thresholds (0.975/0.99), REVERSED in OOS
    (SELL>BUY). No causal conclusion drawn - treated as evidence of mono-window instability, not a
    structural market asymmetry.

OOS:
    Descriptive only throughout. PnL ranking by threshold in OOS/VALIDATION diverges from TRAIN - used
    explicitly in this report as evidence for why no threshold selection is scientifically defensible here.

COST LIMITATION:
    Real cost schedule unavailable (unchanged, Lot 14.10/14.11/14.12). All PnL/drawdown figures in this
    lot are costs-disabled diagnostics only, never a real-account result.

STOP LOSS:
    NOT CALIBRATED

RISK:
    NOT CALIBRATED

NEXT RECOMMENDED LOT:
    A dataset-expansion or alternative-data-source lot (to escape the Yahoo 60-day wall, Lot 14.12 §14.2)
    would be the highest-value next step BEFORE any further AmbiguityGateThreshold work - the instability
    found in §13/§14 (signal-quantity sensitivity, cross-window PnL ranking flips) cannot be resolved by
    re-analysing the SAME 59-day window again. If dataset expansion is not available, a Decision/Fusion-
    layer investigation into why AmbiguityScore concentrates near 1.0 on this dataset (§16) would explain
    the dead zone below 0.90 - a Decision/Fusion lot, not a threshold-recalibration lot.

WHY:
    This lot's own robustness analysis (§14) proves that a single 59-day mono-window is not sufficient to
    distinguish a real threshold effect from mono-window noise for signal quantity or economic PnL -
    exactly the limitation the brief required this lot to surface, not paper over.

DO NOT DO:
    Do NOT select 0.95 (or any other tested value) as a "better" production threshold based on this
    report - the report's own §10/§14/§17 explicitly forbid that conclusion. Do NOT extend the grid beyond
    [0.80, 0.99] and re-run without a new approved brief. Do NOT touch RegimeEngine/FusionEngine/
    DecisionEngine/DecisionArbitrator/SignalEngine/EntryEngine/EntryTriggerEngine/EntryTriggerBuilder/
    TradePlanBuilder/RiskEngine/RiskPolicy/Execution logic. Do NOT calibrate Stop Loss or Risk. Do NOT
    treat the regime-conditional finding (§12) as evidence that regime analysis is now unblocked for OTHER
    parameters - the D14.11-2 join blocker is unchanged for anything besides AmbiguityGateThreshold.
```

**STOP.**
