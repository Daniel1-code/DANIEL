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

Les modules Semelles filantes, Longrine et Escalier suivent la feuille de route decrite
dans `docs/ARCHITECTURE-V3.md`.

## Rappel

Le moteur applique les dispositions constructives et la verification de resistance de section.
Il ne remplace pas l'analyse globale de la structure. Le ferraillage produit est un
avant-projet, a verifier et valider par l'ingenieur responsable du projet.
