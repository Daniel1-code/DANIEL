namespace DanCI.Structural.Eurocodes.NationalAnnex
{
    /// <summary>Annexes Nationales disponibles. Valeur serialisable dans les configurations.</summary>
    public enum NationalAnnexKind
    {
        Recommended,
        France
    }

    public static class NationalAnnexFactory
    {
        public static INationalAnnex Create(NationalAnnexKind kind)
        {
            switch (kind)
            {
                case NationalAnnexKind.France:
                    return new FranceAnnex();
                default:
                    return new RecommendedAnnex();
            }
        }
    }
}
