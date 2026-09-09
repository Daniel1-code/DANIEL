# DASH-02 — Les couleurs de contrôle

**Module** : synthèse, toutes familles
**Tests** : `tests/DanCI.Structural.Tests/Documentation/ControlColourTests.cs` (14 cas)
**Version** : 3.15.0

---

## Ce qu'une couleur est, et n'est pas

**Une couleur n'est pas une vérification.** Elle ne dit rien que la note de calcul ne dise
déjà ; elle dit seulement **où regarder**. C'est utile sur deux cents éléments, c'est
trompeur si l'on s'y fie seul.

---

## Les bandes

| Bande | Taux | Couleur | Ce que ça veut dire |
|---|---|---|---|
| Non dimensionné | — | `#808080` gris | Aucun résultat. **L'absence de couleur n'est pas une absence de problème.** |
| Peu sollicité | ≤ 0,50 | `#2C7BB6` bleu | Conforme, et **probablement surdimensionné** |
| Régime courant | 0,50 – 0,85 | `#ABD9E9` bleu pâle | Conforme, marge normale |
| Passe sans marge | 0,85 – 1,00 | `#FDAE61` orange | Conforme, mais toute évolution de charge le fera basculer |
| Ne passe pas | > 1,00 ou défaut | `#D7191C` rouge | Non conforme |

**Un taux de 1,000 exactement passe encore.** L'Eurocode demande E_d ≤ R_d, l'égalité est
admise ; une convention de couleur qui déclarerait le contraire contredirait la note de
calcul juste à côté.

**Une bande basse n'est pas une bonne nouvelle.** Un élément à 0,20 passe, mais c'est du
béton et de l'acier payés pour rien. La légende le dit au lieu de le peindre en vert
rassurant : ce n'est pas un défaut, c'est une économie possible.

---

## Le statut prime sur le taux, dans les deux sens

C'est le cœur de la règle, et les deux sens comptent.

**Un élément non dimensionné a un taux de zéro** — et zéro ressemble beaucoup à « passe
largement ». Il est donc gris, jamais dans la bande favorable.

**Un élément dont une vérification est en défaut est rouge même si son taux maximal reste
sous 1.** Une vérification peut échouer sur autre chose qu'un rapport : un espacement, une
longueur d'ancrage, une longueur de chapeau — le § 9.3.1.2(2) en est justement un exemple.
Peindre celui-là en bleu serait exactement le contresens que la couleur doit éviter.

---

## La palette est lisible en vision dichromate

La rampe va du **bleu au rouge** (RdYlBu à quatre classes), et non du vert-orange-rouge
habituel. **Un deutéranope confond le vert et le rouge**, c'est-à-dire exactement « ça
passe » et « ça ne passe pas » : le seul couple qu'une convention de contrôle ne peut pas se
permettre de rendre ambigu. Un test refuse tout vert dominant dans la palette.

Chaque bande porte en outre un **libellé** et une **signification**, pour que la légende se
lise sans la couleur du tout — impression noir et blanc et photocopie comprises. Une vue
colorée sans légende n'est pas un document : elle demande à son lecteur de deviner la
convention.

---

## Une asymétrie assumée

Un test compare les deux lectures d'un même projet — le tableau et les couleurs — et exige
qu'elles désignent les mêmes éléments. Il a trouvé le seul cas où elles divergent : une
vérification rendue **satisfaite** avec un taux supérieur à 1. Le tableau la classe conforme
(il lit le statut) ; la couleur la peint en rouge (elle lit aussi le rapport).

Aucun module ne produit cela. Mais l'asymétrie est conservée, parce que **son sens est le
seul acceptable** : elle peut attirer l'attention sur un élément qui va bien, jamais la
détourner d'un élément qui va mal.

---

## Limites

- L'**application** de ces couleurs à une vue Revit — filtres de vue, remplacements
  graphiques — n'est pas faite. Seule la règle l'est. C'est elle qui porte le raisonnement ;
  la poser dans une vue ne se valide que dans Revit.
- Les bornes 0,50 / 0,85 / 1,00 sont un **choix de lecture**, pas une norme. Aucun Eurocode
  ne dit qu'un élément à 0,84 se lit différemment d'un élément à 0,86.
