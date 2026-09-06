using System.Collections.Generic;

namespace ArmaturesPoteaux.Core
{
    /// <summary>
    /// Resultat de la verification de resistance en flexion composee, second ordre inclus.
    /// </summary>
    public class CapacityCheck
    {
        /// <summary>La verification a-t-elle ete demandee et menee a son terme ?</summary>
        public bool Performed { get; set; }

        public bool Passes { get; set; }

        /// <summary>Taux de travail : &lt;= 1,00 signifie que la section resiste.</summary>
        public double Utilisation { get; set; }

        public double AxialLoadKn { get; set; }

        /// <summary>Moment de calcul autour de X, minimum reglementaire et 2e ordre inclus (kN.m).</summary>
        public double DesignMomentXKnm { get; set; }
        /// <summary>Moment de calcul autour de Y, minimum reglementaire et 2e ordre inclus (kN.m).</summary>
        public double DesignMomentYKnm { get; set; }

        public double ResistanceMomentXKnm { get; set; }
        public double ResistanceMomentYKnm { get; set; }

        /// <summary>Effort normal resistant de la section entierement comprimee (kN).</summary>
        public double AxialResistanceKn { get; set; }

        public double SlendernessX { get; set; }
        public double SlendernessY { get; set; }
        public double SlendernessLimit { get; set; }
        public bool SecondOrderRequired { get; set; }
        public double SecondOrderMomentXKnm { get; set; }
        public double SecondOrderMomentYKnm { get; set; }
        public double MinimumEccentricityMm { get; set; }

        /// <summary>Exposant a de la formule d'interaction biaxiale (EC2 5.8.9(4)).</summary>
        public double BiaxialExponent { get; set; }

        public List<string> Notes { get; private set; }

        public CapacityCheck()
        {
            Notes = new List<string>();
        }

        public string Label
        {
            get
            {
                if (!Performed) return "-";
                return string.Format("{0} ({1:0.00})", Passes ? "OK" : "NON", Utilisation);
            }
        }
    }
}
