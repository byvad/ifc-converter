"""Resource layer: the bottom of the descent.

Nothing here knows what a wall is. These entities are pure geometry and are
referenced by everything above them, never the other way round. This module is
where raw coordinates finally turn into triangles.

Handled here:
    Geometry            IfcCartesianPoint, IfcDirection, IfcAxis2Placement*
    Geometric Constraint IfcLocalPlacement
    Profile             rectangle / circle / arbitrary closed profiles
    Geometric Model     IfcExtrudedAreaSolid, IfcFacetedBrep, face sets
    Topology            IfcClosedShell, IfcFace, IfcPolyLoop
"""

import math

CIRCLE_SEGMENTS = 24


# ==========================================================================
# Small vector / matrix helpers. 4x4 matrices as nested tuples, row-major.
# ==========================================================================

IDENTITY = (
    (1.0, 0.0, 0.0, 0.0),
    (0.0, 1.0, 0.0, 0.0),
    (0.0, 0.0, 1.0, 0.0),
    (0.0, 0.0, 0.0, 1.0),
)


def mat_multiply(a, b):
    return tuple(
        tuple(sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4))
        for r in range(4)
    )


def mat_apply(m, point):
    x, y, z = point
    return (
        m[0][0] * x + m[0][1] * y + m[0][2] * z + m[0][3],
        m[1][0] * x + m[1][1] * y + m[1][2] * z + m[1][3],
        m[2][0] * x + m[2][1] * y + m[2][2] * z + m[2][3],
    )


def cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def normalize(v):
    length = math.sqrt(dot(v, v))
    if length < 1e-12:
        raise ValueError("Cannot normalize a zero-length vector")
    return tuple(c / length for c in v)


def subtract(a, b):
    return tuple(x - y for x, y in zip(a, b))


def scale(v, k):
    return tuple(c * k for c in v)


# ==========================================================================
# Mesh: the thing we are building on the way back up.
# ==========================================================================

class Mesh:
    """Vertices plus triangle indices. Indices are zero-based and local."""

    def __init__(self):
        self.vertices = []
        self.triangles = []

    def add_polygon_ring(self, points):
        """Append points and return their indices."""
        start = len(self.vertices)
        self.vertices.extend(points)
        return list(range(start, len(self.vertices)))

    def add_triangle(self, a, b, c):
        self.triangles.append((a, b, c))

    def extend(self, other, transform=None):
        """Merge another mesh in, optionally transforming its vertices."""
        offset = len(self.vertices)
        if transform is None:
            self.vertices.extend(other.vertices)
        else:
            self.vertices.extend(mat_apply(transform, v) for v in other.vertices)
        self.triangles.extend(
            (a + offset, b + offset, c + offset) for a, b, c in other.triangles
        )

    def transformed(self, matrix):
        out = Mesh()
        out.vertices = [mat_apply(matrix, v) for v in self.vertices]
        out.triangles = list(self.triangles)
        return out

    def scaled(self, factor):
        out = Mesh()
        out.vertices = [scale(v, factor) for v in self.vertices]
        out.triangles = list(self.triangles)
        return out

    def to_y_up(self):
        """Rotate Z-up (IFC) into Y-up (most OBJ viewers).

        IFC is Z-up by specification. OBJ does not mandate an up-axis, but
        three.js, Unity, Maya and most web previews assume Y-up, so an
        unconverted model appears tipped onto its back.

        This is a -90 degree rotation about X: (x, y, z) -> (x, z, -y).
        Its determinant is +1, so handedness is preserved and the existing
        triangle winding stays correct. A naive axis *swap* such as
        (x, y, z) -> (x, z, y) would mirror the model and invert every normal.
        """
        out = Mesh()
        out.vertices = [(x, z, -y) for x, y, z in self.vertices]
        out.triangles = list(self.triangles)
        return out

    def __len__(self):
        return len(self.triangles)


# ==========================================================================
# Polygon triangulation (ear clipping).
#
# A triangle fan would be simpler but is only correct for convex polygons,
# and real building profiles are routinely L-shaped or worse. Ear clipping
# handles any simple polygon without holes.
# ==========================================================================

def _signed_area_2d(poly):
    total = 0.0
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        total += x1 * y2 - x2 * y1
    return total / 2.0


def _point_in_triangle(p, a, b, c):
    def sign(p1, p2, p3):
        return (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])

    d1, d2, d3 = sign(p, a, b), sign(p, b, c), sign(p, c, a)
    has_neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
    has_pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    return not (has_neg and has_pos)


def triangulate_2d(poly):
    """Ear-clip a simple 2D polygon. Returns index triples into poly."""
    n = len(poly)
    if n < 3:
        return []
    if n == 3:
        return [(0, 1, 2)]

    # Work counter-clockwise so the ear test has a consistent sense.
    indices = list(range(n))
    if _signed_area_2d(poly) < 0:
        indices.reverse()

    triangles = []
    guard = 0
    while len(indices) > 3 and guard < n * n:
        guard += 1
        clipped = False
        for i in range(len(indices)):
            prev_i = indices[i - 1]
            curr_i = indices[i]
            next_i = indices[(i + 1) % len(indices)]
            a, b, c = poly[prev_i], poly[curr_i], poly[next_i]

            # Convex corner?
            crossz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
            if crossz <= 0:
                continue

            # Does any other vertex sit inside the candidate ear?
            if any(
                _point_in_triangle(poly[j], a, b, c)
                for j in indices
                if j not in (prev_i, curr_i, next_i)
            ):
                continue

            triangles.append((prev_i, curr_i, next_i))
            indices.pop(i)
            clipped = True
            break

        if not clipped:
            # Degenerate or self-intersecting polygon; fall back to a fan so
            # we emit something rather than dropping the face entirely.
            return [(indices[0], indices[i], indices[i + 1])
                    for i in range(1, len(indices) - 1)]

    if len(indices) == 3:
        triangles.append(tuple(indices))
    return triangles


def triangulate_3d(points):
    """Triangulate a planar 3D polygon by projecting onto its own plane."""
    if len(points) < 3:
        return []

    # Newell's method gives a stable normal even for slightly non-planar rings.
    nx = ny = nz = 0.0
    for i in range(len(points)):
        cur = points[i]
        nxt = points[(i + 1) % len(points)]
        nx += (cur[1] - nxt[1]) * (cur[2] + nxt[2])
        ny += (cur[2] - nxt[2]) * (cur[0] + nxt[0])
        nz += (cur[0] - nxt[0]) * (cur[1] + nxt[1])

    normal = (nx, ny, nz)
    if dot(normal, normal) < 1e-20:
        return []
    normal = normalize(normal)

    # Build an in-plane basis to flatten into 2D.
    helper = (0.0, 0.0, 1.0) if abs(normal[2]) < 0.9 else (1.0, 0.0, 0.0)
    u = normalize(cross(helper, normal))
    v = cross(normal, u)

    flat = [(dot(p, u), dot(p, v)) for p in points]
    return triangulate_2d(flat)


# ==========================================================================
# Geometry schema: points, directions, placements.
# ==========================================================================

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
    """IfcAxis2Placement2D or 3D -> 4x4 matrix.

    Axis is the local Z, RefDirection the local X. They are not guaranteed
    orthogonal, so X is re-orthogonalised against Z (Gram-Schmidt) exactly as
    the IFC specification requires.
    """
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
            # RefDirection was parallel to Axis; pick any perpendicular.
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
    """Resolve an IfcLocalPlacement chain up to the world.

    Each IfcLocalPlacement holds a RelativePlacement and optionally a
    PlacementRelTo pointing at its parent. Composing parent-first gives the
    world matrix. This is the mechanism 'use-world-coords' hides from you.
    """
    if placement is None:
        return IDENTITY
    if not placement.is_a("IfcLocalPlacement"):
        # IfcGridPlacement is not supported; treat as identity.
        return IDENTITY

    local = axis_placement_matrix(placement.RelativePlacement)
    parent = local_placement_matrix(placement.PlacementRelTo)
    return mat_multiply(parent, local)


# ==========================================================================
# Profile schema: 2D cross-sections, returned as lists of (x, y).
# ==========================================================================

def _polyline_points(curve):
    return [tuple(float(c) for c in p.Coordinates[:2]) for p in curve.Points]


def _indexed_polycurve_points(curve):
    """IfcIndexedPolyCurve: a point list plus optional segment indices."""
    pts_entity = curve.Points
    raw = [tuple(float(c) for c in coord[:2]) for coord in pts_entity.CoordList]

    segments = getattr(curve, "Segments", None)
    if not segments:
        return raw

    ordered = []
    for seg in segments:
        # IfcLineIndex is a list of point indices (1-based); IfcArcIndex is a
        # three-point arc, approximated here by its endpoints.
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
    """IfcProfileDef -> list of (x, y) forming a closed ring.

    The ring is returned without a repeated closing point.
    """
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
        # Voids are ignored: ear clipping here does not support holes.
        points = _read_curve_2d(profile.OuterCurve)

    elif name == "IfcDerivedProfileDef":
        base = read_profile(profile.ParentProfile)
        matrix = axis_placement_matrix(profile.Operator.LocalOrigin) \
            if hasattr(profile.Operator, "LocalOrigin") else IDENTITY
        return [mat_apply(matrix, (x, y, 0.0))[:2] for x, y in base]

    else:
        raise NotImplementedError(f"Profile type not supported: {name}")

    # Most profile defs carry their own 2D placement.
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


# ==========================================================================
# Geometric Model schema: solids -> Mesh.
# ==========================================================================

def extruded_area_solid(solid):
    """IfcExtrudedAreaSolid: sweep a 2D profile along a direction."""
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
        mesh.add_triangle(bottom_idx[c], bottom_idx[b], bottom_idx[a])  # facing down
        mesh.add_triangle(top_idx[a], top_idx[b], top_idx[c])           # facing up

    # Side walls: one quad per profile edge, split into two triangles.
    count = len(ring)
    for i in range(count):
        j = (i + 1) % count
        mesh.add_triangle(bottom_idx[i], bottom_idx[j], top_idx[j])
        mesh.add_triangle(bottom_idx[i], top_idx[j], top_idx[i])

    # The solid's own Position places the swept shape in its parent frame.
    return mesh.transformed(axis_placement_matrix(solid.Position))


def faceted_brep(brep):
    """IfcFacetedBrep: an explicit boundary representation."""
    mesh = Mesh()
    _add_shell(mesh, brep.Outer)
    return mesh


def _add_shell(mesh, shell):
    for face in shell.CfsFaces:
        for bound in face.Bounds:
            # Inner bounds are holes; ear clipping cannot handle them, so only
            # outer bounds are meshed. Reported by the caller as a limitation.
            if bound.is_a("IfcFaceBound") and not bound.is_a("IfcFaceOuterBound"):
                continue
            loop = bound.Bound
            if not loop.is_a("IfcPolyLoop"):
                continue
            points = [read_point(p) for p in loop.Polygon]
            if getattr(bound, "Orientation", True) is False:
                points.reverse()
            indices = mesh.add_polygon_ring(points)
            for a, b, c in triangulate_3d(points):
                mesh.add_triangle(indices[a], indices[b], indices[c])


def triangulated_face_set(fs):
    """IfcTriangulatedFaceSet: already triangles, just reindex.

    CoordIndex is 1-based, which is the single most common off-by-one in
    hand-written IFC readers.
    """
    mesh = Mesh()
    mesh.vertices = [tuple(float(c) for c in pt) for pt in fs.Coordinates.CoordList]
    for tri in fs.CoordIndex:
        mesh.add_triangle(int(tri[0]) - 1, int(tri[1]) - 1, int(tri[2]) - 1)
    return mesh


def polygonal_face_set(fs):
    """IfcPolygonalFaceSet: n-gon faces over a shared point list."""
    mesh = Mesh()
    coords = [tuple(float(c) for c in pt) for pt in fs.Coordinates.CoordList]
    mesh.vertices = list(coords)
    for face in fs.Faces:
        indices = [int(i) - 1 for i in face.CoordIndex]
        points = [coords[i] for i in indices]
        for a, b, c in triangulate_3d(points):
            mesh.add_triangle(indices[a], indices[b], indices[c])
    return mesh


def surface_model(model):
    """IfcFaceBasedSurfaceModel / IfcShellBasedSurfaceModel."""
    mesh = Mesh()
    shells = getattr(model, "FbsmFaces", None) or getattr(model, "SbsmBoundary", [])
    for shell in shells:
        if hasattr(shell, "CfsFaces"):
            _add_shell(mesh, shell)
    return mesh


# Dispatch table: which Resource-layer builder handles which entity.
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


def build_item(item):
    """Turn one Resource-layer representation item into a Mesh.

    This is the bottom of the descent. Everything above this function has been
    resolving references; here the numbers finally become triangles.
    """
    name = item.is_a()

    builder = SOLID_BUILDERS.get(name)
    if builder is not None:
        return builder(item)

    if name in ("IfcBooleanResult", "IfcBooleanClippingResult"):
        # Proper CSG needs a solid modelling kernel. Keeping the first operand
        # gives the uncut host element: a wall keeps its doorway filled in.
        return build_item(item.FirstOperand)

    if name == "IfcMappedItem":
        source = item.MappingSource
        origin = axis_placement_matrix(source.MappingOrigin)
        target = _transformation_operator_matrix(item.MappingTarget)
        mesh = Mesh()
        for sub in source.MappedRepresentation.Items:
            try:
                mesh.extend(build_item(sub))
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