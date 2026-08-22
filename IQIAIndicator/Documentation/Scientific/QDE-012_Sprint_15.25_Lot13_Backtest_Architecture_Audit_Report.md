# QDE-012 — Sprint 15.25 — Lot 13
# Audit architectural du futur Backtest Engine IQIA

**Type** : AUDIT ARCHITECTURAL — LECTURE SEULE
**Portée** : inspection du dépôt réel, aucune modification de code
**Livrable** : ce document uniquement
**Date** : 2026-08-19
**Branche auditée** : `feature/structural-stability-v2` (HEAD `f485ea4`)

---

## 0. Contrat d'exécution de ce lot

| Interdiction | Respectée |
|---|---|
| Modification de code | OUI — aucun `.cs` touché |
| Build | OUI — aucun `dotnet build` lancé |
| Génération de DLL | OUI |
| Deployment | OUI |
| Commit | OUI |
| Modification de dataset | OUI |
| Modification du seuil (`AmbiguityGateThreshold`) | OUI |
| Modification du Risk Engine | OUI |
| Modification de `DecisionArbitrator`, `EntryTriggerBuilder`, `TradePlanBuilder`, `RiskEngine`, `RiskPolicy`, `InstrumentRiskSpecification`, `RiskEngineRequest`, `RiskAssessment` | OUI |
| Lancement d'ATAS | OUI |

Une seule opération d'inspection binaire a été tentée (lecture de métadonnées de `ATAS.Indicators.dll` en `ReflectionOnly`, sans exécution) ; elle a échoué pour incompatibilité de runtime (PowerShell 5.1 = .NET Framework vs assembly .NET 8) et **n'a pas été contournée par un build**. La conclusion correspondante (§4.2) s'appuie donc exclusivement sur les affirmations déjà documentées et vérifiées dans le dépôt.

---

## 1. EXECUTIVE SUMMARY

### 1.1 Résultat principal

**Le pipeline scientifique IQIA est déjà, dans sa quasi-totalité, indépendant d'ATAS.** L'audit n'a trouvé **qu'un seul point de couplage réel** entre ATAS et la chaîne de calcul :

```
Core/MarketContextBuilder.cs:14
    private readonly Func<int, IndicatorCandle> _getBar;
```

Tout ce qui est en aval — `RegimeEngine`, `EvidenceFusionEngine`, `FusionStateManager`, `DecisionEngine`, `DecisionArbitrator`, `MethodologyEngine`, `ScientificModelRegistry`, les 5 modèles scientifiques, `ScientificFusionEngine`, `EntryEngine`, `EntryTriggerEngine`/`EntryTriggerBuilder`, `TradePlanEngine`/`TradePlanBuilder`, `RiskEngine`, `ScientificDatasetCollector` — consomme exclusivement des types **purs du projet** (`Core.MarketContext`, `Engine.ScientificModels.Abstractions.MarketContext`), **jamais un type ATAS**.

Ce n'est pas une hypothèse : c'est déjà **démontré empiriquement** par le harness `Tests/Research/StopLossCalibration/`, qui fait tourner depuis le Sprint 15.10 les modèles scientifiques réels (`KalmanFilterModel`, `OrnsteinUhlenbeckModel`, `DynamicZScoreModel`, `VolatilityModel`, `HalfLifeEvidence`) sur des séries `decimal[]` arbitraires, sans ATAS, avec preuve automatisée d'absence de look-ahead.

### 1.2 Ce qui manque réellement

Quatre briques, **toutes additives**, aucune ne nécessitant de modifier un composant protégé :

| # | Brique manquante | Existe-t-il un précédent réutilisable ? |
|---|---|---|
| 1 | Source de données historiques (port + adapter) | NON en production ; `RealMarketOhlcvCsvReader` (Tests) est le précédent |
| 2 | Construction d'un `Core.MarketContext` sans `IndicatorCandle` | PARTIEL — les 7 types du contexte sont publics et librement constructibles |
| 3 | Compte simulé (mutation de l'equity barre par barre) | NON — `AccountState` existe mais est immuable et alimenté uniquement par ATAS |
| 4 | Modèle d'exécution + modèle de coûts (fill/slippage/commission) | NON — n'existe nulle part |

### 1.3 Blocage fonctionnel à connaître avant le LOT 14

**Aujourd'hui, un backtest bout-en-bout produirait zéro trade accepté**, et ce n'est pas un défaut de l'architecture backtest :

```
TradePlanContext.RiskParameters == null           (IQIAIndicator.cs:602, commentaire ligne 599-601)
  → TradePlanBuilder.cs:57  stopLoss = null
  → TradePlan.Status = SIGNAL_ONLY
  → RiskEngineRequest.StopLoss = null
  → RiskEngine.cs:66-73  RiskRejectionReason.INVALID_STOP_LOSS
  → RiskAssessment.Status = REJECTED, systématiquement
```

Le point d'injection existe déjà et est **optionnel** : `TradePlanContext(EntryTriggerCandidate, InstrumentInfo, TradeRiskParameters?)`. Le Backtest Engine peut donc fournir un `StopLoss` **sans modifier `TradePlanBuilder`**. La *valeur* de ce stop-loss relève de la calibration A1 (`k × InnovationStd`, `Output/A1_calibration_summary.txt`) et **ne doit pas être décidée par le LOT 14**.

### 1.4 Verdict condensé

| Axe | Verdict |
|---|---|
| ARCHITECTURE STATUS | **PARTIALLY READY** |
| ATAS DECOUPLING | **GOOD** (couche calcul) / **PARTIAL** (assembly + ingestion) |
| BACKTEST FEASIBILITY | **YES** |

Détail en §20.

---

## 2. CURRENT ARCHITECTURE

### 2.1 Vue d'ensemble (code réel, `IQIAIndicator.cs:436-953`)

```
ATAS Indicator host
  │
  ├─ OnCalculate(bar, value)                      IQIAIndicator.cs:436
  │    │
  │    ├─ [1] MarketContextBuilder.Build(bar, CurrentBar)      ← SEUL POINT ATAS
  │    │        └─ GetCandle(bar) : IndicatorCandle            IQIAIndicator.cs:1250
  │    │    → Core.MarketContext                               (type PUR)
  │    │
  │    ├─ [2] MarketContextValidator.Validate(context)
  │    │        → si !IsValid : return (pipeline gelé pour cette barre)
  │    │
  │    ├─ [3] RegimeEngine.Collect(context)        → EvidenceSet   (9 évidences)
  │    ├─ [4] EvidenceFusionEngine.Fuse(...)       → FusionResult  (5 dimensions)
  │    ├─ [5] FusionStateManager.Update(...)       → FusionSnapshot (lissage temporel)
  │    ├─ [6] DecisionEngine.Evaluate(...)         → DecisionResult (Winner + Ambiguity)
  │    ├─ [7] MethodologyEngine.Evaluate(...)      → MethodologySelection
  │    │
  │    ├─ [8] CreateScientificMarketContext(context, bar)      IQIAIndicator.cs:1226
  │    │        └─ GetCandle(i).Close pour i ∈ [bar-499 .. bar] ← 2e POINT ATAS
  │    │    → ScientificModels.Abstractions.MarketContext      (type PUR)
  │    │
  │    ├─ [9] SignalEngine.Process(...)            Engine/Signal/SignalEngine.cs:48
  │    │        ├─ ScientificModelRegistry.Resolve → 5 modèles
  │    │        ├─ ScientificFusionEngine.Assess   → ScientificAssessment
  │    │        ├─ EntryEngine.Process             → EntryCandidate
  │    │        ├─ EntryTriggerEngine.Process      → EntryTriggerCandidate + EntryTiming
  │    │        ├─ VisualizationEngine.Process     → VisualizationCandidate
  │    │        ├─ ChartAnnotationEngine.Process   → ChartAnnotationCandidate
  │    │        └─ OpportunityPresentationEngine   → OpportunityPresentation
  │    │
  │    ├─ [10] TradePlanEngine.Process(TradePlanContext)   → TradePlan
  │    │
  │    ├─ [11] RISK STAGE (try/catch)              IQIAIndicator.cs:657-859   ← 3e POINT ATAS
  │    │        ├─ TradingManager.Portfolio.IsReplay()
  │    │        ├─ ATASEquityReplayDetector.IsReplayContext(...)
  │    │        ├─ ATASAccountStateAdapter.TryGetCurrentEquity(TradingStatisticsProvider, ...)
  │    │        ├─ ATASAccountStateAdapter.Build(...)         → AccountState
  │    │        ├─ RiskPolicyFactory.FromRawInputs(...)       → RiskPolicy
  │    │        ├─ ATASInstrumentAdapter.Build(Security, ...) → InstrumentRiskSpecification
  │    │        ├─ RiskEngineRequestFactory.FromTradePlan(...)→ RiskEngineRequest?
  │    │        └─ RiskEngine.Evaluate(request)               → RiskAssessment
  │    │
  │    ├─ [12] TradePlanAnnotationEngine.Process(...)
  │    │
  │    └─ [13] ScientificDatasetCollector.Add(ScientificDatasetRecord.From(...))
  │
  └─ OnRender(renderContext, layout)               IQIAIndicator.cs:1046  ← 4e POINT ATAS
       ├─ ATASRenderer.Render / RenderTradePlan
       └─ DashboardManager.Draw(DashboardContext)
```

### 2.2 Cartographie détaillée demandée au §2 du brief

Légende « Réutilisable hors ATAS » : **YES** = compile et s'exécute sans référence à un type ATAS · **PARTIAL** = code utile mais un point d'entrée ou une dépendance de compilation à corriger · **NO** = dépend structurellement d'ATAS.

#### (1) Point d'entrée des données marché

| Champ | Valeur |
|---|---|
| Fichier | `IQIAIndicator.cs` |
| Classe | `IQIAIndicator : Indicator` |
| Méthode | `OnCalculate(int bar, decimal value)` — ligne 436 ; source réelle : `GetCandle(bar)` héritée de `ATAS.Indicators.Indicator`, câblée ligne 1250 |
| Rôle | Callback ATAS invoqué par barre ; orchestre tout le pipeline |
| Dépendances | `ATAS.Indicators`, `ATAS.DataFeedsCore`, `OFT.Rendering` |
| Dépendances ATAS | Totales (héritage de `Indicator`) |
| **Réutilisable** | **NO** |
| Raison | Classe hôte ATAS. Le Backtest Engine sera un **hôte alternatif**, pas une modification de celui-ci. |

#### (2) Structure représentant une barre/candle

| Champ | Valeur |
|---|---|
| Fichier | *(externe)* `ATAS.Indicators.dll` |
| Classe | `IndicatorCandle` |
| Rôle | Barre native ATAS : `Open/High/Low/Close/Volume/Bid/Ask/Delta/Time` |
| Dépendances ATAS | Type ATAS |
| **Réutilisable** | **NO** |
| Raison | Type tiers. Le dépôt ne l'instancie **nulle part**, y compris en test — `Tests/Core/MarketContextBuilderSelfHealingTests.cs:19-21` documente explicitement que `MarketContextBuilder.Build` « remains a live-ATAS-only path ». |

#### (3) Structures OHLCV + Timestamp (côté IQIA)

| Type | Fichier | Contenu | Réutilisable |
|---|---|---|---|
| `PriceInfo` | `Core/PriceInfo.cs` | `Open, High, Low, Close, Median, TypicalPrice` (`readonly record struct`) | **YES** |
| `VolumeInfo` | `Core/VolumeInfo.cs` | `Volume, BidVolume, AskVolume, Delta` | **YES** |
| `InstrumentInfo` | `Core/InstrumentInfo.cs` | `Symbol, TickSize, TickValue, PointValue, Decimals` | **YES** |
| `MarketClock` | `Core/MarketClock.cs` | `CurrentTime, CurrentDate, DayOfWeek, Session, ElapsedMinutes, IsFirstBar, IsLastBar` | **YES** |
| `SessionInfo` | `Core/SessionInfo.cs` | `Name, MarketOpen, MarketClose` | **YES** |
| `ExecutionContext` | `Core/ExecutionContext.cs` | `CurrentBar, LastCalculatedBar, IsRealtime, IsHistorical, IsReplay` | **YES** |
| `MarketContext` | `Core/MarketContext.cs` | agrégat des 6 ci-dessus + `BarIndex`, `TimeFrame` | **YES** |

**Fait déterminant** : ces 7 types sont `public`, sans dépendance ATAS, et `MarketContext` n'expose que des propriétés `required { get; init; }`. **Un `Core.MarketContext` complet peut donc être construit par n'importe quel code, sans ATAS et sans modifier une seule ligne existante.**

#### (4) Constructeur de bougies

| Champ | Valeur |
|---|---|
| Fichier | `Core/MarketContextBuilder.cs` |
| Classe | `MarketContextBuilder` |
| Méthodes | `Build(int bar, int currentBar)` (ligne 88), `RefreshInstrument(...)` (ligne 68), `TickSize` (ligne 52) |
| Rôle | Unique traducteur `IndicatorCandle` → `Core.MarketContext` ; calcule `Median`, `TypicalPrice`, `ElapsedMinutes`, et l'heuristique Replay |
| État interne | `_firstBarTime`, `_maxRealtimeBar` (reset si `bar == 0`), snapshot instrument |
| Dépendances ATAS | `using ATAS.Indicators` + `Func<int, IndicatorCandle>` |
| **Réutilisable** | **PARTIAL** |
| Raison | La *logique* (mapping + dérivés) est réutilisable à 100 % ; **la signature ne l'est pas**. `RefreshInstrument`/`TickSize` sont déjà prouvés ATAS-indépendants par les tests. |

#### (5) Feature Engine

**Il n'existe pas de « Feature Engine » nommé.** Le rôle est réparti sur trois emplacements, à ne pas confondre :

| Emplacement | Rôle réel | Réutilisable |
|---|---|---|
| `Core/MarketCache.cs` | **Coquille vide** (Sprint 1, aucun champ) — placeholder documenté pour Atr/Vwap/RollingMean | **YES** (sans objet) |
| `Engine/Regime/RegimeEngine.cs` | Fenêtrage : buffer circulaire de 128 closes → 6 `EvidenceContext` de tailles différentes | **YES** |
| `IQIAIndicator.cs:1226` `CreateScientificMarketContext` | Fenêtre glissante de 500 closes pour la couche scientifique | **PARTIAL** (appelle `GetCandle`) |

#### (6) Regime Engine

| Champ | Valeur |
|---|---|
| Fichier | `Engine/Regime/RegimeEngine.cs` |
| Méthode | `Collect(Core.MarketContext) → EvidenceSet` |
| Rôle | Orchestrateur pur : buffer 128 closes, découpe 6 fenêtres, appelle 9 modèles d'évidence (ADF, KPSS, Hurst, HalfLife, VarianceRatio, CUSUM, Volatility, DFA, Bai-Perron) |
| **État interne** | `_priceBuffer[128]`, `_priceBufferHead`, `_priceBufferCount` — **reset sur `context.Clock.IsFirstBar`** |
| Dépendances ATAS | **Aucune** |
| **Réutilisable** | **YES** |
| Raison | Consomme `Core.MarketContext` (pur). Contrat : appel séquentiel croissant, `IsFirstBar=true` sur la 1re barre. |

Sous-composants avec état : `HurstEvidence` (`_buf[30]`, reset sur `IsFirstBar` — ligne 19) et `VolatilityEvidence` (idem). Tous les autres (`AdfEvidence`, `KpssEvidence`, `HalfLifeEvidence`, `VarianceRatioEvidence`, `CusumEvidence`, `DfaEvidence`, `BaiPerronEvidence`) sont **stateless** et consomment un `EvidenceContext` (record pur : `Series`, `SampleSize`, `MinimumSampleSize`, `WindowSize`, `Timestamp`, `Symbol`, `TimeFrame`). **YES** pour tous.

#### (7) Decision Engine

| Champ | Valeur |
|---|---|
| Fichiers | `Engine/Decision/Core/DecisionEngine.cs`, `Engine/Decision/Arbitration/DecisionArbitrator.cs`, `Engine/Decision/Rules/*.cs` (5 règles) |
| Méthode | `Evaluate(DecisionContext) → DecisionResult` |
| Entrée | `DecisionContext { FusionResult, EvidenceSet }` — **aucune donnée de marché directe** (commentaire du fichier) |
| Sortie | `DecisionResult { Winner, WinnerScore, Candidates, AmbiguityScore, TriggeredRules, RejectedRules, ... }` |
| Dépendances ATAS | **Aucune** |
| **Réutilisable** | **YES** — et **PROTÉGÉ** par le brief |

#### (8) Entry

| Champ | Valeur |
|---|---|
| Fichiers | `Engine/Entry/EntryEngine.cs`, `EntryAssessmentBuilder.cs`, `EntryContext.cs`, `EntryCandidate.cs` |
| Méthode | `Process(EntryContext) → EntryCandidate` |
| Entrée | `EntryContext(ScientificAssessment)` |
| Dépendances ATAS | **Aucune** |
| **Réutilisable** | **YES** |
| Réserve | `EntryEngine.cs:21` horodate avec `DateTime.UtcNow` (cf. §17, RISK-04) |

#### (9) EntryTrigger

| Champ | Valeur |
|---|---|
| Fichiers | `Engine/EntryTrigger/EntryTriggerEngine.cs`, `EntryTriggerBuilder.cs`, `EntryBusinessContext.cs`, `EntryTriggerContext.cs` |
| Méthode | `Process(EntryTriggerContext) → EntryTriggerResult(Candidate, Timing)` |
| Rôle | Détermine `DirectionCandidate` (BUY/SELL/WATCH/NO_ACTION) et `EntryTriggerReason` ; porte `CurrentPrice` |
| Paramètre clé | `AmbiguityGateThreshold = 0.95` (`EntryTriggerBuilder.cs:19`) — **PROTÉGÉ, non modifié** |
| Dépendances ATAS | **Aucune** |
| **Réutilisable** | **YES** — et **PROTÉGÉ** |
| Réserve | `EntryTriggerBuilder.cs:102,110` horodatent avec `DateTime.UtcNow` (cf. §17, RISK-04) |

#### (10) TradePlan

| Champ | Valeur |
|---|---|
| Fichiers | `Engine/TradePlan/TradePlanEngine.cs`, `TradePlanBuilder.cs`, `TradePlan.cs`, `TradePlanContext.cs` |
| Méthode | `Process(TradePlanContext) → TradePlan` |
| Entrée | `TradePlanContext(EntryTriggerCandidate, Core.InstrumentInfo, TradeRiskParameters? = null)` |
| Sortie | `TradePlan { IsValid, Status, Direction, EntryPrice, StopLoss, TakeProfit, RiskPerUnit, RiskAmount, PositionSize, RiskRewardRatio, InvalidationReason, Diagnostics, Timestamp }` |
| Dépendances ATAS | **Aucune** |
| **Réutilisable** | **YES** — et **PROTÉGÉ** |
| **Point d'injection backtest** | `TradeRiskParameters(StopLoss, RiskPerTrade)` — **déjà optionnel, déjà en place, aucune modification requise** |

#### (11) RiskEngine

| Champ | Valeur |
|---|---|
| Fichiers | `Engine/Risk/RiskEngine.cs` + `AccountState`, `PortfolioState`, `RiskPolicy`, `InstrumentRiskSpecification`, `RiskEngineRequest`, `RiskAssessment`, `RiskRejectionReason`, `TradeDirection`, `IStopLossStrategy`, `RiskEngineRequestFactory`, `RiskPolicyFactory` |
| Méthode | `Evaluate(RiskEngineRequest) → RiskAssessment` — 11 phases |
| Dépendances ATAS | **Aucune** — doc de classe : « *Pure, deterministic, no ATAS/clock/network dependency and no global mutable state* » |
| **Réutilisable** | **YES (100 %)** — et **PROTÉGÉ** |
| Vérification | Aucun branchement sur `Symbol` dans `RiskEngine.cs` (vérifié ligne à ligne) ; `Tests/Risk/RiskEngineIntegrationTests.Test05_EsVsMesUseDistinctSpecificationsNoHardcoding` couvre déjà ce point. |

#### (12) Dashboard

| Champ | Valeur |
|---|---|
| Fichiers | `Visualization/Dashboards/*` (7), `Visualization/Rendering/*` (4), `Visualization/Widgets/*` (4), `Visualization/State/*` (3) |
| Dépendances ATAS | `OFT.Rendering.Context` / `OFT.Rendering.Tools` dans **13 fichiers sur 18** |
| **Réutilisable** | **NO** (rendu) / **YES** (2 exceptions ci-dessous) |
| Exceptions pures | `Visualization/Rendering/RiskDashboardPresenter.cs` (`RiskDashboardView`, texte seul, « *no RenderContext, no ATAS dependency* ») et `Visualization/State/DashboardContext.cs` (agrégat de références) |

#### (13) ScientificDataset

| Fichier | Classe | Dépendance ATAS | Réutilisable |
|---|---|---|---|
| `Core/Calibration/ScientificDatasetCollector.cs` | `ScientificDatasetCollector` | **aucune** (`using` : System.*) | **YES** |
| `Core/Calibration/ScientificDatasetRecord.cs` | `ScientificDatasetRecord` | `using IQIAIndicator.Infrastructure.ATAS` (types `ATASAccountDiagnostic`/`ATASInstrumentDiagnostic` en **paramètres optionnels nullables**) | **PARTIAL** |
| `Core/Calibration/ScientificDatasetSession.cs` | `ScientificDatasetSession`, `...SessionWriter` | aucune | **YES** |
| `Core/Calibration/ScientificDatasetExporter.cs` | `ScientificDatasetExporter` | aucune | **YES** |
| `Core/Calibration/DatasetLifecycleLog.cs` / `...SnapshotWriter.cs` | — | aucune | **YES** |
| `Core/Calibration/ScientificDatasetStatistics.cs`, `ExportResult.cs` | — | aucune | **YES** |

`ScientificDatasetRecord.From(...)` accepte `null` pour tous les paramètres ATAS ; le backtest les passerait à `null` et obtiendrait `"NOT AVAILABLE"` — comportement déjà prévu et testé.

#### (14) Adaptateurs ATAS

| Fichier | Rôle | Types ATAS touchés | Réutilisable |
|---|---|---|---|
| `Infrastructure/ATAS/ATASAccountStateAdapter.cs` | `Portfolio` + `ITradingStatisticsProvider` → `AccountState` | `Portfolio`, `ITradingStatistics` | **NO** |
| `Infrastructure/ATAS/ATASInstrumentAdapter.cs` | `Security` → `InstrumentRiskSpecification` | `Security` | **NO** |
| `Infrastructure/ATAS/ATASRuntimeDiagnostics.cs` | Capture diagnostique brute | `Portfolio`, `Position`, `ITradingStatisticsProvider` | **NO** (les 2 *records* de sortie sont purs) |
| `Infrastructure/ATAS/ATASRenderer.cs`, `ATASDrawingFactory.cs`, `ATASCoordinateMapper.cs`, `ATASAnnotationMapper.cs` | Rendu chart | `OFT.Rendering`, `IChartContainer` | **NO** |
| `Infrastructure/ATAS/ATASEquityReplayDetector.cs` | Décide Replay vs Realtime | **aucun** (bool/DateTime/TimeSpan) | **YES** (sans objet en backtest) |
| `Infrastructure/ATAS/AtasDataContextResolver.cs` | bool → `AtasDataContext` | aucun | **YES** (sans objet) |
| `Infrastructure/ATAS/ATASInstrumentQuantityStepResolver.cs` | Investigué, **non câblé** (Lot 12.6) | `Security` | **NO** |

**Fait notable** : le rôle d'`ATASInstrumentAdapter` (produire une `InstrumentRiskSpecification`) est exactement ce que le backtest devra faire à partir d'une configuration — le *pattern* est donc déjà validé, seule la source change.

#### (15) Endroits où ATAS est couplé directement au pipeline

Liste **exhaustive** (recherche `using ATAS|using OFT|ATAS\.|OFT\.` sur tout le dépôt, hors `obj/`, `bin/`) :

| # | Emplacement | Nature du couplage | Sévérité pour le backtest |
|---|---|---|---|
| C-1 | `Core/MarketContextBuilder.cs:2,14,32,90` | `Func<int, IndicatorCandle>` — **entrée des données** | **CRITICAL** |
| C-2 | `IQIAIndicator.cs:1234` (`CreateScientificMarketContext`) | `GetCandle(index).Close` × 500 | **CRITICAL** |
| C-3 | `IQIAIndicator.cs:657-859` (Risk stage) | `TradingManager`, `TradingStatisticsProvider`, `Portfolio`, `Security` | **HIGH** (compte + instrument) |
| C-4 | `IQIAIndicator.cs:1046-1159` (`OnRender`) | `RenderContext`, `ChartArea`, `IChartContainer` | **LOW** (optionnel) |
| C-5 | `IQIAIndicator.cs:407-408,483-484,1251-1256` | `InstrumentInfo?.Instrument/TickSize`, `ChartInfo?.TimeFrame` | **MEDIUM** (métadonnées) |
| C-6 | `Core/Calibration/ScientificDatasetRecord.cs:13` | `using Infrastructure.ATAS` (types purs, paramètres nullables) | **LOW** |
| C-7 | `Visualization/**` (13 fichiers) | `OFT.Rendering` | **LOW** (optionnel) |
| C-8 | `Core/VolumeInfo.cs` | **commentaire uniquement** (« lus directement depuis l'API ATAS ») | **NONE** |
| C-9 | `Core/Calibration/DatasetLifecycleLog.cs` | **commentaire uniquement** | **NONE** |
| C-10 | `IQIAIndicator.csproj:18-34` | 5 `Reference HintPath` vers `C:\Program Files (x86)\ATAS Platform\` | **HIGH** (compilation) |

**Aucun autre fichier du dépôt ne référence ATAS.** Sur 203 fichiers `.cs` de production, **16 seulement** touchent ATAS, dont 10 dans `Infrastructure/ATAS` et `Visualization`.

#### (16) Dépendances empêchant une exécution hors ATAS

| # | Dépendance | Empêche quoi exactement | Contournable sans modifier le code existant ? |
|---|---|---|---|
| D-1 | `IndicatorCandle` dans `MarketContextBuilder` | Produire un `Core.MarketContext` **via ce builder** | **OUI** — en construisant `Core.MarketContext` directement (tous les types sont publics) |
| D-2 | `GetCandle` dans `CreateScientificMarketContext` | Produire le contexte scientifique | **OUI** — méthode `private` de la classe hôte, à réimplémenter côté backtest (18 lignes) |
| D-3 | Assembly unique référençant ATAS par `HintPath` | **Compiler sur une machine sans ATAS installé** | **NON** — nécessite un split de projet |
| D-4 | `Indicator.TradingManager` / `TradingStatisticsProvider` | Obtenir `AccountState` et `InstrumentRiskSpecification` | **OUI** — les deux types sont publics et constructibles |
| D-5 | `OFT.Rendering` | Afficher les dashboards | **OUI** — dashboards non requis en backtest |
| D-6 | `TradeRiskParameters == null` en production | Obtenir un `StopLoss`, donc un `RiskAssessment` ACCEPTED | **OUI** — paramètre optionnel déjà présent |

---

## 3. CURRENT DATA FLOW

### 3.1 Flux réel, par type, avec sources exactes

```
ATAS IndicatorCandle
  { Open, High, Low, Close, Volume, Bid, Ask, Delta, Time }
        │  MarketContextBuilder.Build          (MarketContextBuilder.cs:88-136)
        ▼
Core.MarketContext
  ├─ BarIndex        = bar
  ├─ TimeFrame       = ChartInfo.TimeFrame          (snapshot au bar 0)
  ├─ Price           = PriceInfo(O,H,L,C, (H+L)/2, (H+L+C)/3)
  ├─ Volume          = VolumeInfo(Volume, Bid, Ask, Delta)
  ├─ Instrument      = InstrumentInfo(Symbol, TickSize, TickValue, PointValue, Decimals)
  ├─ Clock           = MarketClock(Time, Date, DayOfWeek,
  │                       Session = SessionInfo("", MinValue, MaxValue),   ← JAMAIS PEUPLÉ
  │                       ElapsedMinutes, IsFirstBar, IsLastBar)
  └─ Execution       = ExecutionContext(CurrentBar, LastCalculatedBar,
                           IsRealtime, IsHistorical, IsReplay)
        │
        ├──────────────► MarketContextValidator.Validate  → ValidationResult
        │                   (Open>0, Close>0, High>=Low, TickSize>0, CurrentTime!=default)
        │
        ├──────────────► RegimeEngine.Collect
        │                   buffer[128] ← Close
        │                   → EvidenceContext ×6 (60/128/30/30/30/128, minSample 30/80/20/20/20/64)
        │                   → EvidenceSet { Adf, Kpss, Hurst, HalfLife, VarianceRatio,
        │                                   Cusum, Volatility, BaiPerron, Dfa }
        │                          │
        │                          ├─► EvidenceFusionEngine.Fuse  → FusionResult (5 dimensions)
        │                          │      └─► FusionStateManager.Update → FusionSnapshot
        │                          │              (lissage, _previousStableResult, _updateCount)
        │                          │
        │                          └─► DecisionEngine.Evaluate(FusionSnapshot.StableResult, EvidenceSet)
        │                                 → DecisionResult { Winner, AmbiguityScore, ... }
        │                                        └─► MethodologyEngine.Evaluate
        │                                               → MethodologySelection
        │
        └──────────────► CreateScientificMarketContext(context, bar)   (IQIAIndicator.cs:1226)
                            History = 500 derniers Close (via GetCandle)
                            → ScientificModels.Abstractions.MarketContext
                              { Timestamp, CurrentBar, History, CurrentPrice,
                                Symbol, TimeFrame, Exchange=null, Session }
                                   │
                                   ▼  SignalEngine.Process(scientificContext, methodologySelection, trace)
                            ScientificModelRegistry.Resolve(methodology)
                              MeanReversionMethodology → [Kalman, OU, DynamicZScore, Volatility, SPRT]
                              tout autre régime        → [] (aucun modèle)
                                   │
                                   ▼
                            ScientificFusionEngine.Assess → ScientificAssessment
                                   ▼
                            EntryEngine.Process           → EntryCandidate
                                   ▼
                            EntryBusinessContext(CurrentPrice, EstimatedEquilibrium,
                                DistanceToEquilibrium, DynamicZScore, ...)
                                   ▼
                            EntryTriggerEngine.Process    → EntryTriggerCandidate
                                   │                          { Assessment.Direction, CurrentPrice, ... }
                                   ▼
                            TradePlanEngine.Process(TradePlanContext(candidate, instrumentInfo, null))
                                   → TradePlan { Status = SIGNAL_ONLY, StopLoss = null }
                                   ▼
                            RiskEngineRequestFactory.FromTradePlan(plan, account, policy, instrument)
                                   → RiskEngineRequest?  (null si Direction ∉ {BUY,SELL} ou EntryPrice null)
                                   ▼
                            RiskEngine.Evaluate → RiskAssessment { REJECTED, [INVALID_STOP_LOSS, ...] }
                                   ▼
                            ScientificDatasetRecord.From(...) → ScientificDatasetCollector.Add
```

### 3.2 Trois observations structurantes pour le backtest

**(a) Le pipeline a deux représentations du marché, pas une.**
`Core.MarketContext` (barre courante complète, OHLCV) alimente Regime/Decision.
`Abstractions.MarketContext` (série de 500 closes) alimente les modèles scientifiques.
Les deux sont purs. **Le Backtest Engine devra produire les deux**, exactement comme `IQIAIndicator.OnCalculate` le fait.

**(b) `MarketClock.Session` n'est jamais peuplé.**
`MarketContextBuilder.cs:123` écrit en dur `new SessionInfo(string.Empty, DateTime.MinValue, DateTime.MaxValue)`. Aucune notion de session marché n'existe donc aujourd'hui, ni en ATAS ni ailleurs. Conséquence directe pour le LOT 14 : **le backtest ne peut pas filtrer par session sans créer cette capacité**, et s'il la crée, il divergera d'ATAS (cf. §17, RISK-08).

**(c) Le seul consommateur du volume est le dataset.**
Aucun modèle scientifique, aucune règle de décision, aucune évidence ne lit `VolumeInfo`. Les 9 évidences et les 5 modèles travaillent **exclusivement sur les closes**. Cela simplifie considérablement le contrat de données historiques (§7) : Yahoo suffit pour le calcul, le volume n'étant requis que pour la validation qualité et le dataset.

---

## 4. ATAS DEPENDENCIES

### 4.1 Classement des 10 couplages (repris de §2.2-15)

| Niveau | Couplages | Conséquence |
|---|---|---|
| **Structurel — entrée de données** | C-1, C-2 | Le pipeline ne peut pas démarrer sans un fournisseur de barres |
| **Structurel — compilation** | C-10 | L'assembly entier exige les DLL ATAS pour compiler |
| **Fonctionnel — compte/instrument** | C-3, C-5 | Le RiskEngine reçoit ses entrées d'ATAS aujourd'hui |
| **Cosmétique — optionnel** | C-4, C-7 | Rendu/dashboards, non requis en backtest |
| **Documentaire** | C-6, C-8, C-9 | Commentaires ou types purs |

### 4.2 Le cas `IndicatorCandle` — pourquoi c'est un verrou et pas un détail

Le dépôt affirme **trois fois**, dans trois fichiers indépendants, qu'aucun `IndicatorCandle` n'est jamais construit hors d'un hôte ATAS vivant :

- `Tests/Core/MarketContextBuilderSelfHealingTests.cs:19-21` — « *no ATAS.Indicators.IndicatorCandle instance needs to be constructed here; MarketContextBuilder.Build itself (which does invoke getBar) remains a live-ATAS-only path, consistent with this project's existing convention* »
- `Tests/Core/MarketContextBuilderSelfHealingTests.cs:25-26` — le délégué de test **lève une exception** s'il est invoqué
- `QDE-012_Sprint_15.16_Real_Market_Validation_Report.md:41-45` — « *a live, bar-by-bar MarketContext builder fed by ATAS's IndicatorCandle type. It requires ATAS installed locally … There is no historical-data export tool.* »

À l'inverse, `Tests/Infrastructure/ATAS/ATASRuntimeBindingTests.cs:41-51` **construit** `Security` et `Portfolio` (« *confirmed by the Lot 12.1 reflection audit to have public parameterless constructors and settable properties* »). L'asymétrie est donc délibérée et documentée : certains types ATAS sont instanciables en test, **`IndicatorCandle` n'a jamais été prouvé tel**.

**Conclusion de l'audit** : le LOT 14 ne doit **pas** parier sur la construction d'`IndicatorCandle`. Il doit **contourner le builder**, pas l'alimenter. C'est précisément ce que permet la pureté de `Core.MarketContext` (§2.2-3).

### 4.3 Le cas de l'assembly unique (C-10)

```xml
<!-- IQIAIndicator.csproj:14-34 -->
<ATAS_BASE>C:\Program Files (x86)\ATAS Platform</ATAS_BASE>
<Reference Include="OFT.Attributes" />       <Reference Include="ATAS.DataFeedsCore" />
<Reference Include="ATAS.Indicators" />      <Reference Include="OFT.Rendering" />
<Reference Include="Utils.Common" />
```

Conséquence exacte : **`RiskEngine.cs`, pourtant sans une ligne d'ATAS, n'est pas compilable sur une machine sans ATAS installé.** Le même constat vaut pour les 187 autres fichiers purs.

**Nuance importante** : ceci **n'empêche pas** le Backtest Engine de fonctionner. Il empêche seulement de le compiler/exécuter sur un poste ou une CI sans ATAS. Le harness `Tests/Research/StopLossCalibration` tourne déjà dans cette configuration (`IQIAIndicator.Tests.csproj` référence à la fois le projet principal **et** trois DLL ATAS). Ce couplage est donc **HIGH, pas CRITICAL** — et sa résolution est explicitement traitée en §6.4 comme *optionnelle et différable*.

---

## 5. REUSABLE PIPELINE COMPONENTS

### 5.1 Réponse au §3.A du brief — ce qui est DÉJÀ indépendant d'ATAS

| Étage | Composants | Preuve |
|---|---|---|
| Modèle de contexte | `MarketContext`, `PriceInfo`, `VolumeInfo`, `InstrumentInfo`, `MarketClock`, `SessionInfo`, `ExecutionContext`, `MarketCache`, `ValidationResult` | Aucun `using` ATAS ; types publics `required init` |
| Validation | `MarketContextValidator` | idem |
| Régime | `RegimeEngine` + 9 modèles d'évidence + `EvidenceContext`/`EvidenceSet` | idem ; couverts par `Tests/GoldenDatasets/*` sans ATAS |
| Fusion | `EvidenceFusionEngine` (2 implémentations), 5 `IFusionRule`, `FusionStateManager`, `FusionProfileAnalyzer` | couverts par `Tests/Fusion/*` |
| Décision | `DecisionEngine`, `DecisionArbitrator`, 5 `IDecisionRule`, `MarketState` | couverts par `Tests/Decision/*` |
| Méthodologie | `MethodologyEngine`, `MethodologyRegistry`, `QuantitativeMethodology` | couverts par `Tests/Methodology/*` |
| Modèles scientifiques | `ScientificModelRegistry`, `KalmanFilterModel`, `OrnsteinUhlenbeckModel`, `DynamicZScoreModel`, `VolatilityModel`, `SPRTModel` (+ 2 stubs non câblés) | **tournent déjà hors ATAS** dans `BarMetricsComputer` |
| Fusion scientifique | `ScientificFusionEngine`, `ScientificAssessmentBuilder` | — |
| Signal | `SignalEngine` + `EntryEngine` + `EntryTriggerEngine` + `VisualizationEngine` + `ChartAnnotationEngine` + `OpportunityPresentationEngine` | couverts par `Tests/EntryTrigger/*`, `XunitWrappers/SignalEngineCoverageIntegrationXunitTests` |
| TradePlan | `TradePlanEngine`, `TradePlanBuilder`, `TradePlan`, `TradePlanContext` | `Tests/XunitWrappers/TradePlanBuilderXunitTests` |
| Risque | **tout `Engine/Risk/`** | `Tests/Risk/*` — 3 suites, zéro ATAS |
| Dataset | `ScientificDatasetCollector`, `...Session`, `...Exporter`, `...Statistics`, `DatasetLifecycleLog`, `...SnapshotWriter`, `ExportResult` | `Tests/Calibration/*` |
| Observabilité | `PipelineTraceCollector`, `PipelineTraceContext`, `IPipelineTraceCollector` | — |
| Présentation pure | `RiskDashboardPresenter` (`RiskDashboardView`) | `Tests/Visualization/RiskDashboardPresenterTests` |

**Total : 187 fichiers de production sur 203 sont ATAS-free.**

### 5.2 Réponse au §3.B — ce qui dépend encore d'ATAS

| Composant | Nature | Remplaçable par |
|---|---|---|
| `IQIAIndicator` (hôte) | Orchestrateur + callback | `BacktestEngine` (hôte alternatif) |
| `MarketContextBuilder.Build` | Traduction candle → contexte | Construction directe de `Core.MarketContext` |
| `CreateScientificMarketContext` | Fenêtre 500 closes | Fonction équivalente côté backtest |
| `ATASAccountStateAdapter` | Compte | `SimulatedAccount` (§9) |
| `ATASInstrumentAdapter` | Instrument | `InstrumentSpecificationCatalog` (§10) |
| `ATASRenderer` + `Visualization/**` | Rendu | *(non requis)* |
| `ATASEquityReplayDetector`, `AtasDataContextResolver`, `ATASRuntimeDiagnostics` | Live/Replay & diagnostics | *(sans objet en backtest)* |

### 5.3 Précédent décisif : le harness de recherche est déjà un demi-backtest

`Tests/Research/StopLossCalibration/` **exécute déjà le pipeline scientifique réel hors ATAS** :

| Fichier | Rôle | Équivalent backtest |
|---|---|---|
| `BarMetricsComputer.cs:41` | `Compute(IReadOnlyList<decimal> series, int barIndex)` — construit un `Abstractions.MarketContext`, résout le registre, exécute les 5 modèles | **Étage « Features/Signal » du backtest** |
| `CalibrationEntryBuilder.cs:24` | Boucle `for (t = 30; t <= n-2; t++)`, filtre l'éligibilité, appelle l'outcome | **Boucle de barres du backtest** |
| `OutcomeSimulator.cs:16` | Marche le chemin de prix futur, calcule MAE/MFE/EquilibriumBar | **Mesure d'issue / MFE / MAE** |
| `StopLossEvaluator.cs:29` | `(StopHit, StopHitBar, EntryFate)` depuis une `OutcomeMeasurement` | **Simulation de sortie sur stop** |
| `CampaignRunner.cs:26` | Balaye datasets × splits × échelles × grilles k | **Runner multi-scénarios** |
| `CampaignDatasetCatalog.cs:16-18` | `TrainSeed=42 / ValidationSeed=43 / TestSeed=44` | **Séparation TRAIN/VALIDATION/TEST** |
| `Hybrid/HybridTrainValidationTestAnalysis.cs` | Restitue TRAIN/VALIDATION/TEST **côte à côte, jamais moyennés** | **Reporting OOS** |
| `RealMarket/RealMarketBar.cs` + `RealMarketOhlcvCsvReader` | Modèle de barre + parseur CSV strict | **Contrat de données historiques** |
| `RealMarket/RealMarketQualityAnalyzer.cs` | Timestamps, doublons, intervalles, sessions | **Validation d'ingestion** |

**Ce que ce harness ne fait pas** (et que le Backtest Engine doit faire) : il court-circuite Regime → Fusion → Decision (il fixe `Winner = MeanReverting`, `Confidence = 1.0`, `AmbiguityScore = 0.0` — `BarMetricsComputer.cs:61`), il ignore `EntryTriggerBuilder`, `TradePlanBuilder` et `RiskEngine`, et il n'a ni compte, ni position, ni PnL, ni coûts.

**Conséquence pour le LOT 14** : le Backtest Engine n'est **pas** une réécriture de ce harness ; c'est la **version complète du même pattern**, branchée sur le pipeline entier au lieu des seuls modèles. La discipline anti-look-ahead du harness (§13) est directement transposable.

---

## 6. MISSING ABSTRACTIONS

### 6.1 Réponse au §3.C — ce qui manque vraiment

| # | Abstraction | Existe ? | Justification de sa nécessité |
|---|---|---|---|
| A-1 | **Barre historique neutre** (`HistoricalBar`) | Non en production ; `RealMarketBar` en Tests | Sans elle, aucune source ne peut alimenter le pipeline |
| A-2 | **Port de source historique** (`IHistoricalBarSource`) | Non | Sans elle, Yahoo/CSV/Databento ne sont pas interchangeables (exigence explicite §10 du brief) |
| A-3 | **Fabrique de `Core.MarketContext`** partagée | Non | Sans elle, la logique `Median`/`TypicalPrice`/`ElapsedMinutes`/`IsFirstBar` est dupliquée → divergence ATAS/Backtest garantie |
| A-4 | **Compte simulé** (`ISimulatedAccount`) | Non | `AccountState` est immuable et n'a aucun producteur non-ATAS |
| A-5 | **Modèle d'exécution** (`IExecutionModel`) | Non | Aucun fill, aucune position, aucun ordre n'existe nulle part |
| A-6 | **Modèle de coûts** (`ICostModel`) | Non | Slippage/commission/spread absents du dépôt entier |
| A-7 | **Catalogue d'instruments data-driven** | Non | ES/MES aujourd'hui : ATAS en live, littéraux dans les tests |
| A-8 | **Fenêtres temporelles** (`BacktestWindow` par dates) | Non — les splits actuels sont par *seed*, pas par date | Indispensable pour TRAIN/VALIDATION/OOS/HOLDOUT sur données réelles |

### 6.2 Ce qu'il ne faut **PAS** créer (réutilisation possible)

Le brief impose de ne rien proposer qui duplique une abstraction existante. Sont donc **explicitement exclus** :

| Tentation | Pourquoi elle est inutile |
|---|---|
| Une interface `IMarketDataProvider` en amont du pipeline | `Core.MarketContext` **est déjà** le contrat neutre. Ajouter une couche au-dessus n'apporte rien. |
| Un nouveau modèle de contexte scientifique | `Abstractions.MarketContext` est déjà un `record` pur à 8 champs. |
| Une nouvelle `AccountState` mutable | `AccountState` est un `record` : `with` produit un nouvel état par barre. C'est exactement ce que veut un backtest déterministe. |
| Un `IStopLossStrategy` supplémentaire | L'interface **existe** (`Engine/Risk/IStopLossStrategy.cs`) avec `ProvidedStopLossStrategy`. |
| Un nouveau format de dataset | `ScientificDatasetRecord`/`Collector`/`Exporter` sont purs et déjà instrumentés Entry/EntryTrigger/TradePlan/Risk (Lots 9/11/12.5). |
| Une nouvelle `RiskEngineRequest` | Elle est déjà « *deliberately decoupled from EntryTriggerCandidate/TradePlanContext … no dependency on the signal pipeline or on ATAS* ». |
| Un `IBarClock` | `MarketClock` porte déjà `CurrentTime`, `IsFirstBar`, `IsLastBar`. |

### 6.3 Réponse au §3.D — la plus petite modification architecturale nécessaire

**Réponse : UNE SEULE modification du code existant, et elle est optionnelle.**

Le Backtest Engine peut être écrit **en pur ajout**, sans toucher un seul fichier existant, parce que `Core.MarketContext` et ses 6 sous-types sont publics et librement constructibles. La séquence est :

```
HistoricalBar (nouveau)
   → construction directe d'un Core.MarketContext (aucun code existant touché)
   → MarketContextValidator.Validate            (existant, réutilisé)
   → RegimeEngine.Collect                       (existant, réutilisé)
   → ... tout le pipeline existant ...
```

**La modification recommandée — et la seule** : extraire de `MarketContextBuilder.Build` (lignes 108-136) une fonction pure partagée :

```
// Emplacement proposé : Core/MarketContextFactory.cs (NOUVEAU fichier)
public static MarketContext Create(
    int barIndex, int currentBar, string timeFrame,
    decimal open, decimal high, decimal low, decimal close,
    decimal volume, decimal bid, decimal ask, decimal delta,
    InstrumentInfo instrument,
    DateTime barTime, DateTime firstBarTime,
    bool isRealtime, bool isHistorical, bool isReplay)
```

`MarketContextBuilder.Build` deviendrait un appel à cette fabrique après lecture de l'`IndicatorCandle`.

| Aspect | Évaluation |
|---|---|
| Ampleur | ~30 lignes déplacées, zéro changement de comportement |
| Fichier touché | `Core/MarketContextBuilder.cs` — **non protégé** par le brief |
| Bénéfice | Supprime définitivement RISK-07 (divergence Backtest/ATAS sur `Median`, `TypicalPrice`, `ElapsedMinutes`, `IsFirstBar`) |
| Alternative | Dupliquer ces 30 lignes côté backtest — fonctionne, mais crée exactement le « deuxième IQIA » que le brief interdit |
| **Statut** | **RECOMMANDÉE, non bloquante** — le LOT 14 peut démarrer sans elle et l'appliquer ensuite |

### 6.4 Modification différable : split d'assembly

Pour compiler sans ATAS installé, il faudrait scinder :

```
IQIAIndicator.Core.csproj      → Core/*, Engine/*        (zéro référence ATAS)
IQIAIndicator.csproj           → IQIAIndicator.cs, Infrastructure/ATAS/*, Visualization/*
IQIAIndicator.Backtest.csproj  → Backtest/*              (référence Core uniquement)
```

Coût : déplacement de `ScientificDatasetRecord.cs`'s `using Infrastructure.ATAS` (les 2 *records* diagnostiques devraient migrer vers `Core`), révision des `namespace`, adaptation des tests.

**Verdict de l'audit : à NE PAS faire au LOT 14.** Ce n'est pas nécessaire pour que le backtest fonctionne, cela touche beaucoup de fichiers, et cela ferait porter au LOT 14 un risque de régression sans rapport avec son objectif. À planifier séparément (LOT 16+) une fois le backtest opérationnel et sa valeur démontrée.

---

## 7. HISTORICAL DATA CONTRACT

### 7.1 Réponse au §4 du brief — un modèle équivalent existe-t-il ?

| Candidat | Emplacement | Verdict |
|---|---|---|
| `ATAS.Indicators.IndicatorCandle` | DLL tierce | **Rejeté** — type ATAS, non instanciable hors hôte (§4.2) |
| `Core.PriceInfo` + `VolumeInfo` | production | **Insuffisant** — ne porte ni `Timestamp`, ni `Symbol`, ni `TimeFrame` |
| `Core.MarketContext` | production | **Ce n'est pas une barre** — c'est le contexte *dérivé* (BarIndex, Median, ElapsedMinutes, Execution) |
| `ScientificDatasetRecord` | production | **Rejeté** — c'est un enregistrement de *sortie* (SessionId, Metrics, Categories), pas une barre d'entrée |
| **`RealMarketBar`** | `Tests/Research/StopLossCalibration/RealMarket/RealMarketBar.cs` | **Le plus proche** — mais couplé au format d'export du collector |

`RealMarketBar` :
```csharp
public sealed record RealMarketBar(
    Guid SessionId, DateTime Timestamp, string Symbol, string TimeFrame,
    decimal Open, decimal High, decimal Low, decimal Close,
    decimal Volume, int CurrentBar);
```

Ses deux défauts pour un usage général : `SessionId` (Guid) et `CurrentBar` (int) sont des artefacts du CSV produit par `ScientificDatasetCollector.ToOhlcvCsv()`. Une source Yahoo n'a ni l'un ni l'autre. Il est en outre dans l'assembly de **tests**.

### 7.2 Conclusion : un nouveau modèle est nécessaire

**Spécification proposée (À NE PAS IMPLÉMENTER DANS CE LOT)** :

```
Nom          : HistoricalBar
Emplacement  : IQIAIndicator/Core/MarketData/HistoricalBar.cs   (nouveau dossier)
Forme        : public readonly record struct   (cohérent avec PriceInfo/VolumeInfo/InstrumentInfo)
Mutabilité   : immuable
```

| Champ | Type | Obligatoire | Source Yahoo | Justification |
|---|---|---|---|---|
| `Timestamp` | `DateTime` | **OUI** | `Date` | Alimente `MarketClock.CurrentTime` ; `MarketContextValidator` rejette `default` |
| `Open` | `decimal` | **OUI** | `Open` | `MarketContextValidator` exige `> 0` |
| `High` | `decimal` | **OUI** | `High` | Exigé `>= Low` |
| `Low` | `decimal` | **OUI** | `Low` | idem |
| `Close` | `decimal` | **OUI** | `Close` | **Seule valeur consommée par tous les modèles** ; exigée `> 0` |
| `Volume` | `decimal` | **OUI** | `Volume` | Exigé `>= 0` ; consommé uniquement par le dataset |
| `BidVolume` | `decimal?` | non | *(absent)* | `VolumeInfo.BidVolume` ; `null` → 0 |
| `AskVolume` | `decimal?` | non | *(absent)* | `VolumeInfo.AskVolume` ; `null` → 0 |
| `Delta` | `decimal?` | non | *(absent)* | `VolumeInfo.Delta` ; `null` → 0 |
| `OpenInterest` | `decimal?` | non | *(absent)* | Aucun consommateur aujourd'hui — champ de réserve, jamais fabriqué |

**Symbol et TimeFrame n'appartiennent PAS à la barre.** Ce sont des propriétés de la *série*, constantes sur toutes ses barres. Les répéter par barre (comme le fait `RealMarketBar`) invite à des séries incohérentes. Spécification du conteneur :

```
Nom          : HistoricalSeries
Emplacement  : IQIAIndicator/Core/MarketData/HistoricalSeries.cs
Champs       : Symbol (string), TimeFrame (string), TimeZone (string),
               Provider (string), Bars (IReadOnlyList<HistoricalBar>)
Invariants   : Bars strictement croissantes par Timestamp, Count > 0,
               Symbol/TimeFrame/TimeZone non vides
```

**`TimeZone` est obligatoire et sans valeur par défaut.** Motif : le rapport 15.16 §3 a établi que « *timezone/session handling does not exist yet* » dans tout le codebase — `MarketContextBuilder` passe `IndicatorCandle.Time` sans conversion, et `BarMetricsComputer.cs:66` horodate à `DateTime.UtcNow`. Toute valeur par défaut ici reproduirait silencieusement cette ambiguïté sur des données réelles où elle a des conséquences.

### 7.3 Correspondance `HistoricalBar` → pipeline existant

| Cible | Source | Règle |
|---|---|---|
| `PriceInfo.Open/High/Low/Close` | direct | copie |
| `PriceInfo.Median` | dérivé | `(High + Low) / 2m` — **identique à `MarketContextBuilder.cs:114`** |
| `PriceInfo.TypicalPrice` | dérivé | `(High + Low + Close) / 3m` — **identique à la ligne 115** |
| `VolumeInfo.*` | direct / `?? 0m` | jamais fabriqué |
| `InstrumentInfo` | **configuration du scénario**, pas la barre | cf. §10 |
| `MarketClock.CurrentTime/CurrentDate/DayOfWeek` | `Timestamp` | copie + dérivés |
| `MarketClock.Session` | `SessionInfo(string.Empty, MinValue, MaxValue)` | **reproduire exactement le comportement ATAS actuel** — ne pas inventer de session (cf. §17, RISK-08) |
| `MarketClock.ElapsedMinutes` | `(Timestamp - firstBarTime).TotalMinutes` | identique à la ligne 124 |
| `MarketClock.IsFirstBar` | `index == 0` | pilote le reset d'état de `RegimeEngine`/`HurstEvidence`/`VolatilityEvidence` |
| `MarketClock.IsLastBar` | `index == Count - 1` | |
| `ExecutionContext.CurrentBar` | `index + 1` | conserver la convention ATAS (`CurrentBar` = nombre de barres) |
| `ExecutionContext.IsRealtime` | `false` | |
| `ExecutionContext.IsHistorical` | `true` | |
| `ExecutionContext.IsReplay` | `false` | **jamais `true`** : un backtest n'est pas un Replay ATAS ; l'heuristique n'a aucun sens ici |
| `MarketContext.BarIndex` | `index` | |
| `MarketContext.TimeFrame` | `HistoricalSeries.TimeFrame` | |

---

## 8. BACKTEST ENGINE ARCHITECTURE

*(Conception uniquement — aucun code à écrire dans ce lot.)*

### 8.1 Responsabilités

| Le BacktestEngine EST responsable de | Le BacktestEngine N'EST PAS responsable de |
|---|---|
| Itérer les barres dans l'ordre chronologique | Calculer un régime, une décision, une entrée, un plan ou un risque |
| Construire `Core.MarketContext` par barre | Décider d'un stop-loss (il le reçoit d'une stratégie injectée) |
| Construire `Abstractions.MarketContext` par barre | Modifier `RiskAssessment` |
| Instancier et séquencer les moteurs **existants** | Réimplémenter une métrique scientifique |
| Tenir le compte simulé et les positions | Rendre un dashboard |
| Simuler fills, sorties, coûts | Choisir une valeur de capital, de coût ou de policy |
| Produire trades, PnL, MFE, MAE, statistiques, dataset | Écrire dans un dataset existant |

### 8.2 Interfaces nécessaires

```
IHistoricalBarSource
    HistoricalSeries Load(string symbol, string timeFrame, DateTime from, DateTime to)

IExecutionModel
    FillResult? TryFillEntry(TradePlan plan, RiskAssessment risk, HistoricalBar bar)
    ExitResult? TryExit(OpenTrade trade, HistoricalBar bar)

ICostModel
    decimal EntryCost(int quantity, InstrumentRiskSpecification spec)
    decimal ExitCost(int quantity, InstrumentRiskSpecification spec)
    decimal EntrySlippageTicks / ExitSlippageTicks
    decimal SpreadTicks

ISimulatedAccount
    AccountState Current { get; }
    void ApplyFill(...) / ApplyExit(...) / MarkToMarket(HistoricalBar bar)

IInstrumentSpecificationSource
    InstrumentRiskSpecification Resolve(string symbol)

IStopLossStrategy                                   ← EXISTANT, Engine/Risk/IStopLossStrategy.cs
```

Cinq interfaces nouvelles, une réutilisée. **Aucune ne touche un composant protégé.**

### 8.3 Entrées / Sorties

**Entrées** — un `BacktestScenario` immuable :

| Champ | Type | Origine |
|---|---|---|
| `Series` | `HistoricalSeries` | `IHistoricalBarSource` |
| `Symbol`, `TimeFrame` | `string` | scénario |
| `Window` | `BacktestWindow(Name, From, To)` | scénario (§14) |
| `InitialCapital` | `decimal` | **configuration du scénario** (exigence explicite §6 du brief) |
| `InstrumentSpecification` | `InstrumentRiskSpecification` | `IInstrumentSpecificationSource` |
| `RiskPolicy` | `RiskPolicy` | scénario — **jamais de valeur par défaut inventée** |
| `CostModel` | `ICostModel` | scénario |
| `ExecutionModel` | `IExecutionModel` | scénario |
| `StopLossStrategy` | `IStopLossStrategy` | scénario |
| `MaxHoldingBars` | `int?` | scénario (sortie par horizon, cf. `CampaignGrids.Horizon`) |
| `Seed` | `ulong?` | uniquement si un modèle stochastique est introduit un jour |

**Sorties** — un `BacktestResult` :

| Champ | Contenu |
|---|---|
| `Trades` | `IReadOnlyList<BacktestTrade>` : entrée, sortie, direction, quantité, PnL brut/net, coûts, MFE, MAE, durée (barres + temps), motif de sortie |
| `Decisions` | une ligne par barre : `Winner`, `AmbiguityScore`, `Direction`, `TriggerStatus`, `TradePlanStatus` |
| `Rejections` | `RiskRejectionReason[]` par barre, **plus** les rejets amont (`EntryTriggerReason`, `TradePlanStatus.NO_TRADE/SIGNAL_ONLY`) |
| `Equity` | courbe d'equity barre par barre |
| `Statistics` | nb trades, win rate, PnL total/moyen, expectancy, max drawdown, profit factor, MFE/MAE médians, `BarsAvailable` |
| `Dataset` | `IReadOnlyList<ScientificDatasetRecord>` via le collector **existant** |
| `Diagnostics` | barres rejetées par `MarketContextValidator`, barres en warmup, trades tronqués en fin de fenêtre |

### 8.4 Cycle d'une barre — ordre normatif

```
POUR index = 0 .. N-1 :

  1. bar ← Series.Bars[index]
  2. context ← MarketContext depuis bar          (données ≤ index UNIQUEMENT)
  3. validation ← MarketContextValidator.Validate(context)
       si !IsValid → journaliser, PASSER À LA BARRE SUIVANTE
                     (identique au comportement ATAS, IQIAIndicator.cs:508-514)

  ── A. MISE À JOUR DES POSITIONS OUVERTES (avant tout nouveau signal) ──
  4. account.MarkToMarket(bar)
  5. POUR chaque position ouverte :
       a. ExecutionModel.TryExit(trade, bar)      ← ordre d'évaluation SL puis TP, cf. §11.4
       b. mise à jour MFE/MAE depuis High/Low de CETTE barre
       c. si sortie → account.ApplyExit(...) → BacktestTrade fermé

  ── B. GÉNÉRATION DU SIGNAL (données ≤ index) ──
  6. evidence      ← RegimeEngine.Collect(context)
  7. fusionResult  ← EvidenceFusionEngine.Fuse(FusionContext{...})
  8. fusionSnapshot← FusionStateManager.Update(fusionResult, evidence.Timestamp)
  9. decision      ← DecisionEngine.Evaluate(DecisionContext{fusionSnapshot.StableResult, evidence})
 10. methodology   ← MethodologyEngine.Evaluate(decision)
 11. sciContext    ← Abstractions.MarketContext(500 derniers closes ≤ index)
 12. presentation  ← SignalEngine.Process(sciContext, methodology, trace)
 13. entryTrigger  ← signalEngine.LastEntryTriggerCandidate

  ── C. PLAN ET RISQUE ──
 14. si entryTrigger != null :
       riskParams ← TradeRiskParameters(StopLossStrategy.Resolve(...), budget)
       plan       ← TradePlanEngine.Process(TradePlanContext(entryTrigger, instrumentInfo, riskParams))
 15. accountState ← account.Current                     (AccountState immuable)
 16. request      ← RiskEngineRequestFactory.FromTradePlan(plan, accountState, policy, spec, portfolio)
 17. assessment   ← request is null ? null : RiskEngine.Evaluate(request)

  ── D. EXÉCUTION SIMULÉE ──
 18. si assessment?.IsAccepted == true ET aucune position ouverte (ou politique multi-positions) :
       fill ← ExecutionModel.TryFillEntry(plan, assessment, bar)     ← cf. §11.2 sur la barre de fill
       si fill → account.ApplyFill(...) → nouvelle position ouverte

  ── E. INSTRUMENTATION ──
 19. collector.Add(ScientificDatasetRecord.From(..., riskAccountState: accountState, ...))

FIN POUR

 20. Clôture forcée de toute position encore ouverte à la dernière barre,
     marquée ForcedCloseAtEndOfWindow — jamais silencieusement ignorée.
```

### 8.5 Gestion du temps

| Règle | Justification |
|---|---|
| L'horloge du backtest est **exclusivement** `HistoricalBar.Timestamp` | `MarketClock.CurrentTime` est déjà la seule horloge lue par `RegimeEngine`, `EvidenceContext`, `EvidenceSet.Timestamp` et `ScientificDatasetRecord` |
| `DateTime.UtcNow` est **interdit** dans tout code du Backtest Engine | 26 occurrences existent dans le pipeline (§17, RISK-04) : le backtest ne doit pas en ajouter une 27e |
| Les `Timestamp` produits par `TradePlanBuilder`/`EntryTriggerBuilder`/`EntryEngine` (wall-clock) sont **ignorés** par le backtest | Ces champs sont `DateTime.UtcNow` ; les corriger exigerait de modifier des composants protégés |
| Le timestamp faisant foi pour tout enregistrement est `context.Clock.CurrentTime` | C'est déjà ce que fait `ScientificDatasetRecord.From` (`marketContext.Timestamp`, ligne 325) |
| Aucune conversion de fuseau n'est appliquée par le moteur | La conversion appartient à l'adapter d'ingestion (§12), le moteur reçoit des timestamps déjà normalisés et documentés |

### 8.6 Gestion du look-ahead

Traitée intégralement en §13.

### 8.7 Gestion des positions

| Décision | Recommandation | Motif |
|---|---|---|
| Nombre de positions simultanées | **Une seule au LOT 14** | `PortfolioState.OpenPositions` existe mais `RiskEngine.Evaluate` **ne le lit pas** (doc de `PortfolioState` : « *RiskEngine.Evaluate does NOT read OpenPositions…* »). Multi-positions exigerait une agrégation qui n'existe pas. |
| Signal opposé sur position ouverte | **Ignorer, journaliser** | Retourner une position est une règle de trading que le pipeline n'exprime pas aujourd'hui |
| Pyramidage | **Interdit** | idem |
| Modèle de position | `OpenPosition` **existe déjà** (`Engine/Risk/PortfolioState.cs`) : `Symbol, Direction, Quantity, EntryPrice, StopLoss, RiskAmount` | Réutilisable tel quel |
| `PortfolioState` alimenté ? | **Oui, en lecture seule** — le passer dans `RiskEngineRequest` pour l'observabilité | Le champ est déjà optionnel dans `RiskEngineRequest` |

### 8.8 Gestion des ordres simulés

Aucun carnet d'ordres, aucune file. Le modèle est : « une position ouverte au prix de fill, fermée au prix de sortie ». Les seuls « ordres » sont les niveaux `StopLoss`/`TakeProfit` du `TradePlan`, évalués contre le `High`/`Low` de chaque barre postérieure (§11).

### 8.9 Gestion du capital

`ISimulatedAccount` produit un `AccountState` neuf par barre. Détail en §9.

### 8.10 Gestion du risque

**Aucune logique de risque n'est écrite dans le Backtest Engine.** Il appelle `RiskEngine.Evaluate` et respecte `RiskAssessment.Status`/`PositionSize`. Un `RiskAssessment.REJECTED` **n'ouvre jamais** de position. C'est la propriété qui garantit que backtest et production partagent le même risque.

---

## 9. SIMULATED ACCOUNT ARCHITECTURE

### 9.1 Réponse au §6 du brief

| Type existant | Modifiable ? | Utilisation par le backtest |
|---|---|---|
| `AccountState` | **NON (protégé)** | Produit à neuf chaque barre — c'est un `record` immuable à 8 champs |
| `PortfolioState` / `OpenPosition` | **NON (protégé)** | `OpenPosition` réutilisé pour les positions ; `PortfolioState` passé en lecture seule |
| `RiskPolicy` | **NON (protégé)** | Fourni par le scénario |

**Aucun de ces trois types n'a besoin d'être modifié.** `AccountState` étant un `record`, `account with { CurrentEquity = ... }` suffit.

### 9.2 Correspondance demandée par le brief

| Concept du brief | Champ `AccountState` | Origine en backtest |
|---|---|---|
| `InitialCapital` | `InitialCapital` | **Configuration du scénario**, constante sur tout le run |
| `CurrentEquity` | `CurrentEquity` | `InitialCapital + ClosedPnL + OpenPnL − CumulativeCosts`, recalculé chaque barre |
| `Balance` | `CurrentBalance` (`decimal?`) | `InitialCapital + ClosedPnL − CumulativeCosts` (hors PnL latent) |
| `OpenPnL` | *(pas de champ)* | Interne au simulateur ; réinjecté via `CurrentEquity` |
| `ClosedPnL` | *(pas de champ)* | Interne ; réinjecté via `CurrentBalance` |
| `BuyingPower` | *(pas de champ)* | **Non modélisé** — aucun consommateur ; ne rien inventer |
| `Position` | `OpenPosition` (`PortfolioState`) | Interne au simulateur |
| `RealizedPnL` | *(pas de champ)* | = `ClosedPnL` |
| `UnrealizedPnL` | *(pas de champ)* | = `OpenPnL` |
| — | `PeakEquity` | `Max(PeakEquity, CurrentEquity)` mis à jour chaque barre |
| — | `DailyStartingEquity` | `CurrentEquity` au premier bar de chaque **date** (`Clock.CurrentDate`) |
| — | `DailyPnL` | `CurrentEquity − DailyStartingEquity` |
| — | `RiskUsedToday` | somme des `RiskAmount` des trades ouverts **ce jour** |
| — | `OpenRisk` | somme des `RiskAmount` des positions **actuellement ouvertes** |

Les quatre derniers champs sont **exactement** ceux que `RiskEngine.Evaluate` consomme pour ses contraintes drawdown/daily-loss/open-risk (phases 7-8, lignes 101-167). Un compte simulé qui ne les tient pas rend ces contraintes inertes — et un backtest où les limites de risque ne s'activent jamais n'a aucune valeur prédictive.

### 9.3 Ce que le compte simulé ne doit PAS faire

- Ne jamais appeler `ATASAccountStateAdapter` (il exige un `Portfolio` ATAS)
- Ne jamais fabriquer une equity quand elle est incalculable — `RiskEngine` traite déjà `0m` comme `INVALID_EQUITY`, et **c'est le comportement voulu**
- Ne jamais réinjecter `RiskAssessment` dans le pipeline signal — le pipeline est unidirectionnel (Lot 11 §11 : « *strictly observational* »)
- Ne pas modéliser le margin call : aucune spécification de marge n'existe dans le dépôt

### 9.4 Réinitialisation entre fenêtres

Un `ISimulatedAccount` **neuf** par fenêtre (`BacktestWindow`). L'equity finale de TRAIN **ne doit jamais** devenir l'equity initiale de VALIDATION : cela créerait une dépendance de trajectoire entre fenêtres censées être indépendantes (§14).

---

## 10. INSTRUMENT ARCHITECTURE

### 10.1 État réel

`RiskEngine.cs` ne contient **aucun** branchement sur `Symbol` — vérifié ligne par ligne. Toute la spécification passe par `RiskEngineRequest.Instrument` (`InstrumentRiskSpecification`). L'exigence « ES/MES sans hardcoding dans RiskEngine » est donc **déjà satisfaite**.

Producteurs actuels d'`InstrumentRiskSpecification` :

| Producteur | Contexte | Source | Utilisable en backtest |
|---|---|---|---|
| `ATASInstrumentAdapter.Build` | Live | `TradingManager.Security` | **NON** |
| `InstrumentRiskSpecification.FromInstrumentInfo` | générique | `Core.InstrumentInfo` + 3 bornes | **OUI** |
| Littéraux `EsSpec()` / `MesSpec()` | `Tests/Risk/RiskEngineIntegrationTests.cs:51-59` | codés en dur dans les tests | **NON** (test uniquement) |

### 10.2 Ce qui manque

Une **source de spécification pilotée par la donnée**. Deux valeurs sont particulièrement sensibles :

| Champ | Statut actuel | Note |
|---|---|---|
| `PointValue` | Dérivé `TickCost / TickSize` par `ATASInstrumentAdapter` ; paramètre UI en fallback | ES : `12.50 / 0.25 = 50` — la relation est déjà documentée |
| `QuantityStep` | **Aucune source ATAS** (Lots 12.1/12.4/12.6) ; exclusivement manuel | Le Lot 12.6 a explicitement **refusé** d'utiliser `Security.LotSize` (défaut SDK `1` indiscernable d'une vraie donnée) — **le backtest ne doit pas réintroduire cette ambiguïté** |

### 10.3 Spécification proposée (À NE PAS IMPLÉMENTER)

```
IInstrumentSpecificationSource
    InstrumentRiskSpecification Resolve(string symbol)

InstrumentSpecificationCatalog : IInstrumentSpecificationSource
    - chargé depuis un fichier de configuration (JSON), versionné
    - Resolve lève une exception explicite si le symbole est absent — jamais de valeur par défaut
    - aucune valeur codée en dur dans la classe
```

Schéma de configuration :

```json
{
  "instruments": [
    { "symbol": "ES",  "tickSize": ?, "tickValue": ?, "pointValue": ?,
      "minQuantity": ?, "maxQuantity": ?, "quantityStep": ?, "contractMultiplier": null },
    { "symbol": "MES", "tickSize": ?, "tickValue": ?, "pointValue": ?,
      "minQuantity": ?, "maxQuantity": ?, "quantityStep": ?, "contractMultiplier": null }
  ]
}
```

**Les `?` sont délibérés.** Cet audit ne fixe aucune valeur : elles doivent provenir des spécifications contractuelles CME (ES/MES) ou d'une capture ATAS documentée, et être validées par `InstrumentRiskSpecification.IsValid` **avant** tout run. Une valeur inventée ici deviendrait silencieusement la référence du backtest.

Point de validation naturel : `InstrumentRiskSpecification.IsValid` existe déjà et couvre les 7 conditions (`Symbol` non vide, `TickSize>0`, `TickValue>0`, `PointValue>0`, `MinQuantity>0`, `MaxQuantity>=MinQuantity`, `QuantityStep>0`). Le catalogue le réutilise, il ne le réécrit pas.

### 10.4 Sélection ES vs MES sans modifier RiskEngine

```
BacktestScenario.Symbol = "MES"
   → InstrumentSpecificationCatalog.Resolve("MES") → InstrumentRiskSpecification
   → RiskEngineRequestFactory.FromTradePlan(plan, account, policy, spec)
   → RiskEngine.Evaluate(request)
```

`RiskEngine` ne voit qu'un `InstrumentRiskSpecification`. Changer d'instrument = changer une ligne de configuration de scénario. **Aucune modification de `RiskEngine`.** C'est exactement le pattern déjà couvert par `Test05_EsVsMesUseDistinctSpecificationsNoHardcoding`.

---

## 11. SIMULATED EXECUTION ARCHITECTURE

### 11.1 Principe directeur

> **Le modèle de coût doit être explicitement séparé de la logique scientifique.** (§8 du brief)

Séparation retenue :

| Couche | Contenu | Connaît les coûts ? |
|---|---|---|
| Pipeline scientifique (existant) | Regime → Decision → Entry → EntryTrigger → TradePlan | **NON, jamais** |
| RiskEngine (existant, protégé) | Sizing, budget, contraintes | **NON** — travaille sur des distances de prix et `PointValue` |
| `IExecutionModel` (nouveau) | Quand et à quel prix un ordre est rempli | Consomme `ICostModel` |
| `ICostModel` (nouveau) | Slippage, commission, spread | **OUI, exclusivement** |
| `ISimulatedAccount` (nouveau) | Applique les coûts à l'equity | Reçoit des montants déjà calculés |

Conséquence : un backtest « sans frictions » = injecter un `ICostModel` à zéro. Le pipeline scientifique est **bit-identique** dans les deux cas. C'est la propriété qui permet d'isoler l'effet des coûts.

### 11.2 Entry / Fill

| Question | Décision recommandée | Justification |
|---|---|---|
| Sur quelle barre le fill a-t-il lieu ? | **Barre `i+1`** — signal sur `i`, fill sur `i+1` | `TradePlan.EntryPrice` vient de `EntryTriggerCandidate.CurrentPrice`, qui est le **Close** de la barre `i` (`MarketContextBuilder` → `PriceInfo.Close` → `Abstractions.MarketContext.CurrentPrice`). Remplir au Close de `i` suppose de connaître la clôture au moment de décider — **c'est du look-ahead** (§13.3). |
| À quel prix ? | `Open` de la barre `i+1`, ajusté du slippage et du demi-spread | Le seul prix de `i+1` disponible sans regarder le reste de la barre |
| Alternative retenue ? | Un mode `FillAtSignalClose` **peut** être offert, **explicitement étiqueté optimiste** dans le résultat | Utile pour comparer, jamais comme défaut |
| Quantité | `RiskAssessment.PositionSize` **exactement** | Ne jamais recalculer une taille : ce serait une seconde logique de sizing |

### 11.3 Stop Loss / Take Profit

Niveaux : `RiskAssessment.StopLoss` / `.TakeProfit` (recopiés depuis `TradePlan`). Ils **ne sont jamais recalculés** par le moteur d'exécution.

### 11.4 Ordre d'évaluation intra-barre — règle explicite obligatoire

Quand une barre touche **à la fois** SL et TP (`Low <= SL` et `High >= TP` pour un BUY), le résultat est indécidable sans données infra-barre. Trois politiques possibles ; **une seule doit être choisie, écrite, et journalisée dans chaque `BacktestResult`** :

| Politique | Comportement | Recommandation |
|---|---|---|
| `StopFirst` | SL prioritaire | **RECOMMANDÉE par défaut** — conservatrice, ne surestime jamais la performance |
| `TargetFirst` | TP prioritaire | À proscrire par défaut (optimiste) |
| `Ambiguous` | Trade marqué indécidable, exclu des stats de PnL, **compté séparément** | À offrir — c'est l'analogue direct de `EntryFate.Undetermined` (`StopLossEvaluator.cs`), une catégorie que le projet utilise déjà plutôt que de deviner |

Le précédent interne est clair : `OutcomeMeasurement`/`EntryFate` distinguent déjà `Reverted` / `StoppedOut` / **`Undetermined`** — « *Never silently folded into either bucket above* ». Le Backtest Engine doit hériter de cette discipline.

### 11.5 MFE / MAE

Le calcul existe déjà et est **directement réutilisable** : `OutcomeSimulator.Simulate` (`Tests/Research/StopLossCalibration/OutcomeSimulator.cs:16-63`).

| Aspect | Comportement existant | Adaptation backtest |
|---|---|---|
| Ajustement directionnel | Fait à la construction — MAE et MFE sont **toujours ≥ 0** | conserver |
| Source de prix | `series[entryBarIndex + h]` = **closes uniquement** | **Étendre à `High`/`Low`** : une excursion intra-barre est plus fidèle. À documenter comme un écart assumé avec le harness de calibration. |
| Chemin cumulé | `AdverseExcursionPath` / `FavorableExcursionPath` monotones | conserver |
| `BarsAvailable < Horizon` | Jamais complété silencieusement | conserver — impératif en fin de fenêtre |

### 11.6 PnL

```
GrossPnL = (ExitPrice − EntryPrice) × PointValue × Quantity     [BUY]
           (EntryPrice − ExitPrice) × PointValue × Quantity     [SELL]
NetPnL   = GrossPnL − EntryCost − ExitCost
```

`PointValue` provient d'`InstrumentRiskSpecification` — **la même instance que celle donnée à `RiskEngine`**. C'est cette identité qui garantit que le PnL réalisé est cohérent avec le `RiskAmount` calculé par le moteur de risque.

### 11.7 Slippage / Commission / Spread

**Cet audit ne propose aucune valeur.** Points d'injection :

| Coût | Injecté où | Unité | Appliqué quand |
|---|---|---|---|
| Slippage entrée | `ICostModel.EntrySlippageTicks` | ticks (× `TickSize`) | prix de fill d'entrée |
| Slippage sortie | `ICostModel.ExitSlippageTicks` | ticks | prix de fill de sortie |
| Spread | `ICostModel.SpreadTicks` | ticks | demi-spread à chaque côté |
| Commission | `ICostModel.EntryCost/ExitCost` | devise par contrat | déduit du PnL net |

Sources acceptables pour ces valeurs (aucune n'existe aujourd'hui dans le dépôt) : barème du broker, statistiques de fills ATAS réelles, ou barème CME publié. Une valeur inventée dans le code deviendrait la référence par défaut de tous les backtests futurs — c'est précisément le type de fabrication que les Lots 12.x ont systématiquement refusé.

### 11.8 Motifs de sortie à tracer

`StopLoss` · `TakeProfit` · `MaxHoldingBarsReached` · `EndOfWindow` · `AmbiguousIntrabar` — jamais agrégés en un seul « Closed ».

---

## 12. SCIENTIFIC DATASET INTEGRATION

### 12.1 Réutilisabilité

| Composant | Réutilisable en backtest | Réserve |
|---|---|---|
| `ScientificDatasetRecord` | **OUI** | paramètres ATAS optionnels → `null` → `"NOT AVAILABLE"` |
| `ScientificDatasetCollector` | **OUI, avec une réserve majeure** | cf. §12.3 |
| `ScientificDatasetSession` / `...Writer` | OUI | métadonnées de provenance à adapter |
| `ScientificDatasetExporter` | OUI | inchangé |
| `DatasetLifecycleLog` / `...SnapshotWriter` | OUI | sémantique ATAS (OnDispose) sans objet |
| `ToCsv()` / `ToJson()` / `ToOhlcvCsv()` | OUI | inchangés |

### 12.2 Champs demandés par le brief — disponibilité réelle

| Champ demandé | Disponible ? | Clé exacte |
|---|---|---|
| Timestamp | **OUI** | champ `Timestamp` (= `marketContext.Timestamp`) |
| Symbol | **OUI** | champ `Symbol` |
| Timeframe | **OUI** | champ `TimeFrame` |
| Regime | **OUI** | `Categories["Decision.Winner"]` |
| Decision | **OUI** | `Metrics["Decision.FinalScore"]`, `Categories["Decision.TriggeredRules"/"RejectedRules"]` |
| Winner | **OUI** | `Categories["Decision.Winner"]` |
| Difference | **PARTIEL** | non exporté tel quel ; dérivable : `Difference = 1 − AmbiguityScore` |
| AmbiguityScore | **NON** | **absent du record** — seuls `ScientificScore`/`QualityScore`/`FinalScore` sont exportés |
| Direction | **OUI** | `Categories["EntryTrigger.Direction"]` (Lot 9) |
| Entry | **OUI** | `Categories["TradePlan.EntryPrice"]` |
| StopLoss | **OUI** | `Categories["TradePlan.StopLoss"]` + `["TradePlan.StopLossAvailable"]` |
| TakeProfit | **OUI** | `Categories["TradePlan.TakeProfit"]` |
| RiskAssessment | **OUI** | `Categories["Risk.*"]` (Lot 11) |
| RiskDecision | **OUI** | `Categories["Risk.Status"]`, `["Risk.RejectionReasons"]` |
| PositionSize | **OUI** | `Categories["Risk.PositionSize"]` |
| **PnL** | **NON** | aucun champ — le pipeline live n'a pas de notion de PnL de trade |
| **MFE** | **NON** | aucun champ |
| **MAE** | **NON** | aucun champ |
| Durée de trade | **NON** | aucun champ |
| Motif de sortie | **NON** | aucun champ |

**Conclusion §11 du brief** : `ScientificDatasetRecord` couvre intégralement le **signal**, et **pas du tout l'issue**. C'est logique — le pipeline live ne connaît aucune issue.

### 12.3 Deux réserves opérationnelles sur `ScientificDatasetCollector`

**(a) La machine à états « forming bar » (Sprint 15.19) est sans objet en backtest.**
Le collector met en tampon la barre courante et ne la valide que lorsqu'une callback pour une barre **postérieure** prouve sa clôture ; `Flush()` **exclut définitivement** la dernière barre en formation. En backtest, chaque barre est close par construction. Effet mécanique : **la dernière barre de chaque run serait systématiquement absente** du dataset exporté. Comportement à connaître et à documenter ; il ne justifie pas de modifier le collector (interdit d'ailleurs de fait par la discipline « ne pas modifier ce qui fonctionne »).

**(b) L'identité logique est `SessionId + Symbol + TimeFrame + CurrentBar`.**
Elle reste valide en backtest à condition que chaque run ait son propre `SessionId` (un `Guid` par `BacktestScenario`). Deux fenêtres du même symbole partageant un `SessionId` produiraient des doublons rejetés.

### 12.4 Architecture recommandée : deux sorties, pas une

```
BacktestResult
  ├─ ScientificDataset  : ScientificDatasetRecord[]   ← EXISTANT, inchangé (signal par barre)
  └─ TradeLedger        : BacktestTrade[]             ← NOUVEAU (issue par trade)
```

Jointure par `(SessionId, Symbol, TimeFrame, Timestamp)`. Ceci respecte l'interdiction de modifier `ScientificDatasetRecord` dans ce lot **et** évite d'y ajouter des champs d'issue qui seraient perpétuellement `"NOT AVAILABLE"` en production live. Si une fusion est jugée souhaitable plus tard, elle se fera par un enrichissement en aval, pas par une modification du record.

---

## 13. LOOK-AHEAD PROTECTION

C'est le point le plus critique du LOT 14. Le dépôt possède déjà une doctrine éprouvée : il faut l'appliquer, pas la réinventer.

### 13.1 Le contrat

```
Signal(bar i)   → fonction UNIQUEMENT de X[0..i]
Outcome(bar i)  → fonction UNIQUEMENT de X[i+1..], calculé APRÈS le signal,
                  et ne réalimentant JAMAIS le signal
```

### 13.2 Ce qui protège déjà, structurellement

| Protection | Emplacement | Nature |
|---|---|---|
| Modèles scientifiques stateless | `ScientificModelRegistry.Resolve` crée des instances **neuves** à chaque appel | structurelle |
| `VolatilityModel` — contrat causal | doc de classe (3 faits vérifiés au Lot B2) | documentée + testée |
| `KalmanFilterModel` — fenêtre bornée | `ObservationWindowSize = 20` derniers points | structurelle |
| Historique fini par construction | `CreateScientificMarketContext` : 500 closes **finissant** à la barre courante | structurelle |
| `RegimeEngine` — buffer circulaire | `_priceBuffer[128]`, alimenté **après** l'arrivée de la barre | structurelle |
| Un seul lecteur du futur | `OutcomeSimulator` — « *THE ONLY class in this harness allowed to read series[entryBarIndex+1 ..]* » | doctrine + test |
| Test de troncature | `StopLossCalibrationPocTests.AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics` | automatisé |
| Audit ciblé volatilité | `VolatilityRegimeLookAheadAuditTests` — 5 datasets × 11 barres, **comparaison bit-exacte** | automatisé |
| Tests de causalité | `Tests/ScientificModels/VolatilityModelCausalityTests.cs` (B2-01..B2-05, non commité) | automatisé |

**Le pipeline scientifique lui-même est donc déjà propre.** Les fuites possibles sont toutes du côté du **moteur de backtest**.

### 13.3 Les six fuites que le Backtest Engine peut introduire

| # | Fuite | Manifestation concrète | Défense obligatoire |
|---|---|---|---|
| L-1 | **Utilisation de la barre future pour générer un signal** | Passer `Bars[0..N]` à la construction du contexte de la barre `i` | La construction du contexte de `i` ne reçoit que `Bars[0..i]` — et la fenêtre scientifique est bornée à `[max(0, i−499) .. i]` |
| L-2 | **Fill au Close de la barre de signal** | Entrer à `Close[i]` alors que le signal est *dérivé* de `Close[i]` | Fill à `Open[i+1]` (§11.2) ; le mode `FillAtSignalClose` doit être étiqueté optimiste dans le résultat |
| L-3 | **Évaluation SL/TP sur la barre d'entrée** | Tester le stop contre `High[i]`/`Low[i]` alors qu'on est entré à `Open[i+1]` | La boucle de sortie ne démarre qu'à la barre **suivant** le fill |
| L-4 | **Contamination du calibrage** | Un `k` de stop-loss choisi en regardant l'OOS | Le `IStopLossStrategy` est une **entrée figée** du scénario ; le moteur ne l'ajuste jamais |
| L-5 | **Fuite train/test par l'état des moteurs** | Réutiliser `FusionStateManager` / `RegimeEngine` entre deux fenêtres | **Instance neuve de chaque moteur par fenêtre** (§13.4) |
| L-6 | **Fuite entre fenêtres OOS par le compte** | Reporter l'equity finale de TRAIN sur VALIDATION | `ISimulatedAccount` neuf par fenêtre (§9.4) |

### 13.4 État à réinitialiser — inventaire exhaustif

| Composant | État | Mécanisme de reset | Suffisant ? |
|---|---|---|---|
| `RegimeEngine` | `_priceBuffer[128]`, `_priceBufferHead`, `_priceBufferCount` | `if (context.Clock.IsFirstBar)` | **OUI** |
| `HurstEvidence` | `_buf[30]`, `_h`, `_n` | `if (ctx.Clock.IsFirstBar)` | **OUI** |
| `VolatilityEvidence` | `_buf[30]`, `_h`, `_n` | `if (ctx.Clock.IsFirstBar)` | **OUI** |
| `MarketContextBuilder` | `_firstBarTime`, `_maxRealtimeBar` | `if (bar == 0)` | sans objet (non utilisé) |
| **`FusionStateManager`** | `_previousStableResult`, `_updateCount` | **AUCUN** | **NON — instance neuve OBLIGATOIRE** |
| `SignalEngine` | `LastXxx` (7 propriétés) | écrasées chaque appel | OUI |
| `KalmanFilterModel._diagnostics` | `List` **statique** | `ResetDiagnostics()` | manuel — cf. RISK-06 |

**Règle normative pour le LOT 14** : *un `BacktestRun` instancie l'intégralité de ses moteurs ; aucune instance n'est jamais partagée entre deux runs ou deux fenêtres.* C'est la seule protection fiable contre L-5, car `FusionStateManager` n'offre aucune alternative.

### 13.5 Warmup

`CalibrationEntryBuilder.WarmupBars = 30`. Mais les besoins réels du pipeline complet sont plus élevés :

| Composant | Minimum |
|---|---|
| `HurstEvidence` / `VolatilityEvidence` | 20 |
| `HalfLife` / `VarianceRatio` / `CUSUM` | 20 |
| ADF / KPSS | 30 |
| Bai-Perron | 64 |
| **DFA** | **80** |
| `KalmanFilterModel` (fenêtre pleine) | 20 |

**Aucune décision d'entrée ne devrait être prise avant que les 9 évidences aient quitté leur warmup, soit ≥ 80 barres** — et l'idéal est ≥ 128 (`DfaWindowSize`/`BaiPerronWindowSize`) pour que les fenêtres les plus longues soient pleines. Les barres de warmup doivent être **comptées et rapportées**, jamais silencieusement absorbées. Ceci est un choix de configuration du scénario, pas une constante à coder en dur.

### 13.6 Preuve automatisée attendue au LOT 14

Le test décisif, calqué sur `AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics` :

> Faire tourner le backtest sur `Bars[0..N]`, puis sur `Bars[0..M]` avec `M < N`. **Toutes** les décisions, tous les `RiskAssessment`, et tous les trades **ouverts avant M** doivent être **bit-identiques** entre les deux runs. Toute différence prouve une fuite.

C'est la version « moteur complet » d'un test que le projet applique déjà aux métriques, et il faut l'exiger dès le premier jour.

---

## 14. CALIBRATION / OOS ARCHITECTURE

### 14.1 Précédent existant

Le harness sépare déjà TRAIN/VALIDATION/TEST, **par seed** (`CampaignDatasetCatalog.cs:16-18`), avec :
- une découverte de région **uniquement sur TRAIN** (`A1StableRegionDiscovery`)
- une vérification sur VALIDATION **sans redécouverte** (`A1ValidationConfirmation`)
- une confirmation TEST **évaluée en dernier, jamais utilisée pour créer une région** (`A1TestConfirmation`)
- un reporting **côte à côte, jamais moyenné** (`HybridTrainValidationTestAnalysis`)

Cette discipline est la bonne. Ce qui manque, ce sont des **fenêtres de dates** au lieu de seeds.

### 14.2 Architecture proposée

```
BacktestWindow
    Name    : "CALIBRATION" | "VALIDATION" | "OOS" | "FINAL_HOLDOUT"
    From    : DateTime
    To      : DateTime            (exclusif)
    Purpose : WindowPurpose enum

BacktestWindowSet
    Windows : IReadOnlyList<BacktestWindow>
    Invariants (validés à la construction, jamais silencieusement corrigés) :
      - aucun chevauchement (Window[i].To <= Window[i+1].From)
      - ordre chronologique strict
      - FINAL_HOLDOUT est toujours la dernière fenêtre
```

**Le brief demande explicitement de ne PAS implémenter les dates d'exemple comme configuration par défaut.** Position de cet audit : `BacktestWindowSet` **ne doit avoir aucun défaut**. Les fenêtres sont fournies explicitement par le scénario, sinon le moteur refuse de démarrer.

### 14.3 Règles d'isolation

| Règle | Mise en œuvre |
|---|---|
| Chaque fenêtre a ses propres instances de moteurs | §13.4 |
| Chaque fenêtre a son propre `ISimulatedAccount` (même `InitialCapital`) | §9.4 |
| Chaque fenêtre a son propre `SessionId` de dataset | §12.3(b) |
| Le warmup d'une fenêtre peut lire les barres **précédant** `From` | **Autorisé et recommandé** : c'est de l'information passée, pas du futur. À rapporter explicitement (`WarmupBarsFromPriorPeriod`). |
| Les résultats sont rapportés côte à côte, jamais agrégés | précédent `HybridTrainValidationTestAnalysis` |
| `FINAL_HOLDOUT` est exécuté **une seule fois**, en dernier | discipline, à documenter dans le rapport de run |

### 14.4 Le point de contamination le plus probable

Ce n'est pas le code — c'est **le processus humain** : ajuster un paramètre après avoir vu l'OOS. Deux gardes concrets :

1. Le `BacktestScenario` (avec `IStopLossStrategy` et `RiskPolicy`) est **sérialisé et horodaté** dans `BacktestResult`. Un run OOS avec un scénario différent d'un run TRAIN antérieur est ainsi détectable.
2. Un hash du scénario est écrit dans les métadonnées du dataset. Ré-exécuter l'OOS avec un scénario modifié produit un hash différent, visible.

---

## 15. TEST STRATEGY

*(Matrice conceptuelle — aucun test à créer dans ce lot.)*

### 15.1 Unit

| # | Test | Cible | Critère |
|---|---|---|---|
| U-01 | Ingestion de barre | `HistoricalBar` → `Core.MarketContext` | Tous champs mappés ; `Median`/`TypicalPrice` **identiques** à `MarketContextBuilder.cs:114-115` |
| U-02 | Timestamp | mapping horloge | `CurrentTime`, `CurrentDate`, `DayOfWeek`, `ElapsedMinutes` cohérents |
| U-03 | Ordre chronologique | `HistoricalSeries` | Rejet (exception) d'une série non strictement croissante — jamais de tri silencieux |
| U-04 | Doublons de timestamp | validation d'ingestion | Détection et rejet explicite |
| U-05 | `IsFirstBar`/`IsLastBar` | horloge | Vrai uniquement aux extrémités |
| U-06 | Warmup | boucle | Aucune entrée avant le seuil configuré |
| U-07 | Sizing | `RiskEngine` (existant) | **Aucun nouveau test** — `Tests/Risk/*` couvre déjà |
| U-08 | Fill d'entrée | `IExecutionModel` | Prix = `Open[i+1]` + slippage + demi-spread |
| U-09 | Fill de sortie SL | `IExecutionModel` | Déclenché si `Low <= SL` (BUY) |
| U-10 | Fill de sortie TP | `IExecutionModel` | Déclenché si `High >= TP` (BUY) |
| U-11 | Barre ambiguë SL+TP | `IExecutionModel` | Politique appliquée **et** journalisée |
| U-12 | Coûts | `ICostModel` | Modèle nul ⇒ `NetPnL == GrossPnL` |
| U-13 | PnL | calcul | Signe correct BUY/SELL ; `PointValue` issu de la spec |
| U-14 | MFE/MAE | mesure | Toujours ≥ 0 ; monotones |
| U-15 | Compte | `ISimulatedAccount` | `Equity`, `PeakEquity`, `DailyPnL`, `OpenRisk` cohérents après séquence connue |
| U-16 | Reset journalier | `ISimulatedAccount` | `DailyStartingEquity` réinitialisé au changement de `CurrentDate` |
| U-17 | Catalogue d'instruments | `IInstrumentSpecificationSource` | Symbole absent ⇒ exception, jamais de défaut |
| U-18 | Fenêtres | `BacktestWindowSet` | Chevauchement ⇒ rejet |

### 15.2 Integration

| # | Test | Critère |
|---|---|---|
| I-01 | Données historiques → pipeline | Une série réelle produit ≥ 1 `EvidenceSet` valide après warmup |
| I-02 | Pipeline → RiskEngine | Un `TradePlan` avec SL injecté produit un `RiskEngineRequest` non nul |
| I-03 | RiskEngine → exécution simulée | Un `ACCEPTED` ouvre exactement une position, de taille `PositionSize` |
| I-04 | RiskEngine `REJECTED` | **Aucune** position ouverte, quel que soit le motif |
| I-05 | Sans stop-loss | `INVALID_STOP_LOSS` et zéro trade — reproduit le comportement production actuel |
| I-06 | Cycle complet | Entrée → SL/TP → compte mis à jour → dataset écrit |
| I-07 | ES vs MES | Même série, deux specs ⇒ `PositionSize` et PnL différents, **zéro modification de `RiskEngine`** |
| I-08 | Dataset | `Records.Count == BarsProcessed − 1` (effet « forming bar », §12.3a) |
| I-09 | Contexte invalide | Une barre rejetée par `MarketContextValidator` n'interrompt pas le run |

### 15.3 Scientific

| # | Test | Critère |
|---|---|---|
| S-01 | **No look-ahead (troncature)** | Run sur `[0..N]` vs `[0..M]` ⇒ décisions/trades avant `M` **bit-identiques** (§13.6) |
| S-02 | No look-ahead (perturbation) | Modifier `Bars[i+1..]` ne change **aucune** sortie de la barre `i` |
| S-03 | Replay déterministe | Deux exécutions du même scénario ⇒ résultats bit-identiques (§17, RISK-05) |
| S-04 | Séparation train/OOS | Réordonner l'exécution des fenêtres ne change aucun résultat par fenêtre |
| S-05 | Isolation d'état | Fenêtre exécutée seule vs dans une séquence ⇒ résultats identiques (prouve le reset de `FusionStateManager`) |
| S-06 | Reproductibilité | Même scénario, deux processus ⇒ même hash de résultat |
| S-07 | Coûts nuls | `ICostModel` nul ⇒ PnL brut = PnL net, décisions inchangées |
| S-08 | Invariance d'échelle | Série × 10 ⇒ mêmes décisions (précédent : `A1ScaleAnalysis`, écart observé `1,8e-15`) |

### 15.4 Regression

| # | Test | Critère |
|---|---|---|
| R-01 | Même entrée → même sortie | Golden file d'un run de référence |
| R-02 | Backtest vs dataset scientifique existant | Rejouer une capture ATAS réelle (`RealMarket/RawCapture/Sprint15_18`) via le backtest ⇒ `Decision.Winner`, `EntryTrigger.Direction`, `TradePlan.Status` **identiques** aux valeurs enregistrées par ATAS |
| R-03 | `RiskEngine` identique | Mêmes `RiskEngineRequest` ⇒ mêmes `RiskAssessment` en backtest et en live |
| R-04 | Pipeline scientifique intact | Les suites existantes (`Tests/GoldenDatasets/*`, `Tests/Risk/*`, `Tests/Decision/*`, `Tests/Fusion/*`) restent vertes |

**R-02 est le test le plus important de toute la matrice.** C'est la seule preuve empirique que backtest et ATAS calculent réellement la même chose, et le dépôt possède déjà les données pour le faire : `Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/` contient une capture ATAS réelle au format OHLCV, lisible par `RealMarketOhlcvCsvReader`.

---

## 16. ATAS vs BACKTEST MATRIX

### 16.1 Matrice corrigée d'après le code réel

| Fonction | ATAS | Backtest | Correction apportée à la matrice du brief |
|---|---|---|---|
| Market data | **YES** | **YES** (via `IHistoricalBarSource`) | — |
| Historical data | **PARTIAL** | **YES** | Confirmé : ATAS fournit les barres déjà chargées du chart via `GetCandle`, mais **aucun outil d'export historique n'existe** (rapport 15.16) |
| Candle / Feature pipeline | YES | YES | Ajout : `MarketCache` est vide des deux côtés |
| Regime | **SAME** | **SAME** | Confirmé — `RegimeEngine` identique, entrée `Core.MarketContext` identique |
| Decision | **SAME** | **SAME** | Confirmé |
| Entry | **SAME** | **SAME** | Confirmé |
| EntryTrigger | **SAME** | **SAME** | Ligne ajoutée (absente du brief) |
| TradePlan | **SAME (code)** | **SAME (code), ENTRÉES DIFFÉRENTES** | **Correction** : `RiskParameters == null` en ATAS, injecté en backtest ⇒ `Status` diffère structurellement (`SIGNAL_ONLY` vs `PLAN_READY`) |
| RiskEngine | **SAME** | **SAME** | Confirmé — bit-identique, `RiskEngine.cs` sans aucune dépendance externe |
| Account | **REAL** | **SIMULATED** | Précision : « REAL » = `Portfolio.Balance` + `TradingStatisticsProvider.Equity` ; indisponible ⇒ `0m` ⇒ `INVALID_EQUITY` |
| Instrument | **ATAS-SOURCED** | **CONFIG-SOURCED** | **Ligne ajoutée** — omise du brief, pourtant c'est son §7 |
| **Execution** | **NONE** | **SIMULATED** | **Correction majeure** : le brief indique « REAL/Replay ». **L'indicateur IQIA n'envoie aucun ordre**, en aucune circonstance (Lot 11 §11 : « *this indicator never sends an order regardless of RiskAssessment*, *strictly observational* »). Aucun `TradingManager.OpenOrder`/`ClosePosition` n'existe dans le dépôt. |
| Position tracking | **NONE** (observé seulement) | **SIMULATED** | Ligne ajoutée |
| PnL | **NONE** (lu depuis ATAS pour diagnostic) | **COMPUTED** | Ligne ajoutée |
| MFE / MAE | **NONE** | **COMPUTED** | Ligne ajoutée |
| Coûts (slippage/commission/spread) | **NONE** | **INJECTED** | Ligne ajoutée |
| Dashboard | **YES** | **NO** | **Correction** : le brief indique « OPTIONAL ». 13 fichiers sur 18 dépendent d'`OFT.Rendering` ⇒ **impossible** sans réécriture complète, pas simplement « optionnel ». Seul `RiskDashboardPresenter` (texte) est réutilisable. |
| Dataset | **YES** | **YES** | Réserve : effet « forming bar » (§12.3a) |
| Live/Replay detection | **YES** (`ATASEquityReplayDetector`) | **N/A** | Ligne ajoutée — `IsReplay = false` toujours |
| Pipeline tracing | YES | YES | `PipelineTraceCollector` est pur |
| Look-ahead protection | **IMPLICITE** (temps réel) | **EXPLICITE (à construire)** | Ligne ajoutée — c'est la différence de nature la plus importante entre les deux modes |

### 16.2 Ce que la matrice démontre

Sur 22 fonctions, **7 sont strictement identiques** (Regime, Decision, Entry, EntryTrigger, TradePlan-code, RiskEngine, Dataset). Ces 7 constituent **l'intégralité du calcul scientifique et du risque**. Toutes les différences sont concentrées dans l'ingestion (amont) et l'exécution/comptabilité (aval).

**C'est exactement la propriété que le brief exige** : « L'objectif est que les calculs scientifiques soient communs. » Elle est déjà vraie dans le code — il ne reste qu'à construire les deux extrémités.

---

## 17. ARCHITECTURE RISKS

### 17.1 CRITICAL

**RISK-01 — Duplication du pipeline (« deuxième IQIA »)**
*Constat* : le harness `Tests/Research/StopLossCalibration` a déjà reproduit une partie du pipeline — `BarMetricsComputer.cs:61` fixe `Winner = MeanReverting, Confidence = 1.0, AmbiguityScore = 0.0`, contournant Regime/Fusion/Decision ; `CalibrationEntryBuilder` réimplémente la règle de direction d'`EntryTriggerBuilder` (`SignalDirection.cs` : « *deliberately distinct … restatement of that existing rule* »). C'était justifié pour une étude ciblée ; ce serait fatal pour un backtest censé prédire la production.
*Impact* : un backtest qui diverge du live n'a aucune valeur.
*Mitigation* : le `BacktestEngine` appelle les moteurs **réels**, sans exception. Test R-02 (rejeu de capture ATAS) comme garde-fou permanent.

**RISK-02 — Aucun stop-loss en production**
*Constat* : `TradePlanContext.RiskParameters == null` ⇒ `StopLoss == null` ⇒ `RiskEngine` rejette avec `INVALID_STOP_LOSS`, toujours.
*Impact* : le backtest, sans injection, produit zéro trade ; **et le système live ne peut aujourd'hui accepter aucun trade**.
*Mitigation* : injecter `TradeRiskParameters` via le point d'extension **existant**. La valeur relève de la calibration A1 — hors périmètre du LOT 14.

**RISK-03 — Couplage `IndicatorCandle` à l'entrée du pipeline**
*Constat* : §4.2.
*Impact* : sans traitement, aucune donnée historique n'entre dans le pipeline.
*Mitigation* : construire `Core.MarketContext` directement (possible sans modification) ; idéalement via la fabrique partagée de §6.3.

### 17.2 HIGH

**RISK-04 — `DateTime.UtcNow` dans le pipeline**
*Constat* : 26 occurrences hors tests, dont `TradePlanBuilder.cs:41,157`, `EntryTriggerBuilder.cs:102,110`, `EntryEngine.cs:21`, `MethodologyEngine.cs:18`, `VisualizationEngine.cs:20`, `ChartAnnotationBuilder.cs:54,100,155`.
*Impact* : (a) les `Timestamp` des artefacts ne correspondent pas au temps de la barre ; (b) deux runs identiques produisent des objets différents sur ces champs.
*Mitigation sans modification* : le backtest **ignore** ces champs et n'utilise que `context.Clock.CurrentTime` — comportement déjà appliqué par `ScientificDatasetRecord.From`. À **documenter comme contrat**, car un futur développeur pourrait naturellement lire `TradePlan.Timestamp`.
*Note* : le harness a déjà le même défaut (`BarMetricsComputer.cs:66`), signalé au rapport 15.16 §3.

**RISK-05 — Non-déterminisme par `Guid.NewGuid()`**
*Constat* : `IQIAIndicator.cs:532` `EvaluationId = Guid.NewGuid()` (dans `FusionContext`) ; `:480` (trace) ; `:464` (SessionId).
*Impact* : deux runs identiques produisent des identifiants différents ⇒ comparaison bit-exacte impossible sur les objets complets.
*Vérification effectuée* : `EvaluationId` n'est lu par **aucune** règle de fusion ni décision — il n'influence aucun calcul.
*Mitigation* : identifiants dérivés déterministes en backtest (ex. `SessionId` figé par scénario), ou exclusion explicite du périmètre de comparaison des tests S-03/S-06.

**RISK-06 — État statique mutable dans `KalmanFilterModel`**
*Constat* : `DiagnosticsEnabled` (`static bool`, ligne 60) et `_diagnostics` (`static List`, ligne 66, capacité 10, **non thread-safe**).
*Impact* : interdit d'exécuter plusieurs backtests **en parallèle** avec diagnostics actifs ; risque de corruption de la liste.
*Atténuation existante* : par conception, ces champs n'influencent aucun calcul, score ni métrique (doc de classe).
*Mitigation* : `DiagnosticsEnabled = false` en backtest (défaut), ou exécution strictement séquentielle.

**RISK-07 — Divergence Backtest/ATAS sur la construction du contexte**
*Constat* : sans la fabrique partagée (§6.3), les formules `Median`, `TypicalPrice`, `ElapsedMinutes`, `IsFirstBar` existeraient en deux exemplaires.
*Impact* : une correction appliquée d'un seul côté crée une divergence silencieuse, indétectable sans R-02.
*Mitigation* : appliquer §6.3 ; à défaut, exiger R-02 dans la CI.

**RISK-08 — `MarketClock.Session` jamais peuplé**
*Constat* : `MarketContextBuilder.cs:123` écrit des valeurs vides ; aucun consommateur n'existe.
*Impact* : le backtest n'a aucun moyen de filtrer overnight/RTH, ni de gérer les changements de session — sur données Yahoo journalières ou intraday, c'est une différence de nature avec le live.
*Mitigation LOT 14* : **reproduire exactement le comportement actuel** (session vide). Créer une notion de session serait une divergence, pas une amélioration, tant que le live ne l'a pas.

**RISK-09 — Assembly unique dépendant d'ATAS**
*Constat* : §4.3.
*Impact* : impossible de compiler/tester le backtest sur une CI sans ATAS.
*Mitigation* : différée (§6.4) — non bloquante.

### 17.3 MEDIUM

**RISK-10 — État inter-barres non réinitialisable dans `FusionStateManager`**
*Constat* : aucun `Reset()`, aucune sensibilité à `IsFirstBar`.
*Impact* : fuite d'état entre fenêtres si l'instance est partagée (L-5).
*Mitigation* : instance neuve par run (§13.4) — la règle est simple, mais elle doit être **écrite**, car rien dans le code ne l'impose.

**RISK-11 — Effet « forming bar » du collector**
*Constat* : §12.3(a) — la dernière barre est systématiquement exclue.
*Impact* : décompte de barres inattendu ; test I-08 doit l'anticiper.
*Mitigation* : documenter ; ne pas modifier le collector.

**RISK-12 — Fenêtre scientifique fixée à 500**
*Constat* : `const int historyWindow = 500` (`IQIAIndicator.cs:1228`), **valeur littérale dans une méthode privée de la classe hôte**.
*Impact* : si le backtest choisit une autre valeur, `KalmanFilterModel` (fenêtre 20) reste inchangé mais `VolatilityModel.ComputeReferenceVolatility`/`ComputeVolatilityPercentile` **parcourent tout l'historique fourni** ⇒ résultats différents.
*Mitigation* : **le backtest doit utiliser exactement 500.** C'est une constante de comportement, pas un réglage. À exposer comme constante partagée si §6.3 est appliqué.

**RISK-13 — Dépendance de `ScientificDatasetRecord` à `Infrastructure.ATAS`**
*Constat* : `using` ligne 13 pour deux `record` purs.
*Impact* : empêche de déplacer le dataset dans un assembly Core sans ATAS.
*Mitigation* : au moment du split (§6.4) uniquement.

**RISK-14 — Absence de notion de roll de contrat**
*Constat* : aucune gestion de roulement futures nulle part ; `RealMarketQualityAnalyzer` liste « contract-roll events » comme dimension de qualité **à auditer**, sans traitement.
*Impact* : sur ES/MES multi-trimestres, un roll crée un saut de prix qui sera interprété comme un événement de marché (`StructuralBreak`, `Cusum`, `BaiPerron` réagiront).
*Mitigation* : la source historique doit documenter sa politique (continu ajusté / non ajusté / par contrat) ; le moteur n'invente rien.

### 17.4 LOW

**RISK-15 — `MarketCache` vide** : placeholder Sprint 1 jamais rempli ; sans effet, mais peut induire en erreur.
**RISK-16 — Deux classes `EvidenceFusionEngine`** : `Engine/Fusion/` et `Engine/Regime/Core/`, désambiguïsées par alias dans `IQIAIndicator.cs:37`. Piège de lisibilité pour l'implémenteur du LOT 14.
**RISK-17 — Deux classes `MarketContext`** : `Core` et `ScientificModels.Abstractions`, plus une classe `IQIAIndicator` homonyme de son namespace (d'où les `global::` dans `ScientificDatasetRecord.cs:79-84`). Le code du backtest devra appliquer la même discipline d'alias.
**RISK-18 — Modèles stubs câblables par erreur** : `BOCPDModel` et `TimeSeriesMomentumModel` retournent `Success=false` inconditionnellement ; `ScientificModelRegistry` les exclut délibérément. Un backtest sur un régime ≠ MeanReverting obtiendra **zéro modèle scientifique** et donc zéro signal — comportement correct mais surprenant, **à rapporter explicitement** dans les statistiques (« barres sans couverture méthodologique »).

### 17.5 Synthèse

| Sévérité | Nombre | Bloquants pour le LOT 14 |
|---|---|---|
| CRITICAL | 3 | RISK-03 (traité par conception) ; RISK-02 (traité par injection) ; RISK-01 (traité par discipline + R-02) |
| HIGH | 6 | Aucun bloquant ; RISK-09 différé |
| MEDIUM | 5 | Aucun |
| LOW | 4 | Aucun |

**Aucun risque n'interdit de démarrer le LOT 14.**

---

## 18. RECOMMENDED IMPLEMENTATION ORDER

Ordre conçu pour que **chaque étape soit vérifiable seule** et n'invalide jamais la précédente.

| Phase | Contenu | Livrable vérifiable | Touche du code existant ? |
|---|---|---|---|
| **P0** | `MarketContextFactory` partagée (§6.3) | Suites existantes toujours vertes ; comportement inchangé | **OUI** — `MarketContextBuilder.cs` uniquement (non protégé) |
| **P1** | `HistoricalBar`, `HistoricalSeries`, validation d'ingestion | U-01..U-05 ; une série CSV se charge et se valide | NON |
| **P2** | `IHistoricalBarSource` + adapter CSV (rejouant `RawCapture/Sprint15_18`) | Une capture ATAS réelle se charge | NON |
| **P3** | Boucle de barres nue : `HistoricalSeries` → `MarketContext` → Regime → Decision → SignalEngine, **sans risque ni exécution** | I-01 ; **et surtout R-02** : les `Decision.Winner`/`EntryTrigger.Direction` rejoués correspondent à ceux enregistrés par ATAS | NON |
| **P4** | `InstrumentSpecificationCatalog` (§10) | U-17 ; ES et MES résolus depuis configuration | NON |
| **P5** | `ISimulatedAccount` (§9) | U-15, U-16 | NON |
| **P6** | Branchement `TradeRiskParameters` + `RiskEngine` (aucune exécution encore) | I-02, I-05 : `RiskAssessment` produits, `ACCEPTED` observés | NON |
| **P7** | `ICostModel` + `IExecutionModel` + positions + PnL/MFE/MAE | U-08..U-14, I-03, I-04, I-06 | NON |
| **P8** | `BacktestResult` + statistiques + `TradeLedger` + intégration dataset | I-08, R-01 | NON |
| **P9** | `BacktestWindow` / `BacktestWindowSet` (§14) | U-18, S-04, S-05 | NON |
| **P10** | Suite scientifique complète (S-01..S-08) | **Preuve d'absence de look-ahead** | NON |

**P3 est le jalon décisif.** S'il passe (les décisions rejouées correspondent à celles produites par ATAS sur la même capture), toute l'hypothèse de cet audit est empiriquement validée et le reste est de l'ingénierie sans risque architectural. S'il échoue, **il faut s'arrêter** et comprendre pourquoi avant d'écrire une ligne d'exécution simulée.

Une seule phase (P0) touche du code existant, et elle est optionnelle.

---

## 19. LOT 14 SPECIFICATION

### 19.1 Objectif

Construire un `BacktestEngine` qui exécute le pipeline IQIA **existant et non modifié** sur des données historiques, produisant trades, PnL, statistiques et dataset scientifique, sans créer un second pipeline.

### 19.2 Périmètre

**Inclus** :
- `Core/MarketData/` : `HistoricalBar`, `HistoricalSeries`, `HistoricalSeriesValidator`, `IHistoricalBarSource`
- `Backtest/` : `BacktestEngine`, `BacktestScenario`, `BacktestResult`, `BacktestTrade`, `BacktestWindow`, `BacktestWindowSet`, `SimulatedAccount`, `ExecutionModel`, `CostModel`, `InstrumentSpecificationCatalog`
- Adapter CSV (rejeu des captures ATAS existantes)
- Suite de tests (§15)
- **Optionnel** : `MarketContextFactory` (P0)

**Exclu explicitement** :
- Toute modification de : `RiskEngine`, `RiskPolicy`, `InstrumentRiskSpecification`, `RiskEngineRequest`, `RiskAssessment`, `DecisionArbitrator`, `EntryTriggerBuilder`, `TradePlanBuilder`
- Toute modification de `ScientificDatasetRecord`
- Toute modification du seuil `AmbiguityGateThreshold`
- L'adapter Yahoo (LOT 15)
- Le choix des valeurs de stop-loss, de coûts, de capital ou de policy
- Le split d'assembly (LOT 16+)
- Tout dashboard de backtest

### 19.3 Contrats normatifs du LOT 14

1. **Le moteur n'implémente aucun calcul scientifique.** Toute métrique vient d'une classe existante.
2. **Signal(i) ne lit que `Bars[0..i]`.** Une seule fonction lit le futur : le modèle d'exécution/mesure d'issue, et jamais en retour vers le signal.
3. **Une instance de chaque moteur par run.** Aucune instance partagée entre fenêtres.
4. **Le temps est `HistoricalBar.Timestamp`.** `DateTime.UtcNow` interdit dans le code du moteur.
5. **Aucune valeur inventée.** Capital, coûts, policy, spécification d'instrument, stop-loss : tous fournis par le scénario ; absence ⇒ échec explicite, jamais un défaut silencieux.
6. **`RiskAssessment.REJECTED` n'ouvre jamais de position.**
7. **Fenêtre scientifique = 500 closes**, identique à la production (RISK-12).
8. **Chaque `BacktestResult` porte son scénario complet** (sérialisé + haché).
9. **Les catégories indécidables sont conservées**, jamais fusionnées (précédent `EntryFate.Undetermined`).

### 19.4 Critère d'acceptation du LOT 14

> Rejouer `RealMarket/RawCapture/Sprint15_18` via le `BacktestEngine` et obtenir, barre par barre, les **mêmes** `Decision.Winner`, `EntryTrigger.Direction`, `EntryTrigger.TriggerStatus` et `TradePlan.Status` que ceux enregistrés par ATAS dans le dataset scientifique correspondant.

C'est la seule preuve qui établit qu'il n'existe qu'un seul IQIA.

---

## 20. FINAL VERDICT

### ARCHITECTURE STATUS

**PARTIALLY READY**

Le pipeline scientifique et le moteur de risque sont prêts à l'emploi, non modifiés, dès aujourd'hui (187 fichiers sur 203 sans aucune dépendance ATAS). Les deux extrémités — ingestion historique et exécution simulée — n'existent pas et doivent être construites. Aucune reconstruction, aucun refactoring de l'existant n'est nécessaire.

### ATAS DECOUPLING

**GOOD** pour la couche de calcul · **PARTIAL** pour l'ingestion et la compilation

- Calcul (Regime → Decision → Entry → EntryTrigger → TradePlan → Risk) : **GOOD** — zéro dépendance ATAS, déjà exercé hors ATAS par les suites de tests existantes.
- Ingestion : **PARTIAL** — un seul point de couplage (`Func<int, IndicatorCandle>`), contournable sans modification.
- Compilation : **PARTIAL** — assembly unique référençant ATAS par `HintPath` (RISK-09, différable).
- Compte et instrument : **PARTIAL** — alimentés par ATAS aujourd'hui, mais via des adaptateurs isolés dont les types de sortie sont purs.
- Rendu : **POOR** — et sans importance, car non requis en backtest.

### BACKTEST FEASIBILITY

**YES**

Quatre briques additives suffisent (source historique, compte simulé, modèle d'exécution, modèle de coûts), plus un catalogue d'instruments et un découpage en fenêtres. Aucun composant protégé n'a besoin d'être modifié. Une seule modification recommandée (`MarketContextFactory`) et elle est optionnelle. Le précédent `Tests/Research/StopLossCalibration` démontre déjà empiriquement que les modèles scientifiques réels tournent hors ATAS avec une garantie anti-look-ahead testée.

### REQUIRED CHANGES

Changements nécessaires au LOT 14, par ordre de nécessité :

| # | Changement | Type | Obligatoire |
|---|---|---|---|
| 1 | `HistoricalBar` + `HistoricalSeries` + validation | **AJOUT** | OUI |
| 2 | `IHistoricalBarSource` + adapter CSV | **AJOUT** | OUI |
| 3 | Construction de `Core.MarketContext` depuis `HistoricalBar` | **AJOUT** | OUI |
| 4 | Réimplémentation de la fenêtre scientifique 500 closes côté backtest | **AJOUT** | OUI |
| 5 | `InstrumentSpecificationCatalog` (data-driven, ES/MES) | **AJOUT** | OUI |
| 6 | `SimulatedAccount` produisant un `AccountState` par barre | **AJOUT** | OUI |
| 7 | `ICostModel` + `IExecutionModel` | **AJOUT** | OUI |
| 8 | `BacktestEngine` + `BacktestScenario` + `BacktestResult` + `BacktestTrade` | **AJOUT** | OUI |
| 9 | `BacktestWindow` / `BacktestWindowSet` | **AJOUT** | OUI |
| 10 | Injection de `TradeRiskParameters` (point d'extension **existant**) | **UTILISATION** | OUI |
| 11 | `MarketContextFactory` extraite de `MarketContextBuilder` | **MODIFICATION** (1 fichier non protégé) | **NON — recommandé** |
| 12 | Split d'assembly Core/ATAS | **MODIFICATION** (structurelle) | **NON — différé LOT 16+** |
| 13 | Correction de `DateTime.UtcNow` dans le pipeline | **MODIFICATION** (fichiers protégés) | **NON — contourné par contrat** |
| 14 | Ajout de PnL/MFE/MAE à `ScientificDatasetRecord` | **MODIFICATION** | **NON — `TradeLedger` séparé** |

**10 ajouts purs, 1 modification recommandée non bloquante, 3 modifications explicitement écartées.**

### PROTECTED COMPONENTS

Vérification par `git status` — aucun de ces fichiers n'apparaît comme modifié :

| Composant | Fichier | État |
|---|---|---|
| `RiskEngine` | `Engine/Risk/RiskEngine.cs` | **NON MODIFIÉ** |
| `RiskPolicy` | `Engine/Risk/RiskPolicy.cs` | **NON MODIFIÉ** |
| `InstrumentRiskSpecification` | `Engine/Risk/InstrumentRiskSpecification.cs` | **NON MODIFIÉ** |
| `RiskEngineRequest` | `Engine/Risk/RiskEngineRequest.cs` | **NON MODIFIÉ** |
| `RiskAssessment` | `Engine/Risk/RiskAssessment.cs` | **NON MODIFIÉ** |
| `DecisionArbitrator` | `Engine/Decision/Arbitration/DecisionArbitrator.cs` | **NON MODIFIÉ** |
| `EntryTriggerBuilder` | `Engine/EntryTrigger/EntryTriggerBuilder.cs` | **NON MODIFIÉ** |
| `TradePlanBuilder` | `Engine/TradePlan/TradePlanBuilder.cs` | **NON MODIFIÉ** |

Également non modifiés : seuil `AmbiguityGateThreshold = 0.95`, tous les datasets de `Tests/Research/StopLossCalibration/Output/`, `ScientificDatasetRecord`, et l'ensemble des fichiers de production.

### FILES MODIFIED

**Créé par ce lot — un seul fichier :**

```
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot13_Backtest_Architecture_Audit_Report.md
```

**Modifications préexistantes du working tree, antérieures à ce lot et non touchées par lui** (constatées en début d'audit, `git status`) :

```
 M IQIAIndicator/Engine/ScientificModels/Context/VolatilityModel.cs        (Lot B2 - commentaires XML uniquement, +45 lignes, aucun changement de comportement)
 M IQIAIndicator/Tests/Research/StopLossCalibration/Output/A1_calibration_summary.txt
 M IQIAIndicator/Tests/Research/StopLossCalibration/Output/campaign_summary.txt
?? IQIAIndicator/Tests/ScientificModels/VolatilityModelCausalityTests.cs
?? IQIAIndicator/Tests/XunitWrappers/VolatilityModelCausalityXunitTests.cs
```

### BUILD

**NON LANCÉ.**

### TESTS

**NON LANCÉS.**

### DLL

**NON GÉNÉRÉE.**

### ATAS

**NON LANCÉ.**

### GIT

**AUCUN COMMIT.**

---

## STOP — FIN DU LOT 13
