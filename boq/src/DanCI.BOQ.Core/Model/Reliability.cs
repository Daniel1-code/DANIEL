namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Fiabilite d'une quantite. L'ordre des valeurs est significatif : une ligne prend la
    /// fiabilite la PLUS FAIBLE de ses contributions (Missing &gt; Estimated &gt; Measured).
    /// </summary>
    public enum Reliability
    {
        /// <summary>✅ Mesure sur la geometrie reelle ou un parametre calcule fiable.</summary>
        Measured = 0,

        /// <summary>✍ Saisi manuellement (forfait) : ni mesure, ni estime par le moteur.</summary>
        Manual = 1,

        /// <summary>⚠️ Estime : ratio, hypothese, valeur par defaut. Toujours accompagne de l'hypothese.</summary>
        Estimated = 2,

        /// <summary>❌ Manquant : la quantite n'a pas pu etre determinee.</summary>
        Missing = 3
    }

    public static class Reliabilities
    {
        public static string Label(this Reliability reliability)
        {
            switch (reliability)
            {
                case Reliability.Measured: return "✅ mesuré";
                case Reliability.Manual: return "✍ forfait saisi";
                case Reliability.Estimated: return "⚠️ estimé";
                default: return "❌ manquant";
            }
        }

        public static Reliability Worst(Reliability a, Reliability b)
        {
            return a >= b ? a : b;
        }
    }
}
