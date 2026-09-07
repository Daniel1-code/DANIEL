# BEAM-01 — Poutre isostatique 300 × 600, portée 6,00 m

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Section | 300 × 600 mm, rectangulaire |
| Portée | 6 000 mm, travée isostatique |
| Béton | C25/30 → f_cd = 16,667 MPa, f_ctm = 2,565 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC1, 50 ans, sans contrôle de production spécial |
| Moment | M_Ed = 250 kN·m en travée |
| Effort tranchant | V_Ed = 200 kN aux deux appuis |
| Redistribution | δ = 1,00 (aucune) |

## 2. Calcul manuel

### 2.1 Enrobage — art. 4.4.1

Le calcul converge sur des HA16 (§2.2), donc :

```
Classe structurale S4 (XC1, C25/30 sous le seuil C30/37)
c_min,dur = 15 mm      c_min,b = φ = 16 mm
c_min = max(16 ; 15 ; 10) = 16 mm      c_nom = 16 + 10 = 26 mm
d = 600 − 26 − 8 (cadre) − 16/2 = 558 mm
```

### 2.2 Flexion — art. 6.1, diagramme rectangulaire

```
μ    = M_Ed / (b d² f_cd) = 250·10⁶ / (300 × 558² × 16,667) = 0,1606
μ_lim = 0,8 ξ_lim (1 − 0,4 ξ_lim) = 0,2942   avec ξ_lim = (1 − 0,44)/1,25 = 0,448
μ = 0,1606 ≤ 0,2942  →  section simplement armée

ξ = 1,25 (1 − √(1 − 2,5 μ)) = 1,25 (1 − √0,5985) = 0,2829
z = d (1 − 0,4 ξ) = 558 × 0,8868 = 494,9 mm
A_s = M_Ed / (z f_yd) = 250·10⁶ / (494,9 × 434,78) = 1 162 mm²
```

**Minimum** — art. 9.2.1.1(1) :

```
A_s,min = max(0,26 × 2,565/500 × 300 × 558 ; 0,0013 × 300 × 558)
        = max(223 ; 218) = 223 mm²        ✓ 1 162 ≫ 223
```

**Choix des barres** : largeur utile entre cadres = 300 − 2(26 + 8) = 232 mm ;
espacement libre minimal = max(16 ; 20+5 ; 20) = 25 mm → **6 HA16 par lit au maximum**.

```
6 HA16 = 6 × 201,06 = 1 206 mm²   ≥ 1 162 mm²   (excès 3,8 %)
```

### 2.3 Effort tranchant — art. 6.2

**Résistance sans armatures**, art. 6.2.2(1) :

```
k     = 1 + √(200/558) = 1,599   (≤ 2,0)
ρ_l   = 1 206 / (300 × 558) = 0,00721   (≤ 0,02)
V_Rd,c = 0,12 × 1,599 × (100 × 0,00721 × 25)^(1/3) × 300 × 558 = 84,1 kN
v_min  = 0,035 × 1,599^1,5 × √25 = 0,354 MPa → plancher 59,3 kN
V_Rd,c = 84,1 kN  <  V_Ed = 200 kN  →  armatures d'effort tranchant nécessaires
```

**Bielles**, art. 6.2.3 :

```
z    = 0,9 d = 502,2 mm
ν₁   = 0,6 (1 − 25/250) = 0,54
V_Rd,max(cot θ = 2,5) = 300 × 502,2 × 0,54 × 16,667 / (2,5 + 0,4) = 467,6 kN
V_Ed = 200 kN ≤ 467,6 kN  →  on garde cot θ = 2,5 (θ = 21,8°), le plus économique
A_sw/s = V_Ed / (z f_ywd cot θ) = 200 000 / (502,2 × 434,78 × 2,5) = 0,3664 mm²/mm
```

**Minimum et espacement**, art. 9.2.2 :

```
ρ_w,min = 0,08 √25 / 500 = 0,0008  →  A_sw/s ≥ 0,0008 × 300 = 0,24 mm²/mm
s_max   = 0,75 d = 418,5 mm
```

**Zonage** — l'effort tranchant varie linéairement de +200 kN à −200 kN :

| Zone | Étendue | V_Ed | A_sw/s requis | Cadre HA8 (2 brins) = 100,5 mm² | s retenu |
|---|---|---|---|---|---|
| Appui gauche | 0 → 1 500 | 200 kN | 0,3664 | 100,5 / 0,3664 = 274 mm | **250 mm** |
| Travée | 1 500 → 4 500 | 100 kN | 0,24 (minimum) | 100,5 / 0,24 = 419 mm, plafonné à 418,5 | **400 mm** |
| Appui droit | 4 500 → 6 000 | 200 kN | 0,3664 | 274 mm | **250 mm** |

Les espacements sont arrondis au multiple de 25 mm inférieur.

### 2.4 Ancrages et décalage — art. 8.4 et 9.2.1.3

```
l_bd (HA16, C25/30) = 646 mm → arrondi à 650 mm
l₀                   = 968 mm → arrondi à 1 000 mm
a_l = z cot θ / 2 = 502,2 × 2,5 / 2 = 628 mm
Chapeaux : max(L/4 ; a_l + l_bd) = max(1 500 ; 1 278) = 1 500 mm
```

## 3. Comparaison

| Grandeur | Manuel | Test |
|---|---|---|
| c_nom | 26 mm | exact |
| d | 558 mm | exact |
| A_s requis | 1 162 mm² | [1 150 ; 1 175] |
| Barres | 6 HA16 = 1 206 mm² | exact |
| φ cadre | HA8 | exact |
| s appuis | 250 mm | exact |
| s travée | 400 mm | exact |
| a_l | 628 mm | [620 ; 635] |
| l_bd | 650 mm | exact |
| l₀ | 1 000 mm | exact |

## 4. Conclusion

✅ **Validé.** Le moteur reproduit le calcul manuel de bout en bout, y compris le zonage
des cadres.

Tests associés : `tests/DanCI.Structural.Tests/Validation/Beam01DesignTests.cs`,
`tests/DanCI.Structural.Tests/EC2/BendingDesignTests.cs`,
`tests/DanCI.Structural.Tests/EC2/ShearDesignTests.cs`.

**Limites connues** :
- l'effort tranchant est supposé varier **linéairement** entre les deux appuis, ce qui
  correspond à une charge uniformément répartie. Une charge concentrée en travée n'est pas
  représentée par ce modèle : saisir alors les efforts enveloppes ;
- la **réduction de V_Ed à la distance d du nu d'appui** (art. 6.2.1(8)) n'est pas appliquée :
  le calcul est fait au nu, ce qui est **sécuritaire** ;
- les zones d'appui occupent **L/4** de chaque côté, une règle de mise en œuvre courante et
  non une prescription de l'Eurocode ;
- la vérification de la bielle d'about et de l'ancrage des barres inférieures sur appui
  (art. 9.2.1.4) n'est pas encore implémentée.
