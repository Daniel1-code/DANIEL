# FOOT-01 — Semelle isolée 2400 × 2400 × 600 sous poteau 400 × 400

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Semelle | 2 400 × 2 400 × 600 mm |
| Poteau porté | 400 × 400 mm, centré |
| Béton | C25/30 → f_cd = 16,667 MPa, f_ctm = 2,565 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC2, 50 ans, coulée sur béton de propreté |
| Sol | contrainte admissible 250 kPa |
| Charge | N_Ed = 1 200 kN centrée, sans moment ni effort horizontal |
| Poids propre | inclus, béton armé à 25 kN/m³ |

## 2. Calcul manuel

### 2.1 Contraintes sous la semelle — EN 1997-1

Deux contraintes distinctes, et les confondre est l'erreur classique :

```
Poids propre        = 2,4 × 2,4 × 0,6 × 25          = 86,4 kN
N total à la base   = 1 200 + 86,4                  = 1 286,4 kN

σ_géo (sol)         = 1 286,4 / (2,4 × 2,4)         = 223,3 kPa   ≤ 250 kPa  ✓
σ_net (structure)   = 1 200 / (2,4 × 2,4)           = 208,3 kPa
```

Le poids propre de la semelle est directement équilibré par le sol qui la porte :
il charge le sol mais ne sollicite pas la console en flexion. Le calcul structurel
n'utilise donc que σ_net.

Charge centrée : e_x = e_y = 0, la résultante est dans le noyau central, la
distribution est uniforme et B' × L' = B × L.

### 2.2 Enrobage — art. 4.4.1 et 4.4.1.3(4)

```
Classe structurale S4 (XC2, C25/30 sous le seuil C35/45)
c_min,dur = 25 mm      c_min,b = φ = 12 mm
c_min = max(12 ; 25 ; 10) = 25 mm      c_nom = 25 + 10 = 35 mm
Plancher fondation, béton de propreté (4.4.1.3(4)) : c = 40 mm

d_x = 600 − 40 − 12/2            = 554 mm   (premier lit, barres // X)
d_y = 600 − 40 − 12 − 12/2       = 542 mm   (second lit, barres // Y)
d_moyen = (554 + 542)/2          = 548 mm
```

### 2.3 Flexion de la console — art. 6.1

Console encastrée au nu du poteau, débord a = (2 400 − 400)/2 = 1 000 mm :

```
M_Ed = L a² σ_net / 2 = 2 400 × 1 000² × 0,20833 / 2 = 250·10⁶ N·mm = 250 kN·m

μ    = 250·10⁶ / (2 400 × 554² × 16,667) = 0,0204
ξ    = 1,25 (1 − √(1 − 2 × 0,0204)) = 0,0258
z    = 554 × (1 − 0,4 × 0,0258) = 548,3 mm
A_s  = 250·10⁶ / (548,3 × 434,78) = 1 049 mm²
```

**Minimum** — art. 9.2.1.1(1), déterminant ici :

```
A_s,min = max(0,26 × 2,565/500 × 2 400 × 554 ; 0,0013 × 2 400 × 554)
        = max(1 773 ; 1 728) = 1 773 mm²        →  1 773 > 1 049, le minimum gouverne
        = 1 773 / 2,40 = 739 mm²/m
```

Le même calcul suivant Y avec d_y = 542 mm donne A_s,min = 1 735 mm², soit 723 mm²/m.

### 2.4 Choix des nappes — art. 9.3.1.1(3)

```
s_max = min(3h ; 400) = min(1 800 ; 400) = 400 mm
HA12 e = 150  →  1 000 × 113,1 / 150 = 754 mm²/m   ≥ 739 mm²/m  ✓  (excès 2,0 %)
HA14 e = 200  →  1 000 × 153,9 / 200 = 770 mm²/m   ≥ 739 mm²/m  ✓  (excès 4,2 %)
```

Nappe inférieure HA12 e = 150 dans les deux sens, soit 16 barres par sens.
Les deux solutions ci-dessus sont pratiquement équivalentes au regard du score de
l'optimiseur (excès d'acier + pénalité de pose) ; le test de validation vérifie donc
que la nappe couvre la section requise sans excès supérieur à 10 %, et non l'identité
exacte du vainqueur.

### 2.5 Poinçonnement — art. 6.4

Au nu du poteau, art. 6.4.5(3) :

```
u_0    = 4 × 400 = 1 600 mm
v_Ed   = 1 200 000 / (1 600 × 548) = 1,369 MPa
ν      = 0,6 (1 − 25/250) = 0,54
v_Rd,max = 0,5 × 0,54 × 16,667 = 4,50 MPa        →  taux 0,30  ✓
```

Résistance de base, art. 6.4.4(1) :

```
ρ_x = 754 / (1 000 × 554) = 0,001361      ρ_y = 754 / (1 000 × 542) = 0,001391
ρ_l = √(ρ_x ρ_y) = 0,001376
k   = 1 + √(200/548) = 1,604
v_Rd,c = 0,12 × 1,604 × (100 × 0,001376 × 25)^(1/3) = 0,291 MPa
v_min  = 0,035 × 1,604^1,5 × √25 = 0,356 MPa      →  v_min gouverne
```

Pour une semelle, l'article 6.4.4(2) impose de **balayer** les périmètres de contrôle
entre le nu et 2d, en déduisant la réaction du sol comprise à l'intérieur du périmètre
et en majorant la résistance par 2d/a. Le périmètre le plus défavorable n'est donc
pas celui à 2d :

| a (mm) | a/d | u (mm) | V_Ed net (kN) | v_Ed (MPa) | v_Rd = v_min·2d/a | taux |
|---|---|---|---|---|---|---|
| 200 | 0,36 | 2 857 | 1 074 | 0,686 | 1,949 | 0,35 |
| 350 | 0,64 | 3 799 | 970 | 0,466 | 1,114 | 0,42 |
| **400** | **0,73** | **4 113** | **929** | **0,412** | **0,974** | **0,42** |
| 500 | 0,91 | 4 742 | 836 | 0,322 | 0,780 | 0,41 |
| 1 000 | 1,82 | 7 883 | 179 | 0,041 | 0,390 | 0,11 |

Maximum vers a ≈ 0,73 d, taux ≈ 0,42.

### 2.6 Effort tranchant unidirectionnel — art. 6.2.2

Section à la distance d du nu du poteau, soit à 1 000 − 554 = 446 mm du bord :

```
V_Ed   = 0,20833 × 2 400 × 446 = 223 kN
ρ_l    = 0,001361      k = 1 + √(200/554) = 1,601
v_min  = 0,035 × 1,601^1,5 × 5 = 0,354 MPa
V_Rd,c = 0,354 × 2 400 × 554 = 471 kN               →  taux 0,47  ✓
```

Une semelle ne porte pas d'armatures d'effort tranchant : si V_Rd,c est dépassé,
la seule réponse est d'épaissir.

### 2.7 Attentes — art. 8.4

```
HA16 : f_bd = 2,25 × 1,213 = 2,73 MPa
       l_b,rqd = 16/4 × 434,78/2,73 = 637 mm
Hauteur disponible pour l'ancrage vertical = 600 − 40 − 12 − 12 = 536 mm  <  637 mm
```

L'ancrage droit ne tient pas dans l'épaisseur : le retour horizontal en pied est
indispensable, ce que le moteur signale par un **Avertissement** et non par un échec.

## 3. Résultat moteur

`Foot01DesignTests`, 14 cas.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| Poids propre | 86,4 kN | 86,4 kN | 0 % |
| σ' (aire effective) | 223,3 kPa | 223,3 kPa | 0 % |
| σ_net | 208,3 kPa | 208,3 kPa | 0 % |
| Enrobage | 40 mm | 40 mm | 0 % |
| d_x / d_y | 554 / 542 mm | 554 / 542 mm | 0 % |
| A_s requis | 739 mm²/m | ≈ 749 mm²/m | < 2 % |
| Nappe retenue | HA12 e = 150 | HA12 e = 150 | excès 2,0 % |
| Taux capacité portante | 0,893 | 0,893 | 0 % |
| v_Ed au nu | 1,369 MPa | ≈ 1,37 MPa | < 1 % |
| Périmètre critique | 0,73 d | 0,6–0,9 d | — |
| Taux poinçonnement | 0,42 | 0,38–0,47 | — |
| V_Ed / V_Rd,c | 223 / 471 kN | 215–232 / 450–490 kN | < 4 % |

## 4. Comparaison

L'écart de 2 % sur A_s,min vient de f_ctm : le calcul manuel prend 2,565 MPa arrondi,
le moteur applique 0,30 f_ck^(2/3) sans arrondi. Il ne change ni la nappe retenue ni
le fait que le minimum gouverne.

Le taux de poinçonnement est encadré plutôt que fixé : le balayage du moteur avance
par pas de limite/20, donc la position exacte du maximum dépend de ce pas. Ce qui est
vérifié, et qui compte, est que le maximum ne se situe **pas** à 2d.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'une semelle isolée centrée : contraintes
géotechnique et nette distinguées, enrobage de fondation relevé à 40 mm, minimum
d'armature déterminant, balayage des périmètres de poinçonnement, effort tranchant
à d du nu.

## 6. Limites connues

- **Charge centrée uniquement dans cette fiche.** L'excentrement est implémenté
  (aire effective de Meyerhof, glissement, renversement) mais n'est pas validé ici
  par un calcul manuel ; c'est l'objet de FOOT-02.
- **Contrainte uniforme pour la flexion.** Sous charge excentrée, le moteur retient
  une contrainte uniforme majorée (le pic de la distribution linéaire ramené à la
  part nette), ce qui est sécuritaire mais plus grossier qu'une intégration du
  trapèze sur la console.
- **Semelle rigide supposée.** Aucune vérification de la rigidité relative
  semelle/sol n'est faite ; pour une semelle élancée, la répartition linéaire des
  contraintes n'est plus fondée.
- **Pas de tassement.** Seuls les états limites ultimes géotechniques sont traités,
  pas l'ELS de tassement de l'EN 1997-1 §6.6.
- **Pas de soulèvement global ni de nappe phréatique.**
- **Rien n'a encore été exécuté dans Revit.** Le plan de ferraillage est validé
  géométriquement, pas par une pose réelle.
