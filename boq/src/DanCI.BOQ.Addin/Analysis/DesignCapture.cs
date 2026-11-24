using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Export;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Images de la conception pour le rapport (PDF et Excel) : une vue 3D ombree du modele et le plan
    /// de chaque niveau, exportees par Revit en PNG.
    /// La vue 3D est creee dans un groupe de transactions ANNULE apres l'export : le modele n'est pas
    /// modifie. Les plans sont les vues en plan existantes du projet (rien n'est cree).
    /// </summary>
    internal static class DesignCapture
    {
        /// <summary>Nombre maximal de plans d'etage ajoutes au rapport.</summary>
        public const int MaxPlans = 8;
        private const int PixelSize = 2000;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private static (string Key, DateTime At, List<DesignImage> Images)? cache;

        /// <summary>Images recentes (moins de 15 min) du meme document, pour l'apercu ; sinon nouvelle capture.</summary>
        public static List<DesignImage> Cached(Document doc)
        {
            string key = doc.PathName + "|" + doc.Title;
            if (cache.HasValue && cache.Value.Key == key && (DateTime.Now - cache.Value.At).TotalMinutes < 15 &&
                cache.Value.Images.All(i => File.Exists(i.Path)))
            {
                return cache.Value.Images;
            }
            string dir = Path.Combine(Path.GetTempPath(), "DanCI_conception", Math.Abs(key.GetHashCode()).ToString(CultureInfo.InvariantCulture));
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { /* fichiers ouverts : on ecrase */ }
            var images = Capture(doc, dir, new List<string>());
            cache = (key, DateTime.Now, images);
            return images;
        }

        /// <summary>Capture les images dans <paramref name="folder"/> ; les problemes sont ajoutes a <paramref name="warnings"/>.</summary>
        public static List<DesignImage> Capture(Document doc, string folder, List<string> warnings)
        {
            var result = new List<DesignImage>();
            if (doc == null || doc.IsFamilyDocument) return result;
            Directory.CreateDirectory(folder);
            try
            {
                DesignImage view3D = Capture3D(doc, folder);
                if (view3D != null) result.Add(view3D);
                else warnings.Add("Image 3D de la conception non produite (voir le journal).");
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Image 3D de la conception : " + ex.Message);
                warnings.Add("Image 3D de la conception non produite : " + ex.Message);
            }
            try
            {
                result.AddRange(CapturePlans(doc, folder));
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Plans de la conception : " + ex.Message);
                warnings.Add("Plans de la conception non produits : " + ex.Message);
            }
            BoqLog.Info("Images de la conception : " + result.Count + " (" + string.Join(", ", result.Select(r => r.Title)) + ")");
            return result;
        }

        private static DesignImage Capture3D(Document doc, string folder)
        {
            string target = Path.Combine(folder, "01_Vue_3D.png");
            // 1. Vue 3D temporaire, ombree, sans annotations, cadree sur tout le modele (annulee apres l'export).
            if (!doc.IsReadOnly)
            {
                try
                {
                    using (var group = new TransactionGroup(doc, "DanCI BOQ — image 3D de la conception"))
                    {
                        group.Start();
                        try
                        {
                            ElementId id = null;
                            using (var t = new Transaction(doc, "DanCI BOQ — vue 3D temporaire"))
                            {
                                t.Start();
                                ViewFamilyType vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                                if (vft != null)
                                {
                                    View3D v = View3D.CreateIsometric(doc, vft.Id);
                                    v.DisplayStyle = DisplayStyle.ShadingWithEdges;
                                    v.DetailLevel = ViewDetailLevel.Fine;
                                    v.AreAnnotationCategoriesHidden = true;
                                    id = v.Id;
                                }
                                t.Commit();
                            }
                            if (id != null && ExportView(doc, id, target))
                            {
                                return new DesignImage { Title = "Vue 3D du modèle", Caption = "Vue axonométrique ombrée de l'ensemble du projet", Path = target, Is3D = true };
                            }
                        }
                        finally
                        {
                            group.RollBack(); // la vue temporaire disparait : le modele est inchange
                        }
                    }
                }
                catch (Exception ex)
                {
                    BoqLog.Warn("Vue 3D temporaire impossible (" + ex.Message + ") : vue 3D existante utilisee.");
                }
            }
            // 2. A defaut (modele en lecture seule...) : une vue 3D existante du projet.
            View3D existing = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && !v.Name.StartsWith("DanCI", StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v.Name == "{3D}" ? 0 : v.IsPerspective ? 2 : 1).FirstOrDefault();
            if (existing != null && ExportView(doc, existing.Id, target))
            {
                return new DesignImage { Title = "Vue 3D du modèle", Caption = "Vue Revit « " + existing.Name + " »", Path = target, Is3D = true };
            }
            return null;
        }

        private static IEnumerable<DesignImage> CapturePlans(Document doc, string folder)
        {
            var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.CanBePrinted && v.GenLevel != null)
                .ToList();
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            int n = 0;
            foreach (Level level in levels)
            {
                if (n >= MaxPlans) yield break;
                // Plan du niveau : celui qui porte le nom du niveau, sinon le premier.
                ViewPlan plan = plans.Where(p => p.GenLevel.Id == level.Id)
                                     .OrderBy(p => string.Equals(p.Name, level.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                     .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (plan == null) continue;
                string target = Path.Combine(folder, (n + 2).ToString("00", CultureInfo.InvariantCulture) + "_Plan_" + Safe(level.Name) + ".png");
                bool ok;
                try { ok = ExportView(doc, plan.Id, target); }
                catch (Exception ex) { BoqLog.Warn("Plan « " + plan.Name + " » non exporté : " + ex.Message); ok = false; }
                if (!ok) continue;
                n++;
                double meters = UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Meters);
                yield return new DesignImage
                {
                    Title = "Plan — " + level.Name,
                    Caption = "Vue Revit « " + plan.Name + " » — niveau à " + (meters >= 0 ? "+" : string.Empty) + meters.ToString("0.00", Fr) + " m",
                    Path = target
                };
            }
        }

        /// <summary>Exporte une vue en PNG (dossier temporaire propre, puis deplacement vers <paramref name="target"/>).</summary>
        private static bool ExportView(Document doc, ElementId viewId, string target)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "DanCI_img_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var options = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(tmp, "vue"),
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ZoomType = ZoomFitType.FitToPage,
                    FitDirection = FitDirectionType.Horizontal,
                    PixelSize = PixelSize
                };
                options.SetViewsAndSheets(new List<ElementId> { viewId });
                doc.ExportImage(options);
                string produced = Directory.GetFiles(tmp, "*.png").FirstOrDefault();
                if (produced == null) return false;
                if (File.Exists(target)) File.Delete(target);
                File.Move(produced, target);
                return true;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (Exception) { /* nettoyage au mieux */ }
            }
        }

        private static string Safe(string s) =>
            new string((s ?? "niveau").Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c).ToArray());
    }
}
