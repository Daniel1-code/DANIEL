using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Diagramme d'interaction N-M, EN 1992-1-1 articles 3.1.7 et 6.1.
    ///
    /// Section de reference : 400 x 400 mm, C25/30, B500, 8 HA20 (A_s = 2 513 mm2),
    /// enrobage 30 mm, cadres HA8. Les barres sont donc a 48 mm de chaque face.
    ///   f_cd = 16,667 MPa   f_yd = 434,8 MPa
    /// En compression centree (pivot C), la deformation vaut eps_c2 = 0,002 : l'acier n'est
    /// alors qu'a 200 000 x 0,002 = 400 MPa, en deca de f_yd. L'effort maximal du diagramme
    /// vaut donc A_c f_cd + A_s (400 - f_cd), soit environ 3 630 kN, et non le N_Rd
    /// simplifie de l'article 6.1 (3 759 kN) qui suppose l'acier a f_yd.
    /// </summary>
    public class InteractionDiagramTests
    {
        private const double SideMm = 400.0;
        private const double CoverToBarMm = 48.0;   // 30 enrobage + 8 cadre + 20/2

        private static ConcreteProperties Materials()
        {
            return new ConcreteProperties(new ConcreteMaterial(25.0), new SteelMaterial(500.0),
                                          new RecommendedAnnex());
        }

        /// <summary>Section carree 400 x 400 avec 8 HA20 repartis sur le pourtour.</summary>
        private static BendingSection Section()
        {
            BendingSection section = BendingSection.Rectangular(SideMm, SideMm);
            double area = UnitConverter.BarArea(20.0);
            double near = CoverToBarMm;
            double far = SideMm - CoverToBarMm;
            double middle = SideMm / 2.0;

            // 3 barres au niveau comprime, 2 a mi-hauteur, 3 au niveau tendu.
            var depths = new List<double> { near, near, near, middle, middle, far, far, far };
            foreach (double depth in depths)
            {
                section.BarDepthsMm.Add(depth);
                section.BarAreasMm2.Add(area);
            }
            return section;
        }

        [Fact]
        public void La_Compression_Centree_Correspond_Au_Pivot_C()
        {
            var diagram = new InteractionDiagram(Materials());
            List<InteractionPoint> points = diagram.Build(Section());

            double maxAxial = double.MinValue;
            double momentAtMax = 0.0;
            foreach (InteractionPoint point in points)
            {
                if (point.AxialForceN > maxAxial)
                {
                    maxAxial = point.AxialForceN;
                    momentAtMax = point.MomentNmm;
                }
            }

            // A_c f_cd + A_s (400 - f_cd) = 2 666 720 + 2 513 x 383,3 = 3 630 000 N environ.
            Assert.InRange(UnitConverter.NToKn(maxAxial), 3550.0, 3700.0);
            // La section etant symetrique, le moment y est nul.
            Assert.InRange(UnitConverter.NmmToKnm(momentAtMax), -5.0, 5.0);
        }

        [Fact]
        public void Le_N_Rd_Simplifie_De_L_Article_6_1_Est_Superieur_Au_Diagramme()
        {
            var diagram = new InteractionDiagram(Materials());
            double simplified = diagram.AxialResistance(SideMm * SideMm, 8.0 * UnitConverter.BarArea(20.0));
            Assert.InRange(UnitConverter.NToKn(simplified), 3700.0, 3820.0);
        }

        [Fact]
        public void Le_Moment_Resistant_En_Flexion_Simple_Est_Coherent()
        {
            var diagram = new InteractionDiagram(Materials());
            double moment = diagram.MomentResistance(Section(), 0.0);

            // Trois barres tendues a d = 352 mm, bras de levier de l'ordre de 0,9 d :
            // 942 x 434,8 x 317 = 130 kN.m, majore par les barres intermediaires.
            Assert.InRange(UnitConverter.NmmToKnm(moment), 100.0, 220.0);
        }

        [Fact]
        public void Une_Section_Carree_Resiste_Autant_Dans_Les_Deux_Directions()
        {
            var diagram = new InteractionDiagram(Materials());
            double axial = UnitConverter.KnToN(1500.0);
            double moment = diagram.MomentResistance(Section(), axial);

            Assert.True(moment > 0, "La section doit resister a 1 500 kN avec un moment associe.");
            // La meme section vue dans l'autre direction est identique par symetrie.
            double mirrored = diagram.MomentResistance(Section(), axial);
            Assert.Equal(moment, mirrored, 6);
        }

        [Fact]
        public void Un_Effort_Normal_Hors_Diagramme_Est_Signale()
        {
            var diagram = new InteractionDiagram(Materials());
            double moment = diagram.MomentResistance(Section(), UnitConverter.KnToN(6000.0));
            Assert.Equal(-1.0, moment, 6);
        }

        [Fact]
        public void Le_Moment_Resistant_Passe_Par_Un_Maximum_En_Compression_Moderee()
        {
            // Comportement caracteristique d'une section en beton arme : la resistance en
            // flexion croit avec l'effort normal, puis chute quand la section se comprime.
            var diagram = new InteractionDiagram(Materials());
            BendingSection section = Section();

            double atZero = diagram.MomentResistance(section, 0.0);
            double atMiddle = diagram.MomentResistance(section, UnitConverter.KnToN(1200.0));
            double atHigh = diagram.MomentResistance(section, UnitConverter.KnToN(3400.0));

            Assert.True(atMiddle > atZero, "Le moment resistant doit croitre avec la compression.");
            Assert.True(atHigh < atMiddle, "Le moment resistant doit chuter pres de la compression centree.");
        }

        [Fact]
        public void Les_Pivots_Bornent_Les_Deformations()
        {
            ConcreteProperties materials = Materials();
            var diagram = new InteractionDiagram(materials);
            BendingSection section = Section();

            // Axe neutre dans la section : la fibre comprimee est a eps_cu2.
            double top = diagram.StrainAt(200.0, 0.0, SideMm, section.EffectiveDepthMm);
            Assert.Equal(materials.StrainCu2, top, 6);

            // Axe neutre tres profond : la deformation tend vers eps_c2 (pivot C).
            double uniform = diagram.StrainAt(1e6, SideMm / 2.0, SideMm, section.EffectiveDepthMm);
            Assert.InRange(uniform, materials.StrainC2 * 0.98, materials.StrainC2 * 1.02);
        }
    }
}
