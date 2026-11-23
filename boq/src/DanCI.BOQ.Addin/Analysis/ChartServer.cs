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

        private readonly object gate = new object();
        private readonly Dictionary<string, List<Solid8>> layers = new Dictionary<string, List<Solid8>>(StringComparer.Ordinal);
        private string documentKey;
        private Outline outline;
        private bool dirty = true;
        private List<Buffers> opaque = new List<Buffers>(), transparent = new List<Buffers>();

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
            public bool IsValid => TriangleCount > 0 && Vertices != null && Vertices.IsValid() && Indices != null && Indices.IsValid();
        }

        public static bool IsActive => instance != null && instance.layers.Values.Any(l => l.Count > 0);

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

        public static void Hide4D() => ClearLayer(BuildLayer);

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
                if (key != documentKey)
                {
                    layers.Clear(); // autre projet : on ne garde rien de l'ancien
                    documentKey = key;
                }
                layers[layer] = solids ?? new List<Solid8>();
                var all = layers.Values.SelectMany(l => l).SelectMany(s => s.Corners).ToList();
                outline = all.Count == 0 ? null : new Outline(
                    new XYZ(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z)),
                    new XYZ(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z)));
                dirty = true;
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
                return layers.Values.Any(l => l.Count > 0) && dBView is View3D && dBView.Document != null && DocumentKey(dBView.Document) == documentKey;
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
                    if (dirty)
                    {
                        var all = layers.Values.SelectMany(l => l).ToList();
                        opaque = BuildAll(all.Where(s => !s.Transparent).ToList());
                        transparent = BuildAll(all.Where(s => s.Transparent).ToList());
                        dirty = false;
                    }
                    var format = new VertexFormat(VertexFormatBits.PositionNormalColored);
                    var effect = new EffectInstance(VertexFormatBits.PositionNormalColored);
                    foreach (Buffers b in DrawContext.IsTransparentPass() ? transparent : opaque)
                    {
                        if (b == null || !b.IsValid)
                        {
                            if (b != null && b.TriangleCount > 0) dirty = true; // tampons perdus (changement de vue) : reconstruits au prochain rendu
                            continue;
                        }
                        DrawContext.FlushBuffer(b.Vertices, b.VertexCount, b.Indices, b.TriangleCount * 3, format, effect,
                            PrimitiveType.TriangleList, 0, b.TriangleCount);
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
                    dirty = true;
                }
            }
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
