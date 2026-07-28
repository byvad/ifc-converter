using System;

namespace Conversion.Layers.Resource
{
    /// <summary>
    /// A row-major 4x4 transform, matching the nested-tuple layout the Python
    /// resource layer uses so placement code ports across line for line.
    /// <para>
    /// Passed by <c>in</c> everywhere it matters: sixteen doubles is 128 bytes,
    /// well past the point where copying a struct per call is free.
    /// </para>
    /// </summary>
    public readonly struct Matrix4
    {
        public readonly double M00, M01, M02, M03;
        public readonly double M10, M11, M12, M13;
        public readonly double M20, M21, M22, M23;
        public readonly double M30, M31, M32, M33;

        public Matrix4(
            double m00, double m01, double m02, double m03,
            double m10, double m11, double m12, double m13,
            double m20, double m21, double m22, double m23,
            double m30, double m31, double m32, double m33)
        {
            M00 = m00; M01 = m01; M02 = m02; M03 = m03;
            M10 = m10; M11 = m11; M12 = m12; M13 = m13;
            M20 = m20; M21 = m21; M22 = m22; M23 = m23;
            M30 = m30; M31 = m31; M32 = m32; M33 = m33;
        }

        public static readonly Matrix4 Identity = new Matrix4(
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0);

        /// <summary>
        /// Build a transform from three basis axes and an origin. The axes become
        /// the matrix columns, which is the layout every IfcAxis2Placement resolves to.
        /// </summary>
        public static Matrix4 FromBasis(Vec3 xAxis, Vec3 yAxis, Vec3 zAxis, Vec3 origin)
        {
            return new Matrix4(
                xAxis.X, yAxis.X, zAxis.X, origin.X,
                xAxis.Y, yAxis.Y, zAxis.Y, origin.Y,
                xAxis.Z, yAxis.Z, zAxis.Z, origin.Z,
                0.0, 0.0, 0.0, 1.0);
        }

        public static Matrix4 Multiply(in Matrix4 a, in Matrix4 b)
        {
            return new Matrix4(
                a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20 + a.M03 * b.M30,
                a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21 + a.M03 * b.M31,
                a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22 + a.M03 * b.M32,
                a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03 * b.M33,

                a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20 + a.M13 * b.M30,
                a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
                a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
                a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

                a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20 + a.M23 * b.M30,
                a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
                a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
                a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

                a.M30 * b.M00 + a.M31 * b.M10 + a.M32 * b.M20 + a.M33 * b.M30,
                a.M30 * b.M01 + a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
                a.M30 * b.M02 + a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
                a.M30 * b.M03 + a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33);
        }

        public Vec3 TransformPoint(Vec3 p) => new Vec3(
            M00 * p.X + M01 * p.Y + M02 * p.Z + M03,
            M10 * p.X + M11 * p.Y + M12 * p.Z + M13,
            M20 * p.X + M21 * p.Y + M22 * p.Z + M23);

        /// <summary>Rotate and scale without translating. For directions, not positions.</summary>
        public Vec3 TransformDirection(Vec3 d) => new Vec3(
            M00 * d.X + M01 * d.Y + M02 * d.Z,
            M10 * d.X + M11 * d.Y + M12 * d.Z,
            M20 * d.X + M21 * d.Y + M22 * d.Z);

        /// <summary>
        /// Determinant of the rotation/scale block. A negative value means the
        /// transform mirrors, which silently reverses triangle winding — worth
        /// asserting on before a mesh reaches a backface-culling renderer.
        /// </summary>
        public double Determinant3x3 =>
            M00 * (M11 * M22 - M12 * M21)
          - M01 * (M10 * M22 - M12 * M20)
          + M02 * (M10 * M21 - M11 * M20);

        public bool IsMirroring => Determinant3x3 < 0.0;

        public override string ToString() =>
            $"[{M00:0.###} {M01:0.###} {M02:0.###} {M03:0.###}; " +
            $"{M10:0.###} {M11:0.###} {M12:0.###} {M13:0.###}; " +
            $"{M20:0.###} {M21:0.###} {M22:0.###} {M23:0.###}]";
    }
}
