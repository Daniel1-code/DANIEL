using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Reseaux (plomberie, electricite, ventilation) et familles generiques :
    ///  - canalisations, conduits, chemins de cables, gaines : longueur (ml) avec la section en
    ///    parametre « Section » (« Ø 110 », « 300 × 100 »), pour une ligne par section ;
    ///  - appareils (sanitaires, appareillage, luminaires, equipements) : comptage par instance ;
    ///  - modeles generiques et equipements specialises : comptage ET volume ; sans regle de
    ///    mapping, ils remontent en « Non classés », visibles.
    /// Raccords et accessoires de reseau : non comptes (voir README).
    /// </summary>
    internal sealed class NetworkExtractor
    {
        public const string Name = "Réseaux et génériques";

        private static readonly BuiltInCategory[] Curves =
        {
            BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray, BuiltInCategory.OST_DuctCurves
        };

        private static readonly BuiltInCategory[] Devices =
        {
            BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_MechanicalEquipment
        };

        private static readonly BuiltInCategory[] Generic =
        {
            BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment
        };

        private readonly ElementDescriber describer;
        private readonly IProgressSink progress;

        public NetworkExtractor(ElementDescriber describer, IProgressSink progress)
        {
            this.describer = describer;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            Document doc = describer.Document;
            List<Element> all = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(Curves.Concat(Devices).Concat(Generic).ToList()))
                .WhereElementIsNotElementType()
                .ToList();
            int index = 0;
            foreach (Element e in all)
            {
                progress.Step(Name, ++index, all.Count);
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(e, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                string category = ElementDescriber.CategoryName(e.Category);
                try
                {
                    if (Curves.Any(c => c.ToString() == category))
                    {
                        AddLength(e, report);
                    }
                    else if (e is FamilyInstance fi && fi.SuperComponent == null)
                    {
                        // Sous-composants partages : comptes par leur famille hote, pas en double.
                        AddCount(e, report);
                        if (Generic.Any(c => c.ToString() == category))
                        {
                            AddVolume(e, report);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC", "Élément " + e.Id.Value + " illisible : " + ex.Message, e.Id.Value);
                }
            }
        }

        private void AddLength(Element e, ExtractionReport report)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (p == null || !p.HasValue || p.AsDouble() <= 0)
            {
                return;
            }
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Length;
            q.Unit = BoqUnit.LinearMeter;
            q.Value = RevitUnits.ToMeters(p.AsDouble());
            q.MethodCode = "CURVE_LENGTH";
            q.MethodDescription = "Longueur de l'élément linéaire (raccords non compris).";
            q.Parameters["Section"] = Section(e);
            report.Quantities.Add(q);
        }

        private static string Section(Element e)
        {
            double? Mm(BuiltInParameter bip)
            {
                Parameter p = e.get_Parameter(bip);
                return p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0
                    ? Math.Round(RevitUnits.ToMillimeters(p.AsDouble()))
                    : (double?)null;
            }
            double? d = Mm(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ?? Mm(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM) ?? Mm(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            if (d.HasValue)
            {
                return "Ø " + d.Value.ToString(CultureInfo.InvariantCulture);
            }
            double? w = Mm(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM) ?? Mm(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            double? h = Mm(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM) ?? Mm(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            return w.HasValue && h.HasValue
                ? w.Value.ToString(CultureInfo.InvariantCulture) + " × " + h.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private void AddCount(Element e, ExtractionReport report)
        {
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Count;
            q.Unit = BoqUnit.Each;
            q.Value = 1;
            q.MethodCode = "COUNT";
            q.MethodDescription = "Comptage de l'instance.";
            report.Quantities.Add(q);
        }

        private void AddVolume(Element e, ExtractionReport report)
        {
            double v = RevitUnits.ToCubicMeters(ConcreteExtractor.SolidVolume(e));
            if (v <= 1e-9)
            {
                return;
            }
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Volume;
            q.Unit = BoqUnit.CubicMeter;
            q.Value = v;
            q.MethodCode = "SOLID_VOLUME";
            q.MethodDescription = "Somme des volumes des solides de la famille.";
            report.Quantities.Add(q);
        }
    }
}
