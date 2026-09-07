# COLUMN-03 — Élancement et second ordre d'un poteau 300 × 300

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Section | 300 × 300 mm — A_c = 90 000 mm² |
| Hauteur | 6,00 m ; longueur de flambement l₀ = 1,0 × 6 000 = **6 000 mm** |
| Béton | C25/30 → f_cd = 16,667 MPa |
| Acier | B500 → f_yd = 434,78 MPa ; ε_yd = 434,78 / 200 000 = **0,0021739** |
| Armatures | 8 HA16 → A_s = 1 608,5 mm² |
| Enrobage | 30 mm + cadre HA8 → d = 300 − 30 − 8 − 8 = **254 mm** |
| Effort | N_Ed = 900 kN |
| Fluage | φ_ef = 2,0 |

## 2. Calcul manuel

### 2.1 Efforts relatifs

```
n     = N_Ed / (A_c f_cd)     = 900 000 / (90 000 × 16,667) = 0,600
ω     = A_s f_yd / (A_c f_cd) = 1 608,5 × 434,78 / 1 500 000 = 0,4662
```

### 2.2 Élancement — art. 5.8.3.1 et 5.8.3.2

```
i        = h / √12 = 300 / 3,4641 = 86,60 mm
λ        = l₀ / i  = 6 000 / 86,60 = 69,28
A        = 1 / (1 + 0,2 φ_ef)  = 1 / 1,4      = 0,7143
B        = √(1 + 2 ω)          = √1,9325      = 1,3901
C        = 0,7                 (rapport des moments d'extrémité inconnu)
λ_lim    = 20 A B C / √n = 20 × 0,7143 × 1,3901 × 0,7 / √0,600 = 17,95
```

**λ = 69,3 > λ_lim = 17,9 → les effets du second ordre doivent être pris en compte.**

### 2.3 Courbure nominale — art. 5.8.8.3

```
1/r₀ = ε_yd / (0,45 d) = 0,0021739 / (0,45 × 254) = 1,9019 × 10⁻⁵ /mm

n_u  = 1 + ω = 1,4662        n_bal = 0,4
K_r  = (n_u − n) / (n_u − n_bal) = (1,4662 − 0,600) / (1,4662 − 0,400) = 0,8124   ≤ 1 ✓

β    = 0,35 + f_ck/200 − λ/150 = 0,35 + 0,125 − 0,4619 = 0,01313
K_φ  = 1 + β φ_ef = 1 + 0,01313 × 2,0 = 1,0263   ≥ 1 ✓

1/r  = K_r K_φ (1/r₀) = 0,8124 × 1,0263 × 1,9019e−5 = 1,5857 × 10⁻⁵ /mm
```

### 2.4 Excentricité et moment du second ordre — art. 5.8.8.2

```
e₂ = (1/r) l₀² / c   avec c = 10
   = 1,5857e−5 × 6 000² / 10 = 1,5857e−5 × 3,6e6 = 57,08 mm

M₂ = N_Ed e₂ = 900 000 × 57,08 = 51,4 × 10⁶ N·mm = 51,4 kN·m
```

### 2.5 Excentricité minimale — art. 6.1(4)

```
e₀ = max(h/30 ; 20 mm) = max(10 ; 20) = 20 mm
M₀ min = 900 × 0,020 = 18,0 kN·m
```

## 3. Comparaison

| Grandeur | Manuel | Tolérance du test |
|---|---|---|
| λ | 69,28 | [69,2 ; 69,4] |
| λ_lim | 17,95 | [17,8 ; 18,1] |
| e₂ | 57,08 mm | [56,5 ; 57,7] |
| M₂ | 51,4 kN·m | [50,8 ; 52,0] |
| e₀ (h = 300) | 20 mm | exact |
| e₀ (h = 900) | 30 mm | exact |

## 4. Conclusion

✅ **Validé.** Le moteur reproduit la méthode de la courbure nominale.

Tests associés : `tests/DanCI.Structural.Tests/Validation/Column03SecondOrderTests.cs`.

**Limites connues** :
- **C = 0,7** est retenu systématiquement, faute de connaître le rapport des moments
  d'extrémité M₀₁/M₀₂. C'est la valeur recommandée en l'absence d'information, et elle est
  **sécuritaire** pour un poteau en simple courbure ; elle est en revanche **optimiste** pour
  un poteau à moments d'extrémité opposés. À reprendre quand les efforts seront importés
  avec leurs deux extrémités ;
- **φ_ef est saisi par l'utilisateur**, il n'est pas déduit de l'analyse de fluage (art. 5.8.4) ;
- la longueur de flambement l₀ est saisie par un coefficient, elle n'est pas déduite de la
  rigidité des nœuds (art. 5.8.3.2(3)).
