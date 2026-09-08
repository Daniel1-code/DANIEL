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
| `STRIP-` | Semelles filantes |
| `SLAB-` | Dalles |
| `WALL-` | Voiles |
| `GRADE-` | Longrines |

## État

| Fiche | Sujet | État |
|---|---|---|
| COLUMN-01 | Ancrage et recouvrement, C25/30 B500 HA16 | ✅ Validé |
| COLUMN-02 | Diagramme d'interaction N-M 400×400 | ✅ Validé (écart de modèle documenté) |
| COLUMN-03 | Élancement et second ordre, poteau 300×300 élancé | ✅ Validé |
| COLUMN-04 | Interaction biaxiale | ✅ Validé |
| COLUMN-05 | Dispositions constructives complètes 300×500 | ✅ Validé |
| COLUMN-06 | Enrobage EC2 §4.4.1 (tableaux 4.3N et 4.4N) | ✅ Validé |
| BEAM-01 | Poutre isostatique 300×600 : flexion, tranchant, zonage des cadres | ✅ Validé |
| BEAM-02 | Poutre en T, travée de rive, cas limites et optimiseur | ✅ Validé |
| FOOT-01 | Semelle isolée centrée 2400×2400×600 : sol, flexion, poinçonnement | ✅ Validé |
| FOOT-02 | Semelle rectangulaire excentrée : Meyerhof, glissement, tranchant biaxial | ✅ Validé |
| SLAB-01 | Dalle isostatique 220 mm sur 5,00 m : flexion, tranchant, flèche, fissuration | ✅ Validé |
| SLAB-02 | Ce qui gouverne réellement une dalle (flèche, continuité, bidirectionnel) | ✅ Validé |
| WALL-01 | Voile porteur 200 mm sur 3,00 m : flambement, second ordre, art. 9.6 | ✅ Validé |
| WALL-02 | Les limites du voile (poteau déguisé, raidisseur inutile, contreventement) | ✅ Validé |
| STRIP-01 | Semelle filante 900×400 sous voile : minimum, tranchant absent, ancrage | ✅ Validé |
| STRIP-02 | Ce qui la distingue d'une semelle isolée (rigidité, excentrement, poinçonnement) | ✅ Validé |
| GRADE-01 | Longrine 300×500 sur 5,00 m en zone sismique : tirant, minimum 0,4 %, cadres | ✅ Validé |
| GRADE-02 | Ce qui la distingue d'une poutre posée bas (liaison, section minimale, appui du sol) | ✅ Validé |

Toutes les fiches sont adossées à des tests automatisés exécutés par la CI : si le moteur
s'écarte d'un calcul manuel, le build casse.

## Une fiche se calcule à la main, jamais d'après le moteur

En septembre 2026, une erreur d'inversion du moment réduit a été trouvée dans
`BendingDesign`, la routine que **tous** les modules de flexion traversent. Elle avait
vécu de la phase 2 à la phase 7 parce que le cas de référence de ses tests avait été écrit
d'après le code : son commentaire recopiait le coefficient erroné, si bien que le test
confirmait le code au lieu de le contrôler.

Deux règles en sont sorties, et elles s'appliquent à toute nouvelle fiche :

1. **Le calcul manuel se fait avant de lire la sortie du moteur**, avec l'article de la
   norme sous les yeux. Une valeur du moteur recopiée dans une fiche ne valide rien.
2. **Quand une fonction expose les deux sens d'une même relation, leur composition doit
   être testée** : elle doit rendre l'identité. C'est ce contrôle-là, impossible à écrire
   d'après le code, qui a fini par révéler l'erreur.
