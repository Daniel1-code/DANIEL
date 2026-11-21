# DanCI — Métré & BOQ

Extension Revit qui produit un **bordereau des quantités (BOQ) / devis quantitatif et
estimatif** traçable jusqu'aux `ElementId`, à partir d'une maquette Revit.

- Éditeur : DanCI (Dan Consult Intelligente), Kinshasa, RDC — AMISI RIDVAN DANIEL.
- Cibles : **Revit 2025, 2026, 2027**. Une base de code, un build par version.
- Ruban : panneau **« Métré & BOQ »** dans l'onglet **« DanCI »** (partagé avec les autres
  extensions DanCI).

Ce dossier est **indépendant** de DanCI Structural Studio (racine du dépôt) : il a sa propre
solution, son propre `Directory.Build.props` et son propre `global.json`.

> **État : Étape 1 / 8** : squelette multi-version, ruban, manifestes, commande « À propos ».
> Aucune quantité n'est encore calculée.

---

## 1. Arborescence

```
boq/
├── DanCI.BOQ.sln              configurations R2025 | R2026 | R2027 (pas de Debug/Release)
├── Directory.Build.props      configuration → RevitVersion, framework, constantes REVIT20xx
├── Directory.Build.targets    refuse toute autre configuration (Debug, Release…)
├── global.json                SDK .NET 10.0.100 (rollForward latestFeature)
├── build-all.ps1              compile + teste les 3 versions, produit deploy\ et artifacts\*.zip
├── src/
│   ├── DanCI.BOQ.Core/        modèles, moteur de règles, prix : 0 dépendance Revit
│   │   ├── ProductInfo.cs             versions (application, moteur, schéma JSON, Revit ciblé)
│   │   └── Diagnostics/               HostInfo, RuntimeReport, BoqLog (journal fichier)
│   ├── DanCI.BOQ.RevitAdapter/ extraction depuis l'API Revit (lecture seule)
│   │   └── RevitHostReader.cs         version / build / langue de Revit → HostInfo
│   ├── DanCI.BOQ.Export/      Excel, PDF, CSV (vide à l'étape 1)
│   ├── DanCI.BOQ.UI/          WPF, sans API Revit (vide à l'étape 1)
│   └── DanCI.BOQ.Addin/       IExternalApplication, commandes, manifeste
│       ├── BoqApplication.cs          panneau « Métré & BOQ », log de démarrage
│       ├── Commands/AboutCommand.cs   « À propos »
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

1. Fermer Revit, lancer `.\build-all.ps1` (ou copier le contenu de `deploy\Revit<année>\` dans
   `%AppData%\Autodesk\Revit\Addins\<année>\`).
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
   **ses boutons sont toujours là et fonctionnent** (voir §5, risque n°1).
6. Ouvrir le journal (`À propos` → *Ouvrir le dossier des journaux*) :
   `%AppData%\DanCI\BOQ\logs\boq-AAAA-MM-JJ.log`, il contient le bloc de démarrage.

**Si l'extension ne charge pas du tout**, le journal DanCI sera vide (notre code n'a pas été
exécuté). Regarder alors le journal Revit :
`%LocalAppData%\Autodesk\Revit\Autodesk Revit <année>\Journals\journal.*.txt` (rechercher
`DanCI` / `8A7F9F5D`), et m'envoyer le message d'erreur exact.

## 4. Revit 2027 : ce qui a été vérifié et ce qui reste à vérifier

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

## 5. Limites et risques identifiés à l'étape 1

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

## 6. Dépendances prévues : compatibilité .NET 8 → .NET 10

Relevé sur nuget.org à l'étape 1 ; à revérifier au moment de les ajouter (étape 6).

| Paquet | Version | Frameworks publiés | .NET 10 |
|---|---|---|---|
| ClosedXML | 0.105.1 | netstandard2.0, netstandard2.1 | chargeable (netstandard), pas de build net10 dédié |
| DocumentFormat.OpenXml (dép. de ClosedXML) | 3.5.1 | net8.0, **net10.0**, netstandard2.0 | ✅ |
| QuestPDF | 2026.9.2 | net6.0, net8.0, **net10.0**, netstandard2.0 | ✅. **Licence** : Community gratuite seulement sous un seuil de chiffre d'affaires annuel ; à confirmer sur questpdf.com avant usage commercial |
| System.Text.Json | inclus dans le runtime | — | ✅ (fourni par .NET 8 / .NET 10 ; ne pas embarquer de version NuGet) |

Point d'attention pour ClosedXML : il est publié pour netstandard uniquement. Il fonctionne
sous .NET 10, mais ses dépendances transitives (polices, `SixLabors.Fonts`…) sont à vérifier
dans Revit même, et c'est là que l'isolation du §4 compte.
