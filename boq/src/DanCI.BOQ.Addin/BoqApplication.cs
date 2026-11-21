using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using DanCI.BOQ.Addin.Resources;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.RevitAdapter;

namespace DanCI.BOQ.Addin
{
    /// <summary>
    /// Point d'entree : ajoute le panneau "Métré & BOQ" a l'onglet "DanCI".
    ///
    /// L'onglet DanCI est PARTAGE avec les autres extensions DanCI (Structures metalliques...).
    /// Regles pour ne rien casser :
    ///  - l'onglet n'est cree que s'il n'existe pas ; s'il existe deja, on s'y ajoute ;
    ///  - on ne touche a aucun panneau qui n'est pas le notre ;
    ///  - un echec de creation du ruban est journalise et n'empeche pas Revit de demarrer.
    /// </summary>
    public sealed class BoqApplication : IExternalApplication
    {
        public const string TabName = "DanCI";
        public const string PanelName = "Métré & BOQ";

        public Result OnStartup(UIControlledApplication application)
        {
            HostInfo host = null;
            try
            {
                host = RevitHostReader.Read(application.ControlledApplication);
            }
            catch (Exception ex)
            {
                BoqLog.Error("Lecture de la version de Revit impossible.", ex);
            }

            BoqLog.Info("==== Démarrage de " + ProductInfo.Name + " ====");
            foreach (string line in RuntimeReport.Build(host))
            {
                BoqLog.Info(line);
            }
            BoqLog.Info("Assembly          : " + Assembly.GetExecutingAssembly().Location);

            string mismatch = RuntimeReport.MismatchWarning(host);
            if (mismatch != null)
            {
                BoqLog.Warn(mismatch);
            }

            try
            {
                CreateRibbon(application);
                BoqLog.Info("Panneau '" + PanelName + "' ajouté à l'onglet '" + TabName + "'.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Création du ruban impossible.", ex);
                TaskDialog.Show(ProductInfo.Name,
                    "Le panneau « " + PanelName + " » n'a pas pu être créé :" + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Détails dans le journal : " + BoqLog.CurrentFile);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            BoqLog.Info("Arrêt de " + ProductInfo.Name + ".");
            return Result.Succeeded;
        }

        private static void CreateRibbon(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // L'onglet existe deja : cree par une autre extension DanCI. On s'y ajoute.
                BoqLog.Info("Onglet '" + TabName + "' déjà présent : ajout du panneau BOQ à l'onglet existant.");
            }

            RibbonPanel panel = application.GetRibbonPanels(TabName)
                .FirstOrDefault(p => p.Name == PanelName)
                ?? application.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var aboutData = new PushButtonData(
                "DanCI_BOQ_About",
                "À propos",
                assemblyPath,
                typeof(Commands.AboutCommand).FullName)
            {
                ToolTip = "Version de l'extension, version de Revit et runtime .NET chargé.",
                LongDescription =
                    "Affiche la version de DanCI — Métré & BOQ, l'année Revit pour laquelle elle a " +
                    "été compilée, la version et le build de Revit en cours, et le runtime .NET " +
                    "réellement chargé. À consulter en premier si l'extension se comporte mal " +
                    "après une mise à jour de Revit.",
                AvailabilityClassName = typeof(Commands.AlwaysAvailable).FullName
            };

            var boqData = new PushButtonData(
                "DanCI_BOQ_Window",
                "Métré" + Environment.NewLine + "& BOQ",
                assemblyPath,
                typeof(Commands.ShowBoqCommand).FullName)
            {
                ToolTip = "Analyse le modèle, audite, et affiche le BOQ : lots, articles, détail du métré, statut de chaque ligne.",
                LongDescription =
                    "Fenêtre non modale. Choisissez la portée (modèle entier, vue active, sélection, niveaux), " +
                    "lancez l'analyse (annulable), puis vérifiez : l'onglet Audit liste ce qui fausse le métré, " +
                    "le double-clic sur une ligne sélectionne et zoome les éléments dans Revit. Lecture seule.",
                AvailabilityClassName = typeof(Commands.ProjectAvailability).FullName
            };

            var diagnosticData = new PushButtonData(
                "DanCI_BOQ_Diagnostic",
                "Diagnostic" + Environment.NewLine + "métré",
                assemblyPath,
                typeof(Commands.DiagnosticCommand).FullName)
            {
                ToolTip = "Extrait béton, coffrage, armatures, charpente, maçonnerie et menuiseries, et écrit le métré brut en CSV.",
                LongDescription =
                    "Lecture seule. Parcourt le modèle hôte entier (toutes phases) et écrit à côté du " +
                    "projet : le métré brut (une ligne par quantité d'élément, avec ElementId, méthode et " +
                    "fiabilité), les lignes du BOQ calculées avec la configuration installée, et les " +
                    "anomalies. À comparer à un métré manuel avant tout usage.",
                AvailabilityClassName = typeof(Commands.ProjectAvailability).FullName
            };

            var autotestData = new PushButtonData(
                "DanCI_BOQ_Autotest",
                "Autotest",
                assemblyPath,
                typeof(Commands.AutotestCommand).FullName)
            {
                ToolTip = "Vérifie l'installation en un clic et produit le fichier à envoyer au support.",
                LongDescription =
                    "Contrôle Revit et le runtime .NET, l'onglet DanCI, la configuration, le moteur, l'Excel, le PDF, les CSV et " +
                    "les dépendances chargées, puis fait l'inventaire du projet ouvert (lecture seule). Résultat : un fichier " +
                    "DanCI-BOQ-support_….zip dans Documents\\DanCI\\BOQ, à envoyer tel quel.",
                AvailabilityClassName = typeof(Commands.AlwaysAvailable).FullName
            };

            var existing = panel.GetItems().OfType<PushButton>().Select(b => b.Name).ToList();
            if (!existing.Contains(boqData.Name) && panel.AddItem(boqData) is PushButton boq)
            {
                boq.LargeImage = IconFactory.CreateBoqIcon(32);
                boq.Image = IconFactory.CreateBoqIcon(16);
            }
            if (!existing.Contains(diagnosticData.Name) && panel.AddItem(diagnosticData) is PushButton diagnostic)
            {
                diagnostic.LargeImage = IconFactory.CreateDiagnosticIcon(32);
                diagnostic.Image = IconFactory.CreateDiagnosticIcon(16);
            }
            if (!existing.Contains(autotestData.Name) && panel.AddItem(autotestData) is PushButton autotest)
            {
                autotest.LargeImage = IconFactory.CreateDiagnosticIcon(32);
                autotest.Image = IconFactory.CreateDiagnosticIcon(16);
            }
            if (!existing.Contains(aboutData.Name) && panel.AddItem(aboutData) is PushButton about)
            {
                about.LargeImage = IconFactory.CreateInfoIcon(32);
                about.Image = IconFactory.CreateInfoIcon(16);
            }
        }
    }
}
