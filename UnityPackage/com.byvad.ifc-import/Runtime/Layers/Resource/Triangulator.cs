// @author: Davy Bellens

using System;
using System.Collections.Generic;

namespace Conversion.Layers.Resource
{
    /// <summary>
    /// Counts of inner bounds that were successfully cut open versus filled in.
    /// <para>
    /// An instance rather than the module-level singleton the Python uses. Meshing
    /// is the obvious thing to push onto worker threads in Unity, and a shared
    /// mutable counter is the obvious way to get a data race for free.
    /// </para>
    /// </summary>
    public sealed class HoleStats
    {
        public int Bridged;
        public int Filled;

        public void Reset()
        {
            Bridged = 0;
            Filled = 0;
        }

        public override string ToString() => $"bridged {Bridged}, filled {Filled}";
    }

    public static class Triangulator
    {
        /// <summary>A projected point, paired with the 3D position it came from.</summary>
        private readonly struct FlatPoint
        {
            public readonly Vec2 Flat;
            public readonly Vec3 Source;

            public FlatPoint(Vec2 flat, Vec3 source)
            {
                Flat = flat;
                Source = source;
            }
        }

        public static double SignedArea(IReadOnlyList<Vec2> polygon)
        {
            double total = 0.0;
            int count = polygon.Count;
            for (int i = 0; i < count; i++)
            {
                Vec2 current = polygon[i];
                Vec2 following = polygon[(i + 1) % count];
                total += current.X * following.Y - following.X * current.Y;
            }
            return total * 0.5;
        }

        private static double Orient(Vec2 a, Vec2 b, Vec2 c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
        {
            double d1 = Orient(p, a, b);
            double d2 = Orient(p, b, c);
            double d3 = Orient(p, c, a);
            bool hasNegative = d1 < 0.0 || d2 < 0.0 || d3 < 0.0;
            bool hasPositive = d1 > 0.0 || d2 > 0.0 || d3 > 0.0;
            return !(hasNegative && hasPositive);
        }

        /// <summary>Ear-clip a simple 2D polygon. Returns index triples into the input.</summary>
        public static List<Tri> Triangulate2D(IReadOnlyList<Vec2> polygon)
        {
            var triangles = new List<Tri>();
            int n = polygon.Count;
            if (n < 3)
            {
                return triangles;
            }
            if (n == 3)
            {
                triangles.Add(new Tri(0, 1, 2));
                return triangles;
            }

            var indices = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                indices.Add(i);
            }
            if (SignedArea(polygon) < 0.0)
            {
                indices.Reverse();
            }

            int guard = 0;
            int guardLimit = n * n;
            while (indices.Count > 3 && guard < guardLimit)
            {
                guard++;
                bool clipped = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int count = indices.Count;
                    int previousIndex = indices[(i - 1 + count) % count];
                    int currentIndex = indices[i];
                    int nextIndex = indices[(i + 1) % count];

                    Vec2 a = polygon[previousIndex];
                    Vec2 b = polygon[currentIndex];
                    Vec2 c = polygon[nextIndex];

                    if (Orient(a, b, c) <= 0.0)
                    {
                        continue;   // reflex corner, not an ear
                    }

                    bool contaminated = false;
                    for (int k = 0; k < count; k++)
                    {
                        int j = indices[k];
                        if (j == previousIndex || j == currentIndex || j == nextIndex)
                        {
                            continue;
                        }
                        Vec2 candidate = polygon[j];
                        if (candidate.Equals(a) || candidate.Equals(b) || candidate.Equals(c))
                        {
                            continue;
                        }
                        if (PointInTriangle(candidate, a, b, c))
                        {
                            contaminated = true;
                            break;
                        }
                    }
                    if (contaminated)
                    {
                        continue;
                    }

                    triangles.Add(new Tri(previousIndex, currentIndex, nextIndex));
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    // No ear found: the ring is self-intersecting or degenerate.
                    // A fan is wrong in detail but bounded, and beats emitting nothing.
                    for (int i = 1; i < indices.Count - 1; i++)
                    {
                        triangles.Add(new Tri(indices[0], indices[i], indices[i + 1]));
                    }
                    return triangles;
                }
            }

            if (indices.Count == 3)
            {
                triangles.Add(new Tri(indices[0], indices[1], indices[2]));
            }
            return triangles;
        }

        private static bool Crosses(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            double d1 = Orient(a, b, c);
            double d2 = Orient(a, b, d);
            double d3 = Orient(c, d, a);
            double d4 = Orient(c, d, b);
            return (d1 > 0.0) != (d2 > 0.0) && (d3 > 0.0) != (d4 > 0.0);
        }

        private static bool PointInRing(Vec2 point, IReadOnlyList<FlatPoint> ring)
        {
            bool inside = false;
            int count = ring.Count;
            for (int i = 0; i < count; i++)
            {
                Vec2 a = ring[(i - 1 + count) % count].Flat;
                Vec2 b = ring[i].Flat;
                if ((a.Y > point.Y) != (b.Y > point.Y))
                {
                    double t = (point.Y - a.Y) / (b.Y - a.Y);
                    if (point.X < a.X + t * (b.X - a.X))
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        private static bool BridgeClear(Vec2 from, Vec2 to,
            IReadOnlyList<FlatPoint> ring, IReadOnlyList<FlatPoint> hole)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                IReadOnlyList<FlatPoint> loop = pass == 0 ? ring : hole;
                int count = loop.Count;
                for (int i = 0; i < count; i++)
                {
                    Vec2 c = loop[(i - 1 + count) % count].Flat;
                    Vec2 d = loop[i].Flat;
                    if (c.Equals(from) || c.Equals(to) || d.Equals(from) || d.Equals(to))
                    {
                        continue;
                    }
                    if (Crosses(from, to, c, d))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Join a hole into its outer ring with a pair of coincident bridge edges,
        /// producing a single self-touching contour the ear clipper can eat.
        /// Returns null when no bridge is possible.
        /// <para>
        /// The Python splices flat 2D lists and then recovers the 3D positions
        /// through a dictionary keyed on the projected point. That silently picks
        /// the wrong source vertex whenever two points project onto each other.
        /// Splicing the paired list directly removes the failure mode.
        /// </para>
        /// </summary>
        private static List<FlatPoint> SpliceHole(List<FlatPoint> ring, List<FlatPoint> hole)
        {
            var candidates = new List<(double Distance, int RingIndex, int HoleIndex)>(ring.Count * hole.Count);
            for (int i = 0; i < ring.Count; i++)
            {
                for (int j = 0; j < hole.Count; j++)
                {
                    double dx = ring[i].Flat.X - hole[j].Flat.X;
                    double dy = ring[i].Flat.Y - hole[j].Flat.Y;
                    candidates.Add((dx * dx + dy * dy, i, j));
                }
            }
            candidates.Sort((x, y) => x.Distance.CompareTo(y.Distance));

            foreach ((double _, int i, int j) in candidates)
            {
                Vec2 a = ring[i].Flat;
                Vec2 b = hole[j].Flat;
                if (a.Equals(b))
                {
                    continue;
                }
                if (!BridgeClear(a, b, ring, hole))
                {
                    continue;
                }

                var middle = new Vec2((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                if (!PointInRing(middle, ring) || PointInRing(middle, hole))
                {
                    continue;   // bridge leaves the ring, or re-enters the hole
                }

                var merged = new List<FlatPoint>(ring.Count + hole.Count + 2);
                for (int k = 0; k <= i; k++) merged.Add(ring[k]);
                for (int k = j; k < hole.Count; k++) merged.Add(hole[k]);
                for (int k = 0; k <= j; k++) merged.Add(hole[k]);
                for (int k = i; k < ring.Count; k++) merged.Add(ring[k]);
                return merged;
            }
            return null;
        }

        private static double SignedAreaOf(IReadOnlyList<FlatPoint> points)
        {
            double total = 0.0;
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                Vec2 current = points[i].Flat;
                Vec2 following = points[(i + 1) % count].Flat;
                total += current.X * following.Y - following.X * current.Y;
            }
            return total * 0.5;
        }

        private static List<FlatPoint> BridgeHoles(
            List<FlatPoint> outer, List<List<FlatPoint>> holes, HoleStats stats)
        {
            if (SignedAreaOf(outer) < 0.0)
            {
                outer = new List<FlatPoint>(outer);
                outer.Reverse();
            }

            var prepared = new List<List<FlatPoint>>();
            foreach (List<FlatPoint> hole in holes)
            {
                if (hole.Count < 3)
                {
                    continue;
                }
                List<FlatPoint> oriented = hole;
                if (SignedAreaOf(hole) > 0.0)
                {
                    oriented = new List<FlatPoint>(hole);
                    oriented.Reverse();
                }
                prepared.Add(oriented);
            }

            // Rightmost hole first: its bridge has the clearest run to the outer ring.
            double RightmostX(IReadOnlyList<FlatPoint> loop)
            {
                double rightmost = double.MinValue;
                foreach (FlatPoint p in loop)
                {
                    if (p.Flat.X > rightmost)
                    {
                        rightmost = p.Flat.X;
                    }
                }
                return rightmost;
            }
            prepared.Sort((x, y) => RightmostX(y).CompareTo(RightmostX(x)));

            List<FlatPoint> ring = outer;
            foreach (List<FlatPoint> hole in prepared)
            {
                List<FlatPoint> merged = SpliceHole(ring, hole);
                if (merged == null)
                {
                    if (stats != null)
                    {
                        stats.Filled++;
                    }
                    continue;
                }
                if (stats != null)
                {
                    stats.Bridged++;
                }
                ring = merged;
            }
            return ring;
        }

        private static double TriangulationArea(IReadOnlyList<Vec2> points, IReadOnlyList<Tri> triangles)
        {
            double total = 0.0;
            foreach (Tri t in triangles)
            {
                total += Math.Abs(Orient(points[t.A], points[t.B], points[t.C])) * 0.5;
            }
            return total;
        }

        private static List<Vec2> FlatsOf(IReadOnlyList<FlatPoint> points)
        {
            var flats = new List<Vec2>(points.Count);
            foreach (FlatPoint p in points)
            {
                flats.Add(p.Flat);
            }
            return flats;
        }

        /// <summary>
        /// Triangulate a planar polygon in 3D, bridging any inner bounds.
        /// Returns the ring the triangle indices address, which is not the input
        /// ring once holes have been spliced in.
        /// </summary>
        public static (List<Vec3> Ring, List<Tri> Triangles) Triangulate3D(
            IReadOnlyList<Vec3> points,
            IReadOnlyList<IReadOnlyList<Vec3>> holes = null,
            HoleStats stats = null)
        {
            var identity = new List<Vec3>(points);
            if (points.Count < 3)
            {
                return (identity, new List<Tri>());
            }

            // Newell's method: robust on the slivers that breps are full of, where
            // a single cross product of three near-collinear points is noise.
            if (!Vec3.TryNewellNormal(points, 1e-20, out Vec3 normal))
            {
                return (identity, new List<Tri>());
            }

            Vec3 helper = Math.Abs(normal.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
            Vec3 u = Vec3.Cross(helper, normal).Normalized();
            Vec3 v = Vec3.Cross(normal, u);

            List<FlatPoint> Flatten(IReadOnlyList<Vec3> ring)
            {
                var flat = new List<FlatPoint>(ring.Count);
                foreach (Vec3 p in ring)
                {
                    flat.Add(new FlatPoint(new Vec2(Vec3.Dot(p, u), Vec3.Dot(p, v)), p));
                }
                return flat;
            }

            List<FlatPoint> flatOuter = Flatten(points);

            if (holes == null || holes.Count == 0)
            {
                return (identity, Triangulate2D(FlatsOf(flatOuter)));
            }

            var flatHoles = new List<List<FlatPoint>>(holes.Count);
            foreach (IReadOnlyList<Vec3> hole in holes)
            {
                flatHoles.Add(Flatten(hole));
            }

            List<FlatPoint> merged = BridgeHoles(flatOuter, flatHoles, stats);
            List<Vec2> mergedFlat = FlatsOf(merged);
            List<Tri> triangles = Triangulate2D(mergedFlat);

            // Sanity check the bridge: the triangulated area should equal the outer
            // ring minus its holes. When it does not, the splice self-intersected
            // and produced garbage, so fall back to an unpunctured face — a filled
            // window is wrong, but a shredded one is worse.
            double outerArea = Math.Abs(SignedAreaOf(flatOuter));
            double expected = outerArea;
            foreach (List<FlatPoint> hole in flatHoles)
            {
                expected -= Math.Abs(SignedAreaOf(hole));
            }
            double covered = TriangulationArea(mergedFlat, triangles);

            if (outerArea > 0.0 && Math.Abs(covered - expected) > outerArea * 0.01)
            {
                if (stats != null)
                {
                    stats.Bridged -= holes.Count;
                    stats.Filled += holes.Count;
                }
                return (identity, Triangulate2D(FlatsOf(flatOuter)));
            }

            var ringOut = new List<Vec3>(merged.Count);
            foreach (FlatPoint p in merged)
            {
                ringOut.Add(p.Source);
            }
            return (ringOut, triangles);
        }
    }
}