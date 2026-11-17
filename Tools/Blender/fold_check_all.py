"""Side-view collision check of every set's thigh/calf/foot models in all 5 leg stances.
Usage: python fold_check_all.py <blend with all sets> [--ironclad-blend <ironclad_set.blend>]"""
import bpy, math, sys
from shapely.geometry import MultiPoint, Point

blend = sys.argv[1]
def _arg(flag, default):
    return float(sys.argv[sys.argv.index(flag) + 1]) if flag in sys.argv else default
# Weight-class multipliers (see MechWeightClass in the Unity project): armor width / depth, seam gap, joint radius.
AW, AD, AI, JR = _arg("--aw", 1.0), _arg("--ad", 1.0), _arg("--ai", 1.0), _arg("--jr", 1.0)
T = C = 1.15; r = 0.15 * JR; gap = 0.06; inner = 0.09 * AI; ankleH = 0.26
ENV = {"Thigh": (0.46 * AW, 0.22 * AD), "Calf": (0.42 * AW, 0.22 * AD)}
POSES = [("Human", True, 0.94), ("ChickenFwd", True, 0.80), ("ReverseExt", False, 0.93), ("ChickenBack", False, 0.74), ("CritFold", False, 0.70)]
NAMES = ["Ironclad", "Bastion", "Overclock", "Ripjaw", "Afterburn", "Voltaic", "Salvo", "Goliath", "Mantis", "Aegis"]

def pts_of(name):
    o = bpy.data.objects.get(name)
    if o is None: return None
    dg = bpy.context.evaluated_depsgraph_get()
    return [(v.co.z, -v.co.y, v.co.x) for v in o.evaluated_get(dg).data.vertices]

def fitted(pts, part, Ls):
    W, D = ENV[part]
    b = [p[0] for p in pts]; o = [p[1] for p in pts]; w = [p[2] for p in pts]
    sx = W / (max(w) - min(w)); sy = Ls / (max(b) - min(b)); sz = min((sx + sy) / 2, 1.6 * D / (max(o) - min(o)))
    return [(-Ls / 2 + (bb - min(b)) * sy, inner + (oo - min(o)) * sz) for bb, oo, _ in pts]

def foot(pts, toe):
    zs = [p[0] for p in pts]; s = (0.48 if toe else 0.28) / (max(zs) - min(zs)); ys = [p[1] for p in pts]
    c = 0.44 if toe else -0.30
    return MultiPoint([(0.06 + (h - min(ys)) * s, c + (z * s if toe else -z * s)) for z, h, _ in pts]).convex_hull

def place(pts, top, bot, sign):
    dy = top[0] - bot[0]; dz = top[1] - bot[1]; L = math.hypot(dy, dz); u = (dy / L, dz / L); n = (-u[1], u[0])
    if n[1] < 0: n = (-n[0], -n[1])
    mid = ((top[0] + bot[0]) / 2, (top[1] + bot[1]) / 2)
    return MultiPoint([(mid[0] + u[0] * a + n[0] * b * sign, mid[1] + u[1] * a + n[1] * b * sign) for a, b in pts]).convex_hull

def ik(H, A, fwd):
    dy = A[0] - H[0]; dz = A[1] - H[1]; d = math.hypot(dy, dz); base = math.atan2(dz, dy)
    a = math.acos((T * T + d * d - C * C) / (2 * T * d)); t = base + (-a if fwd else a)
    return (H[0] + T * math.cos(t), H[1] + T * math.sin(t))

Ls = T - 2 * (r + gap)
def check(srcs):
    """srcs: dict part key -> blender object name."""
    shapes = {}
    for part in ("Thigh", "Calf"):
        for half in ("Front", "Back"):
            shapes[(part, half)] = fitted(pts_of(srcs[part + half]), part, Ls)
    toe = foot(pts_of(srcs["FootFront"]), True); heel = foot(pts_of(srcs["FootBack"]), False)
    report = {}
    for name, fwd, e in POSES:
        A = (ankleH, 0); H = (ankleH + e * 2.3, 0); K = ik(H, A, fwd)
        polys = {"toe": toe, "heel": heel}
        for half, s in (("Front", 1), ("Back", -1)):
            polys["th" + half] = place(shapes[("Thigh", half)], H, K, s)
            polys["ca" + half] = place(shapes[("Calf", half)], K, A, s)
        hits = []
        ks = list(polys)
        for i in range(len(ks)):
            for j in range(i + 1, len(ks)):
                a, b = ks[i], ks[j]
                if a[:2] == b[:2] or {a, b} <= {"toe", "heel"}: continue
                inter = polys[a].intersection(polys[b])
                if inter.area > 1e-6: hits.append(f"{a}-{b} {inter.area:.5f} at h={inter.centroid.x:.3f} fwd={inter.centroid.y:.3f}")
        report[name] = hits
    return report

bpy.ops.wm.open_mainfile(filepath=blend)
bad = 0
for i, n in enumerate(NAMES, 1):
    tag = f"Set{i:02d}_{n}"
    srcs = {k: f"{k}_{tag}" for k in ("ThighFront", "ThighBack", "CalfFront", "CalfBack", "FootFront", "FootBack")}
    if any(bpy.data.objects.get(v) is None for v in srcs.values()):
        print(tag, "missing parts"); continue
    rep = check(srcs)
    hits = {k: v for k, v in rep.items() if v}
    bad += bool(hits)
    print(tag, "clear" if not hits else hits)
print("SETS WITH HITS:", bad)
