# QDE-012 — Sprint 15.25 — Lot 15.7 — Structural Break: Temporal Semantics & Freshness Decision Audit

**Date** : 2026-08-26
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.6
**Statut** : **AUDIT COMPLETE**
**Type** : **AUDIT SCIENTIFIQUE + DÉCISION D'ARCHITECTURE — AUCUNE MODIFICATION DE PRODUCTION**

*(Note méthodologique : ce lot réutilise intégralement les mesures empiriques déjà produites au Lot 15.6 — même dataset Yahoo MES M5, même fenêtre temporelle, code source inchangé entre les deux lots — plutôt que de relancer une analyse identique. Aucune nouvelle donnée n'était nécessaire pour trancher les questions posées ici, qui sont architecturales, pas empiriques.)*

---

# 1. EXECUTIVE SUMMARY

Le Lot 15.6 a laissé deux questions ouvertes : (a) le modèle conceptuel Detection→Confirmation→Freshness→Persistence→Active State est-il réellement nécessaire dans son intégralité ? (b) comment définir Freshness sans inventer un paramètre ?

**Réponse (a), la simplification centrale de ce lot** : **Non — le modèle à 5 étages se réduit à 3 étages réellement nécessaires.** `FusionStateManager` possède déjà un mécanisme de persistance (EMA+hystérésis) appliqué à TOUTE dimension qui y entre. Construire un second mécanisme de "Freshness"+"Persistence" AVANT que l'evidence n'atteigne la Fusion créerait un **double retard** — exactement le risque identifié au Lot 15.6 §19/§7 de ce lot. Le modèle se réduit à : **Detection → Confirmation (gratuite, sans paramètre) → [Fusion existante prend le relais pour la persistance] → Active State.** "Freshness" et "Persistence" en tant que COUCHES SÉPARÉES sont supprimées du contrat, pas parce qu'elles sont inutiles conceptuellement, mais parce qu'un mécanisme ÉQUIVALENT existe déjà en aval et qu'en ajouter un second serait une duplication architecturale, jamais justifiée scientifiquement.

**Réponse (b)** : **FRESHNESS PARAMETER NOT YET IDENTIFIED — déclaré explicitement, comme l'exige le brief.** Aucune des 5 familles de modèles (Hard Expiration, Linear Decay, Exponential Decay, Event+State, No Explicit Freshness) ne peut être choisie sans un paramètre libre, SAUF la dernière (E), qui ne calcule aucune decay du tout. **Modèle E retenu** — non par élimination faute de mieux, mais parce que c'est la seule option cohérente avec la découverte ci-dessus (la persistance existe déjà en aval).

**Distinction clarifiée (Section 4/5), non résolue explicitement au Lot 15.6** : `DetectionTimestamp` (le moment où l'algorithme calcule un résultat — TOUJOURS "maintenant", puisque `CusumEvidence`/`BaiPerronEvidence` sont recalculées à neuf chaque barre) est **trivial et peu informatif en soi** — il vaut toujours l'horodatage de la barre courante. L'information réellement intéressante est entièrement portée par `BreakLocation` (l'estimation statistique RÉTROSPECTIVE de QUAND le changement a eu lieu, jusqu'à ~100 barres dans le passé, Lot 15.6 §11). **Conséquence directe pour le contrat** : `DetectionTimestamp` n'a pas besoin d'être un champ dédié — c'est redondant avec l'horodatage de barre déjà universellement disponible.

**Architecture recommandée (Section 21-22)** : **State-based, SANS couche de Freshness explicite** — l'option qui obtient le meilleur score sur tous les critères autorisés (causalité, simplicité, absence de double retard, cohérence Fusion, symétrie live/backtest), jamais sur la performance.

**Contrat minimal proposé, réduit par rapport au Lot 15.6** : `Detected (bool), Strength (double, = Cusum.Confidence), BreakLocationBarIndex (int?, brut, diagnostic), Source (enum), Agreement (enum, gratuit)`. `AgeBars`/`Freshness`/`Persistence`/`DetectionTimestamp` explicitement retirés ou fusionnés.

**Aucune modification de production. Aucune calibration. Aucun paramètre choisi.**

---

# 2. LOT 15.6 CONTEXT

CUSUM exploitable (dynamique, 340 séquences, Confidence saine). Bai-Perron : `BreakCount>0` quasi-omniprésent (99.98%), `Confidence` saturée. Detection ≠ Active State. Freshness insuffisamment définie. Risque de double comptage Cusum×BaiPerron (fenêtres imbriquées). `FusionStateManager` possède déjà EMA/hystérésis/persistance. AUGMENT recommandé (jamais Replace). Aucune implémentation.

---

# 3. EVENT VS STATE

| | Modèle A — Event | Modèle B — State |
|---|---|---|
| Sémantique | "Une rupture vient d'être détectée" — ponctuel | "Le marché EST actuellement dans un état structurellement différent" — continu |
| Avantages | Correspond littéralement à ce que CUSUM/BaiPerron calculent (un test statistique par barre) ; pas d'ambiguïté sur la durée de vie du signal | Cohérent avec les 4 autres dimensions Fusion (toutes des états lissés) ; s'intègre sans nouvelle infrastructure |
| Limites | Nécessite un mécanisme de décroissance explicite pour être utile à `Decision` (qui lit un état, pas un flux d'événements) — exactement le problème Freshness | Risque de conflation Detection/Active State si mal implémenté (le piège déjà identifié Lot 15.6) |
| Causalité | OK, chaque événement est causal | OK, la valeur d'état à `t` ne dépend que de `bars≤t` |
| Downstream (Decision) | `DecisionEngine`/`DecisionArbitrator` n'ont aucune notion "d'événement" — ils lisent un `FusionResult` par barre, un ÉTAT. Un modèle Event pur nécessiterait un traducteur événement→état, réintroduisant B | **Compatible nativement** — `FusionResult`/`FusionDimension` sont déjà des états, pas des événements |
| Interaction FusionStateManager | Incompatible tel quel — `FusionStateManager.Update` attend un `FusionResult` (état instantané), jamais un flux d'événements | Compatible directement — une nouvelle `FusionDimension` reçoit exactement le même traitement EMA+hystérésis que les 4 existantes |

**Conclusion** : le modèle Event PUR est architecturalement incompatible avec `Decision`/`FusionStateManager` sans être d'abord traduit en état — ce qui revient, de fait, à réintroduire le modèle B. **Le modèle State est retenu**, non par préférence esthétique mais parce que c'est la seule option qui s'intègre sans traducteur intermédiaire supplémentaire (lui-même un risque de double retard).

---

# 4. TEMPORAL ANCHOR

**Pour CUSUM** : le "moment où l'information devient causalement disponible" est **exactement la barre courante** — `CusumEvidence.Compute` est une fonction pure, recalculée à neuf à chaque barre à partir de la fenêtre glissante de 30 barres qui se termine à la barre courante (confirmé Lot 15.2). Il n'y a pas de délai de calcul, pas de latence — la valeur EST disponible à `t` pour la barre `t`.

**Pour Bai-Perron** : identique structurellement — recalculé à neuf chaque barre à partir de la fenêtre de 128 barres se terminant à `t`. La disponibilité CAUSALE est immédiate (à `t`). Ce qui n'est **PAS** immédiat, c'est la LOCALISATION du changement lui-même DANS le passé (`Breakpoints`), qui peut pointer jusqu'à ~100 barres en arrière (Lot 15.6 §11) — mais cette estimation rétrospective EST ELLE-MÊME disponible dès `t`, jamais plus tard.

**Distinction formalisée (brief §4)** :
```
DetectionTimestamp = t                                    (toujours "maintenant", par construction)
BreakLocation       = t - BreakAgeBars                     (une estimation, potentiellement ancienne, mais CONNUE dès t)
```

---

# 5. DETECTION TIMESTAMP

**Verdict : `DetectionTimestamp` est un champ TRIVIAL, jamais informatif en tant que tel.** Puisque `CusumEvidence`/`BaiPerronEvidence` sont des fonctions pures recalculées chaque barre, `DetectionTimestamp` vaut **toujours** l'horodatage de la barre courante — une information que TOUT consommateur possède déjà implicitement (c'est la barre qu'il est en train de traiter). L'inclure comme champ dédié dans le contrat serait une duplication sans valeur ajoutée. **Retiré du contrat final (§19).**

---

# 6. BREAK LOCATION

**Le champ réellement porteur d'information temporelle.** Traduction (formule Lot 15.6 §11/§12) : `BreakLocationBarIndex = CurrentBarIndex - (SampleSize - 1 - LocalIndex)`, avec le garde-fou contre le cas `LocalIndex == SampleSize` (jamais observé sur le dataset, mais réel dans le code de `CusumStatistics`). C'est une estimation STATISTIQUE, jamais une certitude — elle peut changer d'une barre à l'autre à mesure que la fenêtre glisse et que la segmentation BIC se recalcule entièrement (Bai-Perron en particulier n'a AUCUNE garantie de stabilité de `Breakpoints` d'une barre à l'autre, puisque c'est une ré-optimisation globale complète à chaque appel).

---

# 7. BREAK AGE

**Question posée (brief §5) : l'âge doit-il partir de la Détection ou de la Localisation ?**

Réponse, sans ambiguïté maintenant que §5 a établi que `DetectionTimestamp` vaut toujours `t` : les deux formulations sont **mathématiquement identiques** —
```
CurrentBar - DetectionBar     = CurrentBar - t = 0            (toujours nul, sans intérêt)
CurrentBar - BreakLocationBar = CurrentBar - (t - AgeBars) = AgeBars   (la seule quantité informative)
```
Il n'y a donc pas de choix à faire entre deux définitions concurrentes — **une seule est mathématiquement non triviale : l'âge doit être mesuré depuis `BreakLocation`, jamais depuis `DetectionTimestamp`** (qui donnerait toujours zéro). `BreakAgeBars` retenu tel que déjà défini au Lot 15.6 (mean=21.08, médiane=17, sur données réelles).

---

# 8. FRESHNESS

**Question centrale du lot.** Comparaison des 5 modèles, **sans choisir aucun paramètre libre** :

| Modèle | Paramètre libre requis | Verdict |
|---|---|---|
| A. Hard Expiration (N barres) | `N` — **interdit d'inventer** (brief §17) | **Rejeté** — aucune valeur de N scientifiquement fondée n'existe |
| B. Linear Decay | Pente/durée totale — **interdit** | **Rejeté** — même problème |
| C. Exponential Decay | Half-life — **interdit** | **Rejeté** — même problème |
| D. Event + State | Nécessite malgré tout une fonction de décroissance pour convertir l'événement en contribution d'état — **retombe sur A/B/C** | **Rejeté** — ne résout pas le problème, le déplace |
| **E. No Explicit Freshness** | **Aucun** | **RETENU** |

**Justification du choix E, jamais par défaut faute d'alternative, mais par cohérence architecturale positive (§9)** : `FusionStateManager` applique DÉJÀ un EMA (Alpha=0.20) + hystérésis (seuil=0.03) à toute dimension qui y entre — c'est-à-dire un mécanisme de décroissance/persistance temporelle **déjà existant, déjà testé (Lot 14.17), déjà cohérent avec les 4 autres dimensions**. Ajouter une SECONDE fonction de décroissance (Freshness) AVANT que la nouvelle evidence n'atteigne cette Fusion reviendrait à empiler deux mécanismes de lissage temporel INDÉPENDANTS sur la même information — un double retard, jamais justifié.

**Déclaration explicite requise par le brief §17** : **FRESHNESS PARAMETER NOT YET IDENTIFIED.** Aucune fonction de décroissance ne peut être définie aujourd'hui sans un paramètre libre non fondé scientifiquement. Ceci n'est pas un échec du lot — c'est exactement la question à laquelle il devait répondre, et la réponse honnête est qu'aucune n'est nécessaire compte tenu de §9.

---

# 9. DOUBLE PERSISTENCE

**Comptage explicite des étages temporels déjà présents dans le pipeline, AVANT tout ajout** :

```
Étage 1 : Fenêtre glissante CUSUM (30 barres) / Bai-Perron (128 barres)
          → un premier lissage/retard INHÉRENT à l'algorithme lui-même
          (Bai-Perron : jusqu'à 100 barres de retard sur la localisation, Lot 15.6 §11)

Étage 2 : FusionStateManager EMA(alpha=0.20) + hystérésis(seuil=0.03)
          → un second lissage, appliqué à CHAQUE dimension Fusion existante,
          démontré geler jusqu'à 95%+ des mouvements bruts (Lot 14.17)
```

**Deux étages existent DÉJÀ, avant même d'ajouter StructuralBreak.** Un troisième étage (Freshness/decay) et un quatrième (une persistance dédiée, distincte de celle de `FusionStateManager`) empileraient un retard sur un retard sur un retard — chaque étage supplémentaire éloigne davantage le signal final de l'événement de marché réel. **Objectif explicite du brief atteint : éviter un second système de persistance qui ferait doublon avec `FusionStateManager` — répondu en n'en créant aucun.**

---

# 10. FUSIONSTATEMANAGER INTERACTION

Si une nouvelle `FusionDimension` (ex. `StructuralBreakEvidence`) était câblée à l'avenir, elle traverserait **exactement** le même code que les 4 dimensions existantes (`FusionStateManager.BuildStableResult`, EMA puis hystérésis) — **aucune modification de `FusionStateManager` lui-même ne serait nécessaire** (confirmé par lecture : la boucle sur `Dimensions` — Lot 15.2 §11 — itère déjà sur un tableau statique de `FusionDimension`, auquel une valeur additionnelle s'ajouterait naturellement sans changer la logique). C'est l'argument architectural le plus fort en faveur du modèle State (§3) : **zéro nouvelle logique de persistance à écrire, zéro risque de divergence avec le mécanisme existant.**

---

# 11. CUSUM TEMPORAL ANALYSIS

Repris du Lot 15.6 (même dataset, même code, aucune raison de remesurer) : 340 séquences de détection sur 11 421 barres, longueur moyenne 31.3 barres, médiane 21, maximum 235. **Comportement clairement intermittent** — alterne entre périodes "détecté" et "non détecté" tout au long du dataset (770 barres non détectées au total, réparties en gaps entre les 340 runs). **CUSUM ressemble davantage à une information d'ÉTAT à variation modérée qu'à un événement ponctuel isolé** — ni parfaitement statique (comme Bai-Perron), ni un flash instantané d'une seule barre.

---

# 12. BAI-PERRON TEMPORAL ANALYSIS

Repris du Lot 15.6 : sous l'opérationnalisation `BreakCount>0`, seulement **2 séquences sur tout le dataset** (11 419/11 421 barres "détectées"), l'une couvrant 11 357 barres consécutives. **Ceci n'est PAS une information temporelle complémentaire à CUSUM — c'est essentiellement une CONSTANTE** sous cette définition. La seule variation réelle de Bai-Perron réside dans `BreakCount` (magnitude, gradient de régime réel) et `BreakLocation`/`AgeBars` (position, mean=21 barres) — jamais dans le fait binaire "détecté ou non", qui n'apporte quasiment aucune information.

---

# 13. CUSUM/BAI-PERRON AGREEMENT

Repris du Lot 15.6 : `Both`=93.24%, `BaiPerronOnly`=6.74%, `CusumOnly`=0.02%, `Neither`=0%. Écart de localisation moyen quand les deux détectent : 8.9 barres (médiane 7, max 83).

**Concept "Agreement" scientifiquement justifié (jamais un vote)** : la catégorie (`Both`/`CusumOnly`/`BaiPerronOnly`/`Neither`) est un fait OBSERVÉ, sans paramètre — retenue dans le contrat minimal (§19). Un score numérique combinant les deux (ex. moyenne pondérée) NÉCESSITERAIT un choix de pondération non justifié — **rejeté**, conformément à l'interdiction explicite du brief.

---

# 14. DOUBLE COUNTING

Repris et confirmé du Lot 15.6 (§17 de ce rapport-là) : **CUSUM (30 barres) et Bai-Perron (128 barres) partagent la même source de prix, avec des fenêtres imbriquées** — 93.24% d'accord "Both" confirme qu'un même événement de marché déclenche fréquemment les deux simultanément. **Réponse à la question du brief §11 : CUSUM et Bai-Perron doivent rester DEUX evidences distinctes dans le contrat (jamais fusionnées en une seule mesure a priori), MAIS leur combinaison éventuelle dans une future Fusion doit traiter explicitement leur non-indépendance** (jamais les sommer/moyenner comme si elles étaient statistiquement indépendantes) — documenté, pas résolu ici (une décision de pondération, hors mandat de ce lot).

---

# 15. STRUCTURALSTABILITY

**Question du brief : `StructuralStability` est-elle déjà une forme de `StructuralBreak State` ?**

**Réponse : partiellement, en esprit, jamais en substance.** `StructuralStability` (via `StructuralStabilityRule.EvaluateAnalysis`/`FusionProfileAnalyzer`) mesure la variabilité RÉCENTE des 4 AUTRES dimensions Fusion **déjà stabilisées** (`BehaviourConsistency`/`ProfileStability`/`ProfileVelocity`) — c'est un signal **ENDOGÈNE** (relatif au pipeline Fusion lui-même) qui répond à "la classification du marché a-t-elle été instable récemment ?". `Cusum`/`BaiPerron` sont **EXOGÈNES** (dérivés directement du prix) et répondent à "existe-t-il une preuve statistique indépendante d'un changement de niveau/variance dans le PRIX ?". Ce sont deux questions apparentées mais distinctes — un marché peut connaître une VRAIE rupture de prix (CUSUM/BaiPerron positifs) sans que la CLASSIFICATION de régime ait encore eu le temps de devenir instable (StructuralStability encore élevée), et inversement.

**Ce qui manque réellement** (justifiant Augment, jamais Replace, confirmé une seconde fois) : une confirmation INDÉPENDANTE, basée sur le prix, que `StructuralStability` ne peut structurellement pas fournir puisqu'elle ne regarde jamais le prix directement — seulement les dimensions Fusion déjà dérivées.

---

# 16. MARKETSTATE SEMANTICS

Reconfirmé du Lot 15.6, sans changement : `MarketState.StructuralBreak` est une **CLASSIFICATION** (argmax par barre de `DecisionArbitrator`), jamais un événement, jamais un état ontologique. **Dette architecturale déclarée, non résolue, hors du périmètre de correction de ce lot** (toucherait `DecisionArbitrator`, protégé).

---

# 17. TRANSITIONAL

**Réponse explicite à l'interdiction du brief ("NON simplement parce que les noms semblent liés")** : `StructuralBreak` et `Transitional` restent des concepts **architecturalement indépendants** — rien dans le code ne les relie, et `Transitional` demeure structurellement inatteignable comme `Decision.Winner` (Lots 15.0-15.6, reconfirmé). Une rupture structurelle PEUT conceptuellement correspondre à un événement temporaire, un changement durable, une transition, ou un faux signal — l'architecture actuelle **n'a aucun moyen de distinguer ces quatre cas**, et ce lot ne cherche pas à en construire un (hors mandat). `Transitional` doit rester une classification indépendante, jamais automatiquement PRODUITE par `StructuralBreak` — aucune modification proposée dans cette direction.

---

# 18. ACTIVE STRUCTURAL STATE

Définition formelle candidate (non implémentée) :

```
QUOI ?                Une valeur continue [0,1], produite par une nouvelle FusionDimension,
                      alimentée par Cusum.Confidence (principalement — sain, non saturé) et
                      BaiPerron.BreakCount (comme modulateur de magnitude, jamais comme
                      booléen quasi-constant).
QUAND ?               Recalculée chaque barre, exactement comme les 4 dimensions existantes.
JUSQU'À QUAND ?       Aussi longtemps que FusionStateManager la maintient stable via son
                      EMA+hystérésis EXISTANT — AUCUNE durée de vie supplémentaire définie
                      par ce contrat lui-même (§8/§9 : pas de second mécanisme de persistance).
POURQUOI ?            Fournir à StructuralBreakRule une evidence indépendante du prix,
                      remédiant à l'absence actuelle (Lot 15.0/15.2).
AVEC QUELLE CONFIANCE ? Celle déjà produite par Cusum.Confidence (jamais BaiPerron.Confidence,
                      saturée) + le champ Agreement (gratuit) comme qualificatif descriptif.
SUR QUELLE INFORMATION ? Cusum.ChangeDetected/.Confidence, BaiPerron.BreakCount,
                      BreakLocationBarIndex/AgeBars (diagnostic uniquement, jamais un facteur
                      de score), Agreement.
```

---

# 19. MINIMAL CONTRACT

**Comparaison des 3 tiers proposés par le brief, décision explicite :**

| Tier | Champs | Verdict |
|---|---|---|
| Minimal | Detected, DetectionTimestamp, Strength, Source | `DetectionTimestamp` trivial (§5) — insuffisant en l'état mais proche |
| Temporal | + BreakLocation, AgeBars, Freshness | `Freshness` rejetée (§8) — le reste est utile |
| Composite | + Agreement, Persistence | `Persistence` rejetée (§9, déjà fournie par Fusion) — `Agreement` utile et gratuit |

**CONTRAT RETENU, un tier réduit sur mesure (ni Minimal, ni Temporal, ni Composite tels que proposés)** :

```
StructuralBreakEvidence (PROPOSÉ, NON IMPLÉMENTÉ) :

Detected              : bool                                — Cusum.ChangeDetected (jamais BaiPerron,
                                                                quasi-constant, §12)
Strength              : double [0,1]                         — Cusum.Confidence (sain, §11)
BreakCountMagnitude   : int >= 0                              — BaiPerron.BreakCount (informatif, jamais
                                                                comme booléen)
BreakLocationBarIndex : int? (ou AgeBars équivalent)          — diagnostic brut, JAMAIS un facteur de score
Agreement             : enum {Both, CusumOnly, BaiPerronOnly, Neither} — gratuit, préserve le désaccord
Source                : implicite (les 2 champs ci-dessus nomment déjà leur origine — pas de champ séparé nécessaire)

RETIRÉS DU CONTRAT, ET POURQUOI :
- DetectionTimestamp  : trivial, toujours "maintenant" (§5)
- Freshness           : FRESHNESS PARAMETER NOT YET IDENTIFIED (§8)
- Persistence         : déjà fournie par FusionStateManager (§9) — un second mécanisme ferait doublon
```

---

# 20. LOOK-AHEAD

| Proposition | Barres nécessaires | Dépendance future |
|---|---|---|
| Event (rejeté, §3) | t seulement | Aucune |
| State (retenu) | t seulement | Aucune |
| Freshness (rejetée) | N/A — jamais implémentée | N/A |
| BreakAge / BreakLocation | t seulement (déjà connu à t, §4) | Aucune |
| Confirmation / Agreement | t seulement | Aucune |

**Aucune proposition retenue ne requiert de donnée future.** Le seul piège demeure sémantique (traiter une `BreakLocation` ancienne comme un événement actuel), jamais causal — reconfirmé identique au Lot 15.6.

---

# 21. LIVE/BACKTEST SYMMETRY

Chaque champ du contrat retenu (§19) est calculable IDENTIQUEMENT en live et en backtest : `Cusum.ChangeDetected`/`.Confidence`, `BaiPerron.BreakCount`, `BreakLocationBarIndex` sont tous des sorties DÉJÀ produites par `RegimeEngine.Collect`, identique sur les deux chemins (aucune divergence backtest-only, contrairement au cas de `VolatilityStopLossModel`/réconciliation de prix des Lots 15.3/15.5 qui, eux, dépendaient spécifiquement du remplissage réel). **Symétrie confirmée triviale** — aucune adaptation nécessaire entre live et backtest pour ce contrat spécifique.

---

# 22. REGIME CONDITIONAL ANALYSIS

Repris du Lot 15.6 (§24 de ce rapport) : `StructuralBreak` a le %CusumDétecté et la CusumConfidence les plus élevés de tous les régimes (99.69%, 0.9992) — la seule différenciation cohérente. `BaiPerron` ne différencie presque rien (saturé partout). `StableRange` reste statistiquement fragile (N=113, historiquement ~1% du dataset). Aucune conclusion causale tirée d'un dataset de 59 jours — observation descriptive uniquement, comme au Lot 15.6.

---

# 23. DECISION MATRIX

| Architecture | Causalité | Simplicité | Freshness | Double retard | Fusion | Live |
|---|---|---|---|---|---|---|
| Event (pur) | OK | Faible (nécessite un traducteur état) | Non résolue | Risque (traducteur = 3ᵉ étage) | Incompatible sans traduction | OK |
| State + Freshness explicite (Lot 15.6 tel quel) | OK | Moyenne | Non résolue sans paramètre | **Risque confirmé** (§9, 3 étages) | Compatible | OK |
| Event + State | OK | Faible | Retombe sur Freshness (§8) | Risque | Incompatible sans traduction | OK |
| **State SANS Freshness explicite (retenu)** | **OK** | **Maximale** | **Explicitement non résolue, sans en avoir besoin** | **Aucun (2 étages seulement, déjà existants)** | **Compatible nativement** | **OK, trivial** |

---

# 24. RECOMMENDATION

**Architecture recommandée : State-based, sans couche de Freshness ni de Persistence dédiées — la nouvelle evidence, une fois formalisée (§19), s'intègre comme une `FusionDimension` supplémentaire consommant le mécanisme EMA+hystérésis DÉJÀ EXISTANT de `FusionStateManager`, sans aucune modification de ce dernier.**

Justification exclusive (brief §22, aucune mention de performance) :
- **Causalité** : chaque champ retenu est disponible à `t` sans exception.
- **Sémantique** : `DetectionTimestamp`/`Freshness`/`Persistence` ont été retirés non par paresse mais parce qu'ils sont soit triviaux (§5), soit non définissables sans paramètre libre (§8), soit déjà fournis ailleurs (§9) — chaque suppression est justifiée individuellement, jamais une simplification arbitraire.
- **Architecture** : zéro nouvelle infrastructure de persistance, zéro risque de divergence avec le mécanisme existant.
- **Symétrie live/backtest** : triviale, confirmée (§21).
- **Absence de double comptage** : Cusum et Bai-Perron restent deux champs distincts, jamais fusionnés a priori (§14).
- **Absence de double persistance** : un seul mécanisme de lissage temporel (`FusionStateManager`), jamais deux (§9).

---

# 25. FUTURE IMPLEMENTATION BOUNDARY

**Lot suggéré : "StructuralBreak Evidence Implementation"** (le nom "15.8" n'est pas imposé si un découpage différent s'avère préférable au moment venu).

**Fichiers probablement à créer** : un nouveau `Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs` (nouvelle `IFusionRule`, implémentant exactement le contrat §19 — jamais plus) ; une extension additive de l'enum `FusionDimension` (une nouvelle valeur, ex. `StructuralBreakEvidence`) ; l'ajout de cette règle à la liste déjà existante dans `BacktestEngine.RunSignalPipeline`/`IQIAIndicator.cs` (pattern déjà établi, une ligne).

**Fichiers protégés, à ne PAS toucher** : `CusumEvidence.cs`/`CusumStatistics.cs`/`BaiPerronEvidence.cs`/`BaiPerronStatistics.cs` (algorithmes déjà validés) ; `RegimeEngine.cs` (expose déjà tout ce qui est nécessaire) ; `StructuralStabilityRule.cs`/`FusionProfileAnalyzer.cs` (Replace rejeté, Lot 15.6 §20, reconfirmé §15 de ce lot) ; `FusionStateManager.cs` (aucune modification requise, §10 — c'est précisément le point) ; `DecisionArbitrator.cs`/`DecisionEngine.cs` ; les 4 autres `Decision.Rules` (`MeanRevertingRule`/`TrendingRule`/`RandomWalkRule`/`StableRangeRule` — aucune ne doit consommer cette nouvelle dimension sans justification explicite séparée).

**Décision explicite requise AVANT ce futur lot, non tranchée ici** : la repondération de `StructuralBreakRule` (4 poids existants somment à 1.00 ; un 5ᵉ input exige une décision de pondération, jamais une calibration de performance, mais une décision architecturale consciente).

**Le futur lot doit être** : minimal (un seul nouveau fichier de règle + une ligne de câblage), isolé (aucun fichier protégé touché), testable (golden dataset synthétique + tests de causalité/déterminisme, pattern déjà établi aux Lots 15.2-15.5), réversible (une nouvelle `IFusionRule`/`FusionDimension` peut être retirée de la liste sans toucher le reste de l'architecture).

---

# 26. CALIBRATION BOUNDARY

| Paramètre | Pourquoi calibrable | Données nécessaires | Validation |
|---|---|---|---|
| Poids de `StructuralBreakEvidence` dans `StructuralBreakRule` | Détermine l'influence relative de la nouvelle evidence vs `(1-StructuralStability)` existant | Aucune donnée supplémentaire nécessaire — décision architecturale, jamais empirique | Revue explicite par l'utilisateur, jamais un grid search |
| Seuil éventuel sur `Strength` (si un futur lot ajoute un seuillage) | Pourrait filtrer le bruit à faible confiance | Dataset plus long pour caractériser la distribution de `Cusum.Confidence` par régime au-delà de 59 jours | Walk-forward / OOS, jamais un seul dataset |
| Toute future fonction de decay (si §8 était un jour reconsidérée) | Nécessiterait de caractériser empiriquement la dynamique réelle de persistance d'une rupture | Dataset significativement plus long (mois/années), au-delà de ce qui existe aujourd'hui | Étude dédiée, hors de la portée de ce projet à ce stade |

**Aucun de ces paramètres n'a été sélectionné, estimé ou suggéré numériquement dans ce lot.**

---

# 27. RISKS

1. **Le contrat retenu (§19) écarte délibérément `BaiPerron` du champ `Detected` principal** (gardé uniquement comme `BreakCountMagnitude`) — un futur lot pourrait vouloir reconsidérer si `BaiPerron.BreakCount` devrait AUSSI contribuer à un `Detected` composite ; non tranché ici par choix (Cusum seul est déjà suffisamment discriminant, §11).
2. **`Agreement` reste un champ catégoriel, jamais un scalaire** — si un futur lot veut l'utiliser dans une formule numérique, une décision de conversion (jamais un vote arbitraire) devra être prise explicitement, hors de ce lot.
3. **La dette sémantique MarketState (classification vs événement vs état, §16)** reste non résolue et hors du périmètre de tout lot StructuralBreak — elle affecte potentiellement l'interprétation de TOUS les régimes, pas seulement StructuralBreak.
4. **Le dataset de 59 jours reste la limite fondamentale** de toute future calibration (§26) — non résolu, ne peut l'être sans une source de données étendue.

---

# 28. HANDOFF CONTEXT

```
PROJECT:
IQIA

CURRENT LOT:
15.7

LAST COMPLETED:
15.6

CALIBRATION:
PAUSED

EVENT VS STATE:
STATE retained. Event-pure model is architecturally incompatible with DecisionEngine/FusionStateManager
without an intermediate event-to-state translator, which would itself reintroduce the State model as a
3rd temporal layer - rejected on that basis, never on preference.

TEMPORAL ANCHOR:
Both Cusum and BaiPerron are causally available exactly at t (pure per-bar recomputation, no calculation
lag). What is NOT immediate is the retrospective BreakLocation estimate itself (up to ~100 bars in the
past, Lot 15.6), but that estimate is fully KNOWN at t, never delayed further.

DETECTION TIMESTAMP:
Confirmed TRIVIAL - always equals the current bar's own timestamp by construction (both evidence classes
are stateless, recomputed fresh every bar). REMOVED from the proposed contract - redundant with
information every consumer already has.

BREAK LOCATION:
The only temporally-informative field. BreakAgeBars = CurrentBarIndex - BreakLocationBarIndex is the sole
non-trivial age formula (CurrentBar - DetectionBar is always exactly 0, mathematically uninteresting -
resolves the Lot 15.7 brief's own §5 question definitively).

BREAK AGE:
Must be measured from BreakLocation, never from DetectionTimestamp (the latter is always "now", so that
formulation is always 0). Retained as a RAW DIAGNOSTIC field only - never a scoring input, never decayed.

FRESHNESS:
FRESHNESS PARAMETER NOT YET IDENTIFIED (declared explicitly per brief §17). All 5 candidate models
compared (Hard Expiration/Linear/Exponential/Event+State all require an unjustifiable free parameter;
Event+State just relocates the problem). Model E (No Explicit Freshness) chosen - not by elimination, but
because FusionStateManager ALREADY provides an equivalent temporal-smoothing mechanism (EMA+hysteresis)
that any new FusionDimension would automatically inherit - building a second, separate Freshness/decay
layer BEFORE Fusion would stack a redundant third temporal-lag layer on top of (1) Cusum/BaiPerron's own
rolling-window lag and (2) FusionStateManager's existing EMA+hysteresis (Lot 14.17: already freezes 95%+
of raw movements). See DOUBLE PERSISTENCE below.

DOUBLE PERSISTENCE:
Explicitly counted: 2 temporal-smoothing layers already exist before any StructuralBreak addition
(1: Cusum/BaiPerron's own rolling-window retrospective lag; 2: FusionStateManager's EMA+hysteresis, shared
by all 4 existing Fusion dimensions). Adding a bespoke Freshness/decay layer (a would-be 3rd layer) and a
separate Persistence mechanism (a would-be 4th layer) was explicitly rejected - the new evidence should
feed the EXISTING FusionStateManager mechanism directly, inheriting layer 2 without building a redundant
equivalent upstream of it.

FUSION STATE MANAGER:
Confirmed NO modification needed - FusionStateManager.BuildStableResult already iterates a static
FusionDimension array; a new dimension value would be picked up by the exact same EMA+hysteresis logic
with zero code changes to FusionStateManager itself. This is the strongest architectural argument for the
State model and for rejecting a separate Freshness/Persistence layer.

CUSUM:
Reused Lot 15.6 data (same dataset/code, no re-measurement needed): 340 detection runs, mean length 31.3
bars, median 21, max 235 - genuinely intermittent, state-like-with-moderate-variation behavior, neither a
one-bar event nor a near-constant.

BAI-PERRON:
Reused Lot 15.6 data: under BreakCount>0, only 2 runs total across 11421 bars (one spanning 11357
consecutive bars) - confirmed this is essentially a CONSTANT under this operationalization, not
complementary temporal information to Cusum. The only real Bai-Perron signal is BreakCount magnitude and
BreakLocation/AgeBars, never the binary "detected" fact.

CUSUM/BAI-PERRON AGREEMENT:
Reused Lot 15.6 data: Both=93.24%, BaiPerronOnly=6.74%, CusumOnly=0.02%, Neither=0%. Mean location gap
when both detect = 8.9 bars (median 7, max 83). Agreement retained as a categorical (never scalar,
never voted) contract field - free, no invented parameter.

DOUBLE COUNTING:
Reconfirmed from Lot 15.6: Cusum(30-bar)/BaiPerron(128-bar) windows are nested, same price source -
93.24% co-detection confirms non-independence. Kept as two SEPARATE contract fields (never pre-fused into
one), but any future Fusion combination must treat their dependency explicitly, never sum/average as if
independent - deferred to the implementation lot's explicit weighting decision.

STRUCTURAL STABILITY:
Answered explicitly: partially analogous in spirit (both relate to "instability"), never equivalent in
substance. StructuralStability is ENDOGENOUS (measures the Fusion pipeline's OWN recent output
variability, never touches price directly); Cusum/BaiPerron are EXOGENOUS (independent, price-based
statistical tests). This is exactly what's missing today and justifies Augment (not Replace) a second,
independent time.

MARKET STATE:
Reconfirmed unchanged from Lot 15.6: MarketState.StructuralBreak is a per-bar CLASSIFICATION (Decision
Arbitrator's argmax), never an event, never an ontological state - a real, undecided semantic debt,
outside this lot's (and any StructuralBreak-scoped lot's) mandate to resolve, since it touches
DecisionArbitrator (protected) and affects all 5 regimes' interpretation, not just StructuralBreak.

TRANSITIONAL:
Confirmed independent of StructuralBreak - no automatic equivalence, none proposed. Remains structurally
unreachable as Decision.Winner (Lots 15.0-15.7). The architecture has no current mechanism to distinguish
temporary/durable/transitional/false-signal breaks - not built here, out of mandate.

ACTIVE STRUCTURAL STATE:
Formal candidate definition constructed (report §18) - answers What/When/Until-when/Why/Confidence/
Basis-information - explicitly NOT implemented. "Until-when" is answered by "as long as
FusionStateManager's existing mechanism keeps it stable" - no separate lifetime defined by this contract.

MINIMAL CONTRACT:
A custom, reduced tier (narrower than the brief's own "Minimal" example) proposed: Detected (bool, =Cusum.
ChangeDetected), Strength (double, =Cusum.Confidence), BreakCountMagnitude (int, =BaiPerron.BreakCount),
BreakLocationBarIndex (int?, diagnostic only, never a scoring input), Agreement (enum, free). Removed
entirely: DetectionTimestamp (trivial), Freshness (no free-parameter definition exists), Persistence
(already provided downstream by FusionStateManager).

LOOK-AHEAD:
PASS for every retained field - all available at t with no future dependency, reconfirmed identical to
Lot 15.6's finding. The only remaining risk is semantic (treating an old BreakLocation as a current
event), never causal.

LIVE/BACKTEST:
Confirmed trivially symmetric - every retained contract field is a per-bar RegimeEngine.Collect output,
identical on both paths, unlike Lots 15.3/15.5's fill-price-dependent logic which needed explicit
reconciliation.

REGIME INTERACTION:
Reused Lot 15.6 data: StructuralBreak regime shows the highest CusumDetected rate (99.69%) and
CusumConfidence (0.9992) of all regimes - the only evidence with real cross-regime differentiation.
BaiPerron differentiates almost nothing (saturated everywhere). Descriptive only, no causal claim from a
59-day dataset.

RECOMMENDED ARCHITECTURE:
State-based, WITHOUT a separate Freshness or Persistence layer - the new evidence becomes a new
FusionDimension consumed by a new, isolated IFusionRule, feeding the EXISTING FusionStateManager
EMA+hysteresis mechanism unmodified. Justified exclusively by causality, architecture, live/backtest
symmetry, absence of double-counting and absence of double-persistence - never by any performance metric.

FUTURE IMPLEMENTATION:
A new, isolated Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs implementing the §19 contract exactly,
plus an additive FusionDimension enum value, plus a one-line addition to the existing rule-list
construction sites (BacktestEngine.cs/IQIAIndicator.cs). Explicitly requires, BEFORE that lot starts, an
explicit user decision on StructuralBreakRule's reweighting (4 existing weights summing to 1.00, a 5th
input needs a conscious architectural choice, never a calibration). No other file should be touched.

CALIBRATION PARAMETERS:
None touched. Documented for a LATER stage, after implementation: the new evidence's Fusion weight inside
StructuralBreakRule (architectural decision, not empirical), a possible future Strength threshold (would
need a longer dataset), any future reconsideration of the Freshness question (would need a dataset spanning
months/years, not the current 59 days).

P0 REMAINING:
StructuralBreak evidence integration remains the sole open item from the entire Lot 15.0-15.7 lineage - now
with BOTH a documented contract (Lot 15.6) AND a resolved temporal-semantics decision (this lot, State
without explicit Freshness/Persistence). The only remaining blocker is (a) explicit user approval of this
lot's recommendation, and (b) the StructuralBreakRule reweighting decision that must precede
implementation.

NEXT LOT:
"StructuralBreak Evidence Implementation" (name not mandatory) - scoped exactly per report §25: one new
Fusion rule file, one additive FusionDimension value, one-line wiring change at the two existing rule-list
construction sites, an explicit user-approved reweighting of StructuralBreakRule, golden-dataset +
causality + determinism tests following the established Lot 15.2-15.5 pattern. No file outside this exact
list should be touched.

DO NOT DO:
Do NOT build a Freshness decay function or a separate Persistence mechanism for StructuralBreak evidence -
explicitly rejected on architectural grounds (double-persistence risk, §9), not merely deferred. Do NOT
add a DetectionTimestamp field to any future contract - it is mathematically trivial (always "now"). Do
NOT use BaiPerron.BreakCount>0 or .Confidence as the primary "Detected"/"Strength" signal - both are
confirmed non-discriminant/saturated (Lot 15.6, reconfirmed here); Cusum.ChangeDetected/.Confidence must
be primary, BaiPerron.BreakCount a secondary magnitude modifier only. Do NOT modify FusionStateManager to
"support" the new evidence - it already supports it natively, zero changes needed, confirmed explicitly
(§10). Do NOT collapse the Agreement field into an arbitrary numeric vote without a separate, explicit
future decision. Do NOT assume StructuralStability and the new evidence measure the same thing (§15) -
they are endogenous vs exogenous, both needed, Augment reconfirmed for a second time across two lots.
```

**STOP.**
