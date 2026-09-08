**DanCI Structural Studio** — Structural Design & Reinforcement Automation for Autodesk Revit.

Plugin de calcul, verification, dimensionnement, ferraillage et documentation des structures
en beton arme, integre a **Revit 2026**.

## Installation

1. Telecharger et decompresser `DanCI-Structural-Studio-Revit2026.zip` ci-dessous.
2. Clic droit sur `Installer.ps1` -> **Executer avec PowerShell**
   (ou : `powershell -ExecutionPolicy Bypass -File .\Installer.ps1`).
3. Redemarrer Revit 2026 : l'onglet **DanCI Structural Studio** apparait dans le ruban.

L'installeur desinstalle automatiquement l'ancien plugin "Armatures de poteaux".

Desinstallation : `powershell -ExecutionPolicy Bypass -File .\Installer.ps1 -Uninstall`

## Modules disponibles

- **DanCI Column Design** : poteaux rectangulaires et circulaires. Dispositions constructives
  EN 1992-1-1 art. 9.5, choix automatique des barres, cadres a zones critiques, epingles,
  ancrages et recouvrements, verification de resistance en flexion composee avec second ordre,
  apercu de la coupe, quantitatif et note de calcul.

- **DanCI Beam Design** : poutres rectangulaires et en T. Flexion (art. 6.1), effort
  tranchant par bielles a inclinaison variable (art. 6.2), cadres a espacement variable
  resserres aux appuis, chapeaux, ancrages et recouvrements, coupe et elevation dessinees,
  quantitatif et note de calcul.

- **DanCI Isolated Footing** : semelles isolees rectangulaires. Contraintes sous la semelle
  par distribution lineaire et par aire effective de Meyerhof (EN 1997-1 annexe D), capacite
  portante, non-soulevement, glissement et renversement, enrobage de fondation
  (art. 4.4.1.3(4)), flexion des consoles dans les deux directions, choix des nappes,
  effort tranchant a d du nu dans les deux directions, poinconnement de l'art. 6.4.4(2)
  par balayage des perimetres de controle, attentes en L, coupe et vue en plan dessinees,
  quantitatif et note de calcul.

- **DanCI Slab Design** : dalles pleines portant dans un sens, calculees sur une bande de
  1 metre. Combinaisons EN 1990, flexion, armature de repartition, espacements de
  l'art. 9.3.1.1, effort tranchant sans armatures, fleche par l'elancement limite
  (art. 7.4.2) et maitrise de la fissuration (art. 7.3.3). Le sens porteur est lu dans le
  modele et soumis avant le calcul. Coupe dessinee et diagramme des taux de travail,
  quantitatif et note de calcul.

- **DanCI Wall Design** : voiles en beton arme, verifies a deux echelles. Hors plan sur
  une bande verticale de 1 m : longueur de flambement de l'art. 12.6.5.1, excentricite
  minimale, second ordre par courbure nominale, capacite N-M, dispositions de l'art. 9.6
  (aciers verticaux, horizontaux, epingles de liaison). Dans le plan : effort tranchant de
  contreventement avec les aciers horizontaux tenant lieu de cadres, ecrasement des
  bielles, traction de rive et barres de rive. Elevation et coupe dessinees, quantitatif
  et note de calcul.

- **DanCI Strip Footing** : semelles filantes sous voile, dimensionnees au metre courant.
  Contraintes du sol par distribution lineaire et aire effective, capacite portante,
  non-soulevement, glissement, renversement, enrobage de fondation, flexion de la console,
  repartition longitudinale, effort tranchant a d du nu quand la section existe, attentes
  de voile en L. Coupe transversale dessinee avec les crochets reellement exiges,
  diagramme des contraintes du sol, quantitatif et note de calcul.

- **DanCI Grade Beam** : longrines de fondation. Effort de liaison entre semelles de
  l'EN 1998-5, deux nappes continues au minimum sismique de 0,4 %, section minimale selon
  le nombre de niveaux, flexion, effort tranchant, cadres fermes a espacement constant.
  Elevation avec les deux nappes et la double fleche de l'effort alterne, coupe
  transversale, quantitatif et note de calcul.

- **DanCI Stair Design** : volees d'escalier DROIT, calculees sur une bande de 1 metre.
  Descente de charge EN 1991-1-1 avec la paillasse corrigee de la pente et le poids des
  marches, travee isostatique sous deux charges reparties, situation alternative a charge
  concentree, flexion, effort tranchant, fleche par l'elancement limite, maitrise de la
  fissuration, chapeaux aux appuis, et noeud volee-palier a nappes croisees. Les volees
  balancees et helicoidales sont detectees et REFUSEES, pas approximees. Elevation avec
  les marches, agrandissement du noeud, quantitatif et note de calcul.

## Nouveautes de cette version

- **Enrobage calcule** selon l'EC2 art. 4.4.1 : classe d'exposition, duree d'utilisation et
  controle de production determinent la classe structurale, puis c_min,dur, c_min et c_nom.
  Un enrobage impose inferieur a l'exigence est signale comme non conforme.
- **Moteur valide** : six fiches de validation confrontent le moteur a des calculs manuels
  detailles (diagramme N-M, second ordre, interaction biaxiale, dispositions constructives,
  ancrages, enrobage). Elles sont adossees a des tests executes a chaque modification.
- **Trace du dimensionnement** : chaque poteau ferraille conserve dans le modele le moteur,
  la norme et les donnees qui l'ont produit. Relancer la commande propose de remplacer les
  armatures precedentes au lieu de les superposer.
- **Reperes de barres uniques**, prefixes par le repere Revit de l'element.
- **Module Poutre** : voir ci-dessus. Les cadres suivent la variation de l'effort tranchant
  le long de la travee, au lieu d'un espacement unique.
- **Module Semelle isolee** : l'EN 1997 entre dans le moteur. Le poids propre de la semelle
  charge le sol mais ne sollicite pas la structure : les deux contraintes sont distinguees,
  et les confondre est l'erreur classique du calcul de semelle.
- **Poinconnement des semelles fait correctement** : l'art. 6.4.4(2) impose de balayer les
  perimetres de controle entre le nu du poteau et 2d, en deduisant la reaction du sol
  comprise a l'interieur du perimetre et en majorant la resistance de 2d/a. Le perimetre le
  plus defavorable n'est jamais celui a 2d, et un test le verifie explicitement.
- **Effort tranchant verifie dans les deux directions.** Sur une semelle rectangulaire, c'est
  le grand debord qui gouverne, et il n'est pas forcement suivant X.
- **Deux fiches de validation supplementaires** (FOOT-01 centree, FOOT-02 excentree), portant
  la suite a plus de 200 cas de test executes a chaque modification.

- **Module Dalle** : voir ci-dessus. Une dalle n'est presque jamais limitee par sa
  resistance, mais par sa fleche : l'apercu le montre d'un coup d'oeil, et le message
  d'echec nomme l'action efficace, qui est d'epaissir.
- **Combinaisons d'actions EN 1990** : ELU eq. 6.10, ELS caracteristique et
  quasi-permanente, coefficients psi du tableau A1.1 selon la categorie d'usage. A l'ELU,
  un plancher de bureaux et un plancher de stockage donnent le meme ferraillage ; c'est a
  l'ELS que la categorie compte, et le moteur en tient compte.
- **Honnetete sur les coefficients de continuite** : l'Eurocode 2 ne fournit aucun tableau
  de moments pour travees continues, il demande une analyse. Le moteur en propose
  d'usuels pour l'avant-projet, en disant explicitement qu'ils ne sont pas de l'Eurocode,
  et accepte les moments d'une analyse exterieure.
- **Dalles bidirectionnelles detectees, pas calculees** : sous un rapport de cotes de 2,
  le moteur signale que le calcul en bande unique n'est pas representatif au lieu de
  rendre un resultat rassurant et faux.
- **Deux fiches de validation supplementaires** (SLAB-01 et SLAB-02), portant la suite a
  plus de 230 cas de test executes a chaque modification.

- **Module Voile** : voir ci-dessus. Un voile porteur courant est dimensionne par ses
  dispositions constructives, pas par sa resistance, et la fiche WALL-01 le montre.
- **Un element trop court n'est pas un voile.** L'art. 9.6.1 demande longueur >= 4 x
  epaisseur. En deca, l'Eurocode dit que c'est un poteau et ce sont les dispositions de
  l'art. 9.5 qui s'appliquent. Le moteur le signale et renvoie vers le module Column au
  lieu de produire un ferraillage reglementairement faux.
- **Un retour de voile ne raidit que s'il est proche.** Au-dela de trois fois la hauteur
  libre, la partie courante du voile flambe comme s'il n'etait tenu qu'en tete et en pied.
  Le moteur le dit au lieu de rendre un coefficient flatteur.
- **Le contreventement sismique n'est pas couvert et le moteur l'annonce** : les elements
  de rive confines de l'EN 1998-1 ne sont pas dimensionnes.
- **Deux fiches de validation supplementaires** (WALL-01 et WALL-02), portant la suite a
  plus de 260 cas de test executes a chaque modification. Deux de ces cas ont d'abord ete
  ecrits avec une attente fausse, corrigee apres confrontation au calcul manuel : c'est
  exactement a cela que servent les fiches.

- **Module Semelle filante** : voir ci-dessus. Il traite la semelle filante pour ce
  qu'elle est, et non comme une semelle isolee allongee.
- **Une semelle filante ne poinconne pas**, et le moteur le declare Sans objet au lieu de
  l'omettre : la charge d'un voile arrive repartie, pas concentree. Il rappelle toutefois
  le cas ou cette conclusion serait fausse, celui des poteaux portes par la meme semelle,
  et le lecteur Revit le detecte.
- **Le minimum d'armature gouverne, et de loin** : sur le cas de reference, A_s,min
  depasse l'acier de flexion d'un facteur sept.
- **L'ancrage transversal est verifie serieusement.** Le coefficient alpha_1 = 0,70 du
  tableau 8.2 n'est applique que si l'enrobage depasse trois diametres, ce qui tombe des
  HA14 avec 40 mm d'enrobage. Le crochet est pose, et le moteur renvoie explicitement au
  modele bielles-tirants de l'art. 9.8.2.2 qu'il ne fait pas.
- **Deux fiches de validation supplementaires** (STRIP-01 et STRIP-02), portant la suite a
  plus de 290 cas de test executes a chaque modification.

- **Module Longrine** : voir ci-dessus. Trois choses distinguent une longrine d'une poutre
  posee bas, et ce sont elles qui font le module.
- **C'est d'abord un TIRANT.** L'effort de l'EN 1998-5 art. 5.4.1.2(7) est axial et
  alterne, et il s'ajoute a la flexion dans les deux nappes a la fois. Hors dimensionnement
  sismique, aucun effort n'est invente : le moteur dit que l'EN 1992 seul n'en impose pas
  et renvoie a l'EN 1991-1-7.
- **Ses DEUX NAPPES FILENT.** L'EN 1998-1 art. 5.8.2(5) exige 0,4 % en haut et en bas, soit
  600 mm2 sur une 300 x 500 contre 177 mm2 pour l'EC2 : un facteur 3,4 qui gouverne a lui
  seul la nappe superieure.
- **CE SUR QUOI ELLE REPOSE change tout.** Le moteur la calcule toujours comme suspendue,
  ce qui est securitaire, et refuse de prendre credit d'un appui du sol sans analyse de
  poutre sur sol elastique.
- **Deux fiches de validation supplementaires** (GRADE-01 et GRADE-02), portant la suite a
  plus de 400 cas de test executes a chaque modification.

- **Module Escalier** : voir ci-dessus. Une volee est une dalle inclinee qui porte des
  marches, et les deux mots comptent.
- **SON POIDS PROPRE n'est pas gamma t.** La paillasse est mesuree perpendiculairement a
  la pente, donc la hauteur de beton au-dessus d'un point du plan vaut gamma t / cos alpha ;
  et chaque marche pese gamma R / 2 par metre carre de projection, terme purement
  geometrique qui ne depend pas de la pente. Negliger les deux corrections coute PLUS DE
  40 % du poids propre reel, toujours du cote non securitaire. Le moteur affiche l'ecart.
- **SA PORTEE porte deux charges.** La volee et le palier ne pesent pas la meme chose.
  Etaler celle de la volee partout est securitaire mais faux, de 6,9 % sur le cas de
  reference. Le calcul exact est une travee isostatique sous deux charges reparties, de
  solution fermee : le moteur le fait et rend les deux valeurs.
- **SON NOEUD est un angle rentrant tendu.** Une barre qui suivrait le pli developperait a
  l'interieur du coude une resultante dirigee vers l'exterieur du beton : elle ferait
  sauter l'enrobage, et le noeud cederait avant la section courante. Les deux nappes sont
  CROISEES et ancrees chacune dans la face opposee. Le moteur ne propose aucune variante
  suivant le pli, un test le verifie pour toutes les longueurs de palier, et l'apercu
  agrandit le noeud pour que le detail se voie.
- **C'est la fleche qui decide de l'epaisseur, pas la resistance.** Sur la volee de
  reference, une paillasse de 120 mm passe encore en flexion et echoue de 60 % en fleche.
- **La majoration de 15 % souvent accordee a la fleche des escaliers vient de la BS 8110.**
  Elle n'existe pas dans l'EN 1992-1-1 et le moteur ne l'applique pas.
- **Un escalier Revit n'accepte pas forcement d'armatures**, et le module ne le suppose
  pas : il pose la question a l'API et, si la reponse est non, il le dit AVANT le calcul et
  propose de continuer sans poser de barres. Pour les modeliser, la paillasse doit etre un
  plancher structurel incline ou un element in situ.
- **Ce qui n'est pas fait est ecrit** : le rendement du noeud n'est pas calcule -- le
  modele bielles-tirants des articles 5.6.4 et 6.5 n'est pas construit, seule la longueur
  d'ancrage disponible est verifiee ; les marches en console et le limon central ne sont
  pas traites. (Les deux autres limites annoncees en 3.8.0 -- charge concentree non
  combinee, volees balancees non detectees -- sont levees en 3.9.0, voir plus bas.)
- **Deux fiches de validation supplementaires** (STAIR-01 et STAIR-02), portant la suite a
  plus de 450 cas de test executes a chaque modification.

Les plans automatiques et le carnet de ferraillage suivent la feuille de route decrite
dans `docs/ARCHITECTURE-V3.md`.
## Ce que le module Escalier ne suppose plus (3.9.0)

Trois hypotheses tombent, dont une qui produisait un ferraillage faux.

- **BUG REEL corrige.** Une volee d'une seule contremarche n'a aucun giron, donc aucune
  portee -- et le moteur ferraillait quand meme, sur une portee reduite au seul palier. Il
  avertissait, mais produisait un plan. Le controle de geometrie est desormais une PORTE :
  geometrie impossible, aucun ferraillage. Il en va de meme de toute dimension nulle.
- **Les volees BALANCEES et HELICOIDALES sont detectees et refusees.** C'etait la limite la
  plus dangereuse du module : ni calculees, ni detectees, elles auraient rendu un resultat
  d'apparence normale et faux. Elles portent en flexion ET en torsion, et leur portee n'est
  pas la projection d'une droite. La detection est GEOMETRIQUE -- la ligne de foulee d'une
  volee droite est un segment de droite unique -- et non fondee sur un parametre de type
  dont le nom pourrait changer d'une version de Revit a l'autre. Quand la ligne de foulee
  n'est pas lisible, la forme reste INDETERMINEE : le moteur poursuit, parce que refuser
  bloquerait un usage legitime, mais il le dit et n'endosse pas l'hypothese a la place de
  l'ingenieur.
- **La charge concentree Q_k est verifiee, plus seulement citee.** L'article 6.3.1.2(1) de
  l'EN 1991-1-1 l'impose en ALTERNATIVE a la charge repartie. Le moteur affirmait jusqu'ici
  que la charge repartie gouverne une volee courante : c'etait vrai, mais non verifie. Il
  evalue desormais les deux situations et retient la plus defavorable -- et la marge etait
  plus mince qu'annonce, 23,32 contre 23,88 kN.m/m sur la volee de reference, soit 2,4 %.
  Sur une volee courte, Q_k gouverne franchement et c'est bien son moment qui sert au
  dimensionnement.
- **La largeur de diffusion de Q_k n'est fixee par aucun article de l'EN 1992-1-1.** Le
  moteur retient donc la plus DEFAVORABLE physiquement raisonnable, 45 degres a travers la
  seule paillasse : toute diffusion plus large donnerait un moment plus faible, donc la
  conclusion ne depend d'aucune regle contestable. C'est l'inverse du choix fait pour la
  fleche, ou la majoration de 15 % de la BS 8110 est refusee parce qu'elle, est favorable.
  Quand aucun article ne tranche, le moteur prend l'hypothese qui ne peut pas nuire.
- **Maitrise de la fissuration de l'art. 7.3.3**, absente jusqu'ici. Sur une volee
  interieure en XC1 elle est rarement determinante, mais l'omettre revenait a le supposer.
- **Une fiche de validation supplementaire** (STAIR-03), portant la suite a plus de 460 cas
  de test executes a chaque modification.

## Correction importante de la version 3.7.0

- **Une erreur a ete trouvee dans l'inversion du moment reduit en flexion simple**, la
  routine que TOUS les modules de flexion traversent depuis la phase 2. L'equation
  0,32 xi^2 - 0,8 xi + mu = 0 s'inverse en 1,25 (1 - racine(1 - 2 mu)), et le code ecrivait
  1,25 (1 - racine(1 - 2,5 mu)). Le bras de levier rendu etait trop court, donc la section
  d'acier trop grande : de 2 % sur un cas courant, jusqu'a 8 % pres de la limite d'axe
  neutre.
- **L'erreur allait dans le sens du surdimensionnement.** Aucun ferraillage insuffisant n'a
  ete produit par les versions 3.2.0 a 3.6.0 ; les sections etaient trop grandes, pas trop
  petites. Elle etait neanmoins fausse et se reclamait de l'article 6.1.
- **Un ferraillage recalcule avec la 3.7.0 peut donc etre legerement plus leger** qu'avec
  une version anterieure, a geometrie et efforts identiques. C'est la valeur correcte.
- Elle a survecu cinq phases parce que le cas de reference de ses tests avait ete ecrit
  d'apres le code et non d'apres l'Eurocode. Deux tests qui ne peuvent pas etre ecrits
  ainsi ont ete ajoutes : la composition des deux sens de la relation doit rendre
  l'identite, et la resultante de compression multipliee par le bras de levier doit
  restituer le moment applique.


## Rappel

Le moteur applique les dispositions constructives et la verification de resistance de section.
Il ne remplace pas l'analyse globale de la structure. Le ferraillage produit est un
avant-projet, a verifier et valider par l'ingenieur responsable du projet.
