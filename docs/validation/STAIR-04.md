# STAIR-04 — Où tombent réellement les barres

## 1. Énoncé

Cette fiche est née d'une **exécution réelle dans Revit** — la première du projet. Le
symptôme rapporté était simple et sans appel : *« dès que je génère les armatures de
l'escalier, elles sortent dehors et d'autres sont horizontales »*.

Les deux symptômes ont chacun leur cause, toutes deux présentes dans le code depuis la
phase 8, et toutes deux invisibles pour les tests existants.

| Symptôme observé | Cause |
|---|---|
| Des nappes restent horizontales sur une volée inclinée | La répétition d'un groupe se fait le long d'une **normale horizontale** |
| Des barres sortent du béton | Le **repère local** est construit sur la boîte englobante, avec une erreur de signe |

## 2. Analyse

### 2.a Pourquoi les nappes restaient horizontales

Revit répartit les copies d'une armature *shape-driven* **le long de la normale du plan de
la barre**, par translation rectiligne. La normale est donc aussi la direction de
répétition : ce sont deux noms pour un seul vecteur.

Le constructeur de plan calculait Z pour la **première barre seulement**, puis répétait le
groupe suivant l'axe X local, horizontal :

```
Layout = ArrayLayout.FixedNumber(LocalVector.AxisX, count, arrayLength)
WithNormal(LocalVector.AxisX)
```

Toutes les copies restaient donc à la même altitude, pendant que la paillasse montait sous
elles.

```
Pente 170/280, pas horizontal 150 mm
Montée attendue entre deux barres : 150 × 170/280 = 91,07 mm
Montée obtenue                    : 0
Écart au dixième intervalle       : 910,7 mm
```

Une barre transversale prise isolément *est* horizontale sur la largeur de la volée : c'est
normal. C'est la **succession** de ces barres qui doit monter.

**Et une répétition rectiligne ne peut pas suivre une pente puis un plat.** Il n'existe
aucune direction unique qui longe la paillasse puis le palier. Les nappes transversales
sont donc séparées en deux groupes :

| Groupe | Direction de répétition | Longueur du réseau |
|---|---|---|
| Répartition inférieure, **volée** | tangente inclinée (1, 0, tan α) | mesurée **en longueur développée** |
| Répartition inférieure, **palier** | axe X horizontal | mesurée horizontalement |

L'espacement des barres est ainsi mesuré **le long de la pente**, ce qui est la façon dont
elles se posent sur le coffrage — et ce qui donne un peu plus d'acier par mètre de
projection horizontale que l'interprétation inverse, donc du côté de la sécurité.

### 2.b Pourquoi les barres sortaient du béton

Le repère était construit sur la boîte englobante :

```
Origin = (Min.X, Min.Y, Min.Z)
AxisX  = alongX ? +X : +Y
AxisY  = alongX ? +Y : −X
```

Avec `AxisX = +Y`, on a `AxisY = −X`, mais **l'origine restait Min.X**. Un point local de
coordonnée Y positive se projetait alors en :

```
X monde = Min.X − Y
```

Exemple : `Min.X = 1000 mm`, `Y local = 30 mm` → `X = 970 mm`, soit **hors de l'emprise**.
Toute la nappe partait à côté du béton.

Le même défaut de signe avait été corrigé dans `SlabReader` dès la phase 4 ; il ne l'avait
pas été ici. C'est une leçon en soi : un correctif appliqué à un module ne se propage pas
tout seul aux modules écrits ensuite sur le même patron.

**Mais corriger le signe ne suffisait pas.** Une boîte englobante ne connaît ni le sens de
la montée, ni le départ de la sous-face, ni l'orientation réelle de la volée. Un escalier
tourné, ou montant vers les X décroissants, restait mal placé.

Le repère est donc désormais construit sur la **ligne de foulée** :

```
AxisX  = direction horizontale de la ligne de foulée, orientée du bas vers le haut
AxisZ  = vertical
AxisY  = AxisZ ∧ AxisX          (direct, horizontal, en travers)
Origin = départ de la ligne de foulée, décalé d'une demi-largeur pour tomber sur le bord,
         à l'altitude du bas de l'emprise
```

Le sens de montée est **confirmé par les altitudes** des extrémités, pas supposé : la ligne
de foulée peut être stockée dans un sens ou dans l'autre.

Quand la ligne de foulée n'est pas lisible, le repère de secours reste celui de
l'enveloppe — **avec le signe corrigé** — et un avertissement explicite demande de vérifier
la position des barres. C'est le même principe que pour la forme de la volée : ne pas
savoir n'est pas savoir.

### 2.c Ce que la relecture a trouvé en plus

Une fois les deux causes principales identifiées, la même relecture géométrique a montré
quatre autres défauts, tous réels :

**L'enrobage d'une face inclinée n'est pas un décalage vertical.** Pour une distance
normale c à un plan de pente m, le décalage vertical vaut c·√(1+m²) = c/cos α.

```
c = 22 mm, α = 31,26°  →  décalage vertical requis 25,7 mm
Décalage appliqué      :  22 mm
Enrobage réel obtenu   :  22 × cos α = 18,8 mm,  soit 15 % de moins que prévu
```

**La « répartition supérieure » était construite depuis l'enrobage inférieur.** Elle
appelait la fonction transversale avec un simple décalage négatif, mais cette fonction
part de la sous-face et n'ajoute aucune épaisseur : le groupe étiqueté supérieur se posait
près du bas de la section.

**Le chapeau haut était toujours un segment à Z constant.** Sans palier, il passait
au-dessus de la paillasse ; à cheval sur le raccord, il la traversait. Il suit désormais la
pente, et se décompose en trois segments quand il chevauche le raccord.

**Deux polylignes présentaient un saut.** Au raccord volée/palier, l'enrobage normal à la
pente et l'enrobage vertical du palier ne donnent pas la même altitude — le décrochement
est réel. Mais `Rebar.CreateFromCurves` exige une **chaîne de courbes continue** : un saut
fait échouer la création de la barre. Les trajets sont maintenant construits par **suite de
points**, ce qui rend la continuité structurelle plutôt que surveillée.

**En portée transversale**, la répartition longitudinale utilisait `SpanMm`, qui vaut alors
la largeur de la volée : les barres étaient tronquées.

## 3. Résultat moteur

`Stair04GeometryTests`, 9 cas — et c'est ici que se joue le point de méthode.

Les tests des fiches précédentes vérifiaient le **nombre de groupes**, la **présence d'une
normale**, une **longueur positive**. Aucun ne vérifiait **où les barres tombent**. Ils
étaient donc parfaitement verts pendant que le plugin posait des armatures hors du béton.

Ces tests reconstruisent la position de **chaque copie**, exactement comme Revit le fait :

```
copie k = trajet translaté de  direction × (k × pas)
```

puis échantillonnent chaque segment — une corde peut sortir du béton entre deux extrémités
qui, elles, y sont — et exigent que **tout point de toute copie** soit dans l'enveloppe de
béton.

| Cas | Attendu | Moteur |
|---|---|---|
| Toute copie dans le béton, 4 configurations (volée+palier, volée seule, paillasse épaisse/palier court, paillasse mince/palier long) | aucun point hors enveloppe | conforme |
| Nappe transversale de volée | normale à composante Z non nulle, montée = longueur du réseau × sin α | conforme |
| Volée et palier | deux groupes distincts, palier à normale horizontale | conforme |
| Sans palier | aucun groupe de palier produit | conforme |
| Répartition supérieure | dans la moitié haute de la section | conforme |
| Chapeau haut sans palier | monte de plus de 100 mm | conforme |
| Enrobage | distance **normale** = c + φ/2 | conforme |
| Continuité des trajets, 4 longueurs de palier | rupture < 0,1 mm entre segments | conforme |
| Cohérence normale / direction de réseau | même vecteur, produit scalaire normalisé > 0,999 | conforme |

## 4. Comparaison

Rien à comparer numériquement : ces tests portent sur des **positions**, pas sur des
sollicitations. C'est précisément ce qui manquait.

La leçon rejoint celle de l'inversion du moment réduit trouvée en 3.7.0, sous une autre
forme. Là, un test avait été écrit d'après le code plutôt que d'après l'Eurocode. Ici, les
tests portaient sur ce que le code produisait *facilement* — un compte, une propriété
non nulle — plutôt que sur ce que le résultat doit *être* : des barres dans le béton.

**Un plan de ferraillage se valide sur la géométrie des barres, pas sur le nombre de
groupes.**

## 5. Conclusion

Les deux symptômes observés dans Revit sont expliqués, corrigés à la racine, et couverts
par des tests qui les auraient attrapés. Six défauts géométriques supplémentaires,
trouvés par la même relecture, le sont aussi.

## 6. Limites connues

- **Ces corrections n'ont pas encore été rejouées dans Revit.** Les tests vérifient la
  géométrie du plan dans le repère local ; ils ne peuvent pas vérifier que le repère
  correspond au béton réel de l'hôte. C'est exactement la vérification qui manque et que
  seule une exécution donnera.
- **Le repère dépend de la ligne de foulée.** Sans elle, le repère de secours suppose une
  volée alignée sur un axe du modèle et montant vers les coordonnées croissantes, et le
  moteur le dit.
- **Un escalier à plusieurs volées reste réduit à une seule**, et le nombre de
  contremarches lu est celui de l'escalier entier : trop grand, et avec lui la projection
  et la portée. Le lecteur l'annonce désormais explicitement.
- **Les paliers réels de Revit ne sont pas extraits** : le palier est une donnée saisie.
- **La distance normale est vérifiée à la sous-face théorique**, pas aux faces réelles du
  solide de l'hôte.
