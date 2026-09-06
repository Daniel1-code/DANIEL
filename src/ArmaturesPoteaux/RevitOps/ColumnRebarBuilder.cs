using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.RevitOps
{
    /// <summary>
    /// Modelise dans Revit le ferraillage decide par le dimensionnement : barres
    /// longitudinales, cadres (avec zones critiques resserrees) et epingles.
    /// Doit etre utilise a l'interieur d'une transaction ouverte.
    /// </summary>
    public class ColumnRebarBuilder
    {
        private readonly Document _document;
        private readonly RebarTypeProvider _types;
        private readonly DesignInput _input;
        private readonly View3D _activeThreeD;

        public ColumnRebarBuilder(Document document, RebarTypeProvider types, DesignInput input)
        {
            _document = document;
            _types = types;
            _input = input;
            _activeThreeD = document.ActiveView as View3D;
        }

        public BuildOutcome Build(DesignResult design)
        {
            var outcome = new BuildOutcome();
            ColumnGeometry g = design.Geometry;

            RebarBarType longitudinalType = _types.GetBarType(design.BarDiameterMm);
            RebarBarType transverseType = _types.GetBarType(design.StirrupDiameterMm);
            if (longitudinalType == null || transverseType == null)
            {
                outcome.Errors.Add(g.HostName + " : aucun type de barre d'armature n'est disponible " +
                                   "dans le projet. Chargez une famille d'armatures puis relancez.");
                return outcome;
            }

            RebarHookType hook = _types.GetStirrupHook();

            try
            {
                if (g.Kind == SectionKind.Circular)
                {
                    BuildCircularLongitudinal(g, design, longitudinalType, outcome);
                    BuildCircularStirrups(g, design, transverseType, hook, outcome);
                }
                else
                {
                    BuildRectangularLongitudinal(g, design, longitudinalType, outcome);
                    BuildRectangularStirrups(g, design, transverseType, hook, outcome);
                    BuildCrossTies(g, design, transverseType, hook, outcome);
                }
                outcome.ColumnsProcessed = 1;
            }
            catch (Exception ex)
            {
                outcome.Errors.Add(g.HostName + " : " + ex.Message);
            }
            return outcome;
        }

        // ------------------------------------------------------------------
        // Cotes du ferraillage dans le repere local du poteau (mm)
        // ------------------------------------------------------------------

        /// <summary>Demi-distance entre axes des barres d'angle suivant X.</summary>
        private double BarHalfSpanX(ColumnGeometry g, DesignResult d)
        {
            return g.WidthMm / 2.0 - _input.CoverMm - d.StirrupDiameterMm - d.BarDiameterMm / 2.0;
        }

        /// <summary>Demi-distance entre axes des barres d'angle suivant Y.</summary>
        private double BarHalfSpanY(ColumnGeometry g, DesignResult d)
        {
            return g.DepthMm / 2.0 - _input.CoverMm - d.StirrupDiameterMm - d.BarDiameterMm / 2.0;
        }

        /// <summary>Demi-largeur de l'axe du cadre suivant X.</summary>
        private double StirrupHalfX(ColumnGeometry g, DesignResult d)
        {
            return g.WidthMm / 2.0 - _input.CoverMm - d.StirrupDiameterMm / 2.0;
        }

        /// <summary>Demi-largeur de l'axe du cadre suivant Y.</summary>
        private double StirrupHalfY(ColumnGeometry g, DesignResult d)
        {
            return g.DepthMm / 2.0 - _input.CoverMm - d.StirrupDiameterMm / 2.0;
        }

        private double BarBottom(DesignResult d)
        {
            return d.BottomOffsetMm;
        }

        private double BarTop(ColumnGeometry g, DesignResult d)
        {
            return g.HeightMm + d.TopExtensionMm;
        }

        // ------------------------------------------------------------------
        // Barres longitudinales
        // ------------------------------------------------------------------

        private void BuildRectangularLongitudinal(ColumnGeometry g, DesignResult d, RebarBarType type,
                                                  BuildOutcome outcome)
        {
            double x0 = BarHalfSpanX(g, d);
            double y0 = BarHalfSpanY(g, d);
            double zBottom = BarBottom(d);
            double zTop = BarTop(g, d);

            // Lits paralleles a X : les deux faces portent les barres d'angle.
            double arrayX = 2.0 * x0;
            AddBarRow(g, d, type, -x0, -y0, zBottom, zTop, g.AxisX, d.BarsAlongX, arrayX, outcome,
                      "Lit inferieur");
            AddBarRow(g, d, type, -x0, y0, zBottom, zTop, g.AxisX, d.BarsAlongX, arrayX, outcome,
                      "Lit superieur");

            // Barres intermediaires des faces paralleles a Y (les angles sont deja poses).
            int intermediate = d.BarsAlongY - 2;
            if (intermediate > 0)
            {
                double pitch = 2.0 * y0 / (d.BarsAlongY - 1);
                double start = -y0 + pitch;
                double arrayY = (intermediate - 1) * pitch;
                AddBarRow(g, d, type, -x0, start, zBottom, zTop, g.AxisY, intermediate, arrayY, outcome,
                          "Face gauche");
                AddBarRow(g, d, type, x0, start, zBottom, zTop, g.AxisY, intermediate, arrayY, outcome,
                          "Face droite");
            }
        }

        private void AddBarRow(ColumnGeometry g, DesignResult d, RebarBarType type,
                               double xMm, double yMm, double zBottomMm, double zTopMm,
                               XYZ arrayDirection, int count, double arrayLengthMm,
                               BuildOutcome outcome, string label)
        {
            if (count <= 0) return;

            Line curve = Line.CreateBound(g.ToWorld(xMm, yMm, zBottomMm), g.ToWorld(xMm, yMm, zTopMm));
            Rebar rebar = CreateRebar(RebarStyle.Standard, type, null, null, g,
                                      arrayDirection, new List<Curve> { curve });
            if (rebar == null)
            {
                outcome.Errors.Add(g.HostName + " : impossible de creer les barres longitudinales (" +
                                   label + ").");
                return;
            }

            RebarShapeDrivenAccessor accessor = rebar.GetShapeDrivenAccessor();
            if (count > 1 && arrayLengthMm > 1.0)
            {
                accessor.SetLayoutAsFixedNumber(count, Units.MmToFeet(arrayLengthMm), true, true, true);
            }
            else
            {
                accessor.SetLayoutAsSingle();
            }

            Decorate(rebar, string.Format("{0} - {1} HA{2:0}", label, count, d.BarDiameterMm));
            outcome.LongitudinalSets++;
            outcome.LongitudinalBars += count;
            outcome.Created.Add(rebar.Id);
        }

        private void BuildCircularLongitudinal(ColumnGeometry g, DesignResult d, RebarBarType type,
                                               BuildOutcome outcome)
        {
            int count = d.TotalBars;
            double radius = g.DiameterMm / 2.0 - _input.CoverMm - d.StirrupDiameterMm - d.BarDiameterMm / 2.0;
            double zBottom = BarBottom(d);
            double zTop = BarTop(g, d);

            for (int i = 0; i < count; i++)
            {
                double angle = 2.0 * Math.PI * i / count;
                double x = radius * Math.Cos(angle);
                double y = radius * Math.Sin(angle);
                Line curve = Line.CreateBound(g.ToWorld(x, y, zBottom), g.ToWorld(x, y, zTop));
                Rebar rebar = CreateRebar(RebarStyle.Standard, type, null, null, g,
                                          g.AxisX, new List<Curve> { curve });
                if (rebar == null) continue;
                rebar.GetShapeDrivenAccessor().SetLayoutAsSingle();
                Decorate(rebar, string.Format("Barre {0}/{1} HA{2:0}", i + 1, count, d.BarDiameterMm));
                outcome.LongitudinalSets++;
                outcome.LongitudinalBars++;
                outcome.Created.Add(rebar.Id);
            }
        }

        // ------------------------------------------------------------------
        // Cadres
        // ------------------------------------------------------------------

        private class StirrupZone
        {
            public double StartMm;
            public double EndMm;
            public double SpacingMm;
            public bool IncludeFirst;
            public bool IncludeLast;
            public string Label;
        }

        /// <summary>
        /// Decoupe la hauteur du poteau en zones : pied resserre, zone courante, tete resserree.
        /// Les zones critiques sont abandonnees si le poteau est trop court pour les accueillir.
        /// </summary>
        private List<StirrupZone> BuildZones(ColumnGeometry g, DesignResult d)
        {
            double start = _input.FirstStirrupOffsetMm;
            double end = g.HeightMm - _input.FirstStirrupOffsetMm;
            var zones = new List<StirrupZone>();
            if (end - start <= 0) return zones;

            double critical = d.CriticalZoneLengthMm;
            bool useCritical = _input.UseCriticalZones
                               && critical > 0
                               && d.SpacingCriticalMm < d.SpacingCurrentMm - 1.0
                               && (end - start) > 2.0 * critical + d.SpacingCurrentMm;

            if (!useCritical)
            {
                double spacing = _input.UseCriticalZones && critical > 0 && (end - start) <= 2.0 * critical
                    ? d.SpacingCriticalMm     // poteau court : entierement en zone critique
                    : d.SpacingCurrentMm;
                zones.Add(new StirrupZone
                {
                    StartMm = start,
                    EndMm = end,
                    SpacingMm = spacing,
                    IncludeFirst = true,
                    IncludeLast = true,
                    Label = "Cadres"
                });
                return zones;
            }

            zones.Add(new StirrupZone
            {
                StartMm = start,
                EndMm = start + critical,
                SpacingMm = d.SpacingCriticalMm,
                IncludeFirst = true,
                IncludeLast = true,
                Label = "Zone critique basse"
            });
            zones.Add(new StirrupZone
            {
                StartMm = start + critical,
                EndMm = end - critical,
                SpacingMm = d.SpacingCurrentMm,
                IncludeFirst = false,
                IncludeLast = false,
                Label = "Zone courante"
            });
            zones.Add(new StirrupZone
            {
                StartMm = end - critical,
                EndMm = end,
                SpacingMm = d.SpacingCriticalMm,
                IncludeFirst = true,
                IncludeLast = true,
                Label = "Zone critique haute"
            });
            return zones;
        }

        private void BuildRectangularStirrups(ColumnGeometry g, DesignResult d, RebarBarType type,
                                              RebarHookType hook, BuildOutcome outcome)
        {
            double sx = StirrupHalfX(g, d);
            double sy = StirrupHalfY(g, d);
            if (sx <= 0 || sy <= 0)
            {
                outcome.Errors.Add(g.HostName + " : l'enrobage est trop important pour la section.");
                return;
            }

            foreach (StirrupZone zone in BuildZones(g, d))
            {
                List<Curve> loop = RectangularLoop(g, sx, sy, zone.StartMm);
                CreateStirrupSet(g, d, type, hook, loop, zone, outcome, "Cadre " + zone.Label, false);
            }
        }

        private void BuildCircularStirrups(ColumnGeometry g, DesignResult d, RebarBarType type,
                                           RebarHookType hook, BuildOutcome outcome)
        {
            double radius = g.DiameterMm / 2.0 - _input.CoverMm - d.StirrupDiameterMm / 2.0;
            if (radius <= 0)
            {
                outcome.Errors.Add(g.HostName + " : l'enrobage est trop important pour la section.");
                return;
            }

            foreach (StirrupZone zone in BuildZones(g, d))
            {
                List<Curve> loop = CircularLoop(g, radius, zone.StartMm);
                CreateStirrupSet(g, d, type, hook, loop, zone, outcome, "Cerce " + zone.Label, false);
            }
        }

        private void BuildCrossTies(ColumnGeometry g, DesignResult d, RebarBarType type,
                                    RebarHookType hook, BuildOutcome outcome)
        {
            if (d.CrossTiesAlongX <= 0 && d.CrossTiesAlongY <= 0) return;

            double x0 = BarHalfSpanX(g, d);
            double y0 = BarHalfSpanY(g, d);
            List<StirrupZone> zones = BuildZones(g, d);

            // Epingles orientees suivant Y, posees au droit des barres intermediaires des lits X.
            if (d.CrossTiesAlongY > 0 && d.BarsAlongX > 2)
            {
                double pitch = 2.0 * x0 / (d.BarsAlongX - 1);
                for (int i = 1; i <= d.BarsAlongX - 2; i++)
                {
                    double x = -x0 + i * pitch;
                    foreach (StirrupZone zone in zones)
                    {
                        var curve = new List<Curve>
                        {
                            Line.CreateBound(g.ToWorld(x, -y0, zone.StartMm), g.ToWorld(x, y0, zone.StartMm))
                        };
                        CreateStirrupSet(g, d, type, hook, curve, zone, outcome,
                                         "Epingle //Y " + zone.Label, true);
                    }
                }
            }

            // Epingles orientees suivant X, posees au droit des barres intermediaires des faces Y.
            if (d.CrossTiesAlongX > 0 && d.BarsAlongY > 2)
            {
                double pitch = 2.0 * y0 / (d.BarsAlongY - 1);
                for (int i = 1; i <= d.BarsAlongY - 2; i++)
                {
                    double y = -y0 + i * pitch;
                    foreach (StirrupZone zone in zones)
                    {
                        var curve = new List<Curve>
                        {
                            Line.CreateBound(g.ToWorld(-x0, y, zone.StartMm), g.ToWorld(x0, y, zone.StartMm))
                        };
                        CreateStirrupSet(g, d, type, hook, curve, zone, outcome,
                                         "Epingle //X " + zone.Label, true);
                    }
                }
            }
        }

        private void CreateStirrupSet(ColumnGeometry g, DesignResult d, RebarBarType type,
                                      RebarHookType hook, List<Curve> curves, StirrupZone zone,
                                      BuildOutcome outcome, string label, bool isCrossTie)
        {
            double lengthMm = zone.EndMm - zone.StartMm;
            if (lengthMm <= 1.0) return;

            Rebar rebar = CreateRebar(RebarStyle.StirrupTie, type, hook, hook, g, g.AxisZ, curves);
            if (rebar == null)
            {
                outcome.Errors.Add(g.HostName + " : impossible de creer " + label + ".");
                return;
            }

            rebar.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(
                Units.MmToFeet(zone.SpacingMm), Units.MmToFeet(lengthMm), true,
                zone.IncludeFirst, zone.IncludeLast);

            Decorate(rebar, string.Format("{0} - HA{1:0} e={2:0}", label, d.StirrupDiameterMm, zone.SpacingMm));
            if (isCrossTie) outcome.CrossTieSets++; else outcome.StirrupSets++;
            outcome.Created.Add(rebar.Id);
        }

        private List<Curve> RectangularLoop(ColumnGeometry g, double halfX, double halfY, double zMm)
        {
            XYZ p1 = g.ToWorld(-halfX, -halfY, zMm);
            XYZ p2 = g.ToWorld(halfX, -halfY, zMm);
            XYZ p3 = g.ToWorld(halfX, halfY, zMm);
            XYZ p4 = g.ToWorld(-halfX, halfY, zMm);
            return new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };
        }

        private List<Curve> CircularLoop(ColumnGeometry g, double radiusMm, double zMm)
        {
            XYZ center = g.ToWorld(0, 0, zMm);
            double radius = Units.MmToFeet(radiusMm);
            return new List<Curve>
            {
                Arc.Create(center, radius, 0.0, Math.PI, g.AxisX, g.AxisY),
                Arc.Create(center, radius, Math.PI, 2.0 * Math.PI, g.AxisX, g.AxisY)
            };
        }

        /// <summary>
        /// Cree une armature ; en cas de refus lie aux crochets, un second essai est fait
        /// sans crochet afin de ne jamais perdre l'ensemble du ferraillage.
        /// </summary>
        private Rebar CreateRebar(RebarStyle style, RebarBarType type, RebarHookType startHook,
                                  RebarHookType endHook, ColumnGeometry g, XYZ normal, IList<Curve> curves)
        {
            try
            {
                return Rebar.CreateFromCurves(_document, style, type, startHook, endHook, g.Host,
                                              normal, curves, RebarHookOrientation.Right,
                                              RebarHookOrientation.Left, true, true);
            }
            catch (Exception)
            {
                if (startHook == null && endHook == null) return null;
                try
                {
                    return Rebar.CreateFromCurves(_document, style, type, null, null, g.Host,
                                                  normal, curves, RebarHookOrientation.Right,
                                                  RebarHookOrientation.Left, true, true);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Renseigne le commentaire et rend l'armature lisible dans la vue 3D active.</summary>
        private void Decorate(Rebar rebar, string comment)
        {
            try
            {
                Parameter parameter = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (parameter != null && !parameter.IsReadOnly) parameter.Set(comment);
            }
            catch (Exception)
            {
                // Le commentaire est purement informatif.
            }

            if (_activeThreeD == null) return;
            try
            {
                rebar.SetSolidInView(_activeThreeD, true);
                rebar.SetUnobscuredInView(_activeThreeD, true);
            }
            catch (Exception)
            {
                // La vue peut etre un gabarit ou une vue non modifiable.
            }
        }
    }
}
