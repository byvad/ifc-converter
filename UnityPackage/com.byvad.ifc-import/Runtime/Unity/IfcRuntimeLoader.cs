// @author: Davy Bellens

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Conversion.Unity
{
    public sealed class IfcLoadOptions
    {
        public bool IncludeOpenings = true;
        public bool Colour = true;
        public double MinAlpha = 0.25;
        public bool SplitForFlatShading = true;
        public int ObjectsPerFrame = 250;
        public ICollection<string> Layers;
        public ICollection<string> Schemas;
        public ICollection<string> Classes;
        public Func<string, string> SchemaText;
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

    public sealed class IfcRuntimeLoader : MonoBehaviour
    {
        /// <summary>How much of the progress bar the background prepare phase owns;
        /// the rest goes to building the scene. The two are defined from each other
        /// so they can't drift apart and leave the bar short of 100%.</summary>
        private const float PrepareProgressShare = 0.8f;
        private const float BuildProgressShare = 1f - PrepareProgressShare;

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
            var progress = new PipelineProgress();

            IfcImportPipeline.LoadDefaultTables(options,
                out Dictionary<string, string> schemaCache, out List<string> taxonomyDocuments);

            Task<PreparedImport> work = Task.Run(
                () => IfcImportPipeline.Prepare(path, options, schemaCache, taxonomyDocuments, progress));

            while (!work.IsCompleted)
            {
                progress.Read(out string stage, out float fraction);
                onProgress?.Invoke(fraction * PrepareProgressShare, stage);
                yield return null;
            }

            if (work.IsFaulted)
            {
                result.Error = work.Exception?.GetBaseException();
                Debug.LogError($"[IFC] Load failed: {result.Error}");
                onComplete?.Invoke(result);
                yield break;
            }

            PreparedImport prepared = work.Result;
            var watch = Stopwatch.StartNew();

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            var materials = new IfcMaterialFactory { MinimumAlpha = (float)options.MinAlpha };
            var builder = new IfcSceneBuilder(materials, prepared.Meshes, prepared.Layers, prepared.Included);

            foreach (int created in builder.Build(prepared.Roots, root.transform, options.ObjectsPerFrame))
            {
                float builtFraction = BuildProgressShare * created / Mathf.Max(1, prepared.Roots.Count + prepared.Products);
                onProgress?.Invoke(PrepareProgressShare + builtFraction, "Building scene");
                yield return null;
            }
            watch.Stop();

            PopulateResult(result, root, builder, materials, prepared, watch.ElapsedMilliseconds);

            onProgress?.Invoke(1f, "Done");
            onComplete?.Invoke(result);
        }

        private static void PopulateResult(IfcLoadResult result, GameObject root, IfcSceneBuilder builder,
            IfcMaterialFactory materials, PreparedImport prepared, long instantiateMilliseconds)
        {
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
            result.InstantiateMilliseconds = instantiateMilliseconds;
        }
    }
}