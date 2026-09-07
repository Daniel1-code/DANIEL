# SLAB-02 — Ce qui gouverne réellement une dalle

## 1. Énoncé

Une dalle est rarement limitée par sa résistance. Cette fiche ne valide pas une formule
de plus : elle vérifie que le moteur **annonce** les trois situations qu'un module de dalle
ne doit jamais masquer.

| Cas | Question posée |
|---|---|
| a | Une dalle mince sur grande portée échoue-t-elle sur la flèche, et non sur la flexion ? |
| b | Les coefficients de continuité sont-ils annoncés comme n'étant pas de l'Eurocode ? |
| c | Un panneau presque carré est-il signalé comme portant dans deux sens ? |

Matériaux communs : C25/30, B500, XC1, habitation, g additionnel 2,0 kN/m², q 2,5 kN/m².

## 2. Calcul manuel

### 2.a Dalle de 160 mm sur 6,00 m — la flèche gouverne

```
Poids propre = 0,160 × 25 = 4,00      g = 6,00 kN/m²      q = 2,50 kN/m²
w_ELU = 1,35 × 6,00 + 1,50 × 2,50 = 11,85 kN/m²
M     = 11,85 × 6,00² / 8 = 53,3 kN·m/m
```

**La flexion passe.** Avec d ≈ 130 mm, le moteur trouve une nappe qui fournit l'acier
requis : la résistance n'est pas le problème.

**La flèche refuse.**

```
l/d réel = 6 000 / 130 ≈ 46
(l/d) admissible, éq. 7.16a avec K = 1,0 : de l'ordre de 26 à 30
```

Le rapport dépasse la limite d'environ 60 %. Aucune quantité d'acier ne rattrape cela :
la correction A_s,prov/A_s,req est plafonnée à 1,5 par l'article lui-même. **La seule
réponse efficace est d'épaissir**, et c'est ce que le message du moteur doit dire.

Contre-épreuve : la même portée avec **h = 260 mm** passe.

### 2.b Travée de rive — des coefficients qui ne sont pas de l'Eurocode

Pour une travée continue, le moteur applique :

```
M_travée = w l² / 11        M_appui = w l² / 9        V = 0,60 w l
```

Ces valeurs sont des **coefficients de continuité usuels**. L'Eurocode 2 ne fournit aucun
tableau de ce type : il demande une analyse de la structure (art. 5.1.1), linéaire ou avec
redistribution. Les utiliser est une pratique courante et acceptable en avant-projet, mais
les présenter comme « conformes EC2 » serait faux.

Le moteur les emploie donc en **le disant deux fois** : dans la note de calcul, et dans un
avertissement qui remonte jusqu'au tableau de résultats. Et il accepte, en alternative, les
moments issus d'une analyse extérieure — auquel cas plus rien n'est estimé.

### 2.c Panneau 5,00 × 6,00 m — la dalle porte dans deux sens

```
Rapport de côtés = 6 000 / 5 000 = 1,20  <  2
```

En deçà de 2, une dalle appuyée sur ses quatre côtés partage la charge entre ses deux
directions. Le calcul en bande unique :
- **surestime** le ferraillage dans la direction déclarée,
- **oublie** entièrement celui de l'autre direction — qui est le vrai danger.

Le moteur ne refuse pas de calculer, mais il signale que le résultat n'est pas
représentatif et que le module ne traite pas encore les dalles bidirectionnelles.

Cas voisin : une portée déclarée de 6 000 mm sur un panneau de 4 000 mm de large. Une dalle
porte par le plus court chemin ; la portée a donc été déclarée dans le mauvais sens, et le
moteur le dit.

## 3. Résultat moteur

`Slab02ServiceTests`, 8 cas.

| Cas | Attendu | Moteur |
|---|---|---|
| Dalle 160 mm / 6 m | flexion OK, flèche en échec | conforme |
| Message d'échec | nomme « épaissir » | conforme |
| Même portée à 260 mm | flèche OK, état « OK » | conforme |
| Travée de rive | M_appui > M_travée | conforme |
| Travée de rive | note et avertissement « hors Eurocode » | conforme |
| Travée de rive | chapeaux posés d'eux-mêmes | conforme |
| Moments saisis | repris tels quels, aucun avertissement de coefficient | conforme |
| Panneau 5 × 6 m | avertissement « deux sens », état « À vérifier » | conforme |
| Portée dans le sens long | avertissement « plus court chemin » | conforme |
| Console 1,50 m | M = w l²/2 = 14,85 kN·m/m, K = 0,4, acier en haut | conforme |
| Stockage vs bureaux | même ELU, σ_s plus élevée en stockage | conforme |

Le dernier cas mérite un mot : à l'ELU, un plancher de bureaux et un plancher de stockage
supportant la même charge donnent **exactement le même ferraillage**. C'est à l'ELS que la
catégorie d'usage compte, par ψ₂ (0,3 contre 0,8) : la combinaison quasi-permanente est plus
lourde en stockage, donc σ_s plus élevée, donc la fissuration plus difficile à maîtriser.
Un moteur qui ignorerait la catégorie d'usage ne verrait pas la différence.

## 4. Comparaison

Cette fiche ne compare pas des nombres mais des **comportements**. Chaque ligne du tableau
ci-dessus est un test qui échoue si le moteur cesse de signaler la situation.

## 5. Conclusion

Le module dit ce qu'il fait et ce qu'il ne fait pas : la flèche gouverne et il le montre,
les coefficients de continuité sont hors Eurocode et il l'écrit, un panneau bidirectionnel
n'est pas traité et il le signale au lieu de rendre un résultat rassurant et faux.

## 6. Limites connues

- **Les dalles portant dans deux sens ne sont pas calculées.** Elles sont détectées et
  signalées, rien de plus. Les tableaux de coefficients pour panneaux rectangulaires
  (Marcus, Pigeaud, BS 8110) ne sont pas de l'Eurocode et n'ont pas été introduits.
- **Les planchers-dalles** (appuis ponctuels) ne sont pas traités : ni la répartition en
  bandes d'appui et de travée, ni le poinçonnement intégré à ce module.
- **Aucune analyse de continuité réelle.** Le moteur ne construit pas de modèle de poutre
  continue : les moments de travée continue sont estimés ou saisis.
- **Trémies et découpes** ne sont pas prises en compte dans la portée : le lecteur signale
  un panneau non rectangulaire, il ne le redécoupe pas.
- **Rien n'a encore été exécuté dans Revit.**
