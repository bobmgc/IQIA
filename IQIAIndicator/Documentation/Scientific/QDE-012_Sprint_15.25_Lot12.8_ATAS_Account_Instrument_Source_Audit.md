# QDE-012 — Sprint 15.25 (Lot 12.8) — Audit des sources ATAS de compte et de spécification instrument

**Lot d'audit technique uniquement.** Aucun fichier `.cs` de production modifié, aucun build de la
solution IQIA, aucune DLL générée, aucun déploiement, aucun commit. Toute l'inspection a été effectuée
par réflexion .NET en lecture seule, hors d'un hôte ATAS réel, dans un projet console éphémère
(`dotnet run`, `System.Runtime.Loader.AssemblyLoadContext` avec résolveur pointant vers
`C:\Program Files (x86)\ATAS Platform`), produit uniquement dans le répertoire scratchpad de cette
session, hors du dépôt IQIA. Aucune DLL ATAS n'a été modifiée.

---

## 1. Objectif

Après les LOTs 12.5 → 12.7, trois causes de rejet sont isolées et confirmées par données réelles
(Replay MES, `ScientificDataset_MES_M5_20260817_225152`) :

1. **Equity Replay** : `TradingStatisticsProvider.Replay.Equity` vide (`SeriesCount=0`) sur 100% des
   barres de toutes les captures réelles disponibles (Lots 12.5/12.6/12.7).
2. **Instrument MES** : `QuantityStep`/`MinQuantity` restent à `0` — aucune source ATAS fiable trouvée
   jusqu'ici.
3. **Stop Loss** : `TradePlan.Status = SIGNAL_ONLY` — hors périmètre, connu depuis le Sprint 15.8.

Ce lot élargit délibérément le périmètre d'inspection au-delà des Lots 12.1/12.4/12.6 (qui se
limitaient à `ATAS.DataFeedsCore.dll`/`ATAS.Types.dll`/`ATAS.Indicators.dll`/`ATAS.Strategies.dll`) pour
couvrir l'ensemble des DLL ATAS/OFT réellement installées, et documenter précisément — sans implémenter
— toute source candidate trouvée.

---

## 2. APIs ATAS inspectées

Version installée confirmée : **7.0.9.461** (identique aux Lots 12.1/12.4/12.6, aucune dérive).
`ATAS.Platform.dll`/`ATAS.Utils.Common.dll` cités dans le brief **n'existent pas sous ce nom exact** dans
cette installation — les DLL réellement présentes et pertinentes sont `OFT.Platform.dll`/
`OFT.Platform.Core.dll` et `Utils.Common.dll` (sans préfixe `ATAS.`). Chargées et inspectées :

| DLL | Types chargés | Statut |
|---|---|---|
| `ATAS.DataFeedsCore.dll` | 469/469 | Complet |
| `ATAS.Types.dll` | 66/66 | Complet |
| `ATAS.Indicators.dll` | 137/153 | Partiel (16 échecs, dépendance `PresentationCore`/WPF absente hors hôte ATAS — identique à la limite déjà documentée Lots 12.1/12.4) |
| `ATAS.Strategies.dll` | 118/119 | Partiel (1 échec) |
| `ATAS.Indicators.Other.dll` | 213/229 | Partiel (16 échecs) |
| `ATAS.Indicators.Technical.dll` | 463/521 | Partiel (58 échecs) |
| `OFT.Platform.dll` | 1026/1547 | Partiel (521 échecs — application WPF principale, majoritairement hors périmètre indicateur) |
| `OFT.Platform.Core.dll` | 2563/2695 | Partiel (132 échecs) |
| `OFT.Models.dll` | 31/31 | Complet |
| `OFT.Core.dll` | 719/719 | Complet |
| `Utils.Common.dll` | 455/455 | Complet |
| `Utils.Windows.dll` | 72/111 | Partiel |
| `OFT.Controls.dll` | 103/136 | Partiel |
| `OFT.Docking.dll` | 262/412 | Partiel |
| `OFT.Attributes.dll` | 43/43 | Complet |
| `OFT.Editors.dll` | 25/67 | Partiel |
| `OFT.SystemStatistics.dll` | 70/70 | Complet |

Recherche par mot-clé (`Equity`,`Balance`,`PnL`,`Profit`,`Loss`,`AccountValue`,`NetLiquidation`,
`BuyingPower`,`AvailableBalance`,`Quantity`,`Volume`,`Step`,`Lot`,`ContractSize`,`Increment`,
`MinQuantity`,`MaxQuantity`,`OrderValidator`,`TradingCommand`) sur **tous les membres publics de tous
les types chargés** (plusieurs milliers de types au total), puis dump complet des types déjà identifiés
comme centraux (`Security`, `Portfolio`, `Position`, `ITradingManager`, `ITradingStatisticsProvider`,
`ITradingStatistics`, `EquityValue`, `ITradingVolumeInfo`, `IVolumeSelectorItem`, `SymbolInfo`), complété
par un second passage ciblé sur `ISecurityTradingOptions`, `RiskInfo`, `TradingStatistics`,
`PortfolioState`, `PositionState` (pistes découvertes en cours d'audit). Aucun fichier `.xml` de
documentation n'est fourni avec l'installation (`Get-ChildItem *.xml` négatif) — aucune documentation
officielle au-delà des signatures elles-mêmes n'est disponible sur cette machine.

---

## 3. Sources Equity trouvées

| Source | Type | Sémantique |
|---|---|---|
| `ITradingStatistics.Equity` (`Realtime`/`Replay`) | `IMutableEnumerable<EquityValue>` | **Seule source explicitement nommée "Equity"** dans tout le SDK — série temporelle, `EquityValue.Equity`/`.TotalEquity` (decimal). Déjà câblée (Lot 12.2/12.6). |
| `Portfolio.Balance` | `decimal` | Solde courant du compte — **aucune propriété `Equity` n'existe sur `Portfolio`** (dump complet, 34 propriétés, confirmé exhaustivement). |
| `Portfolio.OpenPnL` / `.ClosedPnL` / `.TotalClosedPnL` / `.TotalPnL` | `decimal` | P&L latent/réalisé — `TotalPnL` est get-only (calculé en interne, formule non documentée). |
| `Portfolio.MaxEquityValue` | `decimal` | **Piège de nom** — c'est un plus-haut historique (high-water mark), PAS l'équité courante. Risque de faux positif explicite (Section 15). |
| `Position.UnrealizedPnL` / `.RealizedPnL` | `decimal` | Par position, pas un agrégat compte. |
| `PortfolioState.Balance` / `.UnrealizedPnL` | `decimal` | **Type nouveau, non catalogué aux Lots précédents.** Semble un DTO de snapshot/reconciliation (porte un champ `Date`, une liste `Positions : List<PositionState>`). Aucune propriété sur `ITradingManager`/`Portfolio`/`ITradingStatisticsProvider` n'expose `PortfolioState` — **chemin d'accès depuis `Indicator` NON PROUVÉ**, probablement un type interne au moteur de réconciliation ATAS. |
| `ATAS.DataFeedsCore.Statistics.TradingStatistics.Statistics` | `IMutableEnumerable<IStatisticsParameterGroup>` | Collection générique de paramètres statistiques — contenu de `IStatisticsParameterGroup` **non exploré dans ce lot** (hors périmètre temporel), sémantique non déterminée. |

---

## 4. Sources Balance trouvées

`Portfolio.Balance` (`decimal`), `Portfolio.BalanceAvailable` (`decimal?`), `Portfolio.BalancePower`
(`decimal`) — les trois **CONFIRMÉS**, déjà catalogués Lot 12.1, re-confirmés par dump complet. Aucune
propriété `NetLiquidation`/`AccountValue`/`BuyingPower`/`AvailableBalance` (au sens littéral de ces noms)
n'existe où que ce soit dans les 17 DLL inspectées.

## 5. Sources PnL trouvées

`Portfolio.OpenPnL`/`.ClosedPnL`/`.TotalClosedPnL`/`.TotalPnL`/`.ClosedPnlDate`,
`Position.UnrealizedPnL`/`.RealizedPnL`, `HistoryMyTrade.PnL`/`.TicksPnL`/`.PricePnL`,
`TradeStatistics.Matching.PnlData.Pnl`/`.TicksPnl`/`.PricePnl` (calculé par
`HistoryMyTradeExtensions.CalcPnl`, mécanisme de rapprochement de trades — hors périmètre compte
courant). Rien de nouveau par rapport au Lot 12.1 au niveau sémantique.

---

## 6. Comportement Replay

Observé directement sur les 3 captures réelles disponibles (Lots 12.5/12.6/12.7, sessions distinctes,
comptes distincts, fenêtres temporelles distinctes) : `ITradingStatistics.Replay.Equity` **vide sur
100% des barres, dans les trois captures, sans exception**. Dans la capture LOT 12.7 (compte
`AccountID="Replay"`, `IsRealAccount=False`), TOUTES les valeurs financières brutes sont simultanément
nulles : `Balance=0`, `BalanceAvailable=NOT AVAILABLE`, `BalancePower=0`, `OpenPnL=0`, `ClosedPnL=0`,
`TotalPnL=0` — pas seulement `Equity`. C'est un fait nouveau et important pour la Phase C : **si le code
utilisait `Balance + OpenPnL` au lieu de `Replay.Equity`, le résultat aurait été identique (`0 + 0 = 0`)
sur cette capture précise** — reconstruire via Balance/PnL n'aurait rien changé à l'issue observée.

## 7. Comportement Live

**Non déterminable dans ce lot** (interdit — aucun test en conditions ATAS réelles). Aucune des 3
captures disponibles ne couvre un mode Live/Realtime avec compte réellement financé. Reste
**REQUIRES LIVE ATAS VALIDATION**, inchangé depuis le Lot 12.1.

---

## 8. Relation Balance/PnL/Equity

Recherche explicite d'une documentation SDK confirmant `Equity = Balance + OpenPnL` : **aucune trouvée**
(pas de fichier `.xml` de documentation livré avec l'installation, pas de commentaire XML dans les
métadonnées IL, aucune propriété calculée observable qui implémenterait cette formule sur `Portfolio`
lui-même — `TotalPnL` est get-only mais sa formule interne n'est pas inspectable par réflexion de
signature seule). Réponses point par point (Section C du brief) :

- **Documentée par ATAS ?** NON — aucune preuve trouvée.
- **Observée en Replay ?** Impossible à confirmer : dans la seule capture Replay disponible où on
  pourrait la tester, `Balance` et `OpenPnL` valent tous deux `0` en permanence — la formule est
  numériquement compatible (`0=0`) mais ceci ne la **prouve pas**, ça ne fait que ne pas la contredire.
- **Cohérente avec les valeurs exposées ?** Ni confirmée ni infirmée — données insuffisantes.
- **Valable avec positions ouvertes ?** Non testé (aucune position ouverte dans les captures
  disponibles).
- **Valable sans transaction ?** C'est le seul cas observé (compte `"Replay"` sans transaction) — la
  formule y est vérifiée à `0=0`, un cas dégénéré qui ne discrimine rien.
- **Valable en Replay ?** Non démontré — voir ci-dessus.

**Conclusion de cette section : `EQUITY SOURCE NOT AVAILABLE`** pour la relation `Balance + OpenPnL`
spécifiquement (aucune preuve, ni pour ni contre, au-delà d'un cas dégénéré `0=0` qui ne tranche rien).

---

## 9. Sources QuantityStep

Recherche exhaustive, élargie au-delà des Lots 12.1/12.4/12.6, sur les 17 DLL :

| Piste | Type hôte | Verdict |
|---|---|---|
| `Security.LotSize` | `ATAS.DataFeedsCore.Security` | **Rejetée (Lot 12.6, re-confirmée ce lot)** — `decimal` non-nullable, défaut SDK `1`, indiscernable d'une vraie donnée MES=1 (Section 13). |
| `ITradingManager.TradingVolumeInfo.VolumeItems[]` | `ATAS.Indicators.ITradingVolumeInfo` | Liste de valeurs discrètes (`IVolumeSelectorItem.Volume : decimal?`), pas un pas scalaire — en déduire un "step" serait une inférence, pas une lecture directe. Rejetée (cohérent Lot 12.4). |
| `ITradingManager.GetSecurityTradingOptions()` → `ISecurityTradingOptions` | `ATAS.DataFeedsCore.ISecurityTradingOptions` | **Piste nouvelle inspectée ce lot** — dump complet : `TimeInForce`, `TriggerPriceTypes`, 4 méthodes `Create*OrderFlagsObject()`. **Aucune métadonnée de quantité.** Rejetée, définitivement. |
| `IDataFeedConnector.CalcMaxOrderVolume(...)` / `CurrencyExtensions.GetMinVolume(ITradingCore, ...)` | `IDataFeedConnector`/`ITradingCore`/`PlatformTradingCore` | Méthodes réelles calculant des bornes de volume d'ordre à partir de `Security`+`Portfolio` — **mais hébergées sur des types que `ITradingManager` n'expose jamais** (voir Section 12). Inaccessibles depuis `Indicator`. |
| `OFT.Core.Models.Contract.LotSize`/`.MinLot`, `OFT.Core.Sbe.*` (messages `ContractsDefinitionsMessage`, `SecurityMetaDataMessage`, etc.) | `OFT.Core.dll` (couche protocole/stockage bas niveau) | Type `Contract`, distinct de `Security`, avec `LotSize`/`MinLot` **et** des bornes `*MinValue`/`*MaxValue`/`*NullValue` — mais ce sont des **bornes d'encodage du protocole SBE** (validité du champ binaire), pas des bornes de trading. `Contract` n'apparaît nulle part dans le graphe accessible depuis `ITradingManager`/`Portfolio`/`Position`/`Security`. Inaccessible. |
| `OFT.Platform.Core.ViewModels.Strategies.ATM.MultipleStopProfitLevelEditorViewModel.QuantityStep`/`.MinimumQuantity` | `OFT.Platform.Core.dll` (ViewModel WPF) | Le **concept** "QuantityStep"/"MinimumQuantity" existe bien dans le code d'ATAS lui-même (éditeur de stratégie Multi-Target Stop-Profit intégré), preuve qu'ATAS gère cette notion en interne — mais c'est un ViewModel d'UI, hors surface SDK publique, hors de portée de `Indicator`. |

## 10. Sources MinQuantity

Identique à `QuantityStep` — `Security.LotMinSize` (`decimal?`, défaut `null`, sans ambiguïté) reste la
seule source ATAS confirmée, déjà câblée (Lot 12.2). Aucune alternative trouvée.

## 11. Sources MaxQuantity

Identique — `Security.LotMaxSize` (`decimal?`, défaut `null`). Aucune alternative trouvée.

## 12. Sources Order Quantity

`ATAS.DataFeedsCore.Order` (dump complet, 30 propriétés) : `QuantityToFill` (`decimal`), `QuoteVolume`
(`decimal?`) — ce sont des champs **d'un ordre déjà construit** (quantité restant à exécuter), pas des
bornes de validation pour construire un ordre. Aucun type `OrderValidator`/`TradingCommand` trouvé nulle
part dans les 17 DLL (recherche par nom exact et par sous-chaîne, aucun résultat dans un namespace
`ATAS.*`). `ITradingManager` (interface complète, 14 membres, dump exhaustif Section 2 ci-dessous)
n'expose **aucune** méthode `ValidateOrder`/`CalcMaxOrderVolume`/équivalent — seulement
`OpenOrder`/`ModifyOrder`/`CancelOrder`/`ClosePosition`/`SetStopLoss`/`SetTakeProfit`/`SetBreakeven` (des
actions d'exécution, jamais interdites d'usage ici puisque non appelées) et
`GetSecurityTradingOptions()` (Section 9, rejetée). Les seules méthodes ATAS calculant réellement une
borne de volume d'ordre (`CalcMaxOrderVolume`, `GetMinVolume`) existent bien dans le SDK mais sur des
types hors d'atteinte de l'indicateur (Section 9).

---

## 13. Vérification LotSize

Re-testé **indépendamment** ce lot (nouvelle construction directe, distincte de celle du Lot 12.6) :

```
Type: ATAS.DataFeedsCore.Security — 1 constructeur public, sans paramètre.
NEW Security().LotSize      = 1
NEW Security().LotMinSize   = null
NEW Security().LotMaxSize   = null
NEW Security().TickSize     = 0
NEW Security().TickCost     = 0
NEW Security().Digits       = 0
```

**CONFIRMÉ, résultat identique au Lot 12.6** : `LotSize = 1` est la valeur par défaut du constructeur
SDK, présente même sur un objet jamais peuplé par aucun connecteur — rigoureusement indiscernable d'une
vraie donnée MES rapportant authentiquement `1`. `LotMinSize`/`LotMaxSize` restent `null` par défaut,
sans ambiguïté (déjà exploité, Lot 12.2). Aucun changement de conclusion depuis le Lot 12.6.

## 14. Vérification MES/ES

Grep exhaustif sur `IQIAIndicator/**/*.cs` (hors `Tests/`) pour toute occurrence littérale `"MES"`/`"ES"`
: **0 résultat en dehors des fichiers de test** (12 fichiers `Tests/*.cs` utilisent ces littéraux comme
données de test, ce qui est attendu et sans rapport avec une logique de branchement en production).
Aucun `if symbol == "ES"`/`"MES"` ni table de correspondance codée en dur dans le code de production
actuel. Confirme et actualise (à la date de ce lot) la conclusion du Lot 12.1 : différenciation
strictement data-driven via l'instance `Security` propre à chaque graphique, jamais un branchement sur
le symbole.

---

## 15. Risques de faux positifs

Explicitement identifiés pour éviter qu'un futur lot les exploite par erreur :

1. **`Portfolio.MaxEquityValue`** — nom trompeur, c'est un plus-haut historique, pas l'équité courante ;
   l'utiliser comme `CurrentEquity` fausserait silencieusement tout calcul de risque.
2. **`Security.LotSize = 1`** — piège déjà documenté (Lot 12.6) : indiscernable d'une vraie donnée MES.
   Toujours valable, re-confirmé ce lot.
3. **`OFT.Platform.Core.*.QuantityStep`/`.MinimumQuantity`** (ViewModels) — le nom coïncide exactement
   avec le besoin recherché, mais ce sont des propriétés d'UI de dialogue de stratégie, sans lien avec
   `ITradingManager`/`Security`. Un grep par nom seul, sans vérifier le namespace ni l'atteignabilité
   depuis `Indicator`, conclurait à tort qu'une source existe.
4. **Champs `*MinValue`/`*MaxValue`/`*NullValue` des messages SBE** (`OFT.Core.Sbe.Messages.*`) — bornes
   d'encodage du protocole binaire (validité du champ transmis sur le fil), pas des bornes de trading
   `MinQuantity`/`MaxQuantity`. Un nom `LotSizeMinValue`/`LotSizeMaxValue` pourrait être mal interprété.
5. **`RiskInfo.LeverageIncrement`** — porte le mot "Increment" mais concerne exclusivement le pas de
   sélection du levier (marge crypto), sans rapport avec une quantité d'ordre.
6. **`PortfolioState`/`PositionState`** — types réels, mais chemin d'accès depuis `Indicator` **non
   prouvé** ; les traiter comme immédiatement utilisables sans avoir d'abord confirmé leur atteignabilité
   serait prématuré.

---

## 16. Conclusion Equity

## **EQUITY-D**

Aucune source Equity Replay fiable n'est disponible pour le compte/la session réellement testés
(pseudo-compte ATAS `"Replay"`, trois captures indépendantes concordantes — Lots 12.5/12.6/12.7). La
seule source correctement nommée et sémantiquement documentée par le SDK lui-même
(`ITradingStatistics.Replay.Equity`) est déjà câblée (Lot 12.2/12.6) et reste structurellement vide.
Aucune alternative n'a été trouvée malgré une recherche élargie à 17 DLL : `Portfolio` n'expose aucune
propriété `Equity`, la relation `Balance + OpenPnL` n'est ni documentée ni démontrable (Section 8), et
les nouveaux types découverts (`PortfolioState`) ont un chemin d'accès non prouvé depuis `Indicator`.
**Nuance nécessaire** : cette classification concerne le compte pseudo-`"Replay"` testé — un compte
Démo/Simulateur réellement financé n'a jamais été testé et pourrait, en théorie, peupler
`Realtime.Equity` différemment (Section 7, hors périmètre offline de ce lot).

## 17. Conclusion Quantity

## **QUANTITY-D**

Aucune source ATAS fiable n'est disponible pour `QuantityStep` dans cette version (7.0.9.461),
confirmé par un second audit indépendant élargi à 17 DLL (contre 4 aux Lots 12.1/12.4/12.6). Toutes les
pistes nouvellement inspectées ce lot (`ISecurityTradingOptions`, `CalcMaxOrderVolume`/`GetMinVolume`,
`OFT.Core.Models.Contract`, ViewModels `OFT.Platform.Core.*`) sont soit sémantiquement vides, soit
structurellement inaccessibles depuis `ITradingManager` (interface complète et close, 14 membres,
dumpée intégralement Section 2/12). `MinQuantity`/`MaxQuantity` restent en **QUANTITY-A** de facto via
`Security.LotMinSize`/`LotMaxSize` (déjà câblés, Lot 12.2) — c'est uniquement `QuantityStep` qui reste en
`QUANTITY-D`.

---

## 18. Prochaine action recommandée

**Aucune implémentation n'est justifiée par cet audit** (conforme à la RÈGLE ABSOLUE — Phase J). Pistes
concrètes pour un lot séparé, aucune décision prise ici :

1. **Sans aucun code** : tester un Replay (ou une session Live) sur un compte Démo/Simulateur
   réellement financé (pas le pseudo-compte `"Replay"`) et observer si `Realtime.Equity`/`Replay.Equity`
   se peuplent — testerait directement l'hypothèse EQUITY-D vs une limitation propre à ce type de compte
   spécifique (Section 16).
2. **Sans aucun code** : configurer manuellement `RiskInstrumentQuantityStep`/`MinQuantity`/`MaxQuantity`
   dans le panneau ATAS avant un nouveau Replay — validation directe déjà recommandée depuis le Lot 12.3,
   toujours la seule voie fiable pour `QuantityStep` (QUANTITY-D confirmé).
3. Investiguer, en conditions ATAS réelles uniquement, si `ATAS.DataFeedsCore.Statistics
   .IStatisticsParameterGroup` (contenu de `ITradingStatistics.Statistics`, non exploré ce lot) porte une
   information Equity/Balance exploitable — **REQUIRES LIVE ATAS VALIDATION**, piste non fermée.
4. Le Stop Loss scientifique reste hors périmètre — traité dans un lot séparé, comme demandé.

---

## Annexe — dumps complets des types clés (preuve brute)

```
ATAS.DataFeedsCore.Security — 34 propriétés (exhaustif) :
BaseCurrency, BestAskPrice, BestAskVolume, BestBidPrice, BestBidVolume, Code, ConnectorId, Digits,
EntityType, Exchange, ExchangeInstance, Expiration, FundingRate, Id, Instrument, IsinId,
IsInverseFutures, LastTradePrice, LastTradeVolume, LotMaxSize, LotMinSize, LotSize, MarginBuy,
MarginSell, MarkPrice, MaxPrice, MinPrice, MoneyPnLFormat, NextFundingTime, OpenInterest, OptionType,
Parent, PriceMultiplier, QuoteCurrency, QuoteCurrencyPrecision, SecurityId, StrikePrice, TickCost,
TickSize, Type, UnderlyingSecurity, VolumeMultiplier
→ Aucun QuantityStep/VolumeStep/LotStep/ContractSize/MinQuantity/MaxQuantity.

ATAS.DataFeedsCore.Portfolio — 34 propriétés (exhaustif) :
AccountID, Accounts, ActiveOrders, AtasId, Balance, BalanceAvailable, BalancePower, BlockedMargin,
ClosedPnL, ClosedPnlDate, Commission, CommissionRulesGroup, CommissionState, ConnectionState, Currency,
Data, DepoName, EntityType, FcmId, IbId, IsAdviserPortfolio, IsLocked, IsRealAccount, IsSuspended,
Leverage, MaxEquityValue, OpenPnL, ProcessedTradeTime, StatisticsUrl, TotalClosedPnL, TotalPnL,
TPlusLimit, TradingOptions, TradingOptionsId, User, UserId, Viewers
→ Aucun champ "Equity".

ATAS.Indicators.ITradingManager — 9 propriétés + 12 méthodes (exhaustif, interface close) :
IsStopLossModeActivated, IsTakeProfitModeActivated, MyTrades, Orders, Portfolio, Position, Security,
TPlusLimit, TradingVolumeInfo ; CancelOrder(Async), ClosePosition(Async), GetSecurityTradingOptions(),
IsStopLossOrder, IsTakeProfitOrder, ModifyOrder(Async), OpenOrder(Async), SetBreakeven, SetStopLoss,
SetTakeProfit
→ Aucune référence à un Connector/ITradingCore/IDataFeedConnector.
```

Dumps complets additionnels (Portfolio, Position, ITradingStatisticsProvider, ITradingStatistics,
EquityValue, ITradingVolumeInfo, IVolumeSelectorItem, SymbolInfo, ISecurityTradingOptions, RiskInfo,
TradingStatistics, PortfolioState, PositionState, Order, OrderExtendedOptions, Extensions) produits par
le script d'audit — conservés uniquement dans le répertoire scratchpad de cette session (hors dépôt),
disponibles sur demande, non requis ici au-delà des extraits ci-dessus qui portent la preuve des
conclusions.

---

# LOT 12.8 RESULT

Status : AUDIT COMPLETE — AUCUNE MODIFICATION EFFECTUÉE

## EQUITY
Classification : EQUITY-D
Source : ITradingStatistics.Realtime/.Replay.Equity (IMutableEnumerable<EquityValue>) — seule source nommée "Equity" du SDK, déjà câblée (Lot 12.2/12.6)
Replay disponible : NON — vide (SeriesCount=0) sur 100% des barres, 3 captures réelles indépendantes concordantes (Lots 12.5/12.6/12.7)
Live disponible : REQUIRES LIVE ATAS VALIDATION (non testé, hors périmètre offline)
Valeur dynamique : NON pour le compte pseudo-"Replay" testé (Balance/OpenPnL/ClosedPnL/TotalPnL également tous à 0 en permanence dans la capture Lot 12.7)
Conclusion : Aucune source Equity Replay alternative trouvée malgré recherche élargie à 17 DLL. Balance+OpenPnL non documenté, non démontrable (Section 8), et de toute façon également nul dans la seule capture testable.

## QUANTITY
Classification : QUANTITY-D (QuantityStep) / QUANTITY-A de facto (MinQuantity/MaxQuantity via Security.LotMinSize/LotMaxSize, déjà câblés Lot 12.2)
Source : NONE pour QuantityStep — Security.LotMinSize/LotMaxSize pour Min/MaxQuantity
Replay disponible : LotMinSize/LotMaxSize = NOT AVAILABLE sur ce flux (constant, 3 captures)
Live disponible : REQUIRES LIVE ATAS VALIDATION
Équivalence QuantityStep : NON DÉMONTRÉE — Security.LotSize rejeté (défaut SDK=1, indiscernable, re-confirmé indépendamment ce lot), ISecurityTradingOptions/CalcMaxOrderVolume/GetMinVolume/Contract(OFT.Core)/ViewModels(OFT.Platform.Core) tous inspectés ce lot et rejetés (vides sémantiquement ou inaccessibles depuis ITradingManager)
Conclusion : Aucune source ATAS fiable pour QuantityStep dans cette version (7.0.9.461), confirmé par audit élargi (17 DLL contre 4 aux lots précédents). Le paramètre manuel reste nécessaire.

## BALANCE / PNL
Balance disponible : YES (Portfolio.Balance, decimal, dynamique) — mais = 0 en permanence dans la capture Replay testée
OpenPnL disponible : YES (Portfolio.OpenPnL) — idem, = 0 en permanence
ClosedPnL disponible : YES (Portfolio.ClosedPnL/.TotalClosedPnL) — idem, = 0 en permanence
TotalPnL disponible : YES (Portfolio.TotalPnL, get-only, formule interne non documentée)
Relation Equity démontrée : NO

## MES
Métadonnées instrument : Security.Instrument="Micro E-mini S&P 500", TickSize=0.25, TickCost=1.25 (tous confirmés réels, ATAS Raw, 3 captures)
QuantityStep : 0 (paramètre manuel non configuré — aucune source ATAS automatique, QUANTITY-D)
MinQuantity : 0 (Security.LotMinSize=NOT AVAILABLE sur ce flux → fallback manuel)
MaxQuantity : 0 (Security.LotMaxSize=NOT AVAILABLE sur ce flux → fallback manuel)
LotSize : 1 (Security.LotSize, re-testé indépendamment ce lot par construction directe — IDENTIQUE au défaut SDK non peuplé, indiscernable, PROUVÉ)
Hardcoded mapping : NONE (grep exhaustif Engine/Infrastructure/IQIAIndicator.cs — 0 occurrence hors Tests/)

## RECOMMANDATION

Aucune implémentation automatique n'a été effectuée (Phase J). Deux pistes actionnables sans aucun code
(Section 18) : (1) tester Equity sur un compte Démo/Simulateur réellement financé plutôt que le
pseudo-compte "Replay" ; (2) configurer manuellement RiskInstrumentQuantityStep/MinQuantity/MaxQuantity
dans le panneau ATAS avant le prochain Replay. Une piste non fermée nécessitant validation ATAS réelle :
ITradingStatistics.Statistics (IStatisticsParameterGroup), non explorée ce lot. Le Stop Loss reste hors
périmètre, inchangé.

Files modified : NONE

Build : NOT RUN

Tests : NOT RUN

DLL deployed : NO

Commit : NO

STOP — FIN DU LOT 12.8.
