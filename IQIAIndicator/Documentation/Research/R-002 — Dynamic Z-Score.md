R-002 — Dynamic Z-Score

Je construis ici la référence scientifique officielle du Dynamic Z-Score pour IQIA.

1. Pourquoi le Z-Score existe ?

Le Z-Score a été inventé pour répondre à une question simple mais fondamentale : quelle est la distance réelle d'une observation à sa moyenne quand l'échelle des valeurs est différente ?

Dans les séries statistiques, un écart de 2 points n'a pas le même sens sur une variable centrée autour de 100 que sur une variable centrée autour de 1000. Le Z-Score résout ce problème en normalisant la distance par l'écart-type.

En finance quantitative, le Z-Score est largement utilisé parce qu'il permet de comparer des actifs de tailles et de volatilités différentes, de détecter des extrêmes statistiquement significatifs et de construire des règles de mean reversion objectives.

2. L'idée fondamentale

Le Z-Score mesure combien d'écarts-types une observation est éloignée de sa moyenne.

La notion est intuitive : si un prix est un écart-type au-dessus de la moyenne, il se situe dans la zone dite « normale ». Si un prix est deux écart-types au-dessus, il devient déjà inhabituel.

Exemple A :

100
100.2
99.8
100.1
99.9

Dans ce cas, 102 serait un écart énorme par rapport à la variabilité de la série.

Exemple B :

100
105
95
104
96

Dans cette série plus volatile, 102 est une valeur beaucoup plus banale.

Le Z-Score capture cette différence : il ne regarde pas seulement l'écart absolu, il le met en relation avec la dispersion du signal.

3. Formule classique

La formule académique du Z-Score est :

Z = (X - μ) / σ

avec :

- X : la valeur observée, ici le prix ou la variable actuelle.
- μ : la moyenne de la distribution ou de l'équilibre.
- σ : l'écart-type de la distribution.

Interprétation :

- Z = 0 : la valeur est exactement sur sa moyenne.
- Z = +1 : la valeur est un écart-type au-dessus de la moyenne ; situation normale.
- Z = +2 : la valeur est déjà très éloignée.
- Z = +3 : la valeur est très rare dans une loi normale.
- Z négatif : même logique symétrique, la valeur est en dessous de la moyenne.

4. Pourquoi le Z-Score classique est insuffisant

Le Z-Score classique repose sur des paramètres fixes : moyenne fixe et variance fixe. Or le marché n'est pas stationnaire.

- La moyenne peut évoluer quand l'équilibre du marché change.
- La volatilité peut varier fortement d'une période à l'autre.
- Les changements de régime rendent les paramètres historiques obsolètes.

Dans un moteur temps réel, utiliser une moyenne ou une variance fixes conduit à des erreurs de qualification. Un Z-Score élevé dans un régime très volatil peut être interprété à tort comme extrême. Inversement, un Z-Score faible dans une période de faible volatilité peut masquer un déséquilibre réel.

5. Dynamic Z-Score

Le Dynamic Z-Score conserve la formule académique du Z-Score, mais il remplace les paramètres statiques par des estimations dynamiques.

La moyenne devient dynamique.
La volatilité devient dynamique.

Dans IQIA, l'équilibre est fourni par le Kalman Filter. La vitesse de retour vers cet équilibre est fournie par le modèle Ornstein-Uhlenbeck.

Le Dynamic Z-Score devient ainsi une mesure de distance par rapport à un équilibre vivant, non pas un seuil statique.

6. Variante IQIA Real-Time Dynamic Z-Score

IQIA ne recalcule jamais :

- EstimatedMean
- InnovationStd
- Theta
- HalfLife

Ces informations proviennent déjà du pipeline scientifique.

Le rôle du Dynamic Z-Score est uniquement de mesurer la distance statistique à cet équilibre dynamique.

Il n'introduit pas de nouveau modèle mathématique. Il applique le principe académique du Z-Score avec des paramètres fournis par le pipeline temps réel d'IQIA.

7. Principe d'architecture

Dans IQIA, le pipeline scientifique se lit comme suit :

Market Data

↓

Kalman

↓

Ornstein-Uhlenbeck

↓

Dynamic Z-Score

Le Dynamic Z-Score ne réestime jamais les modèles précédents. Il s'appuie sur les résultats scientifiques déjà disponibles pour enrichir la chaîne.

8. Données consommées

Le Dynamic Z-Score doit consommer exclusivement des métriques issues des ScientificResults déjà produits :

- EstimatedMean
- InnovationStd
- Theta
- HalfLife
- MeanReversionStrength
- KalmanGain (si pertinent)

Ces données ne sont pas recalculées par le Dynamic Z-Score.

9. Données produites

Le modèle produit au minimum les métriques suivantes :

- DynamicZScore : la distance normalisée du prix actuel par rapport à l'équilibre dynamique.
- NormalizedDistance : la mesure de distance relative à la volatilité observée.
- ExpectedReversionDistance : l'indication de la distance de retour attendue vers l'équilibre.
- DynamicConfidence : le degré de confiance scientifique associé à la mesure.
- Diagnostics : informations qualitatives ou quantitatives sur le calcul.

NormalizedDistance

Dans IQIA v1, `NormalizedDistance` correspond à :

NormalizedDistance = |DynamicZScore|

avec :

DynamicZScore = (CurrentPrice - EstimatedMean) / InnovationStd

Cette métrique représente uniquement la valeur absolue de l'écart statistique. Elle mesure la distance statistique au point d'équilibre sans tenir compte de la direction. Cette définition est propre à IQIA v1.

ExpectedReversionDistance

Dans IQIA v1, `ExpectedReversionDistance` correspond à :

ExpectedReversionDistance = |CurrentPrice - EstimatedMean|

Cette métrique représente la distance absolue restant à parcourir pour revenir vers l'équilibre estimé. Il ne s'agit pas d'une prévision de prix, pas d'une probabilité, et pas d'une estimation temporelle ; elle représente uniquement la distance actuelle entre le marché et l'équilibre dynamique.

DynamicConfidence

Dans IQIA v1, `DynamicConfidence` est calculé comme :

DynamicConfidence = 1 - min(|DynamicZScore| / 6, 1)

Cette métrique est une métrique interne au pipeline scientifique d'IQIA. Elle ne représente pas une probabilité, pas une confiance statistique au sens académique, pas un niveau de certitude mathématique. Elle constitue uniquement un indicateur interne permettant de pondérer l'interprétation du Dynamic Z-Score.

Rôle de chaque métrique :

- DynamicZScore : preuve quantitative de l'écart par rapport à l'équilibre.
- NormalizedDistance : ratio d'écart pris par rapport à la dispersion dynamique.
- ExpectedReversionDistance : distance anticipée avant retour vers l'équilibre.
- DynamicConfidence : confort scientifique du résultat.
- Diagnostics : permet l'audit et l'explicabilité.

10. Hypothèses

Le Dynamic Z-Score repose sur les hypothèses suivantes :

✔ Mean Reversion validée
✔ Kalman exécuté
✔ Ornstein-Uhlenbeck exécuté
✔ Stationnarité relative validée
✔ ScientificResults disponibles et cohérents

Si une hypothèse est violée, le modèle ne doit pas produire de preuve scientifique.

11. Limites

Le Dynamic Z-Score ne doit pas être utilisé dans les situations suivantes :

- forte tendance continue
- rupture structurelle du marché
- Random Walk pur
- qualité des données insuffisante
- absence de résultats scientifiques antérieurs

Dans ces cas, il ne fournit pas une preuve fiable.

12. Compatibilité IQIA

Ce modèle s'intègre naturellement à IQIA parce qu'il respecte les principes du Decision Engine, du Methodology Engine et du Signal Engine.

- Decision Engine : autorise l'usage du Dynamic Z-Score seulement si le comportement est Mean Reverting.
- Methodology Engine : inclut le Dynamic Z-Score dans la méthodologie Mean Reversion.
- Signal Engine : exécute les modèles scientifiques dans la bonne séquence.

Le Dynamic Z-Score est un élément du pipeline scientifique, pas un signal de trading.

Synchronisation avec le pipeline

Le `DynamicZScoreModel` ne calcule aucune des informations suivantes : `EstimatedMean`, `InnovationStd`, `Theta`, `HalfLife`, `MeanReversionStrength`. Ces informations proviennent des modèles scientifiques précédents. Le Dynamic Z-Score ne fait que les exploiter. Il respecte ainsi le principe de source unique de vérité défini dans ARC-003.

Évolutions futures

Les définitions de `DynamicConfidence` et `ExpectedReversionDistance` pourront évoluer dans les futures versions d'IQIA. Toute évolution devra être documentée dans R-002, être implémentée dans le code, et rester rétrocompatible lorsque cela est possible.

13. Principe fondamental

Le Dynamic Z-Score ne déclenche jamais une position.

Il produit uniquement une preuve quantitative.

La décision appartient exclusivement au futur Entry Engine.

14. Conclusion

Le Dynamic Z-Score est un maillon du pipeline scientifique d'IQIA. Il mesure l'écart statistique entre le marché et son équilibre dynamique sans remplacer ni recalculer les informations produites en amont.

Il ne doit jamais être considéré comme une stratégie autonome.

15. Note scientifique

Critère | Note
--- | ---
Fondement mathématique | ⭐⭐⭐⭐⭐
Publications académiques | ⭐⭐⭐⭐⭐
Robustesse | ⭐⭐⭐⭐⭐
Explicabilité | ⭐⭐⭐⭐⭐
Compatibilité IQIA | ⭐⭐⭐⭐⭐

16. Verdict

Le Dynamic Z-Score doit devenir une preuve centrale du Signal Engine pour les environnements mean reversion. Il reste un instrument de mesure, pas un déclencheur de trade.
