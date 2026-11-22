using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Audit
{
    /// <summary>
    /// Audit prealable (§6 du cahier des charges). Rassemble :
    ///  - les constats calcules ici sur les quantites (beton sans classe, ratios, profils, doublons
    ///    de position, ferraillage incomplet) ;
    ///  - les constats releves par l'adaptateur Revit sur le modele (materiaux par defaut, pieces,
    ///    murs, liens, avertissements de doublons, chevauchements) — fournis comme BoqIssue ;
    ///  - les anomalies du moteur (non classes, quantites manquantes, prix...).
    /// </summary>
    public static class BoqAuditor
    {
        /// <summary>Codes des constats de modele et leur rubrique.</summary>
        private static readonly Dictionary<string, AuditSection> FactSections = new Dictionary<string, AuditSection>(StringComparer.Ordinal)
        {
            { "SANS_MATERIAU", AuditSection.Materiaux },
            { "ELEMENT_GENERIQUE_SANS_MATERIAU", AuditSection.Materiaux },
            { "ELEMENT_GENERIQUE_MATERIAU_PAR_CATEGORIE", AuditSection.Materiaux },
            { "ELEMENT_GENERIQUE_MATERIAU_GENERIQUE", AuditSection.Materiaux },
            { "ELEMENT_GENERIQUE_TYPE_GENERIQUE", AuditSection.Materiaux },
            { "ELEMENT_GENERIQUE_MODELE_GENERIQUE", AuditSection.Materiaux },
            { "MATERIAU_PAR_DEFAUT", AuditSection.Materiaux },
            { "PIECE_NON_PLACEE", AuditSection.PiecesEtMurs },
            { "PIECE_NON_FERMEE", AuditSection.PiecesEtMurs },
            { "MUR_SANS_HAUTEUR_LIEE", AuditSection.PiecesEtMurs },
            { "DOUBLON_REVIT", AuditSection.Doublons },
            { "CHEVAUCHEMENT_NON_JOINT", AuditSection.Doublons },
            { "LIEN_DECHARGE", AuditSection.Liens },
            { "NON_CLASSE", AuditSection.NonClasses }
        };

        /// <summary>
        /// Seuil du ferraillage incomplet : un element ferraille dans Revit dont le ratio modelise est
        /// inferieur a cette fraction du ratio de sa categorie est signale (attentes seules, par exemple).
        /// </summary>
        public const decimal PartialRebarFraction = 0.25m;

        public static AuditReport Run(
            IReadOnlyList<ElementQuantity> quantities,
            IEnumerable<BoqIssue> modelFacts,
            BoqResult boq,
            ExtractionSettings extraction)
        {
            var report = new AuditReport { ReliabilityIndex = boq?.ReliabilityIndex };
            quantities = quantities ?? new List<ElementQuantity>();
            var fr = CultureInfo.GetCultureInfo("fr-FR");

            // 2. Beton sans classe.
            foreach (ElementQuantity q in quantities.Where(q => q.Kind == QuantityKinds.ConcreteVolume && string.IsNullOrEmpty(Param(q, "ClasseBeton"))))
            {
                Add(report, AuditSection.BetonSansClasse, IssueSeverity.Warning, "BETON_SANS_CLASSE",
                    "Élément " + q.ElementId + " (" + q.TypeName + ", matériau « " + q.Material + " », " +
                    q.Value.ToString("0.###", fr) + " m³) : aucune classe Cxx/yy dans le nom du matériau ni du type. " +
                    "Il ne sera rattaché à aucun article de béton classé.", q);
            }

            // 3. Armatures : ratio, manquantes, ferraillage incomplet.
            foreach (ElementQuantity q in quantities.Where(q => q.Kind == QuantityKinds.Reinforcement && q.MethodCode == RebarRatioRule.MethodCode))
            {
                bool missing = q.Reliability == Reliability.Missing;
                Add(report, AuditSection.Armatures, missing ? IssueSeverity.Error : IssueSeverity.Warning,
                    missing ? "ARMATURES_MANQUANTES" : "ARMATURES_PAR_RATIO",
                    "Élément " + q.ElementId + " (" + q.Category + ", " + q.TypeName + ") sans armature modélisée : " +
                    (missing ? "aucun ratio pour sa catégorie, armatures MANQUANTES." : q.Assumption), q);
            }
            AuditPartialRebar(quantities, extraction, report, fr);

            // 4. Profils sans masse lineique.
            foreach (ElementQuantity q in quantities.Where(q => q.Kind == QuantityKinds.StructuralSteel &&
                                                                (q.MethodCode == "SOLID_VOLUME_X_DENSITY" || q.Reliability == Reliability.Missing)))
            {
                Add(report, AuditSection.Profils, q.Reliability == Reliability.Missing ? IssueSeverity.Error : IssueSeverity.Warning,
                    "PROFIL_SANS_MASSE", "Profilé " + q.ElementId + " (" + q.TypeName + ") : " + (q.Assumption ?? q.MethodDescription), q);
            }

            // 6. Doublons de position (meme categorie, meme type, meme boite englobante au mm).
            foreach (var g in quantities
                         .Where(q => !string.IsNullOrEmpty(Param(q, "Position")))
                         .GroupBy(q => q.SourceDocument + "|" + q.Category + "|" + q.TypeName + "|" + Param(q, "Position")))
            {
                List<long> ids = g.Select(q => q.ElementId).Distinct().OrderBy(i => i).ToList();
                if (ids.Count < 2)
                {
                    continue;
                }
                var f = new AuditFinding
                {
                    Section = AuditSection.Doublons,
                    Severity = IssueSeverity.Error,
                    Code = "DOUBLON_POSITION",
                    Message = ids.Count + " éléments de même catégorie et même type occupent exactement la même boîte englobante (" +
                              string.Join(", ", ids) + ")" + SourceSuffix(g.First()) + " : double comptage probable.",
                    LinkInstanceId = ElementRef.Of(g.First()).LinkInstanceId
                };
                f.ElementIds.AddRange(ids);
                report.Findings.Add(f);
            }

            // Constats de modele et anomalies du moteur.
            foreach (BoqIssue issue in (modelFacts ?? Enumerable.Empty<BoqIssue>()).Concat(boq?.Issues ?? Enumerable.Empty<BoqIssue>()))
            {
                if (issue.Severity == IssueSeverity.Info)
                {
                    continue;
                }
                AuditSection section = FactSections.TryGetValue(issue.Code, out AuditSection s) ? s : AuditSection.AutresAnomalies;
                var f = new AuditFinding
                {
                    Section = section,
                    Severity = issue.Severity,
                    Code = issue.Code,
                    Message = issue.Message,
                    ArticleCode = issue.ArticleCode,
                    LinkInstanceId = issue.LinkInstanceId
                };
                f.ElementIds.AddRange(issue.ElementIds);
                report.Findings.Add(f);
            }

            // Part mesuree par nature (en quantite) : lisible meme sans prix.
            foreach (var g in quantities.Where(q => !q.ExcludedByUser).GroupBy(q => q.Kind ?? string.Empty))
            {
                decimal total = g.Sum(q => (decimal)q.Value);
                if (total > 0)
                {
                    report.MeasuredShareByKind[g.Key] = g.Where(q => q.Reliability == Reliability.Measured).Sum(q => (decimal)q.Value) / total;
                }
            }

            // Tri stable : rubrique, gravite (erreurs d'abord), code, premier element.
            var sorted = report.Findings
                .OrderBy(f => f.Section)
                .ThenByDescending(f => f.Severity)
                .ThenBy(f => f.Code, StringComparer.Ordinal)
                .ThenBy(f => f.ElementIds.Count > 0 ? f.ElementIds.Min() : long.MaxValue)
                .ToList();
            report.Findings.Clear();
            report.Findings.AddRange(sorted);
            return report;
        }

        private static void AuditPartialRebar(IReadOnlyList<ElementQuantity> quantities, ExtractionSettings extraction,
            AuditReport report, CultureInfo fr)
        {
            if (extraction?.RebarRatios == null || extraction.RebarRatios.Count == 0)
            {
                return;
            }
            Dictionary<string, decimal> modeledKgByHost = quantities
                .Where(q => q.Kind == QuantityKinds.Reinforcement && q.MethodCode != RebarRatioRule.MethodCode && !string.IsNullOrEmpty(Param(q, "HoteId")))
                .GroupBy(q => q.SourceDocument + "|" + Param(q, "HoteId"))
                .ToDictionary(g => g.Key, g => g.Sum(q => (decimal)q.Value));

            foreach (var host in quantities
                         .Where(q => q.Kind == QuantityKinds.ConcreteVolume && string.IsNullOrEmpty(Param(q, PhaseStatus.SourceElementParameter)))
                         .GroupBy(q => new { q.SourceDocument, q.ElementId, q.Category, q.TypeName }))
            {
                string key = host.Key.SourceDocument + "|" + host.Key.ElementId.ToString(CultureInfo.InvariantCulture);
                if (!modeledKgByHost.TryGetValue(key, out decimal kg))
                {
                    continue;
                }
                decimal volume = host.Sum(q => (decimal)q.Value);
                RebarRatio ratio = extraction.RebarRatios.FirstOrDefault(r => r.Category != null && r.Category.IsMatch(host.Key.Category));
                if (volume <= 0 || ratio == null)
                {
                    continue;
                }
                decimal modeled = kg / volume;
                if (modeled < ratio.KgPerM3 * PartialRebarFraction)
                {
                    Add(report, AuditSection.Armatures, IssueSeverity.Warning, "FERRAILLAGE_INCOMPLET",
                        "Élément " + host.Key.ElementId + " (" + host.Key.TypeName + ") : " + modeled.ToString("0.#", fr) +
                        " kg/m³ modélisés, contre " + ratio.KgPerM3.ToString("0.#", fr) + " kg/m³ de ratio de référence. " +
                        "Ferraillage probablement partiel (attentes seules ?) : le métré ne complète PAS par ratio.", host.First());
                }
            }
        }

        private static void Add(AuditReport report, AuditSection section, IssueSeverity severity, string code, string message, ElementQuantity q)
        {
            var f = new AuditFinding
            {
                Section = section,
                Severity = severity,
                Code = code,
                Message = message + SourceSuffix(q),
                LinkInstanceId = ElementRef.Of(q).LinkInstanceId
            };
            f.ElementIds.Add(q.ElementId);
            report.Findings.Add(f);
        }

        private static string SourceSuffix(ElementQuantity q) =>
            string.IsNullOrEmpty(q.SourceDocument) ? string.Empty : " [lien " + q.SourceDocument + "]";

        private static string Param(ElementQuantity q, string key) =>
            q.Parameters != null && q.Parameters.TryGetValue(key, out string v) ? v : null;
    }
}
