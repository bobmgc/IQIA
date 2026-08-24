# QDE-012 — Sprint 15.25 — Lot 14.12 — Historical Dataset Expansion & Regime Coverage

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.9 (audit global), Lot 14.10 (P0-1/P0-2/P0-3), Lot 14.11 (protocole de calibration)
**Statut** : **IMPLEMENTED**
**Portée** : audit et extension du dataset historique, qualité des données, couverture de régime/signal/direction, faisabilité physique de TRAIN/VALIDATION/TEST/OOS. **Aucune calibration exécutée. Aucun paramètre de production modifié. Aucune logique métier modifiée.**

---

# 1. EXECUTIVE SUMMARY

**Constat central, découvert par ce lot et non anticipé par les rapports précédents** : la source Yahoo Finance ne fournit **jamais** plus de 60 jours de données 5 minutes, quel que soit le mécanisme de chunking utilisé. Ce n'est pas une limite par requête contournable — c'est un mur dur côté serveur Yahoo (confirmé empiriquement et déjà documenté dans le code, `YahooHistoricalBarSource.cs:49-51` : *"Yahoo's own documented (and empirically confirmed 2026-08-22) limit for 5-minute data is 60 days"*). **La recommandation des Lots 14.9/14.11 de viser 6-12 mois de données M5 est donc structurellement IRRÉALISABLE via Yahoo, quelle que soit la méthode.** Ce lot documente ce blocage comme un `DATA SOURCE BLOCKER` plutôt que de tenter de le contourner (brief §26).

Le dataset maximal réellement obtenu (§2, réseau réel, MES M5, fenêtre de 59 jours — la marge de sécurité déjà en place) contient **11 078 barres** (2026-06-25 → 2026-08-21), contre ~6 200-8 900 barres pour les fenêtres de 45 jours utilisées jusqu'ici — une amélioration réelle mais modeste, plafonnée par la source elle-même, pas par un choix de ce lot.

**Qualité des données** : excellente sur tous les axes vérifiables — 0 violation OHLC structurelle, 0 timestamp dupliqué, 0 désordre temporel, 0 volume négatif, et surtout **0 gap non expliqué** (79 gaps détectés, 100% classifiables comme fermetures de marché plausibles — 71 pauses de maintenance quotidiennes CME + 8 fermetures de week-end). Aucun signe du défaut "forming bar capture" qui avait bloqué la seule capture ATAS réelle disponible (Sprint 15.18, 36,5% de barres en formation) — seulement 4 barres à range nul sur 11 078 (0,04%).

**Couverture de régime** (mesurée en exécutant le pipeline réel, jamais en l'inventant) : fortement déséquilibrée. MeanReverting (58,7%) et StructuralBreak (30,5%) dominent ; Trending (4,8%), RandomWalk (4,7%) et StableRange (1,3%) sont sous-représentés ; Transitional et Unknown sont **totalement absents** sur les 11 078 barres.

**Verdict de suffisance** : **NOT READY FOR CALIBRATION** au sens large (Walk-Forward multi-période impossible via Yahoo ; StableRange/Trending/RandomWalk insuffisamment représentés pour une analyse conditionnelle par régime robuste ; coûts réels toujours non sourcés). **Exception documentée** : ce dataset unique est probablement suffisant pour une **première revalidation contrainte, mono-fenêtre, de `AmbiguityGateThreshold`** (2 339 candidats directionnels mesurés, échantillon substantiel) — une calibration limitée en portée que le Lot 14.13+ pourra envisager, sans être une déclaration de "READY" générale.

**Infrastructure ajoutée** : un seul nouveau fichier de test (`Tests/Backtest/Dataset/YahooDatasetCoverageTests.cs`), qui **réutilise intégralement** l'analyseur de qualité déjà existant (`RealMarketQualityAnalyzer`, Sprint 15.18) via un adaptateur local — aucune nouvelle logique d'analyse n'a été écrite, conformément à l'instruction "utiliser exclusivement l'infrastructure existante". Aucun type "Dataset Identity" séparé n'a été créé : `CalibrationDatasetSpecification`/`CalibrationDataset`/`HistoricalSeriesFingerprint` (Lot 14.9, inchangés) couvrent déjà entièrement ce besoin (§13).

---

# 2. CURRENT DATASET (avant ce lot)

Fenêtres de 45 jours, utilisées par convention dans tous les tests d'intégration réseau des Lots 14.2-14.11 (ex. `ExecutionYahooIntegrationTests`, `CalibrationYahooIntegrationTests`) : ~6 200-8 900 barres selon le taux de gap observé au moment du test, jamais figées (fenêtre glissante ancrée sur "maintenant").

---

# 3. TARGET DATASET

## Cible déterminée par l'audit (pas supposée a priori)

```
Instrument : MES (via le mapping Yahoo continu MES=F)
Timeframe  : M5
Provider   : Yahoo(Continuous)
Fenêtre    : les 59 derniers jours glissants (YahooHistoricalBarSource.DefaultMaxChunkSpanDays)
```

**Pourquoi 59 jours et pas plus** : c'est la profondeur MAXIMALE que Yahoo fournit pour du 5 minutes, point final — voir §5 pour la preuve. Viser davantage produirait une exception HTTP 422, pas une donnée manquante à combler.

**Pourquoi 59 jours et pas moins** : c'est un gain réel et mesurable par rapport aux 45 jours utilisés jusqu'ici (11 078 barres contre ~6 200-8 900), sans coût supplémentaire ni risque — Yahoo fournit cette profondeur dans une seule requête HTTP (`ChunkCount=1`, confirmé §4).

## Ce que cette cible couvre

Voir §16 (couverture de régime réelle, mesurée).

## Ce qui manque structurellement

Toute profondeur au-delà de 59 jours (donc toute étude multi-mois/multi-saison, tout Walk-Forward multi-période — voir §14.2) — **irréalisable via Yahoo**, quelle que soit la méthode d'acquisition employée dans ce dépôt.

---

# 4. HISTORICAL DEPTH ANALYSIS

## Profondeurs envisagées, évaluées puis écartées

| Profondeur | Réalisable via Yahoo ? | Preuve |
|---|---|---|
| 45 jours | Oui (déjà utilisé) | Usage établi depuis le Lot 14.2 |
| 59 jours | **Oui — nouvelle cible de ce lot** | Testé §5, `ChunkCount=1`, 11 078 barres obtenues |
| 3 mois (~90 jours) | **Non** | Dépasse le mur des 60 jours |
| 6 mois | **Non** | idem |
| 1 an | **Non** | idem |
| 2 ans | **Non** | idem |

**Aucune de ces profondeurs n'a été "téléchargée automatiquement" pour test** — seule la profondeur de 59 jours (déjà validée comme la limite technique réelle par le commentaire de code existant, `YahooHistoricalBarSource.cs:49-51`, écrit et vérifié le 2026-08-22 lors d'un lot antérieur) a fait l'objet d'un appel réseau dans ce lot, conformément à l'instruction de ne pas télécharger toutes les périodes candidates.

## Justification scientifique de la conclusion "59 jours est le maximum, pas un choix"

- Nombre de barres M5 : 11 078 sur 59 jours calendaires ≈ 187,8 barres théoriques disponibles par jour glissées sur les 59 jours (contre un maximum théorique de 288 barres/jour pour un marché 24h) — cohérent avec le taux de gap observé (§9).
- Diversité des régimes : mesurée §16 — insuffisante pour 4 des 7 régimes réels du pipeline.
- Stabilité statistique / événements extrêmes / saisonnalité : **non évaluables au-delà de cette fenêtre par construction** — c'est précisément la limite documentée ici.

---

# 5. YAHOO AVAILABILITY

## Preuve empirique du mur des 60 jours

Confirmée par le code existant (non modifié par ce lot, cité en preuve) :

> `Core/MarketData/Yahoo/YahooChunkPlanner.cs:9-11` — *"a live request for a >60-day-old window returned HTTP 422 with 'Y5m data not available for startTime=... The requested range must be within the last 60 days.'"*

Reconfirmée par ce lot lors du test réseau §6 : une requête pour exactement 59 jours (`DefaultMaxChunkSpanDays`) a réussi en **un seul appel HTTP** (`ChunkCount=1`) — aucun chunking n'a même été nécessaire, puisque 59 ≤ 59 (la limite par requête ET la limite de profondeur coïncident pour ce provider). Le chunking existant (`YahooChunkPlanner`) reste utile pour toute requête future dépassant 59 jours EN DURÉE DE REQUÊTE, mais ne peut jamais reculer plus loin dans le temps que le mur des 60 jours — les deux contraintes sont indépendantes et ce lot les distingue explicitement, ce que les rapports précédents n'avaient pas fait aussi clairement.

**Statut : YAHOO = PASS pour 59 jours, BLOCKED au-delà.**

---

# 6. DATA ACQUISITION

Exclusivement `YahooHistoricalBarSource` (Lot 14.2, non modifié) — aucune nouvelle API, aucune dépendance NuGet ajoutée. Nouveau test réseau : `Tests/Backtest/Dataset/YahooDatasetCoverageTests.Integration_Network_YahooMesFiveMinuteBars_MaximumAvailableDepth_CoverageReport`.

## Résultat de l'exécution réelle (2026-08-23)

```
Instrument=MES, Timeframe=M5, Provider=Yahoo(Continuous), TimeZone=UTC
Requested range: from=2026-06-25T19:39:49Z, to=2026-08-23T19:39:49Z (59 jours demandés)
Actual range: FirstTimestamp=2026-06-25T19:35:00Z, LastTimestamp=2026-08-21T20:59:59Z
BarCount=11078, ChunkCount=1, YahooReportedGapSlots=2764
Fingerprint=F7B34C25153BDFC2472821E0BFC095869D05395CE46A868B81008A2E37109B05
```

Note : la fin réelle de la série (2026-08-21T20:59:59Z) est environ 2 jours avant l'instant de la requête (2026-08-23T19:39:49Z) — Yahoo ne fournit pas les toutes dernières heures/jours de données 5 minutes en temps quasi-réel pour ce contrat continu ; comportement observé, non expliqué plus avant (hors scope de ce lot).

`YahooReportedGapSlots=2764` : créneaux que Yahoo lui-même rapporte comme vides (OHLCV null) et que le parser omet sans les fabriquer (comportement inchangé depuis le Lot 14.2) — cohérent avec le taux de gap ~24-29% déjà mesuré au Lot 14.9 sur des fenêtres plus courtes.

---

# 7. DATA QUALITY

Résumé — détail par section ci-dessous (§8-11).

| Dimension | Verdict |
|---|---|
| Timestamp integrity | **PASS** |
| OHLC integrity | **PASS** |
| Gap analysis | **PASS** (100% des gaps classifiés comme fermetures de marché plausibles) |
| Duplicate analysis | **PASS** (0 doublon) |
| Volume | PASS (0 valeur négative ; 36 barres à volume nul sur 11 078, soit 0,3%) |
| Zero-range (forming-bar signature) | PASS — 4 barres isolées sur 11 078 (0,04%), sans commune mesure avec le défaut ATAS de 36,5% (Sprint 15.18) |

---

# 8. TIMESTAMP INTEGRITY

Mesuré via `RealMarketQualityAnalyzer.AnalyzeTimestamps` (Sprint 15.18, réutilisé tel quel — voir §24) appliqué à la série Yahoo via un adaptateur local (`YahooDatasetCoverageTests.ToRealMarketBars`) :

```
StrictlyIncreasing=True, DuplicateTimestampCount=0
```

**PASS.** Déjà structurellement garanti par le constructeur validant de `HistoricalSeries.Create` (Lot 14.1, `Core/MarketData/HistoricalSeries.cs:119-144` — ordre strict et absence de doublon sont deux violations distinctes, toutes deux vérifiées à la construction) — cette mesure est une confirmation en profondeur via un analyseur indépendant, pas une découverte nouvelle.

---

# 9. OHLC INTEGRITY

```
TotalBars=11078, StructuralViolations=0, NonPositivePrice=0, NegativeVolume=0, ZeroVolume=36, IdenticalOhlc=4
```

**PASS.** Aucune violation structurelle (`High >= max(Open,Close)`, `Low <= min(Open,Close)`, `High >= Low` — toutes vérifiées), aucun prix non positif, aucun volume négatif. 36 barres à volume nul (0,3%, plausible pour des créneaux M5 à très faible activité, ex. sessions asiatiques creuses sur un future indiciel US) et 4 barres à OHLC identique (barres plates isolées, cohérent avec une activité de trading minimale sur ce créneau précis, PAS le symptôme de capture-en-formation observé sur ATAS — voir §11).

---

# 10. GAP ANALYSIS

Classification par `RealMarketQualityAnalyzer.ClassifyGaps` (heuristique générique par taille, sans calendrier CME encodé — voir sa propre doc-comment) :

```
MEDIUM_GAP_PLAUSIBLE_INTRADAY_CLOSURE: 71 gap(s)
LARGE_GAP_PLAUSIBLE_MULTI_DAY_CLOSURE: 8 gap(s)
Total gaps=79
```

**PASS — aucun gap classé `IRREGULAR_INTERVAL_UNRESOLVED` ni `UNRESOLVED_GAP_SIZE`.** 100% des 79 gaps détectés sont classifiables comme des fermetures de marché plausibles :
- 71 gaps de taille moyenne (2h-20h) ≈ 1,25/jour sur 57 jours — cohérent avec **une pause de maintenance quotidienne CME** (créneau ~22h-23h UTC, déjà documenté comme hypothèse dans les rapports précédents, jamais confirmé faute de calendrier CME intégré au dépôt — voir la mise en garde ci-dessous).
- 8 gaps de plus de 20h ≈ 8,1 sur 8-9 week-ends couverts par 57 jours — cohérent avec **des fermetures de week-end**.

## MARKET GAP vs DATA GAP — distinction explicite

**Aucun gap de cette série n'a été classé `DATA_GAP`** au sens où ce terme impliquerait un défaut d'acquisition (donnée manquante alors que le marché était ouvert) — la totalité des 79 gaps est de taille et de fréquence cohérentes avec des fermetures de marché réelles (maintenance quotidienne + week-ends), jamais avec une anomalie d'acquisition. **Mise en garde méthodologique explicite** (héritée de la doc-comment de l'analyseur réutilisé) : cette classification est une heuristique de taille/fréquence, **pas** une vérification positive contre un calendrier CME réel (qui n'existe nulle part dans ce dépôt) — un DATA_GAP qui coïnciderait accidentellement en taille avec une fermeture plausible ne serait pas détecté par cette méthode. Ce risque résiduel est documenté, pas résolu, dans ce lot (voir §25).

---

# 11. DUPLICATE ANALYSIS

`DuplicateTimestampCount=0` (§8) — **PASS**. Aucune barre dupliquée après la fusion interne des chunks (ici, un seul chunk, donc aucune fusion multi-chunk à valider dans cette exécution précise — le mécanisme de dédoublonnage de `YahooHistoricalBarSource.Load` lignes 96-104, qui décale d'une seconde chaque requête de chunk suivante précisément pour empêcher un doublon à la frontière, reste néanmoins déjà validé par les tests réseau existants du Lot 14.2 sur des fenêtres antérieures ayant nécessité plusieurs chunks).

---

# 12. DATASET FINGERPRINT

```
Fingerprint=F7B34C25153BDFC2472821E0BFC095869D05395CE46A868B81008A2E37109B05
```

Calculé par `HistoricalSeriesFingerprint.Compute` (Lot 14.2, `Backtest/HistoricalSeriesFingerprint.cs`, inchangé). **PASS** : déjà prouvé déterministe et sensible à toute modification du contenu par les tests existants (`Tests/Backtest/Yahoo/HistoricalSeriesFingerprintTests.cs`, Lot 14.2) — non ré-exercé par ce lot, aucune garantie supplémentaire n'aurait été apportée par une répétition.

---

# 13. DATASET IDENTITY

**Aucun nouveau type n'a été créé.** `Backtest/Calibration/CalibrationDatasetSpecification`/`CalibrationDataset` (Lot 14.9, inchangés) couvrent déjà entièrement l'identité demandée par le brief :

| Champ demandé | Source existante |
|---|---|
| Instrument | `CalibrationDatasetSpecification.Symbol` |
| Timeframe | `CalibrationDatasetSpecification.Timeframe` |
| Start / End | `CalibrationDatasetSpecification.Start`/`.End` |
| Provider | `CalibrationDatasetSpecification.Provider` |
| BarCount | `HistoricalSeries.Count` (porté par `CalibrationDataset.Series`) |
| Fingerprint | `CalibrationDataset.Fingerprint` (réutilise `HistoricalSeriesFingerprint.Compute`, jamais un second algorithme — règle du Lot 14.9 §4) |

**PASS** — reproductible par construction : `CalibrationDataset(spec, series)` recalcule toujours le même fingerprint pour la même série (prouvé Lot 14.9, `CalibrationDatasetSpecificationTests.cs`).

---

# 14. TRAIN / VALIDATION / TEST / OOS

## 14.1 Faisabilité physique — vérifiée sur données réelles

Sur le dataset obtenu (11 078 barres, `ReadyBars=10950` après 128 barres de warmup — chiffre confirmé exactement par le pipeline réel, §16), une répartition illustrant le protocole du Lot 14.11 §11 (proportions 55/15/15/15%, non appliquée au code) donnerait approximativement :

| Segment | Proportion | Barres (approx.) | Durée calendaire (approx.) |
|---|---|---|---|
| Warmup (avant TRAIN) | — | 128 | ~0,44 jour |
| TRAIN | 55% | ~6 023 | ~31,4 jours |
| VALIDATION | 15% | ~1 643 | ~8,6 jours |
| TEST | 15% | ~1 643 | ~8,6 jours |
| OOS (la plus récente) | 15% | ~1 642 | ~8,6 jours |

**Physiquement constructible dès aujourd'hui**, en suivant exactement le mécanisme à deux `CalibrationWindowSet` défini au Lot 14.11 §11.3 (aucune modification de `CalibrationWindowRole`, toujours fermé à 3 valeurs) — non exécuté dans ce lot (aucune calibration n'a lieu), mais vérifié réalisable avec les vraies dates du dataset obtenu.

## 14.2 Limite structurelle découverte par ce lot — Walk-Forward multi-période

Le Lot 14.11 §12 notait que le Walk-Forward est "atteignable sans nouveau code" en répétant `CalibrationWindowSet`/`CalibrationExperiment` avec des dates décalées. **Ce lot précise une limite non identifiée jusqu'ici** : cette répétition suppose de disposer de PLUSIEURS fenêtres historiques disjointes — or Yahoo ne fournit jamais qu'**une seule fenêtre glissante de 59 jours ancrée sur "maintenant"** (§5). Il est donc possible de re-télécharger la même requête demain et d'obtenir une fenêtre légèrement décalée (un jour de plus, un jour de moins) — mais **jamais** d'obtenir aujourd'hui une fenêtre de 59 jours centrée sur, par exemple, il y a six mois. **Un Walk-Forward au sens classique (plusieurs époques historiques réellement indépendantes, ex. TRAIN sur janvier-février, TEST sur mars, puis TRAIN sur février-mars, TEST sur avril) est donc IRRÉALISABLE via Yahoo seul**, quelle que soit la patience du chercheur — pas seulement limité en profondeur, structurellement bloqué. Seul un Walk-Forward "temps réel" (attendre que de nouvelles barres s'accumulent au fil des semaines, re-calibrer) resterait possible, à un rythme dicté par le calendrier, pas par la recherche. Documenté ici comme `DATA SOURCE BLOCKER` (§25, D14.12-1).

## 14.3 Base de fenêtrage — rappel de la règle du Lot 14.10/14.11

Le split proposé §14.1 est exprimé par date calendaire (`SignalTimestamp`-compatible). Conformément à la règle posée au Lot 14.11 §11.4, toute construction réelle de ce split devra choisir explicitement entre fenêtrer par `SignalTimestamp` ou par `EntryTimestamp` (potentiellement décalé d'une barre depuis le Lot 14.10) — ce lot ne tranche pas cette question (elle appartient à la Signal vs Economic Calibration du futur lot exécutant), il rappelle seulement qu'elle devra être tranchée explicitement.

---

# 15. WARMUP

`CalibrationWarmupContract`/`BacktestEngine.RunSignalPipeline` (Lots 14.1/14.9, inchangés) exigent déjà que les barres de warmup soient **strictement antérieures** à `TRAIN.Start`, jamais empruntées à `VALIDATION`/`OOS` (règle déjà verrouillée et testée, `CalibrationWindowSet.TryCreate`, Lot 14.9).

**Confirmé sur données réelles par ce lot** : avec `warmupBars=128` (la valeur déjà utilisée par tous les lots précédents), le pipeline réel a produit `BarsProcessed=11078` et `WarmupBars=128` — soit `ReadyBars=10950` (`11078-128`), exactement le nombre de barres disponibles pour TRAIN+VALIDATION+TEST+OOS dans le split illustratif §14.1. **DATA USED FOR WARMUP** (les 128 premières barres, jamais comptées dans une métrique de split) et **DATA USED FOR MEASUREMENT** (les 10 950 barres suivantes) sont donc déjà distinguées par construction dans l'infrastructure existante — vérifié, pas re-construit.

---

# 16. REGIME COVERAGE

Mesuré en exécutant `BacktestEngine.RunSignalPipeline` (réel, non modifié) sur le dataset §6, puis en tabulant `BacktestSignalResult.Decision.Winner` (le `MarketState` réellement arbitré par bar — jamais une catégorie inventée) :

| Regime (MarketState réel) | Bars | % | Statut de représentativité (§19) |
|---|---|---|---|
| MeanReverting | 6 507 | 58,7% | REPRESENTED |
| StructuralBreak | 3 376 | 30,5% | REPRESENTED |
| Trending | 532 | 4,8% | UNDERREPRESENTED |
| RandomWalk | 518 | 4,7% | UNDERREPRESENTED |
| StableRange | 145 | 1,3% | UNDERREPRESENTED |
| Unknown | 0 | 0,0% | ABSENT |
| Transitional | 0 | 0,0% | ABSENT |

Total = 11 078 (100% des barres ont un `Decision` — `Bars without a Decision = 0`). `RegimeDetectedCount` (Winner != Unknown) = 11 078, confirmé égal à la somme des 6 régimes non-Unknown ci-dessus (`11078 = 6507+3376+532+518+145`, vérifié par assertion dans le test).

**Constat descriptif notable, non expliqué par ce lot (aucune calibration n'a été tentée pour l'expliquer)** : StructuralBreak (30,5%) est presque aussi fréquent que MeanReverting (58,7%) et dépasse largement Trending+RandomWalk+StableRange combinés (10,8%). C'est un fait mesuré, pas une preuve de bug ni de bonne calibration des poids Decision/Fusion (tous deux Type D, non calibrés — Lot 14.9/14.11) — une piste d'investigation pour un futur lot, jamais une conclusion ici.

**Rappel important** : `SignalCount=6507` (§17) est rigoureusement égal au nombre de barres `MeanReverting` — confirmation empirique directe, sur données réelles, du constat déjà fait par audit de code au Lot 14.9 : seule la régime MeanReverting reçoit de vrais modèles scientifiques (`ScientificModelRegistry`), donc seule elle produit jamais un `Signal` ou une direction BUY/SELL.

## Catégories du brief non représentées dans la taxonomie réelle du pipeline

Le brief §2 suggère aussi "Volatility Expansion"/"Volatility Compression" comme régimes à couvrir. **Ces catégories n'existent pas dans `MarketState`** (7 valeurs listées ci-dessus, `Engine/Decision/States/MarketState.cs`, non modifié) — elles appartiennent à un axe orthogonal (`VolatilityModel`/`VolatilityEvidence`, classification LOW/MEDIUM/HIGH, Lot 14.9), jamais joint à `Decision.Winner`. Conformément à l'instruction explicite de ce lot ("ne pas inventer un nouveau Regime Engine"), ce rapport ne fabrique pas de mapping artificiel entre les deux — c'est la même limite de jointure déjà documentée au Lot 14.11 (D14.11-2), qui bloque toute analyse combinée Régime×Volatilité comme elle bloque l'analyse par régime tout court.

---

# 17. SIGNAL COVERAGE

Mesuré directement sur les compteurs agrégés du pipeline réel (`BacktestSignalPipelineResult`, Lot 14.3, inchangé) :

```
BarsProcessed=11078, BarsRejected=0, WarmupBars=128, ReadyBars=10950
RegimeDetectedCount=11078, DecisionCount=11078, SignalCount=6507, EntryCandidateCount=6507
BUY=1185, SELL=1154, NO_ACTION=8739, WATCH=0
TradePlan: SIGNAL_ONLY=2339, READY=0, NO_TRADE=8739, BLOCKED=0, ExceptionCount=0
```

- **0 barre rejetée** (`BarsRejected=0`) — la validation `MarketContextValidator` accepte 100% des barres Yahoo de ce dataset, contrairement à une hypothèse implicite qui aurait pu justifier un rejet significatif.
- **0 exception** de pipeline sur 11 078 barres — robustesse mécanique confirmée à cette échelle.
- **`SIGNAL_ONLY=2339` = `BUY(1185)+SELL(1154)`** exactement — confirme que 100% des candidats directionnels produisent un `TradePlan.SIGNAL_ONLY` (jamais `PLAN_READY`, jamais `PLAN_BLOCKED`) — cohérent avec le constat déjà établi (Lot 14.9/14.10) : `TradePlan.StopLoss` est toujours `null` en production.
- **`WATCH=0`** : aucune barre de ce dataset n'a atteint le statut `WATCHLIST` — fait descriptif, non expliqué ici (pourrait indiquer que ce statut est rarement/jamais atteint en pratique, ou être spécifique à cette fenêtre — pas de conclusion générale tirée d'un seul dataset).

---

# 18. BUY/SELL COVERAGE

```
BUY=1185 (50,7% des candidats directionnels)
SELL=1154 (49,3%)
```

**Équilibre BUY/SELL correct** — aucun déséquilibre significatif détecté sur ce dataset. Rappel méthodologique (Lot 14.11 §16) : ce résultat descriptif ne doit jamais être utilisé comme un objectif à reproduire (une future calibration pourrait légitimement produire un ratio différent) — il est seulement rapporté ici comme fait mesuré, sans être ni corrigé ni interprété comme "bon" ou "mauvais" au-delà du simple constat d'absence de déséquilibre extrême.

---

# 19. DATASET SUFFICIENCY

| Usage futur | Verdict | Justification |
|---|---|---|
| **Signal Calibration** (fenêtres régime, poids Fusion/Decision, `AmbiguityGateThreshold`) | **PARTIALLY SUFFICIENT** | Substantiel pour MeanReverting (6 507 barres, 2 339 candidats directionnels) et StructuralBreak (3 376 barres) — mais Trending/RandomWalk/StableRange trop rares pour une étude par régime robuste |
| **Stop Loss Calibration** | **INSUFFICIENT** | Volume de barres MeanReverting suffisant en théorie, mais Yahoo ne fournit que de l'OHLCV (pas de carnet/tick) — la précision de fill nécessaire à une calibration SL fine reste hors de portée de cette source, indépendamment du nombre de barres (constat déjà établi Lot 14.9, confirmé ici pour cette fenêtre précise aussi) |
| **Risk Calibration** | **NOT APPLICABLE** | Dépend d'une méthodologie SL (non prête) et d'une décision de valeur `RiskPolicy` (pas une question de volume de données) |
| **Economic Calibration** | **INSUFFICIENT** | Échantillon de positions plausiblement suffisant (2 339 candidats), mais aucun barème de coûts réel n'est encore sourcé (Lot 14.10/14.11, inchangé) — condition bloquante indépendante du dataset |
| **Walk-Forward** | **INSUFFICIENT (structurel)** | Une seule fenêtre historique de 59 jours existe — aucune répétition sur des époques réellement indépendantes n'est possible via Yahoo (§14.2) |

**Aucune de ces conclusions n'a été "résolue" par ce lot** — chacune reste un blocage documenté pour un futur lot (§25/§26).

---

# 20. UNDERREPRESENTED REGIMES

| Régime | Statut |
|---|---|
| MeanReverting | REPRESENTED |
| StructuralBreak | REPRESENTED |
| Trending | UNDERREPRESENTED |
| RandomWalk | UNDERREPRESENTED |
| StableRange | UNDERREPRESENTED |
| Unknown | ABSENT |
| Transitional | ABSENT |

**Aucune tentative de corriger artificiellement ce déséquilibre** (rééchantillonnage, sur-pondération, filtrage sélectif) n'a été faite, conformément à l'instruction explicite du brief.

---

# 21. OUTLIER ANALYSIS

Détection descriptive uniquement (`RealMarketQualityAnalyzer.AnalyzeVolume`/`ClassifyGaps`/`ValidateOhlc`, aucune donnée supprimée, winsorisée ou normalisée) :

- **Volume** : Min=0, Max=54 709, Médiane=1 395, Moyenne≈3 796 — la moyenne bien supérieure à la médiane indique une distribution à queue lourde (quelques barres à volume très élevé), cohérent avec un instrument indiciel actif sans traitement particulier requis à ce stade.
- **Gaps** : voir §10 — 79 gaps, tous classifiables comme fermetures de marché plausibles, aucun outlier de gap non expliqué.
- **Barres à range nul** : 4 sur 11 078 (0,04%), isolées (aucun segment de plus d'une barre) — négligeable, sans commune mesure avec le défaut de capture ATAS (36,5%).

**Aucune donnée n'a été modifiée, supprimée, winsorisée ou normalisée par ce lot.**

---

# 22. DATA SNOOPING CONTROLS

La fenêtre de 59 jours a été choisie **exclusivement** parce qu'elle est la profondeur maximale techniquement disponible (§5) — jamais parce qu'elle produirait de meilleurs résultats de calibration (aucune calibration n'a été exécutée pour comparer). Aucune période alternative n'a été testée puis écartée sur la base d'une performance quelconque. La date de fin (2026-08-21, la plus récente disponible au moment de l'exécution) n'a pas non plus été choisie pour son contenu — c'est simplement "maintenant moins le délai de publication Yahoo" (§6).

---

# 23. REPRODUCIBILITY

Un autre développeur peut reproduire cette mesure sans accès à ATAS :
1. Exécuter `YahooHistoricalBarSource().Load("MES", "M5", DateTime.UtcNow.AddDays(-59), DateTime.UtcNow)`.
2. Calculer `HistoricalSeriesFingerprint.Compute(series)`.
3. Exécuter `BacktestEngine.RunSignalPipeline(scenario, warmupBars: 128)` et lire les compteurs agrégés.
4. Appliquer `RealMarketQualityAnalyzer` via le même adaptateur (`YahooDatasetCoverageTests.ToRealMarketBars`, copiable telle quelle).

**Limite de reproductibilité honnêtement documentée** : la fenêtre étant glissante (ancrée sur "maintenant"), un autre développeur exécutant ces mêmes étapes un autre jour obtiendra un `BarCount`/`Fingerprint`/une distribution de régime **différents** — reproductible en MÉTHODE, jamais bit-identique en RÉSULTAT, exactement la même discipline que tous les tests réseau Yahoo de ce dépôt depuis le Lot 14.2 (jamais un nombre figé asserté).

---

# 24. TESTS

## Nouveau fichier

`Tests/Backtest/Dataset/YahooDatasetCoverageTests.cs` — 1 test réseau (`Integration_Network_YahooMesFiveMinuteBars_MaximumAvailableDepth_CoverageReport`), **PASS** (6 min 36 s, réseau réel).

Réutilise intégralement `RealMarketQualityAnalyzer` (Sprint 15.18, `Tests/Research/StopLossCalibration/RealMarket/`, non modifié) via un adaptateur local pur (`ToRealMarketBars`, `HistoricalBar`→`RealMarketBar`) — **aucune logique de détection de gap/OHLC/continuité/volume n'a été réécrite**, conformément à l'instruction "utiliser exclusivement l'infrastructure existante". Assertions : uniquement structurelles (0 violation OHLC, 0 doublon, comptages qui s'additionnent correctement, 0 exception de pipeline) — jamais un jugement de "suffisance", qui appartient à ce rapport, pas au code de test.

## Audit de couverture des invariants "obligatoires" du brief §25

| Catégorie demandée | Couverture |
|---|---|
| Timestamp monotonicity / uniqueness | `HistoricalSeriesTests.cs` (Lot 14.1, existant) + `YahooDatasetCoverageTests` (ce lot, sur données réelles) |
| OHLC validity | `HistoricalBarTests.cs` (Lot 14.1, existant) + ce lot (données réelles) |
| Gap detection | **Nouveau pour ce lot** — jamais testé au niveau série avant (Lot 14.9 D13, confirmé toujours absent au niveau `HistoricalSeries` lui-même ; couvert ici via l'analyseur réutilisé, appliqué en test, pas en production) |
| Duplicate detection | `HistoricalSeriesTests.DuplicateTimestamp_Rejected_AsDistinctFromOutOfOrder` (Lot 14.1, existant) |
| Fingerprint déterministe / sensible à la modification | `HistoricalSeriesFingerprintTests.cs` (Lot 14.2, existant) |
| Splits (no overlap, ordre chronologique, isolation OOS) | `CalibrationWindowTests.cs`/`CalibrationWindowIsolationTests.cs` (Lot 14.9, existant) — audité au Lot 14.11 §26, non dupliqué ici |
| Warmup (pas de contamination des métriques) | `CalibrationWarmupContract`/tests associés (Lot 14.9, existant) — confirmé sur données réelles §15 |
| Run isolation / determinism | `CalibrationRunIsolationTests.cs` (Lot 14.9, existant) — non applicable à ce lot (aucune expérience de calibration n'a été construite, seulement une mesure de couverture à passage unique) |

**Décision explicite** : conformément à la discipline déjà établie aux Lots 14.10/14.11, aucun test dupliquant une garantie déjà prouvée n'a été ajouté. Le seul invariant réellement nouveau pour ce lot (détection de gap au niveau série) est couvert en réutilisant un analyseur déjà testé indépendamment (`RealMarketQualityAnalyzerTests.cs`, Sprint 15.18) plutôt qu'en écrivant une nouvelle suite de tests pour une logique déjà validée ailleurs.

## Vérification technique

```
dotnet build IQIAIndicator.csproj -c Debug    → PASS, 0 avertissement, 0 erreur
dotnet build IQIAIndicator.csproj -c Release  → PASS, 0 avertissement, 0 erreur
dotnet build IQIAIndicator.Tests.csproj -c Debug    → PASS
dotnet build IQIAIndicator.Tests.csproj -c Release  → PASS
dotnet test IQIAIndicator.Tests.csproj (nouveau test seul) → PASS, 1/1, 6m36s (réseau réel)
dotnet test IQIAIndicator.Tests.csproj (suite complète) → Réussi : échec 0, réussite 778, ignorée(s) 1, total 779, durée 25m42s
```

**Confirmation** : la suite complète est passée de 777 réussis/1 ignoré/0 échec (Lot 14.10) à 778 réussis/1 ignoré/0 échec/779 total après l'ajout du seul nouveau fichier de ce lot (+1 test réseau) — zéro régression. Aucun fichier de production n'ayant été modifié (seul un fichier de test a été ajouté), aucune régression n'était possible dans le reste de la suite ; seul le nouveau test lui-même pouvait échouer, et il est PASS.

---

# 25. LIMITATIONS

- Le mur des 60 jours de Yahoo est définitif pour cette source — aucune extension future de ce dataset au-delà de ~59-60 jours glissants n'est possible sans changer de fournisseur de données (décision hors scope de ce lot, jamais prise ici).
- Le Walk-Forward multi-période reste irréalisable via Yahoo (§14.2) — nouveau blocage structurel découvert par ce lot, non anticipé par le Lot 14.11.
- La classification des gaps (§10) reste une heuristique de taille/fréquence, jamais vérifiée contre un vrai calendrier CME (qui n'existe nulle part dans ce dépôt).
- 4 des 7 régimes réels du pipeline (Trending, RandomWalk, StableRange, et totalement Transitional/Unknown) restent sous-représentés ou absents sur cette fenêtre unique — aucune garantie qu'une future fenêtre glissante les couvrira mieux.
- Aucune donnée de coûts réels n'a été sourcée (hors scope, inchangé depuis les Lots 14.10/14.11).
- Aucune méthodologie Stop Loss n'existe encore (hors scope, inchangé).
- Le dataset mesuré ici est déjà glissant au moment de la lecture de ce rapport — toute reproduction future donnera un contenu différent (§23).

---

# 26. NEXT LOT RECOMMENDATION

Aucune calibration n'est recommandée immédiatement à large échelle (Walk-Forward, Regime-Conditional complet — tous deux bloqués §19/§20). **Un seul lot ciblé, à portée volontairement étroite, est raisonnable en prochaine étape** :

**Lot 14.13 (proposé) — AmbiguityGateThreshold Revalidation, mono-fenêtre, MES M5, 59 jours.** Prérequis déjà tous satisfaits : P0-1/P0-2/P0-3 résolus (Lot 14.10), protocole TRAIN/VALIDATION/TEST/OOS défini et physiquement constructible sur CE dataset précis (§14.1), 2 339 candidats directionnels mesurés comme échantillon de départ. Limites à assumer explicitement dans ce futur lot : pas de Walk-Forward (une seule fenêtre), pas de coûts réels (barème toujours à sourcer), conclusions à qualifier comme "revalidation contrainte à 59 jours", jamais comme une validation générale multi-régime.

**Ne pas encore lancer** : toute calibration de fenêtres de régime, de poids Fusion/Decision, de Stop Loss, ou toute étude nécessitant Trending/RandomWalk/StableRange en volume suffisant — bloqué par la sous-représentation de ces régimes sur la seule fenêtre disponible (§20).

---

# 27. HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
14.12 (Historical Dataset Expansion & Regime Coverage) - COMPLETE

LAST COMPLETED:
14.11 (Calibration Experiment Design & Parameter Dependency Validation)

CURRENT STAGE:
Pre-calibration dataset preparation - COMPLETE, avec un blocage structurel majeur découvert (Yahoo = 60
jours maximum, jamais plus, quelle que soit la méthode)

PRODUCTION:
MES / M5 / ATAS

RESEARCH:
Yahoo Historical Data (MES=F, M5) - PLAFONNÉ À 59-60 JOURS GLISSANTS, DÉFINITIVEMENT

CURRENT DATASET:
Fenêtres de 45 jours (convention historique des Lots 14.2-14.11)

TARGET DATASET:
MES M5, 59 jours glissants (YahooHistoricalBarSource.DefaultMaxChunkSpanDays) - la profondeur MAXIMALE
que Yahoo fournit, pas un choix arbitraire. Mesuré concrètement le 2026-08-23 :
2026-06-25T19:35Z -> 2026-08-21T20:59:59Z, 11078 barres, fingerprint
F7B34C25153BDFC2472821E0BFC095869D05395CE46A868B81008A2E37109B05.

DATASET FINGERPRINT:
F7B34C25153BDFC2472821E0BFC095869D05395CE46A868B81008A2E37109B05 (spécifique à cette exécution du
2026-08-23 - non reproductible bit-à-bit un autre jour, la fenêtre étant glissante ; voir §23 du rapport
pour la méthode reproductible).

DATE RANGE:
2026-06-25T19:35:00Z -> 2026-08-21T20:59:59Z (mesuré le 2026-08-23 ; se décale chaque jour si re-téléchargé)

BAR COUNT:
11078 (WarmupBars=128, ReadyBars=10950)

REGIME COVERAGE:
MeanReverting 6507 (58.7%, REPRESENTED) / StructuralBreak 3376 (30.5%, REPRESENTED) / Trending 532 (4.8%,
UNDERREPRESENTED) / RandomWalk 518 (4.7%, UNDERREPRESENTED) / StableRange 145 (1.3%, UNDERREPRESENTED) /
Unknown 0 (ABSENT) / Transitional 0 (ABSENT).

BUY/SELL:
BUY=1185 (50.7%), SELL=1154 (49.3%) - équilibré, aucune correction appliquée ni nécessaire.

TRAIN:
~55% proposé (~6023 barres, ~31.4 jours) - conceptuel, non construit dans ce lot.

VALIDATION:
~15% proposé (~1643 barres, ~8.6 jours) - conceptuel, non construit.

TEST:
~15% proposé (~1643 barres, ~8.6 jours) - conceptuel, non construit ; représenté par un second
CalibrationWindowSet selon le mécanisme du Lot 14.11 §11.3 (pas un rôle natif de CalibrationWindowRole).

OOS:
~15% proposé (~1642 barres, ~8.6 jours, la plus récente) - conceptuel, non construit, jamais consultée.

WARMUP:
128 barres (convention établie depuis le Lot 14.1, confirmée suffisante et non-contaminante sur ce
dataset réel : ReadyBars=BarCount-WarmupBars exactement, 10950=11078-128).

DATA QUALITY:
Excellente sur tous les axes vérifiables : 0 violation OHLC, 0 doublon, 0 désordre temporel, 0 volume
négatif, 100% des gaps classifiables comme fermetures de marché plausibles (0 gap non expliqué), 4
barres à range nul sur 11078 (0.04%, sans commune mesure avec le défaut ATAS Sprint 15.18 de 36.5%).

DATA LIMITATIONS:
Yahoo = 60 jours maximum pour du M5, mur définitif et non contournable (confirmé empiriquement et déjà
documenté dans YahooChunkPlanner.cs avant ce lot). Walk-Forward multi-période IMPOSSIBLE via Yahoo seul
(une seule fenêtre glissante existe, jamais plusieurs époques historiques indépendantes accessibles).
Trending/RandomWalk/StableRange sous-représentés ; Transitional/Unknown totalement absents sur cette
fenêtre. Aucun calendrier CME réel dans le dépôt - classification des gaps = heuristique, pas une preuve
positive.

CALIBRATION:
NOT STARTED

KNOWN BLOCKERS:
D14.12-1 (P0, ce lot) : mur des 60 jours Yahoo - bloque toute extension au-delà de ~59 jours, bloque tout
Walk-Forward multi-période. D14.12-2 (P1, ce lot) : 4 des 7 régimes réels insuffisamment couverts sur
cette fenêtre unique - bloque toute analyse conditionnelle par régime robuste (hérite du blocage de
jointure D14.11-2, Decision.Winner non joint aux positions). Blocages hérités inchangés : coûts réels non
sourcés, méthodologie Stop Loss absente, RiskPolicy de production inexistante, sémantique OverallConfidence
non corrigée (D14.9/D14.11).

NEXT LOT:
14.13 (proposé) - AmbiguityGateThreshold Revalidation, mono-fenêtre, MES M5, 59 jours - portée
volontairement étroite (voir §26 du rapport pour la justification complète et les limites à assumer).

WHY:
C'est le seul lot de calibration dont TOUS les prérequis sont aujourd'hui satisfaits : binding réel
(Lot 14.10), fill réaliste (Lot 14.10), échantillon substantiel pour MeanReverting/StructuralBreak
(ce lot), protocole TRAIN/VALIDATION/TEST/OOS physiquement constructible sur ce dataset précis (ce lot).
Toute autre calibration (fenêtres de régime, poids Fusion/Decision, Stop Loss, Walk-Forward) reste bloquée
par au moins un prérequis non satisfait.

DO NOT CHANGE:
Ne PAS tenter d'étendre le dataset Yahoo au-delà de 59-60 jours - c'est un mur serveur, pas un paramètre
de ce dépôt. Ne PAS ajouter une nouvelle source de données sans une décision explicite de l'utilisateur
(brief §26 : documenter le blocage, ne pas le contourner automatiquement). Ne PAS interpréter la
sur-représentation de StructuralBreak (30.5%) comme un signal à corriger - c'est un fait descriptif, pas
un défaut prouvé, tant qu'aucune investigation dédiée n'a eu lieu. Ne PAS construire de Walk-Forward
multi-période en pensant que le chunking Yahoo le permettrait - il ne le permet pas (§14.2). Ne PAS
toucher RegimeEngine/EvidenceFusionEngine/FusionStateManager/DecisionEngine/DecisionArbitrator/
SignalEngine/EntryEngine/EntryTriggerEngine/EntryTriggerBuilder/TradePlanBuilder/RiskEngine/RiskPolicy/
Execution logic sans nécessité stricte documentée et approuvée.

IMPORTANT DECISIONS:
Cible de dataset fixée à 59 jours glissants (YahooHistoricalBarSource.DefaultMaxChunkSpanDays), jamais
plus - décision technique, pas un choix arbitraire (mur Yahoo confirmé §5). Aucun nouveau type "Dataset
Identity" créé - CalibrationDatasetSpecification/CalibrationDataset (Lot 14.9) couvrent déjà ce besoin.
Détection de gap réalisée en réutilisant RealMarketQualityAnalyzer (Sprint 15.18) via un adaptateur local,
jamais une nouvelle logique d'analyse. Aucune correction de déséquilibre régime/direction appliquée -
documentation uniquement, conformément à la règle absolue de ce lot.
```

**STOP.**
