# DASH-03 — Le mode batch et les couleurs dans Revit

**Module** : synthèse, toutes familles
**Tests** : `tests/DanCI.Structural.Tests/Documentation/BatchClassificationTests.cs` (16 cas)
**Version** : 3.16.0

---

## La seule question du mode batch

Le batch n'a qu'une décision à prendre, et c'est la seule où il peut se tromper
**gravement** : *à quel module un élément appartient-il ?*

Calculer une longrine comme une poutre, ou une semelle filante comme une semelle isolée,
produirait un résultat d'apparence normale et faux — exactement ce que le moteur refuse de
faire partout ailleurs.

**Le classeur ne devine donc rien.** Il classe ce que la catégorie désigne sans ambiguïté et
**refuse le reste en disant pourquoi**. Un élément écarté sans raison est un élément perdu :
l'ingénieur ne saura pas qu'il doit le reprendre à la main. Un test exige que chaque refus
dépasse quarante caractères — une raison doit expliquer, pas étiqueter.

| Catégorie Revit | Module | |
|---|---|---|
| Poteaux porteurs | Poteau | ✔ |
| Ossature structurelle | Poutre | ✔ *(voir plus bas)* |
| Sols | Dalle | ✔ *(voir plus bas)* |
| Murs | Voile | ✔ |
| Escaliers | Escalier | ✔ |
| Fondation, instance de famille | Semelle isolée | ✔ |
| Fondation, sous un mur | Semelle filante | ✔ |
| Fondation, radier | — | **refusé** |
| Fondation, forme illisible | — | **refusé** |

**Revit range la semelle isolée, la semelle filante et le radier sous la même catégorie.**
La forme les distingue quand elle est lisible ; sinon le classement est refusé, parce que
l'une poinçonne et l'autre non. Le radier n'est approché par aucun module : le moteur ne
traite pas la fondation continue en deux directions, et la ramener à une dalle ignorerait la
réaction du sol.

### Deux ambiguïtés qui ne se résolvent pas — donc qui se disent

Elles sont rappelées à **chaque** lot, dans la note :

- **La longrine est une ossature structurelle**, exactement comme une poutre. Rien dans la
  catégorie ne les distingue, et le niveau ne suffit pas : une poutre de soubassement n'est
  pas une longrine. Le batch la classe en poutre — le moins pire — mais **l'effort de
  liaison de l'EN 1998-5 § 5.4.1.2(7) et le minimum de 0,4 % sur les deux nappes ne sont
  alors pas vérifiés**.
- **Un plancher incliné modélisant une paillasse est classé en dalle.** Calculé à plat, son
  poids propre est sous-estimé de plus de 40 %.

---

## Deux refus assumés

### Le batch ne pose aucune armature

Un lot entier ferraillé en une commande, sans qu'on ait regardé une seule coupe, est
exactement la façon dont un modèle se remplit de barres que personne n'a validées. Le batch
**vérifie** ; le ferraillage se pose module par module, après lecture.

### Le batch n'invente aucune charge

La charge d'exploitation d'un plancher, la classe d'exposition, la contrainte admissible du
sol ne se devinent pas. Le batch ne traite donc **que les familles dont la fenêtre a été
ouverte et validée cette session** ; les autres sont nommées, avec le nom de la fenêtre à
ouvrir.

Rien n'est persisté entre deux sessions Revit : des réglages vieux d'une semaine
ressembleraient à des réglages voulus.

C'est la règle habituelle du moteur — quand une donnée manque, on le dit — mais elle compte
davantage ici : calculer un projet entier avec des charges par défaut produirait un résultat
faux **sur tout le lot d'un seul coup**.

---

## La portée est la vue active

Pas le modèle entier. Sur un projet réel, « tout le modèle » comprend les étages qu'on ne
regarde pas, les variantes et les éléments de référence. **Un lot qu'on n'a pas choisi n'est
pas un lot qu'on peut vérifier.**

---

## Les couleurs dans Revit : trois règles non négociables

1. **La coloration est propre à la vue.** Elle passe par les remplacements graphiques de la
   vue active, jamais par les matériaux ni par les paramètres des éléments. Le modèle n'est
   pas modifié : changez de vue et les couleurs disparaissent.
2. **Elle est réversible**, et le retrait est livré avec la pose. Une coloration qu'on ne
   sait pas défaire dégrade le modèle au lieu de l'éclairer.
3. **Elle refuse une vue qui ne l'accepte pas.** Un gabarit, ou une vue dont les
   remplacements sont pilotés par un gabarit, les rejette silencieusement : poser sans
   vérifier laisserait croire que la vue est colorée alors qu'elle ne l'est pas — pire que
   ne rien poser.

Le motif de remplissage plein est cherché **sur sa propriété**, pas sur son nom : *Solid
fill* devient *Remplissage plein* en français, et un motif trouvé par son nom ne serait
trouvé que dans une seule langue.

### Le plan de couleurs, testé sans Revit

Les éléments sont regroupés **par bande** — c'est ainsi qu'une vue se colore, par
remplacement appliqué à beaucoup d'éléments. Une bande sans élément ne produit aucun groupe :
inutile d'encombrer une vue d'un remplacement qui ne s'applique à rien. Les groupes suivent
l'ordre de gravité, du gris au rouge ; une légende qui ne le suit pas se lit deux fois.

**Un élément sans identifiant est compté, pas oublié.** On ne peut pas colorer ce qu'on ne
sait pas désigner, mais un élément absent de la vue colorée doit s'expliquer — sinon la
couleur ment par omission.

---

## Limites — et celle-ci est la principale

**Rien de la couche Revit n'a été exécuté dans Revit.** Ces tests valident la règle de
classement et le plan de couleurs, qui sont du code pur. Ils ne peuvent pas vérifier :

- que la traduction catégorie Revit → catégorie structurale désigne les bons éléments ;
- que la distinction des trois fondations par leur type .NET tient sur un modèle réel ;
- que les remplacements graphiques se posent et se retirent comme prévu ;
- que la collecte par vue ramène ce que l'utilisateur croit voir.

C'est la même limite que pour l'escalier en 3.10.0, et elle a déjà coûté une fois :
**une lecture de modèle ne se valide que dans le modèle.**

Autres limites déclarées :

- les longrines ne sont jamais calculées par le batch ;
- le batch ne relit pas les armatures déjà posées : il recalcule, il ne compare pas ;
- les réglages sont ceux de la session, donc un lot relancé après redémarrage de Revit
  redemande de passer par les fenêtres.
