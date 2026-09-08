# GRADE-01 — Longrine 300 × 500 sur 5,00 m entre semelles, en zone sismique

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Longrine | 300 mm × 500 mm, portée 5,00 m entre nus de semelles |
| Appuis | isostatique, **suspendue** entre les deux semelles |
| Béton | C25/30 → f_cd = 16,667 MPa, f_ctm = 2,565 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC2, coulée contre coffrage sur béton de propreté |
| Charge de mur | 40 kN/m (ELU) |
| Sismique | oui, sol de classe C, α = 0,15, S = 1,15 |
| Poteaux reliés | N_Ed moyen 800 kN |
| Bâtiment | 3 niveaux |

## 2. Calcul manuel

### 2.1 Charges et efforts — statique seule

```
Poids propre = 0,30 × 0,50 × 25          = 3,75 kN/m
w            = 40 + 1,35 × 3,75          = 45,06 kN/m

M_Ed = w l²/8 = 45,06 × 5,00² / 8        = 140,8 kN·m
V_Ed = w l/2  = 45,06 × 5,00 / 2         = 112,7 kN
```

Aucun coefficient de continuité n'intervient : la longrine est isostatique et les efforts
sortent de la statique seule. Le moteur le dit en note, pour qu'on ne cherche pas une
redistribution qui n'a pas eu lieu.

### 2.2 Enrobage — art. 4.4.1.3(4)

```
XC2, C25/30  →  c_min,dur = 25 mm, Δc_dev = 10 mm  →  c_nom = 35 mm
Élément de fondation                                →  relevé à 40 mm
d = 500 − 40 − 8 (cadre) − 20/2 = 442 mm
```

Une longrine est un élément de fondation, pas une poutre de plancher : c'est le plancher
d'enrobage de l'article 4.4.1.3(4) qui s'applique, et non le seul tableau 4.4N.

### 2.3 Flexion — art. 6.1

```
mu = 140,8·10⁶ / (300 × 442² × 16,667)   = 0,1442
x/d = 1,25 (1 − √(1 − 2 × 0,1442))        = 0,1955
z   = 442 (1 − 0,4 × 0,1955)              = 407,4 mm
A_s = 140,8·10⁶ / (407,4 × 434,78)        = 795 mm²
```

### 2.4 Effort de liaison — EN 1998-5 art. 5.4.1.2(7)

C'est **la** grandeur qui fait d'une longrine autre chose qu'une poutre :

```
Sol de classe C  →  epsilon = 0,4
N = ±0,4 × 0,15 × 1,15 × 800 = ±55,2 kN     (traction ET compression, alternées)

A_s,N = 55 200 / 434,78 = 127 mm²,  répartis à parts égales : 63 mm² par nappe
```

L'effort est **axial et alterné** : il s'ajoute à la flexion dans les deux nappes à la
fois. Le partage à parts égales est la méthode manuelle usuelle pour une traction faible
combinée à de la flexion, et elle est sécuritaire ici.

### 2.5 Minimum sismique — EN 1998-1 art. 5.8.2(5)

```
0,4 % de la section, EN HAUT ET EN BAS :
A_s,min = 0,004 × 300 × 500 = 600 mm² par nappe

Pour mémoire, l'EC2 seul (art. 9.2.1.1) n'en demanderait que :
0,26 × 2,565/500 × 300 × 442 = 177 mm²
```

**Facteur 3,4.** Le minimum sismique gouverne à lui seul la nappe supérieure, et un module
qui ne connaîtrait que l'EC2 poserait trois fois trop peu d'acier en haut.

### 2.6 Sections retenues

```
Nappe inférieure : max(795 + 63 ; 600) = 858 mm²   →  3 HA20 = 942 mm²
Nappe supérieure : max(63 ; 600)       = 600 mm²   →  3 HA16 = 603 mm²
```

Les deux nappes **filent d'un appui à l'autre** et sont prolongées de l'ancrage au-delà de
chaque nu : ce n'est pas un montage constructif, la nappe supérieure travaille.

### 2.7 Effort tranchant — art. 6.2.2 et 6.2.3

```
rho_l = 942 / (300 × 442) = 0,00710      k = 1 + √(200/442) = 1,673
v_Rd,c = 0,12 × 1,673 × (100 × 0,00710 × 25)^(1/3) = 0,524 MPa
V_Rd,c = 0,524 × 300 × 442 = 69,4 kN   <   112,7 kN   →  cadres nécessaires

cot θ = 2,5 ;  z = 0,9 d = 397,8 mm
A_sw/s = 112 700 / (397,8 × 434,78 × 2,5) = 0,261 mm²/mm = 261 mm²/m
HA8 à 2 brins (101 mm²)  →  e = 386 mm
Plafond art. 9.2.2(6) : 0,75 d = 332 mm   →  e = 300 mm retenu
```

### 2.8 Effort de liaison en compression

L'effort étant alterné, il faut aussi le vérifier en compression :

```
N_Rd = A_c f_cd + A_s f_yd  ≫  55,2 kN     taux < 0,10   ✓
```

Le flambement ne concerne pas cette vérification : la longrine est maintenue sur toute sa
longueur par le sol ou par la forme perdue, il n'y a pas de longueur de flambement libre.

## 3. Résultat moteur

`Grade01DesignTests`, 13 cas.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| Charge pondérée | 45,06 kN/m | 45,0625 kN/m | 0 % |
| M_Ed | 140,8 kN·m | 140,82 kN·m | 0 % |
| V_Ed | 112,7 kN | 112,66 kN | 0 % |
| Enrobage | 40 mm | 40 mm | 0 % |
| Effort de liaison | ±55,2 kN | 55,2 kN | 0 % |
| A_s nappe supérieure | 600 mm² | 600 mm² | 0 % |
| A_s nappe inférieure | 858 mm² | 855–900 mm² | < 1 % |
| A_sw/s | 261 mm²/m | 235–290 mm²/m | — |
| Plafond d'espacement | 332 mm | 325–340 mm | < 2 % |
| Compression de liaison | taux < 0,10 | vérifié | — |
| Plan de ferraillage | 2 nappes + cadres | 3 groupes | — |

## 4. Comparaison

Les écarts sont nuls ou inférieurs au pourcent : les efforts sortent de la statique et le
reste est de l'arithmétique de section. Le point à retenir n'est pas la concordance
numérique mais **ce que le moteur ajoute** à un calcul de poutre : l'effort de liaison,
le minimum de 0,4 % sur les deux faces, et le refus de créditer l'appui du sol.

Les valeurs de cette fiche ont été recalculées après la correction de l'inversion du
moment réduit dans `BendingDesign` (voir le commit correspondant) : A_s de flexion passe
de 814 à **795 mm²**. L'ancienne valeur était trop grande, donc sécuritaire, mais fausse.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'une longrine courante en zone sismique et traite
les trois choses qui la distinguent d'une poutre : elle tire, ses deux nappes filent, et
ce sur quoi elle repose ne lui est jamais crédité sans analyse.

## 6. Limites connues

- **La traction est traitée par partage à parts égales**, pas par un diagramme
  d'interaction M-N. C'est la méthode manuelle usuelle et elle est sécuritaire pour une
  traction faible ; elle cesserait de l'être pour un effort axial dominant.
- **Aucune analyse de poutre sur sol élastique.** La longrine est toujours calculée
  suspendue, ce qui est sécuritaire en flexion ; le crédit d'un appui du sol demanderait
  un modèle de Winkler que le moteur ne fait pas.
- **Le tassement différentiel des semelles reliées n'est pas calculé**, alors qu'il est la
  sollicitation réelle de bien des longrines.
- **Pas de vérification de fissuration ni de flèche** : elles ont peu de sens pour un
  élément enterré et non visible, mais leur absence est un choix, pas un oubli.
- **Rien n'a encore été exécuté dans Revit.**
