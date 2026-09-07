using System;
using System.Collections.Generic;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>
    /// Section vue depuis une direction de flexion : hauteur mesuree depuis la fibre la plus
    /// comprimee, largeur variable, et positions des barres exprimees dans le meme repere.
    /// </summary>
    public sealed class BendingSection
    {
        /// <summary>Hauteur totale mesuree depuis la fibre la plus comprimee (mm).</summary>
        public double HeightMm { get; set; }

        /// <summary>Largeur de la section a la profondeur y (mm).</summary>
        public Func<double, double> WidthAt { get; set; }

        /// <summary>Profondeur de chaque barre depuis la fibre comprimee (mm).</summary>
        public List<double> BarDepthsMm { get; private set; }

        /// <summary>Aire de chaque barre (mm2), dans le meme ordre.</summary>
        public List<double> BarAreasMm2 { get; private set; }

        public BendingSection()
        {
            BarDepthsMm = new List<double>();
            BarAreasMm2 = new List<double>();
        }

        /// <summary>Section rectangulaire de largeur constante.</summary>
        public static BendingSection Rectangular(double heightMm, double widthMm)
        {
            return new BendingSection { HeightMm = heightMm, WidthAt = y => widthMm };
        }

        /// <summary>Section circulaire, largeur = corde du cercle a la profondeur y.</summary>
        public static BendingSection Circular(double diameterMm)
        {
            double radius = diameterMm / 2.0;
            return new BendingSection
            {
                HeightMm = diameterMm,
                WidthAt = y =>
                {
                    double dy = y - radius;
                    double inside = radius * radius - dy * dy;
                    return inside > 0 ? 2.0 * Math.Sqrt(inside) : 0.0;
                }
            };
        }

        /// <summary>Profondeur de la nappe la plus eloignee de la fibre comprimee (mm).</summary>
        public double EffectiveDepthMm
        {
            get
            {
                double deepest = 0.0;
                foreach (double depth in BarDepthsMm) deepest = Math.Max(deepest, depth);
                return deepest;
            }
        }

        public double TotalSteelAreaMm2
        {
            get
            {
                double total = 0.0;
                foreach (double area in BarAreasMm2) total += area;
                return total;
            }
        }
    }

    /// <summary>Un point du diagramme d'interaction : effort normal (N) et moment (N.mm).</summary>
    public readonly struct InteractionPoint
    {
        public double AxialForceN { get; }
        public double MomentNmm { get; }

        public InteractionPoint(double axialForceN, double momentNmm)
        {
            AxialForceN = axialForceN;
            MomentNmm = momentNmm;
        }
    }

    /// <summary>
    /// Diagramme d'interaction N-M d'une section en beton arme, obtenu par integration des
    /// contraintes sur la hauteur : loi parabole-rectangle pour le beton (EN 1992-1-1 3.1.7),
    /// loi elastoplastique parfaite pour l'acier (3.2.7), et regle des trois pivots pour la
    /// distribution des deformations (6.1, figure 6.1).
    /// </summary>
    public sealed class InteractionDiagram
    {
        /// <summary>Deformation ultime de l'acier au pivot A, 0,045 pour une classe B ou C.</summary>
        public const double SteelUltimateStrain = 0.045;

        private readonly ConcreteProperties _materials;
        private readonly int _stripCount;

        public InteractionDiagram(ConcreteProperties materials, int stripCount = 240)
        {
            _materials = materials;
            _stripCount = stripCount;
        }

        /// <summary>
        /// Deformation a la profondeur y pour une position d'axe neutre x, compression positive.
        /// Pivot A : allongement de l'acier limite a eps_ud. Pivot B : raccourcissement du beton
        /// limite a eps_cu2. Pivot C : compression centree, la droite passe par eps_c2 a la
        /// profondeur (1 - eps_c2/eps_cu2) h.
        /// </summary>
        public double StrainAt(double neutralAxisMm, double depthMm, double heightMm,
                               double effectiveDepthMm)
        {
            double curvature;
            if (neutralAxisMm <= heightMm)
            {
                curvature = _materials.StrainCu2 / Math.Max(neutralAxisMm, 1e-6);      // pivot B
                if (neutralAxisMm < effectiveDepthMm)
                {
                    double pivotA = SteelUltimateStrain / (effectiveDepthMm - neutralAxisMm);
                    curvature = Math.Min(curvature, pivotA);                            // pivot A
                }
            }
            else
            {
                double pivotDepth = heightMm * (1.0 - _materials.StrainC2 / _materials.StrainCu2);
                curvature = _materials.StrainC2 / Math.Max(neutralAxisMm - pivotDepth, 1e-6); // pivot C
            }
            return curvature * (neutralAxisMm - depthMm);
        }

        /// <summary>
        /// Effort normal (N) et moment autour du centre de gravite geometrique (N.mm)
        /// resistants pour une position d'axe neutre donnee.
        /// </summary>
        public InteractionPoint Resultants(BendingSection section, double neutralAxisMm)
        {
            double height = section.HeightMm;
            double effectiveDepth = section.EffectiveDepthMm;
            double stripHeight = height / _stripCount;
            double normalForce = 0.0;
            double moment = 0.0;

            for (int i = 0; i < _stripCount; i++)
            {
                double y = (i + 0.5) * stripHeight;
                double strain = StrainAt(neutralAxisMm, y, height, effectiveDepth);
                double force = _materials.ConcreteStress(strain) * section.WidthAt(y) * stripHeight;
                normalForce += force;
                moment += force * (height / 2.0 - y);
            }

            for (int i = 0; i < section.BarDepthsMm.Count; i++)
            {
                double y = section.BarDepthsMm[i];
                double strain = StrainAt(neutralAxisMm, y, height, effectiveDepth);
                // La barre comprimee remplace du beton : on retranche la contrainte du beton.
                double stress = _materials.SteelStress(strain) - _materials.ConcreteStress(strain);
                double force = section.BarAreasMm2[i] * stress;
                normalForce += force;
                moment += force * (height / 2.0 - y);
            }

            return new InteractionPoint(normalForce, moment);
        }

        /// <summary>Construit le diagramme complet, de la flexion simple a la compression centree.</summary>
        public List<InteractionPoint> Build(BendingSection section, int steps = 220)
        {
            var points = new List<InteractionPoint>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                // Balayage geometrique : fin pres de la flexion simple, large en compression.
                double ratio = (double)i / steps;
                double neutralAxis = section.HeightMm * (0.02 * Math.Pow(2000.0, ratio));
                points.Add(Resultants(section, neutralAxis));
            }
            return points;
        }

        /// <summary>
        /// Moment resistant (N.mm) pour l'effort normal donne (N), par interpolation sur le
        /// diagramme. Renvoie -1 si l'effort normal sort du diagramme, c'est-a-dire si la
        /// section ne peut pas equilibrer cet effort.
        /// </summary>
        public double MomentResistance(BendingSection section, double axialForceN, int steps = 220)
        {
            List<InteractionPoint> points = Build(section, steps);
            double best = -1.0;

            for (int i = 1; i < points.Count; i++)
            {
                double previousN = points[i - 1].AxialForceN;
                double currentN = points[i].AxialForceN;
                if ((previousN - axialForceN) * (currentN - axialForceN) > 0.0) continue;
                if (Math.Abs(currentN - previousN) < 1e-9) continue;

                double t = (axialForceN - previousN) / (currentN - previousN);
                double moment = points[i - 1].MomentNmm + t * (points[i].MomentNmm - points[i - 1].MomentNmm);
                best = Math.Max(best, moment);
            }

            return best;
        }

        /// <summary>
        /// Effort normal resistant de la section entierement comprimee (N) :
        /// N_Rd = A_c f_cd + A_s f_yd.
        /// </summary>
        public double AxialResistance(double grossAreaMm2, double steelAreaMm2)
        {
            return grossAreaMm2 * _materials.Fcd + steelAreaMm2 * _materials.Fyd;
        }
    }
}
