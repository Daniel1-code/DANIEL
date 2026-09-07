# COLUMN-02 — Diagramme d'interaction N-M d'un poteau 400 × 400

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Section | 400 × 400 mm — A_c = 160 000 mm² |
| Béton | C25/30 → f_cd = 1,0 × 25 / 1,5 = **16,667 MPa** |
| Acier | B500 → f_yd = 500 / 1,15 = **434,78 MPa**, E_s = 200 000 MPa |
| Armatures | 8 HA20 → A_s = 8 × 314,16 = **2 513 mm²** |
| Enrobage | 30 mm + cadre HA8 → axe des barres à **48 mm** de chaque face |
| Disposition | 3 barres au niveau comprimé, 2 à mi-hauteur (y = 200), 3 au niveau tendu (y = 352) |

**Modèle du calcul manuel** : diagramme rectangulaire simplifié (EC2 3.1.7(3)), λ = 0,8 et η = 1,0.
**Modèle du moteur** : loi parabole-rectangle (EC2 3.1.7(1)), intégrée sur 240 bandes.
Les deux sont autorisés par la norme ; l'écart attendu est de l'ordre de 1 à 3 %.

## 2. Calcul manuel

### 2.1 Équations

Pour une position d'axe neutre x mesurée depuis la fibre la plus comprimée :

```
F_c   = η f_cd b (λx)                          appliqué à 0,4 x de la fibre comprimée
ε_s(y) = ε_cu2 (x − y) / x    avec ε_cu2 = 0,0035
σ_s   = min(E_s ε_s ; f_yd) en compression, max(E_s ε_s ; −f_yd) en traction
```

Les barres situées **dans** le bloc comprimé (y < λx) voient leur contrainte diminuée de f_cd :
elles remplacent du béton déjà comptabilisé.

### 2.2 Cas N_Ed = 1 500 kN

Itération sur x : x = 240 mm → N = 1 439 kN ; x = 250 mm → N = 1 546 kN.
Interpolation → **x = 245,7 mm**, λx = 196,6 mm.

| Terme | ε | σ (MPa) | Force (N) | Bras / centre (mm) | Moment (N·mm) |
|---|---|---|---|---|---|
| Béton | — | 16,667 | +1 310 400 | +101,7 | +133 293 888 |
| 3 HA20 à y = 48 | +0,002816 | 434,78 − 16,67 = 418,11 | +394 071 | +152 | +59 898 792 |
| 2 HA20 à y = 200 | +0,000651 | 130,2 | +81 804 | 0 | 0 |
| 3 HA20 à y = 352 | −0,001514 | −302,8 | −285 389 | −152 | +43 379 128 |
| **Total** | | | **+1 500 886 N** | | **+236 571 808 N·mm** |

```
N = 1 501 kN ≈ 1 500 kN ✓
M_Rd = 236,6 kN·m
```

### 2.3 Compression centrée

Au pivot C, la déformation est uniforme et vaut ε_c2 = 0,002. L'acier n'est alors qu'à
200 000 × 0,002 = **400 MPa**, en deçà de f_yd :

```
N_max = A_c f_cd + A_s (400 − f_cd) = 2 666 720 + 2 513 × 383,3 = 3 630 kN
```

À distinguer du N_Rd simplifié de l'article 6.1, qui suppose l'acier à f_yd :

```
N_Rd = A_c f_cd + A_s f_yd = 2 666 720 + 1 092 740 = 3 759 kN
```

### 2.4 Flexion simple (N_Ed = 0)

Itération : x = 80 mm → N = −8 kN ; **x = 81 mm** → N = +2 kN.

| Terme | σ (MPa) | Force (N) | Bras (mm) | Moment (N·mm) |
|---|---|---|---|---|
| Béton | 16,667 | +432 000 | +167,6 | +72 403 200 |
| 3 HA20 à y = 48 | 285,2 − 16,7 = 268,5 | +253 061 | +152 | +38 465 272 |
| 2 HA20 à y = 200 | −434,78 | −273 175 | 0 | 0 |
| 3 HA20 à y = 352 | −434,78 | −409 731 | −152 | +62 279 112 |
| **Total** | | **+2 155 N ≈ 0** | | **+173 147 584 N·mm** |

```
M_Rd = 173,1 kN·m
```

## 3. Résultat du moteur

`InteractionDiagram.MomentResistance(section, N_Ed)` sur la même section.

## 4. Comparaison

| Grandeur | Manuel (bloc rectangulaire) | Moteur (parabole-rectangle) | Tolérance du test |
|---|---|---|---|
| M_Rd à N_Ed = 1 500 kN | 236,6 kN·m | vérifié par test | ± 6 % → [222 ; 251] |
| M_Rd en flexion simple | 173,1 kN·m | vérifié par test | ± 6 % → [163 ; 184] |
| N max du diagramme | 3 630 kN | vérifié par test | [3 550 ; 3 700] |
| N_Rd simplifié (art. 6.1) | 3 759 kN | vérifié par test | [3 750 ; 3 770] |

## 5. Conclusion

✅ **Validé** dans les tolérances annoncées.

L'écart entre les deux modèles provient uniquement de la loi de comportement du béton :
le bloc rectangulaire mobilise une contrainte moyenne de 0,80 f_cd, la parabole-rectangle
d'environ 0,81 f_cd, avec un centre de gravité légèrement différent. Cet écart est
**intrinsèque à la norme**, qui autorise les deux, et non une erreur de l'un ou de l'autre.

Tests associés : `tests/DanCI.Structural.Tests/Validation/Column02InteractionTests.cs`.

**Limites connues** :
- la disposition des barres du cas manuel (3 / 2 / 3) est projetée sur un seul axe ; pour une
  flexion autour de l'autre axe la section est identique par symétrie ;
- le pivot A (ε_ud = 0,045) n'intervient pas dans ce cas, l'axe neutre restant dans la section.
