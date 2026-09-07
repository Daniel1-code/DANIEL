# BEAM-02 — Poutre en T, travée de rive, et cas limites

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Âme | 300 × 600 mm |
| Table | 1 500 mm de large, 180 mm d'épaisseur |
| Portée | 7 000 mm, **travée de rive** |
| Béton | C30/37 |
| Moments | M_travée = 400 kN·m ; M_appui droit = 320 kN·m ; appui gauche libre |
| Efforts tranchants | 220 kN à gauche, 300 kN à droite |

## 2. Calcul manuel

### 2.1 Largeur participante — art. 5.3.2.1

```
l₀ = 0,85 L = 0,85 × 7 000 = 5 950 mm      (travée de rive, figure 5.2)
débord de chaque côté : b_i = (1 500 − 300)/2 = 600 mm
b_eff,i = min(0,2 b_i + 0,1 l₀ ; 0,2 l₀ ; b_i)
        = min(120 + 595 ; 1 190 ; 600) = 600 mm
b_eff   = 2 × 600 + 300 = 1 500 mm, plafonné à la largeur réelle → 1 500 mm
```

### 2.2 Travée : la table est comprimée

L'axe neutre est vérifié dans la table :

```
μ = 400·10⁶ / (1 500 × d² × f_cd)   →   0,8 x ≈ 60 mm  <  h_f = 180 mm
```

La section travaille donc comme un **rectangle de 1 500 mm de large**, ce qui réduit
fortement l'acier par rapport à la même poutre calculée sur son âme seule. Le test le
vérifie par comparaison directe des deux calculs.

### 2.3 Appuis : la table est tendue

Sur appui, le moment est négatif : la table est tendue et ne participe pas à la compression.
Le calcul se fait donc sur **l'âme seule (300 mm)**, ce qui demande davantage d'acier —
d'où les chapeaux.

L'appui gauche ne porte aucun moment : **aucun chapeau n'y est posé**. L'appui droit reçoit
les siens, prolongés de max(L/4 ; a_l + l_bd) depuis le nu.

### 2.4 Cadres

L'appui droit reprend 300 kN contre 220 kN à gauche : ses cadres sont **au moins aussi
rapprochés**.

### 2.5 Cas limite — section insuffisante

Poutre 200 × 400 sous M = 400 kN·m et V = 400 kN : ni la flexion ni les bielles ne passent.
Le moteur doit le **signaler explicitement**, avec les actions possibles, et ne jamais
produire un ferraillage arbitraire.

### 2.6 Optimiseur de barres

| Vérification | Attendu |
|---|---|
| 224 mm utiles, HA16, espacement libre 25 mm | 6 barres par lit |
| 224 mm utiles, HA25 | 1 + ⌊(224 − 25)/50⌋ = 4 barres |
| Largeur 10 mm | 0 barre |
| 3 000 mm² sur 224 mm utiles | passe à deux lits, jamais plus de barres qu'un lit n'en accepte |
| 1 000 mm² sur 250 mm utiles | excès d'acier ≤ 35 % |

## 3. Conclusion

✅ **Validé.**

Tests associés : `tests/DanCI.Structural.Tests/Validation/Beam02ContinuousTests.cs`.

**Limites connues** :
- la **table collaborante est déclarée par l'ingénieur**, pas déduite du modèle : dans Revit
  la dalle n'appartient pas à l'élément poutre, et la deviner reviendrait à supposer le
  modèle structurel ;
- les **conditions d'appui** (isostatique, rive, intermédiaire, console) sont également
  déclarées : elles déterminent l₀ et donc b_eff ;
- une poutre continue est traitée **travée par travée** ; la continuité des chapeaux d'une
  travée à l'autre n'est pas encore assurée automatiquement ;
- la **torsion** (art. 6.3) n'est pas implémentée ;
- les vérifications ELS — **fissuration** (art. 7.3) et **flèche** (art. 7.4) — restent à
  faire ; elles sont prévues avec le module Dalle.
