using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Wall;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des voiles.</summary>
    public sealed class WallRowViewModel
    {
        public WallDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public WallRowViewModel(WallDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Wall.Name; } }

        public string Epaisseur { get { return string.Format("{0:0}", Result.Wall.ThicknessMm); } }

        public string Longueur { get { return string.Format("{0:0}", Result.Wall.LengthMm); } }

        public string Hauteur { get { return string.Format("{0:0}", Result.Wall.ClearHeightMm); } }

        public string Elancement { get { return string.Format("{0:0.0}", Result.SlendernessRatio); } }

        public string SecondOrdre
        {
            get
            {
                if (Result.SecondOrder == null) return "-";
                return Result.SecondOrder.Required
                    ? string.Format("{0:0} mm", Result.SecondOrder.SecondOrderEccentricityMm)
                    : "neglige";
            }
        }

        public string Verticaux { get { return Result.Reinforcement.VerticalPerFace.Label; } }

        public string Horizontaux { get { return Result.Reinforcement.HorizontalPerFace.Label; } }

        public string Rives { get { return Result.Reinforcement.EdgeLabel; } }

        public string Poids
        {
            get { return Quantities == null ? "-" : string.Format("{0:0.0}", Quantities.TotalMassKg); }
        }

        public string Utilisation { get { return string.Format("{0:0.00}", Result.MaxUtilization); } }

        public string Etat { get { return Result.Status; } }
    }
}
