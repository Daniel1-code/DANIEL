using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class EngineQuantityTests
    {
        private readonly BoqEngine engine = new BoqEngine();

        [Fact]
        public void Les_quantites_d_un_article_s_additionnent_et_gardent_leurs_ElementId()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 250m));

            BoqResult r = engine.Build(new[] { TestData.Concrete(11, 0.27), TestData.Concrete(12, 0.27), TestData.Concrete(13, 0.27) }, config);

            BoqLine line = r.AllLines.Single();
            Assert.Equal("03.01", line.Code);
            Assert.Equal(0.81m, line.NetQuantity);
            Assert.Equal(0.81m, line.Quantity);
            Assert.Equal(202.50m, line.Amount);
            Assert.Equal(new long[] { 11, 12, 13 }, line.ElementIds.ToArray());
            Assert.All(line.Contributions, c => Assert.Equal("POT", c.RuleId));
            Assert.Equal(Reliability.Measured, line.Reliability);
        }

        [Fact]
        public void La_somme_ne_depend_pas_de_l_ordre_d_addition()
        {
            // En double, 0,1 + 0,2 + 0,3 != 0,3 + 0,2 + 0,1 au dernier bit ; en decimal, egalite exacte.
            var config = TestData.Config();
            var a = engine.Build(new[] { TestData.Concrete(1, 0.1), TestData.Concrete(2, 0.2), TestData.Concrete(3, 0.3) }, config);
            var b = engine.Build(new[] { TestData.Concrete(3, 0.3), TestData.Concrete(2, 0.2), TestData.Concrete(1, 0.1) }, config);
            Assert.Equal(0.6m, a.AllLines.Single().NetQuantity);
            Assert.Equal(a.AllLines.Single().NetQuantity, b.AllLines.Single().NetQuantity);
        }

        [Fact]
        public void Le_BOQ_est_identique_quel_que_soit_l_ordre_des_elements()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 237.45m));
            config.Prices.Prices.Add(TestData.Price("03.08", BoqUnit.Kilogram, 2.15m));
            config.Coefficients.Items.Add(new Coefficient { Code = "P", Value = 0.035m, AppliesTo = TestData.List("03.*") });

            var random = new Random(20261121);
            var items = new List<ElementQuantity>();
            for (int i = 1; i <= 300; i++)
            {
                items.Add(i % 3 == 0
                    ? TestData.Rebar(i, random.NextDouble() * 80, i % 2 == 0 ? Reliability.Estimated : Reliability.Measured)
                    : TestData.Concrete(i, random.NextDouble() * 2, i % 2 == 0 ? "OST_StructuralColumns" : "OST_StructuralFraming"));
            }

            string reference = Fingerprint(engine.Build(items, config));
            for (int run = 0; run < 5; run++)
            {
                var shuffled = items.OrderBy(_ => random.Next()).ToList();
                Assert.Equal(reference, Fingerprint(engine.Build(shuffled, config)));
            }
        }

        [Fact]
        public void Une_quantite_negative_ou_non_numerique_n_est_pas_comptee_et_est_signalee()
        {
            var r = engine.Build(new[] { TestData.Concrete(1, 1.0), TestData.Concrete(2, -0.5), TestData.Concrete(3, double.NaN) }, TestData.Config());
            Assert.Equal(1.0m, r.AllLines.Single().NetQuantity);
            Assert.Contains(r.Issues, i => i.Code == "QUANTITE_NEGATIVE" && i.ElementIds.Contains(2) && i.Severity == IssueSeverity.Error);
            Assert.Contains(r.Issues, i => i.Code == "QUANTITE_INVALIDE" && i.ElementIds.Contains(3));
        }

        [Fact]
        public void Une_meme_quantite_fournie_deux_fois_n_est_comptee_qu_une_fois_et_l_erreur_est_levee()
        {
            var r = engine.Build(new[] { TestData.Concrete(7, 1.5), TestData.Concrete(7, 1.5) }, TestData.Config());
            Assert.Equal(1.5m, r.AllLines.Single().NetQuantity);
            Assert.Contains(r.Issues, i => i.Code == "DOUBLON_QUANTITE" && i.Severity == IssueSeverity.Error);
            Assert.Single(r.Excluded);
        }

        [Fact]
        public void Meme_ElementId_dans_deux_documents_differents_n_est_pas_un_doublon()
        {
            var host = TestData.Concrete(7, 1.0);
            var linked = TestData.Concrete(7, 2.0);
            linked.SourceDocument = "Structure.rvt";
            var r = engine.Build(new[] { host, linked }, TestData.Config());
            Assert.Equal(3.0m, r.AllLines.Single().NetQuantity);
            Assert.DoesNotContain(r.Issues, i => i.Code == "DOUBLON_QUANTITE");
        }

        [Fact]
        public void Element_marque_a_exclure_et_regle_d_exclusion()
        {
            var config = TestData.Config();
            var exclusion = TestData.Rule("PROVISOIRE", 1, null, kind: "VolumeBeton", type: "*provisoire*");
            exclusion.Exclude = true;
            exclusion.Reason = "Ouvrages provisoires hors marché";
            config.Mapping.Rules.Add(exclusion);

            var user = TestData.Concrete(1, 1.0);
            user.ExcludedByUser = true;
            var temp = TestData.Concrete(2, 1.0);
            temp.TypeName = "Étai provisoire";

            var r = engine.Build(new[] { user, temp, TestData.Concrete(3, 1.0) }, config);

            Assert.Equal(1.0m, r.AllLines.Single().NetQuantity);
            Assert.Equal(2, r.Excluded.Count);
            Assert.Contains(r.Excluded, e => e.Element.ElementId == 2 && e.RuleId == "PROVISOIRE" && e.Reason == "Ouvrages provisoires hors marché");
            Assert.Contains(r.Excluded, e => e.Element.ElementId == 1 && e.Reason.Contains("DanCI_BOQ_Exclure"));
            Assert.Contains(r.Issues, i => i.Code == "ELEMENTS_EXCLUS");
        }

        [Fact]
        public void Sans_regle_la_quantite_va_en_non_classes_visible_et_non_valorisee()
        {
            var generic = new ElementQuantity
            {
                ElementId = 99,
                Category = "OST_GenericModel",
                CategoryLabel = "Modèles génériques",
                TypeName = "Socle",
                Kind = QuantityKinds.Volume,
                Value = 0.4,
                Unit = BoqUnit.CubicMeter
            };
            var r = engine.Build(new[] { TestData.Concrete(1, 1.0), generic }, TestData.Config());

            BoqLotResult nc = r.Lots.Last();
            Assert.Equal("NC", nc.Code);
            BoqLine line = nc.Lines.Single();
            Assert.Equal("NC.001", line.Code);
            Assert.True(line.IsUnclassified);
            Assert.Null(line.Amount);
            Assert.Equal(0.4m, line.Quantity);
            Assert.Contains("Modèles génériques", line.Designation);
            Assert.Contains(r.Issues, i => i.Code == "NON_CLASSE" && i.ElementIds.Contains(99));
        }

        [Fact]
        public void Une_unite_incompatible_n_est_jamais_convertie()
        {
            var wrong = TestData.Concrete(5, 3.0);
            wrong.Unit = BoqUnit.SquareMeter; // une surface rattachee par la regle POT a un article en m3
            var r = engine.Build(new[] { wrong }, TestData.Config());

            Assert.DoesNotContain(r.AllLines, l => l.Code == "03.01");
            Assert.Contains(r.Issues, i => i.Code == "UNITE_INCOMPATIBLE" && i.Severity == IssueSeverity.Error && i.ElementIds.Contains(5));
            Assert.Equal(BoqUnit.SquareMeter, r.Lots.Single(l => l.Code == "NC").Lines.Single().Unit);
        }

        [Fact]
        public void Les_lots_et_articles_suivent_l_ordre_du_catalogue()
        {
            var r = engine.Build(new[]
            {
                new ElementQuantity { ElementId = 1, Kind = "Maconnerie", Value = 10, Unit = BoqUnit.SquareMeter },
                TestData.Concrete(2, 1.0, "OST_StructuralFraming"),
                TestData.Concrete(3, 1.0, "OST_StructuralColumns")
            }, TestData.Config());

            Assert.Equal(new[] { "03", "05" }, r.Lots.Select(l => l.Code).ToArray());
            Assert.Equal(new[] { "03.01", "03.02" }, r.Lots[0].Lines.Select(l => l.Code).ToArray());
        }

        [Fact]
        public void Une_configuration_invalide_bloque_le_moteur()
        {
            var config = TestData.Config();
            config.Mapping.Rules.Add(TestData.Rule("DOUBLE", 10, "03.01", kind: "VolumeBeton"));
            var ex = Assert.Throws<BoqConfigurationException>(() => engine.Build(new[] { TestData.Concrete(1, 1) }, config));
            Assert.Contains(ex.Issues, i => i.Code == "ORDRE_AMBIGU");
        }

        internal static string Fingerprint(BoqResult r)
        {
            var sb = new StringBuilder();
            foreach (BoqLine l in r.AllLines)
            {
                sb.Append(l.Code).Append('|').Append(l.NetQuantity.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(l.Quantity.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(l.Amount?.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(string.Join(",", l.ElementIds)).AppendLine();
            }
            sb.Append(r.TotalIncludingVat.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.ReliabilityIndex?.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    public class EngineCoefficientAndPriceTests
    {
        private readonly BoqEngine engine = new BoqEngine();

        [Fact]
        public void Le_coefficient_est_affiche_a_part_et_la_quantite_brute_est_valorisee()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new Coefficient { Code = "PERTE-BETON", Designation = "Pertes", Value = 0.03m, AppliesTo = TestData.List("03.0?") });
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 100m));

            BoqLine line = engine.Build(new[] { TestData.Concrete(1, 10.0) }, config).AllLines.Single();

            Assert.Equal(10m, line.NetQuantity);
            Assert.Equal(1.03m, line.CoefficientFactor);
            Assert.Equal(10.3m, line.GrossQuantity);
            Assert.Equal(10.30m, line.Quantity);
            Assert.Equal(1030.00m, line.Amount);
            Assert.Equal("PERTE-BETON", line.Coefficients.Single().Code);
        }

        [Fact]
        public void En_mode_quantite_nette_le_coefficient_n_entre_pas_dans_le_montant()
        {
            var config = TestData.Config();
            config.Settings.PricedQuantity = PricedQuantity.Nette;
            config.Coefficients.Items.Add(new Coefficient { Code = "P", Value = 0.03m, AppliesTo = TestData.List("03.01") });
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 100m));

            BoqLine line = engine.Build(new[] { TestData.Concrete(1, 10.0) }, config).AllLines.Single();
            Assert.Equal(10.3m, line.GrossQuantity);
            Assert.Equal(10.00m, line.Quantity);
            Assert.Equal(1000.00m, line.Amount);
        }

        [Fact]
        public void Plusieurs_coefficients_se_combinent_par_produit()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new Coefficient { Code = "A", Value = 0.05m, AppliesTo = TestData.List("03.*") });
            config.Coefficients.Items.Add(new Coefficient { Code = "B", Value = 0.10m, AppliesTo = TestData.List("03.01") });

            BoqLine line = engine.Build(new[] { TestData.Concrete(1, 100.0) }, config).AllLines.Single();
            Assert.Equal(1.155m, line.CoefficientFactor);
            Assert.Equal(115.5m, line.GrossQuantity);
        }

        [Fact]
        public void Un_coefficient_a_valider_est_signale()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new Coefficient { Code = "P", Value = 0.03m, AppliesTo = TestData.List("03.01"), NeedsValidation = true });
            var r = engine.Build(new[] { TestData.Concrete(1, 1.0) }, config);
            Assert.Contains(r.Issues, i => i.Code == "COEF_A_VALIDER" && i.Message.Contains("03.01"));
        }

        [Fact]
        public void Le_montant_est_calcule_sur_la_quantite_arrondie_comme_la_formule_Excel()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 333.33m));

            BoqLine line = engine.Build(new[] { TestData.Concrete(1, 1.005) }, config).AllLines.Single();
            Assert.Equal(1.01m, line.Quantity); // arrondi commercial, au plus loin de zero
            Assert.Equal(Math.Round(line.Quantity * line.UnitPrice.Value, 2, MidpointRounding.AwayFromZero), line.Amount);
            Assert.Equal(336.66m, line.Amount);
        }

        [Fact]
        public void Decimales_par_unite()
        {
            var config = TestData.Config();
            config.Settings.QuantityDecimalsByUnit[BoqUnit.Kilogram] = 0;
            BoqLine line = engine.Build(new[] { TestData.Rebar(1, 1234.56, Reliability.Measured) }, config).AllLines.Single();
            Assert.Equal(1235m, line.Quantity);
        }

        [Fact]
        public void Sans_prix_le_montant_reste_vide_et_les_totaux_sont_des_minorants()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 100m));
            var r = engine.Build(new[] { TestData.Concrete(1, 1.0), TestData.Concrete(2, 1.0, "OST_StructuralFraming") }, config);

            BoqLine unpriced = r.AllLines.Single(l => l.Code == "03.02");
            Assert.Null(unpriced.UnitPrice);
            Assert.Null(unpriced.Amount);
            Assert.Equal(1.00m, unpriced.Quantity);
            Assert.False(r.Lots.Single().IsComplete);
            Assert.False(r.IsComplete);
            Assert.Equal(100m, r.WorksTotal);
            Assert.Contains(r.Issues, i => i.Code == "PRIX_ABSENT" && i.ArticleCode == "03.02");
            Assert.Contains(r.Issues, i => i.Code == "TOTAUX_INCOMPLETS");
        }

        [Fact]
        public void Un_prix_en_CDF_est_converti_au_taux_date()
        {
            var config = TestData.Config();
            config.Settings.ExchangeRate = new ExchangeRate { CdfPerUsd = 2850m, Date = new DateTime(2026, 11, 21), Source = "test" };
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 285000m, Currency.CDF));

            BoqLine line = engine.Build(new[] { TestData.Concrete(1, 2.0) }, config).AllLines.Single();
            Assert.Equal(100.00m, line.UnitPrice);
            Assert.Equal(285000m, line.OriginalUnitPrice);
            Assert.Equal(Currency.CDF, line.OriginalCurrency);
            Assert.Equal(200.00m, line.Amount);
        }

        [Fact]
        public void Un_prix_dans_une_autre_devise_sans_taux_bloque_le_calcul()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 285000m, Currency.CDF));
            var ex = Assert.Throws<BoqConfigurationException>(() => engine.Build(new[] { TestData.Concrete(1, 1.0) }, config));
            Assert.Contains(ex.Issues, i => i.Code == "TAUX_ABSENT");
        }
    }

    public class EngineFooterTests
    {
        private readonly BoqEngine engine = new BoqEngine();

        private static BoqConfiguration ThousandDollars()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 100m));
            return config;
        }

        [Fact]
        public void Majorations_sur_le_total_des_travaux_puis_TVA_16()
        {
            var config = ThousandDollars();
            config.Settings.Markups.Add(new Markup { Code = "FG", Designation = "Frais généraux", Percent = 10m, Base = MarkupBase.Travaux });
            config.Settings.Markups.Add(new Markup { Code = "BEN", Designation = "Bénéfice", Percent = 5m, Base = MarkupBase.Travaux });

            BoqResult r = engine.Build(new[] { TestData.Concrete(1, 10.0) }, config);

            Assert.Equal(1000.00m, r.WorksTotal);
            Assert.Equal(new[] { 100.00m, 50.00m }, r.Markups.Select(m => m.Amount).ToArray());
            Assert.Equal(1150.00m, r.TotalExcludingVat);
            Assert.Equal(184.00m, r.Vat);
            Assert.Equal(1334.00m, r.TotalIncludingVat);
            Assert.True(r.IsComplete);
        }

        [Fact]
        public void Majorations_en_cascade()
        {
            var config = ThousandDollars();
            config.Settings.Markups.Add(new Markup { Code = "FG", Designation = "Frais généraux", Percent = 10m, Base = MarkupBase.Cumul });
            config.Settings.Markups.Add(new Markup { Code = "BEN", Designation = "Bénéfice", Percent = 5m, Base = MarkupBase.Cumul });

            BoqResult r = engine.Build(new[] { TestData.Concrete(1, 10.0) }, config);
            Assert.Equal(new[] { 100.00m, 55.00m }, r.Markups.Select(m => m.Amount).ToArray());
            Assert.Equal(1155.00m, r.TotalExcludingVat);
            Assert.Equal(184.80m, r.Vat);
            Assert.Equal(1339.80m, r.TotalIncludingVat);
        }

        [Fact]
        public void Total_TTC_en_devise_secondaire()
        {
            var config = ThousandDollars();
            config.Settings.SecondaryCurrency = Currency.CDF;
            config.Settings.ExchangeRate = new ExchangeRate { CdfPerUsd = 2850m, Date = new DateTime(2026, 11, 21), Source = "test" };

            BoqResult r = engine.Build(new[] { TestData.Concrete(1, 10.0) }, config);
            Assert.Equal(1160.00m, r.TotalIncludingVat);
            Assert.Equal(3306000.00m, r.TotalIncludingVatSecondary);
        }

        [Fact]
        public void Forfaits_saisis_dans_leur_lot_hors_indice_de_fiabilite()
        {
            var config = ThousandDollars();
            var manual = new[]
            {
                new ManualLine { Lot = "00", Designation = "Installation et repli de chantier", UnitPrice = 5000m, Justification = "Devis entreprise" },
                new ManualLine { Lot = "77", Designation = "Ligne mal rangée", UnitPrice = 10m }
            };

            BoqResult r = engine.Build(new[] { TestData.Concrete(1, 10.0) }, config, manual);

            BoqLine installation = r.Lots.First().Lines.Single();
            Assert.Equal("00", r.Lots.First().Code);
            Assert.Equal("00.F01", installation.Code);
            Assert.Equal(Reliability.Manual, installation.Reliability);
            Assert.Equal(BoqUnit.LumpSum, installation.Unit);
            Assert.Equal(5000m, installation.Amount);
            Assert.Contains(r.Issues, i => i.Code == "FORFAIT_LOT_INCONNU");
            Assert.Equal("NC", r.Lots.Last().Code);
            Assert.Equal(5010m, r.ManualAmount);
            Assert.Equal(1m, r.ReliabilityIndex); // le seul metre est mesure : 100 %
        }
    }

    public class EngineReliabilityTests
    {
        private readonly BoqEngine engine = new BoqEngine();

        [Fact]
        public void Indice_de_fiabilite_part_du_montant_mesure()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 100m));
            config.Prices.Prices.Add(TestData.Price("03.08", BoqUnit.Kilogram, 2m));

            BoqResult r = engine.Build(new[]
            {
                TestData.Concrete(1, 1.0),                      // 100 $ mesures
                TestData.Rebar(2, 60, Reliability.Measured),    // 120 $ mesures
                TestData.Rebar(3, 40, Reliability.Estimated)    //  80 $ estimes (ratio)
            }, config);

            BoqLine rebar = r.AllLines.Single(l => l.Code == "03.08");
            Assert.Equal(Reliability.Estimated, rebar.Reliability);
            Assert.Equal(60m, rebar.MeasuredNetQuantity);
            Assert.Equal(40m, rebar.EstimatedNetQuantity);
            Assert.Contains("ratio 120 kg/m³ (poteaux)", rebar.Assumptions);

            Assert.Equal(220m, r.MeasuredAmount);
            Assert.Equal(80m, r.EstimatedAmount);
            Assert.Equal(220m / 300m, r.ReliabilityIndex);
        }

        [Fact]
        public void Une_estimation_sans_hypothese_est_une_erreur()
        {
            var q = TestData.Rebar(1, 10, Reliability.Estimated);
            q.Assumption = null;
            BoqResult r = engine.Build(new[] { q }, TestData.Config());
            Assert.Contains(r.Issues, i => i.Code == "ESTIMATION_SANS_HYPOTHESE" && i.ElementIds.Contains(1));
        }

        [Fact]
        public void Une_quantite_manquante_rend_la_ligne_manquante()
        {
            var missing = TestData.Concrete(2, 0.0);
            missing.Reliability = Reliability.Missing;
            BoqResult r = engine.Build(new[] { TestData.Concrete(1, 1.0), missing }, TestData.Config());
            Assert.Equal(Reliability.Missing, r.AllLines.Single().Reliability);
            Assert.Contains(r.Issues, i => i.Code == "QUANTITE_MANQUANTE" && i.ElementIds.SequenceEqual(new long[] { 2 }));
        }

        [Fact]
        public void Pas_d_indice_sans_montant()
        {
            Assert.Null(engine.Build(new[] { TestData.Concrete(1, 1.0) }, TestData.Config()).ReliabilityIndex);
        }
    }
}
