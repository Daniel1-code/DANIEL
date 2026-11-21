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

### Licence QuestPDF (PDF) — à confirmer par DanCI

QuestPDF est distribué sous licence *Community* (gratuite) ou payante (*Professional*,
*Enterprise*). Extrait du fichier de licence du paquet 2026.9.2 :

- **Petite entreprise** : licence Community gratuite si le chiffre d'affaires annuel brut du
  dernier exercice clos est **inférieur à 1 000 000 USD**, consolidé avec les entités sous
  contrôle commun.
- **Exclus de la Community**, quel que soit leur chiffre d'affaires : entités publiques,
  administrations, sociétés cotées.
- Une entreprise non éligible doit acheter une licence avant tout usage en production
  (60 jours d'évaluation autorisés).

L'extension déclare la licence **Community** (`QuestPDF.Settings.License`). **C'est à DanCI de
confirmer son éligibilité.** Sinon, il faut acquérir une licence Professional et me le dire
pour changer la déclaration.

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
- **Prix** : la bibliothèque (`prix_unitaires.json`) reste commune à l'installation ; une
  bibliothèque par projet n'est pas encore choisie depuis le fichier projet.

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
