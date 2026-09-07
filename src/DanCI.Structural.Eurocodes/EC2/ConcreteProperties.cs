using System;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>
    /// Proprietes de calcul du beton et de l'acier selon l'EN 1992-1-1:2004, article 3.1
    /// (tableau 3.1) et article 3.2. Toutes les valeurs sont en MPa.
    /// </summary>
    public sealed class ConcreteProperties
    {
        private readonly INationalAnnex _annex;

        public ConcreteProperties(ConcreteMaterial concrete, SteelMaterial steel, INationalAnnex annex)
        {
            Concrete = concrete;
            Steel = steel;
            _annex = annex;
        }

        public ConcreteMaterial Concrete { get; private set; }
        public SteelMaterial Steel { get; private set; }

        public double Fck { get { return Concrete.FckMPa; } }

        /// <summary>Resistance moyenne f_cm = f_ck + 8 (tableau 3.1).</summary>
        public double Fcm { get { return Fck + 8.0; } }

        /// <summary>Resistance moyenne en traction f_ctm (tableau 3.1).</summary>
        public double Fctm
        {
            get
            {
                return Fck <= 50.0
                    ? 0.30 * Math.Pow(Fck, 2.0 / 3.0)
                    : 2.12 * Math.Log(1.0 + Fcm / 10.0);
            }
        }

        /// <summary>Fractile 5 % de la resistance en traction f_ctk,0,05 = 0,7 f_ctm.</summary>
        public double Fctk005 { get { return 0.7 * Fctm; } }

        /// <summary>Resistance de calcul en compression f_cd = alpha_cc f_ck / gamma_c (3.1.6).</summary>
        public double Fcd { get { return _annex.AlphaCc * Fck / _annex.GammaC; } }

        /// <summary>Resistance de calcul en traction f_ctd = alpha_ct f_ctk,0,05 / gamma_c (3.1.6).</summary>
        public double Fctd { get { return _annex.AlphaCt * Fctk005 / _annex.GammaC; } }

        /// <summary>Module secant E_cm = 22 (f_cm/10)^0,3 en GPa, rendu en MPa (tableau 3.1).</summary>
        public double Ecm { get { return 22000.0 * Math.Pow(Fcm / 10.0, 0.3); } }

        /// <summary>Limite d'elasticite de calcul f_yd = f_yk / gamma_s (3.2.7).</summary>
        public double Fyd { get { return Steel.FykMPa / _annex.GammaS; } }

        public double Es { get { return Steel.ElasticModulusMPa; } }

        /// <summary>Deformation au pic eps_c2 du diagramme parabole-rectangle (tableau 3.1).</summary>
        public double StrainC2
        {
            get
            {
                if (Fck <= 50.0) return 0.0020;
                return (2.0 + 0.085 * Math.Pow(Fck - 50.0, 0.53)) / 1000.0;
            }
        }

        /// <summary>Deformation ultime eps_cu2 (tableau 3.1).</summary>
        public double StrainCu2
        {
            get
            {
                if (Fck <= 50.0) return 0.0035;
                return (2.6 + 35.0 * Math.Pow((90.0 - Fck) / 100.0, 4.0)) / 1000.0;
            }
        }

        /// <summary>Exposant n du diagramme parabole-rectangle (tableau 3.1).</summary>
        public double ParabolaExponent
        {
            get
            {
                if (Fck <= 50.0) return 2.0;
                return 1.4 + 23.4 * Math.Pow((90.0 - Fck) / 100.0, 4.0);
            }
        }

        /// <summary>Deformation d'entree en plasticite de l'acier eps_yd = f_yd / E_s.</summary>
        public double SteelYieldStrain { get { return Fyd / Es; } }

        /// <summary>Contrainte du beton (MPa) pour une deformation de compression positive (3.1.7).</summary>
        public double ConcreteStress(double strain)
        {
            if (strain <= 0.0) return 0.0;
            if (strain >= StrainC2) return Fcd;
            return Fcd * (1.0 - Math.Pow(1.0 - strain / StrainC2, ParabolaExponent));
        }

        /// <summary>Contrainte de l'acier (MPa), elastoplastique parfait, compression positive (3.2.7).</summary>
        public double SteelStress(double strain)
        {
            double stress = Es * strain;
            if (stress > Fyd) return Fyd;
            if (stress < -Fyd) return -Fyd;
            return stress;
        }
    }
}
