"""
IronFauna - Ironclad (Set01), third generation: sleek, polished armor built from smooth curved surfaces.

  python sleek_ironclad.py --out <dir> [--blend] [--no-export]     (pip `bpy` module, or blender -b -P ... --)

Why a new method: the earlier generators lofted convex polygons through a handful of stations, so every plate was
a faceted slab with bevelled corners and read as blocky however it was shaped. Here every plate is a dense smooth
surface - a superellipse cross-section swept along spline width/depth profiles - shaded smooth, with crisp creases
only where they are wanted (a keel line, the seam edge, the walls of a raised panel). Detail is a few large layered
panels, thin inlaid panel lines, swept fins and razor-thin emissive slits; no bolts, boxes or hazard bands.

Authoring frames are unchanged (see ironclad.py / ClamshellMeshLibrary): plates have X = width, Z = along the bone,
outward = -Y with the seam face at Y = 0; lengthwise parts (feet) have X = width, Z = length (+Z = free tip) and
height toward -Y. So the parts drop into the same slots and fold_check_all.py still applies.
"""
import math
import os
import sys

import bpy  # noqa: E402  (must load before bmesh)
import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ironclad as ic  # noqa: E402
from ironclad import ENV  # noqa: E402

OUT_DIR = ic.OUT_DIR if "--out" in sys.argv else "/tmp/sleek"
os.makedirs(OUT_DIR, exist_ok=True)
TAG = "Set01_Ironclad"
NU, NT = 26, 40          # surface resolution across / along


# =============================================================================== maths
def spline(pts, t):
    """Catmull-Rom through [(t, v...)] sorted by t; returns the interpolated value tuple at t (clamped)."""
    if t <= pts[0][0]:
        return pts[0][1:]
    if t >= pts[-1][0]:
        return pts[-1][1:]
    for i in range(len(pts) - 1):
        if pts[i][0] <= t <= pts[i + 1][0]:
            break
    p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
    s = (t - p1[0]) / max(p2[0] - p1[0], 1e-9)
    out = []
    for k in range(1, len(p1)):
        a, b, c, d = p0[k], p1[k], p2[k], p3[k]
        v = 0.5 * (2 * b + (-a + c) * s + (2 * a - 5 * b + 4 * c - d) * s * s + (-a + 3 * b - 3 * c + d) * s ** 3)
        out.append(v)
    return tuple(out)


def ease(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


# =============================================================================== surface
class Surface:
    """Outer skin of one plate: u in [-1, 1] across, t in [0, 1] along Z from -L/2 to +L/2.
    prof = [(t, width_frac, depth_frac[, x_shift_frac])]; section exponent n (2 = ellipse, 4 = squarish);
    crease = keel height as a fraction of depth (a sharp centre line); skirt = depth kept at the seam edge."""

    def __init__(self, w, L, d, prof, n=1.25, crease=0.08, skirt=0.14, z0=0.0, off=(0.0, 0.0), shoulder=0.58, crown=0.07):
        self.w, self.L, self.d, self.prof, self.n = w, L, d, prof, n
        self.shoulder, self.crown = shoulder, crown
        self.crease, self.skirt, self.z0, self.off = crease, skirt, z0, off

    def frac(self, t):
        v = spline(self.prof, t)
        return v[0], v[1], (v[2] if len(v) > 2 else 0.0)

    def depth(self, u, t):
        _, sd, _ = self.frac(t)
        # car-body section: a gently crowned top out to a crisp shoulder line, then a curved flank down to the seam
        a = abs(u)
        us = self.shoulder
        if a <= us:
            body = 1.0 - self.crown * (a / us) ** 2
        else:
            k = (a - us) / (1.0 - us)
            body = (1.0 - self.crown) * (1.0 - k ** self.n) ** (1.0 / self.n)
        keel = self.crease * max(0.0, 1.0 - a / us) ** 1.2
        return self.d * max(sd, 0.0) * (self.skirt + (1.0 - self.skirt) * body + keel)

    def point(self, u, t, lift=0.0):
        sw, _, sx = self.frac(t)
        x = u * self.w / 2 * max(sw, 0.0) + sx * self.w + self.off[0]
        z = -self.L / 2 + t * self.L + self.z0
        p = Vector((x, -self.depth(u, t) + self.off[1], z))
        if lift:
            p += self.normal(u, t) * lift
        return p

    def normal(self, u, t):
        e = 1e-3
        du = self.point(min(u + e, 1), t) - self.point(max(u - e, -1), t)
        dt = self.point(u, min(t + e, 1)) - self.point(u, max(t - e, 0))
        nrm = dt.cross(du)
        if nrm.length < 1e-9:
            return Vector((0, -1, 0))
        nrm.normalize()
        return nrm if nrm.y < 0 else -nrm


# =============================================================================== part builder
class Part:
    def __init__(self):
        self.bm = bmesh.new()
        self.sharp = set()

    def cap(self, ring, role):
        """Fan-triangulated cap around the ring's centroid (end caps are not planar)."""
        mi = ic.ROLE_ORDER.index(role)
        c = self.bm.verts.new(sum((v.co for v in ring), Vector()) / len(ring))
        out = []
        for j in range(len(ring)):
            try:
                f = self.bm.faces.new([ring[j], ring[(j + 1) % len(ring)], c])
                f.material_index = mi
                out.append(f)
            except ValueError:
                pass
        return out

    def _faces(self, grid, role, close_u=False, cap=None, flip=False):
        """grid[i][j] verts (i along t, j across u). Adds quads, marks material, returns faces."""
        mi = ic.ROLE_ORDER.index(role)
        out = []
        rows, cols = len(grid), len(grid[0])
        for i in range(rows - 1):
            for j in range(cols - (0 if close_u else 1)):
                jj = (j + 1) % cols
                q = [grid[i][j], grid[i][jj], grid[i + 1][jj], grid[i + 1][j]]
                if flip:
                    q.reverse()
                try:
                    f = self.bm.faces.new(q)
                    f.material_index = mi
                    out.append(f)
                except ValueError:
                    pass
        return out

    def shell(self, s, role="Shell"):
        """Closed solid: smooth outer skin + flat seam face at the skin's base (Y = off.y)."""
        ts = [i / (NT - 1) for i in range(NT)]
        us = [-1 + 2 * j / (NU - 1) for j in range(NU)]
        # cluster samples toward the ends and edges so tips and seam edges stay crisp
        ts = [0.5 - 0.5 * math.cos(math.pi * t) for t in ts]
        us = [math.copysign(abs(u) ** 0.8, u) for u in us]
        outer = [[self.bm.verts.new(s.point(u, t)) for u in us] for t in ts]
        base = [[self.bm.verts.new(Vector((s.point(u, t).x, s.off[1], s.point(u, t).z))) for u in (us[-1], us[0])] for t in ts]
        mi = ic.ROLE_ORDER.index(role)
        fs = self._faces(outer, role)
        # rings: outer arc then the two base corners -> side walls and seam face
        rings = [outer[i] + base[i] for i in range(NT)]
        n = len(rings[0])
        for i in range(NT - 1):
            for j in range(NU - 1, n):
                jj = (j + 1) % n
                try:
                    f = self.bm.faces.new([rings[i][j], rings[i][jj], rings[i + 1][jj], rings[i + 1][j]])
                    f.material_index = mi
                    fs.append(f)
                except ValueError:
                    pass
        for ring in (rings[0], rings[-1]):
            fs += self.cap(ring, role)
        self._orient(fs)
        return fs

    def panel(self, s, u0, u1, t0, t1, lift=0.008, role="Shell", inset=(0.0, 0.0), sink=0.004, res=(12, 16), gap=True):
        """Raised conformal panel: the skin patch u in [u0(t), u1(t)] x t in [t0, t1], lifted along the normal,
        with walls dropping just below the skin. u0/u1 may be callables of the local s in [0, 1] (chevrons)."""
        if gap:
            g = 0.035
            ga = (lambda k: (u0(k) if callable(u0) else u0) - g)
            gb = (lambda k: (u1(k) if callable(u1) else u1) + g)
            self.panel(s, ga, gb, max(0.0, t0 - g * 0.6), min(1.0, t1 + g * 0.6), lift=lift * 0.35, role="Dark", gap=False, res=res)
        nu, nt = res
        top, bot = [], []
        for i in range(nt):
            k = i / (nt - 1)
            t = t0 + (t1 - t0) * k
            a = u0(k) if callable(u0) else u0
            b = u1(k) if callable(u1) else u1
            rt, rb = [], []
            for j in range(nu):
                u = a + (b - a) * j / (nu - 1)
                rt.append(self.bm.verts.new(s.point(u, t, lift)))
                rb.append(self.bm.verts.new(s.point(u, t, -sink)))
            top.append(rt)
            bot.append(rb)
        fs = self._faces(top, role)
        ring = lambda g: [g[0][j] for j in range(nu)] + [g[i][-1] for i in range(1, nt)] + [g[-1][j] for j in range(nu - 2, -1, -1)] + [g[i][0] for i in range(nt - 2, 0, -1)]
        rt, rb = ring(top), ring(bot)
        mi = ic.ROLE_ORDER.index(role)
        for j in range(len(rt)):
            jj = (j + 1) % len(rt)
            try:
                f = self.bm.faces.new([rt[j], rt[jj], rb[jj], rb[j]])
                f.material_index = mi
                fs.append(f)
            except ValueError:
                pass
        self._orient(fs, ref=s.point((a + b) / 2, (t0 + t1) / 2, -0.05))
        return fs

    def line(self, s, u0, u1, t0, t1, width=0.006, role="Dark", lift=0.0015):
        """Thin inlaid panel line (or emissive slit) from (u0, t0) to (u1, t1), conforming to the skin."""
        n = 16
        pts = [(u0 + (u1 - u0) * k / (n - 1), t0 + (t1 - t0) * k / (n - 1)) for k in range(n)]
        top, bot = [], []
        for idx, (u, t) in enumerate(pts):
            p = s.point(u, t)
            q = s.point(*pts[min(idx + 1, n - 1)]) - s.point(*pts[max(idx - 1, 0)])
            nrm = s.normal(u, t)
            side = q.cross(nrm).normalized() * width / 2
            top.append([self.bm.verts.new(p + side + nrm * lift), self.bm.verts.new(p - side + nrm * lift)])
            bot.append([self.bm.verts.new(p + side - nrm * 0.003), self.bm.verts.new(p - side - nrm * 0.003)])
        grid = [[bot[i][0], top[i][0], top[i][1], bot[i][1]] for i in range(n)]
        fs = self._faces(grid, role, close_u=True)
        mi = ic.ROLE_ORDER.index(role)
        for g in (grid[0], grid[-1]):
            f = self.bm.faces.new(g)
            f.material_index = mi
            fs.append(f)
        self._orient(fs, ref=s.point((u0 + u1) / 2, (t0 + t1) / 2, -0.05))
        return fs

    def fin(self, root, axis_out, axis_len, chord, height, sweep, thick=0.016, tip_chord=0.35, role="Shell"):
        """Swept fin with a lens (airfoil-like) section: root centre point, outward and lengthwise unit axes.
        chord along axis_len, rises by height along axis_out while sweeping back by sweep along -axis_len."""
        side = axis_len.cross(axis_out).normalized()
        sec = []
        m = 12
        for k in range(m):
            a = 2 * math.pi * k / m
            c = (1 - math.cos(a)) / 2                      # 0 leading edge -> 1 trailing edge -> 0
            th = math.sin(a) * (1 - c) ** 0.5 * 1.3          # thicker toward the leading edge, sharp trailing edge
            sec.append((c, th))
        grid = []
        steps = 8
        for i in range(steps):
            k = i / (steps - 1)
            ch = chord * (1 - (1 - tip_chord) * k)
            lead = root + axis_len * (chord / 2) - axis_len * (sweep * k ** 1.3) + axis_out * (height * k)
            tk = thick * (1 - 0.7 * k) / 2
            grid.append([self.bm.verts.new(lead - axis_len * (c * ch) + side * (th * tk)) for c, th in sec])
        fs = self._faces(grid, role, close_u=True)
        mi = ic.ROLE_ORDER.index(role)
        for g in (grid[0], grid[-1]):
            fs += self.cap(g, role)
        self._orient(fs, ref=root + axis_out * height * 0.4 - axis_len * sweep * 0.4)
        return fs

    def pod(self, centre, length, rx, ry, prof, role="Shell", n=3.0, along=Vector((0, 0, 1))):
        """Closed smooth nacelle (thruster, finger) along Z: superellipse section with radii rx, ry scaled by
        prof [(t, scale)] from tail (t=0) to nose (t=1)."""
        m, steps = 20, 18
        grid = []
        for i in range(steps):
            t = 0.5 - 0.5 * math.cos(math.pi * i / (steps - 1))
            sc = spline(prof, t)[0]
            ring = []
            for k in range(m):
                a = 2 * math.pi * k / m
                ca, sa = math.cos(a), math.sin(a)
                x = math.copysign(abs(ca) ** (2 / n), ca) * rx * sc
                y = math.copysign(abs(sa) ** (2 / n), sa) * ry * sc
                ring.append(self.bm.verts.new(centre + Vector((x, y, -length / 2 + t * length))))
            grid.append(ring)
        fs = self._faces(grid, role, close_u=True)
        mi = ic.ROLE_ORDER.index(role)
        for g in (grid[0], grid[-1]):
            fs += self.cap(g, role)
        self._orient(fs, ref=centre)
        return fs

    def _orient(self, faces, ref=None):
        verts = {v for f in faces for v in f.verts}
        c = ref if ref is not None else sum((v.co for v in verts), Vector()) / max(len(verts), 1)
        for f in faces:
            f.normal_update()
            if f.normal.dot(f.calc_center_median() - c) < 0:
                f.normal_flip()

    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-6)
        self.bm.normal_update()
        # smooth everywhere, crisp where the surface folds: seam edges, keels, panel walls, fin edges
        for e in self.bm.edges:
            e.smooth = not (e.is_manifold and len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(34))
        self.bm.to_mesh(me)
        self.bm.free()
        for role in ic.ROLE_ORDER:
            me.materials.append(bpy.data.materials[role])
        for p in me.polygons:
            p.use_smooth = True
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        # a fine 3 mm rolled edge on every crease, so each one catches the light as a thin clean highlight
        bev = obj.modifiers.new("Bevel", "BEVEL")
        bev.width = 0.003
        bev.segments = 2
        bev.limit_method = "ANGLE"
        bev.angle_limit = math.radians(34)
        bev.harden_normals = True
        wn = obj.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
        wn.keep_sharp = True
        return obj


# =============================================================================== limbs
# Width / depth profiles along the bone (t = 0 at the -Z end). Each limb is widest in its bulk half and narrows
# smoothly toward the joint it must fold past, ending in a rounded point.
LIMB_PROF = {
    #            t,   width, depth
    "Thigh":    [(0.0, 0.18, 0.22), (0.1, 0.5, 0.5), (0.4, 0.92, 0.9), (0.76, 1.0, 1.0), (0.94, 0.94, 0.86), (1.0, 0.8, 0.5)],
    "Calf":     [(0.0, 0.72, 0.42), (0.07, 0.92, 0.78), (0.34, 1.0, 1.0), (0.66, 0.86, 0.9), (0.88, 0.5, 0.6), (1.0, 0.14, 0.22)],
    "UpperArm": [(0.0, 0.24, 0.24), (0.14, 0.62, 0.6), (0.52, 1.0, 1.0), (0.9, 0.96, 0.84), (1.0, 0.78, 0.48)],
    "LowerArm": [(0.0, 0.3, 0.28), (0.18, 0.72, 0.64), (0.6, 1.0, 1.0), (0.92, 0.98, 0.9), (1.0, 0.82, 0.5)],
}


def build_limb(kind, front):
    w, L, d = ENV[kind]
    prof = LIMB_PROF[kind]
    p = Part()
    if front:
        s = Surface(w, L, d * 0.95, prof, n=1.25, crease=0.1)
        p.shell(s)
        bulk = (0.42, 0.9) if kind in ("Thigh", "UpperArm") else (0.12, 0.66)
        t0, t1 = bulk
        # layered chevron panel over the bulk: narrows to a point toward the joint the limb folds past
        point_low = kind in ("Thigh", "UpperArm", "LowerArm")
        if point_low:
            p.panel(s, lambda k: -0.12 - 0.58 * ease(k * 1.6), lambda k: 0.12 + 0.58 * ease(k * 1.6), t0, t1, lift=0.012)
        else:
            p.panel(s, lambda k: -0.7 + 0.58 * ease((k - 0.38) * 1.6), lambda k: 0.7 - 0.58 * ease((k - 0.38) * 1.6), t0, t1, lift=0.012)
        # inlaid panel lines tracing the flanks of the layer, a thin emissive slit on the calf / forearm
        for sx in (-1, 1):
            p.line(s, sx * 0.84, sx * 0.62, t0 - 0.06, t1 + 0.02)
        if kind == "Calf":
            p.line(s, -0.18, 0.18, 0.76, 0.76, width=0.008, role="Glow", lift=0.002)
        if kind == "LowerArm":
            p.line(s, -0.2, 0.2, 0.3, 0.3, width=0.007, role="Glow", lift=0.002)
            p.fin(s.point(0.62, 0.55), Vector((0.55, -0.83, 0)).normalized(), Vector((0, 0, -1)), L * 0.5, d * 0.55, L * 0.22, thick=0.014)
    else:
        s = Surface(w, L, d * 0.78, prof, n=1.25, crease=0.06)
        p.shell(s)
        p.panel(s, -0.46, 0.46, 0.26, 0.74, lift=0.01, role="Frame")
        p.line(s, -0.3, 0.3, 0.5, 0.5, width=0.006, role="Dark")
        if kind in ("Thigh", "Calf"):
            # twin swept fins, leaning out from the centre line and raking toward the hip
            t_root = 0.62 if kind == "Thigh" else 0.4
            for sx in (-1, 1):
                root = s.point(sx * 0.3, t_root)
                out = Vector((sx * 0.35, -1, 0)).normalized()
                p.fin(root, out, Vector((0, 0, 1)) if kind == "Calf" else Vector((0, 0, -1)), L * 0.42, d * 0.62, L * 0.2, thick=0.016)
    return p


# =============================================================================== shoulder
def build_shoulder(front):
    w, L, d = ENV["Shoulder"]       # +Z = outward toward the arm
    p = Part()
    prof = [(0.0, 0.74, 0.5), (0.2, 0.96, 0.9), (0.55, 1.0, 1.0), (0.85, 0.84, 0.86), (1.0, 0.42, 0.4)]
    s = Surface(w, L, d * (1.0 if front else 0.8), prof, n=1.25, crease=0.1)
    p.shell(s)
    if front:
        p.panel(s, lambda k: -0.6 + 0.3 * k, lambda k: 0.6 - 0.3 * k, 0.22, 0.86, lift=0.014)
        p.line(s, -0.2, 0.2, 0.14, 0.14, width=0.008, role="Glow", lift=0.002)
        p.fin(s.point(0.0, 0.62), Vector((0, -1, 0)), Vector((0, 0, 1)), L * 0.55, d * 0.5, L * 0.25, thick=0.018)
    else:
        p.panel(s, -0.5, 0.5, 0.3, 0.8, lift=0.01, role="Frame")
    return p


# =============================================================================== torso
BODY_PROF = [(0.0, 0.3, 0.5), (0.16, 0.38, 0.62), (0.36, 0.6, 0.84), (0.58, 0.92, 1.0), (0.84, 1.0, 0.96), (0.96, 0.97, 0.78), (1.0, 0.86, 0.5)]   # V: broad shoulders, tight waist


def build_body_front():
    w, L, d = ENV["Body"]
    p = Part()
    s = Surface(w, L, d, BODY_PROF, n=1.25, crease=0.12, shoulder=0.52)
    p.shell(s)
    # chest: two large swept plates meeting in a chevron that points down the centre line
    for sx in (-1, 1):
        a = lambda k, sx=sx: sx * (0.06 + 0.1 * k)
        b = lambda k, sx=sx: sx * (0.38 + 0.5 * ease(k * 1.4))
        lo, hi = (a, b) if sx > 0 else (b, a)
        p.panel(s, lo, hi, 0.5, 0.92, lift=0.016)
        # abdominal plates: two lapped bands
        for i, t in enumerate((0.3, 0.17)):
            p.panel(s, sx * 0.08 if sx > 0 else -0.62 + i * 0.08, 0.62 - i * 0.08 if sx > 0 else -0.08, t, t + 0.1, lift=0.01 - i * 0.002, role="Frame")
        p.line(s, sx * 0.86, sx * 0.7, 0.1, 0.48)            # flank panel lines
    p.line(s, -0.24, 0.24, 0.47, 0.47, width=0.009, role="Glow", lift=0.002)    # sensor slit under the chevron
    p.panel(s, -0.06, 0.06, 0.62, 0.96, lift=0.006, role="Panel")             # polished spine strip
    return p


def build_body_back():
    w, L, d = ENV["Body"]
    p = Part()
    s = Surface(w, L, d * 0.82, BODY_PROF, n=1.25, crease=0.06)
    p.shell(s)
    p.panel(s, -0.42, 0.42, 0.62, 0.94, lift=0.012, role="Frame")
    for sx in (-1, 1):
        # vectoring thrusters: smooth nacelles lying along the back, nozzles pointing down
        base = s.point(sx * 0.42, 0.44)
        p.pod(base + Vector((0, -0.03, 0)), L * 0.42, w * 0.085, d * 0.36, [(0.0, 0.78), (0.12, 1.0), (0.7, 0.95), (1.0, 0.35)], role="Shell")
        p.pod(base + Vector((0, -0.03, -L * 0.22)), 0.04, w * 0.06, d * 0.27, [(0.0, 1.0), (1.0, 0.9)], role="Dark")
        p.pod(base + Vector((0, -0.03, -L * 0.235)), 0.012, w * 0.04, d * 0.18, [(0.0, 1.0), (1.0, 1.0)], role="Glow")
        # stabiliser fins behind the shoulders
        p.fin(s.point(sx * 0.22, 0.8), Vector((sx * 0.3, -1, 0)).normalized(), Vector((0, 0, -1)), L * 0.3, d * 0.95, L * 0.16, thick=0.02)
    return p


# =============================================================================== head
HEAD_PROF = [(0.0, 0.34, 0.5), (0.25, 0.68, 0.86), (0.6, 1.0, 1.0), (0.88, 0.96, 0.9), (1.0, 0.72, 0.5)]        # wedge: narrow chin, broad brow


def build_head_front():
    w, L, d = ENV["Head"]
    p = Part()
    s = Surface(w, L, d, HEAD_PROF, n=1.25, crease=0.22, shoulder=0.5)
    p.shell(s)
    # brow: a raked plate over the visor
    p.panel(s, lambda k: -0.9 + 0.2 * k, lambda k: 0.9 - 0.2 * k, 0.6, 0.98, lift=0.012)
    p.line(s, -0.66, 0.66, 0.56, 0.56, width=0.012, role="Glow", lift=0.002)             # visor slit
    for sx in (-1, 1):
        p.panel(s, (lambda k: -0.95) if sx < 0 else (lambda k: 0.5 + 0.2 * k), (lambda k: -0.5 - 0.2 * k) if sx < 0 else (lambda k: 0.95), 0.12, 0.48, lift=0.01)   # cheek guards
    p.fin(s.point(0, 0.86), Vector((0, -1, 0)), Vector((0, 0, 1)), L * 0.5, d * 0.7, L * 0.3, thick=0.014)                # crest
    return p


def build_head_back():
    w, L, d = ENV["Head"]
    p = Part()
    s = Surface(w, L, d * 0.86, HEAD_PROF, n=1.25, crease=0.08)
    p.shell(s)
    p.panel(s, -0.4, 0.4, 0.2, 0.7, lift=0.01, role="Frame")
    for sx in (-1, 1):
        p.fin(s.point(sx * 0.5, 0.7), Vector((sx * 0.5, -1, 0)).normalized(), Vector((0, 0, 1)), L * 0.42, d * 0.8, L * 0.34, thick=0.012)
    return p


# =============================================================================== hand
def build_hand(front):
    w, L, d = ENV["Hand"]
    p = Part()
    s = Surface(w, L, d * (1.0 if front else 0.8), [(0.0, 0.8, 0.6), (0.4, 1.0, 1.0), (1.0, 0.62, 0.45)], n=1.25, crease=0.06)
    p.shell(s)
    if front:
        for i in (-1, 0, 1):
            c = s.point(i * 0.56, 0.72)
            p.pod(c + Vector((0, 0.01, 0)), L * 0.42, w * 0.1, d * 0.3, [(0.0, 1.0), (0.7, 0.92), (1.0, 0.4)], role="Frame")
    else:
        p.panel(s, -0.5, 0.5, 0.25, 0.75, lift=0.008, role="Frame")
    return p


# =============================================================================== feet (lengthwise, true size)
def build_foot(toe):
    p = Part()
    if toe:
        W, L, H = 0.50, 0.48, 0.22
        prof = [(0.0, 0.96, 0.62), (0.12, 1.0, 1.0), (0.5, 0.94, 0.86), (0.82, 0.66, 0.52), (1.0, 0.18, 0.16)]
        s = Surface(W, L, H, prof, n=1.25, crease=0.1, skirt=0.3)
        p.shell(s)
        p.panel(s, lambda k: -0.7 + 0.5 * k, lambda k: 0.7 - 0.5 * k, 0.2, 0.86, lift=0.01)
        p.line(s, -0.5, 0.5, 0.16, 0.16)
    else:
        W, L, H = 0.46, 0.28, 0.16
        prof = [(0.0, 0.96, 0.66), (0.18, 1.0, 1.0), (0.7, 0.8, 0.72), (1.0, 0.36, 0.3)]
        s = Surface(W, L, H, prof, n=1.25, crease=0.08, skirt=0.3)
        p.shell(s)
        p.panel(s, -0.5, 0.5, 0.2, 0.7, lift=0.008, role="Frame")
    return p


# =============================================================================== export
def parts():
    out = {}
    for kind in LIMB_PROF:
        out[kind + "Front"] = build_limb(kind, True)
        out[kind + "Back"] = build_limb(kind, False)
    out["ShoulderFront"], out["ShoulderBack"] = build_shoulder(True), build_shoulder(False)
    out["BodyFront"], out["BodyBack"] = build_body_front(), build_body_back()
    out["HeadFront"], out["HeadBack"] = build_head_front(), build_head_back()
    out["HandFront"], out["HandBack"] = build_hand(True), build_hand(False)
    out["FootFront"], out["FootBack"] = build_foot(True), build_foot(False)
    return out


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", add_leaf_bones=False, bake_anim=False, path_mode="AUTO")


if __name__ == "__main__":
    ic.reset()
    made = 0
    for slot, part in parts().items():
        obj = part.to_object(f"{slot}_{TAG}")
        if "--no-export" not in sys.argv:
            export(obj, os.path.join(OUT_DIR, f"{slot}_{TAG}.fbx"))
        made += 1
    if "--blend" in sys.argv:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "sleek_ironclad.blend"))
    print("BUILT", made)
