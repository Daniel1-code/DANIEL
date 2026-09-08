using System;
using Autodesk.Revit.DB;
using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit une longrine Revit et la traduit en <see cref="GradeBeamData"/>.
    ///
    /// Une longrine est modelisee dans Revit comme une poutre structurelle : la lecture de
    /// geometrie est donc celle de <see cref="BeamReader"/>, et il n'y a aucune raison de
    /// la reecrire. Ce qui change, c'est **l'interpretation** : la meme geometrie devient
    /// un element de fondation, avec son enrobage propre, ses deux nappes filantes et son
    /// effort de liaison.
    ///
    /// Ce lecteur ne devine pas non plus qu'une poutre est une longrine. C'est
    /// l'utilisateur qui le declare en lancant la commande, et le lecteur se contente de
    /// signaler ce qui ne colle pas avec cette declaration.
    /// </summary>
    public static class GradeBeamReader
    {
        public static RevitGradeBeam TryRead(Element element, out string error)
        {
            RevitBeam beam = BeamReader.TryRead(element, out error);
            if (beam == null) return null;

            var data = new GradeBeamData
            {
                Id = beam.Data.Id,
                Name = beam.Data.Name,
                Mark = beam.Data.Mark,
                WidthMm = beam.Data.WebWidthMm,
                HeightMm = beam.Data.HeightMm,
                SpanMm = beam.Data.SpanMm,
                SpanKind = GradeBeamSpanKind.SimplySupported
            };

            foreach (string remark in beam.Data.Remarks) data.Remarks.Add(remark);
            AddRemarks(element, data);

            return new RevitGradeBeam { Data = data, Frame = beam.Frame };
        }

        private static void AddRemarks(Element element, GradeBeamData data)
        {
            // Une longrine se trouve au niveau des fondations. Une poutre situee en
            // etage a probablement ete selectionnee par erreur.
            if (IsWellAboveTheLowestLevel(element))
            {
                data.Remarks.Add(
                    "Cet element ne se situe pas au niveau le plus bas du projet. Une " +
                    "longrine est un element de fondation : verifiez la selection, ou " +
                    "utilisez le module Beam.");
            }

            if (data.SpanToDepth > 20.0)
            {
                data.Remarks.Add(string.Format(
                    "Portee sur hauteur = {0:0.0}. Au-dela de 20, l'element est tres elance " +
                    "pour une longrine : verifiez la portee lue.", data.SpanToDepth));
            }
        }

        private static bool IsWellAboveTheLowestLevel(Element element)
        {
            try
            {
                Document document = element.Document;
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null) return false;

                double lowest = double.MaxValue;
                var collector = new FilteredElementCollector(document)
                    .OfClass(typeof(Level));
                foreach (Element item in collector)
                {
                    var level = item as Level;
                    if (level != null && level.Elevation < lowest) lowest = level.Elevation;
                }

                if (lowest == double.MaxValue) return false;

                // Plus de trois metres au-dessus du niveau le plus bas : ce n'est pas une
                // longrine.
                return box.Min.Z - lowest > Core.Units.UnitConverter.MmToFeet(3000.0);
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
