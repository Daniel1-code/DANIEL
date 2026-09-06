Plugin **Armatures de poteaux** pour **Revit 2026**.

## Installation

1. Telecharger et decompresser `ArmaturesPoteaux-Revit2026.zip` ci-dessous.
2. Clic droit sur `Installer.ps1` -> **Executer avec PowerShell**
   (ou dans un terminal : `powershell -ExecutionPolicy Bypass -File .\Installer.ps1`).
3. Redemarrer Revit 2026 : l'onglet **Beton arme** apparait dans le ruban.

Desinstallation : `powershell -ExecutionPolicy Bypass -File .\Installer.ps1 -Uninstall`

## Utilisation

Selectionner un ou plusieurs poteaux structurels en beton, puis
**Beton arme** -> **Armer les poteaux**. La fenetre affiche le ferraillage
calcule et la note de calcul justifiee ; **Generer** modelise les armatures
en une seule transaction, annulable d'un `Ctrl+Z`.

## Contenu du paquet

- `ArmaturesPoteaux.dll` : le plugin
- `ArmaturesPoteaux.addin` : le manifeste Revit
- `Installer.ps1` : installation sans droits administrateur
- `README.md` : mode d'emploi complet et regles de calcul appliquees

## Rappel

Le plugin applique les dispositions constructives de l'Eurocode 2 ou de
l'ACI 318-19. Il ne remplace pas la verification de resistance du poteau :
le ferraillage produit est un avant-projet, a valider par l'ingenieur.
