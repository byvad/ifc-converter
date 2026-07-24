# @author: Davy Bellens

"""Geometric Model schema: solids -> Mesh."""

from conversion.layers.resource.math3d import scale
from conversion.layers.resource.mesh import Mesh
from conversion.layers.resource.placement import axis_placement_matrix, read_direction, read_point
from .profile import read_profile
from conversion.layers.resource.triangulate import triangulate_2d, triangulate_3d


def extruded_area_solid(solid):
    ring = read_profile(solid.SweptArea)
    if len(ring) < 3:
        return Mesh()

    depth = float(solid.Depth)
    direction = read_direction(solid.ExtrudedDirection, (0.0, 0.0, 1.0))
    offset = scale(direction, depth)

    mesh = Mesh()
    bottom = [(x, y, 0.0) for x, y in ring]
    top = [(x + offset[0], y + offset[1], offset[2]) for x, y in ring]

    bottom_idx = mesh.add_polygon_ring(bottom)
    top_idx = mesh.add_polygon_ring(top)

    caps = triangulate_2d(ring)
    for a, b, c in caps:
        mesh.add_triangle(bottom_idx[c], bottom_idx[b], bottom_idx[a])
        mesh.add_triangle(top_idx[a], top_idx[b], top_idx[c])

    count = len(ring)
    for i in range(count):
        j = (i + 1) % count
        mesh.add_triangle(bottom_idx[i], bottom_idx[j], top_idx[j])
        mesh.add_triangle(bottom_idx[i], top_idx[j], top_idx[i])

    return mesh.transformed(axis_placement_matrix(solid.Position))


def faceted_brep(brep):
    mesh = Mesh()
    _add_shell(mesh, brep.Outer)
    return mesh


def _add_shell(mesh, shell):
    for face in shell.CfsFaces:
        outer = None
        holes = []

        for bound in face.Bounds:
            loop = bound.Bound
            if not loop.is_a("IfcPolyLoop"):
                continue
            points = [read_point(p) for p in loop.Polygon]
            if len(points) < 3:
                continue
            if getattr(bound, "Orientation", True) is False:
                points.reverse()

            if bound.is_a("IfcFaceOuterBound"):
                outer = points
            else:
                holes.append(points)

        if outer is None:
            if not holes:
                continue
            holes.sort(key=_ring_extent, reverse=True)
            outer, holes = holes[0], holes[1:]

        ring, triangles = triangulate_3d(outer, holes)
        indices = mesh.add_polygon_ring(ring)
        for a, b, c in triangles:
            mesh.add_triangle(indices[a], indices[b], indices[c])


def _ring_extent(points):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    zs = [p[2] for p in points]
    return ((max(xs) - min(xs)) ** 2 + (max(ys) - min(ys)) ** 2
            + (max(zs) - min(zs)) ** 2)


def triangulated_face_set(fs):
    mesh = Mesh()
    mesh.vertices = [tuple(float(c) for c in pt) for pt in fs.Coordinates.CoordList]
    for tri in fs.CoordIndex:
        mesh.add_triangle(int(tri[0]) - 1, int(tri[1]) - 1, int(tri[2]) - 1)
    return mesh


def polygonal_face_set(fs):
    mesh = Mesh()
    coords = [tuple(float(c) for c in pt) for pt in fs.Coordinates.CoordList]
    mesh.vertices = list(coords)
    for face in fs.Faces:
        points = [coords[int(i) - 1] for i in face.CoordIndex]
        holes = [[coords[int(i) - 1] for i in inner]
                 for inner in (getattr(face, "InnerCoordIndices", None) or [])]

        if not holes:
            indices = [int(i) - 1 for i in face.CoordIndex]
            _, triangles = triangulate_3d(points)
            for a, b, c in triangles:
                mesh.add_triangle(indices[a], indices[b], indices[c])
            continue

        ring, triangles = triangulate_3d(points, holes)
        indices = mesh.add_polygon_ring(ring)
        for a, b, c in triangles:
            mesh.add_triangle(indices[a], indices[b], indices[c])
    return mesh


def surface_model(model):
    mesh = Mesh()
    shells = getattr(model, "FbsmFaces", None) or getattr(model, "SbsmBoundary", [])
    for shell in shells:
        if hasattr(shell, "CfsFaces"):
            _add_shell(mesh, shell)
    return mesh