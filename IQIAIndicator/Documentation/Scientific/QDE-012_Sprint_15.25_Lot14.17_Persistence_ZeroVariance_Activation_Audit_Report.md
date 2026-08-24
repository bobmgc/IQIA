# QDE-012 — Sprint 15.25 — Lot 14.17 — Persistence : Audit de la Variance Nulle & du Mécanisme d'Activation

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.13-14.16
**Statut** : **IMPLEMENTED**
**Type** : **AUDIT / INVESTIGATION — AUCUNE REPONDÉRATION, AUCUN SEUIL SÉLECTIONNÉ, AUCUNE MODIFICATION DE PRODUCTION**

---

# 1. EXECUTIVE SUMMARY

Le Lot 14.16 a découvert que `Persistence` présente une variance strictement nulle sur 6 des 40 jours du dataset. Ce lot répond, avec une preuve directe et sans ambiguïté, à la question POURQUOI.

**Réponse** : ce n'est ni un comportement de marché réel (Hypothèse A), ni un effet de fenêtre/warmup (B), ni un problème de données amont (C), ni une zone de saturation de la formule `Persistence` elle-même (E), ni un bug d'implémentation (H) — **c'est le mécanisme d'hystérésis de `FusionStateManager` (Hypothèses D+F combinées : Normalisation + Configuration)**, qui gèle la valeur STABLE consommée par `MeanRevertingRule`/`StableRangeRule` chaque fois que la valeur RAW (fraîchement calculée à partir de DFA/Variance Ratio, mesurée directement) se déplace de moins de `0.03` (le seuil d'hystérésis) depuis la dernière valeur stable retenue.

**Preuve directe, mesurée sur 10 930 barres réelles fraîches** :

```
Bar-à-bar : RawChangé & StableGelé (hystérésis absorbée) = 10 382 / 10 929 (95.0%)
            RawChangé & StableChangé                       =    547 / 10 929 (5.0%)
            RawGelé (le raw lui-même ne bouge pas)          =      0 / 10 929 (0.0%)
```

**Le RAW ne reste JAMAIS strictement constant** (0 transition "RawGelé" sur 10 929) — il bouge à CHAQUE barre, confirmé sur les 6 journées à variance nulle : par exemple le 2026-08-05, `PersistenceRaw` prend **275 valeurs distinctes sur 275 barres** (de 0.000001 à 0.168972), tandis que `PersistenceStable` reste figée à **exactement 0.132691** toute la journée. C'est une démonstration causale directe, pas une corrélation.

**Découverte supplémentaire, plus importante que les "6 jours" du Lot 14.16** : l'analyse par "séries gelées" (indépendante des frontières de jour calendaire) révèle des gels BEAUCOUP plus longs que ce que la classification quotidienne suggérait — la plus longue série atteint **926 barres consécutives (≈77.2 heures, plus de 3 jours) à la valeur EXACTE 0.136088**, du 2026-08-14 au 2026-08-19. Le chiffre "6 jours sur 40" du Lot 14.16 était donc une SOUS-ESTIMATION du phénomène réel, qui déborde largement les frontières calendaires.

**Zone d'activation** : l'analyse fine (30 bins à effectif égal) montre une transition PROGRESSIVE, pas un seuil dur, commençant vers `Persistence≈0.20-0.25` (cohérent avec l'observation du Lot 14.16, jamais présentée ici comme une valeur calibrée) et s'intensifiant nettement au-delà de `≈0.31`, avec `ScoreDifference` moyen devenant même NÉGATIF (StableRange en moyenne devant MeanReverting) dans le dernier bin (`Persistence∈[0.43,0.75]`, 3.4% du dataset).

**Classification de cause racine (§26)** : **NORMALIZATION SATURATION** (primaire, prouvée directement) + **FALLBACK/CONFIGURATION** (le seuil d'hystérésis `0.03` est le paramètre techniquement responsable) — avec **REGIME DEPENDENT** comme facteur secondaire (les gels sont plus fréquents pendant les périodes à faible mouvement de Persistence, typiquement MeanReverting/StructuralBreak, jamais pendant Trending où les mouvements dépassent naturellement le seuil).

**Aucune modification de production. Aucun poids touché. Aucun seuil calibré.**

---

# 2. LOT 14.16 FINDINGS (repris tels quels)

- 6/40 jours à variance nulle de `Persistence`.
- `Corr(Persistence,ScoreDifference)` : Pearson=-0.8140, Spearman=-0.4494 (écart révélateur).
- Dans MeanReverting : Pearson=-0.4977, Spearman=-0.3011.
- Zone de transition observée autour de `Persistence≈0.25` (jamais calibrée).
- Ablation : baseline≈0.9652, shared-removed≈0.108, persistence-removed≈0.9913.
- Robustesse : DATASET/REGIME-DEPENDENT (puissance statistique) + mécanisme causal STRONG.

---

# 3. SCIENTIFIC QUESTION

> Pourquoi `Persistence` présente-t-elle une variance strictement nulle certaines journées, et pourquoi son pouvoir discriminant semble-t-il apparaître uniquement au-dessus d'un certain niveau ?

Décomposée en 9 hypothèses (A à I) — évaluées §26.

---

# 4. PERSISTENCE ARCHITECTURE (chaîne complète tracée)

```
Raw Market Data (Close prices, buffer circulaire de 128 barres, RegimeEngine._priceBuffer)
    ↓
DfaEvidence.Compute / VarianceRatioEvidence.Compute (Engine/Regime/Evidence)
    → PURS, recalculés fraîchement à CHAQUE barre à partir de la fenêtre glissante courante
    → AUCUN cache, AUCUNE mémoïsation du résultat (confirmé par lecture directe, §7)
    ↓
DfaResult{Hurst, RSquared, Confidence, WindowCount, IsValid}
VarianceRatioResult{VarianceRatio, ZStatistic, PValue, Confidence, SampleSize, IsValid}
    ↓
PersistenceRule.Evaluate (Engine/Fusion/Rules/PersistenceRule.cs)
    → formule mathématique (§5) → FusionConfidence "RAW" (PersistenceRaw)
    ↓
FusionStateManager.Update (Engine/Fusion/State/FusionStateManager.cs)
    → EMA(alpha=0.20) PUIS hystérésis(seuil=0.03) → FusionConfidence "STABLE" (PersistenceStable)
    → C'EST ICI QUE LE GEL SE PRODUIT (§9/§21, preuve directe)
    ↓
MeanRevertingRule / StableRangeRule (consomment EXCLUSIVEMENT la valeur STABLE, jamais RAW)
```

---

# 5. FORMULA (rappel exact, Lot 14.16 §5, non modifiée)

```
dfaDirection      = tanh(6.0 × (Clamp(Hurst,0,2) - 0.5))
dfaPersistence    = dfaDirection² × sigmoid(4.0 × dfaDirection)
vrDirection       = tanh(2.0 × ln(max(VarianceRatio, 1e-12)))
vrPersistence     = PValueStrength(VR.PValue) × PersistenceSupport(vrDirection)
scientificScore   = 0.5 × (dfaPersistence + vrPersistence)
qualityScore      = 0.5 × (dfaQuality + vrQuality)
Persistence.Value = Clamp(0.90 × scientificScore + 0.10 × qualityScore, 0, 1)   ← ceci est "RAW"
```

**Existe-t-il une zone où `Persistence` (RAW) devient mathématiquement constante ?** Non — vérifié empiriquement (§9) : sur les 10 930 barres, **0 transition** montre un RAW strictement identique d'une barre à l'autre. Les fonctions `tanh`/`sigmoid` ont des zones de faible pente (loin de leur point d'inflexion) mais jamais une pente EXACTEMENT nulle sur un intervalle — la formule elle-même n'a pas de plateau plat.

**Ce que `FusionStateManager` fait APRÈS (jamais modifié dans ce lot)** :

```
smoothedValue = 0.20 × Clamp(rawValue) + 0.80 × Clamp(previousStableValue)     [EMA]
valueChanged  = |smoothedValue - previousStableValue| >= 0.03                  [Hystérésis]
newStableValue = valueChanged ? smoothedValue : previousStableValue            ← LE GEL EXACT
```

**Ici EXISTE une zone de constance mathématique explicite** : tant que `|smoothedValue - previousStableValue| < 0.03`, `newStableValue` est **BIT-IDENTIQUE** à `previousStableValue` — pas une approximation, une ré-assignation de l'ancienne valeur.

---

# 6. INPUT SOURCES

| Input | Influence | Variabilité réelle mesurée | Saturation possible |
|---|---|---|---|
| DFA Hurst (fenêtre 128, min 80) | `dfaDirection`/`dfaPersistence` | Continue, jamais identique bar-à-bar (confirmé, §9) | Non — `tanh` sature en VALEUR mais continue de varier en PENTE |
| Variance Ratio (fenêtre 30, min 20) | `vrDirection`/`vrPersistence` | Continue | Idem |
| DFA Confidence/RSquared, VR Confidence/SampleSize | `qualityScore` (poids 0.10 seulement) | Continue mais poids mineur | Non |

Aucun input n'est lui-même constant — la variabilité RAW existe à chaque étage en amont de `FusionStateManager`.

---

# 7. WINDOW ANALYSIS

```
DFA            : fenêtre=128 barres, minimum=80, ROLLING (buffer circulaire), reset UNIQUEMENT à context.Clock.IsFirstBar (une seule fois, au tout début du backtest)
Variance Ratio : fenêtre=30 barres, minimum=20, ROLLING (sous-tranche du même buffer)
```

**Une fenêtre trop longue pourrait-elle rendre Persistence artificiellement stable ?** Partiellement responsable de la LENTEUR du RAW (127/128 barres partagées entre deux calculs consécutifs, forte autocorrélation), mais **PAS la cause du gel STABLE** — le RAW, bien que lent, ne s'arrête JAMAIS complètement (§9). La fenêtre explique pourquoi le RAW bouge PEU, pas pourquoi le STABLE ne bouge PAS DU TOUT.

---

# 8. WARMUP ANALYSIS

`RegimeEngine._priceBuffer` (128 éléments) n'est réinitialisé QUE lorsque `context.Clock.IsFirstBar` est vrai — vérifié par lecture directe (`RegimeEngine.cs:47-51`), cela ne se produit qu'**UNE SEULE FOIS**, à la toute première barre du backtest entier — jamais par jour, jamais par session. **Les 6 journées à variance nulle ne coïncident PAS avec le début du dataset** (elles sont réparties du 5 août au 21 août, bien après le warmup initial de 128 barres) — le warmup de 128 barres n'est pas la cause (Hypothèse B écartée).

---

# 9. SESSION/RESET ANALYSIS — LA PREUVE CENTRALE

**Aucun reset par session ou par jour n'existe dans `RegimeEngine`** (confirmé §8) ni dans `PersistenceRule` (classe sans état, `Evaluate` est une fonction pure). Le SEUL état persistant de toute la chaîne est celui de `FusionStateManager._previousStableResult` (et `FusionProfileAnalyzer`/`_history`, sans effet sur `Persistence` — cette dimension n'est jamais réécrite par `ReplaceStructuralStability`, contrairement à `StructuralStability`).

**Mesure directe (10 930 barres, RAW capturé immédiatement après `EvidenceFusionEngine.Fuse`, AVANT tout passage par `FusionStateManager`)** :

```
Bar-à-bar : RawChangé & StableGelé = 10 382 / 10 929 (95.0%)
            RawChangé & StableChangé =    547 / 10 929 (5.0%)
            RawGelé (0 mouvement du raw lui-même) =      0 / 10 929 (0.0%)
```

**C'est la démonstration directe et sans ambiguïté** (brief §21, "le point le plus important du lot") : `Persistence` RAW varie à CHAQUE barre observée (jamais deux valeurs RAW consécutives bit-identiques) ; c'est le passage par `FusionStateManager.Update` qui absorbe 95% de ces mouvements et ne laisse passer que 5% vers la valeur STABLE réellement consommée par `MeanRevertingRule`/`StableRangeRule`.

---

# 10. STATE/CACHE ANALYSIS

- `DfaEvidence`/`VarianceRatioEvidence` : classes sans champ d'état mutable, `Compute()` est une fonction pure de son `EvidenceContext` (confirmé par lecture, aucun champ privé hors les constructeurs des sous-composants).
- `PersistenceRule` : classe sans état (`Evaluate` ne lit/écrit aucun champ d'instance).
- `RegimeEngine` : état = le buffer circulaire de prix (128 valeurs), partagé par TOUS les évidences, jamais spécifique à `Persistence`.
- `FusionStateManager` : **seul point d'état réel affectant `Persistence`** — `_previousStableResult`, relu et réécrit à chaque `Update()`.

**Persistence est-elle réellement recalculée à chaque barre ?** Le RAW : OUI, intégralement (§9). Le STABLE : la valeur est TECHNIQUEMENT réévaluée à chaque barre (le code exécute la comparaison), mais le RÉSULTAT de cette réévaluation est, dans 95% des cas sur ce dataset, la réutilisation explicite de la valeur précédente (`newConfidence.Value = previousConfidence.Value`) — une décision de conception, pas un défaut de recalcul.

---

# 11. INVALID DATA / FALLBACK ANALYSIS

`DfaValid`/`VarianceRatioValid` = **100% `true` sur les 10 930 barres analysées** (assertion vérifiée dans le test, §Tests) — le mécanisme "Missing Evidence" (`IsAvailable=false`, sentinelle neutre) n'est **jamais activé** sur ce dataset une fois le warmup de 128 barres passé. **Le fallback de valeur manquante n'est pas la cause du gel observé** — le gel touche des barres où toutes les données sont parfaitement valides.

---

# 12. RAW VS NORMALIZED ANALYSIS

```
PersistenceRaw    : n=10930, min=0.0000, max=0.8932, mean=0.0775, stdDev=0.1410, P25=0.0111, P50=0.0145, P75=0.0627
PersistenceStable : n=10930, min=0.0344, max=0.7466, mean=0.1687, stdDev=0.0919, P25=0.1327, P50=0.1397, P75=0.1480
```

**Constat majeur, non anticipé** : `PersistenceRaw` a une médiane BEAUCOUP plus basse (0.0145) que `PersistenceStable` (0.1397) — presque dix fois moins ! Le RAW passe la majorité de son temps près de 0 (P25=0.011, P50=0.0145) avec une queue haute occasionnelle, tandis que le STABLE, freiné par l'hystérésis, "traîne" à des valeurs plus élevées héritées d'anciens pics RAW qui ont eu le temps de faire franchir le seuil de 0.03 avant de retomber. **Le RAW et le STABLE ne mesurent PAS la même chose au sens strict** — le STABLE est une version fortement amortie et en retard (lag) du RAW, jamais sa copie lissée fidèle. C'est cohérent avec la conclusion §9 : **NORMALIZATION ISSUE confirmée** (brief §13 : "si PersistenceRaw varie mais PersistenceNormalized ne varie pas → NORMALIZATION ISSUE").

---

# 13. ZERO-VARIANCE DAYS (détail complet des 6 jours)

| Date | n | Stable (valeur unique) | Raw min/max | Raw valeurs distinctes | Hurst min/max | VR min/max | Winner dominant | ScoreDiff moyen |
|---|---|---:|---|---:|---|---|---|---:|
| 2026-08-05 | 275 | 0.132691 | [0.000001, 0.168972] | 275 | [0.269, 0.568] | [0.196, 1.920] | MeanReverting (144) | 0.0651 |
| 2026-08-11 | 275 | 0.129099 | [0.000148, 0.146108] | 275 | [0.237, 0.570] | [0.303, 1.551] | MeanReverting (155) | 0.0665 |
| 2026-08-12 | 275 | 0.129099 | [0.000040, 0.093192] | 275 | [0.202, 0.584] | [0.258, 1.373] | MeanReverting (215) | 0.0657 |
| 2026-08-17 | 276 | 0.136088 | [0.000001, 0.132031] | 276 | [0.201, 0.601] | [0.160, 1.330] | MeanReverting (156) | 0.0623 |
| 2026-08-18 | 275 | 0.136088 | [0.000013, 0.019864] | 275 | [0.288, 0.535] | [0.255, 1.200] | MeanReverting (212) | 0.0669 |
| 2026-08-21 | 251 | 0.143559 | [0.000000, 0.112150] | 251 | [0.248, 0.593] | [0.241, 1.732] | MeanReverting (161) | 0.0563 |

**Chaque jour : le nombre de valeurs RAW distinctes égale EXACTEMENT le nombre de barres** (275=275, 276=276, etc.) — le RAW ne répète JAMAIS une valeur identique deux fois, confirmant §9 jour par jour, pas seulement globalement. Notons aussi que **2026-08-11 et 2026-08-12 partagent la MÊME valeur STABLE exacte (0.129099)** et que **2026-08-17/2026-08-18 partagent également la même valeur (0.136088)** — ces paires de jours consécutifs sont en réalité un SEUL épisode de gel continu traversant la frontière calendaire (confirmé §16).

---

# 14. TEMPORAL ANALYSIS — SÉRIES GELÉES (au-delà des frontières de jour)

| Valeur gelée | Longueur | Durée | Période |
|---:|---:|---:|---|
| 0.136088 | 926 barres | **≈77.2h** | 2026-08-14 04:20 → 2026-08-19 12:45 |
| 0.129099 | 813 barres | ≈67.8h | 2026-08-10 14:55 → 2026-08-13 13:50 |
| 0.132691 | 630 barres | ≈52.5h | 2026-08-04 15:20 → 2026-08-06 23:20 |
| 0.149685 | 440 barres | ≈36.7h | 2026-08-03 00:05 → 2026-08-04 13:45 |
| 0.143559 | 393 barres | ≈32.8h | 2026-08-20 15:30 → 2026-08-24 02:22 |

```
Total de séries distinctes = 548
Séries de longueur ≥20 barres (≥1.67h) = 65
Plus grand saut bar-à-bar du STABLE = 0.1221, le 2026-07-17T14:05Z
```

**Le phénomène est BEAUCOUP plus étendu que "6 jours sur 40"** — la plus longue série gelée (926 barres, 0.136088) traverse ENTIÈREMENT le 2026-08-17 ET le 2026-08-18 (les deux jours classés "ZERO_VARIANCE" au sens strict du jour calendaire) mais déborde aussi sur des heures du 14, 15, 16 et 19 août qui n'ont PAS individuellement atteint le seuil `stdDev=0` sur la journée entière (parce que le RESTE de ces journées contenait un ou deux mouvements du STABLE) — la classification par jour calendaire (Lot 14.16) sous-estime donc la durée réelle de l'inertie.

---

# 15. DAILY DISTRIBUTION

Voir le tableau complet dans le log de test (§Tests) — 40 jours classés `ZERO_VARIANCE` (6) / `LOW_VARIANCE` (`stdDev<0.01`, 5 jours) / `NORMAL_VARIANCE` (`stdDev<0.05`, 15 jours) / `HIGH_VARIANCE` (`stdDev≥0.05`, 14 jours). Seuils de classification documentés explicitement (brief §14, "ne pas utiliser une valeur arbitraire sans la documenter") — choisis pour refléter des ordres de grandeur naturels de l'échelle de dispersion déjà observée (stdDev global ≈0.09, Lot 14.16), pas optimisés.

---

# 16. SESSION DISTRIBUTION

**Non applicable** — le projet ne distingue pas de "sessions" nommées au niveau du pipeline Regime/Fusion (`MarketContext.Clock.Session.Name` existe mais n'est consommé nulle part dans `RegimeEngine`/`PersistenceRule`/`FusionStateManager`, vérifié par grep). Aucune analyse par session n'est donc possible sans inventer une segmentation non utilisée par la production (brief §15, "si les données permettent" — elles ne le permettent pas ici sans construire quelque chose hors du pipeline réel). La `TEMPORAL ANALYSIS` (§14, séries gelées horodatées) reste la meilleure approximation disponible.

---

# 17. REGIME DISTRIBUTION (Persistence STABLE, rappel/confirmation du Lot 14.16)

| Winner | n | stdDev | median | range |
|---|---:|---:|---:|---|
| MeanReverting | 6 520 | 0.0373 | 0.1383 | [0.0344, 0.4409] |
| StructuralBreak | 3 359 | 0.0459 | 0.1397 | [0.1212, 0.4241] |
| RandomWalk | 483 | 0.0461 | 0.1389 | [0.1212, 0.3693] |
| Trending | 450 | 0.1260 | 0.4743 | [0.2685, 0.7466] |
| StableRange | 118 | 0.1047 | 0.4716 | [0.2973, 0.5969] |

Reproduit fidèlement le Lot 14.16 (mêmes ordres de grandeur, léger écart lié au dataset frais).

---

# 18. ZERO-VARIANCE × REGIME

```
ZeroVarianceDays (n=1627) : MeanReverting=64.1%, StructuralBreak=30.6%, RandomWalk=5.3%
OtherDays        (n=9303) : MeanReverting=58.9%, StructuralBreak=30.8%, Trending=4.8%, RandomWalk=4.3%, StableRange=1.3%
meanStationarity/meanMeanReversion/meanStructuralStability : très proches entre les deux groupes (§Tests, écarts <0.05)
```

**Les journées à variance nulle appartiennent-elles systématiquement au même régime ?** **Non, pas strictement** — mais elles sont légèrement SUR-représentées en `MeanReverting` (64.1% vs 58.9%) et TOTALEMENT ABSENTES de `Trending`/`StableRange` (0% vs 4.8%/1.3% le reste du temps) — cohérent avec §17 : ces deux régimes sont précisément ceux où `Persistence` a le plus de dispersion naturelle (stdDev 0.10-0.13), donc les plus susceptibles de franchir le seuil d'hystérésis. **Verdict : facteur contributif secondaire (Hypothèse G), pas une cause suffisante à elle seule** — le mécanisme structurel reste celui identifié §9.

---

# 19. ACTIVATION ZONE ANALYSIS / §20 THRESHOLD LOCALIZATION

30 bins à effectif égal (`n≈364` chacun) de `PersistenceStable` vs `ScoreDifference` moyen et taux de victoire StableRange :

| Bin | Plage | meanScoreDiff | SR winRate |
|---|---|---:|---:|
| 00-25 | [0.034, 0.198] | 0.055 à 0.070 (plat) | 0.0% partout |
| 26 | [0.198, 0.247] | 0.0460 | 0.0% |
| **27** | **[0.247, 0.309]** | **0.0320** | **4.7%** |
| **28** | **[0.309, 0.427]** | **0.0147** | **8.2%** |
| **29** | **[0.427, 0.747]** | **-0.0230** | **19.0%** |

**Caractérisation de la transition (brief §18, "ne pas prendre 0.25 comme vérité")** : la zone `[0.20, 0.75]` (bins 26-29, les 4 derniers des 30, soit ~13% du dataset) montre une décroissance PROGRESSIVE et continue de `ScoreDifference`, PAS un saut discontinu à une valeur précise. `0.25` correspond approximativement au DÉBUT visible de cette décroissance (transition entre bin 25 et 26), mais la transition se poursuit sur toute la plage `[0.25, 0.75]` sans point de rupture net unique — **`0.25` est donc une observation raisonnable du début de zone, jamais une valeur seuil exacte ni optimale** (brief §27, discipline respectée). Le `ScoreDifference` moyen devient NÉGATIF uniquement dans le tout dernier bin (les 3.4% les plus hauts de `Persistence`).

---

# 20. CHANGE-POINT ANALYSIS

Le plus grand saut bar-à-bar (0.1221, le 2026-07-17T14:05Z) coïncide avec la journée déjà signalée au Lot 14.16 comme la plus dispersée (`HIGH_VARIANCE`, stdDev=0.179). Aucun "Change Point Engine" de production n'a été créé — l'analyse ci-dessus (§14, séries gelées + plus grand saut) est purement descriptive, réalisée hors ligne dans le test (brief §20, discipline respectée).

---

# 21. CAUSAL CHECK — SYNTHÈSE

Réponse à la question posée exactement dans les termes du brief §21 :

```
Input variable (RAW Persistence bouge à CHAQUE barre, confirmé 0/10929 "RawGelé")
    →
STABLE Persistence gelé (95.0% des transitions absorbées par l'hystérésis de FusionStateManager)
```

**C'est le second des trois scénarios envisagés par le brief** ("Input variable → Persistence constant") — PAS "Input constant → Persistence constant" (écarté, §9), et PAS non plus le RAW lui-même qui se stabiliserait avant normalisation (le RAW ne se stabilise jamais, §6/§9/§12).

---

# 22. SYNTHETIC TESTS

Fichier : `Tests/Fusion/PersistenceZeroVarianceSyntheticTests.cs` — 8 cas (A-G + démonstration d'hystérésis), tous PASS, via les vraies classes `PersistenceRule`/`FusionStateManager` :

| Cas | Test | Résultat |
|---|---|---|
| A | Inputs identiques, 3 appels | Sortie bit-identique (déterminisme confirmé) |
| B | Hurst 0.50→0.51→0.52 (petite variation) | Mouvement non-décroissant et petit (<0.05) |
| C | Hurst 0.50→0.90 (forte variation) | Mouvement plus grand que le cas B |
| D | Hurst=2.0/VR=1e6 et Hurst=0.0/VR=1e-6 (extrêmes) | Aucun NaN/Infinity, reste dans [0,1] |
| E | DfaResult.Invalid (échantillon insuffisant) | Sentinelle Missing Evidence exacte (Value=0, Confidence=0, IsAvailable=false) |
| F | VarianceRatio=0.0 exactement (dénominateur nul potentiel) | Aucun NaN/Infinity (le plancher `1e-12` de production protège correctement) |
| G | Evidence.Dfa=null, Evidence.VarianceRatio=null | Sentinelle Missing Evidence |
| **Hystérésis** | Séquence RAW dérivant par pas de +0.005 (7 valeurs, toutes sous le seuil individuel de 0.03) à travers un VRAI `FusionStateManager` | **Le nombre de valeurs STABLE distinctes est inférieur au nombre de valeurs RAW distinctes**, avec au moins une paire consécutive de STABLE bit-identiques malgré un RAW qui a bougé entre les deux — **reproduit le mécanisme exact observé sur données réelles, avec les vraies classes de production** |

---

# 23. HISTORICAL DATASET AUDIT

Repris du Lot 14.16 (§23 de ce lot-là, non ré-audité en profondeur ici) : la seule capture ATAS réelle du dépôt (`Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18`) reste `SCIENTIFIC_ADMISSIBILITY=BLOCKED`. Aucune autre source de données MES/ES M5 historique exploitable n'existe dans le dépôt au-delà de Yahoo (59 jours). Rien de nouveau à ajouter pour ce lot spécifiquement — le mécanisme de gel identifié (§9) est un fait de CODE (FusionStateManager), pas un fait de DONNÉES, donc il ne dépend pas d'un historique plus long pour être établi (il pourrait cependant se manifester différemment sur un dataset différent — voir §27 Limitations).

---

# 24. ABLATION CONTROL

```
Baseline                    : Corr(MR,SR) = 0.9653
Dimensions partagées retirées : Corr(MR,SR) = 0.1047
Persistence retirée          : Corr(MR,SR) = 0.9913
```

**Reproduit fidèlement une quatrième fois** (0.9652/0.9653 aux Lots 14.15/14.16/14.17 ; 0.1033/0.1083/0.1047 ; 0.9913/0.9913/0.9913 — cette dernière valeur est IDENTIQUE sur les trois derniers runs). Le comportement causal établi aux Lots 14.15/14.16 reste intact après cette investigation — aucune régression, aucune anomalie introduite (brief §25, contrôle uniquement).

---

# 25. LIMITATIONS

- Le mécanisme de gel (§9) est établi sur CE dataset (MES M5, 59 jours) — sa PROPORTION exacte (95.0% des transitions absorbées) pourrait différer sur un autre instrument/timeframe où le RAW bougerait naturellement plus vite (Hypothèse "Dataset/Timeframe Dependent" non exclue pour l'AMPLEUR précise, bien que le MÉCANISME lui-même — la logique de `FusionStateManager` — soit indépendant du dataset par construction du code).
- Aucune investigation n'a été menée sur la raison ÉCONOMIQUE ou de marché du choix de `HysteresisThreshold=0.03` lui-même (paramètre "provisoire" selon son propre commentaire de code, Sprint antérieur, jamais recalibré) — hors scope, ne pas modifier ici.
- L'analyse par "session" (brief §15/§16) n'a pas pu être menée faute de segmentation de session réellement consommée par le pipeline (§16).
- Le lien entre les séries gelées les plus longues et un éventuel calendrier de marché (CME maintenance, week-ends) n'a pas été recherché explicitement (les timestamps sont documentés, l'interprétation calendaire reste à faire par un futur lot si jugée utile).

---

# 26. ROOT CAUSE CLASSIFICATION

| Catégorie | Verdict |
|---|---|
| Normal Market Behaviour | **Écartée comme cause suffisante** — le RAW varie continuellement (§9), donc le marché/la statistique produit bien un signal ; ce n'est pas ce signal qui est constant |
| Window Effect | Contributeur (explique la LENTEUR du RAW) mais pas la cause du gel STABLE (§7) |
| Warmup Effect | **Écartée** — aucun reset lié au warmup après la première barre (§8) |
| Reset Effect | **Écartée** — aucun reset par session/jour n'existe dans le code (§9) |
| Input Saturation | **Écartée** — les inputs (Hurst, VR) varient continuellement, jamais saturés à une valeur fixe (§6) |
| **Normalization Saturation** | **CONFIRMÉE — cause primaire** — `FusionStateManager`'s EMA+hystérésis absorbe 95.0% des mouvements RAW (§9, preuve directe) |
| **Fallback Effect (Configuration)** | **CONFIRMÉE — mécanisme technique précis** — le paramètre `HysteresisThreshold=0.03` est le seuil exact responsable (§5/§9) |
| Implementation Bug | **Écartée** — le code fonctionne exactement comme documenté (`StabilizationConfiguration`, doc-comments explicites sur la suppression des micro-variations) ; c'est un choix de conception dont l'AMPLEUR de l'effet à ce pas de temps n'avait simplement jamais été mesurée avant ce lot |
| Regime Dependent | **Confirmée — facteur secondaire** — gels absents de Trending/StableRange, légèrement sur-représentés en MeanReverting (§18) |
| Dataset Dependent | **Non exclue pour l'ampleur précise** (95.0%), mais le MÉCANISME est indépendant du dataset (§25) |
| Unknown | Non retenue — cause déterminée avec un niveau de preuve élevé |

**Verdict final : NORMALIZATION SATURATION (primaire) + FALLBACK/CONFIGURATION (mécanisme technique précis : seuil d'hystérésis 0.03) + REGIME DEPENDENT (facteur secondaire).**

---

# 27. RECOMMENDED NEXT LOT

**Lot proposé (investigation, pas calibration)** : mesurer la sensibilité DESCRIPTIVE de l'ampleur du gel (proportion de transitions absorbées, durée des séries gelées) à différentes valeurs HYPOTHÉTIQUES du seuil d'hystérésis (0.01, 0.02, 0.03 actuel, 0.05...) — SANS jamais modifier la production, en rejouant `FusionStateManager` avec des instances construites différemment SI son constructeur le permet (à vérifier — actuellement `StabilizationConfiguration` est un champ `private readonly` fixé à `Default`, donc une telle étude nécessiterait soit un constructeur non encore disponible, soit une réplique manuelle de la logique EMA+hystérésis en code de test uniquement observationnel, jamais en production).

**Alternative, complémentaire** : maintenant que le mécanisme (§9) et sa zone d'activation approximative (§19) sont établis, un futur lot de calibration POURRAIT être justifié pour re-examiner `HysteresisThreshold` — mais seulement après une revue explicite et approuvée, jamais dans ce lot ni le suivant sans décision explicite de l'utilisateur.

**Ne pas encore lancer** : toute modification de `HysteresisThreshold`, `Alpha`, ou du poids de `Persistence` tant que l'impact d'un changement d'hystérésis sur les 4 AUTRES dimensions Fusion (Stationarity, MeanReversion, RandomWalk — toutes soumises au MÊME mécanisme) n'a pas été étudié conjointement — modifier ce paramètre affecterait TOUT le pipeline Fusion, pas seulement `Persistence`.

---

# 28. HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
14.17

LAST COMPLETED:
14.16

PRIMARY QUESTION:
Why is Persistence zero-variance on some days? ANSWERED with direct causal proof.

REFERENCE:
6/40 days with zero variance (Lot 14.16) - CONFIRMED, but shown here to UNDERSTATE the real phenomenon:
the longest continuous frozen run spans 926 bars (~77.2h, >3 days), crossing calendar-day boundaries.

CURRENT WEIGHT:
0.20 (unchanged)

REFERENCE CORRELATION:
Pearson = -0.8140, Spearman = -0.4494 (Lot 14.16, unchanged by this lot)

MEANREVERTING:
Pearson = -0.4977, Spearman = -0.3011 (Lot 14.16, unchanged)

OBSERVED ACTIVATION ZONE:
Persistence > ~0.20-0.25 (progressive transition start, NOT a hard threshold - confirmed via 30-bin fine
analysis, §19). ScoreDifference turns negative (StableRange average advantage) only in the top ~3.4% of
the dataset (Persistence in [0.43,0.75]).

STATUS OF 0.25:
OBSERVED ONLY - NOT A CALIBRATED THRESHOLD (confirmed again, transition is gradual across [0.20,0.75], no
single sharp breakpoint)

ABLATION:
baseline=0.9653, shared removed=0.1047, persistence removed=0.9913 (4th consecutive reproduction, the
0.9913 figure is now identical across the last 3 independent dataset downloads)

ZERO VARIANCE CAUSE:
DEFINITIVELY IDENTIFIED - FusionStateManager's EMA(alpha=0.20)+hysteresis(threshold=0.03) mechanism. Direct
proof: RAW Persistence (immediately post-EvidenceFusionEngine.Fuse, pre-smoothing) changes on EVERY single
bar transition (0/10929 "both frozen"), but the STABLE value (post-FusionStateManager.Update, what
MeanRevertingRule/StableRangeRule actually consume) only updates 5.0% of the time (547/10929) - the other
95.0% of raw movements are individually below the 0.03 hysteresis threshold and get discarded, holding the
previous stable value bit-identical.

PERSISTENCE FORMULA:
Unchanged from Lot 14.16 (§5) - verified to have NO flat/constant zone itself (0 "raw frozen" transitions
observed). The formula is not the cause.

RAW INPUT:
DFA (Hurst, window=128/min=80) and Variance Ratio (window=30/min=20) are BOTH pure, freshly-computed rolling
statistics every bar - no caching, no state, confirmed by direct code reading (RegimeEngine, DfaEvidence,
VarianceRatioEvidence). Raw Persistence median (0.0145) is ~10x lower than Stable median (0.1397) - the
stable value is a heavily lagged/damped version of raw, not a faithful smoothed copy.

NORMALIZATION:
FusionStateManager.BuildStableResult: newConfidence.Value = valueChanged ? smoothedValue :
previousConfidence.Value - an EXACT hold of the prior stable value (bit-identical reassignment, not an
approximation) whenever |smoothedValue - previousStableValue| < 0.03.

WINDOW:
Not the cause by itself - explains why RAW moves SLOWLY (127/128 bars shared between consecutive DFA
windows), not why STABLE freezes completely. Ruled out as sole explanation.

RESET:
No session/day reset exists anywhere in RegimeEngine/PersistenceRule/FusionStateManager - the only buffer
reset (RegimeEngine._priceBuffer) fires once at context.Clock.IsFirstBar, at the very start of the whole
backtest, never per-day. Ruled out.

FALLBACK:
DfaValid/VarianceRatioValid = 100% true on all 10930 analyzed bars (past the 128-bar warmup) - the Missing
Evidence sentinel path is never activated on this dataset. Not the cause of the freeze.

REGIME EFFECT:
Secondary contributor only. Zero-variance periods are 0% Trending/StableRange (vs 4.8%/1.3% on other days)
and slightly over-represented in MeanReverting (64.1% vs 58.9%) - consistent with Trending/StableRange
being exactly the regimes where Persistence has the most natural dispersion (Lot 14.16 §8), so most likely
to cross the 0.03 threshold and escape the freeze.

ROOT CAUSE:
NORMALIZATION SATURATION (primary, directly proven) + FALLBACK/CONFIGURATION (the HysteresisThreshold=0.03
parameter is the precise technical mechanism) + REGIME DEPENDENT (secondary contributing factor). NOT a
market-behavior artifact, NOT a warmup/reset effect, NOT an implementation bug (the code works exactly as
documented - the effect's MAGNITUDE at this timeframe had simply never been measured before this lot).

REWEIGHTING:
NOT PERFORMED

THRESHOLD:
NOT CALIBRATED (0.20-0.25 activation zone is descriptive only, never proposed as a production value)

PRODUCTION:
UNCHANGED

CALIBRATION:
NOT PERFORMED

ATAS:
NOT USED

ORDERS:
NONE

DLL:
NOT DEPLOYED

COMMIT:
NO

NEXT LOT:
Descriptively study how the hysteresis threshold's magnitude affects freeze duration (0.01/0.02/0.03/0.05,
observation only, never modifying production) - OR formally propose (document only, never apply) a
hysteresis review as a future explicitly-approved calibration lot, jointly considering all 4 dimensions
affected by the same mechanism (Stationarity, MeanReversion, RandomWalk, not just Persistence).

WHY:
This lot closes the causal chain opened at Lot 14.14: AmbiguityScore compression (14.14) <- StableRange/
MeanReverting correlation (14.15 ablation) <- Persistence's real but unstable discriminative power (14.16)
<- Persistence's own zero-variance episodes (14.17, THIS lot) are caused by FusionStateManager's hysteresis,
not by the market or the Persistence formula itself. Any future reweighting or threshold-tuning decision
now has a complete, evidence-based picture of WHERE in the pipeline the constraint actually lives.

DO NOT DO:
Do NOT modify FusionStateManager's HysteresisThreshold, Alpha, Persistence's weight, MeanRevertingRule,
StableRangeRule, RegimeEngine, DecisionEngine, or AmbiguityGateThreshold - this lot is investigation-only.
Do NOT treat "Persistence > 0.25" as a calibrated production threshold - it is an observed, gradual
transition zone start, not a breakpoint. Do NOT assume the 95.0% freeze-absorption figure generalizes to
other instruments/timeframes without re-measuring - the MECHANISM is dataset-independent (it's a property
of the code), but its MAGNITUDE is not yet verified beyond MES M5/59 days.
```

**STOP.**
