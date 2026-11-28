using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Une ouverture d'un mur : surface lue dans le modele, ou null si illisible.</summary>
    public sealed class WallOpening
    {
        public long ElementId { get; set; }
        public string Description { get; set; }

        /// <summary>Surface en m² ; null si les dimensions n'ont pas pu etre lues.</summary>
        public double? AreaM2 { get; set; }
    }

    /// <summary>Resultat de la regle de deduction pour un mur.</summary>
    public sealed class DeductionResult
    {
        public double AreaM2 { get; set; }
        public double OpeningsDeductedM2 { get; set; }
        public double OpeningsKeptM2 { get; set; }
        public int DeductedCount { get; set; }
        public int KeptCount { get; set; }
        public Reliability Reliability { get; set; } = Reliability.Measured;
        public string Assumption { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Applique la regle de deduction des ouvertures.
    ///
    /// Principe : la surface de face lue dans la geometrie Revit est DEJA percee de toutes
    /// les ouvertures (surface nette maximale). La regle consiste donc a RAJOUTER les
    /// ouvertures qu'on ne doit pas deduire :
    ///   surface = surface de face percee + somme des ouvertures non deduites.
    /// Consequence : une ouverture dont la surface est illisible ne peut pas etre rajoutee ;
    /// elle reste deduite, et la quantite est marquee ESTIMEE avec cette hypothese.
    /// </summary>
    public static class OpeningDeductionRule
    {
        public static DeductionResult Apply(double piercedFaceAreaM2, IEnumerable<WallOpening> openings, ExtractionSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var list = (openings ?? Enumerable.Empty<WallOpening>()).ToList();
            var result = new DeductionResult { AreaM2 = piercedFaceAreaM2 };
            var unreadable = new List<WallOpening>();
            double threshold = (double)settings.OpeningDeductionThresholdM2;

            foreach (WallOpening o in list)
            {
                if (o.AreaM2 == null)
                {
                    // Deja deduite par la geometrie, et impossible a rajouter.
                    result.DeductedCount++;
                    if (settings.OpeningDeduction != OpeningDeductionMode.Toutes)
                    {
                        unreadable.Add(o);
                    }
                    continue;
                }

                bool deduct;
                switch (settings.OpeningDeduction)
                {
                    case OpeningDeductionMode.Toutes: deduct = true; break;
                    case OpeningDeductionMode.Aucune: deduct = false; break;
                    default: deduct = o.AreaM2.Value > threshold; break;
                }

                if (deduct)
                {
                    result.DeductedCount++;
                    result.OpeningsDeductedM2 += o.AreaM2.Value;
                }
                else
                {
                    result.KeptCount++;
                    result.OpeningsKeptM2 += o.AreaM2.Value;
                    result.AreaM2 += o.AreaM2.Value;
                }
            }

            result.Description = Describe(settings, result, list.Count);
            if (unreadable.Count > 0)
            {
                result.Reliability = Reliability.Estimated;
                result.Assumption = unreadable.Count + " ouverture(s) aux dimensions illisibles (" +
                    string.Join(", ", unreadable.Select(u => u.ElementId.ToString(CultureInfo.InvariantCulture))) +
                    ") supposée(s) déduite(s) : la règle « " + ModeLabel(settings) + " » n'a pas pu être vérifiée pour elles.";
            }
            return result;
        }

        public static string ModeLabel(ExtractionSettings s)
        {
            switch (s.OpeningDeduction)
            {
                case OpeningDeductionMode.Toutes: return "toutes les ouvertures déduites";
                case OpeningDeductionMode.Aucune: return "aucune ouverture déduite";
                default: return "ouvertures > " + s.OpeningDeductionThresholdM2.ToString(CultureInfo.GetCultureInfo("fr-FR")) + " m² déduites";
            }
        }

        private static string Describe(ExtractionSettings s, DeductionResult r, int count)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            return "Règle : " + ModeLabel(s) + ". " + count + " ouverture(s) : " +
                   r.DeductedCount + " déduite(s) (" + r.OpeningsDeductedM2.ToString("0.###", fr) + " m² lus), " +
                   r.KeptCount + " non déduite(s) (" + r.OpeningsKeptM2.ToString("0.###", fr) + " m² rajoutés).";
        }
    }
}
