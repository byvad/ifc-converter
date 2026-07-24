"""Regression tests for hole bridging and style spans."""
from conversion.layers.resource.resource_layer import triangulate_3d, Mesh, HOLE_STATS

def tri_area(ring, tris):
    total = 0.0
    for a, b, c in tris:
        pa, pb, pc = ring[a], ring[b], ring[c]
        u = (pb[0]-pa[0], pb[1]-pa[1], pb[2]-pa[2])
        v = (pc[0]-pa[0], pc[1]-pa[1], pc[2]-pa[2])
        cx, cy, cz = u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]
        total += 0.5*(cx*cx+cy*cy+cz*cz)**0.5
    return total

SQ   = [(0,0,0),(10,0,0),(10,10,0),(0,10,0)]
HOLE = [(4,4,0),(6,4,0),(6,6,0),(4,6,0)]
SMALL= [(1,1,0),(2,1,0),(2,2,0),(1,2,0)]
WALL = [(0,0,0),(0,0,10),(0,10,10),(0,10,0)]
WIN  = [(0,3,3),(0,3,7),(0,7,7),(0,7,3)]
NINE = [[(x,y,0),(x+.5,y,0),(x+.5,y+.5,0),(x,y+.5,0)] for x in (1,4,7) for y in (1,4,7)]
PANEL= [(0,0,0),(1000,0,0),(1000,2500,0),(0,2500,0)]
PANES= [[(230,z,0),(970,z,0),(970,z+300,0),(230,z+300,0)] for z in (200,700,1200,1700,2200)]

CASES = [
    ("no holes",                SQ,   [],                 100.0),
    ("centre hole",             SQ,   [HOLE],              96.0),
    ("two holes",               SQ,   [HOLE, SMALL],       95.0),
    ("reversed hole winding",   SQ,   [HOLE[::-1]],        96.0),
    ("triangular hole",         SQ,   [[(3,3,0),(7,3,0),(5,7,0)]], 92.0),
    ("nine holes",              SQ,   NINE,                97.75),
    ("vertical wall + window",  WALL, [WIN],               84.0),
    ("L-shaped outer + hole",   [(0,0,0),(10,0,0),(10,4,0),(4,4,0),(4,10,0),(0,10,0)],
                                      [SMALL],             63.0),
    ("five stacked panes",      PANEL, PANES,   1000*2500 - 5*740*300),
    # malformed input must degrade to a filled hole, never to an empty face
    ("hole on the boundary",    SQ,   [[(0,0,0),(2,0,0),(2,2,0),(0,2,0)]],  96.0),
    ("hole larger than outer",  SQ,   [[(-5,-5,0),(15,-5,0),(15,15,0),(-5,15,0)]], 100.0),
    ("empty hole ring",         SQ,   [[]],               100.0),
    ("degenerate outer",        [(0,0,0),(1,0,0)], [HOLE],  0.0),
]

def main():
    failures = 0
    for name, outer, holes, expect in CASES:
        HOLE_STATS.reset()
        ring, tris = triangulate_3d(outer, holes)
        got = tri_area(ring, tris)
        ok = abs(got - expect) < 1e-6
        failures += not ok
        print("%-26s %10.3f  expect %10.3f  %s" % (name, got, expect, "ok" if ok else "FAIL"))

    # style spans must survive all of this untouched
    m = Mesh()
    m.vertices = [(0,0,0)]*9
    m.triangles = [(0,1,2),(3,4,5),(6,7,8)]
    m.groups = [["red", 0, 1]]
    m.fill_style("blue")
    ok = m.spans() == [("red",0,1), ("blue",1,3)]
    failures += not ok
    print("%-26s %s" % ("style spans", "ok" if ok else "FAIL"))

    print("\n%s" % ("ALL PASS" if not failures else "%d FAILURES" % failures))
    return failures

if __name__ == "__main__":
    raise SystemExit(main())
