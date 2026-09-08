# DanCI Structural Studio — Analyse de l'existant et Architecture V3

*Document de décision. À valider avant toute modification du code.*

---

# PARTIE A — ANALYSE DU CODE ACTUEL

## A.1 Ce que fait le plugin aujourd'hui

Un seul cas d'usage : **ferrailler des poteaux béton déjà modélisés dans Revit**.

L'utilisateur sélectionne des poteaux structurels, une fenêtre WPF affiche le ferraillage
proposé, il valide, et le plugin crée les armatures dans une transaction unique.

Fonctionnellement, la v2.0.0 couvre :

| Domaine | État |
|---|---|
| Sections | Rectangulaires et circulaires, y compris tournées en plan. Poteaux inclinés refusés. |
| Dispositions constructives | EC2 §9.5 (A<sub>s,min</sub>/A<sub>s,max</sub>, nombre de barres, φ<sub>t</sub>, s<sub>cl,tmax</sub>, zones critiques, barres tenues à 150 mm) et ACI 318-19 équivalent. |
| Choix des barres | Balayage φ8→φ40 × dispositions, score = excès d'acier + nombre de barres + dissymétrie. |
| Recouvrement | l<sub>0</sub> EC2 §8.4/§8.7, utilisé comme longueur d'attente en tête. |
| Résistance | Diagramme d'interaction N-M par intégration, second ordre par courbure nominale, interaction biaxiale §5.8.9. Renforcement itératif si ça ne passe pas. |
| Quantitatif | Longueurs de coupe développées, nombre de cadres, poids par diamètre, kg/m³, export CSV. |
| Restitution | Aperçu graphique de la coupe, note de calcul texte, configurations enregistrées. |

**Ce qu'il ne fait pas** : aucune combinaison de charges, aucun autre élément que le poteau,
aucun plan, aucune BBS Revit, aucune persistance du calcul, aucun test.

## A.2 Architecture actuelle

Assembly unique `ArmaturesPoteaux.dll`, 29 fichiers, ~4 700 lignes, 5 dossiers :

```
Core/       ColumnGeometry, DesignInput, DesignResult, RebarLayout, StirrupZones,
            SteelQuantities, CapacityCheck, PresetStore, LengthUnits
Design/     IDesignCode, Eurocode2Code, Aci318Code, DesignCodeFactory,
            ColumnRebarDesigner, SectionCapacity, QuantityCalculator, QuantityReport
RevitOps/   ColumnInspector, RebarTypeProvider, ColumnRebarBuilder, BuildOutcome
UI/         MainWindow(.xaml/.cs), SectionPreview, ColumnRow, IconFactory, InverseBooleanConverter
Commands/   GenerateColumnRebarCommand, AboutCommand, ColumnSelectionFilter, DocumentAvailability
```

Le flux réel :

```
GenerateColumnRebarCommand
   → ColumnInspector          (Revit → ColumnGeometry)
   → MainWindow               (saisie + orchestration du calcul)
       → ColumnRebarDesigner  (choix des barres, boucle de vérification)
           → IDesignCode      (constantes normatives)
           → SectionCapacity  (N-M, second ordre)
           → QuantityCalculator
   → ColumnRebarBuilder       (DesignResult → Rebar Revit)
```

**Le bon point** : `Core/` et `Design/` ne référencent Revit que par `LengthUnits`
(conversion pieds↔mm) et `ColumnGeometry.Host/AxisX/...`. La séparation calcul/Revit est
donc **déjà à 80 %** — c'est la meilleure nouvelle de cette analyse.

**Le mauvais point** : c'est la fenêtre WPF qui orchestre le calcul (`MainWindow.Calculate()`
instancie le designer et boucle sur les poteaux). Aucun pipeline réutilisable en mode batch
ou en test.

## A.3 Version de Revit ciblée

**Revit 2026 uniquement.** API fournie par NuGet `Nice3point.Revit.Api.RevitAPI` /
`.RevitAPIUI` 2026.4.10, en `ExcludeAssets=runtime` (compilation sans Revit installé).
Manifest déployé dans `%APPDATA%\Autodesk\Revit\Addins\2026\`.

## A.4 Version de .NET

**`net8.0-windows`**, `UseWPF=true`, `PlatformTarget=x64`, C# `latest`, nullable désactivé,
`ImplicitUsings=false`. C'est le bon runtime pour Revit 2025/2026.

## A.5 Comment les armatures sont générées

Via les **vrais objets `Rebar`** — pas de `DirectShape`. Point conforme à ton §14.

1. `RebarTypeProvider` cherche un `RebarBarType` au diamètre voulu ; sinon il **duplique** le
   type le plus proche et corrige `BarNominalDiameter`. Idem pour le `RebarHookType` (135°,
   sinon 90°, sinon aucun).
2. `RebarLayout` (Core) donne les coordonnées locales en mm : barres, demi-portées de cadre,
   positions d'épingles. `StirrupZones` découpe la hauteur en zones (pied resserré / courant /
   tête resserré).
3. `ColumnRebarBuilder` transforme ces coordonnées en `XYZ` monde via
   `ColumnGeometry.ToWorld()` puis appelle
   `Rebar.CreateFromCurves(...)`, suivi de `SetLayoutAsFixedNumber` (lits longitudinaux) ou
   `SetLayoutAsMaximumSpacing` (cadres).
4. Un `IFailuresPreprocessor` avale les avertissements (barres en attente dépassant l'hôte).
5. Chaque `Rebar` reçoit un commentaire descriptif et `SetUnobscuredInView` dans la 3D active.

## A.6 Classes importantes

| Classe | Rôle | Devenir en V3 |
|---|---|---|
| `ColumnGeometry` | Section + hauteur + repère local, en mm | Devient `ColumnSection` + `ElementFrame` génériques |
| `RebarLayout` | **Source unique** des coordonnées de barres | Devient le `ReinforcementPlan` générique — pièce maîtresse |
| `StirrupZones` | Découpage en zones d'espacement | Généralisé (poutres : zones d'appui/courante) |
| `IDesignCode` | Abstraction des constantes normatives | Éclaté : constantes → `Eurocodes/EC2`, NDP → `NationalAnnex` |
| `ColumnRebarDesigner` | Choix des barres + boucle de vérification | Éclaté : `RebarOptimizer` + `ColumnDesignModule` |
| `SectionCapacity` | Diagramme N-M, second ordre | Devient `EC2.InteractionDiagram` + `EC2.SecondOrder`, réutilisé par voiles et semelles |
| `ColumnRebarBuilder` | Écriture des `Rebar` | Devient `RebarWriter` générique piloté par `ReinforcementPlan` |
| `ColumnInspector` | Lecture géométrie Revit | Devient `Revit/Geometry/ColumnReader` + frères |
| `RebarTypeProvider` | Types de barres et crochets | **Conservé quasi tel quel** |

## A.7 Ce qu'il faut conserver

1. **`RebarTypeProvider`** — robuste, gère l'absence de types dans le projet. À garder.
2. **`ColumnRebarBuilder`** — la mécanique `CreateFromCurves` + accessors fonctionne et
   représente le savoir-faire Revit le plus difficile à réécrire. À **généraliser**, pas à jeter.
3. **`RebarLayout` / `StirrupZones`** — l'idée « une seule source de coordonnées consommée par
   le modeleur, l'aperçu, le quantitatif et le calcul » est exactement le bon principe. À étendre.
4. **`SectionCapacity`** — l'intégration par bandes avec règle des trois pivots est réutilisable
   pour les voiles et les semelles. À déplacer et **à valider par tests**.
5. **`SectionPreview`** — l'aperçu graphique. À généraliser en contrôle réutilisable.
6. **La chaîne CI/release** — compilation Windows + paquet d'installation automatique. Acquis.
7. **La discipline de traçabilité déjà présente** : chaque valeur retenue est justifiée par un
   article dans `Notes`. C'est l'embryon de ton `CheckResult` (§31).

## A.8 Problèmes techniques à corriger

Classés par gravité.

### Bloquants pour la fiabilité

| # | Problème | Conséquence |
|---|---|---|
| ~~P1~~ ✅ | ~~Zéro test unitaire.~~ | Résolu en phase 0 : suite de tests exécutée par la CI. |
| ~~P2~~ ✅ | ~~Moteur N-M jamais validé numériquement.~~ | Résolu en phase 1 : fiches COLUMN-02 à COLUMN-04. |
| P3 | **Le plugin n'a jamais tourné dans Revit.** Validé à la compilation uniquement. | Les appels API peuvent échouer au premier essai. |
| ~~P4~~ ✅ | ~~Aucune combinaison de charges.~~ | Résolu en phase 0 : `InternalForces` / `LoadCombination`. Reste à alimenter par import (phase 2). |
| ~~P5~~ ✅ | ~~Coefficients normatifs codés en dur.~~ | Résolu en phase 0 : `INationalAnnex`, vérifié par test. |

### Structurels

| # | Problème | Conséquence |
|---|---|---|
| ~~P6~~ ✅ | ~~EC2 et ACI mélangés.~~ | Résolu en phase 0 : implémentations séparées, ACI hors du chemin critique. |
| ~~P7~~ ✅ | ~~La fenêtre WPF orchestre le calcul.~~ | Résolu en phase 0 : `DesignPipeline`. |
| P8 | `DesignInput` est un objet fourre-tout (28 propriétés) mêlant matériaux, efforts, préférences de ferraillage et options d'affichage. | Ingérable dès qu'on ajoute 7 types d'éléments. |
| ~~P9~~ ✅ | ~~Aucune persistance.~~ | Résolu en phase 1 : Extensible Storage + empreinte des données. |
| ~~P10~~ ✅ | ~~Enrobage saisi sans classe d'exposition.~~ | Résolu en phase 1 : EC2 §4.4.1 complet, fiche COLUMN-06. |
| P11 | Ancrages absents : seule la longueur de recouvrement est calculée, les barres n'ont pas de crochet ni de retour en pied. | Contraire à ton §17. |
| P12 | Tous les recouvrements au même niveau (tête de poteau). | Contraire à ton §18. |

### Techniques mineurs

| # | Problème |
|---|---|
| P13 | `Rebar.CreateFromCurves(...)` utilisé dans sa surcharge **obsolète** en 2026 (remplacée par `BarTerminationsData`). Fonctionne, mais à migrer. |
| ~~P14~~ ✅ | ~~Paramètre mort dans le modeleur.~~ Résolu en phase 0. |
| P15 | Pas de contrôle de congestion : rien ne vérifie que les barres + cadres tiennent physiquement une fois les crochets pris en compte. |
| ~~P16~~ ✅ | ~~Nom du produit incohérent.~~ Résolu en phase 0. |

### Point de vigilance sur ta spécification (§22)

**Revit ne contient pas d'efforts internes.** Le modèle analytique Revit 2026 porte la
géométrie, les appuis et les charges appliquées, mais Revit **n'a pas de solveur** : il n'y a
ni N, ni M, ni V à lire. Conséquences :

- « Mode Revit Analytical Model » ne peut fournir que **géométrie, appuis et cas de charge** —
  pas les sollicitations.
- Les efforts viendront donc de : **saisie manuelle**, **CSV/JSON**, ou **Robot Structural
  Analysis** (API `RobotOM`, COM, nécessite Robot installé sur le poste).
- Je propose : manuel + CSV/JSON en Phase 2, adaptateur Robot isolé derrière une interface
  `ILoadEffectsProvider` en Phase 9+.

---

# PARTIE B — ARCHITECTURE V3

## B.1 Les cinq principes

1. **Le moteur ne connaît pas Revit.** Aucun projet de calcul ne référence `RevitAPI.dll`.
   Revit entre par un adaptateur, sort par un traducteur.
2. **Une seule unité interne : N, mm, MPa** (donc N·mm pour les moments). Toute conversion se
   fait aux frontières (`RevitUnits` en entrée, formatage en sortie). Zéro conversion dispersée.
3. **Toute vérification renvoie un `CheckResult` traçable** : norme, clause, équation, entrées,
   demande, résistance, taux, statut. La note de calcul est alors une simple mise en forme.
4. **Toute constante normative vit dans `Eurocodes/`**, jamais dans un module de
   dimensionnement, jamais dans l'UI.
5. **Aucune formule n'entre sans test.** Un test = un cas calculé à la main et documenté.

## B.2 Solution multi-projets

```
DanCI.StructuralStudio.sln
│
├── src/
│   ├── DanCI.Structural.Core/            net8.0 — aucune dépendance
│   │   ├── Units/                        UnitConverter, conversions, formatage
│   │   ├── Materials/                    ConcreteGrade, SteelGrade, ExposureClass, Cover
│   │   ├── Geometry/                     SectionShape, RectangularSection, CircularSection,
│   │   │                                 TSection, ElementFrame, Point2D
│   │   ├── Elements/                     StructuralElementData + Column/Beam/Slab/...
│   │   ├── Loads/                        LoadCase, InternalForces, ForceStation,
│   │   │                                 LoadCombination, DesignSituation
│   │   └── Results/                      CheckResult, UtilizationStatus, DesignOutcome
│   │
│   ├── DanCI.Structural.Eurocodes/       net8.0 — le savoir normatif
│   │   ├── Configuration/                EurocodeGeneration, CodeSettings
│   │   ├── NationalAnnex/                INationalAnnex, Recommended, France, Belgium…
│   │   ├── EC0/                          PartialFactors, CombinationGenerator
│   │   ├── EC1/                          (réservé)
│   │   ├── EC2/                          ConcreteProperties, Bending, Shear, Torsion,
│   │   │                                 Punching, InteractionDiagram, SecondOrder,
│   │   │                                 Anchorage, Laps, Cracking, Deflection, Detailing
│   │   └── EC7/                          BearingCapacity, Sliding, Overturning, SoilPressure
│   │
│   ├── DanCI.Structural.Reinforcement/   net8.0 — DanCI Rebar Engine (géométrie pure)
│   │   ├── Plan/                         ReinforcementPlan, BarRun, StirrupSet, BarMark
│   │   ├── Optimization/                 RebarOptimizer, BarDatabase, LayoutCandidate
│   │   ├── Detailing/                    SpacingChecker, CongestionChecker
│   │   └── Bbs/                          BendingShape, BarSchedule
│   │
│   ├── DanCI.Structural.Engine/          net8.0 — DanCI Structural Engine
│   │   ├── Pipeline/                     IDesignModule, DesignPipeline, DesignRequest
│   │   ├── Column/  Beam/  Slab/  Wall/
│   │   ├── IsolatedFooting/  StripFooting/  GradeBeam/  Stair/
│   │   └── Rules/                        ColumnRules, BeamRules, … (dispositions constructives)
│   │
│   ├── DanCI.Structural.Documentation/   net8.0
│   │   ├── Reports/                      CalculationReport (texte, HTML, plus tard DOCX)
│   │   ├── Bbs/                          BarBendingScheduleBuilder
│   │   └── Quantities/                   QuantityReport (repris de l'existant)
│   │
│   ├── DanCI.Structural.Revit/           net8.0-windows — seul point de contact Revit
│   │   ├── Selection/                    filtres, sélection interactive
│   │   ├── Geometry/                     ColumnReader, BeamReader, SlabReader, …
│   │   ├── Materials/                    lecture béton/acier/enrobage du modèle
│   │   ├── Rebar/                        RebarTypeProvider, RebarWriter (générique)
│   │   ├── Views/                        création de vues, coupes, annotations
│   │   ├── Schedules/                    nomenclatures d'armatures
│   │   └── Storage/                      Extensible Storage — DesignDataStore
│   │
│   ├── DanCI.Structural.UI/              net8.0-windows — WPF, MVVM
│   │   ├── Views/  ViewModels/  Controls/ (SectionPreviewControl)  Resources/
│   │
│   └── DanCI.Structural.App/             net8.0-windows — l'add-in
│       ├── Application.cs                ruban DanCI Structural Studio
│       └── Commands/                     une commande par bouton
│
├── tests/
│   └── DanCI.Structural.Tests/           net8.0, xUnit — exécuté par la CI
│       ├── EC2/  EC7/  Engine/  Reinforcement/  Validation/
│
├── docs/
│   ├── ARCHITECTURE-V3.md                ce document
│   └── validation/                       une fiche par cas validé à la main
│
└── install/                              manifest .addin + installeur
```

**Règle de dépendance** (vérifiée par la CI) :

```
Core ← Eurocodes ← Reinforcement ← Engine ← Documentation
                                       ↑
                            Revit ─────┤
                            UI ────────┤
                                       App
```

`RevitAPI` n'apparaît que dans `Revit`, `UI` (handle fenêtre) et `App`.

## B.3 Le flux de données cible

```
Revit Element
   │  Revit/Geometry/*Reader  +  Revit/Materials/MaterialReader
   ▼
StructuralElementData          (Core — géométrie + matériaux, en mm/MPa)
   │  + LoadEffects            (manuel, CSV/JSON, Robot)
   ▼
DesignRequest                  (élément + combinaisons + préférences)
   │  DesignPipeline → IDesignModule (Column, Beam, …)
   ▼
DesignOutcome                  (CheckResult[] + ReinforcementPlan + statut)
   │
   ├──► Revit/Rebar/RebarWriter        → objets Rebar 3D
   ├──► Documentation/Reports          → note de calcul
   ├──► Documentation/Bbs              → Bar Bending Schedule
   ├──► Revit/Views                    → plans, coupes, annotations
   └──► Revit/Storage/DesignDataStore  → Extensible Storage (permet UPDATE)
```

## B.4 Les types pivots

**`InternalForces`** — les six composantes restent **toujours ensemble** (répond au §23) :

```csharp
public readonly struct InternalForces          // N, N·mm
{
    public double N { get; }                    // + compression
    public double Vy { get; }  public double Vz { get; }
    public double T  { get; }                   // torsion
    public double My { get; }  public double Mz { get; }
}
```

**`LoadCombination`** — porte une situation de projet et, pour les éléments linéiques, un
jeu de stations le long de la portée :

```csharp
public sealed class LoadCombination
{
    public string Id { get; }                        // "ULS-COMB-001"
    public DesignSituation Situation { get; }        // ULS / SLS_Char / SLS_QP …
    public IReadOnlyList<ForceStation> Stations { get; }
}
```

Chaque vérification renvoie la **combinaison dimensionnante** dans son `CheckResult` — on ne
compare jamais un N et un M issus de combinaisons différentes.

**`CheckResult`** — la brique de traçabilité (§31) :

```csharp
public sealed class CheckResult
{
    public string Code { get; }            // "EN 1992-1-1:2004"
    public string Clause { get; }          // "6.2.3 (3)"
    public string Equation { get; }        // "VRd,s = (Asw/s)·z·fywd·cot θ"
    public string Description { get; }
    public IReadOnlyDictionary<string, Quantity> Inputs { get; }
    public Quantity Demand { get; }        // VEd = 102 kN
    public Quantity Resistance { get; }    // VRd = 145 kN
    public double Utilization { get; }     // 0,70
    public CheckStatus Status { get; }      // Pass / Warning / Fail / NotApplicable
    public string GoverningCombination { get; }
}
```

La note de calcul, le tableau de résultats, le dashboard et les couleurs de contrôle sont
alors **quatre mises en forme de la même liste** — aucun calcul dupliqué.

**`ReinforcementPlan`** — la généralisation de `RebarLayout`, et la clé pour ajouter 7 types
d'éléments sans réécrire le code Revit :

```csharp
public sealed class ReinforcementPlan
{
    public IReadOnlyList<BarRun> Bars { get; }         // polyligne locale + répartition
    public IReadOnlyList<StirrupSet> Stirrups { get; } // boucle locale + zones d'espacement
}
```

Un seul `RebarWriter` traduit n'importe quel plan en objets Revit. Écrit une fois, réutilisé
par le poteau, la poutre, la semelle, la dalle, le voile, l'escalier.

## B.5 Normes, générations et Annexes Nationales

```csharp
public sealed class CodeSettings
{
    public EurocodeGeneration Generation { get; }   // EN_1992_2004 | EN_1992_2023
    public INationalAnnex NationalAnnex { get; }    // Recommended | France | …
}
```

`INationalAnnex` expose les **NDP** (γ<sub>c</sub>, γ<sub>s</sub>, α<sub>cc</sub>,
c<sub>min,dur</sub>, k<sub>1</sub>…k<sub>4</sub>, w<sub>max</sub>, θ<sub>min/max</sub>, …).
Aucune de ces valeurs n'apparaît ailleurs. Un calcul mené avec l'AN France et un calcul mené
avec les valeurs recommandées sont deux calculs distincts, tracés comme tels dans le rapport.

**Recommandation** : démarrer sur **EN 1992-1-1:2004+A1:2014 + AN France**, et n'ouvrir la
branche 2023 qu'une fois les modules Poteau et Poutre validés — la 2ᵉ génération change
notamment le cisaillement et les ancrages, c'est un chantier à part entière.

**ACI 318** : sorti du chemin critique. L'implémentation actuelle est conservée comme
`Codes/ACI/` mais n'est plus mélangée à EC2 derrière une interface commune (P6).

## B.6 Enrobage (§13)

`Core/Materials/ConcreteCover` implémente la chaîne EC2 §4.4.1 :

```
c_nom = c_min + Δc_dev
c_min = max(c_min,b ; c_min,dur + Δc_dur,γ − Δc_dur,st − Δc_dur,add ; 10 mm)
```

avec classe d'exposition (XC1…XA3), classe structurale S1…S6 modulée par la durée de service,
la classe de résistance et la maîtrise de production. L'utilisateur choisit l'exposition ;
l'enrobage devient un **résultat calculé et tracé**, modifiable mais jamais arbitraire.

## B.7 Persistance et commande UPDATE (§38-39)

`Revit/Storage/DesignDataStore` écrit dans l'**Extensible Storage** de chaque élément :

| Champ | Contenu |
|---|---|
| `EngineVersion` | version du moteur ayant produit le calcul |
| `CodeSettings` | génération d'Eurocode + Annexe Nationale |
| `InputHash` | empreinte de la géométrie, des matériaux et des efforts |
| `DesignJson` | `DesignOutcome` sérialisé |
| `RebarIds` | armatures créées, pour pouvoir les remplacer |

`UPDATE STRUCTURAL DESIGN` recalcule l'empreinte : si elle diffère, l'élément est signalé
« Design data has changed » et peut être recalculé et referraillé.

## B.8 Versionning (§ Versionning)

Quatre numéros indépendants, écrits dans chaque note de calcul :

```
ApplicationVersion        3.0.0     (le produit)
CalculationEngineVersion  1.0.0     (Engine + Reinforcement)
EurocodeLibraryVersion    1.0.0     (Eurocodes)
DesignDataSchemaVersion   1         (Extensible Storage)
```

Un calcul stocké sait avec quel moteur il a été produit ; une montée de version du moteur
invalide explicitement les calculs antérieurs.

## B.9 Tests et validation (§33-34)

Projet `DanCI.Structural.Tests` (xUnit), **exécuté par la CI à chaque push** — un test rouge
casse le build.

Trois familles :

1. **Tests de formule** — chaque fonction EC2 contre un calcul manuel documenté.
2. **Tests de non-régression** — un ferraillage de référence par type d'élément.
3. **Fiches de validation** — `docs/validation/BEAM-01.md` : énoncé, calcul manuel détaillé,
   résultat du moteur, écart, commentaire. **Aucun module n'est déclaré terminé sans sa fiche.**

Règle explicite, à rappeler dans le README : **une compilation verte n'est pas une validation.**

## B.10 Migration : ce qu'on garde, déplace, réécrit

| Existant | Action | Destination |
|---|---|---|
| `LengthUnits` | Étendu | `Core/Units/UnitConverter` |
| `ColumnGeometry` | Scindé | `Core/Geometry/*` + `Core/Elements/ColumnData` |
| `RebarLayout`, `StirrupZones` | Généralisés | `Reinforcement/Plan/*` |
| `SteelQuantities`, `QuantityCalculator/Report` | Déplacés | `Documentation/Quantities` |
| `Eurocode2Code` | Éclaté | `Eurocodes/EC2/Detailing` + `NationalAnnex` |
| `Aci318Code` | Isolé | `Codes/ACI` (hors chemin critique) |
| `SectionCapacity` | Déplacé **et testé** | `Eurocodes/EC2/InteractionDiagram` + `SecondOrder` |
| `ColumnRebarDesigner` | Scindé | `Engine/Column` + `Reinforcement/Optimization` |
| `ColumnInspector` | Déplacé | `Revit/Geometry/ColumnReader` |
| `RebarTypeProvider` | Déplacé tel quel | `Revit/Rebar` |
| `ColumnRebarBuilder` | Généralisé | `Revit/Rebar/RebarWriter` |
| `MainWindow` | Réécrit en MVVM | `UI/Views` + `UI/ViewModels` |
| `SectionPreview` | Transformé en contrôle | `UI/Controls/SectionPreviewControl` |
| `PresetStore` | Étendu | `Core/Settings/ProjectSettings` |

**Aucune réécriture depuis zéro.** Le poteau doit continuer à fonctionner à chaque étape.

---

# PARTIE C — FEUILLE DE ROUTE

| Phase | Contenu | Sortie vérifiable |
|---|---|---|
| **0** ✅ | Renommage DanCI Structural Studio, découpage en 9 projets, projet de tests, règle de dépendance en CI. Le poteau fonctionne à l'identique. | **Fait** — build et tests verts, ruban `DanCI Structural Studio` |
| **1** ✅ | Stabilisation du poteau : `CheckResult`, combinaisons, enrobage EC2 §4.4.1, persistance du calcul, **validation contre calcul manuel** | **Fait** — 6 fiches `docs/validation/COLUMN-*.md`, toutes adossées à des tests |
| **2** ✅ | **MODULE POUTRE** — flexion, effort tranchant, cadres zonés, section en T | **Fait** — fiches BEAM-01 et BEAM-02 |
| **3** ✅ | **MODULE SEMELLE ISOLÉE** — sol EC7 (aire effective, glissement, renversement), flexion des consoles, effort tranchant dans les deux directions, poinçonnement §6.4.4(2) par balayage | **Fait** — fiches FOOT-01 et FOOT-02 |
| **4** ✅ | **MODULE DALLE** — dalle pleine portant dans un sens : combinaisons EN 1990, flexion, effort tranchant sans armatures, flèche §7.4.2, fissuration §7.3.3, répartition §9.3.1.1 | **Fait** — fiches SLAB-01 et SLAB-02 |
| **5** ✅ | **MODULE VOILE** — flambement §12.6.5.1, second ordre hors plan, dispositions §9.6, effort tranchant et flexion dans le plan | **Fait** — fiches WALL-01 et WALL-02 |
| **6** ✅ | **MODULE SEMELLE FILANTE** — au mètre courant : sol EC7, flexion de console, minimum déterminant, ancrage transversal, poinçonnement sans objet | **Fait** — fiches STRIP-01 et STRIP-02 |
| **7** ✅ | **MODULE LONGRINE** — effort de liaison EN 1998-5 §5.4.1.2(7), minimum 0,4 % sur les deux nappes §5.8.2(5), section minimale §5.8.1(4), appui du sol jamais crédité | **Fait** — fiches GRADE-01 et GRADE-02 |
| **8** ✅ | **MODULE ESCALIER** — poids propre incliné EN 1991-1-1 (γ t / cos α et γ R / 2), statique sous deux charges réparties, flexion et flèche, nœud volée-palier à nappes croisées | **Fait** — fiches STAIR-01 et STAIR-02 |
| **9** ⏭ | Plans automatiques, BBS Revit, repérage des barres — prochaine étape | |
| **10** | Notes de calcul complètes, dashboard, mode batch, couleurs de contrôle | |

## Détail de la Phase 2 — DanCI Beam Design

Étapes, **compilation + tests verts entre chaque** :

1. `Core/Elements/BeamData` + `Revit/Geometry/BeamReader` (rectangulaire et en T réelle,
   détection de la dalle collaborante).
2. `Loads` : saisie manuelle + import CSV/JSON, combinaisons EN 1990 (6.10 / 6.10a-b).
3. `EC2/Bending` : A<sub>s,req</sub> par section rectangulaire et en T, avec limitation de
   x/d, A<sub>s,min</sub> §9.2.1.1, A<sub>s,max</sub>. **Tests d'abord.**
4. `EC2/Shear` : V<sub>Rd,c</sub> §6.2.2, V<sub>Rd,s</sub> et V<sub>Rd,max</sub> §6.2.3 avec
   bielle à inclinaison variable θ, A<sub>sw</sub>/s, espacements §9.2.2. **Tests d'abord.**
5. `EC2/Anchorage` + `Laps` : l<sub>bd</sub>, l<sub>0</sub>, décalage des recouvrements,
   règle du décalage a<sub>l</sub>.
6. `Reinforcement/Optimization/RebarOptimizer` : `OptimizeReinforcementLayout()` — plusieurs
   combinaisons évaluées, espacements libres et congestion contrôlés, lits multiples gérés.
7. `Engine/Beam/BeamDesignModule` : découpage en zones (appuis / travée), chapeaux, aciers de
   montage, étriers à espacement variable par zone.
8. `ReinforcementPlan` de poutre → `RebarWriter` → armatures 3D Revit.
9. UI : onglet Beam, tableau de vérifications, aperçu de coupe en travée et sur appui.
10. Fiches de validation `docs/validation/BEAM-01..05.md`.

---

# PARTIE D — CE QUE J'AI BESOIN QUE TU VALIDES

1. **Découpage en 8 projets** (§B.2) plutôt qu'un assembly unique — plus de fichiers, mais
   c'est ce qui rend le moteur testable et indépendant de Revit.
2. **Renommage complet en Phase 0** : `DanCI.Structural.*`, ruban `DanCI Structural Studio`,
   nouveau `.addin`. L'ancien plugin `ArmaturesPoteaux` sera désinstallé par l'installeur.
3. **EN 1992-1-1:2004 + Annexe Nationale France en premier**, 2ᵉ génération plus tard.
4. **Efforts : manuel + CSV/JSON d'abord**, Robot plus tard — puisque Revit ne fournit pas de
   sollicitations (§A.8).
5. **Phase 1 avant Phase 2** : valider le N-M du poteau existant avant d'écrire la poutre.
   Sauter cette étape, c'est construire sur un moteur non vérifié.
6. **Langue** : identifiants et ruban en anglais (comme ta spécification), commentaires et
   interface en français, avec une couche de ressources pour basculer plus tard.

Dis-moi ce que tu valides ou ce que tu veux changer, et j'enchaîne sur la Phase 0.
