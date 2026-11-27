using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Extraction;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Dessins 3D de DanCI dans les vues 3D (DirectContext3D), par calques : « diagramme » (barres
    /// du panneau) et « fouilles » (trace des fouilles calculees). Affichage seulement : aucune
    /// transaction, rien n'est ecrit dans le modele ni enregistre avec le projet ; non selectionnable,
    /// non imprime, non exporte.
    /// </summary>
    internal sealed class ChartServer : IDirectContext3DServer
    {
        public const string ChartLayer = "diagramme";
        public const string ExcavationLayer = "fouilles";

        private static readonly Guid ServerId = new Guid("B8E2D0A4-5F37-4C19-8E6A-2D41C7F09B53");
        private static ChartServer instance;

        /// <summary>Un volume a dessiner : 8 coins (4 du bas puis 4 du haut, sens trigonometrique), couleur et transparence (0 opaque .. 255).</summary>
        private sealed class Solid8
        {
            public XYZ[] Corners;
            public ColorWithTransparency Color;
            public bool Transparent;
        }

        /// <summary>Triangle de maillage reel (element en construction), couleur opaque.</summary>
        private struct Tri
        {
            public XYZ A, B, C;
            public ColorWithTransparency Color;
        }

        private readonly Dictionary<string, List<Tri>> meshLayers = new Dictionary<string, List<Tri>>(StringComparer.Ordinal);
        /// <summary>Aretes (segments) par calque : contours des elements en construction, comme « ombre avec aretes ».</summary>
        private readonly Dictionary<string, List<(XYZ A, XYZ B)>> lineLayers = new Dictionary<string, List<(XYZ A, XYZ B)>>(StringComparer.Ordinal);

        /// <summary>
        /// Tampons PAR CALQUE : seul un calque modifie est reconstruit (le terrain, la palissade, les fouilles ne
        /// sont plus renvoyes a la carte graphique a chaque image de la montee des elements).
        /// </summary>
        private sealed class LayerBuffers
        {
            public List<Buffers> Opaque = new List<Buffers>(), Transparent = new List<Buffers>(), Mesh = new List<Buffers>(), Lines = new List<Buffers>();
        }

        private readonly Dictionary<string, LayerBuffers> built = new Dictionary<string, LayerBuffers>(StringComparer.Ordinal);
        private readonly HashSet<string> dirtyLayers = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Triangles par tampon : chaque triangle dessine sur ses 2 faces (6 sommets) -> 10 000 x 6 = 60 000 sommets (index sur 16 bits).</summary>
        private const int TrianglesPerBuffer = 10000;

        private readonly object gate = new object();
        private readonly Dictionary<string, List<Solid8>> layers = new Dictionary<string, List<Solid8>>(StringComparer.Ordinal);
        private string documentKey;
        private Outline outline;
        /// <summary>Tout reconstruire (tampons perdus, autre projet).</summary>
        private bool dirty = true;

        /// <summary>
        /// Volumes par tampon : les indices sont sur 16 bits (65 535 sommets au plus) ; 24 sommets par
        /// volume -> 2 700 volumes par tampon. Au-dela, plusieurs tampons (sinon le dessin est faux ou vide).
        /// </summary>
        private const int SolidsPerBuffer = 2700;

        private sealed class Buffers
        {
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public int VertexCount, TriangleCount;
            /// <summary>Tampon de segments (aretes) : TriangleCount = nombre de segments.</summary>
            public bool IsLines;
            public bool IsValid => TriangleCount > 0 && Vertices != null && Vertices.IsValid() && Indices != null && Indices.IsValid();
        }

        public static bool IsActive => instance != null && (instance.layers.Values.Any(l => l.Count > 0) || instance.meshLayers.Values.Any(l => l.Count > 0));

        // ------------------------------------------------------------------ calque « diagramme »
        public static void Show(Document doc, List<ChartBox> chartBoxes) =>
            SetLayer(doc, ChartLayer, chartBoxes.Select(b => Box(b)).ToList());

        public static void Hide() => ClearLayer(ChartLayer);

        // ------------------------------------------------------------------ calque « fouilles »
        /// <summary>Fouilles a dessiner (prismes du parametre Geo3D), terre semi-transparente.</summary>
        public static int ShowExcavations(Document doc, IEnumerable<Prism> prisms)
        {
            var solids = prisms.Select(p => Prism8(p, "#8B5A2B", 110)).ToList();
            SetLayer(doc, ExcavationLayer, solids);
            return solids.Count;
        }

        public static void HideExcavations() => ClearLayer(ExcavationLayer);

        // ------------------------------------------------------------------ calque « 4D » (elements en construction)
        public const string BuildLayer = "4D";

        /// <summary>Elements en cours de construction (formes partielles : assises, coulage), couleur du lot, opaques.</summary>
        public static int Show4D(Document doc, IEnumerable<(Prism Shape, string Color)> parts)
        {
            var solids = parts.Select(p => Prism8(p.Shape, p.Color, 0)).ToList();
            SetLayer(doc, BuildLayer, solids);
            return solids.Count;
        }

        /// <summary>
        /// Elements en construction en VRAIE geometrie (maillages decoupes au rang atteint) + volumes simples
        /// de secours (elements sans maillage lisible).
        /// </summary>
        public static int Show4D(Document doc, IEnumerable<(Prism Shape, string Color)> parts,
            IEnumerable<(DanCI.BOQ.Core.Planning.TriMesh Mesh, string Color)> meshes) => Show4D(doc, parts, meshes, null);

        public static int Show4D(Document doc, IEnumerable<(Prism Shape, string Color)> parts,
            IEnumerable<(DanCI.BOQ.Core.Planning.TriMesh Mesh, string Color)> meshes, IEnumerable<(DanCI.BOQ.Core.Planning.V3 A, DanCI.BOQ.Core.Planning.V3 B)> edges)
        {
            var solids = parts.Select(p => Prism8(p.Shape, p.Color, 0)).ToList();
            var tris = new List<Tri>();
            foreach (var m in meshes ?? Enumerable.Empty<(DanCI.BOQ.Core.Planning.TriMesh, string)>())
            {
                if (m.Mesh == null) continue;
                ColorWithTransparency col = ToColor(m.Color, 0);
                var pts = m.Mesh.Points;
                for (int i = 0; i + 2 < pts.Count; i += 3)
                {
                    tris.Add(new Tri { A = new XYZ(pts[i].X, pts[i].Y, pts[i].Z), B = new XYZ(pts[i + 1].X, pts[i + 1].Y, pts[i + 1].Z), C = new XYZ(pts[i + 2].X, pts[i + 2].Y, pts[i + 2].Z), Color = col });
                }
            }
            Ensure();
            instance.UpdateLines(DocumentKey(doc), BuildLayer,
                (edges ?? Enumerable.Empty<(DanCI.BOQ.Core.Planning.V3, DanCI.BOQ.Core.Planning.V3)>())
                    .Select(e => (new XYZ(e.Item1.X, e.Item1.Y, e.Item1.Z), new XYZ(e.Item2.X, e.Item2.Y, e.Item2.Z))).ToList());
            instance.UpdateMeshes(DocumentKey(doc), BuildLayer, tris);
            instance.Update(DocumentKey(doc), BuildLayer, solids);
            return solids.Count + tris.Count;
        }

        public const string EnvironmentLayer = "chantier";

        /// <summary>Environnement de chantier (terrain, palissade, grue, echafaudages...) : dessin seulement.</summary>
        public static void ShowEnvironment(Document doc, IEnumerable<(DanCI.BOQ.Core.Planning.TriMesh Mesh, string Color)> meshes)
        {
            var tris = new List<Tri>();
            foreach (var m in meshes ?? Enumerable.Empty<(DanCI.BOQ.Core.Planning.TriMesh, string)>())
            {
                if (m.Mesh == null) continue;
                ColorWithTransparency col = ToColor(m.Color, 0);
                var pts = m.Mesh.Points;
                for (int i = 0; i + 2 < pts.Count; i += 3)
                    tris.Add(new Tri { A = new XYZ(pts[i].X, pts[i].Y, pts[i].Z), B = new XYZ(pts[i + 1].X, pts[i + 1].Y, pts[i + 1].Z), C = new XYZ(pts[i + 2].X, pts[i + 2].Y, pts[i + 2].Z), Color = col });
            }
            Ensure();
            instance.UpdateMeshes(DocumentKey(doc), EnvironmentLayer, tris);
        }

        public static void HideEnvironment() => instance?.UpdateMeshes(instance.documentKey, EnvironmentLayer, new List<Tri>());

        public static void Hide4D()
        {
            instance?.UpdateLines(instance.documentKey, BuildLayer, new List<(XYZ, XYZ)>());
            instance?.UpdateMeshes(instance.documentKey, BuildLayer, new List<Tri>());
            ClearLayer(BuildLayer);
        }

        private void UpdateMeshes(string key, string layer, List<Tri> tris)
        {
            lock (gate)
            {
                Switch(key);
                meshLayers[layer] = tris ?? new List<Tri>();
                RecomputeOutline();
                dirtyLayers.Add(layer);
            }
        }

        private void UpdateLines(string key, string layer, List<(XYZ A, XYZ B)> lines)
        {
            lock (gate)
            {
                Switch(key);
                lineLayers[layer] = lines ?? new List<(XYZ, XYZ)>();
                dirtyLayers.Add(layer);
            }
        }

        /// <summary>Autre projet : on ne garde rien de l'ancien.</summary>
        private void Switch(string key)
        {
            if (key == documentKey) return;
            layers.Clear();
            meshLayers.Clear();
            lineLayers.Clear();
            built.Clear();
            documentKey = key;
            dirty = true;
        }

        // ------------------------------------------------------------------ enregistrement
        private static void SetLayer(Document doc, string layer, List<Solid8> solids)
        {
            Ensure();
            instance.Update(DocumentKey(doc), layer, solids);
        }

        private static void ClearLayer(string layer) => instance?.Update(instance.documentKey, layer, new List<Solid8>());

        private static void Ensure()
        {
            if (instance != null) return;
            instance = new ChartServer();
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
            if (service == null)
            {
                instance = null;
                throw new InvalidOperationException("Service DirectContext3D indisponible dans cette version de Revit.");
            }
            service.AddServer(instance);
            IList<Guid> active = service.GetActiveServerIds();
            if (!active.Contains(ServerId)) active.Add(ServerId);
            service.SetActiveServers(active);
            BoqLog.Info("Serveur DirectContext3D DanCI enregistré.");
        }

        /// <summary>A l'arret de Revit.</summary>
        public static void Unregister()
        {
            if (instance == null) return;
            try
            {
                var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
                service?.RemoveServer(ServerId);
            }
            catch (Exception ex)
            {
                BoqLog.Warn("Retrait du serveur DirectContext3D : " + ex.Message);
            }
            instance = null;
        }

        public static string DocumentKey(Document doc) => doc == null ? null : doc.PathName + "|" + doc.Title;

        private void Update(string key, string layer, List<Solid8> solids)
        {
            lock (gate)
            {
                Switch(key);
                layers[layer] = solids ?? new List<Solid8>();
                RecomputeOutline();
                dirtyLayers.Add(layer);
            }
        }

        private void RecomputeOutline()
        {
            {
                // Emprise sans liste intermediaire (des centaines de milliers de points possibles a chaque image).
                double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
                void Grow(XYZ p)
                {
                    if (p.X < x0) x0 = p.X; if (p.Y < y0) y0 = p.Y; if (p.Z < z0) z0 = p.Z;
                    if (p.X > x1) x1 = p.X; if (p.Y > y1) y1 = p.Y; if (p.Z > z1) z1 = p.Z;
                }
                foreach (var l in layers.Values) foreach (Solid8 s8 in l) foreach (XYZ c in s8.Corners) Grow(c);
                foreach (var l in meshLayers.Values) foreach (Tri t in l) { Grow(t.A); Grow(t.B); Grow(t.C); }
                outline = x0 > x1 ? null : new Outline(new XYZ(x0, y0, z0), new XYZ(x1, y1, z1));
            }
        }

        private static Solid8 Box(ChartBox b)
        {
            return new Solid8
            {
                Corners = new[]
                {
                    new XYZ(b.MinX, b.MinY, b.MinZ), new XYZ(b.MaxX, b.MinY, b.MinZ), new XYZ(b.MaxX, b.MaxY, b.MinZ), new XYZ(b.MinX, b.MaxY, b.MinZ),
                    new XYZ(b.MinX, b.MinY, b.MaxZ), new XYZ(b.MaxX, b.MinY, b.MaxZ), new XYZ(b.MaxX, b.MaxY, b.MaxZ), new XYZ(b.MinX, b.MaxY, b.MaxZ)
                },
                Color = ToColor(b.Color, 0),
                Transparent = false
            };
        }

        private static Solid8 Prism8(Prism p, string color, uint transparency)
        {
            var c = new XYZ[8];
            for (int i = 0; i < 4; i++)
            {
                c[i] = new XYZ(p.Bottom[2 * i], p.Bottom[2 * i + 1], p.Zb);
                c[i + 4] = new XYZ(p.Top[2 * i], p.Top[2 * i + 1], p.Zt);
            }
            return new Solid8 { Corners = c, Color = ToColor(color, transparency), Transparent = transparency > 0 };
        }

        // ------------------------------------------------------------------ IDirectContext3DServer
        public Guid GetServerId() => ServerId;
        public string GetVendorId() => "DanCI";
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "DanCI — dessins 3D du métré";
        public string GetDescription() => "Diagramme du BOQ et fouilles calculées dessinés dans la vue 3D (affichage seulement).";
        public string GetApplicationId() => string.Empty;
        public string GetSourceId() => string.Empty;
        public bool UsesHandles() => false;

        public bool UseInTransparentPass(View dBView)
        {
            lock (gate)
            {
                return layers.Values.Any(l => l.Any(s => s.Transparent));
            }
        }

        public bool CanExecute(View dBView)
        {
            lock (gate)
            {
                return (layers.Values.Any(l => l.Count > 0) || meshLayers.Values.Any(l => l.Count > 0)) && dBView is View3D && dBView.Document != null && DocumentKey(dBView.Document) == documentKey;
            }
        }

        public Outline GetBoundingBox(View dBView)
        {
            lock (gate)
            {
                return outline;
            }
        }

        public void RenderScene(View dBView, DisplayStyle displayStyle)
        {
            try
            {
                lock (gate)
                {
                    // Seuls les calques modifies sont reconstruits (tous apres une perte des tampons).
                    var names = layers.Keys.Concat(meshLayers.Keys).Concat(lineLayers.Keys).Distinct().ToList();
                    foreach (string name in names)
                    {
                        if (!dirty && built.ContainsKey(name) && !dirtyLayers.Contains(name)) continue;
                        var solids = layers.TryGetValue(name, out var l) ? l : new List<Solid8>();
                        built[name] = new LayerBuffers
                        {
                            Opaque = BuildAll(solids.Where(x => !x.Transparent).ToList()),
                            Transparent = BuildAll(solids.Where(x => x.Transparent).ToList()),
                            Mesh = BuildMeshes(meshLayers.TryGetValue(name, out var m) ? m : new List<Tri>()),
                            Lines = BuildLines(lineLayers.TryGetValue(name, out var e) ? e : new List<(XYZ, XYZ)>())
                        };
                    }
                    dirty = false;
                    dirtyLayers.Clear();
                    var format = new VertexFormat(VertexFormatBits.PositionNormalColored);
                    var effect = new EffectInstance(VertexFormatBits.PositionNormalColored);
                    bool transparentPass = DrawContext.IsTransparentPass();
                    foreach (LayerBuffers lb in built.Values)
                    {
                        foreach (Buffers b in transparentPass ? lb.Transparent : lb.Opaque.Concat(lb.Mesh))
                        {
                            if (b == null || !b.IsValid)
                            {
                                if (b != null && b.TriangleCount > 0) dirty = true; // tampons perdus (changement de vue) : reconstruits au prochain rendu
                                continue;
                            }
                            DrawContext.FlushBuffer(b.Vertices, b.VertexCount, b.Indices, b.TriangleCount * 3, format, effect,
                                PrimitiveType.TriangleList, 0, b.TriangleCount);
                        }
                        if (transparentPass) continue;
                        foreach (Buffers b in lb.Lines)
                        {
                            if (b == null || !b.IsValid)
                            {
                                if (b != null && b.TriangleCount > 0) dirty = true;
                                continue;
                            }
                            DrawContext.FlushBuffer(b.Vertices, b.VertexCount, b.Indices, b.TriangleCount * 2, LineFormat, LineEffect,
                                PrimitiveType.LineList, 0, b.TriangleCount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Jamais d'exception dans la boucle de rendu de Revit : on journalise et on n'affiche plus rien.
                BoqLog.Error("Dessin 3D DanCI en échec.", ex);
                lock (gate)
                {
                    layers.Clear();
                    meshLayers.Clear();
                    lineLayers.Clear();
                    built.Clear();
                    dirty = true;
                }
            }
        }

        private static readonly VertexFormat LineFormat = new VertexFormat(VertexFormatBits.PositionColored);
        private static readonly EffectInstance LineEffect = new EffectInstance(VertexFormatBits.PositionColored);
        private const int LinesPerBuffer = 30000;

        /// <summary>Tampons d'aretes (segments gris fonce), comme les aretes de la vue « ombree avec aretes ».</summary>
        private static List<Buffers> BuildLines(List<(XYZ A, XYZ B)> lines)
        {
            var list = new List<Buffers>();
            var col = new ColorWithTransparency(45, 50, 58, 0);
            for (int start = 0; start < lines.Count; start += LinesPerBuffer)
            {
                int count = Math.Min(LinesPerBuffer, lines.Count - start);
                var b = new Buffers { VertexCount = count * 2, TriangleCount = count, IsLines = true };
                int floats = b.VertexCount * VertexPositionColored.GetSizeInFloats();
                b.Vertices = new VertexBuffer(floats);
                b.Vertices.Map(floats);
                VertexStreamPositionColored vs = b.Vertices.GetVertexStreamPositionColored();
                for (int i = start; i < start + count; i++)
                {
                    vs.AddVertex(new VertexPositionColored(lines[i].A, col));
                    vs.AddVertex(new VertexPositionColored(lines[i].B, col));
                }
                b.Vertices.Unmap();
                int shorts = count * IndexLine.GetSizeInShortInts();
                b.Indices = new IndexBuffer(shorts);
                b.Indices.Map(shorts);
                IndexStreamLine ls = b.Indices.GetIndexStreamLine();
                for (int q = 0; q < count; q++) ls.AddLine(new IndexLine(q * 2, q * 2 + 1));
                b.Indices.Unmap();
                list.Add(b);
            }
            return list;
        }

        /// <summary>
        /// Tampons des maillages reels : chaque triangle sur ses deux faces (aucune face manquante quelle que
        /// soit l'orientation donnee par Revit), ombrage par face selon la pente (dessus clair, cotes nuances).
        /// </summary>
        private const double LightX = -0.38, LightY = -0.52, LightZ = 0.765;

        private static List<Buffers> BuildMeshes(List<Tri> tris)
        {
            var list = new List<Buffers>();
            for (int start = 0; start < tris.Count; start += TrianglesPerBuffer)
            {
                int count = Math.Min(TrianglesPerBuffer, tris.Count - start);
                var b = new Buffers { VertexCount = count * 6, TriangleCount = count * 2 };
                int vertexFloats = b.VertexCount * VertexPositionNormalColored.GetSizeInFloats();
                b.Vertices = new VertexBuffer(vertexFloats);
                b.Vertices.Map(vertexFloats);
                VertexStreamPositionNormalColored vs = b.Vertices.GetVertexStreamPositionNormalColored();
                for (int i = start; i < start + count; i++)
                {
                    Tri t = tris[i];
                    XYZ n = (t.B - t.A).CrossProduct(t.C - t.A);
                    n = n.GetLength() > 1e-12 ? n.Normalize() : XYZ.BasisZ;
                    // Lumiere directionnelle proche de celle de Revit (haut, legerement de cote) + ambiance.
                    double lit = Math.Abs(n.X * LightX + n.Y * LightY + n.Z * LightZ);
                    double shade = 0.42 + 0.58 * lit;
                    ColorWithTransparency col = Shade(t.Color, shade);
                    vs.AddVertex(new VertexPositionNormalColored(t.A, n, col));
                    vs.AddVertex(new VertexPositionNormalColored(t.B, n, col));
                    vs.AddVertex(new VertexPositionNormalColored(t.C, n, col));
                    XYZ m = n.Negate();
                    vs.AddVertex(new VertexPositionNormalColored(t.A, m, col));
                    vs.AddVertex(new VertexPositionNormalColored(t.C, m, col));
                    vs.AddVertex(new VertexPositionNormalColored(t.B, m, col));
                }
                b.Vertices.Unmap();
                int indexShorts = b.TriangleCount * IndexTriangle.GetSizeInShortInts();
                b.Indices = new IndexBuffer(indexShorts);
                b.Indices.Map(indexShorts);
                IndexStreamTriangle ts = b.Indices.GetIndexStreamTriangle();
                for (int q = 0; q < b.TriangleCount; q++) ts.AddTriangle(new IndexTriangle(q * 3, q * 3 + 1, q * 3 + 2));
                b.Indices.Unmap();
                list.Add(b);
            }
            return list;
        }

        // Faces d'un volume a 8 coins : bas (0-3), haut (4-7), 4 cotes.
        private static readonly int[][] Faces =
        {
            new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 },
            new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 }
        };

        private static List<Buffers> BuildAll(List<Solid8> solids)
        {
            var list = new List<Buffers>();
            for (int i = 0; i < solids.Count; i += SolidsPerBuffer)
            {
                list.Add(Build(solids.GetRange(i, Math.Min(SolidsPerBuffer, solids.Count - i))));
            }
            return list;
        }

        private static Buffers Build(List<Solid8> solids)
        {
            var b = new Buffers { VertexCount = solids.Count * 24, TriangleCount = solids.Count * 12 };
            if (solids.Count == 0) return b;
            int vertexFloats = b.VertexCount * VertexPositionNormalColored.GetSizeInFloats();
            b.Vertices = new VertexBuffer(vertexFloats);
            b.Vertices.Map(vertexFloats);
            VertexStreamPositionNormalColored vertices = b.Vertices.GetVertexStreamPositionNormalColored();
            foreach (Solid8 s in solids)
            {
                foreach (int[] f in Faces)
                {
                    XYZ a = s.Corners[f[0]], c1 = s.Corners[f[1]], c2 = s.Corners[f[2]];
                    XYZ n = (c1 - a).CrossProduct(c2 - a);
                    n = n.GetLength() > 1e-12 ? n.Normalize() : XYZ.BasisZ;
                    // Ombrage simple par face (dessus clair, cotes plus sombres) : relief lisible.
                    double shade = n.Z > 0.5 ? 1.0 : n.Z < -0.5 ? 0.45 : Math.Abs(n.X) > Math.Abs(n.Y) ? 0.8 : 0.65;
                    ColorWithTransparency col = Shade(s.Color, shade);
                    foreach (int k in f)
                    {
                        vertices.AddVertex(new VertexPositionNormalColored(s.Corners[k], n, col));
                    }
                }
            }
            b.Vertices.Unmap();

            int indexShorts = b.TriangleCount * IndexTriangle.GetSizeInShortInts();
            b.Indices = new IndexBuffer(indexShorts);
            b.Indices.Map(indexShorts);
            IndexStreamTriangle triangles = b.Indices.GetIndexStreamTriangle();
            for (int q = 0; q < solids.Count * 6; q++)
            {
                int v = q * 4;
                triangles.AddTriangle(new IndexTriangle(v, v + 1, v + 2));
                triangles.AddTriangle(new IndexTriangle(v, v + 2, v + 3));
            }
            b.Indices.Unmap();
            return b;
        }

        private static ColorWithTransparency ToColor(string hex, uint transparency)
        {
            string h = (hex ?? "#00E5FF").TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            return new ColorWithTransparency(Convert.ToUInt32(h.Substring(0, 2), 16), Convert.ToUInt32(h.Substring(2, 2), 16),
                Convert.ToUInt32(h.Substring(4, 2), 16), transparency);
        }

        private static ColorWithTransparency Shade(ColorWithTransparency c, double f) =>
            new ColorWithTransparency((uint)(c.GetRed() * f), (uint)(c.GetGreen() * f), (uint)(c.GetBlue() * f), c.GetTransparency());
    }
}
