# QDE-012 — Sprint 15.25 — Lot 18 — Coût économique end-to-end de `StructuralBreakRule`

> **Type :** Mesure end-to-end (contrefactuel par ablation) — **Mode :** conclusion observationnelle
> **Production comportementale modifiée :** NON (seam de recherche défaulté, non-régression prouvée)
> **Calibration / Optimisation / Repondération :** NON — **ATAS :** non utilisé — **Ordres :** aucun
> **DLL déployée :** non — **Commit :** non
>
> Suite du Lot 17. Objectif : chiffrer si `StructuralBreakRule` (la règle de décision qui gagne ~31 %
> des barres et les route toutes en `NO_ACTION / UNSUPPORTED_REGIME`) **fait gagner ou perdre de
> l'argent** au backtest, en exécutant le pipeline de production réel **avec** et **sans** cette règle,
> sur le même dataset / la même fenêtre / les mêmes configs.

---

## 0. Méthode

1. **Seam de recherche minimal** ajouté à la production, selon le précédent explicite du Lot 14.10
   (`EntryTriggerBuilder.AmbiguityGateThreshold`) :
   - `PipelineParameterOverrides` : nouveau champ nullable `AblateStructuralBreakRegimeRule` (bool?).
   - `BacktestEngine.RunSignalPipeline(…, overrides)` : si `overrides.AblateStructuralBreakRegimeRule == true`,
     la liste de règles du `DecisionEngine` **retire uniquement `StructuralBreakRule`** ; les 4 autres
     règles et leur ordre sont inchangés.
   - **Non-régression** : `PipelineParameterOverrides.None` (tout appelant non-recherche) et le drapeau
     à `null`/`false` reproduisent la production **bit-for-bit** (`RiskResult.DeterministicHash`
     identique — assertion du test). Le champ n'est **jamais** lu par
     `CalibrationParameterBinding` : aucun `CalibrationParameterSet` ne peut l'atteindre.
2. **Test additif isolé** :
   `Tests/Research/StructuralBreakRegimeCost/StructuralBreakRegimeCostAuditTests.cs` — 4 exécutions de
   `BacktestEngine.RunFullBacktestWithRisk` sur le dataset Yahoo `MES=F` M5 partagé :
   - A = production (overload 7 args) ;
   - B = drapeau `false` → `Assert.Equal(A.hash, B.hash)` (non-régression) ;
   - C = drapeau `true` (ablation) ;
   - D = ablation rejouée → `Assert.Equal(C.hash, D.hash)` (déterminisme du chemin ablé).
   Configs identiques entre toutes : warmup 128, `MeasurementConfiguration.Create(10, {0.001,0.002})`,
   `ExecutionConfiguration.Create(10)`, `PnLConfiguration` quantité 1, **coûts désactivés**, **risque
   désactivé** (isole l'effet de la règle de décision ; PnL brut).
   Sorties : `Tests/Research/StructuralBreakRegimeCost/Output/`.

> Pourquoi un seam et pas un contrefactuel purement externe : pour une barre dont le gagnant
> production est `StructuralBreak`, le pipeline de production **ne calcule jamais** le `DynamicZScore`
> (le `ScientificModelRegistry` ne résout `DynamicZScoreModel` que pour `MeanReverting`). Sans le seam,
> impossible de connaître la direction (BUY/SELL) — donc le PnL — de ces barres. Avec le seam, elles
> deviennent gagnantes `MeanReverting`, le vrai code calcule alors direction → entrée → mesure →
> exécution → risque → PnL. Le contrefactuel est joué par le pipeline réel, pas ré-implémenté.

---

## 1. Ce que l'ablation change (et ne change pas)

Le Lot 17 a établi que retirer `StructuralBreakRule` **ne touche aucun autre régime** (les gagnants
non-`StructuralBreak` sont inchangés à 100 %). Les 3494 barres `StructuralBreak` se réassignent au
runner-up : ~70 % → `MeanReverting`, ~13 % → `Trending`, ~17 % → `RandomWalk`. Seules les
`→ MeanReverting` peuvent produire un trade.

⇒ Le **delta économique** (ablé − production) est **entièrement attribuable** à `StructuralBreakRule` :
positions ouvertes uniquement dans le run ablé = trades que la règle bloque aujourd'hui.

Assertion structurelle du test : `prod.positions ⊆ ablé.positions` (l'ablation n'ajoute que des trades,
n'en retire aucun).

---

## 2. Résultats

Dataset : Yahoo `MES=F` M5, `ReadyBars = 11298` (fenêtre glissante — l'acquisition a bougé de ~9 barres
entre deux exécutions du test à un jour d'intervalle ; l'écart sur les nombres absolus est marginal et
la conclusion inchangée). Coûts et risque **désactivés** (PnL brut, quantité 1). Chiffres
**déterministes** : double agrégation → hash identique ; runs C et D bit-for-bit identiques.

### 2.1 Non-régression & déterminisme — **PASS**

- `A.hash == B.hash` : drapeau `AblateStructuralBreakRegimeRule = false` **≡ production bit-for-bit**.
- `C.hash == D.hash` : le chemin ablé est **déterministe**.
- `A.hash != C.hash` : l'ablation change bien le résultat (non-trivial).
- `CalibrationParameterBindingTests` : **8/8** (le nouveau champ ne casse pas le binding).

### 2.2 Entonnoir de signal & économie — `sb_regime_cost_summary.csv`

| Métrique | Production (5 règles) | Ablé (sans `StructuralBreakRule`) | Delta (ablé − prod) |
|---|---:|---:|---:|
| ReadyBars | 11298 | 11298 | 0 |
| BUY_CANDIDATE | 1171 | 2731 | **+1560** |
| SELL_CANDIDATE | 1159 | 3000 | **+1841** |
| NO_ACTION | 8968 | 5567 | **−3401** |
| Positions clôturées | 2330 | 5755 | **+3425** (×2.5) |
| Wins | 1731 | 4162 | +2431 |
| Losses | 598 | 1589 | +991 |
| **WinRate %** | **74.29** | **72.32** | **−1.97 pt** |
| **FinalNetPnL** | **−401.80** | **−1846.49** | **−1444.69** (×4.6 la perte) |
| **MaximumDrawdown** | **−1491.53** | **−3481.89** | **−1990.37** (×2.3) |

### 2.3 Churn de positions — `sb_regime_cost_position_churn.csv`

Attribution par `PositionId` (= index de barre-signal, stable entre runs).

| | Positions | Win rate | Net PnL |
|---|---:|---:|---:|
| **Ajoutées** en retirant la règle | **3425** | — | **−1444.69** |
| …dont la barre-signal gagnait `StructuralBreak` en production | **1667** (48.7 %) | 70.85 % | **−1176.32** |
| **Retirées** en retirant la règle (churn du gate d'ambiguïté) | **0** | — | 0 |

> **Effet économique de GARDER `StructuralBreakRule` = `prod − ablé (FinalNetPnL)` = +1444.69**
> (moins de perte) **et +1990.37 de drawdown** (bien moins de drawdown) sur cette fenêtre / config.
>
> Sur ce dataset l'ablation ne fait qu'**ajouter** des trades (0 retirée) : les 1736 flips du gate
> d'ambiguïté du Lot 17 ne se traduisent pas ici par une position perdue. Les 3425 trades débloqués
> perdent **−1445** en agrégat ; les 1667 issus d'anciennes barres `StructuralBreak` ont pourtant un
> win rate de 70.8 % — beaucoup de petits gains, quelques grosses pertes (profil d'EV négative).

---

## 3. Interprétation

**`StructuralBreakRule` PROTÈGE le compte sur ce dataset — elle ne coûte pas, elle épargne.**

- Les deux configurations sont **nettes perdantes** sur cette fenêtre de 59 jours (prod −402, config
  sans coûts ni risque) : la stratégie `MeanReverting` sous-jacente perd de l'argent ici. Ce lot ne
  juge **pas** la stratégie, seulement l'effet **relatif** de la règle.
- Retirer la règle **multiplie par 2.5 le nombre de trades** (2330 → 5755), **baisse le win rate**
  (74.3 → 72.3 %), **multiplie la perte par 4.6** (−402 → −1846) et **double le drawdown**
  (−1492 → −3482).
- ⇒ Les ~3425 trades que `StructuralBreakRule` bloque aujourd'hui sont, **en agrégat, à espérance
  négative** (−1445) sur ce dataset. La règle — bien qu'elle soit dérivée de dimensions sans rapport et
  **ignore toute évidence de rupture structurelle** (Lot 17) — fonctionne empiriquement comme un
  **filtre « ne pas trader ici » utile**.
- Nuance : la borne haute « ~1677 signaux directionnels supprimés » du Lot 17 correspond ici à des
  signaux qu'il est **bénéfique** de supprimer sur cette donnée.
- **Limites** : fenêtre Yahoo glissante, un seul instrument (MES M5), coûts et risque désactivés, une
  seule config, pas de découpage TRAIN/VAL/OOS. Ce n'est **pas** un verdict de calibration — mais la
  direction et l'amplitude de l'effet sont sans ambiguïté.

### 3.1 Réconciliation Lot 17 ↔ Lot 18 et recommandation

| | Lot 17 (structure) | Lot 18 (économie) |
|---|---|---|
| `FusionDimension.StructuralBreak` (D6) | inerte, 0 consommateur | *(non concerné — jamais lu)* |
| `StructuralBreakRule` (régime) | `NO_ACTION_SINK` : gagne 31 %, 0 signal, ~1677 lectures potentielles supprimées | ces ~1667 barres tradées perdraient **−1176** ; total débloqué **−1445** |

Les deux lots sont cohérents : la règle **est** un puits de NO_ACTION (Lot 17) **et** ce puits est
**économiquement bénéfique** (Lot 18) sur cette donnée.

**Recommandation (non engagée) :**
- **Garder `StructuralBreakRule` telle quelle.** Elle ne coûte rien — elle épargne. Aucune action
  comportementale requise. Le point 1 de la discussion préalable (« chiffrer le coût ») est tranché :
  pas de coût, un gain.
- **D6 (`FusionDimension.StructuralBreak`)** : la question de son câblage devient **moins urgente** —
  la règle de régime, même « aveugle » à l'évidence, filtre déjà utilement. La câbler resterait plus
  cohérent scientifiquement mais c'est une repondération calibrée à part entière, et le bénéfice
  marginal n'est plus évident. **Statu quo : garder D6 documentée comme "parkée".**
- **Amélioration de lisibilité (faible risque, indépendante)** : mapper `MarketState.StructuralBreak`
  en `SIGNAL_ONLY` explicite (« régime de rupture — filtre actif, pas de modèle directeur ») plutôt
  qu'en `NO_ACTION / UNSUPPORTED_REGIME` générique. L'utilisateur verrait *pourquoi* la barre est
  filtrée. Zéro changement de trading.
- **Si validation ultérieure souhaitée** : rejouer ce contrefactuel sur d'autres fenêtres /
  instruments / avec coûts et risque activés, en TRAIN/VAL/OOS, avant toute décision de calibration.

---

## 4. Fichiers

### Production modifiés (seam de recherche, non-régression prouvée)

- `Backtest/PipelineParameterOverrides.cs` — `+ bool? AblateStructuralBreakRegimeRule` (défaut null).
- `Backtest/BacktestEngine.cs` — `RunSignalPipeline` : liste de règles conditionnelle (retire
  `StructuralBreakRule` si le drapeau est `true`). Production inchangée pour tout appelant `None`.

### Tests ajoutés

- `Tests/Research/StructuralBreakRegimeCost/StructuralBreakRegimeCostAuditTests.cs` + `Output/*.csv`.

### Non modifiés

`StructuralBreakRule.cs`, `DecisionEngine.cs`, `DecisionArbitrator.cs`, `EntryTriggerBuilder.cs`,
`CalibrationParameterBinding.cs`, tous les moteurs scientifiques : **aucune modification**.

---

## 5. FINAL OUTPUT

```
STATUS: MEASUREMENT COMPLETE
SCOPE: end-to-end economic cost of StructuralBreakRule (the regime decision rule), by ablation
PRODUCTION BEHAVIOUR MODIFIED: NO (research-only defaulted seam; None == production, hash-identical)
CALIBRATION / OPTIMIZATION / REWEIGHTING: NO
ATAS: NOT USED   ORDERS: NONE   DLL DEPLOYED: NO   COMMIT: NO

NON-REGRESSION: PASS (flag=false hash == production hash, bit-for-bit; CalibrationParameterBindingTests 8/8)
DETERMINISM: PASS (ablated run C hash == run D hash; report aggregation hash stable)

RESULT (production vs ablated, Yahoo MES M5 ~59d, costs & risk disabled, quantity 1):
  FinalNetPnL:      -401.80  ->  -1846.49   (delta -1444.69, x4.6 the loss)
  MaximumDrawdown: -1491.53  ->  -3481.89   (delta -1990.37, x2.3)
  ClosedPositions:    2330   ->     5755     (delta +3425, x2.5)
  WinRate:           74.29%  ->    72.32%    (-1.97 pt)
  positions added by ablation: 3425 (net -1444.69) ; removed: 0
    of the added, 1667 (48.7%) were StructuralBreak-winner bars in prod (win rate 70.8%, net -1176.32)

CONCLUSION: StructuralBreakRule PROTECTS the account on this dataset - it does not cost money, it
saves ~+1445 PnL and ~+1990 drawdown by keeping ~3425 net-negative-EV trades out of the book. Both
configs are net-losing (the underlying strategy is unprofitable on this window); this lot measures the
RELATIVE effect of the rule, not the strategy. Not a calibration verdict (single window/instrument/config).

CONCLUSIONS: OBSERVATIONAL ONLY
ROADMAP IMPACT: NONE UNLESS EXPLICITLY APPROVED BY USER
NEXT ACTION: STOP - WAIT FOR USER DECISION

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot18_StructuralBreak_Regime_Rule_Economic_Cost_Report.md
TEST: Tests/Research/StructuralBreakRegimeCost/StructuralBreakRegimeCostAuditTests.cs
```
