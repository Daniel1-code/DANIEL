using System;
using System.Globalization;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Un pli de la barre : son angle de deviation et ce qu'il coute en longueur.</summary>
    public sealed class BendResult
    {
        /// <summary>Angle de DEVIATION du pli (degres), zero pour une barre droite.</summary>
        public double DeviationAngleDegrees { get; set; }

        /// <summary>Diametre de mandrin retenu (mm).</summary>
        public double MandrelDiameterMm { get; set; }

        /// <summary>Rayon de la fibre moyenne dans le pli (mm).</summary>
        public double CentrelineRadiusMm { get; set; }

        /// <summary>
        /// Difference entre le chemin d'angle a angle et la fibre moyenne reelle (mm),
        /// toujours positive : la barre coupe le coin.
        /// </summary>
        public double DeductionMm { get; set; }

        /// <summary>Le pli depasse-t-il ce que le modele sait traiter ?</summary>
        public bool IsBeyondModel { get; set; }
    }

    /// <summary>
    /// Faconnage des armatures selon l'EN 1992-1-1 article 8.3.
    ///
    /// DEUX CHOSES DIFFERENTES Y SONT CALCULEES, et les confondre est l'erreur courante.
    ///
    /// 1. Le DIAMETRE DE MANDRIN minimal. Le tableau 8.1N donne 4 phi jusqu'a 16 mm et
    ///    7 phi au-dela : c'est ce qu'il faut pour ne pas endommager l'acier en le pliant.
    ///    L'article 8.3(3) impose en outre, dans certains cas, un mandrin plus grand pour
    ///    ne pas ecraser le beton a l'interieur du coude — c'est l'expression (8.1), qui
    ///    depend de l'effort dans la barre et de l'espacement, pas du seul diametre.
    ///
    /// 2. La LONGUEUR DE COUPE. Une barre pliee ne suit pas le chemin d'angle a angle :
    ///    elle coupe le coin par un arc de rayon (phi_m + phi)/2. Sommer les segments
    ///    d'une polyligne SURESTIME donc la longueur developpee, de
    ///    2 R tan(beta/2) - R beta par pli. Sur un cadre 300 x 500 en HA8 cela fait
    ///    34 mm, soit 2,3 % du developpe ; sur une attente HA20 en L, 34 mm d'un coup.
    ///    L'ecart est petit par barre et se compte en tonnes sur un projet.
    /// </summary>
    public static class BarBending
    {
        /// <summary>Au-dela de cet angle, le pli releve du crochet, pas du faconnage courant.</summary>
        public const double MaximumModelledAngleDegrees = 150.0;

        /// <summary>
        /// Diametre de mandrin minimal du tableau 8.1N, celui qui evite d'endommager
        /// l'acier. Il ne prend PAS en compte l'ecrasement du beton dans le coude : voir
        /// <see cref="MandrelAgainstConcreteFailure"/>.
        /// </summary>
        public static double MinimumMandrelDiameterMm(double barDiameterMm)
        {
            if (barDiameterMm <= 0) return 0.0;
            return barDiameterMm <= 16.0 ? 4.0 * barDiameterMm : 7.0 * barDiameterMm;
        }

        /// <summary>
        /// Expression (8.1) de l'article 8.3(3) : mandrin minimal pour eviter la rupture du
        /// beton a l'interieur du coude.
        ///
        ///   phi_m,min &gt;= F_bt (1/a_b + 1/(2 phi)) / f_cd
        ///
        /// Cette verification n'est PAS toujours requise. L'article la dispense si
        /// l'ancrage au-dela du pli ne demande pas plus de 5 phi, ou si la barre n'est pas
        /// en rive et qu'une barre au moins aussi grosse passe a l'interieur du coude. Le
        /// moteur ne devine pas ces conditions : il rend la valeur et laisse l'ingenieur
        /// decider si elle s'applique.
        /// </summary>
        /// <param name="tensileForceN">F_bt, effort de traction a l'origine du pli (N).</param>
        /// <param name="halfSpacingMm">a_b, demi-entraxe des barres perpendiculairement au
        /// plan du pli ; pour une barre de rive, l'enrobage augmente de phi/2.</param>
        /// <param name="barDiameterMm">phi.</param>
        /// <param name="fcdMPa">f_cd, plafonne a la valeur du C55/67 par l'article.</param>
        public static double MandrelAgainstConcreteFailure(double tensileForceN,
                                                           double halfSpacingMm,
                                                           double barDiameterMm,
                                                           double fcdMPa)
        {
            if (tensileForceN <= 0 || halfSpacingMm <= 0 || barDiameterMm <= 0 || fcdMPa <= 0)
            {
                return 0.0;
            }

            // L'article plafonne f_cd a celui du C55/67 : 55/1,5 = 36,67 MPa avec
            // alpha_cc = 1,0, valeur recommandee.
            double cappedFcd = Math.Min(fcdMPa, 55.0 / 1.5);
            return tensileForceN * (1.0 / halfSpacingMm + 1.0 / (2.0 * barDiameterMm))
                   / cappedFcd;
        }

        /// <summary>
        /// Ce que coute un pli en longueur developpee, par rapport au chemin d'angle a
        /// angle.
        ///
        /// La barre entre dans le pli a une distance R tan(beta/2) du sommet et en ressort
        /// a la meme distance ; le chemin polyligne vaut donc 2 R tan(beta/2) alors que
        /// l'arc vaut R beta. La difference est toujours positive : sommer les segments
        /// d'une polyligne surestime la longueur reelle.
        /// </summary>
        /// <param name="deviationAngleDegrees">Angle dont la barre change de direction.</param>
        /// <param name="barDiameterMm">phi.</param>
        /// <param name="mandrelDiameterMm">Mandrin retenu ; zero pour le minimum du tableau.</param>
        public static BendResult Bend(double deviationAngleDegrees, double barDiameterMm,
                                      double mandrelDiameterMm = 0.0)
        {
            double mandrel = mandrelDiameterMm > 0
                ? mandrelDiameterMm : MinimumMandrelDiameterMm(barDiameterMm);
            double radius = (mandrel + barDiameterMm) / 2.0;

            var result = new BendResult
            {
                DeviationAngleDegrees = deviationAngleDegrees,
                MandrelDiameterMm = mandrel,
                CentrelineRadiusMm = radius
            };

            if (deviationAngleDegrees <= 0.01 || barDiameterMm <= 0)
            {
                return result;
            }

            if (deviationAngleDegrees >= MaximumModelledAngleDegrees)
            {
                // tan(beta/2) diverge en approchant 180 degres : un tel pli est un
                // crochet, il se compte par son retour et non par une deduction.
                result.IsBeyondModel = true;
                return result;
            }

            double beta = deviationAngleDegrees * Math.PI / 180.0;
            result.DeductionMm = 2.0 * radius * Math.Tan(beta / 2.0) - radius * beta;
            return result;
        }

        /// <summary>
        /// Longueur de coupe d'une barre dont le trajet est donne d'angle a angle.
        ///
        /// C'est la longueur qui part chez le facconnier. Elle vaut la somme des segments
        /// MOINS la deduction de chaque pli, plus les retours de crochets s'il y en a.
        /// </summary>
        /// <param name="polylineLengthMm">Somme des segments, d'angle a angle.</param>
        /// <param name="deviationAnglesDegrees">Angle de deviation de chaque pli.</param>
        /// <param name="barDiameterMm">phi.</param>
        /// <param name="hookAllowanceMm">Retours de crochets, deja calcules par ailleurs.</param>
        /// <param name="mandrelDiameterMm">Mandrin retenu ; zero pour le minimum du tableau.</param>
        public static CutLengthResult CutLength(double polylineLengthMm,
                                                double[] deviationAnglesDegrees,
                                                double barDiameterMm,
                                                double hookAllowanceMm = 0.0,
                                                double mandrelDiameterMm = 0.0)
        {
            var result = new CutLengthResult
            {
                PolylineLengthMm = polylineLengthMm,
                HookAllowanceMm = hookAllowanceMm,
                MandrelDiameterMm = mandrelDiameterMm > 0
                    ? mandrelDiameterMm : MinimumMandrelDiameterMm(barDiameterMm)
            };

            if (deviationAnglesDegrees != null)
            {
                foreach (double angle in deviationAnglesDegrees)
                {
                    BendResult bend = Bend(angle, barDiameterMm, mandrelDiameterMm);
                    if (bend.DeviationAngleDegrees <= 0.01) continue;

                    result.BendCount++;
                    result.TotalDeductionMm += bend.DeductionMm;
                    if (bend.IsBeyondModel) result.HasBendBeyondModel = true;
                }
            }

            result.CutLengthMm = Math.Max(
                polylineLengthMm - result.TotalDeductionMm + hookAllowanceMm, 0.0);

            result.Justification = string.Format(CultureInfo.InvariantCulture,
                "EN 1992-1-1 8.3 : mandrin {0:0} mm (tableau 8.1N), rayon de fibre moyenne " +
                "{1:0.0} mm. Developpe d'angle a angle {2:0} mm, moins {3} pli(s) pour " +
                "{4:0.0} mm, plus {5:0.0} mm de crochets = {6:0} mm de longueur de coupe.",
                result.MandrelDiameterMm, (result.MandrelDiameterMm + barDiameterMm) / 2.0,
                polylineLengthMm, result.BendCount, result.TotalDeductionMm,
                hookAllowanceMm, result.CutLengthMm);

            return result;
        }
    }

    /// <summary>Longueur de coupe d'une barre et sa decomposition.</summary>
    public sealed class CutLengthResult
    {
        /// <summary>Somme des segments, d'angle a angle (mm).</summary>
        public double PolylineLengthMm { get; set; }

        /// <summary>Nombre de plis reels.</summary>
        public int BendCount { get; set; }

        /// <summary>Total des deductions de pli (mm).</summary>
        public double TotalDeductionMm { get; set; }

        /// <summary>Retours de crochets (mm).</summary>
        public double HookAllowanceMm { get; set; }

        /// <summary>Diametre de mandrin retenu (mm).</summary>
        public double MandrelDiameterMm { get; set; }

        /// <summary>Longueur de coupe (mm).</summary>
        public double CutLengthMm { get; set; }

        /// <summary>Un pli sort du domaine du modele et n'a pas ete deduit.</summary>
        public bool HasBendBeyondModel { get; set; }

        public string Justification { get; set; }
    }
}
