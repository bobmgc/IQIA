Parfait.

Maintenant on attaque ce que je considère comme le modèle le plus sophistiqué de toute la recherche quantitative.

Je vais être honnête : c'est probablement l'étude la plus difficile que nous allons faire.

Mais si ce modèle est retenu, il pourrait devenir l'évolution naturelle du Decision Engine V2.

R-009 — Hidden Markov Models (HMM)

Auteurs principaux :

Leonard E. Baum
Lloyd Welch
Applications majeures : Speech Recognition, Bioinformatics, Finance Quantitative
⭐ Verdict avant l'étude

Je pense que le HMM est probablement :

Le meilleur modèle mathématique existant pour détecter les régimes de marché.

Mais...

Je ne suis pas encore convaincu qu'il doive remplacer le Decision Engine.

Voyons pourquoi.

1. Pourquoi les HMM existent ?

Imagine une personne dans une pièce.

Tu ne peux pas la voir.

Tu peux seulement entendre :

des pas
une chaise
une porte

Question :

Que fait cette personne ?

Tu ne l'observes jamais directement.

Tu dois le déduire.

C'est exactement ce qu'est un Hidden Markov Model.

2. L'idée fondamentale

Le marché possède un état caché.

Tu n'observes jamais directement :

Trending

Mean Reverting

Random Walk

Structural Break

Tu observes seulement :

les prix
les rendements
la volatilité
les volumes

Autrement dit :

Le régime est caché.

Les observations sont visibles.

3. Structure d'un HMM

Un HMM possède deux couches.

Hidden States

Trending

Mean Reverting

Random Walk

↓

Observations

Price

Returns

Volatility

...

Le modèle tente de répondre :

Quel état caché explique le mieux les observations ?

4. Pourquoi est-il utilisé en finance ?

Parce que les marchés changent constamment de régime.

Par exemple :

Range

↓

Trending

↓

Transition

↓

Range

↓

Random Walk

Les HMM modélisent naturellement ces transitions.

5. Les probabilités de transition

C'est ce qui rend les HMM extraordinaires.

Le modèle apprend :

Trending

↓

Trending

95 %

Mais :

Trending

↓

Mean Reverting

4 %

Ou encore :

Trending

↓

Structural Break

1 %

Il ne dit donc pas :

Le marché EST en tendance.

Il dit :

Il y a 95 % de chances que le marché reste en tendance.

6. Les probabilités d'émission

Chaque état produit des observations.

Exemple :

Trending :

Persistence élevée

Stationarity faible

Momentum fort

Mean Reversion :

Persistence faible

Stationarity élevée

OU fort

Z élevé

Le modèle apprend automatiquement ces relations.

7. Pourquoi les fonds quantitatifs adorent les HMM

Parce qu'ils permettent :

la détection automatique des régimes ;
des transitions probabilistes ;
des changements progressifs ;
l'adaptation dynamique des modèles.

Ils sont très utilisés dans :

Asset Allocation
Volatility Trading
Statistical Arbitrage
CTA
Portfolio Management
8. Compatibilité avec IQIA

C'est là que les choses deviennent intéressantes.

Aujourd'hui :

Evidence

↓

Fusion

↓

Decision

Le HMM pourrait théoriquement remplacer :

Fusion +

Decision.

Mais...

9. Pourquoi je ne veux PAS remplacer IQIA

Ton architecture actuelle possède plusieurs avantages.

Elle est :

✔ explicable

✔ modulaire

✔ testable

✔ auditée

✔ déterministe

Un HMM :

❌ est plus difficile à expliquer.

Tu ne peux pas facilement dire :

Pourquoi ai-je obtenu Trending = 0.81 ?

Le modèle l'a appris.

10. Une énorme différence

IQIA aujourd'hui :

Stationarity

↓

Persistence

↓

Mean Reversion

↓

Fusion

↓

Decision

Chaque étape est visible.

Un HMM :

Observations

↓

Optimisation

↓

Hidden State

Tu perds une partie de la transparence.

11. Peut-on utiliser les deux ?

Oui.

Et je pense que c'est la meilleure idée.

Par exemple :

IQIA

↓

Behaviour

↓

Trending

En parallèle :

HMM

↓

Trending

88 %

Si les deux sont d'accord :

Très forte confiance.

Si les deux divergent :

Le Dashboard peut indiquer :

Scientific Conflict

Je trouve cette idée excellente.

12. Compatibilité avec Bayes

Très forte.

Les deux sont probabilistes.

Le HMM fournit déjà des probabilités.

Bayes peut les intégrer.

13. Compatibilité avec BOCPD

Très intéressante.

BOCPD détecte :

Un changement est-il en cours ?

Le HMM estime :

Dans quel état suis-je probablement ?

Les deux se complètent.

14. Compatibilité avec le Signal Engine

Je ne mettrais PAS le HMM dans le Signal Engine.

Pourquoi ?

Le HMM répond :

Quel régime domine ?

Pas :

Dois-je acheter maintenant ?
15. Les limites

Le HMM :

nécessite beaucoup de données ;
doit être entraîné ;
dépend des hypothèses choisies (nombre d'états, distributions, etc.) ;
est plus difficile à auditer qu'un pipeline déterministe.

Pour un projet qui vise la transparence comme IQIA, c'est un point majeur.

16. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Interprétabilité	⭐⭐⭐☆☆
Temps réel	⭐⭐⭐⭐☆
Compatibilité IQIA	⭐⭐⭐⭐☆