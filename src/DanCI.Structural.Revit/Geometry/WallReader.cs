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
    /// Lit la geometrie utile d'un mur Revit et la traduit en <see cref="WallData"/>.
    ///
    /// Repere local produit : X suit la longueur du voile depuis son extremite de depart,
    /// Y traverse l'epaisseur, Z monte. L'origine est a l'extremite de depart, au milieu
    /// de l'epaisseur, sur la base du voile.
    ///
    /// Un mur Revit n'est pas forcement un voile en beton arme : les murs non porteurs,
    /// non structurels, courbes ou multicouches sont ecartes explicitement plutot que
    /// calcules avec des hypotheses fausses.
    /// </summary>
    public static class WallReader
    {
        private const double Tolerance = 1e-6;

        public static RevitWall TryRead(Element element, out string error)
        {
            error = null;
            var wall = element as RevitWallElement;
            if (wall == null)
            {
                error = "L'element n'est pas un mur.";
                return null;
            }

            var location = wall.Location as LocationCurve;
            if (location == null)
            {
                error = "Le mur n'a pas de ligne de base exploitable.";
                return null;
            }

            var line = location.Curve as Line;
            if (line == null)
            {
                error = "Seuls les murs droits sont pris en charge. Un voile courbe demande " +
                        "un decoupage en panneaux droits.";
                return null;
            }

            if (!IsValidRebarHost(element))
            {
                error = "Ce mur ne peut pas recevoir d'armatures (il doit etre structurel " +
                        "et en beton).";
                return null;
            }

            double thickness = ReadThickness(wall);
            if (thickness <= Tolerance)
            {
                error = "L'epaisseur du mur n'a pas pu etre lue.";
                return null;
            }

            double length = UnitConverter.FeetToMm(line.Length);
            double height = ReadClearHeight(wall);
            if (height <= Tolerance)
            {
                error = "La hauteur du mur n'a pas pu etre lue.";
                return null;
            }

            var data = new WallData
            {
                Id = element.UniqueId,
                Name = ColumnReader.Describe(element),
                Mark = ColumnReader.ReadMark(element),
                ThicknessMm = thickness,
                LengthMm = length,
                ClearHeightMm = height
            };

            AddRemarks(wall, data);

            XYZ start = line.GetEndPoint(0);
            XYZ direction = line.Direction.Normalize();
            XYZ up = XYZ.BasisZ;
            XYZ across = up.CrossProduct(direction).Normalize();

            var frame = new RevitElementFrame
            {
                Host = element,
                // Origine : extremite de depart, au milieu de l'epaisseur, en base.
                Origin = new XYZ(start.X, start.Y, start.Z),
                AxisX = direction,
                AxisY = across,
                AxisZ = up
            };

            return new RevitWall { Data = data, Frame = frame };
        }

        private static void AddRemarks(RevitWallElement wall, WallData data)
        {
            // Un mur multicouche n'a pas une epaisseur structurelle unique : l'epaisseur
            // lue est celle de la couche porteuse si elle est identifiable.
            try
            {
                var type = wall.Document.GetElement(wall.GetTypeId()) as WallType;
                if (type != null)
                {
                    CompoundStructure structure = type.GetCompoundStructure();
                    if (structure != null && structure.LayerCount > 1)
                    {
                        data.Remarks.Add(
                            "Mur multicouche : seule la couche structurelle est prise en " +
                            "compte. Verifiez l'epaisseur retenue.");
                    }
                }
            }
            catch (Exception)
            {
                // La structure composee peut etre absente sur certains types de murs.
            }

            if (!data.IsWallByCode)
            {
                data.Remarks.Add(string.Format(
                    "Longueur {0:0} mm pour {1:0} mm d'epaisseur, soit un rapport de {2:0.0}. " +
                    "L'article 9.6.1 demande au moins 4 : cet element releve du module Column.",
                    data.LengthMm, data.ThicknessMm, data.LengthMm / data.ThicknessMm));
            }

            if (HasOpenings(wall))
            {
                data.Remarks.Add(
                    "Le voile comporte des ouvertures. Le calcul les ignore : la longueur " +
                    "retenue est la longueur totale, et aucun chainage de trumeau ni " +
                    "renfort de linteau n'est dimensionne.");
            }
        }

        private static bool HasOpenings(RevitWallElement wall)
        {
            try
            {
                var collector = new FilteredElementCollector(wall.Document)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType();
                foreach (Element door in collector)
                {
                    var instance = door as FamilyInstance;
                    if (instance != null && instance.Host != null
                        && instance.Host.Id == wall.Id)
                    {
                        return true;
                    }
                }

                collector = new FilteredElementCollector(wall.Document)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType();
                foreach (Element window in collector)
                {
                    var instance = window as FamilyInstance;
                    if (instance != null && instance.Host != null
                        && instance.Host.Id == wall.Id)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Une lecture de contexte qui echoue ne doit pas empecher de lire le mur.
            }

            return false;
        }

        private static double ReadThickness(RevitWallElement wall)
        {
            try
            {
                if (wall.Width > Tolerance) return UnitConverter.FeetToMm(wall.Width);
            }
            catch (Exception)
            {
                // Certains murs empilés n'exposent pas Width.
            }

            Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
            if (parameter != null && parameter.HasValue
                && parameter.StorageType == StorageType.Double)
            {
                return UnitConverter.FeetToMm(parameter.AsDouble());
            }

            return 0.0;
        }

        private static double ReadClearHeight(RevitWallElement wall)
        {
            Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (parameter != null && parameter.HasValue
                && parameter.StorageType == StorageType.Double)
            {
                double height = UnitConverter.FeetToMm(parameter.AsDouble());
                if (height > Tolerance) return height;
            }

            BoundingBoxXYZ box = wall.get_BoundingBox(null);
            if (box != null) return UnitConverter.FeetToMm(box.Max.Z - box.Min.Z);
            return 0.0;
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
    }
}
