# SLAB-01 — Dalle pleine isostatique 220 mm, portée 5,00 m

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Épaisseur | 220 mm |
| Portée | 5 000 mm, isostatique |
| Panneau | 12 000 mm perpendiculairement (rapport 2,4 → porte bien dans un sens) |
| Béton | C25/30 → f_cd = 16,667 MPa, f_ctm = 2,565 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC1, 50 ans |
| Charges | g additionnel 2,0 kN/m², q 2,5 kN/m², poids propre inclus |
| Usage | catégorie A, habitation → ψ₂ = 0,3 |

Le calcul porte sur une **bande de 1 000 mm**.

## 2. Calcul manuel

### 2.1 Actions — EN 1990

```
Poids propre = 0,220 × 25                    = 5,50 kN/m²
g            = 5,50 + 2,00                   = 7,50 kN/m²
q                                             = 2,50 kN/m²

ELU  eq. 6.10  : 1,35 × 7,50 + 1,50 × 2,50   = 13,875 kN/m²
ELS quasi-perm : 7,50 + 0,3 × 2,50           =  8,250 kN/m²
```

### 2.2 Sollicitations — statique pure

Une travée isostatique ne demande aucun coefficient :

```
M_Ed = w l² / 8 = 13,875 × 5,00² / 8         = 43,36 kN·m/m
V_Ed = w l / 2  = 13,875 × 5,00 / 2          = 34,69 kN/m
```

### 2.3 Enrobage — art. 4.4.1

L'article 4.4.1.2(5) réduit la classe structurale d'une unité pour une géométrie de dalle :

```
XC1, C25/30, 50 ans, dalle  →  classe S4 − 1 = S3
c_min,dur = 10 mm      c_min,b = φ = 12 mm
c_min = max(12 ; 10 ; 10) = 12 mm      c_nom = 12 + 10 = 22 mm
d = 220 − 22 − 12/2 = 192 mm
```

Le diamètre entre dans l'enrobage, qui entre dans d, qui entre dans le diamètre : le
moteur fait **deux passages**, en partant de HA10, et converge sur HA12.

### 2.4 Flexion — art. 6.1

```
μ  = 43,36·10⁶ / (1 000 × 192² × 16,667) = 0,0706
ξ  = 1,25 (1 − √(1 − 2 × 0,0706)) = 0,0916
z  = 192 (1 − 0,4 × 0,0916) = 185,0 mm
A_s = 43,36·10⁶ / (185,0 × 434,78) = 539 mm²/m
```

**Minimum** — art. 9.3.1.1(1) renvoyant à 9.2.1.1(1) :

```
A_s,min = max(0,26 × 2,565/500 × 1 000 × 192 ; 0,0013 × 1 000 × 192)
        = max(256 ; 250) = 256 mm²/m       ✓ 539 > 256, le minimum ne gouverne pas
```

**Nappe retenue** : `s_max = min(3h ; 400) = 400 mm` → **HA12 e = 200** (565 mm²/m).

### 2.5 Répartition — art. 9.3.1.1(2)

```
A_s,trans >= 0,20 × 565 = 113 mm²/m
s_max = min(3,5h ; 450) = 450 mm  →  HA8 e = 300 (168 mm²/m)   ✓
```

L'armature de répartition n'est pas décorative : elle diffuse les charges concentrées et
reprend le retrait perpendiculairement à la portée.

### 2.6 Effort tranchant — art. 6.2.2

Une dalle courante ne porte **pas** d'armatures d'effort tranchant :

```
ρ_l = 565 / (1 000 × 192) = 0,00295
k   = 1 + √(200/192) = 2,02  →  plafonné à 2,0
v_Rd,c = 0,12 × 2,0 × (100 × 0,00295 × 25)^(1/3) = 0,467 MPa
v_min  = 0,035 × 2,0^1,5 × √25 = 0,495 MPa       →  v_min gouverne
V_Rd,c = 0,495 × 1 000 × 192 = 95,0 kN/m         contre 34,7 kN/m   taux 0,37  ✓
```

### 2.7 Flèche — art. 7.4.2

C'est le critère qui décide du sort d'une dalle :

```
ρ  = A_s,req / (b d) = 545 / (1 000 × 192) = 0,00284
ρ₀ = 10⁻³ √25 = 0,00500              →  ρ < ρ₀, équation 7.16a

l/d = 11 + 1,5 √25 × (ρ₀/ρ) + 3,2 √25 × (ρ₀/ρ − 1)^1,5
    = 11 + 7,5 × 1,762 + 16 × (0,762)^1,5
    = 11 + 13,22 + 10,65 = 34,87

K = 1,0 (isostatique, tableau 7.4N)
A_s,prov / A_s,req = 565 / 545 = 1,038
(l/d)_adm = 34,87 × 1,038 = 36,2      contre  l/d = 5 000 / 192 = 26,0   taux 0,72  ✓
```

### 2.8 Fissuration — art. 7.3.3

```
M_qp / M_Ed = 8,25 / 13,875 = 0,595
sigma_s = 434,78 × 0,595 × (545/565) = 249 MPa
XC1 → w_max = 0,4 mm (tableau 7.1N)

Tableau 7.2N à 249 MPa : phi_max = 19,1 mm    →  HA12 ✓
Tableau 7.3N à 249 MPa : s_max   = 239 mm     →  e = 200 ✓
```

L'article 7.3.3(2) n'en exige **qu'un seul** ; ici les deux passent.

## 3. Résultat moteur

`Slab01DesignTests`, 12 cas.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| Charge ELU | 13,875 kN/m² | 13,875 kN/m² | 0 % |
| Charge quasi-permanente | 8,250 kN/m² | 8,250 kN/m² | 0 % |
| M en travée | 43,359 kN·m/m | 43,359 kN·m/m | 0 % |
| V | 34,688 kN/m | 34,688 kN/m | 0 % |
| Enrobage | 22 mm | 22 mm | 0 % |
| d | 192 mm | 192 mm | 0 % |
| A_s requis | 539 mm²/m | 535–555 mm²/m | < 2 % |
| Nappe | HA12 e = 200 | excès ≤ 10 % | — |
| Répartition | ≥ 20 % | vérifié | — |
| V_Rd,c | 95,0 kN/m | 91–99 kN/m | < 4 % |
| (l/d) de base | 34,87 | 33,5–36,5 | < 4 % |
| l/d réel | 26,0 | 25,5–26,5 | < 2 % |
| σ_s | 249 MPa | 235–265 MPa | < 6 % |

## 4. Comparaison

Les charges et les sollicitations sont exactes au chiffre près : elles ne relèvent que de
l'EN 1990 et de la statique. Les écarts résiduels viennent des arrondis du calcul manuel
sur f_ctm et sur ξ, et ne changent aucune décision de ferraillage.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'une dalle isostatique de bout en bout : combinaisons
EN 1990, statique, enrobage avec la réduction de classe propre aux dalles, flexion,
répartition, effort tranchant sans armatures, flèche par l'élancement limite, fissuration
sans calcul direct.

## 6. Limites connues

- **σ_s est estimée, pas calculée.** La contrainte de l'acier sous combinaison
  quasi-permanente est déduite du rapport des combinaisons et du rapport des sections,
  sans analyse en section fissuurée. C'est l'approximation usuelle, et le moteur le dit
  dans le commentaire de la vérification.
- **La flèche n'est pas calculée en millimètres.** L'article 7.4.2 répond « admissible ou
  non », il ne rend pas une valeur. Lorsqu'il est dépassé, le moteur exige le calcul
  détaillé de 7.4.3 — qu'il ne fait pas.
- **Retrait et fluage** ne sont pas modélisés explicitement ; ils sont couverts
  forfaitairement par la méthode de l'élancement limite.
- **Pas de poinçonnement** dans ce module : une dalle sur appuis ponctuels
  (plancher-dalle) relève du module Semelle pour la vérification de poinçonnement,
  et n'est pas encore intégrée ici.
- **Rien n'a encore été exécuté dans Revit.**
