# @author: Davy Bellens

"""Mesh: the thing we are building on the way back up."""

from conversion.layers.resource.math3d import mat_apply, scale

class Mesh:
    def __init__(self):
        self.vertices = []
        self.triangles = []
        self.groups = []

    def fill_style(self, style):
        if style is None:
            return
        for start, stop in self._gaps():
            self.groups.append([style, start, stop])

    def _gaps(self):
        gaps = []
        cursor = 0
        for _, start, stop in sorted(self.groups, key=lambda g: g[1]):
            if start > cursor:
                gaps.append((cursor, start))
            cursor = max(cursor, stop)
        if cursor < len(self.triangles):
            gaps.append((cursor, len(self.triangles)))
        return gaps

    def spans(self):
        out = []
        cursor = 0
        for style, start, stop in sorted(self.groups, key=lambda g: g[1]):
            if start > cursor:
                out.append((None, cursor, start))
                cursor = start
            if stop > cursor:
                out.append((style, cursor, stop))
                cursor = stop
        if cursor < len(self.triangles):
            out.append((None, cursor, len(self.triangles)))
        return out

    def add_polygon_ring(self, points):
        start = len(self.vertices)
        self.vertices.extend(points)
        return list(range(start, len(self.vertices)))

    def add_triangle(self, a, b, c):
        self.triangles.append((a, b, c))

    def extend(self, other, transform=None):
        offset = len(self.vertices)
        base = len(self.triangles)
        if transform is None:
            self.vertices.extend(other.vertices)
        else:
            self.vertices.extend(mat_apply(transform, v) for v in other.vertices)
        self.triangles.extend(
            (a + offset, b + offset, c + offset) for a, b, c in other.triangles
        )
        self.groups.extend(
            [style, start + base, stop + base]
            for style, start, stop in other.groups
        )

    def transformed(self, matrix):
        out = Mesh()
        out.vertices = [mat_apply(matrix, v) for v in self.vertices]
        out.triangles = list(self.triangles)
        out.groups = [list(g) for g in self.groups]
        return out

    def scaled(self, factor):
        out = Mesh()
        out.vertices = [scale(v, factor) for v in self.vertices]
        out.triangles = list(self.triangles)
        out.groups = [list(g) for g in self.groups]
        return out

    def to_y_up(self):
        out = Mesh()
        out.vertices = [(x, z, -y) for x, y, z in self.vertices]
        out.triangles = list(self.triangles)
        out.groups = [list(g) for g in self.groups]
        return out

    def __len__(self):
        return len(self.triangles)