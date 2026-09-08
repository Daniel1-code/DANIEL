using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Lit une volee d'escalier Revit et la traduit en <see cref="StairData"/>.
    ///
    /// DEUX SELECTIONS SONT POSSIBLES, ET ELLES NE SERVENT PAS A LA MEME CHOSE.
    ///
    /// - Un **escalier Revit** porte toute la geometrie utile : nombre de contremarches,
    ///   hauteur de contremarche, giron, largeur de volee. Le lecteur la reprend telle
    ///   quelle, sans rien deviner.
    /// - Un **plancher structurel incline** modelisant la paillasse est, lui, un hote
    ///   d'armatures. Il ne porte en revanche aucune information de marche : la geometrie
    ///   de la marche reste alors celle saisie dans la fenetre.
    ///
    /// La question « cet element accepte-t-il des armatures ? » n'est jamais supposee :
    /// elle est posee a l'API, comme pour tous les autres modules, et la reponse est
    /// rapportee telle qu'elle vient. Si l'escalier la refuse, le calcul, les quantitatifs
    /// et la note restent produits — c'est la pose des barres, et elle seule, qui demande
    /// un hote valide.
    /// </summary>
    public static class StairReader
    {
        private const double Tolerance = 1e-6;

        public static RevitStair TryRead(Element element, StairData defaults, out string error)
        {
            error = null;
            if (element == null)
            {
                error = "Element nul.";
                return null;
            }

            var stairs = element as Stairs;
            if (stairs != null) return ReadStairs(stairs, defaults, out error);

            if (element.Category != null
                && element.Category.Id.Value == (long)BuiltInCategory.OST_Floors)
            {
                return ReadFlightModelledAsFloor(element, defaults, out error);
            }

            error = "L'element n'est ni un escalier, ni un plancher modelisant une paillasse.";
            return null;
        }

        // ------------------------------------------------------------------
        // Escalier Revit
        // ------------------------------------------------------------------

        private static RevitStair ReadStairs(Stairs stairs, StairData defaults, out string error)
        {
            error = null;
            StairData data = Clone(defaults);
            data.Id = stairs.UniqueId;
            data.Name = ColumnReader.Describe(stairs);
            data.Mark = ColumnReader.ReadMark(stairs);
            data.Provenance = new StairGeometryProvenance();

            try
            {
                // LE NOMBRE DE CONTREMARCHES EST CELUI DE LA VOLEE, PAS DE L'ESCALIER.
                // Sur un escalier a plusieurs volees, stairs.ActualRisersNumber les compte
                // toutes : la projection horizontale et la portee etaient alors trop
                // grandes d'une volee entiere.
                int risers = ReadRunRisers(stairs);
                if (risers <= 0) risers = stairs.ActualRisersNumber;
                if (risers > 0)
                {
                    data.RiserCount = risers;
                    data.Provenance.Set(StairDimension.RiserCount,
                                        StairDimensionSource.ReadFromModel);
                }

                double riser = UnitConverter.FeetToMm(stairs.ActualRiserHeight);
                double tread = UnitConverter.FeetToMm(stairs.ActualTreadDepth);
                if (riser > Tolerance)
                {
                    data.RiserHeightMm = riser;
                    data.Provenance.Set(StairDimension.RiserHeight,
                                        StairDimensionSource.ReadFromModel);
                }
                if (tread > Tolerance)
                {
                    data.TreadDepthMm = tread;
                    data.Provenance.Set(StairDimension.TreadDepth,
                                        StairDimensionSource.ReadFromModel);
                }

                data.Remarks.Add(string.Format(
                    "Geometrie lue sur l'escalier Revit : {0} contremarches de {1:0} mm, " +
                    "giron {2:0} mm.", data.RiserCount, data.RiserHeightMm, data.TreadDepthMm));
            }
            catch (Exception)
            {
                data.Remarks.Add(
                    "La geometrie de marche n'a pas pu etre lue sur l'escalier : les valeurs " +
                    "de la fenetre sont conservees. Verifiez-les avant de calculer.");
            }

            data.Shape = ResolveShape(stairs, data);

            double width = ReadRunWidth(stairs);
            if (width > Tolerance)
            {
                data.WidthMm = width;
                data.Provenance.Set(StairDimension.Width, StairDimensionSource.ReadFromModel);
                data.Remarks.Add(string.Format("Largeur de volee lue : {0:0} mm.", width));
            }
            else
            {
                data.Remarks.Add(
                    "La largeur de volee n'a pas pu etre lue : la valeur de la fenetre est " +
                    "conservee.");
            }

            // L'epaisseur de paillasse est CHERCHEE dans le modele, et seulement supposee
            // si elle ne s'y trouve pas. Elle pilote tout le poids propre : la difference
            // entre une valeur lue et une valeur par defaut n'est pas un detail.
            double waist = ReadStructuralDepth(stairs);
            if (waist > Tolerance)
            {
                data.WaistThicknessMm = waist;
                data.Provenance.Set(StairDimension.WaistThickness,
                                    StairDimensionSource.ReadFromModel);
                data.Remarks.Add(string.Format(
                    "Epaisseur de paillasse lue sur le type de volee : {0:0} mm.", waist));
            }
            else
            {
                data.Remarks.Add(
                    "L'epaisseur de paillasse n'a pas pu etre lue sur ce type de volee : " +
                    "c'est la valeur de la fenetre qui est utilisee, et c'est elle qui " +
                    "pilote tout le poids propre. Verifiez-la.");
            }

            ReadSupportCondition(stairs, data);

            if (stairs.MultistoryStairsId != null
                && stairs.MultistoryStairsId != ElementId.InvalidElementId)
            {
                data.Remarks.Add(
                    "Cet escalier appartient a un escalier multi-etages : verifiez que la " +
                    "volee calculee est bien celle qui vous interesse.");
            }

            var stair = new RevitStair
            {
                Data = data,
                Frame = BuildFrame(stairs, data),
                CanHostRebar = IsValidRebarHost(stairs)
            };

            if (!stair.CanHostRebar)
            {
                stair.RebarHostMessage =
                    "Revit refuse cet escalier comme hote d'armatures : aucune barre ne peut " +
                    "y etre posee. Le calcul, l'apercu, le quantitatif et la note restent " +
                    "produits. Pour poser les armatures dans le modele, modelisez la " +
                    "paillasse par un plancher structurel incline ou un element in situ, puis " +
                    "relancez la commande sur cet element.";
                data.Remarks.Add(stair.RebarHostMessage);
            }

            return stair;
        }

        /// <summary>
        /// Determine la forme de la volee a partir de sa LIGNE DE FOULEE, et non d'un
        /// parametre de type dont le nom pourrait changer d'une version de Revit a l'autre.
        ///
        /// Le critere est geometrique et sans ambiguite : la ligne de foulee d'une volee
        /// droite est un segment de droite unique. Des qu'elle comporte un arc, ou plusieurs
        /// segments non alignes, la volee est balancee ou helicoidale — et le moteur ne sait
        /// pas la calculer.
        ///
        /// Si la ligne de foulee ne peut pas etre lue, la forme reste INDETERMINEE : ce
        /// n'est pas la meme chose que droite, et le moteur le dira.
        /// </summary>
        private static StairFlightShape ResolveShape(Stairs stairs, StairData data)
        {
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null || runs.Count == 0) return StairFlightShape.Undetermined;

                if (runs.Count > 1)
                {
                    data.Remarks.Add(string.Format(
                        "L'escalier compte {0} volees, et le moteur n'en calcule qu'une. Le " +
                        "nombre de contremarches lu est en outre celui de l'ESCALIER ENTIER, " +
                        "pas de la volee : sur un escalier a plusieurs volees il est trop " +
                        "grand, et avec lui la projection horizontale et la portee. Corrigez " +
                        "le nombre de contremarches dans la fenetre pour n'en decrire qu'une " +
                        "volee, ou modelisez chaque paillasse par un plancher structurel.",
                        runs.Count));
                }

                var shape = StairFlightShape.Undetermined;
                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;

                    CurveLoop path = run.GetStairsPath();
                    if (path == null) return StairFlightShape.Undetermined;

                    shape = ClassifyPath(path);
                    break;
                }
                return shape;
            }
            catch (Exception)
            {
                // Le type de volee peut refuser sa ligne de foulee : on ne devine pas.
                return StairFlightShape.Undetermined;
            }
        }

        /// <summary>Classe une ligne de foulee : droite, courbe, ou brisee.</summary>
        private static StairFlightShape ClassifyPath(CurveLoop path)
        {
            var segments = new List<Curve>();
            foreach (Curve curve in path) segments.Add(curve);
            if (segments.Count == 0) return StairFlightShape.Undetermined;

            foreach (Curve curve in segments)
            {
                // Un arc dans la ligne de foulee : la volee tourne.
                if (!(curve is Line)) return StairFlightShape.Spiral;
            }

            if (segments.Count == 1) return StairFlightShape.Straight;

            // Plusieurs segments droits : ils doivent tous etre paralleles, sinon la volee
            // est balancee.
            XYZ reference = segments[0].GetEndPoint(1) - segments[0].GetEndPoint(0);
            if (reference.GetLength() < Tolerance) return StairFlightShape.Undetermined;
            reference = reference.Normalize();

            for (int i = 1; i < segments.Count; i++)
            {
                XYZ direction = segments[i].GetEndPoint(1) - segments[i].GetEndPoint(0);
                if (direction.GetLength() < Tolerance) continue;
                if (Math.Abs(direction.Normalize().DotProduct(reference)) < 0.999)
                {
                    return StairFlightShape.Winder;
                }
            }

            return StairFlightShape.Straight;
        }

        // ------------------------------------------------------------------
        // Ce que l'escalier DESSINE porte reellement
        // ------------------------------------------------------------------

        /// <summary>
        /// Nombre de contremarches de la PREMIERE VOLEE. Sur un escalier a plusieurs
        /// volees, le compte de l'escalier entier decrit un objet qui n'existe pas : une
        /// volee unique de toute la hauteur.
        /// </summary>
        private static int ReadRunRisers(Stairs stairs)
        {
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return 0;
                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;
                    if (run.ActualRisersNumber > 0) return run.ActualRisersNumber;
                }
            }
            catch (Exception)
            {
                // Le type de volee peut refuser ses volees : l'appelant retombera sur le
                // compte de l'escalier entier, en le disant.
            }
            return 0;
        }

        /// <summary>
        /// Epaisseur structurelle de la paillasse, cherchee sur la volee puis sur son type.
        ///
        /// Les parametres sont resolus PAR LEUR NOM D'ENUMERATION, a l'execution : un
        /// identifiant absent d'une version de Revit ne casse alors ni la compilation ni
        /// la lecture, il est simplement ignore. Et comme ce sont des parametres integres
        /// et non des libelles, la lecture ne depend pas de la langue de l'interface.
        /// </summary>
        private static double ReadStructuralDepth(Stairs stairs)
        {
            string[] candidates =
            {
                "STAIRS_RUNTYPE_STRUCTURAL_DEPTH",
                "STAIRS_ATTR_RUN_STRUCTURAL_DEPTH",
                "STAIRSTYPE_MONOLITHIC_STRUCTURAL_DEPTH",
                "STAIRS_MONOLITHIC_STRUCTURAL_DEPTH"
            };

            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return 0.0;
                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;

                    double depth;
                    if (TryReadLength(run, candidates, out depth)) return depth;

                    Element type = run.Document.GetElement(run.GetTypeId());
                    if (type != null && TryReadLength(type, candidates, out depth)) return depth;
                }
            }
            catch (Exception)
            {
                // Aucune epaisseur lisible : l'appelant le dira au lieu d'en inventer une.
            }
            return 0.0;
        }

        /// <summary>
        /// COMMENT LA VOLEE PORTE, lu sur l'escalier dessine.
        ///
        /// C'est la lecture la plus importante du module, parce que le mode d'appui fixe la
        /// portee, donc le moment, donc toute la suite. Jusqu'a la 3.12.0 incluse elle
        /// n'existait pas : le moteur SUPPOSAIT un palier de 1 300 mm sur tout escalier, y
        /// compris ceux qui n'en ont aucun. La portee etait alors majoree de 1 300 mm, et
        /// le ferraillage du palier etait pose la ou il n'y a pas de beton.
        ///
        /// L'ABSENCE DE PALIER EST UNE LECTURE, PAS UNE IGNORANCE. Si l'escalier dessine ne
        /// comporte aucun palier, la portee est celle de la volee seule, et ce n'est pas
        /// une hypothese : c'est ce que le modele dit.
        /// </summary>
        private static void ReadSupportCondition(Stairs stairs, StairData data)
        {
            ICollection<ElementId> landings;
            try
            {
                landings = stairs.GetStairsLandings();
            }
            catch (Exception)
            {
                data.Remarks.Add(
                    "Les paliers de cet escalier n'ont pas pu etre lus : le mode d'appui " +
                    "et la longueur de palier restent ceux de la fenetre, et ce sont des " +
                    "HYPOTHESES. Verifiez-les : ce sont elles qui fixent la portee.");
                return;
            }

            if (landings == null) return;

            if (landings.Count == 0)
            {
                data.SpanKind = StairSpanKind.AlongFlightOnly;
                data.LandingSpanMm = 0.0;
                data.Provenance.Set(StairDimension.Support,
                                    StairDimensionSource.ReadFromModel);
                data.Provenance.Set(StairDimension.LandingSpan,
                                    StairDimensionSource.ReadFromModel);
                data.Remarks.Add(
                    "L'escalier dessine ne comporte AUCUN PALIER : la portee retenue est " +
                    "celle de la volee seule, et aucune armature de palier n'est posee. " +
                    "Si la volee prend appui au-dela de son sommet, declarez le palier " +
                    "dans la fenetre.");
                return;
            }

            XYZ start, direction;
            if (!TryReadAscent(stairs, out start, out direction))
            {
                data.Remarks.Add(string.Format(
                    "Cet escalier comporte {0} palier(s), mais sa ligne de foulee n'a pas " +
                    "pu etre lue : leur longueur portante n'a pas pu etre mesuree. Le mode " +
                    "d'appui reste celui de la fenetre, et c'est une HYPOTHESE.",
                    landings.Count));
                return;
            }

            double flightTop = ProjectedMaximum(stairs, direction);
            double best = 0.0;
            double bestGap = double.MaxValue;
            double thickness = 0.0;

            foreach (ElementId id in landings)
            {
                Element landing = stairs.Document.GetElement(id);
                if (landing == null) continue;

                double min, max;
                if (!TryProjectedExtent(landing, direction, out min, out max)) continue;

                // Seul un palier situe AU-DELA du sommet de la volee prolonge la portee.
                // Un palier de depart, lui, est du cote de l'appui bas : il ne s'ajoute pas.
                double gap = min - flightTop;
                if (gap < -50.0) continue;
                if (gap >= bestGap) continue;

                bestGap = gap;
                best = max - min;

                double read;
                string[] candidates =
                {
                    "STAIRS_LANDINGTYPE_STRUCTURAL_DEPTH",
                    "STAIRS_ATTR_LANDING_STRUCTURAL_DEPTH",
                    "STAIRS_LANDINGTYPE_TOTAL_THICKNESS",
                    "STAIRS_LANDING_THICKNESS"
                };
                if (TryReadLength(landing, candidates, out read)) thickness = read;
                else
                {
                    Element type = landing.Document.GetElement(landing.GetTypeId());
                    if (type != null && TryReadLength(type, candidates, out read)) thickness = read;
                }
            }

            if (best <= Tolerance)
            {
                data.SpanKind = StairSpanKind.AlongFlightOnly;
                data.LandingSpanMm = 0.0;
                data.Provenance.Set(StairDimension.Support,
                                    StairDimensionSource.ReadFromModel);
                data.Provenance.Set(StairDimension.LandingSpan,
                                    StairDimensionSource.ReadFromModel);
                data.Remarks.Add(string.Format(
                    "Les {0} palier(s) de cet escalier sont tous situes au PIED de la " +
                    "volee : aucun ne prolonge la portee, qui reste celle de la volee " +
                    "seule.", landings.Count));
                return;
            }

            data.SpanKind = StairSpanKind.AlongFlightWithLanding;
            data.LandingSpanMm = best;
            data.Provenance.Set(StairDimension.Support, StairDimensionSource.ReadFromModel);
            data.Provenance.Set(StairDimension.LandingSpan, StairDimensionSource.ReadFromModel);
            data.Remarks.Add(string.Format(
                "Palier lu au sommet de la volee : {0:0} mm mesures suivant la ligne de " +
                "foulee. La portee retenue est la volee PLUS ce palier, ce qui suppose que " +
                "l'appui est au bord oppose du palier. S'il existe une poutre ou un voile " +
                "au droit du noeud, declarez l'appui a la volee seule.", best));

            if (thickness > Tolerance)
            {
                data.LandingThicknessMm = thickness;
                data.Provenance.Set(StairDimension.LandingThickness,
                                    StairDimensionSource.ReadFromModel);
            }

            if (landings.Count > 1)
            {
                data.Remarks.Add(string.Format(
                    "L'escalier compte {0} paliers : seul celui qui suit immediatement la " +
                    "volee calculee est pris dans la portee.", landings.Count));
            }
        }

        /// <summary>Abscisse maximale des volees suivant la direction de montee (mm).</summary>
        private static double ProjectedMaximum(Stairs stairs, XYZ direction)
        {
            double max = double.MinValue;
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return 0.0;
                foreach (ElementId id in runs)
                {
                    Element run = stairs.Document.GetElement(id);
                    if (run == null) continue;
                    double min, runMax;
                    if (!TryProjectedExtent(run, direction, out min, out runMax)) continue;
                    if (runMax > max) max = runMax;
                }
            }
            catch (Exception)
            {
                return 0.0;
            }
            return max > double.MinValue ? max : 0.0;
        }

        /// <summary>
        /// Etendue d'un element suivant une direction horizontale (mm), mesuree sur les
        /// huit sommets de sa boite englobante ramenes dans le repere du modele.
        /// </summary>
        private static bool TryProjectedExtent(Element element, XYZ direction,
                                               out double minMm, out double maxMm)
        {
            minMm = 0.0;
            maxMm = 0.0;
            BoundingBoxXYZ box = element != null ? element.get_BoundingBox(null) : null;
            if (box == null || direction == null) return false;

            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new XYZ((i & 1) == 0 ? box.Min.X : box.Max.X,
                                     (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                                     (i & 4) == 0 ? box.Min.Z : box.Max.Z);
                XYZ world = box.Transform != null ? box.Transform.OfPoint(corner) : corner;
                double t = world.X * direction.X + world.Y * direction.Y;
                if (t < min) min = t;
                if (t > max) max = t;
            }

            minMm = UnitConverter.FeetToMm(min);
            maxMm = UnitConverter.FeetToMm(max);
            return maxMm > minMm;
        }

        /// <summary>
        /// Lit une longueur portee par un parametre integre, en resolvant l'identifiant PAR
        /// SON NOM a l'execution : un parametre absent de la version de Revit utilisee est
        /// ignore au lieu d'empecher la compilation.
        /// </summary>
        private static bool TryReadLength(Element element, string[] builtInNames, out double mm)
        {
            mm = 0.0;
            if (element == null || builtInNames == null) return false;

            foreach (string name in builtInNames)
            {
                BuiltInParameter id;
                if (!Enum.TryParse(name, out id)) continue;

                Parameter parameter;
                try
                {
                    parameter = element.get_Parameter(id);
                }
                catch (Exception)
                {
                    continue;
                }

                if (parameter == null || !parameter.HasValue) continue;
                if (parameter.StorageType != StorageType.Double) continue;

                double value = UnitConverter.FeetToMm(parameter.AsDouble());
                if (value <= Tolerance) continue;

                mm = value;
                return true;
            }
            return false;
        }

        private static double ReadRunWidth(Stairs stairs)
        {
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return 0.0;
                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;
                    double width = UnitConverter.FeetToMm(run.ActualRunWidth);
                    if (width > Tolerance) return width;
                }
            }
            catch (Exception)
            {
                // Le type de volee peut ne pas exposer sa largeur : l'appelant le dira.
            }
            return 0.0;
        }

        // ------------------------------------------------------------------
        // Paillasse modelisee par un plancher
        // ------------------------------------------------------------------

        private static RevitStair ReadFlightModelledAsFloor(Element element, StairData defaults,
                                                            out string error)
        {
            error = null;
            StairData data = Clone(defaults);
            data.Id = element.UniqueId;
            data.Name = ColumnReader.Describe(element);
            data.Mark = ColumnReader.ReadMark(element);
            data.Provenance = new StairGeometryProvenance();

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                error = "La geometrie de ce plancher n'a pas pu etre lue.";
                return null;
            }

            double extentXMm = UnitConverter.FeetToMm(box.Max.X - box.Min.X);
            double extentYMm = UnitConverter.FeetToMm(box.Max.Y - box.Min.Y);
            double riseMm = UnitConverter.FeetToMm(box.Max.Z - box.Min.Z);
            bool alongX = extentXMm >= extentYMm;

            data.WidthMm = alongX ? extentYMm : extentXMm;
            data.Provenance.Set(StairDimension.Width, StairDimensionSource.ReadFromModel);
            data.Remarks.Add(string.Format(
                "Paillasse modelisee par un plancher : emprise {0:0} x {1:0} mm, denivele " +
                "d'enveloppe {2:0} mm. La largeur de volee est prise sur la plus petite " +
                "emprise.", extentXMm, extentYMm, riseMm));

            // Un plancher ne porte AUCUNE information de marche. Le lecteur ne fabrique donc
            // ni contremarche ni giron : il le dit, et la fenetre garde la main.
            data.Remarks.Add(
                "Un plancher ne porte aucune information de marche : le nombre de " +
                "contremarches, la hauteur de contremarche et le giron restent ceux saisis " +
                "dans la fenetre. Ce sont eux qui fixent la pente, donc le poids propre.");

            // L'epaisseur, en revanche, un plancher la porte : c'est la paillasse elle-meme.
            string[] thicknessParameters =
            {
                "FLOOR_ATTR_THICKNESS_PARAM", "FLOOR_ATTR_DEFAULT_THICKNESS_PARAM",
                "STRUCTURAL_FLOOR_CORE_THICKNESS"
            };
            double slab;
            if (TryReadLength(element, thicknessParameters, out slab)
                || TryReadLength(element.Document.GetElement(element.GetTypeId()),
                                 thicknessParameters, out slab))
            {
                data.WaistThicknessMm = slab;
                data.Provenance.Set(StairDimension.WaistThickness,
                                    StairDimensionSource.ReadFromModel);
                data.Remarks.Add(string.Format(
                    "Epaisseur de paillasse lue sur le plancher : {0:0} mm.", slab));
            }

            // UN PLANCHER NE DIT PAS COMMENT IL PORTE. Le mode d'appui et la longueur de
            // palier restent donc des HYPOTHESES, et le moteur les annonce comme telles :
            // c'est le mode d'appui qui fixe la portee, donc le moment, donc tout le reste.
            data.Remarks.Add(
                "Un plancher ne dit pas ou sont ses appuis : le mode d'appui et la longueur " +
                "de palier portante restent ceux de la fenetre, et ce sont des HYPOTHESES. " +
                "Ce sont elles qui fixent la portee.");

            // Un plancher ne dit pas non plus si la volee est droite. On ne le suppose pas.
            data.Shape = StairFlightShape.Undetermined;

            // Un plancher ne porte pas de ligne de foulee : le repere est celui de
            // l'enveloppe, avec l'origine au coin depuis lequel Y croit vers l'interieur.
            data.Remarks.Add(
                "Le repere des armatures est construit sur l'enveloppe du plancher : il " +
                "suppose une paillasse alignee sur un axe du modele et montant vers les " +
                "coordonnees croissantes. VERIFIEZ LA POSITION DES BARRES apres generation.");

            var stair = new RevitStair
            {
                Data = data,
                Frame = FallbackFrame(element, box),
                CanHostRebar = IsValidRebarHost(element)
            };

            if (!stair.CanHostRebar)
            {
                stair.RebarHostMessage =
                    "Ce plancher n'accepte pas d'armatures : il doit etre un plancher " +
                    "STRUCTUREL en beton. Le calcul reste produit, mais les barres ne " +
                    "pourront pas etre posees.";
                data.Remarks.Add(stair.RebarHostMessage);
            }

            return stair;
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit le repere de la volee A PARTIR DE SA LIGNE DE FOULEE, et non de sa
        /// boite englobante.
        ///
        /// Une boite englobante ne connait ni le SENS de la montee ni le depart de la
        /// sous-face : elle ne donne qu'un coin, et choisir l'axe sur la plus grande
        /// dimension place les armatures n'importe ou des que l'escalier est tourne ou
        /// monte vers les X ou les Y decroissants. La ligne de foulee, elle, part du pied
        /// de la volee et pointe vers le haut : c'est exactement l'axe X du repere local.
        ///
        /// L'origine est ramenee au BORD de la volee, a une demi-largeur du milieu de la
        /// ligne de foulee, parce que le constructeur de plan compte les Y depuis le bord.
        /// </summary>
        private static RevitElementFrame BuildFrame(Stairs stairs, StairData data)
        {
            BoundingBoxXYZ box = stairs.get_BoundingBox(null);
            double baseZ = box != null ? box.Min.Z : 0.0;

            XYZ start, direction;
            if (TryReadAscent(stairs, out start, out direction))
            {
                XYZ axisX = direction;
                XYZ axisY = XYZ.BasisZ.CrossProduct(axisX).Normalize();
                XYZ origin = new XYZ(start.X, start.Y, baseZ)
                             - axisY.Multiply(UnitConverter.MmToFeet(data.WidthMm) / 2.0);

                return new RevitElementFrame
                {
                    Host = stairs,
                    Origin = origin,
                    AxisX = axisX,
                    AxisY = axisY,
                    AxisZ = XYZ.BasisZ
                };
            }

            data.Remarks.Add(
                "La ligne de foulee n'a pas pu etre lue : le repere des armatures est " +
                "construit sur la boite englobante, ce qui suppose une volee alignee sur un " +
                "axe du modele et montant vers les coordonnees croissantes. VERIFIEZ LA " +
                "POSITION DES BARRES apres generation.");

            return FallbackFrame(stairs, box);
        }

        /// <summary>
        /// Sens de la montee : point de depart et direction horizontale de la ligne de
        /// foulee. Elle est orientee du bas vers le haut de la volee ; le controle sur les
        /// altitudes le confirme plutot que de le supposer.
        /// </summary>
        private static bool TryReadAscent(Stairs stairs, out XYZ start, out XYZ direction)
        {
            start = null;
            direction = null;
            try
            {
                ICollection<ElementId> runs = stairs.GetStairsRuns();
                if (runs == null) return false;

                foreach (ElementId id in runs)
                {
                    var run = stairs.Document.GetElement(id) as StairsRun;
                    if (run == null) continue;

                    CurveLoop path = run.GetStairsPath();
                    if (path == null) continue;

                    Curve first = null;
                    Curve last = null;
                    foreach (Curve curve in path)
                    {
                        if (first == null) first = curve;
                        last = curve;
                    }
                    if (first == null) return false;

                    XYZ a = first.GetEndPoint(0);
                    XYZ b = last.GetEndPoint(1);

                    // La ligne de foulee peut etre stockee dans un sens ou dans l'autre :
                    // c'est l'altitude qui dit lequel monte.
                    if (b.Z < a.Z)
                    {
                        XYZ swap = a;
                        a = b;
                        b = swap;
                    }

                    XYZ horizontal = new XYZ(b.X - a.X, b.Y - a.Y, 0.0);
                    if (horizontal.GetLength() < Tolerance) return false;

                    start = a;
                    direction = horizontal.Normalize();
                    return true;
                }
            }
            catch (Exception)
            {
                // Le type de volee peut refuser sa ligne de foulee : l'appelant le dira.
            }
            return false;
        }

        /// <summary>
        /// Repere de secours, sur la boite englobante. Il reste faux pour une volee
        /// tournee, mais au moins il est COHERENT : l'origine est le coin depuis lequel X
        /// et Y balayent reellement l'emprise.
        /// </summary>
        private static RevitElementFrame FallbackFrame(Element element, BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return new RevitElementFrame
                {
                    Host = element,
                    Origin = XYZ.Zero,
                    AxisX = XYZ.BasisX,
                    AxisY = XYZ.BasisY,
                    AxisZ = XYZ.BasisZ
                };
            }

            bool alongX = (box.Max.X - box.Min.X) >= (box.Max.Y - box.Min.Y);
            XYZ axisX = alongX ? XYZ.BasisX : XYZ.BasisY;
            XYZ axisY = XYZ.BasisZ.CrossProduct(axisX).Normalize();

            // L'origine doit etre le coin depuis lequel Y local croit VERS L'INTERIEUR de
            // l'emprise. Avec AxisX = +Y, AxisY vaut -X : l'origine est alors du cote
            // Max.X, et non Min.X — c'est ce signe qui envoyait les barres hors du beton.
            double originX = axisY.X < 0 ? box.Max.X : box.Min.X;
            double originY = axisY.Y < 0 ? box.Max.Y : box.Min.Y;

            return new RevitElementFrame
            {
                Host = element,
                Origin = new XYZ(originX, originY, box.Min.Z),
                AxisX = axisX,
                AxisY = axisY,
                AxisZ = XYZ.BasisZ
            };
        }

        private static StairData Clone(StairData defaults)
        {
            var data = new StairData();
            if (defaults == null) return data;

            data.RiserHeightMm = defaults.RiserHeightMm;
            data.TreadDepthMm = defaults.TreadDepthMm;
            data.RiserCount = defaults.RiserCount;
            data.WaistThicknessMm = defaults.WaistThicknessMm;
            data.WidthMm = defaults.WidthMm;
            data.LandingThicknessMm = defaults.LandingThicknessMm;
            data.LandingSpanMm = defaults.LandingSpanMm;
            data.SpanKind = defaults.SpanKind;
            data.Provenance = defaults.Provenance != null
                ? defaults.Provenance.Clone() : new StairGeometryProvenance();
            return data;
        }

        public static bool IsValidRebarHost(Element element)
        {
            return SlabReader.IsValidRebarHost(element);
        }

        public static string Describe(Element element)
        {
            return ColumnReader.Describe(element);
        }
    }
}
