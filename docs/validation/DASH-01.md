# DASH-01 — Le tableau de bord de projet

**Module** : synthèse, toutes familles
**Tests** : `tests/DanCI.Structural.Tests/Documentation/ProjectDashboardTests.cs` (24 cas)
**Version** : 3.15.0

---

## Le pivot qui manquait

Les huit modules produisent chacun leur type de résultat, et c'est normal : un poteau et une
semelle n'ont pas les mêmes grandeurs. Mais ils produisent tous **la même chose au fond** —
une liste de vérifications, un plan de ferraillage, un statut — et c'est cela, et cela seul,
dont un tableau de bord, un mode batch ou une couleur de contrôle ont besoin.

`DesignedElement` est cette vue commune. Elle ne remplace aucun résultat de module : elle en
est l'adaptation.

### Le risque, et comment il est fermé

La vue **recalcule** le statut. Deux définitions du mot « conforme » qui divergeraient
seraient pires qu'une seule imparfaite. Un test prend donc deux volées **réelles** — une à
150 mm qui échoue en flèche, une à 220 mm qui passe — et vérifie que la vue retrouve
exactement le statut, le taux maximal et le drapeau de défaut du module.

---

## Les trois questions, dans l'ordre où l'on décide

### 1. Qu'est-ce qui ne passe pas

Les éléments triés du plus chargé au moins chargé. **À taux égal, l'élément en défaut passe
devant** : c'est lui qu'on veut lire.

Un lot n'est livrable que si **aucun** élément n'est non conforme et qu'aucun n'a échoué. Un
projet n'est pas conforme « à 97 % ». Et **un lot vide ne l'est pas non plus** : zéro non
conforme sur zéro élément n'est pas une bonne nouvelle, c'est une absence de vérification.

### 2. Pourquoi

Les articles en défaut, classés par **nombre d'éléments touchés**.

C'est la ligne la plus utile du tableau. Sur un projet de deux cents éléments :

```
Article                          Vérification             Elem.     Pire
------------------------------------------------------------------------
EN 1992-1-1 art. 7.4.2           Fleche par l'elancem…       18     1,67
EN 1992-1-1 art. 6.1             Flexion composee             1     3,00
```

« 18 éléments en défaut de flèche » dit où est le problème de conception ; la liste des 18
noms, non. Un défaut isolé est souvent une erreur de saisie ; **un article qui tombe
dix-huit fois est une hypothèse de projet à revoir**.

Deux détails qui font que ce classement mesure quelque chose :

- un élément qui échoue **deux fois sur le même article** — une semelle en tranchant dans
  les deux directions — ne compte qu'**une** fois dans les effectifs, mais le taux retenu
  est le pire des deux ;
- le **même numéro d'article dans deux normes** fait deux problèmes distincts (§ 5.8.2 de
  l'EN 1992-1-1 et de l'EN 1998-1 n'ont rien à voir).

Le classement mesure **l'étendue** du problème, pas sa pointe : l'article isolé à 3,00 vient
après celui qui tombe dix-huit fois à 1,67.

### 3. Combien

Acier, béton et **ratio kg/m³ par famille**. Un poteau courant tourne autour de 120–180
kg/m³, une dalle autour de 70–100. Un ratio hors de ces ordres de grandeur ne prouve pas une
erreur, mais il en signale souvent une.

---

## Ce que le tableau de bord ne fait pas

**Il ne moyenne aucun taux de travail**, ni globalement, ni par famille.

Un élément dont neuf vérifications passent à 0,30 et la dixième échoue à 2,90 n'est pas « à
0,56 en moyenne » : il est en défaut. Moyenner des taux de travail est la façon la plus
simple de rendre un projet **dangereux et rassurant** en même temps.

Un test le verrouille **par réflexion** sur les quatre types publics : aucun membre ne peut
s'appeler `Average`, `Moyenne` ni `Mean`. Ce n'est pas de la paranoïa — c'est la propriété
qu'une prochaine session « améliorerait » sans y penser.

Les vérifications **Sans objet** n'entrent pas dans le taux maximal, dans les deux sens : ni
pour le tirer vers le bas, ni — plus dangereux — pour le tirer vers le haut si l'une d'elles
était mal remplie.

---

## La note de synthèse

`CalculationReport.BuildProject` rend tout cela en texte. Elle **ne remplace aucune note
d'élément** et le dit explicitement : elle dit où regarder, pas pourquoi une section passe.
Un tableau qui se ferait passer pour une justification serait pire qu'utile.

Un test lit la note produite et vérifie qu'elle ne contient nulle part le mot « moyen ».

---

## Limites

- La vue uniforme est construite **par l'appelant**, qui sait de quel module vient le
  résultat. Rien n'empêche un appelant de mal renseigner `Kind` : ce n'est pas vérifiable ici.
- Les ordres de grandeur de ratio cités plus haut sont de la **pratique**, pas de la norme.
  Le moteur ne les applique pas et ne signale rien automatiquement.
- Le **mode batch** au sens de la feuille de route — parcourir un modèle Revit entier et
  dispatcher chaque élément vers son module — n'est pas fait. L'agrégation qu'il produirait
  l'est ; la sélection et le dispatch dépendent de Revit et ne se valident que dedans.
