# FOOT-02 — Semelle rectangulaire 2400 × 3000 × 700, charge excentrée

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Semelle | 2 400 (X) × 3 000 (Y) × 700 mm |
| Poteau porté | 400 × 600 mm, centré |
| Béton | C30/37 → f_cd = 20,0 MPa, f_ctm = 2,896 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC2, 50 ans, **coulée directement contre le sol** |
| Sol | contrainte admissible 300 kPa, δ = 30°, adhérence nulle |
| Charge | N_Ed = 1 500 kN, M autour de Y = 300 kN·m, V suivant X = 90 kN |
| Poids propre | inclus, 25 kN/m³ |

Cette fiche complète FOOT-01 sur les trois points qu'elle ne couvrait pas :
**l'excentrement**, la **semelle rectangulaire**, et le fait que le grand débord
— celui qui gouverne l'effort tranchant — n'est pas forcément suivant X.

## 2. Calcul manuel

### 2.1 Efforts à la base

L'effort horizontal en tête crée un moment supplémentaire sur la hauteur de la semelle :

```
Poids propre  = 2,4 × 3,0 × 0,7 × 25          = 126 kN
N total       = 1 500 + 126                    = 1 626 kN
M à la base   = 300 + 90 × 0,70                = 363 kN·m
e_x           = 363 / 1 626                    = 0,223 m = 223 mm
B/6           = 2 400 / 6                      = 400 mm    →  e_x < B/6  ✓
```

### 2.2 Contraintes — EN 1997-1

```
Distribution linéaire (contrôle du soulèvement) :
  sigma_moy = 1 626 / (2,4 × 3,0)              = 225,8 kPa
  variation = 6 e_x / B = 6 × 223 / 2 400      = 0,558
  sigma_max = 225,8 × 1,558                    = 352 kPa
  sigma_min = 225,8 × 0,442                    = 100 kPa   →  aucune traction  ✓

Aire effective (capacité portante, annexe D) :
  B' = 2 400 − 2 × 223 = 1 954 mm        L' = 3 000 mm
  sigma' = 1 626 / (1,954 × 3,000)             = 277 kPa  ≤ 300 kPa   taux 0,92  ✓
```

### 2.3 Glissement et renversement

```
Glissement    : R_d = 1 626 × tan(30°)/1,25 = 1 626 × 0,462 = 751 kN  contre 90 kN   ✓
Renversement  : M_stb = 0,9 × 1 626 × 2,4/2 = 1 756 kN·m    contre 363 kN·m          ✓
```

### 2.4 Enrobage — art. 4.4.1.3(4)

```
XC2, C30/37 (sous le seuil C35/45) → S4 → c_min,dur = 25 mm
c_nom = max(25 ; 12) + 10 = 35 mm       →  relevé à 75 mm : coulée contre le sol
d_x = 700 − 75 − 16/2            = 617 mm
d_y = 700 − 75 − 16 − 16/2       = 601 mm
```

### 2.5 Flexion des consoles

Sous charge excentrée, le moteur retient une contrainte uniforme majorée : le pic de la
distribution linéaire ramené à la part nette du poteau seul.

```
sigma_calcul = 352 × 1 500 / 1 626           = 324,5 kPa

Console X (débord 1 000 mm, largeur 3 000) :
  M = 3 000 × 1 000² × 0,3245 / 2            = 487 kN·m
  mu = 0,0212  →  z = 611 mm  →  A_s = 1 833 mm²
  A_s,min = 0,26 × 2,896/500 × 3 000 × 617   = 2 797 mm²   →  le minimum gouverne
          = 932 mm²/m

Console Y (débord 1 200 mm, largeur 2 400) :
  M = 2 400 × 1 200² × 0,3245 / 2            = 561 kN·m
  mu = 0,0317  →  z = 595 mm  →  A_s = 2 168 mm²
  A_s,min = 0,26 × 2,896/500 × 2 400 × 601   = 2 194 mm²   →  le minimum gouverne
          de justesse (écart 1,2 %)          = 914 mm²/m
```

Nappe retenue : **HA16 e = 200** dans les deux sens (1 005 mm²/m).

### 2.6 Effort tranchant — c'est ici que se joue la fiche

Section à d du nu, dans **chaque** direction :

| Direction | Débord | d | Distance au bord | V_Ed | V_Rd,c | taux |
|---|---|---|---|---|---|---|
| X | 1 000 mm | 617 mm | 383 mm | 373 kN | 698 kN | 0,53 |
| **Y** | **1 200 mm** | **601 mm** | **599 mm** | **462 kN** | **548 kN** | **0,84** |

Le grand débord est suivant Y : c'est lui qui gouverne. Une vérification limitée à X
aurait annoncé un taux de 0,53 sur une semelle qui travaille en réalité à 0,84 — un
facteur 1,6 d'erreur sur la marge réelle.

*Ce cas a effectivement révélé le défaut : la première version du module ne vérifiait
que la direction X. La fiche a été écrite avant que le code soit corrigé.*

### 2.7 Poinçonnement — art. 6.4

```
d_moyen = (617 + 601)/2 = 609 mm       u_0 = 2 (400 + 600) = 2 000 mm
Au nu : v_Ed = 1 500 000 / (2 000 × 609) = 1,23 MPa
        v_Rd,max = 0,5 × 0,528 × 20      = 5,28 MPa       taux 0,23  ✓

rho_l = sqrt(0,001629 × 0,001673) = 0,001651
k = 1 + sqrt(200/609) = 1,573
v_Rd,c = 0,12 × 1,573 × (100 × 0,001651 × 30)^(1/3) = 0,322 MPa
v_min  = 0,035 × 1,573^1,5 × sqrt(30)               = 0,378 MPa   →  v_min gouverne
```

Balayage des périmètres, art. 6.4.4(2) : maximum vers a ≈ 450–500 mm, soit 0,74–0,82 d,
avec un taux de **0,375**.

### 2.8 Attentes

```
HA20 : l_b,rqd = 20/4 × 434,78/3,041 = 715 mm
Hauteur disponible = 700 − 75 − 16 − 16 = 593 mm  <  715 mm
```

Comme en FOOT-01, l'ancrage droit ne tient pas dans l'épaisseur : le retour horizontal
est indispensable et le moteur émet un **Avertissement**, pas un échec.

## 3. Résultat moteur

`Foot02EccentricTests`, 11 cas.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| e_x | 223 mm | 218–228 mm | < 2 % |
| B' | 1 954 mm | 1 944–1 964 mm | < 1 % |
| σ' | 277 kPa | 272–283 kPa | < 2 % |
| σ_max / σ_min | 352 / 100 kPa | 344–360 / 94–106 kPa | < 3 % |
| Enrobage | 75 mm | 75 mm | 0 % |
| Taux capacité portante | 0,92 | 0,90–0,96 | — |
| R_d glissement | 751 kN | 720–780 kN | < 4 % |
| M_stb renversement | 1 756 kN·m | 1 700–1 810 kN·m | < 3 % |
| A_s requis X / Y | 932 / 914 mm²/m | 915–950 / 895–935 | < 2 % |
| V_Ed,Y / V_Rd,c | 462 / 548 kN | 445–480 / 520–575 kN | < 4 % |
| Taux tranchant Y > taux X | oui | oui | — |
| Périmètre critique | 0,74–0,82 d | 0,60–0,95 d | — |
| Taux poinçonnement | 0,375 | 0,32–0,42 | — |

Un dernier cas pousse le moment à 2 500 kN·m : la résultante sort du noyau central,
la vérification de non-soulèvement passe en **Échec** et l'état de la semelle cesse
d'être « OK ». Le moteur ne ferraille pas silencieusement une semelle qui décolle.

## 4. Comparaison

Les tolérances sont ici des fourchettes plutôt que des égalités : le calcul manuel
arrondit e_x à 223 mm et f_ctm à 2,896 MPa, alors que le moteur enchaîne les valeurs
exactes. Aucun écart n'atteint 4 %, et aucun ne change une décision de ferraillage.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'une semelle rectangulaire excentrée : moment de
la force horizontale repris sur la hauteur, aire effective de Meyerhof, contrôle du
soulèvement, glissement, renversement, et — point central de cette fiche — effort
tranchant vérifié dans les deux directions.

## 6. Limites connues

- **Contrainte uniforme majorée pour la flexion.** Le moteur ramène la distribution
  trapézoïdale à une contrainte uniforme égale au pic net. C'est sécuritaire, mais plus
  grossier qu'une intégration du trapèze sur la console : l'écart peut atteindre 15 %
  d'acier en trop pour un excentrement voisin de B/6.
- **Excentrement dans une seule direction validé ici.** Le calcul biaxial (e_x et e_y
  simultanés) est implémenté — l'aire effective se réduit dans les deux sens — mais
  n'est pas confronté à un calcul manuel.
- **Soulèvement partiel non traité.** Lorsque e > B/6, le moteur le signale et refuse
  de conclure ; il ne calcule pas la distribution triangulaire sur la surface encore
  comprimée. C'est un choix : une semelle qui décolle se redimensionne, elle ne se
  ferraille pas.
- **Semelle rigide supposée**, pas de tassement, pas de nappe phréatique
  (mêmes limites qu'en FOOT-01).
- **Rien n'a encore été exécuté dans Revit.**
