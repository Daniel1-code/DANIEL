# STAIR-06 — La géométrie calculée est celle de l'escalier dessiné

**Module** : DanCI Stair Design
**Tests** : `tests/DanCI.Structural.Tests/Validation/Stair06GeometrySourceTests.cs` (22 cas)
et `tests/DanCI.Structural.Tests/EC2/PartialFixityTests.cs` (6 cas)
**Version** : 3.14.0

---

## Ce que cette fiche vérifie

Une question, posée par l'utilisateur dans ces termes :

> *si j'ai déjà dessiné l'escalier et que je veux mettre les armatures ?*

C'est la bonne question, et le moteur y répondait mal. **Quand l'escalier existe dans le
modèle, c'est le dessin qui décide.** Le formulaire ne doit servir qu'à ce que le dessin ne
porte pas — et il doit alors le **dire**, parce qu'une valeur par défaut qui survit à la
lecture ressemble trait pour trait à une lecture.

---

## Le défaut, tel qu'il s'est manifesté

Première génération d'armatures sur un escalier réel : 18 contremarches de 174 mm, giron
250 mm, paillasse 150 mm, **aucun palier**. Résultat dans Revit : une nappe de barres
flottant **en l'air au-delà de la dernière marche**.

La fenêtre affichait :

```
Dénivelé 3130 mm - projection 4250 mm - palier 1300 mm - portée 5550 mm
M travée 57,7 kN.m/m
Taux 2,95 — Non conforme
```

Le palier de 1 300 mm n'existe nulle part dans le modèle. C'était la valeur par défaut de
`StairData`, et **le lecteur ne lisait pas les paliers** : rien ne pouvait donc la
contredire.

### Chaîne de conséquences

| Étape | Avec le palier inventé | Réel |
|---|---|---|
| Portée | 4 250 + 1 300 = **5 550 mm** | **4 250 mm** |
| Moment de travée | **57,7 kN.m/m** | **34,9 kN.m/m** |
| Nappe principale | HA20 | HA12 |
| Taux de flèche | **2,95** | **≈ 2,1** |
| Armatures de palier | **posées dans le vide** | aucune |

Le moment était majoré de **deux tiers**. Les 11 barres visibles en l'air sur la vue 3D
sont la répartition de palier, distribuée le long d'un palier qui n'a jamais été dessiné.

### Vérification à la main de la ligne « réel »

18 contremarches ⇒ 17 girons ⇒ projection = 17 × 250 = **4 250 mm** ✔ (Revit affiche 4250)
Pente : arctan(174/250) = **34,83°** ✔ (Revit affiche 34,8) ; cos α = 0,8211

Poids propre de la volée :

| Terme | Calcul | kN/m² |
|---|---|---|
| Paillasse | 0,150 × 25 / 0,8211 | 4,567 |
| Marches | 25 × 0,174 / 2 | 2,175 |
| Revêtement | — | 1,000 |
| Sous-face | 0,300 / 0,8211 | 0,365 |
| **g** | | **8,107** |

ELU : 1,35 × 8,107 + 1,5 × 3,0 = **15,44 kN/m²**
M = w·l²/8 = 15,44 × 4,25² / 8 = **34,87 kN.m/m**

Rapport 57,7 / 34,87 = **1,65**.

---

## Ce qui est lu maintenant

| Grandeur | Source | Si absente |
|---|---|---|
| Paliers | `Stairs.GetStairsLandings()` | le mode d'appui reste une hypothèse, **annoncée** |
| Longueur portante du palier | étendue suivant la ligne de foulée | idem |
| Contremarches | **la volée**, pas l'escalier entier | repli sur l'escalier, avec remarque |
| Épaisseur de paillasse | profondeur structurelle du type de volée | hypothèse, **annoncée** |
| Épaisseur de palier | type de palier | valeur de la fenêtre |
| Largeur | `StairsRun.ActualRunWidth` | valeur de la fenêtre |
| Épaisseur (paillasse en plancher) | épaisseur du plancher | hypothèse |

**L'absence de palier est une lecture, pas une ignorance.** Si l'escalier dessiné n'en
comporte aucun, la portée est celle de la volée seule et aucune armature de palier n'est
produite. Ce n'est pas une hypothèse prudente : c'est ce que le modèle dit.

Un palier situé **au pied** de la volée ne prolonge pas la portée : il est du côté de
l'appui bas. Seul un palier au-delà du sommet s'y ajoute.

Les paramètres intégrés sont résolus **par leur nom, à l'exécution**. Un identifiant absent
d'une version de Revit est ignoré au lieu d'empêcher la compilation, et la lecture ne dépend
pas de la langue de l'interface.

---

## Ce qui n'est pas lu est dit

Chaque dimension porte désormais son origine :

| Origine | Sens |
|---|---|
| `ReadFromModel` | lue sur l'élément dessiné — elle fait foi |
| `StatedByEngineer` | saisie délibérément — elle fait foi aussi, mais c'est une personne qui la porte |
| `Assumed` | **hypothèse** : une valeur par défaut qui a survécu |

Le contrôle **« Origine de la géométrie calculée »** les nomme, et il est rendu **avant** les
vérifications de résistance : on lit une note de calcul dans l'ordre, et savoir sur quelle
géométrie on travaille vient avant de savoir si elle passe.

Il sort en **avertissement** quand le **mode d'appui** ou l'**épaisseur de paillasse** est
supposé — l'un fixe la portée donc le moment, l'autre pilote tout le poids propre. Les
autres hypothèses sont citées sans alarmer : un giron supposé ne déplace pas la portée.

---

## Ce que la correction ne fait pas

**Elle ne rend pas cette volée conforme.**

Le palier inventé exagérait le défaut, il ne l'a pas inventé. Une paillasse de 150 mm sur
4,25 m de portée échoue en flèche de toute façon :

d = 150 − 26 − 6 = 118 mm ; A_s ≈ 740 mm²/m ; ρ = 0,0063 > ρ₀ = 0,005
l/d limite = 11 + 1,5 × 5 × (0,005/0,0063) ≈ **17**
l/d réel = 4 250 / 118 = **36** ⇒ rapport ≈ **2,1**

Il faut **environ 200 mm** de paillasse pour passer, et 220 mm pour passer confortablement :

| Paillasse | d | l/d réel | l/d limite | |
|---|---|---|---|---|
| 150 mm | 118 | 36,0 | 17,0 | ✗ |
| 180 mm | 148 | 28,7 | 21,4 | ✗ |
| 200 mm | 168 | 25,3 | 27,6 | ✔ juste |
| 220 mm | 188 | 22,6 | 35,7 | ✔ |

Un test verrouille les deux bouts : la volée de 150 mm **doit** échouer, celle de 220 mm
**doit** passer. Annoncer que la correction « arrange l'escalier » aurait été faux.

---

## Les 16 cas

| # | Cas | Ce qu'il verrouille |
|---|---|---|
| 1 | Portée de la volée réelle | 4 250 mm, et 5 550 avec le palier inventé |
| 2 | Moment majoré de plus de moitié | rapport 1,55 à 1,75 |
| 3 | Aucune barre au-delà du sommet | **le défaut de l'image** |
| 4 | Aucun groupe de palier sans palier | |
| 5 | La correction ne rend pas conforme | flèche toujours en échec, taux 1,7 à 2,4 |
| 6 | 220 mm passe la flèche | la contrepartie honnête |
| 7 | Par défaut tout est supposé | 8 dimensions |
| 8 | Le mode d'appui vient en tête | c'est lui qui fixe la portée |
| 9-10 | Lue ou déclarée ⇒ plus une hypothèse | les deux origines |
| 11 | La provenance se copie | et les copies sont indépendantes |
| 12 | Chaque dimension a un nom lisible | pas de `LandingSpan` dans un message |
| 13 | Géométrie supposée ⇒ avertissement | |
| 14 | Le contrôle nomme la portée en doute | un avertissement muet ne change rien |
| 15 | Géométrie entièrement lue ⇒ conforme | |
| 16 | Épaisseur supposée ⇒ avertissement | elle pilote le poids propre |

---

## Un paramètre ne passe pas sous un article

La couche paramétrique de la 3.12.0 avait une faille, trouvée en relisant ce qu'elle
autorise plutôt que ce qu'elle produit.

Le mode **« ancrage seul »** posait un chapeau d'un l_bd, soit environ 400 mm sur une portée
de 4 250. Une longueur imposée courte, ou une fraction de portée de 0,05, passaient de même.

**L'EN 1992-1-1 art. 9.3.1.2(2)** vise exactement notre cas : l'encastrement partiel n'est
*pas* pris en compte dans l'analyse — la volée est calculée isostatique. C'est sécuritaire
pour la travée, mais cela ne fait pas disparaître le moment négatif qui se développe sur des
appuis coulés en continuité ; cela choisit seulement de ne pas le calculer. L'article impose
alors un forfait :

| | Exigence | Statut |
|---|---|---|
| Section | A_s,sup reprend ≥ **25 % du moment de travée** | vérifiée |
| Longueur | ≥ **0,2 l** depuis le nu de l'appui | **plancher** |

La longueur est un plancher : un paramètre peut allonger un chapeau, **jamais le raccourcir
sous cette valeur**, et la décision dit alors quel article l'a relevée. C'est la même règle
que pour l'ancrage au nœud — majorable, jamais réductible.

La section corrige un défaut réel : quand aucun moment sur appui n'était déclaré, les
chapeaux étaient posés au seul A_s,min.

### Ce que le forfait ne fait pas, et pourquoi

**Il n'est pas monotone en portée.** J'avais écrit l'inverse dans un test — « à section
identique, A_s,min est le même, donc le forfait ne peut que croître avec la portée » — et le
moteur a dit non.

A_s,min = 0,26 f_ctm/f_yk · b · d suit la **hauteur utile**. Or d se mesure sur le diamètre
présumé de la nappe, et ce diamètre est choisi d'après le moment. Une volée longue prend une
barre plus grosse, ce qui **abaisse d**, donc A_s,min ; une volée courte, ferraillée en petit
diamètre, garde un d plus grand et un A_s,min plus élevé. Tant que les deux cas sont gouvernés
par la section minimale, le forfait peut baisser quand la portée monte.

Ce n'est pas une incohérence : c'est la section minimale qui parle, pas le moment. Le test
consigne le comportement et sa raison, et vérifie ce qui doit être vrai dans tous les cas —
le forfait existe, et la nappe posée le couvre. **Sur la volée de référence à 3 kN/m²,
A_s,min gouverne ; à 8 kN/m², le forfait le dépasse** et devient dimensionnant : c'est là que
la règle change le ferraillage, donc là que son absence le rendait insuffisant.

---

## Limites de cette fiche

**Ces tests n'ouvrent pas Revit.** Ils vérifient que le moteur tire les bonnes conclusions
d'une géométrie sans palier, et qu'il dit lesquelles de ses valeurs il n'a pas lues. La
lecture elle-même — `GetStairsLandings`, la mesure suivant la ligne de foulée, les
paramètres de profondeur structurelle — relève du couple `StairReader` / Revit, et **seule
une exécution réelle la valide**.

C'est la troisième règle de méthode du projet appliquée à elle-même : un plan de ferraillage
se valide sur la géométrie des barres, et une lecture de modèle se valide dans le modèle.

Reste non traité, et déclaré :

- les escaliers à **plusieurs volées** sont réduits à la première ; le palier retenu est
  celui qui la suit ;
- un palier dont l'emprise n'est pas alignée sur la ligne de foulée est mesuré sur son
  enveloppe, donc **surestimé** ;
- les autres modules n'ont pas de couche de provenance : leur géométrie est lue ou saisie
  sans que la note dise lequel.
