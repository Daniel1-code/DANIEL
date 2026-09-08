# WALL-02 — Les limites du voile, et ce que le moteur doit dire

## 1. Énoncé

Cette fiche ne valide pas une formule de plus. Elle vérifie que le module **annonce** les
quatre situations qu'un moteur de voile ne doit jamais traiter en silence.

| Cas | Question posée |
|---|---|
| a | Un élément trop court est-il renvoyé vers le module Poteau ? |
| b | Un retour de voile trop éloigné est-il compté comme raidisseur ? |
| c | Un retour proche fait-il vraiment disparaître le second ordre ? |
| d | La flexion dans le plan est-elle annoncée comme insuffisante en zone sismique ? |

Matériaux communs : C25/30, B500, XC1, N = 400 kN/m, φ_ef = 2,0.

## 2. Calcul manuel

### 2.a Élément de 600 × 200 — ce n'est pas un voile

```
L / t = 600 / 200 = 3,0  <  4
```

L'article 9.6.1 définit un voile par **longueur ≥ 4 × épaisseur**. En deçà, l'Eurocode dit
que c'est un poteau, et ce sont les dispositions de l'**article 9.5** qui s'appliquent :
A_s,min liée à N_Ed et non à A_c, nombre minimal de barres, cadres avec zones critiques,
maintien des barres comprimées.

Calculer un tel élément avec les règles de l'article 9.6 donnerait un ferraillage
**réglementairement faux**, et rien dans le résultat ne le signalerait. Le moteur renvoie
donc explicitement vers le module Column.

### 2.b Voile de 15,00 m avec un seul retour — le retour ne sert à rien

Le moteur applique l'article 12.6.5.1, mais avec une réserve que l'article suppose sans
l'écrire : un maintien sur une rive verticale ne raidit le voile que **près de cette rive**.

```
h = 3 000 mm      b = 15 000 mm  =  5,0 h
```

Au milieu d'un voile de 15 m, la partie courante ne « sait pas » que ses extrémités sont
tenues : elle flambe comme un voile tenu seulement en tête et en pied. Le moteur calcule
β mais signale que le résultat n'est représentatif que près des rives, au lieu de rendre
un β flatteur sur toute la longueur.

Le seuil retenu est **b > 3 h**.

### 2.c Voile de 200 mm entre quatre rives à 4,00 m

```
beta = 1 / (1 + (h/b)²) = 1 / (1 + (3 000/4 000)²) = 1 / 1,5625 = 0,640
l_0 = 0,640 × 3 000 = 1 920 mm
lambda = 1 920 / 57,74 = 33,3      contre 52,0 sans retour
lambda_lim = 30,3
```

**33,3 > 30,3 : le second ordre reste obligatoire.** L'intuition « un retour de voile
supprime le second ordre » est fausse ici — il le divise par plus de deux, sans le faire
disparaître.

Contre-épreuve, un voile plus épais et moins haut :

```
t = 300 mm, h = 2 500 mm, b = 4 000 mm
beta = 1 / (1 + (2 500/4 000)²) = 0,719   →  l_0 = 1 798 mm
i = 300/√12 = 86,60   →  lambda = 20,8
n = 400 000 / (1 000 × 300 × 16,667) = 0,080
lambda_lim = 20 × 0,714 × 1,077 × 0,7 / √0,080 = 37,2

20,8 < 37,2   →  le second ordre disparaît réellement
```

### 2.d Voile de contreventement 250 × 5 000 — traction de rive

Modèle simplifié à deux zones de rive, bras de levier z = 0,8 l_w = 4 000 mm :

```
N total = 400 kN/m × 5,00 m = 2 000 kN
Traction de rive = M/z − N/2
```

| M dans le plan | M/z | N/2 | Traction | A_s de rive |
|---|---|---|---|---|
| 2 500 kN·m | 625 kN | 1 000 kN | **−375 kN** | 0 (tout comprimé) |
| 6 000 kN·m | 1 500 kN | 1 000 kN | 500 kN | 1 150 mm² |
| 8 000 kN·m (N = 500 kN) | 2 000 kN | 250 kN | 1 750 kN | 4 025 mm² |
| 8 000 kN·m (N = 2 000 kN) | 2 000 kN | 1 000 kN | 1 000 kN | 2 300 kN → 2 300 mm² |

Deux enseignements :
- Il faut dépasser **M = N z / 2** pour que la rive se tende. En deçà, la compression
  suffit et le voile n'a pas besoin de barres de rive — le moteur ne doit pas en inventer.
- **Plus le voile est comprimé, moins sa rive est tendue.** C'est physique, et le moteur
  le reproduit.

Mais ce modèle reste grossier. Pour un voile de contreventement en zone sismique, l'EN
1998-1 impose des **éléments de rive confinés** : longueur de la zone confinée, cadres
fermés rapprochés, vérification de la déformation ultime du béton confiné. Rien de cela
n'est dimensionné ici, et le moteur le dit dans un avertissement systématique.

## 3. Résultat moteur

`Wall02SlenderTests`, 10 cas.

| Cas | Attendu | Moteur |
|---|---|---|
| 600 × 200 | avertissement « POTEAU », renvoi vers 9.5 et le module Column | conforme |
| Retour à 15 m | `LateralRestraintEffective` faux, avertissement « trop éloigné » | conforme |
| Quatre rives à 4 m | β = 0,640, λ = 33,3, second ordre **toujours** requis | conforme |
| Idem | e_2 et M_Ed plus faibles qu'avec β = 1,00 | conforme |
| 300 mm, 4 rives, h = 2,50 m | λ = 20,8, second ordre négligé, clause 5.8.3.1 citée | conforme |
| Console | β = 2,00, l_0 = 6 000 mm, second ordre requis | conforme |
| h/t = 41,7 | avertissement d'élancement géométrique | conforme |
| 140 mm sous 1 200 kN/m | soit échec nommant « épaissir », soit ferraillage bien au-dessus du minimum | conforme |
| V dans le plan | les aciers horizontaux tiennent lieu de cadres, modèle console cité | conforme |
| M dans le plan = 6 000 | A_s de rive ≈ 1 150 mm², barres de rive posées, avertissement EN 1998-1 | conforme |
| Compression forte vs faible | la rive tendue diminue quand N augmente | conforme |

## 4. Comparaison

Deux cas de cette fiche ont d'abord été écrits avec une **attente fausse**, corrigée après
confrontation au calcul manuel :

1. « Un retour sur quatre rives supprime le second ordre » — faux : λ tombe de 52,0 à 33,3,
   mais la limite vaut 30,3.
2. « Un moment de 2 500 kN·m tend la rive » — faux : il faut dépasser N z / 2 = 4 000 kN·m.

Dans les deux cas c'est le test qui a été corrigé, pas le moteur. C'est exactement ce à
quoi servent les fiches de validation : sans le calcul manuel, ces deux erreurs seraient
passées pour des vérités.

## 5. Conclusion

Le module dit ce qu'il fait et ce qu'il ne fait pas. Il refuse de calculer un poteau avec
les règles du voile, il ne compte pas un raidisseur qui ne raidit rien, il ne fabrique pas
de barres de rive quand la section est toute comprimée, et il annonce que son modèle de
contreventement ne suffit pas en zone sismique.

## 6. Limites connues

- **Aucun élément de rive confiné** (EN 1998-1 §5.4.3.4). Un voile de contreventement en
  zone sismique n'est pas dimensionné par ce module.
- **Les ouvertures sont ignorées** : ni trumeaux, ni linteaux, ni chaînages.
- **Le modèle de flexion dans le plan est à deux zones de rive**, pas une analyse de
  section. Il ne dit rien de la profondeur de l'axe neutre ni du confinement nécessaire.
- **Pas de vérification au feu**, alors qu'elle gouverne souvent l'épaisseur minimale.
- **Pas de voile courbe** : le lecteur Revit les écarte explicitement.
- **Rien n'a encore été exécuté dans Revit.**
