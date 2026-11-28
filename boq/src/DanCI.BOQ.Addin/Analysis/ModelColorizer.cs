using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Vue 3D « DanCI — BOQ par … » ou chaque element metre est colore (lot, statut ou prix) et
    /// le reste du modele est estompe. MODIFIE le modele (une vue et ses remplacements
    /// graphiques, sans toucher aux elements) : une seule transaction, annulable par Ctrl+Z.
    /// </summary>
    internal static class ModelColorizer
    {
        public static string ViewName(ColoringMode mode)
        {
            switch (mode)
            {
                case ColoringMode.Reliability: return "DanCI — BOQ par statut";
                case ColoringMode.Price: return "DanCI — BOQ par prix";
                default: return "DanCI — BOQ par lot";
            }
        }

        public static string Apply(UIDocument uidoc, BoqResult boq, ColoringMode mode)
        {
            Document doc = uidoc.Document;
            ColoringResult coloring = ModelColoring.Build(boq, mode);
            FillPatternElement solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
            if (solid == null)
            {
                return "Aucun motif de remplissage plein dans le projet : vue colorée impossible.";
            }
            ViewFamilyType type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                return "Aucun type de vue 3D dans le projet : vue colorée impossible.";
            }

            string name = ViewName(mode);
            View3D view;
            int colored = 0, ghosted = 0, missing = 0;
            using (var t = new Transaction(doc, "DanCI BOQ — " + name))
            {
                t.Start();
                view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name == name);
                if (view == null)
                {
                    view = View3D.CreateIsometric(doc, type.Id);
                    view.Name = name;
                }
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId; // un gabarit bloquerait les remplacements
                }
                view.DisplayStyle = DisplayStyle.ShadingWithEdges;
                view.DetailLevel = ViewDetailLevel.Medium;
                doc.Regenerate();

                // Tout le modele visible : estompe (contexte), sauf les elements metres, colores.
                foreach (Element e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
                {
                    if (e.Category == null || e.Category.CategoryType != CategoryType.Model) continue;
                    if (coloring.Colors.ContainsKey(e.Id.Value)) continue;
                    var ghost = new OverrideGraphicSettings();
                    ghost.SetHalftone(true);
                    ghost.SetSurfaceTransparency(70);
                    view.SetElementOverrides(e.Id, ghost);
                    ghosted++;
                }
                foreach (var kv in coloring.Colors)
                {
                    var id = new ElementId(kv.Key);
                    if (doc.GetElement(id) == null)
                    {
                        missing++;
                        continue;
                    }
                    Color c = ToRevit(kv.Value);
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetSurfaceForegroundPatternId(solid.Id);
                    ogs.SetSurfaceForegroundPatternColor(c);
                    ogs.SetCutForegroundPatternId(solid.Id);
                    ogs.SetCutForegroundPatternColor(c);
                    view.SetElementOverrides(id, ogs);
                    colored++;
                }
                t.Commit();
            }
            try
            {
                uidoc.RequestViewChange(view);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                BoqLog.Warn("Activation de la vue colorée impossible : " + ex.Message);
            }
            string legend = string.Join(" · ", coloring.Legend.Select(l => l.Label + " (" + l.Count + ")"));
            BoqLog.Info("Vue colorée « " + name + " » : " + colored + " élément(s) coloré(s), " + ghosted + " estompé(s).");
            return "Vue 3D « " + name + " » : " + colored + " élément(s) coloré(s) — " + legend +
                   (coloring.LinkedElements > 0 ? " ; " + coloring.LinkedElements + " élément(s) de liens non colorés" : string.Empty) +
                   (missing > 0 ? " ; " + missing + " supprimé(s) depuis l'analyse" : string.Empty) + ". Annulable (Ctrl+Z).";
        }

        private static Color ToRevit(string hex)
        {
            string h = hex.TrimStart('#');
            return new Color(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
        }
    }
}
