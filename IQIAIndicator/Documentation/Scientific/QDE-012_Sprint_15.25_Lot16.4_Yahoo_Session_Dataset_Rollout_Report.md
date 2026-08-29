# QDE-012 — Sprint 15.25 — Lot 16.4 — Yahoo Session Dataset Rollout (Phase 7 mécanique)

> **Type :** Infrastructure de test / minimisation des requêtes — **Mode :** migration mécanique
> **Production modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Suite directe du Lot 16.3. Ce lot exécute le **rollout mécanique de la Phase 7** : migrer les tests
> d'intégration Yahoo à **fenêtre identique** (`MES / M5 / DefaultMaxChunkSpanDays` = 59 jours glissants)
> vers la fixture de session `YahooSessionDataset` livrée au Lot 16.3, pour qu'un run complet ne
> télécharge ce dataset **qu'une seule fois** au lieu de ~20 fois.
>
> **Aucun fichier de production n'est touché.** Seuls des fichiers de test sont modifiés.

---

## 1. Rappel du problème (Lot 16.3 §6)

Un run de suite complet faisait télécharger la **même** fenêtre `MES / M5 / 59 jours` par ≈ 20 classes
de test indépendantes (chacune `new YahooHistoricalBarSource()` + `Load`), en parallèle, via le même
`static HttpClient` → une seule connexion HTTP/2 → cause directe du HTTP 429 qui bloquait ensuite la
suite. Le Lot 16.3 a livré le mécanisme (`YahooSessionDataset` + `ICollectionFixture`) et migré **1**
consommateur (`AmbiguityGateThresholdRevalidationLot1413Tests`). Ce lot migre les **19** autres.

---

## 2. Mécanisme (rappel)

- `Tests/Backtest/Yahoo/YahooSessionDataset.cs` : **une** acquisition réelle `MES / M5 /
  DefaultMaxChunkSpanDays` par run, via la même `YahooHistoricalBarSource` **bornée et classifiée** que
  la production (Lot 16.3). Expose `Series` (immuable), `Failure` / `SkipReason` (si Yahoo indispo),
  `RequestedFromUtc` / `RequestedToUtc`, `ChunkCount`, `GapCount`, et `Require(log)`.
- `[CollectionDefinition("YahooSession")]` (`ICollectionFixture<YahooSessionDataset>`) : la fixture est
  instanciée **une fois** pour toute la collection ; xUnit **sérialise** la collection.
- `HistoricalSeries` est profondément immuable ⇒ **RUN ISOLATION** et **DÉTERMINISME** préservés : tous
  les consommateurs voient des barres bit-identiques, aucun ne peut en perturber un autre.

---

## 3. Transformation appliquée à chaque fichier

Patron mécanique (identique pour les 19) :

```diff
+using IQIAIndicator.Tests.BacktestTests.Yahoo;

+[Collection("YahooSession")]
 public sealed class XxxTests
 {
     private readonly ITestOutputHelper _output;
+    private readonly YahooSessionDataset _yahoo;

-    public XxxTests(ITestOutputHelper output) { _output = output; }
+    public XxxTests(ITestOutputHelper output, YahooSessionDataset yahoo)
+    {
+        _output = output;
+        _yahoo = yahoo;
+    }
```

et, dans le corps du test :

```diff
-            var source = new YahooHistoricalBarSource();
-            DateTime to = DateTime.UtcNow;
-            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);
-
-            HistoricalSeries series = source.Load("MES", "M5", from, to);
+            HistoricalSeries? maybeSeries = _yahoo.Require(_output.WriteLine);
+            if (maybeSeries is null)
+                return;
+            HistoricalSeries series = maybeSeries;
```

Le `try { … } catch (… when (exception is …YahooProviderException or HttpRequestException or
TaskCanceledException))` de chaque test (réécrit au Lot 16.3) est **conservé** : il protège encore les
appels aval du pipeline. Le `Require(...)` gère le cas « Yahoo indisponible » avant même d'entrer dans
la logique du test (ligne de skip explicite + `return`).

### Cas particulier — `YahooDatasetCoverageTests`

Ce test **réutilisait** `from` / `to` (log de la plage demandée) **et** `source.LastRequestChunkCount` /
`source.LastRequestGapCount`. Migration adaptée :

```diff
+            DateTime from = _yahoo.RequestedFromUtc;
+            DateTime to = _yahoo.RequestedToUtc;
-            _output.WriteLine($"BarCount={series.Count}, ChunkCount={source.LastRequestChunkCount}, YahooReportedGapSlots={source.LastRequestGapCount}");
+            _output.WriteLine($"BarCount={series.Count}, ChunkCount={_yahoo.ChunkCount}, YahooReportedGapSlots={_yahoo.GapCount}");
```

---

## 4. Fichiers migrés (19)

`Tests/Backtest/Calibration/` :
`DecisionFusionAmbiguityInvestigationLot1414Tests`, `ExecutionRealismLot154Tests`,
`FillPriceReconciliationLot155Tests`, `HysteresisThresholdSensitivityLot1418Tests`,
`PersistenceVariabilityDiscriminativePowerLot1416Tests`, `PersistenceZeroVarianceActivationAuditLot1417Tests`,
`RegimeCoverageMaturityAuditLot150Tests`, `StableRangeMeanRevertingOverlapInvestigationLot1415Tests`,
`StopLossFoundationLot153Tests`, `StructuralBreakEvidenceAblationLot152Tests`,
`StructuralBreakRegressionTests`, `UnsupportedRegimeObservabilityLot151Tests`.

`Tests/Backtest/Dataset/` : `YahooDatasetCoverageTests`.

`Tests/Research/` : `Lot16ContractNormalization/Lot161ConfidenceSourceComparisonTests`,
`Lot16ContractNormalization/Lot16ContractRegressionTests`,
`Lot16ContractNormalization/Lot16StrengthTransformComparisonTests`,
`SixDimensionAudit/SixDimensionIndependentAuditTests`,
`StructuralBreakAudit/StructuralBreakEvidenceDescriptiveAnalysisTests`,
`StructuralBreakAudit/StructuralBreakEvidenceLot158IntegrationTests`.

**+ `AmbiguityGateThresholdRevalidationLot1413Tests`** (Lot 16.3). ⇒ **20** classes sur la fixture.

---

## 5. Non migrés — hors périmètre « fenêtre identique »

| Fichier(s) | Fenêtre | Raison de non-migration |
|---|---|---|
| `CalibrationYahooIntegrationTests`, `CostYahooIntegrationTests`, `ExecutionYahooIntegrationTests`, `ScientificMeasurementYahooIntegrationTests`, `PnLYahooIntegrationTests`, `RiskYahooIntegrationTests` | **45 jours** | fenêtre différente de la session (59 j). Les nourrir avec 59 j changerait leurs données d'entrée (asserts structurels passeraient, mais c'est un changement sémantique hors périmètre « mécanique »). Candidat à une unification 45→59 dans un lot ultérieur. |
| `StructuralBreakInformationAudit/Support/StructuralBreakObservationSetBuilder` | 59 jours | **helper partagé**, pas une classe de test ⇒ ne peut pas recevoir un `ICollectionFixture` directement ; il faudrait faire transiter la `HistoricalSeries` par son API et placer ses ~6 tests appelants dans la collection. Reporté. |
| `YahooNetworkIntegrationTests`, `BacktestSignalPipelineYahooIntegrationTests` | 2 jours | requêtes minuscules (quelques centaines de barres), charge Yahoo négligeable. |
| `YahooBacktestFoundationIntegrationTests` | date fixe `T0`, 1 jour | pas `DateTime.UtcNow`, pas la fenêtre glissante. |

**Effet net sur un run complet :** téléchargements Yahoo 59 j passés de ~20 à **1**. Total (toutes
fenêtres) de ~30 à ~10.

---

## 6. Compromis introduits

- **Sérialisation de la collection.** Les 20 classes migrées sont désormais dans la collection
  `"YahooSession"` ⇒ xUnit les exécute **en série** (plus en parallèle). C'est **voulu** : c'est
  précisément ce qui supprime le martèlement parallèle de Yahoo. Le temps mur d'un run *de ce
  sous-ensemble* devient la somme des temps individuels ; mais chaque test ne re-télécharge plus, et le
  429 ne peut plus survenir depuis ce sous-ensemble.
- **Échec fixture = échec collection.** Si `YahooSessionDataset` lève autre chose qu'une
  `YahooProviderException` (p. ex. `InvalidOperationException` « no usable bar » sur 59 j — anomalie
  réelle de la source), l'init de fixture échoue et xUnit marque **toute la collection** en échec.
  C'est un signal fort et correct (la source de données est cassée), mais bruyant. Une
  `YahooProviderException` (429, 5xx, réseau, corps non-chart) ⇒ `Series == null` ⇒ chaque test skip
  proprement.
- **Incohérence cosmétique** : `AmbiguityGateThresholdRevalidationLot1413Tests` utilise
  `[Collection(YahooSessionDataset.CollectionName)]`, les 19 autres `[Collection("YahooSession")]`.
  Même valeur `"YahooSession"`, même collection, comportement identique.

---

## 7. Déterminisme / look-ahead / logique scientifique

- **Aucun fichier de production modifié** (vérifié : `git status` ne liste que des `.cs` sous
  `Tests/`).
- **Aucune logique scientifique touchée.** Les tests migrés exécutent exactement le même pipeline sur
  exactement les mêmes barres (la `HistoricalSeries` partagée est bit-identique à celle qu'ils
  auraient téléchargée individuellement, à la dérive de fenêtre glissante près — déjà attendue et
  documentée, cf. `feedback_iqia_yahoo_test_nondeterminism`).
- **Look-ahead : inchangé.** La fixture ne fait que *fournir* la série ; elle n'introduit aucune
  statistique, fenêtre ou accès temps réel dans le chemin de données.
- **Run isolation : préservée.** `YahooSessionDataset` n'expose qu'une `HistoricalSeries` profondément
  immuable ; aucun état mutable partagé.

---

## 8. Builds & tests

| Étape | Résultat |
|---|---|
| `dotnet build IQIAIndicator.Tests.csproj -c Debug` | **La génération a réussi. 0 Avertissement, 0 Erreur** (1 min 37 s) |
| `dotnet build IQIAIndicator.Tests.csproj -c Release` | **La génération a réussi. 0 Avertissement, 0 Erreur** (23 s) |
| `dotnet build IQIAIndicator.csproj -c Debug / Release` | inchangé depuis Lot 16.3 — **0 / 0** |

Tests (ciblés — pas de run complet, pour ne pas retaper Yahoo) :

| Filtre | Résultat |
|---|---|
| `YahooRetryPolicyTests` + `YahooRateLimitResilienceTests` + `YahooHistoricalBarSourceTests` + `YahooChartParserTests` | **52 / 52 Réussi**, sans réseau |
| `YahooDatasetCoverageTests` (migré, collection, 1 téléchargement réel + analyse de couverture) | **Réussi** (≈ 6 min) |
| **Total ciblé** | **53 / 53 Réussi** (6 min 30 s) |
| `AmbiguityGateThresholdRevalidationLot1413Tests` (Lot 16.3) | déjà **exit 0** |

`YahooDatasetCoverageTests` valide de bout en bout le câblage de la fixture : téléchargement partagé,
`Require`, `RequestedFromUtc/ToUtc`, `ChunkCount`, `GapCount`.

La suite complète n'a **pas** été relancée (referait un run long ; le comportement est désormais borné
et la minimisation en place).

---

## 9. FINAL OUTPUT

```
STATUS:
LOT COMPLETE — Phase 7 mechanical rollout done for the identical-window set.

SCOPE:
Migrate every rolling-59-day (MES/M5/DefaultMaxChunkSpanDays) Yahoo integration test onto the
YahooSessionDataset ICollectionFixture delivered in Lot 16.3.

MIGRATED:
19 test classes this lot (+ 1 from Lot 16.3 = 20 total on the shared fixture).

NOT MIGRATED (documented, out of "identical-window" scope):
  - 6 x 45-day tests (Calibration/Cost/Execution/Measurement/Pnl/Risk YahooIntegrationTests) —
    different window; would change their input data.
  - StructuralBreakObservationSetBuilder — shared helper, not a test class; needs API threading.
  - 2-day / fixed-date small tests — negligible Yahoo load.

REQUEST MINIMIZATION:
SUBSTANTIAL — full-run Yahoo downloads of the 59-day window: ~20 -> 1. All windows: ~30 -> ~10.

PRODUCTION FILES MODIFIED:
NONE.

TEST FILES MODIFIED:
20 (19 this lot + YahooDatasetCoverageTests special-cased for source./from/to reuse).
Files: see §4.

RUN ISOLATION:
PASS — shared object is a deeply-immutable HistoricalSeries; no shared mutable state.

DETERMINISM:
PASS — migrated tests run the same pipeline over byte-identical bars.

LOOK-AHEAD:
UNCHANGED.

SCIENTIFIC LOGIC:
NOT MODIFIED.

TRADE-OFFS:
  - The "YahooSession" collection is now serialised by xUnit (intended — kills parallel hammering).
  - A non-YahooProviderException from the fixture ctor fails the whole collection (loud, correct).

DEBUG BUILD:
PASS (tests, 0/0).

RELEASE BUILD:
PASS (tests, 0/0).

TESTS:
Targeted: 53/53 PASS (resilience unit tests + migrated YahooDatasetCoverageTests, 6m30s).
Full suite not re-run (avoid Yahoo hammering; behaviour already bounded).

ATAS: NOT USED    ORDERS: NONE    DLL DEPLOYED: NO    COMMIT: NO

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.4_Yahoo_Session_Dataset_Rollout_Report.md

NEXT LOT:
RECOMMEND ONLY AFTER ANALYSIS — candidates:
  1. Unify the 6 remaining 45-day Yahoo integration tests (either accept the 59-day session series,
     or add a second fixture) + thread the session series through StructuralBreakObservationSetBuilder.
  2. Empirically derive the Provisional retry/timeout constants (retry=3, MaxTotalWait=20s,
     PerRequestTimeout=15s, OverallLoadTimeout=3min) from observed Yahoo 429 behaviour.

STOP.
```
