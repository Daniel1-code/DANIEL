# STAIR-02 — Ce qui gouverne réellement une volée, et ce que le moteur refuse de faire

## 1. Énoncé

Cette fiche ne rejoue pas le calcul de STAIR-01. Elle vérifie les points sur lesquels une
volée se comporte **autrement** qu'une dalle, et ceux sur lesquels le moteur s'arrête
volontairement.

| Cas | Question posée |
|---|---|
| a | Qu'est-ce qui décide de l'épaisseur de la paillasse ? |
| b | Le nœud existe-t-il toujours ? |
| c | Une barre peut-elle, sous un réglage quelconque, finir par suivre le pli ? |
| d | Un palier trop court est-il détecté ? |
| e | La géométrie de la marche est-elle jugée, et au nom de quoi ? |
| f | Ce que le moteur ne fait pas est-il dit ? |

Volée commune : 9 contremarches de 170 mm, giron 280 mm, palier 1 300 mm, C25/30, B500,
XC1, q_k = 3,00 kN/m².

## 2. Calcul manuel

### 2.a C'est la flèche qui décide de l'épaisseur, pas la résistance

Quatre paillasses sur la même portée de 3,54 m :

| t (mm) | d (mm) | M_Ed (kN·m/m) | A_s (mm²/m) | ρ | l/d | limite ≈ |
|---|---|---|---|---|---|---|
| 120 | 92 | 20,3 | 573 | 0,64 % | **39,8** | 25 |
| 130 | 99 | 20,9 | 522 | 0,53 % | **35,8** | 27 |
| 150 | 119 | 22,1 | 449 | 0,38 % | 29,7 | 30 |
| 180 | 152 | 23,9 | 373 | 0,25 % | 23,3 | 52 |

Deux choses à lire dans ce tableau.

**La résistance ne dit rien.** À 120 mm la flexion passe encore : 573 mm²/m se posent sans
difficulté. C'est la flèche qui échoue, et de 60 %.

**L'épaississement s'auto-finance.** Passer de 120 à 180 mm alourdit la volée et fait
monter M_Ed de 20,3 à 23,9 kN·m/m — et pourtant la flèche s'améliore spectaculairement,
parce que d croît plus vite que M et que la baisse du taux d'armature fait basculer
l'équation 7.16a dans son régime généreux. C'est pour cela que « épaissir » est l'action
utile, et le moteur la nomme.

### 2.b Le nœud n'existe pas toujours

| Mode d'appui | Portée | Nœud |
|---|---|---|
| Volée + palier | 2 240 + 1 300 = 3 540 mm | angle rentrant tendu, nappes croisées |
| Volée seule | 2 240 mm | **aucun** : les paliers sont portés ailleurs |
| Portée transversale | 1 200 mm (la largeur) | **aucun** : les barres principales sont perpendiculaires à la montée et ne traversent pas le pli |

Dans les deux derniers cas, le moteur rend une vérification explicitement marquée **Sans
objet**, avec sa justification, plutôt que de l'omettre — comme pour le poinçonnement d'une
semelle filante. Un tableau de vérifications où une ligne disparaît sans explication laisse
croire à un oubli.

La portée transversale change en outre la statique : la volée franchit sa largeur sous la
**seule charge de volée**, sans palier, donc M = w l²/8 par la statique pure.

### 2.c Aucune barre ne suit jamais le pli

C'est l'invariant du module, et il est testé comme tel. Pour quatre longueurs de palier —
300, 800, 1 300 et 2 000 mm — le test vérifie géométriquement que la nappe inférieure de
volée **remonte au-dessus de l'altitude du pli** :

```
z du pli = enrobage + φ/2 + projection × tan α
La barre doit finir au-dessus, dans la face supérieure du palier.
```

Un test sur une valeur de sortie aurait pu être satisfait par un dessin faux. Celui-ci
porte sur la trajectoire réelle de la barre, et aucun réglage ne peut le contourner.

### 2.d Un palier trop court ne laisse pas ancrer les barres croisées

```
Palier ramené à 300 mm :
  l_bd requis au-delà du pli = 500 mm
  disponible                 = min(300 côté palier ; 1 914 côté paillasse) = 300 mm
  → 500 > 300, la vérification ÉCHOUE
```

Le message nomme les actions possibles : allonger le palier, réduire le diamètre de la
nappe, ou replier les barres en retour d'équerre. Et il dit ce que le moteur ne fera
**jamais** : réduire la longueur d'ancrage requise pour faire passer la vérification.

### 2.e La géométrie de la marche est jugée — mais au nom de l'ergonomie, pas de l'Eurocode

```
R = 200 mm, G = 240 mm  →  α = 39,8°
```

Le moteur signale la pente excessive et renvoie à la réglementation de construction
applicable, **qu'il n'applique pas** : les hauteurs et girons admissibles relèvent du droit
national, pas de l'Eurocode. Aucune vérification de résistance n'échoue pour autant — un
escalier raide n'est pas un escalier faible.

Blondel (2R + G = 640 mm ici) est rendue de la même façon : une valeur, une appréciation,
et la mention explicite qu'elle ne relève d'aucun Eurocode.

Enfin, une volée d'**une seule contremarche** n'a aucun giron, donc aucune portée : le
moteur le dit au lieu de diviser par zéro.

### 2.f Ce qui n'est pas fait est écrit

| Point | Ce que le moteur en dit |
|---|---|
| Rendement du nœud | « L'EFFICACITÉ DU NŒUD N'EST PAS CALCULÉE » — modèle bielles-tirants des art. 5.6.4 et 6.5 non construit |
| Charge concentrée Q_k | rappelée (art. 6.3.1.2(1)), non combinée ; à vérifier séparément sur une volée courte |
| Catégorie d'usage | art. 6.3.1(1) : un escalier prend celle de la zone desservie, il n'en a pas en propre |
| Composante M cos α | le moment entier est retenu, ce qui est sécuritaire, et c'est dit |
| Majoration de flèche de 15 % | vient de la BS 8110, n'existe pas dans l'EN 1992-1-1, **non appliquée** |
| Encastrement partiel aux paliers | les chapeaux sont posés par défaut ; la longueur max(L/4 ; l_bd) relève de la pratique courante |

Une volée déclarée isostatique est en réalité toujours partiellement encastrée dans ses
paliers. Le moteur ne prétend pas calculer ce moment : il pose les chapeaux et explique
pourquoi. Sans eux, la fissuration se déclare en face supérieure des appuis, là où le
calcul isostatique ne prévoit rien.

## 3. Résultat moteur

`Stair02LimitsTests`, 16 cas.

| Cas | Attendu | Moteur |
|---|---|---|
| a | 120 mm : flèche en échec, flexion satisfaite | conforme |
| a′ | épaissir augmente M mais réduit le taux de flèche | conforme |
| b | volée seule : nœud absent, vérification *Sans objet*, taux nul | conforme |
| b′ | portée transversale : portée = largeur, pas de statique à deux charges | conforme |
| c | 4 longueurs de palier : la barre remonte toujours au-dessus du pli | conforme |
| d | palier 300 mm : ancrage en échec, message sans réduction d'ancrage | conforme |
| e | pente 39,8° signalée, aucune vérification en échec | conforme |
| e′ | Blondel rendue comme règle d'ergonomie hors Eurocode | conforme |
| e″ | 1 contremarche : aucun giron, aucune portée, signalé | conforme |
| f | les six renoncements sont écrits dans les notes ou les avertissements | conforme |

## 4. Comparaison

La plupart de ces cas ne comparent aucun nombre : ils portent sur ce que le moteur
**refuse** de faire, ou sur des invariants géométriques. C'est délibéré. Sur un escalier,
l'erreur coûteuse n'est presque jamais arithmétique — c'est un poids propre sous-estimé de
40 %, ou un nœud ferraillé comme un coude ordinaire.

## 5. Conclusion

Le module traite la volée pour ce qu'elle est : une dalle inclinée qui porte des marches,
dont l'épaisseur est décidée par la flèche et dont le point faible est le nœud. Il refuse
de suivre le pli, refuse de raccourcir un ancrage, et refuse d'appliquer une majoration de
flèche qui vient d'un autre code.

## 6. Limites connues

- **Volées droites uniquement.** Un escalier balancé, hélicoïdal ou à marches en console
  n'est ni calculé ni détecté : le moteur le traiterait comme une volée droite de mêmes
  contremarches, ce qui serait faux. C'est la limite la plus dangereuse de ce module.
- **Nœud non justifié**, seulement détaillé et son ancrage vérifié.
- **Pas de charge concentrée**, pas de fissuration, pas de calcul de flèche détaillé.
- **La réglementation de construction n'est pas connue** : la pente et Blondel sont
  rendues, jamais imposées.
- **Rien n'a encore été exécuté dans Revit.** Pour ce module en particulier, la capacité
  d'un escalier Revit à héberger des armatures est interrogée à l'exécution mais n'a
  jamais été observée.
