# QDE-012 — Sprint 15.25 (Lot 11) — Intégration contrôlée du Risk Engine dans le pipeline IQIA

Ce lot câble le Risk Engine construit au Lot 10 dans le pipeline réel `IQIAIndicator.cs`, en mode
strictement passif : `TradePlan → RiskEngine → RiskAssessment`, observable via le dataset scientifique
et le tracing, **sans jamais envoyer d'ordre** et **sans inventer de valeur de capital ou de risque**.

---

## 1. Objectif

Intégrer le Risk Engine (Lot 10) au pipeline réel : `DecisionArbitrator → EntryTriggerBuilder →
EntryCandidate/EntryTriggerCandidate → TradePlanBuilder → TradePlan → RiskEngine → RiskAssessment`, en
préservant strictement le mode observation-only déjà en vigueur dans tout le reste de l'indicateur.

## 2. Architecture avant

État vérifié avant modification (`git status`, `git log -1` = `a9f983f`, build Debug/Release/tests déjà
verts — voir le rapport Lot 10) : le Risk Engine (`Engine/Risk/`, 11 fichiers) existait comme module
autonome, pleinement testé (35 tests) mais **jamais appelé** depuis `IQIAIndicator.cs`. `TradePlan` était
produit à chaque bar (`_tradePlanEngine.Process(...)`, ligne ~409) mais restait toujours `SIGNAL_ONLY`
en production, faute de `TradeRiskParameters`.

## 3. Architecture après

```
TradePlanEngine.Process(...) → _latestTradePlan
        ↓
RiskPolicyFactory.FromRawInputs(...)            (raw UI -> RiskPolicy, convention documentée §6)
InstrumentRiskSpecification.FromInstrumentInfo(...)   (réutilisé tel quel du Lot 10)
new AccountState(...)                            (mapping direct des paramètres UI)
        ↓
RiskEngineRequestFactory.FromTradePlan(tradePlan, account, policy, instrument)  → RiskEngineRequest?
        ↓ (null si TradePlan.Direction n'est pas BUY_CANDIDATE/SELL_CANDIDATE, ou EntryPrice absent)
RiskEngine.Evaluate(request) → RiskAssessment       (Lot 10, non modifié)
        ↓
_latestRiskAssessment  → ScientificDatasetRecord.From(..., riskAssessment: ...)
                        → LastRiskAssessment (propriété publique, observabilité)
                        → PipelineTraceStage.Risk (tracing, si EnablePipelineTracing)
```

`RiskEngine.cs` (Lot 10) n'a **subi aucune modification** — il reste pipeline-independent. Seuls deux
nouveaux fichiers d'intégration (`RiskEngineRequestFactory.cs`, `RiskPolicyFactory.cs`) ont le droit de
dépendre du pipeline (`TradePlan`, `DirectionCandidate`), exactement documenté dans leurs doc-comments.

## 4. Point d'intégration choisi

Dans `IQIAIndicator.cs::OnCalculate`, immédiatement **après** le bloc `TradePlan` existant (qui construit
`_latestTradePlan`) et **avant** le bloc `TradePlanAnnotation`. Ce point est celui identifié dans le
rapport Lot 10 (§15 Limites) comme l'endroit naturel : le `TradePlan` de ce bar est déjà complet
(`Entry`/`SL`/`TP` déjà résolus par `TradePlanBuilder`, jamais recalculés ici — Lot 11 Section 7). Le bloc
est instrumenté avec son propre `PipelineTraceScope` (`PipelineTraceStage.Risk`, ajout additif à l'enum
existant — vérifié qu'aucun `switch`/`case` exhaustif sur cet enum n'existe dans le dépôt avant l'ajout).

## 5. Données injectées dans RiskEngine

`RiskEngineRequest` est construit à chaque bar, exclusivement à partir de :
- `TradePlan.Direction/EntryPrice/StopLoss/TakeProfit` (jamais reconstruits — Section 7 du lot) ;
- `AccountState`, construit directement depuis 8 nouveaux paramètres UI `Risk*` (Section 6) ;
- `RiskPolicy`, construit via `RiskPolicyFactory.FromRawInputs` depuis 10 nouveaux paramètres UI ;
- `InstrumentRiskSpecification`, construit via `InstrumentRiskSpecification.FromInstrumentInfo`
  (réutilisation directe du Lot 10) depuis `Core.InstrumentInfo` (déjà existant) + 3 nouveaux paramètres
  UI (`MinQuantity`/`MaxQuantity`/`QuantityStep`).

Si `RiskEngineRequestFactory.FromTradePlan` retourne `null` (aucun candidat directionnel évaluable),
`RiskEngine.Evaluate` **n'est pas appelé** — `_latestRiskAssessment` reste `null`, jamais une valeur
fictive.

## 6. Gestion du capital

Huit nouveaux paramètres UI (`GroupName = "Risk Engine"`) : `RiskInitialCapital`, `RiskCurrentEquity`,
`RiskCurrentBalance`, `RiskPeakEquity`, `RiskDailyStartingEquity`, `RiskDailyPnL`, `RiskUsedToday`,
`RiskOpenRisk` — tous `decimal`, tous par défaut `0m`. **Aucune valeur (10 000€, 25 000€, 50 000€,
100 000€ ou autre) n'est hardcodée.** Un capital resté à `0` (non configuré) est déjà, par construction du
Lot 10, traité comme `INVALID_CAPITAL`/`INVALID_EQUITY` — jamais une taille de position silencieuse. Les
10 paramètres `RiskPolicy*` suivent une convention documentée dans `RiskPolicyFactory.cs` : une limite
`<= 0` signifie "non configurée" (`RiskPolicy` reçoit `null`, donc aucune contrainte), une valeur
strictement positive est une limite active. Les 4 champs `(%)` sont saisis comme pourcentages entiers
humains (`2` = 2%) et convertis en fraction (`0.02`) uniquement dans `RiskPolicyFactory` — évite le piège
where un utilisateur tapant "2" activerait sans le savoir une limite de 200%.

**Aucune valeur réelle de capital du compte prop firm de l'utilisateur n'a été déduite, supposée ou
inventée** (Section 19 du lot) — tous les paramètres restent à `0`/non configurés par défaut.

## 7. Gestion de l'instrument

`InstrumentRiskSpecification` est construite via la factory déjà existante du Lot 10
(`FromInstrumentInfo`), à partir du `Core.InstrumentInfo` déjà construit pour `TradePlanContext`
(mêmes `TickValue`/`PointValue`/`PriceDecimals` déjà paramétrables) plus 3 nouveaux paramètres
`RiskInstrumentMinQuantity`/`MaxQuantity`/`QuantityStep` (défaut `0` → `InstrumentRiskSpecification.
IsValid` déjà `false` tant que non configurés, mécanisme du Lot 10 réutilisé tel quel).

## 8. Gestion ES/MES

**Aucun `if symbol == "ES"`/`if symbol == "MES"` nulle part** — vérifié par relecture de tout le code
ajouté (`RiskEngineRequestFactory.cs`, `RiskPolicyFactory.cs`, le nouveau bloc `IQIAIndicator.cs`). La
différenciation passe exclusivement par les valeurs d'`InstrumentRiskSpecification` fournies. Testé
explicitement (TEST 5 de `RiskEngineIntegrationTests.cs`) : le même `TradePlan`, passé au même
`RiskEngine`, produit `RiskPerUnit=250` avec une spec ES (`PointValue=50`) et `RiskPerUnit=25` avec une
spec MES (`PointValue=5`) — la différence vient uniquement de la donnée, jamais d'une branche de code.

## 9. Gestion d'un TradePlan sans SL

Réalité de production actuelle (inchangée par ce lot) : `IQIAIndicator.cs` passe toujours
`RiskParameters: null` à `TradePlanContext` (aucune méthodologie SL n'existe encore — Lot 10/11
l'interdisent explicitement dans ce lot). En conséquence `TradePlan.StopLoss` reste `null` et
`TradePlan.Status` reste `SIGNAL_ONLY`. `RiskEngineRequestFactory.FromTradePlan` construit tout de même
une `RiskEngineRequest` (avec `StopLoss: null`) dès qu'une `Direction`/`EntryPrice` existe — c'est
`RiskEngine.Evaluate` lui-même, déjà testé au Lot 10, qui rejette avec `INVALID_STOP_LOSS`
("StopLoss was not provided"). **Aucune stratégie ATR/structure/volatility/pourcentage fixe n'a été
inventée** — testé explicitement (TEST 7 de `RiskEngineIntegrationTests.cs`).

## 10. RiskAssessment produit

Le contrat `RiskAssessment` du Lot 10 est utilisé tel quel, sans aucune modification :
`Status (ACCEPTED/REJECTED)`, `RejectionReasons`, `PositionSize`, `RiskAmount`, `RiskBudget`,
`RiskRewardRatio`, `Diagnostics`, etc. Aucune seconde logique de décision de risque n'a été créée.

## 11. Instrumentation ajoutée

`ScientificDatasetRecord.From(...)` reçoit un nouveau paramètre optionnel `riskAssessment = null` (même
motif exact que le Lot 9 pour `entryCandidate`/`entryTriggerCandidate`/`tradePlan` — trailing optional,
compatible avec tous les appels positionnels préexistants). 6 nouvelles entrées `Categories`, valeur
`"NOT AVAILABLE"` si absent, jamais recalculées : `Risk.Status`, `Risk.RejectionReasons` (jointes par
`;`), `Risk.PositionSize`, `Risk.RiskAmount`, `Risk.RiskBudget`, `Risk.RiskRewardRatio`. Une propriété
publique `IQIAIndicator.LastRiskAssessment` a aussi été ajoutée pour l'observabilité/les tests
(l'affichage dans `TradingDashboard.cs` est délibérément laissé pour un lot ultérieur — voir §14).

## 12. Tests

**8 tests d'intégration** dans `Tests/Risk/RiskEngineIntegrationTests.cs`
(+ `Tests/XunitWrappers/RiskEngineIntegrationXunitTests.cs`), couvrant les 7 minimums de la Section 13
plus un cas supplémentaire (aucun candidat évaluable) :

| Test | Couvre |
|---|---|
| 01 Pipeline produit un RiskAssessment | TradePlan réel (via `TradePlanEngine`) → requête → assessment non-null |
| 02 REJECTED | SL du mauvais côté → `REJECTED` + `INVALID_STOP_LOSS` |
| 03 ACCEPTED | cas entièrement valide → `ACCEPTED` + `PositionSize` résolu |
| 04 Capital absent | capital=0 → `REJECTED`, `INVALID_CAPITAL`/`INVALID_EQUITY`, jamais de sizing silencieux |
| 05 ES vs MES | même TradePlan, deux specs → `RiskPerUnit` différent (250 vs 25), aucun branchement symbole |
| 06 Aucun effet de bord | `TradePlan`/`AccountState`/`RiskPolicy`/`InstrumentRiskSpecification` inchangés (`with {}` snapshot) après l'appel |
| 07 TradePlan sans SL | réalité de production (`RiskParameters: null`) → `REJECTED INVALID_STOP_LOSS`, jamais un SL inventé |
| 08 Aucun candidat évaluable | `NO_ACTION`/`WATCH` → `RiskEngineRequestFactory` retourne `null`, RiskEngine jamais appelé |

Plus **2 tests d'instrumentation** ajoutés à `Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs`
(TEST 17, même style que le TEST 16 du Lot 9) : `From_WithRiskAssessmentParam_DoesNotMutateIt` et
`From_WithRiskAssessmentParam_PopulatesTheRiskCategories`, plus l'extension de
`From_WithoutInstrumentationParams_ReportsNotAvailable` aux 6 nouvelles catégories `Risk.*`.

Total nouveau : **10 tests**. Les 35 tests du Lot 10 (`RiskEngineTests.cs`) restent inchangés et verts.

## 13. Build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` (suite complète, sans charge concurrente) | **0 échec, 180 réussite(s), 1 ignoré(e) (pré-existant, sans lien), 181 au total, durée 6 min 38 s** |

(Un avertissement `CS8602` transitoire est apparu pendant le développement, dû à une assertion
redondante `Assert(x is not null, ...)` sur une variable déjà non-nullable — supprimée, la variable
étant déjà garantie non-null par la signature de `RiskEngine.Evaluate`.)

Deux exécutions successives de la suite ont régénéré des fichiers de sortie de campagne pré-existants
(`Tests/Research/StopLossCalibration/Output/*.txt`) comme effet de bord normal de ces tests de recherche
(sans lien avec ce lot) — restaurés via `git checkout --` après chaque run pour garder le diff strictement
scopé au Lot 11.

## 14. Limitations

- **Aucune valeur réelle de capital/policy n'est configurée** — tous les nouveaux paramètres UI
  restent à `0` par défaut ; tant qu'un utilisateur ne les configure pas explicitement dans le panneau
  ATAS, `RiskEngine` rejette systématiquement (`INVALID_CAPITAL`) — c'est le comportement voulu (Section
  3/19 : ce lot construit le tuyau, pas la valeur).
  - Si `RiskPeakEquity` reste à `0` alors que `RiskCurrentEquity` est configurée, `CurrentDrawdownPercent`
    reste `null` (voir `AccountState.cs`, Lot 10) et la contrainte de drawdown reste inerte tant que
    `RiskPeakEquity` n'est pas également configurée — documenté, pas un bug caché.
- `PortfolioState`/`OpenPosition` (Lot 10, Section 13) ne sont toujours pas consommés par
  `RiskEngine.Evaluate` — `RiskEngineRequestFactory.FromTradePlan` accepte un `portfolio` optionnel mais
  `IQIAIndicator.cs` ne le fournit pas (aucun suivi de position ouverte dans ATAS n'a été ajouté, hors
  périmètre explicite du lot).
- `TradingDashboard.cs` n'affiche pas encore `RiskAssessment` — observabilité assurée via
  `LastRiskAssessment`, le dataset scientifique et le tracing pipeline uniquement. Le fichier est déjà en
  cours de modification hors de ce lot (visible dans `git status` avant ce lot) ; y ajouter un nouveau
  panneau aurait dépassé "les changements strictement nécessaires".
- `IStopLossStrategy` (Lot 10) reste non câblé — aucune méthodologie SL n'existe encore, conformément à
  l'interdiction explicite du Lot 11 (Section 8).

## 15. Ce qui reste volontairement non implémenté

- Aucune stratégie de Stop Loss (ATR/structure/volatility) ;
- Aucun envoi d'ordre, aucune interaction broker, aucun `SubmitOrder`/`Buy`/`Sell` (le mot-clé
  n'existe nulle part dans `IQIAIndicator.cs`, vérifié) ;
- Aucune valeur de capital/policy PropFirm réelle ;
- Aucun portfolio manager multi-position ;
- Aucune modification du seuil `AmbiguityGateThreshold = 0.95` (`EntryTriggerBuilder.cs` non touché) ;
- Aucune modification de `DecisionArbitrator`, `TradePlanBuilder`, `TradePlanContext`, `TradePlan.cs`.

## 16. Prochain lot recommandé

Un lot séparé pourrait : (a) exposer `RiskAssessment` dans `TradingDashboard`/`TradePlanAnnotation` pour
une observation visuelle sur le chart ; (b) démarrer la calibration d'une méthodologie SL scientifique
validée (hors périmètre risque pur) ; (c) une fois des valeurs de capital/policy réelles documentées par
l'utilisateur, les configurer dans les paramètres `Risk*` ajoutés ici — **ce lot ne recommande aucune
valeur** de capital, de risque ou de policy.

---

## LOT 11 RESULT

**Fichiers créés :**
```
IQIAIndicator/Engine/Risk/RiskPolicyFactory.cs
IQIAIndicator/Engine/Risk/RiskEngineRequestFactory.cs
IQIAIndicator/Tests/Risk/RiskEngineIntegrationTests.cs
IQIAIndicator/Tests/XunitWrappers/RiskEngineIntegrationXunitTests.cs
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot11_RiskEngine_Integration_Report.md
```

**Fichiers modifiés (par ce lot) :**
```
IQIAIndicator/IQIAIndicator.cs                                   (Risk stage + 21 nouveaux paramètres UI)
IQIAIndicator/Core/Calibration/ScientificDatasetRecord.cs          (param riskAssessment + 6 categories)
IQIAIndicator/Core/Observability/PipelineTraceContext.cs           (+ PipelineTraceStage.Risk)
IQIAIndicator/Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs  (2 tests + extension NOT AVAILABLE)
```
(`EntryTriggerBuilder.cs`, `Tests/EntryTrigger/DecisionDirectionCoherenceTests.cs`,
`Tests/Signal/DirectionEndToEndTests.cs`, `Visualization/Dashboards/TradingDashboard.cs` : modifications
pré-existantes antérieures à ce lot, non touchées davantage.)

**Point d'intégration :** `IQIAIndicator.cs::OnCalculate`, juste après le bloc `TradePlan`, avant le bloc
`TradePlanAnnotation`.

**Flux exact :** `Decision(AmbiguityScore) → EntryTrigger(gate 0.95, inchangé) → TradePlan(Entry/SL/TP) →
RiskEngineRequestFactory → RiskEngine.Evaluate → RiskAssessment → ScientificDataset/trace/propriété
publique`.

**Capital utilisé :** aucun (paramètres UI présents, tous à `0` par défaut = non configuré) — ni réel, ni
fixture en production ; fixtures explicites uniquement dans les tests.

**Instrument utilisé :** `Core.InstrumentInfo` existant (déjà configurable) + 3 nouveaux paramètres de
quantité, mappés via `InstrumentRiskSpecification.FromInstrumentInfo` (Lot 10, réutilisé).

**Support ES/MES :** confirmé sans branchement hardcodé (TEST 5).

**RiskAssessment observé :** via `LastRiskAssessment` (propriété publique), `ScientificDatasetRecord.
Categories["Risk.*"]`, et `PipelineTraceStage.Risk` (si tracing actif).

**Tests :** 10 nouveaux tests (8 intégration + 2 instrumentation), tous verts. Suite complète : 0 échec,
180 réussite(s), 1 ignoré(e) (pré-existant, sans lien avec ce lot), 181 au total.

**Build Debug :** réussi, 0 avertissement, 0 erreur.
**Build Release :** réussi, 0 avertissement, 0 erreur.

**Git status :** voir liste des fichiers créés/modifiés ci-dessus ; aucun DLL, dataset réel ou fichier
temporaire ajouté.

**Limites restantes :** voir Section 14.

**Prochain lot recommandé :** voir Section 16.

STOP — fin du Lot 11. Aucun DLL déployé, aucun lancement ATAS, aucune modification du seuil, pas de
passage automatique au lot suivant.
