# Armatures de poteaux — plugin Revit 2026

Plugin Revit 2026 qui **génère automatiquement le ferraillage des poteaux en béton armé** :
barres longitudinales, cadres, épingles, zones critiques resserrées et attentes de
recouvrement — le tout en respectant les dispositions constructives de l'**Eurocode 2**
(ou de l'**ACI 318-19**), avec une note de calcul qui justifie chaque valeur retenue.

L'objectif est volontairement simple : **sélectionner les poteaux, vérifier le tableau,
cliquer sur « Générer »**.

---

## 1. Installation (5 minutes)

### Option A — vous avez le paquet compilé

1. Téléchargez l'archive `ArmaturesPoteaux-Revit2026` (onglet **Actions** du dépôt →
   dernier build réussi → section *Artifacts*).
2. Décompressez-la.
3. Clic droit sur `Installer.ps1` → **Exécuter avec PowerShell**
   (ou, dans un terminal : `powershell -ExecutionPolicy Bypass -File .\Installer.ps1`).
4. Redémarrez Revit 2026. L'onglet **Béton armé** apparaît dans le ruban.

Pour désinstaller : `powershell -ExecutionPolicy Bypass -File .\Installer.ps1 -Uninstall`.

### Option B — compiler soi-même

Prérequis : **.NET SDK 8** (Windows). Revit n'a pas besoin d'être installé pour compiler,
l'API est fournie par NuGet.

```powershell
dotnet build src/ArmaturesPoteaux/ArmaturesPoteaux.csproj -c Release
copy install\ArmaturesPoteaux.addin src\ArmaturesPoteaux\bin\Release\
powershell -ExecutionPolicy Bypass -File install\Installer.ps1 -Source src\ArmaturesPoteaux\bin\Release
```

### Installation manuelle

Copier les fichiers dans le dossier des compléments de l'utilisateur :

```
%APPDATA%\Autodesk\Revit\Addins\2026\ArmaturesPoteaux.addin
%APPDATA%\Autodesk\Revit\Addins\2026\ArmaturesPoteaux\ArmaturesPoteaux.dll
```

> Si la DLL a été téléchargée depuis Internet, faites clic droit → **Propriétés** →
> **Débloquer**, sinon Revit refusera de la charger. `Installer.ps1` le fait pour vous.

---

## 2. Utilisation

1. Ouvrez un **projet** contenant des poteaux structurels en béton (une vue 3D est
   idéale pour voir le résultat).
2. *(facultatif)* Sélectionnez les poteaux à armer.
3. Ruban → **Béton armé** → **Armer les poteaux**.
   Si rien n'était sélectionné, Revit vous demande de choisir les poteaux, puis validez
   avec **Terminer**.
4. La fenêtre s'ouvre : à gauche les paramètres, à droite le tableau des poteaux et la
   note de calcul du poteau sélectionné.
5. **Calculer** met le tableau à jour, **Exporter la note** enregistre le justificatif,
   **Générer les armatures** modélise le ferraillage.

Les armatures créées sont sélectionnées à la fin de l'opération, et l'ensemble est
annulable d'un seul `Ctrl+Z`.

### Ce que le plugin décide tout seul

| Élément | Choix automatique |
|---|---|
| Diamètre des barres | balaye HA8 → HA40 et retient la combinaison la plus économique qui atteint A<sub>s</sub> |
| Nombre de barres | réparti par face proportionnellement à la géométrie, espacements libres vérifiés |
| Diamètre des cadres | `max(6 mm ; φ_l/4)` arrondi au diamètre commercial supérieur |
| Espacement des cadres | `min(20 φ_l ; b_min ; 400 mm)`, arrondi au multiple de 25 mm inférieur |
| Zones critiques | pied et tête, espacement × 0,6 (× 0,5 en ACI) |
| Épingles | ajoutées dès qu'une barre intermédiaire est à plus de 150 mm d'une barre tenue |
| Recouvrement | l<sub>0</sub> calculé selon EC2 8.4/8.7, utilisé comme longueur d'attente en tête |

Chaque champ peut être repris à la main : décochez la case « automatique » correspondante.

---

## 3. Règles appliquées

**Eurocode 2 — EN 1992-1-1**

| Règle | Article |
|---|---|
| A<sub>s,min</sub> = max(0,10 N<sub>Ed</sub>/f<sub>yd</sub> ; 0,002 A<sub>c</sub>) | 9.5.2(2) |
| A<sub>s,max</sub> = 0,04 A<sub>c</sub> | 9.5.2(3) |
| φ<sub>l</sub> ≥ 8 mm | 9.5.2(1) |
| 4 barres mini (6 en section circulaire) | 9.5.2(4) |
| φ<sub>t</sub> ≥ max(6 mm ; φ<sub>l</sub>/4) | 9.5.3(1) |
| s<sub>cl,tmax</sub> = min(20 φ<sub>l</sub> ; b ; 400 mm) | 9.5.3(3) |
| Espacement × 0,6 en zone critique | 9.5.3(4) |
| Barre comprimée à moins de 150 mm d'une barre tenue | 9.5.3(6) |
| Espacement libre ≥ max(φ ; d<sub>g</sub>+5 ; 20 mm) | 8.2(2) |
| l<sub>0</sub> = α<sub>6</sub> l<sub>b,rqd</sub>, α<sub>6</sub> = 1,5 | 8.4.3 / 8.7.3 |
| Zones critiques sismiques l<sub>cr</sub> = max(h<sub>c</sub> ; l<sub>cl</sub>/6 ; 450 mm) | EN 1998-1 5.4.3.2.2 |

**ACI 318-19** : ρ entre 1 % et 8 % (10.6.1.1), 4 barres mini (10.7.3.1),
s = min(16 d<sub>b</sub> ; 48 d<sub>bt</sub> ; petite dimension) (25.7.2.1),
recouvrement comprimé (25.4.9.2 / 25.5.5.1).

> ⚠️ Le plugin applique les **dispositions constructives**. Il ne remplace pas la
> vérification de résistance du poteau (flambement, flexion composée) : l'effort normal
> N<sub>Ed</sub> saisi ne sert qu'au calcul de A<sub>s,min</sub>. Le ferraillage produit
> est un avant-projet, à valider par l'ingénieur responsable.

---

## 4. Sections et cas pris en charge

- Poteaux **rectangulaires** et **circulaires**, y compris tournés en plan.
- Sections lues d'abord dans les paramètres de section structurelle, sinon déduites de
  la géométrie réelle (utile pour les familles personnalisées).
- Plusieurs poteaux traités en une seule commande et une seule transaction.

Non pris en charge : poteaux **inclinés**, sections en L/T/creuses, calcul de résistance,
nomenclatures automatiques.

---

## 5. En cas de problème

| Symptôme | Cause / solution |
|---|---|
| L'onglet n'apparaît pas | DLL bloquée par Windows (Propriétés → Débloquer) ou chemin du `.addin` incorrect |
| « Aucun type de barre d'armature » | Insertion → Charger la famille → Structure → Armature |
| « Cet élément ne peut pas recevoir d'armatures » | Le poteau n'est pas structurel ou son matériau n'est pas du béton |
| Les armatures sont invisibles | Vue 3D : passer le niveau de détail sur *Fin* ; en vue de coupe, activer la visibilité des armatures |
| Cadres sans crochets | Le projet ne contient aucun type de crochet : charger un crochet 135° |

---

## 6. Structure du dépôt

```
src/ArmaturesPoteaux/
  App.cs                     onglet et boutons du ruban
  Commands/                  commande principale, sélection, disponibilité
  Core/                      unités, géométrie du poteau, paramètres et résultats
  Design/                    règles Eurocode 2 / ACI 318 et moteur de choix des barres
  RevitOps/                  lecture des poteaux, types de barres, création des armatures
  UI/                        fenêtre WPF, tableau, note de calcul, icônes
install/                     manifeste .addin et script d'installation
.github/workflows/build.yml  compilation et paquet d'installation automatiques
```
