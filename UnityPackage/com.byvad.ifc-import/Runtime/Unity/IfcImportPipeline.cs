using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Conversion.Ifc;
using Conversion.Layers;
using Conversion.Layers.Core;
using Conversion.Layers.Resource;

namespace Conversion.Unity
{
    /// <summary>
    /// Progress reporting that is safe to write from a worker thread and read from
    /// the main one. Plain volatile fields rather than an event: an event fired from
    /// a worker thread into Unity API territory is how you get "can only be called
    /// from the main thread" exceptions in someone else's code, not yours.
    /// </summary>
    public sealed class PipelineProgress
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

    /// <summary>Everything resolved before a single GameObject exists.</summary>
    public sealed class PreparedImport
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

    /// <summary>
    /// The parse-and-mesh half of an import, with no opinion on how the caller
    /// schedules it.
    /// <para>
    /// This exists as its own type because the two front doors onto it need to run
    /// it differently. <see cref="IfcRuntimeLoader"/> is a runtime, frame-budgeted
    /// load: it has to keep the game responsive, so it runs this on a worker thread
    /// via Task.Run and polls <see cref="PipelineProgress"/> once a frame from a
    /// coroutine. An editor import window is a one-shot blocking action — the same
    /// shape as the old Python tool's <c>Process.WaitForExit()</c> — and can call
    /// <see cref="Prepare"/> directly on the main thread with a static progress bar,
    /// because Unity's coroutine scheduler does not tick outside Play Mode and there
    /// is no frame to protect. Neither caller should have to know how the other one
    /// works, so the actual resolution logic lives here exactly once.
    /// </para>
    /// </summary>
    public static class IfcImportPipeline
    {
        public static PreparedImport Prepare(
            string path,
            IfcLoadOptions options,
            IReadOnlyDictionary<string, string> schemaCache,
            IReadOnlyList<string> taxonomyDocuments,
            PipelineProgress progress)
        {
            var prepared = new PreparedImport();
            var watch = Stopwatch.StartNew();

            progress?.Set("Reading file", 0, 1);
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

            Palette palette = options.Colour ? new Palette(prepared.Model, options.MinAlpha) : null;
            var builder = new Builder { PlaneAngleScale = Units.PlaneAngleScale(prepared.Model) };
            var resolver = new ProductResolver(builder, palette);

            prepared.Meshes = new Dictionary<int, IfcMeshData>(selection.Count);
            int index = 0;
            foreach ((IfcEntity product, Layer _) in selection.Entries)
            {
                progress?.Set("Building geometry", index++, selection.Count);

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

        /// <summary>
        /// Instantiate a prepared import into the scene, fully — no frame budget.
        /// Correct for a one-shot editor action; a runtime caller wants
        /// <see cref="IfcSceneBuilder.Build"/> directly so it can spread the work
        /// across frames instead.
        /// </summary>
        public static GameObject Instantiate(
            PreparedImport prepared, IfcLoadOptions options, string rootName, out IfcSceneBuilder builder)
        {
            var root = new GameObject(rootName);
            var materials = new IfcMaterialFactory { MinimumAlpha = (float)options.MinAlpha };
            builder = new IfcSceneBuilder(materials, prepared.Meshes, prepared.Layers, prepared.Included);

            // Drain fully rather than yielding: nothing here needs to give a frame back.
            foreach (int _ in builder.Build(prepared.Roots, root.transform, int.MaxValue))
            {
            }

            return root;
        }

        /// <summary>
        /// Pull the schema tables and taxonomy documents across to plain strings.
        /// <see cref="Resources.Load"/> is main-thread only, so this has to happen
        /// before any work is handed to a worker thread.
        /// </summary>
        public static void LoadDefaultTables(
            IfcLoadOptions options,
            out Dictionary<string, string> schemaCache,
            out List<string> taxonomyDocuments)
        {
            Func<string, string> schemaText = options.SchemaText ?? DefaultSchemaText;
            Func<IEnumerable<string>> taxonomy = options.TaxonomyJson ?? DefaultTaxonomy;

            taxonomyDocuments = new List<string>(taxonomy());
            schemaCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in new[] { "IFC2X3", "IFC4" })
            {
                string text = schemaText(name);
                if (text != null)
                {
                    schemaCache[name] = text;
                }
            }
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
