# QDE-012 — Sprint 15.25 (Lot 12.1) — ATAS Account & Instrument State Discovery

**Lot de discovery/audit uniquement — aucun code de production modifié, aucun fichier créé dans
`IQIAIndicator/`, aucun build de la solution IQIA, aucun test modifié.** Ce document est le seul
livrable, à l'emplacement demandé en Section 15 du lot.

## Méthodologie et limites de la méthode

Les assemblies ATAS installées (`C:\Program Files (x86)\ATAS Platform`, version `7.0.9.461` pour
`ATAS.DataFeedsCore.dll`/`ATAS.Indicators.dll`/`ATAS.Types.dll`) ont été inspectées en lecture seule par
**réflexion .NET** (`System.Reflection`, chargement via `Assembly.LoadFrom` dans un script F# éphémère
exécuté avec `dotnet fsi` — un interpréteur de script, pas une compilation de projet ; aucun `.dll` n'a
été généré, aucun fichier du dépôt IQIA touché) et par **extraction de chaînes** (scan binaire read-only
du contenu texte imprimable des DLL, méthode explicitement suggérée par la Section 12 du lot). Aucune
DLL ATAS n'a été modifiée. Les scripts d'inspection et leurs sorties ont été produits uniquement dans le
répertoire scratchpad temporaire de cette session, hors du dépôt.

**Limite de méthode** : hors d'un hôte ATAS réel (WPF), certains types (`BaseIndicator`,
`ExtendedIndicator`) n'ont pu être énumérés que partiellement (dépendance `PresentationCore` absente du
runtime .NET utilisé pour l'inspection) — dans ces cas, la recherche a été refaite par lookup de membre
nommé individuel (`Type.GetProperty(name, ...)`), qui contourne le problème. Certaines visibilités
d'accesseur (`public` vs `protected`) rapportées par la réflexion hors hôte ATAS restent à confirmer en
conditions réelles — marquées **REQUIRES LIVE ATAS VALIDATION** ci-dessous.

---

## 1. Executive Summary

ATAS expose, via `Indicator` (classe de base d'`IQIAIndicator`), un objet `TradingManager`
(`ATAS.Indicators.ITradingManager`) qui donne accès en lecture à `Portfolio` (compte),
`Position` (position courante) et `Security` (spécification instrument complète : tick size, tick cost,
lot min/max, devise, etc.) — **CONFIRMÉ par réflexion**. Un second point d'accès,
`TradingStatisticsProvider` (**CONFIRMÉ public**, déjà visible dans `Indicator`), donne une série
temporelle d'`Equity` distincte pour Replay et Realtime. **Aucune de ces API n'est câblée aujourd'hui**
dans IQIA (Lots 10-12 utilisent exclusivement les paramètres UI manuels `Risk*`). L'architecture cible
(Section 8) est **directement réalisable avec adaptation** : un nouvel adaptateur de lecture seule
pourrait peupler `AccountState`/`InstrumentRiskSpecification` (Lot 10, inchangés) à partir de
`TradingManager.Portfolio`/`.Security` — sans aucune logique `if symbol == "ES"`, puisque `Security` est
déjà une donnée par-instrument fournie par ATAS. Le comportement en Replay reste
**REQUIRES LIVE ATAS VALIDATION** (l'API existe et distingue explicitement Replay/Realtime au niveau des
statistiques, mais la disponibilité réelle de `Portfolio`/`Position`/`Security` pendant un Replay n'est
pas déterminable par inspection statique).

---

## 2. ATAS APIs découvertes

| API | Assembly | Namespace | Type | Statut |
|---|---|---|---|---|
| `Indicator.TradingManager` | ATAS.Indicators.dll | `ATAS.Indicators` | prop → `ITradingManager` | **CONFIRMED** (existence) ; accessibilité exacte (public/protected) **REQUIRES LIVE ATAS VALIDATION** |
| `ITradingManager.Portfolio` | ATAS.Indicators.dll | `ATAS.Indicators` | prop get-only → `ATAS.DataFeedsCore.Portfolio` | **CONFIRMED** |
| `ITradingManager.Position` | ATAS.Indicators.dll | `ATAS.Indicators` | prop get-only → `ATAS.DataFeedsCore.Position` | **CONFIRMED** |
| `ITradingManager.Security` | ATAS.Indicators.dll | `ATAS.Indicators` | prop get-only → `ATAS.DataFeedsCore.Security` | **CONFIRMED** |
| `ITradingManager.Orders` / `.MyTrades` | ATAS.Indicators.dll | `ATAS.Indicators` | prop get-only → `IEnumerable<Order>`/`IEnumerable<MyTrade>` | **CONFIRMED** |
| `ITradingManager.PortfolioChanged` / `.PositionChanged` / `.SecuritySelected` / `.PortfolioSelected` | ATAS.Indicators.dll | `ATAS.Indicators` | events `Action<T>` | **CONFIRMED** |
| `ITradingManager.OpenOrder/ModifyOrder/CancelOrder/ClosePosition/SetStopLoss/SetTakeProfit` | ATAS.Indicators.dll | `ATAS.Indicators` | methods | **CONFIRMED existants — NE DOIVENT PAS être utilisés** (trading réel, hors périmètre Risk Engine) |
| `Indicator.TradingStatisticsProvider` | ATAS.Indicators.dll | `ATAS.Indicators` | prop get-only, **public confirmé** → `ATAS.DataFeedsCore.Statistics.ITradingStatisticsProvider` | **CONFIRMED** |
| `ITradingStatisticsProvider.Replay` / `.Realtime` | ATAS.DataFeedsCore.dll | `ATAS.DataFeedsCore.Statistics` | prop → `ITradingStatistics` | **CONFIRMED** |
| `ITradingStatistics.Equity` | ATAS.DataFeedsCore.dll | `ATAS.DataFeedsCore.Statistics` | prop get-only → `IMutableEnumerable<EquityValue>` (série temporelle) | **CONFIRMED** |
| `EquityValue.Equity` / `.TotalEquity` | ATAS.DataFeedsCore.dll | `ATAS.DataFeedsCore.Statistics` | prop decimal | **CONFIRMED** |
| `Indicator.InstrumentInfo` | ATAS.Indicators.dll | `ATAS.Indicators` | prop → `IInstrumentInfo` | **CONFIRMED public** (déjà utilisé en production, `IQIAIndicator.cs` depuis le Sprint 2) |

---

## 3. Account State

| Donnée demandée (Section 3.A) | API ATAS | Type | Statut |
|---|---|---|---|
| Account Balance | `Portfolio.Balance` | `decimal` | **CONFIRMED** |
| Equity | Pas de propriété directe sur `Portfolio`. Dérivable de `TradingStatistics.Equity` (dernier `EquityValue.Equity`), ou par calcul `Balance + OpenPnL` | `decimal` (dérivé) | **INFERRED** — aucune propriété nommée `Equity` trouvée directement sur `Portfolio` |
| Available Funds / Buying Power | `Portfolio.BalanceAvailable` (nullable), `Portfolio.BalancePower` | `decimal?` / `decimal` | **CONFIRMED** (existence) — sémantique exacte (marge dispo vs buying power future) **REQUIRES LIVE ATAS VALIDATION** |
| Unrealized P&L | `Position.UnrealizedPnL` (par position) ou `Portfolio.OpenPnL` (compte) | `decimal` | **CONFIRMED** |
| Realized P&L | `Position.RealizedPnL` (par position) ou `Portfolio.ClosedPnL`/`TotalClosedPnL` (compte) | `decimal` | **CONFIRMED** |
| Account P&L | `Portfolio.TotalPnL` (lecture seule) | `decimal` | **CONFIRMED** |
| Account identifier | `Portfolio.AccountID` | `string` | **CONFIRMED** |
| Account currency | `Portfolio.Currency` (enum `Currencies?`) | `Currencies?` | **CONFIRMED** (existence) — mapping vers un code devise texte **REQUIRES LIVE ATAS VALIDATION** |
| Position actuelle | `TradingManager.Position` | `Position` | **CONFIRMED** |
| Quantité détenue | `Position.Volume` (`OpenVolume`, `CurrentBuy`, `CurrentSell` aussi présents) | `decimal` | **CONFIRMED** |
| P&L de la position | `Position.UnrealizedPnL` / `.RealizedPnL` | `decimal` | **CONFIRMED** |
| Levier | `Portfolio.Leverage` | `decimal` | **CONFIRMED** |
| Marge bloquée | `Portfolio.BlockedMargin` | `decimal` | **CONFIRMED** |
| Compte réel vs simulé | `Portfolio.IsRealAccount` | `bool` | **CONFIRMED** |
| Historique équity | `TradingStatisticsProvider.Realtime.Equity` / `.Replay.Equity` | `IMutableEnumerable<EquityValue>` (SecurityId, Time, TotalEquity, Equity) | **CONFIRMED** — série temporelle, pas un scalaire |

**NOT AVAILABLE FROM INDICATOR CONTEXT (aucune propriété trouvée)** : "InitialCapital"/capital de départ
n'existe sous aucune forme dans `Portfolio` — ATAS ne modélise qu'un état de compte courant (`Balance`),
jamais une valeur de capital initial figée (voir Section 9).

---

## 4. Instrument State

| Donnée demandée (Section 4) | API ATAS | Type | Statut |
|---|---|---|---|
| Symbol | `Security.Instrument` | `string` | **CONFIRMED** |
| Exchange | `Security.Exchange` | `string` | **CONFIRMED** |
| Security / Contract | `TradingManager.Security` (objet `Security` complet) | `Security` | **CONFIRMED** |
| Tick Size | `Security.TickSize` | `decimal` | **CONFIRMED** |
| Tick Value/Cost | `Security.TickCost` | `decimal` | **CONFIRMED** (nom exact : `TickCost`, pas `TickValue`) |
| Point Value | Aucune propriété directe sur `Security`. `ATAS.Types.SymbolInfo.PointValue` existe mais dans une **autre** classe (`ATAS.Types.dll`, sans lien confirmé avec `Security`/`TradingManager`) | `decimal` (dérivable) | **INFERRED** — `PointValue = TickCost / TickSize` est une formule standard (déjà implicitement vérifiée par les valeurs par défaut actuelles d'IQIA : `12.50 / 0.25 = 50`), non un champ natif |
| Lot Size | `Security.LotSize` | `decimal` | **CONFIRMED** |
| Min/Max Quantity | `Security.LotMinSize` / `Security.LotMaxSize` | `decimal?` | **CONFIRMED** (existence) — correspondance exacte avec la sémantique "quantité d'ordre" min/max **REQUIRES LIVE ATAS VALIDATION** |
| Price precision (Digits) | `Security.Digits` | `int` | **CONFIRMED** |
| Currency | `Security.BaseCurrency` / `.QuoteCurrency` | `string` | **CONFIRMED** |
| Price/Volume Multiplier | `Security.PriceMultiplier` / `.VolumeMultiplier` | `decimal` | **CONFIRMED** (existence) — rôle exact dans le sizing **REQUIRES LIVE ATAS VALIDATION** |
| Contract month / Expiration | `Security.Expiration` | `DateTime` | **CONFIRMED** |
| Marge achat/vente | `Security.MarginBuy` / `.MarginSell` | `decimal` | **CONFIRMED** |
| QuantityStep (pas de quantité) | Aucune propriété `Step`/`LotStep`/`VolumeStep` trouvée sur `Security` | — | **NOT AVAILABLE FROM INDICATOR CONTEXT** (à défaut, dérivable empiriquement de `LotMinSize` si celui-ci représente déjà le pas minimal — **REQUIRES LIVE ATAS VALIDATION**) |
| `Indicator.InstrumentInfo` (déjà utilisé) | `IInstrumentInfo` | `Instrument`(string), `Exchange`(string), `TickSize`(decimal), `TimeZone`(int) | **CONFIRMED** — ne porte PAS TickValue/PointValue/Digits/quantités, ce qui explique pourquoi ces valeurs sont aujourd'hui des paramètres UI manuels dans IQIA |

---

## 5. ES / MES — comment ATAS les différencie

**Aucune table statique ES/MES n'existe dans les assemblies inspectées.** La différenciation est
purement **data-driven** : chaque instrument sélectionné sur le chart correspond à sa propre instance de
`ATAS.DataFeedsCore.Security`, peuplée par le connecteur de données (broker/flux), avec ses propres
`TickSize`/`TickCost`/`LotSize`/`LotMinSize`/`LotMaxSize`/`Digits`. Pour ES et MES, ces valeurs seront
nécessairement différentes dans les objets `Security` respectifs (ES : tick 0.25pt = $12.50 → point
$50 ; MES : tick 0.25pt = $1.25 → point $5, valeurs déjà utilisées comme fixtures aux Lots 10-11, mais
**jamais lues depuis ATAS aujourd'hui** — actuellement saisies manuellement via `TickValue`/`PointValue`).
**Aucun `if symbol == "ES"` n'a été trouvé ni ne serait nécessaire** : lire `TradingManager.Security`
pour l'instrument actif du chart donne directement les bonnes valeurs, quel que soit le symbole. Statut :
**INFERRED avec haute confiance** (architecture data-driven confirmée par la structure des types 
inspectés) mais les valeurs numériques réelles pour ES/MES telles que rapportées par le flux de données
souscrit restent **REQUIRES LIVE ATAS VALIDATION**.

---

## 6. Replay vs Live

| Question (Section D) | Réponse |
|---|---|
| L'Indicator peut-il accéder au compte en Replay ? | `TradingManager`/`Portfolio`/`Position`/`Security` existent indépendamment du mode — **REQUIRES LIVE ATAS VALIDATION** pour savoir s'ils sont peuplés (compte réel, compte simulé/synthétique, ou vide) en Replay |
| L'Equity est-elle disponible en Replay ? | `ITradingStatisticsProvider` expose explicitement `.Replay` **distinct** de `.Realtime` — preuve API que le SDK ATAS traite les deux séparément, mais le contenu réel de `.Replay.Equity` pendant une session Replay est **REQUIRES LIVE ATAS VALIDATION** |
| Le compte utilisé par ATAS est-il accessible depuis l'Indicator ? | Oui structurellement (`TradingManager.Portfolio`), **CONFIRMED** au niveau API ; comportement réel **REQUIRES LIVE ATAS VALIDATION** |
| Valeurs différentes entre Replay et Live ? | Très probable vu la séparation explicite `Replay`/`Realtime` dans `ITradingStatisticsProvider` — **INFERRED**, non prouvé |
| Un Replay fournit-il un compte synthétique ou aucun état ? | **REQUIRES LIVE ATAS VALIDATION** — aucune assembly inspectée ne documente ce comportement de façon statiquement vérifiable |

L'observation empirique fournie en Section 1 du lot (dashboard affichant `Equity $0.00` en Replay sur
MES) **ne renseigne pas** sur la disponibilité réelle de l'Equity ATAS, puisque le code actuel
(Lots 10-12) ne lit jamais `TradingManager`/`TradingStatisticsProvider` — `$0.00` reflète uniquement le
paramètre UI `RiskCurrentEquity` resté à sa valeur par défaut non configurée.

---

## 7. Mapping ATAS → IQIA

| Donnée ATAS | Modèle IQIA actuel | Mapping direct ? | Adaptateur nécessaire ? |
|---|---|---|---|
| `Portfolio.Balance` | `AccountState.InitialCapital` | **Non** — sémantiquement différent (Balance = courant, pas initial ; voir Section 9) | Oui — `Balance` devrait alimenter `CurrentEquity`/`CurrentBalance`, pas `InitialCapital` |
| `Portfolio.Balance` + `.OpenPnL` (ou `TradingStatistics.Equity` dernier point) | `AccountState.CurrentEquity` | Non (calcul/lecture composée) | Oui — petit adaptateur de lecture, aucune formule de risque |
| `Portfolio.Balance` | `AccountState.CurrentBalance` (déjà `decimal?` optionnel) | **Oui** | Mapping direct trivial |
| `Portfolio.MaxEquityValue` | `AccountState.PeakEquity` | Oui, sémantiquement proche | Adaptateur mineur (nom différent) |
| Aucune (absent d'ATAS) | `AccountState.DailyStartingEquity` / `.DailyPnL` | **Non disponible** | Resterait manuel, ou dérivable d'un snapshot quotidien pris par IQIA lui-même (hors périmètre) |
| `Position.UnrealizedPnL`/`.RealizedPnL` (agrégé) | `AccountState.OpenRisk` / `.RiskUsedToday` | **Non** — ce sont des P&L, pas des budgets de risque engagés | Adaptateur nécessaire, logique à définir (hors périmètre discovery) |
| `Security.Instrument` | `InstrumentRiskSpecification.Symbol` | **Oui** | Mapping direct |
| `Security.TickSize` | `InstrumentRiskSpecification.TickSize` | **Oui** | Mapping direct |
| `Security.TickCost` | `InstrumentRiskSpecification.TickValue` | **Oui** (nom différent, valeur équivalente) | Mapping direct |
| `Security.TickCost / Security.TickSize` | `InstrumentRiskSpecification.PointValue` | Non (champ dérivé) | Adaptateur — formule simple, pas une formule de risque |
| `Security.LotMinSize` / `.LotMaxSize` | `InstrumentRiskSpecification.MinQuantity` / `.MaxQuantity` | **Probable** (nullable → nécessite une valeur de repli explicite si absent, jamais 0 inventé) | Adaptateur avec gestion du cas non fourni |
| Aucune (`QuantityStep` non trouvé) | `InstrumentRiskSpecification.QuantityStep` | **Non disponible** | Resterait manuel (paramètre UI Lot 11 déjà existant) |
| `Security.BaseCurrency`/`.QuoteCurrency`, `Portfolio.Currency` | (aucun champ Currency dans `InstrumentRiskSpecification`/`AccountState` du Lot 10) | N/A | `InstrumentRiskSpecification`/`AccountState` n'ont pas de champ Currency aujourd'hui — hors périmètre de ce lot d'ajouter un champ |

---

## 8. Architecture recommandée

```
ATAS (via Indicator.TradingManager)                ATAS (via Indicator.TradingStatisticsProvider)
 │                                                    │
 ├── Portfolio (compte sélectionné)                   ├── Realtime.Equity  (série temporelle)
 │     Balance, BalanceAvailable, OpenPnL,             └── Replay.Equity    (série temporelle, mode replay)
 │     ClosedPnL, TotalPnL, MaxEquityValue,
 │     Currency, AccountID, IsRealAccount
 │
 └── Security (instrument du chart actif)
       Instrument, TickSize, TickCost, LotSize,
       LotMinSize, LotMaxSize, Digits,
       BaseCurrency/QuoteCurrency
              │
              ▼
   [NOUVEAU, hors périmètre Lot 12.1] Adaptateur de lecture seule
   (mapping pur, aucune formule de risque - Section 11 du lot)
              │
              ▼
   AccountState (Lot 10, INCHANGÉ)  +  InstrumentRiskSpecification (Lot 10, INCHANGÉ)
              │
              ▼
          RiskEngine (Lot 10, INCHANGÉ)
              │
              ▼
        RiskAssessment (Lot 10, INCHANGÉ)
              │
              ▼
        TradingDashboard (Lot 12, INCHANGÉ)
```

**Verdict : réalisable avec adaptation.** Directement réalisable pour `Symbol`/`TickSize`/`TickValue`
(mapping 1:1). Réalisable avec adaptation mineure pour `PointValue` (formule dérivée),
`MinQuantity`/`MaxQuantity` (gestion du `null`), `CurrentEquity`/`CurrentBalance` (choix de la source
exacte parmi `Balance`/`OpenPnL`/série `Equity`, à valider en conditions réelles). **Non réalisable** :
`InitialCapital` (n'existe pas dans ATAS, doit rester une configuration utilisateur — Section 9),
`QuantityStep` (non trouvé, resterait manuel), `DailyStartingEquity`/`DailyPnL`/`RiskUsedToday`/`OpenRisk`
(aucune propriété correspondante — nécessiteraient un état de suivi propre à IQIA, hors périmètre ATAS).

---

## 9. InitialCapital vs CurrentEquity

**Confirmé structurellement** : ATAS ne modélise aucun concept d'"InitialCapital" — `Portfolio.Balance`
est une valeur **courante**, mise à jour en continu (comme le confirme sa présence à côté de
`OpenPnL`/`ClosedPnL`/`TotalPnL` dans la même classe mutable, avec `PropertyChanged`). L'architecture
proposée par la Section 9 du lot est donc **correcte et directement supportée par les faits observés** :

```
InitialCapital = configuration utilisateur (RiskPolicy / scénario)   — reste manuel, ATAS n'a pas d'équivalent
CurrentEquity  = ATAS (Portfolio.Balance + OpenPnL, ou dernier point de TradingStatistics.Equity)
RiskBudget     = fonction de CurrentEquity + RiskPolicy              — RiskEngine.cs, INCHANGÉ (Lot 10)
```

et non l'inverse (`InitialCapital = ATAS`, `CurrentEquity = manuel`) — cette dernière architecture serait
incohérente avec ce qu'ATAS expose réellement.

---

## 10. Fail-Closed

Comportement recommandé, cohérent avec le RiskEngine existant (Lot 10, **inchangé**) : si un futur
adaptateur ATAS ne peut pas lire `Portfolio`/`Security` (propriété `null`, `TradingManager` non
disponible, exception), l'adaptateur ne doit **jamais** substituer une valeur plausible — il doit
produire un `AccountState`/`InstrumentRiskSpecification` avec les champs concernés à `0`/valeurs
"non configurées", exactement comme le fait déjà le mécanisme actuel des paramètres UI `Risk*` du Lot 11
(`0` → `RiskEngine` rejette déjà via `INVALID_CAPITAL`/`INVALID_EQUITY`/`INSTRUMENT_SPEC_INVALID`, LOT 10,
inchangé). Aucun fallback financier ne doit être inventé — le mécanisme de rejet structuré déjà
construit est directement suffisant, sans aucune modification du RiskEngine lui-même.

---

## 11. Limitations

- Inspection **statique uniquement** (réflexion + chaînes) — aucun test en conditions ATAS réelles
  (chart ouvert, compte connecté, Replay actif) n'a été exécuté, conformément au périmètre du lot.
- Certaines visibilités d'accesseur (`TradingManager` notamment) n'ont pu être confirmées `public` vs
  `protected` par réflexion hors hôte ATAS — sans conséquence pratique pour une future implémentation
  (IQIAIndicator hérite de `Indicator`, donc un membre `protected` resterait accessible), mais la
  distinction reste non prouvée ici.
- `BaseIndicator`/`ExtendedIndicator` n'ont pu être énumérés que partiellement (dépendance WPF absente
  du contexte d'inspection) — recherche par nom ciblée utilisée en compensation, mais une propriété non
  cherchée explicitement aurait pu être manquée.
- Le contenu réel de `Portfolio`/`Security`/`TradingStatistics.Equity` en Replay n'a pas pu être observé
  (nécessite ATAS live, explicitement hors périmètre de ce lot).
- Aucune information trouvée sur `QuantityStep`/pas de quantité — peut exister sous un nom non recherché,
  ou ne pas exister du tout dans cette version de l'API (7.0.9.461).

---

## 12. Points nécessitant validation ATAS réelle

1. `Indicator.TradingManager` — accessible (public/protected) et non-null en Replay et en Live ?
2. `Portfolio`/`Position`/`Security` sont-ils peuplés (vs `null`) avant qu'un compte/instrument ne soit
   explicitement sélectionné dans ATAS ?
3. `TradingStatisticsProvider.Replay.Equity` contient-il des données exploitables pendant un Replay, ou
   reste-t-il vide ?
4. `Portfolio.BalanceAvailable` vs `.BalancePower` — quelle est la sémantique exacte de chacun (marge
   disponible vs pouvoir d'achat) ?
5. `Security.LotMinSize`/`.LotMaxSize` correspondent-ils réellement à des bornes de **quantité d'ordre**
   (et non une autre notion de lot) ?
6. Existe-t-il un `QuantityStep`/pas de quantité ailleurs (propriété non nommée `Step` explicitement, ou
   sur un type non inspecté) ?
7. `Portfolio.Currency` (enum `Currencies?`) — mapping exact vers un code devise ISO exploitable.
8. Comportement de `PortfolioChanged`/`PositionChanged`/`SecuritySelected` : fréquence réelle, thread
   d'exécution, et si un abonnement dans `OnCalculate`/constructeur est le point d'intégration correct.

---

## LOT 12.1 — COMPLETE

- **APIs Account trouvées** : `ITradingManager.Portfolio` (`ATAS.DataFeedsCore.Portfolio` : Balance,
  BalanceAvailable, BalancePower, OpenPnL, ClosedPnL, TotalPnL, MaxEquityValue, Currency, AccountID,
  IsRealAccount, Leverage, BlockedMargin) + `ITradingManager.Position` (AveragePrice, UnrealizedPnL,
  RealizedPnL, Volume) + `ITradingStatisticsProvider.Realtime/.Replay.Equity` (série temporelle).
- **APIs Instrument trouvées** : `ITradingManager.Security` (`ATAS.DataFeedsCore.Security` : Instrument,
  Exchange, TickSize, TickCost, LotSize, LotMinSize, LotMaxSize, Digits, BaseCurrency, QuoteCurrency,
  PriceMultiplier, VolumeMultiplier) + `Indicator.InstrumentInfo` (déjà utilisé : Instrument, Exchange,
  TickSize, TimeZone).
- **Equity accessible** : YES (dérivé — pas de champ scalaire direct, via `TradingStatistics.Equity`
  dernier point, ou `Balance + OpenPnL`)
- **Balance accessible** : YES (`Portfolio.Balance`, direct)
- **Buying Power accessible** : YES (`Portfolio.BalancePower`/`BalanceAvailable`, sémantique exacte à
  valider)
- **P&L accessible** : YES (`Portfolio.OpenPnL/ClosedPnL/TotalPnL`, `Position.UnrealizedPnL/RealizedPnL`)
- **Instrument automatique** : YES (`Security.Instrument`, direct)
- **Tick Size automatique** : YES (`Security.TickSize`, direct — et déjà utilisé via `InstrumentInfo.TickSize`)
- **Tick Value automatique** : YES (`Security.TickCost`, direct)
- **ES/MES différenciation automatique** : YES (architecture data-driven confirmée, aucun code ES/MES
  hardcodé trouvé ni nécessaire)
- **Replay compatible** : UNKNOWN (API distincte confirmée `Replay`/`Realtime`, contenu réel non
  vérifiable statiquement — REQUIRES LIVE ATAS VALIDATION)
- **Live compatible** : UNKNOWN (mêmes raisons — API existe, comportement runtime non prouvé sans ATAS)
- **Architecture recommandée** : adaptateur de lecture seule ATAS → `AccountState`/
  `InstrumentRiskSpecification` (Lot 10, inchangés), `InitialCapital` reste une configuration
  utilisateur, `CurrentEquity` vient d'ATAS, fail-closed strict (aucune valeur inventée) — voir Sections
  8-10.
- **Limitations** : voir Section 11 — validation live indispensable avant toute implémentation.
- **LOT 12.2 recommandé** : implémenter UNIQUEMENT les bindings démontrés dans ce rapport
  (`Symbol`/`TickSize`/`TickValue`/`Balance`/`OpenPnL`/`Currency` en priorité, tous **CONFIRMED**),
  en conservant `AccountState`/`RiskPolicy`/`InstrumentRiskSpecification`/`RiskEngine` du Lot 10
  strictement inchangés, avec fail-closed strict pour toute donnée non confirmée en conditions réelles.

STOP.
