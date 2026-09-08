using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// BEAM-01 : poutre isostatique 300 x 600, portee 6,00 m.
    /// Fiche de validation : docs/validation/BEAM-01.md
    ///
    /// C25/30, B500, XC1, M_Ed,travee = 250 kN.m, V_Ed = 200 kN aux deux appuis.
    ///
    /// Enrobage : le calcul converge sur HA16, donc c_min = max(16 ; 15 ; 10) = 16
    ///            et c_nom = 26 mm ; d = 600 - 26 - 8 - 8 = 558 mm.
    /// Flexion   : mu = 250e6 / (300 x 558^2 x 16,667) = 0,1606 -> x/d = 0,2201,
    ///             z = 508,9 mm, As = 1 130 mm2 -> 6 HA16 = 1 206 mm2.
    /// Tranchant : V_Rd,c = 84,1 kN < 200 kN ; z = 502,2 mm ; cot theta = 2,5 ;
    ///             A_sw/s = 200 000 / (502,2 x 434,78 x 2,5) = 0,3664 mm2/mm ;
    ///             cadre HA8 a 2 brins = 100,5 mm2 -> e = 274 mm arrondi a 250 mm.
    ///             En travee, V = 100 kN -> le minimum 0,24 mm2/mm gouverne, e = 400 mm.
    /// </summary>
    public class Beam01DesignTests
    {
        private static BeamData Beam()
        {
            return new BeamData
            {
                Id = "BEAM-01",
                Name = "P1",
                Mark = "B1",
                Shape = BeamSectionShape.Rectangular,
                WebWidthMm = 300.0,
                HeightMm = 600.0,
                SpanMm = 6000.0,
                SpanKind = BeamSpanKind.SimplySupported
            };
        }

        private static BeamDesignSettings Settings()
        {
            return new BeamDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                SpanMomentKnm = 250.0,
                LeftShearKn = 200.0,
                RightShearKn = 200.0
            };
        }

        private static BeamDesignResult Design()
        {
            return new BeamDesignModule().Design(Beam(), Settings(), null);
        }

        [Fact]
        public void Le_Dimensionnement_Aboutit_Sans_Anomalie()
        {
            BeamDesignResult result = Design();
            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck,
                "Aucune verification ne doit etre en defaut : " +
                string.Join(" | ", result.Checks.Where(c => c.Status == CheckStatus.Fail)
                                                .Select(c => c.Description)));
        }

        [Fact]
        public void L_Enrobage_Et_La_Hauteur_Utile_Convergent_Sur_HA16()
        {
            BeamReinforcement r = Design().Reinforcement;
            Assert.Equal(26.0, r.CoverMm, 6);
            Assert.Equal(558.0, r.EffectiveDepthMm, 6);
        }

        [Fact]
        public void Le_Lit_Inferieur_Vaut_6_HA16()
        {
            BeamDesignResult result = Design();
            Assert.Equal(16.0, result.Reinforcement.BottomSpan.DiameterMm, 6);
            Assert.Equal(6, result.Reinforcement.BottomSpan.Count);
            Assert.Equal(1, result.Reinforcement.BottomSpan.Layers);
            // As requis 1 130 mm2, As fourni 1 206 mm2
            Assert.InRange(result.SpanSteelRequiredMm2, 1120.0, 1140.0);
            Assert.InRange(result.Reinforcement.BottomSpan.AreaMm2, 1200.0, 1212.0);
        }

        [Fact]
        public void Les_Cadres_Sont_Resserres_Aux_Appuis_Et_Espaces_En_Travee()
        {
            BeamReinforcement r = Design().Reinforcement;

            Assert.Equal(8.0, r.StirrupDiameterMm, 6);
            Assert.Equal(3, r.StirrupZones.Count);

            BeamStirrupZone left = r.StirrupZones[0];
            BeamStirrupZone middle = r.StirrupZones[1];
            BeamStirrupZone right = r.StirrupZones[2];

            Assert.Equal(250.0, left.SpacingMm, 6);
            Assert.Equal(400.0, middle.SpacingMm, 6);
            Assert.Equal(250.0, right.SpacingMm, 6);
            Assert.True(middle.SpacingMm > left.SpacingMm,
                "L'espacement doit s'ouvrir en travee, la ou l'effort tranchant diminue.");
        }

        [Fact]
        public void Les_Zones_Couvrent_Toute_La_Portee()
        {
            BeamReinforcement r = Design().Reinforcement;
            Assert.Equal(0.0, r.StirrupZones.First().StartMm, 6);
            Assert.Equal(6000.0, r.StirrupZones.Last().EndMm, 6);
            for (int i = 1; i < r.StirrupZones.Count; i++)
            {
                Assert.Equal(r.StirrupZones[i - 1].EndMm, r.StirrupZones[i].StartMm, 6);
            }
        }

        [Fact]
        public void L_Espacement_Reste_Sous_Le_Maximum_Reglementaire()
        {
            BeamReinforcement r = Design().Reinforcement;
            double maxSpacing = 0.75 * r.EffectiveDepthMm;   // 418,5 mm
            foreach (BeamStirrupZone zone in r.StirrupZones)
            {
                Assert.True(zone.SpacingMm <= maxSpacing,
                    "Espacement " + zone.SpacingMm + " mm > 0,75 d = " + maxSpacing + " mm.");
            }
        }

        [Fact]
        public void Le_Decalage_Et_Les_Ancrages_Sont_Calcules()
        {
            BeamReinforcement r = Design().Reinforcement;
            // a_l = z cot theta / 2 = 502,2 x 2,5 / 2 = 627,75 mm
            Assert.InRange(r.ShiftLengthMm, 620.0, 635.0);
            // l_bd HA16 en C25 = 646 mm -> arrondi a 650 mm
            Assert.Equal(650.0, r.AnchorageLengthMm, 6);
            // l_0 = 968 mm -> arrondi a 1 000 mm
            Assert.Equal(1000.0, r.LapLengthMm, 6);
        }

        [Fact]
        public void Le_Plan_Contient_Les_Barres_Et_Les_Cadres()
        {
            BeamDesignResult result = Design();

            Assert.NotEmpty(result.Plan.OfKind(RebarKind.Longitudinal));
            Assert.Equal(3, result.Plan.OfKind(RebarKind.Stirrup).Count());

            foreach (RebarGroup group in result.Plan.Groups)
            {
                Assert.True(group.BarLengthMm > 0);
                Assert.True(group.HasNormal,
                    "Une poutre n'est pas verticale : la normale doit etre imposee.");
                Assert.StartsWith("B1-", group.Mark);
            }
        }

        [Fact]
        public void Les_Barres_Tiennent_Dans_La_Section()
        {
            BeamDesignResult result = Design();
            BeamData beam = result.Beam;
            double halfWidth = beam.WebWidthMm / 2.0;

            foreach (RebarGroup group in result.Plan.Groups)
            {
                foreach (PlanSegment segment in group.Path)
                {
                    foreach (var point in new[] { segment.Start, segment.End })
                    {
                        Assert.InRange(point.Y, -halfWidth, halfWidth);
                        Assert.InRange(point.Z, 0.0, beam.HeightMm);
                        Assert.InRange(point.X, 0.0, beam.SpanMm);
                    }
                }
            }
        }

        [Fact]
        public void Chaque_Verification_Porte_Sa_Clause()
        {
            foreach (CheckResult check in Design().Checks)
            {
                Assert.False(string.IsNullOrWhiteSpace(check.Code));
                Assert.False(string.IsNullOrWhiteSpace(check.Clause));
                Assert.False(string.IsNullOrWhiteSpace(check.Equation));
            }
        }
    }
}
