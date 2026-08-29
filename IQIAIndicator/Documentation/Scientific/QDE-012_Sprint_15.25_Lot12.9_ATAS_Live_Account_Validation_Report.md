# QDE-012 — Sprint 15.25 (Lot 12.9) — Validation ATAS compte réel/connecté

**Lot de validation technique uniquement.** Aucun fichier `.cs` de production modifié, aucun build,
aucune DLL déployée, aucun commit. **Aucun ordre envoyé** (voir Section 16 — vérifié à deux niveaux :
aucune API de trading n'a été invoquée par cette analyse, qui s'est limitée à la lecture d'un fichier
JSON déjà exporté par l'utilisateur).

---

## 1. Environnement ATAS

Session capturée par l'utilisateur le 2026-08-18, entre 13:00:30Z et 13:14:36Z (~14 minutes),
`SessionId c742b094-0802-44c2-8a52-699d1007cd20`, dataset
`ScientificDataset_MES_M5_20260818_150030` (1279 barres écrites sur 1280 reçues).

**Constat central du lot, déterminant pour toutes les sections suivantes** : malgré la connexion d'un
compte réel/démo par l'utilisateur (Section 0 du protocole convenu avec l'utilisateur), la télémétrie
`ATAS.Raw.*` montre que le contexte de compte réellement exposé à l'indicateur pendant cette session est
**identique en tout point** à celui des trois captures Replay précédentes (LOTs 12.5/12.6/12.7/12.8) :
`AccountID = "Replay"`, `IsRealAccount = False`. Les barres traitées portent des dates historiques
(`2026-07-01T06:05:00` → `2026-07-08T01:35:00`, `CurrentBar` 925→2203) et non des barres proches de la
date réelle de la session (2026-08-18) — la signature exacte d'un **Market Replay ATAS**, pas d'un flux
live/temps réel.

**Interprétation confirmée avec l'utilisateur** : le chart était en mode Replay pendant cette session.
Ce lot documente donc, empiriquement pour la première fois, ce qui se produit quand un compte réel/démo
est connecté à la plateforme ATAS **pendant qu'un Replay de chart est actif** — un scénario que les LOTs
précédents n'avaient jamais eu l'occasion de tester (Section 7).

## 2. Type de compte

`AccountID = "Replay"` (chaîne constante interne d'ATAS, non un identifiant personnel — **aucun
masquage nécessaire**, ce n'est pas une donnée sensible). `IsRealAccount = False`,
`Currency = "USD"`. Aucune propriété permettant de distinguer Sim/Demo/Prop au-delà de
`IsRealAccount` n'est exposée par le SDK (confirmé exhaustivement Lot 12.8, Section 3 — `Portfolio` n'a
que `IsRealAccount`, pas de champ `AccountKind`/`Environment`).

## 3. Fournisseur de données

**NOT AVAILABLE** — le schéma de télémétrie actuel (`ATAS.Raw.Account.*`/`ATAS.Raw.Instrument.*`) ne
capture aucun champ identifiant le connecteur/fournisseur de flux (pas de `ConnectorId`/`DataFeedName`
dans les clés exportées, bien que `Security.ConnectorId` existe dans le SDK — Lot 12.8, Section 2 —
il n'est simplement pas exporté par `ScientificDatasetRecord.From` aujourd'hui). Non déterminable sans
modifier le code d'export, hors périmètre de ce lot.

## 4. Instrument

`Micro E-mini S&P 500` (MES), confirmé identique aux captures précédentes — `TickSize=0.25`,
`TickCost=1.25`, `Digits=2` (1279/1279 barres, aucune variation).

---

## 5. Données Portfolio

| Champ | Valeur observée | Disponible ? | Zéro réel ou absence ? | Dynamique pendant la session ? |
|---|---|---|---|---|
| `Balance` | `0` | Disponible (champ peuplé, pas "NOT AVAILABLE") | **Zéro réel** — le champ répond, sa valeur est authentiquement 0 pour ce contexte de compte | Non — constant sur 1279/1279 barres |
| `BalanceAvailable` | `NOT AVAILABLE` | **Absent** (`Portfolio.BalanceAvailable` est `decimal?`, resté `null`) | Absence de donnée, pas un zéro | Non |
| `BalancePower` | `0` | Disponible | Zéro réel | Non |
| `OpenPnL` | `0` | Disponible | Zéro réel | Non |
| `ClosedPnL` | `0` | Disponible | Zéro réel | Non |
| `TotalPnL` | `0` | Disponible | Zéro réel | Non |
| `Currency` | `USD` | Disponible | — | Non |
| `IsRealAccount` | `False` | Disponible | — | Non |

**Un seul état de compte sur toute la session** (1 snapshot distinct détecté sur 1279 barres, script
d'analyse dédié) : aucune des 8 valeurs ci-dessus n'a changé une seule fois entre la première et la
dernière barre. Distinction "0 réel" vs "absence" appliquée explicitement (brief, Phase B) :
`BalanceAvailable` est une authentique absence (`null` côté SDK, exporté `"NOT AVAILABLE"`) ; les 7
autres champs sont des zéros réels (le champ répond, sa valeur mesurée est 0), cohérent avec un compte
pseudo-`"Replay"` jamais financé.

## 6. Données Position

Aucune position ouverte pendant cette session (`ATAS.RiskInput.Present=False` sur 1005/1279 barres —
aucun candidat directionnel ; sur les 274 barres restantes, la requête atteint `RiskEngine` mais est
rejetée avant tout sizing). Le schéma de télémétrie actuel ne capture pas séparément
`Position.Volume`/`.UnrealizedPnL`/`.RealizedPnL` en dehors de leur agrégation dans `Portfolio.OpenPnL`
(champ non exporté distinctement — même limite que Section 3). **NOT AVAILABLE** dans cette capture.

---

## 7. Données Equity

### Test des 4 cas demandés (Phase C)

| Cas | Source | Valeur observée | Utilisable comme CurrentEquity ? |
|---|---|---|---|
| 1 | `Portfolio.Balance` | `0`, constant | Non — zéro réel, ne reflète aucun capital ; l'utiliser produirait `INVALID_EQUITY` de toute façon |
| 2 | `Balance + OpenPnL` | `0 + 0 = 0` | Non — identique au cas 1, ne change rien à l'issue ; relation toujours non démontrée (Lot 12.8, Section 8) |
| 3 | `Portfolio.TotalPnL` | `0` | Non — c'est un P&L, pas un capital ; sémantiquement inadapté même s'il avait été non nul |
| 4 | `TradingStatisticsProvider.Realtime.Equity` | `105.50` constant, `SeriesCount=0` sur la série **Replay** interrogée | Non — cette valeur `105.50` est précisément la valeur obsolète (mode Realtime) que le correctif du LOT 12.6 empêche désormais d'être utilisée pendant un Replay (`SourceMode` reste `"Replay"` sur 1279/1279 barres, jamais de fuite vers cette valeur) |

**Aucun des 4 cas ne fournit une Equity exploitable** dans ce contexte de compte.

## 8. Comparaison Replay / Live

| Donnée | Replay (LOT 12.7, dataset `225152`) | "Live tenté" (LOT 12.9, dataset `150030`) |
|---|---|---|
| Balance | `0` | `0` (identique) |
| BalanceAvailable | `NOT AVAILABLE` | `NOT AVAILABLE` (identique) |
| BalancePower | `0` | `0` (identique) |
| OpenPnL | `0` | `0` (identique) |
| ClosedPnL | `0` | `0` (identique) |
| TotalPnL | `0` | `0` (identique) |
| Realtime Equity | `105.50` (SourceMode jamais sélectionné) | `105.50` (identique, jamais sélectionné) |
| Replay Equity | `NOT AVAILABLE` (SeriesCount=0) | `NOT AVAILABLE` (SeriesCount=0, identique) |
| `AccountID` | `"Replay"` | `"Replay"` (identique — **même en tentant une connexion réelle**) |
| `PortfolioIsReplay` (`Portfolio.IsReplay()`) | `True` | `True` — **première confirmation empirique en conditions réelles** que `Portfolio.IsReplay()` concorde à 100% avec `SourceMode` (1279/1279), point resté `REQUIRES LIVE ATAS VALIDATION` depuis le LOT 12.6 |

### ATAS permet-il d'obtenir une Equity exploitable en LIVE ?

## **INCONCLUSIVE**

Justification stricte : ce lot ne teste PAS un compte Live au sens d'un chart en temps réel — il teste
un compte connecté **pendant qu'un chart est en Replay**, et montre que dans ce cas précis, ATAS
substitue systématiquement le pseudo-compte `"Replay"` au compte réel/démo connecté, quel que soit ce
dernier. **Ceci est un résultat nouveau et actionnable en soi** (Section 9) mais ne répond pas à la
question "un compte Live sur un chart réellement temps réel expose-t-il une Equity exploitable ?" — ce
scénario précis (chart NON-Replay, compte connecté) reste non testé. NO serait trop fort (rien ne prouve
qu'un chart réellement live échouerait de la même façon) ; YES serait injustifié (aucune preuve
positive). INCONCLUSIVE est la seule réponse honnête sur les données disponibles.

---

## 9. Constat nouveau : le contexte Replay du chart prime sur la connexion de compte

Résultat le plus important de ce lot, non anticipé par les LOTs 12.1–12.8 : **`Indicator.TradingManager
.Portfolio` reflète le contexte du chart (Replay ou non), pas le compte que l'utilisateur a connecté par
ailleurs dans la plateforme.** Concrètement, connecter un compte réel/démo n'a aucun effet observable sur
`ATAS.Raw.Account.*` tant que le chart affiché reste en mode Replay — ATAS y substitue son pseudo-compte
interne `"Replay"`, exactement comme documenté par le SDK (`Extensions.IsReplay(Portfolio)` : vrai si et
seulement si `AccountID == "Replay"`, PROUVÉ par réflexion au LOT 12.6, **maintenant confirmé en
conditions réelles** par ce lot : concordance 1279/1279 entre `PortfolioIsReplay` et `SourceMode`).

**Conséquence directe pour la Section 8** : pour valider réellement si un compte connecté expose une
Equity/Balance exploitable, il faut un chart qui n'est **pas** en Replay — un vrai flux live/temps réel.
C'est la seule configuration non encore testée par l'ensemble des LOTs 12.1–12.9.

---

## 10. Données instrument MES

| Champ | ATAS | RiskSpecification | Source |
|---|---:|---:|---|
| Symbol | `Micro E-mini S&P 500` | `Micro E-mini S&P 500` | `Security.Instrument`, direct |
| TickSize | `0.25` | `0.25` | `Security.TickSize`, direct |
| TickCost | `1.25` | `1.25` (→ TickValue) | `Security.TickCost`, direct |
| LotSize | `1` | — (non utilisé, délibéré) | `Security.LotSize` — rejeté comme source QuantityStep (Lot 12.6/12.8, défaut SDK indiscernable) |
| LotMinSize | `NOT AVAILABLE` | — | `Security.LotMinSize` non peuplé par ce flux |
| LotMaxSize | `NOT AVAILABLE` | — | `Security.LotMaxSize` non peuplé par ce flux |
| QuantityStep | — (aucune source ATAS) | `0` | Paramètre manuel `RiskInstrumentQuantityStep`, resté non configuré dans cette session |
| MinQuantity | — (LotMinSize absent) | `0` | Fallback manuel `RiskInstrumentMinQuantity`, resté non configuré |
| MaxQuantity | — (LotMaxSize absent) | `0` | Fallback manuel `RiskInstrumentMaxQuantity`, resté non configuré |

Identique en tout point aux 3 captures précédentes — aucune régression, aucune amélioration : ce compte
étant le pseudo-compte `"Replay"`, il n'apporte aucune donnée nouvelle sur `LotMinSize`/`LotMaxSize`
(propriétés de `Security`, indépendantes du compte, mais ce flux/courtier ne les peuple toujours pas).

## 11. QuantityStep

**Non exposé par ATAS dans cette session**, confirmé une 4e fois (Lots 12.5/12.6/12.7/12.9). Conforme à
la contrainte absolue du brief : **`LotSize` n'a PAS été substitué à `QuantityStep`** — resté à `0`,
valeur authentique du paramètre manuel non configuré, jamais une invention.

## 12. MinQuantity

`Security.LotMinSize = NOT AVAILABLE` sur ce flux → fallback manuel `RiskInstrumentMinQuantity = 0`
(non configuré dans cette session). Mécanisme inchangé (Lot 12.2, protégé).

---

## 13. Paramètres manuels nécessaires

Aucune valeur n'a été modifiée automatiquement — documentation uniquement, conforme à la Phase F :

| Si l'utilisateur renseigne | Alors le Risk Engine reçoit |
|---|---|
| `RiskInstrumentQuantityStep` (panneau ATAS, groupe "Risk Engine") à une valeur entière `> 0` | `InstrumentRiskSpecification.QuantityStep` = cette valeur exacte (passthrough direct, `ATASInstrumentAdapter.Build`) |
| `RiskInstrumentMinQuantity` à une valeur entière `> 0` | `InstrumentRiskSpecification.MinQuantity` = cette valeur, **uniquement si** `Security.LotMinSize` reste indisponible sur ce flux (fallback conditionnel, sinon `LotMinSize` prime) |
| `RiskInstrumentMaxQuantity` à une valeur entière `>= MinQuantity` | `InstrumentRiskSpecification.MaxQuantity` = cette valeur, même logique de fallback conditionnel |
| `RiskInitialCapital` | `AccountState.InitialCapital` — **déjà configuré** dans cette session (`25000` sur 1257/1279 barres ; les 22 premières barres montrent `0.0`, artefact de warm-up déjà documenté au LOT 12.7, sans impact car aucune n'atteint `RiskEngine`) |

Dans cette session précise, **aucun de ces 3 premiers paramètres n'a été configuré** par l'utilisateur
(tous restés à `0`) — ce lot n'a donc pas pu tester leur effet (Section 14).

## 14. État InstrumentRiskSpecification

`IsValid = False` sur 1279/1279 barres. Cause exacte identique au LOT 12.7 (`InstrumentRiskSpecification
.cs:40-48`) : `MinQuantity > 0` faux (`=0`) et `QuantityStep > 0` faux (`=0`) ; `Symbol`/`TickSize`/
`TickValue`/`PointValue` tous valides. **Non testé dans cette session** : configurer
`RiskInstrumentQuantityStep`/`MinQuantity`/`MaxQuantity` à des valeurs positives (Section 13) aurait,
d'après la lecture du code (inchangé, non modifié par ce lot), rendu `IsValid = True` pour ces deux
conditions — mais ceci n'a pas été vérifié empiriquement ce lot, faute d'une session où ces paramètres
étaient effectivement configurés. **Phase G non complétée** — reste l'action recommandée du prochain
lot (Section 19).

## 15. État RiskAssessment

`Risk.Status = REJECTED` sur 274/1279 barres (barres avec un candidat directionnel — 137 Buy / 137 Sell,
échantillon équilibré, la plus riche capture directionnelle obtenue à ce jour). `Risk.RejectionReasons =
INVALID_EQUITY;INSTRUMENT_SPEC_INVALID;INVALID_STOP_LOSS` sur 274/274, sans exception — les trois causes
racines déjà isolées aux LOTs 12.7/12.8 sont toutes reconfirmées, identiques, par cette 4e capture
indépendante. `Risk.PositionSize`/`RiskAmount`/`RiskBudget`/`RiskRewardRatio` = `NOT AVAILABLE` sur
1279/1279 (cohérent, 0 `ACCEPTED`).

---

## 16. Confirmation qu'aucun ordre n'a été envoyé

Cette analyse s'est limitée à la lecture d'un fichier `ScientificDataset_MES_M5_20260818_150030.json`
déjà exporté sur disque par ATAS avant le début de cette analyse (export terminé à
`2026-08-18T13:14:37Z`, avant toute intervention de ce lot). **Aucune connexion à ATAS, aucun appel
`OpenOrder`/`SubmitOrder`/`Buy`/`Sell`/`ClosePosition`, aucune interaction avec la plateforme n'a été
effectuée par cette analyse** — l'outillage utilisé (script Python de lecture JSON, `PowerShell`
`Get-ChildItem`/`Get-Process`) n'a aucune capacité d'interagir avec ATAS. `ATAS.RiskInput.Present=True`
sur 274 barres signifie uniquement qu'une requête a atteint `RiskEngine.Evaluate` (calcul pur, Lot 10,
inchangé) — jamais qu'un ordre a été construit ou envoyé ; `Risk.Status=REJECTED` sur ces 274 barres
confirme d'ailleurs qu'aucune n'aurait pu produire d'ordre même si le pipeline d'exécution avait été
câblé (hors périmètre, non câblé — Lots 10-12).

---

## 17. Conclusion Equity

**EQUITY SOURCE UNAVAILABLE** dans ce contexte de compte (pseudo-compte `"Replay"`, qu'un compte
réel/démo soit connecté par ailleurs ou non — Section 9). Fail-closed strictement préservé : aucun
fallback `CurrentEquity = InitialCapital`/`= Balance`/`= Balance + OpenPnL` n'a été introduit ni
recommandé ; `RiskEngine.cs` (protégé, non modifié) continue de rejeter via `INVALID_EQUITY` sur chaque
barre où une requête est construite. La question posée par le brief LOT 12.9 ("l'environnement Live
fournit-il une donnée exploitable ?") reste **INCONCLUSIVE** — ce lot a testé "compte connecté + chart
Replay" (résultat : non exploitable, cause identifiée Section 9) mais pas "chart réellement live"
(non testé, action recommandée Section 19).

## 18. Conclusion QuantityStep

Inchangée depuis le LOT 12.8 : **aucune source ATAS fiable**, reconfirmée par une 4e capture réelle
indépendante. `MinQuantity`/`MaxQuantity` restent disponibles via `Security.LotMinSize`/`LotMaxSize`
quand ce flux ATAS les peuple (pas le cas dans les 4 captures à ce jour) — le paramètre manuel reste la
seule voie pour `QuantityStep`, confirmé une fois de plus sans qu'aucune substitution par `LotSize`
n'ait été introduite.

## 19. Limites

- **Le scénario "chart réellement live (non-Replay) + compte connecté" n'a jamais été testé** par
  l'ensemble des LOTs 12.1–12.9 — c'est la seule configuration susceptible de répondre définitivement à
  la question Equity du LOT 12.9.
- Le fournisseur de données (connecteur/broker) reste **NOT AVAILABLE** — non exporté par le schéma de
  télémétrie actuel (`Security.ConnectorId` existe dans le SDK, Lot 12.8, mais n'est pas capturé par
  `ScientificDatasetRecord.From`).
- `Position.Volume`/`.UnrealizedPnL`/`.RealizedPnL` non exportés distinctement — non observables dans
  cette capture (Section 6).
- Les paramètres manuels `RiskInstrumentQuantityStep`/`MinQuantity`/`MaxQuantity` n'ont pas été
  configurés pendant cette session — la Phase G (obtenir `IsValid=true` sans coder) reste non testée
  empiriquement, seulement déduite du code (Section 14).
- `ATAS.DataFeedsCore.Statistics.IStatisticsParameterGroup` (contenu de `ITradingStatistics.Statistics`)
  reste non exploré — piste ouverte depuis le LOT 12.8.

## 20. Prochaine action recommandée

Aucune modification de code n'est justifiée par cet audit. Deux actions concrètes, sans code, pour lever
les limites de la Section 19 :

1. **Refaire une capture avec un chart réellement live** (pas de Market Replay actif) et un compte
   démo/réel sélectionné dans le panneau de trading ATAS — seule façon de trancher définitivement la
   question Equity du LOT 12.9 (Section 8, verdict actuel INCONCLUSIVE).
2. **Configurer `RiskInstrumentQuantityStep`/`MinQuantity`/`MaxQuantity` à des valeurs positives** dans
   le panneau ATAS avant un nouveau Replay ou une session live, pour vérifier empiriquement que
   `InstrumentRiskSpecification.IsValid` devient `True` sans aucune modification de code (Phase G,
   non testée ce lot).

---

# LOT 12.9 RESULT

Status : PARTIAL — validation effectuée avec compte connecté + chart Replay ; scénario chart réellement live non testé (voir Section 19)

## ENVIRONMENT
Account type : Pseudo-compte ATAS "Replay" (IsRealAccount=False) — un compte réel/démo était connecté par ailleurs mais n'est pas celui exposé à l'indicateur pendant que le chart est en Replay (Section 9)
Data provider : NOT AVAILABLE (non exporté par le schéma de télémétrie actuel)
Instrument : Micro E-mini S&P 500 (MES), TickSize=0.25, TickCost=1.25
Mode : Replay (chart), malgré une tentative de connexion de compte réel/démo

## ACCOUNT
Balance : 0 (zéro réel, constant, 1279/1279 barres)
BalanceAvailable : NOT AVAILABLE (absence de donnée, decimal? jamais peuplé)
BalancePower : 0 (zéro réel, constant)
OpenPnL : 0 (zéro réel, constant)
ClosedPnL : 0 (zéro réel, constant)
TotalPnL : 0 (zéro réel, constant)
Realtime Equity : 105.50 (constant, jamais sélectionné comme source pendant cette capture)
Replay Equity : NOT AVAILABLE (SeriesCount=0, 1279/1279)

## EQUITY
LIVE source : Aucune testée avec succès — compte connecté mais contexte Portfolio resté "Replay" (Section 9)
LIVE value : N/A
Replay source : TradingStatisticsProvider.Replay.Equity (déjà câblé, Lot 12.2/12.6)
Replay value : NOT AVAILABLE (série vide)
Usable CurrentEquity : NO
Verdict : INCONCLUSIVE (chart réellement live jamais testé — voir Section 8/19)

## INSTRUMENT
Symbol : Micro E-mini S&P 500
TickSize : 0.25
TickCost : 1.25
LotSize : 1 (raw ATAS — non utilisé comme QuantityStep, délibéré)
LotMinSize : NOT AVAILABLE
LotMaxSize : NOT AVAILABLE
QuantityStep : 0 (paramètre manuel non configuré cette session)
MinQuantity : 0 (fallback manuel non configuré cette session)
MaxQuantity : 0 (fallback manuel non configuré cette session)
IsValid : False

## MANUAL PARAMETERS
InitialCapital : 25000 (configuré, 1257/1279 barres ; 22 premières barres = 0.0, artefact de warm-up sans impact)
QuantityStep : 0 (non configuré cette session)
MinQuantity : 0 (non configuré cette session)
MaxQuantity : 0 (non configuré cette session)

## RISK ENGINE
Status : REJECTED (274/1279 barres avec candidat directionnel ; 1005/1279 sans candidat, RiskEngine non invoqué)
Reasons : INVALID_EQUITY;INSTRUMENT_SPEC_INVALID;INVALID_STOP_LOSS (274/274, sans exception)
Risk Budget : NOT AVAILABLE (1279/1279)
Trade Risk : NOT AVAILABLE (1279/1279)
Position Size : NOT AVAILABLE (1279/1279)
R:R : NOT AVAILABLE (1279/1279)

## ORDER SAFETY
Orders sent : NO
Positions opened : NO
SubmitOrder used : NO
Buy used : NO
Sell used : NO

## CONCLUSION

Ce lot apporte une découverte structurelle nouvelle, non anticipée par les LOTs 12.1–12.8 : le contexte
de compte exposé à l'indicateur (`Indicator.TradingManager.Portfolio`) suit le mode du CHART (Replay ou
non), pas la connexion effective d'un compte par l'utilisateur — connecter un compte réel/démo n'a eu
aucun effet observable tant que le chart restait en Replay. Ceci confirme empiriquement, pour la
première fois en conditions réelles, que `Portfolio.IsReplay()` concorde à 100% (1279/1279) avec le
signal déjà utilisé par le correctif du LOT 12.6. La question centrale du LOT 12.9 (Equity exploitable
en environnement connecté) reste **INCONCLUSIVE** : ce lot a testé "compte connecté pendant un Replay"
(réponse : non exploitable, cause désormais identifiée) mais pas "chart réellement live" — la seule
configuration encore jamais testée sur l'ensemble des LOTs 12.1–12.9, et donc la prochaine action
recommandée (Section 20). Aucun fallback Equity n'a été inventé ; le Risk Engine reste fail-closed,
inchangé, cohérent avec les 3 captures précédentes.

Files modified : NONE

Build : NOT RUN

Tests : NOT RUN

DLL deployed : NO

Commit : NO

STOP — FIN DU LOT 12.9.
