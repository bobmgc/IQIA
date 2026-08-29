# QDE-012 — Sprint 15.25 — Lot 16.7 — Nettoyage de la convention de skip (`Assert.Skip`)

> **Type :** Hygiène des tests — **Mode :** balayage mécanique
> **Production modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Suite directe du Lot 16.6 (migration xUnit v3). Ce lot termine le passage à `Assert.Skip` : les
> ~40 sites résiduels qui affichaient encore `_output.WriteLine("SKIPPED …")` + `return` (test tout de
> même **Passed**) deviennent des **`Assert.Skip(...)`** — le test est désormais **`Skipped`** dans le
> runner. **Aucun fichier de production n'est touché.**

---

## 1. Contexte

Le Lot 16.6 a converti le **chemin `YahooSessionDataset.Require()` / `LastDays()`** en
`Assert.SkipUnless`. Restaient trois familles de sites en « log + Passed » :

| # | Site | Occurrences | Sens |
|---|---|---|---|
| 1 | `catch (Exception exception) when (IsConnectivityOrProviderIssue(exception)) { _output.WriteLine($"SKIPPED (network/Yahoo unavailable …): {exception.GetType().Name}: {exception.Message}"); }` | **28 fichiers** | panne fournisseur / réseau sur un appel aval du pipeline |
| 2 | `if (dataset is null) { _output.WriteLine("SKIPPED (network/Yahoo unavailable …)."); return; }` (consommateurs de `StructuralBreakObservationSetBuilder.Instance`) | **12 sites / 7 fichiers** | dataset partagé indisponible (Yahoo down) |
| 3 | `if (remaining < N) { _output.WriteLine($"SKIPPED (not a code failure): only {series.Count} bars …"); return; }` | **4 sites / 2 fichiers** | données insuffisantes pour le découpage TRAIN/VALIDATION/OOS |

---

## 2. Conversion

| Avant | Après |
|---|---|
| `_output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");` | `Assert.Skip($"Yahoo provider unavailable (not a code failure): {exception.GetType().Name}: {exception.Message}");` |
| `_output.WriteLine("SKIPPED (network/Yahoo unavailable, not a code failure).");` + `return;` | `Assert.Skip("Yahoo provider unavailable (not a code failure).");` |
| `_output.WriteLine($"SKIPPED (not a code failure): only {series.Count} bars returned, insufficient for … TRAIN/VALIDATION/OOS split.");` + `return;` | `Assert.Skip($"insufficient data: only {series.Count} bars returned for … TRAIN/VALIDATION/OOS split.");` |
| `_output.WriteLine("SKIPPED (not a code failure): remaining bars too few …");` + `return;` | `Assert.Skip("insufficient data: remaining bars too few …");` |

`Assert.Skip` lève `SkipException`, qui remonte proprement — y compris depuis un bloc `catch` ou
depuis l'intérieur d'un `if` de garde ; le `return;` devient inutile et est supprimé. Les prédicats
`IsConnectivityOrProviderIssue(...)` (filtres `when`) sont **conservés** : seul le corps change.

### Fichiers touchés (35, tests uniquement)

12 × `Backtest/Calibration/*`, 5 × `Backtest/{Cost,Dataset,Execution,Measurement,Pnl,Risk,Pipeline}/*YahooIntegrationTests` + `YahooDatasetCoverageTests`, `Backtest/Yahoo/YahooNetworkIntegrationTests`, 3 × `Research/Lot16ContractNormalization/*`, `Research/SixDimensionAudit/*`, 2 × `Research/StructuralBreakAudit/*`, 7 × `Research/StructuralBreakInformationAudit/*`.

**`git status` : aucun fichier `IQIAIndicator/{Core,Engine,Backtest,Visualization}` non-test modifié par ce lot.**

---

## 3. Effet observable

| Condition | Avant (≤ 16.6, sites résiduels) | Après (16.7) |
|---|---|---|
| Yahoo indisponible (429 / réseau) | test **Passed**, ligne `SKIPPED` dans la sortie | test **`Skipped`**, raison visible dans le runner |
| Données insuffisantes pour le split | test **Passed**, ligne `SKIPPED` | test **`Skipped`**, raison `insufficient data: …` |
| Yahoo disponible, données OK | inchangé (assertions réelles) | inchangé |

Le résultat de suite distingue enfin, **au niveau du runner**, trois états : `Passed` (a réellement
vérifié quelque chose), `Skipped` (panne fournisseur / données insuffisantes — non-régression),
`Failed` (régression scientifique réelle — n'est plus jamais masquée en `Passed`).

---

## 4. Builds & tests

| Étape | Résultat |
|---|---|
| `dotnet build IQIAIndicator.Tests.csproj -c Debug` | **La génération a réussi. 0 Avertissement, 0 Erreur** (1 min 08 s) |
| `dotnet build IQIAIndicator.Tests.csproj -c Release` | **La génération a réussi. 0 Avertissement, 0 Erreur** (1 min 58 s) |
| `dotnet build IQIAIndicator.csproj` Debug / Release | non touché |

Tests (`dotnet test --project … --filter …`, MTP) :

| Filtre | Résultat |
|---|---|
| 5 classes Yahoo sans réseau + `YahooNetworkIntegrationTests` | **63 / 63 Réussi** (28 s), `ignoré : 0` (Yahoo disponible) |

`Assert.Skip` compile dans les 44 sites (build vert) ; le chemin `Skipped` lui-même n'est visible que
lorsque Yahoo est réellement indisponible — non reproductible à la demande, couvert par construction
(API standard xUnit v3, déjà validée pour `Require()` au Lot 16.6).

---

## 5. Ce qui n'est PAS fait (décisions explicites)

- **`xunit.runner.json` pour plafonner le parallélisme** (piste Lot 16.6 §5) : **non ajouté**. Le
  parallélisme des 26 classes lourdes est revenu à l'état d'avant Lot 16.3, qui était opérationnel (le
  problème d'alors était le 429, pas la mémoire). Ajouter un plafond « corrigerait » un problème non
  mesuré et ralentirait les tests unitaires rapides. À faire uniquement si une pression mémoire réelle
  est observée.
- **Dérivation empirique des constantes `Provisional` Yahoo** : toujours hors périmètre (nécessiterait
  de provoquer des 429 + relève de l'optimisation de paramètres, interdite par le sprint).

---

## 6. FINAL OUTPUT

```
STATUS:
LOT COMPLETE — the "log SKIPPED + still Passed" convention is fully replaced by Assert.Skip.

SCOPE:
44 residual skip sites across 35 test files converted:
  - 28 catch-block provider/network skips  -> Assert.Skip(...)
  - 12 "dataset is null" guards (StructuralBreakObservationSetBuilder consumers) -> Assert.Skip(...)
  - 4 low-data TRAIN/VALIDATION/OOS guards -> Assert.Skip(...)
The `when (IsConnectivityOrProviderIssue(...))` filters are unchanged; only the bodies change.

PRODUCTION FILES MODIFIED: NONE.
SCIENTIFIC LOGIC: NOT MODIFIED.
DETERMINISM / LOOK-AHEAD: UNCHANGED (test-reporting change only).

EFFECT:
Provider outage / insufficient data -> test reports as Skipped (was: Passed with a log line).
A real scientific regression can no longer hide as a green Pass on these paths.

DEBUG BUILD: PASS (tests, 0/0).
RELEASE BUILD: PASS (tests, 0/0).
TESTS: 63/63 no-network + small-network Yahoo tests PASS (ignoré: 0, Yahoo up).

NOT DONE (documented): xunit.runner.json parallelism cap (no measured need);
empirical Yahoo retry-constant derivation (out of sprint scope).

ATAS: NOT USED   ORDERS: NONE   DLL DEPLOYED: NO   COMMIT: NO

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.7_Assert_Skip_Convention_Cleanup_Report.md

NEXT LOT:
The Yahoo-resilience / test-infra track (Lots 16.3 -> 16.7) is complete. No further increment is
recommended without a new, explicitly-scoped objective from the user.

STOP.
```
