# DanCI — Métré & BOQ

Extension Revit qui produit un **bordereau des quantités (BOQ) / devis quantitatif et
estimatif** traçable jusqu'aux `ElementId`, à partir d'une maquette Revit.

- Éditeur : DanCI (Dan Consult Intelligente), Kinshasa, RDC — AMISI RIDVAN DANIEL.
- Cibles : **Revit 2025, 2026, 2027**. Une base de code, un build par version.
- Ruban : panneau **« Métré & BOQ »** dans l'onglet **« DanCI »** (partagé avec les autres
  extensions DanCI).

Ce dossier est **indépendant** de DanCI Structural Studio (racine du dépôt) : il a sa propre
solution, son propre `Directory.Build.props` et son propre `global.json`.

> **État : Étape 8 / 8 — outils de validation livrés, validation EN ATTENTE de la maquette
> test et du métré manuel.** Étapes 1 à 7 : squelette, moteur, extraction (béton, maçonnerie,
> menuiseries, coffrage, armatures, charpente), fenêtre et audit, Excel/PDF/CSV, phases, liens,
> Parts, forfaits, écriture des codes. Étape 8 : gabarit de métré manuel, comparateur
> (cible 2 % béton et acier), empreinte du métré et comparateur 2026 / 2027, protocole
> [`docs/VALIDATION.md`](docs/VALIDATION.md).

---

## 1. Arborescence

```
boq/
├── docs/VALIDATION.md         protocole de validation (étape 8) et fiche de résultats
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
│   │   ├── Audit/BoqAuditor.cs        audit préalable (§6) et indice de fiabilité
│   │   └── Reference/RebarMass.cs     masses linéiques HA6…HA40
│   ├── DanCI.BOQ.RevitAdapter/ extraction depuis l'API Revit (lecture seule)
│   │   ├── RevitHostReader.cs         version / build / langue de Revit → HostInfo
│   │   └── Extraction/                béton, coffrage, maçonnerie, menuiseries, armatures, charpente
│   ├── DanCI.BOQ.Export/      Excel (ClosedXML), PDF (QuestPDF), CSV — sans API Revit
│   ├── DanCI.BOQ.UI/          WPF sans API Revit : fenêtre BOQ (MVVM léger), progression annulable
│   └── DanCI.BOQ.Addin/       IExternalApplication, commandes, manifeste
│   (install/pdf/ : installeur des composants PDF, distribués à part)
│       ├── BoqApplication.cs          panneau « Métré & BOQ », log de démarrage
│       ├── Commands/AboutCommand.cs   « À propos »
│       ├── Commands/ShowBoqCommand.cs  fenêtre « Métré & BOQ »
│       ├── Commands/DiagnosticCommand.cs « Diagnostic métré » (CSV)
│       ├── Analysis/                  AnalysisService (analyse partagée), BoqHost (ExternalEvent)
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

0. **Fichiers à télécharger** (chacun fait moins de 4 Mo ; ordre indiqué par le nom, détail dans
   `install/LISEZ-MOI_INSTALLATION.txt`) :
   - `ETAPE1_Extension_DanCI-BOQ_<version>_Revit<année>.zip` : l'extension, un fichier par version de Revit ;
   - `ETAPE2_Composants-PDF_partie1.zip` (moteur de rendu) et `ETAPE3_Composants-PDF_partie2.zip`
     (police) : **une seule fois** pour toutes les versions de Revit, après l'ETAPE1, chacun avec
     son `Installer.cmd`. Le message vert « composants PDF COMPLETS » confirme l'installation.
   - Sans les étapes 2 et 3, tout fonctionne sauf l'export PDF, qui le signale. Une mise à jour de
     l'extension conserve les composants PDF déjà installés.
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

1. **Quantité valorisée = nette** (`"quantiteValorisee": "Nette"`) — **décision DanCI du
   22/11/2026** (version 0.11.1 ; avant : brute). Le BOQ porte les quantités nettes en place,
   et les pertes sont dans les PU (sous-détail de prix). Les coefficients de pertes restent
   calculés et affichés (quantité brute, utile pour les commandes) mais n'entrent pas dans le
   montant. `"Brute"` ne convient qu'avec des PU **hors pertes**.
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

- **Phases** : la commande Diagnostic métré compte toutes les phases ; la fenêtre filtre par
  phase depuis l'étape 7.
- **Les jointures font foi** : deux éléments en béton qui se chevauchent **sans être joints**
  (dalle et poutre non jointes, par exemple) sont comptés deux fois. Détection à l'audit de
  l'étape 5. En attendant, vérifier les jointures.
- **Liens RVT** : lus depuis l'étape 7 (fenêtre) ; la commande Diagnostic métré lit le modèle hôte seul.
- **Murs multicouches** : seule la couche porteuse détermine béton ou maçonnerie ; les couches
  de finition (enduits) seront métrées avec les revêtements.
- **Ouvertures non rectangulaires** (ouverture par esquisse) : dimensions illisibles, donc
  quantité marquée estimée si la règle n'est pas « toutes ».
- **Grandes maquettes** : barre de progression et annulation depuis l'étape 5.
- **Comptage des blocs** : disponible depuis la 0.9.0 (quantités dérivées, voir 4 octies).

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

## 4 quater. Fenêtre « Métré & BOQ » et audit (Étape 5)

Bouton **Métré & BOQ** : fenêtre **non modale**. Elle reste ouverte pendant que vous
travaillez dans Revit et ne touche jamais l'API Revit directement. Chaque action (analyser,
sélectionner, exporter) est transmise à Revit par un `ExternalEvent`.

1. **Portée** : modèle entier, vue active ou sélection courante, et facultativement des
   niveaux. Une armature est dans la portée si elle-même OU son hôte y est. Le filtre par
   niveau s'applique après extraction, si bien que les contacts du coffrage voient tout le
   modèle (un poteau du R+1 reste déduit du coffrage de la poutre du RDC).
2. **Analyser** : une fenêtre de progression, sur son propre fil d'exécution, reste réactive.
   **Annuler** arrête l'extraction au prochain élément, et aucun résultat partiel n'est
   affiché.
3. **Onglet BOQ** : lignes groupées par lot (avec total et mention « incomplet »), quantité
   nette, coefficient, quantité valorisée, PU, montant, statut ✅/⚠️/❌/✍. En dessous, le
   **détail du métré** de la ligne choisie : ElementId, type, matériau, niveau, quantité,
   règle, méthode. **Double-clic** sur une ligne ou un élément : sélection et zoom dans Revit.
4. **Onglet Audit** : rapport du §6, rubrique par rubrique, avec un bouton **Sélectionner**.
5. **Onglet Exclus** : quantités écartées et leur motif.
6. **Pied** : total travaux, majorations, HT, TVA, TTC, et **indice de fiabilité**. Sans prix,
   la part mesurée est donnée par nature de quantité.
7. **Exporter (CSV)** : métré brut, lignes du BOQ, rapport d'audit. Excel et PDF à l'étape 6.

### Contenu de l'audit

| Rubrique | Constats | Source |
|---|---|---|
| 1. Matériaux | élément structurel sans matériau, matériau « Par défaut » ; béton sans matériau | modèle |
| 2. Béton sans classe | aucune classe `Cxx/yy` dans le matériau ni le type | quantités |
| 3. Armatures | élément estimé par ratio ; catégorie sans ratio (❌) ; **ferraillage incomplet** (barres modélisées < 25 % du ratio de référence) | quantités |
| 4. Profilés | masse par volume × 7850, ou manquante | quantités |
| 5. Pièces et murs | pièce non placée, pièce non fermée, mur à hauteur non liée | modèle |
| 6. Doublons | avertissement Revit « instances identiques » ; même catégorie, type et boîte englobante au mm ; **chevauchement de béton non joint** (volume commun > 1 cm³, compté deux fois) | modèle et quantités |
| 7. Non classés | quantités sans règle de mapping | moteur |
| 8. Liens | lien Revit non chargé | modèle |
| 9. Autres | extraction en échec, unité incompatible, quantité manquante, prix, configuration… | tout |

### À tester dans Revit (Étape 5)

1. Ouvrir la fenêtre, analyser le **modèle entier**, puis la **vue active** d'un plan d'étage :
   le nombre de lignes doit baisser.
2. Double-cliquer une ligne du BOQ : les éléments doivent être sélectionnés et cadrés.
3. Créer volontairement un poteau en double (copier-coller au même endroit) et une dalle non
   jointe à une poutre : ils doivent apparaître dans la rubrique 6.
4. Sur une grande maquette, cliquer **Annuler** pendant l'analyse : l'arrêt doit être rapide.

### Limites de l'étape 5

- **Chevauchements** : seuls les chevauchements béton/béton sont recherchés. Le calcul fait une
  intersection booléenne par paire de voisins, ce qui est coûteux sur une grande maquette.
- **Doublons de position** : la boîte englobante au mm détecte les copies exactes, pas les
  éléments décalés d'un millimètre ; ceux-là relèvent du chevauchement.
- **Mémoire entre sessions** : depuis l'étape 7, forfaits, phase et liens dans `.danciboq.json`.
- **Sélection** : seulement dans le projet de la dernière analyse (refus explicite sinon).

## 4 quinquies. Export Excel, PDF et CSV (Étape 6)

Bouton **Exporter Excel / PDF** de la fenêtre, après une analyse. Les fichiers sont écrits à
côté du `.rvt` (ou dans `Documents\DanCI\BOQ`). Si un format échoue, les autres sont quand
même livrés et l'échec est affiché.

| Fichier | Contenu |
|---|---|
| `…_BOQ.xlsx` | 7 feuilles (détail ci-dessous) |
| `…_BOQ.pdf` | BOQ par lot avec sous-totaux, récapitulatif avec **graphique de répartition**, pied de devis (HT, majorations, TVA, TTC, TTC en CDF), indice de fiabilité, **cases de signature** (Établi par / Vérifié par / Maître d'ouvrage), pagination |
| `…_BOQ_lignes_brut.csv`, `…_metre_brut.csv` | export brut pour d'autres outils (virgule, point décimal) |
| `…_audit.csv` | rapport d'audit (format Excel français) |

**Feuilles du classeur**

1. **Page de garde** : projet, numéro, maître d'ouvrage, lieu, date, indice de révision, auteur,
   portée, version du moteur. Ces informations viennent de *Gérer → Informations sur le projet*
   de Revit ; l'indice de révision, du paramètre de projet `DanCI_BOQ_Revision`. Logo :
   `config\logo.png` (ou `.jpg`) s'il existe ; **aucun logo n'est fabriqué**.
2. **BOQ** : N° | Désignation | Unité | Quantité | PU | Montant, groupés par lot.
   - **Formules vivantes** : `Montant = SI(PU="";"";ARRONDI(Quantité×PU;2))`, sous-totaux par
     `SOMME`, récapitulatif, majorations, TVA et TTC calculés. Modifier un PU dans Excel recalcule
     tout.
   - Quantité et PU sont écrits arrondis, comme dans le moteur. **Testé : les formules
     recalculées tombent au centime sur les montants du moteur**, majorations en cascade, TVA et
     CDF compris.
   - Mise en page : A4, une page en largeur, en-tête répété, format `# ##0,00`. Les lignes sans
     prix sont surlignées et un bandeau « MINORANTS » apparaît. Les lignes estimées sont marquées
     **(E)**.
   - La colonne « Statut » est hors zone d'impression.
3. **Récapitulatif** : montant et part de chaque lot, barres de données (voir limite).
4. **Détail du métré** : une ligne par élément (article, ElementId, catégorie, type, matériau,
   niveau, quantité, méthode, statut, hypothèse, règle), avec filtres.
5. **Audit** : le rapport du §6, plus l'indice de fiabilité.
6. **Hypothèses** : quantité valorisée (brute ou nette), arrondis, TVA, majorations, taux de
   change daté, coefficients (« À VALIDER »), ratios d'armatures, conventions d'extraction, règles
   de mapping. **C'est la feuille qui défend le devis.**
7. **Exclus** : quantités écartées et leur motif.

### Limites de l'étape 6

- **Graphique Excel** : ClosedXML ne sait pas créer de graphique Excel natif. La répartition est
  représentée par des barres de données dans la feuille Récapitulatif, et par un vrai graphique
  dans le PDF.
- **Conflit de DLL** : si un autre add-in a déjà chargé une autre version de
  `DocumentFormat.OpenXml` (utilisé par ClosedXML), l'export Excel peut échouer dans Revit.
  Le PDF et les CSV sont alors livrés quand même. **À propos** et le journal indiquent quelle
  version est chargée et depuis quel dossier. Le remède de fond est l'isolation des add-ins (§5).

### Licence QuestPDF (PDF) — Community, éligibilité confirmée par DanCI (22/11/2026)

QuestPDF est distribué sous licence *Community* (gratuite) ou payante (*Professional*,
*Enterprise*). Extrait du fichier de licence du paquet 2026.9.2 :

- **Petite entreprise** : licence Community gratuite si le chiffre d'affaires annuel brut du
  dernier exercice clos est **inférieur à 1 000 000 USD**, consolidé avec les entités sous
  contrôle commun.
- **Exclus de la Community**, quel que soit leur chiffre d'affaires : entités publiques,
  administrations, sociétés cotées.
- Une entreprise non éligible doit acheter une licence avant tout usage en production
  (60 jours d'évaluation autorisés).

L'extension déclare la licence **Community** (`QuestPDF.Settings.License`). **DanCI a confirmé
son éligibilité le 22/11/2026** (chiffre d'affaires inférieur au seuil, pas d'entité publique).
À revérifier chaque année, et avant de diffuser l'extension à une autre société : si le seuil
est dépassé, il faut une licence Professional, et la déclaration doit être changée.

## 4 sexies. Phases, liens, Parts, paramètres, fichier projet (Étape 7)

### Phases

**Phase métrée** (fenêtre, 2ᵉ ligne). Par défaut, la dernière phase du projet (en général
« Nouvelle construction »). Statut de chaque élément dans cette phase (`GetPhaseStatus`) :

| Statut Revit | Traitement |
|---|---|
| Nouveau (créé dans la phase) | **compté** (`StatutPhase = Nouveau`) |
| Démoli dans la phase | compté **dans le lot DEM** si « Démolitions de la phase » est coché (volume béton, surface de maçonnerie, dépose des menuiseries, masse de charpente) ; sinon ignoré. Ni coffrage, ni armatures, ni assemblages pour un ouvrage démoli |
| Existant, temporaire, phase ultérieure | ignoré (compteurs dans le journal) |

`(Toutes les phases)` reproduit le comportement des étapes 3 à 6 (tout compter) et est signalé
à l'audit (`PHASE_NON_CHOISIE`). La commande **Diagnostic métré** compte toutes les phases.

### Liens RVT

**Liens RVT** : cocher les liens à inclure (liens chargés seulement ; un lien déchargé est
signalé à l'audit).
- **Chaque instance** d'un lien est métrée : deux instances valent deux bâtiments.
- Les quantités (volumes, surfaces, longueurs) ne dépendent pas de la position. La
  **transformation du lien** est appliquée aux positions, ce qui permet de détecter les doublons
  entre le modèle hôte et le lien.
- La **phase** est retrouvée dans le lien **par son nom**. Si elle n'existe pas dans le lien,
  c'est une erreur d'audit, et tous les éléments du lien sont comptés.
- Portée « vue active » ou « sélection » : un lien est inclus **en entier** si son instance est
  visible ou sélectionnée.
- **Double-clic** sur un élément de lien : sélection par référence de lien, zoom sur l'instance.
- Les constats d'audit sur un lien portent le nom du lien et ne sélectionnent jamais un élément
  de l'hôte de même ElementId.

### Parts

Un élément découpé en pièces (Parts) : **ce sont ses Parts qui sont comptées**, jamais les deux.
- Volume de béton par matériau, Part par Part ; les règles se lisent sur l'élément d'origine
  (catégorie, type, niveau).
- Le coffrage se mesure sur l'élément d'origine.
- Une Part dont l'élément d'origine porte des armatures n'est pas estimée par ratio.

### Forfaits et fichier projet `<projet>.danciboq.json`

Onglet **Forfaits** : installation de chantier, études, essais… (lot, code facultatif,
désignation, unité, quantité, PU, devise, justification).
- **Enregistrer et recalculer** écrit le fichier projet **à côté du `.rvt`** (sinon dans
  `Documents\DanCI\BOQ`) et recalcule le BOQ **sans réanalyser la maquette**.
- Le fichier garde aussi la phase, l'option démolitions et les liens retenus. Il survit à toutes
  les réanalyses.
- L'écriture est atomique, avec une copie `.bak` de la version précédente. Un fichier existant
  illisible n'est **jamais écrasé** sans que vous l'ayez corrigé ou supprimé.

### Écriture des codes dans le modèle

Bouton **Écrire les codes dans le modèle** : **seule action qui modifie le modèle**.
- Confirmation demandée.
- Un groupe de transactions nommé « DanCI BOQ — écriture des codes BOQ », défait par **un seul
  Ctrl+Z**.

Paramètres partagés à **GUID fixes**, communs à tous les projets DanCI, groupe « Données » :

| Paramètre | Type | Lié à | Rôle |
|---|---|---|---|
| `DanCI_BOQ_Code` | Texte, instance | catégories métrées | codes des articles de l'élément (`03.01; 03.07; 03.08`) |
| `DanCI_BOQ_Lot` | Texte, instance | catégories métrées | lot(s) de l'élément |
| `DanCI_BOQ_Exclure` | Oui/Non, instance | catégories structurelles, murs, menuiseries… | à cocher pour exclure un élément |
| `DanCI_BOQ_Revision` | Texte | Informations sur le projet | indice de révision (page de garde) |

- **Fichier de paramètres partagés** : celui de DanCI (`%AppData%\DanCI\BOQ\…`) est utilisé
  le temps de l'opération, puis **le vôtre est restauré**.
- **Paramètre homonyme non partagé** (créé à la main) : refus explicite, modèle non modifié.
- **Ignorés et comptés** : les éléments de liens (non modifiables) et, en partage de travail, les
  éléments empruntés par un autre utilisateur.

### À tester dans Revit (Étape 7)

1. Projet avec une phase « Existant » et une phase « Nouvelle construction » : métrer la
   nouvelle construction, puis cocher les démolitions (lot DEM).
2. Lier un modèle structure, le cocher, analyser, puis double-cliquer sur une ligne qui en
   provient.
3. Créer des Parts sur une dalle multicouche : les Parts remplacent la dalle dans le détail.
4. Saisir 2 forfaits, « Enregistrer et recalculer », fermer Revit, rouvrir : les forfaits sont là.
5. **Sur une copie du projet** : « Écrire les codes dans le modèle », vérifier les paramètres
   d'un poteau, puis Ctrl+Z.

### Limites de l'étape 7

- **Liens imbriqués** (lien d'un lien) : non lus.
- **Constats du modèle dans les liens** (matériaux par défaut, pièces, murs, doublons Revit,
  chevauchements) : relevés seulement dans le modèle hôte. Les constats calculés sur les
  quantités (béton sans classe, ratios, profils, doublons de position) couvrent aussi les liens.
- **Filtre par niveau** : il compare les noms de niveau, qui peuvent différer dans un lien.
- **Prix** : bibliothèque par projet disponible depuis la 0.9.0 (voir 4 octies).

## 4 septies. Validation (Étape 8)

Onglet **Validation** de la fenêtre. Le protocole complet est dans
[`docs/VALIDATION.md`](docs/VALIDATION.md) : critères, conventions du métré manuel, échantillon,
interprétation des écarts, fiche de résultats.

| Outil | Rôle |
|---|---|
| **Générer le gabarit de métré manuel** | Excel des éléments à mesurer, **sans les quantités du modèle** (métré manuel indépendant) |
| **Comparer au gabarit rempli…** | Rapport : écart par élément, par article et par nature ; verdict sur le **total** béton et acier (≤ 2 %) ; pistes d'explication pour chaque écart, tirées de la méthode de mesure |
| **Empreinte du métré** | SHA-256 des quantités (6 décimales), affichée dans la fenêtre, sur la page de garde Excel, dans les hypothèses et au pied du PDF |
| **Comparer deux métrés bruts (CSV)…** | Revit 2026 contre 2027 : liste chaque quantité qui diffère (valeur, absence, statut) |

**Testé hors Revit** :
- un métré brut relu depuis son CSV, aux deux formats, redonne exactement la même empreinte ;
- l'empreinte ne dépend pas de l'ordre des éléments, ignore le bruit de calcul (10⁻⁹) et détecte
  2 cm³ ;
- le gabarit ne contient aucune quantité du modèle.

**Pas encore fait, et impossible sans vous** : la validation elle-même. Elle demande la maquette
test, le métré manuel et deux analyses dans Revit 2026 et 2027. Aucun résultat de validation
n'est fourni d'avance.

## 4 octies. Bibliothèques de prix, nombre de blocs, robustesse (0.9.0)

### Bibliothèques de prix par projet (§7 du cahier des charges)

Ligne **Prix** de la fenêtre :

1. **Modèle Excel des prix…** : classeur listant **tous les articles** du catalogue (PU vide
   s'il n'y a pas de prix) et les prix existants. Pour chaque ligne : PU, devise (liste
   USD / CDF), date, source, décomposition facultative (matériaux, main-d'œuvre, matériel) et,
   pour les menuiseries ventilées, un motif de type (`P90*`).
2. **Choisir…** :
   - un `.xlsx` rempli est **importé**. Les lignes sans PU ne sont pas importées : aucun prix
     n'est inventé. Une unité ou une devise invalide est refusée, avec le numéro de ligne. Le
     résultat est enregistré en `.json` à côté du classeur ;
   - un `.json` est utilisé tel quel.

   Dans les deux cas, la bibliothèque est retenue **pour ce projet** (fichier `.danciboq.json`,
   chemin relatif si elle est dans le dossier du projet) et le BOQ est recalculé.
3. **Installation** : revenir à `config\prix_unitaires.json`.

Plusieurs bibliothèques peuvent donc coexister (par client, par ville : Kinshasa,
Lubumbashi…). Une bibliothèque choisie mais **introuvable** est une **erreur bloquante** :
jamais de repli silencieux sur d'autres prix.

### Nombre de blocs (§4 du cahier des charges)

Un article peut porter des **quantités dérivées** (`"quantitesDerivees"` dans
`regles_mapping.json`). Les articles 05.01 et 05.02 livrés donnent le nombre de blocs :
quantité **brute** (casse comprise) × 12,5 U/m² (bloc de 40 × 20 cm en face, joints non
déduits), arrondi au bloc supérieur. Le résultat apparaît dans la désignation : « … — soit ≈ 492 U
(blocs 40×20×20, 12,5 U/m²) ». Cette quantité est une **information**, jamais valorisée. Si vos
blocs ou vos joints diffèrent, ajustez `parUnite`.

### Corrections de robustesse

- **Annuler pendant la fin de l'analyse** : plus de risque de fermeture de Revit (exception
  sur le fil de progression).
- **Demande refusée par Revit** (commande ou dialogue en cours) : la fenêtre ne reste plus
  bloquée sur « Analyse en cours… ».
- **Portée « vue active » sur une nomenclature ou une feuille** : refus explicite, au lieu d'un
  résultat vide trompeur.
- **Coffrage** : les contours des faces sont orientés avant l'extrusion de la lame de contact
  (contour extérieur dans un sens, trous dans l'autre, puis un essai inversé). Sans cela, un
  refus de Revit aurait fait compter toutes les faces sans déduction des contacts.

## 4 nonies. Tableau du §4 complété (0.10.0)

Les ouvrages du §4 qui avaient une règle de mapping mais aucune extraction sont maintenant
métrés :

| Ouvrage | Catégories | Méthode | Fiabilité |
|---|---|---|---|
| **Fouilles** (m³) | fondations | sous la **face inférieure réelle** de la fondation : emprise dans son propre repère (pas la boîte englobante), surlargeur de travail, talus, profondeur jusqu'au **terrain naturel** (niveau désigné dans `extraction.json` → `terrassement`), béton de propreté compris ; volume en tronc de pyramide | ⚠️ **toujours estimé** (fouilles non modélisées) ; sans niveau de terrain naturel : non calculé, signalé |
| **Béton de propreté** (m³) | fondations | face inférieure élargie du débord (A + P·d + 4d²) × épaisseur (5 cm et 5 cm par défaut) | ⚠️ estimé (convention) |
| **Enduits, peinture, revêtements de sol, étanchéité, couverture** (m²) | murs, sols, plafonds, toits | aire de chaque **matériau de finition** reconnu par son nom (`extraction.json` → `finitions`), calculée par Revit (`GetMaterialArea`, ouvertures percées), peintures posées avec l'outil « Peindre » comprises | ✅ |
| **Couverture sans matériau reconnu** (m²) | toits non béton | surface **réelle** des faces de dessus (pente incluse, pas la projection) | ✅ |
| **Toits en béton** (m³) | toits | volume par matériau, comme les dalles (toitures-terrasses → article des dalles) | ✅ |
| **Canalisations, conduits, chemins de câbles, gaines** (ml) | réseaux | longueur, **une ligne par section** (« Ø 110 mm », « 300 × 100 mm ») ; prix par section avec `"valeur": "Ø 110"` dans la bibliothèque | ✅ |
| **Appareils** (U) | sanitaires, appareillage, luminaires, équipements | comptage, **une ligne par type** | ✅ |
| **Familles génériques** (U et m³) | modèles génériques, équipements spécialisés | comptage et volume ; sans règle de mapping, ils remontent en **Non classés** (visibles), comme le prévoit le §4 | ✅ |

**Correction de la configuration livrée.** Le coefficient de foisonnement (+25 %)
s'appliquait à l'article des fouilles, ce qui le valorisait 25 % trop haut : une fouille se paie
au volume **en place**. Il est retiré. Le **volume foisonné à évacuer** apparaît désormais en
quantité dérivée de l'article 01.01 (information, non valorisée).

### Limites de la 0.10.0

- **Fouilles voisines qui se recoupent** (semelles rapprochées, semelle filante contre semelle
  isolée) : comptées deux fois. Une fouille en rigole continue doit être vérifiée à la main ;
  l'hypothèse le rappelle dans chaque ligne.
- **Fouilles en pleine masse** (sous-sol, radier général) : seul le volume sous l'emprise des
  fondations est calculé ; le terrassement général n'est pas modélisé.
- **Relevés d'étanchéité** : non comptés (périmètre × hauteur à ajouter en forfait).
- **Finitions des pièces** (paramètres « Finition des murs / du sol » des pièces) : non lues.
  Seules comptent les couches et les peintures modélisées.
- **`GetMaterialArea` sur une couche de mur** : la convention de Revit (une face de la couche)
  est à confirmer à la validation, sur un mur enduit des deux côtés.
- **Raccords et accessoires de réseaux** : non comptés (forfait ou règle à ajouter).

## 4 decies. Autotest et fichier de support (0.10.1)

Bouton **Autotest** (disponible même sans projet ouvert). En un clic, il contrôle :
- Revit et le runtime .NET (avec l'alerte d'année) ;
- les **panneaux de l'onglet DanCI**, ce qui montre si les autres extensions DanCI sont chargées ;
- la configuration, puis le moteur sur des données fictives, l'ordre des éléments étant
  inversé pour vérifier que le résultat ne change pas ;
- l'**Excel**, écrit puis relu, avec la formule du TTC comparée au moteur ;
- le **PDF** (composants installés ? fichier généré ?) ;
- les **CSV** (empreinte identique après relecture) et l'écriture du journal ;
- les **dépendances chargées**, avec leur dossier (conflit possible avec un autre add-in) ;
- l'**inventaire du projet ouvert**, en lecture seule : phases, niveaux, liens et leur état,
  nombre d'éléments par catégorie, avertissements.

Chaque contrôle est isolé : un échec n'empêche pas les suivants, et l'exception est rapportée
avec son type et sa cause. Le résultat est **un seul fichier**,
`Documents\DanCI\BOQ\DanCI-BOQ-support_<date>.zip` : rapport, journal du jour, Excel et PDF
d'essai. **C'est le fichier à envoyer pour tout diagnostic.**

## 4 undecies. Paramètres du projet, tableau de bord, page de garde futuriste (0.11.0)

Deux nouveaux boutons dans le ruban : **Tableau de bord** et **Paramètres projet**. Ils ouvrent
la fenêtre Métré & BOQ sur l'onglet correspondant.

**Paramètres du projet**, enregistrés dans `<projet>.danciboq.json` à côté du .rvt, sans
modifier le modèle :
- *Identité* : nom, objet, n°, référence du devis, indice, date, maître d'ouvrage, maître
  d'œuvre, BET, entreprise, lieu, établi / vérifié par, validité, délai, conditions de paiement,
  notes, **surface de référence** (pour le coût au m²). Un champ vide reprend les
  Informations sur le projet de Revit.
- *Devis* : devise, **TVA**, majorations (imprévus, frais généraux, bénéfice…, sur les travaux
  ou en cascade), devise secondaire et taux daté. Ces valeurs remplacent celles de
  `parametres_devis.json` pour ce projet seulement. Les décimales et la quantité valorisée
  restent celles de l'installation. Un réglage invalide est une erreur, comme dans le fichier.
- *Prix unitaires* : un tableau avec tous les articles du catalogue (PU, devise, date, source,
  décomposition facultative). On peut rechercher un article ou n'afficher que ceux sans prix.
  La bibliothèque de l'installation n'est **jamais** modifiée : les prix sont écrits dans
  `<projet>_prix.json`, ou dans la bibliothèque déjà choisie pour le projet, avec une copie
  `.bak`. Les prix par type ou par diamètre sont conservés. Aucun prix n'est proposé.
- *Page de garde* : style **Futuriste** (fond nuit, cyan et orange) ou Classique, sous-titre,
  image du projet, logo. On peut aussi **importer une page de garde faite ailleurs** (PNG ou
  JPG en A4 portrait) : elle remplace la page générée.

Seuls les paramètres **enregistrés** servent au calcul et aux exports : une saisie en cours
ne change jamais un devis sans que vous le sachiez.

**Tableau de bord de pilotage** (fenêtre, PDF page 2, feuille Excel « Tableau de bord ») :
- totaux HT et TTC (dans Excel, ce sont des formules vers la feuille BOQ) ;
- coût TTC au m², part des lignes qui ont un prix, indice de fiabilité, audit, ratio armatures / béton ;
- répartition par lot, Pareto des 10 articles qui pèsent le plus, quantités clés nettes avec
  leur part mesurée ;
- voyants avec l'action à faire, et la liste des prix à saisir.

Ces indicateurs sont calculés **uniquement** à partir du BOQ, de l'audit et du métré. La note de
santé sur 100 (prix 50, fiabilité 30, audit 20) sert au pilotage ; elle ne remplace pas la
vérification du métreur.

Le PDF commence maintenant par la page de garde, puis le tableau de bord, puis le BOQ. Les
pages du BOQ restent sur fond blanc, pour l'impression et la signature.

## 4 duodecies. Décisions DanCI et mise à jour (0.11.1)

- **Quantité valorisée : nette** (décision du 22/11/2026). Les pertes sont comprises dans les PU.
  Le PDF le précise sous le récapitulatif, et le classeur dans la feuille « Hypothèses ».
- **Licence QuestPDF Community** : DanCI a confirmé son éligibilité (voir § Licence QuestPDF).
- **Mise à jour sans perte** : avant de remplacer le dossier `config`, l'installateur le
  sauvegarde entièrement dans `%AppData%\DanCI\BOQ\sauvegardes\config_Revit<année>_<date>`.
  Il remet ensuite en place votre `logo.png` / `logo.jpg` et votre `prix_unitaires.json` s'il
  contient des prix. Les règles et les paramètres par défaut sont ceux de la nouvelle version.
  Les réglages propres à chaque projet (TVA, majorations, prix) se font désormais dans
  **Paramètres projet**, et l'installateur n'y touche pas.

## 4 terdecies. Autotest de la fenêtre (0.11.2)

La fenêtre WPF ne peut pas être affichée dans l'environnement de développement (Linux) : ses
onglets ne sont vérifiés qu'à la compilation. **Autotest** comble ce manque sur le poste de
l'utilisateur. Il crée la vraie fenêtre hors de l'écran, avec le devis d'essai et un hôte
factice : aucune demande n'atteint Revit et le modèle n'est pas touché. Ensuite, il :
- affiche chaque onglet, et chaque volet des Paramètres du projet ;
- en fait une **capture PNG** (`fenetre_*.png`, dans le zip de support) ;
- relève les **erreurs de liaison WPF** (champ mal relié, conversion impossible).

Une erreur de XAML (fenêtre qui ne s'ouvre pas) est rapportée avec son type, sa cause et sa
ligne, au lieu de bloquer l'extension.

## 4 quaterdecies. Interface entièrement futuriste, Excel au même modèle (0.12.0)

- **Fenêtre** : fond nuit sur toute la fenêtre, avec accents cyan et orange. Les barres de
  portée, de phase et de prix, les boutons (l'action principale en cyan), les listes, les champs,
  les onglets (soulignés en cyan), les tableaux (BOQ, audit, forfaits, prix), les groupes, le
  pied de devis et la fenêtre de progression suivent le même thème.
- Le thème est dans `Themes/FuturisticTheme.xaml` et se charge dans un `try/catch`. En cas
  d'échec, la fenêtre s'ouvre quand même, avec les contrôles Windows standard. Le journal et
  l'Autotest le signalent.
- **Excel** : la page de garde et le tableau de bord suivent maintenant le modèle du PDF et de la
  fenêtre.
  - Page de garde : marque, titre, sous-titre, image, cartes d'identité et encadré TTC.
  - Tableau de bord : 7 tuiles, puis lots et voyants côte à côte, puis Pareto et quantités clés
    côte à côte, et les prix à saisir. Les barres des lots sont des formules qui suivent la part
    de chaque lot.
  - Les feuilles BOQ, récapitulatif, détail, audit et hypothèses restent sur fond blanc, pour
    l'impression et la signature (comme les pages BOQ du PDF).
- Tableau de bord de la fenêtre : un voyant sans détail ne laisse plus de ligne vide, et un
  Pareto vide explique qu'il faut saisir les prix.

## 4 quindecies. Aperçu avant export (0.13.0)

Le bouton **Aperçu…** de la fenêtre se trouve à côté de « Exporter Excel / PDF ». Il ouvre une
fenêtre d'aperçu et n'écrit **rien** sur le disque.
- **Onglet PDF** : chaque page en image, du même document que celui qui sera exporté (même
  composition QuestPDF, seule la sortie change : PNG au lieu de PDF). Un zoom va de 40 à 200 %.
- **Onglet Excel** : chaque feuille du classeur, construite en mémoire par le même code que
  l'export puis relue cellule par cellule, formules calculées. Les largeurs, hauteurs,
  fusions, couleurs et le gras sont respectés.
  - Les images (logo, rendu, page importée) ne sont pas dessinées dans l'aperçu ; elles sont
    signalées et bien présentes dans le fichier.
  - Les feuilles longues sont limitées à 300 lignes, et le nombre de lignes non affichées est
    indiqué.
- **Exporter Excel / PDF** depuis l'aperçu lance l'export habituel, qui produit des fichiers
  identiques à l'aperçu.
- Si les composants PDF manquent, l'onglet PDF le dit et l'aperçu Excel reste disponible.
- L'**Autotest** ouvre aussi l'aperçu, avec le devis d'essai, et en fait des captures.

## 4 sedecies. Couleurs pour la lecture (0.13.1)

Le thème futuriste n'est plus tout bleu. Le code couleur est le même dans la fenêtre, le PDF et
le classeur (`Core/Dashboard/Palette.cs`) :
- **une couleur par lot** : 00 violet, 03 cyan, 05 vert, 06 magenta, 07 vert anis, NC rouge,
  DEM corail… On la retrouve sur le liseré et le titre des groupes du BOQ et de la saisie des
  prix, sur les barres et les codes du tableau de bord, et dans les barres Excel ;
- **statuts** : mesuré en vert, estimé en ambre, forfait en violet, manquant en rouge ; gravité
  d'audit en rouge, ambre ou bleu clair ;
- **chiffres** : quantité en blanc, PU en ambre, montant en vert ; pied de devis HT en bleu,
  majorations en violet, TVA en ambre, TTC en vert ;
- **boutons par rôle** : Analyser en cyan plein, Aperçu en violet, Exporter en vert, Écrire
  dans le modèle en orange (la seule action qui modifie le modèle), Saisir les prix en ambre ;
- **onglets** : chacun est souligné de sa couleur (tableau de bord cyan, BOQ vert, audit
  ambre, forfaits violet, validation bleu, exclus magenta, paramètres orange) ;
- **tableau de bord** : un liseré de couleur propre à chaque tuile, et des titres de panneaux
  colorés (voyants ambre, Pareto orange, quantités vert, prix à saisir rouge).

Corrigé : la liste « Portée » affichait le nom technique `DanCI.BOQ.UI.ViewModels…` au lieu
de « Modèle entier ». Le modèle de liste déroulante du thème ignorait `DisplayMemberPath`.

## 4 septdecies. Graphiques et filtres dans le tableau de bord Excel (0.14.0)

La feuille « Tableau de bord » du classeur se termine maintenant par :
- **4 graphiques Excel natifs** (de vrais graphiques, modifiables dans Excel) :
  1. montant HT par lot, en colonnes de la couleur du lot ;
  2. répartition du montant par lot, en anneau avec les pourcentages ;
  3. montant HT par article, en barres triées, de la couleur de leur lot ;
  4. montant par statut de quantité (mesuré, estimé, forfait, manquant), en anneau.
- **3 tableaux Excel filtrables** (flèche ▼ dans les en-têtes) : `T_Lots`, `T_Articles` (tous
  les articles, triés par montant, avec lot, statut et prix renseigné ou manquant) et
  `T_Statuts`.
  - Les montants sont des **formules vers la feuille BOQ**.
  - Les graphiques lisent ces tableaux et **ne montrent que les lignes visibles** : filtrer
    `T_Articles` (un lot, le statut « Estimé », les prix « Manquant »…) met le graphique des
    articles à jour.

ClosedXML ne sait pas créer de graphiques. Ils sont ajoutés après l'écriture du classeur avec
le SDK Open XML, déjà présent avec ClosedXML (`Excel/ExcelCharts.cs`). Le classeur reste un
.xlsx standard : un test le valide avec `OpenXmlValidator` et le relit avec ClosedXML.
L'aperçu avant export signale les graphiques sans les dessiner.

## 4 octodecies. Fiches Client et Entreprise, avec logos (0.15.0)

Deux nouveaux volets dans **Paramètres du projet** :
- **Client** : fiche propre au projet, enregistrée dans `.danciboq.json`. Elle contient le sigle,
  la raison sociale, l'activité, l'adresse, la ville, le pays, la personne à contacter et sa
  fonction, le téléphone, l'e-mail, le site web, le RCCM, l'Id. Nat., le NIF et le **logo**.
  - Le nom du client remplace le champ « Maître d'ouvrage ».
  - Le logo et les coordonnées apparaissent dans un encadré « CLIENT » de la page de garde
    (PDF et Excel).
- **Notre entreprise** : fiche **commune à tous les projets**, dans
  `%AppData%\DanCI\BOQ\entreprise.json`. Les mises à jour de l'extension n'y touchent pas. Elle
  contient les mêmes champs, plus la **banque / RIB**.
  - Le sigle et la raison sociale remplacent « DanCI » écrit en dur. Le logo est prioritaire
    sur `config\logo.png`, sauf logo propre au projet.
  - Les coordonnées et les **mentions légales** (RCCM, Id. Nat., NIF) figurent en pied de page
    de garde, et la banque dans les conditions de l'offre.
  - Le responsable sert de « Établi par » par défaut, et l'entreprise de « Bureau d'études ».
  - Fiche absente : valeurs DanCI par défaut.

Chaque logo a un aperçu dans la fenêtre. Un logo introuvable est signalé, et le document est
produit sans lui.

## 4 novodecies. Sept améliorations demandées par DanCI (0.16.0)

1. **Montant en toutes lettres** (`Core/Pricing/AmountInWords`) : « Arrêté le présent devis à
   la somme de : … dollars américains et … cents toutes taxes comprises ». En francs congolais :
   « … francs congolais et … centimes ».
   - Règles : soixante-dix, quatre-vingt(s), cent(s), mille invariable, million(s), et
     « un million de … ».
   - Affiché dans le PDF, le classeur (feuille BOQ) et le pied de la fenêtre.
2. **Historique des révisions** (`Core/Revisions`) : **chaque export fige une révision**, un
   instantané JSON des lignes, prix, totaux et de l'empreinte. Il est rangé dans
   `<projet>_DanCI_revisions`, à côté du projet. Le bouton « Enregistrer une révision
   maintenant » fait de même sans exporter.
   - Onglet Validation > 3 : comparer la dernière analyse à une révision.
   - Le classeur produit contient une synthèse (A, B, écart, %), les écarts par lot (avec un
     graphique) et les écarts par article (ajouté, supprimé, modifié ; cause : quantité ou
     prix ; filtrable).
4. **Liste des matériaux à approvisionner** (`Core/Materials`, `config/materiaux.json`) :
   - béton par classe : ciment, sable, gravier, eau ;
   - acier par diamètre : kg et nombre de barres de 12 m ;
   - blocs et mortier de pose, mortier d'enduit, panneaux de coffrage (avec réemplois),
     peinture ;
   - totaux, dont le ciment en sacs de 50 kg.
   Le calcul part des quantités **brutes** (pertes comprises) et chaque ligne montre son
   calcul. Les dosages sont des **hypothèses à valider**, signalées en tête de liste. Résultat :
   une feuille « Matériaux » et une page PDF.
5. **Vue 3D colorée dans Revit** (`Core/Dashboard/ModelColoring`, `Addin/Analysis/ModelColorizer`) :
   boutons Par lot, Par statut et Par prix. Ils créent ou réutilisent la vue
   « DanCI — BOQ par … ».
   - Chaque élément métré prend la couleur de son lot, de son statut ou de son prix ; le reste
     du modèle est estompé.
   - Le modèle est **modifié** (une vue et ses remplacements graphiques) : confirmation
     demandée, une transaction, annulable par Ctrl+Z. Les éléments eux-mêmes ne sont pas
     touchés.
   - Les éléments des liens ne sont pas colorés, et leur nombre est indiqué.
6. **Échéancier financier** (`Core/Planning`) : saisie dans Paramètres > Échéancier (durée,
   démarrage, période de chaque lot, avec une proposition d'enchaînement de départ).
   - Le HT de chaque lot est réparti uniformément sur sa période, et chaque mois porte sa
     part des majorations et de la TVA.
   - La somme des mois égale le TTC au centime : l'écart d'arrondi va sur le dernier mois qui
     a des travaux.
   - Résultat : une page PDF (planning des lots aux couleurs des lots, besoin TTC mois par
     mois) et une feuille Excel en formules, avec un graphique.
7. **Code QR de vérification** (QRCoder 1.6.0, licence MIT) : en bas de la page de garde et à
   côté des signatures.
   - Il contient l'émetteur, le projet, la référence, l'indice, la date, le TTC et
     l'**empreinte complète** du métré.
   - Aucun serveur : une nouvelle analyse du même modèle doit redonner la même empreinte.
8. **Modèles de devis** (`Core/Model/QuoteTemplate`, `%AppData%\DanCI\BOQ\modeles`) : enregistrer
   la saisie en cours (TVA et majorations, prix, page de garde, conditions, client — au choix)
   et l'appliquer à un autre projet en un clic.
   - Appliquer un modèle remplit la saisie, sans rien enregistrer.
   - Un prix dont l'unité ne correspond pas à l'article est ignoré et signalé.

## 4 vicies. Planche « tableau de bord » dessinée sur mesure (0.17.0)

Le tableau de bord n'est plus une grille de tuiles génériques. C'est une **planche dessinée**
(`Export/Pdf/DashboardBoard.cs`, SVG 1600 × 1000) inspirée des modèles fournis par DanCI. Le même
rendu sert dans la fenêtre (onglet Tableau de bord), dans le PDF (page 2, thème futuriste) et dans
le classeur (feuille « Synthèse visuelle »). Chaque graphique lit une donnée réelle du devis :
- **Dynamique du devis** : quatre mini-histogrammes avec leur tendance ▲▼ : TTC (historique des
  révisions), prix renseignés (par lot), part mesurée (par nature), lignes sans prix (par lot).
  Les tendances sont calculées **par rapport à la révision précédente**.
- **Voyants** : l'« empreinte » du BOQ (une barre par ligne, couleur de son statut) et les points
  à traiter.
- **Répartition par lot** : histogramme multicolore (codes de lot, parts) et tableau des lots,
  avec le plus gros lot en surbrillance.
- **Statut du montant** : anneau segmenté en relief (mesuré, estimé, forfait, sans prix), avec
  HT, TVA, majorations et coût au m².
- **Santé du devis** : jauge radiale de la note sur 100.
- **Quantités clés** : curseurs de la part mesurée et ratio acier / béton.
- **Pareto** : arcs concentriques des cinq articles les plus lourds (part en haut, cumul en
  bas), avec le pourcentage cumulé.
- **Profil d'audit** : radar des constats par rubrique (erreurs, avertissements, éléments).

La planche est rendue par le moteur PDF. Si ses composants manquent, la fenêtre revient au
tableau de bord simple et le classeur l'indique.

## 4 unvicies. Compléments du gros œuvre (0.18.0)

- **Bords de dalle** (catégorie Revit « Bords de dalle » : rives, acrotères, bandeaux) : volume de
  béton par matériau (article 03.09 en C25/30 ; 03.06 en C20/25) et coffrage, comme les autres
  éléments en béton.
- **Poteaux architecturaux** (catégorie « Poteaux ») : comptés **s'ils sont en béton** (même
  reconnaissance du béton que le reste), dans l'article des poteaux 03.01, avec leur coffrage.
- **Pieux** (fondations dont la famille ou le type correspond à `pieux.motifs`, sauf
  `pieux.exclure` : semelles sur pieux « pile cap », chevêtres, massifs…) :
  - béton (02.06) ;
  - **longueur de forage** (02.07, hauteur de l'enveloppe du pieu) ;
  - **nombre** pour le recépage des têtes (02.08) ;
  - **coffrage retiré**, car un pieu foré est coulé contre le terrain.
- **Dallages sur terre-plein** (types `coffrage.typesSurTerrePlein`, et en option les sols du
  niveau le plus bas) : **film polyane** en m² (02.10, mesuré) et **hérissonnage** en m³
  (02.09, surface × épaisseur, **ESTIMÉ** : épaisseur à valider).
- **Assemblages de charpente modélisés** (connexions structurelles Revit) : masse mesurée
  (volume des platines et boulons × 7 850 kg/m³), article 04.02. Quand ils existent,
  l'estimation en % de la masse des profilés est retirée pour ce modèle (pas de double compte).

## 4 duovicies. Second œuvre (0.19.0)

`RevitAdapter/Extraction/SecondWorkExtractor.cs`, réglages `extraction.json > secondOeuvre`.

| Ouvrage Revit | Mesure | Article |
|---|---|---|
| Garde-corps (catégorie Garde-corps) | ml, longueur du tracé (somme des segments) | 07.03, une ligne par type |
| Murs-rideaux | m², aire du mur (portes et fenêtres intégrées comprises) | 07.04, par type |
| Gouttières | ml, paramètre Longueur | 06.03, par type |
| Bordures de toit (bandeaux, rives) | ml, paramètre Longueur | 06.04, par type |
| Plafonds (faux-plafonds) | m², aire du plafond | 08.05, par type |
| Pièces : plinthes | ml = périmètre − largeur des portes qui s'ouvrent dans la pièce | 08.04 |

- Les pièces sans plinthes sont exclues par leur nom (`plinthesExclurePieces` : gaines, vides,
  terrasses, balcons…).
- Une longueur illisible est signalée, jamais inventée.
- **Non traités** :
  - les descentes d'eaux pluviales, rarement modélisées de façon homogène (tuyaux ou familles) :
    à passer en forfait ;
  - la quincaillerie et le vitrage, compris dans le prix des menuiseries.

## 4 tervicies. Terrassements généraux, raccords et CVC (0.20.0)

**Terrassements généraux** (`GeneralEarthworkExtractor`, `Core/Extraction/EarthworkBalance`,
réglages `extraction.json > terrassementsGeneraux`), quantités rattachées à leur élément :
- **Décapage de terre végétale** (01.02, m², ESTIMÉ) : sols du niveau le plus bas, élargis du
  débord (1 m par défaut).
- **Fouille en pleine masse** (01.03, m³, ESTIMÉ) : sols situés sous le terrain naturel
  (sous-sols), du terrain naturel au-dessous du sol. Il faut renseigner
  `terrassement.niveauTerrainNaturel`. Si des fouilles de fondations existent aussi, un
  avertissement signale le risque de double compte.
- **Remblai compacté** (01.04, m³), par fondation : fouille − béton de la fondation − béton de
  propreté, jamais négatif.
- **Évacuation des terres** (01.05, m³ foisonné, ESTIMÉ) : (déblai − remblai) × foisonnement
  (1,25), plus la pleine masse et, en option, la terre végétale.
- **Pieux** : ni fouille en puits ni béton de propreté, puisque le forage est l'article 02.07.

**Raccords, accessoires et CVC** (`NetworkExtractor`), comptés par type, avec la taille
calculée par Revit :

| Ouvrage | Article |
|---|---|
| Raccords de tuyauterie | 09.03 |
| Accessoires de tuyauterie (vannes, robinets) | 09.04 |
| Sprinklers | 09.05 |
| Raccords de conduits et de chemins de câbles | 10.03 |
| Appareillage de commande, courants faibles, sécurité incendie (autrefois non classés) | 10.04 |
| **Nouveau lot 12 CVC** : gaines (ml par section) | 12.01 |
| Raccords de gaines | 12.02 |
| Accessoires de gaines | 12.03 |
| Bouches et diffuseurs | 12.04 |
| Équipements CVC (autrefois non classés) | 12.05 |

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

## 7. Dépendances : compatibilité .NET 8 → .NET 10

Ajoutées à l'étape 6, versions figées dans `DanCI.BOQ.Export.csproj`.

| Paquet | Version (figée) | Frameworks publiés | .NET 10 |
|---|---|---|---|
| ClosedXML | 0.105.1 | netstandard2.0, netstandard2.1 | ✅ testé (export complet sous net10.0) |
| DocumentFormat.OpenXml (dép. de ClosedXML) | 3.x | net8.0, **net10.0**, netstandard2.0 | ✅ |
| QuestPDF | 2026.9.2 | net6.0, net8.0, **net10.0**, netstandard2.0 + natifs Windows x64 | ✅ testé (PDF généré sous net10.0) ; licence : voir §4 quinquies |
| System.Text.Json | inclus dans le runtime | — | ✅ (fourni par .NET 8 / .NET 10 ; pas de version NuGet embarquée) |

Point d'attention pour ClosedXML : il est publié pour netstandard uniquement. Il fonctionne
sous .NET 10, mais ses dépendances transitives (polices, `SixLabors.Fonts`…) sont à vérifier
dans Revit même, et c'est là que l'isolation du §5 compte.
