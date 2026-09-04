# QDE-022 — Vérification d'intégrité des données livrées (v3)

**Date :** 2026-09-04
**Portée vérifiée :** job Databento `GLBX-20260904-93PSB55DT3` (`GLBX.MDP3` / `mbp-10` / `ESU6` / `stype_in=raw_symbol` / 2026-08-02 → 2026-09-02), livré dans `c:\Users\rnbch\OneDrive\Bureau\QDE-022_Data\GLBX-20260904-93PSB55DT3\` (hors dépôt).
**Document de référence (au moment de la vérification initiale) :** `QDE-022_OrderFlow_L2_PreRegistration.md`, tag `qde-022-prereg-v3`, commit `2dd383a013e0f2c2a4f493703a73fbb9f45c90e1`.
**Document de référence (depuis la mise à jour du verdict, 2026-09-05) :** même fichier, tag `qde-022-prereg-v4` — voir « Historique du verdict » ci-dessous.
**Type :** vérification de conformité uniquement. **Aucun calcul d'OFI, d'IC, de rendement ou de statistique de prix n'a été effectué**, ni lors de la vérification initiale ni lors de cette mise à jour.

---

## Verdict global

# **CONFORME** *(mis à jour le 2026-09-05 — voir historique du verdict ci-dessous)*

**Ce verdict a changé depuis la publication initiale de ce rapport. Les deux verdicts sont conservés ci-dessous, aucun n'est effacé.**

### Historique du verdict

| Date | Verdict | Motif |
|---|---|---|
| 2026-09-04 (publication initiale, commit `0bc5311`) | **NON CONFORME** | v3 §8.7 affirmait un choix « a priori » de la période/du contrat, factuellement contredit par la préexistence d'un job Databento non conforme (`stype_in=parent`) ayant téléchargé de vraies données ESU6/août 2026 avant le tag `qde-022-prereg-v3`. |
| 2026-09-05 (cette mise à jour) | **CONFORME** | v4 (`QDE-022_OrderFlow_L2_PreRegistration.md`, tag `qde-022-prereg-v4`) réécrit §8.7 pour énoncer la chronologie exacte, sans minimisation, et documente une décision explicite (conserver août 2026) avec risque résiduel assumé. Le motif du NON CONFORME était l'inexactitude du document, pas un défaut des fichiers — cette inexactitude est corrigée. |

**Ce qui n'a pas changé et reste vrai dans les deux versions du verdict :**
- Le **jeu de données lui-même** (job `GLBX-20260904-93PSB55DT3`) est et reste **structurellement conforme** à la portée gelée sur tous les champs vérifiés (contrôles 1, 2, 3, 5, 6 : CONFORME ; contrôle 4 : SANS OBJET, voir reclassement du 2026-09-04 ci-dessous).
- Le job `parent` antérieur (`GLBX-20260903-7XE859LGPE`) a bien téléchargé de vraies données ESU6/août 2026 le 2026-09-03, avant l'existence de `qde-022-prereg-v3` (2026-09-04) — **ce fait n'est pas remis en cause**, il est maintenant correctement documenté (v4 §8.7) plutôt que contredit par le document lui-même.
- Le **risque résiduel** (le choix de la période/du contrat a pu être exposé à la préexistence de ces données) **reste documenté et non neutralisé** — voir v4 §8.7, raisonnement a-f — et devra être rapporté dans QDE-022-R comme menace à la validité, quel que soit le verdict d'analyse (GO / GO conditionnel / STOP). **CONFORME signifie que le document est maintenant honnête sur ce risque, pas que le risque a disparu.**

**Renvoi explicite :** `QDE-022_OrderFlow_L2_PreRegistration.md`, tag `qde-022-prereg-v4`, §8 point 7 (« Choix de la période — historique complet et risque résiduel assumé »).

**Aucune analyse (OFI, IC, gate économique) n'est lancée à la suite de cette mise à jour**, conformément à la consigne, quel que soit le verdict.

---

## Tâche annexe — commit QDE-013 à QDE-021

Vérifié avant les contrôles : le commit demandé existait déjà dans l'historique (`git log` / `git fetch origin` concordants), poussé sur `origin/main`.

- **Commit :** `bbe3266599f41f212f31d6324ed11c543201c82e` — *"research: commit QDE-012 to QDE-021 documents and test probes"*
- **Contenu :** 33 fichiers, 8032 insertions (documents `QDE-012` à `QDE-021` + sondes de test associées).
- **Fichiers volumineux (>10 Mo) dans ce commit :** `git ls-tree -r -l bbe3266` filtré `>10485760` octets → **aucun**.
- **État :** déjà présent sur `origin/main` (`git log origin/main --oneline` le confirme), aucune action de commit/push supplémentaire nécessaire de ma part.

---

## Contrôle 1 — Conformité au gel (`metadata.json`)

| Champ | `metadata.json` | Portée gelée v3 (§2.1/§2.2) | Verdict |
|---|---|---|---|
| `dataset` | `GLBX.MDP3` | `GLBX.MDP3` | conforme |
| `schema` | `mbp-10` | `mbp-10` | conforme |
| `symbols` | `["ESU6"]` | `["ESU6"]` | conforme |
| `stype_in` | `raw_symbol` | `raw_symbol` | conforme |
| `stype_out` | `instrument_id` | (non spécifié explicitement en v3, valeur par défaut du SDK) | conforme |
| `start` | `1785628800000000000` ns = **2026-08-02T00:00:00Z** | 2026-08-02T00:00:00Z | conforme |
| `end` | `1788307200000000000` ns = **2026-09-02T00:00:00Z** | 2026-09-02T00:00:00Z | conforme |
| `encoding` | `dbn` | `dbn` | conforme |
| `compression` | `zstd` | `zstd` | conforme |
| `split_duration` | `day` | (non figé explicitement — valeur par défaut du SDK, cohérente avec la livraison en fichiers journaliers attendue) | conforme |
| `map_symbols` | `false` | (non figé explicitement — cohérent avec `stype_out=instrument_id`, aucune résolution de symbole appliquée en amont) | conforme |

**Aucun champ ne diverge.** Contrôles suivants poursuivis.

**Verdict contrôle 1 : CONFORME.**

---

## Contrôle 2 — Unicité du contrat

`symbology.json` **n'a pas été produit** par ce job (delivery standard sans requête symbology séparée). Le mapping symbole → `instrument_id` est cependant présent en clair dans l'en-tête de **chaque** fichier `.dbn.zst` (champ `metadata.mappings`, lu via `databento.DBNStore`). Les 27 fichiers ont été inspectés individuellement :

- **`symbols` déclaré par fichier :** `['ESU6']` — identique sur les 27 fichiers.
- **`instrument_id` mappé par fichier :** `{'42140870'}` — **un seul et même `instrument_id`, identique sur les 27 fichiers**, aucune variation sur le mois (cohérent avec un contrat unique, sans roulement, sur toute la fenêtre).
- **`partial` / `not_found` (SDK) :** vides sur les 27 fichiers — aucun symbole partiellement résolu ou introuvable.
- **Aucun symbole de type spread calendaire** (`ESU6-ESZ6` ou similaire) : absent, seul `ESU6` apparaît sur l'ensemble des 27 fichiers.

C'est exactement le contrôle qui garantit que le problème du job `parent` précédent (`GLBX-20260903-7XE859LGPE`, `symbols=ES.FUT`, qui aurait pu renvoyer plusieurs contrats ES simultanément) **ne se reproduit pas** dans cette livraison.

**Verdict contrôle 2 : CONFORME.**

---

## Contrôle 3 — Inventaire des jours

Fenêtre gelée : 2026-08-02 → 2026-09-01 inclus (32 dates calendaires ; `end` exclusif au 2026-09-02).

**27 fichiers journaliers livrés :**
2026-08-02, 03, 04, 05, 06, 07, 09, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 23, 24, 25, 26, 27, 28, 30, 31, 2026-09-01.

**Dates absentes (5 sur 32) — toutes des samedis, jour de semaine indiqué :**

| Date | Jour |
|---|---|
| 2026-08-01 | Samedi *(hors fenêtre effective — avant le premier fichier livré, la requête démarre au 2026-08-02)* |
| 2026-08-08 | Samedi |
| 2026-08-15 | Samedi |
| 2026-08-22 | Samedi |
| 2026-08-29 | Samedi |

**Aucun jour de semaine (lundi–vendredi) ne manque.** Les 22 jours ouvrés de la fenêtre (03, 04, 05, 06, 07, 10, 11, 12, 13, 14, 17, 18, 19, 20, 21, 24, 25, 26, 27, 28, 31 août + 1er septembre) sont tous présents.

**Dates présentes mais de taille anormalement faible (< 20 % de la médiane des tailles de fichier, jours ouvrés) :** médiane (22 fichiers ouvrés) = 188 103 686 octets compressés ; seuil 20 % = 37 620 737 octets. **Aucun fichier de jour ouvré sous ce seuil** (plage observée : 64,3 % à 140,1 % de la médiane).

**Dimanches présents (5) mais de taille très faible — attendu, non anormal :** 2026-08-02, 09, 16, 23, 30 (1,3 à 5,6 Mo compressés, 0,7 % à 3,0 % de la médiane des jours ouvrés). Cohérent avec une session Globex du dimanche soir entièrement hors de la fenêtre RTH gelée (13:30–20:00 UTC, §2.1) — aucune intervention manuelle, aucun filtrage appliqué ici, simple constat.

**Verdict contrôle 3 : CONFORME.** Aucun jour ouvré manquant ni anormalement petit.

---

## Contrôle 4 — Qualité déclarée (`condition.json`)

31 dates couvertes (2026-08-02 → 2026-09-01), toutes `available` **sauf une** :

| Date | Condition |
|---|---|
| 2026-08-29 | **`degraded`** |
| (30 autres dates de la fenêtre) | `available` |

**Aucun jour ouvré n'est marqué `degraded` ou `missing`.** Le seul statut non-`available` (2026-08-29) tombe sur un **samedi**, absent des fichiers livrés (contrôle 3) — cohérent avec une anomalie ponctuelle sur un jour sans session RTH exploitable.

**Recoupement avec le job `parent` précédent :** conforme à ce qu'indiquait l'utilisateur, **le même statut `degraded` sur 2026-08-29 se retrouve ici**, sur une livraison `raw_symbol` indépendante. Le signal semble donc attaché à la donnée d'échange sous-jacente elle-même (assessment Databento de la qualité de la journée CME), pas à la voie d'acquisition (`parent` vs `raw_symbol`) — constat, pas d'interprétation plus poussée demandée ici.

**Reclassement (relecture du 2026-09-04) — vérifié par calcul :**
- `datetime.date(2026, 8, 29).strftime('%A')` → **`Saturday`**. Confirmé.
- Balayage complet de `condition.json` : **le seul enregistrement non-`available` de toute la fenêtre est 2026-08-29, un samedi.** Aucun jour ouvré (lundi-vendredi) n'est concerné.
- La méthodologie QDE-022 (§4.2 du pré-enregistrement) n'évalue que la grille RTH 13:30–20:00 UTC, lundi-vendredi. Un samedi ne contient **aucun point de grille RTH** — il est structurellement invisible pour H0/H1, pas seulement « sans impact » sur elles.

**Verdict contrôle 4 : SANS OBJET** *(reclassé, était ÉCART MINEUR)* — le statut `degraded` du 2026-08-29 porte sur un jour hors du domaine de définition de la méthodologie QDE-022 (aucun point de grille RTH possible un samedi), pas sur une dégradation constatée puis jugée sans impact. Aucun jour ouvré n'est affecté.

---

## Contrôle 5 — Comptage des enregistrements

Comptage exact par fichier (itération complète des 27 `.dbn.zst`, ~194 s cumulées) :

| Date | Jour | `record_count` |
|---|---|---|
| 2026-08-02 | Dim | 220 713 |
| 2026-08-03 | Lun | 8 257 020 |
| 2026-08-04 | Mar | 8 960 902 |
| 2026-08-05 | Mer | 10 454 003 |
| 2026-08-06 | Jeu | 10 910 265 |
| 2026-08-07 | Ven | 9 277 024 |
| 2026-08-09 | Dim | 117 635 |
| 2026-08-10 | Lun | 6 752 834 |
| 2026-08-11 | Mar | 6 670 883 |
| 2026-08-12 | Mer | 6 219 159 |
| 2026-08-13 | Jeu | 6 079 639 |
| 2026-08-14 | Ven | 5 067 446 |
| 2026-08-16 | Dim | 55 132 |
| 2026-08-17 | Lun | 5 162 090 |
| 2026-08-18 | Mar | 7 987 830 |
| 2026-08-19 | Mer | 9 456 379 |
| 2026-08-20 | Jeu | 9 667 659 |
| 2026-08-21 | Ven | 6 680 759 |
| 2026-08-23 | Dim | 115 176 |
| 2026-08-24 | Lun | 7 717 165 |
| 2026-08-25 | Mar | 6 770 221 |
| 2026-08-26 | Mer | 6 491 007 |
| 2026-08-27 | Jeu | 8 512 037 |
| 2026-08-28 | Ven | 10 717 460 |
| 2026-08-30 | Dim | 212 891 |
| 2026-08-31 | Lun | 7 284 718 |
| 2026-09-01 | Mar | 9 530 773 |

**Somme totale : 175 348 820 = `record_count` facturé par le job (`get_job_details`), exactement.** Aucune perte, troncature ni double-comptage entre la facturation Databento et le contenu réel des fichiers.

**Statistique sur les 22 jours ouvrés uniquement** (les 5 dimanches, population non comparable — session nocturne partielle — sont exclus de ce calcul, reportés à titre de contexte) :
- Médiane : 7 852 498 — Moyenne : 7 937 603 — Écart-type (population) : 1 717 223.
- **Écart-type maximal observé sur un jour ouvré : 1,73σ** (2026-08-06, 10 910 265 enregistrements). **Aucun jour ouvré au-delà de 3σ.**

**Verdict contrôle 5 : CONFORME.** Aucune anomalie de comptage signalée, aucune exclusion appliquée.

---

## Contrôle 6 — Coût et licence

| Poste | Montant | Détail |
|---|---|---|
| `get_cost` estimé (portée v3) | **30,048361867666 USD** | appel du 2026-09-04 (avant tag v3 — déviation déjà consignée dans le document gelé, §2.2) |
| Coût réellement facturé (job `GLBX-20260904-93PSB55DT3`) | **30,048361867666 USD** | identique à l'estimation au centime près (`cost_usd` du job, `state=done`) |
| Job précédent non conforme (`GLBX-20260903-7XE859LGPE`, `stype_in=parent`, `symbols=ES.FUT`) | **33,13424949347973 USD** | terminé 2026-09-03T08:11:37Z — **hors portée v3**, consigné comme dépense engagée, voir §8 pour l'implication méthodologique (pas seulement comptable) |
| **Total Databento à ce jour** | **63,18261136114597 USD** | somme des deux jobs GLBX.MDP3 ci-dessus (+ 3 jobs d'exemple MSFT à 0 USD, hors périmètre IQIA, non comptés) |
| Statut de licence CME | **TODO, non résolu** | inchangé depuis v3 §2.3 — questionnaire Databento non complété à ce jour |

**Verdict contrôle 6 : CONFORME** sur la structure comptable du job vérifié (coût exact, cohérent avec le gel) ; **voir §8** pour ce que la simple présence du job `parent` révèle sur l'antériorité par rapport au tag v3.

---

## Contrôle 7 — MDE (effet minimal détectable)

Calculé et **inséré dans `QDE-022_OrderFlow_L2_PreRegistration.md` §6.4** (seul TODO du document modifié, comme demandé — la section porte le numéro §6.4 dans le document réel ; le message de tâche la référençait comme « §9 », correction de renvoi apportée dans ce rapport).

Méthode : `n_eff(h)` = borne supérieure théorique = (23 400 s RTH / `h`) × 22 jours ouvrés, sans rejet de carnet croisé/bord de session (donnée non disponible sans rejouer l'état du carnet — hors périmètre de ce contrôle). MDE via approximation Fisher-z, puissance 0,80, deux bornes d'`alpha` : 0,05 non corrigé (indicatif) et 0,05/24 (borne Holm la plus stricte possible sur la famille de 24 tests).

| `h` (s) | `n_eff` (majorant) | MDE (α=0,05) | MDE (α=0,05/24) |
|---|---|---|---|
| 1 | 514 800 | 0,0039 | 0,0055 |
| 5 | 102 960 | 0,0087 | 0,0122 |
| 30 | 17 160 | 0,0214 | 0,0299 |
| 60 | 8 580 | 0,0302 | 0,0423 |
| 300 | 1 716 | 0,0676 | 0,0944 |
| 900 | 572 | 0,1169 | 0,1629 |

**Rappel :** ce tableau documente la puissance statistique de l'échantillon ; il n'entre dans **aucun** critère de décision de §7 (le seuil reste économique, `M(X,h) ≥ 0,80` tick ES, §7.3).

---

## §8 — Constat critique : antériorité du job `parent` par rapport au tag v3

**Chronologie établie via `batch.list_jobs` et `git log` :**

| Horodatage (UTC) | Événement |
|---|---|
| 2026-09-01 | v1/v2 gelés et taggés (portée : avril 2026, `ESM26`) |
| **2026-09-03T07:43:23Z** | **Job `GLBX-20260903-7XE859LGPE` soumis** — `stype_in=parent`, `symbols=ES.FUT`, `mbp-10`, **2026-08-02 → 2026-09-02** (déjà la fenêtre août/ESU6) |
| 2026-09-03T08:11:37Z | Job `parent` terminé — **vraies données de marché ESU6 (parmi d'autres contrats ES) téléchargées**, 193 356 682 enregistrements, 33,13 USD facturés |
| 2026-09-04T18:39:53Z | **Commit `2dd383a` (v3) créé** — première fois qu'un tag/document couvre explicitement la portée août 2026 / `ESU6` |

**Le job `parent` a livré de vraies données de marché sur la portée exacte (`ESU6`, `mbp-10`, 2026-08-02 → 2026-09-02) environ 35 heures avant que cette portée n'existe sous forme de pré-enregistrement gelé.**

Cela contredit factuellement l'affirmation de v3 §8.7 : *« Août 2026 est choisi a priori […] aucune donnée n'a été acquise pour avril ni pour août avant ce changement, donc ce n'est pas un re-choix après résultat. »* Cette phrase, écrite lors de la rédaction de v3 (2026-09-04), **était incorrecte** au moment où elle a été écrite : des données réelles sur cette portée existaient déjà depuis la veille.

**Portée exacte du risque, sans l'exagérer :**
- Les **4 indicateurs (§3), les 6 horizons (§4), la comptabilité Holm (§5) et la règle de décision incluant le gate économique (§7)** sont gelés **depuis v1 (2026-09-01)** — **avant** l'existence même du job `parent` (2026-09-03). Aucun de ces paramètres n'a donc pu être choisi ou ajusté après exposition à de vraies données.
- Le risque est **circonscrit au choix de la période et du contrat** (août 2026 / `ESU6`) — exactement le paramètre que v3 a changé, et exactement celui pour lequel l'affirmation « a priori » est maintenant invérifiable : il n'est pas possible d'exclure, sur la seule base des logs disponibles, qu'une personne ait consulté un aperçu des données `parent` avant de proposer ce changement de portée à v3.
- Ce contrôle d'intégrité **ne peut pas trancher** cette question (elle porte sur l'intégrité du processus de décision humain/agent, pas sur les fichiers de données) — elle est donc **rapportée, pas résolue**, conformément au mandat de ce document (vérification de conformité, aucune modification du pré-enregistrement au-delà du seul TODO §6.4).

**Ce constat n'a pas été corrigé dans `QDE-022_OrderFlow_L2_PreRegistration.md`** au-delà du TODO §6.4 explicitement autorisé — corriger l'affirmation erronée de §8.7 constituerait une modification de fond d'un document gelé, hors du mandat de cette vérification, et doit faire l'objet d'un amendement explicite et tagué (v4), pas d'une correction silencieuse.

---

## §9 — Recommandation

Un amendement **v4** est recommandé avant toute exécution de la sonde d'analyse (`qde022_l2_ofi.py`), avec deux volets :

1. **Correction factuelle obligatoire** de v3 §8.7 : remplacer l'affirmation « choix a priori, aucune donnée acquise » par la chronologie exacte ci-dessus (§8 de ce rapport).
2. **Décision explicite de l'utilisateur, à trancher avant analyse**, entre :
   - **(a) Accepter le risque résiduel**, documenté comme menace à la validité dans QDE-022-R, au motif que seul le choix période/contrat est exposé (indicateurs, horizons, gate figés depuis v1, avant le job `parent`) — et poursuivre l'analyse sur le jeu de données déjà conforme (`GLBX-20260904-93PSB55DT3`) ;
   - **(b) Écarter le risque entièrement** en basculant vers la période de repli déjà prévue en v3 (**juillet 2026, `ESU6`**), qui n'a **jamais** fait l'objet du job `parent` ni d'aucun autre téléchargement à ce jour — nécessite un nouveau `get_cost` + `batch.submit_job` sous un nouveau tag `qde-022-prereg-v4`.

Ce choix relève d'une décision de portefeuille de recherche, pas d'un contrôle technique — il n'est pas tranché par ce rapport.

### Résolution (2026-09-05)

L'utilisateur a tranché l'option **(a)** ci-dessus : **risque résiduel accepté et documenté**, août 2026 conservé comme échantillon unique, aucun achat supplémentaire, budget restant réservé à un élargissement conditionné au résultat de la courbe de décroissance. Le volet 1 (correction factuelle de §8.7) a été appliqué dans **v4** (`QDE-022_OrderFlow_L2_PreRegistration.md`, tag `qde-022-prereg-v4`) — voir cette section pour le raisonnement complet (points a-f) justifiant la décision. Cette recommandation §9 est donc **traitée**, dans son volet 1 comme dans son volet 2(a) ; elle est conservée ici telle qu'écrite le 2026-09-04, sans réécriture, pour que le lecteur voie la question posée avant de voir la réponse.

---

## Tableau récapitulatif des verdicts

| Contrôle | Verdict |
|---|---|
| 1. Conformité au gel (`metadata.json`) | CONFORME |
| 2. Unicité du contrat | CONFORME |
| 3. Inventaire des jours | CONFORME |
| 4. Qualité déclarée (`condition.json`) | SANS OBJET *(2026-08-29, samedi, hors domaine RTH de la méthodologie ; aucun jour ouvré concerné)* |
| 5. Comptage des enregistrements | CONFORME |
| 6. Coût et licence | CONFORME (structure), voir §8 |
| 7. MDE | Renseigné (§6.4 du pré-enregistrement) |
| **Constat §8 (antériorité job `parent`)** | **Résolu par v4** — était NON CONFORME (processus) le 2026-09-04, voir « Historique du verdict » en tête de document |

**Verdict global au 2026-09-04 (archivé, ne plus utiliser comme état courant) : NON CONFORME**, du seul fait du constat §8. **Verdict global courant (depuis 2026-09-05, voir tête de document) : CONFORME**, v4 ayant corrigé l'inexactitude factuelle de §8.7 sans que la structure des fichiers de données n'ait jamais été en cause. **Aucune analyse (OFI/IC/gate) n'est autorisée par ce rapport**, quel que soit le verdict.

---

## Addendum — relecture du 2026-09-04 (trois incohérences mineures)

Trois points signalés à la relecture de ce rapport ont été vérifiés. Correctifs appliqués **au rapport uniquement** ; le pré-enregistrement gelé n'a pas été modifié (sauf accord explicite déjà donné pour le §6.4 lors de la rédaction initiale — voir point 3 ci-dessous pour un second point, non résolu, qui requiert une confirmation séparée).

### Point 1 — Écart 26 vs 27 fichiers

Vérification directe :
- `ls *.dbn.zst` dans `QDE-022_Data\GLBX-20260904-93PSB55DT3\` → **27 fichiers** sur disque.
- `manifest.json` → **27 entrées `.dbn.zst`** + `condition.json` + `metadata.json` = 29 entrées au total (le manifeste ne se liste pas lui-même ; 27 + 2 + `manifest.json` lui-même = 30 fichiers sur disque, cohérent).
- Recomptage exhaustif : disque, `manifest.json` et le rapport (contrôles 2, 3, 5) s'accordent **tous les trois sur 27**.

**Le chiffre « 27 » du rapport est correct et n'a pas été modifié.** La source du chiffre « 26 » n'est ni `batch.download()` (dont la sortie brute liste bien 27 chemins `.dbn.zst`, plus `condition.json`/`metadata.json`/`manifest.json`) ni ce rapport : c'est une erreur de frappe dans mon propre résumé en conversation (*« 26 fichiers journaliers .dbn.zst »*), au moment d'annoncer la fin du téléchargement, jamais reprise dans un document commité. Aucune correction nécessaire ici ; **contrôle 3 reste cohérent** avec le chiffre de 27 (22 jours ouvrés + 5 dimanches, 4 samedis absents).

### Point 2 — Reclassement du 2026-08-29

Traité ci-dessus (contrôle 4) : reclassé **ÉCART MINEUR → SANS OBJET**, tableau récapitulatif mis à jour. Vérifications : samedi confirmé par calcul, aucun jour ouvré non-`available` dans `condition.json`.

### Point 3 — Emplacement du MDE

Numérotation réelle du document `QDE-022_OrderFlow_L2_PreRegistration.md` (v3), vérifiée par relecture complète des en-têtes `##`/`###` :

- **§6.4** (« Effet minimal détectable (MDE) ») est la section où le TODO MDE a **toujours** vécu, depuis v1/v2 (`### 6.4 Effet minimal détectable (MDE) — TODO après lecture du premier fichier journalier`) — ce n'est pas un emplacement nouveau ni déplacé, c'est celui où il a été rempli en place lors de la vérification d'intégrité initiale.
- **§9 réel du document** = « Hors périmètre — INTERDITS GELÉS » — une tout autre section (interdits : schéma `mbo`, 5ᵉ indicateur, horizons hors liste, backtest, etc.), sans rapport avec le MDE. La référence à « §9 » dans la consigne initiale de cette tâche était donc une erreur de numérotation dans la consigne elle-même, pas un signe que le MDE aurait été mal placé.
- **§6.4 est le bon emplacement. Confirmé, rien à déplacer.**

**Un TODO résiduel a cependant été repéré ailleurs, hors du périmètre strict de la question posée :** §8, menace à la validité n°6, ligne inchangée depuis v1/v2 : *« `n`, `n_eff` et MDE réels sont **TODO** (§6.4) — non chiffrés ici. »* Cette phrase est désormais **obsolète** — §6.4 contient des chiffres depuis le commit `0bc5311`. C'est une incohérence interne du document gelé, découverte pendant cette relecture, distincte de la question posée (qui portait sur l'emplacement, pas sur les renvois croisés).

**Conformément à la consigne (« ne pas modifier le pré-enregistrement sauf confirmation explicite »), cette phrase n'a PAS été corrigée.** Elle est seulement signalée ici : §8, menace n°6, à mettre à jour de *« TODO — non chiffrés ici »* vers un renvoi factuel vers le tableau MDE de §6.4, **si et seulement si l'utilisateur confirme** qu'une telle modification (purement éditoriale, aucun chiffre ni conclusion changée) peut être appliquée au document gelé sans nouveau tag.

---

**Commit de cet addendum :** voir hash reporté dans la réponse de la tâche (fichier modifié : ce rapport uniquement — `QDE-022_OrderFlow_L2_PreRegistration.md` non touché).
