# STRIP-01 — Semelle filante 900 × 400 sous voile de 200 mm

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Semelle | 900 mm de large × 400 mm d'épaisseur, longueur 10,00 m |
| Voile porté | 200 mm |
| Béton | C25/30 → f_cd = 16,667 MPa, f_ctm = 2,565 MPa |
| Acier | B500 → f_yd = 434,78 MPa |
| Exposition | XC2, 50 ans, coulée sur béton de propreté |
| Sol | contrainte admissible 200 kPa |
| Charge | N_Ed = 150 kN/m, centrée |

Le calcul porte sur un **mètre courant**.

## 2. Calcul manuel

### 2.1 Contraintes — EN 1997-1

```
Poids propre = 0,90 × 0,40 × 25              = 9,00 kN/m
N total      = 150,0 + 9,0                    = 159,0 kN/m

sigma_geo = 159,0 / 0,90                      = 176,7 kPa  ≤ 200 kPa   taux 0,88  ✓
sigma_net = 150,0 / 0,90                      = 166,7 kPa
```

Charge centrée : e = 0, la distribution est uniforme et B' = B.

### 2.2 Enrobage — art. 4.4.1.3(4)

```
XC2, C25/30, S4  →  c_min,dur = 25 mm, c_min,b = 14 mm  →  c_nom = 35 mm
Plancher fondation sur béton de propreté        →  relevé à 40 mm
d = 400 − 40 − 14/2 = 353 mm
```

### 2.3 Flexion de la console

Débord a = (900 − 200)/2 = 350 mm :

```
M_Ed = 1 000 × 350² × 0,16667 / 2 = 10,21·10⁶ N·mm = 10,21 kN·m/m

mu  = 10,21·10⁶ / (1 000 × 353² × 16,667) = 0,00492
z   = 353 × (1 − 0,4 × 0,0077) = 351,9 mm
A_s = 10,21·10⁶ / (351,9 × 434,78) = 67 mm²/m
```

**Minimum** — art. 9.3.1.1(1) renvoyant à 9.2.1.1(1) :

```
A_s,min = max(0,26 × 2,565/500 × 1 000 × 353 ; 0,0013 × 1 000 × 353)
        = max(471 ; 459) = 471 mm²/m
```

**Le minimum gouverne d'un facteur SEPT.** C'est la signature d'une semelle filante
courante : le débord est court, le moment est faible, et c'est la règle de non-fragilité
qui décide du ferraillage. Un moteur qui ne poserait que l'acier de flexion produirait
une semelle réglementairement non conforme tout en affichant une marge confortable.

### 2.4 Nappes retenues

```
s_max = min(3h ; 400) = min(1 200 ; 400) = 400 mm
HA14 e = 300  →  513 mm²/m  ≥ 471 mm²/m   ✓  (excès 8,9 %)

Répartition longitudinale, art. 9.3.1.1(2) :
  ≥ 0,20 × 513 = 103 mm²/m       s_max = min(3,5h ; 450) = 450 mm
  HA8 e = 300  →  168 mm²/m      ✓
```

### 2.5 Effort tranchant — la section n'existe pas

```
Section à d du nu :  a − d = 350 − 353 = −3 mm
```

La section réglementaire tombe **au-delà du bord de la semelle**. Il n'y a rien à
vérifier, et c'est le cas courant d'une semelle filante épaisse. Le moteur l'écrit en
note plutôt que de produire une vérification vide.

### 2.6 Poinçonnement — sans objet

Une semelle filante sous voile ne poinçonne pas : la charge arrive **répartie** sur toute
la longueur, pas concentrée sur une aire chargée. Le moteur rend une vérification
explicitement marquée *Sans objet*, avec sa justification — c'est plus honnête que de
l'omettre, ce qui laisserait croire à un oubli.

### 2.7 Ancrage des armatures transversales — le détail qui coince

```
HA14 : f_bd = 2,25 × 1,213 = 2,73 MPa
       l_b,rqd = 14/4 × 434,78 / 2,73 = 565 mm

Longueur disponible au-delà du nu = a − c = 350 − 40 = 310 mm
```

Le coefficient α₁ = 0,70 du tableau 8.2 exigerait c_d > 3φ, soit 42 mm ; l'enrobage n'en
fait que 40. **Il ne s'applique pas.**

Aucun diamètre courant ne s'ancre droit dans ce débord :

| Diamètre | l_b,rqd | 3φ | α₁ applicable | disponible |
|---|---|---|---|---|
| HA8 | 323 mm | 24 mm | oui → 226 mm | 310 mm ✓ |
| HA10 | 404 mm | 30 mm | oui → 283 mm | 310 mm ✓ |
| HA12 | 484 mm | 36 mm | oui → 339 mm | 310 mm ✗ |
| **HA14** | **565 mm** | **42 mm** | **non → 565 mm** | **310 mm ✗** |

Un **crochet d'extrémité** est donc indispensable, et le moteur le porte réellement dans
le plan de ferraillage. Il ne prétend pas pour autant avoir justifié l'ancrage : au-delà,
c'est le modèle bielles-tirants de l'article 9.8.2.2 qui gouverne, et le moteur renvoie
explicitement cette vérification à l'ingénieur.

## 3. Résultat moteur

`Strip01DesignTests`, 13 cas.

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| σ' géotechnique | 176,7 kPa | 174–180 kPa | < 2 % |
| σ nette | 166,7 kPa | 164–170 kPa | < 2 % |
| Taux capacité portante | 0,883 | 0,86–0,91 | — |
| Enrobage | 40 mm | 40 mm | 0 % |
| M console | 10,21 kN·m/m | 9,8–10,7 kN·m/m | < 5 % |
| A_s requis | 471 mm²/m | 460–485 mm²/m | < 3 % |
| Nappe | HA14 e = 300 | excès ≤ 15 % | — |
| Répartition ≥ 20 % | oui | vérifié | — |
| Section de tranchant | inexistante | aucune vérification produite | — |
| Poinçonnement | sans objet | *Sans objet* déclaré | — |
| Ancrage | crochet requis | Avertissement + crochet posé | — |

## 4. Comparaison

Les écarts viennent des arrondis du calcul manuel sur f_ctm et sur ξ. Aucun n'atteint 5 %
et aucun ne change une décision de ferraillage.

Le taux de travail maximal de cette semelle vaut **1,82**, et il est porté par la
vérification d'ancrage, pas par une insuffisance de section. C'est voulu : la valeur dit
que la longueur droite dépasse de 82 % ce que le débord permet, et l'état « À vérifier »
oriente le lecteur vers le point réel plutôt que vers une conformité rassurante.

## 5. Conclusion

Le moteur reproduit le calcul manuel d'une semelle filante courante et, surtout, il
signale les trois choses qu'un tableur maison oublie : le minimum d'armature qui gouverne,
la section de tranchant qui n'existe pas, et l'ancrage transversal qui ne tient pas dans
le débord.

## 6. Limites connues

- **L'ancrage n'est pas justifié, seulement signalé.** Le modèle bielles-tirants de
  l'article 9.8.2.2, qui est la vraie justification pour une semelle, n'est pas
  implémenté. Le crochet est posé et l'avertissement le dit.
- **Charge centrée uniquement dans cette fiche.** L'excentrement est implémenté et validé
  en STRIP-02.
- **Poteaux portés non traités.** Si la semelle reçoit aussi des charges concentrées,
  leur poinçonnement doit être vérifié séparément ; le lecteur Revit le signale, le
  moteur ne le calcule pas.
- **Semelle rigide supposée** (a ≤ 2h ici, vérifié), pas de tassement, pas de nappe
  phréatique.
- **Rien n'a encore été exécuté dans Revit.**
