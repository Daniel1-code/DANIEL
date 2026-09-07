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

Les modules Poutre, Dalle, Voile, Semelles, Longrine et Escalier suivent la feuille de route
decrite dans `docs/ARCHITECTURE-V3.md`.

## Rappel

Le moteur applique les dispositions constructives et la verification de resistance de section.
Il ne remplace pas l'analyse globale de la structure. Le ferraillage produit est un
avant-projet, a verifier et valider par l'ingenieur responsable du projet.
