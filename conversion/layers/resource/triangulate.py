# @author: Davy Bellens

"""Polygon triangulation and hole bridging algorithms."""

from conversion.layers.resource.math3d import cross, dot, normalize


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

            crossz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
            if crossz <= 0:
                continue

            if any(
                poly[j] not in (a, b, c) and _point_in_triangle(poly[j], a, b, c)
                for j in indices
                if j not in (prev_i, curr_i, next_i)
            ):
                continue

            triangles.append((prev_i, curr_i, next_i))
            indices.pop(i)
            clipped = True
            break

        if not clipped:
            triangles.extend(
                (indices[0], indices[i], indices[i + 1])
                for i in range(1, len(indices) - 1)
            )
            return triangles

    if len(indices) == 3:
        triangles.append(tuple(indices))
    return triangles


class _HoleStats:
    def __init__(self):
        self.reset()

    def reset(self):
        self.bridged = 0
        self.filled = 0


HOLE_STATS = _HoleStats()


def _orient(a, b, c):
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def _crosses(a, b, c, d):
    d1, d2 = _orient(a, b, c), _orient(a, b, d)
    d3, d4 = _orient(c, d, a), _orient(c, d, b)
    return (d1 > 0) != (d2 > 0) and (d3 > 0) != (d4 > 0)


def _point_in_ring(point, ring):
    x, y = point
    inside = False
    for i in range(len(ring)):
        ax, ay = ring[i - 1]
        bx, by = ring[i]
        if (ay > y) != (by > y):
            t = (y - ay) / (by - ay)
            if x < ax + t * (bx - ax):
                inside = not inside
    return inside


def _bridge_ok(bridge_a, bridge_b, rings):
    for ring in rings:
        for i in range(len(ring)):
            c, d = ring[i - 1], ring[i]
            if c == bridge_a or c == bridge_b or d == bridge_a or d == bridge_b:
                continue
            if _crosses(bridge_a, bridge_b, c, d):
                return False
    return True


def _splice_hole(ring, hole):
    midpoints_ok = []
    for i, outer_point in enumerate(ring):
        for j, hole_point in enumerate(hole):
            distance = ((outer_point[0] - hole_point[0]) ** 2
                        + (outer_point[1] - hole_point[1]) ** 2)
            midpoints_ok.append((distance, i, j))
    midpoints_ok.sort()

    for _, i, j in midpoints_ok:
        a, b = ring[i], hole[j]
        if a == b:
            continue
        if not _bridge_ok(a, b, (ring, hole)):
            continue
        middle = ((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0)
        if not _point_in_ring(middle, ring) or _point_in_ring(middle, hole):
            continue
        return ring[:i + 1] + hole[j:] + hole[:j + 1] + ring[i:]

    return ring


def _signed_area_pairs(pairs):
    return _signed_area_2d([xy for xy, _ in pairs])


def _bridge_holes(outer, holes):
    if _signed_area_pairs(outer) < 0:
        outer = outer[::-1]

    prepared = []
    for hole in holes:
        if len(hole) < 3:
            continue
        if _signed_area_pairs(hole) > 0:
            hole = hole[::-1]
        prepared.append(hole)
    prepared.sort(key=lambda h: -max(xy[0] for xy, _ in h))

    ring = outer
    for hole in prepared:
        flat_ring = [xy for xy, _ in ring]
        flat_hole = [xy for xy, _ in hole]
        merged = _splice_hole(flat_ring, flat_hole)
        if len(merged) == len(flat_ring):
            HOLE_STATS.filled += 1
            continue
        HOLE_STATS.bridged += 1
        lookup = {}
        for xy, payload in list(ring) + list(hole):
            lookup.setdefault(xy, payload)
        ring = [(xy, lookup[xy]) for xy in merged]
    return ring


def triangulate_3d(points, holes=()):
    if len(points) < 3:
        return list(points), []

    nx = ny = nz = 0.0
    for i in range(len(points)):
        cur = points[i]
        nxt = points[(i + 1) % len(points)]
        nx += (cur[1] - nxt[1]) * (cur[2] + nxt[2])
        ny += (cur[2] - nxt[2]) * (cur[0] + nxt[0])
        nz += (cur[0] - nxt[0]) * (cur[1] + nxt[1])

    normal = (nx, ny, nz)
    if dot(normal, normal) < 1e-20:
        return list(points), []
    normal = normalize(normal)

    helper = (0.0, 0.0, 1.0) if abs(normal[2]) < 0.9 else (1.0, 0.0, 0.0)
    u = normalize(cross(helper, normal))
    v = cross(normal, u)

    def flatten(ring3d):
        return [((dot(p, u), dot(p, v)), p) for p in ring3d]

    flat_outer = flatten(points)
    if not holes:
        return list(points), triangulate_2d([xy for xy, _ in flat_outer])

    flat_holes = [flatten(h) for h in holes]
    merged = _bridge_holes(flat_outer, flat_holes)
    merged_flat = [xy for xy, _ in merged]
    triangles = triangulate_2d(merged_flat)

    outer_area = abs(_signed_area_2d([xy for xy, _ in flat_outer]))
    expected = outer_area - sum(
        abs(_signed_area_2d([xy for xy, _ in h])) for h in flat_holes)
    covered = _triangulation_area_2d(merged_flat, triangles)

    if outer_area > 0 and abs(covered - expected) > outer_area * 0.01:
        HOLE_STATS.bridged -= len(holes)
        HOLE_STATS.filled += len(holes)
        return list(points), triangulate_2d([xy for xy, _ in flat_outer])

    return [p for _, p in merged], triangles


def _triangulation_area_2d(points, triangles):
    return sum(
        abs(_orient(points[a], points[b], points[c])) / 2.0
        for a, b, c in triangles
    )