// @author: Davy Bellens

using Conversion.Ifc;

namespace Conversion.Layers.Resource
{
    /// <summary>
    /// Geometric Constraint and Geometry schemas: points, directions, placements.
    /// </summary>
    public static class Placement
    {
        public static Vec3 ReadPoint(IfcEntity entity)
        {
            if (entity == null)
            {
                return Vec3.Zero;
            }
            double[] coordinates = entity.Doubles("Coordinates");
            return new Vec3(
                coordinates.Length > 0 ? coordinates[0] : 0.0,
                coordinates.Length > 1 ? coordinates[1] : 0.0,
                coordinates.Length > 2 ? coordinates[2] : 0.0);
        }

        public static Vec3 ReadDirection(IfcEntity entity, Vec3 fallback)
        {
            if (entity == null)
            {
                return fallback;
            }
            double[] ratios = entity.Doubles("DirectionRatios");
            if (ratios.Length == 0)
            {
                return fallback;
            }
            return new Vec3(
                ratios[0],
                ratios.Length > 1 ? ratios[1] : 0.0,
                ratios.Length > 2 ? ratios[2] : 0.0);
        }

        /// <summary>IfcAxis2Placement2D or 3D to a 4x4 transform.</summary>
        public static Matrix4 AxisPlacementMatrix(IfcEntity placement)
        {
            if (placement == null)
            {
                return Matrix4.Identity;
            }

            Vec3 origin = ReadPoint(placement.Entity("Location"));

            Vec3 xAxis, yAxis, zAxis;

            if (placement.IsA("IfcAxis2Placement2D"))
            {
                Vec3 reference = ReadDirection(placement.Entity("RefDirection"), Vec3.UnitX);
                if (!new Vec3(reference.X, reference.Y, 0.0).TryNormalize(out xAxis))
                {
                    xAxis = Vec3.UnitX;
                }
                yAxis = new Vec3(-xAxis.Y, xAxis.X, 0.0);
                zAxis = Vec3.UnitZ;
            }
            else
            {
                (xAxis, zAxis) = OrthonormalBasis(
                    ReadDirection(placement.Entity("Axis"), Vec3.UnitZ),
                    ReadDirection(placement.Entity("RefDirection"), Vec3.UnitX));
                yAxis = Vec3.Cross(zAxis, xAxis);
            }

            return Matrix4.FromBasis(xAxis, yAxis, zAxis, origin);
        }

        /// <summary>
        /// Build an orthonormal (xAxis, zAxis) pair from an approximate primary
        /// direction and a reference direction, Gram-Schmidt style. If the
        /// reference is parallel to the primary axis (or absent), falls back to
        /// any perpendicular rather than dividing by zero.
        /// </summary>
        private static (Vec3 XAxis, Vec3 ZAxis) OrthonormalBasis(Vec3 rawPrimary, Vec3 rawReference)
        {
            if (!rawPrimary.TryNormalize(out Vec3 zAxis))
            {
                zAxis = Vec3.UnitZ;
            }

            Vec3 projected = rawReference - zAxis * Vec3.Dot(rawReference, zAxis);
            if (projected.LengthSquared < 1e-20)
            {
                // The reference was parallel to the axis, or absent. Pick any
                // perpendicular rather than dividing by zero.
                Vec3 candidate = System.Math.Abs(zAxis.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
                projected = candidate - zAxis * Vec3.Dot(candidate, zAxis);
            }

            if (!projected.TryNormalize(out Vec3 xAxis))
            {
                xAxis = Vec3.UnitX;
            }
            return (xAxis, zAxis);
        }

        /// <summary>Guards against a cyclic PlacementRelTo, not a limit any real chain should reach.</summary>
        private const int MaxPlacementChainLength = 256;

        /// <summary>
        /// Walk an IfcLocalPlacement chain up to the world.
        /// <para>
        /// Iterative rather than recursive: a deeply nested spatial hierarchy is a
        /// chain, not a tree, and it costs nothing to avoid the stack.
        /// </para>
        /// </summary>
        public static Matrix4 LocalPlacementMatrix(IfcEntity placement)
        {
            if (placement == null || !placement.IsA("IfcLocalPlacement"))
            {
                return Matrix4.Identity;
            }

            var chain = new System.Collections.Generic.List<IfcEntity>();
            for (IfcEntity step = placement;
                 step != null && step.IsA("IfcLocalPlacement");
                 step = step.Entity("PlacementRelTo"))
            {
                chain.Add(step);
                if (chain.Count > MaxPlacementChainLength)
                {
                    break;   // cyclic PlacementRelTo; malformed, but do not hang
                }
            }

            Matrix4 result = Matrix4.Identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Matrix4 local = AxisPlacementMatrix(chain[i].Entity("RelativePlacement"));
                result = Matrix4.Multiply(result, local);
            }
            return result;
        }

        /// <summary>
        /// IfcCartesianTransformationOperator3D, as used by IfcMappedItem.MappingTarget.
        /// <para>
        /// Honours Scale2 and Scale3 when the operator is the nonUniform variant.
        /// Axis2 is re-derived rather than taken as authored: EXPRESS defines the
        /// operator's basis as an orthogonal right-handed set built from Axis1 and
        /// Axis3, so a supplied Axis2 is a hint to orthogonalise against, not a
        /// licence to shear.
        /// </para>
        /// </summary>
        public static Matrix4 TransformationOperatorMatrix(IfcEntity op)
        {
            if (op == null)
            {
                return Matrix4.Identity;
            }

            Vec3 origin = ReadPoint(op.Entity("LocalOrigin"));

            bool nonUniform = op.IsA("IfcCartesianTransformationOperator3DnonUniform");
            double scale1 = op["Scale"].IsNull ? 1.0 : op.Double("Scale", 1.0);
            double scale2 = nonUniform && !op["Scale2"].IsNull ? op.Double("Scale2", scale1) : scale1;
            double scale3 = nonUniform && !op["Scale3"].IsNull ? op.Double("Scale3", scale1) : scale1;

            (Vec3 xAxis, Vec3 zAxis) = OrthonormalBasis(
                ReadDirection(op.Entity("Axis3"), Vec3.UnitZ),
                ReadDirection(op.Entity("Axis1"), Vec3.UnitX));

            Vec3 yAxis = Vec3.Cross(zAxis, xAxis);

            // Axis2, when supplied, only decides which of the two perpendiculars
            // to prefer. Flip rather than replace, so the basis stays orthonormal.
            IfcEntity axis2 = op.Entity("Axis2");
            if (axis2 != null)
            {
                Vec3 hint = ReadDirection(axis2, yAxis);
                if (Vec3.Dot(hint, yAxis) < 0.0)
                {
                    yAxis = -yAxis;
                    zAxis = -zAxis;
                }
            }

            return Matrix4.FromBasis(xAxis * scale1, yAxis * scale2, zAxis * scale3, origin);
        }
    }
}