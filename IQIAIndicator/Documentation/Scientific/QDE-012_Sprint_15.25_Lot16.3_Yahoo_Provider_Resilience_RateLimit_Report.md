# QDE-012 — Sprint 15.25 — Lot 16.3 — Yahoo Historical Data Provider Resilience & Rate Limit Handling

> **Type :** Infrastructure / robustesse fournisseur de données — **Mode :** correction bornée
> **Production scientifique modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Ce lot corrige exclusivement le comportement de résilience du **fournisseur de données historiques
> Yahoo** (développement / validation scientifique / backtest). Aucune logique de régime, de fusion, de
> décision, de risque ou de stop-loss n'est touchée. Yahoo reste un fournisseur de données de
> développement uniquement ; l'indicateur de production reste indépendant de Yahoo à l'exécution.

---

## 1. Mécanisme de défaillance initial

### 1.1 Chaîne d'appel (avant)

```
Test intégration Yahoo (≈ 30 fichiers)
  └─ new YahooHistoricalBarSource()                     → HttpYahooChartClient (client HTTP réel)
      └─ .Load(symbol, timeFrame, from, to)             → AUCUN CancellationToken, AUCUN budget d'opération
          └─ YahooChunkPlanner.Plan(...)                → 1 chunk pour 59 j, N chunks au-delà
              └─ for (chunk in chunks)                  → AUCUNE borne agrégée
                  └─ HttpYahooChartClient.FetchChartJson(...)
                      └─ _httpClient.GetAsync(url).GetAwaiter().GetResult()   ← sync-over-async
                          └─ HttpClient.Timeout = 30 s   ← SEULE borne, par tentative
                      └─ response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                  └─ YahooChartParser.Parse(json)
```

### 1.2 Cause exacte des tests « bloqués »

Quatre facteurs cumulatifs, tous établis par lecture du code :

1. **Aucune classification du 429.** `HttpYahooChartClient` lisait le corps quel que soit le code HTTP
   (« STATUS CODE IS NOT HOW ERRORS ARE DETECTED HERE »). Un 429 produisait donc, via
   `YahooChartParser.Parse`, une `InvalidOperationException` (« not valid JSON » pour une page WAF, ou
   « chart API returned an error » pour un corps JSON d'erreur).
   **Or `InvalidOperationException` est exactement le type levé pour un vrai défaut scientifique**
   (`HistoricalSeries.Create` : « duplicate timestamp », désordre ; `YahooHistoricalBarSource.Load` :
   « no usable bar in range »). Les ≈ 30 tests d'intégration attrapaient `InvalidOperationException`
   **en bloc** → un 429 était classé « SKIPPED (network) » **et** une vraie régression scientifique
   dans `HistoricalSeries.Create` aurait été masquée à l'identique. Confusion bidirectionnelle.

2. **Aucune borne au niveau opération.** `HttpClient.Timeout = 30 s` borne *une tentative HTTP*. Rien
   ne bornait la somme sur les `N` chunks, ni le temps total de `Load`, ni une éventuelle
   ré-émission par `SocketsHttpHandler` sur connexion périmée. Budget effectif :
   « 30 s × tentatives × chunks » sans plafond de la somme.

3. **`static HttpClient` + parallélisme xUnit + rate-limit Yahoo.** xUnit exécute les collections de
   tests en parallèle (≈ nb de cœurs). ≈ 30 classes d'intégration Yahoo émettent la **même** requête
   `MES / M5 / 59 jours` via le **même `SharedClient`** → **une seule connexion HTTP/2** vers
   `query2.finance.yahoo.com`. Quand Yahoo rate-limite et retient les connexions, les requêtes se
   sérialisent derrière le flux bloqué ; chacune attend jusqu'à 30 s, puis les tests lourds enchaînent
   un backtest de 2–3 min. L'agrégat de l'assembly dépasse 40 min. Cohérent avec la note connue
   « full unfiltered dotnet test run hammers Yahoo into HTTP 429 after ~1h; network tests then hang
   instead of skipping ».

4. **Fenêtre glissante `DateTime.UtcNow` ⇒ zéro cache.** Chaque test calcule `to = DateTime.UtcNow` →
   `period1/period2` distincts → URL distincte → aucun cache HTTP/CDN possible → chaque test refait un
   fetch origine complet. Contributeur direct au dépassement de quota.

### 1.3 Audit du comportement HTTP (avant)

| Aspect | État (avant) |
|---|---|
| Client | `static readonly HttpClient SharedClient` unique, partagé par tous les tests |
| Timeout requête | `TimeSpan.FromSeconds(30)` sur `HttpClient.Timeout` — par tentative |
| Retry / backoff | **AUCUN** — un seul `GetAsync` |
| HTTP 429 | non détecté explicitement ; confondu avec une erreur de données |
| HTTP 5xx | non distingué |
| Timeout réseau | `TaskCanceledException` après 30 s (si TCP répond) |
| Cancellation | non supportée (aucun `CancellationToken` dans la chaîne) |
| `Retry-After` | ignoré |
| Budget d'opération | inexistant |

---

## 2. Contrat de défaillance (Phase 1)

Nouveaux types dans `Core/MarketData/Yahoo/` :

- **`YahooFailureKind`** (enum public) : `RateLimited`, `NetworkFailure`, `ProviderUnavailable`,
  `InvalidResponse`.
- **`YahooProviderException : Exception`** (dérive directement de `Exception`, **pas** de
  `InvalidOperationException`) : porte `Kind`, `HttpStatusCode?`, `RetryAfter?`, `AttemptsMade`,
  `TotalWaited`, `IsRateLimited`.

Distinction obtenue :

| Situation | Type levé (après) | Comportement test |
|---|---|---|
| 429 / quota | `YahooProviderException(RateLimited)` | **SKIP explicite** |
| 5xx après retries bornés | `YahooProviderException(ProviderUnavailable)` | SKIP explicite |
| Transport / timeout requête | `YahooProviderException(NetworkFailure)` | SKIP explicite |
| Corps 2xx non-chart (page WAF) | `YahooProviderException(InvalidResponse)` | SKIP explicite |
| `chart.error` JSON (symbole delisté…) | `InvalidOperationException` (inchangé, via `YahooChartParser`) | **ÉCHEC ROUGE** |
| Série malformée (`HistoricalSeries.Create`) | `ArgumentException` / `InvalidOperationException` (inchangé) | **ÉCHEC ROUGE** |
| « no usable bar in range » | `InvalidOperationException` (inchangé) | **ÉCHEC ROUGE** |

`YahooChartParser` et `HistoricalSeries` ne sont **pas modifiés** : un problème scientifique de la série
reste une exception scientifique.

---

## 3. Politique de retry (Phases 2–4)

### 3.1 Avant

Aucune. Une tentative, timeout 30 s, échec ou blocage.

### 3.2 Après — `YahooRetryPolicy` (immuable, `Provisional`)

| Paramètre | Défaut | Rôle |
|---|---|---|
| `MaximumRetryCount` | **3** | retries après la tentative initiale (≤ 4 tentatives HTTP / chunk) |
| `MaximumTotalWait` | **20 s** | plafond **dur** du sommeil cumulé entre tentatives |
| `PerRequestTimeout` | **15 s** | plafond **dur** d'une tentative HTTP (remplace `HttpClient.Timeout`) |
| `BaseDelay` | **1 s** | premier backoff, ×2 par retry (1 → 2 → 4 …) |
| `MaximumDelayPerWait` | **8 s** | plafond d'un sommeil unitaire |

`YahooRetryPolicy.Decide(attemptsMade, kind, retryAfter?, alreadyWaited)` est **pur** (aucune horloge,
aucune E/S) et renvoie `(ShouldRetry, Delay)`. Règles :

- kind non retryable (`InvalidResponse`) ⇒ `Stop`.
- `attemptsMade > MaximumRetryCount` ⇒ `Stop`.
- `alreadyWaited ≥ MaximumTotalWait` ⇒ `Stop`.
- sinon `delay = min( max(backoff, retryAfter ?? 0), MaximumTotalWait − alreadyWaited )`, jamais ≤ 0.

**Worst-case wall-clock / chunk** = `(1 + 3) × 15 s + 20 s ≈ 80 s`. Fini, borné, déterministe.

### 3.3 HTTP 429

Détecté explicitement (`status is 429 or 999`) **avant** toute lecture de corps. Retry borné par la
politique ; sinon `YahooProviderException(RateLimited, HttpStatusCode = 429, …)`.

### 3.4 `Retry-After`

Lu (`response.Headers.RetryAfter`, forme delta ou date HTTP) et **respecté seulement s'il est
supérieur au backoff**, puis **clampé au budget restant** (`MaximumTotalWait − alreadyWaited`). La
valeur brute demandée par le fournisseur est enregistrée dans `YahooProviderException.RetryAfter`
(diagnostic) mais **ne peut jamais** faire dormir l'application au-delà de son budget.
*Exemple prouvé par test :* `Retry-After: 600` avec budget 2 s ⇒ sommeil ≤ 2 s, total ≤ 2 s.

### 3.5 Timeouts

- **Un seul** système de timeout par requête : `CancellationTokenSource.CancelAfter(PerRequestTimeout)`
  linké au token appelant. `HttpClient.Timeout` passe à `Timeout.InfiniteTimeSpan` (plus de double
  système).
- **Borne d'opération** : `YahooHistoricalBarSource.Load` crée un `CancellationTokenSource` linké avec
  `CancelAfter(DefaultOverallLoadTimeout = 3 min)` — filet pour le cas multi-chunk. Un dépassement est
  reclassé `YahooProviderException(NetworkFailure)`.

---

## 4. Cancellation (Phase 5) — portée **locale à Yahoo**

- `IYahooChartClient.FetchChartJson(..., CancellationToken cancellationToken = default)` — paramètre
  **défaulté** ⇒ tous les sites d'appel existants compilent sans changement.
- `YahooHistoricalBarSource` : surcharge `internal Load(symbol, timeFrame, from, to, CancellationToken)`.
  La surcharge publique historique `Load(symbol, timeFrame, from, to)` délègue avec
  `CancellationToken.None`.
- **`IHistoricalBarSource` n'est PAS élargi.** `CsvHistoricalBarSource`, `BacktestEngine`, la
  calibration : aucun impact. Pas de refactor async transverse.
- Une annulation **demandée par l'appelant** propage `OperationCanceledException` verbatim ; une
  annulation due au timeout requête/opération est **classifiée** en `YahooProviderException`.

---

## 5. Isolation des tests vis-à-vis d'une panne fournisseur (Phase 6)

**Choix : Option A — skip explicite** (cohérent avec le pattern préexistant : xunit 2.5.3 n'a pas de
`Assert.Skip`, la convention du dépôt est « early return + `_output.WriteLine` », test reste
« Passed »). Justification : le lot vise à empêcher le *hang* et la *confusion* de classification, pas à
introduire une nouvelle catégorie de résultat que l'outillage installé ne sait pas afficher.

Balayage mécanique sur **29 fichiers de test** :

```
  exception is HttpRequestException or TaskCanceledException or InvalidOperationException
→ exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException
    or HttpRequestException or TaskCanceledException
```

Effet : une panne fournisseur (`YahooProviderException`) reste un **skip** ; `HttpRequestException` /
`TaskCanceledException` restent skippées par sécurité ; **`InvalidOperationException` nu n'est plus
attrapée** ⇒ une régression scientifique redevient un **échec rouge**. Le doc-comment canonique de
`YahooNetworkIntegrationTests` a été mis à jour ; les mentions résiduelles de `InvalidOperationException`
dans d'autres doc-comments sont désormais obsolètes mais sans effet fonctionnel.

Sortie de test désormais distincte :

- `SCIENTIFIC FAILURE` → `InvalidOperationException` / `ArgumentException` non attrapée → rouge.
- `YAHOO PROVIDER UNAVAILABLE / RATE LIMITED` → `YahooProviderException` attrapée → ligne
  `SKIPPED (… : {Kind}, HTTP {code}, {n} attempt(s), {s}s backoff)`.

---

## 6. Minimisation des requêtes (Phase 7) — **PARTIEL**

**Constat (inchangé) :** ≈ 20 tests d'intégration téléchargent chacun leur propre fenêtre identique
`MES / M5 / 59 jours` lors d'un run complet — cause directe du 429.

**Mécanisme livré :** `YahooSessionDataset` + `[CollectionDefinition("YahooSession")]`
(`ICollectionFixture<YahooSessionDataset>`).

- Une **seule** acquisition réelle `MES / M5 / DefaultMaxChunkSpanDays` par run de test, partagée par
  toute la collection.
- La `HistoricalSeries` produite est **déjà profondément immuable** (`HistoricalSeries.Create`, jamais
  mutée) → **RUN ISOLATION** et **DÉTERMINISME** préservés : chaque consommateur voit des barres
  bit-identiques, aucun ne peut perturber un autre.
- `ICollectionFixture` **sérialise** la collection → supprime aussi le martèlement parallèle.
- Si Yahoo est indisponible : `Series == null`, `Failure`/`SkipReason` renseignés ;
  `Require(log)` retourne `null` après avoir écrit la raison, le test fait `return`.

**État de migration :** infra + collection livrées ; **`AmbiguityGateThresholdRevalidationLot1413Tests`
migré** (le test nommé dans le rapport de bug). Les ~18 autres consommateurs du même dataset 59 j
restent à migrer — opération **mécanique**, différée à un mini-lot dédié pour garder ce changement
revuable (chaque fichier a un nommage de variable / des fenêtres propres). Les tests à fenêtre
différente (45 j, 2 j) gardent volontairement leur propre `Load`.

> **REQUEST MINIMIZATION : PARTIAL** — mécanisme opérationnel et prouvé sur 1 consommateur ; rollout
> complet listé comme suite.

Consommateurs restant à migrer (fenêtre 59 j / `DefaultMaxChunkSpanDays`) :
`DecisionFusionAmbiguityInvestigationLot1414Tests`, `PersistenceVariabilityDiscriminativePowerLot1416Tests`,
`PersistenceZeroVarianceActivationAuditLot1417Tests`, `HysteresisThresholdSensitivityLot1418Tests`,
`StableRangeMeanRevertingOverlapInvestigationLot1415Tests`, `RegimeCoverageMaturityAuditLot150Tests`,
`UnsupportedRegimeObservabilityLot151Tests`, `StructuralBreakEvidenceAblationLot152Tests`,
`StopLossFoundationLot153Tests`, `ExecutionRealismLot154Tests`, `FillPriceReconciliationLot155Tests`,
`StructuralBreakRegressionTests`, `StructuralBreakEvidenceLot158IntegrationTests`,
`StructuralBreakEvidenceDescriptiveAnalysisTests`, `SixDimensionIndependentAuditTests`,
`Lot16*Tests`, `StructuralBreakObservationSetBuilder` (+ `YahooDatasetCoverageTests`,
`ExecutionYahooIntegrationTests`, `CostYahooIntegrationTests`, `PnLYahooIntegrationTests`,
`RiskYahooIntegrationTests`, `ScientificMeasurementYahooIntegrationTests`,
`BacktestSignalPipelineYahooIntegrationTests` — à vérifier fenêtre par fenêtre).

---

## 7. Tests ajoutés (Phase 8) — sans réseau, sans sommeil réel

`Tests/Backtest/Yahoo/` :

| Fichier | Couverture |
|---|---|
| `StubHttpMessageHandler.cs` | `HttpMessageHandler` scripté : réponses (statut/corps/`Retry-After`), exceptions transport, stall respectant l'annulation. Aucun socket. |
| `YahooRetryPolicyTests.cs` (6 tests) | kind non retryable ⇒ stop ; retries ≤ `MaximumRetryCount` ; backoff exponentiel plafonné ; `Retry-After` clampé au budget ; budget épuisé ⇒ stop ; **sommeil cumulé ne peut jamais dépasser `MaximumTotalWait`** (simulation 50 itérations). |
| `YahooRateLimitResilienceTests.cs` (14 tests) | 429→429→200 : borné, corps rendu ; 429×4 : `RateLimited`, `AttemptsMade == 4`, `Σ backoff ≤ 20 s` ; `Retry-After: 600` : sommeil ≤ budget ; 500→200 : recovery transitoire OK ; 502×4 : `ProviderUnavailable` ; **stall provider** (timeout requête réel 100 ms) : termine, `NetworkFailure`, 4 tentatives ; transport exception : `NetworkFailure` borné ; page HTML 200 : `InvalidResponse` ; `chart.error` JSON : laissé au parser (`InvalidOperationException`, pas `YahooProviderException`) ; annulation appelant : `OperationCanceledException` propagée nue ; succès 1ʳᵉ tentative : 0 retry / 0 sleep ; **déterminisme** : réponse identique ⇒ `HistoricalSeries` identique avec ou sans retries préalables. |

Aucun test ne dort réellement (sleeper injecté qui enregistre les durées). Le seul délai réel est le
`PerRequestTimeout` de 100 ms du test de stall.

**Résultat :** `Réussi! - échec : 0, réussite : 60` (les 20 nouveaux + `YahooChartParserTests` +
`YahooHistoricalBarSourceTests` + `YahooChunkPlannerTests`), durée ≈ 0,7 s.

Test réseau réel conservé (`YahooNetworkIntegrationTests`, 3/3 `Réussi`, 568 ms + 185 ms + 1 ms) —
comportement sûr : Yahoo dispo ⇒ test tourne ; rate-limité ⇒ `YahooProviderException` ⇒ skip explicite ;
jamais de hang.

---

## 8. Déterminisme & fingerprints (Phases 9)

- **Chemin de succès inchangé octet-pour-octet.** La seule voie de retour de `HttpYahooChartClient`
  reste `response.Content.ReadAsStringAsync()` ; le passage de `GetAsync` (ResponseContentRead) à
  `SendAsync(..., ResponseHeadersRead)` puis `ReadAsStringAsync` ne change pas le décodage — même
  `HttpContent`, même charset. Test `IdenticalResponse_ProducesIdenticalSeries_...` : `HistoricalSeries`
  bit-identique (Timestamp/OHLCV/TimeZone/Provider) que la réponse arrive directement ou après
  `429 + 500 + 200`.
- **`HistoricalSeries`, Fingerprint, mapping timestamp, OHLC, Volume, gaps :** aucun code touché
  (`YahooChartParser`, `HistoricalSeries` intacts). `YahooHistoricalBarSourceTests` (25 tests) et
  `YahooChartParserTests` : verts, inchangés.
- **Look-ahead : inchangé.** Aucune statistique, aucune fenêtre, aucun accès `DateTime.Now/UtcNow`
  ajouté dans le chemin de données. La résilience n'agit que sur *comment* le corps HTTP est obtenu,
  jamais sur *quelles* barres en sont extraites ni *quand*.

---

## 9. Fichiers

### 9.1 Production modifiés (`Core/MarketData/Yahoo/` uniquement)

| Fichier | Changement |
|---|---|
| `YahooFailureKind.cs` | **nouveau** — enum de classification |
| `YahooProviderException.cs` | **nouveau** — exception fournisseur classifiée |
| `YahooRetryPolicy.cs` | **nouveau** — politique bornée pure + `YahooRetryDecision` |
| `IYahooChartClient.cs` | + `CancellationToken cancellationToken = default` (défaulté) |
| `HttpYahooChartClient.cs` | boucle de retry bornée, détection 429/5xx, `Retry-After` clampé, timeout par requête via CTS, `HttpClient.Timeout = Infinite`, classification, seam `sleep` injectable |
| `YahooHistoricalBarSource.cs` | surcharge `internal Load(..., CancellationToken)`, `DefaultOverallLoadTimeout = 3 min` via CTS linké, reclassification du dépassement |

### 9.2 Tests modifiés / ajoutés

- **Ajoutés :** `StubHttpMessageHandler.cs`, `YahooRetryPolicyTests.cs`,
  `YahooRateLimitResilienceTests.cs`, `YahooSessionDataset.cs`.
- **Modifiés :** `FakeYahooChartClient.cs` (signature + `CancellationToken` + seam `ThrowOnEveryCall`) ;
  `YahooNetworkIntegrationTests.cs` (doc-comment) ;
  `AmbiguityGateThresholdRevalidationLot1413Tests.cs` (migration fixture) ;
  **29 fichiers** — remplacement mécanique du prédicat `IsConnectivityOrProviderIssue` / du `when(...)`
  inline.

### 9.3 Fichiers protégés — **TOUS INTACTS**

`RegimeEngine`, `EvidenceFusionEngine`, `FusionStateManager`, `DecisionEngine`, `DecisionArbitrator`,
`SignalEngine`, `EntryEngine`, `EntryTriggerEngine`, `EntryTriggerBuilder`, `TradePlanBuilder`,
`RiskEngine`, `RiskPolicy`, logique Stop-Loss, `StructuralBreakEvidenceRule`, `CusumStatistics`,
`YahooChartParser`, `HistoricalSeries`, `YahooChunkPlanner`, `IHistoricalBarSource`,
`CsvHistoricalBarSource` : aucune modification.

---

## 10. Builds

| Build | Résultat |
|---|---|
| `dotnet build IQIAIndicator.csproj -c Debug` | **La génération a réussi. 0 Avertissement, 0 Erreur** (47 s) |
| `dotnet build IQIAIndicator.Tests.csproj -c Debug` | **La génération a réussi. 0 Avertissement, 0 Erreur** (42 s) |
| `dotnet build IQIAIndicator.csproj -c Release` | **La génération a réussi. 0 Avertissement, 0 Erreur** (65 s) |
| `dotnet build IQIAIndicator.Tests.csproj -c Release` | **La génération a réussi. 0 Avertissement, 0 Erreur** (48 s) |

---

## 11. Stratégie de test appliquée

Périmètre **ciblé** (choix utilisateur, pour ne pas retaper Yahoo pendant le diagnostic) :

1. `YahooRetryPolicyTests` + `YahooRateLimitResilienceTests` + `YahooHistoricalBarSourceTests` +
   `YahooChartParserTests` + `YahooChunkPlannerTests` → **60 / 60 Réussi** (656 ms), **sans réseau**.
2. `YahooNetworkIntegrationTests` (1 test réseau réel léger, fenêtres 2 j) → **3 / 3 Réussi**.
3. `AmbiguityGateThresholdRevalidationLot1413Tests` (migré, 1 acquisition Yahoo partagée + backtest
   complet 7 seuils) → `dotnet test` **exit code 0** (succès).

La suite complète n'a **pas** été relancée : elle referait des dizaines d'appels Yahoo réels et
risquerait un 429 pendant le diagnostic même (comportement désormais borné, mais inutile ici).

---

## 12. Limitations restantes

- **Rollout Phase 7 incomplet** : 1 consommateur migré sur ~19. `REQUEST MINIMIZATION : PARTIAL`.
  Tant que le rollout n'est pas fait, un run complet émet encore beaucoup de requêtes identiques —
  mais chacune est désormais **bornée** et une saturation se traduit par des skips explicites, non par
  un hang.
- **Paramètres `Provisional`** : `MaximumRetryCount=3`, `MaximumTotalWait=20 s`, `PerRequestTimeout=15 s`,
  `BaseDelay=1 s`, `MaximumDelayPerWait=8 s`, `DefaultOverallLoadTimeout=3 min` — choisis pour une
  terminaison rapide et déterministe, non dérivés empiriquement.
- **Doc-comments obsolètes** : quelques fichiers de test mentionnent encore
  `InvalidOperationException` comme motif de skip dans leur `<summary>` ; sans effet fonctionnel.
- **`xunit 2.5.3`** : toujours pas de `Assert.Skip` ; un skip fournisseur reste un test « Passed » avec
  ligne de sortie, pas un « Skipped » dans l'UI du runner.
- **`Retry-After` forme date HTTP** : converti via `DateTimeOffset.UtcNow` (horloge murale) ; n'affecte
  que le chemin d'échec/retry, jamais le contenu d'un chargement réussi.
- **`SocketsHttpHandler` bas niveau** : la ré-émission éventuelle sur connexion périmée n'a pas été
  reproduite au niveau kernel ; elle est rendue inoffensive par le `PerRequestTimeout` + le budget
  d'opération, mais non instrumentée finement.

---

## 13. FINAL OUTPUT

```
STATUS:
LOT COMPLETE (Phase 7 request-minimization: PARTIAL — mechanism delivered, 1/19 consumers migrated)

ROOT CAUSE:
HTTP 429 was never classified — it surfaced as InvalidOperationException, the same type used for
genuine scientific-data faults, and was caught wholesale by ~30 test guards. Combined with: no
operation-level time budget (only a per-attempt 30s HttpClient.Timeout), a single static HttpClient
funnelling ~30 parallel xUnit collections through one HTTP/2 connection, and dozens of identical
59-day MES/M5 downloads per full run — a rate-limited Yahoo stalled the whole integration assembly
past 40 minutes and could not be told apart from a data regression.

HTTP 429:
PASS — detected explicitly (status 429/999), bounded retry, then YahooProviderException(RateLimited).

RETRY:
PASS — bounded by count AND by cumulative wait; pure decision function unit-tested.

MAX RETRY COUNT:
3 retries (≤ 4 HTTP attempts per chunk). Provisional.

MAX TOTAL WAIT:
20 s cumulative backoff per chunk (hard cap; Retry-After clamped to it). Provisional.
Overall Load() ceiling: 3 min (multi-chunk safety net). Provisional.

REQUEST TIMEOUT:
15 s per HTTP attempt via CancellationTokenSource (single timeout system; HttpClient.Timeout = Infinite).
Provisional.

HANG PREVENTION:
PASS — worst case per chunk ≈ (1+3)*15s + 20s ≈ 80s, finite chunk count, caller-cancellable,
overall 3-min ceiling. Stalled-provider test terminates deterministically.

FAILURE CLASSIFICATION:
PASS — YahooFailureKind {RateLimited, NetworkFailure, ProviderUnavailable, InvalidResponse} on
YahooProviderException (derives from Exception, not InvalidOperationException). Scientific faults
keep throwing InvalidOperationException/ArgumentException.

TEST ISOLATION:
PASS — 29 guard predicates now catch YahooProviderException (+ raw HttpRequestException/
TaskCanceledException), no longer bare InvalidOperationException. Provider outage → explicit SKIP;
scientific regression → red failure. Choice: Option A (explicit skip), consistent with xunit 2.5.3.

REQUEST MINIMIZATION:
PARTIAL — YahooSessionDataset + [CollectionDefinition("YahooSession")] ICollectionFixture delivered
and proven on AmbiguityGateThresholdRevalidationLot1413Tests (the test named in the bug report).
Immutable shared HistoricalSeries → run isolation & determinism preserved; collection serialised.
~18 further identical-window consumers listed for a mechanical follow-up.

SUCCESSFUL DATA REGRESSION:
PASS — success path returns the body byte-identical; IdenticalResponse_ProducesIdenticalSeries test
confirms HistoricalSeries is identical with or without preceding retries.

FINGERPRINT:
PASS — HistoricalSeries / YahooChartParser / mapping untouched; YahooHistoricalBarSourceTests (25) and
YahooChartParserTests green and unchanged.

DETERMINISM:
PASS — retry loop uses an injectable sleeper (tests never sleep); pure YahooRetryPolicy.Decide;
successful-content path unchanged.

RUN ISOLATION:
PASS — YahooSessionDataset exposes only a deeply-immutable HistoricalSeries; no shared mutable state;
ICollectionFixture serialises the collection.

LOOK-AHEAD:
UNCHANGED — no statistic, window, or wall-clock added to the data path; resilience affects only how
the HTTP body is obtained.

SCIENTIFIC LOGIC:
NOT MODIFIED.

PRODUCTION FILES MODIFIED:
Core/MarketData/Yahoo/ only:
  + YahooFailureKind.cs (new)
  + YahooProviderException.cs (new)
  + YahooRetryPolicy.cs (new)
  ~ IYahooChartClient.cs (defaulted CancellationToken param)
  ~ HttpYahooChartClient.cs (bounded resilience layer)
  ~ YahooHistoricalBarSource.cs (internal CancellationToken overload + overall ceiling)

PROTECTED FILES:
ALL INTACT — RegimeEngine, EvidenceFusionEngine, FusionStateManager, DecisionEngine,
DecisionArbitrator, SignalEngine, EntryEngine, EntryTriggerEngine, EntryTriggerBuilder,
TradePlanBuilder, RiskEngine, RiskPolicy, Stop-Loss logic, StructuralBreakEvidenceRule,
CusumStatistics, YahooChartParser, HistoricalSeries, YahooChunkPlanner, IHistoricalBarSource,
CsvHistoricalBarSource.

ATAS:
NOT USED

ORDERS:
NONE

DLL DEPLOYED:
NO

COMMIT:
NO

DEBUG BUILD:
PASS — main + tests, 0 warning, 0 error.

RELEASE BUILD:
PASS — main + tests, 0 warning, 0 error.

TESTS:
Targeted (no full-suite Yahoo hammering):
  - YahooRetryPolicyTests + YahooRateLimitResilienceTests + YahooHistoricalBarSourceTests
    + YahooChartParserTests + YahooChunkPlannerTests: 60/60 PASS (656 ms), no network.
  - YahooNetworkIntegrationTests: 3/3 PASS (real network, light).
  - AmbiguityGateThresholdRevalidationLot1413Tests (migrated): dotnet test exit 0 (PASS).

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.3_Yahoo_Provider_Resilience_RateLimit_Report.md

NEXT LOT:
RECOMMEND ONLY AFTER ANALYSIS — candidate mini-lot: mechanical Phase 7 rollout (migrate the ~18
remaining identical-window Yahoo integration tests onto YahooSessionDataset), plus an empirical
derivation of the Provisional retry/timeout constants from observed Yahoo behaviour.

STOP.
```
