using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Reference;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Armatures MODELISEES : jeux de barres (Rebar), barres des armatures surfaciques et
    /// lineaires (RebarInSystem) et treillis soudes (FabricSheet).
    /// Barres : masse = longueur totale du jeu (Rebar.TotalLength, toutes barres comprises) x
    /// masse lineique HA du diametre nominal. Treillis : masse du type de panneau.
    /// Les hotes des armatures sont notes : un element qui porte au moins une armature n'est
    /// jamais estime par ratio.
    /// </summary>
    internal sealed class RebarExtractor
    {
        public const string Name = "Armatures";

        private readonly ElementDescriber describer;

        public RebarExtractor(ElementDescriber describer)
        {
            this.describer = describer;
        }

        /// <summary>Cles (document|ElementId) des elements qui portent au moins une armature.</summary>
        public HashSet<string> Hosts { get; } = new HashSet<string>(StringComparer.Ordinal);

        public void Extract(ExtractionReport report)
        {
            Document doc = describer.Document;

            foreach (Rebar r in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType())
            {
                if (Skip(r, report)) continue;
                Try(r, report, () => AddBars(r, r.TotalLength, r.GetHostId(), r.Quantity, report));
            }
            foreach (RebarInSystem r in new FilteredElementCollector(doc).OfClass(typeof(RebarInSystem)).WhereElementIsNotElementType())
            {
                if (Skip(r, report)) continue;
                Try(r, report, () => AddBars(r, r.TotalLength, r.GetHostId(), null, report));
            }
            foreach (FabricSheet f in new FilteredElementCollector(doc).OfClass(typeof(FabricSheet)).WhereElementIsNotElementType())
            {
                if (Skip(f, report)) continue;
                Try(f, report, () => AddFabric(f, report));
            }
        }

        private bool Skip(Element e, ExtractionReport report)
        {
            report.Count(report.ElementsScanned, Name);
            if (describer.ShouldSkip(e, out string reason))
            {
                report.Count(report.Skipped, reason);
                return true;
            }
            return false;
        }

        private static void Try(Element e, ExtractionReport report, Action action)
        {
            try
            {
                action();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC",
                    "Armature " + e.Id.Value + " illisible : " + ex.Message, e.Id.Value);
            }
        }

        private void AddBars(Element bar, double totalLengthInternal, ElementId hostId, int? count, ExtractionReport report)
        {
            Document doc = describer.Document;
            var barType = doc.GetElement(bar.GetTypeId()) as RebarBarType;
            double lengthM = RevitUnits.ToMeters(totalLengthInternal);
            int diameterMm = barType != null ? (int)Math.Round(RevitUnits.ToMillimeters(barType.BarNominalDiameter)) : 0;

            ElementQuantity q = Describe(bar, hostId);
            q.Material = barType?.Name ?? string.Empty;
            q.Parameters["DiametreMm"] = diameterMm.ToString(CultureInfo.InvariantCulture);
            q.Parameters["LongueurTotaleM"] = lengthM.ToString("0.###", CultureInfo.InvariantCulture);
            if (count.HasValue)
            {
                q.Parameters["NbBarres"] = count.Value.ToString(CultureInfo.InvariantCulture);
            }

            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (RebarMass.TryGet(diameterMm, out decimal kgm))
            {
                q.Value = lengthM * (double)kgm;
                q.MethodCode = "TOTAL_LENGTH_X_HA_MASS";
                q.MethodDescription = "Longueur totale du jeu " + lengthM.ToString("0.###", fr) + " m × " +
                                      kgm.ToString("0.###", fr) + " kg/m (HA" + diameterMm + ").";
            }
            else if (diameterMm > 0)
            {
                double theoretical = Math.PI * Math.Pow(diameterMm / 1000.0, 2) / 4.0 * 7850.0;
                q.Value = lengthM * theoretical;
                q.MethodCode = "TOTAL_LENGTH_X_THEORETICAL_MASS";
                q.MethodDescription = "Longueur totale × masse théorique πd²/4 × 7850.";
                q.Reliability = Reliability.Estimated;
                q.Assumption = "Diamètre " + diameterMm + " mm absent de la table HA6–HA40 : masse théorique " +
                               theoretical.ToString("0.###", fr) + " kg/m.";
            }
            else
            {
                q.Value = 0;
                q.MethodCode = "NONE";
                q.MethodDescription = "Diamètre de barre illisible.";
                q.Reliability = Reliability.Missing;
                q.Assumption = "Masse manquante : type de barre sans diamètre lisible.";
            }
            report.Quantities.Add(q);
        }

        private void AddFabric(FabricSheet sheet, ExtractionReport report)
        {
            Document doc = describer.Document;
            var type = doc.GetElement(sheet.GetTypeId()) as FabricSheetType;
            ElementQuantity q = Describe(sheet, sheet.HostId);
            q.Material = type?.Name ?? string.Empty;
            double mass = type != null ? UnitUtils.ConvertFromInternalUnits(type.SheetMass, UnitTypeId.Kilograms) : 0;
            if (mass > 0)
            {
                q.Value = mass;
                q.MethodCode = "FABRIC_SHEET_MASS";
                q.MethodDescription = "Masse du panneau de treillis (FabricSheetType.SheetMass).";
            }
            else
            {
                q.Value = 0;
                q.MethodCode = "NONE";
                q.MethodDescription = "Masse du panneau de treillis illisible.";
                q.Reliability = Reliability.Missing;
                q.Assumption = "Masse du treillis manquante : renseigner la masse du type de panneau.";
            }
            report.Quantities.Add(q);
        }

        private ElementQuantity Describe(Element rebar, ElementId hostId)
        {
            ElementQuantity q = describer.Describe(rebar);
            q.Kind = QuantityKinds.Reinforcement;
            q.Unit = BoqUnit.Kilogram;
            Element host = hostId != null && hostId != ElementId.InvalidElementId ? describer.Document.GetElement(hostId) : null;
            if (host != null)
            {
                q.Parameters["HoteCategorie"] = ElementDescriber.CategoryName(host.Category);
                q.Parameters["HoteId"] = host.Id.Value.ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(q.Level))
                {
                    q.Level = describer.Describe(host).Level;
                }
                Hosts.Add(RebarRatioRule.HostKey(q.SourceDocument, host.Id.Value));
            }
            else
            {
                q.Parameters["HoteCategorie"] = string.Empty;
            }
            return q;
        }
    }
}
