namespace DanCI.Structural.Core.Loads
{
    /// <summary>
    /// Les six composantes de sollicitation d'une section, en unites internes (N et N.mm).
    /// Elles voyagent toujours ensemble : une verification ne doit jamais associer le N d'une
    /// combinaison au M d'une autre.
    /// </summary>
    public readonly struct InternalForces
    {
        /// <summary>Effort normal, positif en compression (N).</summary>
        public double N { get; }

        /// <summary>Effort tranchant suivant Y (N).</summary>
        public double Vy { get; }

        /// <summary>Effort tranchant suivant Z (N).</summary>
        public double Vz { get; }

        /// <summary>Moment de torsion (N.mm).</summary>
        public double T { get; }

        /// <summary>Moment flechissant autour de l'axe Y (N.mm).</summary>
        public double My { get; }

        /// <summary>Moment flechissant autour de l'axe Z (N.mm).</summary>
        public double Mz { get; }

        public InternalForces(double n, double vy, double vz, double t, double my, double mz)
        {
            N = n;
            Vy = vy;
            Vz = vz;
            T = t;
            My = my;
            Mz = mz;
        }

        /// <summary>Sollicitations d'un poteau : effort normal et deux moments.</summary>
        public static InternalForces Column(double n, double momentAboutX, double momentAboutY)
        {
            return new InternalForces(n, 0.0, 0.0, 0.0, momentAboutY, momentAboutX);
        }

        /// <summary>Moment autour de l'axe local X de la section (N.mm).</summary>
        public double MomentAboutX
        {
            get { return Mz; }
        }

        /// <summary>Moment autour de l'axe local Y de la section (N.mm).</summary>
        public double MomentAboutY
        {
            get { return My; }
        }
    }
}
