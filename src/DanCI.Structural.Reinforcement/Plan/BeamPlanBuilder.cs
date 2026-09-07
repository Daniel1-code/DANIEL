using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage decide pour une poutre en un <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local de la poutre : X suit la portee depuis le nu de depart, Y traverse la
    /// largeur (0 au milieu de l'ame), Z remonte depuis la sous-face. La couche Revit
    /// n'a donc rien de specifique aux poutres a connaitre.
    /// </summary>
    public static class BeamPlanBuilder
    {
        /// <summary>Espacement libre entre lits superposes, exprime en diametres.</summary>
        private const double LayerClearSpacingInDiameters = 1.0;

        public static ReinforcementPlan Build(BeamData beam, BeamReinforcement r, string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null) return plan;

            int barMark = 0;
            int tieMark = 0;

            double halfWidth = beam.WebWidthMm / 2.0;
            double endCover = r.CoverMm;
            double xStart = endCover;
            double xEnd = beam.SpanMm - endCover;

            // --- Lit(s) inferieur(s) en travee ---
            foreach (RebarGroup group in LongitudinalLayers(beam, r, r.BottomSpan, true,
                                                            xStart, xEnd, markPrefix, ref barMark,
                                                            "Lit inferieur"))
            {
                plan.Add(group);
            }

            // --- Barres de montage filantes en partie superieure ---
            foreach (RebarGroup group in LongitudinalLayers(beam, r, r.TopContinuous, false,
                                                            xStart, xEnd, markPrefix, ref barMark,
                                                            "Montage superieur"))
            {
                plan.Add(group);
            }

            // --- Chapeaux sur appuis ---
            double topLength = Math.Min(r.TopBarLengthMm, beam.SpanMm / 2.0);
            foreach (RebarGroup group in LongitudinalLayers(beam, r, r.TopLeft, false,
                                                            xStart, xStart + topLength,
                                                            markPrefix, ref barMark,
                                                            "Chapeau appui gauche"))
            {
                plan.Add(group);
            }
            foreach (RebarGroup group in LongitudinalLayers(beam, r, r.TopRight, false,
                                                            xEnd - topLength, xEnd,
                                                            markPrefix, ref barMark,
                                                            "Chapeau appui droit"))
            {
                plan.Add(group);
            }

            // --- Cadres ---
            foreach (BeamStirrupZone zone in r.StirrupZones)
            {
                if (zone.LengthMm <= 1.0) continue;
                tieMark++;
                plan.Add(Stirrup(beam, r, zone, string.Format("{0}-T{1:00}", markPrefix, tieMark)));
            }

            return plan;
        }

        /// <summary>
        /// Cree un groupe par lit de barres. Les barres d'un lit sont reparties sur la largeur
        /// utile et repetees suivant Y ; la normale est donc imposee suivant Y.
        /// </summary>
        private static IEnumerable<RebarGroup> LongitudinalLayers(BeamData beam, BeamReinforcement r,
                                                                  BarSelection selection, bool bottom,
                                                                  double xStartMm, double xEndMm,
                                                                  string markPrefix, ref int barMark,
                                                                  string label)
        {
            var groups = new List<RebarGroup>();
            if (selection == null || selection.Count <= 0 || xEndMm - xStartMm <= 1.0) return groups;

            double diameter = selection.DiameterMm;
            double halfSpan = beam.WebWidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm - diameter / 2.0;
            if (halfSpan <= 0) return groups;

            int perLayer = Math.Max(selection.BarsPerLayer, 1);
            int remaining = selection.Count;
            int layerIndex = 0;

            while (remaining > 0)
            {
                int count = Math.Min(perLayer, remaining);
                double layerOffset = layerIndex * (1.0 + LayerClearSpacingInDiameters) * diameter;

                double z = bottom
                    ? r.CoverMm + r.StirrupDiameterMm + diameter / 2.0 + layerOffset
                    : beam.HeightMm - r.CoverMm - r.StirrupDiameterMm - diameter / 2.0 - layerOffset;

                barMark++;
                var group = new RebarGroup
                {
                    Kind = RebarKind.Longitudinal,
                    DiameterMm = diameter,
                    Label = string.Format("{0}{1} - {2} HA{3:0}", label,
                        selection.Layers > 1 ? " lit " + (layerIndex + 1) : "", count, diameter),
                    Mark = string.Format("{0}-B{1:00}", markPrefix, barMark),
                    Layout = count > 1
                        ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, 2.0 * halfSpan)
                        : ArrayLayout.Single()
                };
                double y = count > 1 ? -halfSpan : 0.0;
                group.Path.Add(PlanSegment.Line(new LocalPoint(xStartMm, y, z),
                                                new LocalPoint(xEndMm, y, z)));
                group.WithNormal(LocalVector.AxisY);
                groups.Add(group);

                remaining -= count;
                layerIndex++;
            }

            return groups;
        }

        /// <summary>Cadre ferme dans le plan de la section, repete suivant la portee.</summary>
        private static RebarGroup Stirrup(BeamData beam, BeamReinforcement r, BeamStirrupZone zone,
                                          string mark)
        {
            double halfY = beam.WebWidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
            double zBottom = r.CoverMm + r.StirrupDiameterMm / 2.0;
            double zTop = beam.HeightMm - r.CoverMm - r.StirrupDiameterMm / 2.0;
            double x = zone.StartMm;

            var group = new RebarGroup
            {
                Kind = RebarKind.Stirrup,
                DiameterMm = r.StirrupDiameterMm,
                IsClosedLoop = true,
                WithHooks = true,
                Label = string.Format("Cadre {0} - HA{1:0} e={2:0}",
                                      zone.Label, r.StirrupDiameterMm, zone.SpacingMm),
                Mark = mark,
                Layout = ArrayLayout.MaximumSpacing(LocalVector.AxisX, zone.SpacingMm,
                                                    zone.LengthMm, true, true)
            };

            var p1 = new LocalPoint(x, -halfY, zBottom);
            var p2 = new LocalPoint(x, halfY, zBottom);
            var p3 = new LocalPoint(x, halfY, zTop);
            var p4 = new LocalPoint(x, -halfY, zTop);
            group.Path.Add(PlanSegment.Line(p1, p2));
            group.Path.Add(PlanSegment.Line(p2, p3));
            group.Path.Add(PlanSegment.Line(p3, p4));
            group.Path.Add(PlanSegment.Line(p4, p1));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }
    }
}
