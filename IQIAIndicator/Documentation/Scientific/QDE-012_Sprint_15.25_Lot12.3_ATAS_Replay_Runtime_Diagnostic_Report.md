# QDE-012 — Sprint 15.25 (Lot 12.3) — Diagnostic runtime ATAS Replay : Account + Instrument Binding

**Lot de diagnostic uniquement.** Aucune formule, aucun comportement du RiskEngine, aucune valeur de
Stop Loss n'a été modifiée. Une instrumentation temporaire, clairement identifiée et read-only a été
ajoutée pour capturer les valeurs ATAS brutes derrière le binding du Lot 12.2, et une analyse de code
rigoureuse a permis d'identifier une cause racine avec un niveau de confiance élevé (confirmée par test,
mais **pas encore par une capture Replay réelle** — voir Section 9/12).

## 1. Objective

Déterminer précisément, pour le Replay MES observé par l'utilisateur (`Capital $25,000.00 / Equity
$0.00 / Instrument MES mais INSTRUMENT_SPEC_INVALID / SL NOT PRODUCED`), quelle source ATAS est
effectivement utilisée, où elle échoue, et pourquoi — sans corriger ni modifier le comportement du Risk
Engine dans ce lot.

## 2. Initial Repository State

`git status --short` avant modification : identique à l'état final du Lot 12.2 (7 fichiers pré-existants
modifiés par des lots précédents, non touchés par ce lot ; les fichiers `Engine/Risk/`,
`Infrastructure/ATAS/`, `Tests/*` créés aux Lots 10-12.2, tous non modifiés davantage sauf mention
explicite ci-dessous). Aucune modification étrangère au lot constatée.

## 3. Account APIs Tested

Relecture complète de `IQIAIndicator.cs`, `ATASAccountStateAdapter.cs`, `ATASInstrumentAdapter.cs`,
`AccountState.cs`, `InstrumentRiskSpecification.cs`, `RiskEngine.cs`, `RiskEngineRequestFactory.cs`,
`RiskDashboardPresenter.cs`, `DashboardContext.cs`, `TradingDashboard.cs` (Section 1 du lot).

Chemin confirmé (inchangé depuis le Lot 12.2, aucun contrat métier modifié) :

```
Indicator.TradingManager?.Portfolio ─┐
Indicator.TradingManager?.Position ──┼─► ATASAccountStateAdapter.Build ──► AccountState ──► RiskEngineRequestFactory ──► RiskEngine
Indicator.TradingStatisticsProvider ─┘        (InitialCapital reste manuel)

Indicator.TradingManager?.Security ──► ATASInstrumentAdapter.Build ──► InstrumentRiskSpecification ──► RiskEngineRequestFactory ──► RiskEngine
```

## 4. Runtime Account Observations

**FAIT OBSERVÉ** (fourni par l'utilisateur, dashboard réel en Replay MES) :
`Capital = $25,000.00` (valeur manuelle `RiskInitialCapital`, confirmée cohérente avec Lot 12.2 —
`InitialCapital` reste toujours manuel) ; `Equity = $0.00` ; `Reason` contient `INVALID_EQUITY`.

**RÉSULTAT DE TEST** (`ATASRuntimeDiagnosticsTests.Test02_UnavailableEquityNeverSilentlyFallsBack`,
Section 9 ci-dessous) : reproduit exactement ce symptôme avec un `Portfolio.Balance=25000` mais un
`ITradingStatisticsProvider` absent/vide → `CurrentEquity` résolu à `0m` → `RiskEngine` →
`REJECTED`/`INVALID_EQUITY`. Ceci confirme, au niveau du code, que le chemin
`ATASAccountStateAdapter.TryGetCurrentEquity` → `Build` produit exactement le symptôme observé
lorsque la série d'équité (`ITradingStatistics.Equity`) est vide ou que le provider/flux est absent —
**sans qu'aucun fallback (Balance, InitialCapital, valeur précédente) n'intervienne**, comportement
vérifié conforme à la Section 5 du Lot 12.2/12.3.

**HYPOTHÈSE** (non confirmée par capture Replay réelle) : la cause la plus probable de
`RealtimeEquity`/`ReplayEquity` vides pendant CE Replay spécifique est que
`ITradingStatistics.Equity` (série temporelle `EquityValue`) ne reçoit un premier point qu'après un
événement de trading (ordre exécuté, position modifiée) ou une clôture de session — un compte Replay qui
n'a encore réalisé aucune transaction pourrait légitimement n'avoir aucun point d'équité, indépendamment
de tout problème de binding. **INCONNUE** : ATAS pourrait aussi (a) ne jamais peupler `.Replay` du tout
en mode Replay (contrairement à `.Realtime`), (b) peupler la série avec un léger délai, ou (c) exposer
Portfolio/TradingStatisticsProvider eux-mêmes comme `null` tant qu'aucun compte n'est explicitement
sélectionné dans ATAS. Ces trois pistes ne sont **pas départageables sans capture Replay réelle** — c'est
exactement le rôle de l'instrumentation ajoutée (Section 7).

## 5. Equity Diagnosis

Réponses aux questions 1-6 de la Section 11 du lot :

1. **Quelle API fournit Balance en Replay ?** `Indicator.TradingManager.Portfolio.Balance` (STATICALLY
   CONFIRMED par le Lot 12.1/12.2 — code compile et s'exécute ; **REPLAY OBSERVED** partiellement : le
   dashboard affiche `Capital=$25,000.00`, mais cette valeur provient du paramètre manuel
   `RiskInitialCapital`, pas de `Portfolio.Balance` — **`Portfolio.Balance` lui-même n'a pas encore été
   observé en Replay réel**, seulement `CurrentBalance` qui n'apparaît pas sur le dashboard actuel).
2. **Quelle API fournit Equity en Replay ?** `Indicator.TradingStatisticsProvider.Replay.Equity` (dernier
   point de la série) — STATICALLY CONFIRMED disponible dans l'API, **TEST CONFIRMED** que le code la lit
   correctement quand elle contient des données, **LIVE NOT TESTED** pour savoir si elle contient
   réellement des données pendant ce Replay précis.
3. **`Replay.Equity` contient-il réellement une valeur ?** **INCONNUE** — c'est exactement ce que
   l'instrumentation de la Section 7 doit permettre de déterminer lors d'un prochain Replay.
4. **Pourquoi `CurrentEquity` arrive-t-il à `$0.00` ?** TEST CONFIRMED (Section 4 ci-dessus) :
   parce que `TryGetCurrentEquity` retourne `null` (aucune donnée disponible dans le flux consulté), et
   `Build` résout alors `0m` — le sentinel volontaire, jamais une valeur inventée.
5. **API indisponible, API vide, timing, ou binding ?** Le CODE du binding est TEST CONFIRMED correct
   (Lot 12.2 + Section 9 ci-dessous). Reste HYPOTHÈSE/INCONNUE laquelle des 3 causes runtime
   (API réellement vide car aucune transaction, timing/délai de peuplement, ou Portfolio/
   TradingStatisticsProvider eux-mêmes `null` avant sélection explicite d'un compte) explique ce Replay
   précis — non déterminable sans capture réelle.
6. **Différence Replay vs Live ?** STATICALLY CONFIRMED que l'API distingue `.Replay` de `.Realtime`
   (Lot 12.1) — **LIVE NOT TESTED**, aucune comparaison possible sans session Live.

## 6. Instrument APIs Tested

`Indicator.TradingManager.Security` : `Instrument`, `TickSize`, `TickCost`, `LotSize`, `LotMinSize`,
`LotMaxSize`, `Digits`, `BaseCurrency`, `QuoteCurrency` — toutes lues sans changement depuis le Lot 12.2
par `ATASInstrumentAdapter.Build`, désormais également capturées côté diagnostic par
`ATASRuntimeDiagnostics.CaptureInstrument` (Section 7).

## 7. MES Specification Diagnosis

**FAIT OBSERVÉ** : le dashboard affiche correctement `Instrument = MES` (donc `Security.Instrument`
a bien été lu — le Symbol N'EST PAS le champ en cause) mais `RiskEngine` rejette avec
`INSTRUMENT_SPEC_INVALID`.

**RACINE LA PLUS PROBABLE, CONFIRMÉE PAR TEST DE CODE** (mais pas encore par capture Replay réelle) :
`InstrumentRiskSpecification.QuantityStep` provient **exclusivement** du paramètre manuel
`RiskInstrumentQuantityStep` (Lot 11/12.2 — confirmé au Lot 12.1 qu'ATAS n'expose aucun équivalent). Ce
paramètre a pour défaut `0`. `InstrumentRiskSpecification.IsValid` (Lot 10, non modifié) exige
`QuantityStep > 0`. **Si l'utilisateur n'a jamais configuré manuellement "Pas de quantite" dans le
panneau ATAS, `QuantityStep` reste `0`, et la spécification est invalide même si Symbol/TickSize/
TickCost/LotMinSize/LotMaxSize sont tous correctement fournis par ATAS.**

TEST CONFIRMED (`ATASRuntimeDiagnosticsTests.Test04_MissingQuantityStepAloneInvalidatesSpecificationEvenWithValidAtasData`) :
reproduit exactement ce scénario — un `Security` MES réaliste et complet (`Instrument="MES"`,
`TickSize=0.25`, `TickCost=1.25`, `LotMinSize=1`, `LotMaxSize=50`) produit une spécification **invalide**
dès lors que `QuantityStep=0`, avec `ExplainInvalidFields` désignant **exclusivement** `QuantityStep`
comme champ fautif — aucune autre condition ne se déclenche.

Diagnostic type produit par l'instrumentation (Section 7), format exact attendu sur un Replay réel via
`LastATASInstrumentDiagnostic`/le rapport de trace pipeline :

```
Instrument detected: MES
TickSize: 0.25 (ou UNAVAILABLE)
TickCost: 1.25 (ou UNAVAILABLE)
LotSize / LotMinSize / LotMaxSize: ... (ou UNAVAILABLE)
Specification valid: NO
Invalid field: QuantityStep <= 0 (actual=0)   [hypothèse la plus probable, à confirmer]
```

**Cause alternative non exclue** : si `Security.LotMinSize`/`LotMaxSize` sont eux-mêmes `UNAVAILABLE`
pour ce flux de données précis (dépend du courtier/connecteur ATAS réellement utilisé — Lot 12.1 §12),
ET que les paramètres manuels de repli `RiskInstrumentMinQuantity`/`MaxQuantity` sont eux aussi restés à
`0`, alors `MinQuantity<=0` s'ajouterait comme second champ invalide. **Seule une capture Replay réelle
via l'instrumentation ci-dessous permet de trancher entre ces deux causes (ou de confirmer qu'elles sont
combinées).**

## 8. Stop Loss Status

**CONFIRMÉ, comportement attendu, aucune action requise** (réponse à la question 12 de la Section 11) :
`TradePlan.StopLoss` reste `null` tant qu'aucune méthodologie SL n'existe (Lots 10-12 l'interdisent
explicitement) — `RiskEngineRequestFactory.FromTradePlan` transmet ce `null` sans le transformer, et
`RiskEngine.Evaluate` (Lot 10, non modifié) rejette avec `INVALID_STOP_LOSS`, exactement comme observé
sur le dashboard réel (`SL NOT PRODUCED` / `INVALID_STOP_LOSS`). TEST CONFIRMED
(`ATASRuntimeDiagnosticsTests.Test05_InvalidStopLossUnchangedWhenAbsent`). **Aucune modification
nécessaire ni effectuée.**

## 9. Replay vs Live

| Élément | Statut |
|---|---|
| Le code lit `.Replay` quand `context.Execution.IsReplay=true` | STATICALLY CONFIRMED + TEST CONFIRMED |
| `.Replay.Equity` contient des données réelles pendant CE Replay | **INCONNUE — nécessite capture réelle** |
| `Portfolio`/`Security` sont peuplés pendant le Replay observé | **REPLAY OBSERVED partiellement** : `Security.Instrument="MES"` l'était (Instrument correctement affiché) ; `Portfolio`/`Equity` non confirmés peuplés |
| Comportement identique en Live | **LIVE NOT TESTED** |

## 10. Tests

**5 tests** dans `Tests/Infrastructure/ATAS/ATASRuntimeDiagnosticsTests.cs`
(+ `Tests/XunitWrappers/ATASRuntimeDiagnosticsXunitTests.cs`), couvrant exactement la Section 9 du lot :

| Test | Couvre |
|---|---|
| 01 Account values captured correctly | Balance/OpenPnL/Position/Equity capturés fidèlement depuis des fixtures ATAS réalistes |
| 02 Unavailable Equity never silently falls back | reproduit exactement le symptôme Replay observé (`Balance=25000`, `Equity=$0.00`, `REJECTED`/`INVALID_EQUITY`) |
| 03 MES instrument built from ATAS data | spécification MES valide et correcte quand toutes les données (y compris QuantityStep) sont fournies |
| 04 Missing QuantityStep alone invalidates | **reproduit la cause racine la plus probable** : MES autrement 100% valide, invalidé uniquement par `QuantityStep=0` |
| 05 INVALID_STOP_LOSS unchanged | confirme qu'aucune régression/changement n'affecte ce rejet |

Aucun test métier existant (Lots 10-12.2) n'a été modifié.

## 11. Build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` (suite complète) | **0 échec, 183 réussite(s), 1 ignoré(e) (pré-existant, sans lien — `Sprint1515HistoricalReconciliationXunitTests`), 184 au total, durée 9 min 26 s** |

Deux fichiers de sortie de campagne pré-existants (`Tests/Research/StopLossCalibration/Output/*.txt`)
ont été régénérés comme effet de bord de la suite complète (sans lien avec ce lot) — restaurés via
`git checkout --` pour garder le diff strictement scopé au Lot 12.3. Aucun test instable/flaky
pré-existant observé lors de cette exécution.

## 12. Exact Root Causes

- **CONFIRMÉ PAR LE CODE (TEST CONFIRMED), le plus probable** : `INSTRUMENT_SPEC_INVALID` provient au
  minimum de `QuantityStep` resté à sa valeur par défaut non configurée (`0`) — un paramètre
  **exclusivement manuel** (Section 11 du Lot 12.1/12.2 : ATAS n'expose aucun équivalent) — et non d'un
  défaut du binding ATAS lui-même. **Action utilisateur possible, hors du périmètre de ce lot** :
  configurer "Pas de quantite" (`RiskInstrumentQuantityStep`) dans le panneau ATAS à une valeur
  positive (ex. `1` pour MES).
- **HYPOTHÈSE, NON CONFIRMÉE** : `CurrentEquity=$0.00` provient d'une série `ITradingStatistics.Equity`
  vide ou d'un provider/flux absent pendant ce Replay — le CODE se comporte correctement dans les deux
  cas (fail-closed testé), mais la cause RUNTIME exacte (compte sans transaction, `.Replay` non peuplé
  par ATAS, ou `TradingManager`/`Portfolio` eux-mêmes indisponibles à ce stade du Replay) reste
  **INCONNUE** sans nouvelle capture via l'instrumentation ajoutée.
- Aucune cause identifiée ne nécessite une modification de `RiskEngine.cs`, `RiskPolicy.cs`,
  `InstrumentRiskSpecification.cs`, `EntryTriggerBuilder.cs`, `DecisionArbitrator.cs`, ou
  `TradePlanBuilder.cs` — tous confirmés fail-closed et corrects par les tests de ce lot et des lots
  précédents.

## 13. Recommended Next Lot

1. **Aucun code — validation utilisateur d'abord** : configurer `RiskInstrumentQuantityStep` (et, par
   prudence, `RiskInstrumentMinQuantity`/`MaxQuantity` en repli) dans le panneau ATAS, relancer le même
   Replay, et observer si `INSTRUMENT_SPEC_INVALID` disparaît — validerait directement l'hypothèse de la
   Section 12 sans écrire une seule ligne de code.
2. Relancer un Replay avec l'instrumentation de ce lot active (`PipelineTraceReport`/
   `LastATASAccountDiagnostic`/`LastATASInstrumentDiagnostic`) pour capturer les valeurs `ATAS.*` brutes
   et trancher définitivement la cause de `CurrentEquity=$0.00` (Section 5, point 3/5).
3. Une fois les deux causes confirmées par capture réelle, un lot séparé pourrait envisager (si
   pertinent et démontré nécessaire) un mécanisme de repli explicite pour `MinQuantity`/`MaxQuantity`
   dans l'UI, ou documenter que `QuantityStep` doit être configuré manuellement par instrument — **aucune
   valeur ni décision de ce type n'est prise dans ce lot**.
4. Retirer l'instrumentation temporaire (`ATASRuntimeDiagnostics.cs` et les champs `ATAS.*` du trace)
   une fois le diagnostic confirmé et documenté, si elle n'est plus jugée utile.

## 14. Protected Components

`Engine/Risk/RiskEngine.cs`, `Engine/Risk/RiskPolicy.cs`, `Engine/Risk/InstrumentRiskSpecification.cs`,
`Engine/EntryTrigger/EntryTriggerBuilder.cs`, `Engine/Decision/Arbitration/DecisionArbitrator.cs`,
`Engine/TradePlan/TradePlanBuilder.cs`, seuil `AmbiguityGateThreshold = 0.95` : **tous vérifiés
inchangés** (`git status`/relecture directe avant remise de ce rapport). Aucune formule scientifique,
aucune stratégie Stop Loss, aucune logique d'envoi d'ordre ajoutée ou modifiée.

## 15. Final Verdict

Le Risk Engine se comporte **correctement et de façon fail-closed** face aux données ATAS actuellement
reçues pendant ce Replay : il rejette honnêtement plutôt que d'inventer une valeur. La cause la plus
probable et la mieux étayée par le code est une **configuration manuelle manquante**
(`QuantityStep`), pas un défaut du binding ATAS. La cause de l'Equity à zéro reste à confirmer par une
nouvelle capture Replay utilisant l'instrumentation ajoutée dans ce lot.

---

## LOT 12.3 RESULT

Equity Replay:
ZERO — resolved via the documented fail-closed sentinel (0m), not a fabricated or fallback value

Root cause:
CONFIRMED BY CODE (TEST CONFIRMED): ITradingStatisticsProvider.Realtime/.Replay.Equity returned no data
for this session (empty curve or absent stream) — TryGetCurrentEquity correctly returned null, Build
correctly resolved 0m. EXACT RUNTIME REASON (empty account history vs. Replay-specific unavailability
vs. timing) — UNKNOWN, requires a new Replay capture with this lot's instrumentation.

MES Instrument Specification:
INVALID

Invalid field:
QuantityStep (confirmed by test to be sufficient on its own to invalidate an otherwise fully ATAS-valid
MES specification) — MinQuantity/MaxQuantity as a possible secondary cause NOT EXCLUDED, requires live
capture to confirm whether Security.LotMinSize/LotMaxSize were themselves available for this session.

Stop Loss:
NOT PRODUCED — EXPECTED

Risk Engine modified:
NO

Risk Policy modified:
NO

EntryTrigger modified:
NO

TradePlan modified:
NO

Hardcoded MES/ES mapping:
NO

Build:
PASS

Tests:
183 passed / 0 failed / 1 skipped (pré-existant, sans lien)

Replay validation:
OBSERVED — diagnostic only

Live validation:
REQUIRED

DLL deployed:
NO

Commit:
NO

STOP — fin du LOT 12.3.
