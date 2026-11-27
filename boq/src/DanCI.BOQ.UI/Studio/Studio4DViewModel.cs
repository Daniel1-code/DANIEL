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
            EndCommand = new RelayCommand(_ => { Boq.PlanCursorDay = Plan.TotalDays; StartLanding(); }, _ => Plan != null);
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
            new SpeedChoice { Label = "Ultra rapide (8 images / s)", Milliseconds = 125 },
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
        private StepChoice stepChoice = Steps[1]; // 2 jours par image : rapide, la montee reste progressive (reglage « Montee »)

        public StepChoice StepSize
        {
            get => stepChoice;
            set { if (value != null && Set(ref stepChoice, value)) Raise(nameof(StepText)); }
        }

        /// <summary>Rotation par image (degres) : petits angles reguliers, plus fins quand la cadence est rapide.</summary>
        private double TurnPerFrame => (speed.Milliseconds <= 125 ? 4 : speed.Milliseconds <= 166 ? 5 : speed.Milliseconds <= 250 ? 6 : speed.Milliseconds <= 500 ? 8 : 12) * turnFactor;

        // ---- vitesse de rotation reglable, independante de la vitesse de construction
        public sealed class TurnChoice
        {
            public string Label { get; set; }
            public double Factor { get; set; }
            public override string ToString() => Label;
        }

        public static readonly TurnChoice[] Turns =
        {
            new TurnChoice { Label = "Rotation lente (10°/s)", Factor = 0.5 },
            new TurnChoice { Label = "Rotation normale (20°/s)", Factor = 1 },
            new TurnChoice { Label = "Rotation rapide (40°/s)", Factor = 2 },
            new TurnChoice { Label = "Rotation très rapide (60°/s)", Factor = 3 },
        };

        public TurnChoice[] TurnChoices => Turns;

        // ---- montee : images minimales par tache (fine = un rang de 20 cm par image)
        public sealed class RiseChoice
        {
            public string Label { get; set; }
            public int MinFrames { get; set; }
            /// <summary>Rangs de 20 cm poses par image et par element.</summary>
            public int Courses { get; set; } = 1;
            public override string ToString() => Label;
        }

        public static readonly RiseChoice[] Rises =
        {
            new RiseChoice { Label = "Montée fine : 20 cm / image", MinFrames = 24, Courses = 1 },
            new RiseChoice { Label = "Montée normale : 40 cm / image", MinFrames = 14, Courses = 2 },
            new RiseChoice { Label = "Montée rapide : 60 cm / image", MinFrames = 8, Courses = 3 },
        };

        public RiseChoice[] RiseChoices => Rises;
        private RiseChoice rise = Rises[1];

        public RiseChoice Rise
        {
            get => rise;
            set { if (value != null) Set(ref rise, value); }
        }

        // Rotation en DEGRES PAR SECONDE : elle garde sa vitesse meme si Revit dessine lentement.
        private DateTime lastTurn = DateTime.MinValue;

        private void Turn()
        {
            DateTime now = DateTime.Now;
            double dt = lastTurn == DateTime.MinValue ? speed.Milliseconds / 1000.0 : (now - lastTurn).TotalSeconds;
            lastTurn = now;
            angle += 20.0 * turnFactor * Math.Max(0.02, Math.Min(2.0, dt));
        }
        private TurnChoice turn = Turns[2];
        private double turnFactor = 2;

        public TurnChoice TurnSpeed
        {
            get => turn;
            set { if (value != null && Set(ref turn, value)) turnFactor = value.Factor; }
        }

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
            runFrames++;
            runMsSum += ms;
            frameTimes.Enqueue(ms);
            while (frameTimes.Count > 10) frameTimes.Dequeue();
            double avg = frameTimes.Average();
            double fps = 1000.0 / Math.Max(speed.Milliseconds, avg);
            CadenceText = "Cadence réelle : " + fps.ToString("0.0", Fr) + " image(s)/s · Revit dessine une image en " + avg.ToString("0", Fr) + " ms" +
                          " · fenêtre Studio " + studioMs.ToString("0", Fr) + " ms" +
                          " · montée " + (AdaptiveCourses * 20) + " cm / image" +
                          (AdaptiveCourses > rise.Courses ? " (adaptée au modèle lourd)" : string.Empty) +
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

        // ---- enregistrement video (AVI) de la simulation, date incrustee
        private string recordPath;
        private ICommand recordCommand;
        public bool IsRecording => recordPath != null;
        public string RecordLabel => recordPath == null ? "⏺ Enregistrer la vidéo" : "⏹ Arrêter l'enregistrement";

        public ICommand RecordCommand => recordCommand ?? (recordCommand = new RelayCommand(_ =>
        {
            if (recordPath != null)
            {
                StopRecording();
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Enregistrer la vidéo de la simulation 4D",
                Filter = "Vidéo AVI (*.avi)|*.avi",
                FileName = "Simulation 4D " + DateTime.Now.ToString("yyyy-MM-dd HH-mm", Fr) + ".avi",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            if (dlg.ShowDialog() != true) return;
            recordPath = dlg.FileName;
            Raise(nameof(IsRecording));
            Raise(nameof(RecordLabel));
            // La video commence au demarrage du chantier et s'arrete d'elle-meme sur la vue de face.
            Boq.PlanCursorDay = 0;
            if (!timer.IsEnabled) TogglePlay();
            Status = "⏺ Enregistrement : la simulation repart du début ; la vidéo s'arrête à la fin, sur la vue de face.";
            Raise(nameof(Status));
        }, _ => Plan != null));

        private void StopRecording()
        {
            if (recordPath == null) return;
            recordPath = null;
            Raise(nameof(IsRecording));
            Raise(nameof(RecordLabel));
            SendFrame(true); // Revit ferme la video
        }

        private bool realShapes, perspective; // dessin superpose par defaut : bien plus rapide sur un gros modele

        /// <summary>Rangs de 20 cm en volumes reels temporaires (s'affichent a coup sur, rotation comprise).</summary>
        public bool RealShapes
        {
            get => realShapes;
            set { if (Set(ref realShapes, value)) SendFrame(true); }
        }

        /// <summary>Couleurs comme SYNCHRO : en cours = jaune, termine = couleur reelle (sinon : couleur du lot).</summary>
        public const string ActiveColor = "#FFC21A";

        /// <summary>Couleurs de la simulation : realiste (materiaux, defaut), par etat (SYNCHRO, en cours = jaune), par lot.</summary>
        public sealed class ColorChoice
        {
            public string Label { get; set; }
            public bool Realistic { get; set; }
            public bool State { get; set; }
            public override string ToString() => Label;
        }

        public static readonly ColorChoice[] ColorModes =
        {
            new ColorChoice { Label = "Réaliste (vraies formes, couleurs des matériaux)", Realistic = true },
            new ColorChoice { Label = "Par état (en cours en jaune)", State = true },
            new ColorChoice { Label = "Par lot (couleur du lot)" },
        };

        public ColorChoice[] ColorChoices => ColorModes;
        private ColorChoice colorMode = ColorModes[0];
        private bool stateColors => colorMode.State;

        public ColorChoice ColorMode
        {
            get => colorMode;
            set { if (value != null && Set(ref colorMode, value)) SendFrame(true); }
        }

        /// <summary>Compatibilite : coche « couleurs d'etat ».</summary>
        public bool StateColors
        {
            get => colorMode.State;
            set => ColorMode = value ? ColorModes[1] : ColorModes[0];
        }

        // ---- ENVIRONNEMENT DE CHANTIER (dessin seulement) : ciel, terrain, palissade, grue qui pivote, echafaudages
        private bool environment = true;
        private double slew = 30;

        public bool SiteEnvironment
        {
            get => environment;
            set { if (Set(ref environment, value)) SendFrame(true); }
        }

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

        // ---- graphiques facon SYNCHRO : courbe en S (montant cumule) et histogramme hebdomadaire
        public const double ChartW = 300, ChartH = 56;

        public sealed class WeekBar
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string Fill { get; set; }
            public string Tooltip { get; set; }
        }

        public System.Collections.ObjectModel.ObservableCollection<WeekBar> WeekBars { get; } = new System.Collections.ObjectModel.ObservableCollection<WeekBar>();
        public string SCurvePoints { get; private set; } = string.Empty;
        public string SCurveDonePoints { get; private set; } = string.Empty;
        public double ChartCursorX { get; private set; }
        public double ChartCursorY { get; private set; } = ChartH;
        public string CumulText { get; private set; } = string.Empty;
        private List<(int FirstDay, decimal Amount, decimal CumulativeShare)> weeks;
        private int lastWeekIndex = -1;
        private WorkPlan lastWeeksPlan;
        private WorkPlan weeksPlan;

        private void RefreshCharts(int day)
        {
            if (Plan == null) return;
            if (weeks == null || !ReferenceEquals(weeksPlan, Plan))
            {
                weeks = Plan.WeeklyAmounts();
                weeksPlan = Plan;
            }
            double total = Math.Max(1, Plan.TotalDays);
            int per = Plan.WorkingDaysPerWeek == 5 ? 5 : 6;
            double X(double d) => Math.Max(0, Math.Min(ChartW, d / total * ChartW));
            var inv = CultureInfo.InvariantCulture;
            // Courbe en S : cumul prevu en fin de chaque semaine.
            var pts = new List<(double x, double y)> { (0, ChartH) };
            foreach (var w in weeks) pts.Add((X(w.FirstDay + per), ChartH - (double)w.CumulativeShare * ChartH));
            SCurvePoints = string.Join(" ", pts.Select(p => p.x.ToString("0.#", inv) + "," + p.y.ToString("0.#", inv)));
            double cx = X(day);
            var done = pts.Where(p => p.x <= cx).ToList();
            var after = pts.FirstOrDefault(p => p.x > cx);
            double cy = ChartH;
            if (done.Count > 0)
            {
                var last = done[done.Count - 1];
                cy = after.x > last.x ? last.y + (after.y - last.y) * (cx - last.x) / (after.x - last.x) : last.y;
                done.Add((cx, cy));
            }
            SCurveDonePoints = string.Join(" ", done.Select(p => p.x.ToString("0.#", inv) + "," + p.y.ToString("0.#", inv)));
            ChartCursorX = cx;
            ChartCursorY = cy;
            double share = (ChartH - cy) / ChartH;
            CumulText = "Cumul réalisé : " + ((decimal)share * Plan.TotalAmount).ToString("N0", Fr) + "  (" + (share * 100).ToString("0", Fr) + " %)";
            foreach (string n in new[] { nameof(SCurvePoints), nameof(SCurveDonePoints), nameof(ChartCursorX), nameof(ChartCursorY), nameof(CumulText) }) Raise(n);
            // Histogramme : reconstruit seulement quand la semaine courante change (pas a chaque image).
            int weekIndex = day / per;
            if (weekIndex == lastWeekIndex && ReferenceEquals(weeksPlan, lastWeeksPlan) && WeekBars.Count > 0) return;
            lastWeekIndex = weekIndex;
            lastWeeksPlan = weeksPlan;
            decimal max = weeks.Count == 0 ? 0 : weeks.Max(w => w.Amount);
            WeekBars.Clear();
            double bw = ChartW / Math.Max(1, weeks.Count);
            foreach (var w in weeks)
            {
                double h = max <= 0 ? 0 : (double)(w.Amount / max) * (ChartH - 4);
                WeekBars.Add(new WeekBar
                {
                    Left = w.FirstDay / total * ChartW,
                    Width = Math.Max(1, bw - 1),
                    Height = Math.Max(0.5, h),
                    Top = ChartH - Math.Max(0.5, h),
                    Fill = w.FirstDay + per <= day ? "#FF00E5FF" : w.FirstDay <= day ? "#FFFFC21A" : "#FF2E3F63",
                    Tooltip = "Semaine du jour " + w.FirstDay + " : " + w.Amount.ToString("N0", Fr)
                });
            }
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
            recordPath = null;
            Boq.PropertyChanged -= OnBoqChanged;
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.PropertyChanged -= OnPanelChanged;
            DanCI.BOQ.UI.Chart.ViewChartViewModel.Suspend3D = false;
            host?.RequestEndSimulation4D();
        }

        private void TogglePlay()
        {
            if (timer.IsEnabled)
            {
                timer.Stop();
                if (playStarted.HasValue) playTime += DateTime.Now - playStarted.Value;
                playStarted = null;
            }
            else
            {
                if (Boq.PlanCursorDay >= MaxDay) { Boq.PlanCursorDay = 0; hostLagging = 0; } // relecture du debut
                if (Boq.PlanCursorDay == 0) NewRun();
                playStarted = DateTime.Now;
                landingFrame = -1;
                finalPerspective = false;
                elevation = Sim4DCamera.ElevationDeg; // la lecture repart en vue plongeante, isometrique
                lastTurn = DateTime.MinValue;
                holdFrames = 0; // apres une pause, la 3D finit d'abord de rattraper la date
                timer.Start();
            }
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayLabel));
        }

        // ---- atterrissage final : la camera se pose sur la face « Avant » du ViewCube, en douceur
        private int landingFrame = -1;
        private bool finalPerspective;
        private double landingStart;
        private double elevation = Sim4DCamera.ElevationDeg;

        private void StartLanding()
        {
            landingStart = angle;
            landingFrame = 0;
            if (!timer.IsEnabled)
            {
                timer.Start();
                Raise(nameof(IsPlaying));
                Raise(nameof(PlayLabel));
            }
        }

        /// <summary>Temps moyen passe par la fenetre du Studio a chaque pas (Gantt, indicateurs) : meme fil que Revit.</summary>
        private double studioMs;

        private void Tick()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { TickCore(); }
            finally
            {
                double ms = sw.Elapsed.TotalMilliseconds;
                if (ms > 0.5) studioMs = studioMs <= 0 ? ms : studioMs * 0.8 + ms * 0.2;
            }
        }

        private void TickCore()
        {
            if (awaitingRevit && (DateTime.Now - sentAt).TotalMilliseconds < 10000) return;
            if (landingFrame >= 0)
            {
                landingFrame++;
                var pose = Sim4DCamera.Landing(landingStart, landingFrame / (double)Sim4DCamera.LandingFrames);
                angle = pose.AngleDeg;
                elevation = pose.ElevationDeg;
                SendFrame(true);
                if (landingFrame >= Sim4DCamera.LandingFrames)
                {
                    landingFrame = -1;
                    perspective = true;
                    finalShot = "Fin du chantier — vue de face (réception le " + (Plan != null ? Plan.FinishDate.ToString("dd/MM/yyyy", Fr) : "—") + ")";
                    reportAfterFinal = true;
                    SendFrame(true); // vue de face en perspective
                    perspective = false;
                    finalPerspective = true;
                    if (timer.IsEnabled) TogglePlay();
                    StopRecording();
                    Status = "Fin du chantier : bâtiment terminé" + (Plan != null ? " le " + Plan.FinishDate.ToString("dddd dd MMMM yyyy", Fr) : string.Empty) +
                             " — vue de face (ViewCube : Avant).";
                    Raise(nameof(Status));
                }
                return;
            }
            if (Plan == null || (Boq.PlanCursorDay >= MaxDay && !Catching && !HostCatching))
            {
                // Tout est construit : la camera finit toujours sur la vue de face.
                if (Plan != null) { StartLanding(); return; }
                if (timer.IsEnabled) TogglePlay();
                return;
            }
            // Cadence harmonieuse : on n'avance que lorsque Revit a dessine l'image precedente (attente
            // verifiee en tete de Tick, jusqu'a 10 s sur un modele lourd) : pas d'image sautee.
            // Laisser a Revit un temps libre entre deux images (boutons, souris, curseur restent reactifs).
            double avg = frameTimes.Count > 0 ? frameTimes.Average() : 0;
            if ((DateTime.Now - appliedAt).TotalMilliseconds < Math.Max(30, avg * 0.25)) return;
            if (rotate) Turn();
            slew = (slew + 4) % 360; // la fleche de la grue pivote doucement pendant la lecture
            advance = true; // UNE seule montee par pas, quel que soit le nombre d'evenements recus
            if (Boq.PlanCursorDay >= MaxDay)
            {
                // Date au bout : la 3D finit de monter ce qui est en retard, sans rien faire surgir.
                holdFrames++;
                SendFrame(false);
                advance = false;
                return;
            }
            // CONTINUITE : la date ne s'arrete jamais en cours de chantier.
            // (Avant la 0.53.0, la date attendait ici que la 3D rattrape : la simulation « faisait des pauses ».
            // Desormais la date avance toujours ; un element en retard accelere, sans jamais surgir.)
            // ACCELERATION : periode sans rien a construire (attente, taches sans element) : saut direct
            // a la prochaine tache.
            int today = Boq.PlanCursorDay;
            // Terrassement compris (ses fouilles se creusent a l'ecran meme sans element Revit).
            bool Visible(PlanTask t) => t.Elements.Count > 0 || Builds(t);
            bool idle = !Plan.Tasks.Any(t => Visible(t) && WorkPlan.ProgressAt(t, today + Step) > 0 && WorkPlan.ProgressAt(t, today) < 1);
            int next = idle ? Plan.Tasks.Where(t => Visible(t) && t.StartDay > today).Select(t => t.StartDay).DefaultIfEmpty(MaxDay).Min() : today;
            holdFrames = 0;
            Move(idle && next > today + Step ? next - today : Step);
            // Captures pour le rapport : a chaque dizieme du chantier (prise des que la 3D a rattrape la date).
            if (MaxDay > 0 && Boq.PlanCursorDay / (double)MaxDay >= nextShot - 1e-9)
            {
                shotPending = true;
                nextShot = Math.Floor(Boq.PlanCursorDay / (double)MaxDay * 10 + 1e-9) / 10 + 0.1;
            }
            if (advance) SendFrame(false); // date inchangee (pas tronque) : on monte quand meme
            advance = false;
        }

        // ---- montee douce : avancement AFFICHE par tache (jamais plus de 1/12 par image en lecture)
        private readonly Dictionary<string, double> shown = new Dictionary<string, double>(StringComparer.Ordinal);
        private bool advance;
        private int lastDay = -1;

        /// <summary>Vrai tant que la 3D n'a pas rattrape la date (taches encore en train de monter).</summary>
        private bool Catching => Plan != null && Plan.Tasks.Any(t => Builds(t) && (shown.TryGetValue(t.Id, out double v) ? v : 0) < WorkPlan.ProgressAt(t, Boq.PlanCursorDay) - 1e-9);

        private HashSet<string> builders;
        private WorkPlan buildersPlan;

        /// <summary>
        /// Vrai si la tache CONSTRUIT au moins un element (premiere tache qui le contient). Les autres
        /// (etudes, terrassement sans element, finitions qui ne font que colorer) suivent directement
        /// la date et ne la retiennent jamais.
        /// </summary>
        private bool Builds(PlanTask t)
        {
            if (builders == null || !ReferenceEquals(buildersPlan, Plan))
            {
                builders = new HashSet<string>(OwnerTasks().Values, StringComparer.Ordinal);
                buildersPlan = Plan;
            }
            return builders.Contains(t.Id) || t.Lot == "01"; // terrassement : les fouilles se creusent en douceur
        }

        private void Move(int delta) => Boq.PlanCursorDay = Math.Max(0, Math.Min(MaxDay, Boq.PlanCursorDay + delta));

        private void OnBoqChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BoqWindowViewModel.PlanCursorDay))
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
            // En lecture (vers l'avant) l'affichage monte en douceur ; en recul ou a la main, il se recale.
            Build4D.Follow mode = advance && day >= lastDay ? Build4D.Follow.Advance
                                : day == lastDay ? Build4D.Follow.Hold
                                : Build4D.Follow.Snap;
            advance = false;
            lastDay = day;
            var progress = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (PlanTask t in Plan.Tasks)
            {
                double now = Build4D.Next(shown.TryGetValue(t.Id, out double v) ? v : 0, WorkPlan.ProgressAt(t, day), Builds(t) ? mode : Build4D.Follow.Snap, AdaptiveMinFrames);
                shown[t.Id] = now;
                progress[t.Id] = now;
            }
            // Un element n'est CONSTRUIT qu'une fois (par sa premiere tache) ; les suivantes le colorent seulement.
            Build4DFrame state = Build4DState.Compute(Plan.Tasks, progress, OwnerTasks());
            var built = state.Built;
            var running = stateColors ? state.Running.ToDictionary(kv => kv.Key, kv => ActiveColor) : state.Running;
            var all = state.All;
            var tasks = state.Building.Select(b => new Sim4DTask { Color = stateColors ? ActiveColor : b.Task.Color, Progress = b.Progress, Elements = b.Elements }).ToList();
            var links = Plan.Tasks.SelectMany(t => t.Elements).Where(r => r.IsLinked).Select(r => r.LinkInstanceId.Value).Distinct().ToList();
            string signature = (perspective || finalPerspective ? "P" : "I") + (realShapes ? "R" : "D") + (recordPath ?? "-") + Plan.Tasks.Where(t => t.Lot == "01").Select(t => progress.TryGetValue(t.Id, out double ep0) ? ep0 : 0).DefaultIfEmpty(0).Max().ToString("0.###", CultureInfo.InvariantCulture) + (colorMode.Realistic ? "V" : stateColors ? "E" : "L") + (environment ? "S" + slew : "-") + built.Count + "/" + running.Count + "/" + day + "/" + angle + "/" + tasks.Sum(x => x.Progress).ToString("0.####", CultureInfo.InvariantCulture);
            if (mode == Build4D.Follow.Advance && HostCatching) signature += "#" + (++catchSeq); // la 3D rattrape : image suivante
            if (!force && signature == lastSignature) return;
            lastSignature = signature;
            awaitingRevit = true;
            sentAt = DateTime.Now;
            string shot = finalShot;
            if (shotPending) shotWait++;
            if (shot == null && shotPending && ((!HostCatching && !Catching) || shotWait > 4))
            {
                shotWait = 0;
                shot = Boq.CursorDate.ToString("dddd dd MMMM yyyy", Fr) + " — jour " + day + " / " + Plan.TotalDays;
                shotPending = false;
            }
            finalShot = null;
            bool makeReport = reportAfterFinal;
            reportAfterFinal = false;
            var frame = new Sim4DFrame
            {
                Snapshot = shot,
                ClearSnapshots = clearShots,
                Built = built.ToList(),
                Running = running,
                All = all.ToList(),
                AngleDeg = (rotate && timer.IsEnabled) || force ? angle : (double?)null, // a l'arret, l'utilisateur garde SON point de vue
                ElevationDeg = elevation,
                Label = DateText,
                Applied = null,
                Follow = mode,
                Realistic = colorMode.Realistic,
                Fast = timer.IsEnabled && !finalPerspective && !perspective,
                Environment = environment,
                Site = environment ? SiteState.At(Plan.Tasks, progress, slew) : null,
                CoursesPerFrame = AdaptiveCourses,
                Tasks = tasks,
                PlannedLinks = links,
                // Fouilles : se creusent pendant le terrassement, restent ouvertes jusqu'a la fin des fondations (remblai).
                Earthwork = Plan.Tasks.Any(t => t.Lot == "01") && !(Plan.Tasks.Where(t => t.Lot == "02").DefaultIfEmpty().All(t => t != null && progress.TryGetValue(t.Id, out double fp) && fp >= 1))
                    ? Plan.Tasks.Where(t => t.Lot == "01").Select(t => progress.TryGetValue(t.Id, out double ep) ? ep : 0).DefaultIfEmpty(0).Max()
                    : -1,
                Perspective = perspective || finalPerspective,
                RealShapes = realShapes,
                Record = recordPath,
                RecordFps = Math.Max(4, Math.Min(12, (int)Math.Round(1000.0 / Math.Max(60, speed.Milliseconds)))),
                Progress = Plan.TotalDays <= 0 ? 1 : Math.Min(1.0, day / (double)Plan.TotalDays),
                Title = (Boq.Dashboard?.HasData == true && !string.IsNullOrWhiteSpace(Boq.Dashboard.Title) ? Boq.Dashboard.Title : "Simulation 4D") + " — DanCI 4D"
            };
            clearShots = false;
            frame.Applied = () =>
            {
                if (frame.Lagging >= 0) hostLagging = frame.Lagging;
                OnApplied();
                if (makeReport) MakeReport(true); // la vue de face finale est capturee : rapport automatique
            };
            host.RequestSimulation4D(frame);
        }

        // ---- CADENCE ADAPTATIVE : sur un modele lourd (Revit lent a dessiner), chaque image pose plus de
        // rangs (jusqu'a 1 m) pour garder la meme vitesse de montee en SECONDES ; rien ne surgit pour autant.
        private const double ReferenceFrameMs = 150;

        private double Heaviness => Math.Max(1.0, Math.Min(5.0, (frameTimes.Count > 0 ? frameTimes.Average() : ReferenceFrameMs) / ReferenceFrameMs));

        private int AdaptiveCourses => Math.Max(rise.Courses, Math.Min(5, (int)Math.Round(rise.Courses * Heaviness)));

        private int AdaptiveMinFrames => Math.Max(4, (int)Math.Round(rise.MinFrames / Heaviness));

        // ---- RAPPORT DE SIMULATION : captures de la vue 3D (chaque 10 % + fin), deroulement, reglages
        private double nextShot = 0.1;
        private bool shotPending, clearShots = true, reportAfterFinal;
        private int shotWait;
        private string finalShot;
        private int runFrames;
        private double runMsSum;
        private TimeSpan playTime;
        private DateTime? playStarted;
        private ICommand reportCommand;

        /// <summary>Nouvelle lecture depuis le debut : statistiques et captures remises a zero ; capture du chantier vide.</summary>
        private void NewRun()
        {
            runFrames = 0;
            runMsSum = 0;
            playTime = TimeSpan.Zero;
            nextShot = 0.1;
            clearShots = true;
            shotPending = true; // etat initial (terrain, debut du chantier)
        }

        /// <summary>Bouton « Rapport » : rapport PDF a tout moment (captures prises jusque-la).</summary>
        public ICommand ReportCommand => reportCommand ?? (reportCommand = new RelayCommand(_ => MakeReport(false), _ => Plan != null && Plan.Tasks.Count > 0));

        private void MakeReport(bool completed)
        {
            if (host == null || Plan == null) return;
            TimeSpan played = playTime + (playStarted.HasValue ? DateTime.Now - playStarted.Value : TimeSpan.Zero);
            Status = "📄 Rapport de simulation en cours de création…";
            Raise(nameof(Status));
            host.RequestSimulationReport(new Sim4DReportRequest
            {
                Plan = Plan,
                Completed = completed || (Boq.PlanCursorDay >= MaxDay && finalPerspective),
                Frames = runFrames,
                AverageFrameMs = runFrames > 0 ? runMsSum / runFrames : 0,
                PlayTime = played,
                Settings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Pas de lecture", stepChoice.Label),
                    new KeyValuePair<string, string>("Vitesse", speed.Label),
                    new KeyValuePair<string, string>("Montée", rise.Label + (AdaptiveCourses > rise.Courses ? " (adaptée : " + AdaptiveCourses * 20 + " cm / image)" : string.Empty)),
                    new KeyValuePair<string, string>("Rotation", rotate ? turn.Label : "désactivée"),
                    new KeyValuePair<string, string>("Couleurs", colorMode.Label),
                    new KeyValuePair<string, string>("Rangs en construction", realShapes ? "volumes réels temporaires" : "dessin superposé"),
                    new KeyValuePair<string, string>("Fin de simulation", "vue de face en perspective (ViewCube : Avant)")
                },
                Done = (path, message) =>
                {
                    Status = message;
                    Raise(nameof(Status));
                }
            });
        }

        /// <summary>Elements que Revit fait encore monter derriere la date (rang par rang) : la date attend.</summary>
        private int hostLagging;
        private int holdFrames;
        /// <summary>Revit fait encore monter des elements (garde-fou : jamais plus de 200 images d'attente).</summary>
        private bool HostCatching => hostLagging > 0 && holdFrames < 200;
        private int catchSeq;

        private Dictionary<long, string> owners;
        private WorkPlan ownersPlan;

        /// <summary>Tache qui CONSTRUIT chaque element : la premiere (debut le plus tot) qui le contient.</summary>
        private Dictionary<long, string> OwnerTasks()
        {
            if (owners != null && ReferenceEquals(ownersPlan, Plan)) return owners;
            owners = Build4DState.Owners(Plan.Tasks);
            ownersPlan = Plan;
            return owners;
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
            RefreshCharts(day);
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
