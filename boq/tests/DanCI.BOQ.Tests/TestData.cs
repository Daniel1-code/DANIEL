using System;
using System.Collections.Generic;
using System.IO;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Tests
{
    /// <summary>Petite configuration ecrite en code pour des tests lisibles.</summary>
    internal static class TestData
    {
        public static BoqConfiguration Config()
        {
            return new BoqConfiguration
            {
                Mapping = new MappingCatalog
                {
                    SchemaVersion = 1,
                    Lots =
                    {
                        new Lot { Code = "00", Designation = "Installation de chantier" },
                        new Lot { Code = "03", Designation = "Gros œuvre" },
                        new Lot { Code = "05", Designation = "Maçonnerie" }
                    },
                    Articles =
                    {
                        new Article { Code = "03.01", Lot = "03", Unit = BoqUnit.CubicMeter, Designation = "Béton C25/30 poteaux" },
                        new Article { Code = "03.02", Lot = "03", Unit = BoqUnit.CubicMeter, Designation = "Béton C25/30 poutres" },
                        new Article { Code = "03.08", Lot = "03", Unit = BoqUnit.Kilogram, Designation = "Armatures HA" },
                        new Article { Code = "05.01", Lot = "05", Unit = BoqUnit.SquareMeter, Designation = "Blocs de 20" }
                    },
                    Rules =
                    {
                        Rule("POT", 10, "03.01", kind: "VolumeBeton", category: "OST_StructuralColumns"),
                        Rule("POU", 20, "03.02", kind: "VolumeBeton", category: "OST_StructuralFraming"),
                        Rule("ARM", 30, "03.08", kind: "Armature"),
                        Rule("MAC", 40, "05.01", kind: "Maconnerie")
                    }
                },
                Coefficients = new CoefficientSet { SchemaVersion = 1 },
                Prices = new PriceLibrary { SchemaVersion = 1 },
                Settings = new EstimateSettings
                {
                    SchemaVersion = 1,
                    Currency = Currency.USD,
                    VatPercent = 16m,
                    PricedQuantity = PricedQuantity.Brute
                }
            };
        }

        public static MappingRule Rule(string id, int order, string article, string kind = null, string category = null,
            string material = null, string type = null)
        {
            return new MappingRule
            {
                Id = id,
                Order = order,
                Article = article,
                When = new RuleCriteria
                {
                    Kind = List(kind),
                    Category = List(category),
                    Material = List(material),
                    TypeName = List(type)
                }
            };
        }

        public static PatternList List(params string[] values) =>
            values == null || values.Length == 0 || values[0] == null ? null : new PatternList(values);

        public static PriceEntry Price(string code, BoqUnit unit, decimal pu, Currency currency = Currency.USD) =>
            new PriceEntry { Code = code, Unit = unit, UnitPrice = pu, Currency = currency, Date = new DateTime(2026, 11, 1), Source = "test" };

        public static ElementQuantity Concrete(long id, double m3, string category = "OST_StructuralColumns",
            Reliability reliability = Reliability.Measured) =>
            new ElementQuantity
            {
                ElementId = id,
                Category = category,
                TypeName = "Poteau 30x30",
                Material = "Béton C25/30",
                Kind = QuantityKinds.ConcreteVolume,
                Value = m3,
                Unit = BoqUnit.CubicMeter,
                MethodCode = "SOLID_VOLUME_BY_MATERIAL",
                Reliability = reliability,
                Assumption = reliability == Reliability.Estimated ? "hypothèse de test" : null
            };

        public static ElementQuantity Rebar(long id, double kg, Reliability reliability) =>
            new ElementQuantity
            {
                ElementId = id,
                Category = "OST_StructuralColumns",
                Kind = QuantityKinds.Reinforcement,
                Value = kg,
                Unit = BoqUnit.Kilogram,
                Reliability = reliability,
                Assumption = reliability == Reliability.Estimated ? "ratio 120 kg/m³ (poteaux)" : null
            };

        public static string ConfigDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "config", BoqConfiguration.MappingFileName);
                if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "DanCI.BOQ.sln")))
                {
                    return Path.Combine(dir.FullName, "config");
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("boq/config introuvable");
        }
    }
}
