using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.RevitOps
{
    /// <summary>
    /// Lit la geometrie utile d'un poteau Revit : type de section, dimensions, hauteur
    /// et repere local. Fonctionne avec les familles de poteaux structurels beton,
    /// rectangulaires ou circulaires, y compris tournees en plan.
    /// </summary>
    public static class ColumnInspector
    {
        private const double Tolerance = 1e-6;

        /// <summary>
        /// Tente de construire la geometrie exploitable d'un element.
        /// Renvoie null et renseigne <paramref name="error"/> si l'element ne convient pas.
        /// </summary>
        public static ColumnGeometry TryExtract(Element element, out string error)
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
                error = "Cet element ne peut pas recevoir d'armatures (il doit etre un poteau " +
                        "structurel en beton).";
                return null;
            }

            Parameter slanted = instance.get_Parameter(BuiltInParameter.SLANTED_COLUMN_TYPE_PARAM);
            if (slanted != null && slanted.HasValue && slanted.AsInteger() != 0)
            {
                error = "Les poteaux inclines ne sont pas pris en charge.";
                return null;
            }

            Transform transform = instance.GetTransform();
            XYZ axisZ = transform.BasisZ.Normalize();
            if (Math.Abs(Math.Abs(axisZ.Z) - 1.0) > 1e-3)
            {
                error = "L'axe du poteau n'est pas vertical.";
                return null;
            }

            var solids = CollectSolids(element);
            if (solids.Count == 0)
            {
                error = "Aucune geometrie solide n'a pu etre lue sur ce poteau.";
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

            if (minX > maxX || maxZ - minZ < Tolerance)
            {
                error = "La geometrie du poteau est degeneree.";
                return null;
            }

            var geometry = new ColumnGeometry
            {
                Host = element,
                HostName = DescribeElement(element),
                HeightMm = Units.FeetToMm(maxZ - minZ),
                AxisX = transform.BasisX.Normalize(),
                AxisY = transform.BasisY.Normalize(),
                AxisZ = axisZ
            };

            // Origine du repere local : centre de la section, au niveau bas du poteau.
            XYZ localBase = new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, minZ);
            geometry.BasePoint = transform.OfPoint(localBase);

            double radiusMm;
            if (TryGetCircularRadius(instance, solids, axisZ, out radiusMm))
            {
                geometry.Kind = SectionKind.Circular;
                geometry.DiameterMm = 2.0 * radiusMm;
            }
            else
            {
                geometry.Kind = SectionKind.Rectangular;
                geometry.WidthMm = Units.FeetToMm(maxX - minX);
                geometry.DepthMm = Units.FeetToMm(maxY - minY);

                // Les parametres de section structurelle sont plus fiables que l'enveloppe
                // quand le poteau est coupe par une poutre ou une dalle.
                double w, d;
                if (TryGetLengthParameter(instance, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH,
                        new[] { "b", "Largeur", "Width" }, out w) &&
                    TryGetLengthParameter(instance, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT,
                        new[] { "h", "Hauteur", "Depth", "Height" }, out d))
                {
                    if (IsPlausibleSection(w, geometry.WidthMm) && IsPlausibleSection(d, geometry.DepthMm))
                    {
                        geometry.WidthMm = w;
                        geometry.DepthMm = d;
                    }
                    else if (IsPlausibleSection(d, geometry.WidthMm) && IsPlausibleSection(w, geometry.DepthMm))
                    {
                        // La famille peut orienter b suivant Y.
                        geometry.WidthMm = d;
                        geometry.DepthMm = w;
                    }
                }
            }

            if (geometry.MinDimensionMm < 100.0)
            {
                geometry.Remarks.Add("Section inhabituellement petite : verifiez les dimensions lues.");
            }
            return geometry;
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

        public static string DescribeElement(Element element)
        {
            string typeName = string.Empty;
            Document doc = element.Document;
            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element type = doc.GetElement(typeId);
                if (type != null) typeName = type.Name;
            }
            string mark = string.Empty;
            Parameter markParam = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (markParam != null && markParam.HasValue) mark = markParam.AsString();

            string label = string.IsNullOrWhiteSpace(mark) ? typeName : mark + " (" + typeName + ")";
            if (string.IsNullOrWhiteSpace(label)) label = "Poteau";
            return string.Format("{0} [{1}]", label, element.Id);
        }

        private static bool IsPlausibleSection(double parameterMm, double boundingMm)
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
            if (geometry == null) return solids;
            Harvest(geometry, solids);
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

        /// <summary>Detecte une section circulaire, d'abord par parametre puis par geometrie.</summary>
        private static bool TryGetCircularRadius(FamilyInstance instance, List<Solid> solids, XYZ axis,
                                                 out double radiusMm)
        {
            double diameter;
            if (TryGetLengthParameter(instance, BuiltInParameter.STRUCTURAL_SECTION_COMMON_DIAMETER,
                    new[] { "Diameter", "Diametre", "Diamètre", "d" }, out diameter) && diameter > 50.0)
            {
                radiusMm = diameter / 2.0;
                return true;
            }

            foreach (Solid solid in solids)
            {
                foreach (Face face in solid.Faces)
                {
                    var cylinder = face as CylindricalFace;
                    if (cylinder == null) continue;
                    if (Math.Abs(cylinder.Axis.Normalize().DotProduct(axis)) < 0.99) continue;
                    radiusMm = Units.FeetToMm(cylinder.get_Radius(0).GetLength());
                    if (radiusMm > 25.0) return true;
                }
            }

            radiusMm = 0.0;
            return false;
        }

        private static bool TryGetLengthParameter(FamilyInstance instance, BuiltInParameter builtIn,
                                                  string[] names, out double valueMm)
        {
            valueMm = 0.0;
            Parameter parameter = instance.get_Parameter(builtIn);
            ElementType symbol = instance.Document.GetElement(instance.GetTypeId()) as ElementType;
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

            valueMm = Units.FeetToMm(parameter.AsDouble());
            return valueMm > Tolerance;
        }
    }
}
