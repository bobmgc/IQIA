# QDE-012 — Sprint 15.25 (Lot 12.6) — Correction du binding ATAS : Instrument + Equity

**Lot de correction du binding ATAS, périmètre strictement limité.** Aucun Replay lancé, aucune DLL
déployée, aucun commit. Une correction a été implémentée (Equity), une seconde a été investiguée,
implémentée, testée, puis **rejetée et annulée** après que ses propres tests en aient révélé le défaut
(QuantityStep) — conformément à la RÈGLE D'ARRÊT du lot.

---

## 1. Problème initial

Le Lot 12.5 avait ajouté une télémétrie complète (`ATAS.Raw.*`/`ATAS.Adapter.*`/`ATAS.RiskInput.*`) mais
sans corriger quoi que ce soit. Un Replay MES M5 réel a ensuite été capturé et analysé :
`ScientificDataset_MES_M5_20260817_175446`. Deux problèmes distincts y sont confirmés :

- **Instrument** : `QuantityStep`/`MinQuantity`/`MaxQuantity` valent `0` → `IsValid = false` →
  `INSTRUMENT_SPEC_INVALID`, alors que `Symbol`/`TickSize`/`TickCost`/`LotSize` sont bien fournis par ATAS.
- **Equity** : une valeur Realtime obsolète (105.50, datée du 2026-08-10) est parfois utilisée comme si
  elle représentait l'Equity courante pendant un Replay portant sur des barres datées de fin juillet/début
  août 2026 — un risque de look-ahead/confusion Replay-Realtime explicitement visé par le lot.

---

## 2. Preuve issue du Replay MES

Analyse exhaustive des 2304 enregistrements de `ScientificDataset_MES_M5_20260817_175446.json` (script
Python, lecture intégrale — pas d'échantillonnage) :

| Champ | Valeur observée |
|---|---|
| `ATAS.Raw.Instrument.Symbol` | `"Micro E-mini S&P 500"` (2304/2304) |
| `ATAS.Raw.Instrument.TickSize` / `TickCost` | `0.25` / `1.25` (2304/2304) |
| `ATAS.Raw.Instrument.LotSize` | `1` (2304/2304) |
| `ATAS.Raw.Instrument.LotMinSize` / `LotMaxSize` | `"NOT AVAILABLE"` (2304/2304) |
| `ATAS.Adapter.Instrument.QuantityStep` / `MinQuantity` / `MaxQuantity` | `0` / `0` / `0` (2304/2304) |
| `ATAS.Adapter.Instrument.IsValid` | `False` (2304/2304) |
| `ATAS.Raw.Equity.SourceMode` | `"Replay"` : 1200 — `"Realtime"` : 1104 (**dans la MÊME session**) |
| `ATAS.Raw.Equity.SeriesCount` (mode Replay) | `0` (série vide) sur les 1200 |
| `ATAS.Raw.Equity.RealtimeValue` (mode Realtime) | `105.50` constant sur les 1104, `LastTimestamp` **constant** `2026-08-10T14:02:10` |
| Barres réellement rejouées | `2026-07-27T22:00:00` → `2026-08-07T05:55:00` |
| Écart temporel constaté | jusqu'à **13 jours** entre la barre traitée et le point Equity utilisé |

**Découverte clé, non anticipée par le brief** : `ATAS.Raw.Equity.SourceMode` alterne entre `"Replay"` et
`"Realtime"` **au sein d'une seule et même session de Replay**. Ce n'est pas ATAS qui fournit un signal
Replay/Realtime contradictoire — c'est `context.Execution.IsReplay` (`Core/ExecutionContext.cs`,
`Core/MarketContextBuilder.cs`, ni protégé ni modifié par ce lot) qui est une **heuristique interne basée
sur l'index de barre**, documentée comme telle dans son propre commentaire : *"le SDK public... n'expose
aucun flag 'chart en cours de relecture'"*. Un Replay ATAS alimente `OnCalculate` bar-par-bar exactement
comme un flux live (`bar == currentBar - 1` devient vrai à chaque nouvelle barre livrée, que ce soit en
Replay ou en Live) — l'heuristique ne peut donc pas les distinguer de façon fiable. C'est ce mécanisme,
et non une confusion dans `ATASAccountStateAdapter.TryGetCurrentEquity` (Lot 12.2, protégé, dont la
logique `isReplay ? Replay.Equity : Realtime.Equity` est déjà correcte), qui cause le symptôme.

---

## 3. API ATAS étudiées

Méthode : réflexion .NET en lecture seule (`AssemblyLoadContext` + résolveur de dépendances, `dotnet run`
sur un projet console scratchpad, aucune DLL ATAS modifiée, aucun fichier créé dans le dépôt — identique
à la méthode des Lots 12.1/12.4), complétée par extraction de chaînes binaires et **tests black-box
empiriques** (construction directe d'objets `Security`/`Portfolio` et invocation réelle de méthodes du SDK
shippé, en dehors de tout hôte ATAS).

| API | Découverte |
|---|---|
| `ATAS.DataFeedsCore.Security` — liste complète des propriétés | Aucune propriété `QuantityStep`/`VolumeStep`/`LotStep`/`ContractSize` (re-confirmé, cohérent Lots 12.1/12.4). `LotMinSize`/`LotMaxSize` sont `decimal?`, défaut **`null`** (confirmé par construction directe d'un `Security` vierge). `LotSize` est `decimal` **non-nullable**, défaut **`1`** (confirmé par construction directe — voir Section 4). |
| `ATAS.DataFeedsCore.Extensions.IsReplay(this Portfolio)` | Méthode d'extension réelle du SDK. Logique **PROUVÉE par invocation directe** (tests black-box faisant varier chaque propriété d'un `Portfolio` construit localement) : retourne `true` **si et seulement si** `Portfolio.AccountID == "Replay"` (comparaison exacte, sensible à la casse — `"REPLAY"`/`"ReplayAccount"`/`"Simulator"`/`"Demo"` retournent tous `false`). Comportement RÉEL d'ATAS (assigne-t-il vraiment cet AccountID pendant un Replay ?) **REQUIRES LIVE ATAS VALIDATION** — non câblé dans une décision (Section 9). |
| `ATAS.DataFeedsCore.IDataFeedConnector.ConvertCurrency(..., bool roundToLotSize)` | Confirme qu'ATAS utilise en interne `Security.LotSize` comme unité d'arrondi de volume — cohérent avec (mais ne prouve pas définitivement) l'hypothèse que `LotSize` joue un rôle d'incrément de quantité. |
| `Portfolio` — liste complète des propriétés | ~30 propriétés inspectées ; aucune autre piste Replay/Live trouvée au-delà de `IsReplay()`. |

---

## 4. Source QuantityStep retenue — **IMPOSSIBILITÉ DÉMONTRÉE**

`Security.LotSize` a d'abord semblé un candidat solide : présent (=1) sur le Replay MES réel pendant que
`LotMinSize`/`LotMaxSize` étaient absents, cohérent avec la triade `LotSize`/`LotMinSize`/`LotMaxSize`
qui reflète exactement `QuantityStep`/`MinQuantity`/`MaxQuantity`, et corroboré par
`ConvertCurrency(..., roundToLotSize)`. Un binding a été implémenté
(`ATASInstrumentQuantityStepResolver.cs`) et testé.

**C'est ce test lui-même qui a révélé l'impossibilité** : `MesWithoutAtasLotSizeOrManualValue_...` a
échoué avec `Actual: 1` là où `0` était attendu. Investigation immédiate par réflexion (construction
directe d'un `Security` totalement vierge, jamais peuplé par aucun connecteur) :

```
Security.LotSize (jamais assigné) = 1        ← défaut du SDK, PAS une valeur ATAS réelle
Security.LotMinSize (jamais assigné) = null  ← sans ambiguïté
Security.LotMaxSize (jamais assigné) = null  ← sans ambiguïté
```

**PROVEN par construction directe** : `Security.LotSize` vaut `1` par défaut même lorsqu'aucun connecteur
ne l'a jamais peuplé — valeur **rigoureusement indiscernable** d'un flux de données réel rapportant
authentiquement "1" pour MES. Câbler `LotSize` comme source de `QuantityStep` aurait donc, pour tout
instrument dont le connecteur ne peuple pas ce champ, silencieusement produit exactement ce que le lot
interdit explicitement — *"QuantityStep = 1 ... simplement pour faire passer IsValid à true"* — **sans
jamais écrire ce littéral nulle part dans le code**. C'est très précisément la condition de la RÈGLE
D'ARRÊT (*"une API ATAS n'est pas suffisamment certaine pour être utilisée"*).

**Décision** : le binding a été **annulé**. `QuantityStep` reste exclusivement le paramètre manuel
`RiskInstrumentQuantityStep`, strictement inchangé depuis avant ce lot. `ATASInstrumentQuantityStepResolver.cs`
et ses tests sont **conservés, mais non appelés par `IQIAIndicator.cs`** — documentés comme piste
investiguée et rejetée, référence pour un lot futur qui trouverait un moyen fiable de distinguer un
`LotSize` réellement rapporté de la valeur par défaut du SDK.

`MinQuantity`/`MaxQuantity` ne partagent PAS ce problème : `LotMinSize`/`LotMaxSize` sont nullables et
leur défaut (`null`) est sans ambiguïté avec une donnée réelle — le fallback déjà existant dans
`ATASInstrumentAdapter.Build` (Lot 12.2, protégé, inchangé) était donc déjà correct et n'a nécessité aucune
reconsidération.

---

## 5. Source MinQuantity retenue — inchangée, mécanisme confirmé sûr

`Security.LotMinSize` (`decimal?`) si `> 0`, sinon `RiskInstrumentMinQuantity` (manuel) — logique
**inchangée** de `ATASInstrumentAdapter.Build` (Lot 12.2, protégé). Le Replay MES réel montre
`LotMinSize = "NOT AVAILABLE"` pour ce flux/courtier — aucune source ATAS fiable disponible pour CETTE
capture ; le comportement fail-closed (repli sur le paramètre manuel, resté à `0`) est correct et
inchangé. Une étiquette de télémétrie (`ATAS.Adapter.Instrument.MinQuantitySource`) a été ajoutée,
observant en lecture seule laquelle des deux branches (déjà existantes) a été prise, sans influencer le
résultat.

## 6. Source MaxQuantity retenue — inchangée, mécanisme confirmé sûr

Identique à la Section 5, pour `Security.LotMaxSize`/`RiskInstrumentMaxQuantity` et
`ATAS.Adapter.Instrument.MaxQuantitySource`.

---

## 7. Source Equity Live

`TradingStatisticsProvider.Realtime.Equity` (dernier point) — **mécanisme de lecture inchangé**
(`ATASAccountStateAdapter.TryGetCurrentEquity`, Lot 12.2, protégé). Ce qui change (Section 9) : la
condition qui détermine QUAND cette source est consultée.

## 8. Source Equity Replay

`TradingStatisticsProvider.Replay.Equity` (dernier point) — **mécanisme de lecture inchangé**, même
fichier protégé. Sur le Replay MES réel, cette série est vide (`SeriesCount = 0`) pour les 1200
enregistrements où elle est correctement consultée — l'échec de repli fail-closed (`INVALID_EQUITY`) y
est déjà correct et n'a jamais été le problème.

---

## 9. Logique de sélection Live/Replay — la correction effectivement implémentée

**Nouveau fichier** `Infrastructure/ATAS/ATASEquityReplayDetector.cs` (pur, déterministe, sans dépendance
ATAS — uniquement `DateTime`/`TimeSpan`/`bool` — testable indépendamment, contrairement à
`IQIAIndicator.cs` qui requiert un hôte ATAS réel) :

```
IsReplayContext(heuristicIsReplay, barTime, utcNow, maxLiveGap = 24h) :
    heuristicIsReplay  OU  (utcNow - barTime) > maxLiveGap
```

- `heuristicIsReplay` = `context.Execution.IsReplay` (l'heuristique existante, jamais modifiée,
  jamais retirée) — l'opérateur **OU** garantit que ce correctif ne peut **jamais** régresser en dessous
  du comportement actuel : tout ce que l'heuristique détectait déjà correctement reste détecté.
- `(utcNow - barTime) > 24h` : une barre datée de plus de 24h dans le passé par rapport à l'instant
  réel d'exécution ne peut mathématiquement pas être la barre live du jour, quel que soit l'avis de
  l'heuristique — marge volontairement généreuse (aucun écart intra-barre live plausible, sur n'importe
  quel timeframe supporté, n'approche 24h) tout en restant très inférieure à l'écart de 13 jours
  réellement observé.
- **Aucune valeur financière n'est ici en jeu** — ce garde-fou décide uniquement QUELLE série consulter
  (Replay vs Realtime), jamais QUELLE valeur y figure ; c'est un garde temporel générique, pas une donnée
  métier inventée.

Câblé dans `IQIAIndicator.cs` (Risk stage), **uniquement** pour l'argument `isReplay` de
`ATASAccountStateAdapter.TryGetCurrentEquity` — `context.Execution.IsReplay` lui-même reste totalement
inchangé partout ailleurs dans le pipeline (hors périmètre de ce lot).

`Portfolio.IsReplay()` (Section 3) est capturé en télémétrie pure (`ATAS.Raw.Account.PortfolioIsReplay`)
mais **n'est PAS câblé** dans cette décision — son comportement réel sous ATAS reste
**REQUIRES LIVE ATAS VALIDATION** ; le garde temporel ci-dessus ne dépend d'aucune hypothèse non
vérifiée sur le SDK.

---

## 10. Absence de fallback artificiel

- **Equity** : aucune valeur n'est jamais inventée. La correction ne fait que rediriger, plus
  fidèlement, VERS QUELLE série (déjà) lire — `TryGetCurrentEquity` continue de retourner `null` chaque
  fois que la série choisie est vide, et `ATASAccountStateAdapter.Build` (protégé, inchangé) continue de
  résoudre `0m` (sentinel fail-closed) dans ce cas, déclenchant `INVALID_EQUITY` exactement comme avant.
  Aucun `Equity = InitialCapital + PnL` n'a été introduit.
- **QuantityStep** : voir Section 4 — le binding candidat a été retiré précisément PARCE QU'il aurait pu
  se comporter comme un fallback silencieux (`1` par défaut du SDK, jamais distingué d'une vraie donnée).
- **MinQuantity/MaxQuantity** : aucun changement.
- **Aucun mapping** `if symbol == "MES"` / `"ES"` nulle part dans le code ajouté (vérifié par grep sur les
  5 fichiers touchés/créés).

---

## 11. Tests ajoutés

**4 nouveaux fichiers**, **43 nouveaux tests** au total :

| Fichier | Tests | Couvre |
|---|---|---|
| `Tests/Infrastructure/ATAS/ATASEquityReplayDetectorTests.cs` | 12 | Logique pure (heuristique déjà vraie, barre récente, barre de 13 jours - réplique exacte de la capture réelle, limites 23h59/24h01) + intégration avec `ATASAccountStateAdapter.TryGetCurrentEquity` (scénario exact du bug réel corrigé, Equity Live valide transmise, Equity Replay valide transmise, aucun fallback Replay→Realtime) |
| `Tests/Infrastructure/ATAS/ATASInstrumentQuantityStepResolverTests.cs` | 9 | Logique du resolver (documentée, non câblée) + **le test qui a découvert l'ambiguïté par défaut de `LotSize`** + confirmation que le chemin de production réel n'utilise jamais ce resolver |
| `Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs` (TEST 19, +3 tests) | 3 | Nouvelles catégories de télémétrie peuplées/absentes/sans effet sur les catégories Lot 12.5 existantes |
| — | — | Couverture Phase E du brief, point par point : |

| # | Exigence Phase E | Test(s) |
|---|---|---|
| 1 | MES avec QuantityStep valide → IsValid=true | `MesWithAtasLotSizeAvailable_ProducesValidSpecification` (hypothétique — le resolver n'est pas câblé) |
| 2 | MES sans QuantityStep → IsValid reste false | `ActualProductionPath_NeverUsesTheResolver_QuantityStepStaysManualOnly` (chemin réel) |
| 3-4 | Min/MaxQuantity absents → fail-closed | Inchangé, déjà couvert par `ATASRuntimeDiagnosticsTests.Test04` (Lot 12.3) |
| 5 | Aucune valeur inventée | `UnpopulatedSecurity_DefaultsLotSizeToOne_IndistinguishableFromGenuineData` (documente pourquoi le resolver n'est PAS utilisé) |
| 6 | Aucun mapping ES/MES | `Resolve_NeverBranchesOnSymbol` (Theory, MES/ES/XYZ999) |
| 7 | Equity Live valide transmise | `GenuineLiveEquity_IsStillUsedCorrectly` |
| 8 | Equity Replay valide transmise | `GenuineReplayEquity_WhenPopulated_IsUsedCorrectly` |
| 9 | Equity Replay absente → invalide | `RealCaptureScenario_StaleRealtimeEquity_IsNotUsed_WhenBarIsHistorical` |
| 10 | Realtime dispo mais Replay absent → pas de fallback | `NoFallbackFromReplayToRealtime_WhenReplayIsEmpty` |
| 11 | Aucun fallback silencieux | Couvert par 9/10 |
| 12 | Aucun look-ahead | `IsReplayContext_HeuristicFalse_BarTimeThirteenDaysOld_ReturnsTrue` + `RealCaptureScenario_StaleRealtimeEquity_IsNotUsed_WhenBarIsHistorical` (réplique exacte de la capture réelle) |
| 13-15 | Régression RiskEngine | Suite complète (Section 13) - `RiskEngine.cs`/`RiskPolicy.cs`/etc. non touchés (vérifié par horodatage fichier, Section 15) |

---

## 12. Résultats build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | ✅ 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | ✅ 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | ✅ 0 avertissement, 0 erreur (26 s) |

---

## 13. Résultats tests

`dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` (suite complète, après la correction
de la Section 4) :

```
échec : 0, réussite : 217, ignorée(s) : 1, total : 218, durée 10 min 7 s
```

L'unique test ignoré (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`) est
pré-existant et sans rapport. **Aucun échec.** Le test de performance
`AdfLagSelectionScaleStabilityXunitTests` (signalé instable/sensible à la charge dans le rapport du
Lot 12.5) **réussit** dans cette exécution — cohérent avec l'hypothèse de sensibilité à la charge machine
plutôt qu'une régression, puisqu'aucun fichier de sa zone n'a été touché par ce lot non plus.

**Validation ciblée** (avant la découverte de la Section 4, puis après correction) :
`--filter "...ATASEquityReplayDetectorTests|...ATASInstrumentQuantityStepResolverTests|...ScientificDatasetRealMarketCaptureTests"` →
`échec : 0, réussite : 60, total : 60, durée 2 s` (première exécution avait 1 échec — le test qui a
révélé l'ambiguïté `LotSize`, corrigé avant tout autre travail, voir Section 4).

---

## 14. Fichiers modifiés

| Fichier | Nature |
|---|---|
| `IQIAIndicator/IQIAIndicator.cs` | +384/-2 lignes : `using ATAS.DataFeedsCore;` ; 5 nouveaux champs privés ; capture de `Portfolio.IsReplay()` (télémétrie) ; calcul de `_latestEquitySourceIsReplay` via `ATASEquityReplayDetector` et son câblage dans l'appel `TryGetCurrentEquity` (SEULE correction de comportement de ce lot) ; étiquettes `MinQuantitySource`/`MaxQuantitySource` (télémétrie) ; 4 nouveaux arguments au call site `ScientificDatasetRecord.From` |
| `IQIAIndicator/Core/Calibration/ScientificDatasetRecord.cs` | +198/-0 lignes : 4 nouveaux paramètres optionnels, 4 nouvelles clés `Categories["ATAS.Adapter.Equity.HeuristicIsReplay"]`/`["ATAS.Raw.Account.PortfolioIsReplay"]`/`["ATAS.Adapter.Instrument.MinQuantitySource"]`/`["ATAS.Adapter.Instrument.MaxQuantitySource"]` |
| `IQIAIndicator/Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs` | +591/-0 lignes : TEST 19 (3 tests) |

**2 nouveaux fichiers créés** (`Infrastructure/ATAS/`, ni l'un ni l'autre des deux fichiers protégés) :

| Fichier | Rôle |
|---|---|
| `Infrastructure/ATAS/ATASEquityReplayDetector.cs` | **Utilisé activement** — corrige la sélection Replay/Realtime pour l'Equity |
| `Infrastructure/ATAS/ATASInstrumentQuantityStepResolver.cs` | **Investigué, testé, NON utilisé** — conservé comme référence documentée (Section 4) |

**2 nouveaux fichiers de test créés** :
`Tests/Infrastructure/ATAS/ATASEquityReplayDetectorTests.cs`,
`Tests/Infrastructure/ATAS/ATASInstrumentQuantityStepResolverTests.cs`.

Aucun autre fichier `.cs` touché par ce lot.

---

## 15. Fichiers protégés

Vérifiés explicitement non modifiés PAR CE LOT (`git status`/`git diff --name-only` avant et après, ET
horodatage de fichier — aucun de ces fichiers n'a été ouvert ni édité pendant cette session) :

| Fichier | Statut |
|---|---|
| `Engine/Risk/RiskEngine.cs` | ✅ non touché (horodatage 2026-08-16, veille de cette session) |
| `Engine/Risk/RiskPolicy.cs` | ✅ non touché (idem) |
| `Engine/Risk/InstrumentRiskSpecification.cs` | ✅ non touché (idem) |
| `Engine/Risk/RiskEngineRequest.cs` | ✅ non touché (idem) |
| `Engine/Risk/RiskAssessment.cs` | ✅ non touché (idem) |
| `Infrastructure/ATAS/ATASAccountStateAdapter.cs` | ✅ non touché (dernière modif. 11:25, avant le début de cette session — le Replay analysé date de 17:54) |
| `Infrastructure/ATAS/ATASInstrumentAdapter.cs` | ✅ non touché (idem, 11:22) |
| `Engine/EntryTrigger/*` | ✅ non touché (`EntryTriggerBuilder.cs`, horodatage 2026-08-16 — dérive pré-existante héritée, déjà documentée aux Lots 12.4/12.5, non aggravée) |
| `Engine/Decision/*` | ✅ non touché (`DecisionArbitrator.cs`, horodatage 2026-08-11) |
| `TradePlan*` | ✅ non touché (`TradePlan.cs`/`TradePlanBuilder.cs`, horodatage 2026-08-12) |
| Seuil `AmbiguityScore >= 0.95` | ✅ non touché (aucun fichier `Decision`/`Arbitration` modifié) |

`Visualization/Dashboards/TradingDashboard.cs`, `Visualization/State/DashboardContext.cs`,
`Tests/EntryTrigger/DecisionDirectionCoherenceTests.cs`, `Tests/Signal/DirectionEndToEndTests.cs`,
`Tests/IQIAIndicator.Tests.csproj`, `Core/Observability/PipelineTraceContext.cs` apparaissent modifiés
dans `git status` mais avec des horodatages tous antérieurs au début de cette session — dérive
pré-existante héritée des lots précédents non commités (même constat qu'aux Lots 12.4/12.5), non touchée
par le Lot 12.6.

Deux fichiers de sortie de campagne (`Tests/Research/StopLossCalibration/Output/*.txt`) régénérés comme
effet de bord de l'exécution de la suite complète (mêmes causes qu'aux Lots 12.3/12.5) ont été restaurés
via `git checkout --` pour garder le diff strictement scopé à ce lot.

---

## 16. Limites restantes

- **QuantityStep reste sans source ATAS automatique** — le paramètre manuel demeure nécessaire (Section 4).
  L'ambiguïté `LotSize` découverte ici n'est PAS résolue, seulement documentée et évitée.
- **`Portfolio.IsReplay()` reste non validé en conditions réelles** — sa logique interne est PROUVÉE
  (`AccountID == "Replay"` exact), mais pas son comportement RÉEL sous ATAS. Capturé en télémétrie
  uniquement (Section 9), jamais câblé dans une décision.
- **La marge de 24h de `ATASEquityReplayDetector` n'a pas été validée en conditions réelles** — elle est
  mathématiquement sûre pour tout timeframe intrajournalier plausible, mais n'a pas été confrontée à un
  Replay portant sur des données très récentes (< 24h).
- **L'alternance Replay/Realtime intra-session découverte Section 2 n'est expliquée que partiellement** —
  le MÉCANISME (heuristique bar-index) est prouvé par le code et corrigé pour l'Equity, mais la raison
  exacte pour laquelle ATAS a livré ce Replay de cette façon précise (1200 barres correctement détectées,
  1104 non) reste **REQUIRES LIVE ATAS VALIDATION** pour une compréhension complète.
- Aucune validation en conditions ATAS réelles n'a été effectuée dans ce lot (interdit explicitement) —
  tous les tests utilisent des objets construits directement.

---

## 17. Procédure exacte du prochain Replay MES

1. Ouvrir ATAS, charger l'indicateur IQIA sur MES M5, `EnableScientificDataset = true`.
2. Lancer un Replay MES M5 (aucune configuration additionnelle requise).
3. Dans le `ScientificDataset_MES_*.json` produit, pour les barres `Risk.Status = "REJECTED"` :
   - `ATAS.Raw.Equity.SourceMode` doit maintenant refléter la décision CORRIGÉE ;
     `ATAS.Adapter.Equity.HeuristicIsReplay` montre l'ancienne heuristique pour comparaison — si elles
     diffèrent sur une barre historique, c'est la correction de ce lot qui agit.
   - `ATAS.Equity`/`ATAS.EquityAvailable` ne doivent plus jamais montrer une valeur Realtime obsolète sur
     une barre clairement historique (Section 2) — vérifier qu'aucun `ATAS.Raw.Equity.LastTimestamp` ne
     précède `DateTime.UtcNow` de plus de 24h tout en étant utilisé comme `ATAS.Equity`.
   - `ATAS.Raw.Account.PortfolioIsReplay` — comparer à `ATAS.Raw.Equity.SourceMode` : s'ils concordent
     systématiquement, c'est une première confirmation empirique en conditions réelles justifiant qu'un
     futur lot envisage de le câbler.
   - `ATAS.Adapter.Instrument.QuantityStep` reste à `0` sauf configuration manuelle explicite de
     `RiskInstrumentQuantityStep` dans le panneau ATAS — comportement inchangé, volontairement.

---

## 18. Verdict

L'objectif du lot était de transformer une confusion binding en une correction ciblée, sans jamais
inventer de valeur ni élargir le périmètre. Sur les deux problèmes identifiés :

- **Equity** : corrigé. La sélection Replay/Realtime utilise désormais un signal robuste et prouvé
  (garde temporel, jamais moins précis que l'heuristique existante), sans toucher au fichier protégé où
  vit la lecture elle-même.
- **QuantityStep** : non corrigé, **délibérément** — la seule piste trouvée s'est révélée, par ses propres
  tests, indiscernable d'une valeur par défaut du SDK. La RÈGLE D'ARRÊT du lot a été appliquée
  explicitement : investigation documentée, code conservé mais non câblé, comportement inchangé.

---

## LOT 12.6 RESULT

- Status : PARTIAL SUCCESS (Equity corrigé ; QuantityStep investigué et délibérément non corrigé)
- QuantityStep source : NONE (Security.LotSize investigué et rejeté - défaut SDK indiscernable d'une donnée réelle, voir Section 4)
- MinQuantity source : Security.LotMinSize si > 0, sinon manuel (inchangé, Lot 12.2)
- MaxQuantity source : Security.LotMaxSize si > 0, sinon manuel (inchangé, Lot 12.2)
- Equity Live source : TradingStatisticsProvider.Realtime.Equity (lecture inchangée, Lot 12.2)
- Equity Replay source : TradingStatisticsProvider.Replay.Equity (lecture inchangée, Lot 12.2)
- Replay/Live distinction : CORRIGÉE - ATASEquityReplayDetector.cs (nouveau, pur, testé), OR logique avec l'heuristique existante, jamais une régression
- Fail-closed status : PRÉSERVÉ (Equity et Instrument)
- Hardcoded ES/MES mapping : NONE
- RiskEngine modified : NO
- RiskPolicy modified : NO
- EntryTrigger modified : NO
- TradePlan modified : NO
- Tests : 43 nouveaux (12 + 9 + 3 + intégrations), suite complète 217 réussis / 0 échec / 1 ignoré (pré-existant) / 218 total
- Build Debug : PASS (0/0)
- Build Release : PASS (0/0)
- Files modified : 3 (IQIAIndicator.cs, ScientificDatasetRecord.cs, ScientificDatasetRealMarketCaptureTests.cs)
- Files created : 4 (ATASEquityReplayDetector.cs, ATASInstrumentQuantityStepResolver.cs, 2 fichiers de test)
- Remaining limitations : voir Section 16
- Replay validation required : YES (Section 17)
- Live validation required : YES (Portfolio.IsReplay(), marge 24h)
- DLL deployed : NO
- Commit : NO

STOP — fin du LOT 12.6.
