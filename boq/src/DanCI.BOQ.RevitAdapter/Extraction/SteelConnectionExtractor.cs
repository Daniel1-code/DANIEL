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
    /// Assemblages de charpente metallique MODELISES (lecture seule) :
    /// 1. assemblages acier Revit (categorie Assemblages structurels, StructuralConnectionHandler) :
    ///    un par type (U), masse = volume de leur geometrie x masse volumique de l'acier, profiles relies ;
    /// 2. pieces d'assemblage creees dans Revit : platines, profiles d'assemblage, goujons (masse) ;
    ///    boulons, tiges d'ancrage, goujons (comptes par type) ; soudures (comptees) ;
    /// 3. familles d'assemblage reconnues par leur NOM (platine, gousset, jarret, eclisse, raidisseur,
    ///    boulon, tige d'ancrage...) dans les categories choisies : nombre et masse.
    /// Les profiles reconnus ici ne sont pas comptes une seconde fois comme charpente.
    /// </summary>
    internal sealed class SteelConnectionExtractor
    {
        public const string Name = "Assemblages métalliques";

        private static readonly BuiltInCategory[] HandlerCategories = { BuiltInCategory.OST_StructConnections };

        /// <summary>Pieces pesees (masse = volume x 7 850 kg/m³).</summary>
        private static readonly BuiltInCategory[] MassParts =
        {
            BuiltInCategory.OST_StructConnectionPlates, BuiltInCategory.OST_StructConnectionProfiles, BuiltInCategory.OST_StructConnectionOthers
        };

        /// <summary>Boulonnerie comptee par type.</summary>
        private static readonly BuiltInCategory[] Fasteners =
        {
            BuiltInCategory.OST_StructConnectionBolts, BuiltInCategory.OST_StructConnectionAnchors, BuiltInCategory.OST_StructConnectionShearStuds
        };

        private readonly ElementDescriber describer;
        private readonly SteelSettings settings;
        private readonly IProgressSink progress;
        private readonly HashSet<long> done = new HashSet<long>();

        public SteelConnectionExtractor(ElementDescriber describer, SteelSettings settings, IProgressSink progress)
        {
            this.describer = describer;
            this.settings = settings ?? new SteelSettings();
            this.progress = progress ?? NoProgress.Instance;
        }

        /// <summary>Famille ou type reconnu comme piece d'assemblage par son nom.</summary>
        public static bool IsConnectionFamily(Element e, SteelSettings s)
        {
            if (s == null || !s.ModeledConnections || s.ConnectionPatterns == null || s.ConnectionPatterns.IsEmpty) return false;
            // Un element declare en beton (ou bois, maconnerie...) n'est pas un assemblage acier, meme si son nom
            // contient « pied de poteau », « about »... : il reste compte dans son lot.
            if (e is FamilyInstance fi && fi.StructuralMaterialType != StructuralMaterialType.Steel &&
                fi.StructuralMaterialType != StructuralMaterialType.Undefined && fi.StructuralMaterialType != StructuralMaterialType.Other)
            {
                return false;
            }
            string family = (e as FamilyInstance)?.Symbol?.FamilyName;
            string type = e.Document.GetElement(e.GetTypeId())?.Name;
            return (!string.IsNullOrEmpty(family) && s.ConnectionPatterns.IsMatch(family)) || (!string.IsNullOrEmpty(type) && s.ConnectionPatterns.IsMatch(type));
        }

        public void Extract(ExtractionReport report)
        {
            if (!settings.ModeledConnections) return;
            Document doc = describer.Document;

            // 1. Assemblages acier Revit
            foreach (Element e in Collect(doc, HandlerCategories))
            {
                if (!Accept(e, report)) continue;
                string connected = null;
                if (e is StructuralConnectionHandler h)
                {
                    try
                    {
                        connected = string.Join(",", h.GetConnectedElementIds().Select(i => i.Value.ToString(CultureInfo.InvariantCulture)));
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        connected = null;
                    }
                }
                AddCount(report, e, SteelConnectionRules.Connection, "CONNECTION_COUNT", "Assemblage modélisé (1 par occurrence), compté par type.", connected);
                AddMass(report, e, SteelConnectionRules.Connection, "Masse de l'assemblage : volume de sa géométrie (platines, boulons, soudures dessinés) × ", connected);
            }

            // 2. Pieces d'assemblage
            foreach (Element e in Collect(doc, MassParts))
            {
                if (!Accept(e, report)) continue;
                AddCount(report, e, SteelConnectionRules.Part, "CONNECTION_PART_COUNT", "Pièce d'assemblage modélisée (1 par occurrence).", null);
                AddMass(report, e, SteelConnectionRules.Part, "Masse de la pièce : volume de sa géométrie × ", null);
            }
            foreach (Element e in Collect(doc, Fasteners))
            {
                if (!Accept(e, report)) continue;
                AddCount(report, e, SteelConnectionRules.Fastener, "FASTENER_COUNT", "Boulon / tige d'ancrage / goujon modélisé (1 par occurrence), compté par type.", null);
            }
            foreach (Element e in Collect(doc, new[] { BuiltInCategory.OST_StructConnectionWelds }))
            {
                if (!Accept(e, report)) continue;
                AddCount(report, e, SteelConnectionRules.Part, "WELD_COUNT", "Soudure modélisée (1 par occurrence).", null);
            }

            // 3. Familles d'assemblage reconnues par leur nom
            var categories = (settings.ConnectionCategories ?? new List<string>())
                .Select(c => Enum.TryParse(c, out BuiltInCategory b) ? b : (BuiltInCategory?)null)
                .Where(b => b.HasValue).Select(b => b.Value).ToArray();
            if (categories.Length > 0)
            {
                foreach (Element e in Collect(doc, categories).OfType<FamilyInstance>())
                {
                    if (done.Contains(e.Id.Value) || !IsConnectionFamily(e, settings)) continue;
                    if (!Accept(e, report)) continue;
                    bool fastener = Fastener(e);
                    AddCount(report, e, fastener ? SteelConnectionRules.Fastener : SteelConnectionRules.Connection,
                        "CONNECTION_FAMILY_COUNT", "Famille d'assemblage reconnue par son nom (extraction.json, acier.motifsAssemblages), 1 par occurrence.", null);
                    if (!fastener) AddMass(report, e, SteelConnectionRules.Connection, "Masse de la famille d'assemblage : volume de sa géométrie × ", null);
                }
            }
        }

        private static bool Fastener(Element e)
        {
            string n = (((e as FamilyInstance)?.Symbol?.FamilyName ?? string.Empty) + " " + (e.Document.GetElement(e.GetTypeId())?.Name ?? string.Empty)).ToLowerInvariant();
            return n.Contains("boulon") || n.Contains("bolt") || n.Contains("tige") || n.Contains("anchor") || n.Contains("goujon") || n.Contains("stud");
        }

        private static IEnumerable<Element> Collect(Document doc, BuiltInCategory[] categories)
        {
            try
            {
                return new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(categories)).WhereElementIsNotElementType().ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return Enumerable.Empty<Element>();
            }
        }

        private bool Accept(Element e, ExtractionReport report)
        {
            if (!done.Add(e.Id.Value)) return false;
            progress.Step(Name, done.Count, done.Count);
            report.Count(report.ElementsScanned, Name);
            if (describer.ShouldSkip(e, out string reason))
            {
                report.Count(report.Skipped, reason);
                return false;
            }
            return !(describer.PhaseAllows(e, out string status, out _) && status == PhaseStatus.Demolished);
        }

        private void AddCount(ExtractionReport report, Element e, string kind, string method, string description, string connected)
        {
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Count;
            q.Unit = BoqUnit.Each;
            q.Value = 1;
            q.MethodCode = method;
            q.MethodDescription = description;
            q.Parameters[SteelConnectionRules.Parameter] = kind;
            if (!string.IsNullOrEmpty(connected)) q.Parameters[SteelConnectionRules.ConnectedParameter] = connected;
            report.Quantities.Add(q);
        }

        private void AddMass(ExtractionReport report, Element e, string kind, string description, string connected)
        {
            double m3 = 0;
            try
            {
                m3 = RevitUnits.ToCubicMeters(ConcreteExtractor.SolidVolume(e));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                m3 = 0;
            }
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.SteelConnections;
            q.Unit = BoqUnit.Kilogram;
            q.Parameters[SteelConnectionRules.Parameter] = kind;
            if (!string.IsNullOrEmpty(connected)) q.Parameters[SteelConnectionRules.ConnectedParameter] = connected;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (m3 <= 1e-9)
            {
                // Assemblage « generique » sans detail (symbole) : compte, mais masse inconnue — rien n'est invente.
                q.Value = 0;
                q.Reliability = Reliability.Missing;
                q.MethodCode = "CONNECTION_NO_GEOMETRY";
                q.MethodDescription = "Assemblage sans géométrie détaillée (symbole) : masse non mesurable.";
                q.Assumption = "MANQUANT : détaillez l'assemblage dans Revit (platines, boulons) ou saisissez un forfait.";
                report.Quantities.Add(q);
                return;
            }
            double kg = m3 * (double)settings.DensityKgPerM3;
            q.Value = kg;
            q.MethodCode = "CONNECTION_SOLID_MASS";
            q.MethodDescription = description + settings.DensityKgPerM3.ToString("0", fr) + " kg/m³ (" + m3.ToString("0.#####", fr) + " m³).";
            report.Quantities.Add(q);
        }
    }
}
