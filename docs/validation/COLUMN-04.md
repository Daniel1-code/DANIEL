# COLUMN-04 — Interaction biaxiale

## 1. Énoncé

Section et matériaux de la fiche COLUMN-02 : 400 × 400, C25/30, B500, 8 HA20.

| Donnée | Valeur |
|---|---|
| N_Ed | 1 500 kN |
| M_Ed,x | 100 kN·m |
| M_Ed,y | 60 kN·m |
| M_Rd,x = M_Rd,y | 236,6 kN·m (fiche COLUMN-02) |

## 2. Calcul manuel — art. 5.8.9(4)

### 2.1 Effort normal résistant

```
N_Rd = A_c f_cd + A_s f_yd = 160 000 × 16,667 + 2 513 × 434,78
     = 2 666 720 + 1 092 740 = 3 759 460 N = 3 759 kN
```

### 2.2 Exposant a

L'exposant est interpolé linéairement entre les valeurs de l'article 5.8.9(4) :

| N_Ed / N_Rd | 0,1 | 0,7 | 1,0 |
|---|---|---|---|
| a | 1,0 | 1,5 | 2,0 |

```
N_Ed / N_Rd = 1 500 / 3 759 = 0,399
a = 1,0 + 0,5 × (0,399 − 0,1) / (0,7 − 0,1) = 1,0 + 0,249 = 1,249
```

### 2.3 Vérification

```
(M_Ed,x / M_Rd,x)^a + (M_Ed,y / M_Rd,y)^a
= (100 / 236,6)^1,249 + (60 / 236,6)^1,249
= 0,4227^1,249 + 0,2536^1,249
= 0,341 + 0,180
= 0,521  ≤  1,00   ✓
```

**Taux de travail : 0,52 — la section résiste.**

### 2.4 Cas limites vérifiés

| Situation | Attendu |
|---|---|
| Flexion uniaxiale avec M_Ed = M_Rd | somme = 1,000 exactement |
| M_Ed,x = 200 et M_Ed,y = 180 | somme > 1,00 → non conforme |
| N_Ed/N_Rd ≤ 0,1 | a = 1,0 (interaction linéaire) |
| N_Ed/N_Rd ≥ 1,0 | a = 2,0 (interaction quadratique) |

## 3. Comparaison

| Grandeur | Manuel | Tolérance du test |
|---|---|---|
| N_Rd | 3 759 kN | [3 750 ; 3 770] |
| a à n = 0,40 | 1,250 | exact |
| a à n = 0,85 | 1,750 | exact |
| Taux de travail | 0,521 | [0,51 ; 0,53] |

## 4. Conclusion

✅ **Validé.**

Tests associés : `tests/DanCI.Structural.Tests/Validation/Column04BiaxialTests.cs`.

**Limite connue** : l'article 5.8.9(2) permet de **négliger** la vérification biaxiale lorsque
les excentricités relatives restent dans certaines bornes. Le moteur applique systématiquement
la formule, ce qui est **sécuritaire** mais peut être légèrement conservateur pour un poteau
faiblement fléchi dans l'une des deux directions.
