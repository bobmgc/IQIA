R-007 — Bayesian Decision Theory

Verdict avant l'étude :

Je pense que Bayes pourrait devenir le chef d'orchestre de tout IQIA.

Pas seulement du Signal Engine.

Peut-être même du Decision Engine V2.

1. Pourquoi Bayes existe ?

Avant Bayes, les décisions étaient souvent :

Vrai

ou

Faux

Mais dans la vraie vie...

On ne sait jamais.

On a seulement des probabilités.

En 1763, le révérend Thomas Bayes publie un principe révolutionnaire :

Une croyance doit évoluer lorsqu'une nouvelle preuve apparaît.

Cette idée est aujourd'hui au cœur de :

l'intelligence artificielle ;
la robotique ;
les véhicules autonomes ;
le diagnostic médical ;
les systèmes radar ;
la finance quantitative.
2. L'idée fondamentale

Supposons que tu penses :

Probabilité d'une tendance

=

40 %

Puis tu observes :

une forte persistance ;
une faible stationnarité ;
un momentum élevé.

Ta croyance doit évoluer.

Pas brutalement.

Progressivement.

C'est exactement ce que fait Bayes.

3. La formule

Le théorème de Bayes est :

P(H∣E)=
P(E)
P(E∣H)×P(H)
	​


Ne retiens pas la formule.

Retiens le sens.

4. Les quatre éléments
Prior

La croyance initiale.

Exemple :

Trending

30 %
Evidence

Nouvelle information.

Exemple :

Persistence = 0.88
Likelihood

Quelle est la probabilité d'observer cette évidence si le marché est réellement en tendance ?

Posterior

Nouvelle croyance.

Exemple :

Trending

↓

72 %
5. Pourquoi c'est énorme

Bayes ne remplace pas les modèles.

Il les combine.

Autrement dit :

OU

↓

Bayes

Kalman

↓

Bayes

Momentum

↓

Bayes

Volatility

↓

Bayes

↓

Décision.
6. Ce que font beaucoup de systèmes

Ils utilisent :

SI

A

ET

B

ET

C

↓

BUY

Bayes fait :

Evidence

↓

Nouvelle probabilité

↓

Nouvelle Evidence

↓

Nouvelle probabilité

↓

Nouvelle Evidence

↓

Nouvelle probabilité

Le système apprend progressivement.

7. Pourquoi les quants adorent Bayes

Parce qu'il permet :

intégrer plusieurs modèles ;
gérer l'incertitude ;
pondérer naturellement les évidences ;
mettre à jour les croyances en temps réel.
8. Compatibilité avec IQIA

Je pense qu'elle est extraordinaire.

Regarde.

Aujourd'hui :

Evidence

↓

Fusion Rules

↓

Decision

Demain :

Evidence

↓

Bayesian Update

↓

Decision

Autrement dit :

Même le Fusion Engine pourrait évoluer un jour.

9. Comparaison avec SPRT

C'est très intéressant.

Le SPRT répond :

Les preuves sont-elles suffisantes ?

Bayes répond :

Quelle est maintenant ma croyance ?

Ce n'est pas la même chose.

10. Peut-on utiliser les deux ?

Oui.

Et je pense même que c'est la meilleure solution.

Par exemple :

Bayes

↓

Posterior Probability

↓

SPRT

↓

Décision finale

Bayes met à jour les probabilités.

Le SPRT décide.

11. Exemple concret

Supposons :

Prior

Mean Reversion

40 %

Nouvelle information :

Kalman

↓

Prix proche de l'équilibre

↓

Nouvelle probabilité :

55 %

Puis :

OU

↓

Retour probable

↓

71 %

Puis :

Dynamic Z

↓

Extrême statistique

↓

91 %

Le système devient progressivement plus confiant.

12. Pourquoi je préfère Bayes aux scores

Aujourd'hui beaucoup de systèmes font :

OU

40 points

Kalman

30 points

Momentum

20 points

↓

90 points

Pourquoi :

40 ?

Pourquoi :

30 ?

C'est arbitraire.

Bayes ne fonctionne pas comme ça.

Il utilise les probabilités.

13. Les limites

Bayes suppose :

des probabilités correctement estimées.

Si les probabilités d'entrée sont mauvaises,

le résultat sera mauvais.

Le modèle est aussi sensible aux hypothèses d'indépendance entre les évidences. Dans la pratique, certaines informations sont corrélées (par exemple OU et Z-Score), ce qu'il faudra prendre en compte.

14. Compatibilité avec tes modèles

OU

↓

Produit une probabilité.

Kalman

↓

Produit une estimation.

Momentum

↓

Produit une probabilité de continuité.

Random Walk

↓

Produit une probabilité d'absence de structure.

Tout cela est naturellement compatible avec Bayes.

15. Ce que je changerais dans IQIA

Je ne parlerais plus de :

Scientific Score

Je parlerais de :

Posterior Probability

C'est beaucoup plus quantitatif.

16. Vision d'ensemble

Je commence à voir ceci :

Decision Engine
        │
        ▼
Methodology Engine
        │
        ▼
Evidence Providers
        │
        ▼
Bayesian Update
        │
        ▼
Sequential Decision (SPRT)
        │
        ▼
Signal
17. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Robustesse	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐⭐☆
Compatibilité IQIA	⭐⭐⭐⭐⭐