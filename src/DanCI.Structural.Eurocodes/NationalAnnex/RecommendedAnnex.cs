namespace DanCI.Structural.Eurocodes.NationalAnnex
{
    /// <summary>
    /// Valeurs recommandees de l'EN 1992-1-1:2004+A1:2014, applicables en l'absence
    /// d'Annexe Nationale. C'est la base de reference du moteur.
    /// </summary>
    public class RecommendedAnnex : INationalAnnex
    {
        public virtual string Name { get { return "Valeurs recommandees EN 1992-1-1"; } }

        public virtual double GammaC { get { return 1.5; } }
        public virtual double GammaS { get { return 1.15; } }
        public virtual double AlphaCc { get { return 1.0; } }
        public virtual double AlphaCt { get { return 1.0; } }

        public virtual double ColumnMinSteelLoadFactor { get { return 0.10; } }
        public virtual double ColumnMinSteelAreaRatio { get { return 0.002; } }
        public virtual double ColumnMaxSteelAreaRatio { get { return 0.04; } }
        public virtual double ColumnMinLongitudinalDiameterMm { get { return 8.0; } }
        public virtual double MinTransverseDiameterMm { get { return 6.0; } }
        public virtual double TieSpacingBarDiameterFactor { get { return 20.0; } }
        public virtual double TieSpacingMaximumMm { get { return 400.0; } }
        public virtual double CriticalZoneSpacingFactor { get { return 0.6; } }
        public virtual double MaxDistanceToRestrainedBarMm { get { return 150.0; } }

        public virtual double ClearSpacingBarFactor { get { return 1.0; } }
        public virtual double ClearSpacingAggregateAdditionMm { get { return 5.0; } }
        public virtual double ClearSpacingMinimumMm { get { return 20.0; } }

        public virtual double LapCoefficientAlpha6 { get { return 1.5; } }
    }

    /// <summary>
    /// Annexe Nationale francaise (NF EN 1992-1-1/NA). Les valeurs qui different des valeurs
    /// recommandees seront redefinies ici ; celles qui coincident sont heritees.
    /// Classe fournie pour que l'architecture soit prete : a completer et a valider avant
    /// d'etre proposee a l'utilisateur.
    /// </summary>
    public sealed class FranceAnnex : RecommendedAnnex
    {
        public override string Name { get { return "Annexe Nationale francaise (a completer)"; } }
    }
}
