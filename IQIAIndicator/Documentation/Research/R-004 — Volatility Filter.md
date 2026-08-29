R-004 — Volatility Filter

Ce document est le contrat officiel de l'implémentation actuelle de `Engine/ScientificModels/Context/VolatilityModel.cs`.

1. Objectif

Le Volatility Filter évalue le contexte de volatilité autour d'un signal de Mean Reversion. Il ne crée pas de signal, il ne recalcule pas les paramètres des modèles précédents et il ne remplace pas le pipeline scientifique existant.

2. Rôle du modèle

Le modèle confirme si le contexte de volatilité est compatible avec l'interprétation des signaux Mean Reverting déjà produits. Il ajoute une preuve de contexte sans modifier l'équilibre latent fourni par le Kalman, l'Ornstein-Uhlenbeck ou le Dynamic Z-Score.

3. Conditions d'exécution

Le VolatilityModel s'exécute seulement si :

- la décision gagnante est `MeanReverting` ;
- la méthodologie sélectionnée a pour nom exact `MeanReversionMethodology` ;
- l'historique de prix contient au moins trois points ;
- le prix courant (`CurrentBar`) est numérique et fini ;
- les métriques issues des modèles antérieurs sont présentes et finies.

Si l'une de ces conditions échoue, le modèle renvoie un résultat `Success = false` et n'émet pas de preuve scientifique.

4. Données consommées

Le Volatility Filter consomme uniquement des données et métriques déjà disponibles dans le pipeline antérieur.

Il ne recalcule jamais :

- `EstimatedMean`
- `InnovationStd`
- `Theta`
- `HalfLife`
- `DynamicZScore`
- `NormalizedDistance`
- `MeanReversionStrength`
- `KalmanGain`

Ces informations proviennent uniquement des `ScientificResults` produits par :

- `KalmanFilterModel`
- `OrnsteinUhlenbeckModel`
- `DynamicZScoreModel`

Le modèle vérifie la présence et la finitude des métriques suivantes :

- `EstimatedMean`, `InnovationStd`, `KalmanGain`
- `Theta`, `HalfLife`, `MeanReversionStrength`
- `DynamicZScore`, `NormalizedDistance`

5. Données produites

L'implémentation actuelle produit exactement ces métriques :

- `CurrentVolatility`
- `ReferenceVolatility`
- `RelativeVolatility`
- `VolatilityPercentile`
- `VolatilityRegime`
- `VolatilityConfidence`
- `Diagnostics`

Aucune autre métrique n'est générée par `ScientificModelResult.Metrics` pour ce modèle.

6. Formules implémentées

6.1. CurrentVolatility

Le modèle calcule `CurrentVolatility` à partir des retours de prix récents :

N = min(20, history.Count - 1)
CurrentVolatility = stddev({price_t - price_{t-1}} pour les N derniers retours)

La volatilité est la déviation standard populationnelle des retours sur la fenêtre retenue.

6.2. ReferenceVolatility

`ReferenceVolatility` est la moyenne des volatilités calculées sur des fenêtres de même longueur N.

Pour chaque position possible d'une fenêtre de longueur N dans l'historique, on calcule l'écart-type des retours de cette fenêtre. Puis on prend la moyenne de ces volatilités partielles.

Si le calcul renvoie une référence nulle ou non positive, le modèle remplace la référence par `CurrentVolatility`.

6.3. RelativeVolatility

RelativeVolatility compare le niveau courant au niveau de référence :

RelativeVolatility = CurrentVolatility / ReferenceVolatility

Si `ReferenceVolatility <= 0`, la mise en œuvre assure `RelativeVolatility = 1`.

6.4. VolatilityPercentile

`VolatilityPercentile` mesure la proportion des volatilités historiques strictement inférieures à `CurrentVolatility`.

La population de référence est constituée des volatilités calculées sur les mêmes fenêtres de longueur N.

VolatilityPercentile = nombre_de_volatilités_historiques_inférieures / taille_de_population

Si la population de référence est insuffisante, `VolatilityPercentile = 0`.

6.5. VolatilityRegime

Le modèle classe le régime de volatilité en trois états :

- `LOW` si `RelativeVolatility < 0.9` et `VolatilityPercentile < 0.33`
- `HIGH` si `RelativeVolatility > 1.1` et `VolatilityPercentile > 0.66`
- `MEDIUM` sinon

Si les valeurs numériques sont invalides, le régime est `UNKNOWN`.

Ces seuils sont actuellement provisoires. Leur calibration devra être validée scientifiquement et documentée dans R-004.

6.6. VolatilityConfidence

`VolatilityConfidence` est définie comme :

VolatilityConfidence = 1 - min(|RelativeVolatility - 1|, 1)

Le résultat est ensuite limité à l'intervalle [0, 1].

Cette métrique est un indicateur interne de robustesse. Elle mesure la proximité de la volatilité courante à la volatilité de référence. Elle n'est pas une probabilité ni une certitude mathématique.

7. Diagnostics

Le champ `Diagnostics` contient toutes les informations permettant d'auditer le calcul :

- les métriques antérieures consommées : `EstimatedMean`, `InnovationStd`, `KalmanGain`, `Theta`, `HalfLife`, `MeanReversionStrength`, `DynamicZScore`, `NormalizedDistance`
- `HistoryCount`
- `CurrentPrice`
- `CurrentVolatility`
- `ReferenceVolatility`
- `RelativeVolatility`
- `VolatilityPercentile`
- `VolatilityRegime`
- `VolatilityConfidence`

8. Pipeline scientifique

Le VolatilityModel s'insère après le Dynamic Z-Score dans le pipeline IQIA :

Market Data
↓
Kalman
↓
Ornstein-Uhlenbeck
↓
Dynamic Z-Score
↓
Volatility Filter
↓
SignalCandidate

Le modèle ne recalcule jamais les paramètres des modèles antérieurs. Il consomme ces paramètres uniquement pour valider le contexte et produire des diagnostics.

9. Évolutions futures

Les seuils de `VolatilityRegime` et la formule de `VolatilityConfidence` sont considérés comme provisoires.

Toute évolution devra :

- être documentée dans R-004 ;
- être synchronisée avec l'implémentation de `Engine/ScientificModels/Context/VolatilityModel.cs`.

10. Conclusion

L'implémentation actuelle de R-004 reflète strictement le comportement réel du code. Le document décrit les métriques calculées, les formules utilisées, les règles de classification et les diagnostics produits par le modèle.

Le Volatility Filter reste une preuve contextuelle de volatilité, pas un déclencheur de position.

- paramètres de fenêtre ou de référence
- valeurs intermédiaires de calcul

L'objectif est de garantir l'auditabilité scientifique du modèle, en conservant suffisamment de détails pour retracer le calcul.

9. Hypothèses

Le Volatility Filter repose sur les hypothèses suivantes :

✔ Kalman exécuté
✔ Ornstein-Uhlenbeck exécuté
✔ Dynamic Z-Score exécuté
✔ MarketContext valide
✔ Stationnarité relative validée

Si une hypothèse est violée, le modèle ne produit pas de preuve scientifique.

10. Limites

Le Volatility Filter n'est plus fiable dans les situations suivantes :

- Flash Crash
- Gap majeur
- Événement macro-économique
- Marché sans liquidité
- Données insuffisantes

Dans ces contextes, la structure de volatilité change trop rapidement ou les données sont trop bruitées pour fournir une évaluation fiable.

11. Compatibilité IQIA

Le Volatility Filter s'intègre naturellement à IQIA parce qu'il complète le pipeline scientifique sans créer de nouveau signal.

- Decision Engine : il fournit une information de contexte qui peut être utilisée par des règles de sélection, mais il ne décide pas seul.
- Methodology Engine : il renforce la méthodologie Mean Reversion en apportant une couche de qualité de marché.
- ScientificModels : il reste un modèle de contexte, compatible avec les autres modèles scientifiques.
- Signal Engine : il s'exécute après les modèles précédents, enrichissant le candidat scientifique sans modifier les résultats antérieurs.

Synchronisation avec le pipeline

Le `VolatilityModel` ne recalcule jamais : `EstimatedMean`, `InnovationStd`, `Theta`, `HalfLife`, `DynamicZScore`, `MeanReversionStrength`. Toutes ces informations proviennent déjà des modèles scientifiques précédents. Le VolatilityModel les exploite uniquement. Il respecte ainsi le principe : une information scientifique = une seule source de vérité.

Évolutions futures

Les définitions de `CurrentVolatility`, `RelativeVolatility`, `VolatilityConfidence` et `VolatilityRegime` pourront évoluer dans les futures versions d'IQIA. Toute évolution devra être documentée dans R-004, être implémentée dans le code et rester cohérente avec les versions précédentes.

12. Principe fondamental

Le VolatilityModel ne déclenche jamais une position.

Il produit uniquement une preuve quantitative.

Il ne décide jamais :

BUY

SELL

WAIT

La décision finale appartiendra exclusivement au futur Entry Engine.

13. Conclusion

Le Volatility Filter est un filtre scientifique, pas un indicateur autonome. Il mesure la dynamique et le contexte de la volatilité pour qualifier la pertinence d'un signal de Mean Reversion.

14. Note scientifique

Critère | Note
--- | ---
Fondement mathématique | ⭐⭐⭐⭐⭐
Publications académiques | ⭐⭐⭐⭐
Robustesse | ⭐⭐⭐⭐
Explicabilité | ⭐⭐⭐⭐
Compatibilité IQIA | ⭐⭐⭐⭐⭐

15. Verdict

Le Volatility Filter doit être la preuve contextuelle qui complète le pipeline IQIA. Il confirme la qualité de marché sans remplacer les modèles antérieurs ni générer lui-même une décision de trading.
