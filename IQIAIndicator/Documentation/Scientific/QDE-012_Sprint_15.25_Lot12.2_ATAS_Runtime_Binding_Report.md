# QDE-012 — Sprint 15.25 (Lot 12.2) — ATAS Account & Instrument Runtime Binding

Ce lot câble, en lecture seule, les API ATAS confirmées CONFIRMED au Lot 12.1
(`Indicator.TradingManager`, `Indicator.TradingStatisticsProvider`) vers `AccountState`/
`InstrumentRiskSpecification` (Lot 10, **strictement inchangés**), remplaçant l'Equity manuelle et la
spécification instrument manuelle par des valeurs automatiquement lues depuis ATAS lorsqu'elles sont
disponibles — avec échec fermé (fail-closed) systématique dans le cas contraire.

## 1. Objective

Fermer l'écart identifié au Lot 12.1 entre l'écran `Equity $0.00` observé en Replay et les données
réellement exposées par ATAS, sans jamais inventer de valeur financière ni modifier le Risk Engine.

## 2. ATAS APIs utilisées

Toutes déjà **CONFIRMED** par la réflexion du Lot 12.1, revérifiées ici par compilation réelle du
projet (Section 1 du lot) :

- `Indicator.TradingManager : ITradingManager` — accesseur `protected` (vérifié par réflexion précise,
  Lot 12.2 §0) ; **compile et s'exécute depuis `IQIAIndicator : Indicator`**, exactement comme
  `Indicator.InstrumentInfo` (également `protected`, déjà utilisé depuis le Sprint 2). Aucune condition
  d'arrêt déclenchée.
- `ITradingManager.Portfolio : Portfolio`, `.Security : Security` — get-only.
- `Indicator.TradingStatisticsProvider : ITradingStatisticsProvider` — accesseur **public confirmé**.
- `ITradingStatisticsProvider.Realtime`/`.Replay : ITradingStatistics`.
- `ITradingStatistics.Equity : Utils.Common.Collections.IMutableEnumerable<EquityValue>` — **découverte
  du Lot 12.2** : `EquityValue` est en réalité une **struct** (type valeur), pas une classe comme le
  Lot 12.1 l'avait rapporté à tort (son script de réflexion ne distinguait pas `IsValueType` de
  `IsInterface`/`IsEnum`) — corrigé ici par compilation réelle (erreur `CS0023` immédiate, corrigée en
  vérifiant `.Any()` avant `.Last()` plutôt qu'un `?.` sur une struct).

## 3. Account mapping

`Infrastructure/ATAS/ATASAccountStateAdapter.Build(Portfolio? portfolio, decimal? currentEquity,
initialCapital, peakEquity, dailyStartingEquity, dailyPnL, riskUsedToday, openRisk) : AccountState` —
mapping pur, aucune logique de stratégie :

| Champ `AccountState` | Source |
|---|---|
| `InitialCapital` | **Manuel** (`RiskInitialCapital`, Lot 11, inchangé — Section 3 du lot) |
| `CurrentEquity` | **ATAS** (voir Section 4 ci-dessous) |
| `CurrentBalance` | **ATAS** (`Portfolio.Balance`, informationnel — non consommé par `RiskEngine.Evaluate`, Lot 10) |
| `PeakEquity`, `DailyStartingEquity`, `DailyPnL`, `RiskUsedToday`, `OpenRisk` | **Manuel** (Lot 11, inchangé — aucun équivalent ATAS confirmé, Lot 12.1) |

`AccountState` (Lot 10) **n'a pas été modifié** — le mapping utilise exclusivement les champs déjà
existants.

## 4. Equity mapping

`ATASAccountStateAdapter.TryGetCurrentEquity(ITradingStatisticsProvider?, bool isReplay) : decimal?` lit
`.Realtime.Equity` ou `.Replay.Equity` (selon `isReplay`, déjà disponible via
`context.Execution.IsReplay` dans `OnCalculate`), prend le **dernier point** de la série temporelle
(`.Any()` puis `.Last()`, jamais un `default(EquityValue)` confondu avec une vraie lecture — struct
oblige), et retourne `.Equity` (pas `.TotalEquity`, ambiguïté documentée au Lot 12.1 §12).

**Comportement critique (Section 5 du lot)** : `RiskCurrentEquity` (paramètre manuel du Lot 11) n'est
**plus jamais lu** par le stage Risk. Si `TryGetCurrentEquity` retourne `null` (provider absent, flux
Replay/Realtime absent, ou courbe vide), `ATASAccountStateAdapter.Build` résout `CurrentEquity` à `0m` —
**jamais** `Balance`, **jamais** `InitialCapital`, **jamais** une valeur précédente. Ce `0m` n'est pas
une valeur financière inventée : c'est le même sentinel "non configuré" que `RiskEngine.Evaluate`
(Lot 10, inchangé) traite déjà via son test `CurrentEquity > 0` pour rejeter avec `INVALID_EQUITY`.
Ce choix a été nécessaire car `AccountState.CurrentEquity` est un `decimal` non-nullable dans un fichier
protégé (`AccountState.cs`) qu'il était hors de question de modifier pour ce lot — voir Section 6 pour la
justification complète et la Section 7 ci-dessous.

## 5. Instrument mapping

`Infrastructure/ATAS/ATASInstrumentAdapter.Build(Security? security, int fallbackMinQuantity, int
fallbackMaxQuantity, int quantityStep) : InstrumentRiskSpecification` :

| Champ `InstrumentRiskSpecification` | Source |
|---|---|
| `Symbol` | ATAS `Security.Instrument`, sinon `""` |
| `TickSize` | ATAS `Security.TickSize`, sinon `0m` |
| `TickValue` | ATAS `Security.TickCost`, sinon `0m` |
| `PointValue` | **Dérivé** : `TickCost / TickSize` si les deux sont positifs, sinon `0m` (aucun champ natif ATAS — Lot 12.1) |
| `MinQuantity` | ATAS `Security.LotMinSize` si `> 0`, **sinon fallback manuel** (`RiskInstrumentMinQuantity`, Lot 11, inchangé) |
| `MaxQuantity` | ATAS `Security.LotMaxSize` si `> 0`, **sinon fallback manuel** (`RiskInstrumentMaxQuantity`) |
| `QuantityStep` | **Toujours manuel** (`RiskInstrumentQuantityStep`) — aucun équivalent ATAS trouvé (Lot 12.1 §11), rien inventé |

`InstrumentRiskSpecification.cs` (Lot 10) **n'a pas été modifié**.

## 6. ES/MES data-driven validation

Aucun `if symbol == "ES"`/`"MES"` n'existe nulle part dans le code ajouté (vérifié par relecture de
`ATASInstrumentAdapter.cs` et `IQIAIndicator.cs`). Preuve testée (TEST 6,
`ATASRuntimeBindingTests.Test06_DataDrivenNotSymbolDriven`) : un `Security` **nommé** `"ES"` mais portant
les caractéristiques tick de MES (`TickSize=0.25`, `TickCost=1.25`) produit le `RiskPerUnit` de MES
(25, pas 250) — la démonstration la plus forte possible que le comportement suit exclusivement les
champs numériques, jamais la chaîne `Instrument`. TEST 4/5 confirment séparément que ES (`RiskPerUnit`
250) et MES (`RiskPerUnit` 25) produisent des résultats distincts pour la même distance de stop, à
partir de la même InstrumentRiskSpecification construite dynamiquement.

## 7. Fail-closed behavior

| Donnée absente | Comportement |
|---|---|
| `TradingStatisticsProvider` entier absent | `TryGetCurrentEquity` → `null` → `CurrentEquity=0m` → `RiskEngine` → `REJECTED`/`INVALID_EQUITY` |
| Flux Replay/Realtime spécifique absent | idem |
| Courbe d'équité vide | idem (jamais confondu avec `default(EquityValue)`) |
| `Security` entier absent | `Symbol=""`, `TickSize=TickValue=PointValue=0m` → `InstrumentRiskSpecification.IsValid=false` → `RiskEngine` → `REJECTED`/`INSTRUMENT_SPEC_INVALID` |
| `TickCost`/`TickSize` incomplet sur `Security` | idem |
| `Portfolio` absent | `CurrentBalance=null` (informationnel, sans effet sur la décision) |

Aucune nouvelle raison de rejet n'a été créée — `INVALID_EQUITY` et `INSTRUMENT_SPEC_INVALID` sont
exactement les raisons déjà définies au Lot 10, réutilisées telles quelles.

## 8. Replay / Live limitations

**Non prouvé par ce lot** (nécessite ATAS réel, hors périmètre) : si `Portfolio`/`Security`/
`TradingStatistics.Equity` sont effectivement peuplés pendant un Replay ou en Live, et si leur contenu
diffère entre les deux modes. Le code lit correctement `.Replay` vs `.Realtime` selon
`context.Execution.IsReplay` (déjà existant), mais **RUNTIME VALIDATION REQUIRED** pour confirmer le
comportement réel — voir Section 12 ci-dessous pour la procédure.

## 9. Tests

**8 tests** dans `Tests/Infrastructure/ATAS/ATASRuntimeBindingTests.cs`
(+ `Tests/XunitWrappers/ATASRuntimeBindingXunitTests.cs`), utilisant de vraies instances des types ATAS
(`Portfolio`, `Security` — constructeurs publics sans paramètres + propriétés settables, confirmés au
Lot 12.1 — et `EquityValue`, struct positionnelle) comme fixtures, plus des implémentations minimales
`ITradingStatistics`/`ITradingStatisticsProvider`/`IMutableEnumerable<T>` (interfaces ATAS, aucune classe
concrète simple disponible pour ces trois-là) :

| Test | Couvre |
|---|---|
| 01 Account mapping | `Portfolio.Balance` → `CurrentBalance`, `InitialCapital` reste manuel |
| 02 Equity mapping | dernier point de la série `.Realtime`/`.Replay.Equity` correctement sélectionné |
| 03 Equity unavailable | provider null / flux absent / courbe vide → `0m` → `REJECTED`/`INVALID_EQUITY`, jamais `Balance`/`InitialCapital` |
| 04 MES specification | `RiskPerUnit=25` depuis les vraies caractéristiques MES |
| 05 ES specification | `RiskPerUnit=250`, distinct de MES |
| 06 Data-driven | `Security` nommé "ES" avec économie tick MES → `RiskPerUnit` MES (25) — preuve anti-hardcoding |
| 07 Fail-closed instrument | `TickCost=0` ou `Security` absent → `INSTRUMENT_SPEC_INVALID` |
| 08 Existing Risk Engine unchanged | reproduit exactement le TEST 20 du Lot 10 (arrondi conservateur 2.8→2) à travers le binding ATAS |

Conformément à la Section 18 du lot, **aucun test ne prétend démontrer** un compte ATAS réel, une Equity
Live/Replay réelle, un ordre réel ou un sizing optimal — uniquement la logique de mapping, avec des
objets ATAS simulés.

## 10. Build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` (suite complète) | **0 échec, 182 réussite(s), 1 ignoré(e) (pré-existant, sans lien — `Sprint1515HistoricalReconciliationXunitTests`), 183 au total, durée 7 min 22 s** |

Deux fichiers de sortie de campagne pré-existants (`Tests/Research/StopLossCalibration/Output/*.txt`)
ont été régénérés comme effet de bord de la suite complète (sans lien avec ce lot) — restaurés via
`git checkout --` pour garder le diff strictement scopé au Lot 12.2.

*(Deux erreurs de compilation intermédiaires ont été corrigées pendant le développement : `EquityValue`
struct vs classe — Section 2 — et une ambiguïté `TradeDirection` entre `ATAS.DataFeedsCore` et
`IQIAIndicator.Engine.Risk`, résolue par un alias `using`. Aucune des deux n'a nécessité de modifier un
fichier protégé.)*

## 11. Files modified

```
IQIAIndicator/IQIAIndicator.cs                          (Risk stage : lecture TradingManager/TradingStatisticsProvider ;
                                                           doc-comments des paramètres Risk* désormais fallback/inertes)
IQIAIndicator/Tests/IQIAIndicator.Tests.csproj            (+références ATAS.DataFeedsCore/Utils.Common, nécessaires aux fixtures)
```

`Engine/Risk/*.cs`, `Engine/Decision/`, `Engine/EntryTrigger/`, `Engine/TradePlan/`,
`AmbiguityGateThreshold`, `DecisionArbitrator`, `TradePlanBuilder` : **AUCUN de ces fichiers protégés
n'a été modifié** (vérifié par `git status`/`git diff` avant remise de ce rapport).

## Files created

```
IQIAIndicator/Infrastructure/ATAS/ATASAccountStateAdapter.cs
IQIAIndicator/Infrastructure/ATAS/ATASInstrumentAdapter.cs
IQIAIndicator/Tests/Infrastructure/ATAS/ATASRuntimeBindingTests.cs
IQIAIndicator/Tests/XunitWrappers/ATASRuntimeBindingXunitTests.cs
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot12.2_ATAS_Runtime_Binding_Report.md
```

## 12. Runtime validation procedure

**Non exécutée dans ce lot — documentée uniquement, comme demandé (Section 21).**

### Test A — MES
1. Construire en Release, déployer manuellement la DLL, charger l'indicateur sur un chart MES.
2. Ouvrir le panneau "RISK ENGINE" (dashboard Trading).
3. Vérifier : `Instrument = MES` (automatique) ; `Capital` = valeur manuelle (`RiskInitialCapital`) ;
   `Equity` = valeur non nulle si un `Portfolio`/`TradingStatistics.Equity` réel est disponible ; aucune
   saisie manuelle de tick/point n'est nécessaire pour que `RiskPerUnit` soit correct.

### Test B — ES
Même procédure sur un chart ES ; vérifier que `RiskPerUnit` diffère de celui observé en Test A sans
changer aucun paramètre `Risk*` manuel.

### Test C — Changement de compte
Si ATAS le permet, changer de compte actif et vérifier que `Balance`/`Equity` affichés suivent le
nouveau compte sans redémarrage de l'indicateur.

### Test D — Replay
Lancer un Replay et observer si `Equity` reste `NOT AVAILABLE`/`REJECTED INVALID_EQUITY` ou si une
valeur réelle apparaît — répond directement à la limitation de la Section 8.

## 13. Limitations

- Sémantique exacte de `BalanceAvailable` vs `BalancePower` toujours **REQUIRES LIVE ATAS VALIDATION**
  (Lot 12.1) — non consommées par ce lot (Section 8 : "AVAILABLE BUT NOT CURRENTLY CONSUMED").
  `Portfolio.AccountID`/`.Currency`/`.OpenPnL`/`.ClosedPnL`/`.TotalPnL`,
  `Position.UnrealizedPnL`/`.RealizedPnL` : idem, aucun champ `AccountState`/`RiskAssessment` ne les
  accueille aujourd'hui, non ajoutés dans ce lot (Section 7/8 du lot).
- `RiskCurrentEquity`/`RiskCurrentBalance` (paramètres UI du Lot 11) restent déclarés (pour ne pas casser
  une configuration ATAS déjà sauvegardée par l'utilisateur) mais ne sont plus consommés par le stage
  Risk — leurs libellés d'affichage ont été mis à jour pour le signaler explicitement.
- Comportement Replay/Live non validé en conditions réelles (voir Section 8/12).
- `QuantityStep` toujours manuel — aucune donnée ATAS correspondante trouvée (Lot 12.1, confirmé
  inchangé).

---

## LOT 12.2 — COMPLETE

Account binding : **IMPLEMENTED**
Equity binding : **IMPLEMENTED**
Instrument binding : **IMPLEMENTED**
MES automatic specification : **YES**
ES automatic specification : **YES**
Hardcoded symbol mapping : **NONE**
Fail-closed : **PASS**
Debug build : **PASS**
Release build : **PASS**
Tests : 182 passed / 0 failed / 1 skipped (pré-existant, sans lien)
Replay validation : **REQUIRED**
Live validation : **REQUIRED**

Files modified:
```
IQIAIndicator/IQIAIndicator.cs
IQIAIndicator/Tests/IQIAIndicator.Tests.csproj
```

Files created:
```
IQIAIndicator/Infrastructure/ATAS/ATASAccountStateAdapter.cs
IQIAIndicator/Infrastructure/ATAS/ATASInstrumentAdapter.cs
IQIAIndicator/Tests/Infrastructure/ATAS/ATASRuntimeBindingTests.cs
IQIAIndicator/Tests/XunitWrappers/ATASRuntimeBindingXunitTests.cs
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot12.2_ATAS_Runtime_Binding_Report.md
```

Protected files modified: **NONE**

DLL deployed: **NO**

Commit: **NO**

STOP.
