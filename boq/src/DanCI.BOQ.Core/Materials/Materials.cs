using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Reference;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Materials
{
    /// <summary>Composition d'un m³ de beton pour une classe (motif sur la classe : « C25/30 », « * »).</summary>
    public sealed class ConcreteMix
    {
        [JsonPropertyName("classe")] public PatternList Class { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("cimentKgParM3")] public decimal CementKg { get; set; }
        [JsonPropertyName("sableM3ParM3")] public decimal SandM3 { get; set; }
        [JsonPropertyName("gravierM3ParM3")] public decimal GravelM3 { get; set; }
        [JsonPropertyName("eauLParM3")] public decimal WaterL { get; set; }
    }

    /// <summary>Mortier : ciment et sable par m³ de mortier.</summary>
    public sealed class MortarMix
    {
        [JsonPropertyName("cimentKgParM3")] public decimal CementKg { get; set; }
        [JsonPropertyName("sableM3ParM3")] public decimal SandM3 { get; set; }
    }

    /// <summary>
    /// Contenu de materiaux.json : HYPOTHESES de conversion des quantites du metre en
    /// approvisionnements. Toutes sont a valider par l'ingenieur (dosages de l'etude, du
    /// fournisseur, du chantier) ; la liste produite le rappelle.
    /// </summary>
    public sealed class MaterialSettings
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        /// <summary>Premier dosage dont le motif correspond a la classe du beton (ordre du fichier).</summary>
        [JsonPropertyName("betons")] public List<ConcreteMix> Concretes { get; set; } = new List<ConcreteMix>();
        [JsonPropertyName("betonProprete")] public ConcreteMix Blinding { get; set; }
        [JsonPropertyName("sacCimentKg")] public decimal CementBagKg { get; set; } = 50m;
        [JsonPropertyName("longueurBarreM")] public decimal BarLengthM { get; set; } = 12m;
        [JsonPropertyName("blocsParM2")] public decimal BlocksPerM2 { get; set; } = 12.5m;
        [JsonPropertyName("mortierPoseM3ParM2")] public decimal LayingMortarM3PerM2 { get; set; } = 0.02m;
        [JsonPropertyName("mortierPose")] public MortarMix LayingMortar { get; set; } = new MortarMix { CementKg = 300m, SandM3 = 1.1m };
        [JsonPropertyName("enduitEpaisseurM")] public decimal PlasterThicknessM { get; set; } = 0.015m;
        [JsonPropertyName("mortierEnduit")] public MortarMix PlasterMortar { get; set; } = new MortarMix { CementKg = 400m, SandM3 = 1.1m };
        [JsonPropertyName("coffrageReemplois")] public decimal FormworkReuses { get; set; } = 4m;
        [JsonPropertyName("panneauCoffrageM2")] public decimal FormworkPanelM2 { get; set; } = 2.9768m;
        [JsonPropertyName("peintureCouches")] public decimal PaintCoats { get; set; } = 2m;
        [JsonPropertyName("peintureRendementM2ParL")] public decimal PaintCoverageM2PerL { get; set; } = 10m;
    }

    /// <summary>Une ligne de la liste des materiaux a approvisionner.</summary>
    public sealed class MaterialLine
    {
        public string Group { get; set; }
        public string Material { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        /// <summary>Calcul en clair (quantite source, dosage, pertes) : rien n'est cache.</summary>
        public string Basis { get; set; }
        public bool FromEstimate { get; set; }
    }

    public sealed class MaterialList
    {
        public List<MaterialLine> Lines { get; } = new List<MaterialLine>();
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Totaux par materiau (ciment en sacs, sable, gravier...).</summary>
        public List<MaterialLine> Totals { get; } = new List<MaterialLine>();
    }

    /// <summary>
    /// Approvisionnements a partir du BOQ : quantites RETENUES (exclus non compris), majorees des
    /// coefficients de pertes de leur ligne (quantite brute). Hypotheses de materiaux.json.
    /// </summary>
    public static class MaterialsCalculator
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static MaterialList Build(BoqResult boq, MaterialSettings s)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            s = s ?? new MaterialSettings();
            var list = new MaterialList();
            // Chaque contribution avec le coefficient de pertes de sa ligne.
            var items = boq.AllLines.SelectMany(l => l.Contributions.Select(c => (q: c.Element, factor: l.CoefficientFactor, line: l))).ToList();

            // ---- Beton par classe
            foreach (var g in items.Where(i => i.q.Kind == QuantityKinds.ConcreteVolume && i.q.Unit == BoqUnit.CubicMeter)
                                   .GroupBy(i => Param(i.q, "ClasseBeton") ?? "(classe non renseignée)").OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                ConcreteMix mix = s.Concretes?.FirstOrDefault(m => m.Class == null || m.Class.IsEmpty || m.Class.IsMatch(g.Key));
                decimal volume = g.Sum(i => (decimal)i.q.Value * i.factor);
                AddConcrete(list, s, "Béton " + g.Key, volume, mix, g.Any(i => i.q.Reliability != Reliability.Measured));
            }
            decimal blinding = items.Where(i => i.q.Kind == QuantityKinds.BlindingConcrete && i.q.Unit == BoqUnit.CubicMeter).Sum(i => (decimal)i.q.Value * i.factor);
            if (blinding > 0m)
            {
                AddConcrete(list, s, "Béton de propreté", blinding, s.Blinding, true);
            }

            // ---- Armatures par diametre : masse, longueur, barres du commerce
            foreach (var g in items.Where(i => i.q.Kind == QuantityKinds.Reinforcement && i.q.Unit == BoqUnit.Kilogram)
                                   .GroupBy(i => int.TryParse(Param(i.q, "DiametreMm"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) ? d : 0)
                                   .OrderBy(g => g.Key))
            {
                decimal kg = g.Sum(i => (decimal)i.q.Value * i.factor);
                bool estimated = g.Any(i => i.q.Reliability != Reliability.Measured);
                string name = g.Key > 0 ? "Acier HA Ø" + g.Key : "Acier HA (diamètre non renseigné)";
                list.Lines.Add(new MaterialLine
                {
                    Group = "Armatures", Material = name, Quantity = Math.Ceiling(kg), Unit = "kg", FromEstimate = estimated,
                    Basis = "Masse du métré, pertes comprises" + (estimated ? " (en partie ESTIMÉE par ratio)" : string.Empty)
                });
                if (g.Key > 0 && RebarMass.TryGet(g.Key, out decimal kgPerM) && kgPerM > 0m && s.BarLengthM > 0m)
                {
                    decimal length = kg / kgPerM;
                    decimal bars = Math.Ceiling(length / s.BarLengthM);
                    list.Lines.Add(new MaterialLine
                    {
                        Group = "Armatures", Material = name + " — barres de " + s.BarLengthM.ToString("0.##", Fr) + " m", Quantity = bars, Unit = "barres",
                        FromEstimate = estimated,
                        Basis = N(kg, 0) + " kg ÷ " + kgPerM.ToString("0.###", Fr) + " kg/m = " + N(length, 1) + " m ÷ " + s.BarLengthM.ToString("0.##", Fr) +
                                " m (chutes de façonnage non comprises au-delà du coefficient de pertes)"
                    });
                }
            }

            // ---- Maconnerie : blocs et mortier de pose
            decimal masonry = items.Where(i => i.q.Kind == QuantityKinds.Masonry && i.q.Unit == BoqUnit.SquareMeter).Sum(i => (decimal)i.q.Value * i.factor);
            if (masonry > 0m)
            {
                list.Lines.Add(new MaterialLine
                {
                    Group = "Maçonnerie", Material = "Blocs de ciment", Quantity = Math.Ceiling(masonry * s.BlocksPerM2), Unit = "U",
                    Basis = N(masonry, 2) + " m² × " + s.BlocksPerM2.ToString("0.##", Fr) + " blocs/m² (pertes comprises)"
                });
                AddMortar(list, s, "Mortier de pose", masonry * s.LayingMortarM3PerM2, s.LayingMortar,
                    N(masonry, 2) + " m² × " + s.LayingMortarM3PerM2.ToString("0.###", Fr) + " m³/m²");
            }

            // ---- Enduits
            decimal plaster = items.Where(i => i.q.Kind == QuantityKinds.Plaster && i.q.Unit == BoqUnit.SquareMeter).Sum(i => (decimal)i.q.Value * i.factor);
            if (plaster > 0m)
            {
                AddMortar(list, s, "Mortier d'enduit", plaster * s.PlasterThicknessM, s.PlasterMortar,
                    N(plaster, 2) + " m² × " + (s.PlasterThicknessM * 1000m).ToString("0.#", Fr) + " mm");
            }

            // ---- Coffrage : panneaux avec reemplois
            decimal formwork = items.Where(i => i.q.Kind == QuantityKinds.Formwork && i.q.Unit == BoqUnit.SquareMeter).Sum(i => (decimal)i.q.Value * i.factor);
            if (formwork > 0m && s.FormworkReuses > 0m && s.FormworkPanelM2 > 0m)
            {
                list.Lines.Add(new MaterialLine
                {
                    Group = "Coffrage", Material = "Panneaux de coffrage (" + s.FormworkPanelM2.ToString("0.##", Fr) + " m²)",
                    Quantity = Math.Ceiling(formwork / s.FormworkReuses / s.FormworkPanelM2), Unit = "U",
                    Basis = N(formwork, 2) + " m² ÷ " + s.FormworkReuses.ToString("0.#", Fr) + " réemplois ÷ " + s.FormworkPanelM2.ToString("0.##", Fr) + " m²/panneau"
                });
            }

            // ---- Peinture
            decimal paint = items.Where(i => i.q.Kind == QuantityKinds.Paint && i.q.Unit == BoqUnit.SquareMeter).Sum(i => (decimal)i.q.Value * i.factor);
            if (paint > 0m && s.PaintCoverageM2PerL > 0m)
            {
                list.Lines.Add(new MaterialLine
                {
                    Group = "Peinture", Material = "Peinture", Quantity = Math.Ceiling(paint * s.PaintCoats / s.PaintCoverageM2PerL), Unit = "L",
                    Basis = N(paint, 2) + " m² × " + s.PaintCoats.ToString("0.#", Fr) + " couches ÷ " + s.PaintCoverageM2PerL.ToString("0.#", Fr) + " m²/L"
                });
            }

            // ---- Totaux par materiau de base
            foreach (var g in list.Lines.Where(l => l.Group == "Liants et granulats").GroupBy(l => (l.Material.Split('—')[0].Trim(), l.Unit)))
            {
                list.Totals.Add(new MaterialLine { Group = "Total", Material = g.Key.Item1, Unit = g.Key.Unit, Quantity = g.Sum(l => l.Quantity), Basis = "Somme des lignes" });
            }
            decimal cementKg = list.Totals.Where(t => t.Material == "Ciment" && t.Unit == "kg").Sum(t => t.Quantity);
            if (cementKg > 0m && s.CementBagKg > 0m)
            {
                list.Totals.Add(new MaterialLine
                {
                    Group = "Total", Material = "Ciment en sacs de " + s.CementBagKg.ToString("0.#", Fr) + " kg", Unit = "sacs",
                    Quantity = Math.Ceiling(cementKg / s.CementBagKg), Basis = N(cementKg, 0) + " kg ÷ " + s.CementBagKg.ToString("0.#", Fr) + " kg"
                });
            }
            list.Notes.Add("Estimation des APPROVISIONNEMENTS à partir du métré (quantités brutes, pertes comprises) et des dosages de config\\materiaux.json : " +
                           "hypothèses à valider par l'ingénieur (étude de formulation, fournisseur, chantier) avant toute commande.");
            if (list.Lines.Any(l => l.FromEstimate))
            {
                list.Notes.Add("Certaines quantités viennent en partie d'ESTIMATIONS (ratios d'armatures, classe de béton non renseignée…) : voir la colonne Calcul.");
            }
            return list;
        }

        private static void AddConcrete(MaterialList list, MaterialSettings s, string name, decimal volume, ConcreteMix mix, bool estimated)
        {
            list.Lines.Add(new MaterialLine
            {
                Group = "Béton", Material = name, Quantity = Math.Round(volume, 2, MidpointRounding.AwayFromZero), Unit = "m³", FromEstimate = estimated,
                Basis = "Volume du métré, pertes comprises" + (mix == null ? " — AUCUN dosage pour cette classe dans materiaux.json" : " — dosage : " + (mix.Designation ?? mix.Class?.ToString()))
            });
            if (mix == null) return;
            string src = N(volume, 2) + " m³ de " + name.ToLower(Fr);
            Add(list, "Ciment — " + name, volume * mix.CementKg, "kg", src + " × " + mix.CementKg.ToString("0.#", Fr) + " kg/m³", estimated);
            Add(list, "Sable — " + name, volume * mix.SandM3, "m³", src + " × " + mix.SandM3.ToString("0.###", Fr) + " m³/m³", estimated);
            Add(list, "Gravier — " + name, volume * mix.GravelM3, "m³", src + " × " + mix.GravelM3.ToString("0.###", Fr) + " m³/m³", estimated);
            Add(list, "Eau — " + name, volume * mix.WaterL, "L", src + " × " + mix.WaterL.ToString("0.#", Fr) + " L/m³", estimated);
        }

        private static void AddMortar(MaterialList list, MaterialSettings s, string name, decimal m3, MortarMix mix, string basis)
        {
            if (mix == null || m3 <= 0m) return;
            Add(list, "Ciment — " + name.ToLower(Fr), m3 * mix.CementKg, "kg", basis + " = " + N(m3, 3) + " m³ × " + mix.CementKg.ToString("0.#", Fr) + " kg/m³", false);
            Add(list, "Sable — " + name.ToLower(Fr), m3 * mix.SandM3, "m³", basis + " = " + N(m3, 3) + " m³ × " + mix.SandM3.ToString("0.###", Fr) + " m³/m³", false);
        }

        private static void Add(MaterialList list, string material, decimal qty, string unit, string basis, bool estimated)
        {
            if (qty <= 0m) return;
            list.Lines.Add(new MaterialLine
            {
                Group = "Liants et granulats", Material = material, Unit = unit, FromEstimate = estimated, Basis = basis,
                Quantity = unit == "kg" || unit == "L" ? Math.Ceiling(qty) : Math.Round(qty, 2, MidpointRounding.AwayFromZero)
            });
        }

        private static string Param(ElementQuantity q, string key) =>
            q.Parameters != null && q.Parameters.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private static string N(decimal v, int d) => v.ToString("N" + d, Fr);
    }
}
