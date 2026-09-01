# QDE-022 — Pré-enregistrement : contenu informationnel du carnet d'ordres L2 (ES, Databento GLBX.MDP3)

## Historique des amendements

| Version | Référence | Statut |
|---|---|---|
| **v1** | commit `2d21ae5`, tag `qde-022-prereg` | **remplacée** (conservée dans l'historique git, non réécrite) |
| **v2** | ce document, tag `qde-022-prereg-v2` | **en vigueur — remplace v1** |

**v2 remplace v1 sur trois points, et trois seulement :**
1. **Gate économique corrigé** : `0,75 pt ES` (v1, infaisable — voir §7.3, note) → **`0,80 tick ES`**.
2. **Instrument de référence du gate explicité** dans §7 (n'est plus une simple menace à la validité en §8) : référence = **ES**, report obligatoire en ticks ES **et** ticks MES, seuil MES documenté, verdict **GO conditionnel** introduit.
3. **Portée de H0 restreinte** : H0 n'est plus « clôture de toute la ligne recherche d'edge » mais un énoncé étroit (un mois, un instrument, un schéma, quatre indicateurs, horizon ≥ 60 s, latence nulle). La clôture de la ligne recherche d'edge devient une **conséquence recommandée** en cas de non-rejet de H0 (§7.5), pas la définition de H0. Même correction de formulation que celle appliquée à la conclusion de QDE-021.

**Aucune donnée de marché n'a été acquise entre v1 et v2.** L'amendement est **antérieur à tout `metadata.get_cost` et à tout `get_range`**. La correction n'a été motivée par aucun résultat — il n'existe aucun résultat.

Tout le reste de v1 est conservé à l'identique : §2 (spécification d'achat), §3 (les 4 indicateurs et leurs formules), §4 (les 6 horizons), §5 (24 tests, Holm α = 0,05), §6 (métrique primaire = courbe de décroissance IC, Pearson + IC95 Newey–West, pas de backtest / P&L / Sharpe / calibration), les deux autres conditions cumulatives de §7 (Holm ; stabilité de signe TRAIN/OOS 60/40 purgé), §8 à §13.

---

**Date v1 :** 2026-09-01 — **Date v2 :** 2026-09-01
**Statut : PRÉ-ENREGISTREMENT — AUCUNE DONNÉE L2 ACQUISE À CE JOUR.**
Ce document est rédigé, commité et **taggé (`qde-022-prereg-v2`) AVANT tout téléchargement de données**. Le hash du commit taggé sera reporté en tête du rapport de résultats (QDE-022-R). Toute modification postérieure au tag exige un nouveau tag horodaté et une justification explicite dans QDE-022-R.
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

> **H0 (hypothèse nulle — portée étroite, à retenir par défaut).** L'OFI et ses variantes (les 4 indicateurs de §3), mesurés sur **ES MBP-10**, ne présentent **pas d'IC exploitable nette de coûts à un horizon ≥ 60 s**, sur un **échantillon d'un mois** (avril 2026, repli mai 2026), avec **latence supposée nulle**.

**Rejet de H0** = au moins une cellule (indicateur × horizon ≥ 60 s) **porteuse** au sens de §7.1.
**Non-rejet de H0** = aucune cellule porteuse à un horizon ≥ 60 s. La clôture de la ligne recherche d'edge n'est **pas** contenue dans H0 : c'est une **conséquence recommandée** traitée en §7.5.

Les horizons < 60 s (1, 5, 30 s) sont **mesurés et rapportés** (ils font partie de la courbe de décroissance et des 24 tests Holm), mais une cellule porteuse dont *tous* les horizons significatifs sont < 60 s est traitée comme **GO conditionnel à haut risque de latence** (§7.4), la latence non modélisée dominant à cette échelle.

### 1.3 Ce que QDE-022 ne fait PAS

QDE-022 **mesure un contenu informationnel**. Il ne construit **aucune stratégie**, ne produit **aucune courbe d'équité**, **aucun P&L**, **aucune calibration de seuil**, **aucun walk-forward**. Ces travaux ne sont autorisés qu'en cas de verdict GO ou GO conditionnel (§7), dans un lot ultérieur (QDE-023) lui-même pré-enregistré.

---

## 2. Achat de données — spécification exacte *(inchangé depuis v1)*

### 2.1 Source et contrat

| Paramètre | Valeur gelée | Justification |
|---|---|---|
| Fournisseur | Databento, API historique | accès programmatique, DBN, `metadata.get_cost` disponible |
| Dataset | `GLBX.MDP3` | CME Globex MDP 3.0, flux natif du CME |
| **Instrument** | **ES (E-mini S&P 500), PAS MES** | le flux institutionnel est dans l'ES ; le MES en est un reflet. Signal **mesuré sur ES**, trading final **possible sur MES** (§7.2) |
| Schéma | **`mbp-10`** (market-by-price, 10 niveaux) | suffisant pour l'OFI top-of-book et l'OFI 10 niveaux, l'imbalance et le micro-prix. **`mbo` est EXCLU** : volume ~10×, non requis pour ces 4 indicateurs, ingérable pour un premier test |
| Contrat (`raw_symbol`) | **`ESM26`** (échéance juin 2026) | mois de front pour avril **et** mai 2026 ; le roulement M26→U26 a lieu ~mi-juin 2026, donc **hors de la fenêtre** |
| `stype_in` | `raw_symbol` | ciblage direct du contrat, pas de résolution `parent`/continu |
| Période primaire | **2026-04-01T00:00:00Z → 2026-05-01T00:00:00Z** | 1 mois calendaire, **hors mois de roulement** (mars/juin/sept./déc. exclus) |
| Période de repli | **2026-05-01 → 2026-06-01** | si avril indisponible/incomplet ; toujours sur `ESM26`, toujours hors roll |
| Filtre de session | RTH uniquement : **13:30:00 → 20:00:00 UTC** (08:30–15:00 CT) | avril/mai = CDT = UTC−5. Session cash S&P 09:30–16:00 ET. Les événements hors de cette plage sont écartés à l'analyse, pas à l'achat |
| Horodatage de référence | **`ts_event`** (heure du moteur d'appariement CME), UTC ns | causalité stricte ; `ts_recv` conservé pour diagnostic de latence uniquement |
| Encodage / compression | `dbn` / `zstd` | format natif Databento |

### 2.2 Procédure d'acquisition (ordre impératif)

1. **`metadata.get_cost(dataset="GLBX.MDP3", schema="mbp-10", symbols=["ESM26"], stype_in="raw_symbol", start=…, end=…, mode="historical")`** — **AVANT tout `get_range` / `batch.submit_job`**.
2. **Consigner dans QDE-022-R : montant estimé** (USD) retourné par `get_cost`, date/heure de l'appel, volume estimé (octets, `record_count`).
3. Si le montant estimé ≤ budget plafond (§2.3, **TODO utilisateur**) → lancer `batch.submit_job(...)` (job batch, pas streaming, pour un mois entier).
4. **Consigner : montant réellement facturé** (relevé Databento) + **volume réel téléchargé**. Écart estimé/réel commenté.
5. **Consigner les frais de licence CME** : Databento refacture une licence CME **séparée** pour `GLBX.MDP3`, dont le tarif dépend du **statut particulier (non-professional) vs professionnel**. Reporter : statut déclaré, montant mensuel exact, période couverte. Le statut est **déterminé par le questionnaire Databento, pas par estimation** (§2.3, **TODO utilisateur**).

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

### 6.4 Effet minimal détectable (MDE) — **TODO après lecture du premier fichier journalier**

Le MDE (plus petit `|IC|` détectable à α corrigé = 0,05 / puissance 0,80) et le `n_eff` par horizon dépendent :
- du **nombre réel de mises à jour du carnet par jour sur ES** (`record_count`/jour) — **inconnu à ce stade, ne PAS inventer** ;
- du **taux de rejet** de points de grille (carnets croisés, bords de session) — inconnu à ce stade ;
- de la structure d'autocorrélation résiduelle réelle à chaque horizon.

**À renseigner dans QDE-022-R après lecture du premier fichier journalier :** `record_count`/jour, `n` valide et `n_eff` par horizon, MDE(`h`) correspondant. Aucune borne chiffrée n'est inscrite ici (la borne « grille 1 s » ≈ 23 400 points/jour RTH est une **borne supérieure théorique**, pas une estimation de `n` valide).

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

## 8. Menaces à la validité — déclarées d'avance *(v2 : ancien point « signal ES → trade MES » retiré, désormais traité en §7.3–§7.4)*

1. **Un seul mois, un seul instrument, un seul contrat.** Aucune généralité saisonnière ni de régime. Un rejet de H0 impose une réplication sur un 2ᵉ mois hors-roll avant tout capital (§7.4).
2. **Latence et slippage nuls supposés.** L'IC est mesuré avec un carnet parfaitement synchrone et une exécution instantanée en `T`. Tout edge trouvé subira un abattement latence/file d'attente/slippage **non modélisé ici** ; le gate à 2× (§7.3) et le verdict « haut risque de latence » pour les horizons < 60 s (§7.4) visent à préserver cette marge, sans la garantir.
3. **MBP-10 = vue agrégée par prix, 10 niveaux.** Position dans la file (queue) et icebergs non observables ; l'OFI 10 niveaux ne "voit" que la liquidité affichée sur 10 crans de prix.
4. **Sémantique des snapshots Databento.** `mbp-10` publie l'état après chaque événement ; l'OFI est reconstruit par différences d'états successifs, conforme à CKS. Une mauvaise gestion des types d'événements (`T` trade, `F` fill, `A/C/M` add/cancel/modify) fausserait l'OFI — la sonde devra journaliser leur ventilation.
5. **Statut de licence.** Un basculement "professionnel" change la licence CME et possiblement les commissions → gate §7.3 à recalculer.
6. **Biais de sur-échantillonnage temporel.** La grille 1 s crée une autocorrélation massive des résidus ; traitée par HAC + sous-échantillon non chevauchant (§6.2–6.3). `n`, `n_eff` et MDE réels sont **TODO** (§6.4) — non chiffrés ici.
7. **Choix du mois.** Avril 2026 est choisi **a priori** (hors-roll, pas de contrainte connue le liant à un résultat). Si un événement macro exceptionnel domine le mois, QDE-022-R le signale ; le mois **n'est pas** re-choisi après coup.

---

## 9. Hors périmètre — INTERDITS GELÉS *(inchangé depuis v1)*

- **Schéma `mbo`** (market-by-order) — exclu (§2.1).
- **Tout 5ᵉ indicateur**, ou toute **variante paramétrique** d'un des 4 (autre fenêtre OFI que 1 s, autre profondeur d'imbalance que 10, micro-prix multi-niveaux, OFI pondéré…) — compte comme test additionnel, réenregistrement + Holm élargi.
- **Tout horizon** hors des 6 gelés.
- **Backtest de stratégie, courbe d'équité, P&L, ratio de Sharpe, calibration de seuil, walk-forward, optimisation** — interdits dans QDE-022. Autorisés seulement après verdict GO ou GO conditionnel, dans QDE-023 pré-enregistré.
- **Choix, après avoir vu les données**, du mois, du jour, de la plage horaire, du contrat, de la profondeur, ou de la "meilleure" cellule sans pénalité Holm.
- **Ré-exécution** de l'analyse avec des paramètres modifiés sans nouveau tag de pré-enregistrement.

---

## 10. Livrables *(inchangé depuis v1)*

| Livrable | Contenu | Moment |
|---|---|---|
| **QDE-022 v2** (ce fichier) | Pré-enregistrement gelé | Maintenant — commité + taggé `qde-022-prereg-v2` |
| **`qde022_l2_ofi.py`** | Sonde d'analyse Python : lecture DBN `mbp-10`, reconstruction du carnet, calcul des **4 indicateurs gelés**, grille 1 s, **6 horizons**, IC Pearson + HAC, Spearman, sous-échantillon non chevauchant, split TRAIN/OOS, Holm(24), gate §7.3. **Zéro code de stratégie.** | Après acquisition |
| **QDE-022-R** | Rapport de résultats : hash du commit taggé ; `get_cost` estimé **vs** facturé **vs** volume réel ; licence CME réelle + statut ; `record_count`/jour, `n`, `n_eff`, MDE (§6.4) ; table des 24 cellules (IC, IC95 HAC, p, Holm, n, n_eff) ; 4 courbes de décroissance ; IC TRAIN/OOS ; gate économique par cellule **en ticks ES et MES** ; **verdict GO / GO conditionnel / STOP** ; menaces §8 réévaluées | Après analyse |

---

## 11. Intégrité du pré-enregistrement *(v2 : tag mis à jour)*

1. Ce fichier v2 est commité **seul** (aucun autre changement dans le commit), message :
   `research(qde-022): pre-registration v2 - economic gate fix + gate reference instrument + narrowed H0 (FROZEN, pre-data)`
2. Tag annoté **`qde-022-prereg-v2`** sur ce commit.
3. **v1 (`2d21ae5`, tag `qde-022-prereg`) reste dans l'historique git, non réécrite.** v2 la remplace explicitement (voir en-tête).
4. **Aucune donnée L2 n'a été téléchargée avant l'existence du tag `qde-022-prereg-v2`, ni entre v1 et v2.**
5. QDE-022-R cite le hash complet du commit taggé `qde-022-prereg-v2` en première ligne.
6. Toute évolution ultérieure de QDE-022 → nouveau tag `qde-022-prereg-v3` (etc.) + section "Écarts au pré-enregistrement" en tête de QDE-022-R, chaque écart justifié.

---

## 12. Index des fichiers *(inchangé depuis v1)*

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` | Ce pré-enregistrement (v2) | Oui (documentation de recherche) |
| `IQIAIndicator/Tests/Research/OrderFlow/qde022_l2_ofi.py` | Sonde d'analyse (à créer post-acquisition) | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlow/Output/qde022_*.txt` | Sorties mesurées (à créer) | Recherche uniquement |
| `IQIAIndicator/Documentation/Scientific/QDE-022-R_*.md` | Rapport de résultats (à créer) | Oui (documentation) |

Les données brutes DBN **ne sont pas versionnées** — stockées hors dépôt, chemin consigné dans QDE-022-R.

---

## 13. FINAL OUTPUT *(v2)*

**STATUS :** Pré-enregistrement **v2** rédigé, amendement visible de v1. **Aucune donnée L2 acquise, ni avant v1, ni entre v1 et v2.** Corrections v2 : (1) **gate économique** `0,75 pt ES` (infaisable, confusion tick/point) → **`0,80 tick ES`** (§7.3, avec note d'explication) ; (2) **instrument de référence du gate = ES**, explicité en §7, report obligatoire en ticks ES **et** ticks MES, **seuil MES documenté à 6,16 ticks**, verdict **GO conditionnel** introduit pour `0,80 ≤ M < 6,16` (décision de passage sur ES renvoyée à un document ultérieur) ; (3) **H0 restreinte** (ES MBP-10, horizon ≥ 60 s, un mois, latence nulle) — la clôture de la ligne recherche d'edge devient une **conséquence recommandée** (§7.5), pas la définition de H0. Inchangé : §2 achat (`GLBX.MDP3` / `mbp-10` / `ESM26` / avril 2026 / `get_cost` obligatoire / licence CME à part), §3 les 4 indicateurs, §4 les 6 horizons, §5 Holm(24) α = 0,05, §6 métrique = courbe de décroissance IC (Pearson + IC95 Newey–West, pas de backtest / P&L / Sharpe / calibration), §7 conditions 1 & 2, §8–§13.

**PRODUCTION MODIFIED :** NO. Aucun code touché. Aucune donnée acquise.

**TESTS :** aucun (document de pré-enregistrement).

**DOCUMENTATION :** ce fichier — `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` (v2).

**NEXT ACTION :** (1) commiter ce fichier seul + tag `qde-022-prereg-v2` ; `git push --follow-tags` reste à faire par l'utilisateur. (2) **[TODO utilisateur]** fixer le budget plafond USD (§2.3). (3) **[TODO utilisateur]** résoudre le statut de licence CME via le questionnaire Databento (§2.3). (4) appeler `metadata.get_cost` et consigner estimé/réel + volume. (5) si ≤ plafond, `batch.submit_job` pour avril 2026 sur `ESM26`. (6) écrire `qde022_l2_ofi.py` conforme au registre gelé ; renseigner `record_count`/jour, `n`, `n_eff`, MDE (§6.4) après lecture du premier fichier journalier. (7) produire QDE-022-R avec verdict GO / GO conditionnel / STOP. **Aucun téléchargement avant l'existence du tag `qde-022-prereg-v2`.**
