"""Mesh booleans for the Resource layer.

A BSP-tree solid subtraction, in the csg.js lineage, with no third-party
dependency. This exists so that Core can honour IfcRelVoidsElement and
IfcBooleanResult: both hand down a host solid and one or more cutters and
expect the difference back.

Scope, honestly stated: BSP booleans are exact for the well-behaved closed
solids that IFC openings almost always are (extruded boxes and prisms), and
unreliable for open shells, self-intersecting breps and coincident-face
degeneracies. Every entry point here fails soft — if the inputs are not
something this can handle, the host mesh comes back uncut rather than
mangled, because a wall with a missing hole is a far smaller error than a
wall turned inside out.

Style spans survive the cut. Each polygon carries the style of the triangle
it came from, and clipped fragments inherit it, so an element that was
styled per representation item keeps its colours. Faces newly created by
the cut have no style of their own and fall through to the product's
material, which is what Core fills in afterwards.
"""

from archive.conversion.layers.resource.mesh import Mesh

# Split tolerance, as a fraction of the model's own extent. IFC files are
# routinely authored in millimetres with site coordinates in the tens of
# thousands, so a fixed absolute epsilon is either uselessly tight at that
# scale or destructive on a model authored in metres.
RELATIVE_EPSILON = 1e-8
MINIMUM_EPSILON = 1e-9

# A guard, not a limit anyone should hit. Openings are boxes; if a cutter
# arrives with thousands of faces something upstream is wrong, and a BSP
# will happily spend minutes proving it.
MAX_POLYGONS = 20000

COPLANAR, BACK, FRONT, SPANNING = 0, 1, 2, 3


class Polygon:
    """A convex polygon plus the style it inherited from its source mesh."""

    __slots__ = ("vertices", "style")

    def __init__(self, vertices, style=None):
        self.vertices = vertices
        self.style = style

    def flipped(self):
        return Polygon(self.vertices[::-1], self.style)


class Plane:
    __slots__ = ("normal", "offset")

    def __init__(self, normal, offset):
        self.normal = normal
        self.offset = offset

    @staticmethod
    def through(vertices):
        """Newell's method: stable on slivers where a single cross product is not."""
        nx = ny = nz = 0.0
        count = len(vertices)
        for i in range(count):
            current = vertices[i]
            following = vertices[(i + 1) % count]
            nx += (current[1] - following[1]) * (current[2] + following[2])
            ny += (current[2] - following[2]) * (current[0] + following[0])
            nz += (current[0] - following[0]) * (current[1] + following[1])
        length = (nx * nx + ny * ny + nz * nz) ** 0.5
        if length < 1e-30:
            return None
        normal = (nx / length, ny / length, nz / length)
        first = vertices[0]
        return Plane(normal, normal[0] * first[0]
                             + normal[1] * first[1]
                             + normal[2] * first[2])

    def flipped(self):
        return Plane((-self.normal[0], -self.normal[1], -self.normal[2]),
                     -self.offset)

    def distance(self, point):
        return (self.normal[0] * point[0]
                + self.normal[1] * point[1]
                + self.normal[2] * point[2]) - self.offset

    def split(self, polygon, coplanar_front, coplanar_back, front, back, eps):
        """Sort one polygon into the four buckets, splitting it if it spans."""
        polygon_type = 0
        types = []
        for vertex in polygon.vertices:
            distance = self.distance(vertex)
            vertex_type = (FRONT if distance > eps
                           else BACK if distance < -eps
                           else COPLANAR)
            polygon_type |= vertex_type
            types.append(vertex_type)

        if polygon_type == COPLANAR:
            plane = Plane.through(polygon.vertices)
            facing = plane is not None and (
                plane.normal[0] * self.normal[0]
                + plane.normal[1] * self.normal[1]
                + plane.normal[2] * self.normal[2]) > 0
            (coplanar_front if facing else coplanar_back).append(polygon)
        elif polygon_type == FRONT:
            front.append(polygon)
        elif polygon_type == BACK:
            back.append(polygon)
        else:
            front_vertices = []
            back_vertices = []
            count = len(polygon.vertices)
            for i in range(count):
                j = (i + 1) % count
                this_type, next_type = types[i], types[j]
                this_vertex, next_vertex = polygon.vertices[i], polygon.vertices[j]
                if this_type != BACK:
                    front_vertices.append(this_vertex)
                if this_type != FRONT:
                    back_vertices.append(this_vertex)
                if (this_type | next_type) == SPANNING:
                    span = (self.normal[0] * (next_vertex[0] - this_vertex[0])
                            + self.normal[1] * (next_vertex[1] - this_vertex[1])
                            + self.normal[2] * (next_vertex[2] - this_vertex[2]))
                    if abs(span) < 1e-30:
                        continue
                    t = (self.offset - (self.normal[0] * this_vertex[0]
                                        + self.normal[1] * this_vertex[1]
                                        + self.normal[2] * this_vertex[2])) / span
                    crossing = (this_vertex[0] + t * (next_vertex[0] - this_vertex[0]),
                                this_vertex[1] + t * (next_vertex[1] - this_vertex[1]),
                                this_vertex[2] + t * (next_vertex[2] - this_vertex[2]))
                    front_vertices.append(crossing)
                    back_vertices.append(crossing)
            if len(front_vertices) >= 3:
                front.append(Polygon(front_vertices, polygon.style))
            if len(back_vertices) >= 3:
                back.append(Polygon(back_vertices, polygon.style))


class Node:
    """One cell of the BSP tree.

    Every traversal below is iterative rather than recursive. Tree depth
    tracks the input's planar complexity, not its triangle count, and a
    curtain wall or a stepped brep can bury the interpreter's recursion
    limit long before it exhausts memory.
    """

    __slots__ = ("plane", "front", "back", "polygons", "eps")

    def __init__(self, eps):
        self.plane = None
        self.front = None
        self.back = None
        self.polygons = []
        self.eps = eps

    def build(self, polygons):
        pending = [(self, polygons)]
        while pending:
            node, batch = pending.pop()
            if not batch:
                continue
            if node.plane is None:
                for candidate in batch:
                    node.plane = Plane.through(candidate.vertices)
                    if node.plane is not None:
                        break
                if node.plane is None:
                    continue
            ahead, behind = [], []
            for polygon in batch:
                node.plane.split(polygon, node.polygons, node.polygons,
                                 ahead, behind, node.eps)
            if ahead:
                if node.front is None:
                    node.front = Node(node.eps)
                pending.append((node.front, ahead))
            if behind:
                if node.back is None:
                    node.back = Node(node.eps)
                pending.append((node.back, behind))

    def clip_polygons(self, polygons):
        """Return the parts of `polygons` lying outside this node's solid."""
        kept = []
        pending = [(self, polygons)]
        while pending:
            node, batch = pending.pop()
            if not batch:
                continue
            if node.plane is None:
                kept.extend(batch)
                continue
            ahead, behind = [], []
            for polygon in batch:
                node.plane.split(polygon, ahead, behind, ahead, behind, node.eps)
            if node.front is not None:
                pending.append((node.front, ahead))
            else:
                kept.extend(ahead)
            if node.back is not None:
                pending.append((node.back, behind))
            # No back child means everything behind this plane is interior,
            # and interior surface is exactly what a difference discards.
        return kept

    def clip_to(self, other):
        pending = [self]
        while pending:
            node = pending.pop()
            node.polygons = other.clip_polygons(node.polygons)
            if node.front is not None:
                pending.append(node.front)
            if node.back is not None:
                pending.append(node.back)

    def invert(self):
        pending = [self]
        while pending:
            node = pending.pop()
            node.polygons = [p.flipped() for p in node.polygons]
            if node.plane is not None:
                node.plane = node.plane.flipped()
            node.front, node.back = node.back, node.front
            if node.front is not None:
                pending.append(node.front)
            if node.back is not None:
                pending.append(node.back)

    def all_polygons(self):
        collected = []
        pending = [self]
        while pending:
            node = pending.pop()
            collected.extend(node.polygons)
            if node.front is not None:
                pending.append(node.front)
            if node.back is not None:
                pending.append(node.back)
        return collected


def _epsilon(meshes):
    low = [float("inf")] * 3
    high = [float("-inf")] * 3
    for mesh in meshes:
        for vertex in mesh.vertices:
            for axis in range(3):
                if vertex[axis] < low[axis]:
                    low[axis] = vertex[axis]
                if vertex[axis] > high[axis]:
                    high[axis] = vertex[axis]
    if low[0] > high[0]:
        return MINIMUM_EPSILON
    extent = max(high[axis] - low[axis] for axis in range(3))
    return max(MINIMUM_EPSILON, extent * RELATIVE_EPSILON)


def _to_polygons(mesh):
    """Triangles to tagged polygons, carrying each triangle's style along."""
    style_of = {}
    for style, start, stop in mesh.spans():
        for index in range(start, stop):
            style_of[index] = style

    polygons = []
    for index, (a, b, c) in enumerate(mesh.triangles):
        vertices = [mesh.vertices[a], mesh.vertices[b], mesh.vertices[c]]
        polygons.append(Polygon(vertices, style_of.get(index)))
    return polygons


def _to_mesh(polygons):
    """Fan-triangulate back into a Mesh, welding exact duplicate vertices.

    Polygons are grouped by style first so that each style occupies one
    contiguous run of triangles, which is the shape Mesh.spans() and the
    OBJ writer's `usemtl` runs both expect.
    """
    grouped = {}
    for polygon in polygons:
        grouped.setdefault(polygon.style, []).append(polygon)

    mesh = Mesh()
    lookup = {}

    def index_of(vertex):
        key = (round(vertex[0], 6), round(vertex[1], 6), round(vertex[2], 6))
        found = lookup.get(key)
        if found is None:
            found = len(mesh.vertices)
            lookup[key] = found
            mesh.vertices.append((float(vertex[0]), float(vertex[1]), float(vertex[2])))
        return found

    for style, group in grouped.items():
        start = len(mesh.triangles)
        for polygon in group:
            indices = [index_of(v) for v in polygon.vertices]
            for i in range(1, len(indices) - 1):
                a, b, c = indices[0], indices[i], indices[i + 1]
                if a == b or b == c or a == c:
                    continue  # collapsed by the weld; contributes no area
                mesh.add_triangle(a, b, c)
        stop = len(mesh.triangles)
        if style is not None and stop > start:
            mesh.groups.append([style, start, stop])
    return mesh


def subtract(mesh, cutters):
    """Return `mesh` with every solid in `cutters` removed.

    Fails soft: on any degeneracy the original mesh is returned unchanged.
    """
    if not mesh.triangles or not cutters:
        return mesh

    usable = [c for c in cutters if c.triangles]
    if not usable:
        return mesh

    total = len(mesh.triangles) + sum(len(c.triangles) for c in usable)
    if total > MAX_POLYGONS:
        return mesh

    eps = _epsilon([mesh] + usable)

    try:
        solid = Node(eps)
        solid.build(_to_polygons(mesh))

        for cutter in usable:
            # The cutter's own styling is meaningless on the host: an opening
            # is a void volume, not a surface anyone sees. Strip it so the
            # faces it contributes fall through to the product's material.
            void = Node(eps)
            void.build([Polygon(p.vertices, None) for p in _to_polygons(cutter)])

            solid.invert()
            solid.clip_to(void)
            void.clip_to(solid)
            void.invert()
            void.clip_to(solid)
            void.invert()
            solid.build(void.all_polygons())
            solid.invert()

        result = _to_mesh(solid.all_polygons())
    except (RecursionError, MemoryError, ValueError, ZeroDivisionError):
        return mesh

    return result