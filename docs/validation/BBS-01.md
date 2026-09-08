# BBS-01 — Façonnage et carnet de ferraillage

## 1. Énoncé

Le carnet de ferraillage est ce qui part chez le façonnier. Deux choses le distinguent d'un
simple quantitatif, et le moteur ne faisait ni l'une ni l'autre.

| | Ce qui était fait | Ce qui est fait |
|---|---|---|
| Longueur | somme des segments, d'angle à angle | **longueur de coupe**, plis déduits (EC2 §8.3) |
| Repère | un numéro par élément | un repère par **forme façonnée**, partagé par tout le lot |

## 2. Calcul manuel

### 2.1 Le diamètre de mandrin — art. 8.3 et tableau 8.1N

```
φ ≤ 16 mm  →  φ_m = 4 φ
φ > 16 mm  →  φ_m = 7 φ
```

| φ | φ_m | Rayon de fibre moyenne R = (φ_m + φ)/2 |
|---|---|---|
| 8 | 32 | 20,0 mm |
| 12 | 48 | 30,0 mm |
| 16 | 64 | 40,0 mm |
| 20 | **140** | 80,0 mm |
| 25 | 175 | 100,0 mm |

Le saut se fait **au-dessus** de 16 mm : le HA16 est encore à 4φ. Le passage de 4φ à 7φ
double presque le rayon d'un coup, et c'est ce qui rend la déduction d'un HA20 si visible.

### 2.2 L'autre mandrin, celui de l'expression (8.1) — et il ne faut pas les confondre

Le tableau 8.1N protège **l'acier** contre l'endommagement au pliage. L'article 8.3(3)
protège le **béton** contre l'écrasement à l'intérieur du coude, et il peut exiger bien
davantage :

```
φ_m,min ≥ F_bt (1/a_b + 1/(2φ)) / f_cd

F_bt = 50 kN, a_b = 60 mm, φ = 16 mm, f_cd = 16,667 MPa (C25/30)
φ_m,min = 50 000 × (1/60 + 1/32) / 16,667 = 143,7 mm
```

**143,7 mm contre 64 mm au tableau** : plus du double. L'article dispense de cette
vérification si l'ancrage au-delà du pli ne dépasse pas 5φ, ou si la barre n'est pas en
rive et qu'une barre au moins aussi grosse passe à l'intérieur du coude.

Le moteur **ne devine pas** ces conditions de dispense : il rend la valeur, séparément, et
laisse l'ingénieur décider si elle s'applique. L'appliquer d'office grossirait tous les
mandrins sans raison ; l'ignorer laisserait croire que le tableau suffit toujours.

L'article plafonne aussi f_cd à celui du C55/67 (36,67 MPa) : au-delà, la résistance n'est
plus créditée.

### 2.3 La longueur de coupe — pourquoi sommer les segments est faux

Une barre pliée **ne passe pas par le sommet de l'angle**. Elle entre dans le pli à une
distance R·tan(β/2) du sommet, décrit un arc de longueur R·β, et ressort symétriquement.

```
Chemin d'angle à angle : 2 R tan(β/2)
Fibre moyenne réelle   : R β
Déduction              : 2 R tan(β/2) − R β        (toujours positive)
```

Pour β = 90° : déduction = (2 − π/2) R = **0,4292 R**.

| Barre | R | Déduction par pli à 90° |
|---|---|---|
| HA8 | 20,0 | **8,58 mm** |
| HA12 | 30,0 | **12,88 mm** |
| HA20 | 80,0 | **34,34 mm** |

La déduction **n'est pas proportionnelle à l'angle**. Sur un HA12 :

| β | Déduction |
|---|---|
| 30° | 0,37 mm |
| 45° | 1,29 mm |
| 90° | 12,88 mm |
| 135° | 74,17 mm |

Elle explose près de 180°, où tan(β/2) diverge. Au-delà de 150° le moteur **refuse de
déduire** et le signale : un pli aussi fermé est un crochet, il se compte par son retour.

### 2.4 Un cadre a quatre plis, pas trois

Un cadre 300 × 500 en HA8 :

```
Périmètre d'angle à angle : 2 × (300 + 500)     = 1 600 mm
4 plis à 90° : 4 × 8,584                        =    34,34 mm  à déduire
Crochets 2 × 10 φ                               =   160 mm  à ajouter
Longueur de coupe                               = 1 725,7 mm
```

Le **retour au point de départ est un pli lui aussi**. Le compter comme trois sous-estime
la déduction d'un quart.

La déduction vaut ici **2,3 % du développé**. Petit par barre — et il se compte en tonnes
sur un projet.

### 2.5 Le repère désigne une forme, pas une barre

Deux cadres rigoureusement identiques dans deux poutres différentes portaient jusqu'ici
deux repères différents, parce que chaque élément numérotait pour lui-même. Le façonnier
recevait donc l'ordre de produire deux fois la même chose.

Le carnet regroupe désormais les formes identiques **à l'échelle du lot** :

```
même diamètre  +  mêmes longueurs dans le même ordre  +  mêmes plis
                à la tolérance de façonnage : 1 mm et 1°
```

Un millimètre d'écart ne fait pas deux formes — c'est la précision du façonnage, pas celle
du calcul.

Le repère est attribué **après le tri**, par diamètre croissant puis longueur décroissante :
c'est l'ordre dans lequel un carnet se vérifie. Et la ligne dit quels éléments partagent la
forme, avec leurs quantités.

## 3. Résultat moteur

`BarBendingTests` (18 cas) et `BarScheduleTests` (17 cas).

| Grandeur | Manuel | Moteur |
|---|---|---|
| Mandrin HA8 / HA16 / HA20 | 32 / 64 / 140 mm | conforme |
| Déduction HA8 / HA12 / HA20 à 90° | 8,584 / 12,876 / 34,336 mm | conforme |
| Déduction HA12 à 45° | 1,291 mm | conforme |
| Pli > 150° | refusé et signalé | conforme |
| Déduction toujours positive, 5° à 145° | oui | conforme |
| Cadre 300×500 HA8 : coupe | 1 725,7 mm | conforme |
| Part de la déduction sur un cadre | 2,3 % | conforme |
| Attente HA20 en L, 1000×400 | 1 365,66 mm | conforme |
| Expression (8.1) | 143,7 mm | conforme |
| Plafond f_cd au C55/67 | actif | conforme |
| Cadre fermé | **4** plis | conforme |
| Regroupement inter-éléments | 1 ligne, 35 barres, 2 emplois | conforme |
| Tolérance de 1 mm | ne sépare pas les formes | conforme |
| Tri et repérage | HA8 avant HA12 avant HA16, plus long d'abord | conforme |
| Masse linéique HA8 / HA12 / HA20 | 0,3946 / 0,8878 / 2,4662 kg/m | conforme |

## 4. Comparaison

Aucun écart. Les formules sont fermées et les valeurs manuelles ont été calculées avant
d'écrire le code.

**Ce que cette fiche corrige.** `QuantityCalculator` sommait les segments de la polyligne
depuis la version 3.0.0 : **toutes les masses annoncées jusqu'à la 3.10.0 incluse étaient
légèrement surestimées** — 2,3 % sur un cadre, moins sur une barre droite, rien du tout sur
un plan sans pli. L'erreur allait dans le sens de la surcommande : personne n'a manqué
d'acier. Un quantitatif recalculé sera un peu plus léger, et c'est la valeur juste.

Deux tests de non-régression le verrouillent : le quantitatif doit rendre **exactement** la
même masse que le carnet, et la coupe doit rester **inférieure** au développé dès qu'il y a
un pli — une correction qui irait dans l'autre sens signalerait une erreur de signe.

Les angles de pli sont **déduits du trajet réel** des barres, jamais déclarés. C'est le même
principe que la vérification des positions de copies en STAIR-04 : la forme et son carnet
ne peuvent pas diverger, puisque le carnet lit la forme.

## 5. Conclusion

Le carnet donne la longueur qui part en commande, et le repère désigne ce que le façonnier
doit produire. Le quantitatif, qui comptait le développé, compte désormais la coupe.

## 6. Limites connues

- **Pas de code de forme normalisé.** Les codes de forme usuels (BS 8666) sont britanniques
  et l'Eurocode n'en définit aucun. Le carnet donne la forme en clair et ses cotes dans
  l'ordre, ce qui est sans ambiguïté mais ne remplace pas un code attendu par un façonnier
  qui en utilise un.
- **L'expression (8.1) est rendue, pas appliquée.** Le moteur ne connaît ni F_bt au droit du
  pli, ni les conditions de dispense de l'art. 8.3(3).
- **Les crochets sont forfaitaires** : 10 φ de retour de chaque côté, valeur de pratique
  courante. Le moteur ne construit pas la géométrie du crochet ni son mandrin propre.
- **Les recouvrements ne sont pas dans le carnet** : les longueurs sont celles des barres du
  plan, sans barres de recouvrement supplémentaires.
- **Pas de rendu graphique de la forme** : ni croquis, ni schéma de façonnage.
- **Aucune écriture dans une nomenclature Revit** : le carnet sort dans la note de calcul,
  pas dans un tableau Revit. C'est la partie de la phase 9 qui reste à faire, et elle n'est
  pas vérifiable sans Revit.
