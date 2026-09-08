# STAIR-05 — Le ferraillage conçu sur des paramètres

## 1. Énoncé

Jusqu'ici, le constructeur de plan de l'escalier décidait **seul** jusqu'où vont les
chapeaux, comment se traite le nœud, dans quel ordre se posent les nappes. Ces choix
étaient corrects, mais **figés** : une disposition prédéfinie plaquée sur une géométrie,
que l'ingénieur ne pouvait ni voir ni changer.

Un plan de ferraillage n'est pas une forme qu'on applique. C'est une **suite de décisions**,
dont chacune a une raison. Cette fiche vérifie que ces décisions sont devenues des
paramètres, que chaque paramètre **change réellement le plan**, et que chacun sort avec sa
raison.

> Une longueur sans sa raison n'est pas un paramètre, c'est un nombre magique.

## 2. Les décisions, et ce qui les fixe

### 2.1 Ce que la géométrie impose — et qui n'est donc pas un paramètre

| Fait géométrique | Conséquence, non négociable |
|---|---|
| Palier dans la portée | Il y a un nœud volée-palier |
| Pas de palier | Aucun nœud, barres droites d'appui à appui |
| Portée transversale | Les porteuses traversent la volée, le pli ne les concerne pas |
| Pente α | L'enrobage vaut c/cos α, la répétition suit la tangente |

Ces choses-là ne se règlent pas : les changer serait faux, pas différent.

### 2.2 Ce qui est décidé — et devient donc paramètre

| Décision | Défaut | D'où vient le défaut |
|---|---|---|
| Longueur des chapeaux | max(L/4 ; l_bd) | **Pratique courante**, pas l'EC2 |
| Détail du nœud | nappes croisées | Pratique établie pour un angle rentrant tendu |
| Ancrage au nœud | 1,0 × l_bd | §8.4.4, l'ancrage de calcul |
| Ordre des nappes | répartition au-dessus | Les porteuses doivent avoir le plus grand d |
| Barre de stock | 12 000 mm | Longueur commerciale usuelle |
| Pas d'arrondi | 50 mm | Pratique de façonnage |

**La longueur des chapeaux ne relève d'aucun article.** L'Eurocode demande une enveloppe
de moments décalée de a_l ; le moteur ne la construit pas. La fraction de portée est une
approximation de pratique courante, et le moteur l'écrit dans la raison — il ne la fait pas
passer pour normative.

### 2.3 Les modes de chapeaux

```
FractionOfSpan  →  max(f·L ; l_bd)     f = 0,25 par défaut
AnchorageOnly   →  l_bd                le minimum défendable
Fixed           →  valeur imposée      appliquée, non justifiée
FullSpan        →  nappe continue      d'appui à appui
```

Sur la volée de référence (L = 3 540 mm, HA12) :

```
L/4 = 885 mm  >  l_bd  →  885 mm, arrondi à 900 mm au pas de 50
f = 0,40      →  1 416 mm         →  1 450 mm
AnchorageOnly →  l_bd             →  plus court que les deux
```

### 2.4 Les deux détails de nœud, et celui qui n'est pas proposé

| Détail | Ce qu'il fait | Ce qu'il coûte |
|---|---|---|
| **Nappes croisées** | chaque nappe s'ancre dans la face opposée | encombre le nœud si les diamètres sont gros |
| **Épingle diagonale** | les nappes s'arrêtent au pli, une épingle franchit l'angle | une barre de plus |

Les deux sont admis pour un angle rentrant tendu. **Aucune variante suivant le pli n'est
proposée, et ce n'est pas un oubli** : une barre qui suit le pli développe à l'intérieur du
coude une résultante dirigée vers l'extérieur du béton, et fait sauter l'enrobage
(voir STAIR-01 §2.7). Un paramètre qui permettrait de produire ce détail serait un piège,
pas une liberté.

### 2.5 Le paramètre qu'on peut majorer mais pas réduire

```
KneeAnchorageFactor ≥ 1,0
```

Majorer l'ancrage au nœud est un choix d'ingénieur légitime. Le **réduire** ne l'est pas :
l_bd est une longueur de calcul, pas une préférence. Un coefficient inférieur à 1 est
**refusé à la validation**, avec le message qui le dit : *« réduire l'ancrage de calcul
n'est pas un réglage, c'est une faute. »*

### 2.6 Une barre plus longue que le stock est signalée, pas découpée

Une barre de 14 m n'existe pas. Mais découper automatiquement placerait **tous les
recouvrements au même endroit**, créant une section affaiblie sur toute la largeur de la
volée. Répartir les recouvrements en quinconce est une décision de plan.

Le moteur signale donc, nomme la barre, sa longueur et le recouvrement nécessaire — et
laisse l'ingénieur trancher.

## 3. Résultat moteur

`Stair05DetailingTests`, 18 cas. **Chaque test vérifie qu'un paramètre change réellement le
plan** : un réglage qu'on peut modifier sans que rien ne bouge n'est pas un paramètre,
c'est un champ mort.

| Cas | Attendu | Moteur |
|---|---|---|
| Toute décision porte une question et une raison | oui | conforme |
| Fraction de portée annoncée hors Eurocode | « PRATIQUE COURANTE », « enveloppe de moments » | conforme |
| Défaut : max(L/4 ; l_bd) arrondi | 900 mm | conforme |
| f = 0,40 | 1 450 mm | conforme |
| AnchorageOnly plus court que le défaut | oui, + « minimum défendable » | conforme |
| Longueur imposée 1 200 mm | appliquée, « ne la justifie pas » | conforme |
| FullSpan remplace les deux chapeaux | nappe continue, aucun chapeau | conforme |
| FullSpan reste dans le béton | oui | conforme |
| Défaut : nappes croisées | oui, épingle absente | conforme |
| Épingle : nappes arrêtées + barre en plus | oui, croisement absent | conforme |
| Épingle reste dans le béton | oui | conforme |
| Facteur d'ancrage 1,5 allonge | oui | conforme |
| Facteur 0,7 refusé | « pas un réglage, c'est une faute » | conforme |
| Répartition au-dessus par défaut | z plus haut que la porteuse | conforme |
| Ordre inversé descend la répartition | oui, + le coût annoncé | conforme |
| Pas d'arrondi de 100 mm | longueur multiple de 100 | conforme |
| Barre > stock signalée, non découpée | avertissement citant « quinconce » | conforme |
| Deux dispositions ≠ même empreinte | oui | conforme |
| Cloner ne partage pas les règles | oui | conforme |

Les deux dispositions nouvelles passent en outre le contrôle de STAIR-04 : **tout point de
toute barre dans le béton**. Un paramètre qui produirait des barres dehors serait pire
qu'un paramètre absent.

## 4. Comparaison

Aucun écart numérique à comparer : cette fiche porte sur des **décisions** et sur leur
visibilité.

Deux points méritent d'être notés.

**L'empreinte du calcul inclut les dispositions.** Deux plans issus de règles différentes ne
sont pas le même ferraillage. Sans cela, relancer la commande sur un élément déjà armé
croirait retrouver le même résultat alors qu'il a changé.

**Le clonage des réglages copie les règles.** `MemberwiseClone` partage la référence :
modifier les dispositions d'un réglage cloné aurait modifié l'original. Un test le verrouille.

## 5. Conclusion

Ce que la géométrie impose reste imposé. Ce qui était décidé en silence est devenu un
paramètre visible, réglable, et accompagné de sa raison — dans la fenêtre comme dans la
note de calcul.

## 6. Limites connues

- **Le nombre de paramètres est volontairement petit.** Chacun correspond à une décision
  qu'un ingénieur prend réellement sur un plan de volée. Multiplier les réglages sans que
  chacun change quelque chose de significatif ferait une fausse liberté.
- **Aucun paramètre ne permet un détail incorrect.** Le pli suivi au nœud n'est pas
  offert ; l'ancrage n'est pas réductible. Ce sont des limites assumées.
- **Les recouvrements ne sont pas placés** : le dépassement de la barre de stock est
  signalé, la répartition en quinconce reste manuelle.
- **Les dispositions ne sont pas persistées dans le modèle** : elles sont dans l'empreinte
  du calcul, mais pas restituées telles quelles à la réouverture d'un élément déjà armé.
- **Les autres modules n'ont pas encore de couche de dispositions équivalente.** Poutre,
  poteau, dalle et les autres gardent leurs décisions en dur. C'est la suite naturelle de
  ce travail.
- **Rien de tout cela n'a été rejoué dans Revit.**
