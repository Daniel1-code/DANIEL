# GRADE-02 — Ce qui distingue une longrine d'une poutre posée bas

## 1. Énoncé

Cette fiche ne rejoue pas le calcul de GRADE-01. Elle vérifie les points sur lesquels une
longrine se comporte **autrement** qu'une poutre, et que le module doit traiter
différemment.

| Cas | Question posée |
|---|---|
| a | Hors zone sismique, un effort de liaison est-il inventé ? |
| b | Un effort de liaison imposé à la main est-il tracé comme tel ? |
| c | Le minimum sismique gouverne-t-il réellement les deux nappes ? |
| d | Une section trop petite est-elle refusée par l'article 5.8.1(4) ? |
| e | L'appui du sol est-il jamais crédité ? |
| f | Le rocher exige-t-il quand même une liaison ? |

Section commune : 300 × 500, portée 5,00 m, C25/30, B500, XC2, charge de mur 40 kN/m.

## 2. Calcul manuel

### 2.a Hors séisme — aucun effort de liaison

**Aucun article de l'EN 1992-1-1 n'impose d'effort de liaison entre semelles.** L'effort
de GRADE-01 vient de l'EN 1998-5 ; l'appliquer à un projet non sismique reviendrait à
importer silencieusement une règle d'un autre code.

Le moteur rend donc N = 0, ne produit pas la vérification en compression, et écrit
pourquoi — en renvoyant à l'**EN 1991-1-7** si le projet exige un chaînage au titre de la
robustesse. Dire « il n'y en a pas et voici où chercher » vaut mieux que d'appliquer une
valeur par défaut que personne ne pourra justifier.

### 2.b Effort imposé par l'utilisateur

Un ingénieur peut vouloir imposer N = 80 kN au titre de la robustesse ou d'un cahier des
charges. Le moteur l'accepte, dimensionne avec, fait apparaître la vérification en
compression — et **note que la valeur est imposée par l'utilisateur et qu'aucun article ne
la fonde**. La traçabilité de l'origine d'une donnée fait partie du calcul.

### 2.c Le minimum sismique contre le minimum EC2

```
EN 1998-1 5.8.2(5) :  0,004 × 300 × 500                = 600 mm² par nappe
EN 1992-1-1 9.2.1.1 : 0,26 × 2,565/500 × 300 × 442     = 177 mm²
```

Facteur **3,4**, et il porte sur la nappe supérieure, celle qu'un calcul de poutre
isostatique laisserait au minimum de construction. Les deux réglages ne diffèrent que par
`SeismicDesign`, et la section d'acier réellement posée en haut change en conséquence.

### 2.d Section minimale — EN 1998-1 art. 5.8.1(4)

```
Bâtiment ≤ 3 niveaux :  250 mm × 400 mm minimum
Bâtiment ≥ 4 niveaux :  250 mm × 500 mm minimum
```

Une section 200 × 350 est signalée, avec les deux valeurs manquantes nommées. Et une
longrine de 450 mm de hauteur **passe à trois niveaux et ne passe plus à six** : c'est le
nombre de niveaux, pas la seule géométrie, qui décide.

Le moteur avertit et poursuit le calcul : la section reste calculable, elle n'est
simplement pas conforme, et masquer le résultat n'aiderait pas à corriger le modèle.

### 2.e L'appui du sol n'est jamais crédité

Une longrine posée sur le sol ne porte presque rien : le sol reprend la charge sous elle.
La tentation est de diviser le moment par un facteur, ou de la traiter en poutre sur appui
continu.

Le moteur **calcule le même moment dans les deux cas**, suspendue ou posée. C'est
sécuritaire, et il l'assume en note : le crédit de l'appui du sol demanderait une analyse
de poutre sur sol élastique qu'il ne fait pas.

Il ajoute une vérification que le cas « suspendue » n'a pas : la contrainte de contact.

```
Longrine 250 × 500 sous 120 kN/m, sol à 150 kPa :
sigma = (120 + 1,35 × 3,125) / 0,250 = 497 kPa   ≫ 150 kPa
```

L'hypothèse « posée sur le sol » est alors matériellement impossible — le sol ne peut pas
reprendre cette charge sur cette largeur — et l'avertissement le dit.

Symétriquement, une longrine déclarée suspendue rappelle la règle de terrain qui va avec :
sur sol gonflant, **le vide sanitaire ou la forme perdue doivent réellement exister** sous
la longrine, faute de quoi le gonflement la sollicite dans l'autre sens.

### 2.f Rocher — pas de liaison, mais le minimum reste dû

```
Sol de classe A  →  epsilon = 0  →  N = 0
```

L'EN 1998-5 n'exige aucune liaison sur rocher : les deux semelles s'y déplacent ensemble.
Le moteur rend donc N = 0 même avec des poteaux à 1 500 kN — mais **le minimum de 0,4 % de
l'article 5.8.2(5) reste dû**, lui, et les 600 mm² par nappe sont toujours posés. Deux
règles d'un même code, deux conditions d'application différentes.

### 2.g Travées continues — les coefficients sont annoncés

Une longrine continue est calculée avec les coefficients de pratique courante
(w L²/11, w L²/9, …), qui **ne sont pas des valeurs de l'Eurocode 2**. Le moteur le dit
deux fois, en note et en avertissement, exactement comme le module Dalle.

## 3. Résultat moteur

`Grade02TieTests`, 11 cas.

| Cas | Attendu | Moteur |
|---|---|---|
| a | N = 0, pas de vérification en compression, note EN 1992 / EN 1991-1-7 | conforme |
| b | N = 80 kN, origine tracée, vérification produite | conforme |
| c | 600 mm² sismique contre < 250 mm² sans séisme | conforme |
| d | 200 × 350 signalée avec 250 et 400 nommés | conforme |
| d′ | 450 mm : muette à 3 niveaux, signalée à 6 | conforme |
| e | moment identique suspendue / posée + note « sol élastique » | conforme |
| e′ | contrainte de contact 497 kPa signalée | conforme |
| e″ | rappel du sol gonflant et de la forme perdue | conforme |
| f | N = 0 sur rocher, minimum de 600 mm² maintenu | conforme |
| g | coefficients de continuité annoncés hors Eurocode | conforme |

## 4. Comparaison

Rien à comparer numériquement dans la plupart de ces cas : ils portent sur ce que le
moteur **refuse** de faire. C'est délibéré. Les erreurs les plus coûteuses sur une longrine
ne sont pas des erreurs d'arithmétique, ce sont des hypothèses appliquées hors de leur
domaine — une règle sismique dans un projet qui n'en relève pas, ou un appui du sol
supposé sans l'avoir vérifié.

## 5. Conclusion

Le module traite la longrine pour ce qu'elle est : un tirant qui relie des semelles. Il
n'invente pas d'effort là où aucun code n'en impose, il applique le minimum sismique aux
deux nappes, il refuse la section non conforme, et il ne prend jamais crédit de ce sur
quoi la longrine repose sans l'analyse qui le justifierait.

## 6. Limites connues

- **Pas de diagramme d'interaction M-N** : la traction est répartie à parts égales entre
  les nappes, méthode manuelle sécuritaire pour une traction faible.
- **La contrainte de contact est signalée, pas exploitée** : le moteur ne redimensionne
  pas la largeur pour la ramener sous la portance admissible.
- **Aucune analyse de sol gonflant** : la note rappelle la règle, elle ne calcule pas la
  pression de gonflement.
- **Coefficients de continuité hors Eurocode**, comme pour la dalle, annoncés en toutes
  lettres.
- **Rien n'a encore été exécuté dans Revit.**
