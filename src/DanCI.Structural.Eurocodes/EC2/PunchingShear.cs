using System;
using System.Collections.Generic;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Verification du poinconnement sur un perimetre de controle donne.</summary>
    public sealed class PunchingPerimeterCheck
    {
        /// <summary>Distance du nu du poteau au perimetre considere (mm).</summary>
        public double DistanceMm { get; set; }
        /// <summary>Longueur du perimetre de controle u (mm).</summary>
        public double PerimeterMm { get; set; }
        /// <summary>Effort de poinconnement reduit V_Ed - delta V_Ed (N).</summary>
        public double NetShearN { get; set; }
        /// <summary>Contrainte de cisaillement appliquee v_Ed (MPa).</summary>
        public double AppliedStressMPa { get; set; }
        /// <summary>Contrainte resistante v_Rd,c majoree de 2d/a (MPa).</summary>
        public double ResistanceMPa { get; set; }

        public double Utilization
        {
            get { return ResistanceMPa > 0 ? AppliedStressMPa / ResistanceMPa : double.PositiveInfinity; }
        }
    }

    /// <summary>Resultat complet de la verification au poinconnement d'une semelle.</summary>
    public sealed class PunchingResult
    {
        /// <summary>Contrainte au nu du poteau (MPa).</summary>
        public double ColumnFaceStressMPa { get; set; }
        /// <summary>Contrainte maximale admissible au nu du poteau v_Rd,max (MPa).</summary>
        public double MaxStressMPa { get; set; }

        /// <summary>Perimetre de controle le plus defavorable.</summary>
        public PunchingPerimeterCheck Critical { get; set; }

        /// <summary>Tous les perimetres examines.</summary>
        public List<PunchingPerimeterCheck> Perimeters { get; private set; }

        public bool ColumnFacePasses
        {
            get { return MaxStressMPa > 0 && ColumnFaceStressMPa <= MaxStressMPa; }
        }

        public bool Passes
        {
            get { return ColumnFacePasses && Critical != null && Critical.Utilization <= 1.0; }
        }

        public string Justification { get; set; }

        public PunchingResult()
        {
            Perimeters = new List<PunchingPerimeterCheck>();
        }
    }

    /// <summary>
    /// Poinconnement selon l'EN 1992-1-1:2004, article 6.4.
    ///
    /// Pour une semelle, l'article 6.4.4(2) impose de rechercher le perimetre de controle le
    /// plus defavorable **a l'interieur** de 2d, et non de se contenter du perimetre a 2d :
    /// la contrainte appliquee decroit quand le perimetre s'eloigne, mais la resistance est
    /// majoree du facteur 2d/a. Le moteur balaye donc les perimetres et retient le pire.
    /// </summary>
    public static class PunchingShear
    {
        /// <summary>
        /// Longueur du perimetre de controle a la distance a du nu d'un poteau rectangulaire
        /// (figure 6.13, angles arrondis) : u = 2(c1 + c2) + 2 pi a.
        /// </summary>
        public static double Perimeter(double columnWidthMm, double columnDepthMm, double distanceMm)
        {
            return 2.0 * (columnWidthMm + columnDepthMm) + 2.0 * Math.PI * distanceMm;
        }

        /// <summary>Aire delimitee par le perimetre de controle a la distance a (mm2).</summary>
        public static double EnclosedArea(double columnWidthMm, double columnDepthMm, double distanceMm)
        {
            return columnWidthMm * columnDepthMm
                   + 2.0 * distanceMm * (columnWidthMm + columnDepthMm)
                   + Math.PI * distanceMm * distanceMm;
        }

        /// <summary>
        /// Contrainte resistante de base v_Rd,c (MPa), article 6.4.4(1), avant majoration 2d/a.
        /// </summary>
        public static double BaseResistance(double effectiveDepthMm, double reinforcementRatio,
                                            ConcreteProperties materials, double gammaC)
        {
            double crdc = 0.18 / gammaC;
            double k = 1.0 + Math.Sqrt(200.0 / effectiveDepthMm);
            if (k > 2.0) k = 2.0;

            double rho = Math.Min(Math.Max(reinforcementRatio, 0.0), 0.02);
            double main = crdc * k * Math.Pow(100.0 * rho * materials.Fck, 1.0 / 3.0);
            double vmin = 0.035 * Math.Pow(k, 1.5) * Math.Sqrt(materials.Fck);
            return Math.Max(main, vmin);
        }

        /// <summary>
        /// Verifie le poinconnement d'une semelle sous un poteau rectangulaire.
        /// </summary>
        /// <param name="columnLoadN">Charge du poteau V_Ed (N).</param>
        /// <param name="columnWidthMm">Dimension du poteau suivant X (c1).</param>
        /// <param name="columnDepthMm">Dimension du poteau suivant Y (c2).</param>
        /// <param name="effectiveDepthMm">Hauteur utile moyenne d.</param>
        /// <param name="netPressureMPa">Contrainte nette du sol sous la semelle (MPa).</param>
        /// <param name="reinforcementRatio">rho_l = sqrt(rho_x rho_y), plafonne a 0,02.</param>
        /// <param name="maxDistanceMm">Distance maximale exploree, en general min(2d ; debord).</param>
        public static PunchingResult Check(double columnLoadN, double columnWidthMm,
                                           double columnDepthMm, double effectiveDepthMm,
                                           double netPressureMPa, double reinforcementRatio,
                                           ConcreteProperties materials, double gammaC,
                                           double maxDistanceMm)
        {
            var result = new PunchingResult();

            // Article 6.4.5(3) : au nu du poteau, v_Ed <= v_Rd,max = 0,5 nu f_cd.
            double columnPerimeter = 2.0 * (columnWidthMm + columnDepthMm);
            result.ColumnFaceStressMPa = columnPerimeter > 0
                ? columnLoadN / (columnPerimeter * effectiveDepthMm) : 0.0;
            double nu = 0.6 * (1.0 - materials.Fck / 250.0);
            result.MaxStressMPa = 0.5 * nu * materials.Fcd;

            double baseResistance = BaseResistance(effectiveDepthMm, reinforcementRatio,
                                                   materials, gammaC);
            double limit = Math.Min(2.0 * effectiveDepthMm, Math.Max(maxDistanceMm, 0.0));

            // Balayage des perimetres de controle, article 6.4.4(2).
            const int steps = 20;
            for (int i = 1; i <= steps; i++)
            {
                double distance = limit * i / steps;
                if (distance <= 0) continue;

                double perimeter = Perimeter(columnWidthMm, columnDepthMm, distance);
                double relieved = netPressureMPa * EnclosedArea(columnWidthMm, columnDepthMm, distance);
                double net = Math.Max(columnLoadN - relieved, 0.0);

                var check = new PunchingPerimeterCheck
                {
                    DistanceMm = distance,
                    PerimeterMm = perimeter,
                    NetShearN = net,
                    AppliedStressMPa = net / (perimeter * effectiveDepthMm),
                    ResistanceMPa = baseResistance * 2.0 * effectiveDepthMm / distance
                };
                result.Perimeters.Add(check);

                if (result.Critical == null || check.Utilization > result.Critical.Utilization)
                {
                    result.Critical = check;
                }
            }

            PunchingPerimeterCheck critical = result.Critical;
            result.Justification = critical == null
                ? "Aucun perimetre de controle exploitable : verifiez le debord de la semelle."
                : string.Format(
                    "EC2 6.4.4(2) : perimetre critique a a = {0:0} mm du nu ({1:0.00} d), " +
                    "u = {2:0} mm, V_Ed reduit = {3:0.0} kN, v_Ed = {4:0.000} MPa, " +
                    "v_Rd,c x 2d/a = {5:0.000} MPa -> taux {6:0.00}. " +
                    "Au nu du poteau : v_Ed = {7:0.000} MPa <= v_Rd,max = {8:0.000} MPa {9}",
                    critical.DistanceMm, critical.DistanceMm / effectiveDepthMm,
                    critical.PerimeterMm, critical.NetShearN / 1000.0,
                    critical.AppliedStressMPa, critical.ResistanceMPa, critical.Utilization,
                    result.ColumnFaceStressMPa, result.MaxStressMPa,
                    result.ColumnFacePasses ? "(OK)" : "(DEPASSE)");
            return result;
        }
    }
}
