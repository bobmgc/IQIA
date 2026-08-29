# QDE-012 — Sprint 15.25 (Lot 12.4) — Audit Post-Replay MES : QuantityStep + Account State

**Lot d'audit/diagnostic uniquement.** Aucun fichier `.cs` modifié, aucun build, aucun DLL, aucun
déploiement, aucun commit. Ce document est le seul livrable produit par ce lot.

---

## 1. Contexte

Le Lot 12.3 avait établi, **par le code et par test unitaire** (`Test04_MissingQuantityStepAloneInvalidatesSpecificationEvenWithValidAtasData`),
que `InstrumentRiskSpecification.QuantityStep` resté à sa valeur par défaut non configurée (`0`) est
**suffisant à lui seul** pour produire `INSTRUMENT_SPEC_INVALID`, sans pouvoir confirmer si c'est
effectivement ce qui s'est produit lors d'un Replay réel (aucune capture disponible à l'époque).

Un Replay MES M5 a depuis été exécuté. Ce lot audite les artefacts qu'il a produits pour déterminer,
avec le niveau de preuve le plus rigoureux possible, si l'hypothèse QuantityStep est confirmée par les
données réelles — sans supposer, et en distinguant explicitement PROVEN / SUPPORTED / CONSISTENT WITH /
NOT PROVEN partout où c'est pertinent.

---

## 2. Artefacts analysés

Les artefacts demandés n'étaient pas dans le dépôt Git — ils ont été localisés dans le répertoire de
sortie par défaut du Scientific Dataset (`IQIAIndicator.cs:276-277`,
`%LOCALAPPDATA%\IQIA\ScientificDataset\`) :

| Fichier | Taille | Rôle |
|---|---|---|
| `ScientificDataset_MES_M5_20260817_135205.json` | 14 767 170 octets (2304 enregistrements) | Dataset principal — analysé intégralement par script Python (`json.load`), pas par lecture partielle |
| `ScientificDataset_MES_M5_20260817_135205_metadata.json` | 623 octets | Métadonnées de session (BarsReceived/Written, Duplicates, etc.) |
| `ScientificDataset_MES_M5_20260817_135205_lifecycle.json` | 622 octets | Horodatages du cycle de vie de la session |
| `ScientificDataset_MES_M5_20260817_135205.csv` | 5,18 Mo | Export CSV (non ré-analysé séparément — mêmes données que le JSON) |
| `ScientificDataset_MES_M5_20260817_135205_ohlcv.csv` | 263 Ko | OHLCV seul (non pertinent pour cet audit) |

Code source lu intégralement : `InstrumentRiskSpecification.cs`, `ATASInstrumentAdapter.cs`,
`ATASAccountStateAdapter.cs`, `ATASRuntimeDiagnostics.cs`, `RiskEngineRequestFactory.cs`, `RiskEngine.cs`,
`RiskRejectionReason.cs`, `RiskPolicyFactory.cs`, `AccountState.cs`, `ScientificDatasetRecord.cs`,
`PipelineTraceContext.cs`, `IQIAIndicator.cs` (sections Risk Engine + wiring Lot 12.2/12.3).

Rapports antérieurs relus pour continuité : Lot 12.1 (discovery ATAS), Lot 12.2 (binding), Lot 12.3
(diagnostic runtime).

Assemblies ATAS inspectées en lecture seule : `ATAS.DataFeedsCore.dll`, `ATAS.Types.dll`,
`ATAS.Indicators.dll`, `ATAS.Strategies.dll` (`C:\Program Files (x86)\ATAS Platform`, version
`7.0.9.461` — identique à la version citée au Lot 12.1, aucune dérive de version entre les deux audits).

---

## 3. Résultats du Replay MES

D'après `_metadata.json` / `_lifecycle.json` (valeurs lues, non recalculées) :

```
Symbol = MES, TimeFrame = M5
BarsReceived = 4428, BarsWritten = 2304
Duplicates = 0, InvalidRejected = 0, OutOfOrder = 0, LastError = null
FormingBarUpdates = 2123, BarsPendingAtDispose = 1
CollectionStart = 2026-08-17T11:52:05.25Z → CollectionEnd = 2026-08-17T12:20:04.62Z
FirstTimestamp (données marché) = 2026-07-27T22:00:00 → LastTimestamp = 2026-08-07T05:55:00
```

2304 enregistrements exportés = 2304 lignes analysées dans le JSON principal (cohérence confirmée,
aucune ligne manquante ou orpheline).

---

## 4. Risk rejection analysis

Comptage exhaustif sur les 2304 enregistrements (clé `Risk.Status` présente dans 100% des cas — 2304/2304) :

| Risk.Status | Nombre | % | Première occurrence | Dernière occurrence |
|---|---|---|---|---|
| `NOT AVAILABLE` | 1930 | 83,77% | 2026-07-27T22:00:00 | 2026-08-07T05:55:00 |
| `REJECTED` | 374 | 16,23% | 2026-07-28T04:55:00 | 2026-08-07T04:50:00 |
| `ACCEPTED` | **0** | 0% | — | — |

**PROVEN (par le code, `ScientificDatasetRecord.cs:125`)** : `Risk.Status = "NOT AVAILABLE"` signifie
`riskAssessment is null`, c'est-à-dire que `RiskEngineRequestFactory.FromTradePlan` a retourné `null` —
`RiskEngine.Evaluate` n'a **même pas été appelé** pour ces 1930 bars (aucun candidat directionnel dans le
TradePlan). Ce n'est donc pas un rejet du Risk Engine, c'est une absence d'évaluation.

Corrélation exacte avec `TradePlan.Status` :

| Risk.Status | TradePlan.Status | Nombre |
|---|---|---|
| `NOT AVAILABLE` | `NO_TRADE` | 1930 |
| `REJECTED` | `SIGNAL_ONLY` | 374 |

**PROVEN** : `INSTRUMENT_SPEC_INVALID` (et toute autre raison de rejet) n'apparaît **exclusivement** que
lorsque `TradePlan.Status = SIGNAL_ONLY` — jamais avec `NO_TRADE`, ce qui est structurellement impossible
(`Risk.Status` serait alors `NOT AVAILABLE`, pas `REJECTED`). Confirme directement la question posée en
Section 2 du lot.

### Combinaisons `Risk.RejectionReasons`

Seulement **2 combinaisons distinctes** existent parmi les 374 `REJECTED` (aucun `INSTRUMENT_SPEC_INVALID`
ni `INVALID_STOP_LOSS` isolé n'a été observé) :

| Combinaison | Nombre | Première occurrence | Dernière occurrence |
|---|---|---|---|
| `INSTRUMENT_SPEC_INVALID;INVALID_STOP_LOSS;RISK_BUDGET_EXCEEDED` | 94 | 2026-07-28T04:55:00 | 2026-07-31T11:55:00 |
| `INVALID_EQUITY;INSTRUMENT_SPEC_INVALID;INVALID_STOP_LOSS` | 280 | 2026-08-02T23:55:00 | 2026-08-07T04:50:00 |

94 + 280 = 374 = total exact des `REJECTED`. La chronologie des 133 transitions observées dans le fichier
montre que ces deux combinaisons **ne s'entrelacent jamais** : toutes les occurrences de la première
combinaison précèdent strictement toutes les occurrences de la seconde (voir Section 8 pour
l'interprétation).

**PROVEN** : `INSTRUMENT_SPEC_INVALID` et `INVALID_STOP_LOSS` apparaissent **ensemble dans 100% des 374
enregistrements REJECTED (374/374)** — jamais l'un sans l'autre dans cette capture. Ceci a une conséquence
importante pour la Section 8 : **le dataset seul ne permet pas d'isoler l'effet de QuantityStep
indépendamment du problème de Stop Loss**, puisque les deux causes sont toujours co-présentes ici. Seule
la lecture du code (Section 6) permet d'établir qu'elles sont des vérifications indépendantes.

`Risk.PositionSize`, `Risk.RiskAmount`, `Risk.RiskBudget`, `Risk.RiskRewardRatio` = `"NOT AVAILABLE"` dans
**100% des 2304 enregistrements**, sans exception — cohérent avec 0 `ACCEPTED` (ces champs ne sont jamais
renseignés par `RiskEngine.Evaluate` en dehors d'un `PositionSize` résolu, `RiskEngine.cs:181-215`).

---

## 5. Instrument specification analysis

Lecture de `InstrumentRiskSpecification.cs:40-48` (`IsValid`) :

```csharp
public bool IsValid =>
    !string.IsNullOrWhiteSpace(Symbol)
    && TickSize > 0m
    && TickValue > 0m
    && PointValue > 0m
    && MinQuantity > 0
    && MaxQuantity >= MinQuantity
    && QuantityStep > 0
    && (ContractMultiplier is null || ContractMultiplier > 0m);
```

C'est un **ET logique unique** : n'importe lequel de ces 7 sous-tests peut, à lui seul, produire
`INSTRUMENT_SPEC_INVALID` (`RiskEngine.cs:38-44`), sans que les autres soient nécessairement en cause.

| Champ | Source attendue (code) | Valeur observable dans CETTE capture |
|---|---|---|
| Symbol | `Security.Instrument` via `ATASInstrumentAdapter.Build` | **NOT OBSERVABLE** — le dataset porte un champ `Symbol="MES"` par enregistrement, mais celui-ci provient de `MarketContext`/`InstrumentInfo` (chemin ATAS distinct, utilisé depuis le Sprint 2), **pas** de `TradingManager.Security.Instrument` (chemin exclusif du Risk Engine, Lot 12.2). Les deux sont très probablement identiques en pratique (même chart, même instrument) mais ce n'est **pas prouvé** par cet artefact — ce sont deux lectures ATAS indépendantes. |
| TickSize | `Security.TickSize` | **NOT OBSERVABLE** (voir Section 7 — jamais exporté dans le dataset) |
| TickValue | `Security.TickCost` | **NOT OBSERVABLE** idem |
| PointValue | `TickCost / TickSize` (dérivé, `ATASInstrumentAdapter.cs:40`) | **NOT OBSERVABLE** idem (dépend des deux précédents) |
| MinQuantity | `Security.LotMinSize` si `>0`, sinon fallback `RiskInstrumentMinQuantity` (défaut code `0`) | **NOT OBSERVABLE** |
| MaxQuantity | `Security.LotMaxSize` si `>0`, sinon fallback `RiskInstrumentMaxQuantity` (défaut code `0`) | **NOT OBSERVABLE** |
| QuantityStep | **exclusivement** `RiskInstrumentQuantityStep` (défaut code `0`, jamais lu depuis ATAS) | **NOT OBSERVABLE** (valeur réelle utilisée pendant cette session précise) |
| ContractMultiplier | non peuplé par `ATASInstrumentAdapter.Build` (toujours `null`) | `null` — **CONFIRMED par le code** (le constructeur de `ATASInstrumentAdapter.Build`, ligne 45, ne passe jamais ce paramètre) |

Aucune de ces valeurs numériques réelles n'a été inventée ici — voir Section 7 pour la raison exacte pour
laquelle elles ne sont **pas observables** dans les artefacts fournis, malgré l'existence d'une
infrastructure de capture dédiée depuis le Lot 12.3.

---

## 6. QuantityStep analysis

**Où QuantityStep est lu** — `IQIAIndicator.cs:250` :
```csharp
public int RiskInstrumentQuantityStep { get; set; } = 0;
```
Paramètre UI manuel (`"Pas de quantite"`, groupe `"Risk Engine"`), défaut **`0`**.

**Quelle propriété ATAS est utilisée** — **aucune**. `ATASInstrumentAdapter.Build` (ligne 31-46) prend
`quantityStep` en paramètre et le transmet tel quel à `InstrumentRiskSpecification` sans jamais le lire
depuis `Security` :
```csharp
public static InstrumentRiskSpecification Build(
    Security? security, int fallbackMinQuantity, int fallbackMaxQuantity, int quantityStep)
{
    ...
    return new InstrumentRiskSpecification(symbol, tickSize, tickValue, pointValue, minQuantity, maxQuantity, quantityStep);
}
```
Le commentaire de la classe (lignes 17-21) le confirme explicitement : *"QuantityStep has no ATAS
equivalent (Lot 12.1, Section 11) - never fabricated"*. **CONFIRMED par la relecture directe du code**
(pas seulement par la documentation) — corroboré indépendamment par la ré-inspection ATAS de la
Section 7 ci-dessous.

**Quelle valeur l'adaptateur reçoit réellement / quelle valeur reçoit RiskEngine** — impossible à
déterminer pour cette session précise (voir Section 7 : l'infrastructure de capture existe mais son
résultat n'est exporté nulle part pour ce Replay).

**Le test PROUVE la suffisance, pas l'occurrence.** `RiskEngine.cs:38-44` vérifie
`request.Instrument.IsValid` de façon inconditionnelle et indépendante des 6 autres sous-conditions ; le
test `ATASRuntimeDiagnosticsTests.Test04` (Lot 12.3) confirme qu'un `QuantityStep=0` isolé, avec tout le
reste valide, suffit à produire `INSTRUMENT_SPEC_INVALID`. C'est un fait **PROVEN par le code et par
test**, indépendant de ce Replay.

**Verdict de cette section** : **NOT PROVEN par CETTE capture** que `QuantityStep` valait effectivement
`0` (ou une autre valeur invalide) pendant ce Replay MES précis — la valeur réelle n'est observable dans
aucun des artefacts fournis. **SUPPORTED avec un niveau de confiance élevé** par (a) la valeur par défaut
du paramètre (`0`), (b) l'absence totale de toute alternative automatique côté ATAS (Section 7), et (c)
la persistance d'`INSTRUMENT_SPEC_INVALID` sur l'intégralité des 374 bars rejetés, du premier au dernier,
sans jamais disparaître — un comportement cohérent avec une mauvaise configuration statique (jamais
modifiée en cours de session), à la différence du comportement d'`Equity` (Section 8, qui lui *change*
en cours de session).

---

## 7. ATAS API discovery

**Méthode** : réflexion .NET en lecture seule, exécutée hors du dépôt (script `dotnet run` scratchpad,
projet console .NET 8/10 éphémère, `System.Runtime.Loader.AssemblyLoadContext` avec résolveur de
dépendances pointant vers `C:\Program Files (x86)\ATAS Platform`). Aucune DLL ATAS modifiée, aucun
fichier créé dans le dépôt. Limite identique à celle notée au Lot 12.1 : `ATAS.Indicators.dll` ne charge
que partiellement (137/153 types — 16 échecs, dépendance WPF absente du runtime d'inspection) ;
`ATAS.Strategies.dll` 118/119 (1 échec) ; `ATAS.DataFeedsCore.dll` et `ATAS.Types.dll` chargent
intégralement.

**Recherche sur `ATAS.DataFeedsCore.Security`** (le type réellement utilisé par
`ATASInstrumentAdapter.Build`) pour tout membre contenant `QuantityStep`, `Quantity`, `VolumeStep`,
`LotStep`, `MinQuantity`, `MaxQuantity`, `ContractSize`, `Step`, `Volume`, `Lot` — résultat complet :

```
LotSize          : decimal
LotMinSize        : decimal?
LotMaxSize        : decimal?
BestAskVolume     : decimal
BestBidVolume     : decimal
LastTradeVolume   : decimal?
VolumeMultiplier  : decimal
```

**CONFIRMED (ré-audit indépendant, cohérent avec le Lot 12.1)** : aucune propriété `QuantityStep`,
`VolumeStep`, `LotStep`, `MinQuantity`, `MaxQuantity` ou `ContractSize` n'existe sur `Security`, ni sur
aucun type de `ATAS.DataFeedsCore.dll` ou `ATAS.Types.dll` (chargement complet des deux assemblies).

**Découverte nouvelle, hors du périmètre inspecté au Lot 12.1** : `ATAS.Indicators.ITradingManager`
(le type exact retourné par `Indicator.TradingManager`, celui-là même déjà utilisé par IQIA) expose une
propriété `TradingVolumeInfo : ITradingVolumeInfo`, non lue nulle part dans le code IQIA actuel :

```
ITradingVolumeInfo
    Currency              : string
    ActualVolume          : decimal
    VolumeFormat          : string
    IsCustomVolumeSelected: bool
    VolumeItems           : IVolumeSelectorItem[]
    CurrentVolumeItem     : IVolumeSelectorItem

IVolumeSelectorItem
    Volume       : decimal?
    ValueFormat  : string
```

**CONSISTENT WITH** un sélecteur de volume d'ordre (probablement le panneau de trading ATAS : liste de
volumes pré-configurés cliquables). **NOT PROVEN** que ceci représente un "pas de quantité" au sens du
Risk Engine : (a) c'est porté par `ITradingManager` (état de compte/UI de trading), pas par `Security`
(spécification de contrat) — portée sémantique différente de `QuantityStep` ; (b) `VolumeItems` est un
tableau de valeurs discrètes, pas un scalaire — en déduire un "pas" supposerait un espacement uniforme
entre éléments, ce qui serait une **inférence**, pas une lecture directe (contraire à la discipline
"jamais fabriqué" déjà appliquée par tout le code Lot 10-12.3) ; (c) sa disponibilité et son contenu réel
en Replay ne sont pas vérifiables par inspection statique. **REQUIRES LIVE ATAS VALIDATION** avant toute
conclusion — voir Section 12.

Aucune autre piste trouvée dans `ATAS.Indicators.dll`/`ATAS.Strategies.dll` (`IChartContainer.Step`,
`IPlatformSettings.ValueAreaStep`, `TrailingStopSettings.Step` sont sans rapport — respectivement pas de
grille chart, pas de value area du profil de volume, et incrément de trailing stop, aucun lien avec une
spécification d'instrument).

---

## 8. Equity analysis

`ATASAccountStateAdapter.cs` (Lot 12.2, inchangé) : `CurrentEquity` provient exclusivement du dernier
point de `TradingStatisticsProvider.Replay.Equity` (en Replay) ; si cette série est vide/absente,
`TryGetCurrentEquity` retourne `null` et `Build` résout `CurrentEquity = 0m` (sentinel volontaire, jamais
un fallback vers `Balance`/`InitialCapital`). `RiskEngine.cs:31-36` (Phase 1) rejette alors avec
`INVALID_EQUITY` si `CurrentEquity <= 0`.

Les artefacts de ce Replay ne contiennent **aucun champ Equity/Balance/Portfolio** (confirmé par
balayage exhaustif des clés `Metrics`/`Categories` du dataset — union vide). La valeur numérique brute de
`CurrentEquity` n'est donc **pas observable directement**. Néanmoins, un raisonnement **PROVEN par
croisement code + données** permet d'établir bien plus que cela :

`RiskEngine.cs`, Phase 8 (`153-177`) — `RISK_BUDGET_EXCEEDED` n'est ajouté que si
`riskBudgetExhausted && validEquity && !drawdownBreached && !dailyLossBreached && !openRiskBreached`.
Cette condition **exige `validEquity = true`**, donc `RISK_BUDGET_EXCEEDED` et `INVALID_EQUITY` sont
**mutuellement exclusifs par construction du code** — exactement ce qui est observé (jamais les deux
raisons ensemble dans les 374 rejets).

`RiskPolicyFactory.cs` (`PositiveOrNull`/`PositiveFractionOrNull`) : tout champ `RiskPolicyMaxRiskPerTrade*`
laissé à sa valeur par défaut `0` devient `null` (non contraint) dans `RiskPolicy`. Si **aucun** budget
par trade, perte journalière ou risque ouvert n'est configuré (les 8 paramètres `RiskPolicy*` par défaut
`0`), `riskBudget` résout systématiquement à `null` → `riskBudgetExhausted = true`, **indépendamment de la
valeur réelle de `CurrentEquity`** (tant qu'elle est positive).

**PROVEN (déduit du code, sans lecture de valeur brute)** :
- Les 94 bars `RISK_BUDGET_EXCEEDED` (2026-07-28T04:55 → 2026-07-31T11:55) prouvent que `CurrentEquity`
  était **strictement positif** (ATAS fournissait bien une lecture d'équité) durant cette fenêtre, et que
  `RiskPolicyMaxRiskPerTradePercent`/`Amount` (et les 6 autres champs `RiskPolicy*`) sont restés à leur
  défaut `0` (jamais configurés) pour toute la session.
- Les 280 bars `INVALID_EQUITY` (2026-08-02T23:55 → 2026-08-07T04:50) prouvent que `CurrentEquity` était
  devenu **≤ 0** durant cette fenêtre.
- L'instant exact de la transition est **NOT PROVEN** : il se situe quelque part entre 2026-07-31T11:55 et
  2026-08-02T23:55, une plage sans aucun candidat directionnel (`TradePlan.Status = NO_TRADE`,
  `Risk.Status = "NOT AVAILABLE"`), donc le Risk Engine n'a jamais été sollicité pour la sonder.
- La **cause** du passage à `Equity ≤ 0` reste **NOT PROVEN** (compte sans transaction, série `.Replay.Equity`
  vidée, `Portfolio`/`TradingStatisticsProvider` devenus indisponibles, ou tout autre comportement ATAS) —
  identique à l'inconnue déjà notée au Lot 12.3, simplement **bornée dans le temps** par ce lot.

**Pourquoi la valeur numérique brute reste invisible malgré une instrumentation dédiée existante** — fait
**PROVEN par lecture directe du code**, et le résultat le plus actionnable de cet audit :
`EnablePipelineTracing` était **actif** pendant ce Replay (**PROVEN** : chaque enregistrement du dataset
contient des champs `Trace.*.ElapsedMs`, qui ne sont ajoutés par `ScientificDatasetRecord.From` — ligne
`134` — que `if (trace is not null)`, c'est-à-dire uniquement quand `EnablePipelineTracing = true`).
`IQIAIndicator.cs:642-696` calcule bien, chaque bar, un dictionnaire `Details` riche
(`ATAS.RealtimeEquity`, `ATAS.ReplayEquity`, `ATAS.FinalQuantityStep`, `ATAS.SpecificationValid`,
`ATAS.InvalidFieldReasons`, etc.) et l'attache à l'événement de trace `Risk`. **Mais**
`ScientificDatasetRecord.From` (`ScientificDatasetRecord.cs:134-138`) ne lit, pour chaque
`PipelineTraceEvent`, que `traceEvent.Elapsed.TotalMilliseconds` — le dictionnaire `Details` qui contient
ces valeurs `ATAS.*` n'est **jamais lu par l'exporteur**, donc **jamais exporté**. Le seul endroit où ces
valeurs auraient été visibles est l'affichage live du `DebugDashboard`
(`context.PipelineTraceReport`, texte du dernier bar uniquement, jamais persisté) — invisible
rétrospectivement, et non capturé dans ces artefacts. Ce n'est donc pas une instrumentation absente : elle
tourne, calcule les bonnes valeurs, mais son résultat est perdu avant l'export.

---

## 9. StopLoss analysis

`TradePlan.StopLoss = "NOT AVAILABLE"` dans **100% des 2304 enregistrements** (les 1930 `NO_TRADE` *et*
les 374 `SIGNAL_ONLY`), sans une seule exception. `RiskEngineRequestFactory.cs:37` transmet
`tradePlan.StopLoss` tel quel (jamais recalculé) ; `RiskEngine.cs`, Phase 4 (`54-73`), rejette
inconditionnellement avec `INVALID_STOP_LOSS` dès que `request.StopLoss is null`.

**PROVEN, indépendant de QuantityStep** :
- Au niveau du **code** : Phase 2 (`validInstrument`) et Phase 4 (`validStopLoss`) sont deux vérifications
  disjointes dans `RiskEngine.Evaluate`, sans branchement conditionnel de l'une vers l'autre.
- Au niveau des **données** : `TradePlan.StopLoss` est absent pour 100% des bars, y compris les 1930
  `NO_TRADE` où le Risk Engine n'est même pas appelé — la cause de l'absence de SL est donc entièrement
  en amont du Risk Engine (le TradePlan lui-même ne produit jamais de SL), pas une conséquence d'un
  problème d'instrument.
- Ceci correspond exactement au comportement **attendu et documenté** au Lot 12.3, Section 8 : aucune
  méthodologie SL n'est implémentée dans cette build (interdit par les Lots 10-12 jusqu'à un lot dédié) —
  `"SL NOT PRODUCED"` est le comportement voulu, pas un défaut.

Comme noté en Section 4, `INSTRUMENT_SPEC_INVALID` et `INVALID_STOP_LOSS` co-occurrent 374/374 fois dans
CETTE capture — non pas parce qu'ils sont liés causalement, mais parce que les deux conditions qui les
déclenchent (QuantityStep non configuré, et absence de toute stratégie SL) sont actuellement **toutes
deux vraies en permanence** dans cette build/configuration.

---

## 10. Proven / Not Proven

### PROVEN
- `RiskEngine.Evaluate` se comporte de façon fail-closed et cohérente avec sa spécification — aucune
  anomalie de code trouvée.
- `QuantityStep <= 0`, isolément, suffit à produire `INSTRUMENT_SPEC_INVALID` (code + test Lot 12.3).
- `QuantityStep` ne provient **jamais** d'ATAS — exclusivement du paramètre manuel
  `RiskInstrumentQuantityStep`, défaut `0` (code).
- Aucune propriété ATAS équivalente à `QuantityStep`/`VolumeStep`/`LotStep`/`ContractSize` n'existe sur
  `Security` (ré-audit indépendant par réflexion, `ATAS.DataFeedsCore.dll`/`ATAS.Types.dll` chargées
  intégralement).
- `INSTRUMENT_SPEC_INVALID` n'apparaît que lorsque `TradePlan.Status = SIGNAL_ONLY` (jamais avec
  `NO_TRADE`).
- `INSTRUMENT_SPEC_INVALID` et `INVALID_STOP_LOSS` sont des vérifications indépendantes dans le code, bien
  que co-occurrentes à 100% dans cette capture précise.
- `TradePlan.StopLoss` est absent dans 100% des 2304 bars — le SL n'est jamais produit, indépendamment de
  l'instrument.
- `CurrentEquity` était positif (ATAS fournissait une lecture réelle) jusqu'à un instant entre
  2026-07-31T11:55 et 2026-08-02T23:55, puis invalide (`≤ 0`) jusqu'à la fin de la session (déduit du
  croisement `RiskEngine.cs` Phase 8 + combinatoire des `RejectionReasons`).
- `RiskPolicyMaxRiskPerTradePercent`/`Amount` (et les 6 autres champs `RiskPolicy*`) sont restés à leur
  défaut `0` (non configurés) pendant toute la session.
- `EnablePipelineTracing` était actif pendant ce Replay, mais les valeurs `ATAS.*` qu'il calcule
  (y compris `ATAS.FinalQuantityStep`) ne sont jamais lues par `ScientificDatasetRecord.From` — perdues
  avant tout export, pour une raison de code identifiée précisément (Section 8).

### NOT PROVEN
- La valeur numérique réelle de `QuantityStep` (ni `TickSize`/`TickValue`/`PointValue`/`LotMinSize`/
  `LotMaxSize`) effectivement utilisée par `RiskEngine` pendant ce Replay précis.
- Que `Security.Instrument` (chemin Risk Engine) valait bien `"MES"` pendant cette session — seul le champ
  `Symbol` du dataset (chemin `InstrumentInfo`, distinct) le confirme.
- La cause exacte du passage d'`Equity` valide à invalide en cours de session.
- Que `ITradingManager.TradingVolumeInfo.VolumeItems` constitue un substitut exploitable à `QuantityStep`
  (piste nouvelle, non testée en conditions réelles).

---

## 11. Verdict

**B — QuantityStep est une cause probable mais non prouvée** *(par cette capture spécifique)*.

Le niveau de confiance sur QuantityStep comme cause **au moins partielle** d'`INSTRUMENT_SPEC_INVALID`
reste élevé et **inchangé depuis le Lot 12.3** : défaut de code à `0`, aucune alternative ATAS
automatique confirmée (ré-audité indépendamment ici), et persistance du rejet sur l'intégralité de la
session sans jamais se résoudre — un pattern cohérent avec une configuration statique jamais modifiée.
Ce lot **n'apporte cependant aucune preuve nouvelle par valeur brute observée** : l'instrumentation
capable de le confirmer tournait bel et bien pendant ce Replay, mais son résultat n'a pas atteint les
artefacts exportés (Section 8) — un problème d'export, pas une absence de mesure. Le lot **fait
progresser** l'audit sur un point non couvert au Lot 12.3 : la preuve, par le code, que `CurrentEquity`
n'était **pas** invalide en permanence pendant ce Replay (contrairement à ce que l'hypothèse initiale du
lot aurait pu laisser supposer), mais l'est devenu à mi-session — une donnée nouvelle et non triviale.

---

## 12. Recommandation du prochain lot

**Aucune modification de `RiskEngine.cs`/`RiskPolicy.cs`/`InstrumentRiskSpecification.cs`/`EntryTriggerBuilder.cs`/
`TradePlanBuilder.cs` n'est justifiée par cet audit** — tous confirmés fail-closed et corrects.

Si une confirmation par valeur brute réelle est souhaitée (pour clore définitivement le Verdict B en A),
le chemin exact est déjà entièrement identifié par le code existant et ne nécessite qu'un export
manquant à combler :

```
RiskInstrumentQuantityStep (paramètre UI manuel "Pas de quantite", défaut 0)
        │  (passthrough direct, jamais lu depuis ATAS — ATASInstrumentAdapter.cs:35,45)
        ▼
ATASInstrumentAdapter.Build(quantityStep: ...)
        ▼
InstrumentRiskSpecification.QuantityStep
        ▼
RiskEngine.Evaluate → InstrumentRiskSpecification.IsValid (Phase 2) → INSTRUMENT_SPEC_INVALID si <= 0
```

Aucune API ATAS automatique n'existe pour remplacer ce paramètre (Section 7) — **le paramètre manuel
reste nécessaire**, ce qui n'est pas une régression mais l'état déjà documenté depuis le Lot 12.1.

Pistes concrètes pour un lot séparé (aucune décision ni implémentation prise ici) :
1. **Sans aucun code** : configurer `RiskInstrumentQuantityStep` (et par prudence
   `RiskInstrumentMinQuantity`/`MaxQuantity`) à une valeur positive dans le panneau ATAS avant un nouveau
   Replay, et observer si `INSTRUMENT_SPEC_INVALID` disparaît — validation directe sans écrire une ligne
   de code, déjà recommandée au Lot 12.3 et toujours valable.
2. Combler l'écart d'export identifié Section 8 (`ScientificDatasetRecord.From` n'exploite que
   `traceEvent.Elapsed`, jamais `traceEvent.Details`) pour qu'un futur Replay avec `EnablePipelineTracing`
   actif capture réellement `ATAS.FinalQuantityStep`/`ATAS.RealtimeEquity`/`ATAS.ReplayEquity`/
   `ATAS.InvalidFieldReasons` dans le dataset exporté — condition nécessaire pour transformer le Verdict B
   en A ou C par preuve directe plutôt que par déduction.
3. Investiguer en conditions ATAS réelles si `ITradingManager.TradingVolumeInfo.VolumeItems` (Section 7)
   contient un signal exploitable de pas de quantité — **REQUIRES LIVE ATAS VALIDATION**, portée
   sémantique différente de `Security` à confirmer avant toute intégration.
4. Le Stop Loss scientifique reste hors périmètre — traité dans un lot séparé, comme demandé.

STOP — fin du LOT 12.4. Aucune implémentation du LOT 12.5 effectuée.
