// @author: Davy Bellens

using System;
using System.Collections.Generic;

namespace Conversion.Layers.Resource
{
    /// <summary>A resolved surface colour. Alpha comes from IfcSurfaceStyleShading.Transparency.</summary>
    public readonly struct Rgba : IEquatable<Rgba>
    {
        public readonly double R;
        public readonly double G;
        public readonly double B;
        public readonly double A;

        public Rgba(double r, double g, double b, double a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public bool Equals(Rgba other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object obj) => obj is Rgba other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = R.GetHashCode();
                hash = hash * 397 ^ G.GetHashCode();
                hash = hash * 397 ^ B.GetHashCode();
                hash = hash * 397 ^ A.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"rgba({R:0.###}, {G:0.###}, {B:0.###}, {A:0.###})";

        /// <summary>
        /// The stable "ifc_RRGGBBAA" name every output path agrees on for this
        /// colour — Unity materials, the .mtl writer, and the Python original all
        /// derive the same name from the same channels, so it's diffable across
        /// all three. Assumes R/G/B/A are already in [0, 1]; each caller is
        /// responsible for its own clamping policy before calling this.
        /// </summary>
        public string HexName() =>
            string.Format("ifc_{0:X2}{1:X2}{2:X2}{3:X2}",
                (int)Math.Round(R * 255.0),
                (int)Math.Round(G * 255.0),
                (int)Math.Round(B * 255.0),
                (int)Math.Round(A * 255.0));
    }

    public readonly struct Tri
    {
        public readonly int A;
        public readonly int B;
        public readonly int C;

        public Tri(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        public override string ToString() => $"({A}, {B}, {C})";
    }

    /// <summary>A contiguous run of triangles sharing one style.</summary>
    public readonly struct StyleGroup
    {
        public readonly Rgba Style;
        public readonly int Start;
        public readonly int Stop;

        public StyleGroup(Rgba style, int start, int stop)
        {
            Style = style;
            Start = start;
            Stop = stop;
        }
    }

    /// <summary>
    /// A run of triangles with a style that may be absent. Gaps between groups come
    /// back with <see cref="Style"/> null so the writer can drop to a fallback material.
    /// </summary>
    public readonly struct StyleSpan
    {
        public readonly Rgba? Style;
        public readonly int Start;
        public readonly int Stop;

        public StyleSpan(Rgba? style, int start, int stop)
        {
            Style = style;
            Start = start;
            Stop = stop;
        }
    }

    /// <summary>The thing we are building on the way back up the descent.</summary>
    public sealed class Mesh
    {
        public readonly List<Vec3> Vertices = new List<Vec3>();
        public readonly List<Tri> Triangles = new List<Tri>();
        public readonly List<StyleGroup> Groups = new List<StyleGroup>();

        public int TriangleCount => Triangles.Count;
        public bool IsEmpty => Triangles.Count == 0;

        public void AddTriangle(int a, int b, int c) => Triangles.Add(new Tri(a, b, c));

        /// <summary>
        /// Append a ring of vertices and return the index the ring starts at.
        /// <para>
        /// The Python version hands back a list of indices; they are always
        /// contiguous, so returning the base index says the same thing without
        /// allocating an array for every face in a brep.
        /// </para>
        /// </summary>
        public int AddPolygonRing(IReadOnlyList<Vec3> points)
        {
            int start = Vertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vertices.Add(points[i]);
            }
            return start;
        }

        /// <summary>
        /// Claim every triangle not already styled. Called once per level of the
        /// descent, so a representation item's own style wins over the product's material.
        /// </summary>
        public void FillStyle(Rgba? style)
        {
            if (!style.HasValue)
            {
                return;
            }
            foreach ((int start, int stop) in Gaps())
            {
                Groups.Add(new StyleGroup(style.Value, start, stop));
            }
        }

        private List<StyleGroup> OrderedGroups()
        {
            var ordered = new List<StyleGroup>(Groups);
            ordered.Sort((x, y) => x.Start.CompareTo(y.Start));
            return ordered;
        }

        private List<(int Start, int Stop)> Gaps()
        {
            var gaps = new List<(int, int)>();
            List<StyleGroup> ordered = OrderedGroups();

            int cursor = 0;
            foreach (StyleGroup group in ordered)
            {
                if (group.Start > cursor)
                {
                    gaps.Add((cursor, group.Start));
                }
                cursor = Math.Max(cursor, group.Stop);
            }
            if (cursor < Triangles.Count)
            {
                gaps.Add((cursor, Triangles.Count));
            }
            return gaps;
        }

        /// <summary>Every triangle exactly once, in order, tagged with its style or null.</summary>
        public List<StyleSpan> Spans()
        {
            var spans = new List<StyleSpan>();
            List<StyleGroup> ordered = OrderedGroups();

            int cursor = 0;
            foreach (StyleGroup group in ordered)
            {
                if (group.Start > cursor)
                {
                    spans.Add(new StyleSpan(null, cursor, group.Start));
                    cursor = group.Start;
                }
                if (group.Stop > cursor)
                {
                    spans.Add(new StyleSpan(group.Style, cursor, group.Stop));
                    cursor = group.Stop;
                }
            }
            if (cursor < Triangles.Count)
            {
                spans.Add(new StyleSpan(null, cursor, Triangles.Count));
            }
            return spans;
        }

        public void Extend(Mesh other) => Extend(other, null);

        public void Extend(Mesh other, Matrix4? transform)
        {
            int vertexOffset = Vertices.Count;
            int triangleOffset = Triangles.Count;

            if (transform.HasValue)
            {
                Matrix4 matrix = transform.Value;
                for (int i = 0; i < other.Vertices.Count; i++)
                {
                    Vertices.Add(matrix.TransformPoint(other.Vertices[i]));
                }
            }
            else
            {
                Vertices.AddRange(other.Vertices);
            }

            for (int i = 0; i < other.Triangles.Count; i++)
            {
                Tri t = other.Triangles[i];
                Triangles.Add(new Tri(t.A + vertexOffset, t.B + vertexOffset, t.C + vertexOffset));
            }

            for (int i = 0; i < other.Groups.Count; i++)
            {
                StyleGroup g = other.Groups[i];
                Groups.Add(new StyleGroup(g.Style, g.Start + triangleOffset, g.Stop + triangleOffset));
            }
        }

        private Mesh CloneTopology()
        {
            var mesh = new Mesh();
            mesh.Triangles.AddRange(Triangles);
            mesh.Groups.AddRange(Groups);
            return mesh;
        }

        public Mesh Transformed(in Matrix4 matrix)
        {
            Mesh mesh = CloneTopology();
            for (int i = 0; i < Vertices.Count; i++)
            {
                mesh.Vertices.Add(matrix.TransformPoint(Vertices[i]));
            }

            // A mirroring transform turns every face inside out. Reversing the
            // winding here keeps the mesh consistent for anything downstream that
            // cares about facing — the boolean does, and so does Unity.
            if (matrix.IsMirroring)
            {
                for (int i = 0; i < mesh.Triangles.Count; i++)
                {
                    Tri t = mesh.Triangles[i];
                    mesh.Triangles[i] = new Tri(t.A, t.C, t.B);
                }
            }
            return mesh;
        }

        public Mesh Scaled(double factor)
        {
            Mesh mesh = CloneTopology();
            for (int i = 0; i < Vertices.Count; i++)
            {
                mesh.Vertices.Add(Vertices[i] * factor);
            }
            return mesh;
        }

        /// <summary>
        /// IFC Z-up to right-handed Y-up. This is the OBJ and glTF convention.
        /// Handedness is preserved, so winding is left alone.
        /// </summary>
        public Mesh ToYUp()
        {
            Mesh mesh = CloneTopology();
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vec3 v = Vertices[i];
                mesh.Vertices.Add(new Vec3(v.X, v.Z, -v.Y));
            }
            return mesh;
        }

        /// <summary>
        /// IFC Z-up right-handed to Unity's Y-up left-handed.
        /// <para>
        /// Note this is <i>not</i> <see cref="ToYUp"/>. Dropping the sign flips
        /// handedness, which reverses the sense of every triangle, so the winding
        /// has to be reversed to compensate. Skip that and the whole model renders
        /// inside out — invisible under backface culling, which is exactly the
        /// failure that never shows up in a double-sided viewer like Blender.
        /// </para>
        /// </summary>
        public Mesh ToUnity()
        {
            var mesh = new Mesh();
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vec3 v = Vertices[i];
                mesh.Vertices.Add(new Vec3(v.X, v.Z, v.Y));
            }
            for (int i = 0; i < Triangles.Count; i++)
            {
                Tri t = Triangles[i];
                mesh.Triangles.Add(new Tri(t.A, t.C, t.B));
            }
            mesh.Groups.AddRange(Groups);
            return mesh;
        }

        /// <summary>
        /// Shift the mesh so its bounding-box centre sits at the origin, reporting
        /// the offset. Do this per product before casting to float32: it is what
        /// keeps a building on a site at (150000, 480000) from shimmering.
        /// </summary>
        public Mesh Recentered(out Vec3 origin)
        {
            var bounds = BoundsAccumulator.Empty;
            bounds.Add(Vertices);
            if (!bounds.TryGetBounds(out Vec3 min, out Vec3 max))
            {
                origin = Vec3.Zero;
                return CloneTopology();
            }

            origin = new Vec3((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);

            Mesh mesh = CloneTopology();
            foreach (Vec3 v in Vertices)
            {
                mesh.Vertices.Add(v - origin);
            }
            return mesh;
        }

        /// <summary>Signed volume via the divergence theorem. Zero for an open shell,
        /// negative for an inverted one — a cheap validity check before a boolean.</summary>
        public double SignedVolume()
        {
            double total = 0.0;
            foreach (Tri t in Triangles)
            {
                Vec3 a = Vertices[t.A];
                Vec3 b = Vertices[t.B];
                Vec3 c = Vertices[t.C];
                total += Vec3.Dot(a, Vec3.Cross(b - a, c - a)) / 6.0;
            }
            return total;
        }
    }
}