using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Charpente metallique : ossatures (poutres, contreventements) et poteaux porteurs dont la
    /// famille se declare en acier (StructuralMaterialType.Steel). Masse par SteelMassRule ;
    /// assemblages en part de la masse, toujours ESTIMES.
    /// </summary>
    internal sealed class SteelExtractor
    {
        public const string Name = "Charpente métallique";

        private static readonly string[] NominalWeightNames = { "Poids nominal", "Nominal Weight", "Masse linéique", "Masse lineique" };

        private readonly ElementDescriber describer;
        private readonly SteelProfileTable table;
        private readonly SteelSettings settings;

        private readonly IProgressSink progress;

        public SteelExtractor(ElementDescriber describer, SteelProfileTable table, SteelSettings settings, IProgressSink progress)
        {
            this.describer = describer;
            this.table = table;
            this.settings = settings ?? new SteelSettings();
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            List<FamilyInstance> elements = new FilteredElementCollector(describer.Document)
                .WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns }))
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            int index = 0;
            foreach (FamilyInstance fi in elements)
            {
                progress.Step("Charpente métallique", ++index, elements.Count);
                if (fi.StructuralMaterialType != StructuralMaterialType.Steel)
                {
                    continue;
                }
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(fi, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                try
                {
                    ExtractElement(fi, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC", "Profilé " + fi.Id.Value + " illisible : " + ex.Message, fi.Id.Value);
                }
            }
        }

        private void ExtractElement(FamilyInstance fi, ExtractionReport report)
        {
            ElementType type = describer.Document.GetElement(fi.GetTypeId()) as ElementType;
            double? length = CutLength(fi);
            double? typeKgPerM = NominalWeight(type, out string weightSource);
            double volume = RevitUnits.ToCubicMeters(ConcreteExtractor.SolidVolume(fi));

            SteelMass mass = SteelMassRule.Resolve(type?.Name, typeKgPerM, length, volume, table, settings);

            ElementQuantity q = describer.Describe(fi);
            q.Kind = QuantityKinds.StructuralSteel;
            q.Unit = BoqUnit.Kilogram;
            q.Value = mass.Kg;
            q.Material = describer.Name(fi.StructuralMaterialId);
            q.MethodCode = mass.MethodCode;
            q.MethodDescription = mass.MethodDescription + (weightSource != null && mass.MethodCode == "CUT_LENGTH_X_TYPE_MASS" ? " " + weightSource : string.Empty);
            q.Reliability = mass.Reliability;
            q.Assumption = mass.Assumption;
            var inv = CultureInfo.InvariantCulture;
            q.Parameters["LongueurCoupeM"] = length?.ToString("0.###", inv) ?? string.Empty;
            q.Parameters["VolumeM3"] = volume.ToString("0.######", inv);
            q.Parameters["Profil"] = SteelProfileName.Normalize(type?.Name) ?? string.Empty;
            report.Quantities.Add(q);

            bool demolished = q.Parameters.TryGetValue(PhaseStatus.Parameter, out string st) && st == PhaseStatus.Demolished;
            if (settings.ConnectionsShare > 0 && mass.Kg > 0 && !demolished)
            {
                ElementQuantity c = describer.Describe(fi);
                c.Kind = QuantityKinds.SteelConnections;
                c.Unit = BoqUnit.Kilogram;
                c.Value = mass.Kg * (double)settings.ConnectionsShare;
                c.Material = q.Material;
                c.MethodCode = "SHARE_OF_STEEL_MASS";
                c.MethodDescription = "Part de la masse du profilé.";
                c.Reliability = Reliability.Estimated;
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                c.Assumption = "ESTIMÉ : assemblages et boulonnerie = " + (settings.ConnectionsShare * 100m).ToString("0.#", fr) +
                               " % de la masse du profilé" + (string.IsNullOrWhiteSpace(settings.ConnectionsSource) ? "." : " (" + settings.ConnectionsSource + ").");
                report.Quantities.Add(c);
            }
        }

        private static double? CutLength(FamilyInstance fi)
        {
            foreach (BuiltInParameter bip in new[] { BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH, BuiltInParameter.INSTANCE_LENGTH_PARAM })
            {
                Parameter p = fi.get_Parameter(bip);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-9)
                {
                    return RevitUnits.ToMeters(p.AsDouble());
                }
            }
            if (fi.Location is LocationCurve lc && lc.Curve != null)
            {
                return RevitUnits.ToMeters(lc.Curve.Length);
            }
            return null;
        }

        /// <summary>Masse lineique lue dans le type, en kg/m : masse par unite de longueur, ou poids (kN/m / g).</summary>
        private static double? NominalWeight(ElementType type, out string source)
        {
            source = null;
            if (type == null)
            {
                return null;
            }
            var candidates = new List<Parameter> { type.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_NOMINAL_WEIGHT) };
            foreach (string name in NominalWeightNames)
            {
                candidates.Add(type.LookupParameter(name));
            }
            foreach (Parameter p in candidates)
            {
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double || p.AsDouble() <= 0)
                {
                    continue;
                }
                ForgeTypeId spec = p.Definition.GetDataType();
                if (spec == SpecTypeId.MassPerUnitLength)
                {
                    source = "(paramètre « " + p.Definition.Name + " »)";
                    return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.KilogramsPerMeter);
                }
                if (spec == SpecTypeId.LinearForce)
                {
                    // Poids lineique : masse = poids / g (g = 9,80665 m/s²).
                    source = "(paramètre « " + p.Definition.Name + " », poids converti en masse avec g = 9,80665 m/s²)";
                    double kNperM = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.KilonewtonsPerMeter);
                    return kNperM * 1000.0 / 9.80665;
                }
            }
            return null;
        }
    }
}
