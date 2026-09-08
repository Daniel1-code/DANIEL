# WALL-01 — Voile porteur 200 mm, longueur 4,00 m, hauteur libre 3,00 m

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Épaisseur | 200 mm |
| Longueur | 4 000 mm (rapport L/t = 20 ≥ 4 → c'est bien un voile, art. 9.6.1) |
| Hauteur libre | 3 000 mm |
| Béton | C25/30 → f_cd = 16,667 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC1, 50 ans |
| Maintien | tête et pied seulement |
| Charge | N_Ed = 400 kN/m, centrée, aucun moment déclaré |
| Fluage | φ_ef = 2,0 |

Le comportement hors plan est calculé sur une **bande verticale de 1 000 mm** : le voile
s'y comporte exactement comme un poteau de section 1 000 × 200.

## 2. Calcul manuel

### 2.1 Enrobage — art. 4.4.1

Un voile a une géométrie en nappe : l'article 4.4.1.2(5) réduit la classe structurale.

```
XC1, C25/30, 50 ans, nappe  →  S4 − 1 = S3
c_min,dur = 10 mm      c_min,b = φ = 10 mm
c_min = max(10 ; 10 ; 10) = 10 mm      c_nom = 10 + 10 = 20 mm
d = 200 − 20 − 10/2 = 175 mm
```

### 2.2 Flambement — art. 12.6.5.1

```
Maintien en tête et en pied  →  beta = 1,00  →  l_0 = 3 000 mm
i = t / √12 = 200 / 3,4641 = 57,74 mm
lambda = 3 000 / 57,74 = 51,96
```

### 2.3 Le second ordre est-il nécessaire ? — art. 5.8.3.1

```
n     = 400 000 / (1 000 × 200 × 16,667) = 0,1200
A_s,min = 0,002 × 200 000 = 400 mm²/m       (art. 9.6.2(1))
omega = 400 × 434,78 / (200 000 × 16,667) = 0,0522

A = 1 / (1 + 0,2 × 2,0) = 0,7143
B = √(1 + 2 × 0,0522) = 1,0509
C = 0,7
lambda_lim = 20 × 0,7143 × 1,0509 × 0,7 / √0,1200 = 10,509 / 0,3464 = 30,3

lambda = 52,0  >  lambda_lim = 30,3   →  le second ordre est OBLIGATOIRE
```

C'est le point important : un voile de 200 mm sur 3,00 m de hauteur libre, même
faiblement chargé, est trop élancé pour ignorer le second ordre.

### 2.4 Second ordre — art. 5.8.8

```
1/r_0 = eps_yd / (0,45 d) = 0,0021739 / (0,45 × 175) = 2,761·10⁻⁵ mm⁻¹
K_r   = (nu − n)/(nu − 0,4) avec nu = 1 + omega = 1,0522
      = (1,0522 − 0,120)/(1,0522 − 0,4) = 1,429  →  plafonné à 1,00
beta  = 0,35 + 25/200 − 51,96/150 = 0,1286
K_phi = 1 + 0,1286 × 2,0 = 1,257

1/r = 1,00 × 1,257 × 2,761·10⁻⁵ = 3,471·10⁻⁵ mm⁻¹
e_2 = (1/r) l_0² / 10 = 3,471·10⁻⁵ × 3 000² / 10 = 31,2 mm
M_2 = 400 × 0,0312 = 12,5 kN·m/m
```

### 2.5 Moment de calcul — art. 6.1(4)

Aucun moment n'est déclaré, mais l'excentricité minimale s'impose :

```
e_0 = max(t/30 ; 20) = max(6,7 ; 20) = 20 mm
M_1 = 400 × 0,020 = 8,0 kN·m/m
M_Ed = M_1 + M_2 = 8,0 + 12,5 = 20,5 kN·m/m
```

### 2.6 Résistance de la bande

Section 1 000 × 200 armée du minimum réglementaire, 200 mm² à chaque parement, sous
N = 400 kN. Diagramme rectangulaire, axe neutre x ≈ 33,9 mm :

```
F_c    = 0,8 x × 1 000 × 16,667 = 452 kN      bras 100 − 0,4x = 86,5 mm
Acier comprimé (25 mm)  ≈  +37 kN            bras 75 mm
Acier tendu (175 mm), plastifié  = −87 kN     bras 75 mm
M_Rd ≈ 452 × 0,0865 + 37 × 0,075 + 87 × 0,075 ≈ 48 kN·m/m
```

**20,5 ≤ 48 kN·m/m** : la section minimale de l'article 9.6.2 suffit largement. Le voile
n'est pas dimensionné par sa résistance mais par ses dispositions constructives — ce qui
est le cas le plus fréquent d'un voile porteur courant.

### 2.7 Nappes retenues

```
Vertical   : 400 mm²/m au total, soit 200 mm²/m par nappe
             s_max = min(3 × 200 ; 400) = 400 mm
             HA8 e = 250 par nappe → 201 mm²/m par nappe, 402 mm²/m au total   ✓

Horizontal : A_s,h,min = max(0,25 × 402 ; 0,001 × 200 000) = max(101 ; 200) = 200 mm²/m
             s_max = 400 mm
             HA8 e = 300 par nappe → 168 mm²/m par nappe, 335 mm²/m au total   ✓

Épingles   : A_s,v = 402 mm²/m contre 0,02 A_c = 4 000 mm²/m  →  aucune (art. 9.6.4)
```

## 3. Résultat moteur

`Wall01DesignTests`, 12 cas.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| Enrobage | 20 mm | 20 mm | 0 % |
| β | 1,00 | 1,00 | 0 % |
| l_0 | 3 000 mm | 3 000 mm | 0 % |
| λ | 51,96 | 51–53 | < 2 % |
| λ_lim | 30,3 | 29–32 | < 5 % |
| e_2 | 31,2 mm | 28–34 mm | < 9 % |
| M_Ed hors plan | 20,5 kN·m/m | 18–23 kN·m/m | < 12 % |
| A_s,v requis | 400 mm²/m | 400 mm²/m | 0 % |
| Taux de capacité | ≈ 0,43 | 0,25–0,60 | — |
| A_s,h min | 200 mm²/m | 195–210 mm²/m | < 5 % |
| Épingles | aucune | aucune | — |
| Groupes du plan | 4 | 4 | — |

## 4. Comparaison

Les fourchettes sur e_2 et M_Ed sont larges parce que le calcul manuel arrondit K_φ et
β ; l'écart ne change ni la nappe retenue ni la conclusion, puisque la marge de capacité
est d'un facteur 2.

La capacité M_Rd est encadrée plutôt que fixée : le calcul manuel utilise le diagramme
rectangulaire, le moteur la loi parabole-rectangle de l'article 3.1.7. C'est la même
différence de **modèle** que celle documentée en COLUMN-02, et elle est bornée par la même
plage.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'un voile porteur courant : enrobage avec la
réduction propre aux nappes, longueur de flambement de l'article 12.6.5.1, élancement
limite, second ordre par courbure nominale, excentricité minimale, dispositions des
articles 9.6.2, 9.6.3 et 9.6.4.

Il montre surtout ce que le calcul manuel montre : **un voile porteur courant est
dimensionné par ses dispositions constructives, pas par sa résistance.**

## 6. Limites connues

- **Aucun contreventement dans cette fiche.** L'effort tranchant et la flexion dans le
  plan sont implémentés mais validés en WALL-02.
- **Les ouvertures sont ignorées.** Un voile percé est signalé à la lecture, mais la
  longueur retenue reste la longueur totale : aucun chaînage de trumeau ni renfort de
  linteau n'est dimensionné.
- **Pas de vérification au feu** (EN 1992-1-2), alors qu'elle gouverne souvent
  l'épaisseur minimale d'un voile.
- **Le fluage φ_ef est saisi, pas calculé.** Il devrait venir de l'annexe B ou d'une
  analyse ; la valeur par défaut de 2,0 est une hypothèse courante, pas un calcul.
- **Rien n'a encore été exécuté dans Revit.**
