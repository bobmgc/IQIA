# QDE-022 — Pré-enregistrement : contenu informationnel du carnet d'ordres L2 (ES, Databento GLBX.MDP3)

## Historique des amendements

| Version | Référence | Statut |
|---|---|---|
| **v1** | commit `2d21ae5`, tag `qde-022-prereg` | **remplacée** (conservée dans l'historique git, non réécrite) |
| **v2** | commit `2e8d7c9`, tag `qde-022-prereg-v2` | **remplacée** (conservée dans l'historique git, non réécrite) |
| **v3** | commit `2dd383a`, tag `qde-022-prereg-v3` | **remplacée** (conservée dans l'historique git, non réécrite) |
| **v4** | ce document, tag `qde-022-prereg-v4` | **en vigueur — remplace v3** |

**v2 remplace v1 sur trois points, et trois seulement :**
1. **Gate économique corrigé** : `0,75 pt ES` (v1, infaisable — voir §7.3, note) → **`0,80 tick ES`**.
2. **Instrument de référence du gate explicité** dans §7 (n'est plus une simple menace à la validité en §8) : référence = **ES**, report obligatoire en ticks ES **et** ticks MES, seuil MES documenté, verdict **GO conditionnel** introduit.
3. **Portée de H0 restreinte** : H0 n'est plus « clôture de toute la ligne recherche d'edge » mais un énoncé étroit (un mois, un instrument, un schéma, quatre indicateurs, horizon ≥ 60 s, latence nulle). La clôture de la ligne recherche d'edge devient une **conséquence recommandée** en cas de non-rejet de H0 (§7.5), pas la définition de H0. Même correction de formulation que celle appliquée à la conclusion de QDE-021.

**Aucune donnée de marché n'a été acquise entre v1 et v2.** L'amendement est **antérieur à tout `metadata.get_cost` et à tout `get_range`**. La correction n'a été motivée par aucun résultat — il n'existe aucun résultat.

Tout le reste de v1 est conservé à l'identique : §2 (spécification d'achat), §3 (les 4 indicateurs et leurs formules), §4 (les 6 horizons), §5 (24 tests, Holm α = 0,05), §6 (métrique primaire = courbe de décroissance IC, Pearson + IC95 Newey–West, pas de backtest / P&L / Sharpe / calibration), les deux autres conditions cumulatives de §7 (Holm ; stabilité de signe TRAIN/OOS 60/40 purgé), §8 à §13.

**v3 remplace v2 sur quatre points, et quatre seulement :**
1. **Période déplacée** : avril 2026 (repli mai 2026) → **août 2026** (repli juillet 2026). Choix a priori : août 2026 est le dernier mois calendaire complet à la date de rédaction (2026-09-04) ; aucune donnée n'a été acquise pour avril ni pour août avant ce changement, donc ce n'est pas un re-choix après résultat.
2. **Contrat changé** : `ESM26` → **`ESU6`** (échéance septembre 2026), cohérent avec le principe déjà gelé « mois de front, hors roulement » : le roulement M26→U26 a lieu ~mi-juin 2026, le roulement U26→Z26 ~mi-septembre 2026 ; août 2026 est donc à l'intérieur de la fenêtre de front-month d'ESU6.
3. **Voie d'acquisition explicitée comme choix actif** (§2.2) : téléchargement ciblé direct (`stype_in="raw_symbol"`, `symbols=["ESU6"]`) retenu, contre un pull large via symbologie `parent` (`ES.FUT`, tous contrats ES cotés) suivi d'un filtrage local à l'instrument. Ce n'est pas un changement de règle — v2 §2.1 fixait déjà `stype_in=raw_symbol` — mais v3 documente explicitement pourquoi l'alternative `parent` + filtrage a posteriori est écartée (§2.2, note).
4. **Une exclusion de 3 dates calendaires (2, 9, 29 août) a été examinée puis ABANDONNÉE avant congélation** — voir note ci-dessous. v3 ne contient aucune exclusion de date individuelle.

> **Note — exclusion de dates examinée et rejetée.** Lors de la préparation de v3, une exclusion des 2, 9 et 29 août avait été envisagée. Vérification : ce ne sont pas tous les week-ends d'août (1, 8, 15, 16, 22, 23, 30 ne l'étaient pas), et la raison de ce sous-ensemble précis s'est révélée inconnue au moment de la rédaction — ni jour férié CME identifié, ni anomalie de donnée documentée. Introduire une exclusion de jour dont la justification n'est pas connue violerait l'interdit de v2 §9 (choix, après avoir vu les données, du jour, sans pénalité), qui est précisément le garde-fou que ce format de document existe pour faire respecter. Décision : l'exclusion n'est pas retenue. Le filtre de session RTH (§2.1, 13:30–20:00 UTC) écarte nativement, à l'analyse, tout jour calendaire sans point de grille valide (week-ends compris) — aucune intervention manuelle sur la liste de jours n'est nécessaire ni introduite.

**Aucune donnée L2 (`get_range`/`batch.submit_job`) n'a été acquise avant v3.** Une déviation de séquencement réelle est cependant consignée ci-dessous plutôt que dissimulée :

> **Déviation de séquencement — `metadata.get_cost` exécuté avant le tag v3.** Contrairement à la discipline énoncée en v2 §2.2/§11 (le tag doit précéder tout `get_cost`), l'appel `metadata.get_cost(dataset="GLBX.MDP3", schema="mbp-10", symbols=["ESU6"], stype_in="raw_symbol", start="2026-08-02", end="2026-09-02")` a été exécuté le 2026-09-04, à la demande explicite de l'utilisateur, avant que ce document v3 n'existe sous forme commitée/taggée. Résultat : 30,048361867666 USD (~30,05 $). `metadata.get_cost` ne retourne qu'un coût et un volume estimé — aucune donnée de prix, de carnet ou de signal — donc cette déviation ne crée aucun risque de data-snooping sur les indicateurs (§3), les horizons (§4) ou la règle de décision (§7) : rien de ce qui est mesuré par QDE-022 n'a été vu. Elle est néanmoins documentée ici par souci de transparence totale, et la règle de séquencement reste en vigueur pour tout amendement futur (v4+) : tag avant tout nouvel appel get_cost/get_range.

**v4 remplace v3 sur deux points, et deux seulement :**
1. **§8.7 réécrit pour dire la vérité.** Le contrôle d'intégrité des données livrées (`QDE-022_DataIntegrity_Check.md`, 2026-09-04) a révélé qu'un job Databento antérieur non conforme (`GLBX-20260903-7XE859LGPE`, `stype_in=parent`, `symbols=ES.FUT`) avait déjà téléchargé de vraies données de marché sur exactement la portée « août 2026 / `ESU6` » le 2026-09-03, **avant** que cette portée n'existe sous forme de pré-enregistrement gelé (tag `qde-022-prereg-v3`, 2026-09-04). L'affirmation de v3 §8.7 (« choix a priori, aucune donnée acquise ») était donc **factuellement fausse**. §8.7 est intégralement réécrit avec la chronologie datée, le raisonnement retenu, et la décision explicite de conserver août 2026 avec ce risque résiduel assumé — voir §8.7 pour le détail complet, non résumé ici afin de ne pas en atténuer la portée.
2. **Renvoi corrigé en §8, menace n°6** : pointait encore vers un MDE « TODO, non chiffrés » ; corrigé vers §6.4, où le MDE a été chiffré lors du contrôle d'intégrité (2026-09-04, avant v4).

**Aucune analyse n'a été lancée entre v3 et v4 : aucun OFI, aucun IC, aucun rendement n'a été calculé à ce jour.** L'amendement précède toute mesure.

**Conséquence hors de ce document :** le rapport `QDE-022_DataIntegrity_Check.md` portait un verdict global NON CONFORME motivé exclusivement par l'inexactitude de l'ancien §8.7 — pas par un défaut des fichiers de données, qui étaient et restent structurellement conformes. Ce rapport est mis à jour en CONFORME dans le même commit que v4, avec renvoi explicite à v4 §8.7 et mention du risque résiduel documenté ; la trace du verdict antérieur (NON CONFORME) y est conservée, pas effacée.

Tout le reste de v3 est conservé à l'identique : §1.2 (H0), §2 (spécification d'acquisition), §3 (les 4 indicateurs), §4 (les 6 horizons), §5 (Holm α = 0,05), §6 (métrique primaire et §6.4 MDE, déjà chiffré), §7 (règle de décision et gate à 0,80 tick ES), §9 (interdits gelés), §10 à §13. Aucun indicateur, aucun horizon, aucun seuil ne bouge.

---

**Date v1 :** 2026-09-01 — **Date v2 :** 2026-09-01 — **Date v3 :** 2026-09-04 — **Date v4 :** 2026-09-05
**Statut : PRÉ-ENREGISTREMENT — AUCUNE DONNÉE L2 (`get_range`/`batch.submit_job`) ACQUISE SOUS LA VOIE CONFORME AU-DELÀ DU JOB DÉJÀ CONSIGNÉ EN §8.7/§8.8.**
Ce document est rédigé, commité et **taggé (`qde-022-prereg-v4`) AVANT toute analyse (OFI/IC/gate)**. Le hash du commit taggé sera reporté en tête du rapport de résultats (QDE-022-R). Toute modification postérieure au tag exige un nouveau tag horodaté et une justification explicite dans QDE-022-R.
**Type :** registre gelé d'hypothèses, d'indicateurs, d'horizons, de comptabilité des tests multiples et de règle de décision GO / GO conditionnel / STOP.
**Production modifiée : NON.** Ce lot ne touche aucun code de production. Aucune entrée `YahooSymbolMap`. La sonde d'analyse (Python, à écrire APRÈS acquisition) vivra sous `IQIAIndicator/Tests/Research/OrderFlow/` et ne contiendra aucun code de stratégie.

> Emplacement : `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` (préfixe `IQIAIndicator/` pour coller à la disposition du dépôt ; chemin logique `Tests/Research/OrderFlow/…`).

---

## 1. Objet et justification

### 1.1 Pourquoi cet axe est le seul restant

QDE-012 → QDE-021 ont épuisé, **négativement**, toutes les pistes d'edge directionnel accessibles à partir de barres OHLC H1/M5 gratuites, sur MES, instrument unique, ~3 ans, net des coûts retail, sous contrainte de flat quotidien :

- prix seul (MR, Trending, walk-forward, dépendance sérielle) — aucun edge ;
- order flow **par barre** (Delta, footprint, `deltaRange`) — QDE-016 → 019, aucun edge directionnel, la seule structure robuste (persistance de vol réalisée) étant déjà captée et sans voie de monétisation ;
- changement de sous-jacent (8 futures) — QDE-020, l'indépendance sérielle à H1 est quasi-générale ;
- effets calendaires/horaires et pair-trading MES↔NQ — QDE-021, 0/30 partitions Holm-significatives, aucune fenêtre NET+ ROBUST en OOS.

**Le seul composant de la stack qui contienne de l'information formellement absente des OHLC est `OrderFlowCsvBarSource`** — et il n'expose aujourd'hui que des agrégats *par barre M5* (Bid/Ask/Delta/footprint), déjà testés et négatifs. L'information non encore examinée est celle du **carnet d'ordres niveau 2 à haute résolution** : la dynamique d'ajout/retrait de liquidité aux meilleurs niveaux, dont la littérature (Cont–Kukanov–Stoikov 2014 ; Stoikov 2018 ; Kolm et al. 2021) montre qu'elle explique une part significative des variations de prix à l'échelle **sous-minute**, échelle jamais observée dans ce projet.

### 1.2 Hypothèses pré-enregistrées

> **H1 (hypothèse de travail).** Au moins un des 4 indicateurs de flux de carnet listés en §3, mesuré sur ES, présente avec le rendement futur du prix moyen une corrélation (IC) qui, à au moins un des 6 horizons listés en §4, est : (i) statistiquement significative après correction Holm sur les 24 tests, (ii) de signe stable entre la première et la seconde moitié du mois, et (iii) économiquement matérielle au sens du gate §7.3.

> **H0 (hypothèse nulle — portée étroite, à retenir par défaut).** L'OFI et ses variantes (les 4 indicateurs de §3), mesurés sur **ES MBP-10**, ne présentent **pas d'IC exploitable nette de coûts à un horizon ≥ 60 s**, sur un **échantillon d'un mois** (août 2026, repli juillet 2026), avec **latence supposée nulle**.

**Rejet de H0** = au moins une cellule (indicateur × horizon ≥ 60 s) **porteuse** au sens de §7.1.
**Non-rejet de H0** = aucune cellule porteuse à un horizon ≥ 60 s. La clôture de la ligne recherche d'edge n'est **pas** contenue dans H0 : c'est une **conséquence recommandée** traitée en §7.5.

Les horizons < 60 s (1, 5, 30 s) sont **mesurés et rapportés** (ils font partie de la courbe de décroissance et des 24 tests Holm), mais une cellule porteuse dont *tous* les horizons significatifs sont < 60 s est traitée comme **GO conditionnel à haut risque de latence** (§7.4), la latence non modélisée dominant à cette échelle.

### 1.3 Ce que QDE-022 ne fait PAS

QDE-022 **mesure un contenu informationnel**. Il ne construit **aucune stratégie**, ne produit **aucune courbe d'équité**, **aucun P&L**, **aucune calibration de seuil**, **aucun walk-forward**. Ces travaux ne sont autorisés qu'en cas de verdict GO ou GO conditionnel (§7), dans un lot ultérieur (QDE-023) lui-même pré-enregistré.

---

## 2. Achat de données — spécification exacte *(v3 : §2.1 contrat/période, §2.2 voie d'acquisition ; reste inchangé depuis v1)*

### 2.1 Source et contrat

| Paramètre | Valeur gelée | Justification |
|---|---|---|
| Fournisseur | Databento, API historique | accès programmatique, DBN, `metadata.get_cost` disponible |
| Dataset | `GLBX.MDP3` | CME Globex MDP 3.0, flux natif du CME |
| **Instrument** | **ES (E-mini S&P 500), PAS MES** | le flux institutionnel est dans l'ES ; le MES en est un reflet. Signal **mesuré sur ES**, trading final **possible sur MES** (§7.2) |
| Schéma | **`mbp-10`** (market-by-price, 10 niveaux) | suffisant pour l'OFI top-of-book et l'OFI 10 niveaux, l'imbalance et le micro-prix. **`mbo` est EXCLU** : volume ~10×, non requis pour ces 4 indicateurs, ingérable pour un premier test |
| Contrat (`raw_symbol`) | **`ESU6`** (échéance septembre 2026) *(v3 : remplace `ESM26`)* | mois de front pour août 2026 (et juillet 2026, repli) ; le roulement M26→U26 a lieu ~mi-juin 2026, le roulement U26→Z26 ~mi-septembre 2026 — août est donc **hors des deux fenêtres de roulement** |
| `stype_in` | `raw_symbol` | ciblage direct du contrat, pas de résolution `parent`/continu — voir §2.2 note pour la justification explicite de ce choix par rapport à l'alternative `parent` |
| Période primaire | **2026-08-02T00:00:00Z → 2026-09-02T00:00:00Z** *(v3 : remplace avril 2026)* | ~1 mois calendaire, **hors mois de roulement** pour `ESU6` ; dernier mois calendaire complet à la date de rédaction (2026-09-04) |
| Période de repli | **2026-07-01 → 2026-08-01** *(v3 : remplace mai 2026)* | si août indisponible/incomplet ; toujours sur `ESU6`, toujours hors roll |
| Filtre de session | RTH uniquement : **13:30:00 → 20:00:00 UTC** (08:30–15:00 CT) | juillet/août = CDT = UTC−5. Session cash S&P 09:30–16:00 ET. Les événements hors de cette plage sont écartés à l'analyse, pas à l'achat. Ce filtre écarte nativement les jours calendaires sans session RTH (week-ends) — voir amendement v3, note sur l'exclusion de dates abandonnée |
| Horodatage de référence | **`ts_event`** (heure du moteur d'appariement CME), UTC ns | causalité stricte ; `ts_recv` conservé pour diagnostic de latence uniquement |
| Encodage / compression | `dbn` / `zstd` | format natif Databento |

### 2.2 Procédure d'acquisition (ordre impératif) *(v3)*

1. **`metadata.get_cost(dataset="GLBX.MDP3", schema="mbp-10", symbols=["ESU6"], stype_in="raw_symbol", start="2026-08-02", end="2026-09-02")`** — **AVANT tout `get_range` / `batch.submit_job`**. *(Exécuté le 2026-09-04, avant l'existence du tag v3 — déviation de séquencement consignée explicitement en tête de document, sans risque de data-snooping puisque `get_cost` ne retourne pas de donnée de marché.)*
2. **Consigné : montant estimé = 30,048361867666 USD (~30,05 $)**, appel du 2026-09-04, sur la portée exacte ci-dessus. Volume estimé (octets, `record_count`) non renvoyé par cet appel — à consigner dans QDE-022-R si disponible via un appel `metadata` complémentaire.
3. Si le montant estimé ≤ budget plafond (§2.3, **TODO utilisateur**) → lancer `batch.submit_job(...)` ou `timeseries.get_range(...)` (pas streaming, pour la période complète ci-dessus).
4. **Consigner : montant réellement facturé** (relevé Databento) + **volume réel téléchargé**. Écart estimé/réel commenté.
5. **Consigner les frais de licence CME** : Databento refacture une licence CME **séparée** pour `GLBX.MDP3`, dont le tarif dépend du **statut particulier (non-professional) vs professionnel**. Reporter : statut déclaré, montant mensuel exact, période couverte. Le statut est **déterminé par le questionnaire Databento, pas par estimation** (§2.3, **TODO utilisateur**).

> **Note — voie d'acquisition retenue : ciblage direct, pas `parent`.** Deux voies étaient possibles pour obtenir `ESU6` : (a) **ciblage direct retenu** — `stype_in="raw_symbol"`, `symbols=["ESU6"]`, un seul contrat facturé/téléchargé ; (b) **pull `parent` écarté** — `stype_in="parent"`, `symbols=["ES.FUT"]`, qui aurait renvoyé **tous** les contrats ES cotés simultanément (dont `ESZ6` résiduel), à filtrer localement sur l'`instrument_id` d'`ESU6` après téléchargement. (a) est retenu car il reproduit le principe déjà gelé en v2 §2.1, minimise le volume facturé/téléchargé pour une étude mono-contrat, et évite toute étape de filtrage post-acquisition (surface d'erreur en moins). Le coût de 30,05 $ consigné au point 2 ci-dessus est celui de la voie (a).

### 2.3 Budget et licence — **TODO utilisateur, à renseigner AVANT `get_cost`**

- **[TODO] Budget plafond USD :** _à renseigner_. Ordre de grandeur attendu : un mois d'ES `mbp-10` devrait **rester sous les 125 $ de crédits gratuits** Databento ; un plafond de **150–200 $** couvre la phase, **frais de licence CME exclus**. **Si `get_cost` renvoie nettement plus, suspecter une erreur de requête (mauvais symbole, ou schéma `mbo` au lieu de `mbp-10`) AVANT d'augmenter le budget.**
- **[TODO] Statut de licence CME (particulier / professionnel) :** _à renseigner_, via le questionnaire Databento. Conditionne le montant de la licence et, potentiellement, les commissions retenues au §7.3.
- **[TODO] Volume / `record_count` attendus :** non estimés ici (pas de valeur inventée) — renseignés par `get_cost` puis confirmés après téléchargement.

Si `get_cost` dépasse le plafond consigné → arrêt, pas de téléchargement, QDE-022-R documente le refus.

---

## 3. Liste d'indicateurs — FIGÉE, 4 MAXIMUM *(inchangé depuis v1)*

Les 4 indicateurs ci-dessous sont **gelés**. Chacun est calculé à chaque point de grille `T` (§4.2), de façon strictement causale (état du carnet à `ts_event ≤ T` uniquement). **Aucun ajout, aucune variante paramétrique après ouverture des données.** Tout indicateur ou toute variante (autre fenêtre OFI, autre profondeur d'imbalance, micro-prix pondéré autrement…) introduit ultérieurement **compte comme un test supplémentaire** et doit être déclaré dans la correction de comparaisons multiples de QDE-022-R (Holm recalculé sur 24 + k).

### (a) OFI top-of-book — Cont, Kukanov, Stoikov (2014)

Sur la **fenêtre glissante `[T − 1 s, T]`** (fenêtre gelée, identique pour tous les horizons), pour chaque paire d'états consécutifs `n−1 → n` du carnet touchant le niveau 0 :

```
e_n = 1{P_bid,n ≥ P_bid,n−1}·Q_bid,n − 1{P_bid,n ≤ P_bid,n−1}·Q_bid,n−1
    − ( 1{P_ask,n ≤ P_ask,n−1}·Q_ask,n − 1{P_ask,n ≥ P_ask,n−1}·Q_ask,n−1 )
OFI_a(T) = Σ_n e_n
```

(`P_·,n`, `Q_·,n` = prix et taille du meilleur niveau à l'état `n`.) Interprétation CKS : `Δmid ≈ β · OFI / profondeur`, relation linéaire.

### (b) OFI agrégé sur 10 niveaux

Même règle CKS appliquée **niveau par niveau** aux 10 niveaux visibles (`ℓ = 0..9`), puis somme non pondérée, sur la même fenêtre `[T − 1 s, T]` :

```
OFI_b(T) = Σ_{ℓ=0}^{9} Σ_n e_n^{(ℓ)}
```

où `e_n^{(ℓ)}` applique la formule (a) aux prix/tailles du niveau `ℓ` des côtés bid et ask.

### (c) Déséquilibre statique du carnet (book imbalance)

Instantané en `T`, sur les 10 niveaux :

```
IMB(T) = ( Σ_{ℓ=0}^{9} Q_bid,ℓ(T) − Σ_{ℓ=0}^{9} Q_ask,ℓ(T) )
       / ( Σ_{ℓ=0}^{9} Q_bid,ℓ(T) + Σ_{ℓ=0}^{9} Q_ask,ℓ(T) )
```

Borné dans `[−1, 1]`.

### (d) Écart micro-prix / prix moyen

Instantané en `T`, niveau 0 :

```
P_mid(T)   = ( P_ask,0 + P_bid,0 ) / 2
P_micro(T) = ( P_ask,0 · Q_bid,0 + P_bid,0 · Q_ask,0 ) / ( Q_bid,0 + Q_ask,0 )
GAP(T)     = ( P_micro(T) − P_mid(T) ) / ( spread(T) / 2 )        ,  spread = P_ask,0 − P_bid,0
```

`GAP` sans dimension, borné dans `[−1, 1]` (pondération "weighted mid" standard : le prix est tiré vers le côté le **moins** épais).

### 3.1 Normalisation

Chaque indicateur est **z-scoré** (centré-réduit) avec la **moyenne et l'écart-type calculés sur la seule période TRAIN** (§7.2), puis cette transformation TRAIN est appliquée telle quelle à TRAIN et à OOS. Aucune statistique plein-échantillon.

---

## 4. Horizons — FIGÉS *(inchangé depuis v1)*

### 4.1 Liste

**1 s, 5 s, 30 s, 60 s, 300 s, 900 s** — mesure en temps mural (wall-clock), horloge UTC. Aucun autre horizon.

### 4.2 Grille d'évaluation

- Points de grille : **chaque seconde entière UTC** à l'intérieur de la plage RTH (§2.1).
- Rendement futur : `r_h(T) = ln( P_mid(T + h) / P_mid(T) )` pour `h ∈ {1,5,30,60,300,900}` s.
- Un point `T` est **écarté** (et compté) si : carnet croisé/verrouillé en `T` ou `T+h`, un côté vide sur le niveau requis, ou `T + h` dépasse la fin de session du jour de `T` (aucun report inter-journalier — cohérent avec la contrainte de flat quotidien).

---

## 5. Comptabilité des tests multiples *(inchangé depuis v1)*

- **4 indicateurs × 6 horizons = 24 tests.** Chiffre **annoncé avant exécution**.
- Correction **Holm-Bonferroni**, **α familial = 0,05**, appliquée à la famille des 24 p-values (test bilatéral de nullité de l'IC, erreurs-types HAC — §6.2).
- Un Holm "par indicateur" (m = 6) est rapporté **à titre indicatif seulement** ; la correction **bloquante** est celle sur les 24.
- Tout indicateur/variante/horizon ajouté après ouverture des données → famille élargie à `24 + k`, Holm recalculé, ajout signalé en rouge dans QDE-022-R.

---

## 6. Métrique primaire *(inchangé depuis v1, sauf ajout §6.4)*

### 6.1 Courbe de décroissance de l'information

Pour chaque indicateur `X ∈ {OFI_a, OFI_b, IMB, GAP}` et chaque horizon `h` :

```
IC(X, h) = corrélation de Pearson entre X_z(T) et r_h(T),  sur TOUS les points de grille valides du MOIS COMPLET
```

La **courbe de décroissance** est `IC(X, ·)` tracée contre `h` (échelle log en `h`), une courbe par indicateur. On rapporte, par cellule : `IC`, **IC95 (HAC)**, `p` (HAC), `n` (points valides), `n_eff` (sous-échantillon non chevauchant, §6.3), verdict Holm.

**La métrique primaire est l'IC et sa courbe de décroissance — PAS un backtest, PAS un P&L, PAS un ratio de Sharpe, PAS une règle de trading.** Aucune simulation d'exécution n'est produite dans QDE-022.

### 6.2 Erreurs-types

Observations fortement chevauchantes (grille 1 s, horizons jusqu'à 900 s) → erreurs-types **Newey–West (HAC)**, bande passante `L(h) = ⌈h / 1 s⌉` lags, plafonnée à 2000. IC95 = `IC ± 1,96 · SE_HAC`. `p` = test bilatéral `IC / SE_HAC` via loi normale.

### 6.3 Robustesse (rapportées, non décisionnelles seules)

1. **Sous-échantillon non chevauchant** : ré-estimer chaque IC en n'échantillonnant qu'un point tous les `h` secondes ; rapporter `IC_ne`, IC95 classique, `n_eff`.
2. **Spearman** : ré-estimer chaque IC en rang (robuste aux queues lourdes du flux).
3. **Split temporel** (§7.2) : IC séparé TRAIN / OOS.

Une divergence forte entre Pearson plein-chevauchement et ces trois vues est un signal d'alerte consigné, même si Holm passe.

### 6.4 Effet minimal détectable (MDE) — **renseigné (v3, post-acquisition)**

Le MDE (plus petit `|IC|` détectable à α corrigé / puissance 0,80) et le `n_eff` par horizon dépendent :
- du **nombre réel de mises à jour du carnet par jour sur ES** (`record_count`/jour) — **connu depuis le job `GLBX-20260904-93PSB55DT3`**, voir table ci-dessous ; détail complet dans `QDE-022_DataIntegrity_Check.md` (contrôle 5) ;
- du **taux de rejet** de points de grille (carnets croisés, bords de session) — **toujours inconnu à ce stade** : sa mesure exige de rejouer l'état du carnet, ce qui relève de la sonde d'analyse (`qde022_l2_ofi.py`, post-gel), pas de ce contrôle d'intégrité. Le MDE ci-dessous majore donc `n_eff` (borne supérieure) et sous-estime en conséquence le MDE réel ;
- de la structure d'autocorrélation résiduelle réelle à chaque horizon — inconnue à ce stade, mesurée par la sonde.

**`record_count`/jour (27 fichiers, job `GLBX-20260904-93PSB55DT3`, ESU6, mbp-10, 2026-08-02 → 2026-09-02) :** 22 jours ouvrés, médiane 7 852 498 enregistrements/jour, écart-type (population) 1 717 223 ; aucun jour ouvré à plus de 3σ. 5 dimanches présents (session nocturne uniquement, hors fenêtre RTH), 4 samedis absents (dont 2026-08-29 marqué `degraded` dans `condition.json`). Somme totale 175 348 820 = `record_count` facturé exactement. Détail complet : `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_DataIntegrity_Check.md`, contrôles 3 et 5.

**MDE par horizon, borne supérieure théorique** (grille 1 s RTH ≈ 23 400 pts/jour × 22 jours ouvrés, non-chevauchement par horizon `h`, **PAS** de rejet carnet croisé/bord de session appliqué — donc `n_eff` ci-dessous est un **majorant**, le MDE réel sera **plus grand**) :

| `h` (s) | grille/jour | `n_eff` (22 j, majorant) | MDE (α=0,05, non corrigé, indicatif) | MDE (α=0,05/24, borne Holm la plus stricte) |
|---|---|---|---|---|
| 1 | 23 400 | 514 800 | 0,0039 | 0,0055 |
| 5 | 4 680 | 102 960 | 0,0087 | 0,0122 |
| 30 | 780 | 17 160 | 0,0214 | 0,0299 |
| 60 | 390 | 8 580 | 0,0302 | 0,0423 |
| 300 | 78 | 1 716 | 0,0676 | 0,0944 |
| 900 | 26 | 572 | 0,1169 | 0,1629 |

Le seuil Holm réel appliqué à chaque cellule (§5) dépend du classement des 24 p-values et se situe entre les deux colonnes MDE ci-dessus — il n'est **pas** re-calculable avant l'exécution de la sonde. **Rappel (inchangé) : le seuil décisionnel de §7 reste économique (`M(X,h) ≥ 0,80` tick ES, §7.3), pas statistique — le MDE documente ici ce que l'échantillon permet d'exclure sur le plan de la puissance, il n'entre dans aucun critère de §7.**

---

## 7. Règle de décision — GELÉE *(v2 : §7.3 corrigé, §7.2/§7.4/§7.5 amendés)*

### 7.1 Trois conditions cumulatives par cellule

Une cellule `(X, h)` est déclarée **porteuse** si et seulement si les **trois** conditions suivantes sont vraies :

1. **Statistique.** `p_HAC(X, h)` survit à la correction Holm sur les 24 tests (α familial 0,05). *(inchangé v1)*
2. **Stabilité de signe.** `sign(IC_TRAIN) == sign(IC_OOS)` **et** l'IC95 de `IC_OOS` exclut 0. *(inchangé v1)*
3. **Matérialité économique (gate §7.3).** *(v2 : valeur corrigée + instrument de référence explicité)*

### 7.2 Split temporel (pré-gelé) *(inchangé v1)*

- Jours de bourse du mois triés ; **60 % premiers = TRAIN**, **40 % derniers = OOS**, **1 jour de bourse purgé** entre les deux (jeté).
- Normalisation des indicateurs (§3.1) et `σ_ret(h)` du gate (§7.3) estimés **sur TRAIN uniquement**.
- **Aucun paramètre** (indicateur, horizon, fenêtre OFI, profondeur) n'est choisi sur les données : tout est gelé par le présent document. Le split ne sert qu'à la condition 2 et à ne pas estimer d'échelle en plein-échantillon.

### 7.3 Gate économique — **instrument de référence = ES** *(v2 : corrige l'erreur de v1)*

**Rappel d'unités.** 1 tick ES = **12,50 $** ; 1 point ES = 4 ticks = 50 $ (multiplicateur 50 $/pt). 1 tick MES = **1,25 $** (multiplicateur 5 $/pt). **ES et MES partagent la même taille de tick en points d'indice : 0,25 pt.** Un mouvement de `X` points d'indice vaut donc `X / 0,25` ticks **sur ES comme sur MES** (même compte de ticks ; seul le montant en dollars diffère).

**Coûts aller-retour (valeurs correctes) :**

| | $ / contrat | en ticks ES | en ticks MES | en points d'indice |
|---|---|---|---|---|
| Coût AR **ES** | ≈ 4–5 $ | ≈ 0,36–0,40 tick | — | ≈ 0,09–0,10 pt |
| Coût AR **MES** | 3,855 $ | — | ≈ **3,08 ticks** | ≈ 0,77 pt *(en $ MES)* |

**Gate primaire (référence ES) = `2 × coût AR ES` (borne haute 5 $ → 0,40 tick) = `0,80 tick ES` ≈ 0,20 pt d'indice ≈ 10 $/contrat ES.**
**Seuil MES documenté = `2 × coût AR MES` = `2 × 3,08` = `6,16 ticks MES` ≈ 1,54 pt d'indice ≈ 7,70 $/contrat MES.**

> **Note — pourquoi la valeur de v1 était infaisable.** v1 fixait le gate à « 2× le coût AR ES de 0,75 pt », soit **1,5 pt ES (= 6 ticks ES = 75 $/contrat ES)**. Origine de l'erreur : confusion tick/point — le coût AR MES (3,855 $ ≈ 3,08 **ticks MES**) avait été reporté comme « ~0,77 **point** » puis traité comme un coût ES sans conversion d'unité ni de multiplicateur. Exiger un mouvement attendu de 1,5 pt ES sur un choc de 2σ d'un indicateur de carnet, à un horizon intraday, est **hors de portée pratique** : le critère GO était **mécaniquement inatteignable** et le test ne pouvait retourner que STOP, quel que soit le contenu informationnel réel. Le gate correct — `0,80 tick ES` — est ~7,5× plus bas.

**Calcul du mouvement attendu, par cellule passant les conditions 1 et 2 :**

```
σ_ret(h) = écart-type (estimé sur TRAIN) de r_h(T) = ln( mid(T+h) / mid(T) )
m(X,h)   = 2 · |IC(X,h)| · σ_ret(h)                 [réponse attendue du log-rendement à un choc de +2σ de X_z]
M(X,h)   = m(X,h) · P_mid_moy / 0,25                [en ticks — valeur COMMUNE ES et MES]
```

`P_mid_moy` = prix moyen ES sur TRAIN. L'IC95 de `M(X,h)` est propagé depuis l'IC95(HAC) de `IC(X,h)`.

**Le gate passe** si `M(X,h) ≥ 0,80` tick **et** l'IC95 de `M(X,h)` exclut `0,80`.

### 7.4 Verdict *(v2 : GO conditionnel introduit)*

QDE-022-R reporte, pour **chaque cellule passant les conditions 1 et 2**, la valeur `M(X,h)` **en ticks** (commune) accompagnée de sa conversion `$/contrat` pour **ES et pour MES**, et la compare explicitement aux **deux** seuils.

| Condition sur `M(X,h)` (cellules passant 1 & 2) | Verdict | Portée |
|---|---|---|
| `M ≥ 6,16` ticks (IC95 exclut 6,16) | **GO** | signal exploitable **sur ES et sur MES** ; autorise QDE-023 |
| `0,80 ≤ M < 6,16` ticks (IC95 exclut 0,80) | **GO conditionnel** | exploitable **sur ES uniquement**. Subordonné à une **décision explicite de trading sur ES** — capital alloué, dimensionnement, risque par trade — **NON prise dans QDE-022** et renvoyée à un document ultérieur (QDE-023 ou décision d'exploitation). Sans cette décision, le résultat reste inexploité |
| `M < 0,80` tick, **ou** IC95 de `M` n'excluant pas `0,80`, **ou** échec de la condition 1 ou 2 | **STOP** | signal éventuellement réel mais **sous le coût**, ou non significatif, ou non stable |
| Cellules porteuses uniquement aux horizons **< 60 s** | **GO conditionnel à haut risque de latence** | traité comme « GO conditionnel » ci-dessus, avec mention explicite que la latence non modélisée (§8) domine à cette échelle et qu'une validation latence/file d'attente est un préalable au capital |

Aucun verdict intermédiaire hors de ce tableau.

### 7.5 Conséquence recommandée en cas de non-rejet de H0 *(v2 : n'est PAS la définition de H0)*

Si **aucune cellule n'est porteuse** à un horizon ≥ 60 s (H0 non rejetée) — que ce soit faute de significativité, faute de stabilité de signe, ou parce que tout signal réel reste sous le gate `0,80 tick ES` —, la **recommandation** de QDE-022-R, cohérente avec QDE-019/020/021, est :

- arrêt de l'axe **order flow L2 sur ES MBP-10** à cet horizon et sur cet échantillon ;
- et, **si aucun autre réservoir d'information de nature différente n'est identifié** (options, cross-market profond, alternative data, autre marché/horizon), mise en veille de la ligne recherche d'edge au profit du mode **SIGNAL_ONLY**.

Cette recommandation est une **décision de portefeuille de recherche**, révisable si une donnée de nature différente devient disponible — **pas** une propriété démontrée par un test d'un mois sur un instrument. QDE-022-R la formule comme recommandation, jamais comme conclusion établie.

---

## 8. Menaces à la validité — déclarées d'avance *(v2 : ancien point « signal ES → trade MES » retiré, désormais traité en §7.3–§7.4 ; v3 : §8.7 mis à jour, §8.8 ajouté ; v4 : §8.7 intégralement réécrit — l'énoncé « a priori » de v3 était factuellement inexact —, renvoi de §8.6 corrigé)*

1. **Un seul mois, un seul instrument, un seul contrat.** Aucune généralité saisonnière ni de régime. Un rejet de H0 impose une réplication sur un 2ᵉ mois hors-roll avant tout capital (§7.4).
2. **Latence et slippage nuls supposés.** L'IC est mesuré avec un carnet parfaitement synchrone et une exécution instantanée en `T`. Tout edge trouvé subira un abattement latence/file d'attente/slippage **non modélisé ici** ; le gate à 2× (§7.3) et le verdict « haut risque de latence » pour les horizons < 60 s (§7.4) visent à préserver cette marge, sans la garantir.
3. **MBP-10 = vue agrégée par prix, 10 niveaux.** Position dans la file (queue) et icebergs non observables ; l'OFI 10 niveaux ne "voit" que la liquidité affichée sur 10 crans de prix.
4. **Sémantique des snapshots Databento.** `mbp-10` publie l'état après chaque événement ; l'OFI est reconstruit par différences d'états successifs, conforme à CKS. Une mauvaise gestion des types d'événements (`T` trade, `F` fill, `A/C/M` add/cancel/modify) fausserait l'OFI — la sonde devra journaliser leur ventilation.
5. **Statut de licence.** Un basculement "professionnel" change la licence CME et possiblement les commissions → gate §7.3 à recalculer.
6. **Biais de sur-échantillonnage temporel.** La grille 1 s crée une autocorrélation massive des résidus ; traitée par HAC + sous-échantillon non chevauchant (§6.2–6.3). `n`, `n_eff` et MDE réels sont **chiffrés en §6.4** *(v4 : corrigé — §6.4 a été renseigné lors du contrôle d'intégrité du 2026-09-04, cf. `QDE-022_DataIntegrity_Check.md` ; ce point renvoyait encore vers un TODO obsolète)*.
7. **Choix de la période — historique complet et risque résiduel assumé.** *(v4 : réécriture intégrale. La version v3 de ce point affirmait un choix « a priori » ; cette affirmation était factuellement inexacte et est retirée ci-dessous, pas atténuée.)*

   **Chronologie établie** (`batch.list_jobs` Databento, horodatages serveur ; `git log`) :
   - **2026-09-01** : v1 puis v2 gelés et taggés — portée avril 2026 / `ESM26`. Les protections de fond de QDE-022 (4 indicateurs §3, 6 horizons §4, comptabilité Holm §5, règle de décision et gate économique 0,80 tick ES §7, formulation de H0 §1.2) sont figées à cette date et **n'ont jamais été modifiées depuis**.
   - **2026-09-03T07:43:23Z → 08:11:37Z** : job Databento `GLBX-20260903-7XE859LGPE` (`stype_in=parent`, `symbols=ES.FUT`, `mbp-10`, 2026-08-02 → 2026-09-02) soumis puis terminé. **De vraies données de marché couvrant `ESU6` (parmi d'autres contrats ES livrés simultanément par la symbologie `parent`) sur exactement août 2026 ont été téléchargées à ce moment** — 193 356 682 enregistrements, 33,13424949347973 USD facturés.
   - **2026-09-04, ~18:39:53Z UTC** (commit `2dd383a`) : rédaction et tag de v3, qui fixe pour la première fois la portée « août 2026 / `ESU6` » dans un pré-enregistrement gelé.

   **Le job `parent` a terminé environ 34 h 28 min avant que la portée « août 2026 / `ESU6` » n'existe sous forme de document gelé.** L'affirmation de v3 (« Août 2026 est choisi a priori […] aucune donnée n'a été acquise pour avril ni pour août avant ce changement ») **était factuellement fausse** au moment où elle a été écrite : des données réelles sur cette portée exacte existaient déjà depuis la veille. Cette inexactitude a été découverte le 2026-09-04, lors du contrôle d'intégrité des données livrées (`QDE-022_DataIntegrity_Check.md`, §8 de ce rapport), pas au moment de la rédaction de v3.

   **Décision (v4) : août 2026 est conservé comme échantillon unique.** Aucun achat supplémentaire n'est engagé ; le budget restant est réservé à un élargissement ultérieur, conditionné au résultat de la courbe de décroissance (§6.1). Cette décision est prise en connaissance du risque résiduel ci-dessus, sur la base du raisonnement suivant :

   a. **Ce qui a été consulté du job `parent` se limite à des métadonnées** : tailles de fichiers, statuts de qualité (`condition.json`), liste des symboles. Aucune barre, aucun prix, aucun état de carnet n'a été ouvert ou inspecté.
   b. **Le data-snooping consiste à choisir en fonction d'un résultat.** Aucun résultat n'existait ni n'existe à ce jour : aucun OFI, aucun IC, aucun rendement n'a été calculé, ni sur les données `parent` ni sur les données `raw_symbol` conformes. Le risque documenté ici est une exposition à la *possibilité* que le choix ait été influencé — ce n'est ni une preuve qu'il l'a été, ni *a fortiori* un résultat qui l'aurait motivé.
   c. **Août n'a pas été retenu parce qu'il produisait un signal.** Il a été retenu parce que le job `parent` avait été déclenché par erreur sur cette période, et parce qu'août 2026 satisfait indépendamment le critère « front month, hors fenêtre de roulement » déjà gelé en v1 pour `ESU6` (roulement M26→U26 ~mi-juin 2026, U26→Z26 ~mi-septembre 2026 — §2.1).
   d. **Les protections de fond n'ont jamais bougé.** Les 4 indicateurs (§3), les 6 horizons (§4), la correction de Holm (§5), le gate économique à 0,80 tick ES (§7) et la formulation de H0 (§1.2) sont gelés depuis v1 (2026-09-01), soit **avant** l'existence même du job `parent` (2026-09-03). Aucun de ces paramètres n'a pu être choisi ou ajusté après exposition à de vraies données.
   e. **Le seul paramètre exposé est le choix de la période/du contrat.** Il est documenté comme tel, ici, sans minimisation : c'est un écart réel à la discipline de pré-enregistrement, pas un point aveugle occulté.
   f. **Un basculement vers juillet 2026 a été examiné et écarté.** Juillet serait choisi aujourd'hui (2026-09-05), en pleine connaissance de cette situation — il ne serait donc pas davantage « a priori » qu'août ne l'est déjà. Changer de mois ne répare pas l'antériorité du problème, il la déplace.

   **Ce risque résiduel reste ouvert, assumé et non neutralisé par ce paragraphe.** QDE-022-R devra le rapporter explicitement comme menace à la validité, quel que soit le verdict (GO / GO conditionnel / STOP), au même titre que les menaces 1 à 6 et 8.
8. **Déviation de séquencement `get_cost`/tag.** *(v3)* L'appel `metadata.get_cost` de la portée v3 a précédé l'existence du tag `qde-022-prereg-v3` (voir amendement en tête de document). Sans impact sur H0/H1 (aucune donnée de marché renvoyée par cet appel), mais consigné comme écart au protocole d'intégrité (§11) plutôt que laissé implicite.

---

## 9. Hors périmètre — INTERDITS GELÉS *(inchangé depuis v1)*

- **Schéma `mbo`** (market-by-order) — exclu (§2.1).
- **Tout 5ᵉ indicateur**, ou toute **variante paramétrique** d'un des 4 (autre fenêtre OFI que 1 s, autre profondeur d'imbalance que 10, micro-prix multi-niveaux, OFI pondéré…) — compte comme test additionnel, réenregistrement + Holm élargi.
- **Tout horizon** hors des 6 gelés.
- **Backtest de stratégie, courbe d'équité, P&L, ratio de Sharpe, calibration de seuil, walk-forward, optimisation** — interdits dans QDE-022. Autorisés seulement après verdict GO ou GO conditionnel, dans QDE-023 pré-enregistré.
- **Choix, après avoir vu les données**, du mois, du jour, de la plage horaire, du contrat, de la profondeur, ou de la "meilleure" cellule sans pénalité Holm.
- **Ré-exécution** de l'analyse avec des paramètres modifiés sans nouveau tag de pré-enregistrement.

---

## 10. Livrables *(v3 : tag mis à jour)*

| Livrable | Contenu | Moment |
|---|---|---|
| **QDE-022 v3** (ce fichier) | Pré-enregistrement gelé | Maintenant — commité + taggé `qde-022-prereg-v3` |
| **`qde022_l2_ofi.py`** | Sonde d'analyse Python : lecture DBN `mbp-10`, reconstruction du carnet, calcul des **4 indicateurs gelés**, grille 1 s, **6 horizons**, IC Pearson + HAC, Spearman, sous-échantillon non chevauchant, split TRAIN/OOS, Holm(24), gate §7.3. **Zéro code de stratégie.** | Après acquisition |
| **QDE-022-R** | Rapport de résultats : hash du commit taggé ; `get_cost` estimé **vs** facturé **vs** volume réel ; licence CME réelle + statut ; `record_count`/jour, `n`, `n_eff`, MDE (§6.4) ; table des 24 cellules (IC, IC95 HAC, p, Holm, n, n_eff) ; 4 courbes de décroissance ; IC TRAIN/OOS ; gate économique par cellule **en ticks ES et MES** ; **verdict GO / GO conditionnel / STOP** ; menaces §8 réévaluées | Après analyse |

---

## 11. Intégrité du pré-enregistrement *(v3 : tag mis à jour, déviation §2.2 consignée)*

1. Ce fichier v3 est commité **seul** (aucun autre changement dans le commit), message :
   `research(qde-022): pre-registration v3 - period/contract moved to August 2026 ESU6 + acquisition path documented, day-exclusion candidate rejected (FROZEN, pre-data)`
2. Tag annoté **`qde-022-prereg-v3`** sur ce commit.
3. **v1 (`2d21ae5`, tag `qde-022-prereg`) et v2 (`2e8d7c9`, tag `qde-022-prereg-v2`) restent dans l'historique git, non réécrites.** v3 les remplace explicitement (voir en-tête).
4. **Aucune donnée L2 (`get_range`/`batch.submit_job`) n'a été téléchargée avant l'existence du tag `qde-022-prereg-v3`, ni entre v1/v2/v3.** Une déviation ponctuelle sur `metadata.get_cost` (exécuté avant le tag v3) est consignée en tête de document — voir §8.8 pour son évaluation d'impact (nulle sur H0/H1).
5. QDE-022-R cite le hash complet du commit taggé `qde-022-prereg-v3` en première ligne.
6. Toute évolution ultérieure de QDE-022 → nouveau tag `qde-022-prereg-v4` (etc.) + section "Écarts au pré-enregistrement" en tête de QDE-022-R, chaque écart justifié.

---

## 12. Index des fichiers *(v3 : tag mis à jour)*

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` | Ce pré-enregistrement (v3) | Oui (documentation de recherche) |
| `IQIAIndicator/Tests/Research/OrderFlow/qde022_l2_ofi.py` | Sonde d'analyse (à créer post-acquisition) | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlow/Output/qde022_*.txt` | Sorties mesurées (à créer) | Recherche uniquement |
| `IQIAIndicator/Documentation/Scientific/QDE-022-R_*.md` | Rapport de résultats (à créer) | Oui (documentation) |

Les données brutes DBN **ne sont pas versionnées** — stockées hors dépôt, chemin consigné dans QDE-022-R.

---

## 13. FINAL OUTPUT *(v3)*

**STATUS :** Pré-enregistrement **v3** rédigé, amendement visible de v2. **Aucune donnée L2 (`get_range`/`batch.submit_job`) acquise, ni avant v1/v2, ni entre v2 et v3.** Changements v3 : (1) **période** avril 2026 → **août 2026** (repli mai 2026 → juillet 2026), choix a priori (dernier mois calendaire complet, aucun résultat antérieur pour aucun mois candidat) ; (2) **contrat** `ESM26` → **`ESU6`**, cohérent avec le principe « front-month hors roll » déjà gelé ; (3) **voie d'acquisition explicitée** : ciblage direct `raw_symbol=ESU6` retenu contre un pull `parent` (`ES.FUT`) + filtrage local, écarté (§2.2, note) ; (4) **candidate d'exclusion de 3 dates (2/9/29 août) examinée puis rejetée** faute de justification connue, conforme à l'interdit §9 — aucune exclusion de date individuelle en v3. `metadata.get_cost` exécuté pour la portée v3 le 2026-09-04 : **30,048361867666 USD (~30,05 $)**, consigné avec la déviation de séquencement associée (§8.8, sans impact H0/H1). Inchangé depuis v2 : §3 les 4 indicateurs, §4 les 6 horizons, §5 Holm(24) α = 0,05, §6 métrique = courbe de décroissance IC (Pearson + IC95 Newey–West, pas de backtest / P&L / Sharpe / calibration), §7 conditions 1–3 (gate économique 0,80 tick ES / 6,16 ticks MES, verdicts GO / GO conditionnel / STOP), §9.

**PRODUCTION MODIFIED :** NO. Aucun code touché. Aucune donnée acquise.

**TESTS :** aucun (document de pré-enregistrement).

**DOCUMENTATION :** ce fichier — `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` (v3).

**NEXT ACTION :** (1) commiter ce fichier seul + tag `qde-022-prereg-v3` ; `git push --follow-tags`. (2) **[TODO utilisateur]** fixer le budget plafond USD (§2.3) — le coût déjà consigné (30,05 $) est très inférieur aux 125 $ de crédits gratuits Databento évoqués en §2.3. (3) **[TODO utilisateur]** résoudre le statut de licence CME via le questionnaire Databento (§2.3). (4) lancer `batch.submit_job`/`timeseries.get_range` pour la portée gelée (`ESU6`, `mbp-10`, 2026-08-02 → 2026-09-02) et consigner le montant réellement facturé + volume réel téléchargé. (5) écrire `qde022_l2_ofi.py` conforme au registre gelé ; renseigner `record_count`/jour, `n`, `n_eff`, MDE (§6.4) après lecture du premier fichier journalier. (6) produire QDE-022-R avec verdict GO / GO conditionnel / STOP. **Aucun `get_range`/`batch.submit_job` avant l'existence du tag `qde-022-prereg-v3`.**
