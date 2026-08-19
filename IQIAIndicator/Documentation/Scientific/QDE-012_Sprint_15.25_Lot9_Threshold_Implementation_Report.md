# QDE-012 — Sprint 15.25 (Lot 9) — Implémentation contrôlée du seuil candidat + traçabilité Entry/TradePlan

Ce lot fait suite aux Lots 1-8 (audit read-only, puis calibration/backtest/OOS offline — voir les
rapports de conversation correspondants, non déposés séparément dans ce dossier). Il implémente, de
façon strictement contrôlée, la valeur candidate identifiée par les Lots 4-8 pour vérifier en conditions
ATAS réelles si l'assouplissement de la porte `DecisionArbitrator`→`EntryTrigger` permet enfin au
pipeline de produire des plans de trade observables. **Ce n'est pas une nouvelle calibration
scientifique.**

## A. Seuil avant

`AmbiguityScore >= 0.5` (équivalent `Difference > 0.5`), dans
`EntryTriggerBuilder.DetermineDirection` — [EntryTriggerBuilder.cs](../../Engine/EntryTrigger/EntryTriggerBuilder.cs).
Cette valeur n'a jamais été satisfaite sur aucune capture ATAS réelle analysée (Lots 2-3), bloquant
`Direction` en permanence.

## B. Seuil testé

`AmbiguityScore >= 0.95` (équivalent `Difference > 0.05`) — valeur candidate issue de l'étude offline
Lots 4-8 (plage `[0.05, 0.06]` la mieux soutenue statistiquement, `0.05` retenue pour ce lot).

**Équivalence utilisée** (documentée dans le code, non redérivée) : `DecisionArbitrator.Arbitrate`
calcule `Difference = WinnerScore - RunnerUpScore` puis `AmbiguityScore = Clamp(1 - Difference, 0, 1)`
— formule inchangée, non touchée par ce lot. `Difference > 0.05 ⟺ AmbiguityScore < 0.95 ⟺` porte
bloquante `AmbiguityScore >= 0.95`.

## C. Justification (résultats Lots 4-8)

- Lot 4 (calibration offline, 5 datasets ES M5 réels) : le plafond réel de `Difference` restreint à
  `Winner==MeanReverting` ne dépasse jamais ~0.089-0.094 sur aucun des 5 jeux de données — le seuil de
  0.5 est structurellement inatteignable.
- Lot 5 (reproduction EntryTrigger offline) : à `Difference>0.05`, conversion candidat→direction de
  100% (aucun blocage résiduel autre que la porte elle-même).
- Lot 6 (backtest offline, no-look-ahead) : tendance directionnelle réelle mais magnitude instable
  entre 2 fenêtres in-sample.
- Lot 7/8 (validation OOS, 4 nouvelles fenêtres jamais utilisées pour le calibrage) : le motif se
  reproduit sur 3 fenêtres indépendantes (9066 bars OOS combinés) ; verdict final Lot 8 :
  `ROBUST RANGE 0.05–0.06` (robustesse **statistique/structurelle**, pas une preuve de rentabilité nette
  — aucun modèle de coûts disponible).

## D. Modifications exactes

### 1. Avant modification — localisation (relecture directe du code, rien supposé)

| Composant | Fichier | Classe/Méthode | Constat |
|---|---|---|---|
| Porte du seuil | [EntryTriggerBuilder.cs:151](../../Engine/EntryTrigger/EntryTriggerBuilder.cs) | `DetermineDirection` | `if (decision.AmbiguityScore >= 0.5)` — seule occurrence du seuil dans tout le dépôt |
| Formule AmbiguityScore | [DecisionArbitrator.cs:37-38](../../Engine/Decision/Arbitration/DecisionArbitrator.cs) | `Arbitrate` | `Difference`/`AmbiguityScore` — **non touché** |
| TradePlan | [TradePlanBuilder.cs](../../Engine/TradePlan/TradePlanBuilder.cs) | `Build` | **non touché** |
| Instrumentation | [ScientificDatasetRecord.cs](../../Core/Calibration/ScientificDatasetRecord.cs) | `From` | ne capturait ni EntryTrigger ni TradePlan (confirmé Lots 3/5/7/8) |
| Point d'appel dataset | [IQIAIndicator.cs:471-481](../../IQIAIndicator.cs) | `OnCalculate` | seul call site production de `ScientificDatasetRecord.From` |
| Renderer | [ATASRenderer](../../Infrastructure/ATAS/) `.RenderTradePlan` | — | **non touché**, vérifié inchangé |

### 2. Modification du seuil — SEUIL

**Fichier** : `IQIAIndicator/Engine/EntryTrigger/EntryTriggerBuilder.cs`

Ajout d'une constante nommée `private const double AmbiguityGateThreshold = 0.95;` (au lieu du littéral
`0.5` inline) — changement minimal, réversible en une ligne. Condition changée :
`if (decision.AmbiguityScore >= 0.5)` → `if (decision.AmbiguityScore >= AmbiguityGateThreshold)`.
Message diagnostique mis à jour pour refléter la nouvelle valeur. **Aucune autre ligne de cette méthode
touchée** — `WATCHLIST`, la vérification `decision is null`, la vérification `Winner != MeanReverting`,
et toute la branche `DynamicZScore` (BUY/SELL/NO_ACTION) restent identiques caractère pour caractère.

### 3. Instrumentation — INSTRUMENTATION

**Fichiers** : `ScientificDatasetRecord.cs`, `IQIAIndicator.cs`

`ScientificDatasetRecord.From(...)` reçoit 3 nouveaux paramètres **optionnels, en position finale**
(`entryCandidate`, `entryTriggerCandidate`, `tradePlan`, tous `= null` par défaut) — compatible avec les
3 sites d'appel positionnels préexistants (production + 2 fichiers de tests), aucun ne dépasse le
paramètre `volume`. À l'intérieur, 8 nouvelles entrées `Categories` (dictionnaire `string→string`
déjà existant, schéma du record `ScientificDatasetRecord` lui-même inchangé) :
`Entry.OpportunityStatus`, `EntryTrigger.TriggerStatus`, `EntryTrigger.Direction`,
`EntryTrigger.Reason`, `TradePlan.Status`, `TradePlan.EntryPrice`, `TradePlan.StopLoss`,
`TradePlan.TakeProfit` — chacune lit une valeur déjà calculée, ou écrit `"NOT AVAILABLE"` si l'objet
source est `null`. Aucun recalcul, aucune formule, aucune décision. Le point d'appel dans
`IQIAIndicator.cs` (bloc `EnableScientificDataset`) passe simplement `_latestEntryCandidate`,
`_signalEngine.LastEntryTriggerCandidate` et `_latestTradePlan` — les mêmes objets déjà lus par le
dashboard et le renderer quelques lignes plus loin dans `OnRender`, jamais recalculés pour l'occasion.

### 4. Tests — TEST

- `DecisionDirectionCoherenceTests.cs` : 3 valeurs `ambiguityScore` (0.7, 0.9, 0.8) recalibrées à 0.97
  — ces bars étaient bloqués sous l'ancien seuil (0.5) mais seraient désormais **ouverts** sous 0.95 ;
  0.97 reste au-dessus du nouveau seuil et préserve l'intention originale de chaque test ("toujours
  ambigu"). 3 nouveaux tests ajoutés : porte ouverte à `Difference=0.06`, porte fermée exactement à
  `Difference=0.05`, et TradePlan reçoit correctement `EntryPrice` une fois la porte ouverte (sans
  toucher `TradePlanBuilder`).
- `DirectionEndToEndTests.cs` : 1 valeur (0.9→0.97) recalibrée pour la même raison.
- `ScientificDatasetRealMarketCaptureTests.cs` : 3 nouveaux tests (non-mutation des 3 nouveaux
  paramètres, population correcte des nouvelles catégories, `"NOT AVAILABLE"` en leur absence).
- **Aucun test supprimé ni désactivé.** Aucun autre test ne référençait une `AmbiguityScore` dans la
  zone affectée par le changement (vérifié par recherche exhaustive dans `Tests/`).

### 5. AUTRE

Aucune modification classée AUTRE.

## E. TradePlan — flux vérifié (§5 de la demande)

Confirmé (re-vérification directe du code, `TradePlanBuilder.cs` inchangé) : le `TradePlan` était
`NO_TRADE`/N/A pour deux raisons distinctes, toujours vraies après ce lot :
1. **Avant ce lot** : `Direction` n'était jamais `BUY_CANDIDATE`/`SELL_CANDIDATE` (porte à 0.5 jamais
   franchie) → `TradePlanBuilder.Build` retournait `NO_TRADE` immédiatement, avant même de lire
   `CurrentPrice`.
2. **Indépendamment, inchangé par ce lot** : même quand `Direction` devient `BUY`/`SELL`, `StopLoss`
   reste `null` car `IQIAIndicator.cs` passe toujours `RiskParameters: null` (aucun Risk Engine —
   `TradePlanBuilder.cs` ligne ~57) → `Status` plafonne à `SIGNAL_ONLY`, jamais `PLAN_READY`.

Le nouveau test `AssertTradePlanBuilderReceivesEntryPriceOnceGateIsOpen` prouve mécaniquement, sans
toucher `TradePlanBuilder.cs`, que le point 1 est résolu (`EntryPrice` produit) tandis que le point 2
persiste (`StopLoss` = `SL NOT PRODUCED`, `Status=SIGNAL_ONLY`) — comportement attendu, documenté, pas
corrigé (Risk Engine hors périmètre).

## F. Flux attendu

```
Decision (DecisionArbitrator, formule inchangée)
  ↓
EntryTrigger (DetermineDirection, porte à 0.95 au lieu de 0.5)
  ↓
BUY_CANDIDATE / SELL_CANDIDATE (désormais atteignables ; logique DynamicZScore inchangée)
  ↓
TradePlan (TradePlanBuilder inchangé) → EntryPrice produit, TakeProfit produit si cohérent,
  StopLoss = SL NOT PRODUCED (Risk Engine absent, hors périmètre)
  ↓
ATASRenderer.RenderTradePlan(...) — chemin de rendu du Lot 1, non modifié, vérifié inchangé
```

## G. Risques / limites

- **Aucune validation ATAS live effectuée dans ce lot.** Tout ce qui précède est prouvé par tests
  unitaires/xunit hors ATAS — la question "est-ce que TradePlan devient réellement visible pendant un
  vrai Replay ATAS" reste ouverte.
- `StopLoss`/`TakeProfit`/sizing restent incomplets par construction (Risk Engine absent) —
  `Status` plafonnera à `SIGNAL_ONLY` même en live.
- Aucun modèle de coûts de transaction n'a jamais été validé (Lots 6-8) — ce lot ne change rien à cela.
- Le seuil `0.05` est un point d'une plage `[0.05, 0.06]` jugée robuste au sens statistique, pas
  "optimale" — voir Lot 8 pour les nuances.

## H. Résultat du build

```
dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug    → Build réussi, 0 avertissement, 0 erreur (44s)
dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release  → Build réussi, 0 avertissement, 0 erreur (19s)
```

DLL Release généré : `IQIAIndicator/bin/Release/net10.0-windows/IQIAIndicator.dll` (506 368 octets).
**Non déployé** vers ATAS.

## I. Résultat des tests

```
dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug
Réussi ! — échec : 0, réussite : 176, ignorée(s) : 1, total : 177, durée : 10 m 40 s
```

Le seul test ignoré (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`) est
`[Fact(Skip=...)]` depuis le Sprint 15.22, sans rapport avec ce lot (vérifié par `git log`/lecture du
fichier — prémisse retirée avant même ce lot).

## Verdict

**`IMPLEMENTED — LIVE ATAS VALIDATION REQUIRED`**

Le code compile (Debug + Release) et l'intégralité de la suite de tests passe (176/176, 0 échec).
Aucune affirmation n'est faite quant au comportement en Replay ATAS réel — non exécuté dans ce lot.
