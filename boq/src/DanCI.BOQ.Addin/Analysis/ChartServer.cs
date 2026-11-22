using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Diagnostics;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Barres 3D du diagramme dessinees DANS la vue 3D par DirectContext3D. Affichage seulement :
    /// aucune transaction, rien n'est ecrit dans le modele, rien n'est enregistre avec le projet ;
    /// les barres ne sont ni selectionnables, ni imprimees, ni exportees.
    /// </summary>
    internal sealed class ChartServer : IDirectContext3DServer
    {
        private static readonly Guid ServerId = new Guid("B8E2D0A4-5F37-4C19-8E6A-2D41C7F09B53");
        private static ChartServer instance;

        private readonly object gate = new object();
        private List<ChartBox> boxes = new List<ChartBox>();
        private string documentKey;
        private Outline outline;
        private VertexBuffer vertexBuffer;
        private IndexBuffer indexBuffer;
        private int vertexCount;
        private int triangleCount;
        private bool dirty = true;

        public static bool IsActive => instance != null && instance.boxes.Count > 0;

        /// <summary>Affiche les barres dans les vues 3D du document (contexte API Revit).</summary>
        public static void Show(Document doc, List<ChartBox> chartBoxes)
        {
            if (instance == null)
            {
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
                BoqLog.Info("Serveur DirectContext3D du diagramme enregistré.");
            }
            instance.Update(DocumentKey(doc), chartBoxes);
        }

        /// <summary>Retire les barres (le serveur reste enregistre, sans rien dessiner).</summary>
        public static void Hide() => instance?.Update(null, new List<ChartBox>());

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

        private void Update(string key, List<ChartBox> chartBoxes)
        {
            lock (gate)
            {
                documentKey = key;
                boxes = chartBoxes ?? new List<ChartBox>();
                outline = boxes.Count == 0 ? null : new Outline(
                    new XYZ(boxes.Min(b => b.MinX), boxes.Min(b => b.MinY), boxes.Min(b => b.MinZ)),
                    new XYZ(boxes.Max(b => b.MaxX), boxes.Max(b => b.MaxY), boxes.Max(b => b.MaxZ)));
                dirty = true;
            }
        }

        // ------------------------------------------------------------------ IDirectContext3DServer
        public Guid GetServerId() => ServerId;
        public string GetVendorId() => "DanCI";
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "DanCI — barres 3D du métré";
        public string GetDescription() => "Diagramme des quantités du BOQ dessiné à côté du modèle (affichage seulement).";
        public string GetApplicationId() => string.Empty;
        public string GetSourceId() => string.Empty;
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View dBView) => false;

        public bool CanExecute(View dBView)
        {
            lock (gate)
            {
                return boxes.Count > 0 && dBView is View3D && dBView.Document != null && DocumentKey(dBView.Document) == documentKey;
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
                if (DrawContext.IsTransparentPass()) return;
                lock (gate)
                {
                    if (boxes.Count == 0) return;
                    if (dirty || vertexBuffer == null || !vertexBuffer.IsValid() || indexBuffer == null || !indexBuffer.IsValid())
                    {
                        BuildBuffers();
                    }
                    if (triangleCount == 0) return;
                    var format = new VertexFormat(VertexFormatBits.PositionNormalColored);
                    var effect = new EffectInstance(VertexFormatBits.PositionNormalColored);
                    DrawContext.FlushBuffer(vertexBuffer, vertexCount, indexBuffer, triangleCount * 3, format, effect,
                        PrimitiveType.TriangleList, 0, triangleCount);
                }
            }
            catch (Exception ex)
            {
                // Jamais d'exception dans la boucle de rendu de Revit : on journalise et on n'affiche rien.
                BoqLog.Error("Dessin des barres 3D en échec.", ex);
                lock (gate)
                {
                    boxes = new List<ChartBox>();
                }
            }
        }

        // Faces d'une boite : normale et 4 coins (indices 0..7 : bit0 = X max, bit1 = Y max, bit2 = Z max).
        private static readonly (XYZ Normal, int[] Corners)[] Faces =
        {
            (new XYZ(0, 0, 1), new[] { 4, 5, 7, 6 }),
            (new XYZ(0, 0, -1), new[] { 0, 2, 3, 1 }),
            (new XYZ(1, 0, 0), new[] { 1, 3, 7, 5 }),
            (new XYZ(-1, 0, 0), new[] { 0, 4, 6, 2 }),
            (new XYZ(0, 1, 0), new[] { 2, 6, 7, 3 }),
            (new XYZ(0, -1, 0), new[] { 0, 1, 5, 4 }),
        };

        private void BuildBuffers()
        {
            vertexCount = boxes.Count * 24;
            triangleCount = boxes.Count * 12;
            int vertexFloats = vertexCount * VertexPositionNormalColored.GetSizeInFloats();
            vertexBuffer = new VertexBuffer(vertexFloats);
            vertexBuffer.Map(vertexFloats);
            VertexStreamPositionNormalColored vertices = vertexBuffer.GetVertexStreamPositionNormalColored();
            foreach (ChartBox b in boxes)
            {
                ColorWithTransparency baseColor = ToColor(b.Color);
                XYZ[] corners = new XYZ[8];
                for (int i = 0; i < 8; i++)
                {
                    corners[i] = new XYZ((i & 1) != 0 ? b.MaxX : b.MinX, (i & 2) != 0 ? b.MaxY : b.MinY, (i & 4) != 0 ? b.MaxZ : b.MinZ);
                }
                foreach (var face in Faces)
                {
                    // Ombrage simple par face (dessus clair, cotes plus sombres) : relief lisible sans eclairage.
                    double shade = face.Normal.Z > 0 ? 1.0 : face.Normal.Z < 0 ? 0.45 : Math.Abs(face.Normal.X) > 0 ? 0.8 : 0.65;
                    ColorWithTransparency c = Shade(baseColor, shade);
                    foreach (int k in face.Corners)
                    {
                        vertices.AddVertex(new VertexPositionNormalColored(corners[k], face.Normal, c));
                    }
                }
            }
            vertexBuffer.Unmap();

            int indexShorts = triangleCount * IndexTriangle.GetSizeInShortInts();
            indexBuffer = new IndexBuffer(indexShorts);
            indexBuffer.Map(indexShorts);
            IndexStreamTriangle triangles = indexBuffer.GetIndexStreamTriangle();
            for (int q = 0; q < boxes.Count * 6; q++)
            {
                int v = q * 4;
                triangles.AddTriangle(new IndexTriangle(v, v + 1, v + 2));
                triangles.AddTriangle(new IndexTriangle(v, v + 2, v + 3));
            }
            indexBuffer.Unmap();
            dirty = false;
        }

        private static ColorWithTransparency ToColor(string hex)
        {
            string h = (hex ?? "#00E5FF").TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            return new ColorWithTransparency(Convert.ToUInt32(h.Substring(0, 2), 16), Convert.ToUInt32(h.Substring(2, 2), 16),
                Convert.ToUInt32(h.Substring(4, 2), 16), 0);
        }

        private static ColorWithTransparency Shade(ColorWithTransparency c, double f) =>
            new ColorWithTransparency((uint)(c.GetRed() * f), (uint)(c.GetGreen() * f), (uint)(c.GetBlue() * f), 0);
    }
}
