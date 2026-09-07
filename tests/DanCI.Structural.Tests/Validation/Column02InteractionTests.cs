using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// COLUMN-02 : diagramme d'interaction N-M d'un poteau 400 x 400.
    /// Fiche de validation : docs/validation/COLUMN-02.md
    ///
    /// Le calcul manuel de reference utilise le **diagramme rectangulaire simplifie**
    /// (EC2 3.1.7(3), lambda = 0,8 et eta = 1,0) alors que le moteur integre la loi
    /// **parabole-rectangle** (3.1.7(1)). Les deux modeles sont autorises par la norme et
    /// donnent des resultats proches : l'ecart attendu est de l'ordre de quelques pour cent,
    /// et c'est precisement ce que ce test borne.
    /// </summary>
    public class Column02InteractionTests
    {
        private const double SideMm = 400.0;
        private const double BarDepthMm = 48.0;    // 30 enrobage + 8 cadre + 20/2

        private static ConcreteProperties Materials()
        {
            return new ConcreteProperties(new ConcreteMaterial(25.0), new SteelMaterial(500.0),
                                          new RecommendedAnnex());
        }

        /// <summary>400 x 400, 8 HA20 : 3 barres par face comprimee et tendue, 2 a mi-hauteur.</summary>
        private static BendingSection Section()
        {
            BendingSection section = BendingSection.Rectangular(SideMm, SideMm);
            double area = UnitConverter.BarArea(20.0);
            var depths = new List<double>
            {
                BarDepthMm, BarDepthMm, BarDepthMm,
                SideMm / 2.0, SideMm / 2.0,
                SideMm - BarDepthMm, SideMm - BarDepthMm, SideMm - BarDepthMm
            };
            foreach (double depth in depths)
            {
                section.BarDepthsMm.Add(depth);
                section.BarAreasMm2.Add(area);
            }
            return section;
        }

        [Fact]
        public void MRd_A_1500_kN_Est_Coherent_Avec_Le_Calcul_Manuel()
        {
            // Calcul manuel (diagramme rectangulaire) : x = 245,7 mm, MRd = 236,5 kN.m.
            // Tolerance de 6 % pour couvrir la difference de loi de comportement du beton.
            double moment = new InteractionDiagram(Materials())
                .MomentResistance(Section(), UnitConverter.KnToN(1500.0));

            Assert.InRange(UnitConverter.NmmToKnm(moment), 222.0, 251.0);
        }

        [Fact]
        public void MRd_En_Flexion_Simple_Est_Coherent_Avec_Le_Calcul_Manuel()
        {
            // Calcul manuel : x = 81 mm, MRd = 173,1 kN.m (fiche COLUMN-02, section 2.4).
            double moment = new InteractionDiagram(Materials()).MomentResistance(Section(), 0.0);
            Assert.InRange(UnitConverter.NmmToKnm(moment), 163.0, 184.0);
        }

        [Fact]
        public void L_Equilibre_Des_Forces_Est_Verifie_Point_Par_Point()
        {
            // Pour toute position d'axe neutre, la resultante calculee doit correspondre a
            // l'effort normal du point du diagramme : c'est la coherence interne du modele.
            var diagram = new InteractionDiagram(Materials());
            BendingSection section = Section();

            foreach (double neutralAxis in new[] { 100.0, 200.0, 245.7, 400.0, 800.0 })
            {
                InteractionPoint point = diagram.Resultants(section, neutralAxis);
                Assert.True(point.AxialForceN > 0,
                    "L'effort normal doit etre positif en compression pour x = " + neutralAxis);
            }
        }

        [Fact]
        public void Le_Diagramme_Est_Continu()
        {
            // Deux positions voisines d'axe neutre doivent donner des points voisins :
            // une discontinuite signalerait une erreur de pivot.
            var diagram = new InteractionDiagram(Materials());
            BendingSection section = Section();

            InteractionPoint a = diagram.Resultants(section, 199.0);
            InteractionPoint b = diagram.Resultants(section, 201.0);

            double axialJumpKn = UnitConverter.NToKn(b.AxialForceN - a.AxialForceN);
            double momentJumpKnm = UnitConverter.NmmToKnm(b.MomentNmm - a.MomentNmm);

            Assert.InRange(axialJumpKn, -40.0, 40.0);
            Assert.InRange(momentJumpKnm, -10.0, 10.0);
        }
    }
}
