using System;
using System.Collections.Generic;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// Verification de resistance d'une section de poteau en flexion composee selon
    /// l'Eurocode 2 : diagramme d'interaction N-M obtenu par integration des contraintes
    /// (loi parabole-rectangle pour le beton, elastoplastique parfait pour l'acier),
    /// effets du second ordre par la methode de la courbure nominale (EC2 5.8.8) et
    /// interaction biaxiale par la formule de l'article 5.8.9(4).
    /// </summary>
    public class SectionCapacity
    {
        private const double ElasticModulusSteel = 200000.0;  // MPa
        private const double StrainC2 = 0.002;                // eps_c2
        private const double StrainCu2 = 0.0035;              // eps_cu2
        private const double StrainUltimateSteel = 0.045;     // eps_ud, pivot A
        private const int StripCount = 240;

        private readonly ColumnGeometry _geometry;
        private readonly DesignResult _design;
        private readonly DesignInput _input;
        private readonly double _fcd;
        private readonly double _fyd;
        private readonly double _exponentN;

        public SectionCapacity(ColumnGeometry geometry, DesignResult design, DesignInput input)
        {
            _geometry = geometry;
            _design = design;
            _input = input;
            _fcd = input.ConcreteStrengthMPa / 1.5;          // alpha_cc = 1,0
            _fyd = input.SteelStrengthMPa / 1.15;
            _exponentN = input.ConcreteStrengthMPa <= 50.0
                ? 2.0
                : 1.4 + 23.4 * Math.Pow((90.0 - input.ConcreteStrengthMPa) / 100.0, 4.0);
        }

        // ------------------------------------------------------------------
        // Lois de comportement
        // ------------------------------------------------------------------

        /// <summary>Contrainte du beton (MPa) pour une deformation de compression positive.</summary>
        private double ConcreteStress(double strain)
        {
            if (strain <= 0.0) return 0.0;
            if (strain >= StrainC2) return _fcd;
            return _fcd * (1.0 - Math.Pow(1.0 - strain / StrainC2, _exponentN));
        }

        /// <summary>Contrainte de l'acier (MPa), compression positive.</summary>
        private double SteelStress(double strain)
        {
            double stress = ElasticModulusSteel * strain;
            if (stress > _fyd) return _fyd;
            if (stress < -_fyd) return -_fyd;
            return stress;
        }

        // ------------------------------------------------------------------
        // Diagramme d'interaction
        // ------------------------------------------------------------------

        /// <summary>Section vue depuis une direction de flexion donnee.</summary>
        private class BendingSection
        {
            /// <summary>Hauteur totale mesuree depuis la fibre la plus comprimee (mm).</summary>
            public double Height;
            /// <summary>Largeur de la section a la profondeur y (mm).</summary>
            public Func<double, double> Width;
            /// <summary>Barres : profondeur depuis la fibre comprimee (mm) et aire (mm2).</summary>
            public List<double> BarDepths = new List<double>();
            public List<double> BarAreas = new List<double>();
            /// <summary>Profondeur utile de la nappe tendue (mm).</summary>
            public double EffectiveDepth;
        }

        /// <summary>
        /// Construit la vue de la section pour une flexion autour de l'axe X (aboutX = true,
        /// la fibre comprimee est une face perpendiculaire a Y) ou autour de l'axe Y.
        /// </summary>
        private BendingSection BuildSection(bool aboutX)
        {
            var section = new BendingSection();
            List<BarPoint> bars = RebarLayout.Bars(_geometry, _design);

            if (_geometry.Kind == SectionKind.Circular)
            {
                double diameter = _geometry.DiameterMm;
                double radius = diameter / 2.0;
                section.Height = diameter;
                section.Width = y =>
                {
                    double dy = y - radius;
                    double inside = radius * radius - dy * dy;
                    return inside > 0 ? 2.0 * Math.Sqrt(inside) : 0.0;
                };
                foreach (BarPoint bar in bars)
                {
                    double coordinate = aboutX ? bar.YMm : bar.XMm;
                    section.BarDepths.Add(radius - coordinate);
                    section.BarAreas.Add(bar.AreaMm2);
                }
            }
            else
            {
                double height = aboutX ? _geometry.DepthMm : _geometry.WidthMm;
                double width = aboutX ? _geometry.WidthMm : _geometry.DepthMm;
                section.Height = height;
                section.Width = y => width;
                foreach (BarPoint bar in bars)
                {
                    double coordinate = aboutX ? bar.YMm : bar.XMm;
                    section.BarDepths.Add(height / 2.0 - coordinate);
                    section.BarAreas.Add(bar.AreaMm2);
                }
            }

            double deepest = 0.0;
            foreach (double depth in section.BarDepths) deepest = Math.Max(deepest, depth);
            section.EffectiveDepth = deepest;
            return section;
        }

        /// <summary>
        /// Deformation a la profondeur y pour une position d'axe neutre x, en appliquant
        /// les regles des trois pivots (A : acier tendu, B : beton comprime, C : compression
        /// centree). Compression positive.
        /// </summary>
        private static double Strain(double x, double y, double height, double effectiveDepth)
        {
            double curvature;
            if (x <= height)
            {
                curvature = StrainCu2 / Math.Max(x, 1e-6);                 // pivot B
                if (x < effectiveDepth)
                {
                    double pivotA = StrainUltimateSteel / (effectiveDepth - x);
                    curvature = Math.Min(curvature, pivotA);               // pivot A
                }
            }
            else
            {
                // Pivot C : la droite passe par eps_c2 a la profondeur (1 - eps_c2/eps_cu2) h.
                double pivotDepth = height * (1.0 - StrainC2 / StrainCu2);
                curvature = StrainC2 / Math.Max(x - pivotDepth, 1e-6);
            }
            return curvature * (x - y);
        }

        /// <summary>Effort normal (N) et moment (N.mm) resistants pour une position d'axe neutre.</summary>
        private void Resultants(BendingSection section, double x, out double normalForce, out double moment)
        {
            double height = section.Height;
            double stripHeight = height / StripCount;
            normalForce = 0.0;
            moment = 0.0;

            for (int i = 0; i < StripCount; i++)
            {
                double y = (i + 0.5) * stripHeight;
                double strain = Strain(x, y, height, section.EffectiveDepth);
                double force = ConcreteStress(strain) * section.Width(y) * stripHeight;
                normalForce += force;
                moment += force * (height / 2.0 - y);
            }

            for (int i = 0; i < section.BarDepths.Count; i++)
            {
                double y = section.BarDepths[i];
                double strain = Strain(x, y, height, section.EffectiveDepth);
                // La barre comprimee remplace du beton : on retranche la contrainte du beton.
                double force = section.BarAreas[i] * (SteelStress(strain) - ConcreteStress(strain));
                normalForce += force;
                moment += force * (height / 2.0 - y);
            }
        }

        /// <summary>
        /// Moment resistant (kN.m) pour l'effort normal donne (kN), obtenu en balayant
        /// l'axe neutre et en interpolant sur le diagramme d'interaction.
        /// Renvoie -1 si l'effort normal sort du diagramme.
        /// </summary>
        public double MomentResistanceKnm(double axialLoadKn, bool aboutX)
        {
            BendingSection section = BuildSection(aboutX);
            double target = axialLoadKn * 1000.0; // N

            double previousN = 0.0, previousM = 0.0;
            double best = -1.0;
            bool first = true;

            const int steps = 220;
            for (int i = 0; i <= steps; i++)
            {
                // Balayage geometrique : fin pres de la flexion simple, large en compression.
                double ratio = (double)i / steps;
                double x = section.Height * (0.02 * Math.Pow(2000.0, ratio));
                double n, m;
                Resultants(section, x, out n, out m);

                if (!first && ((previousN - target) * (n - target) <= 0.0) && Math.Abs(n - previousN) > 1e-9)
                {
                    double t = (target - previousN) / (n - previousN);
                    double interpolated = previousM + t * (m - previousM);
                    best = Math.Max(best, interpolated);
                }
                previousN = n;
                previousM = m;
                first = false;
            }

            return best < 0 ? -1.0 : best / 1e6; // N.mm -> kN.m
        }

        /// <summary>Effort normal resistant de la section entierement comprimee (kN).</summary>
        public double AxialResistanceKn()
        {
            double concrete = _geometry.GrossAreaMm2 * _fcd;
            double steel = _design.AsProvidedMm2 * _fyd;
            return (concrete + steel) / 1000.0;
        }

        // ------------------------------------------------------------------
        // Verification complete
        // ------------------------------------------------------------------

        public CapacityCheck Verify()
        {
            var check = new CapacityCheck { Performed = true, AxialLoadKn = _input.AxialLoadKn };

            double nEd = _input.AxialLoadKn;
            if (nEd <= 0)
            {
                check.Performed = false;
                check.Notes.Add("Verification non effectuee : l'effort normal NEd n'est pas renseigne.");
                return check;
            }

            double heightX = _geometry.Kind == SectionKind.Circular
                ? _geometry.DiameterMm : _geometry.DepthMm;
            double heightY = _geometry.Kind == SectionKind.Circular
                ? _geometry.DiameterMm : _geometry.WidthMm;

            // --- Excentricite minimale (EC2 6.1(4)) ---
            double e0X = Math.Max(heightX / 30.0, 20.0);
            double e0Y = Math.Max(heightY / 30.0, 20.0);
            check.MinimumEccentricityMm = Math.Max(e0X, e0Y);

            double m0X = Math.Max(_input.MomentAboutXKnm, nEd * e0X / 1000.0);
            double m0Y = Math.Max(_input.MomentAboutYKnm, nEd * e0Y / 1000.0);
            check.Notes.Add(string.Format(
                "Excentricite minimale e0 = max(h/30 ; 20 mm) : {0:0} mm suivant X, {1:0} mm suivant Y " +
                "(EC2 6.1(4))", e0X, e0Y));

            // --- Elancement (EC2 5.8.3) ---
            double bucklingLengthMm = _input.BucklingFactor * _geometry.HeightMm;
            double radiusX = heightX / Math.Sqrt(12.0);
            double radiusY = heightY / Math.Sqrt(12.0);
            check.SlendernessX = bucklingLengthMm / radiusX;
            check.SlendernessY = bucklingLengthMm / radiusY;

            double relativeAxial = nEd * 1000.0 / (_geometry.GrossAreaMm2 * _fcd);
            double mechanicalRatio = _design.AsProvidedMm2 * _fyd / (_geometry.GrossAreaMm2 * _fcd);
            double factorA = 1.0 / (1.0 + 0.2 * _input.CreepCoefficient);
            double factorB = Math.Sqrt(1.0 + 2.0 * mechanicalRatio);
            const double factorC = 0.7;   // rM inconnu : valeur recommandee
            check.SlendernessLimit = 20.0 * factorA * factorB * factorC / Math.Sqrt(Math.Max(relativeAxial, 1e-6));
            check.Notes.Add(string.Format(
                "EC2 5.8.3.1 : l0 = {0:0.00} x {1:0} = {2:0} mm, lambda_x = {3:0}, lambda_y = {4:0}, " +
                "lambda_lim = {5:0}",
                _input.BucklingFactor, _geometry.HeightMm, bucklingLengthMm,
                check.SlendernessX, check.SlendernessY, check.SlendernessLimit));

            // --- Second ordre par courbure nominale (EC2 5.8.8) ---
            double mEdX = m0X;
            double mEdY = m0Y;
            double maxSlenderness = Math.Max(check.SlendernessX, check.SlendernessY);
            check.SecondOrderRequired = maxSlenderness > check.SlendernessLimit;

            if (check.SecondOrderRequired)
            {
                if (check.SlendernessX > check.SlendernessLimit)
                {
                    check.SecondOrderMomentXKnm = SecondOrderMoment(nEd, bucklingLengthMm, heightX,
                        check.SlendernessX, relativeAxial, mechanicalRatio, true);
                    mEdX += check.SecondOrderMomentXKnm;
                }
                if (check.SlendernessY > check.SlendernessLimit)
                {
                    check.SecondOrderMomentYKnm = SecondOrderMoment(nEd, bucklingLengthMm, heightY,
                        check.SlendernessY, relativeAxial, mechanicalRatio, false);
                    mEdY += check.SecondOrderMomentYKnm;
                }
                check.Notes.Add(string.Format(
                    "Poteau elance : moments du second ordre ajoutes (EC2 5.8.8.2) - " +
                    "M2x = {0:0.0} kN.m, M2y = {1:0.0} kN.m",
                    check.SecondOrderMomentXKnm, check.SecondOrderMomentYKnm));
            }
            else
            {
                check.Notes.Add("lambda <= lambda_lim : les effets du second ordre peuvent etre negliges.");
            }

            check.DesignMomentXKnm = mEdX;
            check.DesignMomentYKnm = mEdY;

            // --- Resistance de la section ---
            check.AxialResistanceKn = AxialResistanceKn();
            double mRdX = MomentResistanceKnm(nEd, true);
            double mRdY = MomentResistanceKnm(nEd, false);
            check.ResistanceMomentXKnm = mRdX;
            check.ResistanceMomentYKnm = mRdY;

            if (mRdX < 0 || mRdY < 0)
            {
                check.Passes = false;
                check.Utilisation = 99.0;
                check.Notes.Add(string.Format(
                    "NEd = {0:0} kN depasse la capacite de la section (NRd = {1:0} kN) : " +
                    "aucune resistance en flexion n'est disponible.", nEd, check.AxialResistanceKn));
                return check;
            }

            check.Notes.Add(string.Format(
                "Resistance de la section pour NEd = {0:0} kN : MRd,x = {1:0.0} kN.m, MRd,y = {2:0.0} kN.m",
                nEd, mRdX, mRdY));

            // --- Interaction biaxiale (EC2 5.8.9(4)) ---
            double relative = nEd / check.AxialResistanceKn;
            check.BiaxialExponent = BiaxialExponent(relative);
            double utilisation = Math.Pow(mEdX / Math.Max(mRdX, 1e-9), check.BiaxialExponent)
                                 + Math.Pow(mEdY / Math.Max(mRdY, 1e-9), check.BiaxialExponent);

            check.Utilisation = utilisation;
            check.Passes = utilisation <= 1.0;
            check.Notes.Add(string.Format(
                "EC2 5.8.9(4) : (MEdx/MRdx)^a + (MEdy/MRdy)^a = ({0:0.0}/{1:0.0})^{4:0.00} + " +
                "({2:0.0}/{3:0.0})^{4:0.00} = {5:0.00} {6} 1,00",
                mEdX, mRdX, mEdY, mRdY, check.BiaxialExponent, utilisation,
                check.Passes ? "<=" : ">"));
            return check;
        }

        /// <summary>Moment du second ordre par la methode de la courbure nominale (kN.m).</summary>
        private double SecondOrderMoment(double axialLoadKn, double bucklingLengthMm, double heightMm,
                                         double slenderness, double relativeAxial, double mechanicalRatio,
                                         bool aboutX)
        {
            BendingSection section = BuildSection(aboutX);
            double effectiveDepth = Math.Max(section.EffectiveDepth, 0.6 * heightMm);

            double strainYd = _fyd / ElasticModulusSteel;
            double curvature0 = strainYd / (0.45 * effectiveDepth);           // 1/r0

            double nu = 1.0 + mechanicalRatio;
            double kr = (nu - relativeAxial) / (nu - 0.4);
            if (kr > 1.0) kr = 1.0;
            if (kr < 0.0) kr = 0.0;

            double beta = 0.35 + _input.ConcreteStrengthMPa / 200.0 - slenderness / 150.0;
            double kPhi = 1.0 + beta * _input.CreepCoefficient;
            if (kPhi < 1.0) kPhi = 1.0;

            double curvature = kr * kPhi * curvature0;
            double eccentricity = curvature * bucklingLengthMm * bucklingLengthMm / 10.0;  // c = 10
            return axialLoadKn * eccentricity / 1000.0;
        }

        /// <summary>Exposant a de l'interaction biaxiale, interpole selon NEd/NRd.</summary>
        private static double BiaxialExponent(double relativeAxial)
        {
            if (relativeAxial <= 0.1) return 1.0;
            if (relativeAxial >= 1.0) return 2.0;
            if (relativeAxial <= 0.7)
            {
                return 1.0 + 0.5 * (relativeAxial - 0.1) / 0.6;
            }
            return 1.5 + 0.5 * (relativeAxial - 0.7) / 0.3;
        }
    }
}
