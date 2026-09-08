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

            // Un plancher ne porte pas de ligne de foulee : le repere est celui de
            // l'enveloppe, avec l'origine au coin depuis lequel Y croit vers l'interieur.
            data.Remarks.Add(
                "Le repere des armatures est construit sur l'enveloppe du plancher : il " +
                "suppose une paillasse alignee sur un axe du modele et montant vers les " +
                "coordonnees croissantes. VERIFIEZ LA POSITION DES BARRES apres generation.");

            var stair = new RevitStair
            {
                Data = data,
                Frame = FallbackFrame(element, box),
                CanHostRebar = IsValidRebarHost(element)
            };

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

        /// <summary>
        /// Construit le repere de la volee A PARTIR DE SA LIGNE DE FOULEE, et non de sa
        /// boite englobante.
        ///
        /// Une boite englobante ne connait ni le SENS de la montee ni le depart de la
        /// sous-face : elle ne donne qu'un coin, et choisir l'axe sur la plus grande
        /// dimension place les armatures n'importe ou des que l'escalier est tourne ou
        /// monte vers les X ou les Y decroissants. La ligne de foulee, elle, part du pied
        /// de la volee et pointe vers le haut : c'est exactement l'axe X du repere local.
        ///
        /// L'origine est ramenee au BORD de la volee, a une demi-largeur du milieu de la
        /// ligne de foulee, parce que le constructeur de plan compte les Y depuis le bord.
        /// </summary>
        private static RevitElementFrame BuildFrame(Stairs stairs, StairData data)
        {
            BoundingBoxXYZ box = stairs.get_BoundingBox(null);
            double baseZ = box != null ? box.Min.Z : 0.0;

            XYZ start, direction;
            if (TryReadAscent(stairs, out start, out direction))
            {
                XYZ axisX = direction;
                XYZ axisY = XYZ.BasisZ.CrossProduct(axisX).Normalize();
                XYZ origin = new XYZ(start.X, start.Y, baseZ)
                             - axisY.Multiply(UnitConverter.MmToFeet(data.WidthMm) / 2.0);

                return new RevitElementFrame
                {
                    Host = stairs,
                    Origin = origin,
                    AxisX = axisX,
                    AxisY = axisY,
                    AxisZ = XYZ.BasisZ
                };
            }

            data.Remarks.Add(
                "La ligne de foulee n'a pas pu etre lue : le repere des armatures est " +
                "construit sur la boite englobante, ce qui suppose une volee alignee sur un " +
                "axe du modele et montant vers les coordonnees croissantes. VERIFIEZ LA " +
                "POSITION DES BARRES apres generation.");

            return FallbackFrame(stairs, box);
        }

        /// <summary>
        /// Sens de la montee : point de depart et direction horizontale de la ligne de
        /// foulee. Elle est orientee du bas vers le haut de la volee ; le controle sur les
        /// altitudes le confirme plutot que de le supposer.
        /// </summary>
        private static bool TryReadAscent(Stairs stairs, out XYZ start, out XYZ direction)
        {
            start = null;
            direction = null;
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return false;

                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;

                    CurveLoop path = run.GetStairsPath();
                    if (path == null) continue;

                    Curve first = null;
                    Curve last = null;
                    foreach (Curve curve in path)
                    {
                        if (first == null) first = curve;
                        last = curve;
                    }
                    if (first == null) return false;

                    XYZ a = first.GetEndPoint(0);
                    XYZ b = last.GetEndPoint(1);

                    // La ligne de foulee peut etre stockee dans un sens ou dans l'autre :
                    // c'est l'altitude qui dit lequel monte.
                    if (b.Z < a.Z)
                    {
                        XYZ swap = a;
                        a = b;
                        b = swap;
                    }

                    XYZ horizontal = new XYZ(b.X - a.X, b.Y - a.Y, 0.0);
                    if (horizontal.GetLength() < Tolerance) return false;

                    start = a;
                    direction = horizontal.Normalize();
                    return true;
                }
            }
            catch (Exception)
            {
                // Le type de volee peut refuser sa ligne de foulee : l'appelant le dira.
            }
            return false;
        }

        /// <summary>
        /// Repere de secours, sur la boite englobante. Il reste faux pour une volee
        /// tournee, mais au moins il est COHERENT : l'origine est le coin depuis lequel X
        /// et Y balayent reellement l'emprise.
        /// </summary>
        private static RevitElementFrame FallbackFrame(Element element, BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return new RevitElementFrame
                {
                    Host = element,
                    Origin = XYZ.Zero,
                    AxisX = XYZ.BasisX,
                    AxisY = XYZ.BasisY,
                    AxisZ = XYZ.BasisZ
                };
            }

            bool alongX = (box.Max.X - box.Min.X) >= (box.Max.Y - box.Min.Y);
            XYZ axisX = alongX ? XYZ.BasisX : XYZ.BasisY;
            XYZ axisY = XYZ.BasisZ.CrossProduct(axisX).Normalize();

            // L'origine doit etre le coin depuis lequel Y local croit VERS L'INTERIEUR de
            // l'emprise. Avec AxisX = +Y, AxisY vaut -X : l'origine est alors du cote
            // Max.X, et non Min.X — c'est ce signe qui envoyait les barres hors du beton.
            double originX = axisY.X < 0 ? box.Max.X : box.Min.X;
            double originY = axisY.Y < 0 ? box.Max.Y : box.Min.Y;

            return new RevitElementFrame
            {
                Host = element,
                Origin = new XYZ(originX, originY, box.Min.Z),
                AxisX = axisX,
                AxisY = axisY,
                AxisZ = XYZ.BasisZ
            };
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
