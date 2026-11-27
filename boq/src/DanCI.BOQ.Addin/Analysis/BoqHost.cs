using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.RevitAdapter.Extraction;
using DanCI.BOQ.UI.Progress;
using DanCI.BOQ.UI.ViewModels;
using DanCI.BOQ.UI.Views;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Pont entre la fenetre non modale et Revit. Les demandes de la fenetre sont mises en file
    /// et executees par Revit dans le contexte API (ExternalEvent) : la fenetre n'appelle jamais
    /// l'API Revit directement. Une seule fenetre par session.
    /// </summary>
    internal sealed class BoqHost : IBoqHost, DanCI.BOQ.UI.Chart.IChartHost, IExternalEventHandler
    {
        private static BoqHost instance;

        private readonly ExternalEvent externalEvent;
        private readonly Queue<Action<UIApplication>> requests = new Queue<Action<UIApplication>>();
        private BoqWindow window;
        private AnalysisOutcome last;
        private Document lastDocument;

        private BoqHost()
        {
            externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>Ouvre (ou ramene au premier plan) la fenetre. A appeler depuis une commande (contexte API).</summary>
        public static void ShowWindow(UIApplication app, int? tab = null)
        {
            Ensure();
            instance.Show(app);
            if (tab.HasValue && instance.window != null)
            {
                instance.window.ViewModel.SelectedTab = tab.Value;
            }
        }

        /// <summary>Instance unique ; branche aussi le panneau « Diagramme ». Contexte API Revit.</summary>
        public static BoqHost Ensure()
        {
            if (instance == null)
            {
                instance = new BoqHost();
                DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Attach(instance);
            }
            return instance;
        }

        /// <summary>Affiche le panneau « Diagramme du métré » a cote de la vue (contexte API Revit).</summary>
        public static string ShowChart(UIApplication app)
        {
            Ensure();
            return ChartPane.Show(app);
        }

        public void RequestShowChart() => Enqueue(app =>
        {
            string problem = ChartPane.Show(app);
            SetStatus(problem ?? "Panneau « Diagramme du métré » affiché dans Revit, à côté de la vue.");
        });

        // ------------------------------------------------------------ nomenclatures BOQ du projet
        public const string ScheduleName = "DanCI — BOQ";
        public const string ScheduleByLevelName = "DanCI — BOQ par niveau";

        public void RequestSchedules() => Enqueue(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (last?.Boq == null || !IsLastDocument(uidoc))
            {
                SetStatus("Nomenclatures impossibles : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            var confirm = new TaskDialog("DanCI — nomenclatures BOQ")
            {
                MainInstruction = "Créer ou mettre à jour les nomenclatures « " + ScheduleName + " » et « " + ScheduleByLevelName + " » ?",
                MainContent = "Cette action MODIFIE le modèle : elle crée (ou met à jour) deux vues de nomenclature, à poser sur une feuille. " +
                              "Le tableau est un instantané du BOQ affiché : relancez cette commande après une nouvelle analyse. " +
                              "Aucun élément n'est modifié. Une seule annulation (Ctrl+Z) défait l'ensemble.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                SetStatus("Nomenclatures annulées : modèle non modifié.");
                return;
            }
            Document doc = uidoc.Document;
            DateTime now = DateTime.Now;
            string project = window?.ViewModel.ProjectIdentity?.Name ?? doc.Title;
            ViewSchedule main;
            using (var t = new Transaction(doc, "DanCI BOQ — nomenclatures"))
            {
                t.Start();
                try
                {
                    main = BoqScheduleWriter.Write(doc, ScheduleName, "BOQ — " + project, BoqScheduleTable.Boq(last.Boq, now));
                    BoqScheduleWriter.Write(doc, ScheduleByLevelName, "BOQ PAR NIVEAU — " + project,
                        BoqScheduleTable.ByLevel(last.Boq, AnalysisService.LevelNames(doc), now,
                            LevelBreakdown.GroundLevel(AnalysisService.LevelNames(doc), last.Configuration?.Extraction)));
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
                {
                    t.RollBack();
                    BoqLog.Error("Nomenclatures BOQ en échec.", ex);
                    SetStatus("Nomenclatures non créées (modèle inchangé) : " + ex.Message);
                    return;
                }
                t.Commit();
            }
            try
            {
                uidoc.RequestViewChange(main);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                BoqLog.Warn("Ouverture de la nomenclature impossible : " + ex.Message);
            }
            SetStatus("Nomenclatures « " + ScheduleName + " » et « " + ScheduleByLevelName + " » à jour (" + last.Boq.AllLines.Count() +
                      " lignes) : Navigateur de projet > Nomenclatures/Quantités. Annulable (Ctrl+Z).");
        });

        // ------------------------------------------------------------ fouilles en 3D
        public void RequestExcavation3D(IReadOnlyList<ElementQuantity> excavations) => Enqueue(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            var prisms = (excavations ?? new ElementQuantity[0])
                .Where(q => q.Parameters != null && q.Parameters.ContainsKey(DanCI.BOQ.Core.Extraction.Excavation3D.Parameter))
                .SelectMany(q => DanCI.BOQ.Core.Extraction.Excavation3D.Decode(q.Parameters[DanCI.BOQ.Core.Extraction.Excavation3D.Parameter]))
                .ToList();
            if (prisms.Count == 0 || !IsLastDocument(uidoc))
            {
                if (ChartServer.IsActive)
                {
                    ChartServer.HideExcavations();
                    uidoc?.RefreshActiveView();
                }
                return;
            }
            int n = ChartServer.ShowExcavations(uidoc.Document, prisms);
            uidoc.RefreshActiveView();
            SetStatus(excavations.Count + " fouille(s) dessinée(s) en 3D (" + n + " volume(s), terre semi-transparente)" +
                      (uidoc.ActiveView is View3D ? "." : " : ouvrez une vue 3D pour les voir.") +
                      " Affichage seulement : rien n'est écrit dans le modèle. Fouilles ESTIMÉES (non modélisées).");
        });

        // ------------------------------------------------------------ panneau « Diagramme »
        public void RequestOpenBoq() => Enqueue(app => ShowWindow(app));

        public void RequestChartSelection(IReadOnlyCollection<ElementRef> elements) => Enqueue(app => Select(app, elements));

        public void RequestIsolate(IReadOnlyCollection<long> hostElementIds, string label) => Enqueue(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (!IsLastDocument(uidoc))
            {
                SetStatus("Isolement impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;
            if (view == null || view.IsTemplate || !view.CanUseTemporaryVisibilityModes())
            {
                SetStatus("La vue active ne permet pas l'isolement temporaire : ouvrez une vue 3D ou un plan.");
                return;
            }
            List<ElementId> ids = hostElementIds.Select(i => new ElementId(i)).Where(id => doc.GetElement(id) != null).ToList();
            if (ids.Count == 0)
            {
                SetStatus("« " + label + " » : aucun élément du modèle hôte à isoler (éléments de liens ou supprimés depuis l'analyse).");
                return;
            }
            // Mode de vue TEMPORAIRE (comme le bouton lunettes de Revit) : aucun element modifie.
            using (var t = new Transaction(doc, "DanCI BOQ — isoler " + label + " (temporaire)"))
            {
                t.Start();
                if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                {
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                }
                view.IsolateElementsTemporary(ids);
                t.Commit();
            }
            SetStatus("« " + label + " » : " + ids.Count + " élément(s) isolé(s) dans la vue « " + view.Name + " » (temporaire). « Afficher tout » pour revenir.");
            if (label == "Lot 01" && last?.Boq != null)
            {
                // Lot terrassements : ses fouilles calculees sont dessinees avec les elements isoles.
                var pits = last.Boq.AllLines.Where(l => l.LotCode == "01").SelectMany(l => l.Contributions).Select(c => c.Element)
                    .Where(q => q.Kind == QuantityKinds.Excavation && q.Parameters != null && q.Parameters.ContainsKey(DanCI.BOQ.Core.Extraction.Excavation3D.Parameter))
                    .SelectMany(q => DanCI.BOQ.Core.Extraction.Excavation3D.Decode(q.Parameters[DanCI.BOQ.Core.Extraction.Excavation3D.Parameter])).ToList();
                if (pits.Count > 0)
                {
                    ChartServer.ShowExcavations(doc, pits);
                    uidoc.RefreshActiveView();
                }
            }
        });

        // ------------------------------------------------------------ Studio 4D
        public const string Sim4DViewName = "DanCI — Planning 4D";
        private readonly object frameLock = new object();
        private Sim4DFrame pendingFrame;
        private bool frameQueued;
        private ElementId sim4DViewId;
        private (XYZ Min, XYZ Max)? sim4DBox;
        private readonly Dictionary<long, string> sim4DColored = new Dictionary<long, string>();
        private bool reframed;
        private HashSet<long> sim4DShown;
        private string sim4DShownKey;
        private string sim4DVisibleKey;
        private double? sim4DAngle;
        private bool sim4DZoomPending;
        private HashSet<long> sim4DVisibleSet;
        private int sim4DFramesSinceReveal;
        /// <summary>Elements finis reveles par paquets toutes les N images (gros modeles).</summary>
        private const int RevealEvery = 3;
        /// <summary>Elements masques par le Studio dans la vue 4D (masquage incremental, reaffiches a la fin).</summary>
        private HashSet<long> sim4DHidden = new HashSet<long>();
        private const string GhostPrefix = "g:";
        private double sim4DElevation = DanCI.BOQ.Core.Planning.Sim4DCamera.ElevationDeg;
        private ElementId sim4DSolid;

        /// <summary>Une image de la simulation : seule la DERNIERE image en attente est appliquee (pas de retard qui s'accumule).</summary>
        public void RequestSimulation4D(Sim4DFrame frame)
        {
            lock (frameLock)
            {
                pendingFrame = frame;
                if (frameQueued)
                {
                    // Image remplacee avant d'etre dessinee : on ne libere PAS le Studio maintenant ;
                    // il le sera quand Revit aura dessine la nouvelle (pas d'image sautee, pas de saut visuel).
                    return;
                }
                frameQueued = true;
            }
            Enqueue(app =>
            {
                Sim4DFrame f;
                lock (frameLock)
                {
                    f = pendingFrame;
                    frameQueued = false;
                }
                if (f == null) return;
                try
                {
                    ApplySim4D(app, f);
                }
                catch (Exception ex)
                {
                    BoqLog.Error("Studio 4D : image non appliquée.", ex);
                    DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Status = "⚠ Studio 4D — Revit a refusé l'image : " + ex.Message;
                }
                finally
                {
                    f.Applied?.Invoke();
                }
            });
        }

        private void ApplySim4D(UIApplication app, Sim4DFrame f)
        {
            sim4DProfiler.Start();
            UIDocument uidoc = app.ActiveUIDocument;
            if (!IsLastDocument(uidoc))
            {
                SetStatus("Studio 4D : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            Document doc = uidoc.Document;
            View3D view = sim4DViewId != null ? doc.GetElement(sim4DViewId) as View3D : null;
            view = view ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && v.Name == Sim4DViewName);
            if (view == null)
            {
                ViewFamilyType type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (type == null)
                {
                    SetStatus("Studio 4D : aucun type de vue 3D dans le projet.");
                    return;
                }
                using (var t = new Transaction(doc, "DanCI BOQ — créer la vue « " + Sim4DViewName + " »"))
                {
                    t.Start();
                    view = View3D.CreateIsometric(doc, type.Id);
                    view.Name = Sim4DViewName;
                    if (view.ViewTemplateId != ElementId.InvalidElementId) view.ViewTemplateId = ElementId.InvalidElementId;
                    view.DisplayStyle = DisplayStyle.ShadingWithEdges;
                    view.DetailLevel = ViewDetailLevel.Medium;
                    t.Commit();
                }
            }
            if (!view.Id.Equals(sim4DViewId))
            {
                sim4DViewId = view.Id;
                sim4DColored.Clear();
                sim4DBox = null;
                sim4DShown = null;
                sim4DVisibleKey = null;
                sim4DAngle = null;
                sim4DItems.Clear();
                sim4DMeshes.Clear();
                sim4DMaterialColors.Clear();
                sim4DCuts.Clear();
                sim4DSky = false;
                sim4DShapeErrors = 0;
                sim4DVisibleSet = null;
                sim4DShapes.Clear();
                sim4DShapeKeys.Clear();
                sim4DElemShown.Clear();
            }
            bool activated = false;
            if (!uidoc.ActiveView.Id.Equals(view.Id))
            {
                uidoc.ActiveView = view; // hors transaction ; effectif a la fin de ce tour
                activated = true;
                reframed = false;
            }

            // Cadrage fixe : emprise de TOUS les elements du planning (la camera ne saute pas quand le batiment monte).
            if (sim4DBox == null)
            {
                // Emprise des elements, sans les quelques elements tres eloignes (topographie, site,
                // element egare) : 2 % de chaque cote ecartes s'il y a assez d'elements.
                var boxes = f.All.Select(id => doc.GetElement(new ElementId(id))?.get_BoundingBox(null)).Where(b => b != null).ToList();
                if (boxes.Count > 0)
                {
                    int cut = boxes.Count >= 50 ? boxes.Count / 50 : 0;
                    double Lo(Func<BoundingBoxXYZ, double> g) => boxes.Select(g).OrderBy(v => v).Skip(cut).First();
                    double Hi(Func<BoundingBoxXYZ, double> g) => boxes.Select(g).OrderByDescending(v => v).Skip(cut).First();
                    sim4DBox = (new XYZ(Lo(b => b.Min.X), Lo(b => b.Min.Y), Lo(b => b.Min.Z)),
                                new XYZ(Hi(b => b.Max.X), Hi(b => b.Max.Y), Hi(b => b.Max.Z)));
                }
            }

            // Seuls les elements qu'une vue 3D sait masquer et colorer : pas les pieces, surfaces, espaces
            // (rattaches aux taches de finitions), ni les elements d'annotation. Sinon Revit refuse TOUTE l'image.
            var problems = new List<string>();

            // Construction PROGRESSIVE des taches en cours : ordre de chantier dans la tache, puis chaque
            // element monte (assises de mur, poteau qui monte, dalle coulee) en dessin 3D, sans toucher au modele.
            var inProgress = new HashSet<long>();
            var ghosts = new Dictionary<long, string>();
            var parts = new List<(DanCI.BOQ.Core.Extraction.Prism Shape, string Color)>();
            // RENDU REALISTE : vraie geometrie de l'element, decoupee au rang atteint, couleur de son materiau.
            var meshes = new List<(DanCI.BOQ.Core.Planning.TriMesh Mesh, string Color)>();
            var partsByElement = new Dictionary<long, (List<DanCI.BOQ.Core.Extraction.Prism> Prisms, string Color)>();
            // CADENCE DE 20 CM : chaque element a son propre avancement affiche, qui monte d'au plus N rangs
            // par image en lecture, meme si sa tache est deja finie a la date : rien ne surgit, quelle que
            // soit la vitesse. La date du Studio attend (Lagging) que la 3D ait rattrape.
            // BUDGET DE TEMPS : la lecture des geometries et les decoupes s'arretent apres ~70 ms par image ;
            // les elements restants gardent leur derniere decoupe (ou un volume simplifie) jusqu'a l'image
            // suivante. Aucune image ne peut bloquer la simulation, la rotation continue.
            var meshClock = System.Diagnostics.Stopwatch.StartNew();
            bool Budget() => meshClock.ElapsedMilliseconds < MeshBudgetMs;
            var mode = f.Follow;
            int courses = Math.Max(1, f.CoursesPerFrame);
            var seen = new HashSet<long>();
            int lagging = 0;
            void Draw(DanCI.BOQ.Core.Planning.Build4DItem it, double target, string color)
            {
                seen.Add(it.Id);
                double? prev = sim4DElemShown.TryGetValue(it.Id, out double pv) ? pv : (double?)null;
                double p = DanCI.BOQ.Core.Planning.Build4D.NextElement(it, prev, target, mode, courses);
                sim4DElemShown[it.Id] = p;
                sim4DElemColor[it.Id] = color;
                if (p < target - 1e-6) lagging++;
                if (p >= 0.999) return; // fini : le vrai element apparait (couleur du lot)
                inProgress.Add(it.Id);
                if (p > 0)
                {
                    string drawColor = f.Realistic ? MaterialColor(doc, it.Id) : color;
                    var partial = DanCI.BOQ.Core.Planning.Build4D.Partial(it, p);
                    if (partial.Count > 0) partsByElement[it.Id] = (partial, drawColor);
                    DanCI.BOQ.Core.Planning.TriMesh source = f.RealShapes ? null : SourceMesh(doc, it.Id, Budget());
                    List<DanCI.BOQ.Core.Planning.MeshPart> real = null;
                    if (source != null)
                    {
                        try
                        {
                            int key = DanCI.BOQ.Core.Planning.Build4DMesh.StepKey(source, p, out double pq);
                            if (sim4DCuts.TryGetValue(it.Id, out var cut) && (cut.Key == key || !Budget())) real = cut.Parts; // hors budget : derniere decoupe
                            else if (!Budget()) real = null; // hors budget, jamais decoupe : volume simplifie pour cette image
                            else
                            {
                                real = DanCI.BOQ.Core.Planning.Build4DMesh.Partial(it, source, Math.Max(pq, 1e-4));
                                sim4DCuts[it.Id] = (key, real);
                            }
                        }
                        catch (Exception) { real = null; }
                    }
                    if (real != null)
                    {
                        // Rang en cours plus clair : le palier de 20 cm se voit, la forme reste celle du modele.
                        // Decoupe vide (tout premier instant) : rien a dessiner, jamais de boite de remplacement.
                        foreach (var part in real) meshes.Add((part.Mesh, part.Fresh ? Lighten(drawColor) : drawColor));
                    }
                    else
                    {
                        parts.AddRange(partial.Select(x => (x, drawColor)));
                    }
                    // Fantome seulement en mode « couleurs » ET dessin superpose ; jamais en rendu realiste
                    // (il faisait apparaitre tout le contour de l'element et des volumes jaunes en double).
                    if (!f.RealShapes && !f.Realistic) ghosts[it.Id] = color;
                }
            }
            foreach (Sim4DTask task in f.Tasks ?? new Sim4DTask[0])
            {
                var items = new List<DanCI.BOQ.Core.Planning.Build4DItem>();
                foreach (long id in task.Elements)
                {
                    var item = Build4DItemOf(doc, id);
                    if (item != null) items.Add(item);
                    else if (task.Progress < 0.999) inProgress.Add(id); // forme inconnue : apparait a la fin de sa tache, pas au debut
                }
                var prog = DanCI.BOQ.Core.Planning.Build4D.Schedule(items, task.Progress);
                foreach (var it in items) Draw(it, prog[it.Id], task.Color);
            }
            // Elements dont la tache est finie a la date mais qui montent encore : ils continuent rang par rang.
            var builtNow = new HashSet<long>(f.Built);
            foreach (long id in sim4DElemShown.Keys.ToList())
            {
                if (seen.Contains(id)) continue;
                double v = sim4DElemShown[id];
                bool done = builtNow.Contains(id) || f.Running.ContainsKey(id);
                if (!done) { sim4DElemShown.Remove(id); continue; } // date reculee : pas encore commence
                if (v >= 0.999) continue;
                var it = Build4DItemOf(doc, id);
                if (it == null) { sim4DElemShown[id] = 1; continue; }
                Draw(it, 1, sim4DElemColor.TryGetValue(id, out string c) ? c : "#C8CDD6");
            }
            f.Lagging = lagging;
            // Rendu realiste : aucun element recolore (aspect reel du modele, et aucune surcharge de vue a chaque image).
            // ATTENTION : « running » ne sert qu'aux COULEURS ; l'affichage s'appuie sur f.Running (elements des taches
            // en cours deja montes) : un element fini reste visible meme si sa tache n'est pas terminee.
            var running = f.Realistic ? new Dictionary<long, string>() : f.Running.Where(kv => !inProgress.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            // Comme le profil « actif » de SYNCHRO : l'element en construction apparait en fantome (85 % transparent) et les
            // assises opaques le remplissent de bas en haut. Meme si le dessin 3D echouait, rien ne surgit d'un coup.
            foreach (var g in ghosts) running[g.Key] = GhostPrefix + g.Value;

            HashSet<long> allSet = null;
            // Ensembles (recherche directe) : aucun tri ni parcours de liste dont le cout grandirait avec le batiment.
            int allCount = 0;
            HashSet<long> visSet = new HashSet<long>();
            ElementId solidId = sim4DSolid;

            // IMAGE LEGERE / IMAGE COMPLETE : la transaction (isolement, couleurs, rotation) n'est faite que si
            // quelque chose change dans la vue Revit. Le reste du temps, seules les assises en construction sont
            // redessinees (sans transaction) : lecture rapide ET montee progressive.
            string shownKey = view.Id.Value + ":" + f.All.Count + ":" + f.All.Sum(i => i % 1000003);
            bool needShown = sim4DShown == null || sim4DShownKey != shownKey;
            string visibleKey = null;
            if (!needShown)
            {
                allSet = sim4DShown;
                allCount = allSet.Count;
                visSet = VisibleSet(f, allSet, inProgress, ghosts);
                // GROS MODELES : les elements qui viennent d'etre finis sont reveles PAR PAQUETS (toutes les
                // ~8 images) au lieu d'une operation sur la vue a chaque element. En attendant, ils restent
                // dessines a pleine hauteur (rien ne disparait, rien ne surgit).
                sim4DFramesSinceReveal++;
                if (!f.RealShapes && sim4DVisibleSet != null && sim4DFramesSinceReveal < RevealEvery)
                {
                    var added = visSet.Where(i => !sim4DVisibleSet.Contains(i)).ToList();
                    bool removed = sim4DVisibleSet.Any(i => !visSet.Contains(i)); // (avant : liste parcourue pour chaque element -> cout au carre)
                    if (added.Count > 0 && !removed && added.Count < 4000)
                    {
                        foreach (long id in added)
                        {
                            var it = Build4DItemOf(doc, id);
                            if (it == null) continue;
                            string c = running.TryGetValue(id, out string rc) ? (rc.StartsWith(GhostPrefix, StringComparison.Ordinal) ? rc.Substring(GhostPrefix.Length) : rc) : "#C8CDD6";
                            if (f.Realistic) c = MaterialColor(doc, id);
                            DanCI.BOQ.Core.Planning.TriMesh whole = SourceMesh(doc, id, Budget());
                            if (whole != null) meshes.Add((whole, c));
                            else parts.AddRange(DanCI.BOQ.Core.Planning.Build4D.Partial(it, 1).Select(x => (x, c)));
                        }
                        visSet = new HashSet<long>(sim4DVisibleSet);
                        foreach (long id in added) running.Remove(id); // couleur appliquee a la revelation
                    }
                }
                visibleKey = SetKey(visSet);
            }
            bool needVisibility = needShown || visibleKey != sim4DVisibleKey;
            bool needColors = needShown || sim4DColored.Any(kv => !running.TryGetValue(kv.Key, out string c0) || c0 != kv.Value) ||
                              running.Any(kv => !sim4DColored.ContainsKey(kv.Key) && allSet != null && allSet.Contains(kv.Key));
            double elevation = f.ElevationDeg ?? DanCI.BOQ.Core.Planning.Sim4DCamera.ElevationDeg;
            bool needTurn = f.AngleDeg.HasValue && (!sim4DAngle.HasValue || Math.Abs(sim4DAngle.Value - f.AngleDeg.Value) > 1e-6 ||
                                                    Math.Abs(sim4DElevation - elevation) > 1e-6);
            bool needPerspective = view.IsPerspective != f.Perspective;
            bool needShapes = f.RealShapes ? (partsByElement.Count > 0 || sim4DShapes.Count > 0) : sim4DShapes.Count > 0;
            // Pendant la lecture : « ombrée » sans arêtes (cout d'affichage qui grandissait avec le batiment) ;
            // a l'arret et sur la vue de face finale : « ombrée avec arêtes ». Annotations masquees (vue dediee).
            DisplayStyle wantedStyle = f.Fast ? DisplayStyle.Shading : DisplayStyle.ShadingWithEdges;
            bool needStyle = view.DisplayStyle != wantedStyle || !view.AreAnnotationCategoriesHidden || (f.Environment && !sim4DSky);
            bool heavy = needVisibility || needColors || needTurn || needPerspective || needShapes || needStyle;

            sim4DProfiler.Mark("préparation");
            if (heavy)
            using (var t = new Transaction(doc, "DanCI 4D — " + (f.Label ?? "simulation")))
            {
                t.Start();
                if (needStyle)
                {
                    try
                    {
                        if (view.DisplayStyle != wantedStyle) view.DisplayStyle = wantedStyle;
                        if (!view.AreAnnotationCategoriesHidden) view.AreAnnotationCategoriesHidden = true;
                        if (f.Environment && !sim4DSky)
                        {
                            // Ciel en degrade (haut bleu, horizon clair, sol gris-vert) : fond de la vue 4D dediee.
                            view.SetBackground(ViewDisplayBackground.CreateGradient(new Color(96, 150, 214), new Color(214, 230, 244), new Color(150, 160, 140)));
                            sim4DSky = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        problems.Add("style de la vue : " + ex.Message);
                    }
                }
                if (needVisibility)
                try
                {
                    if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                    {
                        view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    }
                    // Tri des elements affichables : fait vue « tout montree » (mode temporaire coupe), pour
                    // qu'aucun element masque par l'image precedente ne soit ecarte a tort ; garde ensuite
                    // en memoire tant que le planning ne change pas.
                    if (needShown)
                    {
                        bool Shown(ElementId id)
                        {
                            Element e = doc.GetElement(id);
                            if (e == null || e.Category == null || e.Category.CategoryType != CategoryType.Model || e is SpatialElement) return false;
                            try { return e.IsHidden(view) || e.CanBeHidden(view); } catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
                        }
                        sim4DShown = new HashSet<long>(f.All.Distinct().Where(i => Shown(new ElementId(i))));
                        sim4DShownKey = shownKey;
                        sim4DSolid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill)?.Id;
                        allSet = sim4DShown;
                        allCount = allSet.Count;
                        visSet = VisibleSet(f, allSet, inProgress, ghosts);
                        visibleKey = SetKey(visSet);
                        // Etat reel de la vue (masquages laisses par une session interrompue compris).
                        sim4DHidden = new HashSet<long>(allSet.Concat(f.PlannedLinks ?? new long[0]).Where(i =>
                        {
                            Element e = doc.GetElement(new ElementId(i));
                            try { return e != null && e.IsHidden(view); } catch (Exception) { return false; }
                        }));
                    }
                    solidId = sim4DSolid;
                    // Comme SYNCHRO : on ne masque QUE les elements du planning pas encore construits. Le
                    // contexte (terrain, batiments voisins, grue, elements hors planning) reste visible.
                    // GROS MODELES : masquage INCREMENTAL dans la vue 4D dediee : seuls les elements qui changent
                    // d'etat sont masques ou reaffiches (avant : tout le modele etait re-masque a chaque revelation).
                    var visibleSet = visSet;
                    var want = new HashSet<long>(allSet.Where(i => !visibleSet.Contains(i)));
                    foreach (long link in f.PlannedLinks ?? new long[0]) want.Add(link);
                    var toShow = sim4DHidden.Where(i => !want.Contains(i)).Select(i => doc.GetElement(new ElementId(i)))
                        .Where(e => e != null && e.IsHidden(view)).Select(e => e.Id).ToList();
                    var toHide = want.Where(i => !sim4DHidden.Contains(i)).Select(i => doc.GetElement(new ElementId(i)))
                        .Where(e => e != null && !e.IsHidden(view) && e.CanBeHidden(view)).Select(e => e.Id).ToList();
                    if (toShow.Count > 0) view.UnhideElements(toShow);
                    if (toHide.Count > 0) view.HideElements(toHide);
                    sim4DHidden = want;
                    sim4DVisibleKey = visibleKey;
                    sim4DVisibleSet = visibleSet;
                    sim4DFramesSinceReveal = 0;
                }
                catch (Exception ex)
                {
                    sim4DVisibleKey = null;
                    sim4DShown = null; // etat de la vue relu a l'image suivante
                    problems.Add("affichage des éléments construits : " + ex.Message);
                }
                sim4DProfiler.Mark("masquage");

                if (needColors)
                {
                    // Taches en cours : couleur du lot ; terminees : aspect normal. Un element refuse n'arrete pas l'image.
                    foreach (var kv in sim4DColored.ToList())
                    {
                        if (running.TryGetValue(kv.Key, out string c) && c == kv.Value) continue;
                        sim4DColored.Remove(kv.Key);
                        var id = new ElementId(kv.Key);
                        try { if (doc.GetElement(id) != null) view.SetElementOverrides(id, new OverrideGraphicSettings()); }
                        catch (Exception) { /* element supprime ou non colorable */ }
                    }
                    int refused = 0;
                    foreach (var kv in running)
                    {
                        if (sim4DColored.ContainsKey(kv.Key) || allSet == null || !allSet.Contains(kv.Key)) continue;
                        bool ghost = kv.Value.StartsWith(GhostPrefix, StringComparison.Ordinal);
                        Color color = HexColor(ghost ? kv.Value.Substring(GhostPrefix.Length) : kv.Value);
                        var ogs = new OverrideGraphicSettings();
                        if (solidId != null)
                        {
                            ogs.SetSurfaceForegroundPatternId(solidId);
                            ogs.SetSurfaceForegroundPatternColor(color);
                        }
                        ogs.SetProjectionLineColor(color);
                        ogs.SetSurfaceTransparency(ghost ? 85 : 15);
                        if (ghost) ogs.SetHalftone(true);
                        try
                        {
                            view.SetElementOverrides(new ElementId(kv.Key), ogs);
                            sim4DColored[kv.Key] = kv.Value;
                        }
                        catch (Exception)
                        {
                            refused++;
                        }
                    }
                    if (refused > 0) problems.Add(refused + " élément(s) non colorables");
                }

                if (needPerspective)
                {
                    try
                    {
                        if (view.CanToggleBetweenPerspectiveAndIsometric())
                        {
                            if (f.Perspective) view.ToggleToPerspective(); else view.ToggleToIsometric();
                            sim4DZoomPending = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        problems.Add("perspective : " + ex.Message);
                    }
                }

                if (needShapes)
                {
                    // Rangs en construction en VOLUMES REELS temporaires : s'affichent a coup sur, meme en rotation.
                    int failed = UpdateBuildShapes(doc, view, f.RealShapes ? partsByElement : new Dictionary<long, (List<DanCI.BOQ.Core.Extraction.Prism>, string)>(), solidId);
                    if (failed > 0) problems.Add(failed + " rang(s) non dessinable(s)");
                }

                sim4DProfiler.Mark("couleurs et volumes");
                // Rotation : camera en orbite autour du batiment (vue isometrique, 25° au-dessus de l'horizon).
                if (needTurn && sim4DBox != null && !view.IsLocked)
                {
                    try
                    {
                        var cam = DanCI.BOQ.Core.Planning.Sim4DCamera.Orientation(f.AngleDeg.Value, elevation);
                        var eye = DanCI.BOQ.Core.Planning.Sim4DCamera.Eye(V(sim4DBox.Value.Min), V(sim4DBox.Value.Max), cam.Forward);
                        view.SetOrientation(new ViewOrientation3D(X(eye), X(cam.Up), X(cam.Forward)));
                        sim4DAngle = f.AngleDeg;
                        sim4DElevation = elevation;
                    }
                    catch (Exception ex)
                    {
                        problems.Add("rotation : " + ex.Message);
                    }
                }
                else if (needTurn && view.IsLocked)
                {
                    problems.Add("vue 3D verrouillée : rotation impossible (déverrouillez-la)");
                }
                sim4DProfiler.Mark("rotation");
                t.Commit();
                sim4DProfiler.Mark("validation Revit");
            }
            ShowDigging(doc, f.Earthwork, f.Environment);
            ShowSite(doc, f);
            try
            {
                ChartServer.Show4D(doc, f.RealShapes ? new List<(DanCI.BOQ.Core.Extraction.Prism, string)>() : parts,
                    f.RealShapes ? new List<(DanCI.BOQ.Core.Planning.TriMesh, string)>() : meshes);
            }
            catch (Exception ex)
            {
                problems.Add("dessin des éléments en construction : " + ex.Message);
            }
            sim4DProfiler.Mark("dessin 3D");
            if (problems.Count > 0) BoqLog.Warn("Studio 4D : " + string.Join(" ; ", problems));
            if (activated) sim4DZoomPending = true; // la vue s'ouvre apres ce tour : la prochaine image la cadre
            if (sim4DBox != null && (needTurn || needShown || needPerspective || sim4DZoomPending))
            {
                // Cadrage CONSTANT pendant la rotation : carre dans le plan de l'ecran autour du batiment
                // (sphere englobante), meme taille a tous les angles : le zoom ne saute plus.
                UIView ui = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Equals(view.Id));
                if (ui != null)
                {
                    try
                    {
                        if (view.IsPerspective)
                        {
                            ui.ZoomToFit(); // vue de face en perspective : tout le batiment dans le champ
                        }
                        else
                        {
                            ViewOrientation3D o = view.GetOrientation();
                            XYZ zmin = sim4DBox.Value.Min, zmax = sim4DBox.Value.Max;
                            if (f.Environment)
                            {
                                // Cadre elargi : grue (a gauche, plus haute que le batiment) et palissade visibles.
                                double m = 3.28084;
                                zmin = new XYZ(zmin.X - 8 * m, zmin.Y - 7 * m, zmin.Z);
                                zmax = new XYZ(zmax.X + 7 * m, zmax.Y + 7 * m, zmax.Z + 11 * m);
                            }
                            var corners = DanCI.BOQ.Core.Planning.Sim4DCamera.ZoomCorners(V(zmin), V(zmax),
                                V(o.ForwardDirection), V(o.UpDirection), sim4DElevation);
                            ui.ZoomAndCenterRectangle(X(corners.A), X(corners.B));
                        }
                        sim4DZoomPending = false;
                    }
                    catch (Exception ex)
                    {
                        BoqLog.Warn("Studio 4D : cadrage impossible : " + ex.Message);
                    }
                }
            }
            sim4DProfiler.Mark("cadrage");
            // Une modification de la vue (transaction validee) est de toute facon redessinee par Revit : on ne
            // force un dessin supplementaire que pour les images legeres, la video et les captures (avant : 2 a 3
            // dessins complets par image).
            if (!heavy || f.Record != null || !string.IsNullOrEmpty(f.Snapshot)) uidoc.RefreshActiveView();
            sim4DProfiler.Mark("affichage");
            string recordInfo = RecordFrame(app, uidoc, view, f);
            TakeSnapshot(app, uidoc, view, f);
            sim4DProfiler.Mark("vidéo et captures");
            sim4DProfiler.End();
            if (sim4DProfiler.Frames % 60 == 0)
                BoqLog.Info("Studio 4D, temps moyen par image (" + sim4DProfiler.Frames + " images) : " +
                            string.Join(" · ", sim4DProfiler.Averages().Select(a => a.Key + " " + a.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms")));
            sim4DNotDisplayable = Math.Max(0, f.All.Count - allCount);
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Status = (recordInfo ?? string.Empty) + (problems.Count > 0 ? "⚠ " + string.Join(" ; ", problems) + " — " : "✓ ") +
                "Revit, vue « " + Sim4DViewName + " » — " + f.Label + (heavy ? string.Empty : " (image légère)") + " · " + sim4DProfiler.Summary() + " : " + visSet.Count + " élément(s) affiché(s) dont " + sim4DColored.Count +
                " en cours (couleur du lot), " + inProgress.Count + " en construction (" + (meshes.Count > 0 ? meshes.Count + " partie(s) en vraie géométrie" + (parts.Count > 0 ? " + " : string.Empty) : string.Empty) + (parts.Count > 0 || meshes.Count == 0 ? parts.Count + " rang(s) simplifié(s)" : string.Empty) + (f.RealShapes ? ", " + sim4DShapes.Count + " volume(s) temporaire(s)" : " dessiné(s)") + ")" +
                (sim4DShapeErrors > 0 ? " ⚠ " + sim4DShapeErrors + " forme(s) illisible(s) (voir le journal)" : string.Empty) + (allCount < f.All.Count ? " ; " + (f.All.Count - allCount) + " pièce(s) / élément(s) non affichables en 3D ignorés" : string.Empty) + ".";
            if (activated && !reframed)
            {
                // La vue vient d'etre activee : Revit l'ouvre apres ce tour ; une seconde image la cadre.
                reframed = true;
                RequestSimulation4D(f);
            }
        }

        // ------------------------------------------------------------ rangs en volumes reels (Studio 4D)
        private readonly Dictionary<long, ElementId> sim4DShapes = new Dictionary<long, ElementId>();
        /// <summary>Avancement AFFICHE de chaque element (cadence de 20 cm par image) et sa couleur.</summary>
        private readonly Dictionary<long, double> sim4DElemShown = new Dictionary<long, double>();
        private readonly Dictionary<long, string> sim4DElemColor = new Dictionary<long, string>();
        private readonly Dictionary<long, string> sim4DShapeKeys = new Dictionary<long, string>();

        /// <summary>
        /// Volumes temporaires (DirectShape, modeles generiques « DanCI 4D ») des elements en construction :
        /// crees, mis a jour rang par rang, supprimes quand l'element est fini. Dans une transaction ouverte.
        /// Retourne le nombre de rangs impossibles a dessiner.
        /// </summary>
        private int UpdateBuildShapes(Document doc, View3D view, Dictionary<long, (List<DanCI.BOQ.Core.Extraction.Prism> Prisms, string Color)> wanted, ElementId solidId)
        {
            int failed = 0;
            var remove = sim4DShapes.Keys.Where(k => !wanted.ContainsKey(k)).ToList();
            if (remove.Count > 0)
            {
                var ids = remove.Select(k => sim4DShapes[k]).Where(id => doc.GetElement(id) != null).ToList();
                try { if (ids.Count > 0) doc.Delete(ids); } catch (Exception) { /* deja supprime */ }
                foreach (long k in remove) { sim4DShapes.Remove(k); sim4DShapeKeys.Remove(k); }
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kv in wanted)
            {
                string key = kv.Value.Color + "|" + string.Join(";", kv.Value.Prisms.Select(p => p.Zt.ToString("0.###", inv) + "," + p.Bottom[2].ToString("0.###", inv)));
                if (sim4DShapeKeys.TryGetValue(kv.Key, out string old) && old == key) continue; // rien n'a monte
                var solids = new List<GeometryObject>();
                foreach (var p in kv.Value.Prisms)
                {
                    Solid sol = PrismSolid(p);
                    if (sol != null) solids.Add(sol); else failed++;
                }
                if (solids.Count == 0) continue;
                try
                {
                    DirectShape ds = sim4DShapes.TryGetValue(kv.Key, out ElementId existing) ? doc.GetElement(existing) as DirectShape : null;
                    if (ds == null)
                    {
                        ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.ApplicationId = "DanCI";
                        ds.ApplicationDataId = "DanCI 4D (temporaire) " + kv.Key.ToString(inv);
                        sim4DShapes[kv.Key] = ds.Id;
                        Color color = HexColor(kv.Value.Color);
                        var ogs = new OverrideGraphicSettings();
                        if (solidId != null)
                        {
                            ogs.SetSurfaceForegroundPatternId(solidId);
                            ogs.SetSurfaceForegroundPatternColor(color);
                        }
                        ogs.SetProjectionLineColor(new Color(60, 60, 60));
                        view.SetElementOverrides(ds.Id, ogs);
                    }
                    ds.SetShape(solids);
                    sim4DShapeKeys[kv.Key] = key;
                }
                catch (Exception)
                {
                    failed++;
                }
            }
            return failed;
        }

        /// <summary>Prisme (4 coins en plan, bas et haut) -> solide d'extrusion verticale ; null si trop petit.</summary>
        private static Solid PrismSolid(DanCI.BOQ.Core.Extraction.Prism p)
        {
            try
            {
                var pts = Enumerable.Range(0, 4).Select(i => new XYZ(p.Bottom[2 * i], p.Bottom[2 * i + 1], p.Zb)).ToList();
                double h = p.Zt - p.Zb;
                if (h < 0.01) return null;
                for (int i = 0; i < 4; i++) if (pts[i].DistanceTo(pts[(i + 1) % 4]) < 0.01) return null;
                double area = 0;
                for (int i = 0; i < 4; i++) area += pts[i].X * pts[(i + 1) % 4].Y - pts[(i + 1) % 4].X * pts[i].Y;
                if (area < 0) pts.Reverse(); // sens trigonometrique
                var loop = new CurveLoop();
                for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % 4]));
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, h);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Supprime tous les volumes temporaires du Studio 4D (fin de simulation).</summary>
        private void DeleteBuildShapes(Document doc)
        {
            if (sim4DShapes.Count == 0 || doc == null) return;
            var ids = sim4DShapes.Values.Where(id => doc.GetElement(id) != null).ToList();
            sim4DShapes.Clear();
            sim4DShapeKeys.Clear();
            if (ids.Count == 0) return;
            using (var t = new Transaction(doc, "DanCI 4D — retirer les volumes temporaires"))
            {
                t.Start();
                try { doc.Delete(ids); } catch (Exception) { }
                t.Commit();
            }
        }

        // ------------------------------------------------------------ fouilles qui se creusent (Studio 4D)
        private List<DanCI.BOQ.Core.Extraction.Prism> sim4DPits;
        private int sim4DPitsShown = -1;

        /// <summary>Fouilles calculees (non modelisees) montrees au fur et a mesure du terrassement, d'un bout a l'autre du chantier.</summary>
        /// <summary>Fouilles creusees a la date (scene de terrassement de l'environnement de chantier).</summary>
        private int sim4DDug;

        private void ShowDigging(Document doc, double progress, bool environment = false)
        {
            try
            {
                if (sim4DPits == null)
                {
                    sim4DPits = (last?.Boq?.AllLines ?? Enumerable.Empty<BoqLine>())
                        .SelectMany(l => l.Contributions).Select(c => c.Element)
                        .Where(q => q.Kind == QuantityKinds.Excavation && q.Parameters != null && q.Parameters.ContainsKey(DanCI.BOQ.Core.Extraction.Excavation3D.Parameter))
                        .SelectMany(q => DanCI.BOQ.Core.Extraction.Excavation3D.Decode(q.Parameters[DanCI.BOQ.Core.Extraction.Excavation3D.Parameter]))
                        .OrderBy(p => p.Bottom[0] + p.Bottom[1]) // creusees d'un cote du chantier vers l'autre
                        .ToList();
                }
                int n = progress < 0 ? 0 : (int)Math.Ceiling(Math.Min(1, progress) * sim4DPits.Count);
                sim4DDug = n;
                if (environment)
                {
                    // Environnement de chantier : les fouilles sont de vrais trous dans le terrain (scene de terrassement).
                    if (sim4DPitsShown != -2) ChartServer.HideExcavations();
                    sim4DPitsShown = -2;
                    return;
                }
                if (n == sim4DPitsShown) return;
                sim4DPitsShown = n;
                if (n == 0) ChartServer.HideExcavations();
                else ChartServer.ShowExcavations(doc, sim4DPits.Take(n));
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Studio 4D, fouilles : " + ex.Message);
            }
        }

        // ------------------------------------------------------------ video du Studio 4D
        private DanCI.BOQ.Core.Video.AviWriter sim4DVideo;
        private DanCI.BOQ.UI.Studio.FrameGrabber sim4DGrabber;
        private string sim4DVideoPath;

        /// <summary>Capture l'image de la vue dans la video en cours ; ferme la video quand l'enregistrement s'arrete.</summary>
        private string RecordFrame(UIApplication app, UIDocument uidoc, View3D view, Sim4DFrame f)
        {
            if (f.Record == null || f.Record != sim4DVideoPath)
            {
                string closed = CloseVideo();
                if (f.Record == null) return closed;
                sim4DVideoPath = f.Record;
                sim4DGrabber = new DanCI.BOQ.UI.Studio.FrameGrabber();
            }
            try
            {
                UIView ui = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Equals(view.Id));
                if (ui == null) return "⏺ vidéo : vue 3D pas encore ouverte — ";
                Rectangle r = ui.GetWindowRectangle();
                byte[] jpeg = sim4DGrabber.Capture(app.MainWindowHandle, r.Left, r.Top, r.Right, r.Bottom, f.Label, f.Progress, f.Title);
                if (jpeg == null) return "⚠ vidéo : capture de la vue impossible — ";
                if (sim4DVideo == null) sim4DVideo = new DanCI.BOQ.Core.Video.AviWriter(sim4DVideoPath, sim4DGrabber.Width, sim4DGrabber.Height, f.RecordFps);
                sim4DVideo.AddFrame(jpeg);
                return "⏺ " + sim4DVideo.Frames + " image(s) enregistrée(s) — ";
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Studio 4D, vidéo : " + ex.Message);
                return "⚠ vidéo : " + ex.Message + " — ";
            }
        }

        // ------------------------------------------------------------ rapport de simulation 4D
        private readonly List<DanCI.BOQ.Export.Sim4DSnapshot> sim4DSnapshots = new List<DanCI.BOQ.Export.Sim4DSnapshot>();
        /// <summary>Temps passe par etape de chaque image (ligne d'etat, journal, rapport).</summary>
        private readonly DanCI.BOQ.Core.Planning.Sim4DProfiler sim4DProfiler = new DanCI.BOQ.Core.Planning.Sim4DProfiler();
        private DanCI.BOQ.UI.Studio.FrameGrabber sim4DSnapGrabber;
        private string sim4DLastVideo;
        private int sim4DNotDisplayable;
        private const int MaxSnapshots = 16;

        /// <summary>Capture de la vue 3D pour le rapport (meme procede que la video, date incrustee).</summary>
        private void TakeSnapshot(UIApplication app, UIDocument uidoc, View3D view, Sim4DFrame f)
        {
            if (f.ClearSnapshots)
            {
                sim4DSnapshots.Clear();
                sim4DProfiler.Reset();
                sim4DSnapGrabber = null;
                sim4DLastVideo = null;
            }
            if (string.IsNullOrEmpty(f.Snapshot)) return;
            try
            {
                UIView ui = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Equals(view.Id));
                if (ui == null) return;
                Rectangle r = ui.GetWindowRectangle();
                if (sim4DSnapGrabber == null) sim4DSnapGrabber = new DanCI.BOQ.UI.Studio.FrameGrabber();
                byte[] jpeg = sim4DSnapGrabber.Capture(app.MainWindowHandle, r.Left, r.Top, r.Right, r.Bottom, f.Label, f.Progress, f.Title);
                if (jpeg == null) return;
                // Au-dela du maximum, on garde la premiere, la derniere et un echantillon regulier.
                if (sim4DSnapshots.Count >= MaxSnapshots) sim4DSnapshots.RemoveAt(sim4DSnapshots.Count / 2);
                sim4DSnapshots.Add(new DanCI.BOQ.Export.Sim4DSnapshot { Caption = f.Snapshot, Progress = f.Progress, Jpeg = jpeg, Date = DateTime.Now });
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Studio 4D, capture pour le rapport : " + ex.Message);
            }
        }

        public void RequestSimulationReport(Sim4DReportRequest request) => Enqueue(app =>
        {
            void Done(string path, string message)
            {
                SetStatus(message);
                try { request?.Done?.Invoke(path, message); } catch (Exception) { /* Studio ferme */ }
            }
            if (request?.Plan == null || last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject)
            {
                Done(null, "⚠ Rapport de simulation impossible : lancez d'abord l'analyse (planning).");
                return;
            }
            string missing = DanCI.BOQ.Export.BoqExporter.MissingPdfComponents();
            if (missing != null)
            {
                Done(null, "⚠ Rapport de simulation non créé : " + missing);
                return;
            }
            try
            {
                DanCI.BOQ.Export.ExportContext ctx = AnalysisService.BuildExportContext(lastDocument, last, window?.ViewModel.ProjectIdentity,
                    window?.ViewModel.ProjectCover, window?.ViewModel.ProjectSchedule);
                var warnings = new List<string>();
                if (sim4DShapeErrors > 0) warnings.Add(sim4DShapeErrors + " élément(s) à la forme illisible : apparus en fin de tâche au lieu de monter rang par rang (voir le journal).");
                if (sim4DNotDisplayable > 0) warnings.Add(sim4DNotDisplayable + " pièce(s) / élément(s) non affichables en 3D (pièces, surfaces, annotations) : comptés au planning, pas dessinés.");
                if (!request.Completed) warnings.Add("Lecture arrêtée avant la fin du chantier : rapport partiel (captures prises jusque-là).");
                var report = new DanCI.BOQ.Export.Sim4DReport
                {
                    Context = ctx,
                    Plan = request.Plan,
                    Snapshots = sim4DSnapshots.ToList(),
                    Settings = request.Settings,
                    Frames = request.Frames,
                    AverageFrameMs = request.AverageFrameMs,
                    PlayTime = request.PlayTime,
                    Completed = request.Completed,
                    SimulatedElements = request.Plan.Tasks.SelectMany(t => t.Elements).Select(e => e.ElementId).Distinct().Count(),
                    VideoPath = sim4DVideoPath ?? sim4DLastVideo,
                    Timings = sim4DProfiler.Averages(),
                    Warnings = warnings
                };
                string folder = AnalysisService.OutputFolder(lastDocument);
                Directory.CreateDirectory(folder);
                string name = Path.GetFileNameWithoutExtension(lastDocument.Title ?? "projet");
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                string path = Path.Combine(folder, name + "_DanCI_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "_rapport_simulation_4D.pdf");
                DanCI.BOQ.Export.Pdf.Sim4DReportPdf.Write(report, path);
                BoqLog.Info("Rapport de simulation 4D : " + path + " (" + report.Snapshots.Count + " capture(s)).");
                Done(path, "📄 Rapport de simulation enregistré : " + path + " (" + report.Snapshots.Count + " capture(s) de la vue 3D).");
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { /* pas de lecteur PDF */ }
            }
            catch (Exception ex)
            {
                BoqLog.Error("Rapport de simulation 4D en échec.", ex);
                Done(null, "⚠ Rapport de simulation en échec : " + ex.Message + " (détails dans le journal).");
            }
        });

        private string CloseVideo()
        {
            if (sim4DVideo == null)
            {
                sim4DVideoPath = null;
                return null;
            }
            string path = sim4DVideoPath;
            int n = sim4DVideo.Frames;
            try { sim4DVideo.Close(); } catch (Exception ex) { BoqLog.Warn("Studio 4D, fermeture de la vidéo : " + ex.Message); }
            sim4DVideo = null;
            sim4DLastVideo = path;
            sim4DVideoPath = null;
            SetStatus("🎬 Vidéo enregistrée : " + path + " (" + n + " images). Lisible avec VLC ou le Lecteur Windows Media.");
            return "🎬 Vidéo enregistrée (" + n + " images) : " + path + " — ";
        }

        private static DanCI.BOQ.Core.Planning.Sim4DCamera.Vec V(XYZ p) => new DanCI.BOQ.Core.Planning.Sim4DCamera.Vec(p.X, p.Y, p.Z);
        private static XYZ X(DanCI.BOQ.Core.Planning.Sim4DCamera.Vec v) => new XYZ(v.X, v.Y, v.Z);

        private readonly Dictionary<long, DanCI.BOQ.Core.Planning.Build4DItem> sim4DItems = new Dictionary<long, DanCI.BOQ.Core.Planning.Build4DItem>();
        private int sim4DShapeErrors;

        /// <summary>Forme simplifiee d'un element pour sa construction progressive (gardee en memoire) : mur = trace x epaisseur, sinon boite.</summary>
        private DanCI.BOQ.Core.Planning.Build4DItem Build4DItemOf(Document doc, long id)
        {
            if (sim4DItems.TryGetValue(id, out var cached)) return cached;
            DanCI.BOQ.Core.Planning.Build4DItem item = null;
            try
            {
                Element e = doc.GetElement(new ElementId(id));
                BoundingBoxXYZ bb = e?.get_BoundingBox(null);
                if (e != null && e.Category != null && bb != null && !(e is SpatialElement))
                {
                    // BuiltInCategory est sur 64 bits (Revit 2024+) : convertir en (int) faisait echouer
                    // Enum.IsDefined (type different) -> aucune forme, les elements surgissaient entiers.
                    string cat = string.Empty;
                    try
                    {
                        var bic = (BuiltInCategory)e.Category.Id.Value;
                        if (Enum.IsDefined(typeof(BuiltInCategory), bic)) cat = bic.ToString();
                    }
                    catch (Exception)
                    {
                        cat = string.Empty; // categorie inconnue : rang « autres », la forme reste calculee
                    }
                    item = new DanCI.BOQ.Core.Planning.Build4DItem
                    {
                        Id = id,
                        Rank = DanCI.BOQ.Core.Planning.Build4D.Rank(cat),
                        Zb = bb.Min.Z,
                        Courses = true // tout element monte par rangs de 20 cm (les elements plats se coulent en longueur)
                    };
                    if (e is Wall w && w.Location is LocationCurve lc && w.Width > 1e-6)
                    {
                        var pts = lc.Curve.Tessellate().Select(p => (p.X, p.Y)).ToList();
                        item.Shape.AddRange(DanCI.BOQ.Core.Extraction.Excavation3D.AlongPolyline(pts, w.Width, bb.Min.Z, bb.Max.Z, 0, 0));
                    }
                    if (item.Shape.Count == 0)
                    {
                        item.Shape.Add(DanCI.BOQ.Core.Planning.Build4D.Box(bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z));
                    }
                }
            }
            catch (Exception ex)
            {
                item = null; // element illisible : il apparaitra a la fin de sa tache
                sim4DShapeErrors++;
                if (sim4DShapeErrors <= 5) BoqLog.Warn("Studio 4D : forme de l'élément " + id + " illisible : " + ex.GetType().Name + " — " + ex.Message);
            }
            sim4DItems[id] = item;
            return item;
        }

        // ------------------------------------------------------------ vraie geometrie et couleur des materiaux (Studio 4D)
        private readonly Dictionary<long, DanCI.BOQ.Core.Planning.TriMesh> sim4DMeshes = new Dictionary<long, DanCI.BOQ.Core.Planning.TriMesh>();
        private readonly Dictionary<long, string> sim4DMaterialColors = new Dictionary<long, string>();
        /// <summary>Derniere decoupe de chaque element (reutilisee tant qu'il n'a pas monte d'un huitieme de rang).</summary>
        private readonly Dictionary<long, (int Key, List<DanCI.BOQ.Core.Planning.MeshPart> Parts)> sim4DCuts = new Dictionary<long, (int, List<DanCI.BOQ.Core.Planning.MeshPart>)>();

        /// <summary>Au-dela, l'element (famille tres detaillee) monte en volume simplifie : la cadence reste fluide.</summary>
        private const int MaxMeshTriangles = 6000;

        /// <summary>Maillage de l'element (lu une fois, garde en memoire) ; null = illisible ou trop detaille.</summary>
        /// <summary>Temps maximal par image pour lire les geometries et decouper (ms).</summary>
        private const int MeshBudgetMs = 70;

        private DanCI.BOQ.Core.Planning.TriMesh SourceMesh(Document doc, long id, bool mayRead = true)
        {
            if (sim4DMeshes.TryGetValue(id, out var cached)) return cached;
            if (!mayRead) return null; // lu a une prochaine image (budget de temps)
            DanCI.BOQ.Core.Planning.TriMesh mesh = null;
            try
            {
                Element e = doc.GetElement(new ElementId(id));
                GeometryElement ge = e?.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Medium, ComputeReferences = false });
                if (ge != null)
                {
                    var m = new DanCI.BOQ.Core.Planning.TriMesh();
                    if (Collect(ge, m) && m.TriangleCount > 0) mesh = m;
                }
            }
            catch (Exception)
            {
                mesh = null; // volume simplifie
            }
            sim4DMeshes[id] = mesh;
            return mesh;
        }

        private static DanCI.BOQ.Core.Planning.V3 P(XYZ p) => new DanCI.BOQ.Core.Planning.V3(p.X, p.Y, p.Z);

        /// <summary>Triangles de tous les solides (instances de famille comprises) ; faux si trop detaille.</summary>
        private static bool Collect(GeometryElement ge, DanCI.BOQ.Core.Planning.TriMesh m)
        {
            foreach (GeometryObject o in ge)
            {
                if (m.TriangleCount > MaxMeshTriangles) return false;
                if (o is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                {
                    foreach (Face f in solid.Faces)
                    {
                        Mesh fm = f.Triangulate();
                        if (fm == null) continue;
                        for (int i = 0; i < fm.NumTriangles; i++)
                        {
                            MeshTriangle t = fm.get_Triangle(i);
                            m.Add(P(t.get_Vertex(0)), P(t.get_Vertex(1)), P(t.get_Vertex(2)));
                        }
                    }
                }
                else if (o is GeometryInstance gi)
                {
                    GeometryElement inner = gi.GetInstanceGeometry();
                    if (inner != null && !Collect(inner, m)) return false;
                }
            }
            return m.TriangleCount <= MaxMeshTriangles;
        }

        /// <summary>Couleur d'ombrage du materiau principal de l'element (comme en vue « couleurs reelles » simplifiee).</summary>
        private string MaterialColor(Document doc, long id)
        {
            if (sim4DMaterialColors.TryGetValue(id, out string cached)) return cached;
            string hex = null;
            try
            {
                Element e = doc.GetElement(new ElementId(id));
                if (e != null)
                {
                    foreach (ElementId mid in e.GetMaterialIds(false))
                    {
                        if (doc.GetElement(mid) is Material mat && mat.Color != null && mat.Color.IsValid)
                        {
                            hex = "#" + mat.Color.Red.ToString("X2") + mat.Color.Green.ToString("X2") + mat.Color.Blue.ToString("X2");
                            break;
                        }
                    }
                    if (hex == null)
                    {
                        string cat = e.Category?.BuiltInCategory.ToString() ?? string.Empty;
                        hex = cat.Contains("Wall") ? "#D9D4C7" : cat.Contains("Floor") || cat.Contains("Slab") ? "#BDBDBD" : cat.Contains("Roof") ? "#9C6B4E" :
                              cat.Contains("Window") ? "#9EC3DE" : cat.Contains("Door") ? "#A0785A" : cat.Contains("Structural") ? "#B4B4B4" : "#C8CDD6";
                    }
                }
            }
            catch (Exception)
            {
                hex = null;
            }
            hex = hex ?? "#C8CDD6";
            sim4DMaterialColors[id] = hex;
            return hex;
        }

        /// <summary>Teinte plus claire (rang en cours de pose).</summary>
        private static string Lighten(string hex)
        {
            string h = (hex ?? "#C8CDD6").TrimStart('#');
            if (h.Length != 6) return hex;
            int L(int c) => Math.Min(255, c + (255 - c) * 45 / 100);
            return "#" + L(Convert.ToInt32(h.Substring(0, 2), 16)).ToString("X2") + L(Convert.ToInt32(h.Substring(2, 2), 16)).ToString("X2") + L(Convert.ToInt32(h.Substring(4, 2), 16)).ToString("X2");
        }

        /// <summary>Elements a montrer : construits, des taches en cours deja montes, fantomes ; parmi ceux que la vue sait masquer.</summary>
        private static HashSet<long> VisibleSet(Sim4DFrame f, HashSet<long> allSet, HashSet<long> inProgress, Dictionary<long, string> ghosts)
        {
            var set = new HashSet<long>();
            void Add(long i)
            {
                if (allSet.Contains(i) && (!inProgress.Contains(i) || ghosts.ContainsKey(i))) set.Add(i);
            }
            foreach (long i in f.Built) Add(i);
            foreach (long i in f.Running.Keys) Add(i);
            foreach (long i in ghosts.Keys) Add(i);
            return set;
        }

        /// <summary>Empreinte d'un ensemble, independante de l'ordre (aucun tri).</summary>
        private static string SetKey(HashSet<long> set)
        {
            ulong h = 0;
            foreach (long i in set)
            {
                ulong z = unchecked((ulong)i + 0x9E3779B97F4A7C15UL);
                z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
                z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
                h = unchecked(h + (z ^ (z >> 31)));
            }
            return set.Count + ":" + h.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ environnement de chantier (Studio 4D)
        private bool sim4DSky;
        private double? sim4DGroundZ;

        /// <summary>
        /// Niveau du terrain naturel (unites du modele) : niveau choisi dans les reglages du metre + decalage ;
        /// a defaut, tete des fouilles calculees ; a defaut, niveau le plus bas ; a defaut, bas du batiment.
        /// </summary>
        private double GroundZ()
        {
            if (sim4DGroundZ.HasValue) return sim4DGroundZ.Value;
            double? z = null;
            try
            {
                var ex = last?.Configuration?.Extraction?.Excavation;
                var levels = lastDocument != null && lastDocument.IsValidObject ? AnalysisService.LevelNames(lastDocument).ToList() : new List<string>();
                string tn = ex?.NaturalGroundLevel;
                if (!string.IsNullOrWhiteSpace(tn) && last?.LevelElevations != null && last.LevelElevations.TryGetValue(tn, out double m))
                    z = (m + (ex?.OffsetM ?? 0)) * 3.28084;
                if (!z.HasValue && sim4DPits != null && sim4DPits.Count > 0) z = sim4DPits.Max(p => p.Zt);
                string low = levels.FirstOrDefault();
                if (!z.HasValue && low != null && last?.LevelElevations != null && last.LevelElevations.TryGetValue(low, out double lm)) z = lm * 3.28084;
            }
            catch (Exception)
            {
                z = null;
            }
            sim4DGroundZ = z ?? sim4DBox?.Min.Z ?? 0;
            return sim4DGroundZ.Value;
        }
        private string sim4DSiteKey;

        /// <summary>Terrain, palissade, grue, echafaudages : redessines seulement quand l'etat du chantier change.</summary>
        private void ShowSite(Document doc, Sim4DFrame f)
        {
            try
            {
                if (!f.Environment || f.Site == null || sim4DBox == null)
                {
                    if (sim4DSiteKey != null) ChartServer.HideEnvironment();
                    sim4DSiteKey = null;
                    return;
                }
                var st = f.Site;
                var dig = new DanCI.BOQ.Core.Planning.DigScene
                {
                    GroundZ = GroundZ(),
                    Pits = sim4DPits ?? new List<DanCI.BOQ.Core.Extraction.Prism>(),
                    Dug = f.Earthwork < 0 ? 0 : sim4DDug,
                    Progress = Math.Max(0, f.Earthwork),
                    Open = f.Earthwork >= 0,
                    Excavating = f.Earthwork > 0 && f.Earthwork < 0.999
                };
                string key = Math.Round(st.Structure, 2) + "|" + st.Crane + "|" + st.Scaffold + "|" + Math.Round(st.SlewDeg) + "|" +
                             (dig.Excavating ? Math.Round(st.Phase, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-") + "|" + dig.Dug + "|" + dig.Open + "|" +
                             sim4DBox.Value.Min + sim4DBox.Value.Max;
                if (key == sim4DSiteKey) return;
                sim4DSiteKey = key;
                ChartServer.ShowEnvironment(doc, DanCI.BOQ.Core.Planning.SiteEnvironment.Build(P(sim4DBox.Value.Min), P(sim4DBox.Value.Max), st, dig)
                    .Select(p => (p.Mesh, p.Color)));
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Studio 4D, environnement de chantier : " + ex.Message);
            }
        }

        private static Color HexColor(string hex)
        {
            string h = (hex ?? "#808080").TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6) return new Color(128, 128, 128);
            return new Color(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
        }

        public void RequestEndSimulation4D() => Enqueue(app =>
        {
            CloseVideo();
            try { DeleteBuildShapes(app.ActiveUIDocument?.Document); } catch (Exception ex) { BoqLog.Warn("Studio 4D, volumes temporaires : " + ex.Message); }
            try { ChartServer.Hide4D(); } catch (Exception) { /* rien a retirer */ }
            try { ChartServer.HideEnvironment(); } catch (Exception) { /* rien a retirer */ }
            sim4DSiteKey = null;
            sim4DGroundZ = null;
            lock (frameLock)
            {
                pendingFrame = null;
            }
            Document doc = app.ActiveUIDocument?.Document;
            View3D view = sim4DViewId != null && doc != null ? doc.GetElement(sim4DViewId) as View3D : null;
            if (view == null) return;
            using (var t = new Transaction(doc, "DanCI 4D — fin de la simulation"))
            {
                t.Start();
                foreach (long id in sim4DColored.Keys)
                {
                    var eid = new ElementId(id);
                    if (doc.GetElement(eid) != null) view.SetElementOverrides(eid, new OverrideGraphicSettings());
                }
                if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                {
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                }
                try
                {
                    var back = sim4DHidden.Select(i => doc.GetElement(new ElementId(i))).Where(e => e != null && e.IsHidden(view)).Select(e => e.Id).ToList();
                    if (back.Count > 0) view.UnhideElements(back);
                }
                catch (Exception ex)
                {
                    BoqLog.Warn("Studio 4D, réaffichage des éléments : " + ex.Message);
                }
                t.Commit();
            }
            sim4DHidden = new HashSet<long>();
            sim4DColored.Clear();
            sim4DShown = null;
            sim4DVisibleKey = null;
            sim4DAngle = null;
            sim4DBox = null;
            sim4DItems.Clear();
            sim4DMeshes.Clear();
            sim4DMaterialColors.Clear();
            sim4DCuts.Clear();
            sim4DShapeErrors = 0;
            sim4DVisibleSet = null;
            sim4DPits = null;
            sim4DPitsShown = -1;
            try { ChartServer.HideExcavations(); } catch (Exception) { }
            try { ChartServer.Hide4D(); } catch (Exception) { }
            app.ActiveUIDocument.RefreshActiveView();
            SetStatus("Studio 4D fermé : la vue « " + Sim4DViewName + " » montre de nouveau tout le modèle (couleurs retirées).");
        });

        public void RequestResetIsolation() => Enqueue(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            View view = uidoc?.ActiveView;
            if (view == null || !view.CanUseTemporaryVisibilityModes() || !view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
            {
                SetStatus("Rien à réafficher : la vue active n'est pas isolée.");
                return;
            }
            using (var t = new Transaction(uidoc.Document, "DanCI BOQ — afficher tout"))
            {
                t.Start();
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                t.Commit();
            }
            SetStatus("Vue « " + view.Name + " » : isolement temporaire annulé.");
        });

        private (string Key, double[] Box)? modelExtents;

        public void RequestChart3D(bool show) => Enqueue(app =>
        {
            DanCI.BOQ.UI.Chart.ViewChartViewModel chart = DanCI.BOQ.UI.Chart.ViewChartViewModel.Current;
            UIDocument uidoc = app.ActiveUIDocument;
            if (!show || chart.Chart == null || chart.Chart.Bars.Count == 0)
            {
                ChartServer.Hide();
                uidoc?.RefreshActiveView();
                if (!show) SetStatus("Barres 3D retirées de la vue.");
                return;
            }
            if (!IsLastDocument(uidoc))
            {
                SetStatus("Barres 3D impossibles : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            double[] box = ModelExtents(uidoc.Document, chart.Chart);
            if (box == null)
            {
                SetStatus("Barres 3D impossibles : aucun élément métré du modèle hôte n'a d'emprise (éléments de liens seulement ?).");
                return;
            }
            List<DanCI.BOQ.Core.Dashboard.ChartBox> boxes = Chart3DBoxes(uidoc.Document, chart, box);
            ChartServer.Show(uidoc.Document, boxes);
            uidoc.RefreshActiveView();
            SetStatus(uidoc.ActiveView is View3D
                ? "Barres 3D affichées devant la façade sud du modèle (affichage seulement, rien n'est écrit dans le modèle)."
                : "Barres 3D prêtes : elles s'affichent dans les vues 3D (ouvrez une vue 3D).");
        });

        /// <summary>
        /// Barres 3D selon le mode du panneau : rangee simple (lots, statuts, prix), une barre par niveau
        /// a son altitude (mode Niveau), une rangee de materiaux par niveau a son altitude (mode Materiaux).
        /// </summary>
        private static List<DanCI.BOQ.Core.Dashboard.ChartBox> Chart3DBoxes(Document doc, DanCI.BOQ.UI.Chart.ViewChartViewModel chart, double[] box)
        {
            var elevations = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                if (!elevations.ContainsKey(l.Name)) elevations[l.Name] = l.Elevation;
            }
            double? Elevation(string level) => level != null && elevations.TryGetValue(level, out double z) ? z : (double?)null;
            var metric = chart.Metric;
            switch (chart.Group)
            {
                case DanCI.BOQ.Core.Dashboard.ChartGroup.Niveau:
                {
                    var rows = new List<DanCI.BOQ.Core.Dashboard.ChartLevelRow>();
                    foreach (var bar in chart.Chart.Bars)
                    {
                        var row = new DanCI.BOQ.Core.Dashboard.ChartLevelRow { Level = bar.Key, ElevationFt = Elevation(bar.Key) };
                        row.Bars.Add((bar.Key, bar.Value(metric), bar.Color));
                        rows.Add(row);
                    }
                    return DanCI.BOQ.Core.Dashboard.ChartLayout.Grouped(rows, box[0], box[1], box[2], box[3], box[4], box[5]);
                }
                case DanCI.BOQ.Core.Dashboard.ChartGroup.Materiau:
                {
                    // Colonnes communes a tous les niveaux : celles du diagramme affiche (ordre et couleurs).
                    List<string> keys = chart.Chart.Bars.Select(b => b.Key).ToList();
                    var rows = new List<DanCI.BOQ.Core.Dashboard.ChartLevelRow>();
                    IEnumerable<string> levels = chart.LevelFilter != null
                        ? new[] { chart.LevelFilter }
                        : chart.LevelChoices.Where(c => c != DanCI.BOQ.UI.Chart.ViewChartViewModel.AllLevels);
                    foreach (string level in levels)
                    {
                        var perLevel = DanCI.BOQ.Core.Dashboard.ViewChart.ByMaterial(chart.Inventory, level, int.MaxValue).Bars.ToDictionary(b => b.Key);
                        var row = new DanCI.BOQ.Core.Dashboard.ChartLevelRow { Level = level, ElevationFt = Elevation(level) };
                        foreach (var global in chart.Chart.Bars)
                        {
                            row.Bars.Add((global.Key, perLevel.TryGetValue(global.Key, out var b) ? b.Value(metric) : 0m, global.Color));
                        }
                        rows.Add(row);
                    }
                    return DanCI.BOQ.Core.Dashboard.ChartLayout.Grouped(rows, box[0], box[1], box[2], box[3], box[4], box[5]);
                }
                default:
                    return DanCI.BOQ.Core.Dashboard.ChartLayout.Bars(chart.Chart, metric, box[0], box[1], box[2], box[3], box[4], box[5]);
            }
        }

        /// <summary>Emprise des elements metres du modele hote (pieds), mise en cache par analyse.</summary>
        private double[] ModelExtents(Document doc, DanCI.BOQ.Core.Dashboard.ViewChart chart)
        {
            string key = ChartServer.DocumentKey(doc) + "|" + (last?.GetHashCode() ?? 0) + "|" + chart.Group + "|" + chart.Level;
            if (modelExtents.HasValue && modelExtents.Value.Key == key) return modelExtents.Value.Box;
            double[] b = null;
            foreach (long id in chart.Bars.SelectMany(x => x.HostElementIds).Distinct())
            {
                BoundingBoxXYZ bb = doc.GetElement(new ElementId(id))?.get_BoundingBox(null);
                if (bb == null) continue;
                XYZ min = bb.Transform.OfPoint(bb.Min), max = bb.Transform.OfPoint(bb.Max);
                double[] e = { Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z), Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z) };
                if (b == null) { b = e; continue; }
                for (int i = 0; i < 3; i++) { b[i] = Math.Min(b[i], e[i]); b[i + 3] = Math.Max(b[i + 3], e[i + 3]); }
            }
            modelExtents = (key, b);
            return b;
        }

        private static DateTime NextMonday()
        {
            DateTime d = DateTime.Today.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
            return d;
        }

        private bool IsLastDocument(UIDocument uidoc) =>
            uidoc != null && lastDocument != null && lastDocument.IsValidObject && uidoc.Document.Equals(lastDocument);

        private void Show(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            if (window != null)
            {
                window.ViewModel.SetLevels(AnalysisService.LevelNames(doc));
                if (window.WindowState == System.Windows.WindowState.Minimized)
                {
                    window.WindowState = System.Windows.WindowState.Normal;
                }
                window.Activate();
                return;
            }
            var vm = new BoqWindowViewModel(this);
            vm.SetLevels(AnalysisService.LevelNames(doc));
            LoadProjectContext(vm, doc);
            window = new BoqWindow(vm);
            if (BoqWindow.ThemeError != null)
            {
                BoqLog.Warn("Thème futuriste non chargé (fenêtre en contrôles standard) : " + BoqWindow.ThemeError);
            }
            new WindowInteropHelper(window) { Owner = app.MainWindowHandle };
            window.Closed += (s, e) => window = null;
            window.Show();
        }

        public void RequestAnalysis(AnalysisScope scope, IReadOnlyList<ManualLine> manualLines) => Enqueue(app => Analyze(app, scope, manualLines));

        public void RequestRecompute(IReadOnlyList<ManualLine> manualLines) => Enqueue(app =>
        {
            if (last == null)
            {
                window?.ViewModel.Apply(new AnalysisOutcome { Failure = "Forfaits enregistrés. Lancez une analyse pour les voir dans le BOQ." });
                return;
            }
            AnalysisOutcome recomputed = AnalysisService.Recompute(last, manualLines, window?.ViewModel.PriceLibraryPath, window?.ViewModel.ProjectEstimate);
            if (recomputed.Boq != null)
            {
                last = recomputed;
            }
            ShowOutcome(recomputed);
        });

        public void RequestSelection(IReadOnlyCollection<ElementRef> elements) => Enqueue(app => Select(app, elements));

        public void RequestExport() => Enqueue(Export);

        public void RequestPreview() => Enqueue(Preview);

        public void RequestColorize(DanCI.BOQ.Core.Dashboard.ColoringMode mode) => Enqueue(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject || uidoc == null || !uidoc.Document.Equals(lastDocument))
            {
                SetStatus("Vue colorée impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            string name = ModelColorizer.ViewName(mode);
            var confirm = new TaskDialog("DanCI — vue 3D colorée")
            {
                MainInstruction = "Créer ou mettre à jour la vue « " + name + " » ?",
                MainContent = "Cette action MODIFIE le modèle : elle crée (ou réutilise) une vue 3D et y applique des couleurs par élément. " +
                              "Les éléments eux-mêmes ne sont pas modifiés. Une seule annulation (Ctrl+Z) défait l'ensemble.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                SetStatus("Vue colorée annulée : modèle non modifié.");
                return;
            }
            SetStatus(ModelColorizer.Apply(uidoc, last.Boq, mode));
        });

        public void RequestFreezeRevision() => Enqueue(app =>
        {
            if (!HasAnalysis(app)) return;
            string path = SaveRevision();
            SetStatus("Révision enregistrée : " + path);
        });

        public void RequestRevisionComparison(string snapshotPath) => Enqueue(app =>
        {
            if (!HasAnalysis(app)) return;
            var issues = new List<BoqIssue>();
            Core.Revisions.BoqSnapshot a = Core.Revisions.RevisionStore.List(Path.GetDirectoryName(snapshotPath), issues)
                .FirstOrDefault(x => string.Equals(x.FilePath, snapshotPath, StringComparison.OrdinalIgnoreCase));
            if (a == null)
            {
                SetStatus("Révision illisible ou introuvable : " + snapshotPath);
                return;
            }
            Core.Revisions.BoqSnapshot b = CurrentSnapshot();
            Core.Revisions.RevisionComparison cmp = Core.Revisions.RevisionComparer.Compare(a, b);
            string path = ValidationFile("_comparaison_revisions.xlsx");
            DanCI.BOQ.Export.Excel.RevisionWorkbook.Write(path, cmp);
            BoqLog.Info("Comparaison de révisions : " + cmp.Summary() + " — " + path);
            SetStatus(cmp.Summary() + " Classeur : " + path);
            OpenFolder(path);
        });

        /// <summary>Instantane de la derniere analyse, avec l'identite et l'empreinte du devis.</summary>
        private Core.Revisions.BoqSnapshot CurrentSnapshot()
        {
            DanCI.BOQ.Core.Model.ProjectInfo identity = window?.ViewModel.ProjectIdentity ?? AnalysisService.ReadProjectInfo(lastDocument);
            return Core.Revisions.BoqSnapshot.From(last.Boq, identity, Core.Validation.QuantityFingerprint.Compute(last.Quantities), DateTime.Now);
        }

        private string SaveRevision()
        {
            string folder = Core.Revisions.RevisionStore.FolderFor(AnalysisService.ProjectFilePath(lastDocument));
            string path = Core.Revisions.RevisionStore.Save(folder, CurrentSnapshot());
            BoqLog.Info("Révision enregistrée : " + path);
            if (window != null)
            {
                var issues = new List<BoqIssue>();
                window.ViewModel.SetRevisions(Core.Revisions.RevisionStore.List(folder, issues));
            }
            return path;
        }

        /// <summary>
        /// Apercu : meme contexte que l'export, rendu en images (PDF) et en feuilles lues en
        /// memoire (Excel). Rien n'est ecrit sur le disque.
        /// </summary>
        private void Preview(UIApplication app)
        {
            if (last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject || window == null)
            {
                SetStatus("Aperçu impossible : lancez d'abord une analyse.");
                return;
            }
            DanCI.BOQ.Export.ExportContext ctx = AnalysisService.BuildExportContext(lastDocument, last, window.ViewModel.ProjectIdentity, window.ViewModel.ProjectCover, window.ViewModel.ProjectSchedule);
            ctx.RevisionHistory = RevisionHistory();
            try { ctx.DesignImages = DesignCapture.Cached(lastDocument); } // apercu fidele a l'export (images gardees 15 min)
            catch (Exception ex) { BoqLog.Warn("Aperçu : images de la conception non produites : " + ex.Message); }
            IReadOnlyList<byte[]> pages = null;
            string pdfProblem = DanCI.BOQ.Export.BoqExporter.MissingPdfComponents();
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                if (pdfProblem == null)
                {
                    try
                    {
                        pages = DanCI.BOQ.Export.Pdf.BoqPdfWriter.GeneratePageImages(ctx);
                    }
                    catch (Exception ex)
                    {
                        pdfProblem = ex.GetType().Name + " : " + ex.Message;
                        BoqLog.Error("Aperçu PDF en échec.", ex);
                    }
                }
                List<DanCI.BOQ.Core.Preview.SheetPreview> sheets = null;
                try
                {
                    sheets = DanCI.BOQ.Export.Preview.WorkbookPreview.Build(ctx);
                }
                catch (Exception ex)
                {
                    BoqLog.Error("Aperçu Excel en échec.", ex);
                    SetStatus("Aperçu Excel en échec : " + ex.Message);
                }
                var preview = new DanCI.BOQ.UI.Preview.PreviewWindow(ctx.ProjectTitle, pages, pdfProblem, sheets, () => RequestExport()) { Owner = window };
                preview.Show();
                SetStatus("Aperçu ouvert : " + (pages?.Count ?? 0) + " page(s) PDF, " + (sheets?.Count ?? 0) + " feuille(s) Excel. Rien n'a été écrit sur le disque." +
                          (ctx.Cover?.MissingFiles().Any() == true ? " ATTENTION : image de page de garde introuvable." : string.Empty));
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        public void RequestSaveProject(ProjectFile project) => Enqueue(app => SaveProject(app, project));

        public void RequestWriteParameters() => Enqueue(WriteParameters);

        public void RequestValidationTemplate() => Enqueue(app =>
        {
            if (!HasAnalysis(app)) return;
            string path = ValidationFile("_validation_gabarit.xlsx");
            DanCI.BOQ.Export.Validation.ValidationWorkbook.WriteTemplate(path, last.Quantities, last.Boq, last.DocumentTitle);
            BoqLog.Info("Gabarit de validation : " + path);
            SetStatus("Gabarit écrit : " + path + " — à faire remplir à la main, puis « Comparer au gabarit rempli ».");
            OpenFolder(path);
        });

        public void RequestManualComparison(string filledTemplatePath) => Enqueue(app =>
        {
            if (!HasAnalysis(app)) return;
            List<DanCI.BOQ.Core.Validation.ManualEntry> manual = DanCI.BOQ.Export.Validation.ValidationWorkbook.ReadManual(filledTemplatePath);
            if (manual.Count == 0)
            {
                SetStatus("Le gabarit ne contient aucune quantité manuelle (colonne « Quantité manuelle »).");
                return;
            }
            var report = DanCI.BOQ.Core.Validation.TakeoffComparer.Compare(last.Quantities, last.Boq, manual);
            string path = ValidationFile("_validation_rapport.xlsx");
            DanCI.BOQ.Export.Validation.ValidationWorkbook.WriteComparison(path, report, last.DocumentTitle,
                DanCI.BOQ.Core.Validation.QuantityFingerprint.Compute(last.Quantities));
            string verdict = report.Kinds.Any(k => k.IsTarget && k.Compared > 0)
                ? (report.TargetsMet ? "OBJECTIF ATTEINT (béton et acier < 2 %)" : "OBJECTIF NON ATTEINT : écarts à expliquer")
                : "objectif non évaluable (aucun béton/acier mesuré des deux côtés)";
            BoqLog.Info("Validation : " + manual.Count + " mesures manuelles — " + verdict + " — " + path);
            SetStatus(manual.Count + " mesure(s) manuelle(s) comparée(s) : " + verdict + ". Rapport : " + path);
            OpenFolder(path);
        });

        public void RequestRunComparison(string rawCsvA, string rawCsvB) => Enqueue(app =>
        {
            var a = DanCI.BOQ.Export.Csv.QuantityCsvReader.ReadFile(rawCsvA);
            var b = DanCI.BOQ.Export.Csv.QuantityCsvReader.ReadFile(rawCsvB);
            var report = DanCI.BOQ.Core.Validation.RunComparer.Compare(a, b);
            string path = Path.Combine(Path.GetDirectoryName(rawCsvA),
                "DanCI_reproductibilite_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx");
            DanCI.BOQ.Export.Validation.ValidationWorkbook.WriteRunComparison(path, report, rawCsvA, rawCsvB);
            BoqLog.Info("Reproductibilité : " + (report.Identical ? "identiques" : report.Differences.Count + " différence(s)") + " — " + path);
            SetStatus((report.Identical
                ? "Métrés IDENTIQUES (empreinte " + DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(report.FingerprintA) + ")."
                : report.Differences.Count + " quantité(s) différente(s) entre les deux métrés.") + " Rapport : " + path);
            OpenFolder(path);
        });

        public void RequestPriceLibrary(string path) => Enqueue(app =>
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null || window == null)
            {
                return;
            }
            string chosen = path;
            if (path != null && path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                // Import Excel -> bibliotheque JSON du meme nom, validee contre le catalogue.
                var issues = new List<BoqIssue>();
                DanCI.BOQ.Core.Pricing.PriceLibrary lib = DanCI.BOQ.Export.Excel.PriceLibraryWorkbook.Read(path, issues);
                if (issues.Any(i => i.Severity == IssueSeverity.Error))
                {
                    SetStatus("Import refusé : " + string.Join(" | ", issues.Where(i => i.Severity == IssueSeverity.Error).Take(3).Select(i => i.Message)));
                    return;
                }
                chosen = Path.ChangeExtension(path, ".json");
                DanCI.BOQ.Core.Pricing.PriceLibraryStore.Save(chosen, lib);
                BoqLog.Info("Bibliothèque importée : " + path + " -> " + chosen + " (" + lib.Prices.Count + " prix).");
            }
            window.ViewModel.PriceLibraryPath = chosen;
            SaveProject(app, window.ViewModel.BuildProjectFile());
            RefreshPriceRows(window.ViewModel);
            SetStatus(chosen == null
                ? "Prix : bibliothèque de l'installation."
                : "Prix : " + Path.GetFileName(chosen) + (last != null ? " — recalcul…" : " — lancez l'analyse."));
            if (last != null)
            {
                AnalysisOutcome recomputed = AnalysisService.Recompute(last, window.ViewModel.CurrentManualLines(), chosen, window.ViewModel.ProjectEstimate);
                if (recomputed.Boq != null)
                {
                    last = recomputed;
                }
                ShowOutcome(recomputed);
            }
        });

        public void RequestPriceTemplate() => Enqueue(app =>
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return;
            }
            LoadResult load = ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory);
            AnalysisService.UsePriceLibrary(load, window?.ViewModel.PriceLibraryPath);
            string folder = AnalysisService.OutputFolder(doc);
            string name = Path.GetFileNameWithoutExtension(doc.Title ?? "projet");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string path = Path.Combine(folder, name + "_DanCI_prix_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx");
            DanCI.BOQ.Export.Excel.PriceLibraryWorkbook.WriteTemplate(path, load.Configuration?.Mapping, load.Configuration?.Prices);
            SetStatus("Modèle des prix écrit : " + path + " — remplissez les PU, puis « Choisir… » ce classeur pour l'importer.");
            OpenFolder(path);
        });

        private bool HasAnalysis(UIApplication app)
        {
            if (last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject)
            {
                SetStatus("Lancez d'abord une analyse.");
                return false;
            }
            return true;
        }

        private string ValidationFile(string suffix)
        {
            string folder = AnalysisService.OutputFolder(lastDocument);
            string name = Path.GetFileNameWithoutExtension(lastDocument.Title ?? "projet");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(folder, name + "_DanCI_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + suffix);
        }

        private static void OpenFolder(string file) =>
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(file)) { UseShellExecute = true });

        /// <summary>Phases, liens et fichier projet du document actif.</summary>
        private void LoadProjectContext(BoqWindowViewModel vm, Document doc)
        {
            string path = AnalysisService.ProjectFilePath(doc);
            var issues = new List<DanCI.BOQ.Core.Model.BoqIssue>();
            ProjectFile project = ProjectFileStore.Load(path, issues);
            vm.SetContext(ModelExtractor.PhaseNames(doc), ModelExtractor.LoadedLinkNames(doc), project, path);
            LoadResult load = ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory);
            var companyIssues = new List<BoqIssue>();
            PartyInfo company = CompanyProfileStore.Load(CompanyProfileStore.DefaultPath, companyIssues);
            if (companyIssues.Count > 0)
            {
                BoqLog.Warn("Fiche entreprise illisible (" + CompanyProfileStore.DefaultPath + ") : " + companyIssues[0].Message + " — valeurs par défaut utilisées.");
            }
            vm.SetSettingsContext(project, AnalysisService.ReadProjectInfo(doc), load.Configuration?.Settings, null, null, company);
            vm.Settings.LoadSchedule(project?.Schedule, CatalogLots(load));
            vm.Settings.LoadExtraction(project?.Extraction, load.Configuration?.Extraction, AnalysisService.LevelNames(doc));
            RefreshPriceRows(vm);
            var revIssues = new List<BoqIssue>();
            vm.SetRevisions(Core.Revisions.RevisionStore.List(Core.Revisions.RevisionStore.FolderFor(path), revIssues));
            if (issues.Count > 0)
            {
                vm.Status = "Fichier projet illisible (" + issues[0].Message + ") : il ne sera PAS écrasé sans votre accord, corrigez-le ou supprimez-le.";
                BoqLog.Warn("Fichier projet illisible : " + path + " — " + issues[0].Message);
            }
        }

        /// <summary>Lots du catalogue (et du lot NC s'il y a des non classes) pour l'echeancier.</summary>
        private static IEnumerable<(string, string)> CatalogLots(LoadResult load, BoqResult boq = null)
        {
            var lots = (load.Configuration?.Mapping?.Lots ?? new List<Lot>()).Select(l => (l.Code, l.Designation)).ToList();
            foreach (BoqLotResult extra in boq?.Lots ?? new List<BoqLotResult>())
            {
                if (!lots.Any(l => l.Code == extra.Code)) lots.Add((extra.Code, extra.Designation));
            }
            return lots;
        }

        /// <summary>Prix generaux de chaque article, lus dans la bibliotheque en vigueur (projet ou installation).</summary>
        private void RefreshPriceRows(BoqWindowViewModel vm)
        {
            LoadResult load = ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory);
            AnalysisService.UsePriceLibrary(load, vm.PriceLibraryPath);
            Core.Pricing.Currency currency = vm.ProjectEstimate?.Currency ?? load.Configuration?.Settings?.Currency ?? Core.Pricing.Currency.USD;
            string info = string.IsNullOrEmpty(vm.PriceLibraryPath)
                ? "Prix lus dans la bibliothèque de l'installation (config\\prix_unitaires.json). À l'enregistrement, vos prix sont écrits dans une bibliothèque PROPRE AU PROJET, à côté du fichier projet : l'installation n'est pas modifiée."
                : "Bibliothèque du projet : " + vm.PriceLibraryPath + ". Les prix saisis y sont enregistrés (copie de sauvegarde .bak).";
            // Comme le BPU : articles et variantes (famille / type / materiau) REELLEMENT dans le modele analyse.
            BoqResult boq = last?.Boq != null && lastDocument != null && lastDocument.IsValidObject ? last.Boq : null;
            vm.Settings.SetPrices(Core.Pricing.PriceEditor.RowsFromModel(boq, load.Configuration?.Mapping, load.Configuration?.Prices, currency), info);
        }

        public void RequestSaveSettings(ProjectFile project, IReadOnlyList<Core.Pricing.PriceEdit> prices, PartyInfo company) => Enqueue(app =>
        {
            if (company != null)
            {
                // Fiche commune a tous les projets : %AppData%\DanCI\BOQ\entreprise.json (logo en chemin absolu).
                CompanyProfileStore.Save(CompanyProfileStore.DefaultPath, company);
                BoqLog.Info("Fiche entreprise enregistrée : " + CompanyProfileStore.DefaultPath);
            }
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null || window == null)
            {
                return;
            }
            BoqWindowViewModel vm = window.ViewModel;
            string projectPath = AnalysisService.ProjectFilePath(doc);
            if (prices != null)
            {
                LoadResult load = ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory);
                AnalysisService.UsePriceLibrary(load, vm.PriceLibraryPath);
                var issues = new List<BoqIssue>();
                Core.Pricing.PriceLibrary updated = Core.Pricing.PriceEditor.Apply(load.Configuration?.Prices, prices, issues);
                if (updated == null)
                {
                    vm.IsBusy = false;
                    SetStatus("Prix NON enregistrés : " + string.Join(" | ", issues.Take(4).Select(i => i.Message)));
                    return;
                }
                // Bibliotheque de l'installation : jamais modifiee ; on cree celle du projet.
                string target = vm.PriceLibraryPath;
                if (string.IsNullOrEmpty(target))
                {
                    string stem = Path.GetFileName(projectPath);
                    stem = stem.Substring(0, stem.Length - ProjectFile.Extension.Length);
                    target = Path.Combine(Path.GetDirectoryName(projectPath), stem + "_prix.json");
                    updated.Name = "Prix du projet " + stem;
                }
                else if (File.Exists(target))
                {
                    File.Copy(target, target + ".bak", overwrite: true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Core.Pricing.PriceLibraryStore.Save(target, updated);
                BoqLog.Info("Prix du projet enregistrés : " + target + " (" + updated.Prices.Count + " prix).");
                vm.PriceLibraryPath = target;
                project = vm.BuildProjectFile();
            }
            if (!SaveProject(app, project))
            {
                vm.IsBusy = false;
                return;
            }
            RefreshPriceRows(vm);
            vm.Settings.Load(project, projectPath, AnalysisService.ReadProjectInfo(doc),
                ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory).Configuration?.Settings);
            vm.Settings.ClientParty.Load(project.ClientParty?.Resolve(projectPath));
            LoadResult reloaded = ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory);
            vm.Settings.LoadSchedule(project.Schedule, CatalogLots(reloaded, last?.Boq));
            vm.Settings.LoadExtraction(project.Extraction, reloaded.Configuration?.Extraction, AnalysisService.LevelNames(doc));
            if (company != null) vm.Settings.Company.Load(company);
            if (last != null)
            {
                AnalysisOutcome recomputed = AnalysisService.Recompute(last, vm.CurrentManualLines(), vm.PriceLibraryPath, vm.ProjectEstimate);
                if (recomputed.Boq != null)
                {
                    last = recomputed;
                }
                ShowOutcome(recomputed);
                if (recomputed.Failure == null)
                {
                    SetStatus("Paramètres du projet enregistrés ; BOQ et tableau de bord recalculés." +
                              (vm.ExtractionChangedSinceAnalysis ? " Réglages du métré modifiés : relancez l'ANALYSE pour les appliquer." : string.Empty));
                }
            }
            else
            {
                vm.IsBusy = false;
                SetStatus("Paramètres du projet enregistrés : " + projectPath + ". Lancez l'analyse.");
            }
        });

        private bool SaveProject(UIApplication app, ProjectFile project)
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return false;
            }
            string path = AnalysisService.ProjectFilePath(doc);
            var issues = new List<DanCI.BOQ.Core.Model.BoqIssue>();
            ProjectFileStore.Load(path, issues);
            if (issues.Count > 0)
            {
                SetStatus("Fichier projet existant illisible : non écrasé. Corrigez ou supprimez " + path);
                return false;
            }
            ProjectFileStore.Save(path, project);
            BoqLog.Info("Fichier projet enregistré : " + path);
            if (window != null)
            {
                window.ViewModel.ProjectFilePath = path;
            }
            SetStatus("Fichier projet enregistré : " + path);
            return true;
        }

        private void WriteParameters(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject || uidoc == null || !uidoc.Document.Equals(lastDocument))
            {
                SetStatus("Écriture impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            var plan = ModelParameterWriter.Plan(last.Boq, out int linked);
            var confirm = new TaskDialog("DanCI — écrire les codes BOQ dans le modèle")
            {
                MainInstruction = "Écrire DanCI_BOQ_Code et DanCI_BOQ_Lot sur " + plan.Count + " élément(s) ?",
                MainContent =
                    "Cette action MODIFIE le modèle :" + Environment.NewLine +
                    "  • crée si besoin les paramètres partagés DanCI (Code, Lot, Exclure, Revision) ;" + Environment.NewLine +
                    "  • écrit le code des articles et le lot de chaque élément métré du modèle hôte." + Environment.NewLine +
                    (linked > 0 ? "  • " + linked + " contribution(s) d'éléments de liens RVT ne seront pas écrites (modèle lié non modifiable)." + Environment.NewLine : string.Empty) +
                    Environment.NewLine + "Une seule annulation (Ctrl+Z) défait l'ensemble.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                SetStatus("Écriture annulée : modèle non modifié.");
                return;
            }
            ModelParameterWriter.WriteResult r = ModelParameterWriter.Write(app.Application, uidoc.Document, last.Boq);
            if (r.Error != null)
            {
                SetStatus(r.Error);
                TaskDialog.Show("DanCI — écriture des codes", r.Error);
                return;
            }
            SetStatus("Codes écrits sur " + r.Written + " élément(s) ; " + r.Unchanged + " déjà à jour" +
                      (r.Failed > 0 ? " ; " + r.Failed + " échec(s) (paramètre absent ou en lecture seule)" : string.Empty) +
                      (r.SkippedOwnedByOthers > 0 ? " ; " + r.SkippedOwnedByOthers + " emprunté(s) par un autre utilisateur" : string.Empty) +
                      (r.SkippedLinked > 0 ? " ; " + r.SkippedLinked + " contribution(s) de liens ignorée(s)" : string.Empty) + ". Annulable (Ctrl+Z).");
        }

        private void Enqueue(Action<UIApplication> request)
        {
            lock (requests)
            {
                requests.Enqueue(request);
            }
            ExternalEventRequest status = externalEvent.Raise();
            if (status == ExternalEventRequest.Denied || status == ExternalEventRequest.TimedOut)
            {
                lock (requests)
                {
                    requests.Clear();
                }
                Sim4DFrame lost;
                lock (frameLock)
                {
                    frameQueued = false; // sinon le Studio 4D n'enverrait plus jamais d'image
                    lost = pendingFrame;
                    pendingFrame = null;
                }
                lost?.Applied?.Invoke(); // le Studio n'attend pas une image qui ne viendra pas
                DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Status = "⚠ Revit n'accepte pas la demande pour l'instant (commande ou dialogue en cours ?).";
                BoqLog.Warn("Requête refusée par Revit : " + status);
                if (window != null)
                {
                    window.ViewModel.IsBusy = false;
                    window.ViewModel.Status = "Revit n'accepte pas la demande pour l'instant (commande ou dialogue en cours ?) : terminez-la, puis réessayez.";
                }
            }
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> next;
                lock (requests)
                {
                    if (requests.Count == 0)
                    {
                        return;
                    }
                    next = requests.Dequeue();
                }
                try
                {
                    next(app);
                }
                catch (Exception ex)
                {
                    BoqLog.Error("Requête de la fenêtre BOQ en échec.", ex);
                    DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Status = "Erreur : " + ex.Message;
                    if (window != null)
                    {
                        window.ViewModel.IsBusy = false;
                        window.ViewModel.Status = "Erreur : " + ex.Message + " (détails dans le journal).";
                    }
                }
            }
        }

        public string GetName() => "DanCI — Métré & BOQ";

        /// <summary>Affiche un resultat et redessine la planche du tableau de bord.</summary>
        private void ShowOutcome(AnalysisOutcome outcome)
        {
            if (outcome?.Boq != null)
            {
                List<string> levels = lastDocument != null && lastDocument.IsValidObject ? AnalysisService.LevelNames(lastDocument).ToList() : new List<string>();
                DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.SetBoq(outcome.Boq, outcome.Boq.Currency.ToString(), outcome.Inventory, levels,
                    LevelBreakdown.GroundLevel(levels, outcome.Configuration?.Extraction));
                // Planning pour la simulation 4D : celui de la fenetre (date de l'echeancier) s'il existe.
                window?.ViewModel.Apply(outcome);
                // Parametres du projet > Prix unitaires : meme liste que le BPU (sauf saisie en cours non enregistree).
                if (window != null && !window.ViewModel.Settings.PricesChanged)
                {
                    try { RefreshPriceRows(window.ViewModel); }
                    catch (Exception ex) { BoqLog.Warn("Prix du projet non rafraîchis : " + ex.Message); }
                }
                var plan = window?.ViewModel.WorkPlan ?? DanCI.BOQ.Core.Planning.WorkPlanner.Build(outcome.Boq, levels,
                    LevelBreakdown.GroundLevel(levels, outcome.Configuration?.Extraction), outcome.Configuration?.Planning, NextMonday(), null, outcome.Inventory);
                DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.SetPlan(plan);
            }
            if (window == null) return;
            if (outcome?.Boq == null) window.ViewModel.Apply(outcome);
            RenderBoard();
        }

        /// <summary>
        /// Planche du tableau de bord (PNG) avec les tendances des revisions. Sans composants PDF ou
        /// en cas d'echec : tableau de bord simple de la fenetre, et la raison dans le journal.
        /// </summary>
        private void RenderBoard()
        {
            if (window == null || last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject)
            {
                window?.ViewModel.SetDashboardImage(null);
                return;
            }
            string missing = DanCI.BOQ.Export.BoqExporter.MissingPdfComponents();
            if (missing != null)
            {
                BoqLog.Warn("Planche du tableau de bord non dessinée (" + missing + ") : tableau de bord simple.");
                window.ViewModel.SetDashboardImage(null);
                return;
            }
            try
            {
                DanCI.BOQ.Export.ExportContext ctx = AnalysisService.BuildExportContext(lastDocument, last, window.ViewModel.ProjectIdentity,
                    window.ViewModel.ProjectCover, window.ViewModel.ProjectSchedule);
                ctx.RevisionHistory = RevisionHistory();
                window.ViewModel.SetDashboardImage(DanCI.BOQ.Export.Pdf.DashboardBoard.Png(ctx, ctx.RevisionHistory, 1.5)); // net sur grand ecran
            }
            catch (Exception ex)
            {
                BoqLog.Error("Planche du tableau de bord en échec : tableau de bord simple.", ex);
                window.ViewModel.SetDashboardImage(null);
            }
        }

        private List<Core.Revisions.BoqSnapshot> RevisionHistory()
        {
            if (lastDocument == null) return new List<Core.Revisions.BoqSnapshot>();
            var issues = new List<BoqIssue>();
            return Core.Revisions.RevisionStore.List(Core.Revisions.RevisionStore.FolderFor(AnalysisService.ProjectFilePath(lastDocument)), issues);
        }

        private void Analyze(UIApplication app, AnalysisScope scope, IReadOnlyList<ManualLine> manualLines)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                window?.ViewModel.Apply(new AnalysisOutcome { Failure = "Ouvrez un projet Revit (pas une famille)." });
                return;
            }

            AnalysisOutcome outcome;
            using (ProgressDialog progress = ProgressDialog.Start("DanCI — analyse du métré"))
            {
                try
                {
                    outcome = AnalysisService.Run(uidoc, scope, manualLines, progress, window?.ViewModel.PriceLibraryPath, window?.ViewModel.ProjectEstimate,
                        window?.ViewModel.ProjectExtraction);
                }
                catch (OperationCanceledException)
                {
                    BoqLog.Info("Analyse annulée par l'utilisateur.");
                    // Aucun resultat partiel : un metre interrompu n'est jamais presente comme un metre.
                    outcome = new AnalysisOutcome { Failure = "Analyse annulée : aucun résultat partiel n'est affiché." };
                }
            }

            if (outcome.Boq != null || outcome.Audit != null)
            {
                last = outcome;
                lastDocument = uidoc.Document;
            }
            window?.ViewModel.SetLevels(AnalysisService.LevelNames(uidoc.Document));
            ShowOutcome(outcome);
        }

        private void Select(UIApplication app, IReadOnlyCollection<ElementRef> refs)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || lastDocument == null || !lastDocument.IsValidObject || !uidoc.Document.Equals(lastDocument))
            {
                SetStatus("Sélection impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            Document doc = uidoc.Document;
            var references = new List<Reference>();
            var zoom = new HashSet<ElementId>();
            int missing = 0;
            foreach (ElementRef r in refs)
            {
                if (!r.IsLinked)
                {
                    Element e = doc.GetElement(new ElementId(r.ElementId));
                    if (e == null) { missing++; continue; }
                    references.Add(new Reference(e));
                    zoom.Add(e.Id);
                    continue;
                }
                // Element d'un lien : reference de lien, zoom sur l'instance du lien.
                var link = doc.GetElement(new ElementId(r.LinkInstanceId.Value)) as RevitLinkInstance;
                Element linked = link?.GetLinkDocument()?.GetElement(new ElementId(r.ElementId));
                if (linked == null) { missing++; continue; }
                references.Add(new Reference(linked).CreateLinkReference(link));
                zoom.Add(link.Id);
            }
            if (references.Count == 0)
            {
                SetStatus("Aucun des éléments n'existe encore dans le modèle (supprimés depuis l'analyse ?).");
                return;
            }
            uidoc.Selection.SetReferences(references);
            try
            {
                uidoc.ShowElements(zoom);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                BoqLog.Warn("Zoom impossible : " + ex.Message);
            }
            SetStatus(references.Count + " élément(s) sélectionné(s) dans Revit" + (missing > 0 ? " (" + missing + " introuvable(s))." : "."));
        }

        private void Export(UIApplication app)
        {
            if (last == null || lastDocument == null || !lastDocument.IsValidObject)
            {
                SetStatus("Rien à exporter : lancez d'abord une analyse.");
                return;
            }
            if (last.Boq == null)
            {
                SetStatus("Rien à exporter : le BOQ n'a pas été calculé (configuration invalide, voir l'onglet Audit).");
                return;
            }
            DanCI.BOQ.Export.ExportResult result = AnalysisService.Export(lastDocument, last, window?.ViewModel.ProjectIdentity, window?.ViewModel.ProjectCover, window?.ViewModel.ProjectSchedule);
            if (result.Files.Count == 0)
            {
                SetStatus("Export en échec : " + string.Join(" | ", result.Errors) + " (détails dans le journal).");
                return;
            }
            string folder = Path.GetDirectoryName(result.Files[0]);
            try
            {
                // Chaque export fige une revision (comparaison ulterieure A / B / C).
                SaveRevision();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                BoqLog.Warn("Révision non enregistrée : " + ex.Message);
            }
            SetStatus(result.Errors.Count == 0
                ? result.Files.Count + " fichier(s) écrit(s) (Excel, PDF, CSV) dans " + folder +
                  (result.Warnings.Count > 0 ? " — ATTENTION : " + string.Join(" | ", result.Warnings) : string.Empty)
                : "Export PARTIEL — " + string.Join(" | ", result.Errors) + " — autres fichiers écrits dans " + folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }

        private void SetStatus(string text)
        {
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Status = text;
            if (window != null)
            {
                window.ViewModel.Status = text;
            }
        }
    }
}
