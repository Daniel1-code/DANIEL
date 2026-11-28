using System;
using System.Collections.Generic;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Corps d'etat d'un lot, pour ranger le BOQ de chaque niveau comme un devis de projet :
    /// GROS ŒUVRE (installation, terrassements, fondations, beton arme, charpente, maconnerie),
    /// puis SECOND ŒUVRE (toiture, menuiseries, revetements-enduits-peinture, plomberie,
    /// electricite, CVC), puis VRD. Un lot peut imposer le sien (« corpsEtat » dans les regles).
    /// </summary>
    public static class Trades
    {
        public const string Demolition = "Démolitions";
        public const string Structure = "Gros œuvre";
        public const string Finishing = "Second œuvre";
        public const string External = "VRD et aménagements extérieurs";
        public const string Unclassified = "Non classés";

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DEM", Demolition },
            { "00", Structure }, { "01", Structure }, { "02", Structure }, { "03", Structure }, { "04", Structure }, { "05", Structure },
            { "06", Finishing }, { "07", Finishing }, { "08", Finishing }, { "09", Finishing }, { "10", Finishing }, { "12", Finishing },
            { "11", External }
        };

        public static string Of(Lot lot) => !string.IsNullOrWhiteSpace(lot?.Trade) ? lot.Trade.Trim() : Of(lot?.Code);

        public static string Of(string lotCode)
        {
            if (string.IsNullOrWhiteSpace(lotCode)) return Unclassified;
            if (Defaults.TryGetValue(lotCode.Trim(), out string t)) return t;
            return lotCode.Trim() == Rules.MappingCatalog.UnclassifiedLotCode ? Unclassified : Finishing;
        }

        public static int Order(string trade)
        {
            if (trade == Demolition) return 0;
            if (trade == Structure) return 1;
            if (trade == Finishing) return 2;
            if (trade == External) return 3;
            if (trade == Unclassified) return 5;
            return 4;
        }
    }
}
