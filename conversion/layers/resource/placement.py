# @author: Davy Bellens

"""Geometric Constraint and Geometry schemas: points, directions, placements."""

from conversion.layers.resource.math3d import (
    IDENTITY, cross, dot, mat_multiply, normalize, scale, subtract
)


def read_point(entity):
    coords = list(entity.Coordinates)
    while len(coords) < 3:
        coords.append(0.0)
    return tuple(float(c) for c in coords[:3])


def read_direction(entity, default=(0.0, 0.0, 1.0)):
    if entity is None:
        return default
    ratios = list(entity.DirectionRatios)
    while len(ratios) < 3:
        ratios.append(0.0)
    return tuple(float(r) for r in ratios[:3])


def axis_placement_matrix(placement):
    """IfcAxis2Placement2D or 3D -> 4x4 matrix."""
    if placement is None:
        return IDENTITY

    origin = read_point(placement.Location)

    if placement.is_a("IfcAxis2Placement2D"):
        ref = read_direction(getattr(placement, "RefDirection", None), (1.0, 0.0, 0.0))
        x_axis = normalize((ref[0], ref[1], 0.0))
        y_axis = (-x_axis[1], x_axis[0], 0.0)
        z_axis = (0.0, 0.0, 1.0)
    else:
        z_axis = normalize(read_direction(getattr(placement, "Axis", None), (0.0, 0.0, 1.0)))
        ref = read_direction(getattr(placement, "RefDirection", None), (1.0, 0.0, 0.0))
        projected = subtract(ref, scale(z_axis, dot(ref, z_axis)))
        if dot(projected, projected) < 1e-20:
            fallback = (1.0, 0.0, 0.0) if abs(z_axis[0]) < 0.9 else (0.0, 1.0, 0.0)
            projected = subtract(fallback, scale(z_axis, dot(fallback, z_axis)))
        x_axis = normalize(projected)
        y_axis = cross(z_axis, x_axis)

    return (
        (x_axis[0], y_axis[0], z_axis[0], origin[0]),
        (x_axis[1], y_axis[1], z_axis[1], origin[1]),
        (x_axis[2], y_axis[2], z_axis[2], origin[2]),
        (0.0, 0.0, 0.0, 1.0),
    )


def local_placement_matrix(placement):
    """Resolve an IfcLocalPlacement chain up to the world."""
    if placement is None:
        return IDENTITY
    if not placement.is_a("IfcLocalPlacement"):
        return IDENTITY

    local = axis_placement_matrix(placement.RelativePlacement)
    parent = local_placement_matrix(placement.PlacementRelTo)
    return mat_multiply(parent, local)