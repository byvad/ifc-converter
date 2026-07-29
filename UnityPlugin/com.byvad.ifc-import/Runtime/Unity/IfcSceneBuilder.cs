using System.Collections.Generic;
using UnityEngine;
using Conversion.Ifc;
using Conversion.Layers.Core;

namespace Conversion.Unity
{
    /// <summary>
    /// Instantiates the spatial tree as GameObjects.
    /// <para>
    /// Main thread only, and deliberately incremental: three and a half thousand
    /// objects created in one frame is a visible freeze, so the caller drives this
    /// a batch at a time.
    /// </para>
    /// </summary>
    public sealed class IfcSceneBuilder
    {
        private readonly IfcMaterialFactory _materials;
        private readonly Dictionary<int, IfcMeshData> _meshes;
        private readonly Dictionary<int, Layer> _layers;
        private readonly HashSet<int> _included;

        public int ObjectsCreated { get; private set; }
        public int RenderersCreated { get; private set; }
        public int TrianglesCreated { get; private set; }

        public IfcSceneBuilder(
            IfcMaterialFactory materials,
            Dictionary<int, IfcMeshData> meshes,
            Dictionary<int, Layer> layers,
            HashSet<int> included)
        {
            _materials = materials;
            _meshes = meshes;
            _layers = layers;
            _included = included;
        }

        /// <summary>
        /// Walk the tree, creating one GameObject per node, yielding after every
        /// <paramref name="objectsPerFrame"/> so the caller can hand a frame back.
        /// </summary>
        public IEnumerable<int> Build(List<SpatialNode> roots, Transform parent, int objectsPerFrame)
        {
            int sinceYield = 0;

            var pending = new Stack<(SpatialNode Node, Transform Parent)>();
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                pending.Push((roots[i], parent));
            }

            while (pending.Count > 0)
            {
                (SpatialNode node, Transform nodeParent) = pending.Pop();
                Transform created = CreateNode(node, nodeParent);

                for (int i = node.Children.Count - 1; i >= 0; i--)
                {
                    pending.Push((node.Children[i], created));
                }

                if (++sinceYield >= objectsPerFrame)
                {
                    sinceYield = 0;
                    yield return ObjectsCreated;
                }
            }

            yield return ObjectsCreated;
        }

        private Transform CreateNode(SpatialNode node, Transform parent)
        {
            IfcEntity entity = node.Entity;

            string label = node.Name;
            var go = new GameObject(string.IsNullOrEmpty(label) ? entity.IsA() : label);
            go.transform.SetParent(parent, worldPositionStays: false);
            ObjectsCreated++;

            var metadata = go.AddComponent<IfcElement>();
            metadata.GlobalId = entity.String("GlobalId");
            metadata.IfcClass = entity.IsA();
            metadata.IfcName = label;
            metadata.EntityId = entity.Id;
            metadata.Relation = node.Relation.ToString();

            if (_layers != null && _layers.TryGetValue(entity.Id, out Layer layer) && layer != null)
            {
                metadata.ConceptualLayer = layer.LayerName;
                metadata.ConceptualSchema = layer.LayerType;
            }

            if (_included != null && !_included.Contains(entity.Id))
            {
                return go.transform;   // in the tree for structure, but filtered out of the render
            }

            if (_meshes == null || !_meshes.TryGetValue(entity.Id, out IfcMeshData data) || data.IsEmpty)
            {
                return go.transform;   // a storey has no geometry of its own
            }

            // The mesh was re-based to its own origin so the vertices stay small;
            // the offset lives on the Transform, where Unity keeps full precision.
            go.transform.localPosition = data.Origin - LocalOriginOf(parent);

            UnityEngine.Mesh mesh = data.ToUnityMesh(go.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[data.SubMeshes.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = _materials.Get(data.SubMeshStyles[i]);
            }
            renderer.sharedMaterials = materials;

            RenderersCreated++;
            TrianglesCreated += data.TriangleCount;
            return go.transform;
        }

        /// <summary>
        /// Meshes are re-based against world zero, so a child's offset has to be
        /// expressed relative to whatever its parent already moved by.
        /// </summary>
        private static Vector3 LocalOriginOf(Transform parent) =>
            parent == null ? Vector3.zero : parent.position;
    }
}
