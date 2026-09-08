# DanCI Structural Studio

**Structural Design & Reinforcement Automation for Autodesk Revit**

Plateforme de **calcul, vérification, dimensionnement, ferraillage et documentation
automatique des structures en béton armé**, intégrée à **Autodesk Revit 2026**.

L'objectif : transformer un modèle Revit en ferraillage justifié.

```
SÉLECTION  →  GÉOMÉTRIE  →  MATÉRIAUX  →  EFFORTS  →  COMBINAISONS
    →  VÉRIFICATIONS EUROCODE  →  DIMENSIONNEMENT  →  OPTIMISATION
    →  DISPOSITIONS CONSTRUCTIVES  →  ARMATURES 3D  →  QUANTITATIF  →  NOTE DE CALCUL
```

> ⚠️ Le moteur applique les dispositions constructives et la vérification de résistance de
> section. Il ne remplace pas l'analyse globale de la structure. **Le ferraillage produit est
> un avant-projet, à vérifier et valider par l'ingénieur responsable du projet.**

---

## 1. Modules

| Module | État |
|---|---|
| **DanCI Column Design** — poteaux | ✅ Disponible |
| **DanCI Beam Design** — poutres | ✅ Disponible |
| **DanCI Isolated Footing** — semelles isolées | ✅ Disponible |
| **DanCI Slab Design** — dalles portant dans un sens | ✅ Disponible |
| **DanCI Wall Design** — voiles | ✅ Disponible |
| **DanCI Strip Footing** — semelles filantes | ✅ Disponible |
| **DanCI Grade Beam** — longrines | ✅ Disponible |
| **DanCI Stair Design** — escaliers droits | ✅ Disponible |
| Plans automatiques, BBS | Phase 9 — prochaine |
| Notes de calcul complètes, dashboard | Phase 10 |

La feuille de route détaillée est dans [`docs/ARCHITECTURE-V3.md`](docs/ARCHITECTURE-V3.md).

---

## 2. Installation

1. Télécharger le `.zip` de la [dernière release](../../releases/latest).
2. Décompresser, puis clic droit sur `Installer.ps1` → **Exécuter avec PowerShell**
   (ou `powershell -ExecutionPolicy Bypass -File .\Installer.ps1`).
3. Redémarrer Revit 2026 → onglet **DanCI Structural Studio**.

L'installeur n'exige aucun droit administrateur, débloque les fichiers téléchargés et
désinstalle l'ancien plugin « Armatures de poteaux ».

Désinstallation : `.\Installer.ps1 -Uninstall`

**Compiler soi-même** (prérequis : .NET SDK 8 sur Windows ; Revit n'est pas nécessaire) :

```powershell
dotnet build DanCI.StructuralStudio.sln -c Release
dotnet test tests\DanCI.Structural.Tests\DanCI.Structural.Tests.csproj -c Release
```

---

## 3. DanCI Column Design

Sélectionner des poteaux structurels → ruban **DanCI Structural Studio** → **Column**.

La fenêtre présente à gauche les réglages, au centre le tableau des éléments et la note de
calcul, à droite la coupe du poteau et le quantitatif. **Générer** modélise les armatures en
une transaction annulable d'un `Ctrl+Z`.

### Ce que le moteur décide seul

| | |
|---|---|
| Diamètre et nombre de barres | balayage HA8→HA40 × dispositions ; la solution est **notée** (excès d'acier, nombre de barres, symétrie), jamais la première qui passe |
| Diamètre des cadres | `max(6 mm ; φ_l/4)` arrondi au diamètre commercial |
| Espacement des cadres | `min(20 φ_l ; b_min ; 400 mm)` |
| Zones critiques | pied et tête, espacement × 0,6 (× 0,5 en ACI), allongées en sismique |
| Épingles | dès qu'une barre est à plus de 150 mm d'une barre tenue |
| Ancrage et recouvrement | l_bd et l₀ calculés (f_bd, l_b,rqd, α₆) |
| Renforcement | si la vérification N-M échoue, le ferraillage monte par paliers de 12 % jusqu'à A_s,max |

Chaque champ reste forçable à la main.

### Vérifications produites

Chaque vérification porte **norme, article, équation, données, sollicitation, résistance, taux
de travail et combinaison dimensionnante** :

```
[OK] Espacement des cadres en zone courante
     EN 1992-1-1:2004 art. 9.5.3 (3) : s <= scl,tmax
     Sollicitation 200 mm / Résistance 300 mm -> taux 0,67
     Combinaison : ULS-COMB-001
```

Couvertes aujourd'hui : A_s,min et A_s,max, nombre de barres, diamètre et espacement des
cadres, espacement libre entre barres, maintien des barres comprimées, élancement limite,
flexion composée biaxiale avec second ordre.

---

## 4. DanCI Beam Design

Sélectionner des poutres structurelles → ruban **DanCI Structural Studio** → **Beam**.

Saisir les moments (travée, appui gauche, appui droit) et les efforts tranchants aux deux
appuis. À droite, la **coupe en travée** et l'**élévation des zones de cadres**, dessinées à
leur espacement réel avec l'effort tranchant de chaque zone.

### Ce que le moteur calcule

| | |
|---|---|
| Flexion | diagramme rectangulaire §6.1, sections simplement et doublement armées, limite x/d de §5.5(4) |
| Sections en T | largeur participante §5.3.2.1, bascule automatique entre table et âme selon la position de l'axe neutre |
| Effort tranchant | V_Rd,c §6.2.2 avec plancher v_min, bielles à inclinaison variable §6.2.3 : cot θ = 2,5 tant que les bielles résistent, redressement ensuite |
| Cadres | **espacement variable par zone** — resserrés aux appuis, ouverts en travée, jamais un pas arbitraire |
| Chapeaux | posés seulement là où il y a un moment négatif, prolongés de max(L/4 ; a_l + l_bd) |
| Barres | optimiseur multi-lits borné par la largeur entre cadres et l'espacement libre minimal |

> La **table collaborante** et les **conditions d'appui** sont déclarées dans la fenêtre : la
> dalle n'appartient pas à l'élément poutre dans Revit, et la deviner reviendrait à supposer
> le modèle structurel.

**Pas encore couvert** : torsion (§6.3), fissuration (§7.3), flèche (§7.4), continuité
automatique des chapeaux entre travées, bielle d'about (§9.2.1.4).

---

## 5. DanCI Isolated Footing

Sélectionner des fondations structurelles → ruban **DanCI Structural Studio** → **Footing**.

Le poteau porté est cherché automatiquement au-dessus de la semelle ; s'il reste
introuvable, le plugin le dit et laisse la saisie à l'utilisateur plutôt que d'inventer
une section. Saisir la charge en pied de poteau et la **contrainte admissible du sol**,
qui vient de l'étude géotechnique : le plugin ne la calcule pas.

À droite, la **coupe** (nappes, attentes en L, cône de poinçonnement) et la **vue en plan**
avec le quadrillage réel des barres et le périmètre de contrôle effectivement retenu.

### Ce que le moteur calcule

| | |
|---|---|
| Contraintes sous la semelle | distribution linéaire pour le contrôle du soulèvement **et** aire effective de Meyerhof (EN 1997-1 annexe D) pour la capacité portante — les deux modèles cohabitent, comme dans la pratique |
| Poids propre | inclus dans la vérification du sol, **exclu** du calcul structurel : le sol l'équilibre directement |
| Stabilité | glissement §6.5.3, renversement EQU, non-soulèvement (e ≤ B/6) |
| Enrobage | plancher de fondation §4.4.1.3(4) : 40 mm sur béton de propreté, 75 mm contre le sol |
| Flexion | console encastrée au nu du poteau dans les deux directions ; A_s,min gouverne presque toujours |
| Nappes | optimiseur diamètre × espacement, s ≤ min(3h ; 400 mm) §9.3.1.1 |
| Effort tranchant | section à d du nu, **dans les deux directions** — le grand débord n'est pas forcément suivant X |
| Poinçonnement | §6.4.4(2) : balayage des périmètres entre le nu et 2d, réaction du sol déduite, résistance majorée de 2d/a. Le périmètre le plus défavorable n'est jamais celui à 2d |
| Attentes | réparties sur le pourtour du poteau, retour horizontal en pied, dépassement égal au recouvrement |

**Pas encore couvert** : semelles à gradins (traitées en pavé équivalent, signalé),
poteaux circulaires (carré équivalent, signalé), soulèvement partiel (e > B/6 est refusé,
pas redistribué), tassement ELS §6.6, radiers et semelles filantes.

---

## 6. DanCI Slab Design

Sélectionner des planchers → ruban **DanCI Structural Studio** → **Slab**.

Le calcul porte sur une **bande de 1 000 mm**, comme sur un plan de ferraillage.

> **Le sens porteur n'est pas deviné au hasard.** Le paramètre Revit « Direction de
> portée » est lu s'il existe ; à défaut, la portée est prise dans la plus courte
> dimension, parce qu'une dalle porte par le plus court chemin. L'hypothèse retenue est
> soumise **avant** le calcul, et la fenêtre laisse corriger.

### Ce que le moteur calcule

| | |
|---|---|
| Actions | EN 1990 : ELU éq. 6.10 (1,35 G + 1,5 Q) et ELS quasi-permanente (G + ψ₂ Q), la catégorie d'usage pilotant ψ₂ |
| Sollicitations | **statique pure** pour l'isostatique (w l²/8) et la console (w l²/2) ; coefficients de continuité usuels pour les travées continues, annoncés comme **n'étant pas de l'Eurocode** ; ou moments saisis depuis une analyse extérieure |
| Enrobage | §4.4.1 avec la réduction de classe structurale propre aux dalles §4.4.1.2(5) |
| Flexion | §6.1 sur la bande, A_s,min et A_s,max |
| Répartition | §9.3.1.1(2) : au moins 20 % de l'armature principale |
| Espacements | §9.3.1.1(3) : min(3h ; 400) en principal, min(3,5h ; 450) en répartition |
| Effort tranchant | §6.2.2 sans armatures — une dalle ne se rattrape pas avec des cadres |
| **Flèche** | §7.4.2, éq. 7.16a/7.16b, K du tableau 7.4N, corrections A_s,prov/A_s,req (plafonnée à 1,5), table large et portée > 7 m |
| **Fissuration** | §7.3.3(2), tableaux 7.2N et 7.3N interpolés — un seul des deux critères suffit |
| Chapeaux | posés d'eux-mêmes dès qu'un moment négatif existe, sur max(L/4 ; l_bd) |

L'aperçu à droite montre la coupe (épaisseur dilatée, étendue réelle des chapeaux) et un
**diagramme des taux de travail** : sur une dalle, c'est presque toujours la flèche qui
gouverne, et le diagramme le montre d'un coup d'œil.

**Pas encore couvert** : dalles portant dans deux sens (détectées et signalées, pas
calculées), planchers-dalles sur appuis ponctuels et leur poinçonnement, analyse de
continuité réelle, trémies et découpes, calcul détaillé de flèche §7.4.3.

---

## 7. DanCI Wall Design

Sélectionner des murs structurels en béton → ruban **DanCI Structural Studio** → **Wall**.

Le voile est vérifié à **deux échelles qu'il ne faut jamais confondre** :

- **Hors plan**, il se comporte comme un poteau de section 1 000 × t. C'est l'échelle de la
  bande verticale de 1 m, et c'est là que jouent l'élancement et le second ordre.
- **Dans son plan**, c'est une console verticale de grande hauteur. C'est l'échelle du voile
  entier, et c'est là que jouent l'effort tranchant de contreventement et les barres de rive.

### Ce que le moteur calcule

| | |
|---|---|
| Flambement | §12.6.5.1 : quatre cas de maintien avec leurs formules. **Un retour de voile ne raidit que s'il est proche** — au-delà de 3 × la hauteur libre, le moteur signale que la partie courante ne le voit pas |
| Excentricité minimale | §6.1(4) : e₀ = max(t/30 ; 20 mm), qui gouverne quand aucun moment n'est déclaré |
| Second ordre | §5.8.3.1 et §5.8.8 : courbure nominale, comme pour un poteau |
| Capacité N-M | diagramme d'interaction sur la bande de 1 m, ferraillage augmenté par paliers jusqu'à A_s,max |
| Dispositions | §9.6.2 (A_s,v ≥ 0,002 A_c, s ≤ min(3t ; 400)), §9.6.3 (A_s,h ≥ max(0,25 A_s,v ; 0,001 A_c)), §9.6.4 (épingles dès A_s,v > 0,02 A_c) |
| Effort tranchant dans le plan | §6.2, les aciers horizontaux tenant lieu de cadres, plus l'écrasement des bielles |
| Flexion dans le plan | modèle à deux zones de rive, la compression soulageant la traction |

> **Un élément dont la longueur est inférieure à 4 × l'épaisseur n'est pas un voile** au
> sens de l'article 9.6.1 : c'est un poteau, et ce sont les dispositions de l'article 9.5
> qui s'appliquent. Le moteur le dit et renvoie vers le module **Column** plutôt que de
> produire un ferraillage réglementairement faux.

**Pas encore couvert** : éléments de rive confinés de l'EN 1998-1 (un voile de
contreventement sismique n'est **pas** dimensionné par ce module, et le moteur l'annonce),
ouvertures — trumeaux, linteaux et chaînages —, voiles courbes, vérification au feu.

---

## 8. DanCI Strip Footing

Sélectionner des semelles filantes → ruban **DanCI Structural Studio** → **Strip**.

Le calcul porte sur un **mètre courant**. L'épaisseur du voile porté est lue sur le mur
hôte, jamais devinée : c'est elle qui fixe le débord, donc le moment de la console.

### Trois choses qu'elle fait autrement qu'une semelle isolée

| | |
|---|---|
| **Elle ne poinçonne pas** | La charge d'un voile arrive répartie sur toute la longueur, pas concentrée sur une aire chargée. Le moteur rend une vérification explicitement **Sans objet** plutôt que de l'omettre — et rappelle que des poteaux portés par la même semelle devraient être vérifiés séparément |
| **Le minimum gouverne** | Sur le cas de référence, A_s,min de §9.2.1.1 dépasse l'acier de flexion d'un **facteur sept**. Un moteur qui ne poserait que l'acier de flexion produirait une semelle non conforme en affichant une marge confortable |
| **L'ancrage transversal ne tient pas** | Sur un débord court, la longueur au-delà du nu ne suffit pas. Le moteur applique α₁ = 0,70 du tableau 8.2 **seulement si c_d > 3φ**, pose un crochet d'extrémité, et renvoie explicitement au modèle bielles-tirants de §9.8.2.2 qu'il ne fait pas |

### Le reste

Contraintes du sol par distribution linéaire et aire effective de Meyerhof, capacité
portante, non-soulèvement, glissement, renversement, enrobage §4.4.1.3(4), flexion de la
console, répartition longitudinale §9.3.1.1(2), effort tranchant à d du nu **quand la
section existe**, attentes du voile en L.

Le moteur contrôle aussi la **rigidité** : au-delà d'un débord de 2 × l'épaisseur, la
répartition linéaire des contraintes cesse d'être représentative et il le dit.

**Pas encore couvert** : semelle souple (signalée, pas recalculée), décollement partiel
(refusé, pas redistribué), semelle-poutre sous poteaux alignés, tassement différentiel.

---

## 9. DanCI Grade Beam

Sélectionner des longrines → ruban **DanCI Structural Studio** → **Grade Beam**.

Une longrine est modélisée dans Revit comme une poutre : c'est le lancement de la commande
qui déclare qu'il s'agit d'un élément de fondation, le lecteur ne le devine pas. Il
signale en revanche ce qui ne colle pas — un élément situé à plus de trois mètres au-dessus
du niveau le plus bas, un élancement portée/hauteur supérieur à 20.

### Trois choses qu'elle fait autrement qu'une poutre

| | |
|---|---|
| **C'est d'abord un tirant** | L'effort de l'EN 1998-5 §5.4.1.2(7) est **axial et alterné**, et il s'ajoute à la flexion dans les deux nappes à la fois. Le moteur répartit A_s,N = N/f_yd à parts égales entre elles, et vérifie aussi la compression — sans flambement, la longrine étant maintenue sur toute sa longueur |
| **Ses deux nappes filent** | L'EN 1998-1 §5.8.2(5) impose 0,4 % **en haut et en bas** : 600 mm² sur une 300 × 500 contre 177 mm² pour l'EC2, un **facteur 3,4** qui gouverne à lui seul la nappe supérieure. Ce n'est pas un montage constructif, elle travaille |
| **Ce sur quoi elle repose change tout** | Suspendue, elle porte toute sa charge ; posée sur le sol, presque rien. Le moteur la calcule **toujours** comme suspendue — sécuritaire — et refuse de prendre crédit d'un appui du sol sans analyse de poutre sur sol élastique. Il vérifie en revanche que la contrainte de contact est physiquement possible |

### Le reste

Enrobage §4.4.1.3(4) au plancher fondation, flexion §6.1, effort tranchant §6.2.2 et
§6.2.3, cadres fermés à espacement constant plafonné à 0,75 d, section minimale de
l'EN 1998-1 §5.8.1(4) selon le nombre de niveaux, nappes prolongées de l'ancrage au-delà
de chaque nu d'appui.

**Hors zone sismique, aucun effort de liaison n'est inventé** : aucun article de
l'EN 1992-1-1 n'en impose, le moteur le dit et renvoie à l'EN 1991-1-7 si le projet en
exige un au titre de la robustesse. Un effort saisi à la main est accepté, dimensionné, et
tracé comme venant de l'utilisateur.

**Pas encore couvert** : diagramme d'interaction M-N (la traction est répartie à parts
égales, sécuritaire pour une traction faible), poutre sur sol élastique, tassement
différentiel des semelles reliées, fissuration et flèche (peu de sens pour un élément
enterré, mais leur absence est un choix).

---

## 10. DanCI Stair Design

Sélectionner un escalier — ou le plancher structurel incliné qui modélise la paillasse →
ruban **DanCI Structural Studio** → **Stair**.

Les deux sélections ne servent pas à la même chose, et le module le dit : **l'escalier
Revit porte la géométrie de marche, le plancher est le seul des deux à pouvoir recevoir
des armatures**. La question « cet élément accepte-t-il des barres ? » n'est jamais
supposée : elle est posée à l'API, et si la réponse est non, la commande l'annonce
**avant** le calcul et propose de continuer sans poser de barres — le dimensionnement,
l'aperçu, le quantitatif et la note de calcul restent utiles.

### Trois choses qu'elle fait autrement qu'une dalle

| | |
|---|---|
| **Son poids propre n'est pas γ t** | La paillasse est mesurée perpendiculairement à la pente, donc la hauteur de béton au-dessus d'un point du plan vaut **γ t / cos α** ; et chaque marche est un prisme triangulaire pesant **γ R / 2** par m² de projection, terme purement géométrique. Négliger les deux corrections coûte **plus de 40 %** du poids propre réel, toujours du côté non sécuritaire. Le moteur affiche l'écart |
| **Sa portée porte deux charges** | La volée et le palier ne pèsent pas la même chose. Étaler celle de la volée partout est sécuritaire mais faux, de 6,9 % sur le cas de référence. Le calcul exact est une travée isostatique sous deux charges réparties, de solution fermée : le moteur le fait, et rend **les deux valeurs** pour que l'écart soit visible |
| **Son nœud est un angle rentrant tendu** | Une barre qui suivrait le pli développerait à l'intérieur du coude une résultante dirigée vers l'extérieur du béton : elle ferait **sauter l'enrobage**, et le nœud céderait avant la section courante. Les deux nappes sont donc **croisées** et ancrées chacune dans la face opposée. Le moteur ne propose aucune variante suivant le pli, et l'aperçu agrandit le nœud pour que le détail se voie |

### Le reste

Combinaisons EN 1990, enrobage §4.4.1, flexion §6.1 avec une hauteur utile mesurée sur
l'épaisseur de paillasse, armature de répartition §9.3.1.1(2), effort tranchant §6.2.2,
flèche par l'élancement limite §7.4.2, maîtrise de la fissuration §7.3.3, chapeaux aux
appuis. Le résumé sous les champs de
géométrie montre en direct ce que la marche implique : pente, cos α, portée de calcul,
paillasse vue verticalement, et la valeur de Blondel.

**C'est la flèche qui décide de l'épaisseur, pas la résistance.** Sur la volée de
référence, une paillasse de 120 mm passe encore en flexion et échoue de 60 % en flèche.

### Ce que le moteur refuse de faire

**Une volée balancée ou hélicoïdale n'est pas calculée — elle est refusée.** Elle porte en
flexion *et* en torsion, et sa portée n'est pas la projection d'une droite : la traiter
comme une volée droite de mêmes contremarches donnerait un résultat d'apparence normale et
faux. La détection est **géométrique** — la ligne de foulée d'une volée droite est un
segment de droite unique — et non fondée sur un paramètre de type dont le nom pourrait
changer d'une version de Revit à l'autre. Quand la ligne de foulée n'est pas lisible, la
forme reste *indéterminée* : le moteur poursuit, mais il le dit et n'endosse pas
l'hypothèse à la place de l'ingénieur.

**Une géométrie impossible ne produit aucun ferraillage.** Moins de deux contremarches,
donc aucun giron, donc aucune portée : le calcul est refusé plutôt qu'approximé.

**La charge concentrée Q_k est vérifiée, pas supposée.** L'EN 1991-1-1 §6.3.1.2(1)
l'impose en *alternative* à la charge répartie ; le moteur évalue les deux et retient la
plus défavorable. Aucun article de l'EN 1992-1-1 ne fixe la largeur de diffusion : le
moteur prend donc la plus **défavorable** raisonnable — 45° à travers la seule paillasse —
si bien que la conclusion ne dépend d'aucune règle contestable. Sur une volée courte, Q_k
gouverne franchement.

**Ce qui n'est pas fait est écrit** : le rendement du nœud n'est pas calculé (le modèle
bielles-tirants des §5.6.4 et 6.5 n'est pas construit, seule la longueur d'ancrage
disponible est vérifiée) ; le poinçonnement local sous Q_k est signalé, pas calculé ; la
majoration de 15 % souvent accordée à la flèche des escaliers vient de la **BS 8110**,
n'existe pas dans l'EN 1992-1-1, et n'est pas appliquée.

**Pas encore couvert** : marches en console, limon central, calcul de flèche détaillé
(§7.4.3). La réglementation de construction nationale (hauteurs et girons admissibles)
n'est pas connue du moteur : la pente et Blondel sont rendues, jamais imposées.

---

## 11. Bases normatives

**EN 1992-1-1:2004+A1:2014**, valeurs recommandées par défaut, Annexe Nationale sélectionnable.

| Sujet | Article |
|---|---|
| Propriétés du béton, loi parabole-rectangle | 3.1.6, 3.1.7, tableau 3.1 |
| Espacement libre des barres | 8.2 (2) |
| Ancrage : f_bd, l_b,rqd, l_bd | 8.4.2, 8.4.3, 8.4.4 |
| Recouvrement l₀ | 8.7.3 |
| Excentricité minimale | 6.1 (4) |
| Élancement limite | 5.8.3.1 |
| Second ordre, courbure nominale | 5.8.8.2, 5.8.8.3 |
| Interaction biaxiale | 5.8.9 (4) |
| Poteaux : armatures et dispositions | 9.5.2, 9.5.3 |
| Poutres : largeur participante de table | 5.3.2.1 |
| Poutres : flexion, limite d'axe neutre | 6.1, 5.5(4) |
| Poutres : effort tranchant, bielles variables | 6.2.2, 6.2.3 |
| Poutres : armatures longitudinales et cadres | 9.2.1.1, 9.2.1.3, 9.2.2 |
| Zones critiques sismiques | EN 1998-1 5.4.3.2.2 |
| Semelles : enrobage de fondation | 4.4.1.3 (4) |
| Semelles : armatures de dalle, espacement | 9.3.1.1 |
| Poinçonnement, périmètre de contrôle, semelles | 6.4.2, 6.4.4 (2), 6.4.5 (3) |
| Sol : aire effective, capacité portante | EN 1997-1 6.5.2, annexe D |
| Sol : glissement, renversement | EN 1997-1 6.5.3, 2.4.7.2 (EQU) |
| Dalles : armatures principales et de répartition | 9.3.1.1 |
| Flèche par l'élancement limite | 7.4.2, éq. 7.16a et 7.16b, tableau 7.4N |
| Fissuration sans calcul direct | 7.3.2, 7.3.3, tableaux 7.1N, 7.2N et 7.3N |
| Combinaisons d'actions | EN 1990 6.10, 6.14b, 6.16b, tableau A1.1 |
| Voiles : définition, armatures verticales et horizontales | 9.6.1, 9.6.2, 9.6.3, 9.6.4 |
| Voiles : longueur de flambement | 12.6.5.1, repris par 5.8.3.2 (6) |
| Semelles : ancrage des armatures, coefficient α₁ | 8.4.4, tableau 8.2, 9.8.2.2 |
| Longrines : effort de liaison entre semelles | EN 1998-5 5.4.1.2 (7) |
| Longrines : section minimale selon le nombre de niveaux | EN 1998-1 5.8.1 (4) |
| Longrines : 0,4 % en haut et en bas | EN 1998-1 5.8.2 (5) |
| Escaliers : catégorie d'usage de la zone desservie | EN 1991-1-1 6.3.1 (1) |
| Escaliers : charge concentrée, situation alternative vérifiée | EN 1991-1-1 6.3.1.2 (1) |
| Escaliers : poids propre des éléments | EN 1991-1-1 annexe A |
| Nœuds : modèle bielles-tirants (non implémenté, cité) | 5.6.4 et 6.5 |

**ACI 318-19** (10.6, 10.7.3, 25.7.2) est disponible pour les projets hors Europe, dans une
implémentation séparée — jamais mélangée aux formules Eurocode.

Tous les paramètres modifiables par une Annexe Nationale (γ_c, γ_s, α_cc, coefficients de 9.5,
α₆…) passent par `INationalAnnex`. **Aucune constante normative n'existe ailleurs dans le code.**

---

## 12. Architecture

Le moteur de calcul **ne connaît pas Revit** — règle vérifiée par la CI à chaque push.

```
Core ← Eurocodes ← Reinforcement ← Engine ← Documentation
                                      ↑
                           Revit ─────┤   ← seuls Revit, UI et App
                           UI ────────┤     référencent RevitAPI.dll
                                     App
```

| Projet | Rôle |
|---|---|
| `DanCI.Structural.Core` | unités (N, mm, MPa), géométrie, éléments, charges, `CheckResult` |
| `DanCI.Structural.Eurocodes` | EC0 (combinaisons), EC2, EC7, Annexes Nationales, dispositions constructives |
| `DanCI.Structural.Reinforcement` | `ReinforcementPlan`, optimisation des barres, zones de cadres |
| `DanCI.Structural.Engine` | modules de dimensionnement : Column, Beam, IsolatedFooting, Slab, Wall, StripFooting |
| `DanCI.Structural.Documentation` | quantitatifs, CSV, notes de calcul |
| `DanCI.Structural.Revit` | lecture de la géométrie, écriture des `Rebar` |
| `DanCI.Structural.UI` | fenêtres WPF (sans RevitAPI) |
| `DanCI.Structural.App` | ruban et commandes Revit |
| `DanCI.Structural.Tests` | tests du moteur, exécutés par la CI |

**Pièce maîtresse** : `ReinforcementPlan` décrit le ferraillage en coordonnées locales (mm).
Un unique `RebarWriter` le traduit en objets Revit `Rebar` — ce code s'écrit une fois et sert
ensuite aux poutres, semelles, dalles et voiles.

---

## 13. Fiabilité du calcul

La priorité est l'exactitude, pas l'apparence. En pratique :

- **Tests unitaires obligatoires** : aucune formule normative n'entre sans son test.
  La CI exécute la suite à chaque push ; un test rouge casse le build.
- **Fiches de validation** dans `docs/validation/` : énoncé, calcul manuel détaillé, résultat
  du moteur, écart. Un module n'est pas terminé sans elles.
- **Une compilation verte n'est pas une validation.** Les deux sont vérifiées séparément.

---

## 14. Versions

Quatre numéros indépendants, reportés dans chaque note de calcul, pour savoir avec quel moteur
un calcul a été produit :

```
ApplicationVersion        3.9.0
CalculationEngineVersion  1.9.0
EurocodeLibraryVersion    1.9.0
DesignDataSchemaVersion   1
```

---

## 15. En cas de problème

| Symptôme | Cause / solution |
|---|---|
| L'onglet n'apparaît pas | DLL bloquée par Windows (Propriétés → Débloquer) ou chemin du `.addin` incorrect |
| « Aucun type de barre d'armature » | Insertion → Charger la famille → Structure → Armature |
| « Cet élément ne peut pas recevoir d'armatures » | L'élément n'est pas structurel ou son matériau n'est pas du béton |
| « Aucun poteau porté n'a été trouvé » | La semelle et le poteau ne se touchent pas dans le modèle : saisir la section du poteau à la main |
| « Seuls les murs droits sont pris en charge » | Découper le voile courbe en panneaux droits |
| « Cet élément relève du module Column » | Longueur < 4 × épaisseur : ce n'est pas un voile au sens de l'art. 9.6.1 |
| « Le mur porté n'a pas pu être lu » | La semelle filante n'est pas associée à un mur dans Revit : sans son épaisseur, le débord est inconnu |
| « Cet élément est à plus de 3 m du niveau le plus bas » | Une longrine est un élément de fondation : vérifier que la sélection n'a pas attrapé une poutre de plancher |
| « Les armatures ne pourront pas être posées » (escalier) | Revit refuse cet escalier comme hôte d'armatures. Le calcul reste produit ; pour poser les barres, modéliser la paillasse par un plancher structurel incliné ou un élément in situ |
| « L'épaisseur de paillasse n'est pas lue » | Revit ne l'expose pas de façon fiable selon le type de volée : la saisir dans la fenêtre, c'est elle qui pilote tout le poids propre |
| « Volée balancée / hélicoïdale : le moteur ne sait pas la calculer » | Ce n'est pas une limite d'implémentation contournable : ces volées portent en torsion et relèvent d'une autre analyse |
| « La forme de la volée n'a pas pu être déterminée » | La ligne de foulée n'est pas lisible (ou l'élément est un plancher) : vérifier soi-même que la volée est droite |
| Armatures invisibles | Vue 3D : niveau de détail *Fin* ; en coupe, activer la visibilité des armatures |
| Cadres sans crochets | Charger un type de crochet à 135° dans le projet |
