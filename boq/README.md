# DanCI — Métré & BOQ

Extension Revit qui produit un **bordereau des quantités (BOQ) / devis quantitatif et
estimatif** traçable jusqu'aux `ElementId`, à partir d'une maquette Revit.

- Éditeur : DanCI (Dan Consult Intelligente), Kinshasa, RDC — AMISI RIDVAN DANIEL.
- Cibles : **Revit 2025, 2026, 2027**. Une base de code, un build par version.
- Ruban : panneau **« Métré & BOQ »** dans l'onglet **« DanCI »** (partagé avec les autres
  extensions DanCI).

Ce dossier est **indépendant** de DanCI Structural Studio (racine du dépôt) : il a sa propre
solution, son propre `Directory.Build.props` et son propre `global.json`.

> **État : Étape 4 / 8** : squelette, ruban, « À propos » (étape 1) ; moteur de métré et de
> chiffrage (étape 2) ; extraction du béton, de la maçonnerie, des menuiseries et commande
> **« Diagnostic métré »** (étape 3) ; **coffrage, armatures (modélisées ou par ratio) et
> charpente métallique** (étape 4).

---

## 1. Arborescence

```
boq/
├── DanCI.BOQ.sln              configurations R2025 | R2026 | R2027 (pas de Debug/Release)
├── Directory.Build.props      configuration → RevitVersion, framework, constantes REVIT20xx
├── Directory.Build.targets    refuse toute autre configuration (Debug, Release…)
├── global.json                SDK .NET 10.0.100 (rollForward latestFeature)
├── build-all.ps1              compile + teste les 3 versions, produit deploy\ et artifacts\*.zip
├── install/                   Installer.cmd / .ps1, Desinstaller.cmd, LISEZMOI.txt (copiés dans chaque paquet)
├── config/                    configuration par défaut, livrée dans DanCI.BOQ\config\
│   ├── regles_mapping.json            lots 00–11, articles, règles de rattachement
│   ├── coefficients.json              pertes, chutes, casse, foisonnement (à valider)
│   ├── prix_unitaires.json            bibliothèque VIDE (modèle) : aucun prix livré
│   ├── parametres_devis.json          devise, taux, TVA 16 %, pied de devis, arrondis
│   ├── extraction.json                béton reconnu, ouvertures, coffrage, ratios d'armatures, acier
│   └── profils_acier.json             masses linéiques IPE, HEA, HEB, UPN (table de repli)
├── src/
│   ├── DanCI.BOQ.Core/        modèles, moteur de règles, prix : 0 dépendance Revit
│   │   ├── ProductInfo.cs             versions (application, moteur, schéma JSON, Revit ciblé)
│   │   ├── Diagnostics/               HostInfo, RuntimeReport, BoqLog (journal fichier)
│   │   ├── Model/                     ElementQuantity, Lot, Article, BoqLine, BoqResult, BoqIssue…
│   │   ├── Rules/                     motifs à jokers, critères, règles, catalogue
│   │   ├── Pricing/                   coefficients, bibliothèque de prix, paramètres du devis
│   │   ├── Configuration/             lecture JSON + validation (erreurs avec fichier et ligne)
│   │   ├── Engine/BoqEngine.cs        classement → coefficients → valorisation → pied de devis
│   │   └── Reference/RebarMass.cs     masses linéiques HA6…HA40
│   ├── DanCI.BOQ.RevitAdapter/ extraction depuis l'API Revit (lecture seule)
│   │   ├── RevitHostReader.cs         version / build / langue de Revit → HostInfo
│   │   └── Extraction/                béton, coffrage, maçonnerie, menuiseries, armatures, charpente
│   ├── DanCI.BOQ.Export/      CSV (métré brut, lignes BOQ, anomalies) ; Excel et PDF à l'étape 6
│   ├── DanCI.BOQ.UI/          WPF, sans API Revit (vide à l'étape 1)
│   └── DanCI.BOQ.Addin/       IExternalApplication, commandes, manifeste
│       ├── BoqApplication.cs          panneau « Métré & BOQ », log de démarrage
│       ├── Commands/AboutCommand.cs   « À propos »
│       ├── Commands/DiagnosticCommand.cs « Diagnostic métré » (CSV)
│       ├── Commands/AlwaysAvailable.cs
│       ├── Resources/IconFactory.cs   icône dessinée en code
│       └── Manifests/DanCI.BOQ.addin.template
└── tests/DanCI.BOQ.Tests/     xUnit (Core + Export), sur net8.0 ou net10.0 selon la config
```

Règle de dépendance (vérifiée par les tests **et** par la CI) : `Core`, `Export` et `UI` ne
référencent jamais l'API Revit.

| Configuration | Revit | Framework | Paquets API | Constantes |
|---|---|---|---|---|
| `R2025` | 2025 | `net8.0-windows` | Nice3point `2025.*` (résolu : 2025.4.60) | `REVIT2025`, `REVIT2025_OR_GREATER` |
| `R2026` | 2026 | `net8.0-windows` | Nice3point `2026.*` (résolu : 2026.4.10) | `REVIT2026`, `…_OR_GREATER` |
| `R2027` | 2027 | `net10.0-windows` | Nice3point `2027.*` (résolu : 2027.4.0) | `REVIT2027`, `…_OR_GREATER` |

Les projets sans Revit (Core, Export, Tests) suivent le même framework (`net8.0` / `net10.0`) :
le paquet 2027 est donc entièrement compilé et testé en .NET 10.

## 2. Compiler

Prérequis : **SDK .NET 10** (il compile aussi les cibles .NET 8). Revit n'est pas nécessaire
pour compiler.

```powershell
cd boq
.\build-all.ps1                      # 3 versions + tests + installation dans %AppData%
.\build-all.ps1 -NoDeploy            # sans installer (poste sans Revit, CI)
.\build-all.ps1 -Versions 2026,2027
```

Ou une seule version :

```powershell
dotnet build DanCI.BOQ.sln -c R2026
dotnet test tests\DanCI.BOQ.Tests\DanCI.BOQ.Tests.csproj -c R2026
```

À chaque compilation, le projet Addin :

1. **génère** `DanCI.BOQ.addin` depuis `Manifests\DanCI.BOQ.addin.template` (ou depuis
   `Manifests\DanCI.BOQ.R20xx.addin.template` s'il existe, pour une année qui aurait besoin
   d'un manifeste différent) ;
2. assemble `boq\deploy\Revit<année>\` = `DanCI.BOQ.addin` + `DanCI.BOQ\*.dll` ;
3. sous Windows, le copie dans `%AppData%\Autodesk\Revit\Addins\<année>\`
   (désactivable : `-p:DanCIDeploy=false`). **Si Revit est ouvert**, les DLL sont verrouillées :
   la copie échoue en avertissement, fermez Revit et recompilez.

`RevitAPI.dll` / `RevitAPIUI.dll` ne sont jamais embarquées (`ExcludeAssets=runtime`) ;
`build-all.ps1` refuse un paquet qui en contiendrait.

> **Visual Studio** : à cause de `global.json` (SDK 10), il faut **Visual Studio 2026**.
> Visual Studio 2022 ne sait pas utiliser le SDK .NET 10. En ligne de commande, `dotnet` suffit.

## 3. Tester dans Revit (Étape 1)

Pour **Revit 2026** puis **Revit 2027** :

1. Fermer Revit. **Sans SDK .NET** : décompresser `DanCI-BOQ-<version>-Revit<année>.zip`
   (artefact `DanCI-BOQ-paquets` du workflow « BOQ - Build », ou fourni directement) et
   double-cliquer sur **`Installer.cmd`** (désinstallation : `Desinstaller.cmd`). L'installeur
   copie dans `%AppData%\Autodesk\Revit\Addins\<année>\` et débloque les fichiers téléchargés.
   **Avec le SDK** : lancer `.\build-all.ps1`.
2. Démarrer Revit. Si Revit demande de faire confiance à l'add-in « DanCI - Metre & BOQ »,
   accepter (**Toujours charger**).
3. Onglet **DanCI** → panneau **Métré & BOQ** → **À propos**. Vérifier :
   - `Compilé pour : Revit 2026` (resp. 2027), sans ligne **ATTENTION** ;
   - `Revit : Autodesk Revit 2026 — version 2026.5 — build …` ;
   - `Runtime .NET : .NET 8.0.x` en 2026 **ou .NET 10.0.x** si la mise à jour de Revit a
     basculé le runtime — **notez ce que vous voyez, c'est précisément l'information recherchée** ;
   - en 2027 : `.NET 10.0.x`.
4. Le bouton doit fonctionner **sans projet ouvert** (écran d'accueil).
5. Si l'onglet DanCI de l'extension *Structures métalliques v0.91* est présent, vérifier que
   **ses boutons sont toujours là et fonctionnent** (voir §6, risque n°1).
6. Ouvrir le journal (`À propos` → *Ouvrir le dossier des journaux*) :
   `%AppData%\DanCI\BOQ\logs\boq-AAAA-MM-JJ.log`, il contient le bloc de démarrage.

**Si l'extension ne charge pas du tout**, le journal DanCI sera vide (notre code n'a pas été
exécuté). Regarder alors le journal Revit :
`%LocalAppData%\Autodesk\Revit\Autodesk Revit <année>\Journals\journal.*.txt` (rechercher
`DanCI` / `8A7F9F5D`), et m'envoyer le message d'erreur exact.

## 4. Moteur et configuration (Étape 2)

### Chaîne de calcul

```
ElementQuantity (1 quantité, 1 élément, 1 nature)        ← étape 3 : extraction Revit
   │  contrôles : valeur finie et ≥ 0, estimation ⇒ hypothèse, doublon (doc + id + nature + matériau)
   ▼
Règles (ordre croissant, la 1re qui correspond gagne)   regles_mapping.json
   ├─ exclure (motif obligatoire) ───────────────► liste des exclus (audit)
   ├─ aucune règle / unité ≠ article ────────────► lot NC « Non classés », jamais valorisé
   ▼
Ligne d'article : quantité nette = Σ exacte (decimal), part mesurée / estimée, fiabilité la plus faible
   ▼
Coefficients : brute = nette × Π(1 + c)                 coefficients.json
   ▼
Quantité valorisée (brute ou nette), ARRONDIE  ×  PU (converti au taux daté si autre devise)
   ▼
Lots → total travaux → majorations → HT → TVA → TTC (+ TTC en devise secondaire)
```

Une quantité porte une **nature** (`VolumeBeton`, `Coffrage`, `Armature`, `Maconnerie`,
`Comptage`…) : un même poteau produit un volume, une surface de coffrage et une masse d'acier,
et c'est la nature, pas la catégorie, qui les envoie vers trois articles différents. Les
catégories s'écrivent en nom `BuiltInCategory` (`OST_StructuralColumns`) : **les règles ne
dépendent pas de la langue de Revit**.

### Garde-fous

| Situation | Comportement |
|---|---|
| Deux règles de même `ordre`, règle sans critère, exclusion sans motif, article inconnu | **Erreur** : le moteur refuse de calculer |
| Propriété JSON inconnue (`"categorei"`) | **Erreur** avec ligne/colonne et liste des propriétés admises : une faute de frappe ne fabrique pas une règle qui correspond à tout |
| Quantité en m² rattachée à un article en m³ | **Erreur**, quantité laissée en Non classés : jamais de conversion |
| Pas de prix | Quantité conservée, montant vide, signalé ; totaux marqués **minorants** |
| Prix dans une autre devise sans taux, taux non daté | **Erreur** |
| Prix dont l'unité diffère de l'article | **Erreur** |
| Estimation sans hypothèse | **Erreur** (la quantité est comptée, l'hypothèse doit être donnée) |
| Même quantité fournie deux fois pour un élément | **Erreur**, comptée une seule fois |
| Coefficient marqué `aValider` | **Avertissement** à chaque utilisation (tous les coefficients livrés le sont) |

### Choix de calcul à confirmer de votre côté

1. **Quantité valorisée = brute par défaut** (`"quantiteValorisee": "Brute"`), comme le prévoit
   le flux du cahier des charges (coefficients, puis valorisation). Cela suppose des **PU hors
   pertes**. En pratique courante, le BOQ remis au client porte des quantités **nettes** et les
   pertes sont dans le sous-détail du prix. Si vos PU incluent les pertes, passez à `"Nette"`,
   sinon les pertes sont comptées deux fois.
2. **Coefficients multiples : produit** (1 + a)(1 + b), et non somme.
3. **Majorations** : par défaut chacune sur le total des travaux (`"base": "Travaux"`) ;
   `"Cumul"` pour une cascade. Les trois majorations livrées sont à 0 %.
4. **Arrondis** : quantité arrondie (2 décimales, 0 pour kg/U/Ff), PU et montants à 2,
   arrondi commercial (0,005 → 0,01). Le montant est calculé **sur la quantité arrondie**, pour
   que la formule Excel `Quantité × PU` de l'étape 6 retombe au centime sur le montant du moteur.
5. **Indice de fiabilité** = montant issu de quantités mesurées ÷ montant des lignes métrées
   (mesurées + estimées). Les forfaits saisis n'entrent pas dans l'indice (ni mesurés, ni
   estimés) ; leur montant est donné à part. Une ligne mixte est répartie au prorata de ses
   quantités mesurées et estimées.

### Reproductibilité

Les quantités sont additionnées en `decimal` : la somme ne dépend pas de l'ordre de lecture
des éléments (testé sur 300 éléments mélangés : BOQ identique au centime). Limite : la
conversion `double → decimal` garde **15 chiffres significatifs**, soit le micromètre cube
sur 1 000 000 m³, bien en deçà de la précision d'une maquette.

### Ce qui n'est PAS livré, volontairement

- **Aucun prix.** `prix_unitaires.json` est un modèle vide. Un prix inventé est pire qu'un
  prix absent.
- **Les coefficients livrés** sont des ordres de grandeur, tous `"aValider": true`.
- **Les ratios d'armatures (kg/m³)** livrés (étape 4) sont des ordres de grandeur, tous à valider.

## 4 bis. Extraction depuis Revit (Étape 3)

Commande **Diagnostic métré** (onglet DanCI → Métré & BOQ). **Lecture seule**, modèle hôte
entier. Elle écrit trois fichiers CSV (format Excel français) à côté du `.rvt`, ou dans
`Documents\DanCI\BOQ` si ce dossier n'est pas inscriptible :

| Fichier | Contenu |
|---|---|
| `…_metre_brut.csv` | une ligne par quantité d'élément, AVANT règles : ElementId, catégorie, type, matériau, niveau, nature, valeur, méthode, fiabilité, hypothèse, paramètres (classe de béton, phases, épaisseur, ouvertures…) |
| `…_BOQ_lignes.csv` | les lignes du BOQ calculées avec la configuration installée |
| `…_anomalies.csv` | extraction, configuration et moteur, avec les ElementId |

### Méthodes de mesure

| Ouvrage | Catégories | Méthode | Fiabilité |
|---|---|---|---|
| Béton, par matériau | Fondations, poteaux, poutres, sols, murs, escaliers (volées et paliers) | `Element.GetMaterialVolume` pour chaque matériau béton de l'élément : volume calculé par Revit **après jointures** | ✅ ; ⚠️ si l'élément a des Parts (étape 7) |
| Béton sans matériau | Familles déclarées en béton (`StructuralMaterialType`) | somme des volumes des solides ; classe inconnue | ✅ volume, signalé |
| Maçonnerie | Murs de base dont la couche porteuse n'est pas en béton | aire réelle des faces latérales (`GetSideFaces`), **moyenne ext./int.** (≈ axe), percée par Revit ; puis règle de déduction | ✅ ; ⚠️ si une ouverture a des dimensions illisibles et que la règle n'est pas « toutes » |
| Menuiseries | Portes, fenêtres | comptage, largeur et hauteur nominales | ✅ |

Béton reconnu par : actif structurel de classe Béton, sinon classe de matériau « Béton »,
sinon motif sur le nom (`extraction.json`). La raison est notée (`ReconnaissanceBeton`). La
**classe de béton** n'est lue que si le nom du matériau, ou à défaut du type, contient un
motif explicite `Cxx/yy`. Sinon elle reste vide, et la quantité tombe en Non classés.

**Déduction des ouvertures** (`extraction.json`) : par défaut, seules les ouvertures de plus de
0,5 m² sont déduites. La face lue est déjà percée de toutes les ouvertures. Le moteur
**rajoute** donc les petites, d'après la **baie brute** (Largeur/Hauteur brute), à défaut
d'après les dimensions nominales.

**Menuiseries** : l'article est ventilé **par type** (`"ventilerPar": "Type"`), avec une
sous-ligne par type et ses dimensions dans la désignation. Un prix se rattache par **nom de
type** (`"type": "P90*"` dans `prix_unitaires.json`), jamais par le code de sous-ligne, qui
dépend des types présents.

### À tester dans Revit (Étape 3)

1. Sur une petite maquette dont vous connaissez le métré, lancez **Diagnostic métré**.
2. Dans `…_metre_brut.csv`, comparez, élément par élément :
   - le volume de 3 poteaux, 3 poutres et 1 dalle à votre calcul manuel ;
   - la surface de 2 murs : un sans ouverture, un avec une fenêtre de moins de 0,5 m² et une porte.
3. Vérifiez que la colonne **ClasseBeton** (dans Paramètres) est remplie. Sinon, renommez les
   matériaux avec la classe (« Béton C25/30 »).
4. Envoyez-moi les trois CSV avec vos valeurs manuelles : chaque écart sera expliqué.

### Limites connues (à ne pas ignorer)

- **Toutes les phases sont comptées**, y compris l'existant et le démoli. Les phases sont
  dans le CSV (`PhaseCreation`, `PhaseDemolition`) ; le filtre arrive à l'étape 7.
- **Les jointures font foi** : deux éléments en béton qui se chevauchent **sans être joints**
  (dalle et poutre non jointes, par exemple) sont comptés deux fois. Détection à l'audit de
  l'étape 5. En attendant, vérifier les jointures.
- **Liens RVT non lus** (étape 7). Sélection, vue, niveau : étape 5.
- **Murs multicouches** : seule la couche porteuse détermine béton ou maçonnerie ; les couches
  de finition (enduits) seront métrées avec les revêtements.
- **Ouvertures non rectangulaires** (ouverture par esquisse) : dimensions illisibles, donc
  quantité marquée estimée si la règle n'est pas « toutes ».
- **Grandes maquettes** : pas encore de barre de progression ni d'annulation (étape 5) ; la
  durée de l'extraction est affichée et journalisée.
- **Comptage des blocs** (blocs/m²) : à faire, sous forme de coefficient d'article.

## 4 ter. Coffrage, armatures, charpente (Étape 4)

| Ouvrage | Méthode | Fiabilité |
|---|---|---|
| **Coffrage** (m²) | Pour chaque élément ayant fourni un volume de béton : faces des solides classées par leur normale (latérale, sous-face, dessus, contre le sol), puis **déduction des contacts** avec les éléments voisins (fondations, poteaux, poutres, sols, murs, escaliers, toits). Contact = la face plane est extrudée de 1 cm vers l'extérieur et le volume commun avec chaque voisin est calculé par Revit (intersection booléenne) ; surface de contact = volume ÷ 1 cm | ✅ ; ⚠️ si une face courbe (poteau rond) ou un contact non calculable touche un voisin : contact non déduit, surface surestimée |
| **Armatures modélisées** (kg) | Jeux de barres et barres des armatures surfaciques/linéaires : longueur totale du jeu × masse linéique HA du diamètre nominal ; treillis : masse du type de panneau | ✅ ; ⚠️ diamètre hors table HA6–HA40 (masse théorique πd²/4 × 7850) ; ❌ treillis sans masse |
| **Armatures par ratio** (kg) | Pour chaque élément en béton qui ne porte **aucune** armature modélisée : volume × ratio de sa catégorie (`extraction.json`) | ⚠️ **ESTIMÉ (ratio)**, hypothèse écrite dans la ligne ; ❌ si la catégorie n'a pas de ratio |
| **Charpente** (kg) | Familles d'ossature et de poteaux déclarées en acier : longueur de coupe × poids nominal du **type**, sinon × table `profils_acier.json` (IPE, HEA, HEB, UPN) ; à défaut volume × 7850 | ✅ ; ⚠️ volume × 7850 ; ❌ rien de lisible |
| **Assemblages** (kg) | Part de la masse des profilés (`assemblagesPart`, 10 % livré) | ⚠️ toujours estimé |

Les armatures sont rattachées au lot de leur **hôte** : le paramètre `HoteCategorie` envoie
les armatures des fondations vers l'article 02.05, les autres vers 03.08.

### À tester dans Revit (Étape 4)

1. **Coffrage** : choisir un poteau isolé sous une poutre, sur une semelle, et vérifier à la main :
   périmètre × hauteur, moins la surface des abouts de poutre. Le détail (`FacesLateralesM2`,
   `SousFacesM2`, `ContactsDeduitsM2`, `DessusNonCoffresM2`) est dans les Paramètres du CSV.
2. **Armatures** : sur un élément ferraillé dans Revit, comparer la masse à la nomenclature
   d'armatures de Revit (longueur totale × kg/m).
3. **Ratio** : sur un élément non ferraillé, vérifier « volume × ratio ».
4. **Charpente** : comparer la masse de 2 ou 3 profilés à « longueur × kg/m » du catalogue.

### Limites de l'étape 4

- **Ferraillage partiel** : un élément qui porte UNE barre modélisée est métré sur ses barres
  seules, jamais complété par ratio. Si vous modélisez seulement les attentes d'un poteau, ses
  armatures seront sous-estimées. L'audit de l'étape 5 signalera les éléments au ferraillage
  incomplet.
- **Recouvrements et chutes** : le coefficient CHUTES-ACIER (5 %) s'applique AUSSI aux
  quantités par ratio. Si vos ratios incluent déjà les recouvrements, mettez-le à 0 pour 03.08
  et 02.05.
- **Coffrage des éléments multicouches** : mesuré sur les faces extérieures de l'élément
  entier (couches de finition comprises). L'écart est négligeable en surface, mais à savoir.
- **Contacts avec des éléments non joints** : un voisin qui pénètre l'élément est déduit de la
  même façon (la face est cachée). En revanche, le **double comptage de volume** de deux
  éléments non joints reste à l'audit.
- **Durée** : le calcul des contacts fait une intersection booléenne par face et par voisin.
  Sur une grande maquette, la commande peut durer plusieurs minutes, sans barre de progression
  (étape 5). La durée est affichée.
- **Cornières, tubes, profils reconstitués** : absents de la table. Leur masse vient du poids
  nominal du type s'il est renseigné ; sinon volume × 7850, marqué estimé.

## 5. Revit 2027 : ce qui a été vérifié et ce qui reste à vérifier

Consigne : *lire la doc du SDK 2027 avant d'écrire le `.addin` 2027, ne rien supposer*.

**Je n'ai pas pu lire la documentation officielle** : `help.autodesk.com` (Revit API Developer's
Guide) n'est pas joignable depuis l'environnement de développement, et le SDK 2027 (`.msi`
Autodesk) n'y est pas disponible. Ce qui suit distingue strictement le vérifié du non vérifié.

**Vérifié (par la compilation) :**
- Les paquets `Nice3point.Revit.Api.*` 2027 ciblent `net10.0-windows` ; 2025 et 2026 ciblent
  `net8.0-windows`.
- Les propriétés utilisées (`VersionNumber`, `SubVersionNumber`, `VersionBuild`, `VersionName`,
  `Language`, `CreateRibbonTab`, `GetRibbonPanels`, `PushButtonData`, `TaskDialog`…) existent avec
  la même signature en 2025, 2026 et 2027. **Aucun `#if` n'a été nécessaire à cette étape.**

**Rapporté par des sources secondaires, NON vérifié dans la doc Autodesk :**
- Revit 2027 tourne en .NET 10 ; add-ins à recompiler en `net10.0-windows`.
- Le manifeste dispose de réglages d'**isolation des dépendances** : un élément
  `ManifestSettings` avec `UseRevitContext` (false = add-in chargé dans son propre
  `AssemblyLoadContext`) et `ContextName`. Une source les attribue à Revit **2026** déjà ;
  2027 ajouterait des **dépendances déclarées** entre add-ins (types API `AddInDependencyBase`,
  `ClientIdDependency`, `ContextNameDependency`) et des réglages `PublicAssemblies` /
  `UseAllContextsForDependencyResolution`.
- En 2027, les manifestes « tous utilisateurs » seraient refusés sous `ProgramData` et devraient
  être sous `Program Files\Autodesk\Revit\Addins\2027`. **Notre installation est par utilisateur
  (`%AppData%`), donc non concernée**, si l'information est exacte.

**Décision à l'étape 1** : le manifeste 2027 est **identique** au 2026 (schéma classique, sans
isolation). C'est le choix qui ne repose sur aucune hypothèse. L'isolation deviendra utile à
l'étape 6 : ClosedXML entraîne `DocumentFormat.OpenXml`, une DLL que d'autres add-ins chargent
souvent dans une autre version (conflit classique « première version chargée gagne »).

**À faire de votre côté (ou me donner accès) avant l'étape 6** : dans le SDK 2027
(`Revit 2027 SDK\Revit API Developers Guide` ou `RevitAPI.chm`, rubrique *Add-in Registration*
et *What's New*), relever :
1. la syntaxe exacte de `ManifestSettings` / `UseRevitContext` / `ContextName`, leur
   emplacement (dans `<RevitAddIns>` ou dans `<AddIn>`) et leur **valeur par défaut** en 2027 ;
2. l'année d'introduction (2026 ou 2027) ;
3. la syntaxe des dépendances déclarées et des assemblies publiques ;
4. tout changement des dossiers de manifestes.
Je créerai alors `Manifests\DanCI.BOQ.R2027.addin.template` (et R2026 si l'isolation y existe).

## 6. Limites et risques identifiés à l'étape 1

1. **Onglet partagé « DanCI ».** Revit lève une exception si un add-in crée un onglet qui
   existe déjà. Notre code la gère. Mais si l'extension *Structures métalliques v0.91* appelle
   `CreateRibbonTab("DanCI")` **sans** l'intercepter et qu'elle charge **après** nous, c'est
   elle qui échouera. L'ordre de chargement des add-ins n'est pas garanti. Je n'ai pas son code
   source : à vérifier dans Revit (§3, point 5). Correctif de son côté : un `try/catch` autour
   de `CreateRibbonTab`.
2. **Versions flottantes des paquets Revit** (`2026.*`) : imposé par le cahier des charges et
   pratique, mais une restauration future peut prendre un paquet plus récent. Les versions
   résolues sont listées au §1. Si la reproductibilité stricte du build l'exige, on figera les
   versions.
3. **.NET 8 en fin de support** (10 novembre 2026) : les builds 2025/2026 restent en `net8.0`
   car c'est le runtime de ces versions de Revit. Une assembly `net8.0` se charge dans un
   runtime .NET 10 : si Autodesk bascule 2025/2026 en .NET 10, le build actuel doit continuer
   à charger. La ligne « Runtime .NET » du journal permettra de le constater.

## 7. Dépendances prévues : compatibilité .NET 8 → .NET 10

Relevé sur nuget.org à l'étape 1 ; à revérifier au moment de les ajouter (étape 6).

| Paquet | Version | Frameworks publiés | .NET 10 |
|---|---|---|---|
| ClosedXML | 0.105.1 | netstandard2.0, netstandard2.1 | chargeable (netstandard), pas de build net10 dédié |
| DocumentFormat.OpenXml (dép. de ClosedXML) | 3.5.1 | net8.0, **net10.0**, netstandard2.0 | ✅ |
| QuestPDF | 2026.9.2 | net6.0, net8.0, **net10.0**, netstandard2.0 | ✅. **Licence** : Community gratuite seulement sous un seuil de chiffre d'affaires annuel ; à confirmer sur questpdf.com avant usage commercial |
| System.Text.Json | inclus dans le runtime | — | ✅ (fourni par .NET 8 / .NET 10 ; ne pas embarquer de version NuGet) |

Point d'attention pour ClosedXML : il est publié pour netstandard uniquement. Il fonctionne
sous .NET 10, mais ses dépendances transitives (polices, `SixLabors.Fonts`…) sont à vérifier
dans Revit même, et c'est là que l'isolation du §5 compte.
