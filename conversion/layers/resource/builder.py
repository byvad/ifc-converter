# @author: Davy Bellens

"""Orchestrator for Resource layer solid generation."""

import conversion.layers.resource.appearance as appearance
from conversion.layers.resource.math3d import (
    IDENTITY, cross, dot, mat_multiply, normalize, scale, subtract
)
from conversion.layers.resource.mesh import Mesh
from conversion.layers.resource.placement import axis_placement_matrix, read_direction, read_point
from conversion.layers.resource.solid import (
    extruded_area_solid, faceted_brep, polygonal_face_set, surface_model, triangulated_face_set
)

SOLID_BUILDERS = {
    "IfcExtrudedAreaSolid": extruded_area_solid,
    "IfcFacetedBrep": faceted_brep,
    "IfcTriangulatedFaceSet": triangulated_face_set,
    "IfcPolygonalFaceSet": polygonal_face_set,
    "IfcFaceBasedSurfaceModel": surface_model,
    "IfcShellBasedSurfaceModel": surface_model,
}


class UnsupportedGeometry(Exception):
    """Raised when a Resource-layer item has no builder."""


def build_item(item, styles=True):
    mesh = _build_geometry(item, styles)
    if styles:
        mesh.fill_style(appearance.item_rgba(item))
    return mesh


def _build_geometry(item, styles=True):
    name = item.is_a()

    builder = SOLID_BUILDERS.get(name)
    if builder is not None:
        return builder(item)

    if name in ("IfcBooleanResult", "IfcBooleanClippingResult"):
        return build_item(item.FirstOperand, styles)

    if name == "IfcMappedItem":
        source = item.MappingSource
        origin = axis_placement_matrix(source.MappingOrigin)
        target = _transformation_operator_matrix(item.MappingTarget)
        mesh = Mesh()
        for sub in source.MappedRepresentation.Items:
            try:
                mesh.extend(build_item(sub, styles))
            except UnsupportedGeometry:
                continue
        return mesh.transformed(mat_multiply(target, origin))

    raise UnsupportedGeometry(name)


def _transformation_operator_matrix(op):
    if op is None:
        return IDENTITY
    origin = read_point(op.LocalOrigin)

    non_uniform = op.is_a("IfcCartesianTransformationOperator3DnonUniform")
    scale1 = float(getattr(op, "Scale", None) or 1.0)
    scale2 = float(getattr(op, "Scale2", None) or scale1) if non_uniform else scale1
    scale3 = float(getattr(op, "Scale3", None) or scale1) if non_uniform else scale1

    z_axis = normalize(read_direction(getattr(op, "Axis3", None), (0.0, 0.0, 1.0)))
    x_axis = normalize(read_direction(getattr(op, "Axis1", None), (1.0, 0.0, 0.0)))
    projected = subtract(x_axis, scale(z_axis, dot(x_axis, z_axis)))
    if dot(projected, projected) < 1e-20:
        projected = (1.0, 0.0, 0.0)
    x_axis = normalize(projected)

    # When Axis2 is actually supplied, take it as authored rather than
    # re-deriving it from X and Z. A mirrored mapping target (left/right-hand
    # instance) is commonly expressed by an Axis2 that is *not* simply
    # cross(Z, X); deriving it instead silently un-mirrors the instance.
    axis2 = getattr(op, "Axis2", None)
    if axis2 is not None:
        y_axis = normalize(read_direction(axis2, (0.0, 1.0, 0.0)))
    else:
        y_axis = cross(z_axis, x_axis)

    return (
        (x_axis[0] * scale1, y_axis[0] * scale2, z_axis[0] * scale3, origin[0]),
        (x_axis[1] * scale1, y_axis[1] * scale2, z_axis[1] * scale3, origin[1]),
        (x_axis[2] * scale1, y_axis[2] * scale2, z_axis[2] * scale3, origin[2]),
        (0.0, 0.0, 0.0, 1.0),
    )