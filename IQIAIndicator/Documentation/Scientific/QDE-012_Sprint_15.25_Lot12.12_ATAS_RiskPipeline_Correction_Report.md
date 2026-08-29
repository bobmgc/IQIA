# QDE-012 — Sprint 15.25 (Lot 12.12) — Diagnostic et correction intégrée ATAS / Risk Engine / Execution Context

**Lot de correction ciblée, multi-problèmes.** Aucun ordre envoyé, aucune position ouverte, aucun
`SubmitOrder`/`Buy()`/`Sell()` ajouté ou modifié, aucune DLL déployée, aucun commit automatique. Toutes
les corrections sont minimales, justifiées ligne par ligne par une trace de code réelle, et respectent la
liste des fichiers protégés (Section 13 du brief) : `RiskEngine.cs`, `RiskPolicy.cs`,
`InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`, `DecisionArbitrator.cs`,
`EntryTriggerBuilder.cs`, `TradePlanBuilder.cs` — **aucun de ces fichiers n'a été modifié par ce lot**
(confirmé par `git status`, Section 4 ci-dessous).

---

## 1. Problèmes observés (rappel du brief)

| # | Problème | Résumé de l'observation |
|---|---|---|
| A | Execution Context incohérent | `Execution.IsReplay` (DEBUG panel) affichait `False` à la fois sur une capture en Chart Replay et sur une capture réellement live |
| B | Risk Engine NOT AVAILABLE en live | Chart actif, compte Rithmic visible, hors Replay — pourtant Status/Instrument/Capital/Equity/... tous `NOT AVAILABLE`, `Current Bar = 0`, `Accepted = 0` |
| C | Replay vs Live mal distingués | Aucune résolution explicite/testable du contexte, risque de confondre les deux |
| D | Equity | Vérifier absence de mélange Replay/Realtime, absence de fallback, cohérence du timestamp |
| E | Capital | Ne jamais confondre Capital (config) avec Balance/Equity ; afficher les trois séparément si disponibles |
| F | Instrument Spec (QuantityStep) | `INSTRUMENT_SPEC_INVALID` sur MES malgré `QuantityStep=0` — vérifier si une source ATAS fiable existe |
| G | Stop Loss | `SL NOT PRODUCED` / `SIGNAL_ONLY` — comportement à clarifier, pas de stratégie SL à inventer |
| H | Rejets Risk Engine | Vérifier l'indépendance réelle de `INVALID_EQUITY`/`INSTRUMENT_SPEC_INVALID`/`INVALID_STOP_LOSS` |
| I | Dashboard | Ne jamais afficher NOT AVAILABLE par simple non-propagation, ni ACCEPTED reconstruit côté renderer |
| J | Dataset/Télémétrie | Garder `ATAS.Raw.*`/`ATAS.Adapter.*`/`ATAS.RiskInput.*` strictement distincts |

---

## 2. Cause racine de chaque problème

### A/C — Execution Context / Replay vs Live

**Cause racine confirmée par trace de code.** Le Lot 12.11 avait déjà introduit une hiérarchie de preuves
correcte (`Portfolio.IsReplay()` en priorité, garde temporel 24h, heuristique bar-index en dernier
recours — `ATASEquityReplayDetector.IsReplayContext`), mais cette décision corrigée n'était utilisée **que**
pour sélectionner la source d'Equity, à l'intérieur de l'étage Risk d'`OnCalculate`. Elle n'était **jamais
propagée** vers `DashboardContext`. Tous les consommateurs qui affichent "sommes-nous en Replay"
lisaient directement `context.Execution.IsReplay` — l'ancienne heuristique bar-index
(`Core/MarketContextBuilder.cs`), documentée comme non fiable depuis le Lot 12.6 et prouvée fausse en
pratique par une capture réelle (Lot 12.10) :

- `DebugDashboard.cs` (panneau EXECUTION CONTEXT) : lisait `context.Execution.IsReplay` brut.
- `DashboardManager.cs` : `bool showReplay = context.Execution?.IsReplay == true;` — décidait d'afficher
  le widget Replay Monitor à partir de la même heuristique brute.
- `DatasetDashboard.cs` : champ "Replay Status" — même heuristique brute.

C'est exactement l'observation du Problème A : une heuristique bar-index (`bar <= _maxRealtimeBar &&
!isRealtime`) peut légitimement produire `False` pendant un Replay réel (elle traite chaque barre reçue
une par une, indiscernable d'un flux live du point de vue de l'indicateur — documenté depuis le Lot 12.6)
et `True`/`False` de façon incohérente en live selon l'historique de barres déjà vues par ce builder.
Le Risk Engine, lui, utilisait déjà la bonne réponse (Lot 12.11) — mais rien d'autre dans le pipeline n'y
avait accès.

### B — Risk Engine NOT AVAILABLE en live

**Deux lacunes structurelles confirmées par trace de code complète** (`OnCalculate → validation →
Decision/Fusion/Methodology/Entry/TradePlan → étage Risk → DashboardContext → TradingDashboard`), toutes
deux capables de produire exactement le symptôme observé, indépendamment de la présence d'un candidat
directionnel :

1. **Builder figé au bar 0.** `IQIAIndicator.cs` ne (re)construit `_builder`
   (`Core.MarketContextBuilder`) qu'une seule fois, au bar 0, en y gravant `InstrumentInfo?.TickSize`
   lu à cet instant précis. Si ATAS n'a pas encore peuplé `TickSize` à ce moment exact (plausible pendant
   la passe de calcul historique initiale, avant résolution complète des métadonnées d'instrument),
   `MarketContextValidator.CheckInstrument` rejette **toutes** les barres suivantes pour le reste de la
   session ("TickSize invalide : 0") — y compris des milliers de barres live bien après que
   l'instrument soit devenu disponible — gelant tout le pipeline aval (étage Risk, BarIndex, dataset)
   sans aucune trace de diagnostic.
2. **Aucune isolation d'exception dans l'étage Risk.** Ce même étage lit directement plusieurs propriétés
   possédées par ATAS (`TradingManager`, `.Portfolio`, `.Security`, `TradingStatisticsProvider`) sans
   garde. Avant ce lot, `catch (Exception exception) { riskTrace.Fail(exception); throw; }` **relançait
   systématiquement** toute exception survenue ici — alors même que `_latestEvidence`/`_latestFusionResult`/
   `_latestDecisionResult` (assignés plus tôt dans la même méthode, avant l'étage Risk) avaient déjà été
   mis à jour ce bar-là. C'est le seul chemin sous lequel la porte de rendu du dashboard
   (`_latestEvidence is not null && ...`) est satisfaite — le panneau s'affiche — pendant que
   `_latestRiskAccountState`/`_latestRiskInstrumentSpec`/`_latestRiskAssessment` restent bloqués à `null`
   depuis le début de la session, donnant exactement "Status/Instrument/Capital/... NOT AVAILABLE" —
   et ce, de façon totalement silencieuse (ATAS rappelant `OnCalculate` bar après bar contre le même état
   ATAS non résolu, potentiellement pour toute la session).

**Aucune des deux causes n'a pu être confirmée à 100 % comme LA cause exacte de la capture décrite dans le
brief** — le fichier de capture original n'était pas disponible pour cette analyse (contrairement aux
Lots 12.5-12.11 qui disposaient d'un export `.json` réel). Conformément à la règle fail-closed du brief
("si une information ATAS ne peut pas être démontrée, STOPPER cette branche"), les deux lacunes sont
corrigées comme **durcissements structurels** — ni l'une ni l'autre n'affaiblit une décision du Risk
Engine, n'invente de valeur, et toutes deux **augmentent strictement la traçabilité** (une exception
capturée est désormais visible là où il n'y avait auparavant qu'un silence total) — voir Section 16 pour
la validation live encore requise.

### D — Equity

**Déjà entièrement corrigé par le Lot 12.11**, vérifié par relecture complète de
`ATASAccountStateAdapter.TryGetCurrentEquity`/`ATASEquityReplayDetector.IsReplayContext` : aucun mélange
Replay/Realtime, aucun fallback dans un sens ou dans l'autre, sélection strictement gouvernée par la
hiérarchie de preuves du Lot 12.11. Le sous-point "timestamp de l'Equity incohérent avec la barre
courante" (brief, Section 4) a été **investigué mais délibérément NON implémenté** ce lot : aucun seuil
de tolérance n'a de base empirique aujourd'hui (contrairement au garde de 24h du Lot 12.6, calibré sur une
capture réelle) — un seuil inventé risquerait de rejeter à tort une Equity valide dans une session live à
faible activité (exactement le scénario resté `INCONCLUSIVE` au Lot 12.10). Conserve le comportement
fail-closed existant, inchangé. Voir Section 16.

### E — Capital

Déjà correct : `RiskInitialCapital` reste une configuration manuelle pure, jamais remplacée par
Balance/Equity/BuyingPower (confirmé par lecture de `ATASAccountStateAdapter.Build` — `initialCapital` est
un paramètre transmis tel quel). **Lacune trouvée** : `AccountState.CurrentBalance` (déjà calculé chaque
bar depuis le Lot 12.2, `portfolio?.Balance`) n'était **jamais affiché** — seuls Capital (=InitialCapital)
et Equity apparaissaient sur le dashboard, risquant précisément la confusion que le brief interdit
("ne pas appeler Capital une donnée qui est en réalité Balance").

### F — Instrument Spec / QuantityStep

Déjà correct et déjà entièrement investigué (Lots 12.1/12.4/12.6) : `QuantityStep` reste exclusivement le
paramètre manuel ; `Security.LotSize` a été exploré puis explicitement rejeté (son défaut SDK, `1`, est
indiscernable d'une vraie donnée de flux — voir `ATASInstrumentQuantityStepResolver.cs`, non câblé,
conservé comme preuve testée pour un lot futur). Aucun hardcodage MES/ES nulle part. Aucun changement
nécessaire.

### G — Stop Loss

Déjà correct : `RiskEngine.Evaluate` (protégé) reçoit `StopLoss = request.StopLoss` et le renvoie
**inchangé** dans `RiskAssessment.StopLoss` — il n'existe donc, par construction des données, qu'une seule
façon pour `RiskAssessment.StopLoss` d'être `null` : que `TradePlan.StopLoss` l'ait été. Un SL présent
mais invalide (mauvais côté de l'entrée) reste visible avec sa vraie valeur de prix, et
`INVALID_STOP_LOSS` apparaît dans `RejectionReasons` — les deux cas ("SL absent du TradePlan" vs "SL
invalide après validation") sont donc déjà distinguables sans ambiguïté à partir des données existantes.
Aucun changement nécessaire.

### H — Rejets Risk Engine indépendants

Déjà correct dans `RiskEngine.cs` (protégé, relu intégralement) : Phase 1 (Capital/Equity), Phase 2
(Instrument), Phase 4 (Stop Loss) sont trois blocs de code strictement indépendants, chacun n'ajoutant sa
propre raison qu'à partir de ses propres entrées. **Non testé explicitement selon la matrice à 4 cas exacte
du brief** avant ce lot — comblé Section 5/tests.

### I — Dashboard

Déjà largement correct : `RiskDashboardPresenter` est un consommateur strictement passif (jamais de
recalcul de risque/sizing/R:R). Le point resté faible : un `RiskAssessment` null pour "pas de candidat ce
bar" et pour "l'étage Risk n'a jamais tourné à cause d'une exception ATAS" (Problème B) étaient rendus de
façon **identique** ("NOT AVAILABLE" générique) — corrigé Section 3.

### J — Dataset/Télémétrie

Déjà largement implémenté (Lots 12.5/12.6/12.11) : les trois niveaux `ATAS.Raw.*`/`ATAS.Adapter.*`/
`ATAS.RiskInput.*` sont bien distincts et jamais recalculés les uns à partir des autres. Deux nouvelles
clés ajoutées ce lot (`ATAS.Context.Resolved`, `ATAS.RiskStage.Exception`) pour couvrir les Problèmes A/C
et B.

---

## 3. Modifications effectuées

### Problème A/C — Contexte résolu propagé à tous les consommateurs

- **`Core/AtasDataContext.cs`** (nouveau) : `enum AtasDataContext { Unknown, Live, Replay }`.
- **`Infrastructure/ATAS/AtasDataContextResolver.cs`** (nouveau) : fonction pure `Resolve(bool
  hasBeenResolved, bool isReplay)` — enveloppe la décision déjà calculée par
  `ATASEquityReplayDetector.IsReplayContext` (Lot 12.6/12.11, **inchangé**), jamais une seconde décision.
- **`IQIAIndicator.cs`** : nouveau champ `_latestAtasDataContext`, calculé juste après
  `_latestEquitySourceIsReplay` (même ligne de décision, aucune logique nouvelle) ; propagé dans
  `DashboardContext.AtasContext`.
- **`Visualization/State/DashboardContext.cs`** : nouveaux champs `AtasContext`/`RiskStageError`.
- **`Visualization/Dashboards/DashboardManager.cs`** : `showReplay` (gate du widget Replay Monitor) lit
  désormais `context.AtasContext == AtasDataContext.Replay` au lieu de l'heuristique brute.
- **`Visualization/Dashboards/DatasetDashboard.cs`** : champ "Replay Status" lit `context.AtasContext`.
- **`Visualization/Dashboards/DebugDashboard.cs`** : nouvelle section "ATAS CONTEXT (RÉSOLU)" ; le champ
  brut `IsReplay` est conservé (transparence diagnostique) mais relabellé "IsReplay (heuristique brute)"
  pour ne plus être confondu avec la réponse corrigée.

### Problème B — Durcissement de l'étage Risk

- **`Core/MarketContextBuilder.cs`** : champs devenus mutables (au lieu de `readonly`) ; nouvelle propriété
  `TickSize` et nouvelle méthode `RefreshInstrument(...)` qui ré-applique l'instantané instrument SANS
  toucher `_firstBarTime`/`_maxRealtimeBar` (l'état de l'heuristique Replay).
- **`IQIAIndicator.cs`** : la condition de (re)construction du builder gagne une branche
  `else if (_builder.TickSize <= 0m && InstrumentInfo?.TickSize > 0m)` qui appelle `RefreshInstrument`
  plutôt que de remplacer entièrement le builder (préserve tout le reste de son état).
- **`IQIAIndicator.cs`** : le `catch` de l'étage Risk ne relance plus systématiquement — il dégrade
  proprement (Account/Instrument/Request/Assessment repassent à `null` pour ce bar, exactement la même
  forme qu'un bar sans candidat évaluable), capture l'exception exacte dans
  `_latestRiskStageError`, et **laisse le reste d'`OnCalculate` continuer** (BarIndex, collecte dataset).
  Aucune ligne du `try` elle-même n'a été modifiée.

### Problème E — Balance affiché séparément

- **`Visualization/Rendering/RiskDashboardPresenter.cs`** : nouveau champ `Balance` sur `RiskDashboardView`,
  lu depuis `AccountState.CurrentBalance` (déjà calculé depuis le Lot 12.2, protégé, inchangé).
- **`Visualization/Dashboards/TradingDashboard.cs`** : nouvelle ligne "Balance" dans le panneau RISK
  ENGINE, entre Capital et Equity.

### Problème I — Distinction "pas de candidat" vs "exception ATAS"

- **`Visualization/Rendering/RiskDashboardPresenter.cs`** : nouveau paramètre optionnel
  `riskStageError` ; quand `risk` est `null` ET qu'une erreur d'étage a été capturée, `StatusText`
  devient `"NOT AVAILABLE (ATAS ERROR)"` (au lieu du `"NOT AVAILABLE"` générique) et `Reason` porte le
  message d'exception exact — jamais un texte générique.

### Problème J — Nouvelles clés de télémétrie

- **`Core/Calibration/ScientificDatasetRecord.cs`** : deux nouveaux paramètres optionnels/finaux
  (`atasContext`, `riskStageError`) → `ATAS.Context.Resolved` et `ATAS.RiskStage.Exception`.

### D, F, G, H — Aucun changement de code (vérifiés corrects, documentés Section 2)

---

## 4. Fichiers modifiés

**Fichiers de logique modifiés** (7) :
`Core/MarketContextBuilder.cs`, `IQIAIndicator.cs`, `Visualization/State/DashboardContext.cs`,
`Visualization/Dashboards/DashboardManager.cs`, `Visualization/Dashboards/DatasetDashboard.cs`,
`Visualization/Dashboards/DebugDashboard.cs`, `Visualization/Dashboards/TradingDashboard.cs`,
`Visualization/Rendering/RiskDashboardPresenter.cs`, `Core/Calibration/ScientificDatasetRecord.cs`.

**Fichiers créés** (2 fichiers de production + 4 fichiers de test + 1 wrapper xUnit) :
`Core/AtasDataContext.cs`, `Infrastructure/ATAS/AtasDataContextResolver.cs` ;
`Tests/Core/MarketContextBuilderSelfHealingTests.cs`,
`Tests/Infrastructure/ATAS/AtasDataContextResolverTests.cs`,
`Tests/Risk/RiskRejectionReasonIndependenceTests.cs`,
`Tests/XunitWrappers/RiskRejectionReasonIndependenceXunitTests.cs` ;
`Tests/Visualization/RiskDashboardPresenterTests.cs` étendu (6 nouveaux cas).

**Fichier projet modifié** : `Tests/IQIAIndicator.Tests.csproj` (ajout de la référence `ATAS.Indicators`,
nécessaire pour construire `ATAS.Indicators.IndicatorCandle` dans les nouveaux tests
`MarketContextBuilderSelfHealingTests.cs` — la `ProjectReference` vers `IQIAIndicator.csproj` n'expose pas
transitivement ses propres références `HintPath` privées).

**Fichiers protégés** (Section 13 du brief) : **AUCUN modifié** — `RiskEngine.cs`, `RiskPolicy.cs`,
`InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`, `DecisionArbitrator.cs`,
`EntryTriggerBuilder.cs`, `TradePlanBuilder.cs` restent tels qu'avant ce lot (`EntryTriggerBuilder.cs`
porte une modification du Lot 9, antérieure à ce lot et hors de son périmètre — voir `git diff`, seul le
seuil `AmbiguityGateThreshold` y a été touché, par un lot précédent, non par celui-ci).

---

## 5. Tests ajoutés

| Fichier | Cas | Couvre |
|---|---|---|
| `Tests/Core/MarketContextBuilderSelfHealingTests.cs` | 6 `[Fact]`/`[Theory]` (9 cas au total) | `TickSize`/`RefreshInstrument` (Problème B, auto-guérison) |
| `Tests/Infrastructure/ATAS/AtasDataContextResolverTests.cs` | 5 `[Fact]` | `AtasDataContextResolver.Resolve` + **test d'intégration critique obligatoire (brief Section 12)** : Scénario A (Replay) et Scénario B (Live), reproduisant exactement les combinaisons `Portfolio.IsReplay`/`Execution.IsReplay`/`Execution.IsRealtime` du brief |
| `Tests/Risk/RiskRejectionReasonIndependenceTests.cs` | 4 cas (`RunAll`) | Matrice exacte à 4 cas du brief Section 8 : SL seul invalide, Equity seule invalide, Instrument seul invalide, les trois invalides — chaque cas vérifie explicitement l'ABSENCE des autres raisons |
| `Tests/Visualization/RiskDashboardPresenterTests.cs` | +6 cas (Test08-13) | Balance affiché séparément de Capital (Problème E) ; distinction "pas de candidat" vs "exception ATAS" (Problème B/I) |

Total : **24 nouveaux cas de test**, tous dans des fichiers/paramètres optionnels-finaux — aucune
signature existante cassée, aucun test préexistant modifié dans sa logique.

---

## 6. Résultats build Debug

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | ✅ 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | ✅ 0 avertissement, 0 erreur |

## 7. Résultats build Release

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | ✅ 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Release` | ✅ 0 avertissement, 0 erreur |

## 8. Résultats tests complets

Tests ciblés Lot 12.12 (`AtasDataContextResolverTests`, `MarketContextBuilderSelfHealingTests`,
`RiskDashboardPresenterXunitTests`, `RiskRejectionReasonIndependenceXunitTests`,
`ATASEquityReplayDetectorTests` — non-régression Lot 12.11) : **43/43 réussis**.

Suite complète (`dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug`) :

**échec : 0, réussite : 247, ignorée(s) : 1, total : 248, durée 8 min 1 s.**

L'unique test ignoré (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`) est
pré-existant, déjà documenté comme sans rapport depuis les Lots précédents. **Aucun échec** cette
exécution — y compris `AdfLagSelectionScaleStabilityXunitTests.RunAll`, le test de performance
instable/sensible à la charge machine déjà documenté aux Lots 12.5/12.6/12.11 (échoue de façon
intermittente selon un pattern connu, zone de code non touchée par ce lot) : il est passé cette fois,
cohérent avec son propre historique documenté ("réussit dans cette exécution").

**Arithmétique cohérente** : 248 = 234 (total Lot 12.11) + 14 (nouveaux cas de test visibles xUnit ce
lot : 8 dans `MarketContextBuilderSelfHealingTests`, 5 dans `AtasDataContextResolverTests`, 1 nouveau
wrapper `RiskRejectionReasonIndependenceXunitTests` — les 6 nouveaux cas ajoutés à
`RiskDashboardPresenterTests.RunAll()` ne comptent pas séparément car ce `RunAll()` était déjà
enveloppé par un seul `[Fact]` xUnit préexistant). Aucun test perdu ni dupliqué.

**Effet de bord observé et écarté** : une vérification intermédiaire pendant l'exécution de la suite
complète a montré `A1_vs_A2_comparison.csv`/`A1_vs_A2_ratio_analysis.csv` (fichiers de sortie de la
campagne de calibration Stop Loss, sans rapport avec ce lot) temporairement réduits de ~112 600 à
~26 700 lignes — un instantané pris pendant que le test de campagne réécrivait ces fichiers, pas une
perte de données. Une fois la suite terminée, `git status` confirme que ces deux fichiers sont revenus
identiques au commit de référence ; seuls `A1_calibration_summary.txt`/`campaign_summary.txt` restent
modifiés, avec un delta anodin (uniquement le champ `Elapsed=`) — le même type de churn déjà présent
dans l'état initial du dépôt avant ce lot.

---

## 9. Comportement Replay

Inchangé, non-régression confirmée (`ATASEquityReplayDetectorTests.Integration_ReplayStillProtected_...`,
toujours vert) : `Portfolio.IsReplay()==true` → `AtasContext=Replay` inconditionnel, `Replay.Equity` vide →
`INVALID_EQUITY`. Nouveau depuis ce lot : ce même `Replay` est désormais visible de façon cohérente sur
**tous** les panneaux (DEBUG, DATASET, widget Replay Monitor), plus seulement dans la sélection de source
Equity.

## 10. Comportement Live

`Portfolio.IsReplay()==false` sur une barre récente → `AtasContext=Live`, `EquitySource=Realtime` — même
quand l'ancienne heuristique bar-index dit encore `True` (correctif Lot 12.11, désormais **visible** sur
le dashboard grâce à ce lot, alors qu'avant ce lot le DEBUG panel aurait continué à afficher l'heuristique
brute contradictoire).

## 11. Equity source

Inchangé (Lot 12.11) : Replay → `Replay.Equity` exclusivement ; Live → `Realtime.Equity` exclusivement ;
aucun mélange, aucun fallback. Vérification du timestamp de l'Equity : investiguée, non implémentée ce
lot faute de base empirique pour un seuil (Section 2/16).

## 12. Instrument source

Inchangé : `QuantityStep` reste exclusivement manuel ; `MinQuantity`/`MaxQuantity` préfèrent
`Security.LotMinSize`/`LotMaxSize` avec repli manuel. Aucun hardcodage MES/ES.

## 13. RiskRequest réel

Inchangé dans sa construction (`RiskEngineRequestFactory.FromTradePlan`, protégé) ; désormais également
`null` (au lieu de construit avec des entrées potentiellement obsolètes) quand l'étage Risk a rencontré une
exception ATAS ce bar (Problème B) — `_latestRiskAccountState`/`_latestRiskInstrumentSpec` sont
explicitement remis à `null` dans ce cas avant que `RiskEngineRequestFactory` ne soit appelé.

## 14. Dashboard

Le panneau RISK ENGINE distingue désormais trois états au lieu de deux : `ACCEPTED`, `REJECTED`,
`NOT AVAILABLE` (pas de candidat) et `NOT AVAILABLE (ATAS ERROR)` (l'étage Risk n'a pas pu s'exécuter) —
ce dernier portant le message d'exception exact dans `Reason`. Le contexte Live/Replay est désormais
cohérent entre le panneau DEBUG, le panneau DATASET et le widget Replay Monitor.

## 15. Sécurité / absence d'ordres

Aucun `SubmitOrder`/`Buy()`/`Sell()` ajouté ou modifié. Aucune ouverture/fermeture de position. Le Risk
Engine reste un moteur de décision pur, jamais consulté pour déclencher une action de trading — confirmé
par relecture intégrale de tous les fichiers modifiés (aucun appel d'ordre nulle part dans ce lot).

## 16. Problèmes restant ouverts

1. **Validation live requise** pour confirmer laquelle (ou lesquelles) des deux causes structurelles du
   Problème B (builder figé / exception non capturée) explique réellement la capture décrite dans le
   brief — le fichier de capture original n'était pas disponible pour cette analyse. Procédure : activer
   `EnableScientificDataset`, observer `ATAS.RiskStage.Exception` et `ATAS.Context.Resolved` sur une
   session live courte (5-15 min, conforme à la préférence déjà exprimée pour ce type de validation).
2. **Vérification du timestamp Equity vs barre courante** (brief, Section 4) : non implémentée, faute de
   seuil calibré empiriquement — nécessite une nouvelle capture live pour établir une marge défendable,
   suivant la même discipline qui a produit le garde de 24h du Lot 12.6.
3. **`Execution.IsReplay` (heuristique brute)** reste utilisée comme dernier recours dans
   `ATASEquityReplayDetector.IsReplayContext` quand `Portfolio.IsReplay()` est indisponible — non modifiée
   par ce lot (hors périmètre, Lot 12.6/12.11), continue de porter le même risque documenté de faux
   positif/négatif dans ce seul cas de repli.

---

# LOT 12.12 RESULT

## CODE

Files modified: Core/MarketContextBuilder.cs ; IQIAIndicator.cs ; Visualization/State/DashboardContext.cs ; Visualization/Dashboards/DashboardManager.cs ; Visualization/Dashboards/DatasetDashboard.cs ; Visualization/Dashboards/DebugDashboard.cs ; Visualization/Dashboards/TradingDashboard.cs ; Visualization/Rendering/RiskDashboardPresenter.cs ; Core/Calibration/ScientificDatasetRecord.cs
Files created: Core/AtasDataContext.cs ; Infrastructure/ATAS/AtasDataContextResolver.cs

## LIVE/REPLAY

Context resolution: unchanged decision (ATASEquityReplayDetector.IsReplayContext, Lot 12.6/12.11) - now propagated end to end via AtasDataContextResolver + DashboardContext.AtasContext to DebugDashboard/DatasetDashboard/DashboardManager (previously Equity-source-selection only)
Fail-closed: PRESERVED (no new Equity value invented; Risk stage ATAS exception degrades to "unavailable", never to a fabricated value)

## EQUITY

Unchanged (Lot 12.11): Replay -> Replay.Equity exclusively; Live -> Realtime.Equity exclusively; no mixing, no fallback
Timestamp consistency check: investigated, NOT implemented (no empirical threshold basis yet - see Section 16)

## INSTRUMENT

QuantityStep: unchanged, manual only
MinQuantity/MaxQuantity: unchanged, ATAS-preferred with manual fallback
No hardcoded MES/ES anywhere

## RISK ENGINE

RiskEngine.cs/RiskPolicy.cs/InstrumentRiskSpecification.cs/RiskEngineRequest.cs/RiskAssessment.cs: UNMODIFIED
Rejection reason independence (4-case matrix, brief Section 8): PROVEN by new Tests/Risk/RiskRejectionReasonIndependenceTests.cs

## DASHBOARD

RiskDashboardPresenter now distinguishes NOT AVAILABLE (no candidate) from NOT AVAILABLE (ATAS ERROR)
Balance displayed separately from Capital/Equity
Live/Replay context now consistent across DEBUG/DATASET/Replay Monitor panels

## TESTS

Debug build: PASS (0/0)
Release build: PASS (0/0)
Targeted new/changed tests: 43/43 PASS
Full suite: 247 passed / 0 failed / 1 skipped (pre-existing, unrelated) / 248 total, 8 min 1 s - includes the previously-flaky AdfLagSelectionScaleStabilityXunitTests.RunAll, which passed this run

## PROTECTED COMPONENTS

RiskEngine: UNMODIFIED
RiskPolicy: UNMODIFIED
InstrumentRiskSpecification: UNMODIFIED
RiskEngineRequest: UNMODIFIED
RiskAssessment: UNMODIFIED
DecisionArbitrator: UNMODIFIED
EntryTriggerBuilder: UNMODIFIED (by this lot - carries an unrelated, pre-existing Lot 9 change)
TradePlanBuilder: UNMODIFIED

## VALIDATION

Replay validation: REQUIRED (confirm AtasContext=Replay end-to-end on a real Replay session)
Live validation: REQUIRED (confirm AtasContext=Live end-to-end, and capture ATAS.RiskStage.Exception if Problem B recurs)

## SAFETY

Orders sent: NO
Positions opened: NO
DLL deployed: NO
Commit: NO

## FINAL VERDICT

STRUCTURAL GAPS IDENTIFIED AND HARDENED - LIVE VALIDATION REQUIRED TO CONFIRM PROBLEM B'S EXACT ROOT CAUSE

STOP — FIN DU LOT 12.12.
