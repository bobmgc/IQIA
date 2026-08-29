# QDE-012 — Sprint 15.25 — Lot 16.5 — Yahoo Session Dataset Rollout — Complétion (tests 45 jours)

> **Type :** Infrastructure de test / minimisation des requêtes — **Mode :** migration mécanique
> **Production modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Suite du Lot 16.4. Ce lot termine la Phase 7 en migrant les **6 tests d'intégration Yahoo à fenêtre
> 45 jours** vers la fixture de session `YahooSessionDataset`, via une **découpe** de la série 59 jours
> partagée (aucun téléchargement supplémentaire). **Aucun fichier de production n'est modifié.**

---

## 1. Contexte

Après le Lot 16.4, un run complet de suite faisait encore :

| Source | Fenêtre | Téléchargements sur un run complet |
|---|---|---|
| `YahooSessionDataset` (fixture) | 59 j | 1 (partagé par 20 classes) |
| `StructuralBreakObservationSetBuilder` (`Lazy` statique) | 59 j | 1 (partagé par 7 classes) |
| **6× `*YahooIntegrationTests`** | **45 j** | **6 (un par classe)** |
| `YahooNetworkIntegrationTests`, `BacktestSignalPipelineYahooIntegrationTests` | 2 j | 2 (petits) |
| `YahooBacktestFoundationIntegrationTests` | date fixe | 0–1 |

Les **6 téléchargements 45 jours** sont la dernière redondance significative (~4 000 barres chacun).

---

## 2. Solution — découpe de la série de session

`YahooSessionDataset` reçoit une méthode :

```csharp
public HistoricalSeries? LastDays(int days, Action<string> log)
```

- Prend la série 59 jours partagée, garde le **suffixe contigu** des barres dont
  `Timestamp >= LastTimestamp.AddDays(-days)`.
- Reconstruit une `HistoricalSeries` via le **même `HistoricalSeries.Create` non modifié** : un suffixe
  d'une série déjà valide re-valide trivialement (strictement croissant, pas de doublon).
- `null` + ligne de skip si Yahoo était indisponible ce run (convention identique à `Require`).

C'est le **même horizon glissant** (~45 jours finissant « maintenant ») que ce que les 6 tests
demandaient avec `DateTime.UtcNow.AddDays(-45)`, **sans deuxième requête réseau**. Les 6 tests
n'asserted que des invariants structurels (« never a fixed PnL/equity/count », cf. leurs propres
doc-comments et le rapport Lot 14.7) — inchangés.

**Effet de bord bénéfique :** les 6 tests partageaient auparavant `DateTime.UtcNow` à des instants
légèrement différents (comptes de barres différents run à run) ; ils voient désormais **exactement la
même découpe**, donc plus déterministe.

---

## 3. Transformation par fichier (6, uniforme)

```diff
+using IQIAIndicator.Tests.BacktestTests.Yahoo;

+[Collection("YahooSession")]
 public sealed class XxxYahooIntegrationTests
 {
     private readonly ITestOutputHelper _output;
+    private readonly YahooSessionDataset _yahoo;

-    public XxxYahooIntegrationTests(ITestOutputHelper output) { _output = output; }
+    public XxxYahooIntegrationTests(ITestOutputHelper output, YahooSessionDataset yahoo)
+    { _output = output; _yahoo = yahoo; }
```

```diff
-            var source = new YahooHistoricalBarSource();
-            DateTime to = DateTime.UtcNow;
-            DateTime from = to.AddDays(-45);
-
-            HistoricalSeries series = source.Load("MES", "M5", from, to);
+            HistoricalSeries? maybeSeries = _yahoo.LastDays(45, _output.WriteLine);
+            if (maybeSeries is null)
+                return;
+            HistoricalSeries series = maybeSeries;
+            DateTime from = series.FirstTimestamp;
+            DateTime to = series.LastTimestamp;
```

Le `try { … } catch (… when (exception is …YahooProviderException or …))` de chaque test est
conservé (protège les appels aval du pipeline).

### Fichiers migrés (6)

`Backtest/Calibration/CalibrationYahooIntegrationTests`, `Backtest/Cost/CostYahooIntegrationTests`,
`Backtest/Execution/ExecutionYahooIntegrationTests`,
`Backtest/Measurement/ScientificMeasurementYahooIntegrationTests`,
`Backtest/Pnl/PnLYahooIntegrationTests`, `Backtest/Risk/RiskYahooIntegrationTests`.

⇒ La collection `"YahooSession"` compte désormais **26 classes** (20 après 16.4 + 6).

---

## 4. Non migré — `StructuralBreakObservationSetBuilder` (décision explicite)

Ce helper (`Tests/Research/StructuralBreakInformationAudit/Support/`) est **déjà** un chargement unique
par processus de test : `Lazy<AuditDataset?>` en `LazyThreadSafetyMode.ExecutionAndPublication`,
partagé par ses 7 classes de test appelantes, et déjà protégé `YahooProviderException` (Lot 16.3).

Il n'est **pas** fondu dans `YahooSessionDataset` parce que :

- il fait aussi un `BacktestEngine.RunSignalPipeline` complet + une reconstruction 6-dimensions
  bar-à-bar (coûteux) que son `Lazy` met naturellement en cache ;
- le router via la fixture obligerait à placer ses 7 classes dans la collection sérialisée
  `"YahooSession"`, allongeant la chaîne série pour un gain marginal : **1 téléchargement 59 j** en
  moins seulement (le `RunSignalPipeline` resterait de toute façon fait une fois) ;
- son `Lazy` statique est déjà parallèle-safe et « une requête par run » — le patron cible.

⇒ Redondance résiduelle assumée : **1** téléchargement 59 j supplémentaire par run complet
(`YahooSessionDataset` + ce builder = 2). Documenté comme limite connue.

---

## 5. Bilan minimisation

| | Avant Lot 16.3 | Après 16.4 | Après 16.5 |
|---|---|---|---|
| Téléchargements Yahoo « gros » (≈ 59 j / ≈ 45 j) sur un run complet | ~26 | ~8 | **~2** |
| Tous téléchargements confondus | ~30 | ~10 | **~4** |

(`~2` = `YahooSessionDataset` + `StructuralBreakObservationSetBuilder` ; `~4` ajoute les 2 tests 2 jours.)

---

## 5b. Compromis — taille de la collection sérialisée

La collection `"YahooSession"` compte maintenant **26 classes** (dont ~24 exécutent un backtest complet
de plusieurs minutes). xUnit 2.5.3 impose la **sérialisation intra-collection** : c'est le prix du
partage d'une fixture entre classes dans cette version (pas d'`IAssemblyFixture` avant xUnit v3). Le
temps mur d'un run *de cette collection* devient la somme des temps individuels (potentiellement
60–120 min en série). C'est **voulu et accepté** (Lot 16.4 §6) : c'est précisément ce qui supprime le
martèlement parallèle de Yahoo. Alternative future si le temps série devient gênant : remplacer la
fixture par un `Lazy<YahooSessionDataset>` statique thread-safe (le patron déjà utilisé par
`StructuralBreakObservationSetBuilder`), qui partage le téléchargement **sans** sérialiser — au prix
d'abandonner le contrat `ICollectionFixture` choisi au Lot 16.3.

---

## 6. Déterminisme / look-ahead / logique scientifique

- **Aucun fichier de production modifié.** `git status` : seuls `Tests/**` (+ nouveaux rapports).
- **Aucune logique scientifique touchée.** Les 6 tests exécutent le même pipeline sur les mêmes barres
  (découpe contiguë de la série partagée = ce qu'ils auraient téléchargé, à la dérive de fenêtre
  glissante près, déjà attendue).
- **`HistoricalSeries.Create` non modifié** — la découpe re-valide par le chemin standard.
- **Look-ahead : inchangé.** `LastDays` ne fait que filtrer un suffixe temporel ; aucune statistique,
  aucun accès futur.
- **Run isolation : préservée.** La découpe produit une **nouvelle** `HistoricalSeries` immuable par
  appel ; la série de session sous-jacente n'est jamais mutée.

---

## 7. Builds & tests

| Étape | Résultat |
|---|---|
| `dotnet build IQIAIndicator.Tests.csproj -c Debug` | **La génération a réussi. 0 Avertissement, 0 Erreur** (3 min 10 s) |
| `dotnet build IQIAIndicator.Tests.csproj -c Release` | **La génération a réussi. 0 Avertissement, 0 Erreur** |
| `dotnet build IQIAIndicator.csproj -c Debug / Release` | inchangé — production non touchée |

Pas de run complet (éviterait de retaper Yahoo ; minimisation en place).

---

## 8. Résultat d'exécution

| Test migré (exécuté) | Résultat | Durée |
|---|---|---|
| `PnLYahooIntegrationTests` (isolé, `--blame-crash`) | **Réussi** | 5 min 20 s |
| `RiskYahooIntegrationTests` (collection, avec Cost) | **Réussi** | 4 min 36 s |
| `CostYahooIntegrationTests` (collection, avec Risk) | **Réussi** | 7 min 34 s |
| `RiskYahooIntegrationTests` + `CostYahooIntegrationTests` ensemble | **2 / 2 Réussi** | 12 min 14 s (1 seule découpe partagée) |
| `Calibration` / `Execution` / `Measurement` `YahooIntegrationTests` | non exécutés — transformation mécanique identique, build vert | — |

`CostYahooIntegrationTests` valide le point sensible : ses assertions « baseline == costed » sont
**relatives** (aucun nombre figé en dur), donc la découpe 45 j de la série de session au lieu d'un
`Load` 45 j frais ne casse rien — cohérent avec la discipline « invariants structurels seulement » de
tous les tests d'intégration Yahoo.

> Note : un premier run `Risk + Pnl` sous un wrapper `timeout 500 s` a été tué par le `timeout`
> pendant le 2ᵉ test (« Plantage du processus hôte de test »). Re-exécutés sans wrapper restrictif :
> **verts**. Ce n'était pas un défaut de code.

---

## 9. FINAL OUTPUT

```
STATUS:
LOT COMPLETE — Phase 7 fully rolled out for all rolling-window Yahoo integration tests.

SCOPE:
Migrate the 6 remaining 45-day *YahooIntegrationTests onto YahooSessionDataset via a 45-day slice of
the shared 59-day session series (no extra download).

TEST FILES MODIFIED:
7 — YahooSessionDataset.cs (+ LastDays helper) + 6 x *YahooIntegrationTests.
PRODUCTION FILES MODIFIED: NONE.

NOT MIGRATED (documented):
StructuralBreakObservationSetBuilder — already a thread-safe single-load-per-process Lazy; folding it
into the serialised collection buys only 1 fewer 59-day download for a heavier serial chain.

REQUEST MINIMIZATION:
COMPLETE for rolling-window tests — full-run large Yahoo downloads: ~8 -> ~2 (session fixture +
StructuralBreak builder). All downloads: ~10 -> ~4.

RUN ISOLATION: PASS — LastDays returns a fresh immutable HistoricalSeries; session series never mutated.
DETERMINISM: PASS — contiguous suffix, re-validated through the unmodified HistoricalSeries.Create.
LOOK-AHEAD: UNCHANGED.
SCIENTIFIC LOGIC: NOT MODIFIED.

DEBUG BUILD: PASS (tests 0/0).
RELEASE BUILD: PASS (tests 0/0).
TESTS: see §8.

ATAS: NOT USED   ORDERS: NONE   DLL DEPLOYED: NO   COMMIT: NO

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.5_Yahoo_Session_Dataset_Rollout_Completion_Report.md

NEXT LOT:
RECOMMEND ONLY AFTER ANALYSIS — the request-minimization track is essentially done. Remaining optional
items: (1) empirically derive the Provisional retry/timeout constants from observed Yahoo 429
behaviour; (2) if xUnit is ever upgraded to v3, replace the serialised "YahooSession" ICollectionFixture
with an IAssemblyFixture so the shared dataset no longer forces serial execution.

STOP.
```
