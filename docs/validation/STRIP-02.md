# STRIP-02 — Ce qui distingue une semelle filante d'une semelle isolée

## 1. Énoncé

Cette fiche ne rejoue pas le calcul de STRIP-01. Elle vérifie les quatre points sur
lesquels une semelle filante se comporte **autrement** qu'une semelle isolée, et que le
module doit traiter différemment.

| Cas | Question posée |
|---|---|
| a | Une semelle large et mince est-elle signalée comme n'étant plus rigide ? |
| b | Un débord long fait-il revenir l'effort tranchant ? |
| c | L'excentrement transversal est-il traité, et le décollement refusé ? |
| d | Le poinçonnement est-il *toujours* sans objet, quelle que soit la géométrie ? |

Matériaux communs : C25/30, B500, XC2 sur béton de propreté, sol à 250 kPa, N = 200 kN/m.

## 2. Calcul manuel

### 2.a Semelle 1800 × 300 — elle n'est plus rigide

```
Débord a = (1 800 − 200)/2 = 800 mm       épaisseur h = 300 mm
a / h = 2,67  >  2
```

Au-delà d'un débord de deux fois l'épaisseur, la semelle fléchit assez pour que la
**répartition linéaire des contraintes** cesse d'être vraie : le sol se charge davantage
sous le voile et moins aux extrémités. Le modèle de console encastrée surestime alors le
moment aux bords et sous-estime la contrainte au centre.

Le moteur ne refuse pas de calculer — le résultat reste conservatif en flexion — mais il
signale que l'hypothèse est sortie de son domaine et propose l'action utile : épaissir.

### 2.b Le même cas fait revenir l'effort tranchant

```
d = 300 − 40 − 12/2 = 254 mm
Section à d du nu :  a − d = 800 − 254 = 546 mm   →  bien à l'intérieur de la semelle
```

Contrairement à STRIP-01 où la section tombait hors de la semelle, elle existe ici et la
vérification est produite. C'est le débord, pas la nature de l'élément, qui décide.

### 2.c Excentrement — semelle 1200 × 400, M = 20 kN·m/m, H = 25 kN/m

L'effort horizontal crée un moment supplémentaire sur la hauteur de la semelle :

```
N total = 200 + 1,20 × 0,40 × 25 = 212 kN/m
M base  = 20 + 25 × 0,40         = 30 kN·m/m
e       = 30 / 212               = 0,1415 m = 142 mm
B/6     = 1 200 / 6              = 200 mm      →  e < B/6, dans le noyau  ✓

B' = 1 200 − 2 × 142 = 916 mm    (Meyerhof, annexe D)
Glissement : R_d = 212 × tan(30°)/1,25 = 98 kN/m   contre 25 kN/m   ✓
```

**Décollement** — semelle 1000 × 400, M = 80 kN·m/m :

```
N total = 200 + 1,00 × 0,40 × 25 = 210 kN/m
e = 80 / 210 = 381 mm    contre B/6 = 167 mm      →  la semelle décolle
```

Le moteur passe la vérification de non-soulèvement en **Échec** et l'état cesse d'être
« OK ». Il ne redistribue pas les contraintes sur la partie encore comprimée : une
semelle qui décolle se redimensionne, elle ne se ferraille pas.

### 2.d Poinçonnement — jamais applicable

Testé sur trois largeurs (900, 1500, 2400 mm) : la vérification reste *Sans objet* dans
tous les cas, avec un taux nul qui n'entre pas dans le maximum.

La raison est physique et ne dépend pas de la géométrie : sous un voile, la charge arrive
répartie. Le commentaire rappelle néanmoins le cas où cette conclusion serait fausse —
si des **poteaux** prennent appui sur la même semelle, leur charge est concentrée et
poinçonne. Le lecteur Revit détecte cette situation et la signale ; le moteur ne la
calcule pas.

## 3. Résultat moteur

`Strip02EccentricTests`, 11 cas.

| Cas | Attendu | Moteur |
|---|---|---|
| 1800 × 300 | avertissement « cesse d'être rigide », action « épaissir » | conforme |
| 1800 × 300 | vérification d'effort tranchant produite | conforme |
| 900 × 400 | aucune vérification d'effort tranchant | conforme |
| e avec H | e = 142 mm, dans le noyau, B' = 916 mm | conforme |
| Glissement | R_d ≈ 98 kN/m, Pass | conforme |
| M = 80 kN·m/m | hors noyau, non-soulèvement en Échec, état ≠ OK | conforme |
| 900 / 1500 / 2400 | poinçonnement *Sans objet*, mention des poteaux | conforme |
| Taux maximal | les vérifications sans objet portent 0 et sont exclues | conforme |
| Ancrage 900 mm | taux > 1, crochet posé, renvoi à 9.8.2.2 | conforme |
| α₁ avec HA10 | 40 mm > 3 × 10 → α₁ = 0,70 appliqué | conforme |
| Coulée contre le sol | enrobage relevé à 75 mm | conforme |

## 4. Comparaison

Une attente de test a dû être corrigée après confrontation au calcul : le taux de travail
maximal de la semelle de référence n'est pas inférieur à 1, il vaut **1,82**, porté par la
vérification d'ancrage. Affirmer le contraire revenait à supposer que toute vérification
en Avertissement reste sous l'unité, ce qui est faux et masquerait précisément le
problème que le module est censé révéler.

Le test vérifie désormais ce qui compte réellement : que les vérifications *Sans objet*
portent un taux nul et sont exclues du maximum.

## 5. Conclusion

Le module traite la semelle filante pour ce qu'elle est, et non comme une semelle isolée
allongée : il refuse le poinçonnement, produit ou non l'effort tranchant selon le débord,
signale la perte de rigidité, et refuse de ferrailler une semelle qui décolle.

## 6. Limites connues

- **Semelle souple non calculée.** Au-delà de a > 2h le moteur signale et continue avec
  le modèle rigide, conservatif en flexion ; il ne fait aucune analyse d'interaction
  sol-structure.
- **Décollement refusé, pas redistribué.** La distribution triangulaire sur la surface
  encore comprimée n'est pas calculée.
- **Poteaux portés non traités**, comme en STRIP-01.
- **Pas de semelle filante sous poteaux alignés** (semelle-poutre) : la flexion
  longitudinale entre poteaux n'est pas modélisée.
- **Pas de tassement différentiel**, alors que c'est le mode de défaillance le plus
  fréquent d'une semelle filante longue.
- **Rien n'a encore été exécuté dans Revit.**
