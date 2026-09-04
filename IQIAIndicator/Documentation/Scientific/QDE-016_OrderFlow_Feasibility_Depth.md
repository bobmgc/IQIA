# QDE-016 — Engagement de l'axe order flow : faisabilité de profondeur historique et cadrage du collecteur

**Date :** 2026-09-01
**Mode :** lecture seule + cadrage. **Production modifiée : NON.**
**Sonde :** `IQIAIndicator/Tests/Research/OrderFlowFeasibility/AtasOrderFlowSurfaceProbeTests.cs` (réflexion pure sur `ATAS.Indicators.dll` / `ATAS.DataFeedsCore.dll` v7.0.9.461, aucune modification de type). Sortie : `Output/atas_orderflow_surface.txt`.

---

## 1. Contexte

QDE-015 ferme sans ambiguïté toute la piste prix-seul : MeanReverting (7 vérifications convergentes, `QDE-012`/`QDE-014`) et Trending (artefact de régime, `QDE-015`). QDE-013 a établi que l'axe order flow est la **seule piste non éliminée** apportant de l'information hors de la série de prix, avec un coût « moyen-élevé » et une **contrainte bloquante** : le harnais de recherche offline (`BacktestEngine` / `HistoricalSeries` / `CsvHistoricalBarSource`) est strictement OHLCV, aucun accès DOM/footprint/T&S n'y est câblé.

Ce lot engage l'axe en commençant par la question qui décide s'il s'agit d'un projet de **semaines** (backfill exploitable) ou de **mois** (accumulation live) : **la profondeur réelle de backfill des données order-flow par barre.**

---

## 2. Phase 0 — Profondeur réelle de backfill

### 2.1 Verdict net

**BACKFILL EXPLOITABLE = OUI.** (Résolu 2026-09-01 via `OrderFlowDepthProbe` exécuté dans ATAS — voir §2.5.)

> Note d'historique : la version initiale de ce rapport concluait « INDÉTERMINÉ dans cet environnement » car le lot QDE-016 était headless (pas d'instance ATAS). Le lot d'outillage QDE-016b a compilé `OrderFlowDepthProbe` en indicateur ATAS chargeable ; l'utilisateur l'a exécuté sur un graphique MES M5 connecté au flux réel. Résultat en §2.5.

**Ce qui est établi :** sur le flux/compte testé, les données order-flow par barre (`Bid`, `Ask`, `Delta`, `MaxDelta`, `MinDelta`, footprint 11–21 niveaux) sont **réelles, cohérentes (`bid + ask == volume` à l'unité près sur toutes les barres témoins) et complètes jusqu'à la barre la plus ancienne chargée (2026-04-28, ~126 jours calendaires)**, sans dégradation avec l'âge. → l'axe order flow est un **projet de semaines** (Phase 1a : export one-shot immédiat), pas de mois d'accumulation live.

**Surface d'API** (sonde de réflexion) : §2.2.

### 2.2 Surface d'API vérifiée (sonde de réflexion, `atas_orderflow_surface.txt`)

**`ATAS.Indicators.IndicatorCandle` — conteneur order-flow PAR BARRE (tous présents) :**

| Membre | Type | Sens |
|---|---|---|
| `Bid` | `decimal` | volume total exécuté au bid dans la barre |
| `Ask` | `decimal` | volume total exécuté à l'ask dans la barre |
| `Delta` | `decimal` | `Ask − Bid` (déséquilibre net) |
| `MaxDelta` / `MinDelta` | `decimal` | extrêmes du delta cumulé intra-barre |
| `Betweens` | `decimal` | volume exécuté ni au bid ni à l'ask (trades au mid) |
| `Ticks` | `decimal` | nombre de transactions dans la barre |
| `OI` / `MaxOI` / `MinOI` | `decimal` | open interest (fin de barre + extrêmes) |
| `Volume`, `Open/High/Low/Close`, `Time`, `LastTime` | — | OHLCV standard |
| `GetAllPriceLevels()` | `IEnumerable<PriceVolumeInfo>` | **footprint** : `{ Price, Bid, Ask, Between, Volume, Ticks }` par niveau de prix |
| `GetPriceVolumeInfo(decimal price)` | `PriceVolumeInfo` | footprint d'un prix donné |
| `MaxVolumePriceInfo`, `MaxBidPriceInfo`, `MaxAskPriceInfo`, `MaxPositiveDeltaPriceInfo`, `MaxNegativeDeltaPriceInfo`, `ValueArea` | — | POC / Value Area / clusters, dérivés du footprint |

**`ATAS.Indicators.Indicator` (classe de base) — accès order-flow (tous `protected`) :**

| Membre | Nature |
|---|---|
| `GetCandle(int bar)` → `IndicatorCandle` | **l'accès aux barres historiques** — c'est par là qu'on lit `Bid/Ask/Delta` de n'importe quelle barre chargée |
| `MarketDepthInfo` → `IMarketDepthInfoProvider`, `GetMarketDepthSnapshot()` → `IEnumerable<MarketDataArg>` | DOM (carnet) — **temps réel** |
| `OnNewTrade(MarketDataArg)`, `OnNewTrades`, `OnCumulativeTrade(CumulativeTrade)`, `OnUpdateCumulativeTrade` | callbacks trade — **temps réel** |
| `GetTradesCache(TimeSpan period)` → `ITradesCache` | **cache de trades borné par une fenêtre `TimeSpan`** — récent uniquement, pas d'historique indexable |
| `GetMarketByOrdersWithTradesCache(TimeSpan period)` → `IMarketByOrdersWithTradesCache` | idem pour le Market-by-Order |
| `RequestForCumulativeTrades(CumulativeTradesRequest)` + `OnCumulativeTradesResponse(...)` | **requête asynchrone** de trades cumulés — bornée par la disponibilité du flux |
| `SubscribeMarketByOrderData()` | abonnement MBO — temps réel |
| `CumulativeDomBids` / `CumulativeDomAsks` | totaux DOM courants |

`ATAS.DataFeedsCore` expose aussi `CumulativeTradesRequest`, `MarketDepthSnapshotRequest`, `MarketByOrdersManager` — une couche de requête existe, mais toujours gated par ce que le flux sert.

### 2.3 Ce que la surface d'API implique (prior, pas preuve)

1. **Le seul accès order-flow pour une barre historique arbitraire est `GetCandle(bar).Bid/.Ask/.Delta/...` + `GetAllPriceLevels()`.** Ces valeurs sont calculées par ATAS à partir des **ticks qu'il a chargés pour cette barre**.
2. **Le SDK modélise explicitement le détail trade-par-trade comme une fenêtre glissante bornée** (`GetTradesCache(TimeSpan)`, `GetMarketByOrdersWithTradesCache(TimeSpan)`) — pas un historique indexable. C'est un signe fort que la reconstruction footprint/delta pour des barres anciennes dépend d'un **backfill de ticks limité**.
3. **Comportement connu d'ATAS (littérature + pratique) :** sur un flux futures retail standard (Rithmic, CQG, dxFeed selon le plan), ATAS reconstruit le footprint/delta à partir des ticks qu'il télécharge, plafonné par le réglage « Nombre de jours à charger » du graphique ET par la profondeur de ticks que le flux fournit. Sans add-on d'historique de ticks payant, l'ordre de grandeur typique est **jour courant + quelques jours à quelques semaines** ; avec dxFeed/IQFeed sur le bon plan, **semaines à mois**. Avant l'instant de souscription de l'indicateur / au-delà de la profondeur de ticks, `Bid/Ask/Delta` valent **0** (ce sont des `decimal`, pas des nullables — 0 = « pas de données », indistinguable d'un vrai zéro sans croiser avec `Volume > 0` et `Ticks > 0`).

**Prior retenu pour le scoping, à confirmer par §2.4 :** backfill exploitable **limité (jours à quelques semaines)** → l'axe est majoritairement un projet **1b (accumulation live)**, avec une fenêtre récente **1a** partielle.

### 2.4 Check décisif minimal à exécuter dans ATAS (≈ 35 lignes, indicateur jetable — PAS de la production)

À déposer dans l'éditeur d'indicateurs d'ATAS (jamais dans `IQIAIndicator.dll`), sur un graphique **MES M5** (ou l'instrument disponible) chargé avec le maximum de jours possible, connecté au flux réel. Il écrit une ligne par barre témoin dans un CSV et permet de lire de vrais nombres à des timestamps identifiés.

```csharp
using System.Globalization;
using System.IO;
using ATAS.Indicators;

// OUTIL DE DIAGNOSTIC JETABLE — QDE-016 Phase 0. Ne pas committer, ne pas mettre dans un flux de production.
public class OrderFlowDepthProbe : Indicator
{
    private bool _done;
    protected override void OnCalculate(int bar, decimal value)
    {
        if (_done || CurrentBar < 10) return;
        _done = true;
        int n = CurrentBar;                       // dernière barre
        int[] probes = { 0, n / 8, n / 4, n / 2, (3 * n) / 4, n - 2 };  // de la plus ancienne à J-1
        string path = Path.Combine(Path.GetTempPath(), "atas_orderflow_depth_probe.csv");
        using var w = new StreamWriter(path, false);
        w.WriteLine("barIndex,time,volume,ticks,bid,ask,delta,maxDelta,minDelta,oi,footprintLevels");
        foreach (int b in probes)
        {
            var c = GetCandle(b);
            int levels = 0; foreach (var _ in c.GetAllPriceLevels()) levels++;
            w.WriteLine(string.Join(",", b, c.Time.ToString("O", CultureInfo.InvariantCulture),
                c.Volume, c.Ticks, c.Bid, c.Ask, c.Delta, c.MaxDelta, c.MinDelta, c.OI, levels));
        }
        this.LogInfo($"[OrderFlowDepthProbe] wrote {path}");
    }
}
```

**Lecture du résultat :**
- Pour chaque barre témoin : `volume > 0` et `ticks > 0` mais `bid == 0 && ask == 0 && footprintLevels == 0` → **pas de données order-flow à cette ancienneté** (barre OHLCV seule).
- `bid + ask ≈ volume` (à `betweens` près) et `footprintLevels > 1` → **données order-flow réelles et cohérentes**.
- La barre la plus ancienne où `bid + ask ≈ volume` définit la **profondeur de backfill réelle** (en jours, à convertir depuis `time`).
- Répéter avec « Nombre de jours à charger » du graphique poussé au max, et noter le flux/compte utilisés (la limite en dépend).

**Ce check tranche la Phase 0.**

### 2.5 Résultat mesuré (2026-09-01, `OrderFlowDepthProbe` dans ATAS)

Lot d'outillage QDE-016b : `OrderFlowDepthProbe` compilé en indicateur ATAS (`net8.0-windows` — ATAS tourne sur .NET 8 ; le premier build `net10` levait `TypeLoadException: InlineArray12`), déposé dans `%APPDATA%\ATAS\Indicators\`, exécuté par l'utilisateur sur un graphique **MES M5, flux réel**. Sortie `%TEMP%\atas_orderflow_depth_probe.csv` :

| barIndex | time (UTC) | volume | ticks | bid | ask | betweens | delta | maxDelta | minDelta | oi | footprintLevels |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | **2026-04-28 22:00** | 2290 | 1672 | 904 | 1386 | 0 | 482 | 543 | 15 | 0 | 21 |
| 3072 | 2026-05-14 01:00 | 1331 | 906 | 583 | 748 | 0 | 165 | 165 | −88 | 0 | 11 |
| 6145 | 2026-05-29 08:05 | 1291 | 797 | 726 | 565 | 0 | −161 | 112 | −198 | 0 | 14 |
| 12291 | 2026-06-30 18:15 | 3969 | 2273 | 1747 | 2222 | 0 | 475 | 480 | −87 | 0 | 14 |
| 18437 | 2026-07-31 05:25 | 790 | 554 | 275 | 515 | 0 | 240 | 259 | −56 | 0 | 15 |
| 24581 | 2026-09-01 11:25 | 987 | 651 | 423 | 564 | 0 | 141 | 158 | −31 | 0 | 15 |

`done.txt` : `CurrentBar=24583`.

**Interprétation :**
- **`bid + ask == volume` exactement** sur les 6 barres témoins, y compris la plus ancienne → réconciliation parfaite, données réelles (pas des zéros).
- **`delta == ask − bid`** partout ; `footprintLevels` 11–21 → footprint par prix réel ; `maxDelta`/`minDelta` peuplés.
- **Barre la plus ancienne chargée = 2026-04-28 → ~126 jours calendaires de MES M5** (24 583 barres), order-flow **complet et cohérent sur toute la fenêtre, sans dégradation avec l'âge**.
- `betweens == 0`, `oi == 0` partout → caractéristiques du flux (classification bid/ask systématique ; OI non fourni sur M5), pas un trou.

**Réserves :** un seul snapshot flux/compte ; 6 barres spot-checkées (6/6 réconcilient — preuve forte, non exhaustive) ; la borne 2026-04-28 = fin de la fenêtre de chargement du graphique, pas nécessairement la limite de l'historique tick du flux (« jours à charger » pas forcément au vrai max). Pousser le chargement plus haut révélerait la limite réelle (6–12 mois de M5 plausibles).

**Conséquence : backfill exploitable confirmé → chemin Phase 1a (§3), corpus immédiat ≈ 24 500 barres M5 (~3× le book H1 720 j de QDE-012) via un export one-shot. Le chemin 1b (§4, accumulation live) n'est plus le chemin dominant — il ne sert qu'à prolonger le corpus au-delà de la fenêtre de backfill.**

---

## 3. Phase 1a — Backfill exploitable CONFIRMÉ (scoping)

*§2.5 a confirmé `bid + ask == volume` jusqu'à la barre la plus ancienne (~126 jours). Corpus one-shot immédiat.*

### 3.1 Collecteur isolé (nouvelle classe, HORS production)

- **Emplacement :** un **projet d'indicateur ATAS séparé** (`OrderFlowCollector.csproj`, sa propre DLL, déposée dans le dossier Indicators d'ATAS) — pas dans `IQIAIndicator.dll`. Son unique rôle : logger, pas de signal, pas de trading. Ne participe à aucun flux de production. Alternative : `Tests/Research/OrderFlowCollector/` comme outil console autonome utilisant `ATAS.DataFeedsCore` + `CumulativeTradesRequest` (plus de travail, même contrainte de profondeur).
- **Comportement :** `OnCalculate` → à la clôture de chaque barre (`bar < CurrentBar`), écrire une ligne append-only :
  `Timestamp,Open,High,Low,Close,Volume,Bid,Ask,Delta,MaxDelta,MinDelta,OI,Ticks,Betweens`
  (optionnel : footprint sérialisé `price:bid:ask;price:bid:ask;...` via `GetAllPriceLevels()`).
- **Export initial :** au premier `OnCalculate`, boucler de `0` à `CurrentBar-1` via `GetCandle(b)` pour vider tout le backfill disponible d'un coup, puis passer en mode append barre-par-barre.
- **Volume de code :** ~60–80 lignes.

### 3.2 Lecteur de recherche (`IHistoricalBarSource`, sous `Tests/Research/`)

- `IHistoricalBarSource` et `HistoricalSeries.Create(...)` sont **publics** — un lecteur de recherche peut produire une `HistoricalSeries` avec les slots order-flow remplis **sans aucune modification de production** :
  - `HistoricalBar.BidVolume` / `AskVolume` / `Delta` / `OpenInterest` (déjà présents, `Core/MarketData/HistoricalBar.cs:36-39`) ← colonnes du CSV.
  - `MarketContextFactory.Create(...)` recopie déjà ces slots dans le `MarketContext` (`Core/MarketContextFactory.cs:156-158`).
- **Nom :** `Tests/Research/OrderFlowFeasibility/OrderFlowCsvBarSource.cs`, ~80 lignes (miroir de `CsvHistoricalBarSource` avec 5–8 colonnes supplémentaires).
- **Pas de nouveau format imposé à la production** : le format vit sous `Tests/Research/`.

### 3.3 Profondeur de corpus immédiatement constructible

À déterminer par §2.4. Estimations selon la profondeur `D` révélée :

| D (backfill) | Corpus M5 (barres) | Corpus H1 (barres) | Puissance vs référence QDE-012 (H1 720 j ≈ 8 500 barres) |
|---|---|---|---|
| 3 jours | ~830 | ~50 | négligeable |
| 2 semaines | ~3 900 | ~230 | faible — 1ᵉʳ coup d'œil seulement |
| 1 mois | ~5 700 | ~370 | faible en H1, correct en M5 pour un test à 3 terciles (~1 900/tercile) |
| 3 mois | ~17 000 | ~1 100 | M5 comparable à QDE-012 ; H1 encore faible |

→ Même avec un backfill « bon » de quelques semaines, seul un test **M5** aurait une puissance exploitable immédiatement. Un test H1 (le timeframe où QDE-012 avait la meilleure puissance) exigerait de toute façon une accumulation live de plusieurs mois. **1a seul ne suffit pas ; il faut 1b.**

---

## 4. Phase 1b — Accumulation live (scoping, PAS de construction dans ce lot)

*C'est le chemin dominant selon le prior §2.3, et le chemin nécessaire pour H1 même si 1a fonctionne.*

### 4.1 Plan d'accumulation minimal

- **Quel composant enregistrer :** l'indicateur logger séparé de §3.1 (DLL distincte, pas `IQIAIndicator.dll`). Rien d'autre à écrire — c'est le même artefact que 1a.
- **Quelle fréquence :** une ligne par barre **fermée** (`bar < CurrentBar`), sur le(s) timeframe(s) cible(s). Recommandé : logger **M5** (et dériver M15/H1 par ré-agrégation, comme fait pour Yahoo depuis QDE-012) plutôt que logger H1 directement — un mois de M5 vaut ~15× plus de barres qu'un mois de H1 pour le même temps réel.
- **Où stocker :** CSV append-only dans un dossier hors du dépôt (ex. `%LOCALAPPDATA%\IQIA\orderflow\MES_M5.csv`), un fichier par (instrument, timeframe). Rotation mensuelle optionnelle. À sauvegarder — un flux perdu = du temps perdu, pas rattrapable.
- **Robustesse :** l'indicateur doit être chargé en permanence sur un graphique ATAS ouvert et connecté (ATAS lancé en continu, ou relancé chaque matin). Gérer les doublons à la lecture (barre déjà loggée) et les trous (ATAS fermé) — le lecteur de recherche filtre sur timestamp strictement croissant, comme `HistoricalSeries.TryCreate` le fait déjà.

### 4.2 Délai réaliste avant un corpus exploitable

Barres par jour de bourse (session MES ~23 h) : **M5 ≈ 276 barres/jour**, **M15 ≈ 92**, **H1 ≈ 17**. ~21 jours de bourse/mois.

| Objectif | Seuil (cohérent avec QDE-012+) | Temps M5 | Temps H1 |
|---|---|---|---|
| 1ᵉʳ coup d'œil faible puissance (n ≈ 300–400/tercile) | ~1 000 barres | **~4 jours** | ~2 mois |
| Test à 3 terciles avec IC utiles (n ≈ 600–1 000/tercile, ~2 000–3 000 barres) | ~2 500 barres | **~2 semaines** | ~6 mois |
| Puissance comparable au book H1 de QDE-012 (≈ 8 500 barres, IC ± ~0,025 R possible) | ~8 500 barres | **~6 semaines** | ~24 mois |
| Grille 2×3 (signe × niveau), n ≥ 30 par cellule après filtrage régime | ~5 000–8 000 barres | ~3–6 semaines | > 1 an |

**Conclusion de délai :**
- En **loggant M5** (et en dérivant M15/H1) : **premier résultat faible-puissance en ~1 semaine**, **résultat robuste comparable à QDE-012 en ~1,5–2 mois** d'accumulation continue.
- En **loggant H1 directement** : **6 mois minimum** pour une puissance décente, **~2 ans** pour égaler la référence QDE-012.

→ **Recommandation ferme : logger M5.** C'est la différence entre « ~2 mois » et « ~2 ans » avant une décision d'edge.

### 4.3 Ce que ça change par rapport à l'évaluation « coût moyen-élevé » de QDE-013

QDE-013 chiffrait l'axe order flow « coût moyen-élevé » en effort d'intégration. Ce lot précise que le coût dominant n'est **pas** l'effort de code (~150 lignes total : logger + lecteur, aucune modification de production) mais le **délai calendaire d'accumulation** : **~2 mois de wall-clock avant un premier verdict d'edge exploitable** (en loggant M5), pendant lesquels l'axe recherche-d'edge n'a rien à analyser. C'est une décision de priorisation différente de « quelques semaines de dev ».

---

## 5. Établi avec confiance

1. **Phase 0 TRANCHÉE : backfill order-flow exploitable = OUI** (§2.5). Sur le flux/compte MES testé, `Bid/Ask/Delta/MaxDelta/MinDelta` + footprint (11–21 niveaux) sont réels et cohérents (`bid + ask == volume` exact) jusqu'à la barre la plus ancienne chargée — **~126 jours de MES M5, sans dégradation avec l'âge**. 24 583 barres disponibles immédiatement.
2. **Surface d'API vérifiée (réflexion) :** `IndicatorCandle` expose par barre `Bid`, `Ask`, `Delta`, `MaxDelta`, `MinDelta`, `Betweens`, `Ticks`, `OI` + footprint complet (`GetAllPriceLevels` → `PriceVolumeInfo{Price,Bid,Ask,Between,Volume,Ticks}`). Accès historique via `GetCandle(bar)` (protected).
3. **Le détail trade-par-trade brut reste une fenêtre glissante bornée** (`GetTradesCache(TimeSpan)`, `GetMarketByOrdersWithTradesCache(TimeSpan)`) — mais les **agrégats par barre** (`GetCandle().Bid/.Ask/.Delta` + footprint) sont, eux, complets sur toute la fenêtre chargée (§2.5). C'est cette voie-là qu'on exploite.
4. **Effort de code du collecteur + lecteur : faible** (~150 lignes, un indicateur ATAS séparé + un `IHistoricalBarSource` sous `Tests/Research/`, `HistoricalBar` a déjà les slots, aucune modification de production requise).
5. **Le coût de l'axe devient un projet de SEMAINES, pas de mois.** Export one-shot de ~24 500 barres M5 (~3× le book H1 720 j de QDE-012) → premier test de conditionnement order-flow immédiat. L'accumulation live (§4) ne sert qu'à prolonger au-delà de ~126 jours.

---

## 6. Recommandation

*(Phase 0 tranchée — recommandation révisée.)*

1. **Construire le logger M5 séparé** (§3.1 : indicateur ATAS jetable, DLL distincte, `net8.0-windows`, ~60 lignes). Deux usages : (a) l'appel initial vide tout le backfill via `GetCandle(0..CurrentBar-1)` → export one-shot des ~24 500 barres ; (b) mode append pour prolonger en continu.
2. **Construire le lecteur de recherche** `Tests/Research/OrderFlowFeasibility/OrderFlowCsvBarSource.cs` (~80 lignes) : lit le CSV, produit une `HistoricalSeries` avec `HistoricalBar.BidVolume/AskVolume/Delta/OpenInterest` remplis. `IHistoricalBarSource` + `HistoricalSeries.Create` publics → zéro modification de production.
3. **Premier test de conditionnement order-flow (QDE-017 candidat), immédiat sur l'export one-shot :** sur les ~24 500 barres M5 (et H1/M15 par ré-agrégation), segmenter par tercile de variables order-flow — `Delta/Volume` (pression signée normalisée), `|Delta|/Volume`, déséquilibre footprint (concentration bid vs ask autour du POC), `MaxDelta − MinDelta` (amplitude d'excursion) — et mesurer l'expectancy du rendement forward net de coût par segment, IC 95 %, split train/OOS purgé. **Méthodologie strictement identique à QDE-014**, donc directement comparable.
4. **Pousser « jours à charger » plus haut** dans ATAS et relancer `OrderFlowDepthProbe` pour connaître la limite réelle du flux (6–12 mois de M5 plausibles) avant de figer la fenêtre de l'export.
5. **Décision de priorisation :** l'axe order flow ne demande plus ~2 mois de wall-clock — l'échantillon existe déjà. Le seul coût est ~150 lignes + une session ATAS pour l'export. → **recommandé : lancer QDE-017.**

**Ne PAS :** modifier `ScientificDatasetCollector`, `CsvHistoricalBarSource`, `HistoricalSeries` ou tout type de production ; mettre le logger dans `IQIAIndicator.dll` ; calibrer quoi que ce soit ; toucher au statut SIGNAL_ONLY.

---

## 7. Interdictions respectées

- **Aucune modification de `ScientificDatasetCollector`, `CsvHistoricalBarSource`, `HistoricalSeries`** ni d'aucun type de production. Fichiers ajoutés dans le dépôt : `Tests/Research/OrderFlowFeasibility/AtasOrderFlowSurfaceProbeTests.cs` (réflexion pure).
- **`OrderFlowDepthProbe`** (QDE-016b) a été compilé et déployé, mais **hors du dépôt IQIA** : projet à `C:\Users\rnbch\OneDrive\Bureau\OrderFlowDepthProbe\` (sibling du dépôt, jamais dans la solution, jamais référencé par la production, `.gitignore *`), DLL déposée dans `%APPDATA%\ATAS\Indicators\` à côté d'`IQIAIndicator.dll`. `IQIAIndicator.csproj` intouché (chemins SDK dupliqués localement dans le projet jetable).
- **Le collecteur/lecteur de recherche ne sont pas construits dans ce lot** — seulement scopés (§3). Quand ils le seront, ils resteront isolés (DLL d'indicateur séparée + `Tests/Research/`).
- **Aucune calibration, aucune Decision Rule modifiée, statut SIGNAL_ONLY inchangé.**

---

## 8. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-016_OrderFlow_Feasibility_Depth.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/AtasOrderFlowSurfaceProbeTests.cs` | Sonde de réflexion sur la surface SDK, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/Output/atas_orderflow_surface.txt` | Dump de la surface d'API (preuve §2.2) | Recherche uniquement |
| `C:\Users\rnbch\OneDrive\Bureau\OrderFlowDepthProbe\` *(hors dépôt)* | Projet jetable QDE-016b : `.csproj` (`net8.0-windows`), `OrderFlowDepthProbe.cs`, `README.txt`, `.gitignore *` | **Jamais** — hors du dépôt IQIA par construction |
| `%APPDATA%\ATAS\Indicators\OrderFlowDepthProbe.dll` | DLL de diagnostic déployée dans ATAS | Jetable — à supprimer après usage |
| `%TEMP%\atas_orderflow_depth_probe.csv` + `.done.txt` | Sortie mesurée (preuve §2.5) | — |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/OrderFlowFeasibility/` ; supprimer le dossier `OrderFlowDepthProbe\` et `%APPDATA%\ATAS\Indicators\OrderFlowDepthProbe.dll`.

---

## 9. FINAL OUTPUT

**STATUS :** Phase 0 **TRANCHÉE — backfill order-flow exploitable = OUI** (§2.5, mesuré via `OrderFlowDepthProbe` dans ATAS le 2026-09-01). Sur le flux/compte MES : `Bid/Ask/Delta/MaxDelta/MinDelta` + footprint (11–21 niveaux) réels et cohérents (`bid + ask == volume` exact) jusqu'à la barre la plus ancienne chargée — **~126 jours de MES M5, 24 583 barres, aucune dégradation avec l'âge**. `betweens`/`oi` = 0 (caractéristiques du flux). L'axe order flow passe de « projet de ~2 mois » à « projet de semaines » : l'échantillon existe déjà, export one-shot immédiat.

**PRODUCTION MODIFIED :** NO. Dépôt IQIA : 1 sonde de réflexion (`Tests/Research/OrderFlowFeasibility/`). Hors dépôt : projet jetable `OrderFlowDepthProbe` (sibling, `.gitignore *`) + DLL dans `%APPDATA%\ATAS\Indicators\`. `IQIAIndicator.csproj` intouché.

**TESTS :** `AtasOrderFlowSurfaceProbeTests` — Passed (surface SDK v7.0.9.461). `OrderFlowDepthProbe` — build `net8.0-windows` OK (le premier build `net10` levait `TypeLoadException: InlineArray12` car ATAS tourne sur .NET 8), exécuté dans ATAS, CSV + marker écrits.

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-016_OrderFlow_Feasibility_Depth.md`.

**NEXT ACTION :** Lancer **QDE-017 — premier test de conditionnement order-flow** : (1) logger M5 séparé (~60 lignes, `net8.0-windows`) → export one-shot des ~24 500 barres via `GetCandle(0..CurrentBar-1)` ; (2) lecteur `OrderFlowCsvBarSource` sous `Tests/Research/` (~80 lignes, remplit `HistoricalBar.BidVolume/AskVolume/Delta`) ; (3) segmenter les barres par tercile de `Delta/Volume`, `|Delta|/Volume`, déséquilibre footprint, `MaxDelta − MinDelta` → expectancy du rendement forward net de coût par segment, IC 95 %, split train/OOS purgé — méthodologie identique à QDE-014. Optionnel : pousser « jours à charger » plus haut dans ATAS + relancer `OrderFlowDepthProbe` pour connaître la limite réelle du flux avant de figer la fenêtre.
