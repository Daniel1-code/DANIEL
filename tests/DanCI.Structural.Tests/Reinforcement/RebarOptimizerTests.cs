using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Eurocodes.Detailing;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using Xunit;

namespace DanCI.Structural.Tests.Reinforcement
{
    /// <summary>
    /// Choix de la disposition des barres. Le point verifie ici n'est pas seulement que la
    /// solution « passe », mais qu'elle est raisonnable : pas de sur-ferraillage grossier,
    /// espacements libres respectes, nombre de barres constructible.
    /// </summary>
    public class RebarOptimizerTests
    {
        private static ColumnData Rectangular(double width, double depth, double height = 3000.0)
        {
            return new ColumnData
            {
                Name = "Poteau d'essai",
                Shape = SectionShape.Rectangular,
                WidthMm = width,
                DepthMm = depth,
                HeightMm = height
            };
        }

        private static RebarOptimizer Optimizer(ColumnLayoutOptions options = null)
        {
            return new RebarOptimizer(new Ec2ColumnDetailing(new RecommendedAnnex()),
                                      options ?? new ColumnLayoutOptions());
        }

        [Fact]
        public void La_Solution_Atteint_La_Section_Visee_Sans_Exces_Grossier()
        {
            ColumnData column = Rectangular(300.0, 500.0);   // Ac = 150 000 mm2
            double target = 0.01 * column.GrossAreaMm2;      // 1 % = 1 500 mm2
            double maximum = 0.04 * column.GrossAreaMm2;

            ColumnBarLayout layout = Optimizer().Optimize(column, target, maximum);

            Assert.NotNull(layout);
            Assert.True(layout.SteelAreaMm2 >= target,
                "La section fournie doit atteindre la section visee.");
            Assert.True(layout.SteelAreaMm2 <= 1.45 * target,
                "L'exces d'acier doit rester raisonnable, ici " + layout.SteelAreaMm2 + " mm2.");
            Assert.True(layout.SteelAreaMm2 <= maximum);
        }

        [Fact]
        public void Les_Espacements_Libres_Sont_Respectes()
        {
            ColumnData column = Rectangular(250.0, 250.0);
            ColumnBarLayout layout = Optimizer().Optimize(column, 0.02 * column.GrossAreaMm2,
                                                          0.04 * column.GrossAreaMm2);

            Assert.NotNull(layout);
            double minClear = new Ec2ColumnDetailing(new RecommendedAnnex())
                .MinClearBarSpacingMm(layout.DiameterMm, 20.0);
            Assert.True(layout.ClearSpacingMm >= minClear,
                "Espacement libre " + layout.ClearSpacingMm + " mm < minimum " + minClear + " mm.");
        }

        [Fact]
        public void Le_Nombre_De_Barres_Reste_Constructible()
        {
            ColumnData column = Rectangular(400.0, 400.0);
            ColumnBarLayout layout = Optimizer().Optimize(column, 0.01 * column.GrossAreaMm2,
                                                          0.04 * column.GrossAreaMm2);

            Assert.NotNull(layout);
            Assert.True(layout.TotalBars >= 4, "Au moins 4 barres (EC2 9.5.2(4)).");
            Assert.Equal(2 * (layout.CountAlongX + layout.CountAlongY) - 4, layout.TotalBars);
        }

        [Fact]
        public void La_Repartition_Suit_Les_Proportions_De_La_Section()
        {
            // Poteau tres allonge : il doit porter plus de barres sur la grande face.
            ColumnData column = Rectangular(250.0, 700.0);
            ColumnBarLayout layout = Optimizer().Optimize(column, 0.015 * column.GrossAreaMm2,
                                                          0.04 * column.GrossAreaMm2);

            Assert.NotNull(layout);
            Assert.True(layout.CountAlongY >= layout.CountAlongX,
                "La grande dimension doit recevoir au moins autant de barres.");
        }

        [Fact]
        public void Le_Diametre_Impose_Est_Respecte()
        {
            var options = new ColumnLayoutOptions { AutoDiameter = false, ForcedDiameterMm = 25.0 };
            ColumnData column = Rectangular(400.0, 400.0);

            ColumnBarLayout layout = Optimizer(options).Optimize(column, 0.01 * column.GrossAreaMm2,
                                                                 0.04 * column.GrossAreaMm2);

            Assert.NotNull(layout);
            Assert.Equal(25.0, layout.DiameterMm, 6);
        }

        [Fact]
        public void Le_Diametre_De_Cadre_Suit_La_Regle_Phi_Sur_Quatre()
        {
            RebarOptimizer optimizer = Optimizer();
            Assert.Equal(6.0, optimizer.TransverseDiameterFor(12.0), 6);   // max(6 ; 3)  -> 6
            Assert.Equal(6.0, optimizer.TransverseDiameterFor(20.0), 6);   // max(6 ; 5)  -> 6
            Assert.Equal(8.0, optimizer.TransverseDiameterFor(32.0), 6);   // max(6 ; 8)  -> 8
            Assert.Equal(10.0, optimizer.TransverseDiameterFor(40.0), 6);  // max(6 ; 10) -> 10
        }

        [Fact]
        public void Une_Section_Trop_Petite_Ne_Renvoie_Aucune_Solution()
        {
            // 150 x 150 avec 4 % d'acier vise : aucune disposition ne tient dans la section.
            ColumnData column = Rectangular(150.0, 150.0);
            ColumnBarLayout layout = Optimizer().Optimize(column, 0.04 * column.GrossAreaMm2,
                                                          0.04 * column.GrossAreaMm2);
            Assert.Null(layout);
        }

        [Fact]
        public void La_Section_Circulaire_Porte_Au_Moins_Six_Barres()
        {
            var column = new ColumnData
            {
                Name = "Poteau rond",
                Shape = SectionShape.Circular,
                DiameterMm = 500.0,
                HeightMm = 3000.0
            };

            ColumnBarLayout layout = Optimizer().Optimize(column, 0.01 * column.GrossAreaMm2,
                                                          0.04 * column.GrossAreaMm2);

            Assert.NotNull(layout);
            Assert.True(layout.TotalBars >= 6, "EC2 9.5.2(4) : 6 barres minimum en section circulaire.");
        }
    }
}
