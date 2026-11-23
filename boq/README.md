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

## 4 quatervicies. Réglages du métré dans la fenêtre (0.20.1)

Paramètres du projet > **Réglages du métré**, enregistrés dans `.danciboq.json` (clé `metre`) :
- niveau du terrain naturel (liste des niveaux du modèle) et son décalage ;
- coefficient de foisonnement ; décapage et son épaisseur ; pleine masse ;
- épaisseur du hérisson et option « sols du niveau le plus bas » ;
- ouvrages de second œuvre à métrer ou non.

Ces réglages remplacent ceux de `extraction.json` **pour ce projet seulement**. Ils agissent à
l'extraction : après l'enregistrement, la barre d'état rappelle de relancer l'analyse.

## 4 quinvicies. Revêtement mural et lot VRD (0.21.0)

- **Correction** : une couche de **carrelage ou de faïence sur un mur** allait dans l'article
  08.03 « Revêtement de sol ». Elle va maintenant dans **08.06 Revêtement mural**. Les sols
  gardent 08.03.
- **Lot 11 VRD**, jusqu'ici vide (`extraction.json > vrd`, `Core/Extraction/VrdRules`) :
  - **sols extérieurs** reconnus par leur type (voirie, chaussée, enrobé, pavé, trottoir,
    parking, allée…) : surface (11.01, par type), béton (11.02), coffrage (11.03), armatures
    (11.04). Leurs couches de finition, leur polyane et leur hérisson ne sont **pas** comptés en
    plus ;
  - **clôtures** : garde-corps dont le type est une clôture (grillage, portail…), 11.05 en ml ;
    les autres garde-corps restent en 07.03 ;
  - **places de stationnement** (11.06), **plantations** (11.07) et **équipements de site**
    (regards, caniveaux, mobilier, 11.08), comptés par type.

## 4 sexvicies. Tableau de bord interactif rétabli (0.22.0)

- L'onglet **Tableau de bord** affiche de nouveau, par défaut, le **tableau de bord interactif**
  (thème nuit, voyant « EN DIRECT ») ; il se met à jour à chaque analyse et à chaque
  enregistrement des prix. La **planche dessinée** du PDF / Excel reste disponible par le bouton
  « ▣ Planche (aperçu du PDF) » en haut à droite.
- Tout est **cliquable** :
  - une **tuile** ouvre l'endroit concerné (montants → BOQ, prix → prix manquants, fiabilité et
    audit → onglet Audit, coût au m² → identité du projet) ;
  - un **lot** ouvre le BOQ **filtré sur ce lot** (bandeau « Filtre : lot … » et bouton
    « Afficher tous les lots ») ;
  - un **voyant** ouvre l'endroit où corriger (prix unitaires sans prix, Audit, lot NC, TVA et
    devise, identité du projet) ;
  - un **article du Pareto** ouvre le BOQ sur cet article, avec son détail du métré.
- Le bouton rond **⊙** (lots, Pareto, quantités clés) **sélectionne et zoome les éléments dans
  Revit** (lecture seule, aucune modification du modèle).

## 4 septvicies. Tableau de bord : anneau, origine des montants, prix à saisir (0.23.0)

Nouvelle rangée de trois panneaux, tous cliquables :
- **Anneau des lots** : un secteur par lot, à la couleur du lot. Au survol, le montant et la
  part s'affichent ; un clic ouvre le BOQ filtré sur ce lot.
- **Origine des montants** : barre empilée Mesuré (vert) / Estimé (ambre) / Manuel (violet).
  Un clic sur Mesuré ou Estimé ouvre l'onglet Audit, un clic sur Manuel ouvre les Forfaits.
- **Prix à saisir** : les 8 premiers articles sans prix, avec leur quantité. Un clic ouvre
  Paramètres > Prix unitaires, déjà filtré sur cet article.

Les secteurs de l'anneau sont calculés dans Core (`DonutGeometry`) et testés.

## 4 duodetricies. Diagramme du métré dans Revit, à côté de la vue 3D (0.24.0)

Nouveau bouton **Diagramme 3D** (onglet DanCI), aussi accessible depuis la fenêtre
(« ▮▮ Diagramme dans Revit »). Il ouvre un **panneau ancrable** dans l'interface de Revit, à
droite par défaut (déplaçable, détachable), qui reste ouvert pendant que vous naviguez dans la vue 3D.

- **Par lot / par statut / par prix** : une barre par groupe, aux mêmes couleurs que la vue 3D
  colorée, le BOQ, le PDF et Excel. Chaque barre indique le montant, sa part, le nombre
  d'éléments et de lignes, et les quantités cumulées par unité (m³, m², kg…).
- **Hauteur : Montant ou Éléments**. Sans prix, le nombre d'éléments est choisi automatiquement.
- **Clic sur une barre** : ses éléments sont **isolés temporairement** dans la vue active
  (comme les lunettes de Revit). **Afficher tout** annule l'isolement. **⊙** sélectionne et zoome
  les éléments, liens compris.
- **Barres 3D dans la vue (temporaire)** : le diagramme est dessiné **dans la vue 3D elle-même**,
  devant la façade sud du modèle (Y min), à la largeur du modèle, numérotées de gauche à droite
  comme dans le panneau. C'est un affichage seulement (DirectContext3D) : rien n'est écrit dans
  le modèle ni enregistré avec le projet, et les barres ne sont ni sélectionnables, ni imprimées,
  ni exportées. Les barres se mettent à jour quand vous changez de mode ou de grandeur, et après
  chaque analyse.
- **Colorer la vue 3D…** crée la vue « DanCI — BOQ par … » aux couleurs du panneau. Cette action
  modifie le modèle (une vue) : une confirmation est demandée.

Limites : un élément d'un lien RVT n'est pas isolable un par un (il reste sélectionnable par ⊙).
Les barres 3D ne portent pas de texte (DirectContext3D ne dessine pas de texte) : les numéros et
les valeurs sont dans le panneau. L'Autotest contrôle le panneau (6 captures) et son
enregistrement dans Revit.

## 4 undetricies. Couleur de fond du logiciel (0.25.0)

Nouvel onglet **Paramètres du projet > Interface** :
- **8 teintes proposées** : Nuit (par défaut), Bleu marine, Ardoise, Anthracite, Noir, Violet
  nuit, Vert forêt, Bordeaux. Un clic suffit pour essayer une teinte.
- **Couleur personnalisée** au format #RRGGBB, avec un échantillon et les boutons **Essayer**,
  **Enregistrer** et **Rétablir la nuit (défaut)**.
- Le changement s'applique **tout de suite** à la fenêtre Métré & BOQ, au panneau Diagramme, à
  l'aperçu avant export et à la barre de progression. Les panneaux, les champs, les filets et le
  dégradé du tableau de bord sont nuancés automatiquement à partir du fond choisi.
- C'est une **préférence de votre poste**, valable pour tous les projets :
  `%AppData%\DanCI\BOQ\interface.json`. Elle ne change ni le BOQ, ni le PDF, ni Excel.
- Le texte de l'interface est clair : une teinte trop claire (luminance supérieure à 6 %) est
  **refusée** avec un message, pour que le texte reste lisible. Un fichier modifié à la main avec
  une teinte claire est ignoré : le fond par défaut est alors utilisé.

## 4 duodetricies bis. Style classique (clair, comme avant) ou futuriste (0.26.0)

**Paramètres du projet > Interface > Style de l'interface** propose deux cartes, et le choix
s'applique tout de suite :
- **Futuriste** : le style sombre actuel, avec la couleur de fond au choix (0.25.0).
- **Classique** : fond clair et contrôles Windows standard, comme les premières versions. Les
  textes, montants, prix, statuts et en-têtes de lots passent en couleurs foncées pour rester
  lisibles sur fond blanc (contraste vérifié par les tests).

Dans les deux styles, le **tableau de bord** et le **panneau Diagramme** restent sombres : ce sont
des « cockpits ». Les couleurs des statuts et des lots déjà affichées s'ajustent à la prochaine
ouverture de la fenêtre. Le choix est enregistré dans `%AppData%\DanCI\BOQ\interface.json`
(clé `style`). L'Autotest capture aussi chaque onglet en style classique.

## 4 tricies. Matériaux par couche : murs, sols, toitures, plafonds composites (0.27.0)

Chaque élément composite est décomposé **couche par couche**. Pour chaque matériau de la
structure du type (enduit, bloc, isolant, plaque de plâtre, béton, chape, membrane…) :
- **surface en m²** (Element.GetMaterialArea) et **volume en m³** (Element.GetMaterialVolume),
  calculés par Revit sur la géométrie réelle de l'élément : ouvertures percées et jointures
  appliquées, épaisseur variable comprise ;
- **épaisseur**, **fonction** (Structure, Substrat, Isolation, Membrane, Lame d'air,
  Finition 1/2), numéro de la couche, niveau, type et lien.

Cas particuliers, tous signalés comme **estimés** avec leur hypothèse :
- un matériau présent dans plusieurs couches : volume réparti au prorata des épaisseurs,
  surface à parts égales ;
- une couche « par catégorie » (sans matériau) : surface de l'élément × épaisseur.

Si Revit donne une surface nulle, elle est déduite du volume ÷ épaisseur. Un élément découpé
en pièces (Parts) est signalé dans l'Audit.

Où voir les résultats :
- **Onglet « Couches »** : un récapitulatif par matériau et par fonction (surface, volume,
  épaisseurs, ouvrages, nombre d'éléments). Sélectionnez un matériau pour voir le détail de
  chaque couche de chaque élément. Un double-clic sélectionne et zoome dans Revit.
- **Excel, feuille « Matériaux par couche »** : le récapitulatif avec ses totaux, puis le détail
  filtrable ligne par ligne, la méthode étant en commentaire.

Rapport au BOQ, sans double comptage :
- la décomposition est **informative** : le béton (03.x), la maçonnerie (05.x) et les finitions
  (08.x) restent métrés par leurs propres méthodes ;
- seules les **couches isolantes** sont ajoutées au devis, dans le nouvel article
  **08.07 Isolation thermique / acoustique**, ventilé par isolant et épaisseur. Une couche est
  isolante si sa fonction est Isolation ou si le nom de son matériau correspond
  (laine, polystyrène, XPS, polyuréthane, liège…) ;
- un isolant déjà compté en finition (étanchéité, enduit) n'est pas repris.

Réglages : `config\extraction.json`, rubrique `couches` (`actif`, `isolationAuDevis`,
`motifsIsolation`).

## 4 untricies. BOQ niveau par niveau (0.28.0)

Pour chaque niveau Revit (Fondation, RDC, Étage 1, Toiture…), l'extension montre **ce qui est
modélisé à ce niveau**, classé **lot par lot** puis **article par article** :
- quantité nette mesurée, quantité du devis, PU, montant, nombre d'éléments et statut ;
- sous-total par lot et total du niveau ;
- les **matériaux des couches** modélisées à ce niveau (murs, sols, toits et plafonds
  composites : surface, volume, épaisseur).

Règle de répartition, sans création ni perte de quantité :
- chaque ligne du BOQ est répartie entre les niveaux de ses éléments **au prorata de leurs
  quantités nettes mesurées** ; la quantité du devis (coefficients compris) et le montant suivent
  la même part ;
- les arrondis au centime sont absorbés par la plus grosse part : la **somme des niveaux redonne
  exactement** chaque ligne et le total du BOQ (vérifié par les tests) ;
- les forfaits et les éléments sans niveau vont dans « (sans niveau / forfaits) ».

Les niveaux sont classés du bas vers le haut, dans l'ordre des altitudes Revit. Le niveau d'un
élément est son niveau Revit : niveau de référence, niveau de base pour un mur ou un poteau.

Où voir les résultats :
- **Onglet « Par niveau »** : la liste des niveaux avec leur montant et leurs lots. Pour le
  niveau choisi, ses articles groupés par lot et les matériaux de ses couches. Un double-clic sur
  un article sélectionne ses éléments dans Revit ; le bouton « ⊙ Voir ce niveau dans Revit »
  sélectionne tout le niveau.
- **Excel, feuille « BOQ par niveau »** : le récapitulatif **lots × niveaux** (montants HT, en
  formules), puis le détail de chaque niveau.

## 4 duotricies. Inventaire de tous les matériaux et éléments génériques (0.29.0)

**Inventaire complet.** Tous les éléments du modèle sont inventoriés, au-delà des lots : chaque
élément dont la catégorie porte des matériaux (murs, sols, toits, plafonds, poteaux, poutres,
fondations, escaliers, menuiseries, familles, mobilier, modèles génériques…). Les couches des
éléments multicouches sont comprises. Pour chaque matériau de chaque élément, Revit calcule la
surface (GetMaterialArea) et le volume (GetMaterialVolume). Cas particuliers :
- un élément sans aucun matériau est inscrit avec le volume de sa géométrie ;
- une couche « par catégorie » est estimée : surface × épaisseur.

L'inventaire est informatif : rien n'est ajouté au BOQ.

**Éléments génériques signalés, un par un :**
- tous les **modèles génériques** (catégorie entière) ;
- les **types ou familles** dont le nom contient Générique / Generic / Par défaut / Default ;
- les **matériaux** génériques (« Default Wall »…) ou « par catégorie » ;
- les éléments **sans aucun matériau**.

Où voir les résultats :
- **Onglet « Matériaux »**, avec trois sous-onglets :
  1. *Tous les matériaux modélisés* : récapitulatif par matériau (ouvrages, niveaux, surface,
     volume, éléments, état) puis ses éléments ;
  2. *Multicouches : par couche* : l'ancien onglet Couches ;
  3. *Éléments génériques* : la liste avec la raison de chaque signalement, et le bouton
     « Sélectionner tous dans Revit ».
- **Onglet Audit**, rubrique Matériaux : un constat par raison et par type, sans doublon avec
  les constats existants (sans matériau, matériau par défaut).
- **Onglet « Par niveau »** et feuille **« BOQ par niveau »** : tous les matériaux modélisés à
  chaque niveau.
- **Excel** : feuilles **« Inventaire matériaux »** (récapitulatif et détail filtrable) et
  **« Éléments génériques »**.

Réglages : `config\extraction.json`, rubrique `inventaire` (`actif`, `motifsGeneriques`,
`categoriesGeneriques`).

## 4 tertricies. Corrections : couleurs de l'interface, fond de « Par niveau » (0.29.1)

- **Paramètres > Interface** : un clic sur une teinte proposée ne faisait rien. Les boutons des
  teintes cherchaient leur commande sur l'onglet, alors que le contenu d'un onglet WPF n'est pas
  affiché dans l'onglet lui-même : la commande n'était jamais trouvée. Corrigé : un clic applique
  la teinte tout de suite. Les boutons « Essayer » et « Enregistrer » marchaient déjà.
- **Onglet « Par niveau »** : la liste des niveaux avait le fond blanc d'une liste Windows, avec
  un texte clair illisible. Chaque niveau est maintenant une carte au fond du panneau, entourée
  d'un liseré : cyan au survol, vert quand le niveau est choisi. Cela marche dans les deux styles
  et avec toutes les teintes. Les autres listes du thème futuriste ont aussi un fond sombre.

## 4 quatertricies. Matériaux par niveau en 3D et BOQ en nomenclature Revit (0.30.0)

**Panneau « Diagramme » : deux nouveaux modes.**
- **Par niveau** : une barre par niveau. La hauteur peut porter le montant, le nombre d'éléments,
  le volume ou la surface des matériaux. En 3D, chaque barre est posée **à l'altitude de son
  niveau**.
- **Matériaux** : tous les matériaux modélisés, couches comprises, pour tous les niveaux ou pour
  un niveau choisi dans la liste. La hauteur porte le volume, la surface ou le nombre d'éléments.
  En 3D :
  - pour **tous les niveaux**, une rangée de barres par niveau, posée à l'altitude du niveau
    devant la façade sud. Une même colonne et une même couleur désignent le même matériau à tous
    les étages, et toutes les rangées ont la même échelle ;
  - pour un seul niveau, une rangée à l'altitude de ce niveau.
  - La hauteur maximale tient dans le plus petit écart entre deux niveaux.
- Comme avant : un clic sur une barre isole ses éléments, ⊙ les sélectionne, et les barres 3D
  sont un simple affichage, rien n'est écrit dans le modèle.

**BOQ dans les nomenclatures du projet.** Le bouton **« ▤ Nomenclatures BOQ »** de la fenêtre
crée, ou met à jour, deux vues de nomenclature Revit : **« DanCI — BOQ »** et **« DanCI — BOQ
par niveau »**.
- Elles se trouvent dans le navigateur de projet, sous *Nomenclatures/Quantités*, et se posent
  sur une feuille comme toute nomenclature.
- Elles contiennent les lots, les articles (N°, désignation, unité, quantité, PU, montant,
  statut), les sous-totaux, les majorations, la TVA et les totaux HT et TTC, ou les mêmes
  informations niveau par niveau. Les en-têtes, les lots et les totaux sont mis en forme.
- Revit n'accepte dans le corps d'une nomenclature que des éléments du modèle. Le BOQ est donc
  écrit dans l'**en-tête** de la nomenclature ; le corps est une nomenclature de modèles
  génériques filtrée pour rester vide.
- Le tableau est un **instantané** : relancez la commande après une nouvelle analyse. Une
  nomenclature existante est mise à jour sur place et reste posée sur ses feuilles.
- Cette commande modifie le modèle (deux vues) : une confirmation est demandée, puis une seule
  transaction est faite, annulable par Ctrl+Z. En cas d'échec, rien n'est modifié et la raison
  est affichée.

## 4 quintricies. VRD : dalles d'étage exclues ; fouilles sous tous les murs (0.31.0)

**VRD : pourquoi des dalles entre niveaux y tombaient.** Un sol est classé VRD quand le nom de
son type correspond à un motif (voirie, pavé, parking, allée…). Le motif « *bitum* » attrapait
aussi des dalles d'étage et des toitures-terrasses dont le type mentionne une étanchéité au
bitume. Leur béton, leur coffrage et leurs armatures partaient alors en VRD (11.02 à 11.04), et
« voir dans Revit » sélectionnait ces dalles. Corrections :
- un sol posé **à plus de 1 m au-dessus du niveau bas** (le terrain naturel s'il est désigné,
  sinon le plus bas niveau portant des murs ou des sols) n'est **jamais** VRD, quel que soit son
  nom. Il reste en gros œuvre / finitions, et l'Audit le signale (VRD_SOL_EN_ETAGE), avec les
  éléments à sélectionner ;
- « *bitum* » est retiré des motifs VRD ;
- chaque quantité classée VRD dit pourquoi, dans le détail du métré : « Classé en VRD : le nom
  du type « … » correspond au motif « … » ».

**Fouilles en rigole sous les murs** (nouvel article **01.06**, toujours ESTIMÉ). Partout où
passent les murs du **niveau bas** (ceux qui partent du sol) :
- longueur = tracé du mur, moins une demi-largeur à chaque extrémité jointe à un autre mur
  (croisements non comptés deux fois) ;
- largeur = épaisseur du mur + 2 × 0,20 m, au minimum 0,40 m ;
- profondeur = 0,80 m sous le terrain naturel, ou sous la base du mur si le TN n'est pas
  désigné ;
- un mur posé sur une **semelle filante modélisée** n'est pas recreusé : sa fouille est déjà
  comptée avec la semelle (01.01) ;
- remblai et évacuation : on déduit le volume enterré du mur (épaisseur × profondeur), jamais le
  béton de tout le mur.

Réglages : `config\extraction.json`, rubrique `fouillesSousMurs` (`actif`, `profondeurM`,
`surlargeurParCoteM`, `largeurMinM`, `murPorteursSeulement`, `exclureSiSemelleFilante`).

## 4 sextricies. Fouilles dessinées en 3D ; VRD au niveau bas (0.32.0)

**Fouilles visibles en 3D.** Les fouilles sont calculées, pas modélisées. Elles sont maintenant
**dessinées dans les vues 3D**, à l'emplacement exact de leur tracé :
- fouilles en rigole sous murs (01.06) : un volume le long de chaque mur, de la largeur et de la
  profondeur calculées, coupé aux croisements, segment par segment pour les murs courbes ;
- fouilles sous fondations (01.01) : une fosse autour de chaque semelle, avec la surlargeur au
  fond et le talus en tête.

Couleur : terre semi-transparente, pour voir les murs et les semelles au travers.

Quand ces fouilles apparaissent :
- **BOQ filtré sur le lot 01**, par exemple par un clic sur le lot 01 du tableau de bord ;
- **une ligne de fouilles sélectionnée** dans le BOQ ;
- **clic sur la barre « Lot 01 »** du panneau Diagramme.

La case « Fouilles en 3D » du bandeau de filtre permet de les masquer. Le dessin est un simple
affichage (DirectContext3D) : rien n'est écrit dans le modèle, et il n'est ni imprimé ni exporté.
Le tracé est calculé à l'analyse et conservé avec chaque quantité (paramètre `Geo3D`, visible
dans le CSV brut). Les éléments des liens sont replacés dans les coordonnées du modèle hôte.

**VRD au niveau bas.** Dans tout le « par niveau » (onglet, feuille Excel, nomenclature « DanCI —
BOQ par niveau », diagramme 3D par niveau), le **lot 11 VRD** est rangé au **niveau bas** : le
niveau du terrain naturel s'il est désigné et présent, sinon le plus bas niveau du modèle. Les
autres lots restent au niveau Revit de leurs éléments.

## 4 septtricies. VRD : voir pourquoi, et décider (0.33.0)

- **Panneau Diagramme** : sous chaque lot, la liste des **types Revit** qu'il contient (les
  plus fréquents d'abord). Pour la VRD, chaque type est suivi du **mot-clé** qui l'a classé, par
  exemple « « Dalle parking 20 » (mot-clé *parking*) ». La liste complète apparaît au survol.
- **Paramètres du projet > Réglages du métré > VRD**, nouvelle rubrique :
  - la liste des types de sols et de garde-corps classés VRD à la dernière analyse, avec leur
    mot-clé et leur nombre d'éléments, et un bouton **« Exclure du VRD »** par type ;
  - le champ **« Types jamais VRD »** : noms exacts ou motifs avec *, séparés par « ; » ;
  - la case **« Classer en VRD d'après le nom du type »** : décochée, aucun sol ni garde-corps
    n'est classé VRD. Les stationnements, plantations et équipements de site restent en VRD par
    leur catégorie.
  - Ces réglages sont enregistrés dans le fichier du projet (`metre.vrdParNomDeType`,
    `metre.vrdTypesExclus`). Il faut enregistrer, puis relancer l'analyse.
- Rappel : un sol posé à plus de 1 m au-dessus du niveau bas n'est jamais VRD (0.31.0).

## 4 octotricies. Doublons : détection renforcée et garde-fou de l'extraction (0.34.0)

- **Correction** : les fouilles sous murs (0.31.0) pouvaient compter deux fois un membre de
  **mur empilé**, renvoyé à la fois par la collecte et par son mur parent. Il n'est plus compté
  qu'une fois.
- **Garde-fou de l'extraction** : une quantité relevée deux fois pour le même élément (même
  document, même instance de lien, même nature, unité, matériau, méthode, ouvrage, couche et
  valeur) ne peut venir que d'un élément parcouru deux fois. Le doublon est retiré du métré et
  signalé dans l'Audit, rubrique Doublons (QUANTITE_EN_DOUBLE). Deux mesures différentes d'un
  même élément sont conservées (béton et coffrage, par exemple).
- **Doublons probables dans le modèle** (nouveau constat DOUBLON_PROBABLE, rubrique Doublons) :
  deux éléments de la même catégorie dont les boîtes englobantes se recouvrent à au moins 90 %
  et ont une taille voisine (rapport des volumes ≥ 0,8). Cela détecte une dalle ou un mur
  copié-collé sur place avec un autre type. Une petite dalle dans l'emprise d'une grande, ou la
  même dalle à l'étage au-dessus, n'est pas signalée. Les doublons exacts (même type, même
  position) restent signalés en erreur (DOUBLON_POSITION), et ceux que Revit signale lui-même en
  DOUBLON_REVIT.
- **Tableau de bord** : un voyant « N doublon(s) d'éléments à vérifier » renvoie à l'onglet Audit.

## 4 novemtricies. Parcourir et sélectionner élément par élément, niveau par niveau (0.35.0)

L'onglet **« Par niveau »** gagne, sous la liste des articles, un sous-onglet **« Éléments »** :
- **tous les éléments du niveau, un par ligne** : n°, ElementId, lien, ouvrage, type, matériaux,
  articles du BOQ où il est compté, quantités nettes (par unité) et statut. Les éléments
  modélisés qui ne sont dans aucun article sont aussi listés (« hors BOQ »), grâce à
  l'inventaire ;
- **choisir un article** dans la liste du dessus limite la liste à ses éléments, et **« Tout le
  niveau »** retire ce filtre ;
- **un clic sur un élément le sélectionne et le zoome dans Revit** (case « Sélectionner dans
  Revit à chaque clic ») ;
- **◀ ▶** passent à l'élément précédent ou suivant, avec un compteur « 12 / 87 » ;
- **« Isoler l'élément »** : seul l'élément choisi reste visible dans la vue active. C'est un
  isolement temporaire, comme les lunettes de Revit ; décocher la case rend la vue normale ;
- **« ⊙ Voir »** resélectionne l'élément courant.

Les matériaux du niveau passent dans le sous-onglet voisin, « Matériaux du niveau ».

## 4 quadragies. Planification des travaux et diagramme de Gantt (0.36.0)

**Planning calculé à partir du BOQ, du niveau bas vers le niveau haut.**
- **Une tâche par lot et par niveau.** Sa durée est la somme, pour ses articles, de la quantité
  du niveau ÷ le rendement d'une équipe par jour, divisée par le nombre d'équipes du lot et
  arrondie au jour supérieur. Les durées sont ESTIMÉES et le calcul est affiché pour chaque
  tâche.
- **Enchaînements réalistes :**
  - installation de chantier → démolitions → terrassements → fondations ;
  - puis, **niveau par niveau** : gros œuvre (le niveau suivant démarre après le délai de
    décoffrage, 7 jours) → maçonnerie (3 jours après le gros œuvre du niveau) → réseaux
    (plomberie, électricité, CVC) → menuiseries et finitions (après la maçonnerie et les
    réseaux du niveau) ;
  - toiture après le gros œuvre et la maçonnerie du dernier niveau ; VRD au niveau bas, après
    la maçonnerie.
- **Une équipe ne travaille que sur un niveau à la fois** : pour un même lot, le niveau n+1
  commence après le niveau n.
- **Calendrier** : jours ouvrés (6 j/semaine par défaut), dimanches et jours fériés chômés.
  Le démarrage est la date de l'Échéancier, sinon le lundi qui suit.
- **Chemin critique** : calcul au plus tôt et au plus tard, marge de chaque tâche ; les tâches à
  marge nulle sont cerclées de rouge. Le jalon « Réception des travaux » marque la fin.
- Les lignes non classées (NC) ne sont pas planifiées, et une note le rappelle.

**Où voir le planning :**
- **Onglet « Planning »** : le Gantt, avec les sections par niveau, les durées, les dates, les
  marges, les barres à la couleur des lots et l'échelle des mois et des semaines. Le calcul de
  chaque tâche apparaît au survol.
- **PDF** : une ou plusieurs pages « Planning des travaux — diagramme de Gantt » en A4
  paysage, avec les liens de précédence et la légende.
- **Excel**, feuille **« Planning »** : le tableau des tâches (dates, durées, marges, montant,
  prédécesseurs, calcul en commentaire) et le Gantt en cellules colorées, une colonne par
  semaine.

**Hypothèses à adapter** dans `config\planning.json` : rendements par article et par unité,
équipes par lot, jours ouvrés par semaine, jours fériés, délais entre niveaux.

## 4 unquadragies. Simulation 4D dans Revit et trésorerie des travaux (0.37.0)

**Simulation 4D** : panneau « Diagramme », nouveau mode **« Planning 4D »**.
- Un **curseur de date** va du démarrage à la réception, jour ouvré par jour ouvré. Les boutons
  **◀◀ semaine / semaine ▶▶** avancent ou reculent d'une semaine, et **▶ Lecture** avance
  automatiquement, une semaine toutes les 1,3 s.
- La **vue 3D montre le bâtiment qui monte niveau par niveau**. À la date choisie, seuls les
  éléments des tâches **terminées et en cours** restent visibles, par isolement temporaire comme
  les lunettes de Revit : rien n'est écrit dans le modèle. « Afficher tout » rend la vue
  normale.
- La liste montre chaque tâche avec son état (✓ terminée, ▶ en cours, · à venir), son
  avancement en % et ses dates. Un clic isole les éléments d'une tâche, ⊙ les sélectionne.
- En tête : la date, le nombre de tâches terminées et en cours, et l'avancement en % du
  montant.
- C'est le même planning que l'onglet Planning : même date de démarrage (Échéancier), mêmes
  rendements (planning.json).

**Trésorerie des travaux** : montant HT par mois.
- Le montant de chaque tâche est réparti à parts égales sur ses jours ouvrés, puis cumulé par
  mois calendaire. La somme des mois égale le total, au centime près (vérifié par les tests).
- **Onglet Planning** : des barres pour le montant du mois et une courbe orange pour le cumul
  (courbe en S), avec le montant et le % cumulé de chaque mois.
- **Excel**, feuille Planning : un tableau Mois / Montant / Cumul / Cumul %, en formules. C'est
  la base de l'échéancier des situations de travaux.

## 4 duoquadragies. Gantt interactif (0.38.0)

Le Gantt de l'onglet **Planning** n'est plus une image figée : on peut le modifier, et le
planning se recalcule aussitôt.
- **Durée modifiable** dans la colonne « Durée » : saisir une valeur puis Entrée ou Tab. Un
  champ vide rétablit la durée calculée. Une tâche à durée imposée est marquée ✎, et la durée
  calculée reste visible au survol.
- **Glisser une barre à la souris** décale le début de la tâche, en jours ouvrés, après ses
  prédécesseurs (jamais avant). Toutes les tâches qui en dépendent suivent. Le décalage est
  affiché (+N j).
- **Un simple clic sur une barre** sélectionne et zoome les éléments de la tâche dans Revit.
- **Équipes par lot** : une liste de choix par lot, de 1 à 10 équipes ; plus d'équipes donnent
  des tâches plus courtes.
- **Zoom** de l'échelle de temps.
- **Curseur de date** : une ligne verte sur le Gantt. Le bouton « ⊙ Voir dans Revit à cette
  date » isole ce qui est construit à cette date.
- **« ↺ Réinitialiser le planning »** revient aux durées calculées, sans décalage, avec les
  équipes de planning.json.
- Les durées et les équipes sont recalculées, ainsi que le chemin critique, les marges, les
  dates, la trésorerie et la simulation 4D du panneau Diagramme.

Les modifications sont **enregistrées dans le fichier du projet** (`planning` : `durees`,
`decalages`, `equipes`) et reprises dans le PDF (Gantt, avec ✎ et les décalages) et dans Excel
(feuille Planning).

## 4 terquadragies. Planning de chantier : 4D complète, Gantt qui défile, jalons, référence, avancement (0.39.0)

**Simulation 4D complète.** Avant, seuls les éléments comptés dans un article du BOQ se
construisaient pendant la simulation. Désormais, **chaque élément modélisé** qui n'est dans
aucune tâche est rattaché à une tâche selon son niveau et sa catégorie : lot NC, modèles
génériques, familles hors BOQ, mobilier, équipements… Exemples :
- modèle générique, poteau ou dalle → gros œuvre de son niveau ;
- mur → maçonnerie ;
- porte ou fenêtre → menuiseries ;
- sanitaire → plomberie ;
- luminaire → électricité ;
- mobilier et divers → finitions du niveau ;
- site → VRD ;
- sans niveau → rattaché quand même.

Le panneau indique « dont N hors BOQ » pour chaque tâche. À la fin de la simulation, tout est
construit (vérifié par les tests).

**Le Gantt défile pendant la simulation.**
- Dans le panneau 4D, chaque tâche a un **mini-Gantt** : sa barre sur toute la durée du
  chantier, la part réalisée qui se remplit, et la ligne de date qui avance. La liste
  **défile toute seule** vers la tâche en cours.
- Le **Gantt de la fenêtre suit la même date** : la ligne verte avance et l'échelle de temps
  défile. Le curseur de la fenêtre et celui du panneau sont synchronisés dans les deux sens.

**Un vrai planning de chantier :**
- **Jalons** : fin du gros œuvre de chaque niveau, **hors d'eau** (toiture terminée),
  **hors d'air** (menuiseries posées), **réception**. Ils sont affichés en losanges violets
  dans la fenêtre, le PDF et Excel.
- **Planning de référence** : le bouton « 📌 Figer la référence » enregistre le planning actuel.
  Chaque barre montre ensuite sa position de référence (trait gris) et son **glissement**
  (« +5 j / réf. »). Le PDF et Excel donnent la fin de référence et le glissement.
- **Avancement réel** : la colonne « Réel % » se saisit par tâche. La partie réalisée de la
  barre est assombrie. À la **date d'état** (le curseur), une tâche dont l'avancement réel est
  inférieur au prévu (tolérance de 5 points) est marquée **⚠ en retard**, avec un résumé des
  retards en haut du Gantt.
- Tout est enregistré dans le fichier du projet (`planning` : `avancement`, `reference`,
  `referenceLe`) et repris dans le PDF et Excel.

## 4 quaterquadragies. BOQ par niveau, coffrage limité à l'ossature, armatures, liens du Gantt (0.40.0)

**BOQ présenté par niveau (par défaut).** Les grands chapitres sont maintenant les **niveaux**,
du plus bas au plus haut. Chaque niveau contient ses **sous-lots** (terrassement, ossature
béton, maçonnerie, plomberie, électricité, finitions…), rangés comme Revit range ses familles
par catégorie. Les éléments **sans niveau** ont leur propre chapitre.
- Chaque niveau affiche sa **cote en mètres**, par exemple « Étage 1 (+3,20 m) » ou
  « Sous-sol (−3,00 m) ». Cela évite toute confusion avec les pieds internes de Revit.
- Un article partagé entre plusieurs niveaux est réparti au prorata de ses quantités nettes,
  avec la mention « [x % de l'article] ».
- La somme des niveaux est **exactement** égale au total de l'article : dans Excel, la plus
  grosse part prend le reste.
- Le BOQ se termine par un **récapitulatif par niveau**, puis un **récapitulatif par lot**.
- Ce mode est utilisé dans la fenêtre (onglet BOQ : « Par niveau » ou « Par lot »), dans Excel
  et dans le PDF. Il se règle dans *Devis → Présentation du BOQ* (`"presentation": "niveau"`
  ou `"lot"`).

**Coffrage seulement sur l'ossature.** Les surfaces de coffrage ne sont plus calculées sur
tout le béton. Elles concernent seulement :
- les **poteaux, poutres, dalles**, rives de dalle, fondations, escaliers et toitures béton
  (`categoriesOssature`) ;
- les éléments **déclarés structurels** dans Revit : mur porteur (« Structurel » coché), sol
  structurel, famille dont le type structurel est poteau ou poutre.

Le béton non structurel n'est pas coffré. Il est compté dans le rapport (« Coffrage : béton hors
ossature »). Ce comportement se désactive dans *Réglages du métré → Coffrage et armatures du
béton* (`"ossatureSeulement": false`).

**Armatures estimées depuis le béton.** Le poids d'acier est calculé ainsi : **volume de béton ×
ratio en kg/m³**, avec un ratio par type d'ouvrage. Exemples : poteaux 150, poutres 120,
dalles 90, voiles 70, fondations 60, escaliers 90, rives de dalle 100, toitures-terrasses 90. Ces ratios se
modifient dans les réglages du métré (de 0 à 400 kg/m³) et sont enregistrés dans le projet
(`ratiosArmatures`). L'article d'armatures est toujours signalé comme **estimation par ratio**,
et non comme un métré.

**Gantt : liens et filtres.**
- **Flèches de précédence** entre la fin d'une tâche et le début de la suivante, en **rouge**
  sur le chemin critique. La case « Afficher les liens » les masque.
- **Filtres** : par niveau, par lot et « chemin critique seulement ». Les liens ne relient que
  les tâches visibles.

## 4 quinquadragies. Finitions estimées comme un BOQ réel, gros œuvre / second œuvre par niveau (0.41.0)

**Les finitions sont estimées quand la maquette ne les modélise pas.** Le calcul suit la méthode
d'un BOQ de projet réel. Chaque quantité reste rattachée à **son élément Revit** (mur, poteau,
poutre ou pièce) et à **son niveau**. Un double-clic la sélectionne dans Revit. Elle est toujours
signalée **⚠️ estimée**, avec son hypothèse.

| Article | Ouvrage | Mesure |
|---|---|---|
| 08.14 | Carrelage de sol | Surface nette de chaque pièce Revit |
| 08.04 | Plinthes en carrelage h = 10 cm | Périmètre des pièces carrelées moins les portes (ml), et surface en m² (0,10 m²/ml) |
| 08.08 | Enduit intérieur au mortier de ciment | Les 2 faces des murs intérieurs, la face intérieure des façades, les faces apparentes des poteaux et poutres en béton |
| 08.09 | Crépissage et enduit extérieur | Face extérieure des murs de façade (fonction « Extérieur » du type de mur) |
| 08.10 / 08.13 | Enduit et peinture sous plafond | Surface des pièces |
| 08.11 / 08.12 | Peinture intérieure et peinture de façade | Mêmes surfaces que l'enduit |
| 08.15 | Faïence des pièces humides | (Périmètre − portes) × 2,10 m |

Comment les surfaces sont calculées :
- Les faces des murs sont calculées par Revit (`HostObjectUtils.GetSideFaces`). Les ouvertures
  sont donc déduites.
- Pour l'ossature, une face plaquée contre un mur, un sol ou un autre élément n'est pas comptée.
  C'est contrôlé au centre de chaque face.
- Ne sont pas enduits : les murs de fondation, la face côté terre d'un mur de soutènement, les
  murs-rideaux, les bardages, les cloisons en plaques de plâtre et les clôtures.
- Les niveaux dont le nom contient « fondation » ou « TN » n'ont pas de finitions.

**Pas de double compte.**
- Un élément qui porte déjà un enduit ou une peinture **modélisés** n'a pas d'estimation pour cette
  finition.
- Un niveau dont les sols portent un revêtement modélisé n'a pas de carrelage estimé par pièce.
- Le rapport indique ce qui a été écarté (`FINITION_ESTIMEE_NON_RETENUE`).

**Sans pièces Revit**, le carrelage, les plinthes, les plafonds et la faïence **ne sont pas
estimés**. Un avertissement `PIECES_ABSENTES` le signale : aucune quantité n'est inventée.

**Réglages.** Dans *Réglages du métré → Finitions non modélisées* : activation et hauteur de
faïence. Les motifs se règlent dans `extraction.json`, rubrique « finitionsEstimees » : pièces
humides, pièces sans carrelage, types de murs sans enduit, niveaux sans finitions. Les rendements
de ces articles ont été ajoutés dans `planning.json`.

**Gros œuvre / second œuvre dans chaque niveau.** Le BOQ par niveau range les lots ainsi :
- **GROS ŒUVRE** : installation, terrassements, fondations, béton armé, charpente, maçonnerie ;
- **SECOND ŒUVRE** : toiture, menuiseries, revêtements-enduits-peinture, plomberie, électricité,
  CVC ;
- **VRD**.

Chaque corps d'état a son sous-total, dans Excel (formules) comme dans le PDF. L'en-tête de
chaque niveau dans la fenêtre donne les montants par corps d'état. Le corps d'état d'un lot peut
être imposé par `"corpsEtat"` dans `regles_mapping.json`.

## 4 sexquadragies. Planning : dates de début et de fin imposées par l'utilisateur (0.42.0)

Les dates calculées par le planning sont une **proposition**, pas une obligation. Dans le Gantt de
la fenêtre (onglet Planning), les colonnes **Début** et **Fin** se modifient pour chaque tâche.

Saisir une date :
- tapez la date au format `jj/mm/aa` (par exemple `15/03/27`, ou `15/03/2027`) ;
- validez avec **Entrée** ou **Tab** ;
- une date imposée s'affiche **en gras**, et la tâche porte « ✎ ».

**Début imposé.** Il prime sur les enchaînements. Si cette date tombe avant la fin des tâches
précédentes, elle est **conservée** mais l'incohérence est **signalée** : « ⚠ » sur la tâche,
détail dans l'info-bulle et le résumé du planning.

**Fin imposée.** La durée en découle, en jours ouvrés, dimanches et jours fériés exclus. Une fin
placée avant le début est refusée et signalée.

**Effets sur le reste du planning.**
- Les **tâches suivantes se recalent** automatiquement.
- Le chemin critique, les jalons, la trésorerie, la simulation 4D, le PDF et Excel suivent.
- **Glisser la barre** d'une tâche à dates imposées déplace ces dates, en gardant la durée.

**Revenir au calcul.** Videz la case pour revenir à la date calculée. Le bouton « Réinitialiser le
planning » efface toutes les dates imposées.

Les dates imposées sont enregistrées dans le fichier du projet : rubrique `planning`, clés
`debuts` et `fins`.

## 4 septquadragies. Studio 4D : vue 3D dédiée, bâtiment qui tourne, Gantt et liens qui défilent (0.43.0)

Le Studio 4D s'ouvre avec le bouton **🎬 Studio 4D** de l'onglet Planning. Il permet de regarder
le chantier se construire.

**Dans Revit**, une vue 3D dédiée **« DanCI — Planning 4D »** est créée une seule fois, puis
activée. Elle est en ombré avec arêtes, au niveau de détail moyen.
- Les tâches **terminées** ont leur aspect normal.
- Les tâches **en cours** sont **colorées dans la couleur de leur lot**.
- Les tâches **à venir** sont masquées. On voit donc les éléments **s'élever** niveau après
  niveau.
- Pendant la lecture, **le bâtiment tourne** : la caméra fait le tour en orbite, 25° au-dessus
  de l'horizon, de 6° par pas.
- Le cadrage reste fixe, calé sur l'emprise de tout le bâtiment, pour que l'image ne saute pas.
- Les boutons ⟲ ⟳ tournent la vue de 30°.
- À l'arrêt, vous gardez votre propre point de vue.

**Dans la fenêtre du Studio**, placée en bas de l'écran sous la vue 3D :
- la date du jour simulé en grand, le jour sur le total, et la date de réception ;
- les tâches terminées, les tâches en cours, et l'avancement en montant ;
- la **lecture** (▶ / ⏸, pas d'un jour ou d'une semaine, vitesse lente, normale ou rapide), et un
  curseur de date que l'on peut faire glisser ;
- le **Gantt complet**, qui **défile tout seul** : horizontalement avec la date, verticalement
  vers les tâches en cours ;
- les tâches en cours sont surlignées et leur barre est pleine, les tâches terminées sont cochées ✓ ;
- les **liens** de précédence des enchaînements en cours sont **animés** : des pointillés défilent
  de la tâche précédente vers la suivante. Les liens rouges indiquent le chemin critique, et les
  liens des tâches terminées s'estompent ;
- la liste des tâches en cours, avec leur pourcentage.

**Ce que le Studio modifie dans le projet.** Il ne touche que la vue « DanCI — Planning 4D » :
isolement temporaire et couleurs de cette vue. Rien n'est écrit sur les éléments.
- À la fermeture du Studio, les couleurs et l'isolement sont retirés. La vue est conservée.
- Chaque image de la simulation est une petite transaction de vue. Elle apparaît donc dans la
  liste « Annuler » de Revit.
- Les éléments des **liens RVT** ne peuvent pas être isolés un par un : ils restent visibles.

Le panneau « Diagramme » et le Gantt de la fenêtre BOQ suivent la même date. Pendant que le Studio
est ouvert, c'est lui seul qui pilote la vue 3D.

## 4 octoquadragies. Bordereau des prix unitaires dynamique, prix selon les types et matériaux du modèle (0.44.0)

**Bordereau des Prix Unitaires (BPU).** Il est disponible dans un nouvel onglet de la fenêtre,
dans une feuille Excel « Bordereau des prix (BPU) » et dans une section du PDF. Il est
**reconstruit à chaque analyse** à partir de ce qui est réellement modélisé.
- Chaque **article présent** dans le modèle est listé avec son PU en chiffres, son **PU en toutes
  lettres** et l'origine du prix.
- Sous chaque article apparaissent ses **variantes du modèle** : famille, type et matériau Revit
  réellement présents, avec leur quantité, leur nombre d'éléments et le prix qui s'y applique.
- Un **nouveau type ou matériau** modélisé apparaît tout seul à l'analyse suivante. Il prend le
  prix général de l'article, ou il est marqué **« À CHIFFRER »** s'il n'en a pas.

**Prix par type, famille ou matériau.** La bibliothèque de prix accepte un nouveau critère
`"materiau"`, en plus de `"type"`, `"famille"` et `"valeur"`. Ces critères s'appliquent
maintenant à **tous** les articles, et plus seulement aux articles ventilés par type.

Exemple :

```json
{ "code": "03.01", "pu": 180, "devise": "USD" },
{ "code": "03.01", "pu": 205, "devise": "USD", "materiau": "*C30/37*" },
{ "code": "05.03", "pu": 24,  "devise": "USD", "materiau": "*brique*", "type": "*20*" }
```

Comment le prix est choisi :
- Chaque variante prend le prix **le plus précis** qui lui correspond, c'est-à-dire celui qui a
  le plus de critères.
- Sans prix précis, elle prend le **prix général** de l'article.
- Le **PU de la ligne** est la **moyenne pondérée par les quantités**. Le détail est donné dans
  l'origine du prix, par exemple « PU pondéré selon 2 variantes : … ».
- Une variante sans aucun prix laisse la ligne **sans montant**, avec un signalement. Rien n'est
  inventé.

**Saisie dans l'onglet BPU.**
- Tapez un PU sur une ligne d'article pour fixer le prix général.
- Tapez un PU sur une ligne « – » pour fixer le prix de **cette variante**. Les noms exacts de la
  famille, du type et du matériau du modèle sont alors repris.
- Cliquez sur **« Enregistrer les prix et recalculer »**. Les prix sont écrits dans la
  bibliothèque de prix du projet, puis le BOQ est recalculé.
- Une nouvelle saisie remplace l'ancienne. Un PU vidé sur une variante la renvoie au prix général.

## 4 octoquadragies bis. Correctif du Studio 4D : la lecture ne démarrait pas (0.44.1)

**Cause.** Depuis la 0.41, les tâches de finitions contiennent des **pièces Revit** : carrelage,
plafonds, faïence. Une pièce ne peut être ni masquée ni colorée dans une vue 3D. Revit refusait
donc **toute l'image** de la simulation. Au jour 0, il fallait masquer tout le bâtiment, et cela
échouait à chaque fois. L'erreur s'affichait dans la fenêtre BOQ, pas dans le Studio.

**Corrections.**
- Seuls les éléments qu'une vue 3D sait masquer et colorer sont envoyés. Les pièces, surfaces,
  espaces et annotations sont ignorés, et leur nombre est indiqué.
- Chaque étape d'une image est protégée séparément : affichage, couleurs, rotation. Un élément
  refusé n'arrête plus l'image.
- Le Studio affiche maintenant en bas **ce que Revit a fait de chaque image**, ou pourquoi il l'a
  refusée. Exemple : « vue 3D verrouillée : rotation impossible ».
- Si Revit refuse une demande, par exemple parce qu'une commande ou un dialogue est en cours,
  le Studio n'est plus bloqué : l'image suivante repart.
- À l'ouverture de la vue « DanCI — Planning 4D », une seconde image est envoyée automatiquement
  pour la cadrer.

## 4 octoquadragies ter. Studio 4D : zoom stable pendant la rotation (0.44.2)

**Avant.** Le zoom était recalculé à chaque image sur la boîte englobante du bâtiment. Vue de
biais, cette boîte change de taille à l'écran selon l'angle. Pendant la rotation, le zoom
sautait donc d'une image à l'autre.

**Maintenant, le cadrage est constant pendant toute la rotation.**
- Le cadre est un carré posé dans le plan de l'écran, centré sur le bâtiment.
- Son côté vaut le diamètre de la sphère qui englobe le bâtiment, plus 15 % de marge.
- Le bâtiment entier reste visible à tous les angles, toujours à la même taille.
- On voit donc bien le bâtiment tourner et s'élever.

Un test vérifie ce comportement sur un tour complet (`Sim4DCamera`).

**Éléments éloignés.** Quelques éléments très éloignés, comme la topographie, des éléments de
site ou un élément égaré, ne gonflent plus le cadrage. 2 % des extrêmes de chaque côté sont
écartés quand il y a au moins 50 éléments.

## 4 octoquadragies quater. Studio 4D : simulation plus rapide, cadence régulière (0.44.3)

**Vitesses.** Cinq vitesses sont proposées : 1, 2, **4 (par défaut)**, 6 images par seconde, et
« maximale », au rythme de Revit. Avant, le Studio allait de 1 image toutes les 2 secondes à
2 images par seconde.

**Pas d'avancement.** Chaque image fait avancer le chantier de 1, 2 (par défaut) ou 3 jours,
ou de 1 ou 2 semaines.
- Un petit pas donne une construction plus fluide.
- Un grand pas parcourt le chantier plus vite.

**Une cadence régulière.**
- Chaque image **attend que Revit ait dessiné la précédente**, avec au plus 3 secondes
  d'attente. Aucune image n'est sautée et tous les pas sont égaux, même sur un modèle lourd.
- Sur un modèle lourd, c'est donc Revit qui impose le rythme, au lieu d'avancer par à-coups.
- La **cadence réelle** s'affiche en bas du Studio, en images par seconde, avec le temps de
  dessin de Revit.

**Rotation régulière.** Le bâtiment tourne par petits angles : 2,5° à 6° par image selon la
vitesse. Plus la cadence est rapide, plus le mouvement est fin.

**Images plus légères pour Revit.** Le tri des éléments affichables et la recherche du motif
plein ne sont plus refaits à chaque image : ils sont calculés une fois par vue.

## 4 octoquadragies quinquies. Studio 4D : le bâtiment remplit l'écran (0.44.4)

**Cadrage serré, toujours fixe.** Le cadre suit maintenant la vraie silhouette du bâtiment, et
non plus la sphère qui l'entoure, qui était trop large.
- Largeur du cadre : la diagonale en plan, c'est-à-dire la plus grande largeur visible.
- Hauteur du cadre : la plus grande hauteur apparente sur un tour complet.
- Marge : 6 %.

Résultat : à l'angle le plus large, le bâtiment occupe presque tout l'écran. Il ne déborde à aucun
angle, et le zoom reste fixe pendant la rotation. Un test le vérifie sur un tour complet : les
8 coins restent dans le cadre et le remplissage dépasse 90 %.

**Fenêtre du Studio plus discrète.**
- Sa hauteur passe de 560 à 340 px.
- Le bouton « ▾ Masquer le Gantt » ne laisse qu'un bandeau compact, collé au bas de l'écran.
  La vue 3D de Revit occupe alors presque tout l'écran.
- « ▴ Afficher le Gantt » le fait revenir.

## 4 octoquadragies sexies. Studio 4D : correctifs de génération (0.44.5)

Depuis la 0.44.3, la simulation pouvait mal fonctionner. Quatre causes ont été corrigées.

1. **Éléments exclus à tort.** Le tri des éléments affichables était gardé en mémoire alors qu'il
   pouvait avoir été calculé sur une vue encore isolée par une lecture précédente. Des éléments
   masqués à ce moment-là restaient alors exclus, et le bâtiment ne se construisait plus.
   - Le tri est maintenant fait *après* la sortie du mode temporaire, sur la vue entière.
   - Il est recalculé dès que la liste des éléments du planning change.
2. **Saccades de 3 secondes.** Une demande refusée par Revit laissait le Studio attendre 3 s.
   - Le Studio est maintenant libéré immédiatement.
   - L'attente maximale passe à 1,5 s.
3. **Revit saturé.** Un petit temps libre est laissé à Revit entre deux images : au moins 40 ms,
   ou 35 % du temps de dessin. Les boutons, la souris et le curseur restent réactifs.
4. **Travail inutile à chaque image.** Pendant le Studio, le panneau « Diagramme » ne reconstruit
   plus sa liste de tâches à chaque image. Il se remet à jour à la fermeture du Studio.

## 4 noviesquadragies. Studio 4D : construction progressive, mur par mur et rang par rang (0.45.0)

**Avant**, dès qu'une tâche démarrait, tous ses éléments apparaissaient d'un coup. Sur un
bâtiment d'un seul niveau, la tâche « Maçonnerie RDC » faisait surgir tous les murs le premier
jour.

**Maintenant**, pour chaque tâche en cours :

1. **Les éléments suivent l'ordre du chantier.**
   - Ils sont groupés du bas vers le haut, par tranches de 1,50 m de cote.
   - Dans chaque tranche, l'ordre est le suivant : fondations, puis poteaux et murs, puis
     poutres, puis dalles et escaliers, puis toiture, puis le reste.
   - Chaque groupe reçoit une part de la durée de la tâche, proportionnelle à son nombre
     d'éléments.
   - Les éléments d'un même groupe montent ensemble, comme des maçons qui lèvent tous les murs
     d'un niveau en même temps.
2. **Chaque élément se construit petit à petit**, en dessin 3D dans la couleur de son lot.
   - **Mur** : il monte **assise par assise**, avec des rangs de 20 cm et des joints visibles.
     La dernière assise se pose **le long du mur, brique après brique**.
   - **Poteau** : il monte.
   - **Élément plat** (dalle, poutre basse, rive) : il se coule d'un bout à l'autre, dans sa plus
     grande longueur.
   - Quand l'élément est fini, le **vrai élément Revit** apparaît, avec ses ouvertures et ses
     couches.

**Ce que le Studio ne modifie pas.** Les formes en construction sont simplifiées : tracé du mur ×
épaisseur, ou boîte de l'élément. Elles sont dessinées sans rien écrire dans le modèle, et
retirées à la fermeture du Studio.

**Pas par défaut.** Il passe à **1 jour par image**, pour voir les murs monter rang par rang.

**Vérifications.** Trois tests couvrent ce comportement :
- assises complètes, puis assise partielle le long du mur ;
- dalle coulée en longueur et poteau qui monte ;
- ordre du chantier dans une tâche, et murs d'un même niveau qui montent ensemble.

## 4 noviesquadragies bis. Studio 4D : vitesse élevée sans aucun mur qui surgit (0.45.1)

Deux situations faisaient encore surgir des murs entiers d'un coup. Elles sont corrigées.

1. **Tâche plus courte que le pas de lecture.** Par exemple, une tâche de 2 jours lue avec un pas
   d'une semaine passait directement de « pas commencée » à « finie » entre deux images.
   - L'avancement **affiché** de chaque tâche suit maintenant l'avancement réel, mais il ne monte
     jamais de plus de **1/12 par image** pendant la lecture.
   - Une tâche met donc toujours au moins 12 images à se construire, quelle que soit la vitesse.
   - Si la date de lecture va plus vite, la 3D rattrape en douceur. À la fin du planning, la
     lecture continue jusqu'à ce que tout soit monté.
2. **Petits groupes dans une tâche**, par exemple 2 murs sur 40. Ils ne recevaient qu'une petite
   part de la durée de la tâche.
   - Chaque groupe (tranche de cote × catégorie) dure maintenant au moins **40 % de la tâche**.
   - Les groupes **se chevauchent**, toujours dans l'ordre du chantier.

**Ce qui est déjà construit n'est pas touché.** En recul, ou quand la date est déplacée à la
main, l'affichage se recale directement sur la date, sans animation.

**Vérifications.** Des tests couvrent ces deux comportements :
- chaque groupe dure au moins 40 % de la tâche ;
- une tâche finie d'un coup se construit quand même en 12 images ;
- l'affichage se recale directement en recul.

## 4 quinquagies. Assemblages de charpente métallique reconnus dans le modèle (0.46.0)

**Avant**, les assemblages n'étaient qu'**estimés**, à 10 % de la masse des profilés.
**Maintenant**, l'extraction **reconnaît les assemblages modélisés** dans la maquette.

### Ce qui est reconnu

1. **Assemblages acier Revit** : onglet Acier, catégorie « Assemblages structurels ».
   - Ils sont comptés **par type**, en unités.
   - Leur masse est mesurée sur leur géométrie : volume × 7 850 kg/m³.
   - Les profilés qu'ils relient sont mémorisés.
2. **Pièces d'assemblage** :
   - **pesées** : platines, profilés d'assemblage, autres pièces ;
   - **comptées par type** : boulons, tiges d'ancrage, goujons ;
   - **comptées** : soudures.
3. **Familles d'assemblage reconnues par leur nom**, de famille ou de type : platine, gousset,
   jarret, éclisse, raidisseur, pied de poteau, platine d'about, boulon, tige d'ancrage, et les
   équivalents anglais.
   - Elles sont cherchées dans les catégories Assemblages structurels, Modèles génériques,
     Ossature, Poteaux, Raidisseurs et Équipements spécialisés.
   - Un gousset modélisé comme une ossature acier n'est **plus compté une seconde fois** comme
     profilé.

### Nouveaux articles

| Article | Contenu | Unité |
|---|---|---|
| **04.03** | Assemblage de charpente modélisé, une ligne par type | U |
| **04.04** | Boulonnerie et ancrages modélisés, une ligne par type | U |
| **04.02** | Masse des assemblages : modélisés, ou estimés en % | kg |

### Pas de double compte

Quand des assemblages sont modélisés, l'estimation « % de la masse » est **retirée** :
- pour les profilés reliés à un assemblage acier Revit ;
- ou pour tout le document, si les assemblages modélisés n'indiquent pas leurs profilés, comme
  les familles et les platines.

Le rapport le signale avec le code `ASSEMBLAGES_MODELISES`. L'option
`garderEstimationSiModelises` garde l'estimation.

Un assemblage « générique » sans détail (simple symbole) est compté, mais sa masse est marquée
**manquante** : rien n'est inventé.

### Réglages

Dans `extraction.json`, rubrique `acier` :
- `assemblagesModelises` ;
- `motifsAssemblages` ;
- `categoriesAssemblages` ;
- `garderEstimationSiModelises`.

## 4 quinquagies bis. Relecture générale : deux correctifs (0.46.1)

**Contrôles passés.**
- Compilation sans avertissement pour Revit 2025, 2026 et 2027.
- 384 tests verts.
- Configuration cohérente : aucune règle ne pointe vers un article inconnu, aucun ordre n'est en
  double, aucun article n'est sans règle, et tous les fichiers JSON se lisent.

**Correctif 1 — Studio 4D : un élément déjà construit ne disparaît plus.**
- Le problème : depuis les finitions estimées (0.41), un mur appartient à plusieurs tâches, la
  maçonnerie puis l'enduit et la peinture. Quand la tâche d'enduit démarrait, le mur était masqué
  puis remontait assise par assise.
- La correction : un élément n'est **construit qu'une fois**, par la première tâche qui le
  contient. Les tâches suivantes ne font que le **colorer**, une fois qu'il est debout.
- Cette logique a été déplacée dans le Core (`Build4DState`) et un test la vérifie.

**Correctif 2 — Assemblages : pas de double compte d'un élément en béton.**
- Le problème : un élément déclaré en béton (ou en bois, en maçonnerie…) dont le nom contient
  « pied de poteau » ou « about » aurait été compté aussi comme assemblage acier.
- La correction : la reconnaissance par le nom ignore maintenant les éléments déclarés dans un
  autre matériau que l'acier.

## 4 unquinquagies. Touche graphique : PDF, Excel, Studio 4D (0.47.0)

**PDF du BOQ et du bordereau des prix.**
- **Bandeaux de niveau** : fond nuit avec un filet cyan à gauche, et la **cote du niveau dans une
  étiquette** (par exemple « −1,50 m »).
- **Bandeaux de corps d'état** (GROS ŒUVRE, SECOND ŒUVRE) : filet bleu à gauche et lettres
  espacées.
- **En-têtes de lot** : **pastille de la couleur du lot**, la même que dans la fenêtre, le Gantt
  et le Studio 4D, avec un filet de cette couleur sous l'en-tête.
- **Lignes alternées** (zébrage léger) : on suit une ligne d'un coup d'œil. Les codes sont en gris
  et les montants en demi-gras.
- **Sous-totaux** : montant **encadré**. Ceux de lot sont discrets, ceux de corps d'état sont
  marqués.
- **Total de niveau** : bandeau nuit, avec le montant en cyan.
- Bordereau des prix : même présentation, et « À CHIFFRER » en rouge.

**Excel.** La feuille BOQ par niveau reprend la même présentation : pastille de couleur du lot,
zébrage, total de niveau sur fond nuit.

**Correctif.** Le bandeau rouge affichait « 0 ligne(s) sans prix » alors qu'aucune ligne n'en
manquait. Il indique maintenant le vrai motif : le nombre de lignes sans prix s'il y en a,
sinon « devis incomplet, voir l'audit ».

**Studio 4D.**
- Les indicateurs (terminées, en cours, avancement) deviennent des **cartes**, avec un filet de
  couleur en haut.
- La barre d'avancement passe en **dégradé** du vert au cyan.
- La date est mise en valeur par un léger halo.

## 4 unquinquagies bis. Planche du tableau de bord : chevauchements corrigés, pleine page (0.47.1)

**Chevauchements corrigés.**
- **Bandeau « Dynamique du devis »** : les mini-graphiques débordaient sur le titre suivant. Par
  exemple, les barres jaunes de « Prix » recouvraient le mot « Mesuré ». Les barres sont
  maintenant plus fines et restent dans leur colonne.
- **Répartition par lot** : le message « aucun montant » passe sous les barres au lieu d'être
  écrit dessus.
- **Voyants** : le libellé est plus long (30 caractères), donc moins souvent coupé.

**PDF.** La planche reste **vectorielle**, nette à tout zoom. Le fond dégradé couvre maintenant
**toute la page A4 paysage** : plus de bande sombre en haut et en bas.

**Fenêtre.** La vue « Planche » est rendue 1,5 fois plus nette, en 2400 × 1500 pixels.

## 4 unquinquagies ter. Thème de la fenêtre : finition professionnelle (0.47.2)

Ces retouches concernent uniquement le style futuriste. Le style classique reste inchangé.

**Tableaux** : BOQ, prix, bordereau, audit, matériaux…
- Lignes horizontales seules, plus discrètes. Les lignes verticales sont retirées.
- Hauteur de ligne d'au moins 24 px, avec une marge intérieure dans les cellules (6 × 3 px).
- La ligne survolée est **mise en surbrillance**.
- En-têtes de colonnes plus discrets : texte atténué en 11 px, plus d'espace, filet cyan affiné.

**Onglets**
- Coins supérieurs arrondis et un peu plus d'espace.
- Au survol, léger fond et filet.

**Cadres de groupe** (réglages, paramètres)
- Filet cyan vertical devant le titre.
- Coins plus arrondis.

**Info-bulles** : plus de marge intérieure, et une largeur limitée pour qu'elles restent lisibles.

**Boutons** : coins un peu plus arrondis, hauteur légèrement augmentée.

## 4 unquinquagies quater. Studio 4D : plus aucun élément qui surgit (0.47.3)

**Le problème.** Sur un vrai projet, des murs et d'autres éléments surgissaient encore d'un coup.

**Les causes**, que les tests ne voyaient pas :
1. **Deux montées par pas.** Chaque pas de lecture produisait deux images : la date, puis la
   position du curseur. La limite de montée par image était donc appliquée deux fois.
2. **Images sautées par Revit.** Quand une image en remplaçait une autre encore en attente, le
   Studio était libéré tout de suite et envoyait la suivante. Revit ne dessinait alors qu'une
   image sur plusieurs, et un mur passait de rien à presque fini entre deux images affichées.
   Sur un modèle lourd, l'attente maximale de 1,5 s produisait le même effet.

**Les corrections :**
- **Une seule montée par pas.** Un événement en double pour la même date ne fait plus avancer la
  construction (modes Avance / Maintien / Recalage, testés).
- **Le Studio attend vraiment que Revit ait dessiné l'image**, jusqu'à 10 s sur un modèle lourd.
  Aucune image n'est sautée.
- **Montée plus fine.** Une tâche se construit en au moins **16 images** (au lieu de 12).
- **Élément sans forme connue** (pas de boîte englobante) : il apparaît **à la fin** de sa tâche,
  et non plus au début.

## 4 unquinquagies quinquies. Studio 4D : lecture rapide, construction graduelle (0.47.4)

**Objectif.** La lecture doit être **rapide**, et chaque élément doit monter **graduellement**.

**Ce qui ralentissait.** Chaque image refaisait toute la mise à jour de la vue Revit dans une
transaction : isolement de tous les éléments, couleurs et rotation. C'était le cas même quand
seule une assise de mur avait monté.

**Images légères et images complètes.**
- **Image légère**, la plupart du temps : seules les formes en construction (assises, coulage) sont
  redessinées, sans transaction. C'est très rapide.
- **Image complète**, seulement quand quelque chose change dans la vue Revit :
  - un élément vient d'être fini et le vrai élément apparaît ;
  - une couleur de tâche change ;
  - la vue tourne : seule l'orientation est alors modifiée ;
  - le planning change.
- **Cadrage** : il n'est refait qu'à la rotation, ou à l'ouverture de la vue.
- La ligne d'état indique « (image légère) » quand c'est le cas.

**Vitesse.** Nouvelle vitesse « Ultra rapide (8 images / s) ». La montée graduelle reste
garantie : au moins 16 images par tâche, et aucune image n'est sautée.

## 4 unquinquagies sexies. Studio 4D : fin de simulation sur la vue de face, en douceur (0.47.5)

**La simulation se termine toujours sur la vue de face.** Il s'agit de la face **« Avant » du
ViewCube** : la caméra est au sud, à l'horizontale, et regarde vers le nord.

**Le mouvement.** Quand tout est construit, la caméra se pose en **24 images** :
- elle finit sa rotation par le **chemin le plus court**, au plus un demi-tour ;
- elle redescend de 25° à 0° au-dessus de l'horizon ;
- elle suit une **courbe douce**, avec un départ et une arrivée ralentis ;
- le cadrage s'ajuste pour que la façade remplisse l'écran.

**Quand l'atterrissage se produit.**
- À la fin de la lecture, que la rotation soit activée ou non.
- Avec le bouton ⏭, qui fait le même atterrissage.
- Une nouvelle lecture repart en vue plongeante.

**Vérification.** Un test couvre plusieurs angles de départ : la caméra arrive toujours face
Avant et à l'horizontale, par le chemin le plus court, sans saut de plus de 12° par image.

## 4 duoquinquagies. Studio 4D façon SYNCHRO : contexte, couleurs d'état, courbe en S, vidéo (0.48.0)

Ces évolutions s'inspirent des vidéos 4D de type SYNCHRO.

**Le contexte reste visible.**
- Seuls les éléments du planning **pas encore construits** sont masqués.
- Le terrain, les bâtiments voisins, une grue modélisée et les éléments hors planning restent à
  l'écran, comme dans une vraie vidéo de chantier.
- Les liens RVT qui portent des éléments du planning restent masqués : leurs éléments ne peuvent
  pas être masqués un par un.

**Couleurs d'état (par défaut).**
- Ce qui se construit est en **jaune**.
- Ce qui est fini prend sa **couleur réelle**.
- La case « Couleurs d'état » décochée revient à la couleur du lot.

**Courbe en S et histogramme**, à droite du Gantt du Studio :
- **courbe en S** du montant cumulé prévu, avec la partie réalisée en cyan, le curseur de date et
  le cumul en montant et en % ;
- **montant par semaine** : semaines passées en cyan, semaine en cours en jaune.
- Ces données sont calculées par `WorkPlan.WeeklyAmounts`. Un test vérifie que la somme des
  semaines est égale au total et que le cumul est croissant jusqu'à 100 %.

**Enregistrer la vidéo** (bouton ⏺) :
- La simulation repart du début et chaque image de la vue 3D est capturée.
- La **date est incrustée en grand** en bas à gauche, avec le nom du projet et une barre
  d'avancement en haut.
- L'enregistrement s'arrête de lui-même à la fin, sur la vue de face.
- Le fichier est une **vidéo AVI (Motion JPEG)** sans dépendance externe, lisible par VLC, le
  Lecteur Windows Media et PowerPoint.
- L'écrivain AVI est vérifié par un test, et le fichier produit est validé par ffprobe et
  ffmpeg : codec mjpeg, cadence et nombre d'images exacts, décodage sans erreur.
- **Capture** : la fenêtre de la vue est copiée, même si une autre fenêtre la recouvre. Si
  Windows ne le permet pas, c'est l'écran qui est copié : gardez alors la vue 3D dégagée.

## 4 duoquinquagies bis. Studio 4D : croissance garantie, hauteur par hauteur (0.48.1)

**Défaut corrigé : dessin vide sur les gros modèles.** Le dessin 3D des éléments en construction
numérotait ses sommets sur 16 bits, soit 65 535 sommets au plus.
- Quand beaucoup de murs montent ensemble, par exemple 200 murs de 15 assises, la limite était
  dépassée et le dessin devenait faux ou vide.
- On ne voyait alors que le vrai mur, une fois terminé, et il surgissait d'un coup.
- Le dessin est maintenant découpé en tampons de 2 700 volumes au plus : il reste juste quelle
  que soit la taille du modèle.

**Profil « actif » à la façon de SYNCHRO.** Dès qu'un élément commence à se construire :
- son vrai volume apparaît en **fantôme**, très transparent (85 %) et en demi-teinte ;
- les assises opaques le remplissent de bas en haut ;
- une fois l'élément fini, le vrai élément s'affiche normalement.

Même si le dessin 3D échouait, plus rien ne surgit de nulle part : on voit le fantôme apparaître,
puis l'élément se remplir.

## 4 duoquinquagies ter. Studio 4D : le bon lot construit chaque élément, rangs de 20 cm, fouilles creusées (0.48.2)

**Le symptôme.** Pendant environ 40 jours, rien n'apparaissait. Puis un mur entier surgissait.

**La cause.** Un mur appartient à plusieurs tâches :
- les fouilles sous le mur (lot 01), qui sont mesurées *sur* le mur ;
- la maçonnerie (lot 05) ;
- l'enduit (lot 08).

La construction de l'élément était confiée à la *première* tâche, c'est-à-dire au terrassement.
Tous les murs « montaient » donc pendant le terrassement. Ce dessin échouait sur un gros modèle
(défaut corrigé en 0.48.1), et à la fin du terrassement les murs apparaissaient entiers.

**Corrections.**
- **Le bon lot construit l'élément** : c'est la première tâche d'un lot *constructeur*
  (fondations, béton armé, charpente, maçonnerie, toiture, menuiseries, réseaux). Le
  terrassement, les finitions et les démolitions ne construisent jamais les éléments qu'ils
  mesurent.
- **Tout élément monte par rangs de 20 cm** : murs, poteaux, voiles, équipements. Le dernier
  rang se pose le long de l'élément. Les éléments plats, comme les dalles, se coulent d'un bout
  à l'autre.
- **Les fouilles se creusent à l'écran** pendant le terrassement : les fouilles calculées
  apparaissent d'un côté du chantier vers l'autre, en terre semi-transparente. Elles restent
  ouvertes jusqu'à la fin des fondations, puis disparaissent, comme au remblai.
- **Rotation plus rapide et réglable** : « lente », « normale » ou « rapide ». Elle est
  indépendante de la vitesse de construction.

**Vérification.** Un test couvre ce cas : un mur présent dans les fouilles, la maçonnerie et
l'enduit est construit par la maçonnerie. À la fin du terrassement, aucun mur n'apparaît. Un
poteau monte par rangs de 20 cm.

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
