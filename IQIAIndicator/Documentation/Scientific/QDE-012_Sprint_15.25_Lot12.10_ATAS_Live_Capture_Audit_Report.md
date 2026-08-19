# QDE-012 — Sprint 15.25 (Lot 12.10) — Audit de la capture ATAS Live

**Lot de validation offline uniquement.** Aucun fichier `.cs` modifié, aucun build, aucune DLL, aucun
commit. Analyse strictement limitée à la lecture d'un fichier déjà exporté par ATAS avant le début de
cette analyse — aucune connexion à ATAS, aucun ordre envoyé.

---

## 1. Capture sélectionnée

`ScientificDataset_MES_M5_20260818_154144` (`SessionId 1d06230e-6361-4ae2-8816-4b16e90c5943`),
`%LOCALAPPDATA%\IQIA\ScientificDataset\`.

## 2. Raison de sa sélection

Trois captures candidates identifiées depuis le LOT 12.9 (par ordre chronologique) :

| SessionId | Lifecycle | BarsWritten | Verdict |
|---|---|---:|---|
| `95eab954-...` (`_153731`) | `LastError: "No data collected"` | 0 | Écartée — session avortée, aucune donnée |
| **`1d06230e-...` (`_154144`)** | Export réussi | **3** | **Retenue** |
| `70cbbd27-...` (`_165234`) | `LastError: "No data collected"` | 0 | Écartée — session avortée, aucune donnée |

Non choisie par simple récence (Phase A) : les deux autres sont vides (0 barre). `_154144` est la seule
à contenir des données exploitables, et sa taille (3 barres, ~10 minutes de données de marché) correspond
exactement au format court demandé (5-15 minutes) pour une validation technique.

## 3. Preuve LIVE / Replay

**Signal mixte, documenté sans arbitrage forcé** (Phase C du brief) :

| Signal | Valeur observée | Indique |
|---|---|---|
| `ATAS.Raw.Account.AccountID` | `PP-CASH-ETL25K-000***-*****` *(masqué, identifiant de compte réel — voir Section 5)* | **PAS Replay** — pour la première fois depuis le LOT 12.5, ce n'est plus la chaîne constante `"Replay"` |
| `ATAS.Raw.Account.PortfolioIsReplay` (`Portfolio.IsReplay()`) | `False` | **PAS Replay** — critère déterminant explicitement requis par la Phase B du brief (`Portfolio.IsReplay == false`) |
| `ATAS.Adapter.Equity.HeuristicIsReplay` (`context.Execution.IsReplay`, ancienne heuristique bar-index) | `True` | Indique Replay — mais c'est précisément l'heuristique documentée comme non fiable depuis le LOT 12.6 |
| `ATAS.Raw.Equity.SourceMode` (décision corrigée du LOT 12.6, `OR` avec l'heuristique) | `Replay` | Hérite du `True` ci-dessus via la clause `OR` (jamais un veto possible, seulement un ajout — Lot 12.6, par construction) |
| Cadence des barres | 3 barres écrites sur 68 minutes d'horloge murale (13:41→14:52), volumes distincts par barre (14928/15427/3358) | **Cohérent avec un flux temps réel** — à comparer aux Replay précédents : LOT 12.7 (63 barres/3,5 min), LOT 12.9 (1279 barres/14 min) — un rythme des centaines à des milliers de fois plus rapide que le temps réel |
| `FormingBarUpdates` (métadonnées) | `0` | Cohérent avec un flux temps réel (barres reçues une à une, déjà finalisées) — les captures Replay complètes montrent des milliers de mises à jour de barre en formation (ex. LOT 12.6 : 2478) |

**Verdict retenu, selon le critère explicitement posé par la Phase B du brief** (*"La capture ne peut
être considérée comme LIVE que si les données permettent de démontrer `Portfolio.IsReplay == false` et
idéalement `Execution.IsReplay == false`"* — le second critère n'étant qu'"idéalement", non bloquant) :

## **LIVE CONFIRMÉ** (critère déterminant satisfait), avec une réserve documentée : `Execution.IsReplay`
(l'ancienne heuristique bar-index, Lot 12.6) reste `True` — très probablement un **faux positif** de
cette heuristique déjà connue pour ce défaut, plutôt qu'une preuve réelle de replay, compte tenu du
faisceau d'indices convergents ci-dessus (compte réel, `Portfolio.IsReplay=False`, cadence temps réel,
`FormingBarUpdates=0`). Ce n'est PAS une preuve absolue — seulement l'interprétation la mieux étayée par
les données disponibles.

**Conséquence directe et actionnable** (nouveau, non observé aux LOTs précédents) : parce que
`ATASEquityReplayDetector.IsReplayContext` est un `OR` avec l'ancienne heuristique (Lot 12.6, "ne peut
jamais retirer une détection Replay déjà correcte"), un faux positif de cette heuristique **peut
maintenir `SourceMode = "Replay"` même sur une session authentiquement live** avec un compte réellement
non-Replay connecté — empêchant la lecture de `Realtime.Equity` précisément dans le cas où elle serait la
plus pertinente. C'est une limite du correctif du LOT 12.6 non identifiée jusqu'ici (le `OR` protège
contre les faux négatifs de l'heuristique, mais reste vulnérable à ses faux positifs).

## 4. Instrument

`Micro E-mini S&P 500` (MES), `TickSize=0.25`, `TickCost=1.25` — identique aux captures précédentes,
3/3 barres.

## 5. Compte

`AccountID` réel observé (masqué ici par prudence, conforme à la consigne du LOT 12.9 — ce n'est pas la
chaîne interne `"Replay"`, c'est un identifiant de compte structuré, cohérent avec un compte cash
d'évaluation prop-firm à 25K) : `PP-CASH-ETL25K-000761-XXXXXX`. `IsRealAccount = False` (compte
démo/évaluation, pas un compte financier réel au sens ATAS — cohérent avec un compte d'évaluation
prop-firm). `Currency = USD`.

---

## 6. Données Portfolio

| Champ | Valeur | Disponible ? | Zéro réel / non nul ? | Dynamique sur les 3 barres ? |
|---|---|---|---|---|
| `Balance` | `25103.92` | Disponible | **Non nul** — première observation d'un Balance réel dans toute la série de captures (LOTs 12.5→12.10) | Statique (constant sur les 3 barres — attendu, aucune position ouverte, aucun ordre envoyé, fenêtre de 10 min) |
| `BalanceAvailable` | `0` | Disponible (pas "NOT AVAILABLE") | Zéro réel | Statique |
| `BalancePower` | `0` | Disponible | Zéro réel | Statique |
| `OpenPnL` | `0` | Disponible | Zéro réel | Statique |
| `ClosedPnL` | `0` | Disponible | Zéro réel | Statique |
| `TotalPnL` | `0` | Disponible | Zéro réel | Statique |

**Observation nouvelle et importante** : `Balance = 25103.92` ≠ `InitialCapital = 25000.0` (paramètre
manuel) — un écart de `103.92` non expliqué par `OpenPnL`/`ClosedPnL`/`TotalPnL` (tous à `0`). Ceci
**contredit empiriquement** l'hypothèse `Equity = Balance + OpenPnL` autant que ne le faisait la
Section 8 du LOT 12.8 par absence de preuve — ici, on a une preuve directe qu'aucune relation additive
simple entre `Balance` et les champs `PnL` exposés n'explique la valeur observée (soit `Balance` inclut
déjà un historique de P&L antérieur à cette session que `ClosedPnL` ne reflète pas, soit ces champs
`PnL` ne sont simplement pas peuplés par ce connecteur/courtier). **Cause exacte NON déterminée** — hors
de portée d'une lecture read-only du dataset.

## 7. Données Equity

### Test des 4 cas (identique structure au LOT 12.9, nouvelles valeurs)

| Cas | Source | Valeur | Utilisable comme CurrentEquity ? |
|---|---|---|---|
| 1 | `Portfolio.Balance` | `25103.92`, statique sur la fenêtre observée | Candidat plausible en apparence (non nul, dynamique de session à session au vu du LOT 12.9 où il valait `0`) — mais **jamais câblé comme source d'Equity** par le code protégé (`ATASAccountStateAdapter`, Lot 12.2) ; l'équivalence Balance≡Equity reste non démontrée (Section 6) |
| 2 | `Balance + OpenPnL` | `25103.92 + 0 = 25103.92` | Même valeur que le cas 1, ne le renforce ni ne l'infirme — relation toujours non prouvée |
| 3 | `Portfolio.TotalPnL` | `0` | Non — un P&L, pas un capital |
| 4 | `TradingStatisticsProvider.Realtime.Equity` (`ATAS.Raw.Equity.RealtimeValue`) | `105.50`, statique sur les 3 barres, `LastTimestamp=NOT AVAILABLE` | Non — **valeur identique et inchangée depuis le LOT 12.5** (compte/session totalement différents), aucun lien apparent avec le compte réel `25103.92` observé ici ; tout indique une lecture obsolète/déconnectée de ce nouveau compte |

**Résultat effectivement reçu par `RiskEngine`** : `ATAS.Adapter.Account.CurrentEquity = "0"` sur 3/3
barres (sentinel fail-closed, `ATASAccountStateAdapter.Build`, protégé, inchangé) — parce que
`SourceMode = "Replay"` (Section 3) et `Replay.Equity` reste une série vide (`SeriesCount=0`), identique
à toutes les captures précédentes. **Le Balance réel observé (25103.92) n'atteint jamais `RiskEngine`**
: aucun chemin de code actuel (protégé, non modifié) ne le lit.

---

## 8. Télémétrie ATAS

Toutes les clés `ATAS.Raw.*`/`ATAS.Adapter.*`/`ATAS.RiskInput.*` sont présentes et peuplées sur 3/3
barres (aucune clé manquante) — l'infrastructure d'observabilité (LOTs 12.5/12.6) fonctionne
correctement, y compris sur cette session live courte. `ATAS.RiskInput.Present = False` sur 3/3 barres
(aucun candidat directionnel dans cette fenêtre de 10 minutes — `TradePlan.Status=NO_TRADE` sur les 3
barres, `EntryTrigger.Direction=NO_ACTION`) → **`RiskEngine.Evaluate` n'a jamais été invoqué** dans
cette capture précise (voir Section 12).

## 9. Test dynamique (Phase F)

| Donnée | Première valeur (13:40) | Dernière valeur (13:50) | STATIC / DYNAMIC |
|---|---|---|---|
| Realtime Equity | `105.50` | `105.50` | STATIC |
| Balance | `25103.92` | `25103.92` | STATIC |
| BalanceAvailable | `0` | `0` | STATIC |
| OpenPnL | `0` | `0` | STATIC |

Toutes les valeurs sont STATIC sur cette fenêtre de 10 minutes. Conforme à la Phase F du brief : **une
valeur statique non nulle n'est pas automatiquement invalide** — l'absence de mouvement sur `Balance`
est l'attendu logique d'une session sans transaction (aucun ordre envoyé, Section 15) sur une fenêtre
courte. Cela reste toutefois une preuve plus faible qu'une variation observée ; seule une session plus
longue avec activité de marché (toujours sans ordre) permettrait d'observer une véritable dynamique.

## 10. Données instrument MES

| Champ | ATAS | RiskSpecification | Source |
|---|---:|---:|---|
| Symbol | `Micro E-mini S&P 500` | `Micro E-mini S&P 500` | `Security.Instrument` |
| TickSize | `0.25` | `0.25` | `Security.TickSize` |
| TickCost | `1.25` | `1.25` | `Security.TickCost` |
| LotSize | `1` | — (non utilisé) | `Security.LotSize` — toujours rejeté (défaut SDK indiscernable, Lots 12.6/12.8) |
| LotMinSize | `NOT AVAILABLE` | — | Non peuplé par ce flux, identique à toutes les captures précédentes |
| LotMaxSize | `NOT AVAILABLE` | — | Idem |
| QuantityStep | — | `0` | Paramètre manuel non configuré |
| MinQuantity | — | `0` | Fallback manuel non configuré |
| MaxQuantity | — | `0` | Fallback manuel non configuré |

Aucun changement par rapport aux LOTs 12.7/12.9 : la spécification instrument dépend uniquement de
`Security` (indépendant du compte connecté), donc sans surprise identique.

## 11. QuantityStep

**`QUANTITY-MANUAL`** — inchangé. Le passage à un compte réel n'apporte aucune nouvelle source
`QuantityStep` (attendu : `QuantityStep` provient de `Security`, jamais de `Portfolio`). `LotSize=1` non
utilisé comme substitut, conforme à la contrainte absolue du brief.

---

## 12. RiskAssessment

`Risk.Status = "NOT AVAILABLE"` sur 3/3 barres — **`RiskEngine.Evaluate` n'a été invoqué à aucun moment**
dans cette capture (aucun candidat directionnel produit par `TradePlan` sur cette fenêtre de 10 minutes).
**Aucune comparaison directe possible** entre `INVALID_EQUITY` en Replay vs en Live à partir des seuls
résultats `RiskAssessment` de cette capture (Phase H) — la fenêtre est trop courte pour avoir généré un
`SIGNAL_ONLY`/candidat directionnel.

**Inférence indirecte, fondée sur le code déjà lu et inchangé** (pas une supposition) : puisque
`ATAS.Adapter.Account.CurrentEquity = "0"` sur les 3 barres (Section 7), **si** un candidat directionnel
était apparu, `RiskEngineRequestFactory.FromTradePlan` (protégé, inchangé) aurait transmis
`CurrentEquity=0` à `RiskEngine`, qui aurait rejeté avec `INVALID_EQUITY` exactement comme aux
LOTs 12.7/12.9 — le passage à un compte live réel **ne change rien** à ce résultat, parce que
`SourceMode` reste bloqué sur `"Replay"` (Section 3), pas parce que le compte lui-même serait invalide.

## 13. Stop Loss

`TradePlan.Status = "NO_TRADE"` sur 3/3 barres (pas même `SIGNAL_ONLY` — aucun candidat directionnel du
tout dans cette fenêtre). `TradePlan.StopLoss = "NOT AVAILABLE"`, `StopLossAvailable = "False"` sur
3/3. **Verdict : blocage indépendant connu, non observable en action dans cette capture précise**
(aucune barre `SIGNAL_ONLY` pour le confirmer directement ici), mais cohérent avec le comportement
documenté et inchangé depuis le Sprint 15.8 (Lots 12.4/12.7/12.9). Aucun SL créé, conforme à la
contrainte absolue.

---

## 14. Comparaison Replay/LIVE

| Donnée | LOT 12.7 (Replay) | LOT 12.9 (Replay, compte connecté) | **LOT 12.10 (Live confirmé)** |
|---|---|---|---|
| AccountID | `"Replay"` | `"Replay"` | **Identifiant réel** (masqué) |
| IsRealAccount | `False` | `False` | `False` (compte démo/évaluation) |
| Portfolio.IsReplay | `True` | `True` | **`False`** |
| Execution.IsReplay (heuristique) | — (non extrait séparément) | `True` | `True` (probable faux positif — Section 3) |
| Balance | `0` | `0` | **`25103.92`** |
| BalanceAvailable | `NOT AVAILABLE` | `NOT AVAILABLE` | `0` (disponible, mais zéro) |
| BalancePower | `0` | `0` | `0` |
| OpenPnL | `0` | `0` | `0` |
| TotalPnL | `0` | `0` | `0` |
| Replay Equity | `NOT AVAILABLE` (SeriesCount=0) | `NOT AVAILABLE` (SeriesCount=0) | `NOT AVAILABLE` (SeriesCount=0) — identique |
| Realtime Equity | `105.50` | `105.50` | `105.50` — **identique**, jamais lié au nouveau compte réel |
| QuantityStep | `0` (manuel) | `0` (manuel) | `0` (manuel) — identique |
| RiskAssessment | `REJECTED` (13/63 barres) | `REJECTED` (274/1279 barres) | Non observé (0 candidat directionnel dans cette fenêtre courte) |

**Différence principale démontrée par le passage Replay → Live** : `AccountID` et `Portfolio.IsReplay`
changent bien (preuve que le compte réel est authentiquement vu par ATAS) et `Balance` devient une
valeur réelle non nulle pour la première fois — **mais `SourceMode`/`Replay.Equity`/`Realtime.Equity`
restent identiques en tout point à toutes les captures Replay précédentes**, parce que la sélection de
source Equity reste gouvernée par `Execution.IsReplay` (resté `True`, Section 3), pas par
`Portfolio.IsReplay`. C'est la preuve la plus directe obtenue à ce jour que la limitation Equity n'est
**pas** une question de connexion de compte, mais du signal utilisé par
`ATASEquityReplayDetector`/`ATASAccountStateAdapter` pour choisir quelle série lire.

---

## 15. Sécurité

Analyse strictement limitée à la lecture du fichier `ScientificDataset_MES_M5_20260818_154144.json`
(export terminé à `2026-08-18T14:52:33Z`, avant le début de cette analyse). Aucune connexion à ATAS,
aucun appel `OpenOrder`/`SubmitOrder`/`Buy`/`Sell`/`ClosePosition`. `ATAS.RiskInput.Present=False` sur
3/3 barres confirme qu'aucune requête n'a même atteint `RiskEngine` — a fortiori aucun ordre n'a pu être
construit ou envoyé.

---

## 16. Verdict Equity

## **EQUITY-LIVE-UNAVAILABLE**

Mode Live confirmé (Section 3 — `Portfolio.IsReplay=False`, critère déterminant de la Phase B), mais
aucune source Equity exploitable n'est atteinte par `RiskEngine` : `Replay.Equity` reste vide, et bien
qu'un `Balance` réel et non nul soit désormais observable (`25103.92`), (a) il n'est câblé nulle part
comme source de `CurrentEquity` par le code protégé, (b) son équivalence avec `Equity` reste non
démontrée (Section 6, écart inexpliqué de `103.92` avec `Balance+OpenPnL`), et (c) même
`Realtime.Equity`, la source déjà conçue pour ce rôle, reste bloquée par le `SourceMode="Replay"`
(faux positif probable de l'ancienne heuristique, Section 3) et affiche une valeur manifestement
obsolète/sans lien avec ce compte. **Non `VALIDATED`** : une valeur non nulle est apparue, mais ni sa
source correcte ni sa sémantique Equity n'ont pu être établies (conforme à l'avertissement explicite de
la Phase K du brief).

## 17. Verdict QuantityStep

**`QUANTITY-MANUAL`** — inchangé, reconfirmé sur une capture réellement live. Aucune source ATAS fiable.

## 18. Conclusion générale

Ce lot confirme, pour la première fois avec un compte réellement connecté et un chart démontrablement
non-Replay au niveau `Portfolio` (`IsReplay=False`, identifiant de compte réel, cadence temps réel), que
**le problème Equity n'est pas un problème de compte** — un vrai compte est bien vu par ATAS, avec un
vrai Balance non nul. **C'est un problème de sélection de source** : le signal `Execution.IsReplay`
(l'ancienne heuristique bar-index) est resté à `True` sur cette session, très probablement un faux
positif, ce qui a maintenu `SourceMode="Replay"` et empêché toute lecture de `Realtime.Equity` — la
source qui, structurellement, aurait été interrogée dans le cas contraire. Le correctif du LOT 12.6
(clause `OR`) protège contre les faux négatifs de l'heuristique mais reste, par construction, vulnérable
à ses faux positifs — une limite désormais démontrée empiriquement, pas seulement théorique. Aucune
modification n'a été apportée à ce lot (audit uniquement) ; le Risk Engine reste fail-closed.

## 19. Prochaine étape

Aucune modification de code n'est justifiée par ce seul lot. Pistes concrètes, sans code :

1. **Répéter une capture live plus longue** (toujours 5-15 min, mais sur une plage horaire où le marché
   bouge davantage) pour observer si `Balance`/`Realtime.Equity` deviennent dynamiques et si
   `Execution.IsReplay` reste bloqué à `True` de façon persistante ou seulement ponctuelle.
2. **Investiguer pourquoi `Execution.IsReplay` renvoie `True`** sur une session dont `Portfolio.IsReplay`
   dit `False` — cela nécessiterait de relire `Core/MarketContextBuilder.cs`/`Core/ExecutionContext.cs`
   (fichiers non protégés par ce lot mais non modifiés ici, hors périmètre strict de ce lot d'audit) pour
   comprendre précisément quelle condition de bar-index a produit ce faux positif dans ce cas précis.
3. Le lien `Balance ↔ Equity` reste `EQUITY SOURCE UNAVAILABLE` au sens strict — aucune preuve
   suffisante pour le câbler, conforme à la Phase H du brief.

---

# LOT 12.10 RESULT

## CAPTURE

File : ScientificDataset_MES_M5_20260818_154144
Bars : 3 (BarsReceived=4, BarsWritten=3)
Instrument : Micro E-mini S&P 500 (MES)
Timeframe : M5
Window : 2026-08-18T13:40:00 → 2026-08-18T13:50:00 (barres) / 13:41:44Z → 14:52:33Z (horloge murale, 68 min)

## MODE

Portfolio.IsReplay : False
Execution.IsReplay : True (probable faux positif de l'ancienne heuristique bar-index, voir Section 3)
SourceMode : Replay (hérité du True ci-dessus via la clause OR du LOT 12.6)
Live confirmed : YES (critère déterminant Portfolio.IsReplay==false satisfait) — avec réserve documentée sur Execution.IsReplay

## ACCOUNT

AccountID : PP-CASH-ETL25K-000761-XXXXXX (masqué)
IsRealAccount : False (compte démo/évaluation)
Currency : USD

## PORTFOLIO

Balance : 25103.92 (non nul, statique sur la fenêtre observée — première observation d'un Balance réel dans toute la série de captures)
BalanceAvailable : 0
BalancePower : 0
OpenPnL : 0
ClosedPnL : 0
TotalPnL : 0

## EQUITY

Realtime Equity : 105.50 (statique, inchangé depuis le LOT 12.5, sans lien apparent avec ce compte)
Source : TradingStatisticsProvider.Replay.Equity (sélectionné car SourceMode=Replay) — série vide
First value : 105.50 (Realtime, jamais sélectionné) / N/A (Replay, vide)
Last value : identique (statique sur les 3 barres)
Dynamic : NO (statique sur la fenêtre observée, 10 minutes sans transaction)
Verdict : EQUITY-LIVE-UNAVAILABLE

## INSTRUMENT MES

Symbol : Micro E-mini S&P 500
TickSize : 0.25
TickCost : 1.25
LotSize : 1 (raw ATAS, non utilisé comme QuantityStep)
LotMinSize : NOT AVAILABLE
LotMaxSize : NOT AVAILABLE
QuantityStep : 0 (manuel, non configuré)
MinQuantity : 0 (manuel, non configuré)
MaxQuantity : 0 (manuel, non configuré)

## RISK ENGINE

Status : NOT AVAILABLE (RiskEngine jamais invoqué — aucun candidat directionnel dans cette fenêtre de 3 barres)
RejectionReasons : NOT AVAILABLE (mais CurrentEquity=0 confirmé reçu en amont — INVALID_EQUITY aurait été inévitable si une requête avait été construite, par inférence directe du code inchangé)

## STOP LOSS

TradePlan.Status : NO_TRADE (3/3 barres — pas même SIGNAL_ONLY dans cette fenêtre)
StopLoss : NOT AVAILABLE
Verdict : blocage indépendant connu, non observé en action dans cette capture précise (fenêtre trop courte pour un candidat directionnel)

## COMPARISON

Replay vs LIVE : AccountID et Portfolio.IsReplay changent authentiquement (preuve d'un vrai compte connecté, Balance réel non nul apparaît pour la première fois) ; SourceMode/Replay.Equity/Realtime.Equity restent identiques en tout point aux captures Replay précédentes
Main difference : le blocage Equity n'est PAS lié à la connexion du compte (un vrai compte est bien vu, avec un vrai Balance) — il est lié au signal Execution.IsReplay resté à True (probable faux positif de l'ancienne heuristique), qui maintient SourceMode=Replay via la clause OR du LOT 12.6

## SAFETY

Orders sent : NO
Positions opened : NO
SubmitOrder : NOT USED
Buy : NOT USED
Sell : NOT USED

## FINAL VERDICTS

Equity : EQUITY-LIVE-UNAVAILABLE
QuantityStep : QUANTITY-MANUAL
Risk Engine : Fail-closed, inchangé et cohérent (CurrentEquity=0 → INVALID_EQUITY inévitable par inférence directe du code ; non observé en action faute de candidat directionnel dans cette fenêtre courte)

## FILES

Files modified : NONE
Build : NOT RUN
Tests : NOT RUN
DLL deployed : NO
Commit : NO

STOP — FIN DU LOT 12.10.
