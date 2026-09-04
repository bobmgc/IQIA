# QDE-013 — Cadrage de faisabilité : nouvelle source de données pour la recherche d'edge (post-QDE-012)

**Date :** 2026-09-01
**Mode :** lecture seule / cadrage. **Production modifiée : NON.**
**Contexte :** QDE-012 a établi qu'il n'existe pas de dépendance sérielle exploitable dans le prix seul (MES, intraday M5/M15/H1). Ce lot ne construit rien — il détermine ce qui est réellement accessible et à quel coût d'intégration, pour une décision de priorisation.
**Sonde :** 1 fichier de connectivité isolé, `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/YahooCrossAssetProbeTests.cs` (read-only, jamais dans un flux de production, réutilise `HttpYahooChartClient` sans le modifier). Sortie : `Output/yahoo_cross_asset_probe.txt`.

---

## 1. Tableau récapitulatif de faisabilité

| Axe | Disponibilité | Profondeur d'historique accessible | Coût d'intégration | Abonnement payant |
|---|---|---|---|---|
| **1. Order flow / carnet d'ordres** (footprint, delta, DOM, T&S) | **Partielle** — surface *agrégée par barre* (bid vol / ask vol / delta) accessible dans le SDK ATAS et **déjà lue** par le code live ; footprint par niveau de prix accessible via le SDK mais **non câblée** ; DOM live et Time&Sales **live seulement** | *Live/replay ATAS uniquement.* Delta/footprint par barre : historique = ce que le flux fournit (souvent quelques semaines de ticks, sauf flux tick premium). **Aucun accès offline** — le harnais de recherche (`BacktestEngine`/`HistoricalSeries`/CSV) est strictement OHLCV. | **Moyen à élevé** — nécessite (a) un collecteur ATAS écrivant delta/footprint par barre dans un nouveau format CSV, (b) un nouveau `IHistoricalBarSource` + extension du lecteur CSV, (c) un corpus ne pouvant être constitué qu'en **faisant tourner ATAS** (pas de backfill Yahoo) | **Conditionnel** — non pour un pilote sur historique récent avec le flux actuel ; **oui** pour un historique profond (mois/années) de ticks/footprint (flux tick premium) |
| **2. Cross-asset** (VIX, rendements, USD, indices/futures corrélés) | **Confirmée** (sonde) — 11 des 12 tickers testés servis à 5m/15m/1h ; seul `DX=F` introuvable (utiliser `DX-Y.NYB`) | **5m / 15m ≈ 58 jours** (comme MES) ; **1h ≈ 700 jours** (2024-10 → 2026-09) — aligné sur l'échantillon H1 MES de QDE-012, celui à la meilleure puissance statistique | **Faible** — mécanisme identique à `YahooHistoricalBarSource` existant ; seul changement production = ajouter des entrées à `YahooSymbolMap` (dictionnaire de données) ; une sonde peut contourner même cela (fait en QDE-012). Aligneur multi-séries (jointure sur timestamp) ≈ 50 lignes | **NON** |
| **3. Calendrier macro / événementiel** (FOMC, NFP, CPI…) | **Absente du projet** (grep production `fomc\|nfp\|cpi\|economic.calendar\|earnings` → 0 résultat). Options externes existent (voir §3.3) | Selon la source : CSV statique maintenu à la main = illimité ; FRED `releases/dates` = historique complet des dates de publication ; FMP `economic_calendar` = ~1 an glissant en tier gratuit | **Faible** — CSV statique (30–40 événements/an, MAJ annuelle) ou FRED API (clé gratuite). C'est un **filtre de conditionnement**, pas un edge autonome | **NON** (options gratuites : CSV statique, FRED, tier gratuit FMP). Payant seulement pour TradingEconomics complet / Econoday |
| **4. Volatilité implicite (options)** | **Niveau IV : confirmée** via `^VIX` / `^VIX9D` / `^VIX3M` (sonde, mêmes chiffres que l'axe 2). **Surface / skew / chaînes historiques : indisponible** — endpoint options Yahoo = HTTP 401 sans crumb, et par conception snapshot-only + actions/ETF seulement (jamais MES/ES) | `^VIX` : 5m/15m ≈ 57 j, **1h ≈ 700 j** ; term-structure `^VIX9D`/`^VIX3M` : 1h ≈ 699 j (RTH seulement). Surface historique : **0** depuis toute source déjà accessible | **Niveau IV : faible** (= axe 2, un ticker de plus). **Surface historique : élevé** (source payante dédiée, hors périmètre) | **Niveau IV : NON.** Surface historique : **OUI** (CBOE DataShop / ORATS / broker options) |

---

## 2. Points d'accès précis (axes marqués disponibles)

### Axe 1 — Order flow (partiel)

- **Surface agrégée par barre, DÉJÀ lue par le code live :**
  `IQIAIndicator/Core/MarketContextBuilder.cs:124-126` —
  ```csharp
  bidVolume:    c.Bid,      // volume à l'offre dans la barre
  askVolume:    c.Ask,      // volume à la demande dans la barre
  delta:        c.Delta,    // Ask - Bid
  ```
  où `c` est un `ATAS.Indicators.IndicatorCandle` obtenu par `GetCandle(bar)` (`IQIAIndicator.cs:1337`, `1353`). Ces valeurs sont **historiques par barre chargée** dans un graphique ATAS. Elles transitent dans `Core/VolumeInfo.cs` (`BidVolume`, `AskVolume`, `Delta`) puis **s'arrêtent là** — aucune Evidence, aucun ScientificModel, aucune Decision Rule ne les consomme aujourd'hui.

- **Slots de données déjà présents mais jamais peuplés hors live ATAS :**
  `IQIAIndicator/Core/MarketData/HistoricalBar.cs:36-39` — champs `BidVolume`, `AskVolume`, `Delta`, `OpenInterest` (nullables) ;
  `IQIAIndicator/Core/MarketContextFactory.cs:156-158` — les recopie dans le `MarketContext` ;
  `IQIAIndicator/Backtest/HistoricalSeriesFingerprint.cs:46-49` — les inclut dans l'empreinte déterministe.
  **Mais** `IQIAIndicator/Core/MarketData/CsvHistoricalBarSource.cs:25,30,34-36` fige l'en-tête à 10 colonnes OHLCV (`SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar`) et documente explicitement : *« Bid/Ask/Delta/OpenInterest are absent from this format and therefore stay null »*. Le `YahooChartParser` ne les produit jamais (Yahoo = OHLCV pur ; le seul `Delta` dans `HttpYahooChartClient.cs:215` est l'en-tête HTTP `Retry-After`, sans rapport).

- **Footprint par niveau de prix / DOM :** non référencés dans la codebase (grep `MarketDepth\|PriceVolumeInfo\|GetAllPriceLevels\|footprint` → 0 hors ce rapport et QDE-012). Le SDK ATAS les expose (`IndicatorCandle.GetAllPriceLevels()` → `PriceVolumeInfo` par prix ; `Indicator.MarketDepthInfo` / abonnement market-by-order pour le DOM live), mais rien n'est câblé. Les seuls autres points de contact ATAS market-data du projet : `ATAS.DataFeedsCore.Security` (métadonnées instrument : tick size, quantity step — `Infrastructure/ATAS/ATASInstrumentAdapter.cs:32`, `ATASInstrumentQuantityStepResolver.cs:46`) et `ATAS.DataFeedsCore.Statistics.EquityValue` (équité compte).

- **Rappel structurel :** l'audit de réflexion du SDK (Sprint 15.19, cité `Infrastructure/ATAS/ATASRuntimeDiagnostics.cs:138`) confirme que `ATAS.Indicators.dll` / `ATAS.DataFeedsCore.dll` / `ATAS.Types.dll` n'exposent **aucun** signal de clôture explicite ni flag de replay — le projet dépend déjà d'heuristiques pour ces points. L'order flow historique profond a la même fragilité : dépendant du flux.

### Axe 2 — Cross-asset (confirmé par sonde, `Output/yahoo_cross_asset_probe.txt`)

- **Point d'accès :** `IQIAIndicator/Core/MarketData/Yahoo/HttpYahooChartClient.cs:82` — `FetchChartJson(yahooTicker, yahooInterval, periodStartUtc, periodEndUtc, ct)` (classe `internal`, accessible au projet Tests via `InternalsVisibleTo("IQIAIndicator.Tests")`, `Properties/AssemblyInfo.cs:3`), suivi de `IQIAIndicator/Core/MarketData/Yahoo/YahooChartParser.cs:36` — `Parse(json)` → `HistoricalBar[]`.
- **Endpoint sous-jacent :** `https://query2.finance.yahoo.com/v8/finance/chart/{ticker}?interval={5m|15m|1h}&period1=…&period2=…`
- **Pour un usage production** il faudrait ajouter les tickers à `IQIAIndicator/Core/MarketData/Yahoo/YahooSymbolMap.cs:26` (aujourd'hui allowlist strict : `Resolve` lève `NotSupportedException` sur un symbole non mappé ; entrées actuelles : ES, MES, NQ, YM, RTY, GC, CL).

**Disponibilité mesurée (fenêtre 5m/15m = 58 j, 1h = 700 j) :**

| instrument | ticker | 5m | 15m | 1h | note |
|---|---|---|---|---|---|
| VIX (IV 30 j) | `^VIX` | 6408 barres / 57 j | 2137 / 57 j | **6823 / 700 j** (2024-10→2026-09) | proxy IV le plus simple |
| VIX 9 jours | `^VIX9D` | 3199 / 56 j | 1067 / 56 j | 3351 / 699 j | front de term-structure (RTH) |
| VIX 3 mois | `^VIX3M` | 3199 / 56 j | 1067 / 56 j | 3351 / 699 j | back de term-structure (RTH) |
| S&P 500 cash | `^GSPC` | 3198 / 56 j | 1067 / 56 j | 3346 / 699 j | RTH seulement, pas d'overnight |
| Rendement 10 ans | `^TNX` | 3280 / 56 j | 1107 / 56 j | 3360 / 699 j | taux / risk-on-off (RTH) |
| USD Index (spot ICE) | `DX-Y.NYB` | 11180 / 57 j | 3757 / 57 j | 11470 / 700 j | 24 h, ~2500–3000 slots vides |
| USD Index (future) | `DX=F` | — | — | — | **introuvable (delisted)** → utiliser `DX-Y.NYB` |
| T-Note 10 ans (future) | `ZN=F` | 11358 / 57 j | 3815 / 57 j | 10942 / 700 j | 23 h, ~2900 gaps (comme MES) |
| T-Bond 30 ans (future) | `ZB=F` | 11217 / 57 j | 3811 / 57 j | 10940 / 700 j | idem |
| EUR/USD (future) | `6E=F` | 11385 / 57 j | 3815 / 57 j | 11007 / 700 j | idem |
| Nasdaq-100 (future) | `NQ=F` | 11386 / 57 j | 3815 / 57 j | 10990 / 700 j | indice actions corrélé, déjà mappé |
| Or (future) | `GC=F` | 11420 / 57 j | 3816 / 57 j | 11013 / 700 j | déjà mappé, contrôle |

**33 combinaisons (ticker, intervalle) sur 36 ont renvoyé des barres exploitables.** Les indices cash (`^GSPC`, `^VIX`, `^VIX9D`, `^VIX3M`, `^TNX`) sont RTH seulement (~6,5 h/j) → alignement partiel avec les sessions MES de ~23 h ; les futures (`ZN=F`, `NQ=F`, `6E=F`, `GC=F`, `DX-Y.NYB`) s'alignent bien.

### Axe 4 — Volatilité implicite (niveau uniquement)

- **Confirmé disponible :** `^VIX`, `^VIX9D`, `^VIX3M` via le même point d'accès que l'axe 2 (`HttpYahooChartClient.FetchChartJson`). Le ratio `^VIX3M / ^VIX` donne une pente de term-structure horaire sur ~700 j.
- **Confirmé indisponible :** endpoint `https://query2.finance.yahoo.com/v7/finance/options/{symbole}` → **HTTP 401** pour `SPY`, `^SPX`, `ES=F`, `MES=F` (nécessite le crumb/cookie que `HttpYahooChartClient` ne gère que pour le endpoint chart). Même avec le crumb : snapshot courant seulement (pas d'historique), et options actions/ETF uniquement (jamais de futures MES/ES). Aucune surface de vol implicite historique depuis une source déjà accessible.

---

## 3. Détail par axe

### 3.1 Order flow — ce que le SDK ATAS expose vraiment

| Donnée | SDK ATAS | Historisée ? | Dans la codebase aujourd'hui |
|---|---|---|---|
| Bid volume / Ask volume / Delta **par barre** | `IndicatorCandle.Bid` / `.Ask` / `.Delta` | Oui, par barre chargée (profondeur = flux + « jours à charger ») | **Lue** (`MarketContextBuilder.cs:126`), stockée dans `VolumeInfo`, **non consommée** en aval |
| MaxDelta / MinDelta par barre | `IndicatorCandle.MaxDelta` / `.MinDelta` | Oui, par barre | Non lue |
| Footprint (volume/bid/ask par niveau de prix) | `IndicatorCandle.GetAllPriceLevels()` → `PriceVolumeInfo` | Oui, par barre | Non lue |
| DOM (carnet) | `Indicator.MarketDepthInfo`, `OnBestBidAskChanged`, abonnement market-by-order | **Live seulement** — pas de snapshot DOM historique | Non câblé |
| Time & Sales / trades individuels | `OnNewTrade(MarketDataArg)`, `CumulativeTrade` | **Live seulement** (l'historique tick, s'il existe, est agrégé dans le footprint) | Non câblé |

**Contrainte bloquante pour la recherche :** tout le pipeline de recherche offline (`BacktestEngine.RunFullBacktest` → `HistoricalSeries` → `CsvHistoricalBarSource` / `YahooHistoricalBarSource`) est **OHLCV pur**. Il n'existe aucun chemin pour rejouer du delta/footprint hors d'un graphique ATAS en direct. Un pilote order flow imposerait donc :
1. étendre le collecteur ATAS (type `ScientificDatasetCollector`) pour écrire delta/footprint par barre → nouveau format CSV ;
2. écrire un `IHistoricalBarSource` qui le relit + peupler `HistoricalBar.BidVolume/AskVolume/Delta` (slots déjà là) ;
3. accepter que le corpus ne se constitue **qu'en faisant tourner ATAS en live/replay** — pas de backfill, et une profondeur limitée par le flux (souvent semaines, pas années, sans flux tick premium).

### 3.2 Cross-asset — voir §2. Rien à ajouter : disponibilité confirmée, coût faible, gratuit.

### 3.3 Calendrier macro — options d'accès (aucune intégrée)

| Option | Type | Coût | Historique | Effort |
|---|---|---|---|---|
| **CSV statique maintenu à la main** | Fichier | Gratuit, MAJ ~1×/an | Illimité (rétro + prospectif) | Faible. FOMC (8/an, publié ~1 an à l'avance par la Fed) + NFP + CPI + PPI + PCE + GDP (calendriers annuels BLS/BEA). ~30–40 événements haut-impact/an |
| **FRED `fred/releases/dates`** (St. Louis Fed) | API REST | Gratuit (clé API) | Complet, officiel | Faible. Dates de publication horodatées pour chaque release FRED |
| **FinancialModelingPrep `/economic_calendar`** | API REST | Tier gratuit (~250 req/j) | ~1 an glissant | Faible. Actual/forecast/previous + timestamp |
| **TradingEconomics API** | API REST | Tier gratuit très limité ; complet **payant** | Long | Moyen |
| Scrapes (Investing.com, ForexFactory, Nasdaq) | HTML | Gratuit | Variable | Fragile, ToS discutable — **non recommandé** |

Nature de l'axe : un calendrier est un **filtre de conditionnement** (« ne pas trader / trader différemment autour d'un événement », ou « la variance conditionnelle post-FOMC est-elle prévisible ») — pas un signal directionnel autonome. À combiner avec un pilote d'edge, pas à tester seul.

### 3.4 Volatilité implicite — voir §2. Niveau (VIX + term-structure) : disponible, gratuit, coût faible. Surface/skew historique : hors de portée sans source payante dédiée.

---

## 4. Recommandation motivée

### Pilote #1 recommandé : **Axe 2 (cross-asset), VIX inclus comme cas particulier de l'axe 4**

**Justification :**

1. **Coût d'intégration le plus faible, risque le plus faible.** Le chemin `HttpYahooChartClient.FetchChartJson` + `YahooChartParser.Parse` est exactement celui déjà utilisé et éprouvé dans QDE-012 (8 sondes, dont le pull `1h` natif). Une sonde de recherche peut consommer n'importe quel ticker sans toucher un seul fichier de production.

2. **Zéro dépendance payante.**

3. **Profondeur d'historique la plus longue immédiatement disponible : ~700 jours en 1h.** C'est précisément la fenêtre H1 qui, dans QDE-012, a donné la meilleure puissance statistique (2 565 trades, IC brut excluant 0). Un test cross-asset y hériterait directement de cette puissance.

4. **Teste frontalement la piste que la conclusion de QDE-012 désigne :** « l'information hors de la série de prix ». Hypothèses concrètes et falsifiables :
   - le **niveau ou la variation du VIX** conditionne-t-il le rendement forward de MES (ex. réversion plus forte en VIX bas, momentum en VIX haut) ?
   - la **pente de term-structure** `^VIX3M/^VIX` (contango/backwardation) sépare-t-elle des régimes exploitables ?
   - un **écart cross-indice** (MES vs `NQ=F` en écart de rendement normalisé) prédit-il un retour à la moyenne du spread ?
   - une **variation de rendement 10 ans** (`^TNX` / `ZN=F`) ou de l'**USD** (`DX-Y.NYB`) a-t-elle un effet directionnel horaire sur MES net de coûts ?
   Chaque test suit exactement la méthodologie QDE-012 (expectancy en R par tranche, IC 95 %, split train/OOS, coût base ~0,77 pt), donc directement comparable.

5. **Élimination rapide.** Si aucune variable cross-asset ne conditionne le rendement MES au-delà du bruit (comme le prix seul), c'est établi vite et à coût quasi nul — et cela renforce le prior avant d'engager le coût élevé de l'axe 1.

**Attention méthodologique :** l'alignement temporel. Les indices cash (`^VIX`, `^GSPC`, `^TNX`) sont RTH-only → n'utiliser leurs valeurs que « dernière valeur connue » (forward-fill causal) hors RTH, ou restreindre les tests aux heures RTH. Les futures (`ZN=F`, `NQ=F`, `DX-Y.NYB`) s'alignent mieux sur les ~23 h de MES.

### Ordre de priorité des axes suivants

- **Pilote #2 — Axe 1 (order flow)**, *si et seulement si* le pilote #1 ne trouve rien : c'est théoriquement la source la plus riche (le déséquilibre acheteur/vendeur est de l'information non contenue dans l'OHLC), mais le coût d'intégration est moyen-à-élevé, il est incompatible avec le harnais offline actuel, et il est potentiellement gated sur un flux tick payant pour un historique exploitable. À n'engager qu'après avoir épuisé le cross-asset.
- **Pilote #3 — Axe 3 (calendrier)** : jamais en premier (filtre, pas edge). À greffer sur le pilote #1 une fois qu'un conditionnement cross-asset prometteur est identifié (« l'effet tient-il aussi hors des fenêtres FOMC/NFP ? »).
- **Axe 4 (surface IV historique)** : **écarté** pour l'instant — indisponible sans source payante dédiée. Le *niveau* IV (VIX) est déjà couvert par le pilote #1.

### Il existe une piste viable

Contrairement au verrou de QDE-012, il **existe** une piste d'edge légitime, immédiatement testable, sans coût et sans modification de production : conditionner le rendement forward de MES sur un état cross-asset / volatilité implicite (VIX niveau + term-structure) sur ~700 jours d'historique horaire. C'est le pilote #1.

---

## 5. Interdictions respectées

- **Aucune modification de code de production.** Un seul fichier ajouté, `Tests/Research/NewDataSourceFeasibility/YahooCrossAssetProbeTests.cs`, isolé, réutilisant `HttpYahooChartClient` / `YahooChartParser` **sans les modifier** (classes `internal`, visibles via `InternalsVisibleTo` déjà présent). Jamais dans un flux de production.
- **Aucune intégration réelle de nouvelle API.** La sonde fait des requêtes de connectivité ponctuelles vers `query2.finance.yahoo.com` (le même hôte que l'intégration Yahoo existante) uniquement pour confirmer l'accessibilité, comme toléré.
- **Aucun abonnement / souscription payante.** Toutes les options recommandées (Yahoo, CSV statique, FRED, tier gratuit FMP) sont gratuites. Les options payantes (TradingEconomics complet, CBOE DataShop, ORATS, flux tick premium) sont **listées, jamais engagées**.
- **Risk Engine, Decision Rule, statut SIGNAL_ONLY : inchangés.**

---

## 6. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-013_Feasibility_NewDataSource.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/YahooCrossAssetProbeTests.cs` | Sonde de connectivité read-only | Recherche uniquement — jamais dans un flux de production |
| `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/Output/yahoo_cross_asset_probe.txt` | Sortie mesurée (preuve des chiffres §2) | Recherche uniquement |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/NewDataSourceFeasibility/`.

---

## 7. FINAL OUTPUT

**STATUS :** Cadrage terminé. 4 axes évalués. Une piste viable identifiée (cross-asset + VIX), immédiatement testable, gratuite, sans modification de production. Order flow : riche mais coûteux et incompatible avec le harnais offline actuel. Calendrier : cheap mais filtre, pas edge. Surface IV historique : indisponible sans source payante.

**PRODUCTION MODIFIED :** NO. 1 sonde de recherche ajoutée sous `Tests/Research/NewDataSourceFeasibility/` ; classes `internal` de production réutilisées sans modification.

**TESTS :** `YahooCrossAssetProbeTests` — Passed (33/36 combinaisons ticker/intervalle servies). Skipped si réseau totalement indisponible. Aucune assertion de comportement de production ajoutée.

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-013_Feasibility_NewDataSource.md`.

**NEXT ACTION :** Décision de priorisation. Recommandé : ouvrir **QDE-014 — Pilote cross-asset**, conditionnement du rendement forward MES (H1, ~700 j) sur : VIX niveau, pente `^VIX3M/^VIX`, variation `^TNX`/`ZN=F`, variation `DX-Y.NYB`, écart MES↔`NQ=F`. Méthodologie QDE-012 (expectancy en R par tranche, IC 95 %, train/OOS, coût base). Aligner les séries RTH-only en forward-fill causal. Ne PAS engager l'axe order flow ni aucune source payante avant le résultat de ce pilote.
