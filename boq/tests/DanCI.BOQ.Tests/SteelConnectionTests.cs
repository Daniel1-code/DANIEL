using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class SteelConnectionTests
    {
        private static ElementQuantity Profile(long id, double kg, string doc = "") => new ElementQuantity
        {
            SourceDocument = doc, ElementId = id, Category = "OST_StructuralFraming", Kind = QuantityKinds.StructuralSteel, Unit = BoqUnit.Kilogram, Value = kg,
            TypeName = "IPE 300", MethodCode = "CUT_LENGTH_X_TYPE_MASS"
        };

        private static ElementQuantity Share(long id, double kg, string doc = "") => new ElementQuantity
        {
            SourceDocument = doc, ElementId = id, Category = "OST_StructuralFraming", Kind = QuantityKinds.SteelConnections, Unit = BoqUnit.Kilogram, Value = kg,
            MethodCode = SteelConnectionRules.ShareMethod, Reliability = Reliability.Estimated, Assumption = "ESTIMÉ"
        };

        private static ElementQuantity Connection(long id, string kind, string type, string linked = null, string doc = "", double? kg = null)
        {
            var q = new ElementQuantity
            {
                SourceDocument = doc, ElementId = id, Category = "OST_StructConnections", TypeName = type, MethodCode = "T",
                Kind = kg.HasValue ? QuantityKinds.SteelConnections : QuantityKinds.Count, Unit = kg.HasValue ? BoqUnit.Kilogram : BoqUnit.Each, Value = kg ?? 1
            };
            q.Parameters[SteelConnectionRules.Parameter] = kind;
            if (linked != null) q.Parameters[SteelConnectionRules.ConnectedParameter] = linked;
            return q;
        }

        [Fact]
        public void Assemblages_modelises_comptes_par_type_et_masse_en_04_02()
        {
            var config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            Assert.True(config.Extraction.Steel.ModeledConnections);
            var q = new List<ElementQuantity>
            {
                Profile(1, 300), Profile(2, 250),
                Connection(10, SteelConnectionRules.Connection, "Platine d'about 20 mm", "1,2"),
                Connection(10, SteelConnectionRules.Connection, "Platine d'about 20 mm", "1,2", kg: 18.5),
                Connection(11, SteelConnectionRules.Connection, "Platine d'about 20 mm", "1"),
                Connection(12, SteelConnectionRules.Connection, "Pied de poteau articulé"),
                Connection(20, SteelConnectionRules.Fastener, "Boulon M20 HR 8.8"),
                Connection(21, SteelConnectionRules.Fastener, "Boulon M20 HR 8.8"),
                Connection(22, SteelConnectionRules.Fastener, "Tige d'ancrage M24"),
            };
            BoqResult boq = new BoqEngine().Build(q, config);
            var u = boq.AllLines.Where(l => l.ArticleCode == "04.03").ToList();
            Assert.Equal(2, u.Count);                                                   // un par type
            Assert.Equal(2m, u.Single(l => l.Designation.Contains("Platine d'about 20 mm")).Quantity);
            var bolts = boq.AllLines.Where(l => l.ArticleCode == "04.04").ToList();
            Assert.Equal(2m, bolts.Single(l => l.Designation.Contains("Boulon M20")).Quantity);
            Assert.Equal(1m, bolts.Single(l => l.Designation.Contains("Tige d'ancrage M24")).Quantity);
            Assert.Equal(18.5m, boq.AllLines.Single(l => l.ArticleCode == "04.02").NetQuantity);
            Assert.DoesNotContain(boq.AllLines, l => l.IsUnclassified);
        }

        [Fact]
        public void Estimation_en_pourcentage_retiree_quand_les_assemblages_sont_modelises()
        {
            var s = new SteelSettings { ConnectionsShare = 0.10m };
            // Assemblage acier Revit relie au profile 1 : seule l'estimation du profile 1 est retiree.
            var q = new List<ElementQuantity> { Share(1, 30), Share(2, 25), Connection(10, SteelConnectionRules.Connection, "Platine", "1") };
            var notes = new List<BoqIssue>();
            Assert.Equal(1, SteelConnectionRules.Reconcile(q, s, notes));
            Assert.DoesNotContain(q, x => x.ElementId == 1 && x.MethodCode == SteelConnectionRules.ShareMethod);
            Assert.Contains(q, x => x.ElementId == 2 && x.MethodCode == SteelConnectionRules.ShareMethod);
            Assert.Contains(notes, n => n.Code == "ASSEMBLAGES_MODELISES");
            // Famille d'assemblage sans lien connu : toutes les estimations du document sont retirees ; un lien RVT garde les siennes.
            var q2 = new List<ElementQuantity> { Share(1, 30), Share(2, 25), Share(3, 10, "lien.rvt"), Connection(30, SteelConnectionRules.Connection, "Gousset") };
            Assert.Equal(2, SteelConnectionRules.Reconcile(q2, s, null));
            Assert.Single(q2, x => x.MethodCode == SteelConnectionRules.ShareMethod);
            // Option : garder l'estimation.
            var q3 = new List<ElementQuantity> { Share(1, 30), Connection(30, SteelConnectionRules.Connection, "Gousset") };
            Assert.Equal(0, SteelConnectionRules.Reconcile(q3, new SteelSettings { KeepShareWhenModeled = true }, null));
            // Sans assemblage modelise : rien ne change.
            var q4 = new List<ElementQuantity> { Share(1, 30) };
            Assert.Equal(0, SteelConnectionRules.Reconcile(q4, s, null));
        }
    }
}
