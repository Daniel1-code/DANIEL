# Fiches de validation

Chaque fiche documente un cas calculé **à la main**, puis comparé au résultat du moteur.

Règle du projet : **une compilation verte n'est pas une validation.** Un module n'est
considéré comme terminé que lorsque ses fiches de validation existent et que l'écart entre
le calcul manuel et le moteur est expliqué.

## Format

```
# <CODE>-<NN> — Titre

## 1. Énoncé          géométrie, matériaux, efforts, hypothèses
## 2. Calcul manuel   chaque étape, avec l'article de la norme
## 3. Résultat moteur valeurs rendues par DanCI Structural Engine
## 4. Comparaison     tableau des écarts
## 5. Conclusion      validé / à corriger, et pourquoi
```

## Nomenclature

| Préfixe | Élément |
|---|---|
| `COLUMN-` | Poteaux |
| `BEAM-` | Poutres |
| `FOOT-` | Semelles isolées |
| `SLAB-` | Dalles |
| `WALL-` | Voiles |

## État

| Fiche | Sujet | État |
|---|---|---|
| COLUMN-01 | Ancrage et recouvrement, C25/30 B500 HA16 | ✅ Validé (test automatisé) |
| COLUMN-02 | Diagramme d'interaction N-M 400×400 | 🚧 Phase 1 |
| COLUMN-03 | Second ordre, poteau élancé | 🚧 Phase 1 |
| COLUMN-04 | Interaction biaxiale | 🚧 Phase 1 |
| COLUMN-05 | Dispositions constructives complètes | 🚧 Phase 1 |
