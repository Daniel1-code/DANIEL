using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit la geometrie utile d'une poutre Revit et la traduit en <see cref="BeamData"/>.
    ///
    /// Le repere local produit suit la convention du moteur : X le long de la portee depuis
    /// l'extremite de depart, Y en travers de l'ame, Z vers le haut depuis la sous-face.
    /// </summary>
    public static class BeamReader
    {
        private const double Tolerance = 1e-6;

        public static RevitBeam TryRead(Element element, out string error)
        {
            error = null;
            var instance = element as FamilyInstance;
            if (instance == null)
            {
                error = "L'element n'est pas une instance de famille.";
                return null;
            }

            if (!IsValidRebarHost(element))
            {
                error = "Cet element ne peut pas recevoir d'armatures (il doit etre une poutre " +
                        "structurelle en beton).";
                return null;
            }

            var location = instance.Location as LocationCurve;
            if (location == null || !(location.Curve is Line))
            {
                error = "Seules les poutres droites sont prises en charge.";
                return null;
            }

            Transform transform = instance.GetTransform();
            XYZ axisX = transform.BasisX.Normalize();
            XYZ curveDirection = ((Line)location.Curve).Direction.Normalize();
            if (Math.Abs(axisX.DotProduct(curveDirection)) < 0.99)
            {
                error = "Le repere de la famille ne suit pas l'axe de la poutre.";
                return null;
            }

            List<Solid> solids = CollectSolids(element);
            if (solids.Count == 0)
            {
                error = "Aucune geometrie solide n'a pu etre lue sur cette poutre.";
                return null;
            }

            Transform inverse = transform.Inverse;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (Solid solid in solids)
            {
                foreach (Edge edge in solid.Edges)
                {
                    foreach (XYZ worldPoint in edge.Tessellate())
                    {
                        XYZ p = inverse.OfPoint(worldPoint);
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Z < minZ) minZ = p.Z;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                        if (p.Z > maxZ) maxZ = p.Z;
                    }
                }
            }

            if (maxX - minX < Tolerance || maxZ - minZ < Tolerance)
            {
                error = "La geometrie de la poutre est degeneree.";
                return null;
            }

            var data = new BeamData
            {
                Id = element.UniqueId,
                Name = Describe(element),
                Mark = ColumnReader.ReadMark(element),
                Shape = BeamSectionShape.Rectangular,
                WebWidthMm = UnitConverter.FeetToMm(maxY - minY),
                HeightMm = UnitConverter.FeetToMm(maxZ - minZ),
                SpanMm = UnitConverter.FeetToMm(maxX - minX)
            };

            // Les parametres de section structurelle sont plus fiables que l'enveloppe quand
            // la poutre est coupee par un poteau ou une autre poutre.
            double width, height;
            if (TryGetLengthParameter(instance, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH,
                    new[] { "b", "Largeur", "Width" }, out width) &&
                IsPlausible(width, data.WebWidthMm))
            {
                data.WebWidthMm = width;
            }
            if (TryGetLengthParameter(instance, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT,
                    new[] { "h", "Hauteur", "Depth", "Height" }, out height) &&
                IsPlausible(height, data.HeightMm))
            {
                data.HeightMm = height;
            }

            if (data.WebWidthMm < 100.0 || data.HeightMm < 150.0)
            {
                data.Remarks.Add("Section inhabituellement petite : verifiez les dimensions lues.");
            }
            if (data.SpanMm < 500.0)
            {
                data.Remarks.Add("Portee inhabituellement courte : verifiez la longueur lue.");
            }

            var frame = new RevitElementFrame
            {
                Host = element,
                // Origine : extremite de depart, au milieu de l'ame, sur la sous-face.
                Origin = transform.OfPoint(new XYZ(minX, (minY + maxY) / 2.0, minZ)),
                AxisX = axisX,
                AxisY = transform.BasisY.Normalize(),
                AxisZ = transform.BasisZ.Normalize()
            };

            return new RevitBeam { Data = data, Frame = frame };
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

        private static bool IsPlausible(double parameterMm, double boundingMm)
        {
            return parameterMm > 50.0 && Math.Abs(parameterMm - boundingMm) < 20.0;
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

        private static bool TryGetLengthParameter(FamilyInstance instance, BuiltInParameter builtIn,
                                                  string[] names, out double valueMm)
        {
            valueMm = 0.0;
            Parameter parameter = instance.get_Parameter(builtIn);
            var symbol = instance.Document.GetElement(instance.GetTypeId()) as ElementType;
            if ((parameter == null || !parameter.HasValue) && symbol != null)
            {
                parameter = symbol.get_Parameter(builtIn);
            }

            if (parameter == null || !parameter.HasValue)
            {
                foreach (string name in names)
                {
                    parameter = instance.LookupParameter(name);
                    if (parameter != null && parameter.HasValue) break;
                    if (symbol != null)
                    {
                        parameter = symbol.LookupParameter(name);
                        if (parameter != null && parameter.HasValue) break;
                    }
                    parameter = null;
                }
            }

            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double)
            {
                return false;
            }

            valueMm = UnitConverter.FeetToMm(parameter.AsDouble());
            return valueMm > Tolerance;
        }
    }
}
