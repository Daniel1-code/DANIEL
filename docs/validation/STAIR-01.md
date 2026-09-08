# STAIR-01 — Volée droite de 9 contremarches, paillasse 180 mm, palier participant

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Contremarches | 9 × 170 mm → dénivelé 1 530 mm |
| Girons | **8** × 280 mm → projection 2 240 mm |
| Paillasse | 180 mm, mesurée **perpendiculairement à la pente** |
| Largeur de volée | 1 200 mm |
| Palier | 180 mm d'épaisseur, 1 300 mm compris dans la portée |
| Mode d'appui | volée + palier entre appuis → **L = 3 540 mm** |
| Béton | C25/30 → f_cd = 16,667 MPa, f_ctm = 2,565 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC1, escalier intérieur |
| Revêtement de marche | 1,00 kN/m² sur la projection horizontale |
| Enduit de sous-face | 0,30 kN/m² sur la surface inclinée |
| Charge d'exploitation | q_k = 3,00 kN/m² |

Le calcul porte sur une **bande de 1 mètre** de largeur de volée.

## 2. Calcul manuel

### 2.1 Géométrie — le compte des girons

```
9 contremarches  →  dénivelé  = 9 × 170 = 1 530 mm
                 →  8 GIRONS  = 8 × 280 = 2 240 mm
```

Une volée de n contremarches ne compte que **n − 1 girons** : la dernière contremarche
débouche sur le palier, dont le nez de marche est le bord. Compter 9 girons allongerait la
volée de 280 mm et fausserait à la fois la portée et la pente.

```
tan α = 170/280 = 0,6071      α = 31,26°
cos α = 280 / √(280² + 170²) = 280 / 327,57 = 0,85479
```

**Blondel** : 2R + G = 2 × 170 + 280 = **620 mm**, dans la plage confortable de 600 à
650 mm. C'est une règle d'ergonomie ; elle ne relève d'aucun Eurocode et n'entre dans
aucune vérification de résistance.

### 2.2 Poids propre — les deux corrections que tout le monde oublie

```
Paillasse : γ t / cos α  = 25 × 0,180 / 0,85479 = 5,264 kN/m²
Marches   : γ R / 2      = 25 × 0,170 / 2       = 2,125 kN/m²
Revêtement de marche                             = 1,000 kN/m²
Sous-face : 0,30 / cos α                         = 0,351 kN/m²
                                                 ---------------
g volée                                          = 8,740 kN/m²
```

Deux corrections, deux raisons :

- **La paillasse est mesurée perpendiculairement à la pente.** La hauteur de béton
  au-dessus d'un point du plan vaut donc t / cos α, pas t.
- **Les marches pèsent.** Chaque marche est un prisme triangulaire de section R × G / 2
  occupant un giron G en plan, soit γ R / 2 par m² de projection. Le giron se simplifie :
  ce terme est purement géométrique et **ne dépend pas de la pente**.

```
Calcul naïf : γ t = 25 × 0,180                   = 4,500 kN/m²
Réel  (hors revêtements) : 5,264 + 2,125          = 7,389 kN/m²
Écart : 2,889 / 7,389                             = 39 % du poids propre réel
```

Toujours du côté **non sécuritaire**.

Le palier, lui, est une dalle horizontale ordinaire : ni pente, ni marches.

```
g palier = 25 × 0,180 + 1,00 + 0,30 = 5,800 kN/m²
```

### 2.3 Combinaisons — EN 1990 éq. 6.10

```
w1 (volée)  = 1,35 × 8,740 + 1,50 × 3,00 = 16,300 kN/m²
w2 (palier) = 1,35 × 5,800 + 1,50 × 3,00 = 12,330 kN/m²
```

L'article 6.3.1(1) de l'EN 1991-1-1 rappelle qu'un escalier n'a **pas de catégorie d'usage
propre** : il prend celle de la zone qu'il dessert, et c'est elle qui fixe q_k et ψ₂.

### 2.4 Statique — deux charges, pas une

La volée et le palier ne pèsent pas la même chose. Étaler la charge de volée sur toute la
portée serait sécuritaire mais faux ; la solution exacte est fermée.

```
W1 = 16,300 × 2,240 = 36,512 kN/m       W2 = 12,330 × 1,300 = 16,029 kN/m

R_bas  = [36,512 × (3,540 − 1,120) + 16,029 × 1,300/2] / 3,540 = 27,903 kN/m
R_haut = 52,541 − 27,903                                        = 24,637 kN/m

Effort tranchant nul à x = 27,903 / 16,300 = 1,712 m, DANS la volée
M_Ed = 27,903 × 1,712 − 16,300 × 1,712²/2 = 23,88 kN·m/m
```

À comparer à ce qu'aurait donné la charge de volée partout :

```
16,300 × 3,540² / 8 = 25,53 kN·m/m     soit +6,9 %
```

Le moteur rend **les deux valeurs**, pour que l'écart soit visible plutôt que supposé.

### 2.5 Enrobage — art. 4.4.1

```
XC1, C25/30, géométrie de dalle → classe S3 → c_min,dur = 10 mm
c_min = max(12 ; 10 ; 10) = 12 mm      c_nom = 12 + 10 = 22 mm
d = 180 − 22 − 12/2 = 152 mm
```

La hauteur utile se mesure sur l'**épaisseur de paillasse**, qui est déjà perpendiculaire
à la pente : aucune correction supplémentaire.

### 2.6 Flexion — art. 6.1

```
μ    = 23,88·10⁶ / (1 000 × 152² × 16,667) = 0,0620
x/d  = 1,25 (1 − √(1 − 2 × 0,0620))         = 0,0801
z    = 152 × (1 − 0,4 × 0,0801)             = 147,1 mm
A_s  = 23,88·10⁶ / (147,1 × 434,78)         = 373 mm²/m
```

**Minimum** — art. 9.3.1.1(1) renvoyant à 9.2.1.1(1) :

```
A_s,min = max(0,26 × 2,565/500 × 1 000 × 152 ; 0,0013 × 1 000 × 152)
        = max(203 ; 198) = 203 mm²/m       ✓ 373 > 203, il ne gouverne pas
```

Le moment est calculé sur la projection horizontale et appliqué à la section de paillasse.
La composante réellement portée par la section vaut M cos α ; retenir M entier est
**sécuritaire**, et le moteur le dit plutôt que de le supposer.

### 2.7 Le nœud volée-palier — le point dur

Au raccordement, la sous-face forme un **angle rentrant**, et la nappe inférieure y est
tendue par le moment de flexion.

Une barre qui suivrait le pli développerait, à l'intérieur du coude, une résultante
dirigée **vers l'extérieur du béton**. Elle ferait sauter l'enrobage, et le nœud céderait
avant la section courante — alors même que le calcul de section serait parfaitement juste.

Le détail correct fait **se croiser** les deux nappes :

| Barre | Trajet |
|---|---|
| Nappe de la **volée** | monte la sous-face, traverse l'épaisseur au droit du pli, s'ancre dans la **face supérieure du palier** |
| Nappe du **palier** | vient de l'appui haut, atteint le pli, s'ancre dans la **face supérieure de la paillasse** |

```
l_bd (HA12) arrondi à 500 mm

Longueur disponible au-delà du pli :
  côté palier    : 1 300 mm
  côté paillasse : 2 240 × 0,85479 = 1 914 mm en projection
  → la plus courte gouverne : 1 300 mm      ✓ 500 ≤ 1 300
```

**Ce que le moteur ne fait pas** : l'EN 1992-1-1 ne donne aucun article propre aux nœuds
d'escalier. Leur justification relève du modèle bielles-tirants des articles 5.6.4 et 6.5,
que le moteur ne construit pas. Il vérifie ce qui est vérifiable — la longueur d'ancrage
disponible — pose le détail de la pratique établie, et laisse le rendement du nœud à
l'ingénieur. C'est écrit en avertissement, pas en note de bas de page.

### 2.8 Effort tranchant et flèche

```
V_Ed = 27,90 kN/m
ρ_l = 452/(1 000 × 152) = 0,297 %      k = 1 + √(200/152) = 2,00 (plafonné)
V_Rd,c ≈ 75 kN/m                        taux < 0,40      ✓

l/d = 3 540 / 152 = 23,3
ρ = 373/(1 000 × 152) = 0,246 %  <  ρ_0 = 0,5 %  →  éq. 7.16a
l/d limite = 11 + 1,5 × 5 × (0,5/0,246) + 3,2 × 5 × (0,5/0,246 − 1)^1,5 = 43,1
             × A_s,prov/A_s,req                                          ≈ 52
```

La flèche passe largement **parce que la paillasse est épaisse**. Elle ne pardonne pas
l'inverse : STAIR-02 montre qu'à 120 mm la même volée échoue par la flèche, pas par la
résistance.

La majoration de 15 % souvent accordée à la flèche des escaliers vient de la **BS 8110**.
Elle n'existe pas dans l'EN 1992-1-1, et le moteur ne l'applique pas.

## 3. Résultat moteur

`Stair01DesignTests`, 17 cas — plus `StairActionsTests` (16 cas) et `StairStaticsTests`
(9 cas) pour les briques.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| Girons | 8 | 8 | 0 % |
| cos α | 0,85479 | 0,85479 | 0 % |
| g volée | 8,740 kN/m² | 8,740 kN/m² | 0 % |
| g palier | 5,800 kN/m² | 5,800 kN/m² | 0 % |
| w1 / w2 | 16,300 / 12,330 | 16,300 / 12,330 | 0 % |
| R_bas / R_haut | 27,903 / 24,637 | idem | 0 % |
| x du moment maximal | 1,712 m | 1,712 m | 0 % |
| M_Ed | 23,88 kN·m/m | 23,88 kN·m/m | 0 % |
| Enrobage / d | 22 / 152 mm | 22 / 152 mm | 0 % |
| A_s requis | 373 mm²/m | 365–385 mm²/m | < 3 % |
| A_s,min | 203 mm²/m | 198–208 mm²/m | < 3 % |
| Ancrage disponible au nœud | 1 300 mm | 1 300 mm | 0 % |
| l/d | 23,3 | 23,3 | 0 % |
| Nappes croisées au nœud | oui | vérifié géométriquement | — |

## 4. Comparaison

Les écarts sont nuls : la descente de charge et la statique sont exactes, et le reste est
de l'arithmétique de section. Ce n'est pas la concordance numérique qui compte ici, c'est
que **les deux corrections de poids propre et le détail du nœud soient dans le moteur**.

Un test vérifie géométriquement que les deux nappes inférieures **remontent au-delà du
pli** au lieu de le suivre, et il est rejoué pour quatre longueurs de palier dans STAIR-02.
C'est le seul moyen de garantir qu'aucun réglage ne produira le mauvais détail.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'une volée courante et traite les trois choses qui
la distinguent d'une dalle : son poids propre corrigé de la pente et des marches, sa
portée sous deux charges différentes, et son nœud en angle rentrant.

## 6. Limites connues

- **Le rendement du nœud n'est pas calculé**, seulement détaillé et sa longueur d'ancrage
  vérifiée. Le modèle bielles-tirants des articles 5.6.4 et 6.5 n'est pas implémenté.
- **La charge concentrée Q_k de l'art. 6.3.1.2(1) n'est pas combinée.** Elle est rappelée :
  sur une volée courante la charge répartie gouverne, mais l'affirmer sans le vérifier
  serait une hypothèse cachée.
- **Volées droites uniquement.** Escaliers balancés, hélicoïdaux, à marches en console ou
  à limon central ne sont pas traités et ne sont pas détectés par le moteur.
- **Pas de fissuration ni de calcul de flèche détaillé** (art. 7.4.3).
- **Rien n'a encore été exécuté dans Revit**, et pour ce module la remarque porte plus
  loin qu'ailleurs : rien ne garantit qu'un escalier Revit accepte des armatures. Le
  lecteur pose la question à l'API et rapporte la réponse, mais elle n'a jamais été
  observée en conditions réelles.
