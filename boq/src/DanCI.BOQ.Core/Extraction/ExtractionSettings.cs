using System.Collections.Generic;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Regle de deduction des ouvertures dans les surfaces de maconnerie.</summary>
    public enum OpeningDeductionMode
    {
        /// <summary>Toutes les ouvertures sont deduites.</summary>
        Toutes,
        /// <summary>Seules les ouvertures de surface STRICTEMENT superieure au seuil sont deduites.</summary>
        AuDelaDuSeuil,
        /// <summary>Aucune ouverture n'est deduite (surface brute).</summary>
        Aucune
    }

    /// <summary>Face du mur sur laquelle la surface de maconnerie est mesuree.</summary>
    public enum MasonryMeasureFace
    {
        /// <summary>Moyenne des faces exterieure et interieure : equivaut a la surface a l'axe.</summary>
        Moyenne,
        Exterieure,
        Interieure
    }

    /// <summary>
    /// Contenu de extraction.json : comment l'adaptateur Revit transforme la maquette en
    /// quantites. Ce sont des conventions de metre, donc des choix explicites et modifiables.
    /// </summary>
    public sealed class ExtractionSettings
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Motifs de nom de materiau reconnus comme beton QUAND le materiau n'a pas d'actif
        /// structurel de classe Beton ni de classe de materiau « Béton/Concrete ».
        /// </summary>
        [JsonPropertyName("materiauxBeton")]
        public PatternList ConcreteMaterialNames { get; set; } =
            new PatternList(new[] { "*béton*", "*beton*", "*concrete*", "*C??/??*" });

        [JsonPropertyName("deductionOuvertures")]
        public OpeningDeductionMode OpeningDeduction { get; set; } = OpeningDeductionMode.AuDelaDuSeuil;

        /// <summary>Seuil en m² (mode AuDelaDuSeuil).</summary>
        [JsonPropertyName("seuilDeductionM2")]
        public decimal OpeningDeductionThresholdM2 { get; set; } = 0.5m;

        [JsonPropertyName("faceMesureMaconnerie")]
        public MasonryMeasureFace MasonryFace { get; set; } = MasonryMeasureFace.Moyenne;

        /// <summary>Valeurs du parametre DanCI_BOQ_Exclure qui excluent l'element.</summary>
        [JsonPropertyName("valeursExclusion")]
        public List<string> ExcludeValues { get; set; } = new List<string> { "Oui", "Yes", "1", "Vrai", "True" };

        [JsonPropertyName("coffrage")] public FormworkSettings Formwork { get; set; } = new FormworkSettings();

        /// <summary>
        /// Ratios d'armatures (kg/m³ de beton) appliques aux elements en beton SANS armature
        /// modelisee. Une categorie sans ratio produit une quantite MANQUANTE, jamais une
        /// quantite inventee.
        /// </summary>
        [JsonPropertyName("ratiosArmatures")] public List<RebarRatio> RebarRatios { get; set; } = new List<RebarRatio>();

        [JsonPropertyName("acier")] public SteelSettings Steel { get; set; } = new SteelSettings();

        [JsonPropertyName("finitions")] public FinishSettings Finishes { get; set; } = new FinishSettings();

        [JsonPropertyName("terrassement")] public ExcavationSettings Excavation { get; set; } = new ExcavationSettings();

        [JsonPropertyName("betonProprete")] public BlindingSettings Blinding { get; set; } = new BlindingSettings();

        /// <summary>Pieux : reconnus par le nom de famille ou de type (fondations).</summary>
        [JsonPropertyName("pieux")] public PileSettings Piles { get; set; } = new PileSettings();

        /// <summary>Terrassements generaux : decapage, pleine masse, remblai, evacuation.</summary>
        [JsonPropertyName("terrassementsGeneraux")] public GeneralEarthworkSettings GeneralEarthworks { get; set; } = new GeneralEarthworkSettings();

        /// <summary>VRD : sols exterieurs (voiries, parkings, trottoirs) et clotures, reconnus par leur type.</summary>
        [JsonPropertyName("vrd")] public VrdSettings Vrd { get; set; } = new VrdSettings();
        [JsonPropertyName("couches")] public LayerSettings Layers { get; set; } = new LayerSettings();
        [JsonPropertyName("inventaire")] public InventorySettings Inventory { get; set; } = new InventorySettings();
        [JsonPropertyName("fouillesSousMurs")] public WallTrenchSettings WallTrenches { get; set; } = new WallTrenchSettings();

        /// <summary>Second oeuvre : garde-corps, gouttieres, bandeaux, plinthes, faux-plafonds, murs-rideaux.</summary>
        [JsonPropertyName("secondOeuvre")] public SecondWorkSettings SecondWork { get; set; } = new SecondWorkSettings();

        /// <summary>Dallages sur terre-plein : herissonnage et polyane.</summary>
        [JsonPropertyName("dallages")] public SlabOnGradeSettings SlabsOnGrade { get; set; } = new SlabOnGradeSettings();
    }

    /// <summary>
    /// Reconnaissance des materiaux de finition par leur NOM (couches des murs, sols, plafonds,
    /// toits, et peintures appliquees avec l'outil Peindre). Premiere famille qui correspond.
    /// </summary>
    public sealed class FinishSettings
    {
        [JsonPropertyName("enduit")] public PatternList Plaster { get; set; } = new PatternList(new[] { "*enduit*", "*crépi*", "*crepi*", "*plâtre*", "*platre*", "*mortier*", "*plaster*" });
        [JsonPropertyName("peinture")] public PatternList Paint { get; set; } = new PatternList(new[] { "*peinture*", "*paint*", "*badigeon*" });
        [JsonPropertyName("revetementSol")] public PatternList FloorFinish { get; set; } = new PatternList(new[] { "*carrelage*", "*carreau*", "*tile*", "*parquet*", "*granito*", "*chape*", "*revêtement*", "*revetement*" });
        [JsonPropertyName("etancheite")] public PatternList Waterproofing { get; set; } = new PatternList(new[] { "*étanch*", "*etanch*", "*membrane*", "*bitum*", "*waterproof*" });
        [JsonPropertyName("couverture")] public PatternList Roofing { get; set; } = new PatternList(new[] { "*tôle*", "*tole*", "*bac acier*", "*tuile*", "*ardoise*", "*roofing*", "*metal deck*" });
    }

    /// <summary>
    /// Fouilles des fondations : la maquette ne modelise pas les fouilles, le volume est donc
    /// toujours ESTIME a partir de la fondation, d'une surlargeur de travail, d'un talus et de la
    /// cote du terrain naturel (niveau designe par son nom).
    /// </summary>
    public sealed class ExcavationSettings
    {
        /// <summary>Nom du niveau du terrain naturel (ex. « TN »). Absent : fouilles non calculees (quantite manquante).</summary>
        [JsonPropertyName("niveauTerrainNaturel")] public string NaturalGroundLevel { get; set; }

        /// <summary>Decalage du terrain naturel par rapport a ce niveau (m, positif vers le haut).</summary>
        [JsonPropertyName("decalageM")] public double OffsetM { get; set; }

        /// <summary>Surlargeur de travail de chaque cote de la fondation (m).</summary>
        [JsonPropertyName("surlargeurM")] public double WorkingMarginM { get; set; } = 0.30;

        /// <summary>Talus : fruit horizontal par metre de hauteur (0 = parois verticales).</summary>
        [JsonPropertyName("talusHsurV")] public double SlopeHorizontalPerVertical { get; set; }

        /// <summary>La fouille descend aussi sous le beton de proprete.</summary>
        [JsonPropertyName("inclureProprete")] public bool IncludeBlinding { get; set; } = true;
    }

    /// <summary>Beton de proprete sous les fondations : face inferieure x epaisseur, avec debord.</summary>
    public sealed class GeneralEarthworkSettings
    {
        /// <summary>Decapage de terre vegetale sous l'emprise (sols du niveau le plus bas + debord).</summary>
        [JsonPropertyName("decapage")] public bool Stripping { get; set; } = true;
        [JsonPropertyName("decapageEpaisseurM")] public double StrippingThicknessM { get; set; } = 0.20;
        [JsonPropertyName("decapageDebordM")] public double StrippingMarginM { get; set; } = 1.0;
        /// <summary>Fouille en pleine masse sous les sols situes sous le terrain naturel (sous-sols).</summary>
        [JsonPropertyName("pleineMasse")] public bool BulkExcavation { get; set; } = true;
        /// <summary>Remblai des fouilles = fouille - beton enterre - beton de proprete.</summary>
        [JsonPropertyName("remblai")] public bool Backfill { get; set; } = true;
        /// <summary>Evacuation des terres excedentaires, foisonnees.</summary>
        [JsonPropertyName("evacuation")] public bool SpoilRemoval { get; set; } = true;
        [JsonPropertyName("coefficientFoisonnement")] public double BulkingFactor { get; set; } = 1.25;
        /// <summary>La terre vegetale decapee est evacuee (sinon : stockee et reutilisee sur place).</summary>
        [JsonPropertyName("evacuerTerreVegetale")] public bool RemoveTopsoil { get; set; } = true;
    }

    /// <summary>
    /// Fouilles en rigole sous les murs du niveau bas, sur tout leur trace (murs sans semelle filante
    /// modelisee). TOUJOURS ESTIMEES : largeur et profondeur sont des hypotheses.
    /// </summary>
    public sealed class WallTrenchSettings
    {
        [JsonPropertyName("actif")] public bool Enabled { get; set; } = true;
        /// <summary>Profondeur du fond de fouille sous le terrain naturel (ou sous la base du mur sans TN), m.</summary>
        [JsonPropertyName("profondeurM")] public double DepthM { get; set; } = 0.80;
        /// <summary>Surlargeur de chaque cote du mur, m.</summary>
        [JsonPropertyName("surlargeurParCoteM")] public double SideMarginM { get; set; } = 0.20;
        /// <summary>Largeur minimale de la rigole, m.</summary>
        [JsonPropertyName("largeurMinM")] public double MinWidthM { get; set; } = 0.40;
        /// <summary>Murs porteurs seulement (faux : tous les murs du niveau bas, cloisons comprises).</summary>
        [JsonPropertyName("murPorteursSeulement")] public bool StructuralOnly { get; set; }
        /// <summary>Ne pas creuser sous un mur qui porte deja une semelle filante modelisee (sa fouille est comptee avec la semelle).</summary>
        [JsonPropertyName("exclureSiSemelleFilante")] public bool SkipWithWallFoundation { get; set; } = true;
    }

    /// <summary>Decomposition des murs, sols, toits et plafonds composites, couche par couche.</summary>
    public sealed class LayerSettings
    {
        [JsonPropertyName("actif")] public bool Enabled { get; set; } = true;
        /// <summary>Isolants (motifs sur le nom du materiau), en plus des couches de fonction « Isolation ».</summary>
        [JsonPropertyName("motifsIsolation")] public PatternList InsulationMaterials { get; set; } = new PatternList(new[]
        {
            "*isolant*", "*isolation*", "*laine*", "*polystyr*", "*polyurétha*", "*polyuretha*", "*xps*", "*insulation*", "*liège*", "*liege*"
        });
        /// <summary>Couches d'isolation vers le BOQ (article Isolation) ; faux : decomposition seulement.</summary>
        [JsonPropertyName("isolationAuDevis")] public bool InsulationToBoq { get; set; } = true;
    }

    public sealed class VrdSettings
    {
        /// <summary>Classer en VRD les sols et clotures d'apres le nom de leur type (faux : seuls stationnements, plantations et site).</summary>
        [JsonPropertyName("parNomDeType")] public bool ByTypeName { get; set; } = true;
        /// <summary>Types JAMAIS classes en VRD, meme si leur nom correspond (motifs ou noms exacts).</summary>
        [JsonPropertyName("typesExclus")] public PatternList ExcludedTypes { get; set; } = new PatternList(new string[0]);
        /// <summary>Types de sols exterieurs (motifs sur le nom du type).</summary>
        [JsonPropertyName("motifsSols")] public PatternList ExteriorFloorTypes { get; set; } = new PatternList(new[]
        {
            "*voirie*", "*chaussée*", "*chaussee*", "*enrobé*", "*enrobe*", "*pavé*", "*pave*", "*trottoir*", "*parking*", "*allée*", "*allee*"
        });
        /// <summary>Types de garde-corps qui sont des clotures (motifs sur le nom du type).</summary>
        [JsonPropertyName("motifsClotures")] public PatternList FenceTypes { get; set; } = new PatternList(new[]
        {
            "*clôture*", "*cloture*", "*grillage*", "*fence*", "*portail*"
        });
    }

    public sealed class SecondWorkSettings
    {
        [JsonPropertyName("gardeCorps")] public bool Railings { get; set; } = true;
        [JsonPropertyName("gouttieresEtBandeaux")] public bool GuttersAndFascias { get; set; } = true;
        [JsonPropertyName("fauxPlafonds")] public bool Ceilings { get; set; } = true;
        [JsonPropertyName("mursRideaux")] public bool CurtainWalls { get; set; } = true;
        [JsonPropertyName("plinthes")] public bool Skirtings { get; set; } = true;
        /// <summary>Pieces sans plinthes (motifs sur le nom de la piece).</summary>
        [JsonPropertyName("plinthesExclurePieces")] public PatternList SkirtingExcludedRooms { get; set; } =
            new PatternList(new[] { "*gaine*", "*vide*", "*shaft*", "*terrasse*", "*balcon*", "*toiture*" });
    }

    public sealed class PileSettings
    {
        /// <summary>Motifs sur le nom de famille OU de type d'une fondation.</summary>
        [JsonPropertyName("motifs")] public PatternList Patterns { get; set; } = new PatternList(new[] { "*pieu*", "*pile*", "*micropieu*" });

        /// <summary>Exclusions : semelles sur pieux, chevetres, massifs (« pile cap » contient « pile »).</summary>
        [JsonPropertyName("exclure")] public PatternList Exclude { get; set; } =
            new PatternList(new[] { "*cap*", "*chev*tre*", "*semelle*", "*massif*", "*longrine*", "*radier*" });
    }

    public sealed class SlabOnGradeSettings
    {
        /// <summary>Produire le herissonnage (m³) sous les dallages sur terre-plein.</summary>
        [JsonPropertyName("herissonnage")] public bool Hardcore { get; set; } = true;
        [JsonPropertyName("herissonnageEpaisseurM")] public double HardcoreThicknessM { get; set; } = 0.20;
        /// <summary>Produire le film polyane (m²) sous les dallages sur terre-plein.</summary>
        [JsonPropertyName("polyane")] public bool Polythene { get; set; } = true;
        /// <summary>
        /// Considerer aussi comme dallages sur terre-plein les sols du niveau le plus bas (en plus des
        /// types de coffrage.typesSurTerrePlein). Hypothese signalee sur chaque quantite.
        /// </summary>
        [JsonPropertyName("niveauLePlusBas")] public bool LowestLevel { get; set; }
    }

    public sealed class BlindingSettings
    {
        [JsonPropertyName("epaisseurM")] public double ThicknessM { get; set; } = 0.05;
        [JsonPropertyName("debordM")] public double OverhangM { get; set; } = 0.05;
    }

    /// <summary>Conventions de metre du coffrage.</summary>
    public sealed class FormworkSettings
    {
        /// <summary>
        /// Une face dont la pente sur l'horizontale est INFERIEURE a cet angle et qui regarde
        /// vers le haut n'est pas coffree (dessus de dalle, de semelle, de poutre).
        /// </summary>
        [JsonPropertyName("penteMaxDessusNonCoffreDeg")] public double MaxUncoveredTopSlopeDeg { get; set; } = 30.0;

        /// <summary>Sous-faces des fondations (sur sol ou beton de proprete) : non coffrees.</summary>
        [JsonPropertyName("exclureSousFacesFondations")] public bool ExcludeFoundationBottoms { get; set; } = true;

        /// <summary>Fondations coulees pleine fouille : leurs faces laterales ne sont pas coffrees non plus.</summary>
        [JsonPropertyName("fondationsPleineFouille")] public bool FoundationsCastAgainstSoil { get; set; }

        /// <summary>Types de sols poses sur terre-plein (motifs) : sous-face non coffree.</summary>
        [JsonPropertyName("typesSurTerrePlein")]
        public PatternList SlabOnGradeTypes { get; set; } = new PatternList(new[] { "*terre*plein*", "*dallage*" });

        /// <summary>Deduire les surfaces en contact avec un autre element (poutre contre poteau, voile sous dalle...).</summary>
        [JsonPropertyName("deduireContacts")] public bool DeductContacts { get; set; } = true;
    }

    /// <summary>Ratio d'armatures pour une categorie Revit (motif sur le nom BuiltInCategory).</summary>
    public sealed class RebarRatio
    {
        [JsonPropertyName("categorie")] public PatternList Category { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("kgParM3")] public decimal KgPerM3 { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }
        [JsonPropertyName("aValider")] public bool NeedsValidation { get; set; } = true;
    }

    /// <summary>Conventions de metre de la charpente metallique.</summary>
    public sealed class SteelSettings
    {
        [JsonPropertyName("masseVolumiqueKgM3")] public decimal DensityKgPerM3 { get; set; } = 7850m;

        /// <summary>Assemblages et boulonnerie, en part de la masse des profiles (0,10 = 10 %). 0 : pas de ligne.</summary>
        [JsonPropertyName("assemblagesPart")] public decimal ConnectionsShare { get; set; }

        [JsonPropertyName("assemblagesSource")] public string ConnectionsSource { get; set; }
    }
}
