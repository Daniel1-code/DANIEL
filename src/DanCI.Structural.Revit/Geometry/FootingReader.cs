using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit la geometrie utile d'une semelle isolee Revit et la traduit en
    /// <see cref="FootingData"/>.
    ///
    /// Le repere local produit suit la convention du moteur : X et Y sont les axes de la
    /// semelle en plan, l'origine est au centre de la semelle sur la sous-face, Z remonte.
    ///
    /// Le poteau porte n'est pas devine : il est cherche au-dessus de la semelle. S'il
    /// reste introuvable, les dimensions par defaut sont conservees et une remarque le
    /// signale, plutot que de laisser croire a une lecture reussie.
    /// </summary>
    public static class FootingReader
    {
        private const double Tolerance = 1e-6;

        public static RevitFooting TryRead(Element element, out string error)
        {
            error = null;
            var instance = element as FamilyInstance;
            if (instance == null)
            {
                error = "Seules les semelles isolees de type instance de famille sont prises " +
                        "en charge. Les radiers et les semelles filantes viendront plus tard.";
                return null;
            }

            if (!IsValidRebarHost(element))
            {
                error = "Cet element ne peut pas recevoir d'armatures (il doit etre une " +
                        "fondation structurelle en beton).";
                return null;
            }

            Transform transform = instance.GetTransform();
            Transform inverse = transform.Inverse;

            List<Solid> solids = CollectSolids(element);
            if (solids.Count == 0)
            {
                error = "Aucune geometrie solide n'a pu etre lue sur cette semelle.";
                return null;
            }

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

            if (maxX - minX < Tolerance || maxY - minY < Tolerance || maxZ - minZ < Tolerance)
            {
                error = "La geometrie de la semelle est degeneree.";
                return null;
            }

            var data = new FootingData
            {
                Id = element.UniqueId,
                Name = Describe(element),
                Mark = ColumnReader.ReadMark(element),
                WidthXMm = UnitConverter.FeetToMm(maxX - minX),
                WidthYMm = UnitConverter.FeetToMm(maxY - minY),
                ThicknessMm = UnitConverter.FeetToMm(maxZ - minZ)
            };

            // Une semelle a gradins ou en pyramide n'est pas un pave : l'enveloppe seule
            // surestimerait le volume et donc le poids propre.
            double boundingVolume = (maxX - minX) * (maxY - minY) * (maxZ - minZ);
            double actualVolume = 0.0;
            foreach (Solid solid in solids) actualVolume += solid.Volume;
            if (boundingVolume > Tolerance && actualVolume / boundingVolume < 0.95)
            {
                data.Remarks.Add(
                    "La semelle n'est pas un simple pave (gradins, chanfreins ou glacis). " +
                    "Le calcul la traite comme un pave de meme enveloppe, ce qui est " +
                    "securitaire pour la flexion mais surestime le poids propre.");
            }

            if (!TryReadSupportedColumn(instance, data))
            {
                data.Remarks.Add(
                    "Aucun poteau porte n'a ete trouve au-dessus de cette semelle. " +
                    "Les dimensions du poteau (" + data.ColumnWidthXMm.ToString("0") + " x " +
                    data.ColumnWidthYMm.ToString("0") + " mm) sont a saisir a la main : " +
                    "elles conditionnent le poinconnement et les debords.");
            }

            if (data.ThicknessMm < 200.0)
            {
                data.Remarks.Add("Epaisseur inhabituellement faible : verifiez la lecture.");
            }
            if (data.OverhangXMm <= 0 || data.OverhangYMm <= 0)
            {
                data.Remarks.Add("Le poteau deborde de la semelle : verifiez les dimensions.");
            }

            var frame = new RevitElementFrame
            {
                Host = element,
                // Origine : centre de la semelle, sur la sous-face.
                Origin = transform.OfPoint(new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, minZ)),
                AxisX = transform.BasisX.Normalize(),
                AxisY = transform.BasisY.Normalize(),
                AxisZ = transform.BasisZ.Normalize()
            };

            return new RevitFooting { Data = data, Frame = frame };
        }

        /// <summary>
        /// Cherche le poteau porte par la semelle et en reprend la section. Renvoie faux
        /// si aucun poteau n'a ete trouve, sans rien inventer.
        /// </summary>
        private static bool TryReadSupportedColumn(FamilyInstance footing, FootingData data)
        {
            try
            {
                BoundingBoxXYZ box = footing.get_BoundingBox(null);
                if (box == null) return false;

                // Une tranche mince juste au-dessus du dessus de la semelle.
                double slab = UnitConverter.MmToFeet(50.0);
                var outline = new Outline(
                    new XYZ(box.Min.X, box.Min.Y, box.Max.Z - slab),
                    new XYZ(box.Max.X, box.Max.Y, box.Max.Z + slab));

                var collector = new FilteredElementCollector(footing.Document)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .WherePasses(new BoundingBoxIntersectsFilter(outline));

                foreach (Element candidate in collector)
                {
                    string ignored;
                    RevitColumn column = ColumnReader.TryRead(candidate, out ignored);
                    if (column == null) continue;

                    if (column.Data.Shape == SectionShape.Circular)
                    {
                        // Le poinconnement d'un poteau circulaire n'est pas celui d'un
                        // rectangle : on ne fait pas passer l'un pour l'autre.
                        data.Remarks.Add(
                            "Le poteau porte est circulaire. Le perimetre de controle d'un " +
                            "poteau circulaire (art. 6.4.2(1)) n'est pas encore implemente : " +
                            "un carre equivalent est utilise, ce qui n'est pas conservatif.");
                        double side = column.Data.DiameterMm * 0.886;   // meme aire
                        data.ColumnWidthXMm = side;
                        data.ColumnWidthYMm = side;
                    }
                    else
                    {
                        data.ColumnWidthXMm = column.Data.WidthMm;
                        data.ColumnWidthYMm = column.Data.DepthMm;
                    }
                    return true;
                }
            }
            catch (Exception)
            {
                // Une lecture de contexte qui echoue ne doit pas empecher de lire la semelle.
            }

            return false;
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
