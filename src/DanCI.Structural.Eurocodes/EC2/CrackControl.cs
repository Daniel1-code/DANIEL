using System;
using DanCI.Structural.Core.Materials;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Resultat de la maitrise de la fissuration sans calcul direct, art. 7.3.3.</summary>
    public sealed class CrackControlResult
    {
        /// <summary>Ouverture de fissure visee w_max (mm).</summary>
        public double CrackWidthLimitMm { get; set; }

        /// <summary>Contrainte de l'acier sous combinaison quasi-permanente (MPa).</summary>
        public double SteelStressMPa { get; set; }

        /// <summary>Diametre maximal admissible, tableau 7.2N (mm).</summary>
        public double MaxBarDiameterMm { get; set; }

        /// <summary>Espacement maximal admissible, tableau 7.3N (mm).</summary>
        public double MaxSpacingMm { get; set; }

        /// <summary>Le critere de diametre est-il satisfait ?</summary>
        public bool DiameterSatisfied { get; set; }

        /// <summary>Le critere d'espacement est-il satisfait ?</summary>
        public bool SpacingSatisfied { get; set; }

        /// <summary>
        /// L'article 7.3.3(2) considere la fissuration maitrisee des que **l'un** des deux
        /// criteres est satisfait : il n'est pas necessaire de verifier les deux.
        /// </summary>
        public bool Passes { get { return DiameterSatisfied || SpacingSatisfied; } }

        /// <summary>Section minimale pour la maitrise de la fissuration, eq. 7.1 (mm2).</summary>
        public double MinimumSteelMm2 { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Maitrise de la fissuration sans calcul direct, EN 1992-1-1 article 7.3.
    ///
    /// Les tableaux 7.2N et 7.3N sont donnes pour trois ouvertures de fissure. Les valeurs
    /// intermediaires sont interpolees lineairement, comme l'autorise la note des tableaux ;
    /// au-dela des bornes, la valeur de bord est conservee, ce qui est securitaire.
    /// </summary>
    public static class CrackControl
    {
        /// <summary>Contraintes de l'acier tabulees (MPa).</summary>
        private static readonly double[] Stresses = { 160, 200, 240, 280, 320, 360, 400, 450 };

        /// <summary>Tableau 7.2N : diametre maximal (mm), une ligne par w_k.</summary>
        private static readonly double[] Diameter04 = { 40, 32, 20, 16, 12, 10, 8, 6 };
        private static readonly double[] Diameter03 = { 32, 25, 16, 12, 10, 8, 6, 5 };
        private static readonly double[] Diameter02 = { 25, 16, 12, 8, 6, 5, 4, 0 };

        /// <summary>Tableau 7.3N : espacement maximal (mm). 0 = aucune valeur tabulee.</summary>
        private static readonly double[] Spacing04 = { 300, 300, 250, 200, 150, 100, 0, 0 };
        private static readonly double[] Spacing03 = { 300, 250, 200, 150, 100, 50, 0, 0 };
        private static readonly double[] Spacing02 = { 200, 150, 100, 50, 0, 0, 0, 0 };

        /// <summary>
        /// Ouverture de fissure recommandee, tableau 7.1N, pour du beton arme.
        /// </summary>
        public static double RecommendedCrackWidthMm(ExposureClass exposure)
        {
            // 0,4 mm pour X0 et XC1, 0,3 mm pour toutes les autres classes en beton arme.
            return exposure == ExposureClass.X0 || exposure == ExposureClass.XC1
                ? 0.4 : 0.3;
        }

        /// <summary>Diametre maximal des barres, tableau 7.2N.</summary>
        public static double MaxBarDiameter(double steelStressMPa, double crackWidthMm)
        {
            return Interpolate(steelStressMPa, crackWidthMm, Diameter04, Diameter03, Diameter02);
        }

        /// <summary>Espacement maximal des barres, tableau 7.3N.</summary>
        public static double MaxSpacing(double steelStressMPa, double crackWidthMm)
        {
            return Interpolate(steelStressMPa, crackWidthMm, Spacing04, Spacing03, Spacing02);
        }

        /// <summary>
        /// Section minimale d'armature pour la maitrise de la fissuration, eq. 7.1 :
        /// A_s,min sigma_s = k_c k f_ct,eff A_ct.
        /// </summary>
        /// <param name="tensionZoneAreaMm2">A_ct, aire de beton tendu juste avant fissuration.</param>
        /// <param name="fctEffMPa">f_ct,eff, pris egal a f_ctm.</param>
        /// <param name="steelStressMPa">sigma_s, au plus f_yk.</param>
        /// <param name="kc">0,4 en flexion pure d'une section rectangulaire, 1,0 en traction.</param>
        /// <param name="k">Effet des contraintes non uniformes : 1,0 si h &lt;= 300 mm.</param>
        public static double MinimumSteel(double tensionZoneAreaMm2, double fctEffMPa,
                                          double steelStressMPa, double kc = 0.4, double k = 1.0)
        {
            if (steelStressMPa <= 0) return 0.0;
            return kc * k * fctEffMPa * tensionZoneAreaMm2 / steelStressMPa;
        }

        /// <summary>Coefficient k de l'eq. 7.1, fonction de la dimension de la section.</summary>
        public static double NonUniformStressFactor(double sectionDepthMm)
        {
            if (sectionDepthMm <= 300.0) return 1.0;
            if (sectionDepthMm >= 800.0) return 0.65;
            // Interpolation lineaire entre 300 et 800 mm, art. 7.3.2(2).
            return 1.0 - 0.35 * (sectionDepthMm - 300.0) / 500.0;
        }

        /// <summary>
        /// Estimation de la contrainte de l'acier sous combinaison quasi-permanente.
        ///
        /// sigma_s = f_yd (M_qp / M_Ed) (A_s,req / A_s,prov), soit la contrainte ELU
        /// reduite dans le rapport des moments et dans celui des sections. C'est
        /// l'approximation usuelle : elle evite une analyse en section fissuree, et
        /// c'est bien une **estimation** — la fiche de validation le dit.
        /// </summary>
        public static double SteelStress(double fydMPa, double quasiPermanentMoment,
                                         double ultimateMoment, double steelRequiredMm2,
                                         double steelProvidedMm2)
        {
            if (ultimateMoment <= 0 || steelProvidedMm2 <= 0) return fydMPa;
            double momentRatio = quasiPermanentMoment / ultimateMoment;
            double steelRatio = steelRequiredMm2 / steelProvidedMm2;
            return fydMPa * momentRatio * steelRatio;
        }

        /// <summary>Verification complete de l'article 7.3.3.</summary>
        public static CrackControlResult Check(double steelStressMPa, double crackWidthMm,
                                               double barDiameterMm, double spacingMm)
        {
            var result = new CrackControlResult
            {
                CrackWidthLimitMm = crackWidthMm,
                SteelStressMPa = steelStressMPa,
                MaxBarDiameterMm = MaxBarDiameter(steelStressMPa, crackWidthMm),
                MaxSpacingMm = MaxSpacing(steelStressMPa, crackWidthMm)
            };

            result.DiameterSatisfied = result.MaxBarDiameterMm > 0
                                       && barDiameterMm <= result.MaxBarDiameterMm + 1e-9;
            result.SpacingSatisfied = result.MaxSpacingMm > 0
                                      && spacingMm <= result.MaxSpacingMm + 1e-9;

            result.Justification = string.Format(
                "EC2 7.3.3 : sigma_s = {0:0} MPa pour w_max = {1:0.0} mm -> " +
                "diametre max {2:0} mm (tableau 7.2N){3}, espacement max {4:0} mm " +
                "(tableau 7.3N){5} ; un seul des deux criteres suffit",
                steelStressMPa, crackWidthMm, result.MaxBarDiameterMm,
                result.DiameterSatisfied ? " respecte" : " DEPASSE",
                result.MaxSpacingMm,
                result.SpacingSatisfied ? " respecte" : " DEPASSE");
            return result;
        }

        private static double Interpolate(double stress, double crackWidthMm,
                                          double[] row04, double[] row03, double[] row02)
        {
            double[] row;
            if (crackWidthMm >= 0.4) row = row04;
            else if (crackWidthMm >= 0.3) row = row03;
            else row = row02;

            if (stress <= Stresses[0]) return row[0];
            if (stress >= Stresses[Stresses.Length - 1]) return row[Stresses.Length - 1];

            for (int i = 0; i < Stresses.Length - 1; i++)
            {
                if (stress > Stresses[i + 1]) continue;

                double t = (stress - Stresses[i]) / (Stresses[i + 1] - Stresses[i]);
                // Une borne nulle signifie « hors tableau » : on ne l'interpole pas,
                // on conserve la valeur nulle qui fera echouer le critere.
                if (row[i + 1] <= 0) return 0.0;
                return row[i] + t * (row[i + 1] - row[i]);
            }

            return row[Stresses.Length - 1];
        }
    }
}
