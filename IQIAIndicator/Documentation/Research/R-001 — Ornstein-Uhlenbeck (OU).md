R-001 — Ornstein-Uhlenbeck (OU)

Je commence volontairement par OU, parce que je pense que c'est probablement le modèle le plus important pour IQIA.

1. Pourquoi OU existe ?

Avant OU, il faut comprendre un problème.

Imaginons une action.

Elle passe de :

100

↓

102

↓

104

↓

103

↓

101

↓

99

↓

101

↓

100

Question :

Le prix revient-il vers une valeur "naturelle" ?

Ou bien est-ce complètement aléatoire ?

Les modèles classiques ne répondaient pas correctement à cette question.

En 1930, George Uhlenbeck et Leonard Ornstein introduisent un processus pour décrire un système qui est :

perturbé en permanence,
mais attiré vers un équilibre.

Au départ, ils ne parlaient pas de finance.

Ils décrivaient le mouvement d'une particule dans un fluide (mouvement brownien avec frottement).

Plus tard, les financiers ont compris que ce modèle décrivait remarquablement certains actifs financiers.

2. L'idée fondamentale

Le prix subit deux forces.

Force 1

Le hasard.

Brownian Motion

Le marché est perturbé.

Force 2

Le retour vers une moyenne.

Mean Reversion

Le marché est attiré vers une valeur d'équilibre.

OU combine ces deux forces.

3. Formule

Le modèle continu est :

dX
t
	​

=θ(μ−X
t
	​

)dt+σdW
t
	​


Elle paraît impressionnante, mais chaque terme a un sens très intuitif.

X
t
	​


Valeur actuelle.

μ

Valeur d'équilibre.

Le marché cherche à revenir ici.

θ

Vitesse de retour.

Si :

θ grand

↓

Le marché revient vite.

Si :

θ petit

↓

Le marché revient lentement.

σ

Le bruit.

Plus il est grand,

plus le marché est chaotique.

dW
t
	​


Le hasard.

Le Brownian Motion.

4. Version discrète utilisée en trading

En trading, les observations sont prises à des instants discrets. La version discrète d'un processus OU s'écrit souvent comme une forme AR(1) :

X_{t+1} = μ + φ (X_t − μ) + ε_t

avec φ = e^{−θ Δt} et ε_t un bruit centré de variance σ^2 Δt.

En pratique, pour des barres régulières, on simplifie souvent ainsi :

X_{t+1} − X_t = θ (μ − X_t) Δt + σ √{Δt} ε_t.

Pour Δt = 1, on retrouve une relation linéaire proche de la forme AR(1).

Cette version discrète est celle que l'on utilise pour estimer les paramètres d'un actif financier à partir d'une série temporelle de prix.

5. Méthodes d'estimation de θ, μ et σ

Les méthodes classiques sont :

- μ : l'équilibre peut être estimé par la moyenne historique de la série ou par une estimation de niveau latent.
- θ : la vitesse de retour se calcule par régression de ΔX_t sur (μ − X_t), ou par estimation de l'autocorrélation φ du terme AR(1) puis conversion θ = −ln(φ)/Δt.
- σ : on estime l'écart-type des résidus de la régression OU, c'est-à-dire de ε_t.

En finance quantitative, on utilise souvent :

- l'estimation OLS d'un AR(1) centré autour de μ ;
- l'estimateur du maximum de vraisemblance (MLE) pour une série Ornstein-Uhlenbeck discrète ;
- le filtre de Kalman lorsque μ est traité comme un état caché et que les observations sont bruitées.

Dans IQIA, la mise en œuvre actuelle dérive des métriques du filtre de Kalman. Au lieu de recalculer séparément θ, μ et σ, le système exploite :

- EstimatedMean (équilibre latent) ;
- Innovation et InnovationStd (bruit observé) ;
- NormalizedInnovation (écart relatif au bruit) ;
- KalmanGain (confiance du filtre) ;
- FilterCovariance (optionnellement, incertitude de l'état latent).

8. Variante IQIA Real-Time Ornstein-Uhlenbeck

Cette variante est spécifique à IQIA. Elle conserve les fondements mathématiques du modèle OU, mais elle adapte uniquement le pipeline de calcul pour une architecture temps réel.

Il ne s'agit pas d'un nouveau modèle mathématique ; il s'agit d'une implémentation architecturale optimisée.

7. Principe d'architecture

Le filtre de Kalman est l'unique source officielle de l'état latent.

Le modèle Ornstein-Uhlenbeck ne réestime jamais :

- μ ;
- Innovation ;
- InnovationStd ;
- KalmanGain ;
- FilterCovariance.

Cette décision est volontaire. Elle évite :

- les calculs redondants ;
- les incohérences ;
- les doubles estimations.

Elle garantit :

une information scientifique
↓
une seule source de vérité.

Schéma du pipeline :

Market Data

↓

Kalman Filter

↓

Estimated Mean
Innovation
InnovationStd
Kalman Gain
Filter Covariance

↓

Ornstein-Uhlenbeck

↓

Theta
Half-Life
Mean Reversion Strength
Diagnostics

↓

Dynamic Z-Score

↓

Volatility

↓

SPRT

↓

SignalCandidate

8. Implémentation spécifique d'IQIA

La théorie classique OU estime μ, θ et σ à partir de la série elle-même (OLS, MLE, AR(1)).

Dans IQIA, cette approche est adaptée à une architecture temps réel orientée pipeline.

Le filtre de Kalman est la source officielle de l'état latent.

Le modèle Ornstein-Uhlenbeck n'est donc jamais autorisé à réestimer :

- μ ;
- Innovation ;
- Variance ;
- Kalman Gain.

Ces informations sont récupérées directement depuis les métriques structurées produites par `KalmanFilterModel`.

Le rôle d'Ornstein-Uhlenbeck est exclusivement :

- d'estimer la dynamique de retour vers la moyenne ;
- de calculer la Half-Life ;
- d'évaluer l'intensité du phénomène de Mean Reversion.

Cette architecture évite les calculs redondants, garantit une seule source de vérité et optimise l'exécution temps réel.

9. Calcul de la Half-Life

La Half-Life est le temps nécessaire pour que l'écart à l'équilibre se réduise de moitié.

Pour le processus continu, elle s'exprime comme :

Half-Life = ln(2) / θ.

Pour la version discrète AR(1), on la calcule aussi via φ :

Half-Life = ln(0.5) / ln(φ)

avec φ = e^{−θ Δt}.

Si θ ≤ 0 ou φ ≥ 1, la Half-Life est considérée comme infinie, car il n'y a pas de rappel vers la moyenne.

10. Pourquoi OU est extraordinaire ?

103

102

101

100 ← moyenne

99

98

97

96

95

Le prix peut s'éloigner.

Mais plus il s'éloigne,

plus la force de rappel augmente.

C'est exactement ce que l'on observe sur beaucoup de marchés en range.

11. Ce qu'il permet de calculer

OU fournit énormément d'informations.

Par exemple :

Valeur d'équilibre
μ
Force du retour
θ
Volatilité
σ
Probabilité de retour

Très intéressant.

Temps moyen de retour

Encore plus intéressant.

Half-Life

Que tu utilises déjà.

En réalité,

Half-Life provient directement du modèle OU.

Donc IQIA exploite déjà une partie de ce modèle.

12. Utilisation en finance

OU est utilisé notamment pour :

Statistical Arbitrage
Pairs Trading
Spread Trading
Volatility Modeling
Fixed Income
Commodities
Options

Il est omniprésent dans les desks quantitatifs.

13. Les limites

OU suppose que :

la moyenne existe ;
elle reste relativement stable ;
le processus est stationnaire.

Donc :

En tendance forte,

OU devient mauvais.

Très mauvais.

C'est important.

Il ne faut jamais appliquer OU partout.

14. Compatibilité avec IQIA

Je pense qu'elle est excellente.

Pourquoi ?

IQIA possède déjà :

✅ Stationarity

↓

OU a besoin de stationnarité.

IQIA possède :

✅ Mean Reversion

↓

OU est précisément un modèle de Mean Reversion.

IQIA possède :

✅ Half-Life

↓

C'est déjà un paramètre du modèle.

IQIA possède :

✅ Decision Engine

↓

Il peut décider :

OU autorisé

ou

OU interdit

selon le comportement détecté.

15. Principe fondamental

Les Scientific Models ne déclenchent jamais une position.

Ils produisent uniquement des preuves quantitatives.

Chaque modèle enrichit les informations produites par les modèles précédents.

Une information scientifique ne doit avoir qu'une seule source de vérité.

La décision finale appartient exclusivement au futur Entry Engine.

16. Conclusion

Beaucoup de traders font :

ZScore > 2

↓

SELL

Je ne veux pas ça.

Je préférerais :

Decision Engine

↓

Mean Reverting

↓

OU valide

↓

Le prix est éloigné de μ

↓

La probabilité de retour est élevée

↓

Signal.

OU devient une preuve,

pas un déclencheur.

17. Note scientifique

Pour moi,

OU n'est PAS une stratégie.

C'est un modèle probabiliste.

C'est très différent.

Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Robustesse	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Explicabilité	⭐⭐⭐⭐⭐
Compatibilité IQIA	⭐⭐⭐⭐⭐
18. Verdict

Mon verdict

Je pense qu'OU doit devenir l'un des piliers du futur Signal Engine, mais uniquement lorsque le Decision Engine a déjà identifié un comportement compatible avec la Mean Reversion.

Je ne l'utiliserais jamais comme un indicateur universel.