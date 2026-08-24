# QDE-012 — Sprint 15.25 — Lot 14.9 — Full Project Calibration & Scientific Audit

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Type** : AUDIT UNIQUEMENT — aucune calibration appliquée, aucun paramètre de production modifié, aucun fichier de production modifié.
**Méthode** : 6 agents de recherche en lecture seule, chacun couvrant un sous-ensemble de la chaîne, plus synthèse manuelle. Chaque affirmation cite un fichier:ligne ou un rapport de lot quand c'est possible. Ce document est conçu pour être lu de façon autonome par une nouvelle conversation.

---

# IQIA PROJECT CONTEXT — HANDOFF

## Project
IQIA — système de trading quantitatif intraday.

## Objective
Produire une décision de trading quantitative complète et déterministe à partir de données de marché, en utilisant des modèles statistiques de détection de régime (stationnarité, persistance, retour à la moyenne, rupture structurelle) fusionnés en une décision, convertie en signal directionnel, plan de trade, dimensionnement de risque et exécution — le tout d'abord validé hors-ligne (Yahoo) puis en conditions réelles (ATAS).

## Production Instrument
MES (Micro E-mini S&P 500, CME). Spécifications réelles confirmées dans le code : TickSize=0.25, TickValue=$1.25, PointValue=$5.

## Primary Timeframe
M5 (5 minutes).

## Runtime
ATAS (plateforme de trading — `IQIAIndicator` dérive de `ATAS.Indicators.Indicator`).

## Research / Backtest Environment
Yahoo Historical Data (`ES=F`/`MES=F`, M5, fenêtre glissante ~45 jours, ~6 200-8 900 barres selon le taux de gap observé).

## Current Architecture

```
Market Data (Yahoo / ATAS)
   → Market Context (OHLC + dérivés, aucun paramètre calibrable)
   → Regime Detection (ADF, KPSS, DFA/Hurst, Half-Life, Variance Ratio, CUSUM, Bai-Perron — mono-fenêtre par modèle)
   → Regime Fusion (Engine.Fusion.EvidenceFusionEngine + 5 IFusionRule, poids "provisoires")
   → Decision (DecisionEngine + 5 règles + DecisionArbitrator : Winner/RunnerUp/Difference/AmbiguityScore)
   → Signal (SignalEngine → ScientificModelRegistry → modèles réels UNIQUEMENT pour MeanReversion : Kalman/OU/DynamicZScore/Volatility/SPRT)
   → Entry (EntryEngine/EntryAssessmentBuilder — seuils sur OverallConfidence, un ratio de complétude d'exécution, PAS une confiance statistique)
   → Entry Trigger (EntryTriggerBuilder — Winner==MeanReverting AND AmbiguityScore<0.95 AND DynamicZScore≠0 → BUY/SELL)
   → TradePlan (TradePlanBuilder — StopLoss TOUJOURS null en production, plafonne à SIGNAL_ONLY)
   → Risk (Engine.Risk.RiskEngine, Lot 10/11 — mécanique validée, AUCUNE valeur de production configurée)
   → Position Sizing (idem, plus Backtest/Risk Lot 14.8 pour le backtest uniquement)
   → Execution (Backtest/Execution Lot 14.5 — fill au Close de la barre de SIGNAL, biais connu et documenté en interne dès le Lot 13, jamais corrigé)
   → Costs (Backtest/Cost Lot 14.7 — DÉSACTIVÉS par défaut partout, y compris dans la campagne de calibration active)
   → P&L (Backtest/Pnl Lot 14.6)
   → Equity / Drawdown (Lot 14.6/14.8 — un point par position FERMÉE, pas de mark-to-market intrabar)
   → Calibration Infrastructure (Backtest/Calibration Lot 14.9 — TRAIN/VALIDATION/OOS, grille, fingerprints ; NE sélectionne ni n'optimise rien)
```

## Completed Lots

| Lot | Objectif | Résultat | Statut | Limites importantes |
|---|---|---|---|---|
| 9 | Seuil `AmbiguityGateThreshold` | 0.95 implémenté, remplace 0.5 (jamais atteignable) | IMPLEMENTED — LIVE ATAS VALIDATION REQUIRED | Méthodologie des Lots 4-8 non redéposée dans le repo ; aucun modèle de coûts appliqué à l'étude |
| 10 | Risk Engine autonome | Moteur pur, déterministe, sans dépendance ATAS | VALIDATED (mécanique) | Aucune valeur de RiskPolicy de production n'existe |
| 11 | Intégration Risk Engine (ATAS live) | UI params + adaptateurs branchés | IMPLEMENTED | Fail-closed : tous les défauts UI = 0 → moteur live rejette systématiquement tant que non configuré |
| 12.1-12.12 | ATAS account/instrument binding, Equity, Replay/Live diagnostics, dashboard | Binding technique complet et testé | TECHNICALLY VALIDATED ONLY | Equity Live dynamique non-nulle **jamais observée** atteignant le Risk Engine sur 5 captures réelles ; `QuantityStep` reste 100% manuel (aucune source ATAS fiable identifiée) |
| 13 | Audit architecture backtest | Identifie 14+ risques (dont L-2 : fill au Close de la barre de signal) | AUDIT COMPLETE | La prescription L-2 (fill à `Open[i+1]`) n'a **jamais été appliquée** par le Lot 14.5 qui a suivi |
| 14.1 | Backtest foundation | Walk déterministe Yahoo→Regime, look-ahead prouvé | VALIDATED (mécanique) | — |
| 14.2 | Yahoo Historical Data Source | Source déterministe, fingerprint SHA-256 | VALIDATED (mécanique) | Taux de gap ~24-29% mesuré ; pas de gestion de roll de contrat |
| 14.3 | Full Signal Pipeline Backtest | Regime→TradePlan rejoué bar par bar, look-ahead prouvé champ-par-champ | VALIDATED (mécanique) | — |
| 14.4 | Scientific Measurement Engine | Return/MFE/MAE/HitRate, formules validées par calcul manuel | VALIDATED (formule) / NOT CALIBRATED (HorizonBars=10, seuils 0.001/0.002) | — |
| 14.5 | Execution Model | TIME_HORIZON exit, fill au Close[i] | VALIDATED (cohérence interne) / **BIAIS CONFIRMÉ** (contredit la prescription L-2 du Lot 13) | Aucune option `Open[i+1]`, aucun flag "optimiste" |
| 14.6 | P&L / Equity Curve Foundation | GrossPnL/Equity/Drawdown | VALIDATED (formule) | Un point par position fermée seulement, pas de mark-to-market |
| 14.7 | Cost / Slippage / Execution Realism | Modèle de coûts complet, formule validée | VALIDATED (formule) / **DÉSACTIVÉ PAR DÉFAUT PARTOUT** | Chiffres "illustratifs", jamais calibrés sur un barème broker réel |
| 14.8 | Risk Backtest Foundation | Réutilise RiskEngine, sizing par position, exposition | VALIDATED (mécanique) | `RiskDistance` toujours fournie manuellement (StopLoss du TradePlan toujours null) ; `OpenRisk` toujours 0 |
| 14.9 | Scientific Calibration Foundation | Framework TRAIN/VALIDATION/OOS, grille, fingerprints | VALIDATED (framework, 61/61 tests) | **`CalibrationParameterSet` n'est jamais automatiquement lié à `CalibrationExperimentSetup`** — aucun binding automatique paramètre→pipeline n'existe (voir Defect Master Table) |

## Current Scientific Status
**PARTIALLY VALIDATED, avec un socle mécanique solide et une couche de calibration/validation empirique très incomplète.** La discipline anti-fabrication de signal (jamais de valeur inventée, statuts explicites plutôt que zéros silencieux, séparation stricte des couches) est réelle et de haute qualité dans TOUT le code audité. Mais presque aucun seuil numérique qui gouverne le comportement réel (poids de Fusion/Decision, seuils Entry/EntryTrigger, fenêtres de régime, coûts, distance de risque) n'a de calibration empirique complète et reproductible depuis le dépôt.

## Current Technical Status
Le pipeline complet Yahoo→Regime→Decision→Signal→Entry→EntryTrigger→TradePlan→Measurement→Execution→Cost→Risk→PnL→Equity→Calibration fonctionne bout-en-bout, de façon déterministe et reproductible, avec 761 tests verts au dernier run complet (2026-08-23, 0 échec, 1 ignoré sans rapport). Le look-ahead est prouvé champ-par-champ pour Regime→TradePlan, et prouvé par agrégats seulement pour Measurement→Risk.

## Known Problems
Voir DEFECT MASTER TABLE ci-dessous — 5 CRITICAL, 12 HIGH.

## Known Missing Components
- Une méthodologie Stop Loss scientifiquement validée (aucune n'existe en production — voir Stop Loss Status).
- Un modèle de coûts activé par défaut.
- Un fill d'exécution réaliste (`Open[i+1]`).
- Un binding automatique paramètre-calibration→configuration-pipeline.
- Des métriques quantitatives standards (Sharpe, Sortino, Profit Factor, etc. — absentes).
- Une preuve de look-ahead champ-par-champ pour Measurement→Risk (seule une preuve agrégée existe).
- Une Equity Live ATAS réellement observée non-nulle atteignant le Risk Engine.

## Calibration Status
Résumé : **aucun paramètre de production de la chaîne de décision n'est aujourd'hui scientifiquement calibré au sens strict (calibration empirique + validation OOS + reproductibilité).** Le seul paramètre ayant une étude empirique OOS documentée est `AmbiguityGateThreshold=0.95` (Lots 4-8), classé PARTIALLY CALIBRATED. Tout le reste (poids Fusion/Decision, fenêtres de régime, seuils Entry/EntryTrigger, HorizonBars/HitThresholds Measurement/Execution, coûts, RiskPolicy, RiskDistance/MaxExposure) est NOT CALIBRATED ou NOT CALIBRATABLE YET.

## Data Available
- Yahoo MES=F/ES=F M5, fenêtre glissante ~45 jours (~6 200-8 900 barres selon gaps), fingerprint SHA-256 déterministe (`HistoricalSeriesFingerprint`).
- Une capture ATAS réelle committée : `Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/ScientificDataset_ES_M5_20260814_165421_ohlcv.csv` (ES, pas MES) — **verdict qualité BLOCKED** (36.5% des bars sont des snapshots de bar en formation, concentrés dans la queue temporelle).
- Aucune donnée de marché réelle n'a servi à une calibration StopLoss aboutie (100% synthétique à ce jour).

## ATAS Validation Status
Mapping instrument/compte, sélection Replay/Live, dashboards : **techniquement validés** (code + tests + 5 captures réelles concordantes). **Equity Live dynamique non-nulle atteignant le Risk Engine : jamais observée** (5/5 captures). Aucune validation live du seuil `AmbiguityGateThreshold`.

## Backtest Validation Status
Pipeline complet testé de façon déterministe sur données synthétiques et Yahoo réel (45 jours). Look-ahead prouvé champ-par-champ pour Regime→TradePlan, par agrégats pour Measurement→Risk. Résultats actuels **optimistes sur au moins deux axes indépendants** : fill au Close de la barre de signal (biais confirmé), coûts désactivés par défaut.

## Risk Engine Status
Moteur (formules) : VALIDATED, 35+ tests. Policy (valeurs de production) : **n'existe pas** — tous les défauts UI sont à 0, le moteur live est fail-closed tant que non configuré manuellement. `RiskPolicy.MinPositionSize` est un champ mort (déclaré, jamais appliqué par `RiskEngine.Evaluate`).

## Stop Loss Status
**Absent en production, sur les deux chemins (live et backtest).** `TradePlan.StopLoss` est toujours `null` car `RiskParameters` n'est jamais fourni à `TradePlanContext` ni dans `IQIAIndicator.cs` ni dans `BacktestEngine.RunSignalPipeline`. Le Backtest Risk Engine (Lot 14.8) contourne ce vide en exigeant une `RiskDistanceConfiguration` fournie EXTERNEMENT (jamais dérivée du TradePlan). Une recherche StopLoss dédiée existe (`QDE-012_StopLoss_Calibration_Protocol.md`, candidats A1/D) mais reste **100% synthétique** — aucune campagne réelle exécutée à ce jour, la seule capture réelle disponible étant `BLOCKED` pour raisons de qualité de données.

## Execution / Cost Status
Exécution : formule validée, mais **fill au Close de la barre de signal — biais confirmé**, contredisant la prescription explicite du Lot 13 (`Open[i+1]`). Coûts : formule validée, mais **désactivés par défaut partout**, y compris dans la campagne de calibration active committée dans le repo. Toute conclusion de performance actuelle est donc optimiste sur ces deux axes indépendants.

## Critical Decisions Already Made
- `AmbiguityScore = Clamp(1 - Difference, 0, 1)` — confirmé exact dans `DecisionArbitrator.cs`.
- `AmbiguityGateThreshold = 0.95` (`Difference > 0.05`) — Lot 9, remplace 0.5 (jamais atteignable, Lots 2-3). Plage `[0.05, 0.06]` jugée robuste OOS (Lots 7-8, 4 fenêtres ES M5, 9066 bars combinés), sans modèle de coûts.
- CUSUM, DFA, ADF, KPSS, Half-Life, Variance Ratio : implémentés avec fenêtres fixes par bar (60/128/30/30/30/30 respectivement pour ADF-KPSS/DFA/HalfLife/VarianceRatio/CUSUM), **aucune trace de calibration empirique de ces fenêtres**.
- Multi-window/consensus/régime dominant : **n'existe pas** dans le code — RegimeEngine est mono-fenêtre par modèle. (Correction d'une hypothèse initiale du brief d'audit : il n'y a pas de fenêtres "20/50/80" génériques.)
- MES/ES : jamais de logique spécifique au symbole câblée en dur en production (vérifié par grep exhaustif) — tout provient dynamiquement d'ATAS ou d'une configuration explicite.
- Risk Engine : fail-closed par construction (Equity=0 → rejet).
- Fill de backtest au Close de la barre de signal : décision du Lot 14.5, **en contradiction avec la prescription du Lot 13** (jamais résolue).

---

# EXECUTIVE AUDIT SUMMARY

### Overall Status
Le projet dispose d'une **architecture mécaniquement solide, honnête et bien testée**, mais d'une **couche de calibration/validation scientifique encore embryonnaire**. Le déterminisme, l'absence de fabrication de valeur, et la séparation des responsabilités sont exemplaires. La confiance qu'on peut accorder aux RÉSULTATS produits aujourd'hui (P&L, taux de succès, robustesse d'un seuil) doit rester prudente : au moins deux biais structurels connus (fill optimiste, coûts désactivés) et une quasi-absence de calibration empirique des poids/seuils/fenêtres affectent toute conclusion de performance.

### Scientific Readiness
**FAIBLE À MOYENNE.** Deux modèles statistiques (Half-Life, Variance Ratio) sont réellement validés contre des références externes indépendantes (scikit-learn/NumPy, tolérance 1e-6) — un vrai étalon d'excellence à répliquer ailleurs. ADF/KPSS/DFA sont validés qualitativement (direction, valeurs critiques textbook) mais leurs références numériques externes (Statsmodels/nolds) restent des placeholders jamais remplis. CUSUM manque de citation théorique pour sa propre formule de seuil. Tous les poids de Fusion/Decision sont explicitement "provisoires". Un seul seuil de décision (`AmbiguityGateThreshold=0.95`) a une étude empirique OOS documentée, mais incomplète (pas de modèle de coûts, pas de validation live).

### Technical Readiness
**ÉLEVÉE.** 761 tests verts, déterminisme prouvé, look-ahead prouvé pour la majorité de la chaîne, fingerprints reproductibles partout, isolation de run prouvée, aucune dépendance ATAS dans le chemin backtest. C'est la partie la plus mature du projet.

### Calibration Readiness
**FAIBLE.** Le laboratoire (Lot 14.9) existe et fonctionne, mais (a) n'a jamais servi à calibrer quoi que ce soit, (b) contient un défaut architectural qui empêcherait une grille de réellement faire varier le pipeline sans câblage manuel supplémentaire, et (c) hérite des deux biais non résolus (fill optimiste, coûts désactivés) qui invalideraient toute calibration tentée aujourd'hui sans correction préalable.

### Biggest Strengths
1. Discipline "jamais fabriquer une valeur" appliquée de façon cohérente sur tout le pipeline (TradePlan refuse d'inventer SL/TP, RiskEngine fail-closed, statuts explicites NoData/InsufficientFutureData plutôt que des zéros silencieux).
2. Look-ahead prouvé par test exécutable (pas seulement affirmé) pour Regime→TradePlan, avec deux méthodes indépendantes (troncature + perturbation).
3. Half-Life et Variance Ratio : validation numérique externe réelle (scikit-learn/NumPy), un standard de preuve rarement atteint même dans l'industrie.
4. Traçabilité historique riche et honnête (chaque lot documente ses propres limites, y compris le Lot 9 qui admet lui-même l'absence de modèle de coûts et de validation live).

### Biggest Weaknesses
1. **Biais de fill d'exécution au Close de la barre de signal** — un défaut identifié en interne (Lot 13) et jamais corrigé au Lot 14.5.
2. **Coûts désactivés par défaut partout**, y compris dans la campagne de calibration active.
3. **Aucune méthodologie Stop Loss de production** — bloque le Risk Engine réel (RiskDistance toujours fournie à la main) et TradePlan (StopLoss toujours null → jamais PLAN_READY).
4. **`OverallConfidence`** est un ratio de complétude d'exécution de modèles, pas une confiance statistique, mais alimente 5 seuils de production comme si c'en était une.
5. **Poids de Fusion/Decision jamais calibrés** ("provisoires" au sens propre du terme dans le code).

### Critical Unknowns
- Combien d'épisodes de régime indépendants 45 jours de MES M5 contiennent réellement (estimation raisonnée : quelques dizaines une fois divisé TRAIN/VALIDATION/OOS — jamais mesuré formellement).
- Si une Equity ATAS Live dynamique non-nulle atteindra un jour le Risk Engine (jamais observé sur 5 captures).
- Si le comportement du seuil `AmbiguityGateThreshold=0.95` reste robuste net de coûts de transaction réels.
- L'origine exacte/justification théorique de la formule de seuil CUSUM (`h=σ√(2N ln N)`) — non citée dans le code.

### Critical Defects
Voir DEFECT MASTER TABLE — 5 items CRITICAL : (1) fill au Close[i], (2) coûts désactivés par défaut, (3) absence totale de RiskPolicy de production (fail-closed), (4) absence de binding automatique Calibration→Setup, (5) absence de méthodologie Stop Loss combinée à l'usage obligatoire d'une RiskDistance arbitraire pour tout sizing par risque.

### Highest Priority Calibration
Aucune calibration de paramètre de décision (seuils, poids, fenêtres) n'est prioritaire tant que les défauts structurels (fill, coûts) ne sont pas corrigés — calibrer sur un pipeline biaisé produirait des résultats non transférables. Une fois ces défauts corrigés, la priorité scientifique est la **méthodologie Stop Loss** (elle conditionne le Risk Engine ET TradePlan simultanément).

### Highest Priority Technical Fix
**Corriger le biais de fill d'exécution (`Close[i]` → `Open[i+1]`, ou a minima l'étiqueter "optimiste" dans chaque résultat)** — c'est un défaut déjà identifié par le projet lui-même (Lot 13, L-2) et non corrigé ; il fausse silencieusement TOUTE conclusion de performance en aval.

---

# CHAIN AUDIT — MODULE PAR MODULE

*(Synthèse distillée des 6 rapports d'agents ; le détail complet — formules, citations ligne par ligne, tests exacts — est disponible dans les rapports de lot individuels référencés à chaque section. Toutes les classifications suivent les échelles définies dans ce document.)*

## 1. Market Data (Yahoo)
- **Role** : unique source de données historiques externes (`Core/MarketData/Yahoo/YahooHistoricalBarSource.cs`).
- **Inputs** : symbol (ES/MES), timeframe (M5 uniquement), from/to (UTC).
- **Outputs** : `HistoricalSeries` (Provider="Yahoo(Continuous)", TimeZone="UTC").
- **Parameters** : `DefaultMaxChunkSpanDays=59` (contrainte externe Yahoo mesurée, TYPE C) ; symbol/timeframe maps (tables fermées, vérifiées par appel réseau réel, datées "2026-08-22").
- **Scientific Status** : TECHNICALLY VALIDATED ONLY (mapping/parsing prouvés, mais aucune analyse indépendante de qualité type `RealMarketQualityAnalyzer` appliquée à Yahoo).
- **Calibration Status** : NOT APPLICABLE (source de données, pas un paramètre).
- **Risk** : taux de gap mesuré ~24-29% (coupures maintenance CME quotidiennes) ; aucune gestion de roll de contrat (RISK-14, hérité du Lot 13, non résolu) ; timezone UTC pure, divergente du chemin ATAS live (`Unspecified`).
- **Test Status** : 66/66 tests + 3 tests réseau réels PASS.

## 2. Historical Data
- **Role** : contrat provider-agnostique (`HistoricalBar`, `HistoricalSeries`), valide et REJETTE, ne répare jamais.
- **Finding clé** : aucun contrôle de RÉGULARITÉ d'espacement temporel au niveau série — seul l'ordre strict est vérifié. La détection de gap n'existe qu'au niveau du parser Yahoo (créneaux explicitement nuls), jamais comme propriété vérifiée de la série finale.
- **Scientific Status** : VALIDATED pour ce qu'elle vérifie réellement (structure, ordre, positivité).
- **Calibration Status** : NOT APPLICABLE.
- **Risk** : MEDIUM — une source de données future moins disciplinée que Yahoo pourrait produire une série "valide" au sens du type mais silencieusement trouée.

## 3. Market Context
- **Role** : assemblage canonique OHLC→MarketContext, un seul point de construction partagé live/backtest (`Core/MarketContextFactory.cs`).
- **Finding** : aucune statistique calculée, aucun paramètre calibrable — arithmétique déterministe pure (Median, TypicalPrice, ElapsedMinutes). SessionInfo est un placeholder vide documenté (pas de logique session/timezone).
- **Scientific/Calibration Status** : NOT APPLICABLE.
- **Risk** : LOW.

## 4. Volatility
Deux modèles distincts et sans lien fonctionnel :
- **`VolatilityEvidence`** (Regime) : ACF(|rendements|) lag 1, seuil `IsClustering = ACF>0.05`, fenêtre W=30/MinN=20 — **jamais consommé par Fusion** (orphelin, display-only). TECHNICALLY VALIDATED ONLY, NOT CALIBRATED, Type C.
- **`VolatilityModel`** (stack MeanReversion) : fenêtres 20/20, seuils de classification 0.9/1.1/0.33/0.66 — cœur de la classification LOW/MEDIUM/HIGH utilisée par le signal réel. Causalité (look-ahead) solidement prouvée ; seuils de classification eux-mêmes non justifiés. TECHNICALLY VALIDATED ONLY, NOT CALIBRATED, Type D pour les seuils.

## 5. Regime Detection — les 6+2 modèles d'évidence

| Modèle | Fenêtre prod. | Formule validée contre référence externe ? | Statut scientifique | Calibration fenêtre |
|---|---|---|---|---|
| ADF | 60/30 | Valeurs critiques MacKinnon (1994) citées et re-vérifiées ; magnitude numérique jamais comparée à Statsmodels (placeholder vide) | PARTIALLY VALIDATED | NOT CALIBRATED |
| KPSS | 60/30 | Valeurs critiques Kwiatkowski (1992) citées et re-vérifiées ; magnitude jamais comparée à Statsmodels (placeholder vide) ; déficit de puissance à T=100 honnêtement documenté | PARTIALLY VALIDATED | NOT CALIBRATED |
| DFA/Hurst | 128/80 | Direction qualitative + invariance d'échelle prouvées mathématiquement ; magnitude jamais comparée à `nolds` (placeholder vide) | PARTIALLY VALIDATED | NOT CALIBRATED |
| Half-Life | 30/20 | **Oui — scikit-learn, tolérance 1e-6/1e-4, référence réelle non-placeholder** | **VALIDATED** | NOT CALIBRATED |
| Variance Ratio | 30/20 | **Oui — NumPy, tolérance 1e-6, référence réelle** | **VALIDATED** | NOT CALIBRATED |
| CUSUM | 30/20 | Recursion S+/S- validée contre référence Python ; **formule du seuil h/k sans aucune citation théorique** | PARTIALLY VALIDATED | NOT CALIBRATED |
| Bai-Perron | 128/64 | Non auditée en détail dans ce lot (hors du périmètre agent, non contredite) | UNKNOWN | NOT CALIBRATED |
| HurstEvidence (proxy VR) | 30/20 | Diagnostic parallèle à DFA, **jamais consommé par Fusion** (orphelin) | TECHNICALLY VALIDATED ONLY | NOT CALIBRATED |

**Finding architectural majeur (corrige une hypothèse du brief d'audit)** : `RegimeEngine.Collect` est un orchestrateur mono-fenêtre par modèle — **aucune logique de multi-fenêtre, fenêtre dominante, consensus, stabilité/intensité ou stratégie recommandée n'existe** dans `Engine/Regime/` (confirmé par grep exhaustif). Les fenêtres réelles ne sont pas "20/50/80" mais 60(ADF/KPSS)/128(DFA)/30(HalfLife/VarianceRatio/CUSUM)/128(Bai-Perron) — toutes des constantes privées sans justification empirique documentée, exprimées en nombre de barres (donc implicitement couplées au timeframe M5).

## 6. Regime Fusion
- **Deux classes `EvidenceFusionEngine` distinctes** — `Engine.Regime.Core.EvidenceFusionEngine` est un **stub mort** (retourne toujours Unknown/0, jamais instancié) ; `Engine.Fusion.EvidenceFusionEngine` est la vraie implémentation utilisée partout (live + backtest), avec 5 `IFusionRule`.
- Tous les poids (Scientific/Quality : 0.80-0.90/0.10-0.20 selon la règle, `HalfLifeDecayScale=10`) portent le commentaire explicite **"provisoire(s)"** dans le code source lui-même.
- `FusionConfiguration` (mécanisme prévu pour rendre ces poids calibrables) existe mais est **vide et non branché** (`EnableCalibration=false`).
- **Statut scientifique** : NOT VALIDATED (poids). **Calibration** : NOT CALIBRATED.
- **Point de vigilance documentaire** : `QDE-012_Sprint_15.25_Lot9...` mis à part, `QDE-006_Fusion_Architecture_Audit.md` note les dimensions "Validated" (9.6/10) sur base d'un audit purement CONCEPTUEL (indépendance/orthogonalité), sans aucune donnée empirique — risque de sur-interprétation si lu isolément comme une validation statistique.

## 7. Decision Engine
- 5 règles (`StableRangeRule`, `MeanRevertingRule`, `TrendingRule`, `StructuralBreakRule`, `RandomWalkRule`), chacune : `finalScore = Clamp(0.90×scientificScore + 0.10×qualityScore, 0,1)`, poids internes tous commentés "Provisional".
- `NeutralMissingEvidenceValue=0.5` : correctif documenté et testé (Sprint 14, DEC-01/FUS-02) — un vrai bug historique (transformer "aucune preuve" en "confirmation de l'hypothèse inverse") a été trouvé et corrigé, couvert par 8 tests. **Résolu.**
- Code mort mineur : `if (finalScore > builder.Confidence)` toujours vrai (builder neuf par règle) — cosmétique, sans effet fonctionnel.
- **Statut scientifique** : UNVALIDATED (poids). **Calibration** : NOT CALIBRATED.

## 8. Direction / AmbiguityScore
- Formule confirmée exacte dans le code : `AmbiguityScore = Clamp(1 - Difference, 0, 1)`, `Difference = Winner.FinalScore - (RunnerUp?.FinalScore ?? 0)`.
- Arithmétique validée par tests (`Tests/Decision/DecisionArbitrationTests.cs`, 4 scénarios, précision 1e-12).
- Le CHOIX de cette forme (linéaire, `1-Difference`) n'a aucune justification théorique documentée — Type D/E.
- **Statut scientifique** : TECHNICALLY VALIDATED (implémentation), pas de justification du choix de forme.

## 9. Signal / Entry
- `ScientificModelRegistry` ne câble de vrais modèles QUE pour `MeanReversionMethodology` (Kalman/OU/DynamicZScore/Volatility/SPRT) — toutes les autres méthodologies retournent des stubs (`Score=0`, `"Scientific model placeholder"`).
- **`OverallConfidence` = SuccessfulModels/5 (liste codée en dur)** — un RATIO DE COMPLÉTUDE D'EXÉCUTION, pas une confiance statistique. Pour tout régime ≠ MeanReverting, ce ratio est mécaniquement 0.0, bloquant systématiquement l'Entry stage. Alimente ensuite directement 5 seuils en aval (Entry: 0.9/0.6/0.3 ; EntryTrigger: 0.70/0.55) comme si c'était une vraie confiance — **finding HIGH sévérité, risque de mauvaise interprétation**.
- Aucun Z-score/DFA/ADX explicite lu directement par Entry/EntryTrigger — uniquement `OverallConfidence` (ratio) et `DynamicZScore` (Kalman).
- **Statut scientifique** : les seuils Entry (0.9/0.6/0.3) sont Type D, NOT CALIBRATED, UNVALIDATED.

## 10. Entry Trigger
- Condition confirmée : `Winner==MeanReverting AND AmbiguityScore<0.95 AND DynamicZScore≠0 (disponible)` → BUY/SELL, sinon NO_ACTION/WATCH.
- **AmbiguityGateThreshold=0.95** — voir deep-dive dédié ci-dessous.
- Seuils READY (`_scientificConfidence>=0.70` pour QUALIFIED, `>=0.55` pour HIGH_PRIORITY) — même métrique `OverallConfidence` trompeuse ; ordre contre-intuitif (HIGH_PRIORITY plus bas que QUALIFIED) non expliqué mais probablement voulu.
- **Tests de la condition jointe** : `DecisionDirectionCoherenceTests.cs` (16 tests) + `DirectionEndToEndTests.cs` (3 tests) prouvent le CÂBLAGE LOGIQUE correct (frontières exactes 0.05/0.06 testées), avec des `DecisionResult` construits À LA MAIN — ils ne prouvent PAS que ces conditions se produisent ensemble fréquemment sur données réelles (question empirique traitée par les Lots 2-8, non redéposés).

## 11. AmbiguityGateThreshold = 0.95 — DEEP DIVE (finding le plus consequential du pipeline de décision)

**Historique complet vérifié** (commentaire de code + `QDE-012_Sprint_15.25_Lot9_Threshold_Implementation_Report.md`) :
- Original 0.5 → jamais atteint sur aucune capture ATAS réelle analysée (Lots 2-3).
- Lot 4 (5 datasets ES M5 réels) : plafond réel de `Difference` restreint à Winner==MeanReverting ≈ 0.089-0.094 — 0.5 structurellement inatteignable.
- Lot 5 : à `Difference>0.05`, conversion candidat→direction 100%.
- Lots 7-8 (OOS, **4 fenêtres indépendantes du calibrage**, 9066 barres combinées) : `ROBUST RANGE 0.05–0.06` — robustesse **structurelle/statistique**, PAS une preuve de rentabilité nette (aucun modèle de coûts disponible à l'époque).
- Lot 9 : retient 0.05 (`AmbiguityScore>=0.95`), verdict officiel `IMPLEMENTED — LIVE ATAS VALIDATION REQUIRED`.

**Limite majeure de traçabilité** : les rapports détaillés des Lots 2-8 ne sont **pas déposés séparément dans le repo** — seul un résumé narratif existe dans le rapport Lot 9. Méthodologie non entièrement reproductible depuis le dépôt seul.

**Classification honnête** :
- Scientific Status : **PARTIALLY VALIDATED** (robustesse structurelle démontrée hors-échantillon avec vraie séparation train/OOS ; aucune preuve de rentabilité nette, aucune validation live).
- Calibration Status : **PARTIALLY CALIBRATED**.
- Type de paramètre : **entre A et B** — calibration empirique réelle avec séparation OOS authentique, mais fonction objectif = "produire un candidat directionnel observable", **pas** "maximiser un critère de performance nette de coûts". Fait méthodologique à énoncer platement.
- Overfitting risk : **POSSIBLE** (pas CONFIRMED : vraie séparation OOS documentée ; pas NOT OBSERVED non plus : le critère de sélection diffère d'un critère de rentabilité).
- Instrument : étude menée sur **ES**, la production cible **MES** (même indice sous-jacent, tick/valeur différents, microstructure non re-testée).

## 12. TradePlan
- Confirmé sur les DEUX chemins d'appel (`IQIAIndicator.cs:602` live, `BacktestEngine.cs:328` backtest) : `TradePlanContext` construit avec **2 arguments seulement**, `RiskParameters` toujours `null`.
- `StopLoss = context.RiskParameters?.StopLoss` → toujours `null` → `PLAN_READY` **structurellement inatteignable en production actuelle** (ni live, ni backtest) — seulement démontré par tests avec paramètres injectés à la main.
- Statuts réalistes en pratique : `NO_TRADE` (cas dominant), `SIGNAL_ONLY` (seul statut atteignable avec une direction valide). `PLAN_BLOCKED` inatteignable aujourd'hui.
- **Statut scientifique** : VALIDATED pour la logique de construction (ne fabrique jamais de valeur) ; NOT APPLICABLE pour StopLoss/PositionSize (absents structurellement).

## 13. Stop Loss
- **Absent en production.** Aucune méthodologie de stop n'alimente TradePlan ni le Risk Engine live.
- Informations disponibles pour une future méthodologie : MAE disponible (Measurement, Lot 14.4), MFE disponible, volatilité disponible (VolatilityModel), regime disponible (Decision.Winner par bar), pas d'ATR implémenté nulle part (grep négatif confirmé par l'agent Decision/Signal).
- Recherche StopLoss dédiée (`QDE-012_StopLoss_Calibration_Protocol.md`, candidats A1 Volatility Stop / D Mean-Reversion Invalidation, formule `SL=EstimatedEquilibrium∓k×InnovationStd` locquée) — **100% synthétique à ce jour**. Sprint 15.16 : verdict `INSUFFICIENT REAL DATA`. Sprint 15.18 : capture réelle disponible mais **`SCIENTIFIC_ADMISSIBILITY=BLOCKED`** (36.5% des bars = snapshots de bar en formation, concentrés exactement dans la portion qu'un split OOS réserverait). Sprint 15.19 : bug de capture corrigé, mais aucune nouvelle campagne réelle exécutée depuis. Sprint 15.14 : calibration exploratoire de `k` sur données **synthétiques uniquement**, verdict `K_SELECTED: NO` — aucun k retenu pour production.

## 14. Risk Engine (mécanique)
- `Engine/Risk/RiskEngine.cs` : fonction pure, sans état, sans dépendance ATAS. Formules tracées et vérifiées : RiskDistance, RiskPerUnit=RiskDistance×PointValue, RiskBudget=min(Equity×%, montant max, daily loss remaining, open risk remaining), PositionSize=floor(budget/RiskPerUnit) arrondi/clampé, RiskRewardRatio.
- **35 tests unitaires + 8 tests d'intégration + 4 tests d'indépendance des raisons de rejet, tous verts.**
- **Statut scientifique (MOTEUR)** : **VALIDATED** — chaque formule prouvée par exemple chiffré exact.
- **Defect confirmé** : `RiskPolicy.MinPositionSize` déclaré mais **jamais lu** par `RiskEngine.Evaluate` (seul usage : hash de fingerprint) — champ mort, risque de faux sentiment de sécurité.

## 15. Risk Policy (valeurs)
- **Aucune valeur de production n'existe** — tous les paramètres UI `Risk*`/`RiskPolicy*` ont pour défaut 0/0.0, donc "non configuré". Le moteur live est **fail-closed** par construction (`CurrentEquity==0 → INVALID_EQUITY`).
- Les seules valeurs numériques observées (1%/2%, MinRiskReward=2.0) sont des fixtures de test, jamais promues en défaut de production ni déclarées dans un config/appsettings.
- **Statut scientifique** : UNKNOWN (rien à évaluer, non configuré). **Calibration** : NOT CALIBRATED (Type D si jamais promu tel quel).
- **Distinction explicite ENGINE CORRECT vs POLICY CALIBRATED** appliquée à chaque champ — voir tableau détaillé dans le rapport agent source ; verdict uniforme : moteur correct, policy inexistante.

## 16. Position Sizing / Instrument Specification
- `TickSize`/`TickValue` (MES: 0.25/1.25, réel CME confirmé) : dérivés dynamiquement d'ATAS `Security` en live, jamais hardcodés en production (grep exhaustif : 0 littéral `"MES"`/`"ES"` de sizing en production).
- `MinQuantity`/`MaxQuantity` : mixte ATAS (`Security.LotMinSize/Max`) / manuel.
- `QuantityStep` : **exclusivement manuel** — un résolveur ATAS existe mais est délibérément non câblé (Lot 12.6, "STOP RULE" documentée : `Security.LotSize` par défaut vaut 1, indiscernable d'une vraie valeur MES=1).
- `ContractMultiplier` : jamais fourni en production.
- **Statut scientifique** : VALIDATED (mapping mécanique Tick/Point) ; UNKNOWN pour Min/Max/QuantityStep tant que non configuré.

## 17. Backtest Risk Configuration (Lot 14.8)
- `RiskDistanceConfiguration` : requiert une distance de stop fournie EXTERNEMENT (jamais dérivée du TradePlan, puisque `TradePlan.StopLoss` est toujours null — voir §12/§13). Sans cette entrée, `PositionRiskEvaluator` rejette explicitement (`InvalidRiskDistance`), jamais un stop inventé.
- `MaxExposure` : entièrement nouveau au Lot 14.8, zéro implémentation antérieure, **aucune valeur calibrée**, exemple de rapport (50000$) purement illustratif, sans ancre à une règle de marge réelle.
- **62 tests PASS** (57 synthétiques + 1 réseau). Zéro-régression avec Lot 14.6/14.7 prouvée algébriquement et par test quand risque désactivé.
- **Statut scientifique** : TECHNICALLY VALIDATED ONLY (mécanisme). **Calibration** : NOT CALIBRATABLE YET (RiskDistance dépend d'une méthodologie SL qui n'existe pas ; MaxExposure dépend d'une règle de marge non documentée).

## 18. Execution Model (Lot 14.5)
- TIME_HORIZON = seul exit rule implémenté (`ExitReason` a un seul membre). `HorizonBars=10` utilisé partout.
- **Convention d'entrée : `Close[i]`, la même barre que le signal** — CONFIRMÉ comme contredisant directement la prescription du propre audit d'architecture antérieur du projet (`QDE-012_Sprint_15.25_Lot13_Backtest_Architecture_Audit_Report.md:1140`, défaut L-2 : *"Entrer à Close[i] alors que le signal est dérivé de Close[i]"* → défense prescrite : *"Fill à Open[i+1] ; mode FillAtSignalClose doit être étiqueté optimiste"*). Le Lot 14.5 implémente exactement la convention flaggée, sans option alternative ni étiquette.
- Sortie = `Close[i+HorizonBars]` (THEORETICAL_CLOSE_EXIT), honnêtement documentée comme non réaliste.
- **35 tests + 1 Yahoo, tous PASS** pour la cohérence interne (Return bit-exact identique entre Measurement et Execution).
- **Statut scientifique** : VALIDATED (cohérence interne) / **UNVALIDATED, biais CONFIRMÉ** (réalisme du fill).

## 19. Measurement (Lot 14.4)
- Return/MFE/MAE/HitRate sur `HorizonBars` barres futures, formules validées par calcul manuel (précision 9 décimales), équivalence prouvée avec un scan direct High/Low.
- `HorizonBars=10`, `HitThresholds={0.001,0.002}` — utilisés PARTOUT, jamais dérivés d'une analyse (ATR, distance de stop, volatilité M5 réelle). Le rapport Lot 14.4 le reconnaît lui-même explicitement.
- **44/44 tests + 1 Yahoo, PASS.**
- **Statut scientifique** : VALIDATED (formule) / NOT CALIBRATED (valeurs).

## 20. P&L / Equity / Drawdown (Lot 14.6/14.8)
- Métriques existantes : GrossProfit/GrossLoss, NetGrossPnL, WinRate, AveragePnL/MedianPnL, FinalGrossPnL, MaximumDrawdown, FinalEquity, EquityCurve.
- **Métriques manquantes confirmées absentes** : Sharpe, Sortino, Calmar, Profit Factor (calculable mais non exposé), Average Win/Loss séparés, Max Consecutive Losses, Time-in-Market, Turnover, Expectancy/R-multiple, VaR, drawdown mark-to-market intrabar/concurrent.
- **Limitation structurelle** : Equity Curve = un point par position FERMÉE — jamais de mark-to-market intrabar/continu ; `MaximumDrawdown` sous-estime potentiellement le vrai drawdown si des positions se chevauchent.
- **46/46 (Lot 14.6) + 57/57 (Lot 14.8) tests + Yahoo, tous PASS.**
- **Statut scientifique** : VALIDATED (formules des métriques existantes) ; incomplétude reconnue et documentée, pas un oubli silencieux.

## 21. Backtest (vue d'ensemble)
- Déterminisme, isolation de run, reproductibilité : **VALIDATED** par tests dédiés (`BacktestDeterminismTests`, `BacktestRunIsolationTests`).
- Warmup : compteur d'index explicite, n'affecte que le `Status` (Warmup/Ready), jamais la production du signal.
- Look-ahead : **VALIDATED champ-par-champ pour Regime→TradePlan** (deux méthodes indépendantes, troncature + perturbation). **VALIDATED PAR AGRÉGATS SEULEMENT pour Measurement→Execution→Cost→Risk→PnL** (`CalibrationLookAheadTests`, compare des métriques agrégées, pas des résultats intermédiaires bar-par-bar/position-par-position) — trou de couverture de test identifié, pas un défaut prouvé.
- **`BacktestScenario.Window`/`CalibrationWindow` ne filtrent JAMAIS les bars traités** — confirmé par grep exhaustif, c'est de la métadonnée d'identité pure. Le découpage TRAIN/VALIDATION/OOS se fait entièrement par tranchage post-hoc par timestamp, ce qui est correct ÉTANT DONNÉ la garantie de look-ahead amont, mais hérite de sa limite (agrégée au-delà de TradePlan).

## 22. Calibration Infrastructure (Lot 14.9)
- Framework TECHNIQUEMENT VALIDÉ (61/61 tests : déterminisme, isolation, look-ahead agrégé, fingerprints, sérialisation JSON).
- **Defect CRITICAL confirmé par lecture directe du code** : `CalibrationParameterSet` (ex. `AmbiguityThreshold=0.95`) est **totalement déconnecté** de `CalibrationExperimentSetup` (Measurement/Execution/PnL/Cost/Risk configuration) — aucune méthode ne lit une valeur du ParameterSet pour construire/modifier le Setup. Le ParameterSet sert UNIQUEMENT à (a) faire partie du fingerprint/ExperimentId, (b) documentation. **Aucun binding automatique n'existe.** Une grille de N `CalibrationParameterSet` sans câblage manuel correspondant produirait N expériences avec N fingerprints DIFFÉRENTS mais un comportement de pipeline STRICTEMENT IDENTIQUE — un piège silencieux pour tout usage naïf futur de `CalibrationGrid`.
- Ce défaut n'est PAS bloquant pour le Lot 14.9 lui-même (qui ne prétend sélectionner/injecter aucun paramètre — conforme à son brief), mais est **HIGH RISK pour le Lot 14.10+**.
- `CalibrationWindowSet`, `CalibrationWarmupContract`, fingerprints, isolation de run : cohérents et validés par tests exhaustifs.
- **Statut scientifique global** : le FRAMEWORK est VALIDATED ; **aucun paramètre de production n'a été calibré** (conforme à l'intention déclarée du lot).

## 23. ATAS Integration
- Account/Instrument binding : TECHNIQUEMENT VALIDÉ (code + tests + 4-5 captures réelles ES/MES concordantes).
- **Equity Live dynamique non-nulle atteignant le Risk Engine : jamais observée sur 5 captures réelles indépendantes**, y compris une session avec un vrai compte prop-firm financé (Balance=$25 103.92) — le Balance n'a jamais atteint le pipeline car `SourceMode` restait bloqué sur "Replay" (faux positif corrigé depuis au Lot 12.11, mais jamais re-testé en conditions Live confirmées avec Equity non-nulle).
- Sélection Replay/Live (`ATASEquityReplayDetector`) : logique déterministe et testée (28+ cas), mais sa justification amont (`Portfolio.IsReplay()` fiable en toutes circonstances) reste une hypothèse de conception, non une certitude SDK documentée.
- **Ce qui doit rester ATAS-validé** : Equity Live réellement dynamique atteignant RiskEngine (jamais observé) ; existence d'une source native pour `QuantityStep` (confirmée absente sur la version SDK installée, 17 DLL audités) ; rendu visuel des dashboards.
- **Ce qui est désormais backtest-suffisant** : mécanique `RiskEngine.Evaluate` (déterministe, déjà testée exhaustivement) ; mapping Symbol/TickSize/TickValue/PointValue (data-driven, prouvé) ; logique de sélection Replay/Live elle-même (pas sa fiabilité amont) ; comportement TradePlan `SIGNAL_ONLY`.

## 24. Scientific Dataset / Observability
- `Core/Calibration/ScientificDatasetCollector`/`Record`/`SessionWriter` (Sprint 15.17-15.19) — **système SANS RAPPORT avec `Backtest/Calibration/` (Lot 14.9)**, collision de nommage explicitement signalée. Le premier est un enregistreur d'observations ATAS bar-par-bar (OHLCV + contexte scientifique + décision + risk + télémétrie ATAS, 39+ clés `ATAS.*`), le second est un framework d'expérimentation backtest indépendant.
- **Champs manquants pour une reproduction offline complète d'une décision** : `RiskPolicy` complet (8 paramètres) jamais exporté — déjà source de difficulté documentée (Lot 12.4 a dû le déduire indirectement) ; `Security.ConnectorId`/broker non exporté ; `Position.Volume`/PnL non exportés distinctement ; aucune clé `Policy.*`/`Threshold.*` de configuration globale de la stratégie.
- Fingerprinting : manifeste SHA-256 pour les fixtures COMMITTÉES uniquement (pas un mécanisme général pour chaque capture live). Provenance Symbol/TimeFrame/SessionId fiable ; `Timezone` honnêtement marqué "Unspecified" partout.
- Discipline "passive-capture" vérifiée par test dédié (`Instrumentation_IsPassive_RiskEngineResultUnchangedBeforeAndAfterCapture`).

---

# PARAMETER CLASSIFICATION LEGEND (appliquée dans toutes les tables ci-dessous)

- **TYPE A** — Scientifiquement calibré, preuve + validation OOS.
- **TYPE B** — Justifié théoriquement, calibration empirique absente ou incomplète.
- **TYPE C** — Constante d'ingénierie, ne nécessite pas de calibration scientifique (contrainte externe, fait de marché objectif, garde structurelle).
- **TYPE D** — Arbitraire, justification insuffisante.
- **TYPE E** — Origine inconnue avec certitude.

---

# CALIBRATION MASTER TABLE

| Priority | Module | Parameter | Current Value | Unit | Source | Status | Evidence | Calibration Needed | Dependencies |
|---|---|---|---|---|---|---|---|---|---|
| CRITICAL | EntryTrigger | `AmbiguityGateThreshold` | 0.95 | score | `EntryTriggerBuilder.cs:19`, Lot 9 | Type A/B limite, PARTIALLY CALIBRATED | Lots 4-8, OOS 4 fenêtres ES M5, 9066 bars | Oui — modèle de coûts + validation live ATAS | Coûts (Lot 14.7 activés), Fusion/Decision weights |
| CRITICAL | Execution | Convention d'entrée (fill) | `Close[i]` | prix | `ExecutionSimulator.cs:166-192` | Type D, biais CONFIRMÉ | `QDE-012_Lot13...md:1140` (L-2) vs Lot 14.5 | Oui — corriger vers `Open[i+1]` ou étiqueter | Toute métrique Measurement/Execution/PnL/Risk en aval |
| CRITICAL | Cost | `ExecutionCostConfiguration.Enabled` | `false` partout en pratique | bool | `ExecutionCostConfiguration.cs:12-14` + tous sites d'appel réels | Type C (défaut), NOT CALIBRATED | grep exhaustif des sites d'appel | Oui — activer avec des valeurs réelles avant toute conclusion de performance | Barème broker réel |
| CRITICAL | Risk Policy | Tous les champs `RiskPolicy.*` | Aucune valeur prod (défaut UI=0) | %/$/ratio | `IQIAIndicator.cs:186-284` | NOT CALIBRATED / n'existe pas | Fail-closed confirmé par le moteur | Oui — décision de valeur avant tout usage live | — |
| CRITICAL | Calibration Infra | Binding ParameterSet→Setup | Absent | — | `CalibrationExperimentRunner.cs:39-49` | Défaut architectural CONFIRMÉ | Lecture directe du code | Oui — avant tout Lot 14.10 basé sur `CalibrationGrid` | — |
| HIGH | TradePlan/Risk | `TradePlan.StopLoss` | Toujours `null` | prix | `IQIAIndicator.cs:602`, `BacktestEngine.cs:328` | NOT APPLICABLE (absent) | `TradePlanBuilder.cs:57-61` | Oui — méthodologie SL scientifique | Voir §13 |
| HIGH | Backtest/Risk | `RiskDistanceConfiguration` | `None()` par défaut ; exemple 4 ticks | pts | `RiskDistanceConfiguration.cs` | NOT CALIBRATABLE YET | Doc-comment + Lot 14.8 §2 | Oui, après méthodologie SL | StopLoss methodology |
| HIGH | Signal/Entry | `OverallConfidence` (nommage/sémantique) | ratio SuccessfulModels/5 | ratio | `ScientificAssessmentBuilder.cs:100-102` | Mal nommé, PAS une confiance statistique | Lecture directe | Renommer/documenter, pas "calibrer" | 5 seuils Entry/EntryTrigger en aval |
| HIGH | Entry | Seuils OpportunityStatus | 0.9/0.6/0.3 | ratio | `EntryAssessmentBuilder.cs:130,134,138` | Type D, NOT CALIBRATED | Aucun commentaire | Oui | `OverallConfidence` (à corriger d'abord) |
| HIGH | EntryTrigger | Seuils READY | 0.70/0.55 | ratio | `EntryTriggerBuilder.cs:243,250` | Type D, NOT CALIBRATED | Aucun commentaire, ordre non expliqué | Oui | idem |
| HIGH | Fusion | Poids Scientific/Quality (5 règles) | 0.80-0.90/0.10-0.20 | ratio | `Engine/Fusion/Rules/*.cs` | "Provisoire" (auto-déclaré), NOT CALIBRATED | Commentaires de code | Oui | `FusionConfiguration` à brancher d'abord |
| HIGH | Decision | Poids dimensionnels (5 règles) | voir §7 | ratio | `Engine/Decision/Rules/*.cs` | "Provisional", NOT CALIBRATED | Commentaires de code | Oui | Couplé à AmbiguityGateThreshold |
| HIGH | Cost | Slippage/Spread/Commission | 0 (prod) / illustratif (tests) | pts/$ | `Backtest/Cost/*.cs` | NOT CALIBRATED, auto-déclaré "illustratif" | Lot 14.7 §6 | Oui — barème broker réel | — |
| HIGH | Regime | Fenêtres ADF/KPSS/DFA/HalfLife/VR/CUSUM | 60/128/30/30/30/30 | barres | `RegimeEngine.cs:19-30` | NOT CALIBRATED, aucune trace de calibration trouvée | grep exhaustif Documentation/Scientific | Oui — étude de sensibilité | Timeframe M5 (couplage implicite) |
| MEDIUM | Risk Engine | `RiskPolicy.MinPositionSize` | Toujours `null`, jamais lu | contrats | `RiskPolicy.cs:20` vs `RiskEngine.cs` | Champ mort, defect | grep confirmé | Implémenter ou retirer (pas une calibration) | — |
| MEDIUM | Measurement | `HorizonBars` | 10 | barres | `MeasurementConfiguration.cs:16` | Type D, NOT CALIBRATED | Utilisé partout sans dérivation | Oui | ATR/durée de trade réelle |
| MEDIUM | Measurement | `HitThresholds` | {0.001, 0.002} | ratio | `MeasurementConfiguration.cs:20` | Type D, NOT CALIBRATED | idem | Oui | idem |
| MEDIUM | Execution | `HorizonBars` | 10 | barres | `ExecutionConfiguration.cs:17` | Type D, NOT CALIBRATED | idem | Oui | idem |
| MEDIUM | Backtest/Risk | `MaxExposure` | `null` (illimité) ; exemple 50000$ | $ | `BacktestRiskConfiguration.cs:39` | Type D, NOT CALIBRATABLE YET | Lot 14.8 §7 | Oui — règle de marge réelle | — |
| MEDIUM | Instrument | `MinQuantity`/`MaxQuantity`/`QuantityStep` (MES) | Aucune valeur prod | contrats | `InstrumentRiskSpecification.cs` | NOT CALIBRATED | fixtures test uniquement | Décision manuelle, pas calibration scientifique | ATAS `Security.LotSize` non fiable |
| MEDIUM | Instrument | `TickSize`/`TickValue`/`PointValue` (MES) | 0.25/1.25/5 | pts/$ | `InstrumentInfo`, spec CME réelle | Type C (fait objectif) | Correct factuellement, non cross-check formel documenté dans le repo | Non — ajouter juste une citation datée | — |
| LOW | Regime | CUSUM seuil h/k | `σ√(2N ln N)` | — | `CusumStatistics.cs:79-80` | Formule sans citation théorique (Type E) | Recursion validée vs Python, formule source non tracée | Retrouver/documenter la source | — |
| LOW | Regime | ADF/KPSS/DFA magnitude numérique | — | — | `*GoldenDataset.cs` placeholders | NOT VALIDATED (placeholders jamais remplis) | grep "placeholder" | Exécuter scripts Python documentés, remplir | — |
| N/A | Regime | Half-Life formule | OLS closed-form | — | `HalfLifeStatistics.cs` | **VALIDATED (Type A)** | scikit-learn, tol 1e-6/1e-4 | Aucune | — |
| N/A | Regime | Variance Ratio formule | Lo-MacKinlay | — | `VarianceRatioStatistics.cs` | **VALIDATED (Type A)** | NumPy, tol 1e-6 | Aucune | — |

---

# DEFECT MASTER TABLE

| Severity | Module | Problem | Evidence | Impact | Confirmed/Likely/Possible | Required Action | Blocking Calibration? |
|---|---|---|---|---|---|---|---|
| **CRITICAL** | Execution | Fill d'entrée au `Close[i]`, la même barre que le signal — biais déjà identifié en interne (Lot 13, défaut L-2, prescription `Open[i+1]` jamais appliquée) | `QDE-012_Lot13...md:1140` vs `BacktestEngine.cs:424-432`, `ExecutionSimulator.cs:166-192` | Tous les Return/MFE/MAE/PnL/coûts/risque en aval sont construits sur un fill optimiste, non réplicable en temps réel | **CONFIRMED** | Implémenter `Open[i+1]` ou étiqueter tout résultat courant "optimiste" | **OUI — bloque toute calibration en aval** |
| **CRITICAL** | Cost | Coûts désactivés par défaut partout, y compris dans la campagne de calibration active committée | `ExecutionCostConfiguration.cs:12-14`, `Tests/Backtest/Calibration/CalibrationTestFixtures.cs:34` | Tous les résultats actuels sont zéro-coût | **CONFIRMED** | Exiger une passe avec coûts réalistes avant toute conclusion de performance | **OUI** |
| **CRITICAL** | Risk Policy | Aucune valeur de production `RiskPolicy` n'existe (fail-closed par défaut) | `IQIAIndicator.cs:186-284`, `RiskEngine.cs:31-36` | Le Risk Engine live n'a aucun contenu opérationnel tant que non configuré | **CONFIRMED** | Documenter/décider des valeurs avant tout usage live | Non-calibrable tant que non configuré |
| **CRITICAL** | Calibration Infra (Lot 14.9) | `CalibrationParameterSet` jamais lié automatiquement à `CalibrationExperimentSetup` | `CalibrationExperimentRunner.cs:39-49`, `CalibrationFingerprint.cs:47` | Une grille future pourrait produire N expériences aux fingerprints distincts mais au comportement de pipeline strictement identique | **CONFIRMED** | Documenter le contrat "câblage manuel obligatoire" ou construire un mécanisme d'application automatique avant le Lot 14.10 | **OUI, pour toute calibration basée sur `CalibrationGrid`** |
| **CRITICAL** | TradePlan/Risk/StopLoss | `TradePlan.StopLoss` toujours `null` (live et backtest) ; aucune méthodologie SL de production ; `RiskDistance` backtest doit être fournie à la main sans lien avec la volatilité réelle | `IQIAIndicator.cs:602`, `BacktestEngine.cs:328`, `TradePlanBuilder.cs:57-61`, `RiskDistanceConfiguration.cs:8-18` | `PLAN_READY` inatteignable ; tout sizing par risque backtest repose sur un chiffre arbitraire | **CONFIRMED** | Développer/valider une méthodologie SL avant de calibrer le Risk Engine | **OUI — bloque toute calibration Risk/Sizing réaliste** |
| HIGH | Entry/EntryTrigger | `OverallConfidence` = ratio de complétude d'exécution (SuccessfulModels/5), pas une confiance statistique, mais alimente 5 seuils en aval comme si c'en était une | `ScientificAssessmentBuilder.cs:100-102`, `EntryAssessmentBuilder.cs:130-145`, `EntryTriggerBuilder.cs:243,250` | Risque de mauvaise lecture (dashboard, humain) ; seuils calibrés dessus sans assise statistique | Confirmed | Renommer/documenter la sémantique réelle | Fausse toute calibration future basée sur ce champ |
| HIGH | Fusion/Decision | Tous les poids sont explicitement "provisoires"/"Provisional", jamais calibrés ; `FusionConfiguration` (mécanisme de calibration prévu) existe mais non branché | `Engine/Fusion/Rules/*.cs`, `Engine/Decision/Rules/*.cs`, `FusionConfiguration.cs:10-16` | Le score de décision final dépend de poids jamais testés empiriquement | Confirmed | Étude de calibration dédiée, reportée dans le repo | Oui, couplé à AmbiguityGateThreshold |
| HIGH | Regime | CUSUM : formule du seuil h/k sans citation théorique (contrairement à ADF/KPSS/DFA) | `CusumStatistics.cs:79-80` | Impossible de juger la correction théorique de la formule elle-même | Probable | Retrouver/documenter la source (Page 1954 ou dérivation ARL) | Réduit la confiance, non bloquant |
| HIGH | Regime | ADF/KPSS/DFA : références numériques externes (Statsmodels/nolds) jamais remplies (placeholders) | `AdfGoldenDataset.cs`, `KpssGoldenDataset.cs`, `DfaGoldenDataset.cs` | Seule la direction qualitative est prouvée, pas l'exactitude numérique | Confirmed | Exécuter les scripts documentés, remplir les références | Recommandé avant calibration de seuils basés sur ces stats |
| HIGH | AmbiguityGateThreshold | Robustesse OOS démontrée mais sans modèle de coûts ni validation live ATAS | `QDE-012_Lot9...md` §G | Seuil peut être robuste structurellement sans être rentable net | Confirmed (auto-déclaré) | Revalider avec coûts (Lot 14.7 activé) + capture live courte | Oui, pour validation de rentabilité |
| HIGH | ATAS/Equity | Aucune Equity Live dynamique/non-nulle n'a jamais atteint `RiskEngine` sur 5 captures réelles | Lot12.7/12.9/12.10 | Risk Engine ne peut être `ACCEPTED` en conditions réelles tant que non résolu | Confirmed (5 captures concordantes) | Capturer une session Live réelle avec compte financé | Non (backtest n'en dépend pas) |
| HIGH | Scientific Dataset | `RiskPolicy` (8 paramètres) jamais exporté dans le dataset capturé | `Core/Calibration/ScientificDatasetRecord.cs` (grep négatif) | Empêche la reproduction offline complète d'une décision Risk | Likely | Exporter `RiskPolicy` en télémétrie passive | Oui, pour calibration Risk future |
| HIGH | Historical Data | Aucun contrôle de régularité d'espacement temporel au niveau `HistoricalSeries` | `HistoricalSeries.cs:126-144` | Une source future moins disciplinée que Yahoo pourrait produire une série trouée non détectée | Likely | Contrôle de continuité optionnel au niveau série | Latent, non bloquant aujourd'hui |
| HIGH | Market Data | Taux de gap Yahoo ~24-29% (coupures CME) | `QDE-012_Lot14.2...md:82,235` | Distord la suffisance apparente des données si non pris en compte | Confirmed | Documenter dans toute planification de fenêtre | Distord, ne bloque pas |
| MEDIUM | Risk Engine | `RiskPolicy.MinPositionSize` déclaré mais jamais appliqué | `RiskPolicy.cs:20` vs `RiskEngine.cs` | Faux sentiment de sécurité pour un appelant qui le configurerait | Confirmed | Implémenter ou retirer/documenter | Non |
| MEDIUM | Risk (Backtest) | `OpenRisk` toujours forcé à 0 alors que les positions peuvent se chevaucher (Lot 14.5) | `BacktestRiskResultBuilder.cs:19-27,94` | Sous-estime le risque concurrent réel | Confirmed (documenté) | Modèle de portefeuille multi-position (futur lot) | Possible, selon stratégie |
| MEDIUM | PnL/Equity | Equity curve = un point par trade fermé, pas de mark-to-market intrabar | `EquityPoint.cs:12-14` | `MaximumDrawdown` sous-estime potentiellement le vrai drawdown | Likely | Documenter la limite ; envisager mark-to-market futur | Non bloquant, fausse interprétation |
| MEDIUM | Cost | Chiffres slippage/spread/commission "illustratifs", aucun barème broker réel cité | `QDE-012_Lot14.7...md:246-247` | Même activés, ne refléteraient pas un coût réel | Confirmed (auto-déclaré) | Calibrer sur barème réel + Bid/Ask réel | Partiellement |
| MEDIUM | Backtest | Aucun test de look-ahead champ-par-champ pour Measurement/Execution/Cost/Risk/PnL (preuve agrégée seulement) | `CalibrationLookAheadTests.cs:38-68` | Un mismatch localisé pourrait être masqué par l'agrégation | Possible | Ajouter un test analogue à `BacktestSignalPipelineLookAheadTests` pour ces couches | Réduit la confiance, non bloquant |
| MEDIUM | Backtest Engine | `FusionStateManager` peut muter son état avant de lever une exception | `BacktestEngine.cs:179-186` | Risque théorique de contamination inter-bar, jamais observé | Possible | Surveiller (fichier protégé) | Non |
| MEDIUM | StopLoss Research | Aucune campagne de calibration réelle exécutée à ce jour (post-fix Sprint 15.19) | Sprint 15.19 gate `K_SELECTED: NO`, absence de rapport postérieur | Protocole StopLoss reste 100% synthétique en pratique | Confirmed | Relancer capture ATAS + qualité + campagne réelle | Oui, explicitement (le protocole le dit) |
| LOW | Decision Engine | Code mort `if (finalScore > builder.Confidence)` toujours vrai | `DecisionEngine.cs:35-36` | Aucun effet fonctionnel | Confirmed | Nettoyage cosmétique | Non |
| LOW | EntryTriggerBuilder | Seuil READY HIGH_PRIORITY (0.55) plus bas que QUALIFIED (0.70), non expliqué | `EntryTriggerBuilder.cs:243,250` | Lisibilité, pas un bug avéré | Possible | Commentaire de justification | Non |
| LOW | Instrument | `ATASInstrumentQuantityStepResolver` existe, testé, délibérément non câblé | `ATASInstrumentQuantityStepResolver.cs:19-31` | `QuantityStep` reste manuel, bien documenté | Confirmed (décision consciente) | Revisiter si ATAS expose un flag "is populated" | Non |
| LOW | Cost | Tick/Point value MES corrects mais non cross-checkés/cités formellement dans le code | `Tests/Backtest/*/*.cs` | Risque de dérive silencieuse si modifiés sans vérification | Possible | Ajouter commentaire daté (comme `YahooSymbolMap.cs`) | Non |
| LOW | Scientific Dataset | `Security.ConnectorId`/broker non exporté | `ScientificDatasetRecord.cs` | Impossible de savoir quel flux a produit une capture a posteriori | Confirmed | Ajouter au schéma si utile | Non |

---

# SCIENTIFIC EVIDENCE MATRIX

| Component | Technical Tests | Historical Validation | OOS | Replay | Live | Scientific Status |
|---|---|---|---|---|---|---|
| Yahoo Data Source | 66 + 3 réseau, PASS | N/A | N/A | N/A | N/A | TECHNICALLY VALIDATED ONLY |
| Historical Data (contrat) | Présent, cohérent | N/A | N/A | N/A | N/A | VALIDATED (structurel) |
| Market Context | Présent | N/A | N/A | Oui (heuristique ATAS) | Oui | NOT APPLICABLE (pas de modèle) |
| VolatilityEvidence | Qualitatif seulement | Non | Non | N/A | Non | TECHNICALLY VALIDATED ONLY |
| VolatilityModel | Look-ahead prouvé | Non | Non | N/A | Non | TECHNICALLY VALIDATED ONLY |
| ADF | Formule + valeurs critiques | Qualitatif (synthétique) | Non | N/A | Non | PARTIALLY VALIDATED |
| KPSS | idem | idem | Non | N/A | Non | PARTIALLY VALIDATED |
| DFA/Hurst | idem | idem | Non | N/A | Non | PARTIALLY VALIDATED |
| **Half-Life** | Formule + réf. externe scikit-learn | **Oui (1e-6/1e-4)** | Non | N/A | Non | **VALIDATED** |
| **Variance Ratio** | Formule + réf. externe NumPy | **Oui (1e-6)** | Non | N/A | Non | **VALIDATED** |
| CUSUM | Recursion vs Python | Qualitatif | Non | N/A | Non | PARTIALLY VALIDATED |
| Regime Fusion (poids) | Câblage testé | Non | Non | N/A | Non | UNVALIDATED |
| Decision Engine (poids) | Câblage testé | Non | Non | N/A | Non | UNVALIDATED |
| AmbiguityScore (formule) | 4 tests, 1e-12 | N/A | N/A | N/A | N/A | TECHNICALLY VALIDATED |
| **AmbiguityGateThreshold** | Câblage logique testé | **Oui (Lots 4-6, ES M5)** | **Oui (Lots 7-8, 4 fenêtres, 9066 bars)** | Non | **Non** | **PARTIALLY VALIDATED** |
| OverallConfidence / Entry seuils | Câblage testé | Non | Non | N/A | Non | UNVALIDATED |
| EntryTrigger (condition jointe) | 19 tests unitaires | Non (construits à la main) | Non | N/A | Non | LOGIQUE VALIDATED / EMPIRIQUE PARTIALLY |
| TradePlan | Tests A-H exhaustifs | N/A | N/A | Non | Non | VALIDATED (logique) / N/A (SL/sizing) |
| Risk Engine (mécanique) | 35+8+4 tests | N/A | N/A | Non | **Jamais Equity≠0 observée** | VALIDATED (mécanique) |
| Risk Policy (valeurs) | N/A (n'existe pas) | Non | Non | N/A | Non | UNKNOWN |
| Measurement | 44 tests + Yahoo | Yahoo 45j | Non | N/A | Non | VALIDATED (formule) / NOT CALIBRATED (valeurs) |
| Execution | 35 tests + Yahoo | Yahoo 45j (biaisé) | Non | N/A | Non | VALIDATED (cohérence) / biais confirmé |
| Costs | 78 tests | Yahoo (désactivé) | Non | N/A | Non | VALIDATED (formule) / désactivé en pratique |
| PnL/Equity/Drawdown | 46+57 tests + Yahoo | Yahoo 45j | Non | N/A | Non | VALIDATED (formules existantes) |
| Backtest Engine | Déterminisme/isolation prouvés | Yahoo 45j | Partiel (Lot 14.9) | N/A | N/A | VALIDATED (mécanique) |
| Calibration Infra (Lot 14.9) | 61 tests | N/A (laboratoire) | N/A (par design) | N/A | N/A | VALIDATED (framework) |
| ATAS Account/Instrument Binding | Testé + 5 captures | N/A | N/A | **Concordant (5/5)** | Confirmé une fois | TECHNICALLY VALIDATED |
| ATAS Equity Tracking | Testé | N/A | N/A | Vide (5/5) | **Jamais non-nul observé** | UNVALIDATED (disponibilité réelle) |
| Scientific Dataset Collector | Testé (passive-capture) | N/A | N/A | Oui | Oui (1 capture réelle) | TECHNICALLY VALIDATED |
| StopLoss (recherche) | Synthétique uniquement | **BLOCKED** (qualité) | Non | N/A | Non | UNVALIDATED (réel) |

---

# OVERFITTING / DATA LEAKAGE / BIAS AUDIT

## Overfitting
- **AmbiguityGateThreshold=0.95** : **POSSIBLE** (pas CONFIRMED — vraie séparation train(Lots4-6)/OOS(Lots7-8) documentée sur 4 fenêtres indépendantes ; pas NOT OBSERVED — le critère de sélection était "produire un candidat observable", pas "maximiser un critère de performance", ce qui laisse une question ouverte sur la généralisation future).
- **Fenêtres de régime (60/128/30...)** : **POSSIBLE** — aucune étude de sensibilité trouvée, choix non documenté, risque de sur-ajustement au timeframe M5/instrument non exclu ni confirmé.
- **Réutilisation du même dataset Yahoo 45 jours pour développement ET toute future calibration** : **LIKELY** si une calibration Lot 14.10+ utilisait la même fenêtre glissante sans discipline TRAIN/VALIDATION/OOS stricte (le Lot 14.9 fournit l'outil pour l'éviter, mais rien ne l'a encore exercé).
- **Sélection manuelle de périodes/instruments/résultats** : **NOT OBSERVED** — aucune preuve de cherry-picking trouvée ; au contraire, plusieurs rapports (Sprint 15.16, Sprint 15.14 "K_SELECTED: NO") refusent explicitement de déclarer un résultat retenu faute de preuve suffisante — c'est un signe positif de discipline.
- **Calibration + validation sur le même dataset** : **NOT OBSERVED** pour AmbiguityGateThreshold (vraie séparation) ; **NOT APPLICABLE** ailleurs (rien d'autre n'a été calibré).

## Data Leakage
- **Future bar** : PASS pour Regime→TradePlan (prouvé champ-par-champ) ; PASS PARTIEL (agrégats) pour Measurement→Risk.
- **Future regime/volatility/statistics** : PASS — mono-fenêtre par bar, jamais de référence en avant construite.
- **Future account state** : PASS — `BacktestScenario.Window` ne filtre jamais les bars, mais ne fournit pas non plus d'information future (confirmé, c'est une identité pure).
- **Future calibration parameters** : PASS structurel — `CalibrationWindowSet` interdit TRAIN/VALIDATION/OOS dans le mauvais ordre ; mais voir le defect CRITICAL Calibration Infra : l'ABSENCE de binding automatique paramètre→pipeline signifie qu'aucune fuite de paramètre n'est POSSIBLE aujourd'hui (rien n'est injecté), ce qui est ironiquement le symptôme inverse (illusion de calibration, pas fuite de calibration).

## Survivorship / Selection Bias
- **Instrument** : MES est le seul instrument de production ciblé, ES utilisé pour les études offline (Lots 2-9) et StopLoss — pas de sélection parmi plusieurs instruments testés, un seul chemin documenté. NOT OBSERVED.
- **Périodes** : fenêtre Yahoo glissante ancrée sur "maintenant" — ne garantit PAS la représentativité de tous les régimes historiques (chocs macro, gaps majeurs) — **LIMITATION STRUCTURELLE**, pas un biais de sélection actif, mais un risque latent documenté au §Data Sufficiency.

---

# CALIBRATION DEPENDENCY GRAPH

```
Fill Execution (Close[i] → Open[i+1])         [CRITIQUE, prérequis technique]
Costs (activer, valeurs réelles)               [CRITIQUE, prérequis technique]
        ↓
Régime (fenêtres ADF/KPSS/DFA/HL/VR/CUSUM)     [aucune calibration ne peut être scientifiquement
        ↓                                        interprétée avant correction des deux prérequis ci-dessus]
Fusion (poids "provisoires")
        ↓
Decision (poids "provisoires", AmbiguityScore)
        ↓
AmbiguityGateThreshold (0.95 - déjà partiellement calibré, à revalider AVEC coûts)
        ↓
Signal/Entry (OverallConfidence à clarifier/renommer avant tout seuillage)
        ↓
EntryTrigger (seuils READY 0.70/0.55)
        ↓
Stop Loss (méthodologie à construire - BLOQUE Risk ET TradePlan)
        ↓
Risk (RiskPolicy à décider, RiskDistance dépend de SL)
        ↓
Position Sizing
        ↓
P&L / Equity / Drawdown (métriques manquantes à ajouter : Sharpe/Sortino/Profit Factor/etc.)
        ↓
Walk-Forward / Global OOS Validation
```

**Règle explicite (brief §27)** : NE PAS calibrer Risk avant d'avoir stabilisé Entrée et Stop Loss — confirmé pertinent ici : le Risk Engine (Lot 10/11/14.8) est mécaniquement prêt, mais toute calibration de `RiskDistance`/`RiskPolicy` serait vaine tant qu'aucune méthodologie SL n'existe pour l'alimenter honnêtement.

---

# DATA SUFFICIENCY VERDICT (§30/§31)

**Verdict : 45 jours de MES M5 (~6 200-8 900 barres selon le taux de gap réel) sont INSUFFISANTS pour une calibration scientifique robuste d'un seuil destiné à généraliser — probablement adéquats seulement comme fenêtre de développement/preuve de concept technique.**

Raisonnement (détaillé section 1 du chain audit, §1/§5 de la source) :
- ~30-32 jours de bourse effectifs sur 45 jours calendaires ; taux de gap mesuré ~24-29% (coupures maintenance CME) réduit encore le nombre de barres réellement exploitables.
- Estimation raisonnée (pas mesurée formellement dans le repo) : 2 à 4 épisodes de régime distincts par jour de session active → **60 à 120 épisodes bruts sur 30 jours**, dont beaucoup structurellement similaires (pas indépendants au sens statistique).
- Une répartition TRAIN/VALIDATION/OOS typique (60/20/20%) laisserait OOS avec ~6-9 jours de bourse, soit **10-20 épisodes de régime** — échantillon fragile pour valider un seuil de décision généralisable.
- Aucune garantie qu'un régime "rare mais important" (choc macro, gap d'ouverture majeur) apparaisse dans une fenêtre glissante ancrée sur "maintenant".
- Séries continues Yahoo splicées au travers des rolls de contrat, sans traitement de roll — risque d'artefact local non quantifié si un roll tombe dans la fenêtre.

## Recommandation de durée
Bien qu'aucun document du dépôt ne quantifie un nombre exact, un raisonnement de puissance statistique simple suggère de viser **au minimum 6 à 12 mois de données M5 continues** (idéalement couvrant plusieurs régimes macro distincts : au moins un épisode de forte tendance, un épisode de range prolongé, et si possible un épisode de rupture structurelle/choc de volatilité) avant de considérer une calibration de seuil comme scientifiquement défendable pour la production. Ceci n'est PAS une valeur validée par une étude formelle dans ce dépôt — c'est une recommandation raisonnée à valider par une analyse de puissance dédiée avant d'être adoptée.

## Dataset Split recommandé (proposition, NON appliquée au code)

| Segment | Durée proposée | Justification |
|---|---|---|
| TRAIN | ~60% (ex. 4-7 mois si fenêtre totale 6-12 mois) | Doit couvrir au moins 2-3 régimes distincts pour éviter un ajustement à un seul type de marché |
| VALIDATION | ~20% (ex. 1.5-2.5 mois) | Doit inclure au moins un épisode de régime non dominant dans TRAIN pour tester la généralisation |
| OOS | ~20% (ex. 1.5-2.5 mois), la plus RÉCENTE | Jamais utilisée pour aucun choix de paramètre, y compris implicite (pas de "coup d'œil" avant calibration) |

Cette proposition suit exactement le mécanisme déjà construit par `CalibrationWindowSet`/`CalibrationWarmupContract` (Lot 14.9) — aucune modification de code n'est nécessaire pour l'appliquer, seulement des dates réelles à fournir.

---

# ROADMAP — PROPOSITION DE FUTURS LOTS (non implémentés)

| Future Lot | Module | Objective | Parameters | Dataset | Method | Train | Validation | OOS | Acceptance |
|---|---|---|---|---|---|---|---|---|---|
| 14.10 | Execution Realism Fix | Corriger le biais de fill (`Open[i+1]`) ou l'étiqueter explicitement | Convention d'entrée | N/A (fix technique) | Non-régression + nouveau test de réalisme | N/A | N/A | N/A | Ancien comportement disponible en option étiquetée "optimiste" ; nouveau comportement par défaut |
| 14.11 | Cost Activation & Calibration | Activer les coûts par défaut avec des valeurs réelles (barème broker MES) | Slippage/Spread/Commission/Fees | Barème broker documenté + Yahoo | Calibration directe (fait de marché, pas un fit statistique) | N/A | N/A | N/A | Coûts non-nuls par défaut, valeurs sourcées et citées |
| 14.12 | Calibration Binding | Construire le mécanisme d'application automatique `CalibrationParameterSet → CalibrationExperimentSetup` | Tous les paramètres du Lot 14.9 | N/A | Ingénierie + tests de garde (une grille DOIT changer le comportement) | N/A | N/A | N/A | Test explicite prouvant qu'un axe de grille change le résultat |
| 14.13 | Extended Dataset Acquisition | Étendre la fenêtre de données (6-12 mois MES M5) | N/A | Yahoo étendu | Acquisition + fingerprint + audit qualité (gaps, rolls) | — | — | — | Dataset fingerprinté couvrant ≥2 régimes macro distincts |
| 14.14 | Regime Window Calibration | Calibrer/valider les fenêtres ADF/KPSS/DFA/HalfLife/VR/CUSUM | Fenêtres (60/128/30...) | Dataset étendu (14.13) | Étude de sensibilité + walk-forward | Oui | Oui | Oui | Stabilité du régime détecté across TRAIN/VALIDATION |
| 14.15 | Fusion / Decision Weight Calibration | Calibrer les poids Scientific/Quality de Fusion et Decision | Poids des 5+5 règles | Dataset étendu, coûts activés (14.11) | Grid search via Lot 14.9/14.12 | Oui | Oui | Oui | Amélioration mesurable sur métrique de décision (à définir sans score combiné arbitraire) |
| 14.16 | AmbiguityGateThreshold Revalidation | Revalider 0.95 avec coûts activés + capture live courte | `AmbiguityGateThreshold` | Dataset étendu + capture ATAS live 5-15 min | Reproduction Lots 4-8 + coûts | Oui | Oui | Oui | Robustesse maintenue net de coûts |
| 14.17 | Stop Loss Methodology | Reprendre le protocole StopLoss (A1/D) sur données réelles de qualité admissible | `k` (InnovationStd multiplier) ou distance A1 | Nouvelle capture ATAS réelle (qualité validée, pas BLOCKED) | Protocole QDE-012_StopLoss_Calibration_Protocol.md déjà locké | Oui | Oui | Oui | `SCIENTIFIC_ADMISSIBILITY = PASS` avant toute campagne |
| 14.18 | Risk Policy Definition | Définir des valeurs de production RiskPolicy | MaxRiskPerTradePercent, etc. | N/A (décision + backtest de stress) | Analyse de scénario, pas un fit statistique | — | — | — | Valeurs documentées avec justification de risque de compte |
| 14.19 | Global OOS Validation / Walk-Forward | Validation walk-forward de bout en bout sur tous les paramètres calibrés ci-dessus | Tous | Dataset étendu | Walk-forward multi-fenêtre | Oui | Oui | Oui | Performance nette de coûts stable across fenêtres |

*(Numérotation indicative — à ajuster selon les décisions réelles prises entre-temps.)*

---

# NEXT ACTION

**Recommandation unique : LOT 14.10 — CORRECTION DU BIAIS DE FILL D'EXÉCUTION (`Close[i]` → `Open[i+1]`, ou étiquetage explicite "optimiste").**

**Justification** : c'est le SEUL défaut de cet audit qui coche simultanément (a) sévérité CRITICAL, (b) statut CONFIRMED (pas juste probable/possible), (c) déjà identifié et prescrit par le projet lui-même (Lot 13, jamais appliqué), et (d) bloquant pour TOUTE calibration ultérieure — tant que ce biais n'est pas corrigé, chaque futur Lot de calibration (poids Fusion/Decision, fenêtres de régime, AmbiguityGateThreshold, Stop Loss, Risk) produirait des résultats structurellement optimistes et non transférables à un comportement réel. C'est aussi le correctif le plus simple et le mieux circonscrit de la liste CRITICAL (contrairement à "construire une méthodologie Stop Loss" ou "obtenir 6-12 mois de données", qui sont des efforts de recherche substantiels) — un bon prochain pas concret avant d'attaquer les questions plus longues (Stop Loss, dataset étendu, RiskPolicy).

L'activation des coûts (autre défaut CRITICAL) devrait suivre immédiatement dans la même veine "corriger les biais techniques avant toute calibration", mais le fill d'exécution est structurellement en amont (il détermine le prix qui alimente ensuite les coûts eux-mêmes) — d'où la priorité unique donnée ici.

---

# HANDOFF FOR NEXT CHAT

```
PROJECT:
IQIA

CURRENT STAGE:
Lot 14.9 complet (framework de calibration livré et testé) + audit scientifique complet du projet réalisé (ce document). Aucune calibration réelle n'a encore été effectuée sur un paramètre de production.

PRODUCTION:
MES / M5 / ATAS

RESEARCH:
Yahoo Historical Data (MES=F/ES=F, M5, fenêtre glissante ~45 jours, ~6200-8900 barres selon gaps)

PIPELINE:
Market Data -> Market Context -> Regime Detection (ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM, mono-fenêtre par modèle, PAS de multi-fenêtre/consensus) -> Regime Fusion (Engine.Fusion.EvidenceFusionEngine, 5 règles, poids "provisoires") -> Decision (5 règles + Arbitrator : Winner/AmbiguityScore=Clamp(1-Difference,0,1)) -> Signal (modèles réels UNIQUEMENT pour MeanReversion) -> Entry (seuils sur OverallConfidence = ratio de complétude d'exécution, PAS une confiance statistique) -> EntryTrigger (Winner==MeanReverting AND AmbiguityScore<0.95 AND DynamicZScore != 0) -> TradePlan (StopLoss TOUJOURS null -> plafonne à SIGNAL_ONLY) -> Risk Engine (mécanique validée, AUCUNE RiskPolicy de production) -> Execution (fill au Close de la barre de SIGNAL -- BIAIS CONFIRMÉ, jamais corrigé depuis le Lot 13) -> Costs (DÉSACTIVÉS par défaut partout) -> P&L/Equity/Drawdown -> Calibration Infrastructure (Lot 14.9, TRAIN/VALIDATION/OOS, ne sélectionne rien)

COMPLETED:
Lots 9-14.9 (voir table "Completed Lots" du corps du rapport). Pipeline complet fonctionnel bout-en-bout, déterministe, 761 tests verts (2026-08-23).

CURRENTLY VALIDATED:
Mécanique du backtest (déterminisme, isolation, look-ahead Regime->TradePlan prouvé champ-par-champ) ; formules Measurement/Execution/Cost/PnL/Risk ; Half-Life et Variance Ratio (validation numérique externe réelle scikit-learn/NumPy) ; Risk Engine (mécanique, 35+ tests) ; TradePlan (logique de construction) ; ATAS account/instrument binding (technique).

NOT SCIENTIFICALLY VALIDATED:
Poids Fusion/Decision ("provisoires") ; fenêtres de régime (60/128/30, aucune calibration trouvée) ; seuils Entry/EntryTrigger (0.9/0.6/0.3/0.70/0.55, Type D) ; AmbiguityGateThreshold=0.95 (PARTIALLY -- robustesse OOS réelle mais sans coûts ni validation live) ; ADF/KPSS/DFA (validées qualitativement, magnitude numérique jamais comparée à une référence externe -- placeholders vides) ; CUSUM (formule de seuil sans citation théorique) ; toute valeur de coût/slippage/commission ; toute valeur de RiskPolicy (n'existe pas en production) ; StopLoss (100% synthétique, capture réelle disponible mais BLOCKED qualité).

KNOWN DEFECTS:
5 CRITICAL (fill Close[i] jamais corrigé depuis Lot13/L-2 ; coûts désactivés par défaut partout y compris calibration active ; RiskPolicy de production inexistante -- fail-closed ; Calibration Lot14.9 sans binding automatique ParameterSet->Setup ; absence de méthodologie StopLoss bloquant Risk+TradePlan). Voir DEFECT MASTER TABLE pour 12 HIGH et le reste.

KNOWN MISSING COMPONENTS:
Méthodologie Stop Loss de production ; fill d'exécution réaliste (Open[i+1]) ; binding automatique calibration->pipeline ; métriques Sharpe/Sortino/Profit Factor/etc. ; test de look-ahead champ-par-champ pour Measurement->Risk (seule preuve agrégée existe) ; Equity Live ATAS réellement observée non-nulle.

CALIBRATION REQUIRED:
Poids Fusion/Decision, fenêtres de régime, seuils Entry/EntryTrigger, HorizonBars/HitThresholds Measurement/Execution, coûts (valeurs réelles), RiskPolicy (valeurs de production), RiskDistance/MaxExposure (après StopLoss). Voir CALIBRATION MASTER TABLE complète.

CRITICAL PARAMETERS:
AmbiguityGateThreshold=0.95 (EntryTriggerBuilder.cs:19, PARTIALLY CALIBRATED, ne pas modifier sans nouvelle étude complète incluant coûts) ; RiskDistanceConfiguration (toujours fournie manuellement, jamais dérivée d'un vrai stop) ; ExecutionCostConfiguration.Disabled() (défaut partout).

CURRENT THRESHOLDS:
AmbiguityGateThreshold=0.95 (EntryTriggerBuilder.cs:19) ; Entry OpportunityStatus 0.9/0.6/0.3 (EntryAssessmentBuilder.cs) ; EntryTrigger READY 0.70/0.55 (EntryTriggerBuilder.cs) ; Fusion/Decision weights "provisional" (voir code, non reproduits ici par souci de longueur) ; Regime windows 60(ADF/KPSS)/128(DFA,BaiPerron)/30(HalfLife/VarianceRatio/CUSUM) barres (RegimeEngine.cs:19-30).

RISK STATUS:
Moteur VALIDATED (mécanique). Policy INEXISTANTE en production (fail-closed). MinPositionSize = champ mort (jamais lu par RiskEngine.Evaluate). Backtest Risk (Lot 14.8) TECHNIQUEMENT VALIDÉ mais RiskDistance/MaxExposure NOT CALIBRATABLE YET.

STOP LOSS STATUS:
Absent en production (live et backtest). TradePlan.StopLoss toujours null. Recherche dédiée 100% synthétique à ce jour ; une capture réelle existe mais BLOCKED pour qualité (36.5% bars en formation dans la queue temporelle).

BACKTEST STATUS:
Fonctionnel, déterministe, testé (761 tests verts). Résultats actuels OPTIMISTES sur deux axes confirmés : fill au Close de la barre de signal, coûts désactivés par défaut.

ATAS STATUS:
Account/Instrument binding techniquement validé. Equity Live dynamique non-nulle JAMAIS observée atteignant le Risk Engine sur 5 captures réelles. QuantityStep reste 100% manuel (pas de source ATAS fiable identifiée sur la version SDK installée).

DATASETS:
Yahoo MES=F/ES=F M5 ~45 jours (fingerprint SHA-256 déterministe via HistoricalSeriesFingerprint). Une capture ATAS réelle ES M5 committée (Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/) -- qualité BLOCKED. Aucune donnée réelle n'a servi à une calibration StopLoss aboutie.

NEXT LOT:
14.10 -- Correction du biais de fill d'exécution (Close[i] -> Open[i+1], ou étiquetage explicite "optimiste").

NEXT OBJECTIVE:
Éliminer le biais de fill CONFIRMÉ (Lot13/L-2, jamais appliqué au Lot 14.5) avant toute tentative de calibration -- c'est le défaut CRITICAL le plus simple à corriger et le plus bloquant pour la validité de tout résultat futur.

DO NOT CHANGE:
AmbiguityGateThreshold (0.95) sans nouvelle étude complète avec coûts ; RiskEngine.cs/RiskPolicy.cs/InstrumentRiskSpecification.cs/RiskEngineRequest.cs/RiskAssessment.cs ; DecisionArbitrator.cs/EntryTriggerBuilder.cs/TradePlanBuilder.cs ; RegimeEngine/EvidenceFusionEngine(Engine.Fusion)/FusionStateManager/DecisionEngine/SignalEngine/EntryEngine/EntryTriggerEngine ; YahooHistoricalBarSource.cs/MarketContextFactory.cs ; BacktestEngine.cs -- sauf nécessité technique explicitement documentée et approuvée.

IMPORTANT HISTORICAL DECISIONS:
AmbiguityScore=Clamp(1-Difference,0,1) (formule confirmée exacte) ; AmbiguityGateThreshold 0.95 remplace 0.5 (jamais atteignable, Lots 2-3) ; plage [0.05,0.06] jugée robuste OOS (Lots 7-8, ES M5, 9066 bars, SANS modèle de coûts) ; RegimeEngine est mono-fenêtre par modèle, PAS de multi-fenêtre/consensus/dominant-window (contrairement à une hypothèse initiale) ; deux classes EvidenceFusionEngine existent, seule Engine.Fusion.EvidenceFusionEngine est utilisée (Engine.Regime.Core.EvidenceFusionEngine est un stub mort) ; fill de backtest au Close de la barre de signal décidé au Lot 14.5 EN CONTRADICTION avec la prescription du Lot 13 (jamais résolue) ; Risk Engine fail-closed par construction (Equity=0 -> rejet) ; deux systèmes "Calibration" distincts et sans rapport (Core/Calibration = capture dataset ATAS Sprint 15.17-15.19 ; Backtest/Calibration = framework d'expérimentation Lot 14.9).
```

**STOP.**
