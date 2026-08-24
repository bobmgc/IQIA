# QDE-012 — Sprint 15.25 — Lot 14.9 — Global Calibration & Scientific Readiness Audit

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Type** : AUDIT UNIQUEMENT. Aucun fichier de production (`.cs`, `.csproj`, configuration, test existant) n'a été modifié, créé ou supprimé pendant ce lot. Aucun paramètre, seuil ou poids n'a été changé. Aucune implémentation de calibration n'a été ajoutée. Aucun commit, aucune DLL déployée, aucun lancement ATAS.
**Méthode** : lecture exhaustive du dépôt (cartographie de répertoires, recherche par grep de toutes les constantes/seuils/fenêtres/poids), un agent de vérification en lecture seule dédié au recoupement ligne-par-ligne des affirmations les plus lourdes de conséquence, `dotnet build` (Debug + Release) et `dotnet test` exécutés pour confirmer l'état technique actuel. Toute affirmation cite un `fichier:ligne` ou un rapport de lot antérieur quand c'est possible. Document conçu pour être lu de façon autonome par une nouvelle conversation (voir §23).
**Note sur l'état du dépôt** : deux fichiers ont été trouvés déjà présents au début de ce lot, non committés, visiblement produits par une session antérieure sous une interprétation légèrement différente du brief Lot 14.9 : `Documentation/Scientific/QDE-012_Sprint_15.25_Lot14.9_Scientific_Calibration_Foundation_Report.md` (rapport décrivant la CONSTRUCTION du framework `Backtest/Calibration/`, 21 fichiers de production + 14 fichiers de tests, déjà présents dans l'arbre de travail) et `Documentation/Scientific/QDE-012_Sprint_15.25_Lot14.9_Full_Project_Calibration_Scientific_Audit_Report.md` (un premier brouillon d'audit global). Conformément à la règle "AUDIT UNIQUEMENT" de ce lot, ces fichiers et le code `Backtest/Calibration/`/`Tests/Backtest/Calibration/` n'ont pas été modifiés — ils sont traités ici comme un ÉTAT EXISTANT du dépôt à auditer au même titre que tout le reste, pas comme un livrable de ce lot. Ce rapport reprend, vérifie, corrige et complète leur contenu pour produire le document unique demandé par le brief actuel, à l'emplacement exact requis.

---

## Vérification technique effectuée dans ce lot

| Vérification | Résultat |
|---|---|
| `dotnet build IQIAIndicator.csproj -c Debug` | **PASS** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator.csproj -c Release` | **PASS** — 0 avertissement, 0 erreur |
| `dotnet test Tests/IQIAIndicator.Tests.csproj` | **PASS** — 760/761 réussis, 1 ignoré, 0 échec (voir §0.1) |
| Fichiers de production modifiés par ce lot | **0** |
| Fichiers de test existants modifiés par ce lot | **0** |
| Code de calibration ajouté par ce lot | **0** (le framework `Backtest/Calibration/` préexistait à ce lot) |
| Commit effectué | **0** |

### §0.1 Résultat des tests

**`dotnet test Tests/IQIAIndicator.Tests.csproj -c Debug`** (exécuté intégralement dans ce lot, durée 18 min 39 s) :

```
Réussi! - échec : 0, réussite : 760, ignorée(s) : 1, total : 761
```

**0 échec, 760 réussites, 1 test ignoré** (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`, `[SKIP]` explicite, sans rapport de régression — cohérent avec l'état déjà documenté par les lots antérieurs). Effet de bord connu et attendu : l'exécution complète de la suite a réécrit trois fichiers de sortie de recherche sous `Tests/Research/StopLossCalibration/Output/*.txt|*.csv` (`A1_calibration_summary.txt`, `campaign_summary.txt`, `A1_A2_Hybrid_trending.csv`) — un artefact inoffensif déjà observé lors de runs précédents (deux de ces fichiers étaient déjà modifiés dans l'arbre de travail avant même le début de ce lot), pas une régression et pas une modification de test.

---

# 1. EXECUTIVE SUMMARY

Le projet IQIA dispose d'une **architecture mécaniquement solide, honnête et abondamment testée**, mais d'une **couche de calibration/validation scientifique encore embryonnaire**. Le déterminisme, la discipline "ne jamais fabriquer une valeur" (statuts explicites plutôt que zéros silencieux), et la séparation stricte des responsabilités entre étages sont réels et de haute qualité sur l'ensemble du code audité — un socle rare et précieux pour ce qui doit suivre.

Cependant, la confiance à accorder à toute PERFORMANCE mesurée aujourd'hui (P&L, taux de succès, robustesse d'un seuil) doit rester prudente : deux biais structurels CONFIRMÉS (fill d'exécution optimiste, coûts désactivés par défaut) et une quasi-absence de calibration empirique des poids/seuils/fenêtres qui gouvernent réellement le comportement affectent toute conclusion de performance actuelle. Le laboratoire de calibration (`Backtest/Calibration/`, déjà présent dans le dépôt) est techniquement fonctionnel mais souffre d'un défaut architectural qui le rend aujourd'hui **incapable de faire varier le comportement réel du pipeline** sans câblage manuel supplémentaire.

**Verdict global** : le projet n'est **PAS PRÊT pour une campagne de calibration de production**. Il est prêt pour une séquence de corrections structurelles ciblées (P0, §21), après quoi la calibration scientifique proprement dite peut commencer, dans l'ordre dérivé du code lui-même (§19), pas d'un ordre supposé a priori.

**Points forts majeurs** :
1. Discipline "jamais fabriquer une valeur" appliquée de façon cohérente (TradePlan refuse d'inventer SL/TP, RiskEngine fail-closed, statuts explicites `NoData`/`InsufficientFutureData` plutôt que des zéros silencieux).
2. Look-ahead prouvé par test exécutable — pas seulement affirmé — pour la chaîne Regime→TradePlan, par deux méthodes indépendantes (troncature + perturbation).
3. Half-Life et Variance Ratio : validation numérique externe réelle (scikit-learn/NumPy, tolérance 1e-6), un standard de preuve rarement atteint même dans l'industrie.
4. Traçabilité historique riche et honnête : chaque lot documente ses propres limites, y compris quand le résultat est négatif (ex. Sprint 15.14 "K_SELECTED: NO", Sprint 15.18 "SCIENTIFIC_ADMISSIBILITY: BLOCKED").

**Faiblesses majeures** :
1. Biais de fill d'exécution au `Close[i]`, la même barre que le signal — identifié en interne par le projet lui-même (Lot 13, défaut L-2) et jamais corrigé au Lot 14.5.
2. Coûts désactivés par défaut partout, y compris dans la campagne de calibration existante dans le dépôt.
3. Aucune méthodologie Stop Loss de production — bloque simultanément le Risk Engine réel et `TradePlan` (`PLAN_READY` est structurellement inatteignable aujourd'hui).
4. `OverallConfidence` est un ratio de complétude d'exécution de modèles (`SuccessfulModels/5`), pas une confiance statistique, mais alimente 5 seuils de production comme si c'en était une.
5. Poids de Fusion et de Decision jamais calibrés empiriquement ; ceux de Decision sont explicitement commentés "Provisional" dans le code source lui-même.
6. Le framework de calibration (`Backtest/Calibration/`) ne relie aujourd'hui AUCUN `CalibrationParameterSet` à un point d'injection réel du pipeline — une grille de N configurations produirait N fingerprints différents pour un comportement strictement identique.

**Readiness scientifique** : FAIBLE À MOYENNE. **Readiness technique** : ÉLEVÉE. **Readiness calibration** : FAIBLE (voir §16, §21).

---

# 2. PROJECT ARCHITECTURE MAP

```
Market Data (Yahoo / ATAS)
   → Historical Data (contrat provider-agnostique)
   → Market Context (OHLC + dérivés, arithmétique pure, aucun paramètre calibrable)
   → Scientific Models (stack MeanReversion : Kalman, OU, DynamicZScore, VolatilityModel, SPRT — SEULE méthodologie câblée à de vrais modèles)
   → Regime Detection (ADF, KPSS, DFA/Hurst, Half-Life, Variance Ratio, CUSUM, Bai-Perron — mono-fenêtre par modèle, PAS de multi-fenêtre/consensus)
   → Evidence Fusion (Engine.Fusion.EvidenceFusionEngine, 5 IFusionRule, poids non calibrés)
   → Decision (DecisionEngine, 5 règles "Provisional" + DecisionArbitrator : Winner/RunnerUp/Difference/AmbiguityScore)
   → Signal (SignalEngine → ScientificModelRegistry)
   → Entry (EntryEngine/EntryAssessmentBuilder — seuils sur OverallConfidence, un ratio de complétude, pas une confiance statistique)
   → Entry Trigger (EntryTriggerBuilder — Winner==MeanReverting AND AmbiguityScore<0.95 AND DynamicZScore≠0 → BUY/SELL)
   → TradePlan (TradePlanBuilder — StopLoss TOUJOURS null en production → plafonne à SIGNAL_ONLY)
   → Stop Loss (aucune méthodologie de production ; recherche dédiée 100% synthétique à ce jour)
   → Risk (Engine.Risk.RiskEngine — mécanique validée, AUCUNE RiskPolicy de production)
   → Position Sizing (idem, plus Backtest/Risk Lot 14.8 pour le backtest)
   → Execution (Backtest/Execution — fill au Close de la barre de SIGNAL, biais connu et documenté en interne dès le Lot 13, jamais corrigé)
   → Costs (Backtest/Cost — DÉSACTIVÉS par défaut partout)
   → P&L (Backtest/Pnl)
   → Equity / Drawdown (un point par position FERMÉE, pas de mark-to-market intrabar)
   → Performance / Calibration Infrastructure (Backtest/Calibration — TRAIN/VALIDATION/OOS, grille, fingerprints ; NE sélectionne ni n'optimise rien, et ne relie aujourd'hui aucun paramètre au pipeline réel)
```

## Table des étages

| Étage | Fichiers clés | Rôle | Paramètres/constantes notables | Consommateurs | Tests | Validation scientifique |
|---|---|---|---|---|---|---|
| Market Data | `Core/MarketData/Yahoo/YahooHistoricalBarSource.cs` | Source historique externe unique | `DefaultMaxChunkSpanDays=59` (contrainte Yahoo mesurée, Type C) | Backtest, Calibration | 66 + 3 réseau, PASS | TECHNIQUEMENT VALIDÉ (mapping/parsing), pas d'analyse indépendante de qualité |
| Historical Data | `Core/MarketData/HistoricalSeries.cs`, `HistoricalBar.cs` | Contrat provider-agnostique, valide et rejette, ne répare jamais | Aucun paramètre calibrable | Backtest, Calibration | Présent, cohérent | VALIDÉ structurellement ; aucun contrôle de régularité d'espacement au niveau série |
| Market Context | `Core/MarketContextFactory.cs` | Assemblage OHLC→MarketContext, point de construction unique live/backtest | Aucun (arithmétique pure) | Regime, Signal | Présent | NON APPLICABLE (pas de modèle) |
| Scientific Models | `Engine/ScientificModels/*` | Kalman, OU, DynamicZScore, VolatilityModel, SPRT — uniquement pour MeanReversion | Fenêtres 20/20, seuils classification volatilité 0.9/1.1/0.33/0.66 | Signal (MeanReversion uniquement) | Nombreux (voir Lots antérieurs) | Look-ahead prouvé ; seuils de classification non justifiés (Type D) |
| Regime Detection | `Engine/Regime/*` | ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM/Bai-Perron, mono-fenêtre | Voir §7 | Fusion | Voir §7 | Voir §7 |
| Evidence Fusion | `Engine/Fusion/*` | 5 `IFusionRule`, agrège les évidences de régime | Poids Scientific/Quality par règle | Decision | Voir §8 | Voir §8 |
| Decision | `Engine/Decision/*` | 5 règles "Provisional" + arbitrage Winner/RunnerUp/AmbiguityScore | Poids par règle, `AmbiguityScore=Clamp(1-Difference,0,1)` | Signal, Entry, EntryTrigger | Voir §9 | Voir §9 |
| Signal/Entry | `Engine/Signal/*`, `Engine/Entry/*` | `OverallConfidence`, seuils OpportunityStatus | 0.9/0.6/0.3 | EntryTrigger | Voir §10 | Voir §10 |
| Entry Trigger | `Engine/EntryTrigger/*` | Condition jointe → BUY/SELL/WATCH/NO_ACTION | `AmbiguityGateThreshold=0.95`, seuils READY 0.70/0.55 | TradePlan | Voir §10 | Voir §10/§11 |
| TradePlan | `Engine/TradePlan/*` | Assemble Entry/SL/TP/Sizing en un plan de trade | `RiskParameters` toujours `null` en production | Risk (conceptuellement) | Voir §11 | Voir §11 |
| Stop Loss | `Documentation/Scientific/QDE-012_StopLoss_Calibration_Protocol.md`, `Tests/Research/StopLossCalibration/` | Recherche dédiée, non connectée en production | Candidats A1 (Volatility Stop)/D (Mean-Reversion Invalidation) | Risk (jamais en production) | Voir §12 | Voir §12 |
| Risk Engine | `Engine/Risk/RiskEngine.cs`, `RiskPolicy.cs` | Sizing, budget de risque, fonction pure sans état | `RiskPolicy.*` tous à 0 par défaut (fail-closed) | Live ATAS, Backtest/Risk | Voir §13 | Voir §13 |
| Backtest Risk | `Backtest/Risk/*` (Lot 14.8) | Réutilise RiskEngine pour le backtest | `RiskDistanceConfiguration`, `MaxExposure` | P&L | Voir §13 | Voir §13 |
| Execution | `Backtest/Execution/*` (Lot 14.5) | Simulation de fill/exit | `HorizonBars=10`, fill=`Close[i]` | P&L, Cost | Voir §14 | Voir §14 |
| Measurement | `Backtest/Measurement/*` (Lot 14.4) | Return/MFE/MAE/HitRate | `HorizonBars=10`, `HitThresholds={0.001,0.002}` | Calibration | Voir §14 | Voir §14 |
| Cost | `Backtest/Cost/*` (Lot 14.7) | Commissions/spread/slippage/tick cost | `ExecutionCostConfiguration.Disabled()` par défaut | P&L | Voir §15 | Voir §15 |
| P&L/Equity | `Backtest/Pnl/*` (Lot 14.6/14.8) | GrossPnL/NetPnL/Equity/Drawdown | Un point par position fermée | Calibration | Voir §16 | Voir §16 |
| Calibration Infra | `Backtest/Calibration/*` | Laboratoire TRAIN/VALIDATION/OOS, grille, fingerprints | Voir §16 | — | 61 tests, PASS | Framework VALIDÉ ; binding paramètre→pipeline ABSENT |
| Scientific Dataset Capture | `Core/Calibration/*` (Sprint 15.17-15.19) | Enregistreur d'observations ATAS bar-par-bar, SANS RAPPORT avec `Backtest/Calibration/` | 39+ clés `ATAS.*` | Recherche StopLoss, dashboards | Testé (passive-capture) | TECHNIQUEMENT VALIDÉ |

---

# 3. COMPLETE PARAMETER INVENTORY

Légende Type : voir §4 (Scientific Validation classification). Légende Statut : `NOT CALIBRATED` / `PARTIALLY CALIBRATED` / `CALIBRATED` / `CALIBRATED + OOS VALIDATED` / `PRODUCTION VALIDATED`. Une valeur codée en dur n'est **jamais** comptée comme "calibrée" simplement parce qu'elle existe.

| Paramètre | Module | Valeur actuelle | Type | Rôle | Source (fichier:ligne) | Calibré ? | Validé ? | Méthode nécessaire | Priorité |
|---|---|---|---|---|---|---|---|---|---|
| `AmbiguityGateThreshold` | EntryTrigger | 0.95 | A/B (limite) | Seuil de suppression d'ambiguïté avant conversion en direction | `EntryTriggerBuilder.cs:19` | PARTIALLY CALIBRATED | PARTIALLY VALIDATED (OOS structurel, sans coûts) | Revalider avec coûts activés + capture live courte | CRITICAL |
| Convention de fill (`Close[i]`) | Execution | Close de la barre de signal | D | Prix d'entrée simulé | `ExecutionSimulator.cs:166-192`, `BacktestEngine.cs:424-432` | N/A (choix d'ingénierie biaisé) | UNVALIDATED — biais CONFIRMÉ | Corriger vers `Open[i+1]` ou étiqueter "optimiste" | CRITICAL |
| `ExecutionCostConfiguration.Enabled` | Cost | `false` partout en pratique | C (défaut) | Active/désactive le modèle de coûts | `ExecutionCostConfiguration.cs:29-36` | NOT CALIBRATED | N/A (désactivé) | Activer avec un barème broker réel | CRITICAL |
| `RiskPolicy.*` (8 champs) | Risk Policy | 0 / 0.0 partout | D si jamais promu tel quel | Budget de risque, contraintes | `IQIAIndicator.cs:229-265` | NOT CALIBRATED / n'existe pas | UNKNOWN (fail-closed) | Décision de valeur avant tout usage live | CRITICAL |
| Binding `CalibrationParameterSet → CalibrationExperimentSetup` | Calibration Infra | Absent | — (défaut architectural) | Devrait injecter un paramètre calibré dans le pipeline | `CalibrationExperimentRunner.cs:46-49` | N/A | N/A | Construire le mécanisme avant tout Lot basé sur `CalibrationGrid` | CRITICAL |
| `TradePlan.StopLoss` | TradePlan/Risk | Toujours `null` | N/A (absent) | Devrait porter le stop calculé | `IQIAIndicator.cs:602`, `BacktestEngine.cs:328` | NOT APPLICABLE | NOT APPLICABLE | Méthodologie Stop Loss scientifique | CRITICAL |
| `RiskDistanceConfiguration` | Backtest Risk | `None()` par défaut ; exemple 4 ticks | D | Distance de stop fournie manuellement | `RiskDistanceConfiguration.cs` | NOT CALIBRATABLE YET | N/A | Dérive d'une méthodologie SL | HIGH |
| `OverallConfidence` (sémantique) | Signal/Entry | `SuccessfulModels/5` | Mal nommé — ratio de complétude, pas Type A/B/C/D/E de confiance statistique | Alimente 5 seuils Entry/EntryTrigger | `ScientificAssessmentBuilder.cs:100-102` | N/A | Trompeur, à renommer/documenter | HIGH |
| Seuils `OpportunityStatus` (QUALIFIED/HIGH_PRIORITY/WATCHLIST) | Entry | 0.9 / 0.6 / 0.3 | D | Classification d'opportunité | `EntryAssessmentBuilder.cs:130,134,138` | NOT CALIBRATED | UNVALIDATED | Étude empirique | HIGH |
| Seuils READY (QUALIFIED/HIGH_PRIORITY) | EntryTrigger | 0.70 / 0.55 | D | Passage à READY | `EntryTriggerBuilder.cs:243,250` | NOT CALIBRATED | UNVALIDATED | Étude empirique, après correction de `OverallConfidence` | HIGH |
| Poids Scientific/Quality (5 règles) | Fusion | 0.80-0.90 / 0.10-0.20 selon règle ; `HalfLifeDecayScale=10` | D | Blend du score de fusion | `Engine/Fusion/Rules/*.cs` | NOT CALIBRATED | UNVALIDATED | `FusionConfiguration` à brancher (`EnableCalibration=false` aujourd'hui) puis étude | HIGH |
| Poids dimensionnels (5 règles, "Provisional") | Decision | Voir `Engine/Decision/Rules/*.cs` | D (auto-déclaré) | Blend du score de décision final | `Engine/Decision/Rules/{MeanRevertingRule,TrendingRule,StructuralBreakRule,StableRangeRule,RandomWalkRule}.cs` (commentaires "Provisional" lignes 13/18-19 de chaque fichier) | NOT CALIBRATED | UNVALIDATED | Étude de calibration dédiée | HIGH |
| Slippage / Spread / Commission / Fees | Cost | 0 en production ; illustratif en test | D (auto-déclaré "illustratif") | Coût de transaction simulé | `Backtest/Cost/*.cs` | NOT CALIBRATED | N/A | Barème broker MES réel | HIGH |
| Fenêtres Regime : ADF/KPSS | Regime | 60 bars (min 30) | D | Fenêtre de calcul | `RegimeEngine.cs:19-22` | NOT CALIBRATED | Aucune trace de calibration trouvée | Étude de sensibilité | HIGH |
| Fenêtre Regime : DFA/Hurst | Regime | 128 bars (min 80) | D | Fenêtre de calcul | `RegimeEngine.cs:23-24` | NOT CALIBRATED | idem | idem | HIGH |
| Fenêtres Regime : Half-Life / Variance Ratio / CUSUM | Regime | 30 bars (min 20) chacune | D | Fenêtre de calcul | `RegimeEngine.cs:25-30` | NOT CALIBRATED | idem | idem | HIGH |
| Fenêtre Regime : Bai-Perron | Regime | 128 bars (min 64) | D | Fenêtre de calcul | `RegimeEngine.cs` | NOT CALIBRATED | idem | idem | HIGH |
| `RiskPolicy.MinPositionSize` | Risk Engine | Champ déclaré, jamais lu | Champ mort (defect) | Cens supposé une taille de position minimale | `RiskPolicy.cs:20` vs `RiskEngine.cs` | N/A (defect) | N/A | Implémenter ou retirer — pas une calibration | MEDIUM |
| `MeasurementConfiguration.HorizonBars` | Measurement | 10 | D | Horizon de mesure Return/MFE/MAE | `MeasurementConfiguration.cs:16` | NOT CALIBRATED | Utilisé partout sans dérivation | ATR/durée de trade réelle | MEDIUM |
| `MeasurementConfiguration.HitThresholds` | Measurement | {0.001, 0.002} | D | Seuils de "hit" pour HitRate | `MeasurementConfiguration.cs:20` | NOT CALIBRATED | idem | idem | MEDIUM |
| `ExecutionConfiguration.HorizonBars` | Execution | 10 (couplé à Measurement) | D | Horizon d'exit TIME_HORIZON | `ExecutionConfiguration.cs:17` | NOT CALIBRATED | idem | idem — coupler explicitement | MEDIUM |
| `BacktestRiskConfiguration.MaxExposure` | Backtest Risk | `null` (illimité) ; exemple 50000$ | D | Plafond d'exposition | `BacktestRiskConfiguration.cs:39` | NOT CALIBRATABLE YET | N/A | Règle de marge réelle | MEDIUM |
| `MinQuantity`/`MaxQuantity`/`QuantityStep` (MES) | Instrument | Aucune valeur prod | N/A | Contraintes de sizing | `InstrumentRiskSpecification.cs` | NOT CALIBRATED | Fixtures test uniquement | Décision manuelle (pas une calibration scientifique) | MEDIUM |
| `TickSize`/`TickValue`/`PointValue` (MES) | Instrument | 0.25 / 1.25 / 5 | **C** | Spécification contrat réelle | Dérivé dynamiquement d'ATAS `Security`, spec CME | N/A — fait objectif | Correct factuellement | Ajouter une citation datée | LOW |
| CUSUM seuil `h=σ√(2N ln N)` | Regime | Formule fixe | E | Seuil de détection de rupture | `CusumStatistics.cs:79-80` | N/A | Recursion validée vs Python ; formule source NON citée | Retrouver/documenter la source théorique | LOW |
| ADF/KPSS/DFA — références numériques externes | Regime | Placeholders vides | B (théoriquement justifié, non validé empiriquement) | Comparaison à Statsmodels/nolds | `*GoldenDataset.cs` | NOT VALIDATED | Placeholders jamais remplis | Exécuter les scripts documentés | LOW |
| Half-Life (formule OLS closed-form) | Regime | — | **A** | Estimation de vitesse de retour à la moyenne | `HalfLifeStatistics.cs` | N/A (formule, pas un seuil à calibrer) | **VALIDATED** (scikit-learn, tol 1e-6/1e-4) | Aucune | N/A |
| Variance Ratio (Lo-MacKinlay) | Regime | — | **A** | Test de marche aléatoire | `VarianceRatioStatistics.cs` | N/A | **VALIDATED** (NumPy, tol 1e-6) | Aucune | N/A |
| `DefaultMaxChunkSpanDays` | Market Data | 59 | **C** | Contrainte externe Yahoo mesurée | `YahooHistoricalBarSource.cs` | N/A | Fait objectif | Aucune | N/A |
| `AmbiguityScore` (forme fonctionnelle) | Decision | `Clamp(1-Difference,0,1)` | D/E (forme non justifiée théoriquement) | Mesure d'ambiguïté de décision | `DecisionArbitrator.cs` | N/A (formule, pas une valeur) | TECHNIQUEMENT VALIDÉ (implémentation, 4 tests 1e-12) ; forme non justifiée | Justifier ou tester d'autres formes | LOW |

*(Table exhaustive pour les paramètres à effet réel documenté sur le pipeline ; le detail ligne-par-ligne de chaque fenêtre/constante mineure figure dans les rapports de lot individuels référencés §22.)*

---

# 4. SCIENTIFIC VALIDATION MATRIX

| Composant | Formule | Littérature identifiable | Référence présente | Test analytique | Comparaison externe (Python/NumPy/Statsmodels/nolds) | Causalité (look-ahead) | Verdict |
|---|---|---|---|---|---|---|---|
| ADF | Dickey-Fuller augmenté | Oui (Dickey & Fuller) | Valeurs critiques MacKinnon (1994) citées | Oui | **Placeholder vide** — jamais comparé à Statsmodels | PASS | **PARTIALLY VALIDATED** |
| KPSS | Kwiatkowski et al. | Oui | Valeurs critiques Kwiatkowski (1992) citées | Oui | **Placeholder vide** | PASS | **PARTIALLY VALIDATED** |
| DFA/Hurst | Detrended Fluctuation Analysis | Oui (Peng et al.) | Direction qualitative + invariance d'échelle prouvées mathématiquement | Oui | **Placeholder vide** — jamais comparé à `nolds` | PASS | **PARTIALLY VALIDATED** |
| Half-Life | OLS closed-form (Ornstein-Uhlenbeck) | Oui | Oui | Oui | **Oui — scikit-learn, tol 1e-6/1e-4, réelle** | PASS | **VALIDATED (Type A)** |
| Variance Ratio | Lo-MacKinlay | Oui | Oui | Oui | **Oui — NumPy, tol 1e-6, réelle** | PASS | **VALIDATED (Type A)** |
| CUSUM | Recursion S+/S- | Recursion oui ; **seuil `h=σ√(2N ln N)` sans citation** | Partielle | Oui, recursion validée vs Python | Recursion oui, formule de seuil non | PASS | **PARTIALLY VALIDATED** |
| Bai-Perron | Rupture structurelle multiple | Non auditée en détail dans ce lot (hors périmètre agent, non contredite par recherche complémentaire) | — | — | — | — | **UNKNOWN** |
| AmbiguityScore | `Clamp(1-Difference,0,1)` | Non — forme ad hoc | N/A | Oui (4 tests, 1e-12) | N/A | PASS (implémentation) | **TECHNIQUEMENT VALIDÉ, forme non justifiée (Type D/E)** |
| Fusion weights | Blend linéaire pondéré | Non | N/A | Câblage testé | N/A | N/A | **UNVALIDATED** |
| Decision weights | Blend linéaire pondéré, "Provisional" | Non | N/A | Câblage testé | N/A | N/A | **UNVALIDATED (auto-déclaré)** |
| `AmbiguityGateThreshold` | Seuil empirique sur `Difference` | N/A (calibration empirique, pas une formule théorique) | Étude interne (Lots 4-9) | Câblage logique testé (16+3 tests) | Séparation OOS réelle (4 fenêtres, 9066 bars) | PASS (câblage) | **PARTIALLY VALIDATED** |
| VolatilityModel (classification) | ATR/percentile-like, seuils 0.9/1.1/0.33/0.66 | Non | N/A | Look-ahead prouvé | N/A | PASS | **TECHNIQUEMENT VALIDÉ, seuils non justifiés (Type D)** |

---

# 5. CALIBRATION STATUS MATRIX

| Statut | Paramètres concernés |
|---|---|
| **PRODUCTION VALIDATED** | Aucun |
| **CALIBRATED + OOS VALIDATED** | Aucun |
| **CALIBRATED** | Aucun |
| **PARTIALLY CALIBRATED** | `AmbiguityGateThreshold=0.95` (seul paramètre avec une séparation TRAIN/OOS empirique documentée, Lots 4-9 — mais fonction objectif = "produire un candidat observable", pas "maximiser une performance nette de coûts", et jamais validé net de coûts ni en live) |
| **NOT CALIBRATED** | Poids Fusion (5 règles), poids Decision (5 règles, "Provisional"), fenêtres Regime (ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM/Bai-Perron), seuils Entry (0.9/0.6/0.3), seuils EntryTrigger READY (0.70/0.55), `HorizonBars`/`HitThresholds` Measurement/Execution, coûts (slippage/spread/commission), `RiskDistanceConfiguration`, `MaxExposure` |
| **NOT CALIBRATABLE YET** (bloqué par une dépendance structurelle) | `RiskDistanceConfiguration`/`MaxExposure` (dépendent d'une méthodologie SL inexistante), `RiskPolicy.*` (dépend d'une décision de valeur de production, pas d'un fit statistique), tout paramètre du framework `Backtest/Calibration/` via `CalibrationGrid` (dépend du binding ParameterSet→Setup, absent) |
| **N'EXISTE PAS EN PRODUCTION** | `TradePlan.StopLoss`, `RiskPolicy` de production |

---

# 6. STRUCTURAL DEFECTS

| # | Sévérité | Module | Défaut | Preuve | Impact | Confirmé/Probable/Possible | Action requise | Bloque la calibration ? |
|---|---|---|---|---|---|---|---|---|
| D1 | **CRITICAL** | Execution | Fill d'entrée au `Close[i]`, la même barre que le signal — biais déjà identifié en interne (Lot 13, défaut L-2, prescription `Open[i+1]` jamais appliquée) | `QDE-012_Sprint_15.25_Lot13_Backtest_Architecture_Audit_Report.md:1140` vs `BacktestEngine.cs:424-432`, `ExecutionSimulator.cs:166-192` | Tous les Return/MFE/MAE/PnL/coûts/risque en aval construits sur un fill optimiste, non réplicable en temps réel | **CONFIRMED** | Implémenter `Open[i+1]` ou étiqueter tout résultat courant "optimiste" | **OUI — bloque toute calibration en aval** |
| D2 | **CRITICAL** | Cost | Coûts désactivés par défaut partout, y compris dans la campagne de calibration active du dépôt | `ExecutionCostConfiguration.cs:29-36` — `Disabled()` documenté comme "le défaut recommandé" | Tous les résultats actuels sont zéro-coût | **CONFIRMED** | Exiger une passe avec coûts réalistes avant toute conclusion de performance | **OUI** |
| D3 | **CRITICAL** | Risk Policy | Aucune valeur de production `RiskPolicy` n'existe (fail-closed par défaut) | `IQIAIndicator.cs:229-265` (tous à 0/0.0), `RiskEngine.cs` (Equity=0 → rejet) | Le Risk Engine live n'a aucun contenu opérationnel tant que non configuré | **CONFIRMED** | Documenter/décider des valeurs avant tout usage live | Non-calibrable tant que non configuré |
| D4 | **CRITICAL** | Calibration Infra | `CalibrationParameterSet` jamais lié automatiquement à `CalibrationExperimentSetup` — vérifié par lecture directe : `CalibrationExperimentRunner.cs:46-49` ne lit que `Setup.{Measurement,Execution,PnL,Cost,Risk}Configuration`, jamais `experiment.ParameterSet` | `CalibrationExperimentRunner.cs:46-49`, corroboré par le rapport de construction du framework lui-même (`...Foundation_Report.md:305` : "NOT CALIBRATABLE YET: aucun point d'injection") | Une grille future produirait N expériences aux fingerprints distincts mais au comportement de pipeline strictement identique | **CONFIRMED** | Documenter le contrat "câblage manuel obligatoire" ou construire un mécanisme d'application automatique avant tout lot basé sur `CalibrationGrid` | **OUI, pour toute calibration via `CalibrationGrid`** |
| D5 | **CRITICAL** | TradePlan/Risk/StopLoss | `TradePlan.StopLoss` toujours `null` (live ET backtest) ; aucune méthodologie SL de production ; `RiskDistance` backtest fournie à la main, sans lien avec la volatilité réelle | `IQIAIndicator.cs:602`, `BacktestEngine.cs:328`, `TradePlanBuilder.cs:57-61`, `RiskDistanceConfiguration.cs:8-18` | `PLAN_READY` structurellement inatteignable ; tout sizing par risque backtest repose sur un chiffre arbitraire | **CONFIRMED** | Développer/valider une méthodologie SL avant de calibrer Risk/Sizing | **OUI — bloque toute calibration Risk/Sizing réaliste** |
| D6 | HIGH | Entry/EntryTrigger | `OverallConfidence` = ratio de complétude d'exécution (`SuccessfulModels/5`), pas une confiance statistique, mais alimente 5 seuils en aval comme si c'en était une | `ScientificAssessmentBuilder.cs:100-102` (`ExpectedModels` = liste fixe de 5), `EntryAssessmentBuilder.cs:130-145`, `EntryTriggerBuilder.cs:243,250` | Risque de mauvaise lecture (dashboard, humain) ; seuils calibrés dessus sans assise statistique | Confirmed | Renommer/documenter la sémantique réelle avant toute calibration de seuil dessus | Fausse toute calibration future basée sur ce champ |
| D7 | HIGH | Decision/Fusion | Poids Decision explicitement "Provisional" dans le code (`Engine/Decision/Rules/*.cs`, commentaires lignes 13/18-19 de chaque règle) ; poids Fusion sans commentaire explicite mais sans citation/test/calibration non plus (`Engine/Fusion/Rules/*.cs`) ; `FusionConfiguration.EnableCalibration=false` — mécanisme de calibration prévu mais non branché | `Engine/Decision/Rules/*.cs`, `Engine/Fusion/Rules/*.cs`, `Engine/Fusion/Core/FusionConfiguration.cs:16` | Le score de décision final dépend de poids jamais testés empiriquement | Confirmed | Étude de calibration dédiée | Oui, couplé à `AmbiguityGateThreshold` |
| D8 | HIGH | Regime | CUSUM : formule du seuil `h=σ√(2N ln N)` sans citation théorique (contrairement à ADF/KPSS/DFA qui citent leurs valeurs critiques) | `CusumStatistics.cs:79-80` | Impossible de juger la correction théorique de la formule elle-même | Probable | Retrouver/documenter la source | Réduit la confiance, non bloquant |
| D9 | HIGH | Regime | ADF/KPSS/DFA : références numériques externes (Statsmodels/nolds) jamais remplies (placeholders) | `AdfGoldenDataset.cs`, `KpssGoldenDataset.cs`, `DfaGoldenDataset.cs` | Seule la direction qualitative est prouvée, pas l'exactitude numérique | Confirmed | Exécuter les scripts documentés, remplir les références | Recommandé avant calibration de seuils basés sur ces stats |
| D10 | HIGH | AmbiguityGateThreshold | Robustesse OOS démontrée mais sans modèle de coûts ni validation live ATAS | `QDE-012_Sprint_15.25_Lot9_Threshold_Implementation_Report.md` | Seuil peut être robuste structurellement sans être rentable net | Confirmed (auto-déclaré) | Revalider avec coûts (D2 corrigé) + capture live courte | Oui, pour validation de rentabilité |
| D11 | HIGH | ATAS/Equity | Aucune Equity Live dynamique/non-nulle n'a jamais atteint `RiskEngine` sur 5 captures réelles indépendantes | Lots 12.7/12.9/12.10 | Risk Engine ne peut être `ACCEPTED` en conditions réelles tant que non résolu | Confirmed (5 captures concordantes) | Capturer une session Live réelle avec compte financé | Non (backtest n'en dépend pas) |
| D12 | HIGH | Scientific Dataset | `RiskPolicy` (8 paramètres) jamais exporté dans le dataset capturé | `Core/Calibration/ScientificDatasetRecord.cs` (grep négatif) | Empêche la reproduction offline complète d'une décision Risk | Likely | Exporter `RiskPolicy` en télémétrie passive | Oui, pour calibration Risk future |
| D13 | HIGH | Historical Data | Aucun contrôle de régularité d'espacement temporel au niveau `HistoricalSeries` | `HistoricalSeries.cs` | Une source future moins disciplinée que Yahoo pourrait produire une série trouée non détectée | Likely | Contrôle de continuité optionnel au niveau série | Latent, non bloquant aujourd'hui |
| D14 | HIGH | Market Data | Taux de gap Yahoo ~24-29% (coupures maintenance CME) | `QDE-012_Sprint_15.25_Lot14.2_Yahoo_Historical_Data_Source_Report.md` | Distord la suffisance apparente des données si non pris en compte | Confirmed | Documenter dans toute planification de fenêtre | Distord, ne bloque pas |
| D15 | MEDIUM | Risk Engine | `RiskPolicy.MinPositionSize` déclaré mais jamais appliqué par `RiskEngine.Evaluate` | `RiskPolicy.cs:20` vs `RiskEngine.cs` | Faux sentiment de sécurité pour un appelant qui le configurerait | Confirmed | Implémenter ou retirer/documenter | Non |
| D16 | MEDIUM | Risk (Backtest) | `OpenRisk` toujours forcé à 0 alors que les positions peuvent se chevaucher | `BacktestRiskResultBuilder.cs:19-27,94` | Sous-estime le risque concurrent réel | Confirmed (documenté) | Modèle de portefeuille multi-position (futur lot) | Possible, selon stratégie |
| D17 | MEDIUM | PnL/Equity | Equity curve = un point par trade fermé, pas de mark-to-market intrabar | `EquityPoint.cs` | `MaximumDrawdown` sous-estime potentiellement le vrai drawdown ; Sharpe/Sortino incalculables honnêtement à cette granularité | Likely | Documenter la limite ; envisager mark-to-market futur | Non bloquant pour ce qui existe, bloquant pour objectifs basés sur la volatilité (§20) |
| D18 | MEDIUM | Cost | Chiffres slippage/spread/commission "illustratifs", aucun barème broker réel cité | `QDE-012_Sprint_15.25_Lot14.7_Cost_Slippage_Execution_Realism_Foundation_Report.md` | Même activés, ne refléteraient pas un coût réel | Confirmed (auto-déclaré) | Calibrer sur barème réel + Bid/Ask réel | Partiellement |
| D19 | MEDIUM | Backtest | Aucun test de look-ahead champ-par-champ pour Measurement/Execution/Cost/Risk/PnL (preuve agrégée seulement, `CalibrationLookAheadTests`) | `Tests/Backtest/Calibration/CalibrationLookAheadTests.cs` | Un mismatch localisé pourrait être masqué par l'agrégation | Possible | Ajouter un test analogue à `BacktestSignalPipelineLookAheadTests` pour ces couches | Réduit la confiance, non bloquant |
| D20 | MEDIUM | StopLoss Research | Aucune campagne de calibration réelle exécutée à ce jour (post-fix Sprint 15.19) | Sprint 15.19 gate `K_SELECTED: NO`, absence de rapport postérieur | Protocole StopLoss reste 100% synthétique en pratique | Confirmed | Relancer capture ATAS + qualité + campagne réelle | Oui, explicitement (le protocole le dit) |
| D21 | LOW | Decision Engine | Code mort `if (finalScore > builder.Confidence)` toujours vrai (builder neuf par règle) | `DecisionEngine.cs` | Aucun effet fonctionnel | Confirmed | Nettoyage cosmétique | Non |
| D22 | LOW | EntryTriggerBuilder | Seuil READY HIGH_PRIORITY (0.55) plus bas que QUALIFIED (0.70), non expliqué | `EntryTriggerBuilder.cs:243,250` | Lisibilité, pas un bug avéré | Possible | Commentaire de justification | Non |
| D23 | LOW | Instrument | `ATASInstrumentQuantityStepResolver` existe, testé, délibérément non câblé (Lot 12.6, "STOP RULE" : `Security.LotSize` défaut=1 indiscernable d'une vraie valeur MES=1) | `ATASInstrumentQuantityStepResolver.cs` | `QuantityStep` reste manuel, bien documenté | Confirmed (décision consciente) | Revisiter si ATAS expose un flag "is populated" | Non |

---

# 7. REGIME AUDIT

| Modèle | Fenêtre / min sample | Formule validée vs référence externe ? | Statut scientifique | Calibration fenêtre | Sensibilité probable |
|---|---|---|---|---|---|
| ADF | 60 / 30 bars | Valeurs critiques MacKinnon (1994) citées ; magnitude jamais comparée à Statsmodels (placeholder vide) | PARTIALLY VALIDATED | NOT CALIBRATED | Élevée à fenêtre courte (T=100 documenté comme sous-puissant) |
| KPSS | 60 / 30 bars | Valeurs critiques Kwiatkowski (1992) citées ; magnitude jamais comparée à Statsmodels (placeholder vide) | PARTIALLY VALIDATED | NOT CALIBRATED | Déficit de puissance à T=100 honnêtement documenté |
| DFA/Hurst | 128 / 80 bars | Direction qualitative + invariance d'échelle prouvées mathématiquement ; magnitude jamais comparée à `nolds` | PARTIALLY VALIDATED | NOT CALIBRATED | Fenêtre la plus longue du groupe — coût en réactivité |
| Half-Life | 30 / 20 bars | **Oui — scikit-learn, tol 1e-6/1e-4, réelle** | **VALIDATED (Type A)** | NOT CALIBRATED | — |
| Variance Ratio | 30 / 20 bars | **Oui — NumPy, tol 1e-6, réelle** | **VALIDATED (Type A)** | NOT CALIBRATED | — |
| CUSUM | 30 / 20 bars | Recursion S+/S- validée contre référence Python ; **seuil h/k sans citation théorique** | PARTIALLY VALIDATED | NOT CALIBRATED | — |
| Bai-Perron | 128 / 64 bars | Non auditée en détail (hors périmètre agent, non contredite) | UNKNOWN | NOT CALIBRATED | — |
| HurstEvidence (proxy VR) | 30 / 20 bars | Diagnostic parallèle à DFA | TECHNICALLY VALIDATED ONLY | NOT CALIBRATED | **Orphelin — jamais consommé par Fusion** |
| VolatilityEvidence | W=30/MinN=20, `IsClustering=ACF(|returns|)lag1>0.05` | Qualitatif seulement | TECHNICALLY VALIDATED ONLY | NOT CALIBRATED | **Orphelin — jamais consommé par Fusion** (display-only) |

**Finding architectural majeur** : `RegimeEngine.Collect()` (`Engine/Regime/RegimeEngine.cs:45-101`) est un orchestrateur **mono-fenêtre par modèle** — confirmé par lecture directe du code : chaque évidence est calculée une fois avec sa propre fenêtre fixe et assemblée dans un `EvidenceSet` unique. **Aucune logique de multi-fenêtre, fenêtre dominante, consensus, stabilité/intensité ou transition de régime n'existe** dans `Engine/Regime/` — il n'y a pas de mécanisme de vote. Ceci corrige toute hypothèse préalable d'un système "20/50/80" multi-fenêtre : les fenêtres réelles sont 60(ADF/KPSS)/128(DFA)/30(HalfLife/VarianceRatio/CUSUM)/128(Bai-Perron), toutes des constantes privées sans justification empirique documentée, exprimées en nombre de barres — donc implicitement couplées au timeframe M5 (changer de timeframe sans recalibrer ces fenêtres changerait silencieusement leur signification temporelle).

---

# 8. FUSION AUDIT

- **Deux classes `EvidenceFusionEngine` distinctes** existent dans le dépôt : `Engine.Regime.Core.EvidenceFusionEngine` est un **stub mort** (retourne toujours Unknown/0, jamais instancié en dehors de ses propres tests) ; `Engine.Fusion.EvidenceFusionEngine` est la vraie implémentation utilisée partout (live + backtest), orchestrant 5 `IFusionRule`.
- Poids Scientific/Quality par règle (0.80-0.90 / 0.10-0.20 selon la règle) : vérifiés dans `Engine/Fusion/Rules/*.cs` (`MeanReversionRule`, `RandomWalkRule`, `StationarityRule`, `PersistenceRule`, et la règle de rupture structurelle). **Correction par rapport à un brouillon antérieur** : contrairement aux poids Decision, ces constantes Fusion ne portent **pas** de commentaire explicite "provisional"/"placeholder"/"arbitrary" dans `Engine/Fusion/Rules/*.cs` (grep exhaustif, zéro résultat). `FusionStateManager.cs` porte bien des commentaires "Provisional" mais pour des constantes différentes et sans rapport (EMA alpha de lissage d'état, seuil d'hystérésis) — pas pour les poids de blend des règles. L'absence de citation/test/calibration reste néanmoins entière : ces poids sont Type D (arbitraires) **par absence de justification**, pas par auto-déclaration.
- `FusionConfiguration` (mécanisme prévu pour rendre ces poids calibrables) existe (`Engine/Fusion/Core/FusionConfiguration.cs`) mais est **non branché** : `EnableCalibration=false` par défaut, confirmé ligne 16.
- **Distinction confiance statistique vs complétude** : `ScientificScore` (par évidence de régime) est une véritable mesure statistique (dérivée de p-values/statistiques de test). `QualityScore`/`MissingEvidence` sont des métriques de COMPLÉTUDE/exécution (combien d'évidences ont pu être calculées), pas des mesures de confiance statistique — le blend 0.80-0.90/0.10-0.20 mélange donc une quantité statistique avec une quantité opérationnelle, sans justification de la pondération relative.
- Point de vigilance documentaire : `QDE-006_Fusion_Architecture_Audit.md` note des dimensions "Validated" (9.6/10) sur la base d'un audit purement CONCEPTUEL (indépendance/orthogonalité des règles), sans donnée empirique — à ne pas lire isolément comme une validation statistique.
- **Statut scientifique** : NOT VALIDATED (poids). **Calibration** : NOT CALIBRATED.

---

# 9. DECISION AUDIT

- 5 règles (`StableRangeRule`, `MeanRevertingRule`, `TrendingRule`, `StructuralBreakRule`, `RandomWalkRule`), chacune : `finalScore = Clamp(w1×scientificScore + w2×qualityScore, 0, 1)` avec `w1` dominant (0.80-0.90) et `w2` mineur (0.10-0.20) selon la règle — **tous explicitement commentés "Provisional scientific weights" / "Provisional final blend weights"** dans le code source (`Engine/Decision/Rules/*.cs`, lignes 13 et 18-19 de chaque fichier). Auto-déclaration honnête, confirmée par grep.
- `NeutralMissingEvidenceValue=0.5` : correctif documenté et testé (Sprint 14, DEC-01/FUS-02) — un bug historique réel (transformer "aucune preuve" en "confirmation de l'hypothèse inverse") a été trouvé et corrigé, couvert par 8 tests. **Résolu, bon signe de discipline.**
- Code mort mineur : `if (finalScore > builder.Confidence)` toujours vrai (builder neuf par règle, `Confidence` initial = 0) — cosmétique, sans effet fonctionnel (D21).
- **Rule weights → FinalScore → Winner/RunnerUp → Difference → AmbiguityScore → AmbiguityGateThreshold** : chaîne câblée et testée logiquement (`DecisionArbitrator.cs` : `AmbiguityScore = Clamp(1 - Difference, 0, 1)`, `Difference = Winner.FinalScore - (RunnerUp?.FinalScore ?? 0)` — confirmé exact par lecture directe, arithmétique validée à 1e-12 par `Tests/Decision/DecisionArbitrationTests.cs`).
- **Paramètres à calibrer ENSEMBLE, jamais isolément** : les 5×2 poids Decision, les poids Fusion en amont (dont ils héritent `scientificScore`), et `AmbiguityGateThreshold` en aval forment un seul système couplé — changer un poids de règle déplace la distribution de `Difference`, ce qui change la signification effective de 0.95 sans que 0.95 lui-même ait changé.
- **Statut scientifique** : UNVALIDATED (poids, auto-déclarés provisoires). **Calibration** : NOT CALIBRATED.

---

# 10. SIGNAL / ENTRY AUDIT

- `ScientificModelRegistry` ne câble de vrais modèles QUE pour `MeanReversionMethodology` (Kalman/OU/DynamicZScore/Volatility/SPRT) — toutes les autres méthodologies retournent des stubs (`Score=0`, `"Scientific model placeholder"`).
- **`OverallConfidence = SuccessfulModels.Count / ExpectedModels.Count`, avec `ExpectedModels` une liste fixe de 5 modèles** (`ScientificAssessmentBuilder.cs:100-102`, `ExpectedModels` lignes 9-16) — confirmé par vérification directe. C'est un **RATIO DE COMPLÉTUDE D'EXÉCUTION**, pas une confiance statistique. Pour tout régime ≠ MeanReverting, ce ratio est mécaniquement 0.0 (aucun modèle réel câblé), bloquant systématiquement l'étage Entry.
- Ce ratio alimente ensuite directement **5 seuils de production** (Entry OpportunityStatus : 0.9/0.6/0.3 ; EntryTrigger READY : 0.70/0.55) comme si c'était une vraie confiance statistique — finding HIGH sévérité, risque de mauvaise interprétation humaine/dashboard (D6).
- Aucun Z-score/DFA/ADX explicite n'est lu directement par Entry/EntryTrigger — uniquement `OverallConfidence` (ratio) et `DynamicZScore` (sortie Kalman).
- **Important** (rappel explicite du brief) : "le système produit davantage de signaux" n'est **jamais** une preuve de calibration réussie — aucun des seuils Entry/EntryTrigger actuels ne possède de fonction objectif documentée qui irait au-delà de "produire un candidat directionnel observable".
- **Statut scientifique** : les seuils Entry (0.9/0.6/0.3) et EntryTrigger READY (0.70/0.55) sont Type D, NOT CALIBRATED, UNVALIDATED.

## Deep-dive `AmbiguityGateThreshold=0.95` (le paramètre le plus consequential du pipeline de décision)

**Historique vérifié** (commentaire de code + `QDE-012_Sprint_15.25_Lot9_Threshold_Implementation_Report.md`) :
- Original 0.5 → jamais atteint sur aucune capture ATAS réelle analysée (Lots 2-3).
- Lot 4 (5 datasets ES M5 réels) : plafond réel de `Difference` restreint à Winner==MeanReverting ≈ 0.089-0.094 — 0.5 structurellement inatteignable.
- Lot 5 : à `Difference>0.05`, conversion candidat→direction 100%.
- Lots 7-8 (OOS, 4 fenêtres indépendantes du calibrage, 9066 barres combinées) : **ROBUST RANGE 0.05–0.06** — robustesse structurelle/statistique, **PAS** une preuve de rentabilité nette (aucun modèle de coûts disponible à l'époque).
- Lot 9 : retient 0.05 (`AmbiguityScore>=0.95`), verdict officiel `IMPLEMENTED — LIVE ATAS VALIDATION REQUIRED`.

**Limite de traçabilité** : les rapports détaillés des Lots 2-8 ne sont **pas déposés séparément** dans le dépôt — seul un résumé narratif existe dans le rapport Lot 9. Méthodologie non entièrement reproductible depuis le dépôt seul.

**Classification honnête** : Scientific Status **PARTIALLY VALIDATED** (robustesse structurelle démontrée hors-échantillon avec vraie séparation train/OOS ; aucune preuve de rentabilité nette, aucune validation live). Calibration Status **PARTIALLY CALIBRATED**. Type entre A et B (calibration empirique réelle avec séparation OOS authentique, mais fonction objectif = "produire un candidat directionnel observable", pas "maximiser un critère de performance nette de coûts"). Overfitting risk **POSSIBLE** (ni CONFIRMED, ni NOT OBSERVED). Instrument : étude menée sur **ES**, production cible **MES** (même sous-jacent, tick/valeur différents, microstructure non re-testée).

---

# 11. TRADEPLAN AUDIT

- Confirmé sur les DEUX chemins d'appel : `IQIAIndicator.cs:602` (`_tradePlanEngine.Process(new TradePlanContext(entryTriggerCandidate, instrumentInfo))`, commentaire explicite lignes 599-601 : "RiskParameters is left null — the TradePlan will honestly report SIGNAL_ONLY rather than fabricate SL/sizing") et `Backtest/BacktestEngine.cs:328` (même forme à deux arguments, commentaire ligne 324 confirmant qu'elle "mirrors `OnCalculate` exactly"). `TradePlanContext` est construit avec **2 arguments seulement** dans les deux cas — `RiskParameters` toujours absent.
- `StopLoss = context.RiskParameters?.StopLoss` → toujours `null` → `PLAN_READY` **structurellement inatteignable en production actuelle** (ni live, ni backtest) — démontré uniquement par des tests avec paramètres injectés à la main (`TradePlanBuilder.cs:57-61`).
- Statuts réalistes en pratique : `NO_TRADE` (cas dominant), `SIGNAL_ONLY` (seul statut atteignable avec une direction valide). `PLAN_BLOCKED` inatteignable aujourd'hui en production.
- **Câblage réel Signal→Entry→TradePlan→Risk** : vérifié PRÉSENT au niveau du flux de données (`entryTriggerCandidate`/`entryTrigger` alimente bien `TradePlanContext`), mais **coupé net** à la frontière `TradePlan→Risk` : rien ne fournit `RiskParameters`, donc la classe `TradePlan` existe et fonctionne, mais son lien vers un Risk réel n'est jamais exercé en production.
- **Statut scientifique** : VALIDATED pour la logique de construction (ne fabrique jamais de valeur, teste A-H exhaustivement) ; NOT APPLICABLE pour StopLoss/PositionSize (structurellement absents).

---

# 12. STOP LOSS AUDIT

- **Absent en production, sur les deux chemins (live et backtest).** Aucune méthodologie de stop n'alimente `TradePlan` ni le Risk Engine live.
- Informations DISPONIBLES pour une future méthodologie : MAE (Measurement, Lot 14.4), MFE (idem), volatilité (`VolatilityModel`), régime (`Decision.Winner` par bar). Pas d'ATR implémenté nulle part (grep négatif confirmé).
- Recherche StopLoss dédiée existe : `QDE-012_StopLoss_Calibration_Protocol.md`, candidats **A1 (Volatility Stop)** et **D (Mean-Reversion Invalidation)**, formule verrouillée `SL = EstimatedEquilibrium ∓ k×InnovationStd` — mais **100% synthétique à ce jour** :
  - Sprint 15.16 : verdict `INSUFFICIENT REAL DATA`.
  - Sprint 15.18 : une capture réelle existe mais **`SCIENTIFIC_ADMISSIBILITY=BLOCKED`** (36.5% des bars sont des snapshots de bar en formation, concentrés exactement dans la portion temporelle qu'un split OOS réserverait).
  - Sprint 15.19 : bug de capture corrigé, mais **aucune nouvelle campagne réelle exécutée depuis**.
  - Sprint 15.14 : calibration exploratoire de `k` sur données synthétiques uniquement, verdict `K_SELECTED: NO` — aucun `k` retenu pour production.
- **Méthodologie de calibration nécessaire (à appliquer, PAS à exécuter dans ce lot)** : (1) obtenir une nouvelle capture ATAS réelle passant le gate de qualité (`SCIENTIFIC_ADMISSIBILITY=PASS`, contrairement à Sprint 15.18) ; (2) appliquer le protocole déjà verrouillé (A1/D) avec séparation TRAIN/VALIDATION/OOS stricte via `CalibrationWindowSet` (une fois D4 résolu) ; (3) évaluer `k` par sa capacité à éviter les sorties MAE excessives SANS sacrifier le MFE capturé — un compromis à deux métriques, pas un simple minimum ; (4) valider hors-échantillon sur une fenêtre disjointe avant toute promotion en production ; (5) seulement alors, dériver `RiskDistanceConfiguration` de ce `k`, jamais l'inverse.
- **Ce lot ne sélectionne aucun candidat SL** — conformément au brief.

---

# 13. RISK AUDIT

## Risk Engine (mécanique)
- `Engine/Risk/RiskEngine.cs` : fonction pure, sans état, sans dépendance ATAS. Formules tracées et vérifiées : `RiskDistance`, `RiskPerUnit=RiskDistance×PointValue`, `RiskBudget=min(Equity×%, montant max, daily loss remaining, open risk remaining)`, `PositionSize=floor(budget/RiskPerUnit)` arrondi/clampé, `RiskRewardRatio`.
- **35 tests unitaires + 8 tests d'intégration + 4 tests d'indépendance des raisons de rejet, tous verts.**
- **Statut scientifique (MOTEUR)** : **VALIDATED** — chaque formule prouvée par exemple chiffré exact.
- **Defect confirmé** : `RiskPolicy.MinPositionSize` déclaré mais **jamais lu** par `RiskEngine.Evaluate` (seul usage : hash de fingerprint) — champ mort (D15).

## Risk Policy (valeurs)
- **Aucune valeur de production n'existe** — tous les paramètres UI `RiskPolicy*` défaut à 0/0.0 (confirmé `IQIAIndicator.cs:229-265`), donc "non configuré". Le moteur live est **fail-closed** par construction (`CurrentEquity==0 → INVALID_EQUITY`).
- Les seules valeurs numériques observées (1%/2%, `MinRiskReward=2.0`) sont des fixtures de test, jamais promues en défaut de production.
- **Distinction explicite ENGINE CORRECT vs POLICY CALIBRÉE** appliquée : moteur correct, policy inexistante — ce ne sont PAS le même constat.

## Live vs Backtest Risk — même logique ou deux systèmes ?
- **Backtest Risk (Lot 14.8) réutilise `RiskEngine` telle quelle** (pas de duplication de formule) — `BacktestRiskConfiguration` transporte `RiskDistanceConfiguration`/`MaxExposure`, propres au backtest et absents du chemin live. Il ne s'agit donc PAS de deux moteurs divergents, mais d'UN moteur (`RiskEngine`) enveloppé différemment selon le contexte d'appel (source de `RiskDistance` : manuelle en backtest, `TradePlan.StopLoss` — toujours null — en live). Le point de divergence réel n'est pas le moteur, c'est l'ABSENCE, dans les deux contextes, d'une source fiable de distance de stop.
- `RiskDistanceConfiguration` (Backtest) : requiert une distance fournie EXTERNEMENT (jamais dérivée du TradePlan). Sans cette entrée, `PositionRiskEvaluator` rejette explicitement (`InvalidRiskDistance`), jamais un stop inventé.
- `MaxExposure` : entièrement nouveau au Lot 14.8, aucune implémentation antérieure, aucune valeur calibrée, exemple de rapport (50000$) purement illustratif, sans ancre à une règle de marge réelle.
- **62 tests PASS** (57 synthétiques + 1 réseau) pour le Backtest Risk. Zéro-régression avec Lot 14.6/14.7 prouvée algébriquement et par test quand le risque est désactivé.
- **Statut scientifique** : TECHNICALLY VALIDATED ONLY (mécanisme). **Calibration** : NOT CALIBRATABLE YET (`RiskDistance` dépend d'une méthodologie SL inexistante ; `MaxExposure` dépend d'une règle de marge non documentée).

## Position Sizing / Instrument Specification
- `TickSize`/`TickValue` (MES : 0.25/1.25, réel CME) : dérivés dynamiquement d'ATAS `Security` en live, jamais hardcodés en production (grep exhaustif : 0 littéral `"MES"`/`"ES"` de sizing en production). Type **C** (fait objectif).
- `MinQuantity`/`MaxQuantity` : mixte ATAS (`Security.LotMinSize/Max`) / manuel.
- `QuantityStep` : **exclusivement manuel** — un résolveur ATAS existe (`ATASInstrumentQuantityStepResolver`) mais est délibérément non câblé (Lot 12.6, "STOP RULE" documentée : `Security.LotSize` par défaut vaut 1, indiscernable d'une vraie valeur MES=1).
- `ContractMultiplier` : jamais fourni en production.

---

# 14. EXECUTION AUDIT

- TIME_HORIZON = seul exit rule implémenté (`ExitReason` a un seul membre). `HorizonBars=10` utilisé partout, couplé par construction au même paramètre de `MeasurementConfiguration`.
- **Convention d'entrée : `Close[i]`, la même barre que le signal** — confirmé comme contredisant directement la prescription du propre audit d'architecture antérieur du projet (`QDE-012_Sprint_15.25_Lot13_Backtest_Architecture_Audit_Report.md:1140`, défaut L-2 : *"Entrer à Close[i] alors que le signal est dérivé de Close[i]"* → défense prescrite : *"Fill à Open[i+1] ; mode FillAtSignalClose doit être étiqueté optimiste"*). Le Lot 14.5 implémente exactement la convention flaggée, sans option alternative ni étiquette (D1).
- Sortie = `Close[i+HorizonBars]` (THEORETICAL_CLOSE_EXIT), honnêtement documentée comme non réaliste.
- **35 tests + 1 Yahoo, tous PASS** pour la cohérence interne (Return bit-exact identique entre Measurement et Execution — invariant qui dépend du couplage `HorizonBars` mentionné ci-dessus).
- **Ce qui est purement mécanique** : la simulation de fill/exit elle-même (déterministe, testée). **Ce qui est calibrable** : `HorizonBars` (une fois recalé sur une durée de trade réaliste). **Ce qui dépend des données** : le réalisme du prix de fill (D1). **Ce qui dépend des coûts** : le net PnL final (D2, désactivé aujourd'hui).
- **Statut scientifique** : VALIDATED (cohérence interne) / **UNVALIDATED, biais CONFIRMÉ** (réalisme du fill).

## Measurement (Lot 14.4)
- Return/MFE/MAE/HitRate sur `HorizonBars` barres futures, formules validées par calcul manuel (précision 9 décimales), équivalence prouvée avec un scan direct High/Low.
- `HorizonBars=10`, `HitThresholds={0.001,0.002}` utilisés partout, jamais dérivés d'une analyse (ATR, distance de stop, volatilité M5 réelle) — le rapport Lot 14.4 le reconnaît lui-même.
- **44/44 tests + 1 Yahoo, PASS.**
- **Statut scientifique** : VALIDATED (formule) / NOT CALIBRATED (valeurs).

---

# 15. COST AUDIT

- `Backtest/Cost/*` (Lot 14.7) : modèle complet (commissions, spread, slippage, tick cost), formule validée.
- **`ExecutionCostConfiguration.Disabled()` est le défaut documenté comme "recommandé"** — confirmé `ExecutionCostConfiguration.cs:29-36`. Vérifié par grep exhaustif des sites d'appel réels : **désactivé partout en pratique**, y compris dans la campagne de calibration active committée dans `Tests/Backtest/Calibration/CalibrationTestFixtures.cs`.
- Les chiffres utilisés dans les tests sont explicitement qualifiés d'"illustratifs" par le rapport Lot 14.7 lui-même — aucun barème broker MES réel cité.
- **78 tests PASS.**
- **Point critique confirmé** (rappel du brief) : **aucune calibration économique finale ne peut être considérée comme valide sans coûts activés et réalistes.** Toute conclusion de performance actuelle (y compris l'étude `AmbiguityGateThreshold`) est donc structurellement incomplète sur cet axe.
- **Statut scientifique** : VALIDATED (formule) / **désactivé en pratique, donc non exercé sur aucun résultat de performance actuel.**

---

# 16. BACKTEST READINESS

| Composant | READY FOR CALIBRATION ? | Justification |
|---|---|---|
| Yahoo / HistoricalSeries | READY (mécaniquement) | Déterminisme, fingerprint SHA-256, 66+3 tests PASS ; gap ~24-29% à documenter dans toute planification |
| SignalPipeline (Regime→TradePlan) | READY (mécaniquement) | Look-ahead prouvé champ-par-champ, deux méthodes indépendantes |
| Measurement/Execution/Risk/Cost/P&L | **NOT READY tel quel** | Look-ahead prouvé par agrégats seulement (D19) ; fill biaisé (D1) ; coûts désactivés (D2) — les résultats produits aujourd'hui ne sont pas transférables à un comportement réel |
| Equity/Drawdown | PARTIELLEMENT READY | Formules validées, mais granularité "un point par position fermée" empêche tout objectif basé sur la volatilité intrabar (Sharpe/Sortino) |
| Calibration Infra (`Backtest/Calibration/`) | **NOT READY** | Framework techniquement validé (61 tests) mais **ne relie aucun `CalibrationParameterSet` au pipeline réel** (D4) — une grille ne ferait rien varier aujourd'hui |
| Fingerprint / déterminisme / run isolation | READY | Prouvés par tests dédiés (`BacktestDeterminismTests`, `BacktestRunIsolationTests`, `CalibrationRunIsolationTests`) |
| Warmup | READY | Compteur d'index explicite, n'affecte que `Status` (Warmup/Ready), jamais la production du signal |

**Verdict global Backtest Readiness** : l'infrastructure MÉCANIQUE est prête (déterminisme, reproductibilité, isolation, look-ahead pour la majeure partie de la chaîne). Le POINT DE BLOCAGE n'est pas mécanique, il est dans les DEUX BIAIS CONFIRMÉS (fill, coûts) et dans le DÉFAUT DE BINDING du framework de calibration lui-même.

---

# 17. DATASET STRATEGY

## Données disponibles aujourd'hui
- Yahoo `MES=F`/`ES=F`, M5, fenêtre glissante ~45 jours calendaires (~6 200-8 900 barres selon le taux de gap observé), fingerprint SHA-256 déterministe.
- Une capture ATAS réelle committée (`Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/`, ES, pas MES) — qualité **BLOCKED** (36.5% de bars en formation, concentrés dans la queue temporelle).
- Aucune donnée réelle n'a servi à une calibration StopLoss aboutie à ce jour (100% synthétique).

## Verdict de suffisance
**45 jours de MES M5 sont INSUFFISANTS pour une calibration scientifique robuste d'un seuil destiné à généraliser** — probablement adéquats seulement comme fenêtre de développement/preuve de concept technique. Raisonnement :
- ~30-32 jours de bourse effectifs sur 45 jours calendaires ; taux de gap ~24-29% réduit encore les barres réellement exploitables.
- Estimation raisonnée (non mesurée formellement dans le dépôt) : 2 à 4 épisodes de régime distincts par jour de session active → ~60-120 épisodes bruts sur 30 jours, beaucoup structurellement corrélés (pas indépendants au sens statistique).
- Une répartition TRAIN/VALIDATION/OOS 60/20/20% laisserait OOS avec ~6-9 jours de bourse, soit **10-20 épisodes de régime** — échantillon fragile pour un seuil généralisable.
- Aucune garantie qu'un régime "rare mais important" (choc macro, gap d'ouverture majeur) apparaisse dans une fenêtre ancrée sur "maintenant".
- Séries Yahoo continues splicées au travers des rolls de contrat, sans traitement de roll — risque d'artefact local non quantifié.

## Recommandation de durée (raisonnement, pas une étude de puissance formelle)
Viser **au minimum 6 à 12 mois de données M5 continues**, couvrant idéalement plusieurs régimes macro distincts (au moins un épisode de forte tendance, un épisode de range prolongé, un épisode de rupture/choc de volatilité si possible) avant de considérer une calibration de seuil comme scientifiquement défendable. Ceci n'est **pas** une valeur validée par une étude formelle dans ce dépôt — recommandation raisonnée à confirmer par une analyse de puissance dédiée.

## Split recommandé (proposition, non appliquée)

| Segment | Durée proposée | Justification |
|---|---|---|
| TRAIN | ~60% | Doit couvrir au moins 2-3 régimes distincts |
| VALIDATION | ~20% | Doit inclure un régime non dominant dans TRAIN, pour tester la généralisation |
| OOS | ~20%, la plus RÉCENTE | Jamais utilisée pour aucun choix de paramètre, y compris implicite |

Cette proposition suit exactement le mécanisme déjà construit par `CalibrationWindowSet`/`CalibrationWarmupContract` (préexistant dans `Backtest/Calibration/`) — aucune modification de code n'est nécessaire pour l'appliquer, seulement des dates réelles.

## Autres axes de dataset à couvrir
- **MES vs ES** : production cible MES ; études historiques (Lots 2-9, StopLoss) menées sur ES — microstructure MES jamais re-testée séparément.
- **Timeframe** : M5 uniquement partout ; aucune infrastructure pour un autre timeframe.
- **Régimes** : tendance, range/mean-reversion, rupture structurelle, volatilité basse/haute — à couvrir explicitement dans le TRAIN, pas seulement "peut-être présents" dans une fenêtre glissante.

---

# 18. OVERFITTING / DATA SNOOPING AUDIT

## Overfitting
- **`AmbiguityGateThreshold=0.95`** : **POSSIBLE** (pas CONFIRMED — vraie séparation train(Lots 4-6)/OOS(Lots 7-8) documentée sur 4 fenêtres indépendantes ; pas NOT OBSERVED — le critère de sélection était "produire un candidat observable", pas "maximiser un critère de performance", ce qui laisse la généralisation future en question ouverte).
- **Fenêtres de régime (60/128/30...)** : **POSSIBLE** — aucune étude de sensibilité trouvée, choix non documenté, risque de sur-ajustement au timeframe M5/instrument ni confirmé ni exclu.
- **Réutilisation du même dataset Yahoo 45 jours pour développement ET toute future calibration** : **LIKELY** si un futur lot utilisait la même fenêtre glissante sans discipline TRAIN/VALIDATION/OOS stricte (l'outil pour l'éviter existe déjà, rien ne l'a encore exercé sur un paramètre réel).
- **Sélection manuelle de périodes/instruments/résultats (cherry-picking)** : **NOT OBSERVED** — au contraire, plusieurs rapports (Sprint 15.16, Sprint 15.14 "K_SELECTED: NO") refusent explicitement de déclarer un résultat retenu faute de preuve suffisante — signe positif de discipline.
- **Calibration + validation sur le même dataset** : **NOT OBSERVED** pour `AmbiguityGateThreshold` (vraie séparation) ; **NOT APPLICABLE** ailleurs (rien d'autre n'a été calibré).

## Data Leakage
- **Future bar** : PASS pour Regime→TradePlan (prouvé champ-par-champ) ; PASS PARTIEL (agrégats) pour Measurement→Risk (D19).
- **Future regime/volatility/statistics** : PASS — mono-fenêtre par bar, jamais de référence en avant construite.
- **Future account state** : PASS — `BacktestScenario.Window`/`CalibrationWindow` ne filtrent jamais les bars, mais ne fournissent pas non plus d'information future (identité pure, confirmé par grep exhaustif).
- **Future calibration parameters** : PASS structurel — `CalibrationWindowSet` interdit TRAIN/VALIDATION/OOS dans le mauvais ordre. Mais voir D4 : l'ABSENCE de binding automatique paramètre→pipeline signifie qu'aucune fuite de paramètre n'est aujourd'hui POSSIBLE (rien n'est injecté) — symptôme inverse (illusion de calibration), pas une fuite.

## Survivorship / Selection Bias
- **Instrument** : MES est le seul instrument de production ciblé, ES utilisé pour les études offline — un seul chemin documenté, pas de sélection parmi plusieurs instruments testés. NOT OBSERVED.
- **Périodes** : fenêtre Yahoo glissante ancrée sur "maintenant" — ne garantit PAS la représentativité de tous les régimes historiques. **LIMITATION STRUCTURELLE**, pas un biais de sélection actif, mais un risque latent (voir §17).

## Classification récapitulative

| Risque | Niveau |
|---|---|
| Overfitting `AmbiguityGateThreshold` | POSSIBLE |
| Overfitting fenêtres de régime | POSSIBLE |
| Réutilisation dataset (futures calibrations) | LIKELY si non corrigé |
| Cherry-picking | NOT OBSERVED |
| Look-ahead Regime→TradePlan | NONE (prouvé) |
| Look-ahead Measurement→Risk | LOW (preuve agrégée seulement, pas champ-par-champ) |
| Contamination TRAIN/VALIDATION/OOS (mécanisme) | NONE (interdite structurellement par `CalibrationWindowSet`) |
| Survivorship instrument | NONE |
| Survivorship période (fenêtre glissante) | MEDIUM (limitation structurelle) |

---

# 19. PARAMETER DEPENDENCY GRAPH

Ordre déduit du CODE (flux de données réel), pas supposé a priori :

```
Fill Execution (Close[i] → Open[i+1])          [CRITIQUE — prérequis technique, en amont de tout]
Costs (activer, valeurs réelles)                [CRITIQUE — prérequis technique]
        ↓
Régime (fenêtres ADF/KPSS/DFA/HalfLife/VR/CUSUM) [aucune calibration interprétable avant correction
        ↓                                          des deux prérequis ci-dessus]
Fusion (poids non calibrés)
        ↓
Decision (poids "Provisional", AmbiguityScore)
        ↓
AmbiguityGateThreshold (0.95 — déjà partiellement calibré, à REVALIDER avec coûts)
        ↓
Signal/Entry (OverallConfidence à clarifier/renommer avant tout seuillage dessus)
        ↓
EntryTrigger (seuils READY 0.70/0.55)
        ↓
Stop Loss (méthodologie à construire — BLOQUE Risk ET TradePlan simultanément)
        ↓
Risk (RiskPolicy à décider, RiskDistance dépend de Stop Loss)
        ↓
Position Sizing
        ↓
Execution (horizon)
        ↓
P&L / Equity / Drawdown (métriques manquantes à ajouter : Sharpe/Sortino/Profit Factor/etc. — bloquées
        ↓                 par la granularité de l'equity curve, D17)
Walk-Forward / Global OOS Validation
```

## Matrice d'interaction — paramètres NE POUVANT PAS être calibrés indépendamment

| Couple | Nature du couplage |
|---|---|
| Poids Decision ↔ `AmbiguityGateThreshold` | Changer les poids de règle déplace la distribution de `Difference` — la signification effective de "0.95" change silencieusement sans que la constante elle-même change |
| Poids Fusion ↔ Poids Decision | `scientificScore` de Decision est dérivé du score de Fusion — calibrer les deux via une seule grille non structurée produit un confounding élevé (impossible d'attribuer un gain à l'un ou l'autre) |
| Seuils Entry/EntryTrigger ↔ définition de `OverallConfidence` | Si le dénominateur du ratio change (ex. d'autres méthodologies reçoivent de vrais modèles un jour), tous les seuils avals changent de signification sans avoir été eux-mêmes modifiés |
| Stop Loss ↔ `RiskDistance`/sizing | Couplage direct — `RiskDistance` EST la distance de stop ; il n'existe pas de sizing par risque indépendant du choix de SL |
| Stop Loss ↔ MFE/MAE | Le choix de `k`/distance doit être dérivé de la distribution MAE (où le stop aurait été touché) ET MFE (ce qui aurait été manqué) conjointement — les calibrer isolément produit une métrique auto-réalisatrice |
| `HorizonBars` (Execution) ↔ `HorizonBars` (Measurement) ↔ distribution de Return | Même constante utilisée dans les deux configurations — c'est déjà couplé PAR CONSTRUCTION (garantit "Return bit-exact identique" entre les deux), donc changer l'un sans l'autre casse un invariant déjà testé |
| Coûts ↔ Fréquence de signal | Les signaux fréquents/petits sont pénalisés disproportionnellement par des coûts fixes — un seuil calé sans coûts paraîtra artificiellement bon sur un ensemble de signaux fréquents |
| Position Sizing (Risk) ↔ Drawdown | Une taille plus grande amplifie un même drawdown directionnel — évaluer conjointement, jamais en fixant la taille pendant qu'on calibre les seuils d'entrée |

**Règle explicite (issue du brief)** : NE PAS calibrer Risk avant d'avoir stabilisé Entry et Stop Loss — confirmé pertinent ici : le Risk Engine (mécanique) est prêt, mais toute calibration de `RiskDistance`/`RiskPolicy` serait vaine tant qu'aucune méthodologie SL n'existe pour l'alimenter honnêtement.

---

# 20. OBJECTIVE FUNCTION ANALYSIS

**Aucune valeur n'est choisie ici — analyse conceptuelle uniquement, conformément au brief.**

| Métrique candidate | Ce qu'elle mesure | Limite majeure dans l'état actuel du projet |
|---|---|---|
| Signal frequency | Volume brut de candidats produits | N'est **jamais** une preuve de qualité (rappel explicite du brief) — utile seulement comme dénominateur de puissance statistique et comme garde-fou (trop bas = pas assez de données pour conclure) |
| Hit rate | % de trades atteignant un seuil de mouvement favorable | Sensible aux `HitThresholds` eux-mêmes NOT CALIBRATED (0.001/0.002) — risque de circularité si utilisée comme objectif alors que son propre seuil est arbitraire ; ignore la magnitude et l'asymétrie gain/perte |
| Mean return | Moyenne des rendements par trade | Sensible aux valeurs extrêmes, typiques des distributions de rendement de trading (asymétrie/queues épaisses) |
| Median return | Médiane des rendements | Robuste aux extrêmes, mais aveugle au risque de queue — précisément ce qui doit être géré par le Stop Loss |
| Expectancy (R-multiple) | Gain moyen par unité de risque pris | Le candidat le plus solide conceptuellement, mais **incalculable aujourd'hui** : nécessite un vrai dénominateur de risque (`RiskDistance` dérivé d'un Stop Loss réel), qui n'existe pas (D5) |
| MAE / MFE | Excursion défavorable/favorable maximale intra-trade | Diagnostics essentiels pour CALIBRER le Stop Loss (§12), mais ne sont pas eux-mêmes une fonction objectif de performance globale |
| Profit factor | Somme des gains / somme des pertes | Ignore la fréquence et le drawdown ; sensible à un seul trade exceptionnel sur un échantillon encore limité (§17) |
| Sharpe / Sortino | Rendement ajusté à la volatilité (totale / à la baisse) | **Incalculable honnêtement aujourd'hui** : l'equity curve n'a qu'un point par position FERMÉE, pas de mark-to-market intrabar (D17) — toute volatilité mesurée sur cette base serait artificiellement lissée/biaisée |
| Max drawdown | Pire perte cumulée depuis un sommet | Nécessaire comme CONTRAINTE (garde-fou), pas comme objectif seul — un système 100% cash a un drawdown de 0, ce qui n'est pas "optimal" |
| Return / Drawdown (type Calmar) | Rendement rapporté au pire drawdown | Bon candidat de métrique SECONDAIRE une fois le P&L net de coûts réaliste disponible |
| Net expectancy after costs | Expectancy après coûts de transaction réels | Le candidat conceptuellement le plus proche d'un objectif économique complet — mais **incalculable aujourd'hui** pour les mêmes raisons que Expectancy, ET parce que les coûts sont désactivés par défaut (D2) |

## Recommandation conceptuelle (structure, pas de valeur)

- **MÉTRIQUE PRIMAIRE recommandée, une fois les prérequis P0 résolus** : **Net Expectancy After Costs**, exprimée en R-multiples une fois qu'une vraie `RiskDistance` existe (dérivée du Stop Loss, §12) — c'est la seule métrique qui combine intrinsèquement fréquence, magnitude, asymétrie gain/perte et coût réel, sans nécessiter la granularité mark-to-market qui manque aujourd'hui (D17).
- **CONTRAINTES SECONDAIRES** : plafond de Max Drawdown ; plancher de Signal Frequency (puissance statistique minimale pour qu'une conclusion soit interprétable) ; Profit Factor > 1 comme garde-fou de sanité.
- **DIAGNOSTIQUES, jamais objectifs directs** : Hit Rate, MAE, MFE — utiles pour EXPLIQUER un résultat ou calibrer le Stop Loss, dangereux comme cible d'optimisation directe (rappel du brief : produire plus de signaux, ou un hit rate plus élevé, n'est pas en soi une preuve de qualité).
- **REPORTÉES** : Sharpe, Sortino, Return/Drawdown en tant qu'objectifs primaires — tant que l'equity curve reste à un point par position fermée (D17), toute conclusion de volatilité serait elle-même non calibrée sur une base honnête. Utilisables seulement en MONITORING une fois cette limite corrigée.

**Constat central** : la fonction objectif "la plus juste" (Net Expectancy After Costs, en R-multiples) est aujourd'hui **NON CALCULABLE** dans ce dépôt, pour trois raisons indépendantes et déjà documentées : (a) fill optimiste (D1), (b) coûts désactivés (D2), (c) absence de dénominateur de risque réel car pas de Stop Loss (D5). Définir cette fonction objectif est donc un exercice conceptuel à ce stade — son calcul réel est un livrable d'un futur lot, pas de celui-ci.

---

# 21. CALIBRATION PRIORITY MATRIX

## P0 — BLOQUANTS (à corriger avant toute calibration de paramètre de trading)
1. **D1** — Corriger le biais de fill d'exécution (`Close[i]` → `Open[i+1]`) ou l'étiqueter explicitement "optimiste" partout où il est utilisé.
2. **D2** — Activer un modèle de coûts avec des valeurs réalistes par défaut (au moins pour toute campagne de calibration).
3. **D5** — Construire/valider une méthodologie Stop Loss de production (bloque simultanément TradePlan et Risk).
4. **D4** — Construire le binding `CalibrationParameterSet → CalibrationExperimentSetup` (ou documenter formellement le contrat "câblage manuel obligatoire") avant tout usage de `CalibrationGrid`.
5. **D3** — Décider et documenter des valeurs `RiskPolicy` de production (nécessaire avant tout usage live, pas nécessairement avant une calibration purement backtest).

## P1 — SCIENTIFIC READINESS (validation nécessaire avant de faire confiance aux résultats d'une calibration)
- **D9** — Remplir les références numériques externes ADF/KPSS/DFA (Statsmodels/nolds), aujourd'hui des placeholders vides.
- **D8** — Retrouver/documenter la source théorique du seuil CUSUM `h=σ√(2N ln N)`.
- **D6** — Renommer/documenter `OverallConfidence` pour ce qu'il est réellement (ratio de complétude), avant de calibrer quoi que ce soit dessus.
- **D19** — Étendre la preuve de look-ahead à Measurement→Execution→Cost→Risk→PnL au niveau champ-par-champ (aujourd'hui agrégée seulement).
- **§17** — Acquérir un dataset étendu (6-12 mois, plusieurs régimes) — la fenêtre actuelle de 45 jours est insuffisante pour une généralisation défendable.
- **D20** — Relancer une capture ATAS réelle passant le gate de qualité pour le Stop Loss (Sprint 15.18 est `BLOCKED`).

## P2 — CALIBRATION (paramètres prêts à être calibrés une fois P0/P1 résolus)
- Fenêtres Regime (ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM/Bai-Perron).
- Poids Fusion (une fois `FusionConfiguration.EnableCalibration` branché).
- Poids Decision.
- `AmbiguityGateThreshold` — REVALIDATION (pas une nouvelle sélection), avec coûts activés + capture live courte.
- Seuils Entry/EntryTrigger (0.9/0.6/0.3/0.70/0.55) — après correction de D6.
- `MeasurementConfiguration.HorizonBars`/`HitThresholds`, `ExecutionConfiguration.HorizonBars` (à calibrer conjointement, cf. §19).
- Modèle de coûts — valeurs réelles (barème broker MES).

## P3 — OPTIMISATION FUTURE (après stabilisation du système)
- Sélection finale de la méthodologie Stop Loss (candidat A1 vs D) et de son paramètre `k`, une fois une capture réelle de qualité admissible disponible.
- `RiskDistanceConfiguration`/`MaxExposure` de production (dépendent du Stop Loss ci-dessus).
- Modèle de portefeuille multi-position / `OpenRisk` concurrent (D16).
- Mark-to-market intrabar de l'equity curve, pour débloquer Sharpe/Sortino/Return-Drawdown comme métriques secondaires fiables (D17).
- Walk-forward global / validation OOS de bout en bout sur l'ensemble des paramètres calibrés ci-dessus.

---

# 22. RECOMMENDED ROADMAP

Le nombre de lots découle des dépendances réelles identifiées ci-dessus (§19, §21), pas d'un objectif de comptage.

| Lot | Objectif | Pourquoi maintenant | Prérequis | Paramètres concernés | Dataset | Méthode | TRAIN/VALIDATION/OOS | Fonction objectif | Risques | Critères PASS/FAIL | Livrables |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 14.10 | Execution Realism Fix | P0 le plus simple, le plus circonscrit, et bloquant pour tout le reste | Aucun | Convention d'entrée (fill) | N/A (fix technique) | Non-régression + nouveau test de réalisme | N/A | N/A | Faible (fix ciblé) | Comportement `Open[i+1]` disponible par défaut ; ancien comportement disponible en option étiquetée "optimiste" | Code + tests + rapport |
| 14.11 | Cost Activation & Calibration | P0, doit suivre immédiatement le fill (le fill détermine le prix qui alimente le coût) | 14.10 | Slippage/Spread/Commission/Fees | Barème broker documenté + Yahoo | Calibration directe (fait de marché, pas un fit statistique) | N/A | N/A | Faible si barème correctement sourcé | Coûts non-nuls par défaut, valeurs sourcées et citées | Config + tests + rapport |
| 14.12 | Calibration Binding | P0 — sans ceci, aucune future grille n'a d'effet réel | Aucun (indépendant de 14.10/14.11) | Mécanisme `CalibrationParameterSet→CalibrationExperimentSetup` | N/A | Ingénierie + tests de garde | N/A | N/A | Moyen (risque de sur-généraliser le mécanisme) | Test explicite prouvant qu'un axe de grille change le résultat | Code + tests + rapport |
| 14.13 | Extended Dataset Acquisition | P1 — 45 jours insuffisants pour toute calibration généralisable | Aucun | N/A | Yahoo étendu (6-12 mois) | Acquisition + fingerprint + audit qualité (gaps, rolls) | — | — | Faible (mécanique déjà prouvée) | Dataset fingerprinté couvrant ≥2 régimes macro distincts | Dataset + rapport de qualité |
| 14.14 | Regime Window Sensitivity Study | P2, mais nécessite 14.13 pour être défendable | 14.13 | Fenêtres ADF/KPSS/DFA/HalfLife/VR/CUSUM | Dataset étendu | Étude de sensibilité + walk-forward | Oui | Oui | Oui | Overfitting fenêtre (POSSIBLE aujourd'hui) | Stabilité du régime détecté across TRAIN/VALIDATION | Rapport + valeurs proposées (non appliquées) |
| 14.15 | Fusion / Decision Weight Calibration | P2, nécessite le binding (14.12) et des coûts réalistes (14.11) pour être interprétable | 14.11, 14.12, 14.13 | Poids des 5+5 règles | Dataset étendu | Grid search via le framework existant | Oui | Oui | Oui | Confounding Fusion↔Decision (§19) | Amélioration mesurable sur métrique de décision définie sans score combiné arbitraire | Rapport + poids proposés (non appliqués) |
| 14.16 | Stop Loss Methodology (real data) | P0/P1 — bloque Risk et TradePlan | Nouvelle capture ATAS passant le gate qualité (D20) | `k` (Innovation Std) ou distance A1/D | Capture ATAS réelle qualité admissible | Protocole déjà verrouillé (`QDE-012_StopLoss_Calibration_Protocol.md`) | Oui | Oui | Oui | Overfitting `k` sur échantillon réduit | `SCIENTIFIC_ADMISSIBILITY=PASS` avant toute campagne ; validation OOS du compromis MAE/MFE | Rapport + candidat SL proposé (non appliqué) |
| 14.17 | Risk Distance / TradePlan Wiring | P2, dépend directement de 14.16 | 14.16 | `RiskDistanceConfiguration`, câblage `TradePlan.StopLoss` | N/A | Câblage + tests de non-régression | N/A | N/A | Risque de rendre `PLAN_READY` atteignable prématurément sans validation suffisante | `PLAN_READY` atteignable en test avec un SL réellement dérivé, jamais fabriqué | Code + tests + rapport |
| 14.18 | AmbiguityGateThreshold Revalidation | P2 — revalidation, pas une nouvelle sélection | 14.11 (coûts), capture live courte | `AmbiguityGateThreshold` | Dataset étendu + capture ATAS live 5-15 min | Reproduction Lots 4-9 + coûts | Oui | Oui | Oui (capture courte) | Robustesse non maintenue net de coûts | Robustesse maintenue net de coûts, ou seuil révisé avec preuve | Rapport |
| 14.19 | Risk Policy Definition | P3 — décision, pas un fit statistique | 14.16/14.17 | `RiskPolicy.*` | N/A (décision + backtest de stress) | Analyse de scénario | — | — | — | Sous-dimensionnement/sur-exposition non quantifiés | Valeurs documentées avec justification de risque de compte | Config + rapport |
| 14.20 | Equity Mark-to-Market | P3 — débloque Sharpe/Sortino honnêtes | Aucun (indépendant) | Granularité equity curve | Dataset étendu | Extension du modèle P&L existant | N/A | N/A | Faible | Drawdown intrabar mesurable, Sharpe/Sortino calculables | Code + tests + rapport |
| 14.21 | Global Walk-Forward OOS Validation | P3 — validation finale de bout en bout | Tous les lots ci-dessus | Tous | Dataset étendu | Walk-forward multi-fenêtre | Oui | Oui | Oui | Résultats non transférables entre fenêtres | Performance nette de coûts stable across fenêtres | Rapport final de calibration |

*(Numérotation indicative — à ajuster selon les décisions réelles prises entre-temps.)*

## Next Action

**Recommandation unique : LOT 14.10 — CORRECTION DU BIAIS DE FILL D'EXÉCUTION.** C'est le seul défaut de cet audit qui coche simultanément : sévérité CRITICAL, statut CONFIRMED (pas juste probable/possible), déjà identifié et prescrit par le projet lui-même (Lot 13, jamais appliqué), bloquant pour toute calibration ultérieure, et le plus simple/circonscrit des défauts CRITICAL (contrairement à "construire une méthodologie Stop Loss" ou "obtenir 6-12 mois de données", qui sont des efforts de recherche substantiels). L'activation des coûts (14.11) doit suivre immédiatement, le fill étant structurellement en amont (il détermine le prix qui alimente ensuite le coût).

---

# 23. HANDOFF CONTEXT — NEXT SESSION

## PROJECT STATE

**Projet** : IQIA — système de trading quantitatif intraday.
**Objectif** : produire une décision de trading complète et déterministe à partir de données de marché, via des modèles statistiques de détection de régime (stationnarité, persistance, retour à la moyenne, rupture structurelle) fusionnés en une décision, convertie en signal directionnel, plan de trade, dimensionnement de risque et exécution — validé d'abord hors-ligne (Yahoo) puis en conditions réelles (ATAS).
**Instrument de production** : MES (Micro E-mini S&P 500, CME). `TickSize=0.25`, `TickValue=$1.25`, `PointValue=$5` (confirmés dans le code, Type C).
**Timeframe primaire** : M5.
**Runtime** : ATAS (`IQIAIndicator` dérive de `ATAS.Indicators.Indicator`).
**Environnement de recherche/backtest** : Yahoo Historical Data (`ES=F`/`MES=F`, M5, fenêtre glissante ~45 jours, ~6 200-8 900 barres selon gaps).

**Architecture** : voir §2 (carte complète).

**Lots terminés** (résumé — table complète dans le corps du rapport, brouillon antérieur) : Lot 9 (`AmbiguityGateThreshold`), Lot 10-11 (Risk Engine + intégration ATAS), Lots 12.1-12.12 (binding ATAS compte/instrument, dashboards), Lot 13 (audit architecture backtest, 14+ risques identifiés dont L-2 jamais corrigé), Lots 14.1-14.8 (Backtest Foundation → Yahoo → Signal Pipeline → Measurement → Execution → PnL/Equity → Cost → Risk), Lot 14.9 (framework de calibration `Backtest/Calibration/`, déjà présent dans le dépôt au début de CE lot, 61 tests PASS). **Ce lot (audit global)** ne modifie rien et ne complète pas de nouveau lot d'implémentation.

**Infrastructure backtest disponible** : pipeline complet Yahoo→Regime→Decision→Signal→Entry→EntryTrigger→TradePlan→Measurement→Execution→Cost→Risk→PnL→Equity→Calibration, fonctionnel bout-en-bout, déterministe, reproductible (build Debug/Release PASS confirmés dans ce lot, voir §0.1 pour le compte de tests).

## SCIENTIFIC STATE

**Modèles validés (Type A)** : Half-Life (scikit-learn, tol 1e-6/1e-4), Variance Ratio (NumPy, tol 1e-6).
**Modèles partiellement validés** : ADF, KPSS, DFA/Hurst (valeurs critiques/direction qualitative correctes, magnitude numérique jamais comparée à une référence externe — placeholders vides) ; CUSUM (recursion validée, seuil non cité) ; `AmbiguityGateThreshold` (séparation OOS réelle, sans coûts ni validation live).
**Modèles non validés** : poids Fusion, poids Decision (auto-déclarés "Provisional"), seuils Entry/EntryTrigger, fenêtres de régime, Bai-Perron (non audité en détail).
**Paramètres calibrés** : aucun au sens strict (calibration empirique + OOS + reproductible).
**Paramètre partiellement calibré** : `AmbiguityGateThreshold=0.95` uniquement.

## STRUCTURAL DEFECTS

Voir §6 pour la table complète (5 CRITICAL, 9 HIGH, 5 MEDIUM, 3 LOW). Les 5 CRITICAL en un coup d'œil : (1) fill `Close[i]` jamais corrigé depuis Lot 13, (2) coûts désactivés par défaut partout, (3) `RiskPolicy` de production inexistante, (4) `Backtest/Calibration/` sans binding paramètre→pipeline, (5) absence de méthodologie Stop Loss bloquant Risk+TradePlan.

## CALIBRATION BLOCKERS

Exactement les 5 défauts CRITICAL ci-dessus (§6, §21 P0). Aucune calibration de paramètre de décision (poids, seuils, fenêtres) ne devrait être entreprise avant leur résolution — calibrer sur un pipeline biaisé produirait des résultats non transférables.

## DATA

- Yahoo `MES=F`/`ES=F` M5, fenêtre glissante ~45 jours (~6 200-8 900 barres selon gaps), fingerprint SHA-256 déterministe.
- Une capture ATAS réelle committée (ES, pas MES) — qualité **BLOCKED**.
- Séparation TRAIN/VALIDATION/OOS recommandée : 60/20/20%, sur un dataset étendu à 6-12 mois (voir §17) — le mécanisme existe déjà (`CalibrationWindowSet`), seules des dates réelles manquent.

## CURRENT PARAMETERS (synthèse — table complète §3)

| Paramètre | État |
|---|---|
| `AmbiguityGateThreshold=0.95` | PARTIALLY CALIBRATED — ne pas modifier sans nouvelle étude complète incluant coûts |
| `RiskDistanceConfiguration` | Toujours fournie manuellement, jamais dérivée d'un vrai stop |
| `ExecutionCostConfiguration` | `Disabled()` par défaut partout |
| Poids Fusion/Decision | NOT CALIBRATED, "Provisional" (Decision, auto-déclaré) |
| Fenêtres Regime | 60(ADF/KPSS)/128(DFA)/30(HalfLife/VR/CUSUM)/128(Bai-Perron), NOT CALIBRATED |
| Seuils Entry/EntryTrigger | 0.9/0.6/0.3/0.70/0.55, NOT CALIBRATED, Type D |
| `RiskPolicy.*` | N'existe pas en production (fail-closed) |

## NEXT LOT

**14.10 — Correction du biais de fill d'exécution** (`Close[i]` → `Open[i+1]`, ou étiquetage explicite "optimiste"). Justification complète en §22.

## DO NOT DO

- Ne PAS calibrer/modifier `AmbiguityGateThreshold` sans une nouvelle étude complète incluant les coûts.
- Ne PAS toucher `RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`, `DecisionArbitrator.cs`, `EntryTriggerBuilder.cs`, `TradePlanBuilder.cs`, `RegimeEngine.cs`, `EvidenceFusionEngine.cs` (`Engine.Fusion`), `FusionStateManager.cs`, `DecisionEngine.cs`, `SignalEngine.cs`, `EntryEngine.cs`, `EntryTriggerEngine.cs`, `YahooHistoricalBarSource.cs`, `MarketContextFactory.cs`, `BacktestEngine.cs` sans nécessité technique explicitement documentée et approuvée.
- Ne PAS lancer de calibration de poids Fusion/Decision/fenêtres de régime avant d'avoir résolu les 5 défauts CRITICAL (§6, §21 P0).
- Ne PAS utiliser `CalibrationGrid` en pensant qu'elle fait varier le comportement du pipeline — elle ne le fait pas tant que D4 n'est pas résolu.
- Ne PAS interpréter "plus de signaux" ou "hit rate plus élevé" comme une preuve de bonne calibration.
- Ne PAS sélectionner de candidat Stop Loss (A1 vs D) sans une nouvelle capture ATAS passant le gate qualité (le Sprint 15.18 est `BLOCKED`).
- Ne PAS committer, déployer de DLL, ou lancer ATAS dans le cadre d'un audit — seulement dans le cadre d'un lot d'implémentation explicitement scopé pour cela.

## IMPORTANT HISTORICAL DECISIONS

`AmbiguityScore=Clamp(1-Difference,0,1)` (formule confirmée exacte) ; `AmbiguityGateThreshold` 0.95 remplace 0.5 (jamais atteignable, Lots 2-3) ; plage [0.05,0.06] jugée robuste OOS (Lots 7-8, ES M5, 9066 bars, SANS modèle de coûts) ; `RegimeEngine` est mono-fenêtre par modèle, PAS de multi-fenêtre/consensus/dominant-window ; deux classes `EvidenceFusionEngine` existent, seule `Engine.Fusion.EvidenceFusionEngine` est utilisée (`Engine.Regime.Core.EvidenceFusionEngine` est un stub mort) ; fill de backtest au Close de la barre de signal décidé au Lot 14.5 EN CONTRADICTION avec la prescription du Lot 13 (jamais résolue) ; Risk Engine fail-closed par construction (Equity=0 → rejet) ; deux systèmes "Calibration" distincts et sans rapport (`Core/Calibration` = capture dataset ATAS Sprint 15.17-15.19 ; `Backtest/Calibration` = framework d'expérimentation, préexistant à ce lot).

---

# 24. FINAL VERDICT

**Le Lot 14.9 (audit global) est PASS au sens de ses propres critères de succès** : le projet entier a été cartographié, les paramètres importants sont inventoriés avec statut de calibration et classification scientifique, les dépendances entre paramètres sont explicitées, les défauts structurels sont séparés des questions de calibration proprement dites, les composants non connectés sont identifiés (TradePlan↔Risk, `CalibrationParameterSet`↔pipeline, `VolatilityEvidence`/`HurstEvidence` orphelins de Fusion), les risques de leakage/overfitting sont analysés, une fonction objectif future est définie conceptuellement (sans valeur choisie), une stratégie TRAIN/VALIDATION/TEST/OOS est définie, et l'ordre des futurs lots est justifié par les dépendances réelles du code plutôt que supposé. Aucun paramètre n'a été modifié, aucun code de calibration n'a été ajouté, aucun test existant n'a été modifié, aucun commit n'a été fait. Debug build PASS, Release build PASS (confirmés dans ce lot). Voir §0.1 pour le résultat détaillé de la suite de tests.

**Le projet IQIA lui-même N'EST PAS PRÊT pour une campagne de calibration de production.** Il dispose d'un socle mécanique exceptionnellement solide et honnête — le meilleur atout du projet — mais d'une couche de calibration scientifique presque entièrement vide, et de deux biais structurels CONFIRMÉS (fill optimiste, coûts désactivés) qui invalideraient toute conclusion de performance obtenue aujourd'hui. Le chemin à suivre est clair et court : corriger les 5 défauts CRITICAL (§21 P0) — le premier d'entre eux, le fill d'exécution, est simple et bien circonscrit — puis reprendre la calibration dans l'ordre dérivé du code (§19), en commençant par les fenêtres de régime et les poids Fusion/Decision une fois le dataset étendu (§17) disponible, et en traitant le Stop Loss comme un prérequis de recherche à part entière avant tout sizing par risque réaliste.

**STOP.**
