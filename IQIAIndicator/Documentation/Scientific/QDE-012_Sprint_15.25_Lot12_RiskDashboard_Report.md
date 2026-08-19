# QDE-012 — Sprint 15.25 (Lot 12) — RiskAssessment Dashboard & Validation Runtime

Ce lot rend le `RiskAssessment` produit par le Risk Engine (Lot 10/11) visible dans le dashboard ATAS,
en mode strictement passif : aucun calcul de risque n'est dupliqué, aucun ordre n'est envoyé, aucune
valeur n'est inventée.

---

## 1. Objectif

Créer une observabilité runtime claire du Risk Engine dans l'interface IQIA, permettant de répondre
immédiatement aux 13 questions listées en Section 2 du lot (Status, capital, equity, budget, risque du
trade, taille, entry/SL/TP, R:R, instrument, raison, timestamp) — sans activer de trading réel.

## 2. Architecture

```
RiskEngine.Evaluate(...) → RiskAssessment  (Lot 10/11, inchangé)
        ↓
DashboardContext.RiskAssessment / RiskAccount / RiskInstrument   (nouveaux champs, lecture seule)
        ↓
RiskDashboardPresenter.Present(risk, account, instrument, timestamp) → RiskDashboardView
        ↓ (chaînes déjà résolues, aucun calcul)
TradingDashboard.Draw(...) → section "RISK ENGINE" (DashboardCanvas.Field, comme toutes les autres sections)
```

`RiskDashboardPresenter` est un nouveau composant pur (aucune dépendance `RenderContext`/ATAS), qui
résout uniquement le TEXTE et l'ÉTAT sémantique à afficher — jamais un montant de risque, une taille de
position ou un R:R. Ce choix reproduit exactement le motif déjà établi par
`TradePlanAnnotationBuilder` (Lot 1) : un "Builder" testable en amont, un renderer ATAS en aval qui ne
fait que dessiner ce que le Builder a déjà résolu.

**Aucun fichier protégé n'a été modifié** : `DecisionArbitrator.cs`, `EntryTriggerBuilder.cs`,
`TradePlanBuilder.cs`, `RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs` sont
strictement inchangés (vérifié par relecture avant modification, Section 1 du lot).

## 3. Point d'affichage

`Visualization/Dashboards/TradingDashboard.cs`, immédiatement après la section `TRADE PLAN` existante
(dashboard déjà actif par défaut, `DashboardKind.Trading` — aucun nouveau dashboard créé, aucun
mécanisme d'affichage dupliqué). `TradingDashboard.Height` passe de `600` à `820` pour accueillir les
13 nouveaux champs + en-tête de section.

## 4. Données affichées

| Champ dashboard | Source réelle |
|---|---|
| Status | `RiskDashboardPresenter` (NotAvailable / `RiskAssessment.Status`) |
| Instrument | `InstrumentRiskSpecification.Symbol` |
| Capital | `AccountState.InitialCapital` |
| Equity | `AccountState.CurrentEquity` |
| Risk Budget | `RiskAssessment.RiskBudget` |
| Trade Risk | `RiskAssessment.RiskAmount` |
| Position Size | `RiskAssessment.PositionSize` |
| Entry | `RiskAssessment.EntryPrice` |
| Stop Loss | `RiskAssessment.StopLoss` |
| Take Profit | `RiskAssessment.TakeProfit` |
| R:R | `RiskAssessment.RiskRewardRatio` |
| Reason | `RiskAssessment.RejectionReasons` (join `", "`, valeurs brutes de l'enum) |
| Last Update | `DashboardContext.Timestamp` (timestamp du bar déjà existant dans le pipeline — voir §16) |

Aucun nouveau champ numérique n'a été inventé : chaque valeur ci-dessus est un read direct d'un champ
déjà présent dans `RiskAssessment`/`AccountState`/`InstrumentRiskSpecification` (Lot 10).

## 5. États NOT AVAILABLE / REJECTED / ACCEPTED

`RiskDashboardStatusKind` (nouvel enum, 3 valeurs : `NotAvailable`, `Rejected`, `Accepted`) distingue
explicitement "le Risk Engine n'a pas produit de résultat ce bar" (`RiskAssessment is null` — gris) de
"le Risk Engine a rejeté" (rouge) et "le Risk Engine a accepté" (vert). `RiskDecisionStatus` (Lot 10,
2 valeurs seulement) ne permettait pas cette distinction — c'est la seule raison de ce nouvel enum, situé
dans la couche présentation (`Visualization/Rendering/`), jamais dans `Engine/Risk/`.

## 6. Gestion du capital

`Capital`/`Equity` proviennent de `AccountState.InitialCapital`/`CurrentEquity`, retenus dans
`IQIAIndicator._latestRiskAccountState` (nouveau champ) et exposés sur `DashboardContext.RiskAccount`.
**Aucune valeur (10 000/25 000/50 000/100 000) n'est hardcodée.** Si `AccountState` est absent (avant le
premier bar), le dashboard affiche `NOT AVAILABLE` ; s'il est présent avec `0` (paramètres UI Lot 11 non
configurés), le dashboard affiche `$0.00` — et la raison réelle (`INVALID_CAPITAL`/`INVALID_EQUITY`)
apparaît dans `Reason` dès qu'un `RiskAssessment` existe. Conforme à la Section 4/11 du lot :
l'absence de configuration reste visible, jamais masquée par une valeur fictive.

**Changement d'architecture justifié (Section 26) :** dans `IQIAIndicator.cs`, la construction
d'`AccountState`/`InstrumentRiskSpecification` a été déplacée hors du bloc `if (_latestTradePlan is not
null)` du Lot 11, pour qu'elle s'exécute à chaque bar indépendamment de l'existence d'un candidat
directionnel. Seul l'appel `RiskEngineRequestFactory.FromTradePlan`/`RiskEngine.Evaluate` reste
conditionné à `_latestTradePlan`. Cela ne modifie ni la formule ni le comportement du Risk Engine —
uniquement le moment où les objets de configuration (déjà construits de façon identique) sont retenus —
et c'était nécessaire pour que Capital/Equity/Instrument restent observables même les bars sans candidat
(répond directement à la Section 4 : "cela doit être clairement visible").

## 7. Gestion ES/MES

Le dashboard affiche `InstrumentRiskSpecification.Symbol` tel quel — aucun `if symbol == "ES"`. Testé
explicitement (TEST 4/5 de `RiskDashboardPresenterTests.cs`) : la même fonction de présentation, avec
deux specs différentes, affiche `"ES"` et `"MES"` respectivement.

## 8. Gestion SL absent

Si `RiskAssessment.StopLoss` est `null` (réalité de production actuelle, aucune méthodologie SL —
Lot 10/11/12 l'interdisent), le dashboard affiche littéralement `"SL NOT PRODUCED"` (texte suggéré par
la Section 12 du lot), jamais une distance ATR/tick/pourcentage inventée. Testé (TEST 6).

## 9. RiskAssessment

Le contrat `RiskAssessment` (Lot 10) n'a subi **aucune modification**. Le dashboard le lit exclusivement
via `RiskDashboardPresenter.Present`, sans jamais recalculer `RiskBudget`/`PositionSize`/`RiskRewardRatio`
(Section 5 du lot — vérifié par relecture : le presenter ne contient que des `switch`/ternaires de
formatage, aucune arithmétique de risque).

## 10. Tests

**7 tests** dans `Tests/Visualization/RiskDashboardPresenterTests.cs`
(+ `Tests/XunitWrappers/RiskDashboardPresenterXunitTests.cs`), correspondant exactement à la Section 19
du lot :

| Test | Couvre |
|---|---|
| 01 Aucun RiskAssessment | `NotAvailable`, jamais confondu avec `Rejected` |
| 02 REJECTED | Status + raisons multiples affichées verbatim (`INVALID_CAPITAL`, `INVALID_EQUITY`) |
| 03 ACCEPTED | Status + Position Size/Risk Budget/Trade Risk/R:R corrects |
| 04 ES | Symbole `"ES"` affiché tel quel |
| 05 MES | Symbole `"MES"` affiché tel quel, sans branchement |
| 06 SL absent | `"SL NOT PRODUCED"`, jamais un SL fabriqué |
| 07 Aucun effet de bord | `RiskAssessment`/`AccountState`/`InstrumentRiskSpecification` inchangés (`with {}` snapshot) après `Present` |

`TradingDashboard`/`RenderContext` restent non unit-testables directement (nécessitent la plateforme
ATAS live — même limite déjà documentée pour `IQIAIndicator` dans
`ScientificDatasetRealMarketCaptureTests.cs`) ; `RiskDashboardPresenter` est le point d'extraction qui
rend la logique de décision d'affichage testable sans ATAS.

## 11. Build Debug

`dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` : **réussi — 0 avertissement, 0 erreur.**
`dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` : **réussi — 0 avertissement, 0 erreur.**

`dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` (suite complète) : 180 réussites,
1 ignoré (pré-existant, sans lien), **1 échec** :
`AdfLagSelectionScaleStabilityXunitTests` — un test de PERFORMANCE (mesure de ms/call de la sélection de
lag ADF) pré-existant, sans aucun rapport avec ce lot (jamais touché, aucune dépendance vers
`Engine/Risk`/`Visualization`). Ce même test avait déjà été observé comme flaky lors du Lot 10 (échec
sous charge CPU concurrente, succès isolé reproductible). Cette fois la suite complète a duré **1h01**
(contre ~9 min lors du Lot 10/11), signe d'une charge système externe inhabituelle sur la machine
pendant l'exécution — indépendante de ce lot. **Reproduit en isolation immédiatement après : succès
(0 échec, 1 réussite, 16s).** Cause confirmée : sensibilité de ce test à la charge machine, pas une
régression introduite par ce lot (`Engine/Risk`/`Visualization` non concernés par ce test).

## 12. Build Release

`dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` : **réussi — 0 avertissement, 0 erreur.**

Deux fichiers de sortie de campagne pré-existants (`Tests/Research/StopLossCalibration/Output/*.txt`)
régénérés comme effet de bord de la suite complète (sans lien avec ce lot) — restaurés via
`git checkout --` pour garder le diff strictement scopé au Lot 12.

## 13. Validation manuelle prévue (procédure documentée, NON exécutée dans ce lot)

1. Construire en configuration Release.
2. Déployer manuellement la DLL dans ATAS (hors de ce lot).
3. Charger l'indicateur "IQIA Signal" sur un chart.
4. Sélectionner ES ou MES comme instrument du chart.
5. Configurer explicitement, dans le groupe de paramètres "Risk Engine" : Capital initial, Equity
   actuelle, Peak equity, Equity de début de journée, Risk Policy (au moins "Risque max par trade (%)"),
   Quantité min/max/pas.
6. Lancer un Replay ATAS.
7. Sélectionner "Dashboard actif = Trading" et observer le panneau "RISK ENGINE".
8. Vérifier : Status, Reason, Risk Budget, Position Size, Entry, Stop Loss, Take Profit, R:R,
   Instrument, Last Update — cohérents avec les paramètres configurés.
9. Activer "Activer le dataset scientifique" et vérifier que les catégories `Risk.*` (Lot 11) sont bien
   présentes dans l'export.
10. Confirmer visuellement qu'aucun ordre n'est envoyé (aucune notification broker, aucune position
    ouverte dans ATAS).

## 14. Limites

- Le panneau RISK ENGINE n'apparaît que sur le dashboard `Trading` (déjà celui par défaut) — aucun
  panneau équivalent n'a été ajouté aux 5 autres dashboards (Scientific/Decision/Dataset/
  Performance/Debug), hors périmètre de ce lot.
- Tant que les paramètres `Risk*` (Lot 11) restent à `0`, le panneau affichera systématiquement
  `REJECTED` / `INVALID_CAPITAL` — comportement voulu, pas un défaut.
- `PortfolioState` (Lot 10) n'est toujours pas affiché (toujours non consommé par `RiskEngine.Evaluate`,
  Lot 11 §14).
- `TradingDashboard`/le rendu visuel réel restent non vérifiables sans ATAS ; seule la logique de
  décision d'affichage (`RiskDashboardPresenter`) est unit-testée.

## 15. Prochain lot recommandé

Validation manuelle en conditions ATAS réelles (procédure Section 13 ci-dessus), suivie — une fois
confirmée — d'une éventuelle calibration scientifique de Stop Loss (lot séparé, hors risque pur) ou
d'une configuration réelle du capital/policy par l'utilisateur. **Ce lot ne recommande aucune valeur.**

---

## LOT 12 RESULT

**Fichiers créés :**
```
IQIAIndicator/Visualization/Rendering/RiskDashboardPresenter.cs
IQIAIndicator/Tests/Visualization/RiskDashboardPresenterTests.cs
IQIAIndicator/Tests/XunitWrappers/RiskDashboardPresenterXunitTests.cs
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot12_RiskDashboard_Report.md
```

**Fichiers modifiés par ce lot :**
```
IQIAIndicator/IQIAIndicator.cs                         (2 nouveaux champs + restructuration du stage Risk + wiring DashboardContext)
IQIAIndicator/Visualization/State/DashboardContext.cs    (+RiskAssessment/RiskAccount/RiskInstrument)
IQIAIndicator/Visualization/Dashboards/TradingDashboard.cs (+section RISK ENGINE, Height 600→820)
```
(`Core/Calibration/ScientificDatasetRecord.cs`, `Core/Observability/PipelineTraceContext.cs`,
`Engine/EntryTrigger/EntryTriggerBuilder.cs`, et les fichiers de tests déjà modifiés par les lots
précédents : non touchés davantage par ce lot.)

**Emplacement exact de l'affichage :** `TradingDashboard.Draw`, section "RISK ENGINE", juste après
"TRADE PLAN", dashboard `Trading` (actif par défaut).

**Données affichées :** Status, Instrument, Capital, Equity, Risk Budget, Trade Risk, Position Size,
Entry, Stop Loss, Take Profit, R:R, Reason, Last Update — voir Section 4 ci-dessus pour la source exacte
de chacune.

**Comportement NOT AVAILABLE :** `RiskAssessment is null` → Status gris "NOT AVAILABLE", jamais confondu
avec un rejet.

**Comportement REJECTED :** Status rouge "REJECTED" + `Reason` listant les `RiskRejectionReason` réels
(ex. `INVALID_CAPITAL, INVALID_EQUITY`).

**Comportement ACCEPTED :** Status vert "ACCEPTED", `Reason` = "NOT AVAILABLE" (rien à rejeter), tous les
champs numériques résolus.

**Capital :** affiché depuis `AccountState` réel, `0` visible tel quel si non configuré — jamais inventé.

**ES/MES :** affichés depuis `InstrumentRiskSpecification.Symbol`, sans branchement.

**SL :** "SL NOT PRODUCED" si absent — aucune stratégie créée.

**TP :** affiché uniquement si présent dans `RiskAssessment.TakeProfit`.

**Risk/Reward :** affiché uniquement si `RiskAssessment.RiskRewardRatio` existe, jamais recalculé.

**Tests :** 7 nouveaux tests (`RiskDashboardPresenterTests.cs`), tous verts. Suite complète : 180
réussites, 1 ignoré (pré-existant), 1 échec (`AdfLagSelectionScaleStabilityXunitTests`, test de
performance ADF pré-existant et sans rapport avec ce lot, confirmé flaky/dépendant de la charge machine
— reproduit en succès isolé juste après, voir Section 11).

**Build Debug :** réussi, 0 avertissement, 0 erreur.
**Build Release :** réussi, 0 avertissement, 0 erreur.

**Git status :** voir liste des fichiers créés/modifiés ci-dessus ; aucun DLL, dataset réel ou fichier
temporaire ajouté.

**Limites :** voir Section 14.

**Procédure de validation ATAS prévue :** voir Section 13 (documentée uniquement, non exécutée).

**Prochain lot recommandé :** voir Section 15.

STOP — fin du Lot 12. Aucun DLL déployé, aucun lancement ATAS, aucune modification du seuil, aucune
stratégie SL créée, pas de passage automatique au lot suivant.
