using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Stair;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des volees d'escalier.</summary>
    public sealed class StairRowViewModel
    {
        public StairDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public StairRowViewModel(StairDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Stair.Name; } }

        public string Marche
        {
            get
            {
                return string.Format("{0} x {1:0}/{2:0}", Result.Stair.RiserCount,
                                     Result.Stair.RiserHeightMm, Result.Stair.TreadDepthMm);
            }
        }

        public string Pente { get { return string.Format("{0:0.0}", Result.Stair.SlopeAngleDegrees); } }

        public string Paillasse
        {
            get { return string.Format("{0:0}", Result.Stair.WaistThicknessMm); }
        }

        public string Portee { get { return string.Format("{0:0}", Result.Stair.SpanMm); } }

        /// <summary>Charge permanente de la volee : c'est elle qu'un calcul naif sous-estime.</summary>
        public string Charge
        {
            get
            {
                return Result.FlightLoad == null
                    ? "-" : string.Format("{0:0.00}", Result.FlightLoad.PermanentKnM2);
            }
        }

        public string Moment { get { return string.Format("{0:0.0}", Result.SpanMomentKnmPerM); } }

        public string NappeInferieure { get { return Result.Reinforcement.BottomMain.Label; } }

        public string Repartition { get { return Result.Reinforcement.BottomTransverse.Label; } }

        public string Chapeaux { get { return Result.Reinforcement.TopLabel; } }

        public string Noeud
        {
            get { return Result.Reinforcement.HasKneeJoint ? "croise" : "-"; }
        }

        public string Poids
        {
            get { return Quantities == null ? "-" : string.Format("{0:0.0}", Quantities.TotalMassKg); }
        }

        public string Utilisation { get { return string.Format("{0:0.00}", Result.MaxUtilization); } }

        /// <summary>
        /// D'ou vient la geometrie de cette volee. Sur un lot, c'est la colonne qui dit
        /// lesquelles reposent encore sur une valeur par defaut — et une portee supposee
        /// n'est pas une portee.
        /// </summary>
        public string Geometrie
        {
            get
            {
                StairGeometryProvenance origin = Result.Stair.Provenance;
                if (origin == null) return "supposee";

                bool spanAssumed = !origin.IsEstablished(StairDimension.Support)
                    || (Result.Stair.SpanKind == StairSpanKind.AlongFlightWithLanding
                        && !origin.IsEstablished(StairDimension.LandingSpan));
                if (spanAssumed) return "appui suppose";
                if (!origin.IsEstablished(StairDimension.WaistThickness)) return "paillasse supposee";

                int assumed = origin.Assumptions().Count();
                return assumed == 0 ? "lue" : "lue (partiel)";
            }
        }

        public string Etat { get { return Result.Status; } }
    }
}
