# COLUMN-05 — Dispositions constructives complètes d'un poteau 300 × 500

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Section | 300 (X) × 500 (Y) mm — A_c = 150 000 mm² |
| Hauteur | 3,00 m |
| Béton | C25/30 |
| Acier | B500 |
| Exposition | XC1, durée d'utilisation 50 ans, sans contrôle de production spécial |
| Effort | N_Ed = 1 200 kN |
| Ferraillage imposé | HA16, 3 barres par face // X, 4 par face // Y → **10 HA16** |

Le ferraillage est imposé pour rendre chaque cote calculable à la main.

## 2. Calcul manuel

### 2.1 Enrobage — art. 4.4.1

```
Classe structurale : S4 (50 ans, C25/30 sous le seuil C30/37 de XC1)
c_min,dur = 15 mm            (tableau 4.4N, XC1 / S4)
c_min,b   = φ = 16 mm
c_min     = max(16 ; 15 ; 10) = 16 mm
c_nom     = 16 + 10 = 26 mm
```

### 2.2 Armatures longitudinales — art. 9.5.2

```
A_s      = 10 × 201,06 = 2 010,6 mm²  →  ρ = 1,34 %
A_s,min  = max(0,10 × 1 200 000 / 434,78 ; 0,002 × 150 000)
         = max(276 ; 300) = 300 mm²            ✓  2 011 ≥ 300
A_s,max  = 0,04 × 150 000 = 6 000 mm²          ✓  2 011 ≤ 6 000
n = 10 ≥ 4                                      ✓
```

### 2.3 Armatures transversales — art. 9.5.3

```
φ_t       ≥ max(6 ; 16/4 = 4) = 6 mm            →  HA6
scl,tmax  = min(20 × 16 ; b_min ; 400) = min(320 ; 300 ; 400) = 300 mm
Zone courante : 300 mm (multiple de 25 inférieur : 300)
Zone critique : 0,6 × 300 = 180 → arrondi à 175 mm
Longueur des zones critiques : plus grande dimension = 500 mm
```

### 2.4 Géométrie du ferraillage

```
Demi-portée X = 300/2 − 26 − 6 − 16/2 = 150 − 40 = 110 mm
Demi-portée Y = 500/2 − 26 − 6 − 16/2 = 250 − 40 = 210 mm
Entraxe X = 2 × 110 / (3 − 1) = 110 mm
Entraxe Y = 2 × 210 / (4 − 1) = 140 mm
```

Espacement libre minimal (art. 8.2(2)) : max(φ ; d_g + 5 ; 20) = max(16 ; 25 ; 20) = 25 mm.
Espacement libre réel : 110 − 16 = 94 mm et 140 − 16 = 124 mm ✓

Maintien des barres (art. 9.5.3(6)) : les deux entraxes (110 et 140 mm) restent sous 150 mm,
**aucune épingle n'est nécessaire**.

### 2.5 Recouvrement — art. 8.4 et 8.7

HA16 en C25/30 : l₀ = 968 mm (fiche COLUMN-01) → arrondi au multiple de 50 supérieur :
**1 000 mm**.

### 2.6 Nombre de cadres

```
Hauteur utile : 3 000 − 2 × 50 = 2 900 mm
Découpage     : 500 (bas) / 1 900 (courante) / 500 (haut)
Zone basse    : ⌈500/175⌉ + 1 = 3 + 1        = 4 cadres
Zone courante : ⌈1 900/300⌉ + 1 − 2 = 7 + 1 − 2 = 6 cadres
Zone haute    :                              = 4 cadres
Total                                        = 14 cadres
```

## 3. Comparaison

| Grandeur | Manuel | Test |
|---|---|---|
| c_nom | 26 mm | exact |
| A_s | 2 010,6 mm² | [2 009 ; 2 012] |
| φ_t | 6 mm | exact |
| Espacement courant | 300 mm | exact |
| Espacement critique | 175 mm | exact |
| Longueur zone critique | 500 mm | exact |
| Demi-portées | 110 / 210 mm | exact |
| Entraxes | 110 / 140 mm | exact |
| Épingles | 0 | exact |
| Recouvrement | 1 000 mm | exact |
| Nombre de cadres | 14 | exact |

## 4. Conclusion

✅ **Validé.** Toutes les cotes du ferraillage modélisé sont reproduites exactement.

Tests associés : `tests/DanCI.Structural.Tests/Validation/Column05DetailingTests.cs`.

Ce cas vérifie aussi que les repères de barres portent le repère du poteau (`C5-B01`,
`C5-T01`…) et qu'aucun doublon n'est produit à l'intérieur d'un élément.
