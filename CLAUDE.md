# Instructions Claude Code — IQIA

## Langue

Claude doit communiquer exclusivement en français avec l'utilisateur.

Cela concerne :
- les réponses ;
- les explications ;
- les analyses ;
- les rapports ;
- les résumés ;
- les messages de progression ;
- les explications concernant le code.

Règle générale : toute phrase destinée à l'utilisateur doit être rédigée en français naturel, clair et complet.
Claude ne doit pas basculer en anglais par habitude, même lorsque le contexte technique, les outils, les logs, les commandes ou les noms de concepts sont en anglais.

Claude doit notamment utiliser le français pour :
- les salutations ;
- les demandes de précision ;
- les plans d'action ;
- les comptes rendus d'exécution ;
- les conclusions finales ;
- les revues de code ;
- les diagnostics ;
- les recommandations ;
- les avertissements ;
- les explications d'erreurs, de tests, de builds et de commandes.

Lorsque Claude cite un élément qui doit rester en anglais selon les exceptions ci-dessous, il doit entourer cet élément d'une explication en français.
Si un message d'erreur, un log ou une sortie de commande est en anglais, Claude peut le citer tel quel, mais son interprétation, son résumé et les actions proposées doivent rester en français.
Si l'utilisateur écrit dans une autre langue, Claude doit quand même répondre en français, sauf demande explicite de l'utilisateur d'utiliser une autre langue.

### Exceptions

Conserver l'anglais lorsque c'est nécessaire pour :
- le code ;
- les noms de variables ;
- les noms de fonctions ;
- les noms de classes ;
- les noms de fichiers ;
- les commandes CLI ;
- les API ;
- les bibliothèques ;
- les messages d'erreur exacts ;
- les termes techniques pour lesquels la traduction serait ambiguë.
