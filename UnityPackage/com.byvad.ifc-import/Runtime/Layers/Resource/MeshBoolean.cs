// @author: Davy Bellens

using System;
using System.Collections.Generic;

namespace Conversion.Layers.Resource
{
    /// <summary>
    /// Solid subtraction over a BSP tree, so Core can honour IfcRelVoidsElement
    /// and IfcBooleanResult without a native geometry kernel.
    /// <para>
    /// Scope, stated honestly: exact for the closed solids that IFC openings almost
    /// always are — extruded boxes and prisms — and unreliable for open shells,
    /// self-intersecting breps, and coincident-face degeneracies. Every entry point
    /// fails soft. A wall with a missing hole is a far smaller error than a wall
    /// turned inside out.
    /// </para>
    /// <para>
    /// Style spans survive the cut: each polygon carries the style of the triangle
    /// it came from and clipped fragments inherit it, so an element styled per
    /// representation item keeps its colours. Faces created by the cut itself are
    /// left unstyled and fall through to the product's material.
    /// </para>
    /// </summary>
    public static class MeshBoolean
    {
        /// <summary>Split tolerance as a fraction of the model's own extent. A fixed
        /// absolute epsilon is either uselessly tight on a millimetre model with site
        /// coordinates in the tens of thousands, or destructive on one authored in metres.</summary>
        public const double RelativeEpsilon = 1e-8;
        public const double MinimumEpsilon = 1e-9;

        /// <summary>A guard, not a limit anyone should reach. Openings are boxes;
        /// a cutter arriving with thousands of faces means something upstream is wrong,
        /// and a BSP will happily spend minutes proving it.</summary>
        public const int MaxPolygons = 20000;

        private const int Coplanar = 0;
        private const int Back = 1;
        private const int Front = 2;
        private const int Spanning = 3;

        private sealed class Polygon
        {
            public readonly List<Vec3> Vertices;
            public readonly Rgba? Style;

            public Polygon(List<Vec3> vertices, Rgba? style)
            {
                Vertices = vertices;
                Style = style;
            }

            public Polygon Flipped()
            {
                var reversed = new List<Vec3>(Vertices);
                reversed.Reverse();
                return new Polygon(reversed, Style);
            }
        }

        private readonly struct Plane
        {
            public readonly Vec3 Normal;
            public readonly double Offset;

            public Plane(Vec3 normal, double offset)
            {
                Normal = normal;
                Offset = offset;
            }

            /// <summary>Newell's method, for the same reason the triangulator uses it.</summary>
            public static bool TryThrough(IReadOnlyList<Vec3> vertices, out Plane plane)
            {
                if (!Vec3.TryNewellNormal(vertices, 1e-30, out Vec3 normal))
                {
                    plane = default;
                    return false;
                }
                plane = new Plane(normal, Vec3.Dot(normal, vertices[0]));
                return true;
            }

            public Plane Flipped() => new Plane(-Normal, -Offset);

            public double Distance(Vec3 point) => Vec3.Dot(Normal, point) - Offset;

            /// <summary>Sort one polygon into the four buckets, splitting it if it spans.</summary>
            public void Split(Polygon polygon,
                List<Polygon> coplanarFront, List<Polygon> coplanarBack,
                List<Polygon> front, List<Polygon> back, double eps)
            {
                int polygonType = 0;
                int count = polygon.Vertices.Count;
                var types = new int[count];

                for (int i = 0; i < count; i++)
                {
                    double distance = Distance(polygon.Vertices[i]);
                    int vertexType = distance > eps ? Front : distance < -eps ? Back : Coplanar;
                    polygonType |= vertexType;
                    types[i] = vertexType;
                }

                if (polygonType == Coplanar)
                {
                    bool facing = TryThrough(polygon.Vertices, out Plane own)
                                  && Vec3.Dot(own.Normal, Normal) > 0.0;
                    (facing ? coplanarFront : coplanarBack).Add(polygon);
                }
                else if (polygonType == Front)
                {
                    front.Add(polygon);
                }
                else if (polygonType == Back)
                {
                    back.Add(polygon);
                }
                else
                {
                    var frontVertices = new List<Vec3>(count + 1);
                    var backVertices = new List<Vec3>(count + 1);

                    for (int i = 0; i < count; i++)
                    {
                        int j = (i + 1) % count;
                        int thisType = types[i];
                        int nextType = types[j];
                        Vec3 thisVertex = polygon.Vertices[i];
                        Vec3 nextVertex = polygon.Vertices[j];

                        if (thisType != Back)
                        {
                            frontVertices.Add(thisVertex);
                        }
                        if (thisType != Front)
                        {
                            backVertices.Add(thisVertex);
                        }
                        if ((thisType | nextType) == Spanning)
                        {
                            Vec3 edge = nextVertex - thisVertex;
                            double span = Vec3.Dot(Normal, edge);
                            if (Math.Abs(span) < 1e-30)
                            {
                                continue;
                            }
                            double t = (Offset - Vec3.Dot(Normal, thisVertex)) / span;
                            Vec3 crossing = thisVertex + edge * t;
                            frontVertices.Add(crossing);
                            backVertices.Add(crossing);
                        }
                    }

                    if (frontVertices.Count >= 3)
                    {
                        front.Add(new Polygon(frontVertices, polygon.Style));
                    }
                    if (backVertices.Count >= 3)
                    {
                        back.Add(new Polygon(backVertices, polygon.Style));
                    }
                }
            }
        }

        /// <summary>
        /// One cell of the BSP tree.
        /// <para>
        /// Every traversal below is iterative. Tree depth tracks the input's planar
        /// complexity rather than its triangle count, and a stepped brep or a curtain
        /// wall will bury a 1 MB default stack long before it exhausts the heap.
        /// </para>
        /// </summary>
        private sealed class Node
        {
            private readonly double _eps;
            private Plane _plane;
            private bool _hasPlane;
            private Node _front;
            private Node _back;
            private List<Polygon> _polygons = new List<Polygon>();

            public Node(double eps)
            {
                _eps = eps;
            }

            public void Build(List<Polygon> polygons)
            {
                var pending = new Stack<(Node Node, List<Polygon> Batch)>();
                pending.Push((this, polygons));

                while (pending.Count > 0)
                {
                    (Node node, List<Polygon> batch) = pending.Pop();
                    if (batch.Count == 0)
                    {
                        continue;
                    }

                    if (!node._hasPlane)
                    {
                        foreach (Polygon candidate in batch)
                        {
                            if (Plane.TryThrough(candidate.Vertices, out node._plane))
                            {
                                node._hasPlane = true;
                                break;
                            }
                        }
                        if (!node._hasPlane)
                        {
                            continue;   // every polygon in this batch was degenerate
                        }
                    }

                    var ahead = new List<Polygon>();
                    var behind = new List<Polygon>();
                    foreach (Polygon polygon in batch)
                    {
                        node._plane.Split(polygon, node._polygons, node._polygons, ahead, behind, _eps);
                    }

                    if (ahead.Count > 0)
                    {
                        node._front = node._front ?? new Node(_eps);
                        pending.Push((node._front, ahead));
                    }
                    if (behind.Count > 0)
                    {
                        node._back = node._back ?? new Node(_eps);
                        pending.Push((node._back, behind));
                    }
                }
            }

            /// <summary>Return the parts of <paramref name="polygons"/> lying outside this solid.</summary>
            public List<Polygon> ClipPolygons(List<Polygon> polygons)
            {
                var kept = new List<Polygon>();
                var pending = new Stack<(Node Node, List<Polygon> Batch)>();
                pending.Push((this, polygons));

                while (pending.Count > 0)
                {
                    (Node node, List<Polygon> batch) = pending.Pop();
                    if (batch.Count == 0)
                    {
                        continue;
                    }
                    if (!node._hasPlane)
                    {
                        kept.AddRange(batch);
                        continue;
                    }

                    var ahead = new List<Polygon>();
                    var behind = new List<Polygon>();
                    foreach (Polygon polygon in batch)
                    {
                        node._plane.Split(polygon, ahead, behind, ahead, behind, _eps);
                    }

                    if (node._front != null)
                    {
                        pending.Push((node._front, ahead));
                    }
                    else
                    {
                        kept.AddRange(ahead);
                    }

                    if (node._back != null)
                    {
                        pending.Push((node._back, behind));
                    }
                    // No back child means everything behind this plane is interior,
                    // and interior surface is exactly what a difference discards.
                }
                return kept;
            }

            public void ClipTo(Node other)
            {
                var pending = new Stack<Node>();
                pending.Push(this);
                while (pending.Count > 0)
                {
                    Node node = pending.Pop();
                    node._polygons = other.ClipPolygons(node._polygons);
                    if (node._front != null) pending.Push(node._front);
                    if (node._back != null) pending.Push(node._back);
                }
            }

            public void Invert()
            {
                var pending = new Stack<Node>();
                pending.Push(this);
                while (pending.Count > 0)
                {
                    Node node = pending.Pop();

                    for (int i = 0; i < node._polygons.Count; i++)
                    {
                        node._polygons[i] = node._polygons[i].Flipped();
                    }
                    if (node._hasPlane)
                    {
                        node._plane = node._plane.Flipped();
                    }

                    Node swap = node._front;
                    node._front = node._back;
                    node._back = swap;

                    if (node._front != null) pending.Push(node._front);
                    if (node._back != null) pending.Push(node._back);
                }
            }

            public List<Polygon> AllPolygons()
            {
                var collected = new List<Polygon>();
                var pending = new Stack<Node>();
                pending.Push(this);
                while (pending.Count > 0)
                {
                    Node node = pending.Pop();
                    collected.AddRange(node._polygons);
                    if (node._front != null) pending.Push(node._front);
                    if (node._back != null) pending.Push(node._back);
                }
                return collected;
            }
        }

        private static double EpsilonFor(Mesh mesh, IReadOnlyList<Mesh> cutters)
        {
            var bounds = BoundsAccumulator.Empty;
            bounds.Add(mesh.Vertices);
            foreach (Mesh cutter in cutters)
            {
                bounds.Add(cutter.Vertices);
            }
            if (!bounds.TryGetBounds(out Vec3 min, out Vec3 max))
            {
                return MinimumEpsilon;
            }

            double extent = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
            return Math.Max(MinimumEpsilon, extent * RelativeEpsilon);
        }

        private static List<Polygon> ToPolygons(Mesh mesh, bool keepStyle)
        {
            var styleOf = new Rgba?[mesh.Triangles.Count];
            if (keepStyle)
            {
                foreach (StyleSpan span in mesh.Spans())
                {
                    for (int i = span.Start; i < span.Stop; i++)
                    {
                        styleOf[i] = span.Style;
                    }
                }
            }

            var polygons = new List<Polygon>(mesh.Triangles.Count);
            for (int i = 0; i < mesh.Triangles.Count; i++)
            {
                Tri t = mesh.Triangles[i];
                var vertices = new List<Vec3>(3)
                {
                    mesh.Vertices[t.A],
                    mesh.Vertices[t.B],
                    mesh.Vertices[t.C],
                };
                polygons.Add(new Polygon(vertices, keepStyle ? styleOf[i] : null));
            }
            return polygons;
        }

        /// <summary>Vertices within this many decimal digits weld into one, closing the
        /// hairline gaps a BSP split leaves along coincident cut edges.</summary>
        private const int WeldPrecisionDigits = 6;

        private static Mesh ToMesh(List<Polygon> polygons)
        {
            // Group by style first so each style occupies one contiguous run of
            // triangles, which is the shape Spans() and the writer's usemtl runs expect.
            var grouped = new Dictionary<Rgba, List<Polygon>>();
            var unstyled = new List<Polygon>();
            var order = new List<Rgba>();

            foreach (Polygon polygon in polygons)
            {
                if (!polygon.Style.HasValue)
                {
                    unstyled.Add(polygon);
                    continue;
                }
                Rgba style = polygon.Style.Value;
                if (!grouped.TryGetValue(style, out List<Polygon> bucket))
                {
                    bucket = new List<Polygon>();
                    grouped[style] = bucket;
                    order.Add(style);
                }
                bucket.Add(polygon);
            }

            var mesh = new Mesh();
            var lookup = new Dictionary<(double, double, double), int>();

            int IndexOf(Vec3 vertex)
            {
                var key = (Math.Round(vertex.X, WeldPrecisionDigits), Math.Round(vertex.Y, WeldPrecisionDigits),
                    Math.Round(vertex.Z, WeldPrecisionDigits));
                if (lookup.TryGetValue(key, out int found))
                {
                    return found;
                }
                found = mesh.Vertices.Count;
                lookup[key] = found;
                mesh.Vertices.Add(vertex);
                return found;
            }

            void Emit(List<Polygon> bucket)
            {
                foreach (Polygon polygon in bucket)
                {
                    int count = polygon.Vertices.Count;
                    var indices = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        indices[i] = IndexOf(polygon.Vertices[i]);
                    }
                    for (int i = 1; i < count - 1; i++)
                    {
                        int a = indices[0];
                        int b = indices[i];
                        int c = indices[i + 1];
                        if (a == b || b == c || a == c)
                        {
                            continue;   // collapsed by the weld, contributes no area
                        }
                        mesh.AddTriangle(a, b, c);
                    }
                }
            }

            foreach (Rgba style in order)
            {
                int start = mesh.Triangles.Count;
                Emit(grouped[style]);
                int stop = mesh.Triangles.Count;
                if (stop > start)
                {
                    mesh.Groups.Add(new StyleGroup(style, start, stop));
                }
            }
            Emit(unstyled);

            return mesh;
        }

        /// <summary>How far a result's volume may exceed its theoretical ceiling before
        /// it's treated as a degeneracy rather than floating-point noise.</summary>
        private const double VolumeTolerance = 1.001;

        private static bool ExceedsCeiling(double produced, double ceiling) =>
            ceiling > 0.0 && produced > ceiling * VolumeTolerance;

        /// <summary>
        /// The specific ways a BSP pass can go wrong on pathological input: a tree
        /// deep enough to blow the stack, a split storm that exhausts memory, or a
        /// degenerate polygon that trips an internal invariant.
        /// </summary>
        private static bool IsGeometricFailure(Exception exception) =>
            exception is InsufficientExecutionStackException ||
            exception is OutOfMemoryException ||
            exception is InvalidOperationException ||
            exception is ArgumentException;

        /// <summary>Union of two solids. Backs IfcBooleanResult with operator UNION.</summary>
        public static Mesh Union(Mesh a, Mesh b) => Pair(a, b, Operation.Union);

        /// <summary>Intersection of two solids. Backs INTERSECTION, and bounds a
        /// polygonally bounded half space against its base plane.</summary>
        public static Mesh Intersect(Mesh a, Mesh b) => Pair(a, b, Operation.Intersect);

        private enum Operation { Union, Intersect }

        private static Mesh Pair(Mesh a, Mesh b, Operation operation)
        {
            if (a == null || a.Triangles.Count == 0)
            {
                return operation == Operation.Union ? b : a;
            }
            if (b == null || b.Triangles.Count == 0)
            {
                return operation == Operation.Union ? a : new Mesh();
            }
            if (a.Triangles.Count + b.Triangles.Count > MaxPolygons)
            {
                return a;
            }

            double eps = EpsilonFor(a, new[] { b });

            try
            {
                var left = new Node(eps);
                left.Build(ToPolygons(a, keepStyle: true));
                var right = new Node(eps);
                right.Build(ToPolygons(b, keepStyle: true));

                if (operation == Operation.Union)
                {
                    left.ClipTo(right);
                    right.ClipTo(left);
                    right.Invert();
                    right.ClipTo(left);
                    right.Invert();
                    left.Build(right.AllPolygons());
                }
                else
                {
                    left.Invert();
                    right.ClipTo(left);
                    right.Invert();
                    left.ClipTo(right);
                    right.ClipTo(left);
                    left.Build(right.AllPolygons());
                    left.Invert();
                }

                Mesh result = ToMesh(left.AllPolygons());

                double volumeA = Math.Abs(a.SignedVolume());
                double volumeB = Math.Abs(b.SignedVolume());
                double produced = Math.Abs(result.SignedVolume());
                double ceiling = operation == Operation.Union
                    ? volumeA + volumeB
                    : Math.Min(volumeA, volumeB);
                if (ExceedsCeiling(produced, ceiling))
                {
                    return a;
                }
                return result;
            }
            catch (Exception exception) when (IsGeometricFailure(exception))
            {
                return a;
            }
        }

        /// <summary>
        /// Return <paramref name="mesh"/> with every solid in <paramref name="cutters"/>
        /// removed. On any degeneracy the original mesh comes back unchanged.
        /// </summary>
        public static Mesh Subtract(Mesh mesh, IReadOnlyList<Mesh> cutters)
        {
            if (mesh == null || mesh.Triangles.Count == 0 || cutters == null || cutters.Count == 0)
            {
                return mesh;
            }

            var usable = new List<Mesh>();
            int total = mesh.Triangles.Count;
            foreach (Mesh cutter in cutters)
            {
                if (cutter != null && cutter.Triangles.Count > 0)
                {
                    usable.Add(cutter);
                    total += cutter.Triangles.Count;
                }
            }
            if (usable.Count == 0 || total > MaxPolygons)
            {
                return mesh;
            }

            double eps = EpsilonFor(mesh, usable);

            try
            {
                var solid = new Node(eps);
                solid.Build(ToPolygons(mesh, keepStyle: true));

                foreach (Mesh cutter in usable)
                {
                    // The cutter's own styling is meaningless on the host: an opening
                    // is a void volume, not a surface anyone sees. Dropping it lets the
                    // faces it contributes fall through to the product's material.
                    var voidSolid = new Node(eps);
                    voidSolid.Build(ToPolygons(cutter, keepStyle: false));

                    solid.Invert();
                    solid.ClipTo(voidSolid);
                    voidSolid.ClipTo(solid);
                    voidSolid.Invert();
                    voidSolid.ClipTo(solid);
                    voidSolid.Invert();
                    solid.Build(voidSolid.AllPolygons());
                    solid.Invert();
                }

                Mesh result = ToMesh(solid.AllPolygons());

                // A difference can only ever remove material. If the result claims
                // more volume than it started with, the tree hit a degeneracy and
                // produced nonsense — take the uncut solid instead. For an open
                // shell both figures are meaningless and this rejects too, which is
                // also the right answer, because a boolean on an open shell is not
                // defined in the first place.
                double before = Math.Abs(mesh.SignedVolume());
                if (ExceedsCeiling(Math.Abs(result.SignedVolume()), before))
                {
                    return mesh;
                }
                return result;
            }
            catch (Exception exception) when (IsGeometricFailure(exception))
            {
                return mesh;
            }
        }
    }
}