using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STAIR-04 : ou tombent reellement les barres.
    /// Fiche de validation : docs/validation/STAIR-04.md
    ///
    /// Les tests des fiches precedentes verifiaient le nombre de groupes, la presence
    /// d'une normale et une longueur positive. Aucun ne verifiait OU les barres se
    /// posent — et c'est precisement la qu'etaient les deux defauts remontes de Revit :
    /// des barres hors du beton et des nappes restees horizontales sur une volee inclinee.
    ///
    /// Ces tests reconstruisent donc la position de CHAQUE COPIE, comme Revit le fait — la
    /// copie k est le trajet translate de direction x (k x pas) — et verifient que tout
    /// point de toute copie est dans le beton.
    /// </summary>
    public class Stair04GeometryTests
    {
        private const double Tolerance = 0.5;

        private static StairData Flight(double waistMm = 180.0, double landingMm = 1300.0)
        {
            return new StairData
            {
                Name = "V4",
                RiserHeightMm = 170.0,
                TreadDepthMm = 280.0,
                RiserCount = 9,
                WaistThicknessMm = waistMm,
                WidthMm = 1200.0,
                LandingThicknessMm = waistMm,
                LandingSpanMm = landingMm,
                SpanKind = landingMm > 0
                    ? StairSpanKind.AlongFlightWithLanding
                    : StairSpanKind.AlongFlightOnly,
                Shape = StairFlightShape.Straight
            };
        }

        private static StairDesignSettings Settings()
        {
            return new StairDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                TreadFinishKnM2 = 1.0,
                SoffitFinishKnM2 = 0.3,
                VariableLoadKnM2 = 3.0,
                ConcentratedLoadKn = 2.0,
                IncludeSelfWeight = true,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true,
                TopReinforcement = true
            };
        }

        private static StairDesignResult Design(StairData stair)
        {
            return new StairDesignModule().Design(stair, Settings(), null);
        }

        // ------------------------------------------------------------------
        // L'invariant central
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(180.0, 1300.0)]   // volee + palier
        [InlineData(180.0, 0.0)]      // volee seule
        [InlineData(250.0, 800.0)]    // paillasse epaisse, palier court
        [InlineData(150.0, 2000.0)]   // paillasse mince, palier long
        public void Toute_Copie_De_Toute_Barre_Est_Dans_Le_Beton(double waist, double landing)
        {
            StairData stair = Flight(waist, landing);
            StairDesignResult result = Design(stair);
            Assert.True(result.IsValid);

            var envelope = new Envelope(stair);
            foreach (RebarGroup group in result.Plan.Groups)
            {
                foreach (LocalPoint point in EnumerateAllCopies(group))
                {
                    Assert.True(envelope.Contains(point),
                        string.Format("{0} : le point {1} sort du beton ({2}).",
                                      group.Label, point, envelope.Describe(point)));
                }
            }
        }

        [Fact]
        public void Les_Nappes_Transversales_De_Volee_Montent_Avec_La_Paillasse()
        {
            // LE DEFAUT REMONTE DE REVIT. La repetition est une translation rectiligne le
            // long de la normale : si la normale est horizontale, toutes les copies restent
            // a la meme altitude et sortent du beton des la deuxieme.
            StairDesignResult result = Design(Flight());

            RebarGroup flightMesh = result.Plan.Groups
                .First(g => g.Label.Contains("Repartition inferieure, volee"));

            Assert.True(flightMesh.BarCount > 1, "Le cas de reference doit repeter la nappe.");
            Assert.NotEqual(0.0, flightMesh.Normal.Z);

            List<LocalPoint> first = CopyPoints(flightMesh, 0).ToList();
            List<LocalPoint> last = CopyPoints(flightMesh, flightMesh.BarCount - 1).ToList();

            // La derniere copie doit avoir monte de la pente x la longueur du reseau.
            double expectedRise = flightMesh.Layout.ArrayLengthMm
                                  * Math.Sin(Math.Atan(0.170 / 0.280));
            Assert.Equal(expectedRise, last[0].Z - first[0].Z, 1);
            Assert.True(last[0].Z - first[0].Z > 100.0,
                        "Sur une volee de 31 degres, la montee du reseau n'est pas negligeable.");
        }

        [Fact]
        public void La_Volee_Et_Le_Palier_Sont_Deux_Groupes_Distincts()
        {
            // Une seule repetition rectiligne ne peut pas suivre une pente PUIS un plat.
            StairDesignResult result = Design(Flight());

            Assert.Contains(result.Plan.Groups,
                g => g.Label.Contains("Repartition inferieure, volee"));
            Assert.Contains(result.Plan.Groups,
                g => g.Label.Contains("Repartition inferieure, palier"));

            RebarGroup landing = result.Plan.Groups
                .First(g => g.Label.Contains("Repartition inferieure, palier"));
            // Sur le palier, la repetition est horizontale, et c'est correct.
            Assert.Equal(0.0, landing.Normal.Z, 6);
        }

        [Fact]
        public void Sans_Palier_Aucun_Groupe_De_Palier_N_Est_Produit()
        {
            StairDesignResult result = Design(Flight(180.0, 0.0));

            Assert.DoesNotContain(result.Plan.Groups, g => g.Label.Contains("palier"));
        }

        [Fact]
        public void La_Repartition_Superieure_Est_Referencee_A_La_Face_Superieure()
        {
            // Elle etait construite depuis l'enrobage INFERIEUR : le groupe etiquete
            // superieur se posait pres du bas de la paillasse.
            StairData stair = Flight();
            StairDesignResult result = Design(stair);

            RebarGroup top = result.Plan.Groups
                .First(g => g.Label.Contains("Repartition superieure"));
            LocalPoint point = CopyPoints(top, 0).First();

            var envelope = new Envelope(stair);
            double soffit = envelope.SoffitZ(point.X);
            double topFace = envelope.TopFaceZ(point.X);

            // Elle doit etre dans la moitie haute de la section.
            Assert.True(point.Z > (soffit + topFace) / 2.0,
                string.Format("z = {0:0.0}, sous-face {1:0.0}, face sup. {2:0.0}",
                              point.Z, soffit, topFace));
        }

        [Fact]
        public void Le_Chapeau_Haut_Suit_La_Pente_Quand_Il_N_Y_A_Pas_De_Palier()
        {
            // Il etait toujours horizontal : sur une volee sans palier, il passait
            // au-dessus de la paillasse.
            StairDesignResult result = Design(Flight(180.0, 0.0));

            RebarGroup upper = result.Plan.Groups
                .First(g => g.Label.Contains("Chapeau appui haut"));
            List<LocalPoint> points = CopyPoints(upper, 0).ToList();

            Assert.True(Math.Abs(points.Last().Z - points.First().Z) > 100.0,
                        "Le chapeau haut d'une volee sans palier doit monter.");
        }

        [Fact]
        public void L_Enrobage_Est_Mesure_Normalement_A_La_Pente()
        {
            // Un decalage vertical de c donne un enrobage reel de c cos alpha, soit 15 %
            // de moins a 31 degres. Le decalage doit valoir c / cos alpha.
            StairData stair = Flight();
            StairDesignResult result = Design(stair);
            StairReinforcement r = result.Reinforcement;

            RebarGroup bottom = result.Plan.Groups
                .First(g => g.Label.Contains("Nappe inferieure de volee"));
            LocalPoint start = bottom.Path[0].Start;

            var envelope = new Envelope(stair);
            double verticalAbove = start.Z - envelope.SoffitZ(start.X);
            double normalDistance = verticalAbove * stair.SlopeCosine;

            Assert.Equal(r.CoverMm + r.BottomMain.DiameterMm / 2.0, normalDistance, 1);
        }

        [Fact]
        public void Chaque_Trajet_Est_Une_Chaine_Continue()
        {
            // Revit refuse une barre dont les courbes ne s'enchainent pas.
            foreach (double landing in new[] { 0.0, 300.0, 1300.0, 2000.0 })
            {
                StairDesignResult result = Design(Flight(180.0, landing));
                foreach (RebarGroup group in result.Plan.Groups)
                {
                    for (int i = 1; i < group.Path.Count; i++)
                    {
                        LocalPoint end = group.Path[i - 1].End;
                        LocalPoint start = group.Path[i].Start;
                        Assert.True(Distance(end, start) < 0.1,
                            string.Format("{0} : rupture de {1:0.00} mm entre les segments {2} et {3}.",
                                          group.Label, Distance(end, start), i - 1, i));
                    }
                }
            }
        }

        [Fact]
        public void Aucun_Groupe_Ne_Se_Repete_Suivant_Une_Direction_Qui_Sort_Du_Beton()
        {
            // Controle de coherence : la normale d'un groupe et la direction de son reseau
            // doivent etre le meme vecteur, puisque Revit repartit le long de la normale.
            StairDesignResult result = Design(Flight());

            foreach (RebarGroup group in result.Plan.Groups.Where(g => g.BarCount > 1))
            {
                Assert.True(group.HasNormal);
                LocalVector n = group.Normal;
                LocalVector d = group.Layout.Direction;
                double dot = n.X * d.X + n.Y * d.Y + n.Z * d.Z;
                double norm = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z)
                              * Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                Assert.True(norm > 0 && dot / norm > 0.999,
                    group.Label + " : la normale et la direction de repetition different.");
            }
        }

        // ------------------------------------------------------------------
        // Outils : on rejoue ce que Revit fait des groupes
        // ------------------------------------------------------------------

        /// <summary>Le beton de la volee, dans le repere local du constructeur de plan.</summary>
        private sealed class Envelope
        {
            private readonly StairData _stair;

            public Envelope(StairData stair) { _stair = stair; }

            public double SoffitZ(double x)
            {
                return Math.Min(Math.Max(x, 0.0), _stair.TotalGoingMm) * _stair.SlopeTangent;
            }

            public double TopFaceZ(double x)
            {
                return x <= _stair.TotalGoingMm
                    ? x * _stair.SlopeTangent
                      + _stair.WaistThicknessMm / _stair.SlopeCosine
                    : _stair.TotalGoingMm * _stair.SlopeTangent + _stair.LandingThicknessMm;
            }

            public bool Contains(LocalPoint p)
            {
                if (p.X < -Tolerance || p.X > _stair.SpanMm + Tolerance) return false;
                if (p.Y < -Tolerance || p.Y > _stair.WidthMm + Tolerance) return false;
                return p.Z >= SoffitZ(p.X) - Tolerance && p.Z <= TopFaceZ(p.X) + Tolerance;
            }

            public string Describe(LocalPoint p)
            {
                return string.Format(
                    "x dans [0 ; {0:0}], y dans [0 ; {1:0}], z dans [{2:0.0} ; {3:0.0}]",
                    _stair.SpanMm, _stair.WidthMm, SoffitZ(p.X), TopFaceZ(p.X));
            }
        }

        /// <summary>Tous les points de toutes les copies d'un groupe.</summary>
        private static IEnumerable<LocalPoint> EnumerateAllCopies(RebarGroup group)
        {
            for (int copy = 0; copy < group.BarCount; copy++)
            {
                foreach (LocalPoint point in CopyPoints(group, copy)) yield return point;
            }
        }

        /// <summary>
        /// Les points d'une copie donnee : le trajet translate le long de la direction de
        /// repetition, exactement comme Revit repartit les barres d'un groupe.
        /// Les segments sont echantillonnes, pas seulement leurs extremites : une corde
        /// peut sortir du beton entre deux points qui, eux, y sont.
        /// </summary>
        private static IEnumerable<LocalPoint> CopyPoints(RebarGroup group, int copyIndex)
        {
            double step = 0.0;
            LocalVector direction = LocalVector.AxisX;

            if (group.Layout != null && group.Layout.Kind != LayoutKind.Single
                && group.BarCount > 1)
            {
                direction = group.Layout.Direction;
                double length = Math.Sqrt(direction.X * direction.X
                                          + direction.Y * direction.Y
                                          + direction.Z * direction.Z);
                if (length > 0)
                {
                    direction = new LocalVector(direction.X / length, direction.Y / length,
                                                direction.Z / length);
                }
                step = group.Layout.ArrayLengthMm / (group.BarCount - 1) * copyIndex;
            }

            const int samples = 12;
            foreach (PlanSegment segment in group.Path)
            {
                for (int i = 0; i <= samples; i++)
                {
                    double t = (double)i / samples;
                    double x = segment.Start.X + (segment.End.X - segment.Start.X) * t;
                    double y = segment.Start.Y + (segment.End.Y - segment.Start.Y) * t;
                    double z = segment.Start.Z + (segment.End.Z - segment.Start.Z) * t;
                    yield return new LocalPoint(x + direction.X * step,
                                                y + direction.Y * step,
                                                z + direction.Z * step);
                }
            }
        }

        private static double Distance(LocalPoint a, LocalPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
