# STAIR-03 — Les trois hypothèses que le module ne fait plus

## 1. Énoncé

Cette fiche ne rejoue pas STAIR-01. Elle documente trois choses que le module **supposait**
jusqu'à la version 3.8.0 et qu'il **vérifie ou refuse** depuis.

| | Avant | Depuis |
|---|---|---|
| a | Une géométrie impossible produisait quand même un ferraillage | Le calcul est refusé |
| b | Une volée balancée ou hélicoïdale n'était ni calculée ni détectée | Elle est détectée et refusée |
| c | La charge concentrée Q_k était citée, pas vérifiée | Elle est vérifiée, et elle peut gouverner |

S'y ajoute la maîtrise de la fissuration (§7.3.3), absente jusque-là.

Volée commune : celle de STAIR-01 — 9 contremarches de 170 mm, giron 280 mm, paillasse
180 mm, palier 1 300 mm, portée 3 540 mm, C25/30, B500, XC1.

## 2. Calcul manuel

### 2.a Une géométrie impossible ne produit plus rien

Une volée d'**une seule contremarche** n'a aucun giron :

```
Girons = n − 1 = 0     →  projection horizontale = 0 mm
Portée (volée + palier) = 0 + 1 300 = 1 300 mm
```

Jusqu'à la 3.8.0 le moteur émettait un avertissement, puis **ferraillait cette portée de
1 300 mm** — une portée qui ne correspond à rien, puisqu'il n'y a pas de volée. Le résultat
avait l'apparence d'un calcul.

C'est précisément ce que ce projet s'interdit : *ne jamais générer d'armatures arbitraires
quand le calcul n'a pas de sens*. Le contrôle de géométrie est devenu une **porte** et non
un commentaire : `IsValid` est faux, le plan de ferraillage est vide, et l'avertissement
dit quoi corriger.

Le même refus s'applique à chaque dimension nulle : contremarche, giron, paillasse,
largeur. Quatre cas, quatre refus.

### 2.b Une volée non droite est détectée et refusée

C'était la limite que les fiches STAIR-01 et STAIR-02 désignaient toutes deux comme **la
plus dangereuse du module** : une volée balancée traitée comme une volée droite de mêmes
contremarches aurait rendu un résultat d'apparence normale et faux.

Une volée balancée ou hélicoïdale **porte en flexion et en torsion**, et sa portée n'est
pas la projection d'une droite. Ce n'est pas une variante de la volée droite, c'est un
autre problème — qui relève d'une analyse par éléments finis ou d'un modèle de poutre
hélicoïdale, que le moteur ne fait pas.

**La détection est géométrique**, et c'est délibéré :

```
Ligne de foulée d'une volée DROITE      →  un unique segment de droite
Ligne de foulée comportant un arc       →  hélicoïdale
Plusieurs segments non parallèles       →  balancée
```

Le critère porte sur la **ligne de foulée réelle** (`StairsRun.GetStairsPath`), et non sur
un paramètre de type dont le nom pourrait changer d'une version de Revit à l'autre. Un
critère fondé sur une énumération d'API aurait pu cesser silencieusement de fonctionner ;
un critère fondé sur la géométrie, non.

**Ne pas savoir n'est pas savoir que c'est droit.** Si la ligne de foulée ne peut pas être
lue — l'API refuse, le type de volée ne l'expose pas, ou l'élément sélectionné est un
plancher, qui ne porte aucune information de forme — la forme reste **indéterminée**. Le
moteur poursuit alors, parce que refuser bloquerait un usage légitime, mais il l'écrit et
n'endosse pas l'hypothèse à la place de l'ingénieur.

### 2.c La charge concentrée est vérifiée, et la marge n'était pas celle annoncée

L'article 6.3.1.2(1) de l'EN 1991-1-1 impose une charge concentrée Q_k sur 50 × 50 mm,
**en alternative** à la charge répartie. Jusqu'à la 3.8.0, le moteur la citait et
affirmait que la charge répartie gouverne une volée courante.

C'était vrai. Mais c'était une **hypothèse non vérifiée**, exactement le genre d'affirmation
que ce projet refuse ailleurs.

**La largeur de diffusion.** Aucun article de l'EN 1992-1-1 ne fixe la largeur sur laquelle
une charge concentrée se répartit dans une dalle portant dans un sens. Le moteur retient
donc la diffusion **la plus défavorable qui reste physiquement raisonnable** : 45° à
travers la seule épaisseur de paillasse.

```
b = 50 + 2 t = 50 + 2 × 180 = 410 mm
```

Toute diffusion plus large — à travers le revêtement, les marches, ou étalée le long de la
volée — donnerait un moment **plus faible**. La conclusion est donc du côté de la sécurité
quelle que soit la règle de diffusion retenue par ailleurs.

C'est l'inverse du choix fait pour la flèche, et pour la même raison : la majoration de
15 % de la BS 8110 est refusée parce qu'elle est **favorable**, la diffusion étroite est
retenue parce qu'elle est **défavorable**. Quand aucun article ne tranche, le moteur prend
l'hypothèse qui ne peut pas nuire.

**Volée de référence** — situation alternative : permanentes pondérées seules, plus Q_k
pondérée. La charge répartie q_k ne s'y ajoute pas, c'est l'une *ou* l'autre.

```
w1 = 1,35 × 8,740 = 11,800 kN/m²      w2 = 1,35 × 5,800 = 7,830 kN/m²
M permanentes = 16,844 kN·m/m         (même statique à deux charges)

M(Q_k) = 1,5 × 2,00 × 3,540 / (4 × 0,410) = 6,476 kN·m/m

M alternatif = 16,844 + 6,476 = 23,32 kN·m/m
M réparti                     = 23,88 kN·m/m       →  la charge répartie gouverne
```

**Mais de 2,4 % seulement.** L'affirmation d'origine était juste ; le mot « gouverne »
laissait croire à une marge confortable qui n'existe pas. C'est tout l'intérêt de vérifier
plutôt que d'affirmer.

**Volée courte** — 5 contremarches, 4 girons = 1 120 mm, sans palier, paillasse 150 mm,
Q_k = 4,00 kN :

```
b = 50 + 2 × 150 = 350 mm

M réparti     = 15,115 × 1,120² / 8                        = 2,370 kN·m/m
M permanentes = 1,35 × 7,863 × 1,120² / 8                  = 1,664 kN·m/m
M(Q_k)        = 1,5 × 4,00 × 1,120 / (4 × 0,350)           = 4,800 kN·m/m
M alternatif                                                = 6,464 kN·m/m
```

**La charge concentrée gouverne, d'un facteur 2,7**, et c'est bien son moment qui sert au
dimensionnement — pas seulement un message. Le moteur ajoute l'avertissement utile : sur
une volée courte, il faut aussi regarder le poinçonnement local de la marche, qu'il ne
calcule pas.

### 2.d La fissuration est vérifiée

Reprise exacte du module Dalle : art. 7.3.3(2), tableaux 7.2N et 7.3N, avec la contrainte
d'acier estimée depuis le rapport des combinaisons et des sections. Sur une volée
intérieure en XC1 ce critère est rarement déterminant — mais l'omettre revenait à le
supposer.

Le commentaire de la vérification le dit en toutes lettres : σ_s est une **estimation**,
pas un calcul de contrainte en section fissurée.

## 3. Résultat moteur

`Stair03RefinementTests`, 14 cas.

| Cas | Attendu | Moteur |
|---|---|---|
| a | 1 contremarche : `IsValid` faux, plan vide, « GÉOMÉTRIE INCALCULABLE » | conforme |
| a′ | 4 dimensions nulles → 4 refus | conforme |
| b | balancée et hélicoïdale : refus motivé citant la torsion | conforme |
| b′ | indéterminée : calcul poursuivi, hypothèse annoncée | conforme |
| c | b = 410 mm ; plafonnée à la largeur de volée | conforme |
| c′ | M = Q L / (4 b) = 6,476 kN·m/m | conforme |
| c″ | référence : 23,32 contre 23,88, marge de 2,4 % | conforme |
| c‴ | volée courte : 6,464 gouverne et est retenu | conforme |
| c⁗ | Q_k non saisie → *Sans objet*, taux nul | conforme |
| c⁵ | règle de diffusion annoncée hors Eurocode | conforme |
| d | fissuration §7.3.3 vérifiée, σ_s annoncée comme estimation | conforme |
| d′ | w_max imposée plus sévère → φ et s admissibles réduits | conforme |

## 4. Comparaison

Deux des trois points ne comparent aucun nombre : ils portent sur des **refus**. Le
troisième en produit, et il a corrigé une affirmation du moteur — la marge de la charge
répartie sur la charge concentrée n'était pas celle que les notes laissaient entendre.

Deux attentes de test de STAIR-02 et de `StairActionsTests` ont dû être réécrites : elles
portaient sur la formulation d'un rappel qui affirmait que la charge répartie gouverne.
Cette affirmation n'existe plus, puisqu'elle est vérifiée. Les tests détectaient
correctement le changement de comportement.

## 5. Conclusion

Le module ne suppose plus trois choses qu'il supposait : que toute volée soumise est
droite, que toute géométrie saisie est calculable, et que la charge répartie gouverne. La
première et la deuxième sont devenues des refus, la troisième une vérification.

## 6. Limites connues

- **La détection de forme dépend de ce que Revit expose.** Si la ligne de foulée n'est pas
  lisible, la forme reste indéterminée et le calcul se poursuit avec un avertissement. Une
  volée balancée dont la ligne de foulée serait illisible passerait donc au travers — le
  garde-fou est réel mais pas absolu.
- **Un plancher modélisant une paillasse ne porte aucune information de forme** : sa forme
  est toujours indéterminée.
- **Le poinçonnement local sous Q_k n'est pas calculé**, seulement signalé quand la charge
  concentrée gouverne.
- **La largeur de diffusion n'est pas un article de l'Eurocode**, c'est un choix
  conservateur assumé et écrit.
- **Rien n'a encore été exécuté dans Revit**, et la lecture de la ligne de foulée n'a jamais
  été observée en conditions réelles.
