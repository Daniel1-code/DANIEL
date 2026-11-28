using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Finitions ESTIMEES « comme un BOQ de projet reel » quand la maquette ne les modelise pas :
    /// carrelage de sol (surface des pieces), plinthes h = 10 cm (deja mesurees par piece),
    /// enduit / crepissage au mortier de ciment sur les deux faces des murs et sur l'ossature
    /// apparente (poteaux, poutres), enduit et peinture sous plafond (surface des pieces),
    /// peinture des murs (interieure et facades), faience des pieces humides.
    /// Chaque quantite reste rattachee a SON element Revit (mur, poteau, poutre, piece) et a son
    /// niveau : l'utilisateur la retrouve et la selectionne comme une quantite mesuree. Elle est
    /// toujours marquee « estimee », avec l'hypothese.
    /// </summary>
    public sealed class EstimatedFinishSettings
    {
        [JsonPropertyName("actif")] public bool Enabled { get; set; } = true;
        /// <summary>Carrelage de sol = surface nette de chaque piece.</summary>
        [JsonPropertyName("carrelageSol")] public bool FloorTiling { get; set; } = true;
        /// <summary>Enduit au mortier de ciment : face interieure et exterieure de chaque mur.</summary>
        [JsonPropertyName("enduitsMurs")] public bool WallPlaster { get; set; } = true;
        /// <summary>Enduit sur les faces apparentes des poteaux et poutres (ossature).</summary>
        [JsonPropertyName("enduitsOssature")] public bool FramePlaster { get; set; } = true;
        /// <summary>Enduit et peinture sous plafond = surface de chaque piece.</summary>
        [JsonPropertyName("plafonds")] public bool Ceilings { get; set; } = true;
        /// <summary>Peinture sur les surfaces enduites (si la maquette n'a pas de materiau peinture).</summary>
        [JsonPropertyName("peinture")] public bool Paint { get; set; } = true;
        [JsonPropertyName("peintureFacades")] public bool ExteriorPaint { get; set; } = true;
        /// <summary>Faience murale des pieces humides (perimetre portes deduites x hauteur).</summary>
        [JsonPropertyName("faience")] public bool WallTiling { get; set; } = true;
        [JsonPropertyName("faienceHauteurM")] public double WallTilingHeightM { get; set; } = 2.10;

        [JsonPropertyName("piecesHumides")] public PatternList WetRooms { get; set; } = new PatternList(new[]
        {
            "*douche*", "*sdb*", "*s.d.b*", "*salle de bain*", "*salle d'eau*", "*salle d’eau*", "*wc*", "*w.c*", "*toilette*", "*bath*", "*shower*"
        });

        [JsonPropertyName("piecesSansCarrelage")] public PatternList NoTilingRooms { get; set; } = new PatternList(new[]
        {
            "*gaine*", "*vide*", "*shaft*", "*toiture*", "*comble*"
        });

        [JsonPropertyName("typesMursSansEnduit")] public PatternList NoPlasterWallTypes { get; set; } = new PatternList(new[]
        {
            "*rideau*", "*curtain*", "*bardage*", "*placo*", "*ba13*", "*plaque de plâtre*", "*plaque de platre*", "*cloison sèche*",
            "*cloison seche*", "*clôture*", "*cloture*", "*vitr*", "*glass*"
        });

        /// <summary>Niveaux sans finitions (fondations, terrain naturel).</summary>
        [JsonPropertyName("niveauxSansFinitions")] public PatternList NoFinishLevels { get; set; } = new PatternList(new[]
        {
            "*fondation*", "*foundation*", "TN", "*terrain naturel*"
        });

        public static bool Matches(PatternList list, string value) =>
            list != null && !list.IsEmpty && !string.IsNullOrEmpty(value) && list.IsMatch(value);
    }

    public static class EstimatedFinishes
    {
        /// <summary>Pseudo-parametre porte par chaque finition estimee (les regles de mapping le lisent).</summary>
        public const string Parameter = "FinitionEstimee";
        public const string Interior = "Interieur";
        public const string Exterior = "Exterieur";
        public const string Ceiling = "Plafond";
        public const string Floor = "Sol";
        public const string WallTiles = "Faience";

        public static bool IsEstimated(ElementQuantity q) =>
            q.Parameters != null && q.Parameters.ContainsKey(Parameter);

        public static string FaceOf(ElementQuantity q) =>
            q.Parameters != null && q.Parameters.TryGetValue(Parameter, out string v) ? v : null;

        /// <summary>Faces d'un mur selon sa fonction : (face, est exterieure). Mur de facade : une face exterieure, une interieure.</summary>
        public static IEnumerable<(string Face, bool Exterior)> WallFaces(bool exteriorWall)
        {
            yield return (exteriorWall ? Exterior : Interior, exteriorWall);
            yield return (Interior, false);
        }

        /// <summary>Faience d'une piece humide : (perimetre - portes) x hauteur, m².</summary>
        public static double WallTilingArea(double perimeterM, IEnumerable<double> doorWidthsM, double heightM)
        {
            double length = SkirtingRule.Length(perimeterM, doorWidthsM?.ToList());
            return Math.Max(0, length) * Math.Max(0, heightM);
        }

        /// <summary>
        /// Retire les estimations qui feraient double emploi avec ce que la maquette MODELISE deja :
        /// - un element qui porte un enduit modelise n'a pas d'enduit estime ; idem pour la peinture ;
        /// - un niveau dont les sols portent un revetement modelise n'a pas de carrelage estime par piece ;
        /// - un niveau dont les murs portent un carrelage / une faience modelise n'a pas de faience estimee.
        /// Retourne le nombre de quantites estimees retirees (signalees dans les notes).
        /// </summary>
        public static int Reconcile(List<ElementQuantity> quantities, List<BoqIssue> notes)
        {
            if (quantities == null || quantities.Count == 0) return 0;
            string Key(ElementQuantity q) => (q.SourceDocument ?? string.Empty) + "|" + q.ElementId;
            string LevelKey(ElementQuantity q) => (q.SourceDocument ?? string.Empty) + "|" + (q.Level ?? string.Empty);

            var modeled = quantities.Where(q => !IsEstimated(q)).ToList();
            var plastered = new HashSet<string>(modeled.Where(q => q.Kind == QuantityKinds.Plaster).Select(Key));
            var painted = new HashSet<string>(modeled.Where(q => q.Kind == QuantityKinds.Paint).Select(Key));
            var tiledFloors = new HashSet<string>(modeled.Where(q => q.Kind == QuantityKinds.FloorFinish && q.Category != "OST_Walls").Select(LevelKey));
            var tiledWalls = new HashSet<string>(modeled.Where(q => q.Kind == QuantityKinds.FloorFinish && q.Category == "OST_Walls").Select(LevelKey));

            var removed = new List<(ElementQuantity Q, string Why)>();
            quantities.RemoveAll(q =>
            {
                if (!IsEstimated(q)) return false;
                string face = FaceOf(q);
                string why = null;
                if (q.Kind == QuantityKinds.Plaster && plastered.Contains(Key(q))) why = "enduit déjà modélisé sur l'élément";
                else if (q.Kind == QuantityKinds.Paint && painted.Contains(Key(q))) why = "peinture déjà modélisée sur l'élément";
                else if (face == Floor && tiledFloors.Contains(LevelKey(q))) why = "revêtement de sol modélisé sur ce niveau";
                else if (face == WallTiles && tiledWalls.Contains(LevelKey(q))) why = "carrelage mural modélisé sur ce niveau";
                if (why == null) return false;
                removed.Add((q, why));
                return true;
            });
            foreach (var g in removed.GroupBy(x => x.Why))
            {
                var issue = new BoqIssue(IssueSeverity.Info, "FINITION_ESTIMEE_NON_RETENUE",
                    g.Count() + " finition(s) estimée(s) non retenue(s) : " + g.Key + " (la quantité modélisée est comptée, pas l'estimation).");
                issue.ElementIds.AddRange(g.Select(x => x.Q.ElementId).Distinct());
                notes?.Add(issue);
            }
            return removed.Count;
        }
    }
}
