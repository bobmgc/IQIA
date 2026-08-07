R-008 — Volatility Models (EWMA, GARCH, Realized Volatility)

Verdict avant l'étude :

Je pense que ce module deviendra le filtre universel d'IQIA. Il ne créera jamais un signal, mais il décidera si un signal peut être exploité.

1. Pourquoi la volatilité est si importante ?

Beaucoup de traders raisonnent ainsi :

Signal

↓

Entrée

Les desks quantitatifs raisonnent plutôt :

Signal

↓

Contexte de volatilité

↓

Entrée

Deux signaux identiques peuvent avoir des espérances très différentes selon la volatilité.

2. La volatilité n'est pas le risque

C'est une confusion très fréquente.

La volatilité mesure :

L'amplitude attendue des mouvements.

Le risque dépend aussi :

du levier ;
du Stop Loss ;
de la liquidité ;
de la stratégie.

IQIA devra garder ces notions séparées.

3. Pourquoi les modèles de volatilité existent ?

Prenons deux journées.

Jour A :

+0.1 %
-0.2 %
+0.1 %
+0.2 %

Jour B :

+2.5 %
-3.0 %
+2.8 %
-2.2 %

Dans les deux cas, la moyenne peut être proche de zéro.

Pourtant, les conditions de trading sont totalement différentes.

Il faut donc modéliser la dispersion, pas seulement la direction.

4. Les trois grandes familles
A. EWMA (Exponentially Weighted Moving Average)

Principe :

Les observations récentes comptent davantage.

Avantages :

très rapide ;
robuste ;
utilisé dans l'industrie (RiskMetrics).

Inconvénients :

ne modélise pas toute la dynamique de la volatilité.
B. GARCH

Le modèle suppose que :

La volatilité d'aujourd'hui dépend de la volatilité d'hier et des chocs récents.

C'est le phénomène de volatility clustering :

Faible volatilité

↓

Faible volatilité

↓

Faible volatilité

↓

Explosion

↓

Forte volatilité

↓

Forte volatilité

Les marchés ont tendance à alterner des périodes calmes et agitées.

C. Realized Volatility

On mesure directement la volatilité observée sur une fenêtre récente.

Très utile en intraday.

Simple.

Robuste.

5. Pourquoi c'est essentiel pour IQIA ?

Supposons :

Behaviour

↓

Mean Reverting

↓

OU valide

↓

Kalman valide

↓

Z-Score = 3

Tout semble parfait.

Mais :

Volatilité

↓

Extrême

Le signal devient beaucoup plus risqué.

Le modèle de volatilité ne dit pas :

"Ne trade pas."

Il dit :

"Les conditions ne sont plus celles pour lesquelles cette méthodologie est optimale."

6. Compatibilité avec les comportements
Mean Reversion

Très forte volatilité :

⚠️ prudence.

Une déviation peut continuer à s'amplifier.

Trending

Une volatilité modérée à élevée est souvent compatible.

Trop faible :

Le mouvement manque d'énergie.

Stable Range

Une volatilité trop élevée remet en cause l'idée même d'un range stable.

Structural Break

Une explosion de volatilité peut renforcer l'hypothèse de transition.

Random Walk

Une forte volatilité sans structure identifiable reste un contexte difficile.

7. Ce que je ne ferais pas

Je ne créerais jamais une règle :

ATR > X

↓

BUY

Ou :

Volatility > Y

↓

SELL

La volatilité n'est pas un signal.

C'est un filtre de qualité.

8. Où placer ce module ?

Je ne l'intégrerais pas au Decision Engine.

Pourquoi ?

Le Decision Engine répond :

Quel comportement domine ?

La volatilité répond :

Les conditions d'application de cette méthodologie sont-elles réunies ?

9. Architecture proposée
Decision Engine

↓

Methodology Engine

↓

Evidence Providers

↓

Volatility Filter

↓

Bayesian Update

↓

SPRT

↓

Signal

Le filtre de volatilité agit avant la décision finale.

10. Quel modèle choisir ?

À mon avis :

EWMA : excellent pour le temps réel.
Realized Volatility : excellent pour l'intraday.
GARCH : très puissant mais plus coûteux.

Je ne choisirais pas un seul modèle.

Je créerais une Evidence de Volatilité qui pourrait intégrer plusieurs mesures.

11. Compatibilité avec le Risk Engine

Le même module servira ensuite au Risk Engine.

Exemple :

Volatilité élevée

↓

Stop Loss plus large

↓

Position plus petite

Le travail n'est donc pas perdu.

12. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐⭐⭐ (EWMA / Realized)
Compatibilité IQIA	⭐⭐⭐⭐⭐
13. Ce que cette étude change

Je pense que nous devons distinguer deux types d'évidences.

Évidences de direction

Elles répondent :

Que fait le marché ?

Exemples :

OU
Momentum
Kalman
Z-Score
Évidences de contexte

Elles répondent :

Les conditions sont-elles favorables ?

Exemples :

Volatilité
BOCPD
(plus tard : Order Flow)