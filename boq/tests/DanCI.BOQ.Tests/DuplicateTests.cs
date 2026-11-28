using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class DuplicateTests
    {
        private static ElementQuantity Q(long id, string type, string position, double value = 2.0, string kind = QuantityKinds.ConcreteVolume, string link = null)
        {
            var q = new ElementQuantity { ElementId = id, Category = "OST_Floors", CategoryLabel = "Sols", TypeName = type, Kind = kind, Unit = BoqUnit.CubicMeter, Value = value, MethodCode = "SOLID" };
            q.Parameters["Position"] = position;
            if (link != null) q.Parameters[ElementRef.LinkInstanceParameter] = link;
            return q;
        }

        [Fact]
        public void Quantite_relevee_deux_fois_retiree_et_signalee()
        {
            var list = new List<ElementQuantity>
            {
                Q(1, "Dalle 20", "0,0,0;1000,1000,200"),
                Q(1, "Dalle 20", "0,0,0;1000,1000,200"),                       // meme element, meme mesure : doublon
                Q(1, "Dalle 20", "0,0,0;1000,1000,200", 3.0, QuantityKinds.Formwork),   // autre nature : garde
                Q(1, "Dalle 20", "0,0,0;1000,1000,200", link: "77"),           // autre instance de lien : garde
            };
            var notes = new List<BoqIssue>();
            Assert.Equal(1, QuantityDedup.Apply(list, notes));
            Assert.Equal(3, list.Count);
            Assert.Contains(notes, n => n.Code == "QUANTITE_EN_DOUBLE" && n.ElementIds.SequenceEqual(new long[] { 1 }));
        }

        [Fact]
        public void Superposition_de_types_differents_signalee_comme_doublon_probable()
        {
            var list = new List<ElementQuantity>
            {
                Q(1, "Dalle 20", "0,0,0;10000,8000,200"),
                Q(2, "Dalle BA 20cm", "10,0,0;10000,8000,200"),                  // meme dalle, autre type
                Q(3, "Balcon", "2000,2000,0;3000,3000,200"),                     // petite dalle dans l'emprise : pas un doublon
                Q(4, "Dalle 20", "0,0,3000;10000,8000,3200"),                    // etage au-dessus : pas un doublon
            };
            var pairs = OverlapCheck.Find(list);
            Assert.Single(pairs);
            Assert.Equal(new long[] { 1, 2 }, new[] { pairs[0].A.ElementId, pairs[0].B.ElementId }.OrderBy(x => x));

            AuditReport audit = BoqAuditor.Run(list, null, null, new ExtractionSettings());
            Assert.Contains(audit.Findings, f => f.Code == "DOUBLON_PROBABLE" && f.Section == AuditSection.Doublons);
        }

        [Fact]
        public void Doublon_exact_meme_type_pas_signale_deux_fois()
        {
            var list = new List<ElementQuantity> { Q(1, "Dalle 20", "0,0,0;1000,1000,200"), Q(2, "Dalle 20", "0,0,0;1000,1000,200") };
            Assert.Empty(OverlapCheck.Find(list));                                         // deja DOUBLON_POSITION
            AuditReport audit = BoqAuditor.Run(list, null, null, new ExtractionSettings());
            Assert.Contains(audit.Findings, f => f.Code == "DOUBLON_POSITION");
        }
    }
}
