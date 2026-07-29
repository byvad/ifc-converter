using System;
using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Resource
{
    /// <summary>Geometric Model schema: solids to Mesh.</summary>
    public static class Solid
    {
        public static double RingSignedArea(IReadOnlyList<Vec2> ring) => Triangulator.SignedArea(ring);

        public static Mesh ExtrudedAreaSolid(IfcEntity solid, HoleStats stats = null, double angleScale = 0.0)
        {
            ProfileRings rings = Profile.Read(solid.Entity("SweptArea"), angleScale);
            if (!rings.IsUsable)
            {
                return new Mesh();
            }

            List<Vec2> ring = rings.Outer;
            List<List<Vec2>> holes = rings.Holes;

            // Normalise winding once, up front. The cap triangulation and the
            // side-quad loop both have to agree on which way is out; an
            // authored-clockwise profile otherwise makes them face opposite ways.
            if (RingSignedArea(ring) < 0.0)
            {
                ring = Reversed(ring);
            }
            for (int i = 0; i < holes.Count; i++)
            {
                if (RingSignedArea(holes[i]) > 0.0)
                {
                    holes[i] = Reversed(holes[i]);
                }
            }

            double depth = solid.Double("Depth");
            Vec3 direction = Placement.ReadDirection(solid.Entity("ExtrudedDirection"), Vec3.UnitZ);
            Vec3 offset = direction * depth;

            // The profile lies in the local XY plane with its normal on +Z. A sweep
            // travelling along -Z builds the solid upside down and turns every face
            // normal inward, so flip the profile to compensate. Invisible in a
            // double-sided viewer; fatal to a boolean and to backface culling.
            if (offset.Z < 0.0)
            {
                ring = Reversed(ring);
                for (int i = 0; i < holes.Count; i++)
                {
                    holes[i] = Reversed(holes[i]);
                }
            }

            var mesh = new Mesh();
            List<Vec3> bottom;
            List<Tri> caps;

            if (holes.Count > 0)
            {
                // Bridge the holes into the outer ring so one triangulated cap can
                // be swept. Triangulate3D hands back the ring its indices address.
                var outer3d = new List<Vec3>(ring.Count);
                foreach (Vec2 p in ring)
                {
                    outer3d.Add(new Vec3(p.X, p.Y, 0.0));
                }
                var holes3d = new List<IReadOnlyList<Vec3>>(holes.Count);
                foreach (List<Vec2> hole in holes)
                {
                    var converted = new List<Vec3>(hole.Count);
                    foreach (Vec2 p in hole)
                    {
                        converted.Add(new Vec3(p.X, p.Y, 0.0));
                    }
                    holes3d.Add(converted);
                }
                (bottom, caps) = Triangulator.Triangulate3D(outer3d, holes3d, stats);
            }
            else
            {
                bottom = new List<Vec3>(ring.Count);
                foreach (Vec2 p in ring)
                {
                    bottom.Add(new Vec3(p.X, p.Y, 0.0));
                }
                caps = Triangulator.Triangulate2D(ring);
            }

            var top = new List<Vec3>(bottom.Count);
            foreach (Vec3 p in bottom)
            {
                top.Add(p + offset);
            }

            int bottomBase = mesh.AddPolygonRing(bottom);
            int topBase = mesh.AddPolygonRing(top);

            foreach (Tri t in caps)
            {
                mesh.AddTriangle(bottomBase + t.C, bottomBase + t.B, bottomBase + t.A);
                mesh.AddTriangle(topBase + t.A, topBase + t.B, topBase + t.C);
            }

            int count = bottom.Count;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                mesh.AddTriangle(bottomBase + i, bottomBase + j, topBase + j);
                mesh.AddTriangle(bottomBase + i, topBase + j, topBase + i);
            }

            return mesh.Transformed(Placement.AxisPlacementMatrix(solid.Entity("Position")));
        }

        private static List<Vec2> Reversed(List<Vec2> ring)
        {
            var copy = new List<Vec2>(ring);
            copy.Reverse();
            return copy;
        }

        public static Mesh FacetedBrep(IfcEntity brep, HoleStats stats = null)
        {
            var mesh = new Mesh();
            AddShell(mesh, brep.Entity("Outer"), stats);
            return mesh;
        }

        public static void AddShell(Mesh mesh, IfcEntity shell, HoleStats stats)
        {
            if (shell == null)
            {
                return;
            }

            foreach (IfcEntity face in shell.Entities("CfsFaces"))
            {
                List<Vec3> outer = null;
                var holes = new List<IReadOnlyList<Vec3>>();

                foreach (IfcEntity bound in face.Entities("Bounds"))
                {
                    IfcEntity loop = bound.Entity("Bound");
                    if (loop == null || !loop.IsA("IfcPolyLoop"))
                    {
                        continue;
                    }

                    var points = new List<Vec3>();
                    foreach (IfcEntity point in loop.Entities("Polygon"))
                    {
                        points.Add(Placement.ReadPoint(point));
                    }
                    if (points.Count < 3)
                    {
                        continue;
                    }

                    // Orientation defaults to TRUE; only an explicit FALSE reverses.
                    if (bound.Logical("Orientation") == false)
                    {
                        points.Reverse();
                    }

                    if (bound.IsA("IfcFaceOuterBound"))
                    {
                        outer = points;
                    }
                    else
                    {
                        holes.Add(points);
                    }
                }

                if (outer == null)
                {
                    if (holes.Count == 0)
                    {
                        continue;
                    }
                    // No bound was flagged as outer. The largest one is the only
                    // sensible candidate.
                    int largest = 0;
                    double largestExtent = -1.0;
                    for (int i = 0; i < holes.Count; i++)
                    {
                        double extent = RingExtent(holes[i]);
                        if (extent > largestExtent)
                        {
                            largestExtent = extent;
                            largest = i;
                        }
                    }
                    outer = new List<Vec3>(holes[largest]);
                    holes.RemoveAt(largest);
                }

                (List<Vec3> ring, List<Tri> triangles) =
                    Triangulator.Triangulate3D(outer, holes.Count > 0 ? holes : null, stats);

                int start = mesh.AddPolygonRing(ring);
                foreach (Tri t in triangles)
                {
                    mesh.AddTriangle(start + t.A, start + t.B, start + t.C);
                }
            }
        }

        private static double RingExtent(IReadOnlyList<Vec3> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (Vec3 p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            return dx * dx + dy * dy + dz * dz;
        }

        public static Mesh TriangulatedFaceSet(IfcEntity faceSet)
        {
            var mesh = new Mesh();
            IfcEntity coordinates = faceSet.Entity("Coordinates");
            foreach (IfcValue point in coordinates["CoordList"].AsList())
            {
                double[] c = point.AsDoubles();
                mesh.Vertices.Add(new Vec3(
                    c.Length > 0 ? c[0] : 0.0,
                    c.Length > 1 ? c[1] : 0.0,
                    c.Length > 2 ? c[2] : 0.0));
            }

            foreach (IfcValue triangle in faceSet["CoordIndex"].AsList())
            {
                IReadOnlyList<IfcValue> indices = triangle.Unwrapped.AsList();
                if (indices.Count < 3)
                {
                    continue;
                }
                mesh.AddTriangle(indices[0].AsInt() - 1, indices[1].AsInt() - 1, indices[2].AsInt() - 1);
            }
            return mesh;
        }

        public static Mesh PolygonalFaceSet(IfcEntity faceSet, HoleStats stats = null)
        {
            var mesh = new Mesh();
            var coordinates = new List<Vec3>();
            IfcEntity list = faceSet.Entity("Coordinates");
            foreach (IfcValue point in list["CoordList"].AsList())
            {
                double[] c = point.AsDoubles();
                coordinates.Add(new Vec3(
                    c.Length > 0 ? c[0] : 0.0,
                    c.Length > 1 ? c[1] : 0.0,
                    c.Length > 2 ? c[2] : 0.0));
            }
            mesh.Vertices.AddRange(coordinates);

            foreach (IfcEntity face in faceSet.Entities("Faces"))
            {
                IReadOnlyList<IfcValue> outerIndices = face["CoordIndex"].AsList();
                var outer = new List<Vec3>(outerIndices.Count);
                var outerSlots = new List<int>(outerIndices.Count);
                foreach (IfcValue index in outerIndices)
                {
                    int slot = index.AsInt() - 1;
                    if (slot < 0 || slot >= coordinates.Count)
                    {
                        continue;
                    }
                    outerSlots.Add(slot);
                    outer.Add(coordinates[slot]);
                }
                if (outer.Count < 3)
                {
                    continue;
                }

                var holes = new List<IReadOnlyList<Vec3>>();
                foreach (IfcValue innerLoop in face["InnerCoordIndices"].AsList())
                {
                    var hole = new List<Vec3>();
                    foreach (IfcValue index in innerLoop.Unwrapped.AsList())
                    {
                        int slot = index.AsInt() - 1;
                        if (slot >= 0 && slot < coordinates.Count)
                        {
                            hole.Add(coordinates[slot]);
                        }
                    }
                    if (hole.Count >= 3)
                    {
                        holes.Add(hole);
                    }
                }

                if (holes.Count == 0)
                {
                    // Reuse the shared vertices rather than duplicating the ring.
                    (_, List<Tri> triangles) = Triangulator.Triangulate3D(outer, null, stats);
                    foreach (Tri t in triangles)
                    {
                        mesh.AddTriangle(outerSlots[t.A], outerSlots[t.B], outerSlots[t.C]);
                    }
                    continue;
                }

                (List<Vec3> ring, List<Tri> bridged) = Triangulator.Triangulate3D(outer, holes, stats);
                int start = mesh.AddPolygonRing(ring);
                foreach (Tri t in bridged)
                {
                    mesh.AddTriangle(start + t.A, start + t.B, start + t.C);
                }
            }
            return mesh;
        }

        public static Mesh SurfaceModel(IfcEntity model, HoleStats stats = null)
        {
            var mesh = new Mesh();
            List<IfcEntity> shells = model.Entities("FbsmFaces");
            if (shells.Count == 0)
            {
                shells = model.Entities("SbsmBoundary");
            }
            foreach (IfcEntity shell in shells)
            {
                if (shell.Has("CfsFaces"))
                {
                    AddShell(mesh, shell, stats);
                }
            }
            return mesh;
        }

        /// <summary>
        /// A half space, realised as a box big enough to swallow whatever it is
        /// cutting. IfcHalfSpaceSolid is infinite by definition; a boolean needs
        /// something bounded, so the caller supplies the region of interest.
        /// <para>
        /// AgreementFlag TRUE means the base surface normal points away from the
        /// material, so the solid lies below the plane in its own frame.
        /// </para>
        /// </summary>
        public static Mesh HalfSpaceSolid(IfcEntity halfSpace, Vec3 targetMin, Vec3 targetMax,
            HoleStats stats = null, double angleScale = 0.0)
        {
            IfcEntity surface = halfSpace.Entity("BaseSurface");
            if (surface == null || !surface.IsA("IfcPlane"))
            {
                throw new UnsupportedGeometryException(
                    $"Half space over {(surface == null ? "nothing" : surface.IsA())} not supported");
            }

            Matrix4 frame = Placement.AxisPlacementMatrix(surface.Entity("Position"));
            bool agreement = halfSpace.Logical("AgreementFlag") != false;

            var centre = new Vec3(frame.M03, frame.M13, frame.M23);
            double reach = 1.0;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vec3(
                    (corner & 1) == 0 ? targetMin.X : targetMax.X,
                    (corner & 2) == 0 ? targetMin.Y : targetMax.Y,
                    (corner & 4) == 0 ? targetMin.Z : targetMax.Z);
                reach = Math.Max(reach, (point - centre).Length);
            }
            reach *= 2.0;

            Mesh box;
            if (halfSpace.IsA("IfcPolygonalBoundedHalfSpace"))
            {
                // Bounded by a prism over PolygonalBoundary, in the half space's own
                // frame, intersected with the plane's half space.
                Matrix4 boundaryFrame = Placement.AxisPlacementMatrix(halfSpace.Entity("Position"));
                List<Vec2> boundary = Profile.ReadCurve(halfSpace.Entity("PolygonalBoundary"), angleScale);
                if (boundary.Count < 3)
                {
                    throw new UnsupportedGeometryException("IfcPolygonalBoundedHalfSpace (empty boundary)");
                }
                if (Triangulator.SignedArea(boundary) < 0.0)
                {
                    boundary.Reverse();
                }
                Mesh prism = Prism(boundary, -reach, reach);
                prism = prism.Transformed(boundaryFrame);

                Mesh slab = LocalBox(frame, reach, agreement);
                box = MeshBoolean.Intersect(prism, slab);
            }
            else
            {
                box = LocalBox(frame, reach, agreement);
            }

            return box;
        }

        private static Mesh LocalBox(in Matrix4 frame, double reach, bool below)
        {
            double low = below ? -2.0 * reach : 0.0;
            double high = below ? 0.0 : 2.0 * reach;
            var square = new List<Vec2>
            {
                new Vec2(-reach, -reach), new Vec2(reach, -reach),
                new Vec2(reach, reach), new Vec2(-reach, reach),
            };
            return Prism(square, low, high).Transformed(frame);
        }

        /// <summary>A closed prism over a counter-clockwise 2D ring, outward facing.</summary>
        private static Mesh Prism(List<Vec2> ring, double low, double high)
        {
            var mesh = new Mesh();
            var bottom = new List<Vec3>(ring.Count);
            var top = new List<Vec3>(ring.Count);
            foreach (Vec2 p in ring)
            {
                bottom.Add(new Vec3(p.X, p.Y, low));
                top.Add(new Vec3(p.X, p.Y, high));
            }

            int bottomBase = mesh.AddPolygonRing(bottom);
            int topBase = mesh.AddPolygonRing(top);
            foreach (Tri t in Triangulator.Triangulate2D(ring))
            {
                mesh.AddTriangle(bottomBase + t.C, bottomBase + t.B, bottomBase + t.A);
                mesh.AddTriangle(topBase + t.A, topBase + t.B, topBase + t.C);
            }
            for (int i = 0; i < ring.Count; i++)
            {
                int j = (i + 1) % ring.Count;
                mesh.AddTriangle(bottomBase + i, bottomBase + j, topBase + j);
                mesh.AddTriangle(bottomBase + i, topBase + j, topBase + i);
            }
            return mesh;
        }
    }
}
