# QDE-012 — Sprint 15.25 — Lot 14.2
# Yahoo Historical Data Source

**Type** : LOT D'IMPLÉMENTATION
**Portée** : première source de données historiques externe du Backtest Engine (Yahoo Finance)
**Date** : 2026-08-22
**Branche** : `feature/structural-stability-v2`

---

## 1. OBJECTIVE

Implémenter `YahooHistoricalBarSource : IHistoricalBarSource`, la première source de données historiques externe du Backtest Engine IQIA, conformément à l'architecture cible :

```
Yahoo → YahooHistoricalBarSource → HistoricalSeries → BacktestEngine → MarketContext → pipeline IQIA futur
```

Yahoo reste strictement une **source de données historiques**. Aucune classe du moteur scientifique, du Risk Engine ou d'ATAS ne connaît Yahoo — vérifié explicitement en §17.

---

## 2. ARCHITECTURE

```
Core/MarketData/Yahoo/
  IYahooChartClient.cs        ← seam HTTP (interface interne, injectable pour les tests)
  HttpYahooChartClient.cs     ← implémentation réelle (HttpClient, sans NuGet)
  YahooChartResponse.cs       ← DTOs JSON (System.Text.Json, sans NuGet)
  YahooChartParser.cs         ← JSON → HistoricalBar, pur, sans réseau
  YahooTimeFrameMap.cs        ← "M5" → "5m", explicite, fermé
  YahooSymbolMap.cs           ← "ES"/"MES" → tickers Yahoo vérifiés, explicite, fermé
  YahooChunkPlanner.cs        ← découpage [from,to) en sous-fenêtres ≤ 59 jours, pur
  YahooHistoricalBarSource.cs ← orchestrateur implémentant IHistoricalBarSource

Backtest/
  HistoricalSeriesFingerprint.cs  ← fingerprint générique d'une HistoricalSeries (réutilise BacktestFingerprint.Sha256Hex)
```

```
YahooHistoricalBarSource.Load(symbol, timeFrame, from, to)
  │
  ├─ YahooSymbolMap.Resolve(symbol)         → ticker Yahoo (throw si non vérifié)
  ├─ YahooTimeFrameMap.ResolveYahooInterval(timeFrame) → intervalle Yahoo (throw si non supporté)
  ├─ YahooChunkPlanner.Plan(from, to, 59j)  → liste de sous-fenêtres
  │
  └─ POUR CHAQUE chunk :
       ├─ IYahooChartClient.FetchChartJson(ticker, interval, chunkFrom, chunkTo)
       ├─ YahooChartParser.Parse(json)      → bars + gapCount
       └─ accumulation
  │
  └─ HistoricalSeries.Create(symbol, timeFrame, "UTC", "Yahoo(Continuous)", allBars)
       (validation strictement inchangée du LOT 14.1 — seul juge final)
```

Aucune dépendance NuGet ajoutée : `System.Net.Http.HttpClient` et `System.Text.Json` font partie du SDK .NET 10 déjà référencé par le projet, qui n'avait auparavant **aucune** dépendance NuGet dans `IQIAIndicator.csproj` (vérifié par inspection avant toute implémentation — seules des `Reference HintPath` locales vers ATAS existaient).

---

## 3. YAHOO API / SOURCE METHOD

### 3.1 Endpoint

```
GET https://query2.finance.yahoo.com/v8/finance/chart/{ticker}?period1={unix}&period2={unix}&interval={interval}
```

Non documenté officiellement (aucune clé API, aucun contrat publié), mais **vérifié empiriquement par appels HTTP réels le 2026-08-22** — pas déduit de documentation tierce.

### 3.2 Découvertes empiriques déterminantes

| Test réel effectué | Résultat observé | Conséquence sur le code |
|---|---|---|
| `curl` avec User-Agent générique vers `query1.finance.yahoo.com` | **HTTP 429** ("Too Many Requests"), corps non-JSON | `query2` retenu ; User-Agent de navigateur réel obligatoire |
| Même requête avec User-Agent navigateur vers `query2` (AAPL puis MES=F) | **HTTP 200**, JSON valide, aucun cookie/crumb nécessaire | Une seule requête directe suffit, pas de handshake en 2 temps |
| `MES=F` en 5m | `meta.instrumentType="FUTURE"`, `exchangeName="CME"` | Confirme le ticker réel (§8 ci-dessous) |
| `ES=F` en 5m | `meta.instrumentType="FUTURE"`, `exchangeName="CME"` | Confirme le ticker réel |
| Fenêtre 5m > 60 jours dans le passé | **HTTP 422**, `chart.error={"code":"Unprocessable Entity","description":"5m data not available for startTime=... The requested range must be within the last 60 days."}` | Limite de profondeur confirmée littéralement (§9) |
| Fenêtre à cheval sur la limite de 60 jours | **HTTP 422** identique — **aucune donnée partielle retournée** | Le chunking ne peut pas compter sur un retour partiel de Yahoo |
| Symbole inconnu | **HTTP 404**, `chart.error={"code":"Not Found","description":"No data found, symbol may be delisted"}` | Message Yahoo repris verbatim dans l'exception |
| Période sans trading (samedi, MES=F) | **HTTP 200**, mais `result[0]` **sans clé `timestamp`**, `indicators.quote=[{}]` | Traité comme "aucune barre", jamais comme une erreur |
| Fenêtre normale récente (5m) | 287 créneaux sur 1 jour, **84 créneaux avec OHLC entièrement `null`** (Volume=0), coïncidant avec la coupure de maintenance quotidienne CME | Traité comme des "gaps" à omettre, jamais interpolés (§7) |
| `adjclose` pour MES=F/ES=F en 5m | **Absent de la réponse** | Confirme qu'il n'y a rien à ignorer explicitement pour ces deux symboles à cette granularité (§7) |

Ces trois réponses distinctes (succès avec barres / succès sans barre / erreur structurée) sont modélisées explicitement dans `YahooChartResponse.cs` et testées une à une dans `YahooChartParserTests.cs`.

### 3.3 Détection des erreurs — pas par code HTTP

`HttpYahooChartClient` ne lit **jamais** le status code HTTP pour décider du succès/échec : les réponses 422 et 404 observées ci-dessus contiennent un corps JSON parfaitement formé avec `chart.error` renseigné. Utiliser `EnsureSuccessStatusCode()`/`GetStringAsync()` aurait jeté ce corps informatif. Le client lit donc systématiquement le corps (`GetAsync` + `ReadAsStringAsync`), quel que soit le code, et laisse `YahooChartParser` — seul interprète de `chart.error` — décider. Seul un échec de transport réel (DNS, timeout, connexion refusée) ou un corps non-JSON (ex. une page de blocage WAF en texte brut, observée lors du premier essai avec un User-Agent générique) remonte comme exception depuis cette couche.

---

## 4. YAHOO → HISTORICALBAR MAPPING

| Champ Yahoo | Champ `HistoricalBar` | Règle |
|---|---|---|
| `timestamp[i]` (secondes Unix, UTC par définition) | `Timestamp` | `DateTimeOffset.FromUnixTimeSeconds(...).UtcDateTime` — conversion pure, déterministe, **jamais** `DateTime.Now`/`UtcNow` |
| `quote.open[i]` | `Open` | verbatim |
| `quote.high[i]` | `High` | verbatim |
| `quote.low[i]` | `Low` | verbatim |
| `quote.close[i]` | `Close` | **jamais** `adjclose` — voir §7 |
| `quote.volume[i]` | `Volume` | verbatim, jamais transformé, jamais divisé/multiplié, jamais converti en Delta |
| *(absent de Yahoo)* | `BidVolume` | **toujours `null`** |
| *(absent de Yahoo)* | `AskVolume` | **toujours `null`** |
| *(absent de Yahoo)* | `Delta` | **toujours `null`** — jamais `Delta = Volume` ou toute approximation |
| *(absent de Yahoo)* | `OpenInterest` | **toujours `null`** |

### Gaps

Si `open[i]`/`high[i]`/`low[i]`/`close[i]` **ou** `volume[i]` est `null` à un index donné, ce créneau est **omis** du résultat — jamais zéro-rempli, jamais interpolé, jamais reporté depuis la barre précédente. C'est le signal que Yahoo lui-même émet pour dire "aucune barre formée à ce créneau" (constaté empiriquement : coupure de maintenance CME, §3.2). Le nombre de créneaux omis est compté dans `YahooChartParser.ParseResult.GapCount`, jamais perdu silencieusement.

**Décision non triviale documentée explicitement dans le code** : un `Volume` `null` alors que l'OHLC est présent (jamais observé empiriquement, mais représentable dans le schéma JSON) est traité **identiquement** à un gap OHLC — jamais complété par `0`, qui serait une valeur fabriquée indiscernable d'un volume réellement nul.

---

## 5. TIMEFRAME SUPPORT

**Un seul timeframe supporté dans ce lot : `"M5"` → `"5m"`.**

`YahooTimeFrameMap` est une table fermée (`Dictionary<string,string>`), pas un branchement. Toute autre valeur (`"M1"`, `"M15"`, `"H1"`, `"D1"`, `""`, une casse différente comme `"m5"`) lève `NotSupportedException` **avant tout appel réseau** — vérifié par test (`Load_UnsupportedTimeFrame_ThrowsBeforeEverCallingTheClient`, assertion `fake.Calls` vide). Aucun repli silencieux vers un autre intervalle.

---

## 6. TIMEZONE HANDLING

Chaque `Timestamp` produit par ce lot est normalisé en **UTC** (`DateTimeKind.Utc`) directement depuis le compteur de secondes Unix de Yahoo — une conversion purement mathématique, sans dépendance à `DateTime.Now`/`DateTime.UtcNow` ni au fuseau de la machine hôte. `HistoricalSeries.TimeZone` vaut systématiquement la chaîne littérale `"UTC"`.

Les champs `meta.timezone` ("EDT", abrégé, dépendant de l'heure d'été) et `meta.exchangeTimezoneName` ("America/New_York", nom IANA) sont capturés dans les DTOs mais **jamais utilisés** pour une conversion — documenté explicitement dans `YahooChartResponse.cs` comme informationnel uniquement dans ce lot.

**Écart à noter pour un futur lot** : c'est la **première** source de ce dépôt à produire des timestamps UTC sans ambiguïté. Le chemin ATAS live (`MarketContextBuilder`) reste `DateTimeKind.Unspecified`, passthrough pur (rapport LOT 13 §3.2b, non modifié par ce lot). Toute comparaison future entre une série Yahoo et une capture ATAS devra tenir compte de cette différence de convention avant de comparer des timestamps directement.

---

## 7. MISSING DATA HANDLING

| Cas | Comportement |
|---|---|
| Timestamp manquant | Impossible par construction : chaque `timestamp[i]` correspond à un index dans les tableaux OHLCV ; l'absence totale de la clé `timestamp` signifie "aucune barre" (§3.2), traité comme période vide |
| OHLC `null` | Créneau omis (gap), compté, jamais fabriqué — §4 |
| Volume `null` | Traité identiquement à un gap OHLC — §4 |
| Timestamp dupliqué | Ne peut survenir qu'à la frontière de deux chunks (Yahoo lui-même ne produit jamais deux fois le même timestamp Unix au sein d'une seule réponse) — voir §10 pour la défense en profondeur |
| Timestamp non croissant | Rejeté par `HistoricalSeries.Create`, **inchangé depuis le LOT 14.1** — cette source ne réimplémente aucune validation existante |
| OHLC invalide (High<Low, prix ≤ 0, volume < 0) | Rejeté par `HistoricalBar.Validate()`/`HistoricalSeries.Create`, inchangé |
| Période vide | `YahooChartParser` retourne une liste vide sans erreur (§3.2) ; si **aucun** chunk ne produit de barre, `YahooHistoricalBarSource.Load` lève `InvalidOperationException` explicite plutôt que de retourner une `HistoricalSeries` vide (qui de toute façon serait rejetée par `HistoricalSeries.Create`, "at least one bar is required") |

**Aucune interpolation, aucun forward-fill, aucune bougie artificielle nulle part dans ce lot.**

---

## 8. FUTURES SYMBOL LIMITATIONS

**YAHOO FUTURES SYMBOL SUPPORT = LIMITED.**

Seuls deux symboles sont résolus, tous deux **vérifiés empiriquement le 2026-08-22** par appel HTTP réel et confirmation du `meta` retourné (`instrumentType="FUTURE"`, `exchangeName="CME"`) :

| Symbole IQIA | Ticker Yahoo | Vérification |
|---|---|---|
| `ES` | `ES=F` | `meta.symbol="ES=F"`, `instrumentType="FUTURE"`, `exchangeName="CME"`, `fullExchangeName="CME"` |
| `MES` | `MES=F` | `meta.symbol="MES=F"`, `instrumentType="FUTURE"`, `exchangeName="CME"`, `shortName="MICRO E-MINI S&P 500 INDEX FUTU"` |

`YahooSymbolMap` est une table fermée. Aucun autre symbole n'est deviné par un motif ("SYMBOL=F") — tout symbole absent de la table lève `NotSupportedException` avant tout appel réseau (même garantie que pour le timeframe, testée explicitement).

Le mapping est **entièrement confiné** à `Core/MarketData/Yahoo/` : ni `RiskEngine`, ni `DecisionArbitrator`, ni `EntryTriggerBuilder`, ni `TradePlanBuilder`, ni aucun adaptateur ATAS ne contiennent de branchement sur `"ES"`/`"MES"` — vérifié par grep et par la simple absence de toute référence à `Core.MarketData.Yahoo` en dehors de ce dossier et des tests.

---

## 9. CONTINUOUS FUTURES LIMITATIONS

`ES=F` et `MES=F` sont les tickers **continus** (front-month) de Yahoo — Yahoo lui-même les recompose au fil des roulements de contrat. Ce ne sont **jamais** des contrats individuels expirants (ex. `ESZ25`).

Pour que cette réserve **voyage avec la donnée** et ne dépende pas de la mémoire d'un futur lecteur du code, chaque `HistoricalSeries` produite par `YahooHistoricalBarSource` porte `Provider = "Yahoo(Continuous)"` — visible directement par tout consommateur qui inspecte `series.Provider`, sans avoir à relire ce rapport ou la documentation de classe.

**Aucune conclusion scientifique définitive sur ES/MES ne doit être tirée de données Yahoo sans validation de qualité indépendante** (roulements de contrat, écarts de continuité, absence de tout traitement de roll dans ce lot — le LOT 13 avait déjà identifié RISK-14 : "absence de notion de roll de contrat" dans le dépôt, toujours vrai après ce lot). Pour le développement technique du Backtest Engine, une série Yahoo est un intrant légitime ; pour une conclusion de calibration, elle ne l'est pas en l'état.

---

## 10. CACHE

**Aucun cache implémenté dans ce lot.** Le brief le rend explicitement optionnel ("créer éventuellement un cache local uniquement si nécessaire"), et rien dans ce lot n'en démontre le besoin — `YahooHistoricalBarSource` est appelé une fois par scénario de backtest, pas dans une boucle chaude. Ajouter un cache maintenant aurait été de la complexité non demandée. À réévaluer si un futur lot (calibration multi-runs, LOT 14.9) sollicite Yahoo de façon répétée sur la même fenêtre.

---

## 11. FINGERPRINTING

### 11.1 Décision de placement

Le brief demande de réutiliser `BacktestFingerprint` du LOT 14.1 « si possible ». Investigation :

- Les formateurs (`D`, `Dt`) de `BacktestFingerprint` sont `private`, scopés à la fingerprint de scénario/barre/évidence — pas à une `HistoricalSeries` nue.
- `Core.MarketData` (où vit `HistoricalSeries`) ne doit **pas** dépendre de `IQIAIndicator.Backtest` : Core est la couche basse, Backtest en dépend déjà, inverser cette relation pour un seul formateur aurait été le mauvais compromis architectural.

**Décision** : `HistoricalSeriesFingerprint` (nouveau, public) vit dans `Backtest/` — qui dépend déjà légitimement de `Core.MarketData` — et réutilise **réellement** `BacktestFingerprint.Sha256Hex` (la seule primitive publique et agnostique du provider de cette classe), sans qu'aucune ligne de `BacktestFingerprint.cs` n'ait été modifiée (§27, fichier protégé du LOT 14.1 confirmé intact).

### 11.2 Propriétés garanties

Dépend uniquement de : `Symbol`, `TimeFrame`, `TimeZone`, `Provider`, nombre de barres, puis pour chaque barre `Timestamp`/`OHLCV`/`BidVolume`/`AskVolume`/`Delta`/`OpenInterest`. **Jamais** de l'heure de téléchargement, de la machine, de l'utilisateur ou d'une URL — vérifié par construction (aucun de ces éléments n'existe dans la signature `Compute(HistoricalSeries series)`) et testé (`SameSeriesContent_ProducesTheSameFingerprint`, `ChangedBarValue_ProducesADifferentFingerprint`, `DifferentProvider_ProducesADifferentFingerprint_SameOhlcv`).

---

## 12. UNIT TESTS

**9 fichiers, 900 lignes, 66 tests, tous réseau-indépendants sauf les 3 explicitement marqués INTEGRATION/NETWORK (§13).**

| Fichier | Couverture | Items du brief §19 |
|---|---|---|
| `YahooChartParserTests.cs` | Réponse valide, plusieurs barres, timestamp, OHLC, volume, gap OHLC, gap volume, période vide, erreurs (2 formes réelles), corps non-JSON, enveloppe manquante, tableau result vide, tableaux quote plus courts que timestamp | #1–12 |
| `YahooHistoricalBarSourceTests.cs` | Mapping symbole/timeframe → client, Bid/Ask/Delta/OpenInterest toujours null, TimeZone="UTC", Provider="Yahoo(Continuous)", validation HistoricalSeries réutilisée telle quelle (doublon via chunks → rejet), chunking (comptage, frontières +1s), gaps comptés et réinitialisés entre appels, aucune barre → exception, validation constructeur | #13–17, #21–26 |
| `YahooTimeFrameMapTests.cs` | M5 accepté ; M1/M15/H1/D1/vide/casse différente rejetés | #18–20 |
| `YahooSymbolMapTests.cs` | ES/MES résolus ; tout autre symbole rejeté | (mapping symbole, complète §14) |
| `YahooChunkPlannerTests.cs` | Un seul chunk sous la limite, exactement à la limite, plusieurs chunks au-delà, bornes dos-à-dos, couverture totale, ordre chronologique, plage inversée, `maxSpanDays` non positif | (support de #10) |
| `HistoricalSeriesFingerprintTests.cs` | Même contenu → même empreinte ; contenu modifié → empreinte différente ; longueur/format SHA-256 | #27–28 |
| `FakeYahooChartClient.cs` | Utilitaire de test (non un fichier de tests) — retourne du JSON préconfiguré, enregistre chaque appel | — |
| `YahooBacktestFoundationIntegrationTests.cs` | Yahoo→HistoricalSeries→BacktestEngine→BacktestFoundationResult, déterminisme, fingerprint calculable, **parité stricte** avec une série construite à la main à valeurs identiques | §21/§22 |
| `YahooNetworkIntegrationTests.cs` | Réseau réel, optionnel — §13 |

Résultat de la suite ciblée `IQIAIndicator.Tests.BacktestTests.Yahoo` : **66/66 PASS** (incluant les 3 tests réseau réels, Internet disponible au moment de l'exécution — voir §13).

---

## 13. NETWORK TESTS

Trois tests dans `YahooNetworkIntegrationTests.cs`, nommage et documentation de classe marqués **INTEGRATION / NETWORK** explicitement :

- `Integration_Network_LoadRecentMesFiveMinuteBars_FromLiveYahooEndpoint`
- `Integration_Network_LoadRecentEsFiveMinuteBars_FromLiveYahooEndpoint`
- `Integration_Network_UnverifiedSymbol_StillThrowsBeforeAnyNetworkCall` (sans appel réseau — le rejet du symbole survient avant)

Chaque test avec appel réseau demande une fenêtre courte (2 jours de 5m, quelques centaines de barres au plus — jamais une année de données) et est enveloppé dans un `try/catch` capturant `HttpRequestException`/`TaskCanceledException`/`InvalidOperationException` (ce dernier étant le type que `YahooChartParser` utilise pour toute erreur Yahoo, y compris un 429 transitoire) : en cas d'échec de connectivité, le test se termine tôt et rapporte via `ITestOutputHelper`, **sans jamais faire échouer la suite**.

**Limitation assumée et documentée dans le code** : la version d'xUnit installée (2.5.3) ne propose ni `Assert.Skip` ni saut dynamique — un `[Fact(Skip=...)]` exige une constante de compilation. Un échec de connectivité ne peut donc pas apparaître comme "Ignoré" dans l'exécuteur de tests, seulement comme un retour anticipé d'un test qui reste "Réussi", avec la raison écrite dans la sortie du test. Compromis délibéré, documenté explicitement dans la doc de classe, pas un oubli.

**Résultat réel de cette exécution** (Internet disponible) :
```
Integration_Network_LoadRecentMesFiveMinuteBars_FromLiveYahooEndpoint : PASS
  OK: loaded 409 bars, gaps=97, chunks=1.
Integration_Network_LoadRecentEsFiveMinuteBars_FromLiveYahooEndpoint : PASS
Integration_Network_UnverifiedSymbol_StillThrowsBeforeAnyNetworkCall : PASS
```
Confirmation en conditions réelles, au-delà des fixtures : 409 barres réelles chargées pour MES sur une fenêtre de 2 jours, 97 créneaux de gap correctement détectés et omis (cohérent avec les coupures de maintenance quotidiennes CME observées lors de l'investigation §3.2).

---

## 14. BACKTEST FOUNDATION INTEGRATION

`YahooBacktestFoundationIntegrationTests.cs` (fixture-based, déterministe — §5) prouve, sans dépendre du réseau :

1. **`BarsProcessed > 0`** : 10/10 barres d'une fixture traitées par `BacktestEngine.Run`.
2. **Timestamps valides** : `FirstTimestamp`/`LastTimestamp` du résultat correspondent exactement à ceux de la série chargée.
3. **Déterminisme** : deux chargements Yahoo indépendants de la même fixture, deux exécutions de `BacktestEngine`, résultats (`BacktestFoundationResult`, y compris `DeterministicHash`) strictement identiques.
4. **Fingerprint présent** : `HistoricalSeriesFingerprint.Compute` produit une empreinte hexadécimale de 64 caractères sur la série chargée.
5. **Aucun accès ATAS** : structurellement garanti — aucun fichier de `Core/MarketData/Yahoo/` ni de `Backtest/HistoricalSeriesFingerprint.cs` ne référence `ATAS`/`OFT` (vérifié par grep, zéro résultat).
6. **Parité stricte (brief §22)** : une série construite à la main avec des valeurs de barres **identiques** à celles issues du parsing Yahoo, exécutée à travers le **même** `BacktestEngine` non modifié, produit un `MarketContext`/`EvidenceSet` **champ par champ identique**, barre par barre — la seule différence entre les deux séries (`Provider`) n'a aucune influence sur le comportement du moteur ou de `MarketContextFactory`. C'est la preuve exécutable, pas seulement déclarative, que `YahooHistoricalBarSource` n'introduit aucun cas particulier en aval.

Conformément au brief §21, **RiskEngine/Decision/Entry/TradePlan ne sont exécutés nulle part** dans cette suite — `BacktestEngine` (LOT 14.1) ne les appelle toujours pas, et ce lot ne change pas cela.

---

## 15. KNOWN LIMITATIONS

1. **Un seul timeframe (M5), deux symboles (ES/MES).** Toute extension nécessite une entrée explicitement vérifiée dans `YahooTimeFrameMap`/`YahooSymbolMap` — jamais une extrapolation de motif.
2. **Chunking non testé en conditions réseau réelles au-delà de 60 jours** dans cette session (le test réseau utilise une fenêtre de 2 jours, un seul chunk). La logique de découpage elle-même est intégralement testée hors-ligne (`YahooChunkPlannerTests`, 8 tests) et la garde anti-doublon (`+1 seconde` sur les chunks suivants + rejet `HistoricalSeries.Create` en filet de sécurité) est testée via un client factice simulant un chevauchement. Une validation avec une vraie fenêtre >60 jours contre l'API réelle n'a pas été effectuée dans ce lot (aurait nécessité une fenêtre de plusieurs mois, hors du principe "requêtes courtes" du brief §20).
3. **Aucun cache** — voir §10, décision délibérée, pas un manque.
4. **Pas de gestion de roll de contrat** — hérité du LOT 13 (RISK-14), non traité ici, cohérent avec le périmètre "source historique uniquement" de ce lot.
5. **`LastRequestGapCount`/`LastRequestChunkCount` sont un état mutable d'instance**, pas un résultat retourné par `Load` — mêmes conventions que `ScientificDatasetCollector` (compteurs observables, jamais influents), mais cela signifie qu'un appelant multi-thread partageant une même instance de `YahooHistoricalBarSource` verrait ces compteurs se chevaucher. Aucun scénario multi-thread n'existe aujourd'hui dans le Backtest Engine (LOT 14.1 est explicitement mono-run), donc sans conséquence actuelle — à documenter si un futur lot introduit du parallélisme.
6. **`meta.exchangeTimezoneName`/`meta.timezone` sont capturés mais jamais exploités** — voir §6. Si une future source doit un jour restituer une heure locale de séance, ces champs existent déjà dans `YahooChartResponse` et n'auraient pas besoin d'un nouvel appel réseau, mais aucune logique ne les consomme dans ce lot.
7. **La détection d'erreur de connectivité réseau dans `YahooNetworkIntegrationTests` capture `InvalidOperationException` de façon large** (§13) — un vrai bug de mapping qui lèverait accidentellement ce même type ne serait pas distingué d'une indisponibilité réseau dans CE fichier spécifique. Les tests déterministes (`YahooChartParserTests`, `YahooHistoricalBarSourceTests`) ne partagent pas cette faiblesse : ils n'attrapent aucune exception et échoueraient normalement sur un vrai bug.

---

## 16. DATA QUALITY WARNINGS

- **Séries continues, pas des contrats individuels** — §9, `Provider = "Yahoo(Continuous)"` sur chaque série.
- **Gaps fréquents et substantiels à 5 minutes** : jusqu'à 84 créneaux sur 287 (~29 %) dans la fenêtre observée, concentrés sur la coupure de maintenance quotidienne CME — un utilisateur du Backtest Engine doit s'attendre à des séries avec des trous documentés (comptés via `GapCount`/`LastRequestGapCount`), pas des séries à cadence parfaitement régulière.
- **Limite de profondeur ferme à 60 jours pour le 5m**, confirmée par le message d'erreur littéral de Yahoo — toute demande plus ancienne échoue avec un message explicite, jamais silencieusement tronquée.
- **`TimeZone="UTC"` pour Yahoo diverge du chemin ATAS (`Unspecified`)** — §6, à réconcilier avant toute comparaison croisée future.
- **Aucune validation de qualité de type `RealMarketQualityAnalyzer` n'est appliquée aux données Yahoo dans ce lot** — cet analyseur (Tests/Research/StopLossCalibration/RealMarket/) reste spécifique aux captures ATAS ; l'appliquer à Yahoo est un travail de lot futur, non fait ici.

---

## 17. FILES CREATED

### Production (9 fichiers, 619 lignes)

```
IQIAIndicator/Core/MarketData/Yahoo/IYahooChartClient.cs
IQIAIndicator/Core/MarketData/Yahoo/HttpYahooChartClient.cs
IQIAIndicator/Core/MarketData/Yahoo/YahooChartResponse.cs
IQIAIndicator/Core/MarketData/Yahoo/YahooChartParser.cs
IQIAIndicator/Core/MarketData/Yahoo/YahooTimeFrameMap.cs
IQIAIndicator/Core/MarketData/Yahoo/YahooSymbolMap.cs
IQIAIndicator/Core/MarketData/Yahoo/YahooChunkPlanner.cs
IQIAIndicator/Core/MarketData/Yahoo/YahooHistoricalBarSource.cs
IQIAIndicator/Backtest/HistoricalSeriesFingerprint.cs
```

### Tests (9 fichiers, 900 lignes, 66 tests)

```
IQIAIndicator/Tests/Backtest/Yahoo/YahooChartParserTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/YahooTimeFrameMapTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/YahooSymbolMapTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/YahooChunkPlannerTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/FakeYahooChartClient.cs
IQIAIndicator/Tests/Backtest/Yahoo/YahooHistoricalBarSourceTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/HistoricalSeriesFingerprintTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/YahooBacktestFoundationIntegrationTests.cs
IQIAIndicator/Tests/Backtest/Yahoo/YahooNetworkIntegrationTests.cs
```

### Documentation

```
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot14.2_Yahoo_Historical_Data_Source_Report.md
```

---

## 18. FILES MODIFIED

**Aucun.** `git status --porcelain` confirme que ce lot n'a modifié aucun fichier préexistant — uniquement des ajouts. Les modifications visibles sur `IQIAIndicator/Core/MarketContextBuilder.cs` datent du LOT 14.1 (extraction vers `MarketContextFactory`, déjà rapportée dans le rapport de ce lot) et n'ont pas été retouchées ici.

---

## 19. BUILD

| Cible | Résultat | Avertissements | Erreurs |
|---|---|---|---|
| `IQIAIndicator.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.csproj` — Release | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Release | **SUCCÈS** | 0 | 0 |

Deux catégories d'erreurs rencontrées et corrigées pendant l'implémentation (documentées par transparence) :

1. **`using` manquant** pour `IQIAIndicator.Core.MarketData.Yahoo` dans deux fichiers de tests d'intégration — corrigé.
2. **`Assert.True(Regex.IsMatch(...))`** signalé par l'analyseur xUnit (règle xUnit2008) — remplacé par `Assert.Matches`.

Aucune dépendance NuGet ajoutée (§3.1).

---

## 20. TEST RESULTS

### Suite ciblée Yahoo (`IQIAIndicator.Tests.BacktestTests.Yahoo`)

```
Nombre total de tests : 66
     Réussi(s) : 66
 Durée totale : 4,91 secondes
```

Inclut les 3 tests réseau réels (§13), exécutés avec succès contre l'API Yahoo réelle au moment de ce lot.

### Suite complète du dépôt

```
Nombre total de tests : 402
     Réussi(s) : 401
    Ignoré(s) : 1
 Durée totale : 8,8850 Minutes
[exited with code 0]
```

402 = 336 (baseline LOT 14.1) + 66 (nouveaux tests Yahoo de ce lot). **Aucun échec.** Le seul test ignoré est le même que celui déjà documenté au LOT 14.1 (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`, retiré depuis le Sprint 15.22, sans rapport avec ce lot). **Aucune régression.**

---

## 21. NEXT LOT

**LOT 14.3 — Full IQIA Signal Pipeline** (Regime → Fusion → Decision → Entry → EntryTrigger → TradePlan), conformément à l'ordre déjà fixé (LOT 13 §18, LOT 14.1 rapport §28).

---

## STATUS
**IMPLEMENTED**

## FILES CREATED
9 fichiers de production (619 lignes), 9 fichiers de tests (900 lignes, 66 tests), 1 document.

## FILES MODIFIED
Aucun.

## YAHOO SOURCE
**PASS** — `YahooHistoricalBarSource` implémente `IHistoricalBarSource`, isolé dans `Core/MarketData/Yahoo/`, zéro dépendance depuis le pipeline scientifique.

## OHLCV MAPPING
**PASS** — mapping vérifié empiriquement, gaps détectés et omis, jamais fabriqués.

## TIMEFRAME
M5 uniquement, mapping fermé, rejet explicite pour tout autre timeframe.

## TIMEZONE
UTC, dérivé mathématiquement du epoch Unix Yahoo — jamais `DateTime.Now`/`UtcNow`.

## FUTURES SUPPORT
ES/MES vérifiés empiriquement (2026-08-22) ; continus (`Provider="Yahoo(Continuous)"`), jamais présentés comme contrats individuels ; aucun autre symbole deviné.

## FINGERPRINT
**PASS** — `HistoricalSeriesFingerprint`, réutilise `BacktestFingerprint.Sha256Hex` sans modifier ce fichier.

## BACKTEST FOUNDATION
**PASS** — intégration fixture déterministe + confirmation réseau réelle (409 barres MES chargées).

## LOOK-AHEAD
**PASS** — aucune connaissance de barres futures dans la source ; parité stricte prouvée avec une série construite à la main (§14 point 6).

## DEBUG BUILD
0 erreur, 0 avertissement (les deux projets).

## RELEASE BUILD
0 erreur, 0 avertissement (les deux projets).

## TESTS
66/66 PASS sur la suite ciblée Yahoo. Suite complète du dépôt : 402 tests, 401 réussis, 1 ignoré (préexistant, sans rapport), **0 échec** (§20).

## PROTECTED FILES
**ALL INTACT** — vérifié individuellement ci-dessous.

| Fichier | État |
|---|---|
| `DecisionArbitrator.cs` | INTACT |
| `EntryTriggerBuilder.cs` | INTACT |
| `TradePlanBuilder.cs` | INTACT |
| `RiskEngine.cs` | INTACT |
| `RiskPolicy.cs` | INTACT |
| `InstrumentRiskSpecification.cs` | INTACT |
| `RiskEngineRequest.cs` | INTACT |
| `RiskAssessment.cs` | INTACT |
| `ScientificDatasetRecord.cs` | INTACT |
| `MarketContextFactory.cs` | INTACT |
| `BacktestEngine.cs` | INTACT |
| `BacktestScenario.cs` | INTACT |
| `BacktestWindow.cs` | INTACT |
| `RegimeEngine`/`EvidenceFusionEngine`/`FusionStateManager`/`DecisionEngine`/`SignalEngine`/`EntryEngine`/`EntryTriggerEngine` | INTACT |
| `AmbiguityGateThreshold = 0.95` | INTACT (fichier `EntryTriggerBuilder.cs` non touché) |

`git status --porcelain` confirme qu'aucun de ces fichiers n'apparaît dans les modifications de ce lot — seuls des fichiers nouveaux ont été ajoutés (§17).

## RISK ENGINE
**NOT MODIFIED**

## ATAS
**NOT USED**

## ORDERS
**NONE**

## DLL DEPLOYED
**NO**

## COMMIT
**NO**

## NEXT LOT
**LOT 14.3 — FULL IQIA SIGNAL PIPELINE**
