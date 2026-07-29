using System;
using System.Collections.Generic;
using Conversion.Ifc;

namespace Conversion.Layers.Resource
{
    /// <summary>A profile: one outer ring, plus any inner rings that punch through it.</summary>
    public sealed class ProfileRings
    {
        public readonly List<Vec2> Outer;
        public readonly List<List<Vec2>> Holes;

        public ProfileRings(List<Vec2> outer, List<List<Vec2>> holes)
        {
            Outer = outer;
            Holes = holes ?? new List<List<Vec2>>();
        }

        public bool IsUsable => Outer != null && Outer.Count >= 3;

        public static ProfileRings Empty => new ProfileRings(new List<Vec2>(), null);
    }

    /// <summary>Profile schema: 2D cross-sections, returned as rings of (x, y).</summary>
    public static class Profile
    {
        /// <summary>Segments used for a full circle. Arcs get a proportional share.</summary>
        public const int CircleSegments = 24;

        public static ProfileRings Read(IfcEntity profile, double angleScale = 0.0)
        {
            if (profile == null)
            {
                throw new UnsupportedGeometryException("(null profile)");
            }

            string name = profile.IsA();
            List<Vec2> points;
            var holes = new List<List<Vec2>>();

            switch (name)
            {
                case "IfcRectangleProfileDef":
                case "IfcRoundedRectangleProfileDef":
                case "IfcRectangleHollowProfileDef":
                {
                    double halfX = profile.Double("XDim") * 0.5;
                    double halfY = profile.Double("YDim") * 0.5;
                    points = new List<Vec2>
                    {
                        new Vec2(-halfX, -halfY), new Vec2(halfX, -halfY),
                        new Vec2(halfX, halfY), new Vec2(-halfX, halfY),
                    };
                    if (name == "IfcRectangleHollowProfileDef")
                    {
                        double thickness = profile.Double("WallThickness");
                        if (thickness > 0.0 && halfX > thickness && halfY > thickness)
                        {
                            double innerX = halfX - thickness;
                            double innerY = halfY - thickness;
                            holes.Add(new List<Vec2>
                            {
                                new Vec2(-innerX, -innerY), new Vec2(innerX, -innerY),
                                new Vec2(innerX, innerY), new Vec2(-innerX, innerY),
                            });
                        }
                    }
                    break;
                }

                case "IfcCircleProfileDef":
                case "IfcCircleHollowProfileDef":
                {
                    double radius = profile.Double("Radius");
                    points = CirclePoints(radius, CircleSegments);
                    if (name == "IfcCircleHollowProfileDef")
                    {
                        double thickness = profile.Double("WallThickness");
                        double inner = radius - thickness;
                        if (thickness > 0.0 && inner > 0.0)
                        {
                            holes.Add(CirclePoints(inner, CircleSegments));
                        }
                    }
                    break;
                }

                case "IfcEllipseProfileDef":
                {
                    double a = profile.Double("SemiAxis1");
                    double b = profile.Double("SemiAxis2");
                    points = new List<Vec2>(CircleSegments);
                    for (int i = 0; i < CircleSegments; i++)
                    {
                        double angle = 2.0 * Math.PI * i / CircleSegments;
                        points.Add(new Vec2(a * Math.Cos(angle), b * Math.Sin(angle)));
                    }
                    break;
                }

                case "IfcArbitraryClosedProfileDef":
                    points = ReadCurve(profile.Entity("OuterCurve"), angleScale);
                    break;

                case "IfcArbitraryProfileDefWithVoids":
                {
                    points = ReadCurve(profile.Entity("OuterCurve"), angleScale);
                    foreach (IfcEntity inner in profile.Entities("InnerCurves"))
                    {
                        List<Vec2> hole = ReadCurve(inner, angleScale);
                        if (hole.Count >= 3)
                        {
                            holes.Add(hole);
                        }
                    }
                    break;
                }

                case "IfcDerivedProfileDef":
                {
                    ProfileRings parent = Read(profile.Entity("ParentProfile"), angleScale);
                    Matrix4 operatorMatrix = OperatorMatrix2D(profile.Entity("Operator"));
                    points = Apply(operatorMatrix, parent.Outer);
                    foreach (List<Vec2> hole in parent.Holes)
                    {
                        holes.Add(Apply(operatorMatrix, hole));
                    }
                    // The Operator carries the whole transform for a derived profile.
                    // There is no separate Position to compose on top of it.
                    return new ProfileRings(points, holes);
                }

                case "IfcCompositeProfileDef":
                {
                    // Several disjoint rings. The descent has one outer ring to give,
                    // so take the largest and let the rest go; a composite profile in
                    // a swept solid is rare enough to be worth noticing rather than
                    // silently approximating.
                    List<IfcEntity> parts = profile.Entities("Profiles");
                    if (parts.Count == 0)
                    {
                        throw new UnsupportedGeometryException("IfcCompositeProfileDef (empty)");
                    }
                    ProfileRings best = null;
                    double bestArea = -1.0;
                    foreach (IfcEntity part in parts)
                    {
                        ProfileRings candidate = Read(part, angleScale);
                        double area = Math.Abs(Triangulator.SignedArea(candidate.Outer));
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = candidate;
                        }
                    }
                    return best;
                }

                default:
                    throw new UnsupportedGeometryException($"Profile type not supported: {name}");
            }

            IfcEntity position = profile.Entity("Position");
            if (position != null)
            {
                Matrix4 matrix = Placement.AxisPlacementMatrix(position);
                points = Apply(matrix, points);
                for (int i = 0; i < holes.Count; i++)
                {
                    holes[i] = Apply(matrix, holes[i]);
                }
            }

            return new ProfileRings(points, holes);
        }

        private static List<Vec2> Apply(in Matrix4 matrix, List<Vec2> points)
        {
            var result = new List<Vec2>(points.Count);
            foreach (Vec2 p in points)
            {
                Vec3 moved = matrix.TransformPoint(new Vec3(p.X, p.Y, 0.0));
                result.Add(new Vec2(moved.X, moved.Y));
            }
            return result;
        }

        private static List<Vec2> CirclePoints(double radius, int segments)
        {
            var points = new List<Vec2>(segments);
            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;
                points.Add(new Vec2(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }
            return points;
        }

        /// <summary>
        /// IfcCartesianTransformationOperator2D(nonUniform).
        /// <para>
        /// This is a transformation operator — Axis1/Axis2/LocalOrigin/Scale — not an
        /// axis placement. The two look alike but an operator has no Location, and
        /// handing one to the axis-placement reader is how derived profiles end up
        /// silently unsupported.
        /// </para>
        /// </summary>
        private static Matrix4 OperatorMatrix2D(IfcEntity op)
        {
            if (op == null)
            {
                return Matrix4.Identity;
            }

            Vec3 origin = Placement.ReadPoint(op.Entity("LocalOrigin"));
            bool nonUniform = op.IsA("IfcCartesianTransformationOperator2DnonUniform");
            double scale1 = op["Scale"].IsNull ? 1.0 : op.Double("Scale", 1.0);
            double scale2 = nonUniform && !op["Scale2"].IsNull ? op.Double("Scale2", scale1) : scale1;

            Vec3 axis1 = Placement.ReadDirection(op.Entity("Axis1"), Vec3.UnitX);
            if (!new Vec3(axis1.X, axis1.Y, 0.0).TryNormalize(out Vec3 xAxis))
            {
                xAxis = Vec3.UnitX;
            }

            Vec3 yAxis = new Vec3(-xAxis.Y, xAxis.X, 0.0);
            IfcEntity axis2 = op.Entity("Axis2");
            if (axis2 != null)
            {
                Vec3 hint = Placement.ReadDirection(axis2, yAxis);
                if (hint.X * yAxis.X + hint.Y * yAxis.Y < 0.0)
                {
                    yAxis = -yAxis;   // mirrored profile, e.g. a left-hand jamb
                }
            }

            return Matrix4.FromBasis(xAxis * scale1, yAxis * scale2, Vec3.UnitZ, origin);
        }

        // ---------------------------------------------------------------- curves

        public static List<Vec2> ReadCurve(IfcEntity curve, double angleScale = 0.0)
        {
            if (curve == null)
            {
                throw new UnsupportedGeometryException("(null curve)");
            }

            if (curve.IsA("IfcPolyline"))
            {
                var points = new List<Vec2>();
                foreach (IfcEntity point in curve.Entities("Points"))
                {
                    double[] c = point.Doubles("Coordinates");
                    points.Add(new Vec2(c.Length > 0 ? c[0] : 0.0, c.Length > 1 ? c[1] : 0.0));
                }
                if (points.Count > 1 && points[0].Equals(points[points.Count - 1]))
                {
                    points.RemoveAt(points.Count - 1);
                }
                return points;
            }

            if (curve.IsA("IfcIndexedPolyCurve"))
            {
                return ReadIndexedPolyCurve(curve);
            }

            if (curve.IsA("IfcCircle"))
            {
                return CirclePoints(curve.Double("Radius"), CircleSegments);
            }

            if (curve.IsA("IfcTrimmedCurve"))
            {
                return ReadTrimmedCurve(curve, angleScale);
            }

            if (curve.IsA("IfcCompositeCurve"))
            {
                var points = new List<Vec2>();
                foreach (IfcEntity segment in curve.Entities("Segments"))
                {
                    List<Vec2> piece = ReadCurve(segment.Entity("ParentCurve"), angleScale);

                    // SameSense is authoritative: a composite curve is free to
                    // traverse a segment backwards, and ignoring it produces a
                    // ring that doubles back on itself.
                    if (segment.Logical("SameSense") == false)
                    {
                        piece.Reverse();
                    }
                    foreach (Vec2 p in piece)
                    {
                        if (points.Count == 0 || !points[points.Count - 1].Equals(p))
                        {
                            points.Add(p);
                        }
                    }
                }
                if (points.Count > 1 && points[0].Equals(points[points.Count - 1]))
                {
                    points.RemoveAt(points.Count - 1);
                }
                return points;
            }

            throw new UnsupportedGeometryException($"Curve type not supported: {curve.IsA()}");
        }

        private static List<Vec2> ReadIndexedPolyCurve(IfcEntity curve)
        {
            IfcEntity list = curve.Entity("Points");
            var raw = new List<Vec2>();
            foreach (IfcValue coordinate in list["CoordList"].AsList())
            {
                double[] c = coordinate.AsDoubles();
                raw.Add(new Vec2(c.Length > 0 ? c[0] : 0.0, c.Length > 1 ? c[1] : 0.0));
            }

            IReadOnlyList<IfcValue> segments = curve["Segments"].AsList();
            if (segments.Count == 0)
            {
                return raw;
            }

            var ordered = new List<Vec2>();
            foreach (IfcValue segment in segments)
            {
                foreach (IfcValue index in segment.Unwrapped.AsList())
                {
                    int i = index.AsInt() - 1;
                    if (i < 0 || i >= raw.Count)
                    {
                        continue;
                    }
                    if (ordered.Count == 0 || !ordered[ordered.Count - 1].Equals(raw[i]))
                    {
                        ordered.Add(raw[i]);
                    }
                }
            }
            if (ordered.Count > 1 && ordered[0].Equals(ordered[ordered.Count - 1]))
            {
                ordered.RemoveAt(ordered.Count - 1);
            }
            return ordered;
        }

        /// <summary>
        /// An arc: IfcTrimmedCurve over an IfcCircle. These turn up in composite
        /// curves for rounded window heads and stair nosings, and are the last thing
        /// standing between the castle and a clean conversion.
        /// </summary>
        private static List<Vec2> ReadTrimmedCurve(IfcEntity curve, double angleScale)
        {
            IfcEntity basis = curve.Entity("BasisCurve");
            if (basis == null || !basis.IsA("IfcCircle"))
            {
                throw new UnsupportedGeometryException(
                    $"Trimmed curve over {(basis == null ? "nothing" : basis.IsA())} not supported");
            }

            double radius = basis.Double("Radius");
            Matrix4 frame = Placement.AxisPlacementMatrix(basis.Entity("Position"));
            var centre = new Vec2(frame.M03, frame.M13);
            var xAxis = new Vec2(frame.M00, frame.M10);
            var yAxis = new Vec2(frame.M01, frame.M11);

            bool preferCartesian = curve.String("MasterRepresentation") != "PARAMETER";

            double start = TrimAngle(curve, curve["Trim1"], centre, xAxis, yAxis, preferCartesian, angleScale, 0.0);
            double end = TrimAngle(curve, curve["Trim2"], centre, xAxis, yAxis, preferCartesian, angleScale, 2.0 * Math.PI);

            bool sameSense = curve.Logical("SenseAgreement") != false;
            double sweep = end - start;

            if (sameSense)
            {
                while (sweep <= 1e-12) sweep += 2.0 * Math.PI;
            }
            else
            {
                while (sweep >= -1e-12) sweep -= 2.0 * Math.PI;
            }

            int steps = Math.Max(2, (int)Math.Ceiling(CircleSegments * Math.Abs(sweep) / (2.0 * Math.PI)));
            var points = new List<Vec2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double angle = start + sweep * i / steps;
                double cos = Math.Cos(angle) * radius;
                double sin = Math.Sin(angle) * radius;
                points.Add(new Vec2(
                    centre.X + xAxis.X * cos + yAxis.X * sin,
                    centre.Y + xAxis.Y * cos + yAxis.Y * sin));
            }
            return points;
        }

        private static double TrimAngle(IfcEntity owner, IfcValue trim, Vec2 centre, Vec2 xAxis, Vec2 yAxis,
            bool preferCartesian, double angleScale, double fallback)
        {
            double? fromPoint = null;
            double? fromParameter = null;

            foreach (IfcValue item in trim.AsList())
            {
                // A trim is a SELECT: either a point on the curve or a parameter
                // along it. Exporters commonly supply both and nominate one through
                // MasterRepresentation.
                IfcEntity point = owner.Model.Resolve(item);
                if (point != null && point.IsA("IfcCartesianPoint"))
                {
                    double[] c = point.Doubles("Coordinates");
                    if (c.Length >= 2)
                    {
                        fromPoint = AngleOf(new Vec2(c[0], c[1]), centre, xAxis, yAxis);
                    }
                    continue;
                }

                IfcValue unwrapped = item.Unwrapped;
                if (unwrapped.TryAsDouble(out double value))
                {
                    fromParameter = value;
                }
            }

            if (preferCartesian && fromPoint.HasValue)
            {
                return fromPoint.Value;
            }
            if (fromParameter.HasValue)
            {
                double value = fromParameter.Value;

                // A trim parameter is expressed in the project's plane-angle unit,
                // which IFC2X3 exporters routinely set to degrees. Use the real
                // scale when the caller supplied one.
                if (angleScale > 0.0)
                {
                    return value * angleScale;
                }

                // No scale available: guess. Anything past a full turn can only have
                // been degrees. This is a fallback, not a substitute — a 3 degree arc
                // is indistinguishable from a 3 radian one without the unit.
                if (Math.Abs(value) > 2.0 * Math.PI + 1e-9)
                {
                    value *= Math.PI / 180.0;
                }
                return value;
            }
            return fromPoint ?? fallback;
        }

        /// <summary>Angle of a trim point expressed as an IfcCartesianPoint.</summary>
        internal static double AngleOf(Vec2 point, Vec2 centre, Vec2 xAxis, Vec2 yAxis)
        {
            Vec2 offset = point - centre;
            double local_x = offset.X * xAxis.X + offset.Y * xAxis.Y;
            double local_y = offset.X * yAxis.X + offset.Y * yAxis.Y;
            return Math.Atan2(local_y, local_x);
        }
    }
}
