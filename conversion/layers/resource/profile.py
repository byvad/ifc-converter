# @author: Davy Bellens

"""Profile schema: 2D cross-sections, returned as lists of (x, y)."""

import math
from conversion.layers.resource.math3d import IDENTITY, mat_apply
from conversion.layers.resource.placement import axis_placement_matrix

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


def read_profile(profile):
    name = profile.is_a()

    if name == "IfcRectangleProfileDef" or name == "IfcRoundedRectangleProfileDef":
        half_x = float(profile.XDim) / 2.0
        half_y = float(profile.YDim) / 2.0
        points = [(-half_x, -half_y), (half_x, -half_y), (half_x, half_y), (-half_x, half_y)]

    elif name in ("IfcCircleProfileDef", "IfcCircleHollowProfileDef"):
        points = _circle_points(float(profile.Radius))

    elif name == "IfcEllipseProfileDef":
        a, b = float(profile.SemiAxis1), float(profile.SemiAxis2)
        points = [
            (a * math.cos(2 * math.pi * i / CIRCLE_SEGMENTS),
             b * math.sin(2 * math.pi * i / CIRCLE_SEGMENTS))
            for i in range(CIRCLE_SEGMENTS)
        ]

    elif name in ("IfcArbitraryClosedProfileDef", "IfcArbitraryProfileDefWithVoids"):
        points = _read_curve_2d(profile.OuterCurve)

    elif name == "IfcDerivedProfileDef":
        base = read_profile(profile.ParentProfile)
        matrix = axis_placement_matrix(profile.Operator.LocalOrigin) \
            if hasattr(profile.Operator, "LocalOrigin") else IDENTITY
        return [mat_apply(matrix, (x, y, 0.0))[:2] for x, y in base]

    else:
        raise NotImplementedError(f"Profile type not supported: {name}")

    position = getattr(profile, "Position", None)
    if position is not None:
        matrix = axis_placement_matrix(position)
        points = [mat_apply(matrix, (x, y, 0.0))[:2] for x, y in points]

    return points


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