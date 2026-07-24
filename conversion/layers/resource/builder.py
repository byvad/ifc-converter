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
    factor = float(getattr(op, "Scale", None) or 1.0)
    x_axis = normalize(read_direction(getattr(op, "Axis1", None), (1.0, 0.0, 0.0)))
    z_axis = normalize(read_direction(getattr(op, "Axis3", None), (0.0, 0.0, 1.0)))
    projected = subtract(x_axis, scale(z_axis, dot(x_axis, z_axis)))
    if dot(projected, projected) < 1e-20:
        projected = (1.0, 0.0, 0.0)
    x_axis = normalize(projected)
    y_axis = cross(z_axis, x_axis)
    return (
        (x_axis[0] * factor, y_axis[0] * factor, z_axis[0] * factor, origin[0]),
        (x_axis[1] * factor, y_axis[1] * factor, z_axis[1] * factor, origin[1]),
        (x_axis[2] * factor, y_axis[2] * factor, z_axis[2] * factor, origin[2]),
        (0.0, 0.0, 0.0, 1.0),
    )