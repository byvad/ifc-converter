# @author: Davy Bellens

"""Profile schema: 2D cross-sections, returned as lists of (x, y)."""

import math
from archive.conversion.layers.resource.math3d import IDENTITY, mat_apply
from archive.conversion.layers.resource.placement import axis_placement_matrix, read_direction, read_point

CIRCLE_SEGMENTS = 24


def _polyline_points(curve):
    return [tuple(float(c) for c in p.Coordinates[:2]) for p in curve.Points]


def _indexed_polycurve_points(curve):
    pts_entity = curve.Points
    raw = [tuple(float(c) for c in coord[:2]) for coord in pts_entity.CoordList]

    segments = getattr(curve, "Segments", None)
    if not segments:
        return raw

    ordered = []
    for seg in segments:
        idx = list(seg[0]) if not isinstance(seg, (list, tuple)) else list(seg)
        for i in idx:
            point = raw[int(i) - 1]
            if not ordered or ordered[-1] != point:
                ordered.append(point)
    if len(ordered) > 1 and ordered[0] == ordered[-1]:
        ordered.pop()
    return ordered


def _circle_points(radius, segments=CIRCLE_SEGMENTS):
    return [
        (radius * math.cos(2 * math.pi * i / segments),
         radius * math.sin(2 * math.pi * i / segments))
        for i in range(segments)
    ]


def _normalize_2d(v):
    length = math.hypot(v[0], v[1])
    if length < 1e-12:
        raise ValueError("Cannot normalize a zero-length 2D vector")
    return (v[0] / length, v[1] / length)


def _apply_2d(matrix, point):
    x, y, _ = mat_apply(matrix, (point[0], point[1], 0.0))
    return (x, y)


def _operator_2d_matrix(operator):
    """IfcCartesianTransformationOperator2D(nonUniform) -> 4x4 matrix.

    This is a *transformation operator* (Axis1/Axis2/LocalOrigin/Scale/Scale2),
    not an axis placement (Axis/RefDirection/Location). The two entities look
    superficially similar but axis_placement_matrix expects the latter and
    will blow up on .Location, which operators don't have.
    """
    origin = read_point(operator.LocalOrigin)
    scale1 = float(getattr(operator, "Scale", None) or 1.0)

    non_uniform = operator.is_a("IfcCartesianTransformationOperator2DnonUniform")
    scale2 = float(getattr(operator, "Scale2", None) or scale1) if non_uniform else scale1

    x_dir = read_direction(getattr(operator, "Axis1", None), (1.0, 0.0, 0.0))
    x_axis = _normalize_2d((x_dir[0], x_dir[1]))

    axis2 = getattr(operator, "Axis2", None)
    if axis2 is not None:
        y_dir = read_direction(axis2, (0.0, 1.0, 0.0))
        y_axis = _normalize_2d((y_dir[0], y_dir[1]))
    else:
        y_axis = (-x_axis[1], x_axis[0])

    return (
        (x_axis[0] * scale1, y_axis[0] * scale2, 0.0, origin[0]),
        (x_axis[1] * scale1, y_axis[1] * scale2, 0.0, origin[1]),
        (0.0, 0.0, 1.0, origin[2] if len(origin) > 2 else 0.0),
        (0.0, 0.0, 0.0, 1.0),
    )


def read_profile(profile):
    """Returns (outer_points, holes) where holes is a list of point rings."""
    name = profile.is_a()
    holes = []

    if name == "IfcRectangleProfileDef" or name == "IfcRoundedRectangleProfileDef":
        half_x = float(profile.XDim) / 2.0
        half_y = float(profile.YDim) / 2.0
        points = [(-half_x, -half_y), (half_x, -half_y), (half_x, half_y), (-half_x, half_y)]

    elif name in ("IfcCircleProfileDef", "IfcCircleHollowProfileDef"):
        points = _circle_points(float(profile.Radius))
        if name == "IfcCircleHollowProfileDef":
            wall = getattr(profile, "WallThickness", None)
            if wall:
                inner_radius = float(profile.Radius) - float(wall)
                if inner_radius > 0:
                    holes = [_circle_points(inner_radius)]

    elif name == "IfcEllipseProfileDef":
        a, b = float(profile.SemiAxis1), float(profile.SemiAxis2)
        points = [
            (a * math.cos(2 * math.pi * i / CIRCLE_SEGMENTS),
             b * math.sin(2 * math.pi * i / CIRCLE_SEGMENTS))
            for i in range(CIRCLE_SEGMENTS)
        ]

    elif name == "IfcArbitraryClosedProfileDef":
        points = _read_curve_2d(profile.OuterCurve)

    elif name == "IfcArbitraryProfileDefWithVoids":
        points = _read_curve_2d(profile.OuterCurve)
        for inner in profile.InnerCurves or []:
            hole_points = _read_curve_2d(inner)
            if len(hole_points) > 1 and hole_points[0] == hole_points[-1]:
                hole_points.pop()
            holes.append(hole_points)

    elif name == "IfcDerivedProfileDef":
        base_points, base_holes = read_profile(profile.ParentProfile)
        matrix = _operator_2d_matrix(profile.Operator)
        points = [_apply_2d(matrix, p) for p in base_points]
        holes = [[_apply_2d(matrix, p) for p in hole] for hole in base_holes]
        # The Operator already carries the full transform for derived
        # profiles; there is no separate Position to apply on top.
        return points, holes

    else:
        raise NotImplementedError(f"Profile type not supported: {name}")

    position = getattr(profile, "Position", None)
    if position is not None:
        matrix = axis_placement_matrix(position)
        points = [mat_apply(matrix, (x, y, 0.0))[:2] for x, y in points]
        holes = [[mat_apply(matrix, (x, y, 0.0))[:2] for x, y in hole] for hole in holes]

    return points, holes


def _read_curve_2d(curve):
    if curve.is_a("IfcPolyline"):
        points = _polyline_points(curve)
        if len(points) > 1 and points[0] == points[-1]:
            points.pop()
        return points
    if curve.is_a("IfcIndexedPolyCurve"):
        return _indexed_polycurve_points(curve)
    if curve.is_a("IfcCircle"):
        return _circle_points(float(curve.Radius))
    if curve.is_a("IfcCompositeCurve"):
        points = []
        for segment in curve.Segments:
            for p in _read_curve_2d(segment.ParentCurve):
                if not points or points[-1] != p:
                    points.append(p)
        return points
    raise NotImplementedError(f"Curve type not supported: {curve.is_a()}")