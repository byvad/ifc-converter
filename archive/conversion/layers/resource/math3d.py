# @author: Davy Bellens

"""Vector and matrix math helpers. 4x4 matrices as nested tuples, row-major."""

import math

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