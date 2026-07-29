using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Conversion.Ifc;
using Conversion.Layers;
using Conversion.Layers.Core;
using Conversion.Layers.Resource;
using Debug = UnityEngine.Debug;

namespace Conversion.Unity
{
    public sealed class IfcLoadOptions
    {
        /// <summary>Subtract IfcRelVoidsElement openings. Off means every window sits
        /// buried in a solid wall.</summary>
        public bool IncludeOpenings = true;

        public bool Colour = true;

        /// <summary>Opacity floor for glazing authored at Transparency 1.0.</summary>
        public double MinAlpha = 0.25;

        public bool SplitForFlatShading = true;

        /// <summary>GameObjects created per frame during instantiation.</summary>
        public int ObjectsPerFrame = 250;

        public ICollection<string> Layers;
        public ICollection<string> Schemas;
        public ICollection<string> Classes;

        /// <summary>Schema name (IFC2X3, IFC4) to the contents of its .schema table.
        /// Defaults to Resources/IfcSchemas/&lt;lowercase name&gt;.</summary>
        public Func<string, string> SchemaText;

        /// <summary>The four conceptual-layer taxonomy documents. Defaults to every
        /// TextAsset under Resources/IfcTaxonomy.</summary>
        public Func<IEnumerable<string>> TaxonomyJson;
    }

    public sealed class IfcLoadResult
    {
        public GameObject Root;
        public int Nodes;
        public int Renderers;
        public int Triangles;
        public int Materials;
        public int Products;
        public int OpeningsCut;
        public double UnitScale;
        public string SchemaName;
        public long ParseMilliseconds;
        public long MeshMilliseconds;
        public long InstantiateMilliseconds;
        public Exception Error;

        public bool Succeeded => Error == null && Root != null;
    }

    /// <summary>
    /// Loads an IFC file into the scene at runtime.
    /// <para>
    /// Parsing and meshing run on a worker thread; only the Unity object creation
    /// happens on the main one, spread across frames. On the castle that is roughly
    /// five seconds of background work and a few hundred milliseconds of
    /// instantiation, rather than a five-second freeze.
    /// </para>
    /// <para>
    /// The worker is deliberately a single thread. IfcModel builds its inverse
    /// indices lazily and caches them in a plain dictionary, and Palette caches
    /// resolved product colours the same way — both would race if products were
    /// resolved in parallel. Fanning this out means pre-building those caches first,
    /// or giving each worker its own resolver.
    /// </para>
    /// </summary>
    public sealed class IfcRuntimeLoader : MonoBehaviour
    {
        private sealed class Progress
        {
            private int _done;
            private int _total;
            private string _stage = "Starting";

            public void Set(string stage, int done, int total)
            {
                Volatile.Write(ref _stage, stage);
                Volatile.Write(ref _done, done);
                Volatile.Write(ref _total, total);
            }

            public void Read(out string stage, out float fraction)
            {
                stage = Volatile.Read(ref _stage);
                int total = Volatile.Read(ref _total);
                fraction = total > 0 ? Mathf.Clamp01(Volatile.Read(ref _done) / (float)total) : 0f;
            }
        }

        private sealed class Prepared
        {
            public IfcModel Model;
            public List<SpatialNode> Roots;
            public Dictionary<int, IfcMeshData> Meshes;
            public Dictionary<int, Layer> Layers;
            public HashSet<int> Included;
            public int Products;
            public int OpeningsCut;
            public int Triangles;
            public double UnitScale;
            public long ParseMilliseconds;
            public long MeshMilliseconds;
        }

        public Coroutine Load(
            string path,
            IfcLoadOptions options = null,
            Action<IfcLoadResult> onComplete = null,
            Action<float, string> onProgress = null)
        {
            return StartCoroutine(LoadRoutine(path, options ?? new IfcLoadOptions(), onComplete, onProgress));
        }

        private IEnumerator LoadRoutine(
            string path, IfcLoadOptions options,
            Action<IfcLoadResult> onComplete, Action<float, string> onProgress)
        {
            var result = new IfcLoadResult();
            var progress = new Progress();

            Func<string, string> schemaText = options.SchemaText ?? DefaultSchemaText;
            Func<IEnumerable<string>> taxonomy = options.TaxonomyJson ?? DefaultTaxonomy;

            // Resources.Load is main-thread only, so pull the tables across before
            // handing off to the worker.
            List<string> taxonomyDocuments = new List<string>(taxonomy());
            var schemaCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in new[] { "IFC2X3", "IFC4" })
            {
                string text = schemaText(name);
                if (text != null)
                {
                    schemaCache[name] = text;
                }
            }

            Task<Prepared> work = Task.Run(() => Prepare(path, options, schemaCache, taxonomyDocuments, progress));

            while (!work.IsCompleted)
            {
                progress.Read(out string stage, out float fraction);
                onProgress?.Invoke(fraction * 0.8f, stage);
                yield return null;
            }

            if (work.IsFaulted)
            {
                result.Error = work.Exception?.GetBaseException();
                Debug.LogError($"[IFC] Load failed: {result.Error}");
                onComplete?.Invoke(result);
                yield break;
            }

            Prepared prepared = work.Result;
            var watch = Stopwatch.StartNew();

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            var materials = new IfcMaterialFactory { MinimumAlpha = (float)options.MinAlpha };
            var builder = new IfcSceneBuilder(materials, prepared.Meshes, prepared.Layers, prepared.Included);

            foreach (int created in builder.Build(prepared.Roots, root.transform, options.ObjectsPerFrame))
            {
                onProgress?.Invoke(0.8f + 0.2f * created / Mathf.Max(1, prepared.Roots.Count + prepared.Products),
                    "Building scene");
                yield return null;
            }
            watch.Stop();

            result.Root = root;
            result.Nodes = builder.ObjectsCreated;
            result.Renderers = builder.RenderersCreated;
            result.Triangles = builder.TrianglesCreated;
            result.Materials = materials.Count;
            result.Products = prepared.Products;
            result.OpeningsCut = prepared.OpeningsCut;
            result.UnitScale = prepared.UnitScale;
            result.SchemaName = prepared.Model.SchemaName;
            result.ParseMilliseconds = prepared.ParseMilliseconds;
            result.MeshMilliseconds = prepared.MeshMilliseconds;
            result.InstantiateMilliseconds = watch.ElapsedMilliseconds;

            onProgress?.Invoke(1f, "Done");
            onComplete?.Invoke(result);
        }

        private static Prepared Prepare(
            string path, IfcLoadOptions options,
            Dictionary<string, string> schemaCache, List<string> taxonomyDocuments,
            Progress progress)
        {
            var prepared = new Prepared();
            var watch = Stopwatch.StartNew();

            progress.Set("Reading file", 0, 1);
            var registry = new IfcSchemaRegistry(name =>
                schemaCache.TryGetValue(name, out string text) ? text : null);
            prepared.Model = StepParser.Load(path, registry);
            prepared.ParseMilliseconds = watch.ElapsedMilliseconds;

            watch.Restart();
            prepared.UnitScale = Units.LengthScale(prepared.Model);

            Classification classification = Classification.FromJson(taxonomyDocuments);
            ProductSelection selection = new Selection(classification)
                .Select(prepared.Model, options.Layers, options.Schemas, options.Classes);

            prepared.Included = new HashSet<int>();
            prepared.Layers = new Dictionary<int, Layer>();
            foreach ((IfcEntity product, Layer layer) in selection.Entries)
            {
                prepared.Included.Add(product.Id);
                prepared.Layers[product.Id] = layer;
            }
            prepared.Products = selection.Count;

            prepared.Roots = Spatial.Build(prepared.Model);

            Palette palette = options.Colour
                ? new Palette(prepared.Model, options.MinAlpha)
                : null;
            var builder = new Builder { PlaneAngleScale = Units.PlaneAngleScale(prepared.Model) };
            var resolver = new ProductResolver(builder, palette);

            prepared.Meshes = new Dictionary<int, IfcMeshData>(selection.Count);
            int index = 0;
            foreach ((IfcEntity product, Layer _) in selection.Entries)
            {
                progress.Set("Building geometry", index++, selection.Count);

                ProductGeometry geometry = resolver.Resolve(product, options.IncludeOpenings);
                prepared.OpeningsCut += geometry.OpeningsCut;
                if (!geometry.HasGeometry)
                {
                    continue;
                }

                IfcMeshData data = IfcMeshData.FromCoreMesh(
                    geometry.Mesh, prepared.UnitScale, options.SplitForFlatShading);
                if (data.IsEmpty)
                {
                    continue;
                }
                prepared.Meshes[product.Id] = data;
                prepared.Triangles += data.TriangleCount;
            }

            prepared.MeshMilliseconds = watch.ElapsedMilliseconds;
            return prepared;
        }

        private static string DefaultSchemaText(string schemaName)
        {
            var asset = Resources.Load<TextAsset>("IfcSchemas/" + schemaName.ToLowerInvariant());
            return asset != null ? asset.text : null;
        }

        private static IEnumerable<string> DefaultTaxonomy()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>("IfcTaxonomy");
            var documents = new List<string>(assets.Length);
            foreach (TextAsset asset in assets)
            {
                documents.Add(asset.text);
            }
            return documents;
        }
    }
}
