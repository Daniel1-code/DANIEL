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

Les modules Poutre, Dalle, Voile, Semelles, Longrine et Escalier suivent la feuille de route
decrite dans `docs/ARCHITECTURE-V3.md`.

## Rappel

Le moteur applique les dispositions constructives et la verification de resistance de section.
Il ne remplace pas l'analyse globale de la structure. Le ferraillage produit est un
avant-projet, a verifier et valider par l'ingenieur responsable du projet.
