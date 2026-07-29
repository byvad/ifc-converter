using System;

namespace Conversion.Layers.Resource
{
    /// <summary>A point in the profile plane. Profiles are 2D by definition in IFC.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 v, double k) => new Vec2(v.X * k, v.Y * k);

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>
    /// A point or direction in model space.
    /// <para>
    /// Double precision throughout, deliberately. IFC site coordinates routinely run
    /// into the tens of thousands of millimetres, and a placement chain multiplies
    /// four or five matrices before a vertex lands. In float32 that accumulates into
    /// visible jitter and z-fighting. Everything stays double until the very last
    /// step, where the mesh is re-based to a local origin and cast down for Unity.
    /// </para>
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0.0, 0.0, 0.0);
        public static readonly Vec3 UnitX = new Vec3(1.0, 0.0, 0.0);
        public static readonly Vec3 UnitY = new Vec3(0.0, 1.0, 0.0);
        public static readonly Vec3 UnitZ = new Vec3(0.0, 0.0, 1.0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 v, double k) => new Vec3(v.X * k, v.Y * k, v.Z * k);
        public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);

        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public double LengthSquared => X * X + Y * Y + Z * Z;
        public double Length => Math.Sqrt(LengthSquared);

        /// <summary>Normalise, or report failure. Preferred over <see cref="Normalized"/> on
        /// anything read out of a file, where a zero-length direction is a real possibility.</summary>
        public bool TryNormalize(out Vec3 result)
        {
            double length = Length;
            if (length < 1e-12)
            {
                result = Zero;
                return false;
            }
            result = new Vec3(X / length, Y / length, Z / length);
            return true;
        }

        public Vec3 Normalized()
        {
            if (!TryNormalize(out Vec3 result))
            {
                throw new InvalidOperationException("Cannot normalize a zero-length vector.");
            }
            return result;
        }

        public Vec2 XY => new Vec2(X, Y);

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = hash * 397 ^ Y.GetHashCode();
                hash = hash * 397 ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
