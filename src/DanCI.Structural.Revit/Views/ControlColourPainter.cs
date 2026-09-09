using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DanCI.Structural.Documentation.Dashboard;

namespace DanCI.Structural.Revit.Views
{
    /// <summary>Ce qu'une passe de coloration a reellement fait.</summary>
    public sealed class PaintOutcome
    {
        public int Painted { get; set; }
        public int Skipped { get; set; }
        public IList<string> Messages { get; set; }

        public PaintOutcome()
        {
            Messages = new List<string>();
        }
    }

    /// <summary>
    /// Pose les couleurs de controle SUR UNE VUE, et les enleve.
    ///
    /// TROIS REGLES, ET AUCUNE N'EST NEGOCIABLE.
    ///
    /// 1. LA COLORATION EST PROPRE A LA VUE. Elle passe par les remplacements graphiques
    ///    de la vue active, jamais par les materiaux ni par les parametres des elements.
    ///    Le modele n'est pas modifie : changez de vue et les couleurs disparaissent.
    /// 2. ELLE EST REVERSIBLE, et le retrait est fourni avec la pose. Une coloration qu'on
    ///    ne sait pas defaire est une degradation du modele, pas un outil de lecture.
    /// 3. ELLE NE S'APPLIQUE PAS A UNE VUE QUI NE L'ACCEPTE PAS. Un gabarit de vue, ou une
    ///    vue dont les remplacements sont pilotes par un gabarit, refuse silencieusement
    ///    les remplacements : poser sans verifier laisserait croire que la vue est coloree
    ///    alors qu'elle ne l'est pas, ce qui est pire que ne rien poser.
    ///
    /// La transaction est ouverte par l'APPELANT : la commande sait ce qu'elle regroupe,
    /// pas le peintre.
    /// </summary>
    public static class ControlColourPainter
    {
        /// <summary>
        /// La vue accepte-t-elle des remplacements graphiques ? Repondre non ici evite de
        /// peindre dans le vide.
        /// </summary>
        public static bool CanPaint(View view, out string reason)
        {
            reason = null;
            if (view == null)
            {
                reason = "Aucune vue active.";
                return false;
            }

            if (view.IsTemplate)
            {
                reason = "La vue active est un GABARIT de vue : les couleurs de controle se "
                         + "posent sur une vue de travail, pas sur le gabarit qui la pilote.";
                return false;
            }

            if (!view.AreGraphicsOverridesAllowed())
            {
                reason = "Cette vue n'accepte pas les remplacements graphiques par element. "
                         + "C'est en general un gabarit de vue qui les pilote : detachez-le "
                         + "de la vue, ou travaillez dans une vue dupliquee.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applique le plan de couleurs a la vue. Les identifiants du plan sont ceux
        /// rendus par <see cref="ElementId.Value"/>.
        /// </summary>
        public static PaintOutcome Apply(Document document, View view, ColourPlan plan)
        {
            var outcome = new PaintOutcome();
            if (document == null || plan == null) return outcome;

            string reason;
            if (!CanPaint(view, out reason))
            {
                outcome.Messages.Add(reason);
                return outcome;
            }

            ElementId solidFill = SolidFillPatternId(document);
            if (solidFill == ElementId.InvalidElementId)
            {
                outcome.Messages.Add(
                    "Aucun motif de remplissage plein n'a ete trouve dans ce projet : seules "
                    + "les lignes seront colorees, pas les surfaces.");
            }

            foreach (ColourGroup group in plan.Groups)
            {
                OverrideGraphicSettings settings = Build(group.Colour, solidFill);

                foreach (string id in group.ElementIds)
                {
                    ElementId elementId = Parse(id);
                    if (elementId == ElementId.InvalidElementId)
                    {
                        outcome.Skipped++;
                        continue;
                    }

                    try
                    {
                        view.SetElementOverrides(elementId, settings);
                        outcome.Painted++;
                    }
                    catch (Exception)
                    {
                        // Un element peut avoir ete supprime entre le calcul et la pose.
                        outcome.Skipped++;
                    }
                }
            }

            if (plan.UnaddressableCount > 0)
            {
                outcome.Messages.Add(string.Format(
                    "{0} element(s) calcules n'ont pas d'identifiant de modele et n'ont donc "
                    + "pas pu etre colores. Ils restent dans la note de synthese.",
                    plan.UnaddressableCount));
            }

            return outcome;
        }

        /// <summary>
        /// Retire les remplacements poses. Le retrait est fourni AVEC la pose : une
        /// coloration qu'on ne sait pas defaire degrade le modele au lieu de l'eclairer.
        /// </summary>
        public static PaintOutcome Clear(Document document, View view, ColourPlan plan)
        {
            var outcome = new PaintOutcome();
            if (document == null || plan == null) return outcome;

            string reason;
            if (!CanPaint(view, out reason))
            {
                outcome.Messages.Add(reason);
                return outcome;
            }

            var neutral = new OverrideGraphicSettings();
            foreach (ColourGroup group in plan.Groups)
            {
                foreach (string id in group.ElementIds)
                {
                    ElementId elementId = Parse(id);
                    if (elementId == ElementId.InvalidElementId)
                    {
                        outcome.Skipped++;
                        continue;
                    }

                    try
                    {
                        view.SetElementOverrides(elementId, neutral);
                        outcome.Painted++;
                    }
                    catch (Exception)
                    {
                        outcome.Skipped++;
                    }
                }
            }

            return outcome;
        }

        private static OverrideGraphicSettings Build(ControlColour colour, ElementId solidFill)
        {
            var settings = new OverrideGraphicSettings();
            var revitColour = new Color(colour.R, colour.G, colour.B);

            settings.SetProjectionLineColor(revitColour);
            settings.SetCutLineColor(revitColour);

            if (solidFill == ElementId.InvalidElementId) return settings;

            settings.SetSurfaceForegroundPatternId(solidFill);
            settings.SetSurfaceForegroundPatternColor(revitColour);
            settings.SetSurfaceForegroundPatternVisible(true);
            settings.SetCutForegroundPatternId(solidFill);
            settings.SetCutForegroundPatternColor(revitColour);
            settings.SetCutForegroundPatternVisible(true);
            return settings;
        }

        /// <summary>
        /// Cherche le motif de remplissage PLEIN. Il est cherche sur sa PROPRIETE, pas sur
        /// son nom : « Solid fill » devient « Remplissage plein » en francais, et un motif
        /// trouve par son nom ne serait trouve que dans une seule langue.
        /// </summary>
        private static ElementId SolidFillPatternId(Document document)
        {
            try
            {
                var collector = new FilteredElementCollector(document)
                    .OfClass(typeof(FillPatternElement));

                foreach (Element element in collector)
                {
                    var pattern = element as FillPatternElement;
                    if (pattern == null) continue;

                    FillPattern fill = pattern.GetFillPattern();
                    if (fill != null && fill.IsSolidFill) return pattern.Id;
                }
            }
            catch (Exception)
            {
                // Projet sans motif accessible : l'appelant le dira.
            }
            return ElementId.InvalidElementId;
        }

        private static ElementId Parse(string id)
        {
            long value;
            return long.TryParse(id, out value)
                ? new ElementId(value) : ElementId.InvalidElementId;
        }
    }
}
