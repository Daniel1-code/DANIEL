using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit la geometrie utile d'un plancher Revit et la traduit en <see cref="SlabData"/>.
    ///
    /// Repere local produit : X suit la portee depuis le coin du panneau, Y traverse le
    /// panneau, l'origine est au coin sur la sous-face, Z remonte.
    ///
    /// Le sens porteur n'est pas devine au hasard : le parametre « Direction de portee »
    /// de Revit est lu s'il existe ; a defaut, la portee est prise dans la **plus courte**
    /// dimension du panneau, parce qu'une dalle porte par le plus court chemin. Dans les
    /// deux cas, la fenetre laisse corriger.
    /// </summary>
    public static class SlabReader
    {
        private const double Tolerance = 1e-6;

        public static RevitSlab TryRead(Element element, out string error)
        {
            error = null;
            if (element == null)
            {
                error = "Element nul.";
                return null;
            }

            if (element.Category == null
                || element.Category.Id.Value != (long)BuiltInCategory.OST_Floors)
            {
                error = "L'element n'est pas un plancher.";
                return null;
            }

            if (!IsValidRebarHost(element))
            {
                error = "Cet element ne peut pas recevoir d'armatures (il doit etre un " +
                        "plancher structurel en beton).";
                return null;
            }

            List<Solid> solids = CollectSolids(element);
            if (solids.Count == 0)
            {
                error = "Aucune geometrie solide n'a pu etre lue sur ce plancher.";
                return null;
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (Solid solid in solids)
            {
                foreach (Edge edge in solid.Edges)
                {
                    foreach (XYZ p in edge.Tessellate())
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Z < minZ) minZ = p.Z;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                        if (p.Z > maxZ) maxZ = p.Z;
                    }
                }
            }

            if (maxX - minX < Tolerance || maxY - minY < Tolerance || maxZ - minZ < Tolerance)
            {
                error = "La geometrie du plancher est degeneree.";
                return null;
            }

            double extentXMm = UnitConverter.FeetToMm(maxX - minX);
            double extentYMm = UnitConverter.FeetToMm(maxY - minY);

            var data = new SlabData
            {
                Id = element.UniqueId,
                Name = ColumnReader.Describe(element),
                Mark = ColumnReader.ReadMark(element),
                ThicknessMm = ReadThickness(element, UnitConverter.FeetToMm(maxZ - minZ)),
                SpanKind = SlabSpanKind.SimplySupported
            };

            // Sens porteur : parametre Revit s'il existe, sinon la plus courte dimension.
            bool spanAlongX = ResolveSpanAlongX(element, extentXMm, extentYMm, data);
            data.SpanMm = spanAlongX ? extentXMm : extentYMm;
            data.WidthMm = spanAlongX ? extentYMm : extentXMm;

            // Un plancher non rectangulaire (trémie, forme en L) n'est pas un panneau :
            // l'enveloppe surestime la portee et le calcul en bande n'a plus de sens.
            double boundingVolume = (maxX - minX) * (maxY - minY) * (maxZ - minZ);
            double actualVolume = 0.0;
            foreach (Solid solid in solids) actualVolume += solid.Volume;
            if (boundingVolume > Tolerance && actualVolume / boundingVolume < 0.95)
            {
                data.Remarks.Add(
                    "Le plancher n'est pas un panneau rectangulaire plein (tremie, forme en L, " +
                    "decrochement). La portee lue est celle de l'enveloppe : verifiez-la, et " +
                    "decoupez le plancher en panneaux si necessaire.");
            }

            if (data.ThicknessMm < 100.0)
            {
                data.Remarks.Add("Epaisseur inferieure a 100 mm : verifiez la lecture.");
            }
            if (!data.IsGenuinelyOneWay)
            {
                data.Remarks.Add(string.Format(
                    "Rapport de cotes {0:0.00} : le panneau porte probablement dans les deux " +
                    "sens. Le module ne traite que les dalles portant dans un sens.",
                    data.PanelAspectRatio));
            }

            var frame = new RevitElementFrame
            {
                Host = element,
                // Origine : coin du panneau, sur la sous-face.
                Origin = new XYZ(minX, minY, minZ),
                AxisX = spanAlongX ? XYZ.BasisX : XYZ.BasisY,
                AxisY = spanAlongX ? XYZ.BasisY : XYZ.BasisX.Negate(),
                AxisZ = XYZ.BasisZ
            };

            // Le repere doit rester direct : X ^ Y = Z.
            if (frame.AxisX.CrossProduct(frame.AxisY).DotProduct(frame.AxisZ) < 0)
            {
                frame.AxisY = frame.AxisZ.CrossProduct(frame.AxisX).Normalize();
            }

            // L'origine suit le sens porteur retenu : elle doit rester le coin depuis
            // lequel X et Y balayent le panneau.
            if (!spanAlongX)
            {
                frame.Origin = new XYZ(maxX, minY, minZ);
            }

            return new RevitSlab { Data = data, Frame = frame };
        }

        /// <summary>
        /// Renvoie vrai si la portee suit l'axe X du modele. Le parametre Revit prime ;
        /// a defaut, la dalle porte dans la plus courte dimension.
        /// </summary>
        private static bool ResolveSpanAlongX(Element element, double extentXMm, double extentYMm,
                                              SlabData data)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.FLOOR_PARAM_SPAN_DIRECTION);
            if (parameter != null && parameter.HasValue
                && parameter.StorageType == StorageType.Double)
            {
                // L'angle est mesure depuis l'axe X du modele.
                double angle = parameter.AsDouble();
                double cos = Math.Abs(Math.Cos(angle));
                bool alongX = cos >= 0.7071;
                data.Remarks.Add(string.Format(
                    "Sens porteur lu dans le parametre Revit : {0:0} degres depuis l'axe X.",
                    UnitConverter.RadiansToDegrees(angle)));
                return alongX;
            }

            bool shortIsX = extentXMm <= extentYMm;
            data.Remarks.Add(
                "Aucun sens porteur n'est renseigne sur le plancher : la portee est prise " +
                "dans la plus courte dimension, parce qu'une dalle porte par le plus court " +
                "chemin. Corrigez-le dans la fenetre si l'hypothese est fausse.");
            return shortIsX;
        }

        private static double ReadThickness(Element element, double boundingThicknessMm)
        {
            try
            {
                var type = element.Document.GetElement(element.GetTypeId()) as HostObjAttributes;
                if (type != null)
                {
                    CompoundStructure structure = type.GetCompoundStructure();
                    if (structure != null)
                    {
                        double width = UnitConverter.FeetToMm(structure.GetWidth());
                        if (width > Tolerance) return width;
                    }
                }
            }
            catch (Exception)
            {
                // La structure composee peut etre absente : l'enveloppe fera l'affaire.
            }

            return boundingThicknessMm;
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
