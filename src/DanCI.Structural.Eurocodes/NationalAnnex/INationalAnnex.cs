namespace DanCI.Structural.Eurocodes.NationalAnnex
{
    /// <summary>
    /// Parametres determines au niveau national (NDP). Toute valeur susceptible d'etre
    /// modifiee par une Annexe Nationale passe par cette interface : aucun coefficient
    /// reglementaire ne doit etre code en dur dans un module de calcul.
    /// </summary>
    public interface INationalAnnex
    {
        string Name { get; }

        // --- EN 1992-1-1, coefficients partiels et de comportement ---

        /// <summary>Coefficient partiel du beton gamma_c (2.4.2.4).</summary>
        double GammaC { get; }

        /// <summary>Coefficient partiel de l'acier gamma_s (2.4.2.4).</summary>
        double GammaS { get; }

        /// <summary>Coefficient tenant compte des effets de longue duree alpha_cc (3.1.6).</summary>
        double AlphaCc { get; }

        /// <summary>Coefficient alpha_ct pour la traction (3.1.6).</summary>
        double AlphaCt { get; }

        // --- EN 1992-1-1, article 9.5 : poteaux ---

        /// <summary>Coefficient de A_s,min lie a l'effort normal, 0,10 recommande (9.5.2(2)).</summary>
        double ColumnMinSteelLoadFactor { get; }

        /// <summary>Taux minimal d'acier rapporte a A_c, 0,002 recommande (9.5.2(2)).</summary>
        double ColumnMinSteelAreaRatio { get; }

        /// <summary>Taux maximal d'acier hors recouvrement, 0,04 recommande (9.5.2(3)).</summary>
        double ColumnMaxSteelAreaRatio { get; }

        /// <summary>Diametre minimal des barres longitudinales, 8 mm recommande (9.5.2(1)).</summary>
        double ColumnMinLongitudinalDiameterMm { get; }

        /// <summary>Diametre minimal des armatures transversales, 6 mm recommande (9.5.3(1)).</summary>
        double MinTransverseDiameterMm { get; }

        /// <summary>Multiple du diametre longitudinal limitant l'espacement, 20 recommande (9.5.3(3)).</summary>
        double TieSpacingBarDiameterFactor { get; }

        /// <summary>Espacement maximal absolu des cadres, 400 mm recommande (9.5.3(3)).</summary>
        double TieSpacingMaximumMm { get; }

        /// <summary>Facteur de reduction de l'espacement en zone critique, 0,6 recommande (9.5.3(4)).</summary>
        double CriticalZoneSpacingFactor { get; }

        /// <summary>Distance maximale a une barre tenue, 150 mm recommande (9.5.3(6)).</summary>
        double MaxDistanceToRestrainedBarMm { get; }

        // --- EN 1992-1-1, articles 8.2 et 8.7 : espacements et recouvrements ---

        /// <summary>Coefficient k1 de l'espacement libre minimal, 1,0 recommande (8.2(2)).</summary>
        double ClearSpacingBarFactor { get; }

        /// <summary>Terme k2 ajoute au diametre du granulat, 5 mm recommande (8.2(2)).</summary>
        double ClearSpacingAggregateAdditionMm { get; }

        /// <summary>Espacement libre minimal absolu, 20 mm recommande (8.2(2)).</summary>
        double ClearSpacingMinimumMm { get; }

        /// <summary>Coefficient alpha_6 de recouvrement, 1,5 quand plus de 50 % des barres
        /// sont recouvertes au meme endroit (8.7.3, tableau 8.3).</summary>
        double LapCoefficientAlpha6 { get; }
    }
}
