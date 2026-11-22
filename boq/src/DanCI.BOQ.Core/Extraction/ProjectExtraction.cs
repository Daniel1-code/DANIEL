using System.Text.Json.Serialization;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Reglages du metre propres a un projet (Parametres du projet > Reglages du metre). Chaque
    /// valeur absente (null) garde celle de l'installation (config\extraction.json). Ils agissent
    /// a l'EXTRACTION : une nouvelle analyse est necessaire pour les voir.
    /// </summary>
    public sealed class ProjectExtraction
    {
        [JsonPropertyName("niveauTerrainNaturel")] public string NaturalGroundLevel { get; set; }
        [JsonPropertyName("decalageTerrainNaturelM")] public double? NaturalGroundOffsetM { get; set; }
        [JsonPropertyName("coefficientFoisonnement")] public double? BulkingFactor { get; set; }
        [JsonPropertyName("decapage")] public bool? Stripping { get; set; }
        [JsonPropertyName("decapageEpaisseurM")] public double? StrippingThicknessM { get; set; }
        [JsonPropertyName("pleineMasse")] public bool? BulkExcavation { get; set; }
        [JsonPropertyName("herissonnageEpaisseurM")] public double? HardcoreThicknessM { get; set; }
        [JsonPropertyName("dallagesNiveauLePlusBas")] public bool? SlabsOnLowestLevel { get; set; }
        [JsonPropertyName("gardeCorps")] public bool? Railings { get; set; }
        [JsonPropertyName("gouttieresEtBandeaux")] public bool? GuttersAndFascias { get; set; }
        [JsonPropertyName("fauxPlafonds")] public bool? Ceilings { get; set; }
        [JsonPropertyName("mursRideaux")] public bool? CurtainWalls { get; set; }
        [JsonPropertyName("plinthes")] public bool? Skirtings { get; set; }

        /// <summary>Applique les valeurs renseignees sur les reglages de l'installation (modifies en place).</summary>
        public void ApplyTo(ExtractionSettings s)
        {
            if (s == null) return;
            s.Excavation = s.Excavation ?? new ExcavationSettings();
            s.GeneralEarthworks = s.GeneralEarthworks ?? new GeneralEarthworkSettings();
            s.SlabsOnGrade = s.SlabsOnGrade ?? new SlabOnGradeSettings();
            s.SecondWork = s.SecondWork ?? new SecondWorkSettings();
            if (NaturalGroundLevel != null) s.Excavation.NaturalGroundLevel = NaturalGroundLevel.Length == 0 ? null : NaturalGroundLevel;
            if (NaturalGroundOffsetM.HasValue) s.Excavation.OffsetM = NaturalGroundOffsetM.Value;
            if (BulkingFactor.HasValue) s.GeneralEarthworks.BulkingFactor = BulkingFactor.Value;
            if (Stripping.HasValue) s.GeneralEarthworks.Stripping = Stripping.Value;
            if (StrippingThicknessM.HasValue) s.GeneralEarthworks.StrippingThicknessM = StrippingThicknessM.Value;
            if (BulkExcavation.HasValue) s.GeneralEarthworks.BulkExcavation = BulkExcavation.Value;
            if (HardcoreThicknessM.HasValue) s.SlabsOnGrade.HardcoreThicknessM = HardcoreThicknessM.Value;
            if (SlabsOnLowestLevel.HasValue) s.SlabsOnGrade.LowestLevel = SlabsOnLowestLevel.Value;
            if (Railings.HasValue) s.SecondWork.Railings = Railings.Value;
            if (GuttersAndFascias.HasValue) s.SecondWork.GuttersAndFascias = GuttersAndFascias.Value;
            if (Ceilings.HasValue) s.SecondWork.Ceilings = Ceilings.Value;
            if (CurtainWalls.HasValue) s.SecondWork.CurtainWalls = CurtainWalls.Value;
            if (Skirtings.HasValue) s.SecondWork.Skirtings = Skirtings.Value;
        }

        /// <summary>Saisie de depart : les valeurs de l'installation.</summary>
        public static ProjectExtraction From(ExtractionSettings s)
        {
            s = s ?? new ExtractionSettings();
            return new ProjectExtraction
            {
                NaturalGroundLevel = s.Excavation?.NaturalGroundLevel ?? string.Empty,
                NaturalGroundOffsetM = s.Excavation?.OffsetM ?? 0,
                BulkingFactor = s.GeneralEarthworks?.BulkingFactor ?? 1.25,
                Stripping = s.GeneralEarthworks?.Stripping ?? true,
                StrippingThicknessM = s.GeneralEarthworks?.StrippingThicknessM ?? 0.20,
                BulkExcavation = s.GeneralEarthworks?.BulkExcavation ?? true,
                HardcoreThicknessM = s.SlabsOnGrade?.HardcoreThicknessM ?? 0.20,
                SlabsOnLowestLevel = s.SlabsOnGrade?.LowestLevel ?? false,
                Railings = s.SecondWork?.Railings ?? true,
                GuttersAndFascias = s.SecondWork?.GuttersAndFascias ?? true,
                Ceilings = s.SecondWork?.Ceilings ?? true,
                CurtainWalls = s.SecondWork?.CurtainWalls ?? true,
                Skirtings = s.SecondWork?.Skirtings ?? true
            };
        }
    }
}
