using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Conversion.Layers.Resource;
using CoreMesh = Conversion.Layers.Resource.Mesh;

namespace Conversion.Unity
{
    /// <summary>
    /// A mesh in a form that can be built on a worker thread and handed to Unity on
    /// the main one.
    /// <para>
    /// Nothing here touches a Unity object. Vector3 is a plain value type, safe to
    /// construct anywhere; UnityEngine.Mesh is not, and creating one off the main
    /// thread throws. So the expensive work — coordinate conversion, vertex
    /// splitting, normal generation — happens on the worker, and the main thread is
    /// left with array assignment.
    /// </para>
    /// </summary>
    public sealed class IfcMeshData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;

        /// <summary>One index array per submesh, one submesh per style span.</summary>
        public int[][] SubMeshes;

        /// <summary>The colour for each submesh, in the same order. Null means unstyled.</summary>
        public Rgba?[] SubMeshStyles;

        /// <summary>Where the mesh sat before it was re-based to its own origin.</summary>
        public Vector3 Origin;

        public int TriangleCount;

        public bool IsEmpty => Vertices == null || Vertices.Length == 0;

        /// <summary>
        /// Convert a resolved mesh into Unity's coordinate system and split it for
        /// flat shading.
        /// <para>
        /// Three things happen here that are easy to get wrong and invisible until
        /// they are:
        /// </para>
        /// <para>
        /// The handedness flip. IFC is Z-up right-handed, Unity is Y-up left-handed.
        /// Mesh.ToUnity swaps the axes and reverses the winding to compensate; do one
        /// without the other and the whole model renders inside out, which backface
        /// culling makes invisible rather than obviously wrong.
        /// </para>
        /// <para>
        /// The re-basing. Site coordinates run to tens of thousands of millimetres.
        /// Casting those straight to float32 gives visible vertex jitter and
        /// z-fighting, so each product is centred on its own origin and the offset
        /// goes onto the Transform, where Unity keeps it in double until render time.
        /// </para>
        /// <para>
        /// The vertex split. Faceted breps already carry one vertex per face corner,
        /// but triangulated and polygonal face sets share vertices between faces, and
        /// Mesh.RecalculateNormals would average across every hard edge — a wall
        /// corner shaded like a cylinder. Splitting per triangle costs memory and
        /// buys correct flat shading, which is what a building wants.
        /// </para>
        /// </summary>
        public static IfcMeshData FromCoreMesh(CoreMesh source, double unitScale, bool splitForFlatShading = true)
        {
            var data = new IfcMeshData();
            if (source == null || source.Triangles.Count == 0)
            {
                return data;
            }

            CoreMesh scaled = unitScale != 1.0 ? source.Scaled(unitScale) : source;
            CoreMesh unityMesh = scaled.ToUnity();
            CoreMesh centred = unityMesh.Recentered(out Vec3 origin);

            data.Origin = new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z);
            data.TriangleCount = centred.Triangles.Count;

            List<StyleSpan> spans = centred.Spans();

            if (splitForFlatShading)
            {
                BuildSplit(data, centred, spans);
            }
            else
            {
                BuildShared(data, centred, spans);
            }

            return data;
        }

        private static void BuildSplit(IfcMeshData data, CoreMesh mesh, List<StyleSpan> spans)
        {
            int triangleCount = mesh.Triangles.Count;
            var vertices = new Vector3[triangleCount * 3];
            var normals = new Vector3[triangleCount * 3];

            var subMeshes = new int[spans.Count][];
            var styles = new Rgba?[spans.Count];

            int cursor = 0;
            for (int s = 0; s < spans.Count; s++)
            {
                StyleSpan span = spans[s];
                var indices = new int[(span.Stop - span.Start) * 3];
                int slot = 0;

                for (int t = span.Start; t < span.Stop; t++)
                {
                    Tri tri = mesh.Triangles[t];
                    Vec3 a = mesh.Vertices[tri.A];
                    Vec3 b = mesh.Vertices[tri.B];
                    Vec3 c = mesh.Vertices[tri.C];

                    // Face normal from the source doubles, before any precision is lost.
                    Vec3 normal = Vec3.Cross(b - a, c - a);
                    if (!normal.TryNormalize(out normal))
                    {
                        normal = Vec3.UnitY;   // degenerate triangle; any normal will do
                    }
                    var faceNormal = new Vector3((float)normal.X, (float)normal.Y, (float)normal.Z);

                    vertices[cursor] = new Vector3((float)a.X, (float)a.Y, (float)a.Z);
                    vertices[cursor + 1] = new Vector3((float)b.X, (float)b.Y, (float)b.Z);
                    vertices[cursor + 2] = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
                    normals[cursor] = faceNormal;
                    normals[cursor + 1] = faceNormal;
                    normals[cursor + 2] = faceNormal;

                    indices[slot] = cursor;
                    indices[slot + 1] = cursor + 1;
                    indices[slot + 2] = cursor + 2;

                    cursor += 3;
                    slot += 3;
                }

                subMeshes[s] = indices;
                styles[s] = span.Style;
            }

            data.Vertices = vertices;
            data.Normals = normals;
            data.SubMeshes = subMeshes;
            data.SubMeshStyles = styles;
        }

        private static void BuildShared(IfcMeshData data, CoreMesh mesh, List<StyleSpan> spans)
        {
            var vertices = new Vector3[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vec3 v = mesh.Vertices[i];
                vertices[i] = new Vector3((float)v.X, (float)v.Y, (float)v.Z);
            }

            var subMeshes = new int[spans.Count][];
            var styles = new Rgba?[spans.Count];

            for (int s = 0; s < spans.Count; s++)
            {
                StyleSpan span = spans[s];
                var indices = new int[(span.Stop - span.Start) * 3];
                int slot = 0;
                for (int t = span.Start; t < span.Stop; t++)
                {
                    Tri tri = mesh.Triangles[t];
                    indices[slot] = tri.A;
                    indices[slot + 1] = tri.B;
                    indices[slot + 2] = tri.C;
                    slot += 3;
                }
                subMeshes[s] = indices;
                styles[s] = span.Style;
            }

            data.Vertices = vertices;
            data.Normals = null;   // let Unity average them
            data.SubMeshes = subMeshes;
            data.SubMeshStyles = styles;
        }

        /// <summary>
        /// Hand the data to Unity. Main thread only.
        /// </summary>
        public UnityEngine.Mesh ToUnityMesh(string name)
        {
            var mesh = new UnityEngine.Mesh { name = name };

            // Must be set before the vertices. A castle storey runs well past the
            // 65535 vertices a 16-bit index buffer can address, and the failure mode
            // is a silently truncated mesh rather than an error.
            if (Vertices.Length > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(Vertices);
            if (Normals != null)
            {
                mesh.SetNormals(Normals);
            }

            mesh.subMeshCount = SubMeshes.Length;
            for (int i = 0; i < SubMeshes.Length; i++)
            {
                mesh.SetTriangles(SubMeshes[i], i, calculateBounds: false);
            }

            if (Normals == null)
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
