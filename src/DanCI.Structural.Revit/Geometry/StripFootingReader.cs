using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;
using RevitWallElement = Autodesk.Revit.DB.Wall;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit une semelle filante Revit (<c>WallFoundation</c>) et la traduit en
    /// <see cref="StripFootingData"/>.
    ///
    /// Repere local produit : X traverse la semelle, Y suit sa longueur depuis
    /// l'extremite de depart, Z remonte. L'origine est au milieu de la largeur, a
    /// l'extremite de depart, sur la sous-face.
    ///
    /// L'epaisseur du voile porte est lue sur le mur hote, pas devinee : c'est elle qui
    /// fixe le debord, donc le moment de la console.
    /// </summary>
    public static class StripFootingReader
    {
        private const double Tolerance = 1e-6;

        public static RevitStripFooting TryRead(Element element, out string error)
        {
            error = null;
            var foundation = element as WallFoundation;
            if (foundation == null)
            {
                error = "L'element n'est pas une semelle filante. Les semelles isolees " +
                        "relevent du module Footing.";
                return null;
            }

            if (!IsValidRebarHost(element))
            {
                error = "Cette semelle ne peut pas recevoir d'armatures (elle doit etre une " +
                        "fondation structurelle en beton).";
                return null;
            }

            List<Solid> solids = CollectSolids(element);
            if (solids.Count == 0)
            {
                error = "Aucune geometrie solide n'a pu etre lue sur cette semelle.";
                return null;
            }

            RevitWallElement wall = ReadHostWall(foundation);
            if (wall == null)
            {
                error = "Le mur porte n'a pas pu etre lu : la semelle n'est pas exploitable " +
                        "sans l'epaisseur du voile, qui fixe le debord.";
                return null;
            }

            var line = wall.Location is LocationCurve
                ? ((LocationCurve)wall.Location).Curve as Line
                : null;
            if (line == null)
            {
                error = "Seules les semelles filantes rectilignes sont prises en charge.";
                return null;
            }

            XYZ direction = line.Direction.Normalize();
            XYZ across = XYZ.BasisZ.CrossProduct(direction).Normalize();

            // Enveloppe exprimee dans le repere de la semelle.
            double minAcross = double.MaxValue, maxAcross = double.MinValue;
            double minAlong = double.MaxValue, maxAlong = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            XYZ origin = line.GetEndPoint(0);

            foreach (Solid solid in solids)
            {
                foreach (Edge edge in solid.Edges)
                {
                    foreach (XYZ p in edge.Tessellate())
                    {
                        XYZ local = p - origin;
                        double a = local.DotProduct(across);
                        double b = local.DotProduct(direction);
                        if (a < minAcross) minAcross = a;
                        if (a > maxAcross) maxAcross = a;
                        if (b < minAlong) minAlong = b;
                        if (b > maxAlong) maxAlong = b;
                        if (p.Z < minZ) minZ = p.Z;
                        if (p.Z > maxZ) maxZ = p.Z;
                    }
                }
            }

            double width = UnitConverter.FeetToMm(maxAcross - minAcross);
            double length = UnitConverter.FeetToMm(maxAlong - minAlong);
            double thickness = UnitConverter.FeetToMm(maxZ - minZ);
            if (width < Tolerance || length < Tolerance || thickness < Tolerance)
            {
                error = "La geometrie de la semelle est degeneree.";
                return null;
            }

            var data = new StripFootingData
            {
                Id = element.UniqueId,
                Name = ColumnReader.Describe(element),
                Mark = ColumnReader.ReadMark(element),
                WidthMm = width,
                ThicknessMm = thickness,
                LengthMm = length,
                WallThicknessMm = ReadWallThickness(wall)
            };

            AddRemarks(foundation, wall, data);

            var frame = new RevitElementFrame
            {
                Host = element,
                // Origine : milieu de la largeur, extremite de depart, sous-face.
                Origin = origin + across * (minAcross + maxAcross) / 2.0
                         + direction * minAlong + XYZ.BasisZ * (minZ - origin.Z),
                AxisX = across,
                AxisY = direction,
                AxisZ = XYZ.BasisZ
            };

            // Le repere doit rester direct : X ^ Y = Z.
            if (frame.AxisX.CrossProduct(frame.AxisY).DotProduct(frame.AxisZ) < 0)
            {
                frame.AxisX = frame.AxisY.CrossProduct(frame.AxisZ).Normalize();
            }

            return new RevitStripFooting { Data = data, Frame = frame };
        }

        private static void AddRemarks(WallFoundation foundation, RevitWallElement wall,
                                       StripFootingData data)
        {
            if (data.OverhangMm <= 0)
            {
                data.Remarks.Add(string.Format(
                    "Le voile ({0:0} mm) est aussi large ou plus large que la semelle " +
                    "({1:0} mm) : verifiez les dimensions lues.",
                    data.WallThicknessMm, data.WidthMm));
            }
            else if (!data.IsRigid)
            {
                data.Remarks.Add(string.Format(
                    "Debord de {0:0} mm pour {1:0} mm d'epaisseur, soit un rapport de {2:0.0}. " +
                    "Au-dela de 2, la semelle n'est plus rigide et le modele de console " +
                    "encastree perd sa validite.",
                    data.OverhangMm, data.ThicknessMm, data.OverhangMm / data.ThicknessMm));
            }

            if (HasSupportedColumns(foundation))
            {
                data.Remarks.Add(
                    "Des poteaux prennent appui au droit de cette semelle. Le module ne " +
                    "verifie PAS leur poinconnement : la charge y est concentree, pas " +
                    "repartie. Traitez-les separement.");
            }
        }

        /// <summary>
        /// Cherche des poteaux prenant appui sur la semelle. Leur presence change la nature
        /// du probleme : une charge concentree poinconne, une charge de voile non.
        /// </summary>
        private static bool HasSupportedColumns(WallFoundation foundation)
        {
            try
            {
                BoundingBoxXYZ box = foundation.get_BoundingBox(null);
                if (box == null) return false;

                double slab = UnitConverter.MmToFeet(50.0);
                var outline = new Outline(
                    new XYZ(box.Min.X, box.Min.Y, box.Max.Z - slab),
                    new XYZ(box.Max.X, box.Max.Y, box.Max.Z + slab));

                var collector = new FilteredElementCollector(foundation.Document)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .WherePasses(new BoundingBoxIntersectsFilter(outline));

                foreach (Element candidate in collector) return true;
            }
            catch (Exception)
            {
                // Une lecture de contexte qui echoue ne doit pas empecher de lire la semelle.
            }

            return false;
        }

        private static RevitWallElement ReadHostWall(WallFoundation foundation)
        {
            try
            {
                ElementId wallId = foundation.WallId;
                if (wallId != null && wallId != ElementId.InvalidElementId)
                {
                    return foundation.Document.GetElement(wallId) as RevitWallElement;
                }
            }
            catch (Exception)
            {
                // La propriete peut ne pas etre disponible sur certaines versions.
            }

            return null;
        }

        private static double ReadWallThickness(RevitWallElement wall)
        {
            try
            {
                if (wall.Width > Tolerance) return UnitConverter.FeetToMm(wall.Width);
            }
            catch (Exception)
            {
                // Certains murs empiles n'exposent pas Width.
            }

            Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
            if (parameter != null && parameter.HasValue
                && parameter.StorageType == StorageType.Double)
            {
                return UnitConverter.FeetToMm(parameter.AsDouble());
            }

            return 200.0;
        }

        public static bool IsValidRebarHost(Element element)
        {
            if (element == null) return false;
            try
            {
                RebarHostData host = RebarHostData.GetRebarHostData(element);
                return host != null && host.IsValidHost();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string Describe(Element element)
        {
            return ColumnReader.Describe(element);
        }

        private static List<Solid> CollectSolids(Element element)
        {
            var solids = new List<Solid>();
            var options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false,
                IncludeNonVisibleObjects = false
            };
            GeometryElement geometry = element.get_Geometry(options);
            if (geometry != null) Harvest(geometry, solids);
            return solids;
        }

        private static void Harvest(GeometryElement geometry, List<Solid> solids)
        {
            foreach (GeometryObject item in geometry)
            {
                var solid = item as Solid;
                if (solid != null)
                {
                    if (solid.Volume > Tolerance && solid.Edges.Size > 0) solids.Add(solid);
                    continue;
                }
                var instance = item as GeometryInstance;
                if (instance != null)
                {
                    GeometryElement nested = instance.GetInstanceGeometry();
                    if (nested != null) Harvest(nested, solids);
                }
            }
        }
    }
}
