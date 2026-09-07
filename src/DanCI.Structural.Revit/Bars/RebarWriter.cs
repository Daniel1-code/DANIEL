using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Revit.Bars
{
    /// <summary>
    /// Traduit un <see cref="ReinforcementPlan"/> en objets Revit <c>Rebar</c>. Ce traducteur
    /// est generique : il ne sait rien du type d'element structurel, ce qui permet de
    /// reutiliser tel quel le meme code pour les poteaux, les poutres, les semelles ou les
    /// voiles. Doit etre utilise a l'interieur d'une transaction ouverte.
    /// </summary>
    public sealed class RebarWriter
    {
        private readonly Document _document;
        private readonly RebarTypeProvider _types;
        private readonly View3D _activeThreeD;

        public RebarWriter(Document document, RebarTypeProvider types)
        {
            _document = document;
            _types = types;
            _activeThreeD = document.ActiveView as View3D;
        }

        public BuildOutcome Write(RevitElementFrame frame, ReinforcementPlan plan, string elementName)
        {
            var outcome = new BuildOutcome();
            if (plan == null || plan.Groups.Count == 0) return outcome;

            RebarHookType hook = _types.GetStirrupHook();

            foreach (RebarGroup group in plan.Groups)
            {
                RebarBarType barType = _types.GetBarType(group.DiameterMm);
                if (barType == null)
                {
                    outcome.Errors.Add(elementName + " : aucun type de barre d'armature n'est " +
                                       "disponible dans le projet. Chargez une famille d'armatures " +
                                       "puis relancez.");
                    return outcome;
                }

                try
                {
                    WriteGroup(frame, group, barType, hook, outcome, elementName);
                }
                catch (Exception ex)
                {
                    outcome.Errors.Add(elementName + " - " + group.Label + " : " + ex.Message);
                }
            }

            outcome.ElementsProcessed = 1;
            return outcome;
        }

        private void WriteGroup(RevitElementFrame frame, RebarGroup group, RebarBarType barType,
                                RebarHookType hook, BuildOutcome outcome, string elementName)
        {
            List<Curve> curves = BuildCurves(frame, group);
            if (curves.Count == 0) return;

            bool isStirrup = group.Kind != RebarKind.Longitudinal;
            RebarStyle style = isStirrup ? RebarStyle.StirrupTie : RebarStyle.Standard;
            RebarHookType startHook = group.WithHooks ? hook : null;
            RebarHookType endHook = group.WithHooks ? hook : null;

            XYZ normal = ResolveNormal(frame, group);

            Rebar rebar = Create(style, barType, startHook, endHook, frame.Host, normal, curves);
            if (rebar == null)
            {
                outcome.Errors.Add(elementName + " : impossible de creer " + group.Label + ".");
                return;
            }

            ApplyLayout(rebar, group);
            Decorate(rebar, group);

            switch (group.Kind)
            {
                case RebarKind.Longitudinal:
                    outcome.LongitudinalSets++;
                    outcome.LongitudinalBars += group.BarCount;
                    break;
                case RebarKind.Stirrup:
                    outcome.StirrupSets++;
                    break;
                default:
                    outcome.CrossTieSets++;
                    break;
            }
            outcome.Created.Add(rebar.Id);
        }

        private static List<Curve> BuildCurves(RevitElementFrame frame, RebarGroup group)
        {
            var curves = new List<Curve>();
            foreach (PlanSegment segment in group.Path)
            {
                if (segment.Kind == SegmentKind.Line)
                {
                    curves.Add(Line.CreateBound(frame.ToWorld(segment.Start), frame.ToWorld(segment.End)));
                }
                else
                {
                    curves.Add(Arc.Create(frame.ToWorld(segment.Center),
                                          UnitConverter.MmToFeet(segment.RadiusMm),
                                          segment.StartAngle, segment.EndAngle,
                                          frame.AxisX, frame.AxisY));
                }
            }
            return curves;
        }

        /// <summary>
        /// Normale au plan des courbes de l'armature, qui est aussi la direction de
        /// repetition. Pour une barre isolee, toute direction perpendiculaire convient.
        /// </summary>
        private static XYZ ResolveNormal(RevitElementFrame frame, RebarGroup group)
        {
            if (group.Layout != null && group.Layout.Kind != LayoutKind.Single)
            {
                return frame.ToWorldDirection(group.Layout.Direction);
            }
            return group.Kind == RebarKind.Longitudinal ? frame.AxisX : frame.AxisZ;
        }

        private static void ApplyLayout(Rebar rebar, RebarGroup group)
        {
            RebarShapeDrivenAccessor accessor = rebar.GetShapeDrivenAccessor();
            ArrayLayout layout = group.Layout;

            if (layout == null || layout.Kind == LayoutKind.Single || layout.ArrayLengthMm <= 1.0)
            {
                accessor.SetLayoutAsSingle();
                return;
            }

            if (layout.Kind == LayoutKind.FixedNumber)
            {
                if (layout.Count <= 1)
                {
                    accessor.SetLayoutAsSingle();
                    return;
                }
                accessor.SetLayoutAsFixedNumber(layout.Count,
                    UnitConverter.MmToFeet(layout.ArrayLengthMm), true,
                    layout.IncludeFirst, layout.IncludeLast);
                return;
            }

            accessor.SetLayoutAsMaximumSpacing(UnitConverter.MmToFeet(layout.SpacingMm),
                UnitConverter.MmToFeet(layout.ArrayLengthMm), true,
                layout.IncludeFirst, layout.IncludeLast);
        }

        /// <summary>
        /// Cree une armature ; en cas de refus lie aux crochets, un second essai est fait sans
        /// crochet afin de ne jamais perdre l'ensemble du ferraillage.
        /// </summary>
        /// <remarks>
        /// La surcharge utilisee est marquee obsolete dans Revit 2026 au profit de celle
        /// prenant un BarTerminationsData ; elle reste fonctionnelle et couvre le besoin
        /// actuel (un crochet identique aux deux extremites).
        /// </remarks>
        private Rebar Create(RebarStyle style, RebarBarType barType, RebarHookType startHook,
                             RebarHookType endHook, Element host, XYZ normal, IList<Curve> curves)
        {
            try
            {
                return Rebar.CreateFromCurves(_document, style, barType, startHook, endHook, host,
                                              normal, curves, RebarHookOrientation.Right,
                                              RebarHookOrientation.Left, true, true);
            }
            catch (Exception)
            {
                if (startHook == null && endHook == null) return null;
                try
                {
                    return Rebar.CreateFromCurves(_document, style, barType, null, null, host,
                                                  normal, curves, RebarHookOrientation.Right,
                                                  RebarHookOrientation.Left, true, true);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Renseigne le repere et le commentaire, et rend l'armature lisible en 3D.</summary>
        private void Decorate(Rebar rebar, RebarGroup group)
        {
            TrySetParameter(rebar, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, group.Label);
            TrySetParameter(rebar, BuiltInParameter.ALL_MODEL_MARK, group.Mark);

            if (_activeThreeD == null) return;
            try
            {
                // Revit 2026 ne propose plus SetSolidInView : l'affichage non masque suffit
                // pour retrouver les armatures dans la vue 3D active.
                rebar.SetUnobscuredInView(_activeThreeD, true);
            }
            catch (Exception)
            {
                // La vue peut etre un gabarit ou une vue non modifiable.
            }
        }

        private static void TrySetParameter(Element element, BuiltInParameter id, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                Parameter parameter = element.get_Parameter(id);
                if (parameter != null && !parameter.IsReadOnly) parameter.Set(value);
            }
            catch (Exception)
            {
                // Information non essentielle.
            }
        }
    }
}
