using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using DanCI.BOQ.Core.Planning;
using DanCI.BOQ.UI.Mvvm;
using DanCI.BOQ.UI.ViewModels;

namespace DanCI.BOQ.UI.Studio
{
    /// <summary>
    /// Studio 4D : lecture du planning avec, dans Revit, une vue 3D dediee « DanCI — Planning 4D »
    /// ou le batiment se construit (termine = normal, en cours = couleur du lot, a venir = masque)
    /// et tourne ; dans la fenetre, le Gantt et ses liens defilent avec la date.
    /// Le curseur est celui du Gantt de la fenetre BOQ (meme date partout, panneau compris).
    /// </summary>
    public sealed class Studio4DViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly IBoqHost host;
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private double angle = 45;
        private bool rotate = true, byDay;
        private SpeedChoice speed;

        public Studio4DViewModel(BoqWindowViewModel boq, IBoqHost host)
        {
            Boq = boq;
            this.host = host;
            speed = Speeds[2];
            timer.Interval = TimeSpan.FromMilliseconds(speed.Milliseconds);
            timer.Tick += (s, e) => Tick();
            boq.PropertyChanged += OnBoqChanged;
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.PropertyChanged += OnPanelChanged;
            PlayCommand = new RelayCommand(_ => TogglePlay(), _ => Plan != null && Plan.TotalDays > 0);
            StepCommand = new RelayCommand(p => Move(p as string == "-" ? -Step : Step), _ => Plan != null);
            StartCommand = new RelayCommand(_ => { Boq.PlanCursorDay = 0; }, _ => Plan != null);
            EndCommand = new RelayCommand(_ => { Boq.PlanCursorDay = Plan.TotalDays; }, _ => Plan != null);
            TurnCommand = new RelayCommand(p => { angle += p as string == "-" ? -30 : 30; SendFrame(true); }, _ => Plan != null);
        }

        public BoqWindowViewModel Boq { get; }
        private WorkPlan Plan => Boq.WorkPlan;

        public sealed class SpeedChoice
        {
            public string Label { get; set; }
            public int Milliseconds { get; set; }
            public override string ToString() => Label;
        }

        public static readonly SpeedChoice[] Speeds =
        {
            new SpeedChoice { Label = "Lente (1 image / s)", Milliseconds = 1000 },
            new SpeedChoice { Label = "Normale (2 images / s)", Milliseconds = 500 },
            new SpeedChoice { Label = "Rapide (4 images / s)", Milliseconds = 250 },
            new SpeedChoice { Label = "Très rapide (6 images / s)", Milliseconds = 166 },
            new SpeedChoice { Label = "Maximale (au rythme de Revit)", Milliseconds = 60 },
        };

        public sealed class StepChoice
        {
            public string Label { get; set; }
            /// <summary>Jours ouvres par image ; 0 = une semaine (jours ouvres du calendrier).</summary>
            public int Days { get; set; }
            public override string ToString() => Label;
        }

        public static readonly StepChoice[] Steps =
        {
            new StepChoice { Label = "1 jour par image", Days = 1 },
            new StepChoice { Label = "2 jours par image", Days = 2 },
            new StepChoice { Label = "3 jours par image", Days = 3 },
            new StepChoice { Label = "1 semaine par image", Days = 0 },
            new StepChoice { Label = "2 semaines par image", Days = -2 },
        };

        public StepChoice[] StepChoices => Steps;
        private StepChoice stepChoice = Steps[0]; // 1 jour par image : les murs montent assise par assise

        public StepChoice StepSize
        {
            get => stepChoice;
            set { if (value != null && Set(ref stepChoice, value)) Raise(nameof(StepText)); }
        }

        /// <summary>Rotation par image (degres) : petits angles reguliers, plus fins quand la cadence est rapide.</summary>
        private double TurnPerFrame => speed.Milliseconds <= 166 ? 2.5 : speed.Milliseconds <= 250 ? 3 : speed.Milliseconds <= 500 ? 4 : 6;

        // ---- cadence : une image a la fois, la suivante quand Revit a dessine la precedente
        private bool awaitingRevit;
        private DateTime sentAt;
        private readonly System.Collections.Generic.Queue<double> frameTimes = new System.Collections.Generic.Queue<double>();
        public string CadenceText { get; private set; } = string.Empty;

        private DateTime appliedAt;

        private void OnApplied()
        {
            if (!awaitingRevit) return;
            awaitingRevit = false;
            appliedAt = DateTime.Now;
            double ms = (DateTime.Now - sentAt).TotalMilliseconds;
            frameTimes.Enqueue(ms);
            while (frameTimes.Count > 10) frameTimes.Dequeue();
            double avg = frameTimes.Average();
            double fps = 1000.0 / Math.Max(speed.Milliseconds, avg);
            CadenceText = "Cadence réelle : " + fps.ToString("0.0", Fr) + " image(s)/s · Revit dessine une image en " + avg.ToString("0", Fr) + " ms" +
                          (avg > speed.Milliseconds * 1.2 ? " — Revit impose le rythme (modèle lourd) : la lecture reste régulière, sans image sautée." : string.Empty);
            Raise(nameof(CadenceText));
        }

        public SpeedChoice[] SpeedChoices => Speeds;

        public SpeedChoice Speed
        {
            get => speed;
            set
            {
                if (value == null || !Set(ref speed, value)) return;
                timer.Interval = TimeSpan.FromMilliseconds(value.Milliseconds);
            }
        }

        private bool showGantt = true;

        /// <summary>Gantt affiche ; masque = bandeau compact, la vue 3D de Revit garde presque tout l'ecran.</summary>
        public bool ShowGantt
        {
            get => showGantt;
            set { if (Set(ref showGantt, value)) Raise(nameof(GanttToggleLabel)); }
        }

        public string GanttToggleLabel => showGantt ? "▾ Masquer le Gantt" : "▴ Afficher le Gantt";
        public ICommand ToggleGanttCommand => toggleGantt ?? (toggleGantt = new RelayCommand(_ => ShowGantt = !ShowGantt));
        private ICommand toggleGantt;

        /// <summary>Le batiment tourne pendant la lecture (camera en orbite).</summary>
        public bool Rotate { get => rotate; set => Set(ref rotate, value); }

        /// <summary>Pas : un jour ouvre (vrai) ou une semaine (faux) — conserve pour compatibilite.</summary>
        public bool ByDay { get => byDay; set { if (Set(ref byDay, value)) StepSize = value ? Steps[0] : Steps[3]; } }
        public int Step => stepChoice.Days > 0 ? stepChoice.Days : (Plan?.WorkingDaysPerWeek ?? 6) * (stepChoice.Days == 0 ? 1 : -stepChoice.Days);
        public string StepText => stepChoice.Label;

        public ICommand PlayCommand { get; }
        public ICommand StepCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand EndCommand { get; }
        public ICommand TurnCommand { get; }

        public bool IsPlaying => timer.IsEnabled;
        public string PlayLabel => timer.IsEnabled ? "⏸  Pause" : "▶  Lecture";

        public int Day
        {
            get => Boq.PlanCursorDay;
            set => Boq.PlanCursorDay = value;
        }

        public int MaxDay => Plan?.TotalDays ?? 0;

        // ---- indicateurs
        public string DateText { get; private set; } = string.Empty;
        public string DayText { get; private set; } = string.Empty;
        public string DoneText { get; private set; } = "0";
        public string RunningText { get; private set; } = "0";
        public string PercentText { get; private set; } = "0 %";
        public double PercentWidth { get; private set; }
        public string CurrentTasks { get; private set; } = string.Empty;
        public string Status { get; private set; } = "▶ Lecture : Revit ouvre la vue 3D « DanCI — Planning 4D » et le bâtiment se construit sous vos yeux.";

        /// <summary>Ce que Revit a fait de la derniere image (ou pourquoi il l'a refusee).</summary>
        public string RevitStatus { get; private set; } = "En attente de Revit…";

        private void OnPanelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DanCI.BOQ.UI.Chart.ViewChartViewModel.Status)) return;
            RevitStatus = DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Status;
            Raise(nameof(RevitStatus));
        }

        /// <summary>Premiere ligne en cours (defilement vertical du Gantt).</summary>
        public int FocusRow { get; private set; } = -1;

        public void Open()
        {
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Suspend3D = true;
            Refresh();
            SendFrame(true);
        }

        public void Close()
        {
            timer.Stop();
            Boq.PropertyChanged -= OnBoqChanged;
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.PropertyChanged -= OnPanelChanged;
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Suspend3D = false;
            host?.RequestEndSimulation4D();
        }

        private void TogglePlay()
        {
            if (timer.IsEnabled) timer.Stop();
            else
            {
                if (Boq.PlanCursorDay >= MaxDay) Boq.PlanCursorDay = 0;
                timer.Start();
            }
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayLabel));
        }

        private void Tick()
        {
            if (Plan == null || Boq.PlanCursorDay >= MaxDay)
            {
                if (timer.IsEnabled) TogglePlay();
                Status = "Fin du chantier : bâtiment terminé" + (Plan != null ? " le " + Plan.FinishDate.ToString("dddd dd MMMM yyyy", Fr) : string.Empty) + ".";
                Raise(nameof(Status));
                return;
            }
            // Cadence harmonieuse : on n'avance que lorsque Revit a dessine l'image precedente
            // (au plus 3 s d'attente) : pas d'image sautee, des pas toujours egaux.
            if (awaitingRevit && (DateTime.Now - sentAt).TotalMilliseconds < 1500) return;
            // Laisser a Revit un temps libre entre deux images (boutons, souris, curseur restent reactifs).
            double avg = frameTimes.Count > 0 ? frameTimes.Average() : 0;
            if ((DateTime.Now - appliedAt).TotalMilliseconds < Math.Max(40, avg * 0.35)) return;
            if (rotate) angle += TurnPerFrame;
            Move(Step);
        }

        private void Move(int delta) => Boq.PlanCursorDay = Math.Max(0, Math.Min(MaxDay, Boq.PlanCursorDay + delta));

        private void OnBoqChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BoqWindowViewModel.PlanCursorDay) || e.PropertyName == nameof(BoqWindowViewModel.CursorLeft))
            {
                Refresh();
                SendFrame(false);
            }
            else if (e.PropertyName == nameof(BoqWindowViewModel.WorkPlan))
            {
                Refresh();
            }
        }

        private string lastSignature;

        /// <summary>Envoie a Revit l'etat de la date (coalesce cote Revit : seule la derniere image compte).</summary>
        private void SendFrame(bool force)
        {
            if (host == null || Plan == null) return;
            int day = Boq.PlanCursorDay;
            var built = new HashSet<long>();
            var running = new Dictionary<long, string>();
            var all = new HashSet<long>();
            var tasks = new List<Sim4DTask>();
            foreach (PlanTask t in Plan.Tasks)
            {
                var st = WorkPlan.StateAt(t, day);
                if (st == TaskState.InProgress)
                {
                    tasks.Add(new Sim4DTask
                    {
                        Color = t.Color,
                        Progress = WorkPlan.ProgressAt(t, day),
                        Elements = t.Elements.Where(x => !x.IsLinked).Select(x => x.ElementId).Distinct().ToList()
                    });
                }
                foreach (var r in t.Elements.Where(x => !x.IsLinked))
                {
                    all.Add(r.ElementId);
                    if (st == TaskState.Done) built.Add(r.ElementId);
                    else if (st == TaskState.InProgress && !running.ContainsKey(r.ElementId)) running[r.ElementId] = t.Color;
                }
            }
            built.ExceptWith(running.Keys);
            string signature = built.Count + "/" + running.Count + "/" + day + "/" + angle;
            if (!force && signature == lastSignature) return;
            lastSignature = signature;
            awaitingRevit = true;
            sentAt = DateTime.Now;
            host.RequestSimulation4D(new Sim4DFrame
            {
                Built = built.ToList(),
                Running = running,
                All = all.ToList(),
                AngleDeg = (rotate && timer.IsEnabled) || force ? angle : (double?)null, // a l'arret, l'utilisateur garde SON point de vue
                Label = DateText,
                Applied = OnApplied,
                Tasks = tasks
            });
        }

        private void Refresh()
        {
            if (Plan == null)
            {
                DateText = "Lancez d'abord l'analyse : le planning se calcule à partir du BOQ.";
                foreach (string n in new[] { nameof(DateText), nameof(MaxDay) }) Raise(n);
                return;
            }
            int day = Boq.PlanCursorDay;
            var states = Plan.Tasks.Select(t => (T: t, S: WorkPlan.StateAt(t, day))).ToList();
            var running = states.Where(x => x.S == TaskState.InProgress).Select(x => x.T).ToList();
            decimal spent = Plan.Tasks.Sum(t => t.Amount * (decimal)WorkPlan.ProgressAt(t, day));
            double pct = Plan.TotalAmount > 0 ? (double)(spent / Plan.TotalAmount) : day / (double)Math.Max(1, Plan.TotalDays);
            DateText = Boq.CursorDate.ToString("dddd dd MMMM yyyy", Fr).ToUpper(Fr);
            DayText = "Jour " + day + " / " + Plan.TotalDays + "   ·   réception le " + Plan.FinishDate.ToString("dd/MM/yyyy", Fr);
            DoneText = states.Count(x => x.S == TaskState.Done) + " / " + Plan.Tasks.Count;
            RunningText = running.Count.ToString(Fr);
            PercentText = (pct * 100).ToString("0", Fr) + " %";
            PercentWidth = Math.Max(0, Math.Min(1, pct)) * 220;
            CurrentTasks = running.Count == 0
                ? (day >= Plan.TotalDays ? "✓ Chantier terminé." : day == 0 ? "Démarrage du chantier." : "Aucune tâche en cours à cette date.")
                : "EN COURS : " + string.Join("   ·   ", running.Take(6).Select(t => "Lot " + t.Lot + (t.Level != null ? " " + t.Level : string.Empty) + " — " + t.LotDesignation +
                                                                                    " (" + (WorkPlan.ProgressAt(t, day) * 100).ToString("0", Fr) + " %)")) +
                  (running.Count > 6 ? "   … +" + (running.Count - 6) : string.Empty);
            FocusRow = -1;
            for (int i = 0; i < Boq.GanttRows.Count; i++)
            {
                if (Boq.GanttRows[i].IsRunning) { FocusRow = i; break; }
            }
            foreach (string n in new[] { nameof(DateText), nameof(DayText), nameof(DoneText), nameof(RunningText), nameof(PercentText), nameof(PercentWidth),
                                         nameof(CurrentTasks), nameof(FocusRow), nameof(Day), nameof(MaxDay) })
            {
                Raise(n);
            }
        }
    }
}
