using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Planning;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class UnitTimesAndDescriptiveTests
    {
        private static ElementQuantity Concrete(long id, string material, double m3, string type, string level) => new ElementQuantity
        {
            ElementId = id, Category = "OST_StructuralColumns", Kind = QuantityKinds.ConcreteVolume, Unit = BoqUnit.CubicMeter,
            Value = m3, Material = material, Family = "Poteau béton", TypeName = type, Level = level, MethodCode = "T"
        };

        private static (BoqResult Boq, BoqConfiguration Config) Model()
        {
            BoqConfiguration c = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            c.Prices = new PriceLibrary
            {
                SchemaVersion = 1, Name = "t",
                Prices =
                {
                    new PriceEntry { Code = "03.01", UnitPrice = 200m, Currency = Currency.USD, Unit = BoqUnit.CubicMeter, Source = "t",
                                     Breakdown = new PriceBreakdown { Materials = 140m, Labour = 48m, Equipment = 12m } }
                }
            };
            BoqResult boq = new BoqEngine().Build(new[]
            {
                Concrete(1, "Béton C25/30", 10, "Poteau 30x30", "RDC"), Concrete(2, "Béton C25/30", 4, "Poteau 30x30", "Étage 1"),
                Concrete(3, "Béton C25/30", 2, "Poteau 40x40", "RDC")
            }, c);
            return (boq, c);
        }

        [Fact]
        public void Temps_unitaire_heures_homme_par_unite_et_taux_horaire_implicite()
        {
            var (boq, c) = Model();
            var settings = new PlanningSettings(); // rendements par defaut : 03.* m3 = 6 /j ; 8 h/j ; equipe type lot 03 = 6
            var rows = UnitTimes.Build(boq, settings, c.Prices);
            UnitTimeRow r = rows.Single(x => x.ArticleCode == "03.01");
            Assert.Equal(6, r.PerDay, 6);
            Assert.Equal(6, r.Workers);
            Assert.Equal(8.0 * 6 / 6, r.UnitHours, 6);                      // 8 h.h / m3
            Assert.Equal(r.UnitHours * (double)r.Quantity, r.TotalHours, 6);
            Assert.Equal((double)r.Quantity / 6, r.CrewDays, 6);
            Assert.Equal(48m, r.LabourPerUnit);
            Assert.Equal(6m, r.ImpliedHourlyRate);                          // 48 / 8 h
            Assert.False(r.IsDefaultRate);
            // Effectif et heures par jour du projet.
            settings.HoursPerDay = 9;
            settings.CrewSizes["03"] = 4;
            UnitTimeRow r2 = UnitTimes.Build(boq, settings).Single(x => x.ArticleCode == "03.01");
            Assert.Equal(9.0 * 4 / 6, r2.UnitHours, 6);
            Assert.Equal(rows.Sum(x => x.TotalHours), UnitTimes.ByLot(rows).Sum(l => l.Hours), 6);
        }

        [Fact]
        public void BOQ_descriptif_composition_reelle_et_mode_de_metre()
        {
            var (boq, c) = Model();
            var rows = DescriptiveBoq.Build(boq, c.Mapping);
            // Composition reelle de l'article (une ou plusieurs lignes selon la ventilation du catalogue).
            var lines = rows.Where(x => x.Code.StartsWith("03.01")).ToList();
            Assert.Contains(lines, r => r.Composition.Contains("Poteau béton : Poteau 30x30 (2)") && r.Composition.Contains("RDC") && r.Composition.Contains("Étage 1"));
            Assert.Contains(lines, r => r.Composition.Contains("Poteau béton : Poteau 40x40 (1)") && r.Composition.Contains("Béton C25/30"));
            Assert.All(lines, r =>
            {
                Assert.Contains("Volume (m³)", r.Measurement);
                Assert.Contains("Fiabilité", r.Measurement);
                Assert.False(string.IsNullOrWhiteSpace(r.Prescription));
            });
            Assert.Equal(3, lines.Sum(r => r.ElementCount));
            Assert.Equal(boq.AllLines.Count(l => l.Quantity != 0m), rows.Count);
        }

        [Theory]
        [InlineData("Coffrage des poteaux", "Coffrage")]
        [InlineData("Armatures HA", "aciers")]
        [InlineData("Enduit extérieur", "Enduit")]
        [InlineData("Porte bois 0,90 x 2,10", "menuiserie")]
        public void Texte_type_selon_la_nature_de_l_ouvrage(string designation, string expected)
        {
            string t = DescriptiveBoq.DefaultPrescription("03", designation, BoqUnit.SquareMeter);
            Assert.Contains(expected, t);
            Assert.Contains("à préciser dans le CCTP", t);
        }
    }
}
