using System.Collections.Generic;

namespace ArmaturesPoteaux.Core
{
    public enum DesignCodeKind
    {
        Eurocode2,
        Aci318
    }

    /// <summary>
    /// Parametres saisis par l'utilisateur et appliques a tous les poteaux selectionnes.
    /// </summary>
    public class DesignInput
    {
        public DesignCodeKind Code { get; set; }

        /// <summary>Resistance caracteristique du beton f_ck (MPa) - f'_c pour l'ACI.</summary>
        public double ConcreteStrengthMPa { get; set; }

        /// <summary>Limite d'elasticite de l'acier f_yk (MPa).</summary>
        public double SteelStrengthMPa { get; set; }

        /// <summary>Effort normal de calcul N_Ed (kN). 0 = non renseigne.</summary>
        public double AxialLoadKn { get; set; }

        /// <summary>Enrobage nominal c_nom (mm), mesure jusqu'au nu exterieur des cadres.</summary>
        public double CoverMm { get; set; }

        /// <summary>Diametre maximal du granulat d_g (mm), pour l'espacement libre minimal.</summary>
        public double AggregateSizeMm { get; set; }

        /// <summary>Taux d'armature vise (%) si superieur au minimum reglementaire.</summary>
        public double TargetRatioPercent { get; set; }

        /// <summary>Zone sismique : allonge les zones critiques et resserre les cadres.</summary>
        public bool Seismic { get; set; }

        public bool AutoLongitudinalDiameter { get; set; }
        public double ForcedLongitudinalDiameterMm { get; set; }

        public bool AutoBarCount { get; set; }
        /// <summary>Nombre de barres sur une face parallele a X (angles compris).</summary>
        public int ForcedBarsAlongX { get; set; }
        /// <summary>Nombre de barres sur une face parallele a Y (angles compris).</summary>
        public int ForcedBarsAlongY { get; set; }
        /// <summary>Nombre total de barres pour une section circulaire.</summary>
        public int ForcedCircularBarCount { get; set; }

        public bool AutoTransverse { get; set; }
        public double ForcedStirrupDiameterMm { get; set; }
        public double ForcedSpacingMm { get; set; }

        /// <summary>Cree des zones critiques (cadres resserres) en pied et en tete.</summary>
        public bool UseCriticalZones { get; set; }

        /// <summary>Ajoute des epingles quand des barres intermediaires ne sont pas tenues.</summary>
        public bool AddCrossTies { get; set; }

        /// <summary>Distance du premier cadre au-dessus du pied du poteau (mm).</summary>
        public double FirstStirrupOffsetMm { get; set; }

        /// <summary>Retrait des barres longitudinales par rapport au pied du poteau (mm).</summary>
        public double BottomOffsetMm { get; set; }

        /// <summary>Depassement des barres au-dessus du poteau (mm). -1 = longueur de recouvrement.</summary>
        public double TopExtensionMm { get; set; }

        // --- Verification de resistance (flexion composee) ---

        /// <summary>Lance la verification N-M et ajuste le ferraillage tant qu'elle ne passe pas.</summary>
        public bool VerifyCapacity { get; set; }

        /// <summary>Moment de calcul autour de l'axe local X (kN.m).</summary>
        public double MomentAboutXKnm { get; set; }

        /// <summary>Moment de calcul autour de l'axe local Y (kN.m).</summary>
        public double MomentAboutYKnm { get; set; }

        /// <summary>Coefficient de longueur de flambement : l0 = coefficient x hauteur du poteau.</summary>
        public double BucklingFactor { get; set; }

        /// <summary>Coefficient de fluage effectif phi_ef utilise pour le second ordre.</summary>
        public double CreepCoefficient { get; set; }

        public DesignInput()
        {
            Code = DesignCodeKind.Eurocode2;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;
            AxialLoadKn = 0.0;
            CoverMm = 30.0;
            AggregateSizeMm = 20.0;
            TargetRatioPercent = 1.0;
            Seismic = false;
            AutoLongitudinalDiameter = true;
            ForcedLongitudinalDiameterMm = 16.0;
            AutoBarCount = true;
            ForcedBarsAlongX = 3;
            ForcedBarsAlongY = 3;
            ForcedCircularBarCount = 6;
            AutoTransverse = true;
            ForcedStirrupDiameterMm = 8.0;
            ForcedSpacingMm = 200.0;
            UseCriticalZones = true;
            AddCrossTies = true;
            FirstStirrupOffsetMm = 50.0;
            BottomOffsetMm = 0.0;
            TopExtensionMm = -1.0;
            VerifyCapacity = false;
            MomentAboutXKnm = 0.0;
            MomentAboutYKnm = 0.0;
            BucklingFactor = 1.0;
            CreepCoefficient = 2.0;
        }

        /// <summary>Diametres de barres proposes par le dimensionnement automatique.</summary>
        public static readonly double[] LongitudinalDiameters = { 8, 10, 12, 14, 16, 20, 25, 32, 40 };

        /// <summary>Diametres disponibles pour les cadres et epingles.</summary>
        public static readonly double[] TransverseDiameters = { 6, 8, 10, 12 };

        public DesignInput Clone()
        {
            return (DesignInput)MemberwiseClone();
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (ConcreteStrengthMPa < 12 || ConcreteStrengthMPa > 90)
                errors.Add("La resistance du beton doit etre comprise entre 12 et 90 MPa.");
            if (SteelStrengthMPa < 200 || SteelStrengthMPa > 700)
                errors.Add("La limite d'elasticite de l'acier doit etre comprise entre 200 et 700 MPa.");
            if (CoverMm < 10 || CoverMm > 120)
                errors.Add("L'enrobage doit etre compris entre 10 et 120 mm.");
            if (TargetRatioPercent < 0 || TargetRatioPercent > 4)
                errors.Add("Le taux d'armature vise doit etre compris entre 0 et 4 %.");
            if (!AutoBarCount && (ForcedBarsAlongX < 2 || ForcedBarsAlongY < 2))
                errors.Add("Il faut au moins 2 barres par face pour une section rectangulaire.");
            if (!AutoBarCount && ForcedCircularBarCount < 6)
                errors.Add("Il faut au moins 6 barres pour une section circulaire.");
            if (!AutoTransverse && ForcedSpacingMm < 40)
                errors.Add("L'espacement des cadres doit etre d'au moins 40 mm.");
            if (BucklingFactor < 0.3 || BucklingFactor > 4.0)
                errors.Add("Le coefficient de longueur de flambement doit etre compris entre 0,3 et 4.");
            if (CreepCoefficient < 0 || CreepCoefficient > 4)
                errors.Add("Le coefficient de fluage doit etre compris entre 0 et 4.");
            if (VerifyCapacity && AxialLoadKn <= 0)
                errors.Add("La verification de resistance demande un effort normal NEd superieur a 0.");
            return errors;
        }
    }
}
