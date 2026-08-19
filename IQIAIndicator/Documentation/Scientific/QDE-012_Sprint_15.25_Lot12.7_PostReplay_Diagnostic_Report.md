# QDE-012 — Sprint 15.25 (Lot 12.7) — Post-Replay Diagnostic Final du Risk Engine MES

**Lot strictement offline. Audit / diagnostic uniquement.** Aucun fichier `.cs` ouvert en écriture,
aucune modification de `RiskEngine`/`RiskPolicy`/`InstrumentRiskSpecification`/`RiskEngineRequest`/
`RiskAssessment`/`EntryTrigger`/`DecisionArbitrator`/`TradePlan`/adaptateurs ATAS, aucun build, aucun
DLL, aucun commit. Toutes les valeurs citées ci-dessous proviennent soit du dataset exporté par ATAS,
soit d'une lecture directe (Read tool) du code source déjà en place — aucune n'a été inventée, déduite
ou extrapolée au-delà de ce que le fichier montre explicitement.

---

## 1. Dataset exact analysé

**`ScientificDataset_MES_M5_20260817_225152`** (`%LOCALAPPDATA%\IQIA\ScientificDataset\`), SessionId
`56d0ca83-2808-4e80-9985-ca49c4767978`.

Identification, non par simple recense-le-plus-récent mais par vérification croisée :

- **Timestamp de session** : `ConstructedAt` = `2026-08-17T20:50:36Z` UTC (session ATAS ouverte),
  `DatasetEnabledAt` = `2026-08-17T20:51:52Z`, `FirstAddAt` = `2026-08-17T20:52:48Z`, `LastAddAt` =
  `2026-08-17T20:55:13Z`. Postérieure au dataset `ScientificDataset_MES_M5_20260817_175446` (fenêtre
  UTC `15:54:46`–`16:17:42`) qui a servi de preuve au rapport du LOT 12.6 — donc bien un Replay
  **effectué après** le LOT 12.6.
- **Écart de dispose/export** : le fichier n'a été matérialisé sur disque (`OnDisposeEnteredAt` /
  `ExportStartedAt` / `ExportCompletedAt` / `SnapshotWrittenAt`) que le **2026-08-18T11:31:54Z**, soit
  ~14h40 après la dernière barre reçue — le chart/indicateur ATAS est resté ouvert (pas de `Dispose()`)
  jusqu'à cet instant. `LastError = null`, `BarsWritten = 63` : export réussi, sans anomalie de lifecycle.
- **Symbole/timeframe/fenêtre** : `Symbol = "MES"`, `TimeFrame = "M5"`, barres de
  `2026-08-02T22:00:00` à `2026-08-03T03:10:00` (`CurrentBar` 1104→1166 dans la série indicateur,
  passthrough `IndicatorCandle.Time`, aucune conversion de fuseau appliquée).
- **Nombre de barres** : `BarsReceived = 64`, `BarsWritten = 63` (1 barre en formation abandonnée au
  dispose — `BarsPendingAtDispose: 1`, comportement normal, non un défaut d'export).
- **Preuve la plus forte de "post-LOT 12.6"** : ce n'est pas seulement une question d'horodatage — les
  clés `ATAS.Adapter.Equity.HeuristicIsReplay`, `ATAS.Raw.Account.PortfolioIsReplay`,
  `ATAS.Adapter.Instrument.MinQuantitySource`, `ATAS.Adapter.Instrument.MaxQuantitySource`
  **introduites précisément par le LOT 12.6** (`ScientificDatasetRecord.cs`, 4 nouveaux paramètres)
  sont présentes et peuplées sur les 63/63 enregistrements. Un dataset produit par le build
  pré-LOT-12.6 n'aurait littéralement pas pu contenir ces clés. C'est donc du code-level, pas
  seulement de l'horodatage, que vient la confirmation.
- **Deux candidats écartés explicitement** : `ScientificDataset_MES_M5_20260817_225133` (même
  session ATAS ouverte 19s plus tôt, `BarsReceived: 0`, `LastError: "No data collected - nothing to
  export."` — dataset vide, sans intérêt diagnostique) et aucun autre dataset MES plus récent
  n'existe sur le disque.

---

## 2. Fenêtre temporelle

Barres rejouées : **`2026-08-02T22:00:00` → `2026-08-03T03:10:00`** (63 barres M5 consécutives,
`CurrentBar` 1104 à 1166, aucun trou/duplicat : `Duplicates: 0`, `OutOfOrder: 0` dans le fichier
`_metadata.json`).

## 3. Nombre de barres

**63** écrites sur **64** reçues (métadonnées `_metadata.json` et `_lifecycle.json` concordantes).

---

## 4. Equity — diagnostic

### Chaîne complète, barre par barre (63/63 identiques, aucune variation observée)

| Étage | Valeur observée (constante sur 63/63 barres) |
|---|---|
| `ATAS.Raw.Account.AccountID` | `"Replay"` |
| `ATAS.Raw.Account.IsRealAccount` | `False` |
| `ATAS.Raw.Account.Balance` / `BalancePower` | `0` |
| `ATAS.Raw.Account.BalanceAvailable` | `NOT AVAILABLE` |
| `ATAS.Raw.Equity.RealtimeValue` | `105.50` |
| `ATAS.Raw.Equity.ReplayValue` | `NOT AVAILABLE` |
| `ATAS.Raw.Equity.SeriesCount` | `0` |
| `ATAS.Raw.Equity.LastTimestamp` | `NOT AVAILABLE` |
| `ATAS.Adapter.Equity.HeuristicIsReplay` (ancienne heuristique bar-index, `context.Execution.IsReplay`) | `True` |
| `ATAS.Raw.Account.PortfolioIsReplay` (`Portfolio.IsReplay()`, télémétrie seule, non câblé) | `True` |
| **`ATAS.Raw.Equity.SourceMode`** (= décision **CORRIGÉE** du LOT 12.6, `ATASEquityReplayDetector.IsReplayContext`, câblée dans `IQIAIndicator.cs:647-653`) | **`"Replay"`** |
| `ATAS.Adapter.Account.CurrentEquity` (sortie de `ATASAccountStateAdapter.Build`) | `0` |
| `ATAS.RiskInput.Account.CurrentEquity` (valeur reçue par `RiskEngine`, sur les 13 barres où `RiskInput.Present=True`) | `0` |

**Vérification du code** (`ScientificDatasetRecord.cs:180-185`) : `ATAS.Raw.Equity.SourceMode` est
dérivé de `atasEquityIsReplay`, qui est exactement `_latestEquitySourceIsReplay` —
c'est-à-dire le résultat de `ATASEquityReplayDetector.IsReplayContext(...)`, la correction du LOT
12.6, **et non** l'ancienne heuristique. `ATAS.Adapter.Equity.HeuristicIsReplay` (nom trompeur — c'est
en réalité l'ANCIEN signal, conservé pour comparaison) porte l'ancienne heuristique
`context.Execution.IsReplay` non modifiée.

### Réponses aux 8 questions du brief

1. **Barres détectées Replay** : 63/63 (100 %), par `ATAS.Raw.Equity.SourceMode`.
2. **Barres détectées Live/Realtime** : 0/63.
3. **Basculements intempestifs** : aucun — `SourceMode` est constant `"Replay"` sur toute la capture (à
   comparer à l'alternance 1200 Replay/1104 Realtime *dans la même session* observée par le LOT 12.5
   sur le dataset `175446`, Section 12 ci-dessous).
4. **Première barre où le mode change** : sans objet — aucun changement de mode sur toute la capture.
5. **Dernière barre où le mode change** : sans objet, idem.
6. **Source Equity réellement sélectionnée** : `Replay` (`TradingStatisticsProvider.Replay.Equity`),
   sur 100 % des barres.
7. **Valeur Equity réellement envoyée à `AccountState`** : `0` (sentinel fail-closed,
   `ATASAccountStateAdapter.Build` ligne 59 : `resolvedEquity = currentEquity ?? 0m`).
8. **Valeur arrivant dans `RiskEngineRequest`** : `0` (confirmé sur les 13 barres où
   `ATAS.RiskInput.Present = True`, seules barres où une requête a effectivement été construite).

### D'où vient le `$0.00` affiché au dashboard ?

Réponse déterminée en suivant la valeur à travers les quatre étages (Raw → Detector → Adapter →
RiskInput → RiskEngine), sans conclure avant d'avoir tout tracé :

- **Ce n'est PAS (A)** une Equity "réellement nulle" au sens d'un compte financé à 0 — c'est un compte
  pseudo-`"Replay"` (`IsRealAccount=False`, `Balance=0`, `AccountID="Replay"`) qui n'a structurellement
  pas de solde/PnL simulé.
- **C'est (B)** une Equity Replay **indisponible** : `TradingStatisticsProvider.Replay.Equity` est une
  série vide (`SeriesCount=0`) sur 100 % des 63 barres, malgré une sélection de source désormais
  correcte (`SourceMode="Replay"` à chaque barre, jamais de fuite vers la valeur Realtime obsolète
  `105.50`).
- **Qui déclenche (C)** un sentinel fail-closed **intentionnel et documenté** :
  `ATASAccountStateAdapter.Build` (protégé, Lot 12.2, inchangé) résout `CurrentEquity = 0m` exactement
  comme conçu quand la série choisie est vide — **pas un bug**, le comportement voulu.
- **Ce n'est PAS (D)** un problème de binding : le binding Replay→Adapter→RiskInput est vérifié
  cohérent à chaque étage (`0` partout, sans falsification, sans conversion erronée).
- **Ce n'est PAS (E)**, dans cette capture précise, un problème de timing observable : `SourceMode` ne
  varie jamais au sein de la session (contrairement au LOT 12.5). Impossible cependant d'exclure un
  problème de timing dans l'absolu, faute d'avoir reproduit ici l'alternance vue au LOT 12.5 (voir
  limites, Section 15).
- **(F)** Cause structurelle probable, non démontrée avec certitude dans ce lot (diagnostic offline
  uniquement) : le compte ATAS `"Replay"` ne fait vraisemblablement remonter aucune valeur sur
  `ITradingStatistics.Replay.Equity` parce qu'un Replay de chart ATAS ne simule pas nécessairement un
  compte financé avec PnL — voir Section 10 pour la catégorisation formelle.

---

## 5. Replay/Live detection

Voir Section 4 — `ATASEquityReplayDetector.IsReplayContext` (LOT 12.6, fichier non modifié par ce lot,
lu uniquement) :

```
IsReplayContext(heuristicIsReplay, barTime, utcNow, maxLiveGap=24h):
    heuristicIsReplay OR (utcNow - barTime) > 24h
```

Dans cette capture, `heuristicIsReplay` (`ATAS.Adapter.Equity.HeuristicIsReplay`) était déjà `True` sur
63/63 barres — la clause `OR` de garde temporelle (barres datées du 2 août face à une exécution le 17
août, écart >> 24h) n'a **pas eu besoin d'intervenir** pour produire ce résultat, les deux signaux étant
d'accord partout. **Conséquence pour l'interprétation** : cette capture ne reproduit pas — et donc ne
teste pas empiriquement — le cas d'alternance intra-session (`heuristicIsReplay` passant à `False`)
qui avait justifié le correctif du LOT 12.6. Elle confirme l'absence de régression et le comportement
fail-closed, mais ne constitue pas une preuve empirique que la clause `OR` corrige un cas réel
d'alternance dans les conditions ATAS actuelles — seule une preuve mathématique (le code lui-même,
`OR` ne peut jamais retirer une détection Replay déjà correcte) le garantit.

---

## 6. QuantityStep — diagnostic

## 7-8. MinQuantity / MaxQuantity — diagnostic

## 9. Instrument specification — diagnostic

### Matrice complète (valeurs constantes sur 63/63 barres)

| Champ | ATAS Raw | Adapter | RiskInput (13 barres où présent) | Valide ? |
|---|---|---|---|---|
| Symbol | `"Micro E-mini S&P 500"` | `"Micro E-mini S&P 500"` | — (non exposé séparément) | ✅ |
| TickSize | `0.25` | `0.25` | — | ✅ |
| TickCost / TickValue | `1.25` | `1.25` | — | ✅ |
| PointValue | — (dérivé, Adapter uniquement) | `5` (= TickCost/TickSize = 1.25/0.25) | — | ✅ |
| LotSize | `1` | — (délibérément **non utilisé**, Lot 12.6 Section 4) | — | n/a |
| LotMinSize | `"NOT AVAILABLE"` | — | — | n/a (source ATAS absente) |
| LotMaxSize | `"NOT AVAILABLE"` | — | — | n/a (source ATAS absente) |
| **QuantityStep** | — (aucun équivalent ATAS, Lot 12.1/12.6) | **`0`** | `0` | ❌ |
| **MinQuantity** | — (via LotMinSize, absent) | **`0`** | `0` | ❌ |
| MaxQuantity | — (via LotMaxSize, absent) | `0` | `0` | ❌ (voir note) |
| MinQuantitySource | — | `"Manual"` | — | (confirme la branche fallback prise) |
| MaxQuantitySource | — | `"Manual"` | — | (confirme la branche fallback prise) |
| **`IsValid`** | — | **`False`** | `False` | — |

### Cause exacte, au niveau code (`InstrumentRiskSpecification.cs:40-48`)

```
IsValid = Symbol non vide && TickSize>0 && TickValue>0 && PointValue>0
          && MinQuantity>0 && MaxQuantity>=MinQuantity && QuantityStep>0
          && (ContractMultiplier null ou >0)
```

Sur les 8 conditions : Symbol/TickSize/TickValue/PointValue sont **vraies** (valeurs ATAS réelles,
non nulles). Échouent exactement deux conditions :

- **`MinQuantity > 0`** → faux (`MinQuantity = 0`).
- **`QuantityStep > 0`** → faux (`QuantityStep = 0`).

`MaxQuantity >= MinQuantity` est **trivialement vraie** (`0 >= 0`) — `MaxQuantity` ne bloque donc pas
`IsValid` par lui-même dans ce cas précis (il n'existe aucune condition `MaxQuantity > 0` isolée dans
la formule), même s'il vaut également `0` et n'est, de fait, pas exploitable.

### Origine de ces zéros (`ATASInstrumentAdapter.cs:31-46`, protégé, lu seulement)

- `QuantityStep` : **jamais dérivé d'ATAS** — c'est, sans branche conditionnelle, le paramètre manuel
  `quantityStep` (`RiskInstrumentQuantityStep`) tel que reçu par `Build(...)`. Le LOT 12.6 a
  explicitement **rejeté** l'usage de `Security.LotSize` comme source (résultat `1` par défaut du SDK
  ATAS, indiscernable d'une vraie donnée `LotSize=1` — voir le rapport LOT 12.6, Section 4). Ce `0`
  vient donc uniquement du fait que `RiskInstrumentQuantityStep` est configuré à `0` (non renseigné)
  dans le panneau ATAS de cette session.
- `MinQuantity`/`MaxQuantity` : `security?.LotMinSize is decimal lotMin && lotMin > 0m ? (int)lotMin :
  fallbackMinQuantity` — `LotMinSize`/`LotMaxSize` valent `"NOT AVAILABLE"` (raw, donc `null` côté
  C#) → la branche fallback est prise → `RiskInstrumentMinQuantity`/`RiskInstrumentMaxQuantity`
  (manuels) sont utilisés, et valent eux aussi `0` dans cette session.

**Aucune valeur n'a été inventée dans ce chemin** (Section 14 ci-dessous) : `0` est la valeur réelle du
paramètre manuel non configuré, jamais une substitution de `1`/`0.01`/autre.

---

## 10. Stop Loss — diagnostic

Sur les 13 barres où `ATAS.RiskInput.Present = True` (seules barres où une requête a effectivement
atteint `RiskEngine`) : `ATAS.RiskInput.StopLoss = "NOT AVAILABLE"` — **13/13**, sans exception.

Confirmé à la source (`TradePlan.StopLoss`, exemple barre `CurrentBar=1111`,
`2026-08-02T22:35:00`) :

```
TradePlan.Status            = "SIGNAL_ONLY"
TradePlan.StopLossAvailable = "False"
TradePlan.StopLoss          = "NOT AVAILABLE"
TradePlan.TakeProfit        = "7548.75088944699"   (présent, calculé indépendamment)
```

`RiskEngineRequestFactory.FromTradePlan` (`RiskEngineRequestFactory.cs:34-42`, lu seulement) transmet
`tradePlan.StopLoss` **tel quel**, sans jamais le fabriquer — la valeur `null` traverse directement
jusqu'à `RiskEngineRequest.StopLoss`. `RiskEngine.Evaluate` (`RiskEngine.cs:58-73`) : `validStopLoss`
reste `false` quand `request.StopLoss` est `null`, et ajoute `INVALID_STOP_LOSS` avec le diagnostic
littéral `"StopLoss was not provided."`.

`TradePlanStatus.SIGNAL_ONLY` est un statut **documenté et pré-existant** (Sprint 15.8,
`TradePlan.cs:10-11`) : *"a directional candidate exists but a required component (StopLoss,
PositionSize, ...) is categorically unavailable in the system today (no methodology/config exists for
it yet)"*. Ce n'est pas un défaut introduit par les LOTs 12.x — c'est une lacune de méthodologie SL
déjà connue et documentée avant ce lot, hors périmètre de ce diagnostic (aucune stratégie SL n'a été
créée, conformément à la RÈGLE ABSOLUE).

**Résultat attendu confirmé** : Stop Loss = **NOT PRODUCED**, exactement comme observé sur le
dashboard.

---

## 11. RiskEngine rejection chain

Lecture intégrale de `RiskEngine.Evaluate` (`RiskEngine.cs`, protégé, lu seulement) :

```
Account
   │
   ├─ Phase 1 : Equity validation      (CurrentEquity > 0)         → indépendante, jamais court-circuitée
   ├─ Phase 2 : Instrument validation  (Instrument.IsValid)        → indépendante, jamais court-circuitée
   ├─ Phase 3 : Entry price validation (EntryPrice > 0)            → indépendante
   ├─ Phase 4 : Stop Loss validation                                → indépendante (dépend seulement d'Entry)
   ├─ Phase 5 : Risk per unit          (dépend de Phase 2 + 4 pour être CALCULÉ, pas pour être VALIDÉ)
   ├─ Phase 6 : Take Profit / Reward validation
   ├─ Phase 7 : Drawdown / Daily loss / Open risk (indépendantes entre elles)
   ├─ Phase 8 : Risk budget            (dépend de Phase 1 pour être calculé)
   ├─ Phase 9 : Position sizing        (dépend de Phase 2 + 8 pour produire un résultat)
   ├─ Phase 10: Risk/Reward
   └─ Phase 11: Safety net (position size non résolu malgré entrées nominalement valides)
          │
          ▼
   reasons.Count == 0 && positionSize != null  →  ACCEPTED, sinon REJECTED
```

**Réponses explicites** :

- **Les trois raisons sont-elles indépendantes ?** OUI, au niveau du code : chaque `if (!validXxx)` est
  une vérification séparée, sans `return` anticipé ni `else` qui empêcherait l'exécution des phases
  suivantes. Les trois blocs (Phase 1 Equity, Phase 2 Instrument, Phase 4 StopLoss) s'exécutent
  systématiquement, quel que soit le résultat des précédents.
- **`INVALID_EQUITY` empêche-t-il l'évaluation de l'instrument ?** NON — Phase 2 ne lit aucune variable
  produite par Phase 1 (`validEquity`/`request.Account.CurrentEquity` n'apparaissent nulle part dans
  Phase 2).
- **`INSTRUMENT_SPEC_INVALID` est-il indépendant de l'Equity ?** OUI, confirmé ci-dessus (Phase 2 ne
  dépend que de `request.Instrument.IsValid`).
- **`INVALID_STOP_LOSS` est-il indépendant des deux autres ?** OUI — Phase 4 ne dépend que de
  `validEntry` et `request.StopLoss`, jamais de `validEquity` ni `validInstrument`.
- **Une première erreur masque-t-elle les suivantes ?** NON — c'est le co-titre du commentaire du code
  lui-même (`RiskEngine.cs:101-102`) : *"Evaluated independently of one another so that several
  simultaneous violations are all reported, not just the first one hit."*
- **Plusieurs validations sont-elles exécutées sur la même requête ?** OUI — les 11 phases s'exécutent
  toutes, dans l'ordre, sur le même `RiskEngineRequest`.

**Conséquence pour l'interprétation de la capture** : la co-occurrence systématique des trois raisons
sur les 13/13 barres REJECTED de ce dataset est une **coïncidence de conditions réellement toutes
vraies simultanément** (Equity=0 ET Instrument invalide ET StopLoss absent, à chaque fois) — **pas** un
artefact de masquage. Le code garantit qu'une seule cause vraie suffirait à faire apparaître UNE seule
raison ; ce dataset ne permet cependant pas, à lui seul, d'observer empiriquement un cas où une seule
des trois est vraie (Section 15, limites).

---

## 12. Comparaison LOT 12.5 / LOT 12.6 / LOT 12.7

| Élément | LOT 12.5 (`..._175446`, 2304 barres) | LOT 12.6 (analyse du même dataset) | **LOT 12.7 (`..._225152`, 63 barres, ce lot)** |
|---|---|---|---|
| `ATAS.Raw.Equity.SourceMode` | Alterne `"Replay"` (1200) / `"Realtime"` (1104) **dans la même session** | Diagnostiqué comme bug d'heuristique bar-index | **`"Replay"` constant, 63/63 — aucune alternance observée** |
| `ATAS.Raw.Equity.RealtimeValue` | `105.50` constant, `LastTimestamp` figé `2026-08-10T14:02:10` | — | `105.50` constant, `LastTimestamp` = `"NOT AVAILABLE"` (jamais lu comme source ici, car `SourceMode` reste `Replay` à chaque barre) |
| `ATAS.Raw.Equity.ReplayValue` / `SeriesCount` | `"NOT AVAILABLE"` / `0` sur les 1200 enregistrements en mode Replay | Diagnostiqué "déjà correct, jamais le problème" | `"NOT AVAILABLE"` / `0` — **identique**, 63/63 |
| `ATAS.Adapter.Instrument.QuantityStep`/`MinQuantity`/`MaxQuantity` | `0`/`0`/`0`, 2304/2304 | Investigué (`LotSize`), binding rejeté (RÈGLE D'ARRÊT) | `0`/`0`/`0`, **identique**, 63/63 |
| `ATAS.Adapter.Instrument.IsValid` | `False`, 2304/2304 | Inchangé, délibéré | `False`, **identique**, 63/63 |
| `Risk.Status` sur barres avec requête | `REJECTED` | — | `REJECTED`, 13/13 |
| `Risk.RejectionReasons` | (non cité individuellement dans le rapport 12.6, mais les 3 causes racines identiques sont documentées) | `INVALID_EQUITY` + `INSTRUMENT_SPEC_INVALID` (StopLoss non traité explicitement au 12.5) | `INVALID_EQUITY;INSTRUMENT_SPEC_INVALID;INVALID_STOP_LOSS`, 13/13 |

### Le LOT 12.6 a-t-il corrigé le problème Equity ?

## **PARTIAL**

Justification, strictement sur données observées :

- **Corrigé** : la fuite de la valeur Realtime obsolète (`105.50`, datée de jours avant les barres
  rejouées) vers l'Equity utilisée pendant un Replay — le symptôme précis ciblé par le LOT 12.6 —
  **n'est plus observée** dans cette capture. `SourceMode` reste `"Replay"` sur 100 % des barres, sans
  aucune des 1104 occurrences `"Realtime"` vues au LOT 12.5.
- **Non corrigé, car hors périmètre déclaré du LOT 12.6** : le symptôme visible (`INVALID_EQUITY`,
  Equity affichée `$0.00`) **persiste à l'identique**, parce que sa cause réelle —
  `TradingStatisticsProvider.Replay.Equity` structurellement vide — est en amont de la correction du
  LOT 12.6 et n'a jamais été son objectif (le LOT 12.6 le documente lui-même explicitement, Section 8
  de son propre rapport : *"l'échec de repli fail-closed... est déjà correct et n'a jamais été le
  problème"*).
- **Nuance PARTIAL plutôt que NO** : la correction n'est pas cosmétique — elle élimine une classe de
  bug réelle et démontrée (utilisation d'une valeur financière obsolète comme si elle était courante,
  un risque de look-ahead). Le fait que le symptôme utilisateur final (REJECTED/$0.00) reste identique
  n'invalide pas la correction ; il révèle qu'il existe une **deuxième cause, distincte et non traitée**,
  en amont.

---

## 13. Cause(s) confirmée(s)

Confirmées par lecture directe du dataset ET du code (double preuve) :

1. **`INVALID_EQUITY`** : `TradingStatisticsProvider.Replay.Equity` (série ATAS) vide (`SeriesCount=0`)
   sur 100 % des 63 barres de cette capture → `ATASAccountStateAdapter.TryGetCurrentEquity` retourne
   `null` → `Build` résout `0m` (sentinel fail-closed, code inchangé, comportement voulu) →
   `RiskEngine` Phase 1 rejette. La sélection de source elle-même (`Replay` vs `Realtime`, LOT 12.6)
   est correcte à chaque barre de cette capture.
2. **`INSTRUMENT_SPEC_INVALID`** : `QuantityStep` et `MinQuantity` valent `0` (paramètres manuels
   `RiskInstrumentQuantityStep`/`RiskInstrumentMinQuantity` non configurés dans cette session ATAS),
   `Security.LotMinSize`/`LotMaxSize` restent non exposés par ce flux ATAS (`"NOT AVAILABLE"`
   sur 63/63) → fallback manuel pris, également à `0` → `InstrumentRiskSpecification.IsValid` faux sur
   exactement ces deux conditions.
3. **`INVALID_STOP_LOSS`** : `TradePlan.StopLoss` est `null` sur les 13/13 barres avec un candidat
   directionnel, parce que `TradePlan.Status = SIGNAL_ONLY` — lacune de méthodologie Stop Loss connue
   et documentée depuis le Sprint 15.8, indépendante des LOTs 12.x.
4. **Indépendance des trois raisons** : prouvée par lecture du code de `RiskEngine.Evaluate` (aucun
   court-circuit entre Phases 1/2/4).

## 14. Cause(s) probable(s)

1. **Pourquoi `TradingStatisticsProvider.Replay.Equity` est vide** : probablement parce que le compte
   `AccountID="Replay"` (`IsRealAccount=False`, `Balance=0`) est un pseudo-compte de rejeu de chart
   ATAS qui ne simule pas de PnL/solde — une hypothèse cohérente avec `Balance`/`BalancePower`/
   `OpenPnL`/`ClosedPnL`/`TotalPnL` valant tous `0` en télémétrie brute, mais **non confirmée** par une
   source ATAS officielle dans ce lot (aucune investigation SDK n'était autorisée ici).
2. **Pourquoi `RiskInstrumentQuantityStep`/`MinQuantity`/`MaxQuantity` valent `0`** : probablement un
   paramètre non configuré dans le panneau ATAS pour cette session de Replay précise (l'utilisateur ne
   les a pas renseignés), plutôt qu'un défaut structurel — mais ce lot ne peut pas distinguer
   "paramètre oublié" de "paramètre volontairement laissé à 0" sans accès à la configuration réelle du
   panneau ATAS au moment du Replay.

## 15. Cause(s) non démontrées

1. **Le correctif du LOT 12.6 (clause `OR` de garde 24h) fonctionne-t-il sous alternance réelle
   heuristique True/False au sein d'une session ?** Non testé par cette capture — `HeuristicIsReplay`
   était déjà `True` sur 100 % des barres ici, contrairement à la capture du LOT 12.5. La preuve
   mathématique du code (`OR` ne peut jamais retirer une détection déjà correcte) tient toujours, mais
   aucune preuve empirique nouvelle n'a été apportée par ce Replay.
2. **`Portfolio.IsReplay()` (`ATAS.Raw.Account.PortfolioIsReplay`) concorde-t-il de façon fiable avec
   `SourceMode` en conditions réelles ?** Dans cette capture, `PortfolioIsReplay=True` concorde avec
   `SourceMode="Replay"` sur 63/63 — une première confirmation empirique cohérente avec la
   recommandation du LOT 12.6 Section 17, mais un seul échantillon (une seule session) reste
   insuffisant pour conclure à une fiabilité générale.
3. **La cause structurelle exacte de la série `Replay.Equity` vide** (hypothèse Section 14.1) —
   nécessiterait une investigation SDK/documentation ATAS, explicitement hors périmètre de ce lot
   offline.
4. **L'anomalie mineure `ATAS.Adapter.Account.InitialCapital = "0.0"`** sur les 6 toutes premières
   barres de la session (`CurrentBar` 1104-1109, `2026-08-02T22:00:00`-`22:25:00`) avant de se
   stabiliser à `25000` sur les 57 barres suivantes — probable artefact de warm-up (paramètre pas
   encore lu par l'indicateur au tout début du Replay). **Sans impact sur les rejets observés** :
   aucune des 6 barres concernées n'a de `RiskInput.Present=True` (aucune n'a atteint le Risk Engine).
   Non investiguée plus avant (hors périmètre, n'affecte aucun rejet réel).

---

## 16. Prochaine action recommandée

Aucune action corrective n'est prescrite par ce lot (audit uniquement). Élément factuel pour une
décision future : les deux blocages restants (`INVALID_EQUITY` par série ATAS structurellement vide,
`INSTRUMENT_SPEC_INVALID` par paramètres manuels non configurés) sont de nature différente l'une de
l'autre et ne partagent pas de cause commune — une correction de l'un ne débloquera pas l'autre.

## 17. Données qui doivent être collectées si une nouvelle capture est nécessaire

1. Un Replay MES M5 avec le panneau ATAS de l'indicateur montré/exporté en même temps (capture écran
   des valeurs `RiskInstrumentQuantityStep`/`MinQuantity`/`MaxQuantity` telles que réellement saisies),
   pour distinguer "paramètre oublié" de "valeur volontaire".
2. Un Replay ou une session live sur un compte ATAS **autre que** le pseudo-compte `"Replay"` (un
   compte Démo/Simulateur réellement financé) pour vérifier si `ITradingStatistics.*.Equity` se peuple
   dans ce contexte — testerait directement l'hypothèse de la Section 14.1.
3. Une session où `context.Execution.IsReplay` (l'heuristique bar-index) est effectivement `False` sur
   au moins une portion des barres rejouées, pour vérifier empiriquement que la clause `OR` du LOT
   12.6 intervient bel et bien (elle ne l'a jamais fait dans cette capture, les deux signaux étant
   toujours d'accord).
4. Idéalement, une capture où au moins une des trois causes de rejet (`Equity`, `Instrument`,
   `StopLoss`) est individuellement résolue, pour observer empiriquement l'indépendance déjà prouvée
   par lecture du code (Section 11) et confirmer qu'un `RiskAssessment` peut porter moins de trois
   raisons simultanément dans un cas réel.

---

# LOT 12.7 RESULT

Status : AUDIT COMPLETE — AUCUNE MODIFICATION EFFECTUÉE

Dataset : ScientificDataset_MES_M5_20260817_225152 (SessionId 56d0ca83-2808-4e80-9985-ca49c4767978)

Bars : 63 written / 64 received (2026-08-02T22:00:00 → 2026-08-03T03:10:00)

Equity Replay :
- Raw : ReplayValue=NOT AVAILABLE, SeriesCount=0, RealtimeValue=105.50 (63/63 barres)
- Detector : ATASEquityReplayDetector.IsReplayContext = True (63/63) ; HeuristicIsReplay seul déjà True (63/63), clause OR de garde 24h non sollicitée dans cette capture
- Adapter : ATAS.Raw.Equity.SourceMode = "Replay" (63/63) → CurrentEquity = 0 (sentinel fail-closed, 63/63)
- RiskInput : CurrentEquity = 0 (13/13 barres où RiskInput.Present=True)
- RiskEngine : validEquity = false → INVALID_EQUITY (13/13)
- Verdict : sélection de source CORRIGÉE et fonctionnelle (plus de fuite vers Realtime obsolète) ; symptôme persiste car la série Replay.Equity elle-même reste vide côté ATAS — cause distincte, en amont, hors périmètre LOT 12.6

Instrument :
- Symbol : "Micro E-mini S&P 500" (ATAS Raw + Adapter, 63/63)
- TickSize : 0.25 (Raw + Adapter, 63/63)
- TickCost : 1.25 (Raw + Adapter, 63/63)
- QuantityStep : 0 (Adapter + RiskInput, 63/63) — jamais dérivé d'ATAS (LOT 12.6, délibéré), = paramètre manuel RiskInstrumentQuantityStep non configuré
- MinQuantity : 0 (Adapter + RiskInput, 63/63) — LotMinSize=NOT AVAILABLE côté ATAS → fallback manuel = 0
- MaxQuantity : 0 (Adapter + RiskInput, 63/63) — LotMaxSize=NOT AVAILABLE côté ATAS → fallback manuel = 0
- IsValid : False (63/63)
- Cause : échec exact de MinQuantity>0 ET QuantityStep>0 dans InstrumentRiskSpecification.IsValid ; Symbol/TickSize/TickValue/PointValue tous valides

Stop Loss :
- Status : NOT PRODUCED (TradePlan.StopLossAvailable=False, TradePlan.Status=SIGNAL_ONLY, 13/13 barres avec requête)
- Cause : lacune de méthodologie Stop Loss connue et documentée depuis le Sprint 15.8 (TradePlan.cs), indépendante des LOTs 12.x, hors périmètre de ce lot

Risk Rejection :
- INVALID_EQUITY : 13/13 barres REJECTED (100%)
- INSTRUMENT_SPEC_INVALID : 13/13 barres REJECTED (100%)
- INVALID_STOP_LOSS : 13/13 barres REJECTED (100%)
- Indépendance confirmée par lecture de RiskEngine.Evaluate (aucun court-circuit entre les 3 phases) ; co-occurrence systématique dans ce dataset = coïncidence de conditions réellement toutes vraies, non un masquage

LOT 12.6 Equity Fix :
- PARTIAL

Root Cause : Deux causes racines distinctes et indépendantes bloquent RiskEngine : (1) TradingStatisticsProvider.Replay.Equity structurellement vide côté ATAS pour ce compte "Replay" (cause probable, non confirmée en amont du LOT 12.6, catégorie B/C) ; (2) paramètres manuels RiskInstrumentQuantityStep/MinQuantity/MaxQuantity non configurés dans cette session (catégorie B — LotMinSize/LotMaxSize non exposés par ce flux ATAS, fallback manuel resté à 0). Stop Loss NOT PRODUCED est un troisième blocage, connu et attendu, sans lien avec les deux premiers.

Next Action : Voir Section 16-17 du rapport — aucune action corrective prescrite par ce lot (audit uniquement) ; collecter les 4 types de données listés Section 17 avant toute décision de correction.

Files modified : NONE

Build : NOT RUN

Tests : NOT RUN

DLL deployed : NO

Commit : NO

Replay executed : YES (already provided by user)

STOP — FIN DU LOT 12.7.
