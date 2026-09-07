# DanCI Structural Studio

**Structural Design & Reinforcement Automation for Autodesk Revit**

Plateforme de **calcul, vérification, dimensionnement, ferraillage et documentation
automatique des structures en béton armé**, intégrée à **Autodesk Revit 2026**.

L'objectif : transformer un modèle Revit en ferraillage justifié.

```
SÉLECTION  →  GÉOMÉTRIE  →  MATÉRIAUX  →  EFFORTS  →  COMBINAISONS
    →  VÉRIFICATIONS EUROCODE  →  DIMENSIONNEMENT  →  OPTIMISATION
    →  DISPOSITIONS CONSTRUCTIVES  →  ARMATURES 3D  →  QUANTITATIF  →  NOTE DE CALCUL
```

> ⚠️ Le moteur applique les dispositions constructives et la vérification de résistance de
> section. Il ne remplace pas l'analyse globale de la structure. **Le ferraillage produit est
> un avant-projet, à vérifier et valider par l'ingénieur responsable du projet.**

---

## 1. Modules

| Module | État |
|---|---|
| **DanCI Column Design** — poteaux | ✅ Disponible |
| **DanCI Beam Design** — poutres | ✅ Disponible |
| DanCI Isolated Footing — semelles isolées | Phase 3 |
| DanCI Slab Design — dalles | Phase 4 |
| DanCI Wall Design — voiles | Phase 5 |
| DanCI Strip Footing — semelles filantes | Phase 6 |
| DanCI Grade Beam — longrines | Phase 7 |
| DanCI Stair Design — escaliers | Phase 8 |
| Plans automatiques, BBS | Phase 9 |
| Notes de calcul complètes, dashboard | Phase 10 |

La feuille de route détaillée est dans [`docs/ARCHITECTURE-V3.md`](docs/ARCHITECTURE-V3.md).

---

## 2. Installation

1. Télécharger le `.zip` de la [dernière release](../../releases/latest).
2. Décompresser, puis clic droit sur `Installer.ps1` → **Exécuter avec PowerShell**
   (ou `powershell -ExecutionPolicy Bypass -File .\Installer.ps1`).
3. Redémarrer Revit 2026 → onglet **DanCI Structural Studio**.

L'installeur n'exige aucun droit administrateur, débloque les fichiers téléchargés et
désinstalle l'ancien plugin « Armatures de poteaux ».

Désinstallation : `.\Installer.ps1 -Uninstall`

**Compiler soi-même** (prérequis : .NET SDK 8 sur Windows ; Revit n'est pas nécessaire) :

```powershell
dotnet build DanCI.StructuralStudio.sln -c Release
dotnet test tests\DanCI.Structural.Tests\DanCI.Structural.Tests.csproj -c Release
```

---

## 3. DanCI Column Design

Sélectionner des poteaux structurels → ruban **DanCI Structural Studio** → **Column**.

La fenêtre présente à gauche les réglages, au centre le tableau des éléments et la note de
calcul, à droite la coupe du poteau et le quantitatif. **Générer** modélise les armatures en
une transaction annulable d'un `Ctrl+Z`.

### Ce que le moteur décide seul

| | |
|---|---|
| Diamètre et nombre de barres | balayage HA8→HA40 × dispositions ; la solution est **notée** (excès d'acier, nombre de barres, symétrie), jamais la première qui passe |
| Diamètre des cadres | `max(6 mm ; φ_l/4)` arrondi au diamètre commercial |
| Espacement des cadres | `min(20 φ_l ; b_min ; 400 mm)` |
| Zones critiques | pied et tête, espacement × 0,6 (× 0,5 en ACI), allongées en sismique |
| Épingles | dès qu'une barre est à plus de 150 mm d'une barre tenue |
| Ancrage et recouvrement | l_bd et l₀ calculés (f_bd, l_b,rqd, α₆) |
| Renforcement | si la vérification N-M échoue, le ferraillage monte par paliers de 12 % jusqu'à A_s,max |

Chaque champ reste forçable à la main.

### Vérifications produites

Chaque vérification porte **norme, article, équation, données, sollicitation, résistance, taux
de travail et combinaison dimensionnante** :

```
[OK] Espacement des cadres en zone courante
     EN 1992-1-1:2004 art. 9.5.3 (3) : s <= scl,tmax
     Sollicitation 200 mm / Résistance 300 mm -> taux 0,67
     Combinaison : ULS-COMB-001
```

Couvertes aujourd'hui : A_s,min et A_s,max, nombre de barres, diamètre et espacement des
cadres, espacement libre entre barres, maintien des barres comprimées, élancement limite,
flexion composée biaxiale avec second ordre.

---

## 4. DanCI Beam Design

Sélectionner des poutres structurelles → ruban **DanCI Structural Studio** → **Beam**.

Saisir les moments (travée, appui gauche, appui droit) et les efforts tranchants aux deux
appuis. À droite, la **coupe en travée** et l'**élévation des zones de cadres**, dessinées à
leur espacement réel avec l'effort tranchant de chaque zone.

### Ce que le moteur calcule

| | |
|---|---|
| Flexion | diagramme rectangulaire §6.1, sections simplement et doublement armées, limite x/d de §5.5(4) |
| Sections en T | largeur participante §5.3.2.1, bascule automatique entre table et âme selon la position de l'axe neutre |
| Effort tranchant | V_Rd,c §6.2.2 avec plancher v_min, bielles à inclinaison variable §6.2.3 : cot θ = 2,5 tant que les bielles résistent, redressement ensuite |
| Cadres | **espacement variable par zone** — resserrés aux appuis, ouverts en travée, jamais un pas arbitraire |
| Chapeaux | posés seulement là où il y a un moment négatif, prolongés de max(L/4 ; a_l + l_bd) |
| Barres | optimiseur multi-lits borné par la largeur entre cadres et l'espacement libre minimal |

> La **table collaborante** et les **conditions d'appui** sont déclarées dans la fenêtre : la
> dalle n'appartient pas à l'élément poutre dans Revit, et la deviner reviendrait à supposer
> le modèle structurel.

**Pas encore couvert** : torsion (§6.3), fissuration (§7.3), flèche (§7.4), continuité
automatique des chapeaux entre travées, bielle d'about (§9.2.1.4).

---

## 5. Bases normatives

**EN 1992-1-1:2004+A1:2014**, valeurs recommandées par défaut, Annexe Nationale sélectionnable.

| Sujet | Article |
|---|---|
| Propriétés du béton, loi parabole-rectangle | 3.1.6, 3.1.7, tableau 3.1 |
| Espacement libre des barres | 8.2 (2) |
| Ancrage : f_bd, l_b,rqd, l_bd | 8.4.2, 8.4.3, 8.4.4 |
| Recouvrement l₀ | 8.7.3 |
| Excentricité minimale | 6.1 (4) |
| Élancement limite | 5.8.3.1 |
| Second ordre, courbure nominale | 5.8.8.2, 5.8.8.3 |
| Interaction biaxiale | 5.8.9 (4) |
| Poteaux : armatures et dispositions | 9.5.2, 9.5.3 |
| Poutres : largeur participante de table | 5.3.2.1 |
| Poutres : flexion, limite d'axe neutre | 6.1, 5.5(4) |
| Poutres : effort tranchant, bielles variables | 6.2.2, 6.2.3 |
| Poutres : armatures longitudinales et cadres | 9.2.1.1, 9.2.1.3, 9.2.2 |
| Zones critiques sismiques | EN 1998-1 5.4.3.2.2 |

**ACI 318-19** (10.6, 10.7.3, 25.7.2) est disponible pour les projets hors Europe, dans une
implémentation séparée — jamais mélangée aux formules Eurocode.

Tous les paramètres modifiables par une Annexe Nationale (γ_c, γ_s, α_cc, coefficients de 9.5,
α₆…) passent par `INationalAnnex`. **Aucune constante normative n'existe ailleurs dans le code.**

---

## 6. Architecture

Le moteur de calcul **ne connaît pas Revit** — règle vérifiée par la CI à chaque push.

```
Core ← Eurocodes ← Reinforcement ← Engine ← Documentation
                                      ↑
                           Revit ─────┤   ← seuls Revit, UI et App
                           UI ────────┤     référencent RevitAPI.dll
                                     App
```

| Projet | Rôle |
|---|---|
| `DanCI.Structural.Core` | unités (N, mm, MPa), géométrie, éléments, charges, `CheckResult` |
| `DanCI.Structural.Eurocodes` | EC0/EC2/EC7, Annexes Nationales, dispositions constructives |
| `DanCI.Structural.Reinforcement` | `ReinforcementPlan`, optimisation des barres, zones de cadres |
| `DanCI.Structural.Engine` | modules de dimensionnement : Column, Beam, puis les suivants |
| `DanCI.Structural.Documentation` | quantitatifs, CSV, notes de calcul |
| `DanCI.Structural.Revit` | lecture de la géométrie, écriture des `Rebar` |
| `DanCI.Structural.UI` | fenêtres WPF (sans RevitAPI) |
| `DanCI.Structural.App` | ruban et commandes Revit |
| `DanCI.Structural.Tests` | tests du moteur, exécutés par la CI |

**Pièce maîtresse** : `ReinforcementPlan` décrit le ferraillage en coordonnées locales (mm).
Un unique `RebarWriter` le traduit en objets Revit `Rebar` — ce code s'écrit une fois et sert
ensuite aux poutres, semelles, dalles et voiles.

---

## 7. Fiabilité du calcul

La priorité est l'exactitude, pas l'apparence. En pratique :

- **Tests unitaires obligatoires** : aucune formule normative n'entre sans son test.
  La CI exécute la suite à chaque push ; un test rouge casse le build.
- **Fiches de validation** dans `docs/validation/` : énoncé, calcul manuel détaillé, résultat
  du moteur, écart. Un module n'est pas terminé sans elles.
- **Une compilation verte n'est pas une validation.** Les deux sont vérifiées séparément.

---

## 8. Versions

Quatre numéros indépendants, reportés dans chaque note de calcul, pour savoir avec quel moteur
un calcul a été produit :

```
ApplicationVersion        3.2.0
CalculationEngineVersion  1.2.0
EurocodeLibraryVersion    1.2.0
DesignDataSchemaVersion   1
```

---

## 9. En cas de problème

| Symptôme | Cause / solution |
|---|---|
| L'onglet n'apparaît pas | DLL bloquée par Windows (Propriétés → Débloquer) ou chemin du `.addin` incorrect |
| « Aucun type de barre d'armature » | Insertion → Charger la famille → Structure → Armature |
| « Cet élément ne peut pas recevoir d'armatures » | Le poteau n'est pas structurel ou son matériau n'est pas du béton |
| Armatures invisibles | Vue 3D : niveau de détail *Fin* ; en coupe, activer la visibilité des armatures |
| Cadres sans crochets | Charger un type de crochet à 135° dans le projet |
