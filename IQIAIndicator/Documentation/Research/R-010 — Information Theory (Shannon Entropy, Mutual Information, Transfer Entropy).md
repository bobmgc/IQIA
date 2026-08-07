R-010 — Information Theory (Shannon Entropy, Mutual Information, Transfer Entropy)

Claude Shannon (1948)

Verdict avant l'étude

Je pense que cette théorie ne remplacera aucun des modèles que nous avons étudiés.

En revanche, elle pourrait devenir un filtre de qualité exceptionnel.

1. Pourquoi la théorie de l'information existe ?

Avant Shannon, on savait transmettre un message.

Mais personne ne savait mesurer :

Combien d'information contient ce message ?

Exemple.

Deux messages :

AAAAAAAAAAAAAAAAAA

et

X4J9Q7L2M8F6K1ZP3

Lequel contient le plus d'information ?

Intuitivement :

Le second.

Pourquoi ?

Parce qu'il est beaucoup moins prévisible.

C'est exactement ce que Shannon formalise.

2. L'idée fondamentale

La théorie de l'information mesure :

L'incertitude.

Plus quelque chose est prévisible :

↓

Moins il contient d'information.

Plus il est imprévisible :

↓

Plus il contient d'information.

3. Entropy

L'Entropy répond à une question simple :

Le marché est-il ordonné ou chaotique ?

Exemple.

Marché :

+1
+1
+1
+1
+1
+1

Très prévisible.

Entropy faible.

Marché :

+1
-2
+4
-3
+5
-4

Très imprévisible.

Entropy élevée.

4. Pourquoi est-ce intéressant ?

Aujourd'hui IQIA détecte :

Trending

Mean Reverting

Random Walk

Mais il ne répond pas directement à :

Le marché transporte-t-il suffisamment d'information pour justifier une décision ?

C'est différent.

5. Entropy vs Random Walk

Très important.

Beaucoup pensent :

Entropy élevée

=

Random Walk

C'est faux.

Un marché peut être :

très volatil ;
très informatif ;
mais parfaitement directionnel.

Inversement :

Un Random Walk possède souvent une entropie élevée, mais ce n'est pas une équivalence.

6. Mutual Information

Maintenant on monte d'un niveau.

Question.

Deux variables.

Par exemple :

Prix

↓

Volatilité

Combien d'information partagent-elles ?

C'est exactement ce que mesure la Mutual Information.

Pourquoi c'est énorme ?

Parce qu'elle détecte :

des relations

même NON linéaires.

Une corrélation de Pearson peut être proche de zéro.

La Mutual Information peut être très forte.

7. Transfer Entropy

Encore plus intéressant.

Question.

Est-ce que :

Variable A

↓

influence

↓

Variable B

Pas simplement :

sont corrélées.

Mais :

A apporte-t-elle de l'information sur le futur de B ?

En finance :

c'est extrêmement puissant.

8. Applications

La théorie de l'information est utilisée pour :

détection de régimes ;
filtrage du bruit ;
causalité ;
sélection de variables ;
machine learning ;
réseaux complexes.
9. Compatibilité avec IQIA

Très intéressante.

Je ne l'utiliserais pas pour générer un signal.

Je l'utiliserais comme filtre.

Par exemple :

Entropy

↓

Très élevée

↓

Signal dégradé

Pourquoi ?

Parce que le marché devient trop chaotique.

10. Mutual Information dans IQIA

Très intéressant.

Elle pourrait mesurer :

Kalman

↓

ZScore

Partagent-ils vraiment une information différente ?

Ou bien :

sont-ils redondants ?

Ça change tout.

11. Une découverte importante

Aujourd'hui on suppose que :

OU

+

Kalman

+

Dynamic Z

apportent trois évidences.

Mais...

Et s'ils racontaient quasiment la même chose ?

Alors :

Le SPRT compterait trois fois la même preuve.

C'est mauvais.

La Mutual Information permet justement de mesurer cette redondance.

12. Transfer Entropy

Je pense qu'elle sera très utile plus tard.

Par exemple :

Volatilité

↓

Influence

↓

Momentum

Ou :

Order Flow

↓

Influence

↓

Prix

Pour IQIA V1 :

Je la garderais de côté.

13. Les limites

La théorie de l'information :

demande beaucoup de données ;
est coûteuse ;
peut être difficile à estimer correctement.

Certaines mesures sont aussi sensibles au choix des estimateurs.

14. Compatibilité avec le Signal Engine

Je ne la mettrais pas au cœur.

Je la placerais ici :

Evidence Providers

↓

Information Quality

↓

Bayes

↓

SPRT

Elle mesure :

La qualité des évidences.

Pas leur direction.

15. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐☆☆
Compatibilité IQIA	⭐⭐⭐⭐☆