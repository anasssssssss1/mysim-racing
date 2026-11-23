"""Build a closed track centreline from OpenStreetMap raceway ways.

usage: python osm_track.py <osm.json> <out.json> <way_id,way_id,...> [pit_way_id]
Ways are chained end to end in the order given (each is flipped if needed).
Output: metres, x east / y north, resampled every STEP m, counter-clockwise,
starting at the point nearest the pit lane's middle.
"""
import json, math, sys

STEP = 2.0  # m between centreline points


def project(geom, lat0, lon0):
    kx = 111320 * math.cos(math.radians(lat0))
    return [((p["lon"] - lon0) * kx, (p["lat"] - lat0) * 110574) for p in geom]


def chain(ways):
    pts = list(ways[0])
    for w in ways[1:]:
        if math.dist(pts[-1], w[-1]) < math.dist(pts[-1], w[0]):
            w = w[::-1]
        assert math.dist(pts[-1], w[0]) < 1.0, "ways do not join"
        pts += w[1:]
    if math.dist(pts[0], pts[-1]) > 1.0 and len(ways) > 1 and math.dist(pts[0], ways[0][-1]) < 1.0:
        raise SystemExit("first way is reversed; reorder")
    assert math.dist(pts[0], pts[-1]) < 1.0, "loop does not close"
    return pts[:-1]


def catmull(pts, per=8):
    """Centripetal Catmull-Rom through a closed polygon."""
    n, out = len(pts), []
    for i in range(n):
        p0, p1, p2, p3 = (pts[(i + k) % n] for k in (-1, 0, 1, 2))
        t = [0.0]
        for a, b in ((p0, p1), (p1, p2), (p2, p3)):
            t.append(t[-1] + max(math.dist(a, b), 1e-6) ** 0.5)
        for s in range(per):
            u = t[1] + (t[2] - t[1]) * s / per
            def lerp(a, b, ta, tb):
                w = (u - ta) / (tb - ta)
                return (a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w)
            a1, a2, a3 = lerp(p0, p1, t[0], t[1]), lerp(p1, p2, t[1], t[2]), lerp(p2, p3, t[2], t[3])
            b1, b2 = lerp(a1, a2, t[0], t[2]), lerp(a2, a3, t[1], t[3])
            out.append(lerp(b1, b2, t[1], t[2]))
    return out


def resample(pts, step):
    ring = pts + [pts[0]]
    seg = [math.dist(a, b) for a, b in zip(ring, ring[1:])]
    total = sum(seg)
    n = round(total / step)
    out, i, acc = [], 0, 0.0
    for k in range(n):
        s = k * total / n
        while acc + seg[i] < s:
            acc += seg[i]
            i += 1
        w = (s - acc) / seg[i]
        a, b = ring[i], ring[i + 1]
        out.append((a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w))
    return out, total


def main():
    src, dst, ids = sys.argv[1], sys.argv[2], [int(x) for x in sys.argv[3].split(",")]
    pit = int(sys.argv[4]) if len(sys.argv) > 4 else None
    els = {e["id"]: e for e in json.load(open(src))["elements"]}
    g0 = els[ids[0]]["geometry"][0]
    lat0, lon0 = g0["lat"], g0["lon"]
    pts = chain([project(els[i]["geometry"], lat0, lon0) for i in ids])
    if sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(pts, pts[1:] + pts[:1])) < 0:
        pts = pts[::-1]  # counter-clockwise
    pts, length = resample(catmull(pts), STEP)
    if pit:
        pg = project(els[pit]["geometry"], lat0, lon0)
        mid = pg[len(pg) // 2]
        # ponytail: start line = nearest point to the pit lane middle; set the real line by hand per track
        k = min(range(len(pts)), key=lambda j: math.dist(pts[j], mid))
        pts = pts[k:] + pts[:k]
    json.dump({"source": "OpenStreetMap contributors (ODbL)", "ways": ids, "origin": [lat0, lon0],
               "length_m": round(length, 1), "step_m": STEP,
               "points": [[round(x, 3), round(y, 3)] for x, y in pts]}, open(dst, "w"))
    print(f"{dst}: {len(pts)} points, {length:.0f} m")


if __name__ == "__main__":
    main()
