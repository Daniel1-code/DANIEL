using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit une volee d'escalier Revit et la traduit en <see cref="StairData"/>.
    ///
    /// DEUX SELECTIONS SONT POSSIBLES, ET ELLES NE SERVENT PAS A LA MEME CHOSE.
    ///
    /// - Un **escalier Revit** porte toute la geometrie utile : nombre de contremarches,
    ///   hauteur de contremarche, giron, largeur de volee. Le lecteur la reprend telle
    ///   quelle, sans rien deviner.
    /// - Un **plancher structurel incline** modelisant la paillasse est, lui, un hote
    ///   d'armatures. Il ne porte en revanche aucune information de marche : la geometrie
    ///   de la marche reste alors celle saisie dans la fenetre.
    ///
    /// La question « cet element accepte-t-il des armatures ? » n'est jamais supposee :
    /// elle est posee a l'API, comme pour tous les autres modules, et la reponse est
    /// rapportee telle qu'elle vient. Si l'escalier la refuse, le calcul, les quantitatifs
    /// et la note restent produits — c'est la pose des barres, et elle seule, qui demande
    /// un hote valide.
    /// </summary>
    public static class StairReader
    {
        private const double Tolerance = 1e-6;

        public static RevitStair TryRead(Element element, StairData defaults, out string error)
        {
            error = null;
            if (element == null)
            {
                error = "Element nul.";
                return null;
            }

            var stairs = element as Stairs;
            if (stairs != null) return ReadStairs(stairs, defaults, out error);

            if (element.Category != null
                && element.Category.Id.Value == (long)BuiltInCategory.OST_Floors)
            {
                return ReadFlightModelledAsFloor(element, defaults, out error);
            }

            error = "L'element n'est ni un escalier, ni un plancher modelisant une paillasse.";
            return null;
        }

        // ------------------------------------------------------------------
        // Escalier Revit
        // ------------------------------------------------------------------

        private static RevitStair ReadStairs(Stairs stairs, StairData defaults, out string error)
        {
            error = null;
            StairData data = Clone(defaults);
            data.Id = stairs.UniqueId;
            data.Name = ColumnReader.Describe(stairs);
            data.Mark = ColumnReader.ReadMark(stairs);

            try
            {
                if (stairs.ActualRisersNumber > 0) data.RiserCount = stairs.ActualRisersNumber;
                double riser = UnitConverter.FeetToMm(stairs.ActualRiserHeight);
                double tread = UnitConverter.FeetToMm(stairs.ActualTreadDepth);
                if (riser > Tolerance) data.RiserHeightMm = riser;
                if (tread > Tolerance) data.TreadDepthMm = tread;

                data.Remarks.Add(string.Format(
                    "Geometrie lue sur l'escalier Revit : {0} contremarches de {1:0} mm, " +
                    "giron {2:0} mm.", data.RiserCount, data.RiserHeightMm, data.TreadDepthMm));
            }
            catch (Exception)
            {
                data.Remarks.Add(
                    "La geometrie de marche n'a pas pu etre lue sur l'escalier : les valeurs " +
                    "de la fenetre sont conservees. Verifiez-les avant de calculer.");
            }

            data.Shape = ResolveShape(stairs, data);

            double width = ReadRunWidth(stairs);
            if (width > Tolerance)
            {
                data.WidthMm = width;
                data.Remarks.Add(string.Format("Largeur de volee lue : {0:0} mm.", width));
            }
            else
            {
                data.Remarks.Add(
                    "La largeur de volee n'a pas pu etre lue : la valeur de la fenetre est " +
                    "conservee.");
            }

            // L'epaisseur de paillasse n'est PAS deduite : selon le type de volee, Revit la
            // porte ou non, et une valeur inventee fausserait tout le poids propre.
            data.Remarks.Add(
                "L'epaisseur de paillasse n'est pas lue sur l'escalier : elle depend du type " +
                "de volee et Revit ne l'expose pas de maniere fiable. C'est la valeur saisie " +
                "dans la fenetre qui est utilisee, et c'est elle qui pilote tout le poids " +
                "propre : verifiez-la.");

            if (stairs.MultistoryStairsId != null
                && stairs.MultistoryStairsId != ElementId.InvalidElementId)
            {
                data.Remarks.Add(
                    "Cet escalier appartient a un escalier multi-etages : verifiez que la " +
                    "volee calculee est bien celle qui vous interesse.");
            }

            var stair = new RevitStair
            {
                Data = data,
                Frame = BuildFrame(stairs, data),
                CanHostRebar = IsValidRebarHost(stairs)
            };

            if (!stair.CanHostRebar)
            {
                stair.RebarHostMessage =
                    "Revit refuse cet escalier comme hote d'armatures : aucune barre ne peut " +
                    "y etre posee. Le calcul, l'apercu, le quantitatif et la note restent " +
                    "produits. Pour poser les armatures dans le modele, modelisez la " +
                    "paillasse par un plancher structurel incline ou un element in situ, puis " +
                    "relancez la commande sur cet element.";
                data.Remarks.Add(stair.RebarHostMessage);
            }

            return stair;
        }

        /// <summary>
        /// Determine la forme de la volee a partir de sa LIGNE DE FOULEE, et non d'un
        /// parametre de type dont le nom pourrait changer d'une version de Revit a l'autre.
        ///
        /// Le critere est geometrique et sans ambiguite : la ligne de foulee d'une volee
        /// droite est un segment de droite unique. Des qu'elle comporte un arc, ou plusieurs
        /// segments non alignes, la volee est balancee ou helicoidale — et le moteur ne sait
        /// pas la calculer.
        ///
        /// Si la ligne de foulee ne peut pas etre lue, la forme reste INDETERMINEE : ce
        /// n'est pas la meme chose que droite, et le moteur le dira.
        /// </summary>
        private static StairFlightShape ResolveShape(Stairs stairs, StairData data)
        {
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null || runs.Count == 0) return StairFlightShape.Undetermined;

                if (runs.Count > 1)
                {
                    data.Remarks.Add(string.Format(
                        "L'escalier compte {0} volees. Une seule est calculee, et sa forme est " +
                        "celle de la premiere volee lue : relancez la commande pour les autres " +
                        "si leur geometrie differe.", runs.Count));
                }

                var shape = StairFlightShape.Undetermined;
                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;

                    CurveLoop path = run.GetStairsPath();
                    if (path == null) return StairFlightShape.Undetermined;

                    shape = ClassifyPath(path);
                    break;
                }
                return shape;
            }
            catch (Exception)
            {
                // Le type de volee peut refuser sa ligne de foulee : on ne devine pas.
                return StairFlightShape.Undetermined;
            }
        }

        /// <summary>Classe une ligne de foulee : droite, courbe, ou brisee.</summary>
        private static StairFlightShape ClassifyPath(CurveLoop path)
        {
            var segments = new List<Curve>();
            foreach (Curve curve in path) segments.Add(curve);
            if (segments.Count == 0) return StairFlightShape.Undetermined;

            foreach (Curve curve in segments)
            {
                // Un arc dans la ligne de foulee : la volee tourne.
                if (!(curve is Line)) return StairFlightShape.Spiral;
            }

            if (segments.Count == 1) return StairFlightShape.Straight;

            // Plusieurs segments droits : ils doivent tous etre paralleles, sinon la volee
            // est balancee.
            XYZ reference = segments[0].GetEndPoint(1) - segments[0].GetEndPoint(0);
            if (reference.GetLength() < Tolerance) return StairFlightShape.Undetermined;
            reference = reference.Normalize();

            for (int i = 1; i < segments.Count; i++)
            {
                XYZ direction = segments[i].GetEndPoint(1) - segments[i].GetEndPoint(0);
                if (direction.GetLength() < Tolerance) continue;
                if (Math.Abs(direction.Normalize().DotProduct(reference)) < 0.999)
                {
                    return StairFlightShape.Winder;
                }
            }

            return StairFlightShape.Straight;
        }

        private static double ReadRunWidth(Stairs stairs)
        {
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return 0.0;
                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;
                    double width = UnitConverter.FeetToMm(run.ActualRunWidth);
                    if (width > Tolerance) return width;
                }
            }
            catch (Exception)
            {
                // Le type de volee peut ne pas exposer sa largeur : l'appelant le dira.
            }
            return 0.0;
        }

        // ------------------------------------------------------------------
        // Paillasse modelisee par un plancher
        // ------------------------------------------------------------------

        private static RevitStair ReadFlightModelledAsFloor(Element element, StairData defaults,
                                                            out string error)
        {
            error = null;
            StairData data = Clone(defaults);
            data.Id = element.UniqueId;
            data.Name = ColumnReader.Describe(element);
            data.Mark = ColumnReader.ReadMark(element);

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                error = "La geometrie de ce plancher n'a pas pu etre lue.";
                return null;
            }

            double extentXMm = UnitConverter.FeetToMm(box.Max.X - box.Min.X);
            double extentYMm = UnitConverter.FeetToMm(box.Max.Y - box.Min.Y);
            double riseMm = UnitConverter.FeetToMm(box.Max.Z - box.Min.Z);
            bool alongX = extentXMm >= extentYMm;

            data.WidthMm = alongX ? extentYMm : extentXMm;
            data.Remarks.Add(string.Format(
                "Paillasse modelisee par un plancher : emprise {0:0} x {1:0} mm, denivele " +
                "d'enveloppe {2:0} mm. La largeur de volee est prise sur la plus petite " +
                "emprise.", extentXMm, extentYMm, riseMm));

            // Un plancher ne porte AUCUNE information de marche. Le lecteur ne fabrique donc
            // ni contremarche ni giron : il le dit, et la fenetre garde la main.
            data.Remarks.Add(
                "Un plancher ne porte aucune information de marche : le nombre de " +
                "contremarches, la hauteur de contremarche et le giron restent ceux saisis " +
                "dans la fenetre. Ce sont eux qui fixent la pente, donc le poids propre.");

            // Un plancher ne dit pas non plus si la volee est droite. On ne le suppose pas.
            data.Shape = StairFlightShape.Undetermined;

            var stair = new RevitStair
            {
                Data = data,
                Frame = new RevitElementFrame
                {
                    Host = element,
                    Origin = new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                    AxisX = alongX ? XYZ.BasisX : XYZ.BasisY,
                    AxisY = alongX ? XYZ.BasisY : XYZ.BasisX.Negate(),
                    AxisZ = XYZ.BasisZ
                },
                CanHostRebar = IsValidRebarHost(element)
            };

            if (stair.Frame.AxisX.CrossProduct(stair.Frame.AxisY)
                     .DotProduct(stair.Frame.AxisZ) < 0)
            {
                stair.Frame.AxisY = stair.Frame.AxisZ.CrossProduct(stair.Frame.AxisX).Normalize();
            }

            if (!stair.CanHostRebar)
            {
                stair.RebarHostMessage =
                    "Ce plancher n'accepte pas d'armatures : il doit etre un plancher " +
                    "STRUCTUREL en beton. Le calcul reste produit, mais les barres ne " +
                    "pourront pas etre posees.";
                data.Remarks.Add(stair.RebarHostMessage);
            }

            return stair;
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static RevitElementFrame BuildFrame(Stairs stairs, StairData data)
        {
            BoundingBoxXYZ box = stairs.get_BoundingBox(null);
            XYZ origin = box != null ? new XYZ(box.Min.X, box.Min.Y, box.Min.Z) : XYZ.Zero;
            bool alongX = box == null
                          || (box.Max.X - box.Min.X) >= (box.Max.Y - box.Min.Y);

            var frame = new RevitElementFrame
            {
                Host = stairs,
                Origin = origin,
                AxisX = alongX ? XYZ.BasisX : XYZ.BasisY,
                AxisY = alongX ? XYZ.BasisY : XYZ.BasisX.Negate(),
                AxisZ = XYZ.BasisZ
            };

            if (frame.AxisX.CrossProduct(frame.AxisY).DotProduct(frame.AxisZ) < 0)
            {
                frame.AxisY = frame.AxisZ.CrossProduct(frame.AxisX).Normalize();
            }
            return frame;
        }

        private static StairData Clone(StairData defaults)
        {
            var data = new StairData();
            if (defaults == null) return data;

            data.RiserHeightMm = defaults.RiserHeightMm;
            data.TreadDepthMm = defaults.TreadDepthMm;
            data.RiserCount = defaults.RiserCount;
            data.WaistThicknessMm = defaults.WaistThicknessMm;
            data.WidthMm = defaults.WidthMm;
            data.LandingThicknessMm = defaults.LandingThicknessMm;
            data.LandingSpanMm = defaults.LandingSpanMm;
            data.SpanKind = defaults.SpanKind;
            return data;
        }

        public static bool IsValidRebarHost(Element element)
        {
            return SlabReader.IsValidRebarHost(element);
        }

        public static string Describe(Element element)
        {
            return ColumnReader.Describe(element);
        }
    }
}
