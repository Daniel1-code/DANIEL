using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        private readonly IProgressSink progress;

        public RebarExtractor(ElementDescriber describer, IProgressSink progress)
        {
            this.describer = describer;
            this.progress = progress ?? NoProgress.Instance;
        }

        /// <summary>Cles (document|ElementId) des elements qui portent au moins une armature.</summary>
        public HashSet<string> Hosts { get; } = new HashSet<string>(StringComparer.Ordinal);

        public void Extract(ExtractionReport report)
        {
            Document doc = describer.Document;

            List<Element> all = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Concat(new FilteredElementCollector(doc).OfClass(typeof(RebarInSystem)).WhereElementIsNotElementType())
                .Concat(new FilteredElementCollector(doc).OfClass(typeof(FabricSheet)).WhereElementIsNotElementType())
                .ToList();

            int index = 0;
            foreach (Element e in all)
            {
                progress.Step("Armatures", ++index, all.Count);
                switch (e)
                {
                    case Rebar r:
                        if (!Skip(r, r.GetHostId(), report)) Try(r, report, () => AddBars(r, r.TotalLength, r.GetHostId(), r.Quantity, report));
                        break;
                    case RebarInSystem ris:
                        if (!Skip(ris, ris.GetHostId(), report)) Try(ris, report, () => AddBars(ris, ris.TotalLength, ris.GetHostId(), null, report));
                        break;
                    case FabricSheet f:
                        if (!Skip(f, f.HostId, report)) Try(f, report, () => AddFabric(f, report));
                        break;
                }
            }
        }

        /// <summary>Une armature est dans la portee si elle y est elle-meme, ou si son hote y est.</summary>
        private bool Skip(Element e, ElementId hostId, ExtractionReport report)
        {
            report.Count(report.ElementsScanned, Name);
            if (ElementDescriber.IsSecondaryOption(e))
            {
                report.Count(report.Skipped, "Option de conception secondaire");
                return true;
            }
            Element host = hostId != null && hostId != ElementId.InvalidElementId ? describer.Document.GetElement(hostId) : null;
            if (!describer.InScope(e) && !describer.InScope(host))
            {
                report.Count(report.Skipped, "Hors de la portée choisie");
                return true;
            }
            if (!describer.PhaseAllows(e, out string status, out string reason))
            {
                report.Count(report.Skipped, "Phase : " + reason);
                return true;
            }
            if (status == PhaseStatus.Demolished)
            {
                report.Count(report.Skipped, "Armature d'un ouvrage démoli");
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
