# QDE-012 — Sprint 15.25 (Lot 12.5) — ATAS Raw Risk Telemetry / MES Diagnostic

**Lot d'instrumentation uniquement.** Aucun Replay, aucun lancement d'ATAS, aucune DLL déployée, aucun
commit. Le Risk Engine n'a pas été modifié — l'objectif est de le rendre **observable**, pas de le rendre
`ACCEPTED`.

---

## 1. Objectif

Le Lot 12.4 a établi que les valeurs ATAS brutes de diagnostic (`ATAS.FinalQuantityStep`,
`ATAS.RealtimeEquity`/`ReplayEquity`, `ATAS.InvalidFieldReasons`, etc.) sont calculées chaque bar par le
Risk stage (`ATASRuntimeDiagnostics`, Lot 12.3) mais ne survivent jamais jusqu'au dataset exporté :
`ScientificDatasetRecord.From` ne lisait, pour chaque `PipelineTraceEvent`, que
`traceEvent.Elapsed.TotalMilliseconds` — jamais `traceEvent.Details`, où vivent ces valeurs.

Ce lot comble exactement cet écart : conserver dans le dataset scientifique les valeurs brutes ATAS, les
valeurs après adaptation, et les valeurs reçues par le Risk Engine, à trois niveaux explicitement
distincts, pour permettre un diagnostic complet sur la prochaine capture MES sans avoir à modifier le
Risk Engine lui-même.

---

## 2. Périmètre

Vérifié par git avant et après modification (Section 11 du lot) :

**Modifiés par ce lot** (3 fichiers, tous explicitement autorisés) :
- `IQIAIndicator/Core/Calibration/ScientificDatasetRecord.cs`
- `IQIAIndicator/IQIAIndicator.cs`
- `IQIAIndicator/Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs`

**Non modifiés par ce lot** (vérifié explicitement — Section 15 ci-dessous) : `RiskEngine.cs`,
`RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequestFactory.cs`,
`ATASAccountStateAdapter.cs`, `ATASInstrumentAdapter.cs`, `EntryTriggerBuilder.cs`,
`DecisionArbitrator.cs`, `TradePlanBuilder.cs`, `TradePlan.cs`, aucun seuil `AmbiguityScore`, aucune
stratégie SL, aucun sizing, aucune logique ACCEPTED/REJECTED, aucun fallback, aucun mapping
MES/ES hardcodé.

**Aucune action interdite effectuée** : aucun Replay, aucun lancement ATAS, aucune DLL, aucun commit.

---

## 3. Architecture avant instrumentation

```
ATAS (Security/Portfolio/ITradingStatistics)
        │
        ▼
ATASAccountStateAdapter.Build / ATASInstrumentAdapter.Build      (Lot 12.2)
        │
        ▼
AccountState / InstrumentRiskSpecification    ── stockés (_latestRiskAccountState/_latestRiskInstrumentSpec)
        │
        ▼
RiskEngineRequestFactory.FromTradePlan                            (Lot 11)
        │
        ▼
RiskEngineRequest (variable LOCALE riskRequest - jamais stockée)
        │
        ▼
RiskEngine.Evaluate → RiskAssessment  ── stocké (_latestRiskAssessment)
        │
        ▼
ATASRuntimeDiagnostics.CaptureAccount/CaptureInstrument            (Lot 12.3)
        │
        ▼
ATASAccountDiagnostic / ATASInstrumentDiagnostic ── stockés (_latestATASAccountDiagnostic/_latestATASInstrumentDiagnostic)
        │
        ▼ (uniquement si EnablePipelineTracing = true)
PipelineTraceEvent.Details["ATAS.*"]   ── calculé, jamais lu en aval
        │
        ✗ ─── PERDU ICI (Lot 12.4) : ScientificDatasetRecord.From ne lit que traceEvent.Elapsed
        │
        ▼
ScientificDatasetRecord.Categories  (Risk.Status/RejectionReasons/PositionSize/... uniquement)
```

Seuls `Risk.Status`, `Risk.RejectionReasons`, `Risk.PositionSize`, `Risk.RiskAmount`, `Risk.RiskBudget`,
`Risk.RiskRewardRatio`, `TradePlan.*` (sans indicateur de disponibilité explicite pour StopLoss)
atteignaient le dataset. `RiskEngineRequest` lui-même n'était jamais retenu au-delà de sa portée locale.

---

## 4. Architecture après instrumentation

```
ATAS (Security/Portfolio/ITradingStatistics)                              [RAW_ATAS]
        │
        ├──────────────────────────────────────────────────────────────────────┐
        ▼                                                                       ▼
ATASAccountStateAdapter.Build / ATASInstrumentAdapter.Build      ATASRuntimeDiagnostics.CaptureAccount/
        │ (Lot 12.2, INCHANGÉ)                                    CaptureInstrument (Lot 12.3, INCHANGÉ)
        ▼                                                                       │
AccountState / InstrumentRiskSpecification            [ADAPTER]                │
        │                                                                       │
        ▼                                                                       │
RiskEngineRequestFactory.FromTradePlan (Lot 11, INCHANGÉ)                       │
        │                                                                       │
        ▼                                                                       │
RiskEngineRequest ── désormais STOCKÉ (_latestRiskEngineRequest, NOUVEAU)  [RISK_INPUT]
        │                                                                       │
        ▼                                                                       │
RiskEngine.Evaluate → RiskAssessment (Lot 10, INCHANGÉ)                         │
        │                                                                       │
        └──────────────────────┬──────────────────────────────────────────────┘
                                ▼
        ScientificDatasetRecord.From(..., atasAccountDiagnostic, atasInstrumentDiagnostic,
            atasEquityIsReplay, atasEquitySeriesCount, atasEquityLastTimestamp,
            riskAccountState, riskInstrumentSpec, riskEngineRequest)      [NOUVEAU - Lot 12.5]
                                │
                                ▼
        Categories["ATAS.Raw.*"] / ["ATAS.Adapter.*"] / ["ATAS.RiskInput.*"] / ["ATAS.Equity*"] /
        ["ATAS.QuantityStep*"] / ["TradePlan.StopLossAvailable"]   (39 nouvelles clés, additives)
```

`RiskEngine.Evaluate` n'a pas de nouvelle entrée dans ce schéma : il reste appelé exactement comme avant
(Lot 10/11, ligne inchangée) — seule la valeur déjà produite par `RiskEngineRequestFactory` est en plus
**retenue** (`_latestRiskEngineRequest = riskRequest;`, une simple affectation passive) pour être capturée
plus tard dans le même bar.

---

## 5. ATAS Account fields capturés

Réutilise intégralement `ATASAccountDiagnostic` (Lot 12.3, non modifié) — aucune nouvelle lecture ATAS,
uniquement un nouveau point d'export :

| Catégorie dataset | Champ source | Niveau |
|---|---|---|
| `ATAS.Raw.Account.AccountID` | `ATASAccountDiagnostic.AccountID` | RAW_ATAS |
| `ATAS.Raw.Account.IsRealAccount` | `.IsRealAccount` | RAW_ATAS |
| `ATAS.Raw.Account.Currency` | `.Currency` | RAW_ATAS |
| `ATAS.Raw.Account.Balance` | `.Balance` | RAW_ATAS |
| `ATAS.Raw.Account.BalanceAvailable` | `.BalanceAvailable` | RAW_ATAS |
| `ATAS.Raw.Account.BalancePower` | `.BalancePower` | RAW_ATAS |
| `ATAS.Raw.Account.OpenPnL` | `.OpenPnL` | RAW_ATAS |
| `ATAS.Raw.Account.ClosedPnL` | `.ClosedPnL` | RAW_ATAS |
| `ATAS.Raw.Account.TotalPnL` | `.TotalPnL` | RAW_ATAS |

Absent (`atasAccountDiagnostic is null`, ou champ individuellement `null`) → `"NOT AVAILABLE"`, jamais
une valeur déduite.

---

## 6. ATAS Equity fields capturés

Section la plus sensible du lot (interdiction explicite de déduire Equity de Balance ou d'InitialCapital) :

| Catégorie dataset | Source | Signification |
|---|---|---|
| `ATAS.Raw.Equity.SourceMode` | `context.Execution.IsReplay` (même flag que `ATASAccountStateAdapter.TryGetCurrentEquity`) | `"Replay"` / `"Realtime"` / `"NOT AVAILABLE"` |
| `ATAS.Raw.Equity.RealtimeValue` | `ATASAccountDiagnostic.RealtimeEquity` | dernier point de `TradingStatisticsProvider.Realtime.Equity` |
| `ATAS.Raw.Equity.ReplayValue` | `ATASAccountDiagnostic.ReplayEquity` | dernier point de `TradingStatisticsProvider.Replay.Equity` |
| `ATAS.Raw.Equity.SeriesCount` | **NOUVELLE lecture** (Section suivante) | nombre de points dans la série du mode actif ce bar |
| `ATAS.Raw.Equity.LastTimestamp` | **NOUVELLE lecture** | `EquityValue.Time` du dernier point de cette même série |
| `ATAS.EquityAvailable` | dérivé de `RealtimeValue`/`ReplayValue` selon `SourceMode` | `"True"`/`"False"` — nom littéral demandé par le lot |
| `ATAS.Equity` | idem | valeur ou `"NOT AVAILABLE"` — nom littéral demandé par le lot |

`ATAS.Raw.Equity.SeriesCount`/`LastTimestamp` sont la seule vraie **nouvelle lecture ATAS** de ce lot : une
seconde consultation, indépendante et passive, de `ITradingStatistics.Equity` (même sélection
Replay/Realtime que l'adaptateur), ajoutée directement dans `IQIAIndicator.cs` (jamais dans
`ATASAccountStateAdapter.cs`, protégé) pour exposer `.Count()` et `.Last().Time` — deux informations que
`TryGetCurrentEquity` (qui ne retourne que `.Last().Equity`) ne renvoie pas. `EquityValue.Time` a été
confirmé par réflexion .NET (méthode identique au Lot 12.4 : `AssemblyLoadContext` + résolveur de
dépendances, lecture seule, `C:\Program Files (x86)\ATAS Platform`) avant d'écrire une seule ligne de
code — jamais deviné.

Aucun calcul `CurrentEquity = Balance` ni `CurrentEquity = InitialCapital` n'existe où que ce soit dans le
code ajouté — vérifié par relecture directe (Section 15) et par test (`From_WithoutAtasTelemetryParams_ReportsNotAvailable`).

---

## 7. ATAS Instrument fields capturés

| Catégorie dataset | Champ source | Niveau |
|---|---|---|
| `ATAS.Raw.Instrument.Symbol` | `ATASInstrumentDiagnostic.Instrument` | RAW_ATAS |
| `ATAS.Raw.Instrument.TickSize` | `.TickSize` | RAW_ATAS |
| `ATAS.Raw.Instrument.TickCost` | `.TickCost` | RAW_ATAS |
| `ATAS.Raw.Instrument.LotSize` | `.LotSize` | RAW_ATAS |
| `ATAS.Raw.Instrument.LotMinSize` | `.LotMinSize` | RAW_ATAS |
| `ATAS.Raw.Instrument.LotMaxSize` | `.LotMaxSize` | RAW_ATAS |
| `ATAS.Raw.Instrument.Digits` | `.Digits` | RAW_ATAS |
| `ATAS.Raw.Instrument.BaseCurrency` | `.BaseCurrency` | RAW_ATAS |
| `ATAS.Raw.Instrument.QuoteCurrency` | `.QuoteCurrency` | RAW_ATAS |

`Instrument.Name` (demandé "si disponible" par le lot) : **NOT AVAILABLE FROM ATAS API** — ni le Lot 12.1
ni la ré-inspection indépendante du Lot 12.4 n'ont trouvé de propriété `Name` distincte de `.Instrument`
sur `ATAS.DataFeedsCore.Security`. Aucun code n'a été écrit pour la lire (l'écrire aurait été une
invention, pas une lecture).

Raw MinQuantity/MaxQuantity = `LotMinSize`/`LotMaxSize` ci-dessus (aucune propriété ATAS distincte
n'existe pour "quantité min/max d'ordre" séparément du lot - Lot 12.1/12.4).

---

## 8. QuantityStep observability

```
ATAS.QuantityStepAvailable = "False"     (constante — voir ci-dessous)
ATAS.QuantityStep          = "NOT AVAILABLE"
        │
        ▼
ATAS.Adapter.Instrument.QuantityStep = InstrumentRiskSpecification.QuantityStep
        │  (ATASInstrumentAdapter.Build - Lot 12.2, INCHANGÉ - passthrough du paramètre manuel)
        ▼
ATAS.RiskInput.Instrument.QuantityStep = RiskEngineRequest.Instrument.QuantityStep
        │  (RiskEngineRequestFactory - Lot 11, INCHANGÉ - jamais reconstruit)
        ▼
ATAS.RiskInput.Instrument.IsValid = RiskEngineRequest.Instrument.IsValid
        (réutilise InstrumentRiskSpecification.IsValid, Lot 10, INCHANGÉ - pas de nouvelle règle)
```

`ATAS.QuantityStepAvailable`/`ATAS.QuantityStep` sont des **constantes**, pas une lecture par bar : aucune
propriété `QuantityStep`/`VolumeStep`/`LotStep`/`ContractSize` n'existe sur
`ATAS.DataFeedsCore.Security` (confirmé Lot 12.1, re-confirmé indépendamment Lot 12.4 par réflexion sur
les 4 assemblies ATAS chargées intégralement/partiellement). C'est un fait sur la surface de l'API ATAS,
pas une observation par bar — documenté ici plutôt que fabriqué comme une "lecture" qui n'existe pas.

`ATAS.Adapter.Instrument.QuantityStep` et `ATAS.RiskInput.Instrument.QuantityStep` sont, eux, de vraies
observations par bar : la valeur réellement portée par `InstrumentRiskSpecification`/`RiskEngineRequest`
ce bar précis — c'est ce qui manquait pour clore le Verdict B du Lot 12.4 (`QuantityStep ATAS = X →
Adapter = X → RiskInput = X → validation = INVALID`, littéralement la question posée par ce lot).

Test dédié : `From_QuantityStep_IsPreservedEndToEnd_FromAdapterThroughRiskInput` (Section 12).

---

## 9. Adapter output observability

`ATAS.Adapter.*` capture `_latestRiskAccountState`/`_latestRiskInstrumentSpec` — les mêmes objets déjà
utilisés par le dashboard Risk Engine (Lot 12), calculés **inconditionnellement chaque bar** (Lot 12,
indépendamment de l'existence d'un TradePlan) :

| Catégorie | Source |
|---|---|
| `ATAS.Adapter.Account.InitialCapital` | `AccountState.InitialCapital` |
| `ATAS.Adapter.Account.CurrentEquity` | `AccountState.CurrentEquity` |
| `ATAS.Adapter.Account.CurrentBalance` | `AccountState.CurrentBalance` |
| `ATAS.Adapter.Instrument.Symbol` | `InstrumentRiskSpecification.Symbol` |
| `ATAS.Adapter.Instrument.TickSize` | `.TickSize` |
| `ATAS.Adapter.Instrument.TickValue` | `.TickValue` |
| `ATAS.Adapter.Instrument.PointValue` | `.PointValue` |
| `ATAS.Adapter.Instrument.QuantityStep` | `.QuantityStep` |
| `ATAS.Adapter.Instrument.MinQuantity` | `.MinQuantity` |
| `ATAS.Adapter.Instrument.MaxQuantity` | `.MaxQuantity` |
| `ATAS.Adapter.Instrument.IsValid` | `.IsValid` (propriété réutilisée, Lot 10) |

Toujours disponible dès qu'un bar a été traité (jamais "NOT AVAILABLE" en fonctionnement réel), sauf dans
les tests qui n'alimentent pas ces paramètres — exactement ce que
`From_WithoutAtasTelemetryParams_ReportsNotAvailable` vérifie.

---

## 10. RiskEngine input observability

`ATAS.RiskInput.*` capture `_latestRiskEngineRequest` — **nouveau** champ de ce lot, la même valeur locale
`riskRequest` que `RiskEngine.Evaluate` reçoit, simplement retenue au lieu d'être perdue à la fin de son
bloc `try`. Absent (`"NOT AVAILABLE"`/`ATAS.RiskInput.Present = "False"`) exactement quand
`RiskEngineRequestFactory.FromTradePlan` retourne `null` — le même filtre que `Risk.Status = "NOT
AVAILABLE"` utilise déjà.

| Catégorie | Source |
|---|---|
| `ATAS.RiskInput.Present` | `riskEngineRequest is not null` |
| `ATAS.RiskInput.Direction` | `.Direction` |
| `ATAS.RiskInput.EntryPrice` | `.EntryPrice` |
| `ATAS.RiskInput.StopLoss` | `.StopLoss` |
| `ATAS.RiskInput.TakeProfit` | `.TakeProfit` |
| `ATAS.RiskInput.Instrument.QuantityStep` / `MinQuantity` / `MaxQuantity` / `IsValid` | `.Instrument.*` |
| `ATAS.RiskInput.Account.CurrentEquity` / `InitialCapital` | `.Account.*` |

`RiskEngineRequestFactory.FromTradePlan` (Lot 11, non modifié) ne reconstruit jamais `Instrument`/`Account`
— ce sont les mêmes objets que `ATAS.Adapter.*`. Les deux niveaux resteront donc numériquement identiques
chaque fois que `RiskInput.Present = "True"` ; ce lot rend cela **observable** plutôt que supposé.

---

## 11. StopLoss observability

```
categories["TradePlan.StopLoss"]           (Lot 9, INCHANGÉ - valeur ou "NOT AVAILABLE")
categories["TradePlan.StopLossAvailable"]  (NOUVEAU - "True"/"False", lit uniquement tradePlan.StopLoss)
```

Aucune stratégie SL créée. Le seul ajout est un booléen explicite jumeau du champ existant, pour permettre
d'interroger sa disponibilité sans comparer une chaîne de caractères. Sur la prochaine capture, ce champ
permettra de confirmer directement (sans dépendre de `Risk.RejectionReasons`) que `INVALID_STOP_LOSS` et
`INSTRUMENT_SPEC_INVALID` sont bien deux constats indépendants côté TradePlan/Instrument.

---

## 12. Tests

9 nouveaux tests xUnit ajoutés dans `Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs`
(TEST 18, à la suite des TEST 16/17 des Lots 9/11 — même style, mêmes conventions de fixtures) :

| Test | Preuve apportée (Section 9 du brief) |
|---|---|
| `From_WithAtasTelemetryParams_DoesNotMutateThem` | aucune mutation des objets passés (9.1, mêmes garanties que TEST 16/17) |
| `From_WithAtasTelemetryParams_PopulatesRawAdapterAndRiskInputCategories` | une valeur ATAS disponible est exportée sans transformation (9.1), aux 3 niveaux |
| `From_WithoutAtasTelemetryParams_ReportsNotAvailable` | une valeur absente reste absente ; aucun fallback (9.2, 9.3) |
| `From_QuantityStep_IsPreservedEndToEnd_FromAdapterThroughRiskInput` | QuantityStep conservé de bout en bout (9.4) |
| `From_Equity_IsPreservedEndToEnd_FromAdapterThroughRiskInput` | Equity conservée de bout en bout (9.5) |
| `From_WithAtasTelemetryParams_ExistingCategoriesRemainUnchanged` | les catégories existantes restent inchangées (9.6) |
| `Instrumentation_IsPassive_RiskEngineResultUnchangedBeforeAndAfterCapture` | RiskAssessment = X avant, = X après (9.7, test explicitement demandé) |
| `From_TelemetryCaptureHasNoSymbolSpecialCasing` | aucun mapping `Symbol == "MES"/"ES"` (9.8) - testé sur MES/ES/un symbole arbitraire inédit |

Relecture manuelle complémentaire (aucun `== "MES"` ni `== "ES"` littéral dans le code ajouté) —
confirmée par grep sur les 3 fichiers modifiés.

**Résultats d'exécution : voir Section 13** (2 tests corrigés après un premier échec — bugs des tests
eux-mêmes, pas du code de production ; détail complet en Section 13).

---

## 13. Build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | ✅ Réussi — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | ✅ Réussi — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | ✅ Réussi — 0 avertissement, 0 erreur (1 min 17 s) |
| `dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | voir ci-dessous — 2 exécutions |

**Exécution 1** (immédiatement après l'instrumentation, avant correction des tests) :
`échec : 2, réussite : 189, ignorée(s) : 1, total : 192, durée 17 min 39 s`.

Les 2 échecs étaient tous deux dans les tests **ajoutés par ce lot** (`ScientificDatasetRealMarketCaptureTests.cs`,
TEST 18) — aucun test pré-existant affecté :

1. `From_WithAtasTelemetryParams_ExistingCategoriesRemainUnchanged` — la boucle de comparaison itérait sur
   *toutes* les clés de `withoutTelemetry.Categories`, y compris les 39 nouvelles clés `ATAS.*`/
   `TradePlan.StopLossAvailable` elles-mêmes (qui, par construction, DOIVENT différer entre l'appel sans
   et l'appel avec télémétrie — c'est exactement ce que les deux tests précédents vérifient séparément).
   Bug du test, pas du code de production. **Corrigé** : la boucle exclut désormais explicitement les
   clés `ATAS.*`/`TradePlan.StopLossAvailable` et ne compare plus que les catégories réellement
   pré-existantes (Risk.\*, TradePlan.Status/EntryPrice/StopLoss/TakeProfit, Decision.\*, Fusion.\*,
   EntryTrigger.\*, Entry.\*).
2. `Instrumentation_IsPassive_RiskEngineResultUnchangedBeforeAndAfterCapture` — `RiskAssessment` (Lot 10,
   protégé, non modifié) porte deux membres `IReadOnlyList<T>` (`RejectionReasons`/`Diagnostics`) que
   `RiskEngine.Evaluate` reconstruit en nouvelles instances `List<T>` à chaque appel ; l'égalité de record
   générée par le compilateur compare ces deux membres par référence, donc deux résultats issus de deux
   appels `Evaluate()` indépendants ne sont **jamais** `Equal()` au sens du record, même à contenu
   strictement identique (confirmé : le message d'échec affichait des valeurs `Expected`/`Actual`
   visuellement identiques). Propriété incidente et pré-existante de `RiskAssessment`, sans rapport avec
   ce lot. **Corrigé** : comparaison champ par champ des valeurs scalaires, plus `SequenceEqual` pour les
   deux listes, ce qui est la véritable définition de "la décision n'a pas changé".

Aucune des deux corrections ne touche `RiskAssessment.cs`, `RiskEngine.cs`, ni aucun fichier protégé —
uniquement l'assertion, dans le fichier de test lui-même.

**Validation ciblée après correction** (`--filter "FullyQualifiedName~ScientificDatasetRealMarketCaptureTests"`) :
`réussite : 34, total : 34, durée 2 s` — les 9 nouveaux tests du Lot 12.5 et les 25 tests pré-existants du
même fichier passent tous.

**Exécution 2** (suite complète, après correction) :
`échec : 1, réussite : 190, ignorée(s) : 1, total : 192, durée 17 min 02 s`.

L'unique échec restant, `AdfLagSelectionScaleStabilityXunitTests.RunAll`
(`Tests/GoldenDatasets/AdfLagSelectionScaleStabilityTests.cs`), est **sans rapport avec ce lot** :

- Sujet totalement étranger — sélection de lag ADF (modèle scientifique de stationnarité), aucun lien
  avec `ScientificDatasetRecord`/`IQIAIndicator.cs`/Risk telemetry ; aucun fichier de ce lot n'est
  référencé dans sa pile d'appel.
- C'est un test de **performance** (`"lag selection at 128 bars must not regress to pathological cost"`,
  120,3156 ms/call observé), donc sensible à la charge machine au moment de l'exécution.
- **Preuve directe de non-régression** : ce même test a **réussi** lors de l'Exécution 1 ci-dessus (compté
  parmi les 189 succès), avec un code de production strictement identique à l'Exécution 2 sur toute la
  zone qu'il exerce (seules les assertions de 2 tests, dans un fichier totalement différent, ont changé
  entre les deux exécutions). Un test de performance qui réussit puis échoue sans qu'aucun code de la
  zone concernée n'ait changé est la signature d'un test instable/sensible à la charge, pas d'une
  régression introduite par ce lot.

Conformément à la Section 10 du lot : **ce test n'a pas été modifié**. Il est identifié précisément
ci-dessus et déterminé, avec preuve à l'appui, comme non lié au Lot 12.5.

---

## 14. Fichiers modifiés

| Fichier | Nature du changement |
|---|---|
| `IQIAIndicator/Core/Calibration/ScientificDatasetRecord.cs` | +170 lignes : 8 nouveaux paramètres optionnels de `From(...)`, 39 nouvelles clés `Categories["ATAS.*"]`/`["TradePlan.StopLossAvailable"]` |
| `IQIAIndicator/IQIAIndicator.cs` | +3 champs privés (`_latestRiskEngineRequest`, `_latestAtasEquitySeriesCount`, `_latestAtasEquityLastTimestamp`), +1 propriété publique (`LastRiskEngineRequest`), +1 lecture passive `ITradingStatistics.Equity` (Count/dernier Time), +1 affectation passive (`_latestRiskEngineRequest = riskRequest`), +8 arguments au call site `ScientificDatasetRecord.From` |
| `IQIAIndicator/Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs` | +530 lignes : 9 tests (TEST 18) + 5 fixtures dédiées |

Aucun autre fichier `.cs` touché par ce lot.

---

## 15. Fichiers protégés

Vérifiés explicitement non modifiés PAR CE LOT (`git diff --name-only` avant/après, et confirmation par
horodatage de fichier - aucun de ces fichiers n'a été ouvert ni édité pendant cette session) :

| Fichier | Statut |
|---|---|
| `Engine/Risk/RiskEngine.cs` | ✅ non touché |
| `Engine/Risk/RiskPolicy.cs` | ✅ non touché |
| `Engine/Risk/InstrumentRiskSpecification.cs` | ✅ non touché |
| `Engine/Risk/RiskEngineRequestFactory.cs` | ✅ non touché |
| `Infrastructure/ATAS/ATASAccountStateAdapter.cs` | ✅ non touché |
| `Infrastructure/ATAS/ATASInstrumentAdapter.cs` | ✅ non touché |
| `Engine/EntryTrigger/EntryTriggerBuilder.cs` | ✅ non touché par ce lot — **déjà modifié avant le début de ce lot** (horodatage fichier : 2026-08-16 07:45, soit la veille ; visible dans `git status` dès le début de la session, hérité de travaux antérieurs non commités) |
| `Engine/Decision/Arbitration/DecisionArbitrator.cs` | ✅ non touché |
| `Engine/TradePlan/TradePlanBuilder.cs` | ✅ non touché |
| `Engine/TradePlan/TradePlan.cs` | ✅ non touché |

`Infrastructure/ATAS/ATASRuntimeDiagnostics.cs` (non explicitement listé comme protégé ni comme autorisé)
a été délibérément laissé **intact** également : la lecture supplémentaire du nombre de points/dernier
timestamp de la série Equity (Section 6) a été ajoutée directement dans `IQIAIndicator.cs` plutôt que
dans ce fichier, pour rester strictement dans le périmètre explicitement autorisé.

`Visualization/Dashboards/TradingDashboard.cs`, `Visualization/State/DashboardContext.cs`,
`Tests/EntryTrigger/DecisionDirectionCoherenceTests.cs`, `Tests/Signal/DirectionEndToEndTests.cs`,
`Tests/IQIAIndicator.Tests.csproj` apparaissent également modifiés dans `git status`, mais avec des
horodatages tous antérieurs au début de cette session (16 et tôt le 17 août) — hérités de lots précédents
non commités, non touchés par le Lot 12.5.

---

## 16. Limites

- **`ATAS.Raw.*` reste une capture existante (Lot 12.3), non ré-auditée par ce lot** : ce lot ne change
  rien à la façon dont `ATASRuntimeDiagnostics` lit ATAS - il change uniquement où ce résultat atterrit.
  Si le Lot 12.3/12.4 avaient une lacune de lecture ATAS elle-même (plutôt qu'un problème d'export), ce
  lot ne la corrige pas.
- **`ATAS.Raw.Equity.SeriesCount`/`LastTimestamp` sont une lecture nouvelle, non couverte par les tests du
  Lot 12.3** — testée ici uniquement au niveau `ScientificDatasetRecord.From` (les valeurs sont passées en
  paramètres), pas au niveau de la lecture ATAS elle-même (qui nécessiterait les mêmes fixtures ATAS
  simulées que `ATASRuntimeDiagnosticsTests.cs`, hors périmètre de ce lot d'instrumentation).
- **`ATAS.QuantityStepAvailable`/`ATAS.QuantityStep` resteront `"False"`/`"NOT AVAILABLE"` sur TOUTE
  future capture**, y compris la prochaine — ce n'est pas un défaut de ce lot, c'est un fait confirmé deux
  fois indépendamment (Lot 12.1, Lot 12.4) sur la surface actuelle de l'API ATAS installée
  (`7.0.9.461`). Une version ATAS différente pourrait exposer un champ différent - non vérifié ici.
- **Aucune validation en conditions ATAS réelles** (Section 13 du lot l'interdit explicitement) : tous les
  tests utilisent des objets construits directement (records), jamais une session ATAS/Replay réelle.
  Reste **REQUIRES LIVE ATAS VALIDATION**.

---

## 17. Procédure exacte du prochain Replay MES

1. Ouvrir ATAS, charger l'indicateur IQIA sur MES M5 (comme pour la capture du Lot 12.4).
2. Vérifier que `EnableScientificDataset = true` (paramètre existant, Lot 9).
3. Lancer un Replay MES M5 (n'importe quelle fenêtre — aucune configuration additionnelle requise, ce lot
   n'ajoute aucun nouveau paramètre UI).
4. Après le Replay, ouvrir le `ScientificDataset_MES_*.json` produit et, pour n'importe quel enregistrement
   où `Risk.Status = "REJECTED"`, lire directement :
   - `ATAS.Adapter.Instrument.QuantityStep` → la valeur réelle utilisée ce bar.
   - `ATAS.RiskInput.Instrument.QuantityStep` → confirmation qu'elle a atteint le Risk Engine inchangée.
   - `ATAS.Adapter.Instrument.IsValid` / `ATAS.RiskInput.Instrument.IsValid` → `True`/`False` observé
     directement (pas déduit de `Risk.RejectionReasons`).
   - `ATAS.EquityAvailable` / `ATAS.Equity` / `ATAS.Raw.Equity.SourceMode` → la valeur Equity réelle et sa
     disponibilité pour le mode actif ce bar.
   - `ATAS.Raw.Equity.SeriesCount` / `LastTimestamp` → pourquoi Equity est ou non disponible (série vide
     vs. jamais interrogée vs. peuplée avec un délai).
   - `TradePlan.StopLossAvailable` → confirmation indépendante que `INVALID_STOP_LOSS` ne dépend pas de
     l'instrument.
5. Aucune configuration ATAS supplémentaire n'est requise pour observer ces valeurs — elles sont capturées
   inconditionnellement dès que `EnableScientificDataset` est actif, exactement comme `Risk.Status` l'est
   déjà.

---

## 18. Verdict

| Question (Section 13 du brief) | Statut après ce lot |
|---|---|
| 1. Quelle valeur ATAS fournit réellement pour QuantityStep ? | **NOT AVAILABLE FROM ATAS API** — confirmé fait permanent (Section 8), pas une lacune d'observabilité |
| 2. Cette valeur est-elle valide ? | Sans objet (aucune valeur ATAS native — voir Q1) |
| 3. Quelle valeur arrive dans InstrumentRiskSpecification ? | **OBSERVABLE AFTER NEXT REPLAY** — `ATAS.Adapter.Instrument.QuantityStep` |
| 4. Quelle valeur arrive dans RiskEngineRequest ? | **OBSERVABLE AFTER NEXT REPLAY** — `ATAS.RiskInput.Instrument.QuantityStep` |
| 5. Quelle valeur ATAS fournit réellement pour Equity ? | **OBSERVABLE AFTER NEXT REPLAY** — `ATAS.Raw.Equity.RealtimeValue`/`ReplayValue`/`SeriesCount`/`LastTimestamp` |
| 6. Cette Equity arrive-t-elle correctement dans AccountState ? | **OBSERVABLE AFTER NEXT REPLAY** — comparaison directe `ATAS.Raw.Equity.*` vs `ATAS.Adapter.Account.CurrentEquity` |
| 7. Quelle valeur arrive dans RiskEngineRequest ? | **OBSERVABLE AFTER NEXT REPLAY** — `ATAS.RiskInput.Account.CurrentEquity` |
| 8. Pourquoi le Risk Engine retourne-t-il INSTRUMENT_SPEC_INVALID ? | **OBSERVABLE AFTER NEXT REPLAY** pour la valeur ; la cause structurelle (QuantityStep sans équivalent ATAS) reste **PROVEN** depuis le Lot 12.1/12.4 |
| 9. Pourquoi retourne-t-il INVALID_STOP_LOSS ? | **PROVEN** (Lot 12.4, confirmé par ce lot) — `TradePlan.StopLossAvailable` le rendra visible sans dépendre de `Risk.RejectionReasons` |
| 10. Ces deux rejets sont-ils indépendants ? | **PROVEN** (code, `RiskEngine.cs` Phase 2 vs Phase 4, inchangées) — observable empiriquement dès la prochaine capture via `ATAS.Adapter.Instrument.IsValid` vs `TradePlan.StopLossAvailable` pris séparément |

**Verdict global** : l'écart d'observabilité identifié par le Lot 12.4 est comblé. Aucune valeur n'a été
inventée, aucun fallback introduit, aucune logique de validation dupliquée (la seule validité réutilisée
— `IsValid` — est une propriété déjà existante et inchangée du Lot 10). Le Risk Engine reste, à l'issue de
ce lot, strictement inchangé dans son comportement (Section 12, test
`Instrumentation_IsPassive_RiskEngineResultUnchangedBeforeAndAfterCapture`).

L'hypothèse "QuantityStep ATAS = X → Adapter = X → RiskInput = X → validation = INVALID" est désormais
**mesurable directement sur la prochaine capture MES**, sans aucune ligne de code supplémentaire.

STOP — fin du LOT 12.5. Aucun Replay lancé. Aucun commit.
