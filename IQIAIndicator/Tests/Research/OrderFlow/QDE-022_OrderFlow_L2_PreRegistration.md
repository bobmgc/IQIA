# QDE-022 — Pré-enregistrement : contenu informationnel du carnet d'ordres L2 (ES, Databento GLBX.MDP3)

**Date :** 2026-09-01
**Statut : PRÉ-ENREGISTREMENT — AUCUNE DONNÉE L2 ACQUISE À CE JOUR.**
Ce document est rédigé, commité et **taggé (`qde-022-prereg`) AVANT tout téléchargement de données**. Le hash du commit taggé sera reporté en tête du rapport de résultats (QDE-022-R). Toute modification postérieure au tag exige un nouveau tag horodaté et une justification explicite dans QDE-022-R.
**Type :** registre gelé d'hypothèses, d'indicateurs, d'horizons, de comptabilité des tests multiples et de règle de décision GO/STOP.
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

### 1.2 Hypothèse pré-enregistrée (H1)

> **H1.** Au moins un des 4 indicateurs de flux de carnet listés en §3, mesuré sur ES, présente avec le rendement futur du prix moyen une corrélation (IC) qui, à au moins un des 6 horizons listés en §4, est : (i) statistiquement significative après correction Holm sur les 24 tests, (ii) de signe stable entre la première et la seconde moitié du mois, et (iii) économiquement matérielle au sens du gate §7.3.

> **H0 (à retenir par défaut).** Aucune cellule (indicateur × horizon) ne satisfait simultanément (i), (ii) et (iii). L'information L2 ne fournit pas d'edge exploitable net des coûts retail. → SIGNAL_ONLY confirmé et **clôture de toute la ligne recherche d'edge** (le L2 était le dernier réservoir d'information hors-OHLC de la stack).

### 1.3 Ce que QDE-022 ne fait PAS

QDE-022 **mesure un contenu informationnel**. Il ne construit **aucune stratégie**, ne produit **aucune courbe d'équité**, **aucun P&L**, **aucune calibration de seuil**, **aucun walk-forward**. Ces travaux ne sont autorisés qu'en cas de verdict GO (§7), dans un lot ultérieur (QDE-023) lui-même pré-enregistré.

---

## 2. Achat de données — spécification exacte

### 2.1 Source et contrat

| Paramètre | Valeur gelée | Justification |
|---|---|---|
| Fournisseur | Databento, API historique | accès programmatique, DBN, `metadata.get_cost` disponible |
| Dataset | `GLBX.MDP3` | CME Globex MDP 3.0, flux natif du CME |
| **Instrument** | **ES (E-mini S&P 500), PAS MES** | le flux institutionnel est dans l'ES ; le MES en est un reflet. Signal **mesuré sur ES**, trading final **possible sur MES** (§8.5) |
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
3. Si le montant estimé ≤ budget consigné ci-dessous → lancer `batch.submit_job(...)` (job batch, pas streaming, pour un mois entier).
4. **Consigner : montant réellement facturé** (relevé Databento) + **volume réel téléchargé**. Écart estimé/réel commenté.
5. **Consigner les frais de licence CME** : Databento refacture une licence CME **séparée** pour `GLBX.MDP3`, dont le tarif dépend du **statut particulier (non-professional) vs professionnel**. Reporter : statut déclaré, montant mensuel exact, période couverte. Si le statut est incertain, le résoudre AVANT l'achat.

### 2.3 Budget et attentes d'ordre de grandeur (indicatif, non gelé)

- Données `mbp-10` ES, 1 mois : attendu **~15–40 Go** compressés ; coût données attendu de l'ordre de **quelques dizaines à ~150 USD** (à confirmer par `get_cost` — c'est l'objet du §2.2).
- Licence CME non-professionnelle : **poste distinct**, à consigner tel quel.
- **Budget plafond consigné : à renseigner par l'utilisateur avant exécution.** Si `get_cost` dépasse ce plafond → arrêt, pas de téléchargement, QDE-022-R documente le refus.

---

## 3. Liste d'indicateurs — FIGÉE, 4 MAXIMUM

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

## 4. Horizons — FIGÉS

### 4.1 Liste

**1 s, 5 s, 30 s, 60 s, 300 s, 900 s** — mesure en temps mural (wall-clock), horloge UTC. Aucun autre horizon.

### 4.2 Grille d'évaluation

- Points de grille : **chaque seconde entière UTC** à l'intérieur de la plage RTH (§2.1).
- Rendement futur : `r_h(T) = ln( P_mid(T + h) / P_mid(T) )` pour `h ∈ {1,5,30,60,300,900}` s.
- Un point `T` est **écarté** (et compté) si : carnet croisé/verrouillé en `T` ou `T+h`, un côté vide sur le niveau requis, ou `T + h` dépasse la fin de session du jour de `T` (aucun report inter-journalier — cohérent avec la contrainte de flat quotidien).

---

## 5. Comptabilité des tests multiples

- **4 indicateurs × 6 horizons = 24 tests.** Chiffre **annoncé avant exécution**.
- Correction **Holm-Bonferroni**, **α familial = 0,05**, appliquée à la famille des 24 p-values (test bilatéral de nullité de l'IC, erreurs-types HAC — §6.2).
- Un Holm "par indicateur" (m = 6) est rapporté **à titre indicatif seulement** ; la correction **bloquante** est celle sur les 24.
- Tout indicateur/variante/horizon ajouté après ouverture des données → famille élargie à `24 + k`, Holm recalculé, ajout signalé en rouge dans QDE-022-R.

---

## 6. Métrique primaire

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

---

## 7. Règle de décision — GELÉE

### 7.1 Trois conditions cumulatives par cellule

Une cellule `(X, h)` est déclarée **porteuse** si et seulement si les **trois** conditions suivantes sont vraies :

1. **Statistique.** `p_HAC(X, h)` survit à la correction Holm sur les 24 tests (α familial 0,05).
2. **Stabilité de signe.** `sign(IC_TRAIN) == sign(IC_OOS)` **et** l'IC95 de `IC_OOS` exclut 0.
3. **Matérialité économique (gate §7.3).**

### 7.2 Split temporel (pré-gelé)

- Jours de bourse du mois triés ; **60 % premiers = TRAIN**, **40 % derniers = OOS**, **1 jour de bourse purgé** entre les deux (jeté).
- Normalisation des indicateurs (§3.1) et `σ_ret(h)` du gate (§7.3) estimés **sur TRAIN uniquement**.
- **Aucun paramètre** (indicateur, horizon, fenêtre OFI, profondeur) n'est choisi sur les données : tout est gelé par le présent document. Le split ne sert qu'à la condition 2 et à ne pas estimer d'échelle en plein-échantillon.

### 7.3 Gate économique (pré-gelé)

Pour une cellule passant 1 et 2 :

```
mouvement_attendu(X, h) = |IC(X, h)| · σ_ret(h) · 2          (réponse du rendement à un choc de 2σ de l'indicateur)
   converti en points ES :  m_pts = mouvement_attendu · P_mid_moyen / (log-échelle ≈ 1)     [≈ mouvement_attendu · P_mid_moyen]
coût_aller-retour_ES       = 0,75 point ES  (≡ 3 ticks ; ≈ 37,50 USD/contrat ; fourchette retenue 2–4 ticks)
```

Le gate **passe** si : `m_pts ≥ 2 × 0,75 = 1,5 point ES` **et** l'IC95 propagé sur `m_pts` exclut `0,75`.
Le facteur **2×** est la marge délibérée pour la latence et le slippage **non modélisés** à ce stade (§8.2).

*Note coût :* mesuré sur ES ; l'exécution finale envisagée sur MES (§8.5) a un coût aller-retour d'ordre `0,77 pt` (≈ 3,855 USD, cohérent QDE-012→021), soit une friction en points quasi identique et une friction en bps légèrement supérieure — le gate ES à 1,5 pt reste le critère bloquant.

### 7.4 Verdict

| Résultat | Verdict | Suite |
|---|---|---|
| ≥ 1 cellule porteuse (7.1) | **GO** | autorise **QDE-023**, lot stratégie, **lui-même pré-enregistré** ; réplication obligatoire sur un 2ᵉ mois hors-roll **avant tout capital** (déjà acté ici) |
| 0 cellule porteuse | **STOP** | H0 retenue. SIGNAL_ONLY confirmé. **Clôture de toute la ligne recherche d'edge** — le L2 était le dernier réservoir d'information hors-OHLC. Toute reprise = donnée d'une autre nature (options, cross-market profond, alternative data) ou autre marché/horizon |

Aucun verdict intermédiaire. Une cellule qui passe 1 et 2 mais échoue 3 est **STOP** (documentée comme "signal réel mais sous le coût").

---

## 8. Menaces à la validité — déclarées d'avance

1. **Un seul mois, un seul instrument, un seul contrat.** Aucune généralité saisonnière ni de régime. Un GO impose une réplication sur un 2ᵉ mois hors-roll avant capital (§7.4).
2. **Latence et slippage nuls supposés.** L'IC est mesuré avec un carnet parfaitement synchrone et une exécution instantanée en `T`. Tout edge trouvé subira un abattement latence/file d'attente/slippage **non modélisé ici** ; le gate à 2× (§7.3) vise à préserver cette marge, sans la garantir.
3. **MBP-10 = vue agrégée par prix, 10 niveaux.** Position dans la file (queue) et icebergs non observables ; l'OFI 10 niveaux ne "voit" que la liquidité affichée sur 10 cran de prix.
4. **Sémantique des snapshots Databento.** `mbp-10` publie l'état après chaque événement ; l'OFI est reconstruit par différences d'états successifs, conforme à CKS. Une mauvaise gestion des événements `T` (trade), `F` (fill), `A/C/M` (add/cancel/modify) fausserait l'OFI — la sonde devra journaliser la ventilation des types d'événements.
5. **Signal ES → trade MES.** Suppose que le MES suit l'ES au tick près sur des horizons ≥ 1 s. Empiriquement quasi-exact, mais **non revérifié** dans QDE-022.
6. **Statut de licence.** Un basculement "professionnel" change la licence CME et possiblement les commissions → gate §7.3 à recalculer.
7. **Biais de sur-échantillonnage temporel.** La grille 1 s crée une autocorrélation massive des résidus ; traitée par HAC + sous-échantillon non chevauchant (§6.2–6.3), mais `n_eff` à `h = 900 s` est modeste (~500–600) et les IC95 y seront larges.
8. **Choix du mois.** Avril 2026 est choisi **a priori** (hors-roll, pas de contrainte connue le liant à un résultat). Si un événement macro exceptionnel domine le mois, QDE-022-R le signale ; le mois **n'est pas** re-choisi après coup.

---

## 9. Hors périmètre — INTERDITS GELÉS

- **Schéma `mbo`** (market-by-order) — exclu (§2.1).
- **Tout 5ᵉ indicateur**, ou toute **variante paramétrique** d'un des 4 (autre fenêtre OFI que 1 s, autre profondeur d'imbalance que 10, micro-prix multi-niveaux, OFI pondéré…) — compte comme test additionnel, réenregistrement + Holm élargi.
- **Tout horizon** hors des 6 gelés.
- **Backtest de stratégie, courbe d'équité, P&L, ratio de Sharpe, calibration de seuil, walk-forward, optimisation** — interdits dans QDE-022. Autorisés seulement après verdict GO, dans QDE-023 pré-enregistré.
- **Choix, après avoir vu les données**, du mois, du jour, de la plage horaire, du contrat, de la profondeur, ou de la "meilleure" cellule sans pénalité Holm.
- **Ré-exécution** de l'analyse avec des paramètres modifiés sans nouveau tag de pré-enregistrement.

---

## 10. Livrables

| Livrable | Contenu | Moment |
|---|---|---|
| **QDE-022** (ce fichier) | Pré-enregistrement gelé | Maintenant — commité + taggé `qde-022-prereg` |
| **`qde022_l2_ofi.py`** | Sonde d'analyse Python : lecture DBN `mbp-10`, reconstruction du carnet, calcul des **4 indicateurs gelés**, grille 1 s, **6 horizons**, IC Pearson + HAC, Spearman, sous-échantillon non chevauchant, split TRAIN/OOS, Holm(24), gate §7.3. **Zéro code de stratégie.** | Après acquisition |
| **QDE-022-R** | Rapport de résultats : hash du commit taggé ; `get_cost` estimé **vs** facturé **vs** volume réel ; licence CME réelle + statut ; table des 24 cellules (IC, IC95 HAC, p, Holm, n, n_eff) ; 4 courbes de décroissance ; IC TRAIN/OOS ; gate économique par cellule ; **verdict GO / STOP** ; menaces §8 réévaluées | Après analyse |

---

## 11. Intégrité du pré-enregistrement

1. Ce fichier est commité **seul** (aucun autre changement dans le commit), message :
   `research(qde-022): pre-registration - L2 order-flow information content on ES (FROZEN, pre-data)`
2. Tag annoté **`qde-022-prereg`** sur ce commit, message identique.
3. **Aucune donnée L2 n'est téléchargée avant que ce tag n'existe.**
4. QDE-022-R cite le hash complet du commit taggé en première ligne.
5. Toute évolution de QDE-022 après le tag → nouveau tag `qde-022-prereg-v2` (etc.) + section "Écarts au pré-enregistrement" en tête de QDE-022-R, chaque écart justifié.

---

## 12. Index des fichiers

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` | Ce pré-enregistrement | Oui (documentation de recherche) |
| `IQIAIndicator/Tests/Research/OrderFlow/qde022_l2_ofi.py` | Sonde d'analyse (à créer post-acquisition) | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlow/Output/qde022_*.txt` | Sorties mesurées (à créer) | Recherche uniquement |
| `IQIAIndicator/Documentation/Scientific/QDE-022-R_*.md` | Rapport de résultats (à créer) | Oui (documentation) |

Les données brutes DBN (~15–40 Go) **ne sont pas versionnées** — stockées hors dépôt, chemin consigné dans QDE-022-R.

---

## 13. FINAL OUTPUT

**STATUS :** Pré-enregistrement rédigé. **Aucune donnée L2 acquise.** Gelés : hypothèse H1/H0 (§1.2) ; achat = Databento `GLBX.MDP3`, schéma `mbp-10`, contrat `ESM26`, avril 2026 (repli mai 2026), `get_cost` obligatoire avant `get_range`, licence CME consignée à part (§2) ; **4 indicateurs** (OFI top-of-book CKS 2014, OFI 10 niveaux, book imbalance 10 niveaux, écart micro-prix/mid) (§3) ; **6 horizons** (1/5/30/60/300/900 s) (§4) ; **24 tests, Holm α = 0,05** annoncé (§5) ; métrique primaire = **courbe de décroissance de l'IC** avec IC95 HAC, **pas de backtest, pas de P&L** (§6) ; règle de décision **GO/STOP** à 3 conditions cumulatives (Holm + stabilité de signe TRAIN/OOS + gate économique 2× le coût aller-retour ES 0,75 pt) (§7).

**PRODUCTION MODIFIED :** NO. Aucun code touché. Aucune donnée acquise.

**TESTS :** aucun (document de pré-enregistrement).

**DOCUMENTATION :** ce fichier — `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md`.

**NEXT ACTION :** (1) commiter ce fichier seul + tag `qde-022-prereg` ; (2) fixer le budget plafond USD ; (3) résoudre le statut de licence CME (particulier/professionnel) ; (4) appeler `metadata.get_cost` et consigner ; (5) si ≤ plafond, `batch.submit_job` pour avril 2026 sur `ESM26` ; (6) écrire `qde022_l2_ofi.py` conforme au registre gelé ; (7) produire QDE-022-R avec verdict GO/STOP. **Aucun téléchargement avant l'existence du tag.**
