using System;
using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Resource
{
    /// <summary>Raised when a Resource-layer item has no builder.</summary>
    public sealed class UnsupportedGeometryException : Exception
    {
        public UnsupportedGeometryException(string message) : base(message)
        {
        }
    }

    /// <summary>Orchestrator for Resource-layer solid generation.</summary>
    public sealed class Builder
    {
        private readonly HoleStats _stats;
        private readonly Appearance _appearance;

        /// <summary>Guards against a representation map that maps itself.</summary>
        private const int MaxDepth = 16;

        public Builder(HoleStats stats = null, Appearance appearance = null)
        {
            _stats = stats ?? new HoleStats();
            _appearance = appearance ?? new Appearance();
        }

        public HoleStats Stats => _stats;

        /// <summary>
        /// Radians per unit of the project's plane-angle measure, or 0 to guess.
        /// Set this from Units.PlaneAngleScale before converting: IFC2X3 files are
        /// commonly authored in degrees, and an arc trim parameter cannot be read
        /// correctly without knowing which.
        /// </summary>
        public double PlaneAngleScale { get; set; }

        public Mesh BuildItem(IfcEntity item, bool styles) => BuildItem(item, styles, 0);

        private Mesh BuildItem(IfcEntity item, bool styles, int depth)
        {
            Mesh mesh = BuildGeometry(item, styles, depth);
            if (styles)
            {
                mesh.FillStyle(_appearance.ItemRgba(item));
            }
            return mesh;
        }

        private Mesh BuildGeometry(IfcEntity item, bool styles, int depth)
        {
            if (item == null)
            {
                throw new UnsupportedGeometryException("(null item)");
            }
            if (depth > MaxDepth)
            {
                throw new UnsupportedGeometryException($"{item.IsA()} (nested too deeply)");
            }

            string name = item.IsA();

            switch (name)
            {
                case "IfcExtrudedAreaSolid":
                    return Solid.ExtrudedAreaSolid(item, _stats, PlaneAngleScale);
                case "IfcFacetedBrep":
                    return Solid.FacetedBrep(item, _stats);
                case "IfcTriangulatedFaceSet":
                    return Solid.TriangulatedFaceSet(item);
                case "IfcPolygonalFaceSet":
                    return Solid.PolygonalFaceSet(item, _stats);
                case "IfcFaceBasedSurfaceModel":
                case "IfcShellBasedSurfaceModel":
                    return Solid.SurfaceModel(item, _stats);
            }

            if (item.IsA("IfcBooleanResult"))
            {
                return BooleanResult(item, styles, depth);
            }

            if (name == "IfcMappedItem")
            {
                return MappedItem(item, styles, depth);
            }

            throw new UnsupportedGeometryException(name);
        }

        /// <summary>
        /// IfcBooleanResult and its clipping subtype.
        /// <para>
        /// The old behaviour of returning FirstOperand untouched is kept only as a
        /// fallback: a clipped solid drawn uncut is wrong, but it is a great deal
        /// less wrong than nothing at all.
        /// </para>
        /// </summary>
        private Mesh BooleanResult(IfcEntity item, bool styles, int depth)
        {
            Mesh first = BuildItem(item.Entity("FirstOperand"), styles, depth + 1);
            if (first.IsEmpty)
            {
                return first;
            }

            IfcEntity secondEntity = item.Entity("SecondOperand");
            if (secondEntity == null)
            {
                return first;
            }

            Mesh second;
            try
            {
                if (secondEntity.IsA("IfcHalfSpaceSolid"))
                {
                    Bounds(first, out Vec3 min, out Vec3 max);
                    second = Solid.HalfSpaceSolid(secondEntity, min, max, _stats, PlaneAngleScale);
                }
                else
                {
                    second = BuildItem(secondEntity, false, depth + 1);
                }
            }
            catch (UnsupportedGeometryException)
            {
                return first;
            }

            if (second.IsEmpty)
            {
                return first;
            }

            string op = item.String("Operator") ?? "DIFFERENCE";
            var cutters = new[] { second };

            switch (op)
            {
                case "UNION":
                    return MeshBoolean.Union(first, second);
                case "INTERSECTION":
                    return MeshBoolean.Intersect(first, second);
                default:
                    return MeshBoolean.Subtract(first, cutters);
            }
        }

        private Mesh MappedItem(IfcEntity item, bool styles, int depth)
        {
            IfcEntity source = item.Entity("MappingSource");
            if (source == null)
            {
                throw new UnsupportedGeometryException("IfcMappedItem (no MappingSource)");
            }

            Matrix4 origin = Placement.AxisPlacementMatrix(source.Entity("MappingOrigin"));
            Matrix4 target = Placement.TransformationOperatorMatrix(item.Entity("MappingTarget"));

            var mesh = new Mesh();
            IfcEntity representation = source.Entity("MappedRepresentation");
            if (representation != null)
            {
                foreach (IfcEntity sub in representation.Entities("Items"))
                {
                    try
                    {
                        mesh.Extend(BuildItem(sub, styles, depth + 1));
                    }
                    catch (UnsupportedGeometryException)
                    {
                        // One unsupported item in a mapped block should not lose the rest.
                    }
                }
            }

            return mesh.Transformed(Matrix4.Multiply(target, origin));
        }

        internal static void Bounds(Mesh mesh, out Vec3 min, out Vec3 max)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (Vec3 v in mesh.Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z > maxZ) maxZ = v.Z;
            }
            if (minX > maxX)
            {
                min = Vec3.Zero;
                max = Vec3.Zero;
                return;
            }
            min = new Vec3(minX, minY, minZ);
            max = new Vec3(maxX, maxY, maxZ);
        }
    }
}
