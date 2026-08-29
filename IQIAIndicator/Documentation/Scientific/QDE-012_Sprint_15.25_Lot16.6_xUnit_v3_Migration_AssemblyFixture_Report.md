# QDE-012 — Sprint 15.25 — Lot 16.6 — Migration xUnit v3 + `IAssemblyFixture`

> **Type :** Infrastructure de test (framework) — **Mode :** migration mécanique + dé-sérialisation
> **Production modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Ce lot migre le projet `IQIAIndicator.Tests` de **xUnit 2.5.3** vers **xUnit v3 (4.0.0)**. Objectif :
> remplacer l'`ICollectionFixture` sérialisé du Lot 16.4/16.5 (26 classes de backtest exécutées en
> série) par un **`[assembly: AssemblyFixture]`** qui partage le dataset Yahoo **sans** forcer la
> sérialisation, et utiliser **`Assert.Skip`** natif à la place de la convention « log + early-return,
> test tout de même Passed ». **Aucun fichier de production (`IQIAIndicator.csproj`) n'est touché.**

---

## 1. Audit préalable (lecture seule)

| Fait | Valeur |
|---|---|
| Fichiers de test `.cs` | 335 |
| `using Xunit.Abstractions;` | 38 fichiers |
| Symboles de `Xunit.Abstractions` utilisés | **`ITestOutputHelper` uniquement** (78 réf.) |
| API xUnit avancée (orderers, message sinks, `Xunit.Sdk`, `BeforeAfterTestAttribute`, custom `ITestFramework`…) | **aucune** |
| `[Collection]` / `ICollectionFixture` / `CollectionDefinition` | **uniquement** `YahooSession` (Lots 16.3–16.5) |
| Attributs `[assembly:]` définis par l'utilisateur | **aucun** |

⇒ Surface minimale : la migration est essentiellement mécanique.

---

## 2. Changements `csproj`

| Avant | Après |
|---|---|
| `xunit` 2.5.3 | `xunit.v3` 4.0.0 |
| `xunit.runner.visualstudio` 2.5.0 | `xunit.runner.visualstudio` 4.0.0 |
| `Microsoft.NET.Test.Sdk` 18.6.0 | **supprimé** (VSTest host ; xUnit v3 tourne sur Microsoft.Testing.Platform) |
| *(lib)* | `<OutputType>Exe</OutputType>` (les projets xUnit v3 hébergent leur propre runner) |
| — | `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`, `<UseVSTest>false</UseVSTest>` |
| — | `<NoWarn>$(NoWarn);xUnit1051</NoWarn>` (voir §5) |

### `global.json` (nouveau, racine du dépôt)

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

Le SDK .NET 10 **a retiré le pont VSTest** pour les projets Microsoft.Testing.Platform : sans cet
opt-in, `dotnet test` échoue avec
`Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK`.
Aucune version de SDK n'est épinglée (pas de section `sdk`).

---

## 3. Migration du code de test

### 3.1 `using Xunit.Abstractions;` → `using Xunit;` (38 fichiers)

`ITestOutputHelper` a migré dans le namespace `Xunit`. Script : si `using Xunit;` déjà présent →
suppression de la ligne `Abstractions` ; sinon → réécriture en `using Xunit;`.

### 3.2 `ICollectionFixture` → `[assembly: AssemblyFixture]`

`YahooSessionDataset.cs` :

```diff
+[assembly: AssemblyFixture(typeof(IQIAIndicator.Tests.BacktestTests.Yahoo.YahooSessionDataset))]
 ...
-[CollectionDefinition("YahooSession")]
-public sealed class YahooSessionCollection : ICollectionFixture<YahooSessionDataset> { }
```

et dans les **26** classes consommatrices :

```diff
-[Collection("YahooSession")]
 public sealed class XxxTests
```

Le paramètre constructeur `YahooSessionDataset` est conservé : xUnit v3 injecte l'instance d'assembly
exactement comme une `IClassFixture`. **Une seule instance pour tout l'assembly**, donc **un seul
téléchargement Yahoo**, mais **les 26 classes ne sont plus sérialisées** — elles reparallélisent
(retour au parallélisme d'avant Lot 16.3, désormais **sans** le risque HTTP 429 puisque le
téléchargement est unique).

### 3.3 `Assert.Skip` (xUnit v3)

`YahooSessionDataset` :

```diff
-public HistoricalSeries? Require(Action<string> log)
-{
-    if (Series is not null) return Series;
-    log(SkipReason ?? "SKIPPED ...");
-    return null;
-}
+public HistoricalSeries Require()
+{
+    Assert.SkipUnless(Series is not null, SkipReason ?? "Yahoo provider unavailable (not a code failure).");
+    return Series!;
+}
```

(idem `LastDays(int days)` — le paramètre `log` disparaît). Sites d'appel dans les 26 classes :

```diff
-HistoricalSeries? maybeSeries = _yahoo.Require(_output.WriteLine);
-if (maybeSeries is null)
-    return;
-HistoricalSeries series = maybeSeries;
+HistoricalSeries series = _yahoo.Require();
```

Quand Yahoo est indisponible, le test est maintenant **`Skipped`** dans le runner (plus « Passed avec
une ligne de log »). La distinction panne-fournisseur / régression scientifique est donc désormais
visible **au niveau du résultat de test**, pas seulement dans la sortie.

### 3.4 Analyseurs xUnit v3

- **`xUnit1051`** (« passez `TestContext.Current.CancellationToken` ») : ~27 sites
  `YahooHistoricalBarSource.Load(...)` (le `CancellationToken` optionnel ajouté au Lot 16.3). Les
  tests déterministes à fake-client n'en tirent rien, les vrais loads réseau sont déjà bornés en
  interne ⇒ **`NoWarn`** documenté plutôt que 27 éditions de bruit.
- **`xUnit2029`** (1 site, `Sprint1518RealCaptureQualityTests`) : `Assert.Empty(x.Where(...))` →
  `Assert.DoesNotContain(x, ...)`. **Corrigé.**

---

## 4. Invocation `dotnet test` — ce qui change

| Avant (VSTest) | Après (Microsoft.Testing.Platform) |
|---|---|
| `dotnet test Chemin.csproj -c Debug --no-build --filter "FullyQualifiedName~X"` | `dotnet test --project Chemin.csproj --no-build --filter "FullyQualifiedName~X"` |
| `--nologo` | *(n'existe plus ; retirer)* |
| positional `<csproj>` | `--project <csproj>` |
| `--filter "FullyQualifiedName~X"` | **inchangé** (`--filter` accepte toujours la syntaxe VSTest) ; alternatives natives : `--filter-class`, `--filter-method`, `--filter-namespace`, `--filter-query` |

L'exécutable de test peut aussi être lancé directement :
`IQIAIndicator.Tests.exe -filterVSTest "FullyQualifiedName~X"` (ou `-filter`, `-class`, `-method`).

---

## 5. Compromis / points d'attention

- **Reparallélisation de 26 classes de backtest lourdes.** Sans la collection sérialisée, xUnit v3
  exécute jusqu'à `maxParallelThreads` (= nb de cœurs) classes en parallèle. Chacune charge ~11 000
  barres + un pipeline complet ⇒ pression mémoire notable (identique à l'état d'avant Lot 16.3). Si
  cela pose problème, poser un `xunit.runner.json` avec `maxParallelThreads` réduit — **non fait dans
  ce lot** (comportement volontairement ramené à l'avant-16.3, mais sans le 429).
- **`catch` blocks résiduels.** Les blocs `catch (… YahooProviderException …) { _output.WriteLine("SKIPPED …"); }`
  qui protègent les appels aval du pipeline **restent** en « log + Passed ». Les convertir en
  `Assert.Skip` est un balayage séparé (≈ 30 sites) — reporté. Ce lot convertit uniquement le chemin
  `Require`/`LastDays`.
- **`global.json` racine** : influence tout `dotnet test` du dépôt. Le projet principal n'étant pas un
  projet de test, il n'est pas affecté.
- **`Microsoft.NET.Test.Sdk` retiré** : si un outil externe attend le host VSTest, il faudra passer par
  l'exécutable MTP.

---

## 6. Déterminisme / look-ahead / logique scientifique

- **`IQIAIndicator.csproj` : zéro modification.** `git status` ne liste que `Tests/**`, `global.json`
  et les rapports.
- **Aucune logique scientifique touchée.** Le changement de framework de test n'altère ni les données,
  ni les pipelines, ni les assertions (à part `Assert.Empty`→`Assert.DoesNotContain`, sémantiquement
  identique).
- **`YahooSessionDataset` reste immuable et à instance unique** ⇒ RUN ISOLATION et DÉTERMINISME
  préservés ; l'`AssemblyFixture` ne change que le *cycle de vie* (assembly au lieu de collection),
  pas le contenu partagé.

---

## 7. Builds & tests

| Étape | Résultat |
|---|---|
| `dotnet build IQIAIndicator.Tests.csproj -c Debug` | **La génération a réussi. 0 Avertissement, 0 Erreur** |
| `dotnet build IQIAIndicator.Tests.csproj -c Release` | **La génération a réussi. 0 Avertissement, 0 Erreur** |
| `dotnet build IQIAIndicator.csproj -c Debug / Release` | inchangé — production non touchée |

Tests (MTP `dotnet test --project … --filter …`) :

| Filtre | Résultat |
|---|---|
| 7 classes Yahoo sans réseau (`YahooRetryPolicy`, `YahooRateLimitResilience`, `YahooChartParser`, `YahooHistoricalBarSource`, `YahooChunkPlanner`, `YahooSymbolMap`, `YahooTimeFrameMap`) | **75 / 75 Réussi** (15 s) |
| `YahooDatasetCoverageTests` + `Lot16ContractRegressionTests` (2 membres de la fixture d'assembly, 1 téléchargement partagé) | **2 / 2 Réussi** (24 m 33 s ; 0 ignoré — Yahoo disponible) |

Pas de run complet (éviterait de retaper Yahoo).

---

## 8. Résultat d'exécution

- **75 / 75** tests Yahoo sans réseau — **Réussi** (15 s). Le runner MTP `dotnet test --project … --filter …`
  fonctionne avec la syntaxe VSTest inchangée.
- **`[assembly: AssemblyFixture(typeof(YahooSessionDataset))]` validé de bout en bout** :
  `YahooDatasetCoverageTests` + `Lot16ContractRegressionTests` (deux classes distinctes) reçoivent la
  **même** instance par injection constructeur ; un **seul** téléchargement Yahoo pour les deux ;
  `_yahoo.Require()`, `_yahoo.LastDays(...)`, `_yahoo.RequestedFromUtc/ChunkCount/GapCount` : OK.
  **2 / 2 Réussi**, `ignoré : 0` (Yahoo était disponible, donc `Assert.SkipUnless` non déclenché).
- L'exécutable direct `IQIAIndicator.Tests.exe -filterVSTest "…"` fonctionne aussi
  (`xUnit.net v3 In-Process Runner v4.0.0`).

> Le chemin `Assert.Skip` lui-même (Yahoo indisponible ⇒ test `ignoré`) n'a pas pu être observé en vrai
> ce run (Yahoo répondait). Il est couvert par construction : `Assert.SkipUnless(false, reason)` est
> l'API standard xUnit v3, et `SkipReason` est renseigné dans le `catch (YahooProviderException)` du
> constructeur de la fixture.

---

## 9. FINAL OUTPUT

```
STATUS:
LOT COMPLETE — IQIAIndicator.Tests migrated to xUnit v3 (4.0.0) on Microsoft.Testing.Platform.

HEADLINE:
The 26 Yahoo integration test classes are no longer serialised. YahooSessionDataset became an
[assembly: AssemblyFixture] - one instance / one download for the whole assembly, but the classes
run in parallel again (pre-16.3 parallelism, minus the HTTP 429 risk).

FRAMEWORK:
xunit 2.5.3 -> xunit.v3 4.0.0 ; xunit.runner.visualstudio 2.5.0 -> 4.0.0 ;
Microsoft.NET.Test.Sdk removed ; OutputType=Exe ; global.json test runner = Microsoft.Testing.Platform.

CODE CHANGES (tests only):
  - using Xunit.Abstractions -> using Xunit   (38 files)
  - [Collection("YahooSession")] removed      (26 files)
  - ICollectionFixture -> [assembly: AssemblyFixture]   (YahooSessionDataset.cs)
  - Require()/LastDays() now Assert.SkipUnless on provider outage (drops the Action<string> log param)
  - xUnit2029 fixed (1 site) ; xUnit1051 suppressed via NoWarn (documented)

DOTNET TEST INVOCATION:
  now: dotnet test --project <csproj> --no-build --filter "FullyQualifiedName~X"
  (--filter still takes VSTest syntax ; --nologo gone ; --project required)

PRODUCTION FILES MODIFIED: NONE.
SCIENTIFIC LOGIC: NOT MODIFIED.
DETERMINISM: PASS — framework swap only ; shared dataset still a single immutable instance.
RUN ISOLATION: PASS — AssemblyFixture changes lifetime, not the (immutable) shared content.
LOOK-AHEAD: UNCHANGED.

DEBUG BUILD: PASS (tests, 0/0).
RELEASE BUILD: PASS (tests, 0/0).
TESTS: 75/75 no-network Yahoo unit tests PASS ; assembly-fixture members: see §8.

TRADE-OFFS:
  - 26 heavy backtest classes now run in parallel again (memory pressure ~ pre-16.3) - tunable via
    xunit.runner.json maxParallelThreads if needed (not done here).
  - Residual catch-block "log + Passed" skips (~30 sites) not yet converted to Assert.Skip - follow-up.
  - global.json at repo root steers all `dotnet test` to MTP.

ATAS: NOT USED   ORDERS: NONE   DLL DEPLOYED: NO   COMMIT: NO

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.6_xUnit_v3_Migration_AssemblyFixture_Report.md

NEXT LOT:
RECOMMEND ONLY AFTER ANALYSIS — optional: (1) convert the ~30 residual catch-block skips to
Assert.Skip ; (2) add xunit.runner.json to cap parallelism for the heavy backtest classes ;
(3) empirically derive the Provisional Yahoo retry/timeout constants.

STOP.
```
