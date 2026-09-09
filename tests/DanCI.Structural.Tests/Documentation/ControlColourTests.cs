using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Dashboard;
using Xunit;

namespace DanCI.Structural.Tests.Documentation
{
    /// <summary>
    /// DASH-02 : les couleurs de controle.
    /// Fiche de validation : docs/validation/DASH-02.md
    ///
    /// Une couleur n'est pas une verification : elle ne dit rien que la note ne dise deja,
    /// elle dit seulement OU regarder. Ces tests verrouillent les deux choses qui rendent
    /// une convention de couleur utilisable — que les bornes soient nettes, et que le
    /// STATUT prime sur le taux dans les deux sens.
    /// </summary>
    public class ControlColourTests
    {
        private static DesignedElement At(double utilization, bool failed = false)
        {
            return new DesignedElement
            {
                Name = "E",
                IsValid = true,
                Checks = new List<CheckResult>
                {
                    new CheckResult
                    {
                        Code = "EC2",
                        Clause = "6.1",
                        Status = failed ? CheckStatus.Fail : CheckStatus.Pass,
                        Utilization = utilization
                    }
                }
            };
        }

        // ------------------------------------------------------------------
        // Les bornes
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0.00, ControlBand.LightlyUtilised)]
        [InlineData(0.49, ControlBand.LightlyUtilised)]
        [InlineData(0.50, ControlBand.LightlyUtilised)]   // borne incluse
        [InlineData(0.51, ControlBand.Normal)]
        [InlineData(0.85, ControlBand.Normal)]            // borne incluse
        [InlineData(0.86, ControlBand.Tight)]
        [InlineData(1.00, ControlBand.Tight)]             // 1,00 passe encore
        [InlineData(1.01, ControlBand.Overloaded)]
        [InlineData(2.95, ControlBand.Overloaded)]
        public void Chaque_Taux_Tombe_Dans_La_Bande_Attendue(double utilization,
                                                             ControlBand expected)
        {
            Assert.Equal(expected, ControlColours.BandOf(At(utilization)));
        }

        [Fact]
        public void Un_Taux_De_Un_Exactement_Passe_Encore()
        {
            // Choix explicite : l'Eurocode demande E_d <= R_d, l'egalite est admise. Une
            // convention de couleur qui declarerait 1,000 en defaut contredirait la note de
            // calcul juste a cote.
            Assert.Equal(ControlBand.Tight, ControlColours.BandOf(At(1.0)));
            Assert.NotEqual(ControlBand.Overloaded, ControlColours.BandOf(At(1.0)));
        }

        // ------------------------------------------------------------------
        // Le statut prime sur le taux, DANS LES DEUX SENS
        // ------------------------------------------------------------------

        [Fact]
        public void Une_Verification_En_Defaut_Surcharge_Meme_Sous_Un_Taux_De_Un()
        {
            // Une verification peut echouer sur autre chose qu'un rapport : un espacement,
            // une longueur d'ancrage, une longueur de chapeau. Peindre celle-la en bleu
            // serait exactement le contresens que la couleur doit eviter.
            DesignedElement element = At(0.30, failed: true);

            Assert.Equal(0.30, element.MaxUtilization, 6);
            Assert.Equal(ControlBand.Overloaded, ControlColours.BandOf(element));
        }

        [Fact]
        public void Un_Element_Non_Dimensionne_Est_Gris_Pas_Bleu()
        {
            // Sans resultat, le taux vaut zero — et zero ressemble beaucoup a « passe
            // largement ». L'absence de couleur n'est pas une absence de probleme.
            var element = new DesignedElement { Name = "X", IsValid = false };

            Assert.Equal(0.0, element.MaxUtilization, 6);
            Assert.Equal(ControlBand.NotDesigned, ControlColours.BandOf(element));
            Assert.Contains("n'est pas une absence de probleme",
                            ControlColours.For(element).Meaning);
        }

        [Fact]
        public void Un_Element_Nul_Est_Traite_Comme_Non_Dimensionne()
        {
            Assert.Equal(ControlBand.NotDesigned, ControlColours.BandOf(null));
        }

        // ------------------------------------------------------------------
        // La palette
        // ------------------------------------------------------------------

        [Fact]
        public void La_Legende_Couvre_Toutes_Les_Bandes_Dans_L_Ordre_De_Charge()
        {
            List<ControlColour> legend = ControlColours.Legend.ToList();
            var bands = Enum.GetValues(typeof(ControlBand)).Cast<ControlBand>().ToList();

            Assert.Equal(bands.Count, legend.Count);
            for (int i = 0; i < bands.Count; i++)
            {
                Assert.Equal(bands[i], legend[i].Band);
            }
        }

        [Fact]
        public void Deux_Bandes_N_Ont_Jamais_La_Meme_Couleur()
        {
            List<string> hex = ControlColours.Legend.Select(c => c.Hex).ToList();

            Assert.Equal(hex.Count, hex.Distinct().Count());
        }

        [Fact]
        public void Chaque_Bande_Se_Lit_Sans_Sa_Couleur()
        {
            // Le point d'accessibilite. Un lecteur dichromate, une impression en noir et
            // blanc, une photocopie : la legende doit rester lisible.
            foreach (ControlColour colour in ControlColours.Legend)
            {
                Assert.False(string.IsNullOrWhiteSpace(colour.Label));
                Assert.False(string.IsNullOrWhiteSpace(colour.Meaning));
                Assert.DoesNotContain(colour.Band.ToString(), colour.Label);
            }
        }

        [Fact]
        public void La_Palette_N_Oppose_Pas_Le_Vert_Et_Le_Rouge()
        {
            // Un deuteranope confond le vert et le rouge, c'est-a-dire exactement « ca
            // passe » et « ca ne passe pas ». La rampe va donc du bleu au rouge.
            ControlColour lightly = ControlColours.Of(ControlBand.LightlyUtilised);
            ControlColour overloaded = ControlColours.Of(ControlBand.Overloaded);

            // Le pole « passe » est franchement bleu : le bleu domine le vert et le rouge.
            Assert.True(lightly.B > lightly.G && lightly.B > lightly.R,
                        "Le pole favorable doit etre bleu, pas vert.");

            // Le pole « ne passe pas » est franchement rouge.
            Assert.True(overloaded.R > overloaded.G && overloaded.R > overloaded.B);

            // Et aucune bande n'est un vert dominant.
            foreach (ControlColour colour in ControlColours.Legend)
            {
                Assert.False(colour.G > colour.R && colour.G > colour.B,
                    string.Format("{0} est un vert dominant.", colour.Label));
            }
        }

        [Fact]
        public void La_Bande_Basse_N_Est_Pas_Presentee_Comme_Une_Bonne_Nouvelle()
        {
            // Un element a 0,20 passe, mais il est probablement surdimensionne. Le peindre
            // en vert rassurant ferait perdre une economie.
            ControlColour colour = ControlColours.Of(ControlBand.LightlyUtilised);

            Assert.Contains("surdimensionne", colour.Meaning);
        }

        [Fact]
        public void L_Hexadecimal_Est_Rendu_Sur_Six_Chiffres()
        {
            ControlColour colour = ControlColours.Of(ControlBand.Overloaded);

            Assert.Equal(7, colour.Hex.Length);
            Assert.StartsWith("#", colour.Hex);
            Assert.Equal("#D7191C", colour.Hex);
        }

        // ------------------------------------------------------------------
        // Cohérence avec le tableau de bord
        // ------------------------------------------------------------------

        [Fact]
        public void Tout_Element_Non_Conforme_Est_Rouge_Et_Reciproquement()
        {
            // Les deux lectures d'un meme projet — le tableau et les couleurs — doivent
            // designer les memes elements. Un element rouge absent de la liste des non
            // conformes, ou l'inverse, serait une contradiction dans le meme document.
            //
            // Les cas sont ceux que les modules produisent reellement : un taux superieur
            // a 1 y est TOUJOURS rendu en defaut, jamais en « satisfait ».
            var elements = new[]
            {
                At(0.30), At(0.70), At(0.90),
                At(1.50, failed: true), At(0.20, failed: true),
                new DesignedElement { Name = "Z", IsValid = false }
            };

            foreach (DesignedElement element in elements)
            {
                bool red = ControlColours.BandOf(element) == ControlBand.Overloaded;
                bool listed = element.Status == DesignStatus.NotCompliant;
                Assert.Equal(listed, red);
            }
        }

        [Fact]
        public void Un_Taux_Superieur_A_Un_Est_Rouge_Meme_Si_La_Verification_Se_Dit_Satisfaite()
        {
            // L'ASYMETRIE EST VOULUE, et c'est le seul cas ou couleur et statut divergent.
            //
            // Aucun module ne produit cela : un taux au-dessus de 1 est toujours rendu en
            // defaut. Si la situation se presentait quand meme — verification mal
            // renseignee, module tiers, resultat relu d'une version anterieure — la couleur
            // se fie AUSSI au rapport, et signale l'element.
            //
            // Le sens de cette asymetrie est le seul acceptable : elle peut attirer
            // l'attention sur un element qui va bien, jamais la detourner d'un element qui
            // va mal.
            DesignedElement suspicious = At(1.50);

            Assert.Equal(DesignStatus.Compliant, suspicious.Status);
            Assert.Equal(ControlBand.Overloaded, ControlColours.BandOf(suspicious));
        }
    }
}
