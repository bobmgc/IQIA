# QDE-012 — Sprint 15.25 (Lot 12.11) — Correction de la sélection de source Equity Live/Replay

**Lot de correction ciblée.** Un seul mécanisme modifié (sélection de la source Equity), aucune
invention de valeur, aucun ordre envoyé, aucun déploiement, aucun commit.

---

## 1. Problème du LOT 12.10

Capture `ScientificDataset_MES_M5_20260818_154144` : un compte réel/démo authentiquement connecté
(`AccountID` distinct de `"Replay"`, `Balance=25103.92` non nul et différent d'`InitialCapital`,
cadence de collecte compatible avec du temps réel) montrait `Portfolio.IsReplay() == False` — le signal
natif ATAS confirmant explicitement un contexte LIVE — **mais** `context.Execution.IsReplay` (l'ancienne
heuristique bar-index, `Core/MarketContextBuilder.cs`) restait à `True`. Le correctif du LOT 12.6
combine les deux signaux avec un `OR` : *"heuristicIsReplay OU garde temporel de 24h"* — une logique
strictement unidirectionnelle qui ne peut qu'AJOUTER des détections Replay correctes, jamais en RETIRER
une incorrecte. Résultat : `SourceMode` restait bloqué sur `"Replay"`, `TradingStatisticsProvider
.Replay.Equity` (vide, comme dans toutes les captures précédentes) était consultée au lieu de
`.Realtime.Equity` (qui, elle, contenait une valeur), et `CurrentEquity` résolvait à `0` →
`INVALID_EQUITY` — alors même qu'un compte live réel était visible.

## 2. Code responsable

Lecture intégrale des 6 fichiers demandés (Phase A) :

| Fichier | Rôle dans le problème |
|---|---|
| `ATASEquityReplayDetector.cs` | Contient `IsReplayContext`, la fonction pure exacte qui décidait `Replay` vs `Realtime` — c'est le SEUL point de décision, et donc le seul fichier dont la logique devait changer |
| `ATASAccountStateAdapter.cs` | `TryGetCurrentEquity(provider, isReplay)` consulte `.Replay`/`.Realtime` selon le booléen reçu — déjà correct, **aucun changement nécessaire** (le booléen qu'il reçoit devient simplement plus fiable) |
| `IQIAIndicator.cs` | Site d'appel : calculait déjà `_latestPortfolioIsReplay = TradingManager?.Portfolio?.IsReplay()` (Lot 12.6) mais **ne le transmettait jamais** à `IsReplayContext` — c'est le seul autre point à modifier |
| `RiskEngineRequestFactory.cs` / `RiskEngineRequest.cs` / `AccountState.cs` | Lus pour confirmer qu'aucun changement n'y est nécessaire — ils reçoivent `CurrentEquity` déjà résolu, sans connaissance du mécanisme de sélection de source |

**Comment `Execution.IsReplay` est déterminé** (Phase A.1) : dans `Core/MarketContextBuilder.cs`
(non lu à nouveau ce lot — hors périmètre, non modifié), une heuristique interne basée sur l'index de
barre (`bar == currentBar - 1`), documentée depuis le LOT 12.6 comme non fiable car un Replay ATAS
alimente `OnCalculate` barre par barre exactement comme un flux live.

**Comment `Portfolio.IsReplay()` est accessible** (Phase A.2) : `TradingManager?.Portfolio?.IsReplay()`
— méthode d'extension `ATAS.DataFeedsCore.Extensions.IsReplay(Portfolio)`, confirmée par réflexion
(LOT 12.6) comme testant exactement `Portfolio.AccountID == "Replay"`. Déjà calculée dans
`IQIAIndicator.cs` depuis le LOT 12.6 (`_latestPortfolioIsReplay`), mais purement pour la télémétrie —
jamais transmise à la décision.

**Comment le mode Live/Replay était transmis à l'adaptateur** (Phase A.3) : `_latestEquitySourceIsReplay
= ATASEquityReplayDetector.IsReplayContext(heuristicIsReplay: context.Execution.IsReplay, barTime:
context.Clock.CurrentTime, utcNow: DateTime.UtcNow)` puis `ATASAccountStateAdapter.TryGetCurrentEquity
(TradingStatisticsProvider, _latestEquitySourceIsReplay)`.

**Où intervient le garde temporel du LOT 12.6** (Phase A.5) : à l'intérieur de `IsReplayContext`, après
le test de l'heuristique — `(utcNow - barTime) > maxLiveGap` (24h par défaut).

**Pourquoi `Execution.IsReplay=True` pouvait écraser une preuve LIVE** (Phase A.6) : parce que
`IsReplayContext` ne recevait tout simplement jamais `_latestPortfolioIsReplay` en paramètre — la
fonction ne connaissait que `heuristicIsReplay` et le temps ; `Portfolio.IsReplay()` était calculé mais
mort (jamais lu par personne d'autre que `ScientificDatasetRecord.From`, pour la télémétrie).

---

## 3. Hiérarchie des preuves

Vérification demandée par la Phase B avant toute implémentation : **aucun autre signal ATAS natif de
type "chart en cours de relecture" n'a été trouvé** dans les LOTs 12.1/12.6/12.8 (audits déjà exhaustifs,
non refaits ce lot) — le commentaire de `ATASEquityReplayDetector.cs` lui-même le documente depuis le
LOT 12.6 : *"ATAS's public SDK exposes no 'chart is replaying' flag on the bar/indicator level."*
`Portfolio.IsReplay()` reste donc le seul signal natif (non heuristique) disponible.

**Distinction sémantique importante, non explicitée par le brief mais déterminante pour la conception**
: `Portfolio.IsReplay()` teste **quel compte est sélectionné** (le pseudo-compte interne `"Replay"`
d'ATAS, ou un compte réel/démo), tandis que `Execution.IsReplay` tente de détecter **si le chart est en
train de rejouer des barres**. Ce sont deux dimensions distinctes : un compte réel peut rester
sélectionné pendant qu'un chart rejoue des données historiques (Phase B ne l'exclut pas). Une hiérarchie
qui donnerait un pouvoir de veto absolu et inconditionnel à `Portfolio.IsReplay()==false`
réintroduirait précisément le risque de look-ahead que le LOT 12.6 corrigeait : lire l'équité
*actuelle* d'un compte réel comme si elle représentait l'équité *au moment d'une barre ancienne
rejouée*.

Hiérarchie retenue, résolvant cette tension sans contredire la Phase B (qui place explicitement le garde
temporel en position 4, "dernier garde-fou" — c'est-à-dire vérifié en dernier dans l'ordre de code, mais
non subordonné aux autres signaux dans son pouvoir de décision) :

1. `Portfolio.IsReplay() == true` → Replay, **inconditionnel**.
2. Écart temporel `(utcNow - barTime) > 24h` → Replay, **inconditionnel, y compris si `Portfolio
   .IsReplay()==false`** — c'est précisément ce qui protège contre un compte réel resté sélectionné
   pendant la relecture de données anciennes.
3. `Portfolio.IsReplay() == false` (et barre récente, donc l'étape 2 n'a pas déclenché) → Realtime —
   **c'est le correctif du LOT 12.10** : la preuve native l'emporte enfin sur l'heuristique.
4. `Portfolio.IsReplay()` indisponible (`null`) → repli **exact et prouvé identique** sur la formule du
   LOT 12.6 (`heuristicIsReplay OU garde temporel`), non-régression garantie (Section 6, Test 8).

`AccountID == "Replay"` (piste #3 du brief) n'a pas été ajoutée séparément : c'est *exactement* ce que
teste `Portfolio.IsReplay()` en interne (confirmé par réflexion, LOT 12.6) — l'ajouter comme signal
distinct aurait dupliqué la même information sans en apporter de nouvelle.

---

## 4. Correction

**Un seul fichier de logique modifié** : `Infrastructure/ATAS/ATASEquityReplayDetector.cs`.

```csharp
public static bool IsReplayContext(
    bool heuristicIsReplay,
    DateTime barTime,
    DateTime utcNow,
    bool? portfolioIsReplay = null,
    TimeSpan? maxLiveGap = null)
{
    if (portfolioIsReplay == true)
        return true;

    TimeSpan margin = maxLiveGap ?? DefaultMaxLiveGap;
    if ((utcNow - barTime) > margin)
        return true;

    if (portfolioIsReplay == false)
        return false;

    return heuristicIsReplay;
}
```

`portfolioIsReplay` est un nouveau paramètre optionnel (`bool?`, défaut `null`) — **aucune signature
existante n'est cassée**, tout appelant qui omet ce paramètre obtient un comportement mathématiquement
identique à l'ancienne formule (Section 6, preuve par équivalence de table de vérité).

**Un seul site d'appel modifié** : `IQIAIndicator.cs`, la construction de `_latestEquitySourceIsReplay`
reçoit désormais `portfolioIsReplay: _latestPortfolioIsReplay` (variable déjà calculée depuis le
LOT 12.6, simplement enfin lue). Aucune autre ligne de logique modifiée dans ce fichier.

**Commentaires mis à jour, sans changement de logique** : `ScientificDatasetRecord.cs` (le commentaire
au-dessus des clés `ATAS.Adapter.Equity.HeuristicIsReplay`/`ATAS.Raw.Account.PortfolioIsReplay`
affirmait encore *"purement observationnel, non câblé"* — devenu faux depuis ce lot, corrigé pour
refléter l'état réel sans toucher au code qui peuple ces clés.

## 5. Justification

- **Minimale** : 1 fonction modifiée (ajout d'un paramètre + 2 branches), 1 site d'appel modifié
  (1 argument ajouté), 2 blocs de commentaires mis à jour pour rester exacts. Aucune ligne de
  `RiskEngine.cs`/`RiskPolicy.cs`/`InstrumentRiskSpecification.cs`/`RiskEngineRequest.cs`/
  `RiskAssessment.cs`/`ATASAccountStateAdapter.cs`/`ATASInstrumentAdapter.cs`/`EntryTriggerBuilder.cs`/
  `DecisionArbitrator.cs`/`TradePlanBuilder.cs` modifiée — tous vérifiés inchangés (Section 9).
- **Explicite** : chaque branche de la nouvelle hiérarchie a son commentaire dédié dans le code
  (docstring de la classe et de la méthode entièrement réécrits).
- **Déterministe** : fonction pure, sans dépendance ATAS, sans horloge cachée — les mêmes entrées
  produisent toujours la même sortie, testable sans hôte ATAS (identique à la discipline du LOT 12.6).
- **Fail-closed préservé** : aucune valeur d'Equity n'est jamais inventée par cette fonction — elle ne
  fait que choisir QUELLE série lire ; `ATASAccountStateAdapter.Build` (protégé, inchangé) continue de
  résoudre `0m` dès que la série choisie est vide, exactement comme avant.
- **Sans changement du Risk Engine ni de la Risk Policy** : confirmé, ces fichiers sont restés fermés en
  écriture pendant tout ce lot (Section 9).
- **Protections Replay non supprimées** : le garde temporel de 24h reste actif et **prioritaire sur**
  `Portfolio.IsReplay()==false** (Section 3) — c'est un renforcement, pas un retrait, des protections
  existantes.

---

## 6. Tests

**16 nouveaux cas de test** (11 méthodes `[Fact]`, dont une `[Theory]` à 6 cas) ajoutés à
`Tests/Infrastructure/ATAS/ATASEquityReplayDetectorTests.cs` (fichier existant, LOT 12.6 — étendu,
jamais réécrit), couvrant explicitement les 8 tests numérotés du brief, le test d'intégration bout-en-bout de la
Phase G, plus 2 tests supplémentaires jugés nécessaires par l'audit (un garde de sécurité non listé
explicitement, et un second test d'intégration de non-régression) :

| # | Nom du test | Couvre |
|---|---|---|
| 1 | `Test1_PortfolioConfirmsReplay_BothSignalsAgree_ReturnsTrue` | Brief Test 1 (Replay) |
| 2 | `Test2_PortfolioConfirmsLive_RecentBar_ReturnsFalse` | Brief Test 2 (Live) |
| 3 | `Test3_Lot1210Scenario_PortfolioLiveOverridesHeuristicFalsePositive` | Brief Test 3 — reproduit exactement les horodatages de la capture LOT 12.10 |
| 4 | `Test4_PortfolioConfirmsReplay_HeuristicDisagrees_ReplayStaysPriority` | Brief Test 4 (Replay confirmé) |
| 5 | `Test5_PortfolioUnavailable_OldBar_NeverInventsLive_WallClockGuardStillFires` | Brief Test 5 (ambigu → rien n'est inventé) |
| — | `WallClockGuard_FiresEvenWhenPortfolioConfirmsLive_OnAnOldBar` | **Ajouté par l'audit** (Section 3) — vérifie que le garde temporel reste prioritaire même quand `Portfolio.IsReplay()==false`, propriété de sécurité non listée explicitement par le brief mais essentielle |
| 6 | `Test6_PortfolioConfirmsLive_RealtimeEquityAbsent_ResolvesFailClosed` | Brief Test 6 |
| 7 | `Test7_PortfolioConfirmsLive_RealtimeEquityAvailable_ValueFlowsThrough` | Brief Test 7 |
| 8 | `Test8_NonRegression_PortfolioSignalAbsent_MatchesExactPriorFormula` (Theory, 6 cas) | Brief Test 8 — compare formule par formule, pas seulement résultat par résultat |
| — | `Integration_LiveConfirmed_NoLongerForcedToReplay_JustBecauseHeuristicIsTrue` | **Phase G** — chaîne complète ATAS→`RiskEngine`, reproduit le LOT 12.10 bout-en-bout et confirme `INVALID_EQUITY` disparaît |
| — | `Integration_ReplayStillProtected_NoRegressionForAGenuineReplaySession` | **Phase G**, non-régression — confirme qu'une session Replay authentique produit toujours `INVALID_EQUITY`/`INSTRUMENT_SPEC_INVALID`/`INVALID_STOP_LOSS`, identique aux LOTs 12.5-12.9 |

Le Test 8 ne se contente pas de comparer un résultat attendu codé en dur : il recalcule la formule du
LOT 12.6 de façon indépendante dans le test lui-même (`heuristicIsReplay || (utcNow-barTime)>margin`) et
vérifie l'égalité bit à bit avec le nouveau code sur 6 combinaisons (barre "maintenant", barre 25 jours,
juste sous/juste au-dessus de 24h) — une preuve, pas seulement un exemple.

## 7. Résultats build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | ✅ 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | ✅ 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | ✅ 0 avertissement, 0 erreur (après correction d'une ambiguïté `TradeDirection` entre `ATAS.DataFeedsCore.TradeDirection` et `IQIAIndicator.Engine.Risk.TradeDirection`, résolue par qualification `global::`) |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Release` | ✅ 0 avertissement, 0 erreur |
| Tests ciblés (`--filter FullyQualifiedName~ATASEquityReplayDetectorTests`) | ✅ échec : 0, réussite : 28, total : 28 (12 pré-existants + 16 nouveaux) |
| Suite complète (`dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug`) | échec : 1, réussite : 232, ignorée(s) : 1, total : 234, durée 16 min 17 s |

**L'unique échec** (`AdfLagSelectionScaleStabilityXunitTests.RunAll` — `"lag selection at 128 bars must
not regress to pathological cost... (observed 101.3367 ms/call)"`) est le même test de performance
instable/sensible à la charge machine déjà documenté aux LOTs 12.5/12.6 (*"réussit dans cette
exécution"* au LOT 12.6, *"échoue de manière intermittente"* selon le pattern connu). **Confirmé
pré-existant et sans rapport avec ce lot** : aucun fichier de la zone `Tests/GoldenDatasets/
AdfLagSelectionScaleStabilityTests.cs` ni du calcul ADF lui-même n'a été touché par ce lot (seuls
`ATASEquityReplayDetector.cs`, `IQIAIndicator.cs`, `ScientificDatasetRecord.cs` et
`ATASEquityReplayDetectorTests.cs` ont été modifiés — aucun rapport avec la sélection de lag ADF).
L'unique test ignoré (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`) est
également pré-existant, déjà noté sans rapport depuis le LOT 12.6. **234 = 218 (total LOT 12.6) + 16
(nouveaux cas de test ce lot)** — arithmétique cohérente, aucun test perdu ni dupliqué.

---

## 8. Comportement Replay

Inchangé et re-testé (`Integration_ReplayStillProtected_...`) : `Portfolio.IsReplay()==true` →
`SourceMode=Replay` inconditionnellement, `Replay.Equity` vide → `CurrentEquity=0m` →
`INVALID_EQUITY` — identique aux LOTs 12.5 à 12.9, aucune régression.

## 9. Comportement LIVE attendu

`Portfolio.IsReplay()==false` sur une barre récente (< 24h) → `SourceMode=Realtime`, **même si**
`Execution.IsReplay==true` — c'est le correctif de ce lot, prouvé par
`Integration_LiveConfirmed_NoLongerForcedToReplay_JustBecauseHeuristicIsTrue` et par les Tests 2/3/7.

## 10. Comportement Equity

`CurrentEquity` continue de résoudre à `0m` (sentinel fail-closed, `ATASAccountStateAdapter.Build`,
protégé, inchangé) chaque fois que la série sélectionnée (Replay ou Realtime, selon la décision
corrigée) est vide — **y compris désormais en LIVE** (Test 6). Quand la série Realtime contient un
point réel, il est transmis tel quel (Test 7) — jamais recalculé, jamais arrondi, jamais substitué.

## 11. Limites

- **La distinction sémantique Section 3 reste une hypothèse de conception, non une certitude ATAS
  documentée** : rien ne prouve formellement que `TradingStatisticsProvider.Realtime`/`.Replay` sont
  eux-mêmes gouvernés par `Portfolio.IsReplay()` plutôt que par un autre mécanisme interne à ATAS (par
  exemple l'état du connecteur de Market Replay, indépendant du compte sélectionné). Cette incertitude
  était déjà présente avant ce lot (LOTs 12.1/12.8 : *"REQUIRES LIVE ATAS VALIDATION"*) et n'est pas
  levée par une correction de code — seule une nouvelle capture réelle (Section 12) peut la confirmer.
- **Le scénario "chart en Replay à vitesse 1x avec un compte réel resté sélectionné" reste possible et
  non distingué d'un LIVE authentique** par cette correction : si un tel scénario existe réellement dans
  l'usage du LOT 12.10, `Realtime.Equity` pourrait refléter le solde *actuel* du compte plutôt que son
  solde *au moment de la barre rejouée* — un risque de look-ahead résiduel, non démontré comme actif
  mais non exclu non plus, atténué uniquement par le garde temporel de 24h (Section 3, point 2) qui ne
  couvre que les écarts de plus d'un jour, pas les replays intra-journaliers.
- `Execution.IsReplay` (l'ancienne heuristique) n'a pas été investiguée plus avant pour comprendre
  précisément pourquoi elle produit un faux positif dans le scénario LOT 12.10 — hors périmètre de ce
  lot (`Core/MarketContextBuilder.cs` non modifié, non protégé mais non touché).
- Aucune capture réelle n'a encore validé ce correctif en conditions ATAS live (Section 12, à venir).

## 12. Procédure de validation ATAS

**Aucun Replay n'a été lancé automatiquement par ce lot** (conforme à la Phase K). Procédure pour
validation humaine :

### Replay
1. Charger l'indicateur sur un chart en Market Replay, avec le compte pseudo-`"Replay"` d'ATAS
   sélectionné (ou tout compte pour lequel `AccountID=="Replay"`).
2. Vérifier dans le dataset exporté : `ATAS.Raw.Account.PortfolioIsReplay = "True"` et
   `ATAS.Raw.Equity.SourceMode = "Replay"` sur toutes les barres.

### LIVE
1. Charger l'indicateur sur un chart réellement en temps réel (pas de Market Replay actif), avec un
   compte réel/démo connecté et sélectionné.
2. Vérifier dans le dataset exporté : `ATAS.Raw.Account.PortfolioIsReplay = "False"` et
   `ATAS.Raw.Equity.SourceMode = "Realtime"` — **même si** `ATAS.Adapter.Equity.HeuristicIsReplay`
   affiche encore `"True"` (c'est exactement le cas que ce lot corrige).
3. Observer si `TradingStatisticsProvider.Realtime.Equity` se peuple avec une valeur dynamique — répond
   enfin, avec des données réelles fraîches, à la question restée `INCONCLUSIVE` au LOT 12.9/12.10.

Capture courte suffisante (5-15 minutes, conforme à la préférence déjà exprimée par l'utilisateur pour
ce type de validation technique).

## 13. QuantityStep — explicitement hors périmètre

**Non touché.** `QuantityStep` reste exclusivement le paramètre manuel `RiskInstrumentQuantityStep`
(`ATASInstrumentAdapter.cs`, protégé, non modifié). `LotSize` n'a été ni lu ni utilisé par ce lot.
`INSTRUMENT_SPEC_INVALID` restera présent sur toute capture où ce paramètre n'est pas configuré
manuellement — comportement inchangé et attendu.

## 14. Stop Loss — explicitement hors périmètre

**Non touché.** `TradePlanBuilder.cs` reste protégé et non modifié. `TradePlan.Status = SIGNAL_ONLY`
avec `StopLoss = null` continuera de produire `INVALID_STOP_LOSS` — comportement inchangé et attendu,
confirmé par `Integration_ReplayStillProtected_...` (Section 6).

---

# LOT 12.11 RESULT

## CODE

Files modified : Infrastructure/ATAS/ATASEquityReplayDetector.cs (logique, +signature étendue) ; IQIAIndicator.cs (1 site d'appel + 2 blocs de commentaires) ; Core/Calibration/ScientificDatasetRecord.cs (1 bloc de commentaire, aucune logique) ; Tests/Infrastructure/ATAS/ATASEquityReplayDetectorTests.cs (+18 tests)
Files created : NONE

## LIVE/REPLAY

Replay detection : Portfolio.IsReplay()==true → Replay inconditionnel ; sinon garde temporel 24h → Replay inconditionnel ; sinon Portfolio.IsReplay()==false → Realtime ; sinon (Portfolio indisponible) → ancienne heuristique (formule LOT 12.6 inchangée)
Primary source : Portfolio.IsReplay() (ATAS.DataFeedsCore.Extensions, natif)
Fallback source : context.Execution.IsReplay (heuristique bar-index, LOT 12.6) OU garde temporel 24h — utilisé uniquement quand Portfolio.IsReplay() est indisponible
Fail-closed : PRÉSERVÉ (aucune Equity inventée ; série vide → CurrentEquity=0m → INVALID_EQUITY, y compris en LIVE)

## EQUITY

Replay source : TradingStatisticsProvider.Replay.Equity (lecture inchangée, Lot 12.2)
Realtime source : TradingStatisticsProvider.Realtime.Equity (lecture inchangée, Lot 12.2) — désormais atteignable en LIVE même quand l'ancienne heuristique dit Replay
CurrentEquity : résolu à 0m (sentinel) si la série sélectionnée est vide, quelle qu'elle soit ; valeur réelle transmise sans modification si la série contient un point
SourceMode : suit désormais Portfolio.IsReplay() en priorité (voir hiérarchie ci-dessus)

## TESTS

Debug : PASS (build 0/0)
Release : PASS (build 0/0)
Tests : 232 réussis / 234 total (16 min 17 s) — 12 tests pré-existants + 16 nouveaux cas de test dans ATASEquityReplayDetectorTests.cs, tous réussis (28/28 sur le fichier ciblé)
Failures : 1 — AdfLagSelectionScaleStabilityXunitTests.RunAll (test de performance pré-existant, instable/sensible à la charge machine, sans rapport avec ce lot — zone de code non touchée) ; 1 ignoré (Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected, pré-existant, sans rapport)

## PROTECTED COMPONENTS

RiskEngine : UNMODIFIED
RiskPolicy : UNMODIFIED
InstrumentRiskSpecification : UNMODIFIED
EntryTrigger : UNMODIFIED
DecisionArbitrator : UNMODIFIED
TradePlan : UNMODIFIED

## VALIDATION

Replay validation : REQUIRED
Live validation : REQUIRED

## SAFETY

Orders sent : NO
Positions opened : NO
DLL deployed : NO
Commit : NO

## FINAL VERDICT

LIVE/REPLAY SOURCE SELECTION FIXED

STOP — FIN DU LOT 12.11.
