"""
IronFauna - every clamshell set (01-10) in Blender: limbs, body, head, hands and feet.
(Library for aero_sets.py, which builds the current parts.)

  blender -b -P clamshell_sets.py -- --out <dir> [--sets 2,3] [--blend]
  python clamshell_sets.py --out <dir>            (with the pip `bpy` module)

Plate parts follow the Ironclad convention (see ironclad.py): X = width, Z = along the bone,
plate on the -Y side with its inner face at Y = 0. Unity fits them into the slot envelope.

Lengthwise parts (feet) are authored at true size in metres:
X = width (centred), Z = length (centred, +Z = the free tip: toe, heel),
height grows toward -Y from Y = 0 (the bottom). Unity scales them uniformly from their length.
Keep every detail inside the Z range of the base shell.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ironclad as ic  # noqa: E402
from ironclad import ENV, Part  # noqa: E402

# Extra colour role for Voltaic coils.
if "Copper" not in ic.ROLE_COLORS:
    ic.ROLE_COLORS["Copper"] = (0.58, 0.16, 0.045, 1)
    ic.ROLE_ORDER.append("Copper")


def argval(flag, default=None):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv
    return argv[argv.index(flag) + 1] if flag in argv else default


OUT_DIR = argval("--out", "/tmp/clamshell_sets")
os.makedirs(OUT_DIR, exist_ok=True)

SET_NAMES = ["Ironclad", "Bastion", "Overclock", "Ripjaw", "Afterburn",
             "Voltaic", "Salvo", "Goliath", "Mantis", "Aegis"]


# =============================================================================== extra primitives
def ramp(p, w, L, h, base, role, x0=0.0, z0=0.0, lo=0.25, rising=1):
    """Block whose height ramps from lo*h to h along +Z (rising=1) or -Z (rising=-1)."""
    sec = [(-w / 2, 0), (w / 2, 0), (w / 2, h * 0.88), (w / 2 - min(w, h) * 0.12, h),
           (-w / 2 + min(w, h) * 0.12, h), (-w / 2, h * 0.88)]
    a, b = (lo, 1.0) if rising > 0 else (1.0, lo)
    return p.loft(sec, [(-L / 2, 1, a), (L / 2, 1, b)], role, offset=(x0, -base, z0))


def facet(p, w, L, h, base, role, x0=0.0, z0=0.0, tip=0.35):
    """Faceted stealth prism: diamond section with pointed ends."""
    sec = [(-w / 2, 0), (w / 2, 0), (w * 0.18, h), (-w * 0.18, h)]
    return p.loft(sec, [(-L / 2, tip, tip), (-L * 0.2, 1, 1), (L * 0.2, 1, 1), (L / 2, tip, tip)], role, offset=(x0, -base, z0))


def fin(p, t, L, h, base, role, x0=0.0, z0=0.0, rake=0.25, rising=1):
    """Thin upright blade, tall at +Z (rising=1) and raked down toward the other end."""
    sec = [(-t / 2, 0), (t / 2, 0), (t / 2, h), (-t / 2, h)]
    a, b = (rake, 1.0) if rising > 0 else (1.0, rake)
    return p.loft(sec, [(-L / 2, 1, a), (L / 2, 0.8, b)], role, offset=(x0, -base, z0))


def spike(p, r, h, base, role, x0=0.0, z0=0.0, lean=0.0, sides=4, tip=0.12, lean_x=0.0):
    """Tapered horn/ear standing outward (-Y), leaning lean (Z) and lean_x (X) at the tip."""
    rings = []
    for k, (d, s) in enumerate(((0.0, 1.0), (h * 0.55, 0.62), (h, tip))):
        ox, oz = x0 + lean_x * d / max(h, 1e-6), z0 + lean * d / max(h, 1e-6)
        ring = []
        for i in range(sides):
            a = (i + 0.5) / sides * 2 * math.pi
            ring.append(p.bm.verts.new((ox + math.cos(a) * r * s, -(base + d), oz + math.sin(a) * r * s)))
        rings.append(ring)
    mi = ic.ROLE_ORDER.index(role)
    faces = []
    for ra, rb in zip(rings, rings[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(p.bm.faces.new([ra[i], ra[j], rb[j], rb[i]]))
    faces.append(p.bm.faces.new(rings[0]))
    faces.append(p.bm.faces.new(rings[-1]))
    verts = {v for f in faces for v in f.verts}
    c = sum((v.co for v in verts), Vector()) / len(verts)
    for f in faces:
        f.material_index = mi
        f.normal_update()
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()
    return faces


def slats(p, w, L, h, base, n, z0, role_a="Dark", role_b=None, x0=0.0):
    """n thin bars across the plate spread over L."""
    for i in range(n):
        z = z0 - L / 2 + (i + 0.5) * L / n
        role = role_a if role_b is None or i % 2 == 0 else role_b
        p.block(w, L / n * 0.42, h, base, role, ch=min(h, L / n) * 0.15, x0=x0, z0=z)


# =============================================================================== part context

class Ctx:
    """w/L/d: plate size; zc/zl: detail band centre/length along the bone; hb: detail height budget."""

    def __init__(self, kind, w, L, d, zc=0.0, zl=None, hb=None, ww=None):
        self.kind, self.w, self.L, self.d = kind, w, L, d
        self.zc, self.zl = zc, zl if zl is not None else L * 0.66
        self.hb = hb if hb is not None else d * 0.5
        self.ww = ww if ww is not None else w * 0.74


def limb_ctx(kind, front):
    w, L, d = ENV[kind]
    if kind == "Thigh":
        return Ctx(kind, w, L, d, zc=L * 0.16, zl=L * 0.5, hb=d * (0.34 if front else 0.28))
    if kind == "Calf":
        return Ctx(kind, w, L, d, zc=-L * 0.04, zl=L * 0.46, hb=d * (0.4 if front else 0.28))
    if kind == "Shoulder":
        return Ctx(kind, w, L, d, zl=L * 0.72, hb=d * 0.55)
    return Ctx(kind, w, L, d, zl=L * 0.66, hb=d * 0.55)


# =============================================================================== base set style

class SetStyle:
    idx = 1
    ch = 0.4          # plate edge chamfer (fraction of min(w/2, d))
    bulge = 1.06      # plate crown
    end_w = 0.55      # plate end taper (width)
    layer_frac = 0.55 # share of the height budget used by the raised second layer
    eye = "visor"

    @property
    def name(self):
        return SET_NAMES[self.idx - 1]

    # ---------------------------------------------------------------- shared pieces
    def plate(self, p, c, bevel=None, end_w=None):
        p.plate(c.w, c.L, c.d, 0.0, bevel=bevel, end_w=end_w or self.end_w, bulge=self.bulge,
                ch=min(c.w * 0.5, c.d) * self.ch)

    def layer(self, p, c, frac=None):
        h = c.hb * (frac if frac is not None else self.layer_frac)
        p.plate(c.ww, c.zl, h, c.d * 0.98, bevel=min(h * 0.85, c.zl * 0.25), end_w=0.7, z0=c.zc,
                bulge=1.0, ch=min(c.ww * 0.5, h) * self.ch)
        return c.d * 0.98 + h

    # ---------------------------------------------------------------- signatures (override)
    def front_sig(self, p, c, top, rem):
        """Signature detail on the front plate, inside the band, at most rem tall above top."""
        p.block(c.ww * 0.12, c.zl * 0.8, rem * 0.5, top - 0.002, "Panel", ch=0.004, x0=c.ww * 0.3, z0=c.zc)

    def back_sig(self, p, c, base):
        p.block(c.ww * 0.84, c.zl * 0.6, c.hb * 0.3, base, "Frame", ch=0.006, z0=c.zc)
        slats(p, c.ww * 0.72, c.zl * 0.5, c.hb * 0.45, base, 5, c.zc)

    # ---------------------------------------------------------------- slots
    def limb_front(self, p, c):
        self.plate(p, c)
        top = self.layer(p, c)
        self.front_sig(p, c, top, c.d + c.hb - top)
        p.bolts([(-c.w * 0.36, c.zc + c.zl * 0.45), (c.w * 0.36, c.zc + c.zl * 0.45),
                 (-c.w * 0.36, c.zc - c.zl * 0.45), (c.w * 0.36, c.zc - c.zl * 0.45)], c.d * 1.0, r=c.w * 0.03)
        p.latch_tabs(c.w, c.L)

    def limb_back(self, p, c):
        self.plate(p, c)
        self.back_sig(p, c, c.d * 0.98)
        p.latch_tabs(c.w, c.L)

    def body_front(self, p):
        w, L, d = ENV["Body"]
        c = Ctx("Body", w, L, d)
        self.plate(p, c, bevel=d * 0.7, end_w=0.75)
        for sx in (-1, 1):  # pectoral plates
            p.plate(w * 0.36, L * 0.42, d * 0.26, d * 1.0, bevel=d * 0.2, end_w=0.7, x0=sx * w * 0.25, z0=L * 0.16,
                    ch=min(w * 0.18, d * 0.26) * self.ch, bulge=1.0)
        cs = Ctx("Body", w, L, d, zc=L * 0.06, zl=L * 0.5, hb=d * 0.5, ww=w * 0.26)
        self.front_sig(p, cs, d * 1.0, d * 0.42)
        self.cockpit(p, w, L, d)
        for i in range(3):  # abdominal plates
            p.block(w * (0.54 - i * 0.07), L * 0.07, d * 0.18, d * 1.0, "Shell", ch=0.01, z0=-L * 0.27 - i * L * 0.075)
        p.bolts([(sx * w * 0.42, sz * L * 0.36) for sx in (-1, 1) for sz in (-1, 1)], d * 1.0, r=0.024)
        p.latch_tabs(w, L)

    def cockpit(self, p, w, L, d):
        p.block(w * 0.34, L * 0.035, d * 0.06, d * 1.25, "Glow", ch=0.003, z0=L * 0.38)

    def body_back(self, p):
        w, L, d = ENV["Body"]
        c = Ctx("Body", w, L, d, zc=L * 0.08, zl=L * 0.56, hb=d * 0.55, ww=w * 0.62)
        self.plate(p, c, bevel=d * 0.7, end_w=0.75)
        p.block(w * 0.7, L * 0.62, d * 0.14, d * 0.98, "Frame", ch=0.012, z0=L * 0.06)
        self.back_sig(p, c, d * 1.1)
        p.hazard_band(w * 0.7, L * 0.07, d * 0.1, d * 0.98, z0=-L * 0.34, n=7)
        p.latch_tabs(w, L)

    def shoulder_front(self, p):
        c = limb_ctx("Shoulder", True)
        self.plate(p, c, bevel=c.d * 0.55, end_w=0.8)
        top = self.layer(p, c, 0.5)
        self.front_sig(p, c, top, c.d + c.hb - top)
        p.latch_tabs(c.w, c.L)

    def shoulder_back(self, p):
        c = limb_ctx("Shoulder", False)
        self.plate(p, c, bevel=c.d * 0.55, end_w=0.8)
        self.back_sig(p, c, c.d * 0.98)
        p.latch_tabs(c.w, c.L)

    def hand_front(self, p):
        w, L, d = ENV["Hand"]
        c = Ctx("Hand", w, L, d, zc=-L * 0.12, zl=L * 0.42, hb=d * 0.4)
        self.plate(p, c, bevel=d * 0.5, end_w=0.8)
        for i in range(4):  # finger guards toward the tip (+Z)
            p.block(w * 0.18, L * 0.34, d * 0.42, d * 0.95, "Frame", ch=0.008, x0=-w * 0.33 + i * w * 0.22, z0=L * 0.28)
        self.front_sig(p, c, d * 0.98, d * 0.4)

    def hand_back(self, p):
        w, L, d = ENV["Hand"]
        c = Ctx("Hand", w, L, d, zc=0.0, zl=L * 0.6, hb=d * 0.4)
        self.plate(p, c, bevel=d * 0.5, end_w=0.8)
        self.back_sig(p, c, d * 0.98)

    # ---------------------------------------------------------------- head (humanoid)
    def head_base(self, p):
        w, L, d = ENV["Head"]
        c = Ctx("Head", w, L, d)
        self.plate(p, c, bevel=d * 0.6, end_w=0.8)
        return w, L, d

    def head_front(self, p):
        w, L, d = self.head_base(p)
        p.block(w * 0.9, L * 0.12, d * 0.5, d * 0.95, "Shell", ch=0.012, z0=L * 0.28)  # brow
        self.eyes(p, w, L, d)
        for sx in (-1, 1):  # cheeks
            p.block(w * 0.2, L * 0.26, d * 0.42, d * 0.98, "Shell", ch=0.01, x0=sx * w * 0.32, z0=-L * 0.06)
        p.block(w * 0.36, L * 0.12, d * 0.36, d * 0.98, "Frame", ch=0.01, z0=-L * 0.3)  # chin
        p.latch_tabs(w, L)

    def eyes(self, p, w, L, d):
        p.block(w * 0.66, L * 0.09, d * 0.12, d * 1.0, "Glow", ch=0.004, z0=L * 0.14)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        p.wedge_ridge(w * 0.16, L * 0.8, d * 0.55, d * 1.0, "Shell", z0=L * 0.05, start=0.4)
        p.latch_tabs(w, L)

    # ---------------------------------------------------------------- feet (lengthwise)
    TOE = (0.50, 0.48, 0.22)
    HEEL = (0.46, 0.28, 0.16)

    def foot_shell(self, p, W, L, H, toe):
        ch = min(W, H) * 0.22
        sec = [(-W / 2, 0), (W / 2, 0), (W / 2, H - ch), (W / 2 - ch, H), (-W / 2 + ch, H), (-W / 2, H - ch)]
        seam, tip = (0.10, 0.12) if toe else (0.06, 0.08)
        st = [(-L / 2, 1.0, 0.6), (-L / 2 + seam, 1.0, 1.0), (L / 2 - tip, 1.0, 1.0), (L / 2, 0.62 if toe else 0.8, 0.5 if toe else 0.7)]
        p.loft(sec, st, "Shell")
        p.block(W * 1.02, L * 0.92, 0.03, 0.0, "Dark", ch=0.008, z0=0.0)  # sole band
        return Ctx("Foot", W, L, H, zc=L * 0.07 if toe else -L * 0.05, zl=L * 0.3 if toe else L * 0.3, hb=0.05, ww=W * 0.62)

    def foot_toe(self, p):
        W, L, H = self.TOE
        c = self.foot_shell(p, W, L, H, True)
        # Kept low: in the forward-chicken stance the calf front passes about 0.05 above the toe top.
        p.plate(c.ww, c.zl, 0.014, H * 0.98, bevel=0.012, end_w=0.75, z0=c.zc, bulge=1.0)
        self.front_sig(p, c, H + 0.01, 0.018)
        self.toe_tip(p, W, L, H)

    def toe_tip(self, p, W, L, H):
        p.block(W * 0.7, L * 0.06, 0.04, 0.0, "Frame", ch=0.008, z0=L * 0.44)

    def foot_heel(self, p):
        W, L, H = self.HEEL
        c = self.foot_shell(p, W, L, H, False)
        p.hazard_band(c.ww, c.zl * 0.5, 0.02, H * 0.98, z0=c.zc, n=5)

    # ---------------------------------------------------------------- lengthwise shell (feet)

    @staticmethod
    def lw_shell(p, W, L, H, role="Shell", tip_len=0.22, tip=0.55, seam=1.0, tip_x=None, ch_frac=0.18):
        """Full lengthwise shell, section scaled about its centre (matches LengthwiseHalf)."""
        ch = min(W, H) * ch_frac
        hw, hh = W / 2, H / 2
        sec = [(-hw + ch, -hh), (hw - ch, -hh), (hw, -hh + ch), (hw, hh - ch), (hw - ch, hh), (-hw + ch, hh), (-hw, hh - ch), (-hw, -hh + ch)]
        tx = tip if tip_x is None else tip_x
        st = [(-L / 2, seam, seam), (L / 2 - tip_len, 1.0, 1.0), (L / 2, tx, tip)]
        return p.loft(sec, st, role, offset=(0, -hh, 0))

# =============================================================================== sets

class Bastion(SetStyle):
    """ERA reactive bricks / reserve ammo drum + recoil anchor."""
    idx, ch, bulge, end_w, layer_frac = 2, 0.24, 1.0, 0.7, 0.45

    def front_sig(self, p, c, top, rem):
        rows = max(2, min(4, int(round(c.zl / (c.ww * 0.34)))))
        cols = 2 if c.ww > 0.2 else 1
        bw, bl = c.ww / cols * 0.86, c.zl / rows * 0.84
        p.block(c.ww * 0.96, c.zl * 0.98, rem * 0.18, top - 0.002, "Dark", ch=0.004, z0=c.zc)
        for i in range(rows):
            for j in range(cols):
                x = (j - (cols - 1) / 2) * c.ww / cols
                z = c.zc - c.zl / 2 + (i + 0.5) * c.zl / rows
                p.block(bw, bl, rem * 0.9, top, "Shell", ch=min(bw, rem) * 0.18, x0=x, z0=z)
                p.prism(min(bw, bl) * 0.08, rem * 0.1, top + rem * 0.9, "Dark", axis="y", x0=x, z0=z)

    def back_sig(self, p, c, base):
        r = min(c.hb * 0.48, c.zl * 0.22)
        p.prism(r, c.ww * 0.78, base, "Frame", axis="x", sides=10, z0=c.zc + c.zl * 0.12)
        for sx in (-1, 1):
            p.prism(r * 1.06, c.ww * 0.06, base - r * 0.06, "Hazard", axis="x", sides=10, x0=sx * c.ww * 0.36, z0=c.zc + c.zl * 0.12)
        p.block(c.ww * 0.5, c.zl * 0.22, c.hb * 0.5, base, "Dark", ch=0.008, z0=c.zc - c.zl * 0.32)

    def eyes(self, p, w, L, d):  # Grenadier: T visor
        p.block(w * 0.7, L * 0.08, d * 0.12, d * 1.0, "Glow", ch=0.004, z0=L * 0.15)
        p.block(w * 0.08, L * 0.3, d * 0.1, d * 1.0, "Glow", ch=0.004, z0=-L * 0.02)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        p.block(w * 0.7, L * 0.5, d * 0.35, d * 0.98, "Frame", ch=0.01, z0=0.0)
        slats(p, w * 0.6, L * 0.4, d * 0.5, d * 0.98, 4, 0.0)

    def toe_tip(self, p, W, L, H):
        p.block(W * 0.86, L * 0.1, H * 0.5, 0.0, "Frame", ch=0.01, z0=L * 0.4)


class Overclock(SetStyle):
    """Cut-out cyan conduit frames / angled heat-sink louvers."""
    idx, ch, end_w = 3, 0.5, 0.5

    def front_sig(self, p, c, top, rem):
        for sx in (-1, 1):
            p.block(c.ww * 0.14, c.zl, rem, top - 0.002, "Frame", ch=0.004, x0=sx * c.ww * 0.36, z0=c.zc)
        for k in (-0.4, 0.0, 0.4):
            p.block(c.ww * 0.82, c.zl * 0.06, rem * 0.55, top - 0.002, "Frame", ch=0.003, z0=c.zc + k * c.zl)
        p.block(c.ww * 0.16, c.zl * 0.88, rem * 0.4, top - 0.002, "Glow", ch=0.003, z0=c.zc)

    def back_sig(self, p, c, base):
        p.block(c.ww, c.zl, c.hb * 0.25, base, "Dark", ch=0.006, z0=c.zc)
        n = 5
        for i in range(n):
            z = c.zc - c.zl / 2 + (i + 0.5) * c.zl / n
            ramp(p, c.ww * 0.9, c.zl / n * 0.8, c.hb * 0.7, base + c.hb * 0.2, "Frame", z0=z, lo=0.2)
        p.block(c.ww * 0.1, c.zl * 0.9, c.hb * 0.3, base + c.hb * 0.2, "Glow", ch=0.003, x0=c.ww * 0.47, z0=c.zc)

    def eyes(self, p, w, L, d):  # Vanguard-Fin: twin eyes + V crest
        for sx in (-1, 1):
            p.block(w * 0.2, L * 0.07, d * 0.12, d * 1.0, "Glow", ch=0.004, x0=sx * w * 0.14, z0=L * 0.14)
            fin(p, 0.02, L * 0.3, d * 0.8, d * 1.4, "Hazard", x0=sx * w * 0.16, z0=L * 0.36, rake=0.3)
        p.block(w * 0.14, L * 0.08, d * 0.3, d * 1.4, "Glow", ch=0.004, z0=L * 0.3)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        for k in range(4):
            ramp(p, w * 0.6, L * 0.14, d * 0.5, d * 0.98, "Frame", z0=L * (0.25 - k * 0.17), lo=0.2)

    def toe_tip(self, p, W, L, H):
        p.block(W * 0.5, L * 0.05, 0.03, H * 0.42, "Glow", ch=0.004, z0=L * 0.46)


class Ripjaw(SetStyle):
    """Serrated cleats and saw teeth / twin micro-thrusters."""
    idx, ch, end_w, bulge = 4, 0.45, 0.4, 1.08

    def front_sig(self, p, c, top, rem):
        p.wedge_ridge(c.ww * 0.32, c.zl, rem * 0.6, top - 0.002, "Shell", z0=c.zc)
        n = 5
        for sx in (-1, 1):
            for i in range(n):
                z = c.zc - c.zl / 2 + (i + 0.5) * c.zl / n
                ramp(p, c.ww * 0.08, c.zl / n * 0.95, rem * 0.9, top - 0.002, "Frame", x0=sx * c.ww * 0.42, z0=z, lo=0.1, rising=-1)

    def back_sig(self, p, c, base):
        r = min(c.ww * 0.17, c.zl * 0.2)
        for sx in (-1, 1):
            p.prism(r, c.hb * 0.55, base, "Frame", axis="y", sides=8, x0=sx * c.ww * 0.24, z0=c.zc)
            p.prism(r * 0.7, c.hb * 0.6, base, "Dark", axis="y", sides=8, x0=sx * c.ww * 0.24, z0=c.zc)
            p.prism(r * 1.12, c.hb * 0.12, base, "Glow", axis="y", sides=8, x0=sx * c.ww * 0.24, z0=c.zc)
        p.hazard_band(c.ww * 0.7, c.zl * 0.12, c.hb * 0.15, base, z0=c.zc - c.zl * 0.4, n=5)

    def eyes(self, p, w, L, d):  # Shade-Eye: mono-eye on a dark rail
        p.block(w * 0.72, L * 0.1, d * 0.1, d * 1.0, "Dark", ch=0.004, z0=L * 0.14)
        p.prism(L * 0.05, d * 0.14, d * 1.0, "Glow", axis="y", sides=8, x0=w * 0.12, z0=L * 0.14)
        for i in range(5):  # jaw teeth
            ramp(p, w * 0.05, L * 0.1, d * 0.3, d * 1.3, "Panel", x0=-w * 0.12 + i * w * 0.06, z0=-L * 0.3, lo=0.2, rising=-1)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        for i in range(3):
            fin(p, 0.02, L * 0.5, d * (0.7 - i * 0.15), d * 0.98, "Shell", x0=(i - 1) * w * 0.16, z0=0.0, rake=0.2)

    def toe_tip(self, p, W, L, H):  # three claws
        for i in (-1, 0, 1):
            ramp(p, W * 0.12, L * 0.22, H * 0.45, 0.0, "Frame", x0=i * W * 0.3, z0=L * 0.38, lo=0.35, rising=-1)


class Afterburn(SetStyle):
    """Ram-air intake scoops / vectored rectangular nozzles."""
    idx, bulge, end_w = 5, 1.1, 0.5

    def front_sig(self, p, c, top, rem):
        for sx in (-1, 1):
            ramp(p, c.ww * 0.4, c.zl * 0.85, rem * 0.9, top - 0.002, "Shell", x0=sx * c.ww * 0.27, z0=c.zc, lo=0.15)
            p.block(c.ww * 0.3, c.zl * 0.06, rem * 0.7, top + rem * 0.05, "Dark", ch=0.004, x0=sx * c.ww * 0.27, z0=c.zc + c.zl * 0.38)

    def back_sig(self, p, c, base):
        for sx in (-1, 1):
            p.block(c.ww * 0.38, c.zl * 0.36, c.hb * 0.6, base, "Frame", ch=0.008, x0=sx * c.ww * 0.23, z0=c.zc - c.zl * 0.18)
            p.block(c.ww * 0.26, c.zl * 0.26, c.hb * 0.64, base, "Dark", ch=0.004, x0=sx * c.ww * 0.23, z0=c.zc - c.zl * 0.18)
        p.hazard_band(c.ww * 0.8, c.zl * 0.14, c.hb * 0.2, base, z0=c.zc + c.zl * 0.3, n=5)

    def eyes(self, p, w, L, d):  # Recon-Scope: narrow visor + side scope
        p.block(w * 0.5, L * 0.06, d * 0.1, d * 1.0, "Glow", ch=0.004, z0=L * 0.15)
        p.prism(L * 0.07, L * 0.3, d * 1.0, "Frame", axis="z", sides=8, x0=w * 0.38, z0=L * 0.16)
        p.prism(L * 0.045, L * 0.04, d * 1.05, "Glow", axis="z", sides=8, x0=w * 0.38, z0=L * 0.32)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        for sx in (-1, 1):
            ramp(p, w * 0.28, L * 0.6, d * 0.45, d * 0.98, "Shell", x0=sx * w * 0.2, z0=0.0, lo=0.2)
            p.block(w * 0.2, L * 0.05, d * 0.3, d * 1.0, "Dark", ch=0.004, x0=sx * w * 0.2, z0=L * 0.26)

    def toe_tip(self, p, W, L, H):
        for sx in (-1, 1):
            ramp(p, W * 0.3, L * 0.3, H * 0.3, H * 0.7, "Shell", x0=sx * W * 0.22, z0=L * 0.15, lo=0.1, rising=-1)


class Voltaic(SetStyle):
    """Copper Tesla coils and arc nodes / capacitor cell banks."""
    idx = 6

    def front_sig(self, p, c, top, rem):
        r = min(rem * 0.5, c.ww * 0.2)
        n = 6
        for i in range(n):
            z = c.zc - c.zl * 0.4 + i * c.zl * 0.8 / (n - 1)
            p.prism(r if i % 2 == 0 else r * 0.78, c.zl * 0.8 / n * 0.9, top - 0.002, "Copper" if i % 2 == 0 else "Dark",
                    axis="z", sides=10, z0=z)
        for sx in (-1, 1):
            for sz in (-1, 1):
                p.prism(c.ww * 0.05, rem * 0.6, top - 0.002, "Glow", axis="y", sides=6, x0=sx * c.ww * 0.38, z0=c.zc + sz * c.zl * 0.38)

    def back_sig(self, p, c, base):
        p.block(c.ww * 0.9, c.zl * 0.8, c.hb * 0.2, base, "Frame", ch=0.006, z0=c.zc)
        r = min(c.ww * 0.1, c.zl * 0.1)
        for i in range(3):
            for sx in (-1, 1):
                x, z = sx * c.ww * 0.2, c.zc - c.zl * 0.26 + i * c.zl * 0.26
                p.prism(r, c.hb * 0.55, base + c.hb * 0.18, "Copper", axis="y", sides=10, x0=x, z0=z)
                p.prism(r * 0.6, c.hb * 0.1, base + c.hb * 0.7, "Glow", axis="y", sides=10, x0=x, z0=z)

    def eyes(self, p, w, L, d):  # Gungnir-Arc: visor + copper antenna spikes
        p.block(w * 0.62, L * 0.08, d * 0.12, d * 1.0, "Glow", ch=0.004, z0=L * 0.14)
        for sx in (-1, 1):
            spike(p, 0.02, d * 1.6, d * 1.4, "Copper", x0=sx * w * 0.3, z0=L * 0.34, lean=0.08, sides=6)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        for i in range(4):
            p.prism(w * 0.14 if i % 2 == 0 else w * 0.11, L * 0.12, d * 0.98, "Copper" if i % 2 == 0 else "Dark", axis="z", sides=10, z0=L * (0.22 - i * 0.12))

    def toe_tip(self, p, W, L, H):
        p.prism(H * 0.18, W * 0.7, H * 0.25, "Copper", axis="x", sides=10, z0=L * 0.36)


class Salvo(SetStyle):
    """Flush micro-missile doors / chaff and flare dispensers."""
    idx, ch = 7, 0.3

    def front_sig(self, p, c, top, rem):
        p.block(c.ww * 0.9, c.zl * 0.86, rem * 0.5, top - 0.002, "Frame", ch=0.006, z0=c.zc)
        rows, cols = 3, 2
        for i in range(rows):
            for j in range(cols):
                x = (j - 0.5) * c.ww * 0.42
                z = c.zc - c.zl * 0.28 + i * c.zl * 0.28
                p.block(c.ww * 0.36, c.zl * 0.22, rem * 0.2, top + rem * 0.45, "Panel", ch=0.003, x0=x, z0=z)
                p.block(c.ww * 0.1, c.zl * 0.04, rem * 0.12, top + rem * 0.62, "Dark", ch=0.002, x0=x, z0=z)

    def back_sig(self, p, c, base):
        r = min(c.ww * 0.11, c.zl * 0.12)
        for i in range(3):
            x = (i - 1) * c.ww * 0.28
            p.prism(r, c.hb * 0.6, base, "Frame", axis="y", sides=8, x0=x, z0=c.zc + c.zl * 0.1)
            p.prism(r * 0.66, c.hb * 0.64, base, "Dark", axis="y", sides=8, x0=x, z0=c.zc + c.zl * 0.1)
        p.hazard_band(c.ww * 0.8, c.zl * 0.14, c.hb * 0.2, base, z0=c.zc - c.zl * 0.3, n=5)

    def eyes(self, p, w, L, d):  # Commando-Vulcan: grille face, visor, temple vulcans
        p.block(w * 0.6, L * 0.07, d * 0.12, d * 1.0, "Glow", ch=0.004, z0=L * 0.15)
        slats(p, w * 0.08, L * 0.24, d * 0.2, d * 1.0, 4, -L * 0.05)
        for sx in (-1, 1):
            p.prism(L * 0.035, L * 0.3, d * 1.3, "Frame", axis="z", sides=8, x0=sx * w * 0.42, z0=L * 0.2)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        for i in range(2):
            p.prism(w * 0.08, d * 0.5, d * 0.98, "Frame", axis="y", sides=8, x0=(i - 0.5) * w * 0.3, z0=0.05)
        p.hazard_band(w * 0.5, L * 0.08, d * 0.08, d * 0.98, z0=-L * 0.3)

    def toe_tip(self, p, W, L, H):
        p.block(W * 0.4, L * 0.12, H * 0.2, H * 0.6, "Panel", ch=0.004, z0=L * 0.28)


class Goliath(SetStyle):
    """Pneumatic pile-driver rams / counterweight ballast tanks."""
    idx, ch, bulge, end_w, layer_frac = 8, 0.3, 1.0, 0.75, 0.4

    def front_sig(self, p, c, top, rem):
        r = min(rem * 0.5, c.ww * 0.22)
        p.prism(r, c.zl * 0.75, top - 0.002, "Frame", axis="z", sides=10, z0=c.zc + c.zl * 0.08)
        p.prism(r * 1.18, c.zl * 0.1, top - 0.002 - r * 0.18, "Hazard", axis="z", sides=10, z0=c.zc + c.zl * 0.38)
        p.block(c.ww * 0.56, c.zl * 0.16, rem * 0.98, top - 0.002, "Dark", ch=0.01, z0=c.zc - c.zl * 0.4)
        for sx in (-1, 1):
            p.block(c.ww * 0.16, c.zl * 0.8, rem * 0.45, top - 0.002, "Shell", ch=0.008, x0=sx * c.ww * 0.42, z0=c.zc)

    def back_sig(self, p, c, base):
        r = min(c.hb * 0.45, c.ww * 0.2)
        for sx in (-1, 1):
            p.prism(r, c.zl * 0.85, base, "Shell", axis="z", sides=10, x0=sx * c.ww * 0.25, z0=c.zc)
        for k in (-0.25, 0.25):
            p.block(c.ww * 0.95, c.zl * 0.07, r * 2.05, base - 0.004, "Frame", ch=0.004, z0=c.zc + k * c.zl)

    def eyes(self, p, w, L, d):  # Juggernaut-EOD: slab brow, slit visor
        p.block(w * 1.0, L * 0.16, d * 0.7, d * 0.95, "Shell", ch=0.014, z0=L * 0.3)
        p.block(w * 0.5, L * 0.035, d * 0.1, d * 1.0, "Glow", ch=0.003, z0=L * 0.16)
        p.block(w * 0.62, L * 0.14, d * 0.4, d * 0.98, "Frame", ch=0.01, z0=-L * 0.24)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        p.prism(w * 0.18, L * 0.6, d * 0.98, "Frame", axis="z", sides=10, z0=0.0)
        for k in (-0.18, 0.18):
            p.block(w * 0.5, L * 0.06, w * 0.38, d * 0.96, "Dark", ch=0.004, z0=k * L)

    def toe_tip(self, p, W, L, H):
        p.block(W * 0.96, L * 0.18, H * 0.62, 0.0, "Frame", ch=0.014, z0=L * 0.36)


class Mantis(SetStyle):
    """Faceted radar-deflecting prisms / baffled ECM shrouds."""
    idx, ch, end_w, bulge = 9, 0.62, 0.42, 1.0

    def front_sig(self, p, c, top, rem):
        facet(p, c.ww * 0.6, c.zl, rem * 0.95, top - 0.002, "Shell", z0=c.zc)
        for sx in (-1, 1):
            facet(p, c.ww * 0.26, c.zl * 0.6, rem * 0.55, top - 0.002, "Frame", x0=sx * c.ww * 0.4, z0=c.zc + c.zl * 0.1)

    def back_sig(self, p, c, base):
        for i in range(4):
            p.block(c.ww * (0.94 - i * 0.16), c.zl * (0.85 - i * 0.12), c.hb * 0.16, base + i * c.hb * 0.16,
                    "Dark" if i % 2 == 0 else "Frame", ch=0.004, z0=c.zc)
        p.block(c.ww * 0.14, c.zl * 0.1, c.hb * 0.15, base + c.hb * 0.64, "Glow", ch=0.003, z0=c.zc)

    def eyes(self, p, w, L, d):  # Kasuar-Locus: faceted face, four small eyes
        facet(p, w * 0.5, L * 0.5, d * 0.45, d * 0.95, "Frame", z0=-L * 0.1)
        for sx in (-1, 1):
            for k in range(2):
                p.block(w * 0.08, L * 0.05, d * 0.12, d * 1.0, "Glow", ch=0.003, x0=sx * w * (0.12 + k * 0.12), z0=L * (0.16 - k * 0.04))

    def head_back(self, p):
        w, L, d = self.head_base(p)
        facet(p, w * 0.7, L * 0.8, d * 0.6, d * 0.98, "Shell")

    def toe_tip(self, p, W, L, H):
        facet(p, W * 0.4, L * 0.3, H * 0.3, H * 0.7, "Frame", z0=L * 0.12)


class Aegis(SetStyle):
    """Slotted cyan hard-light projector rails / field-inverter packs."""
    idx, ch, end_w = 10, 0.45, 0.6

    def front_sig(self, p, c, top, rem):
        for sx in (-1, 1):
            p.block(c.ww * 0.15, c.zl, rem * 0.6, top - 0.002, "Frame", ch=0.004, x0=sx * c.ww * 0.34, z0=c.zc)
            for i in range(4):
                p.block(c.ww * 0.07, c.zl * 0.11, rem * 0.36, top + rem * 0.5, "Glow", ch=0.002, x0=sx * c.ww * 0.34,
                        z0=c.zc - c.zl * 0.36 + i * c.zl * 0.24)
        p.block(c.ww * 0.26, c.zl * 0.4, rem * 0.5, top - 0.002, "Panel", ch=0.006, z0=c.zc)
        p.prism(c.ww * 0.07, rem * 0.2, top + rem * 0.48, "Glow", axis="y", sides=8, z0=c.zc)

    def back_sig(self, p, c, base):
        p.block(c.ww * 0.82, c.zl * 0.72, c.hb * 0.35, base, "Frame", ch=0.008, z0=c.zc)
        r = min(c.ww, c.zl) * 0.3
        p.prism(r, c.hb * 0.2, base + c.hb * 0.33, "Glow", axis="y", sides=6, z0=c.zc)
        p.prism(r * 0.72, c.hb * 0.24, base + c.hb * 0.33, "Dark", axis="y", sides=6, z0=c.zc)

    def eyes(self, p, w, L, d):  # Emile-Aegis: T-slit, crest stripe, side rails
        p.block(w * 0.56, L * 0.05, d * 0.1, d * 1.0, "Glow", ch=0.003, z0=L * 0.15)
        p.block(w * 0.05, L * 0.24, d * 0.1, d * 1.0, "Glow", ch=0.003, z0=L * 0.02)
        p.block(w * 0.08, L * 0.3, d * 0.2, d * 1.4, "Panel", ch=0.004, z0=L * 0.36)

    def head_back(self, p):
        w, L, d = self.head_base(p)
        p.block(w * 0.5, L * 0.5, d * 0.3, d * 0.98, "Frame", ch=0.008)
        p.prism(w * 0.12, d * 0.1, d * 1.26, "Glow", axis="y", sides=6)

    def toe_tip(self, p, W, L, H):
        p.block(W * 0.6, L * 0.04, 0.02, H * 0.45, "Glow", ch=0.003, z0=L * 0.46)


class Ironclad(SetStyle):
    """Set01: limbs/body/head/hands come from ironclad.py; this adds the feet."""
    idx = 1

    def toe_tip(self, p, W, L, H):
        p.hazard_band(W * 0.6, L * 0.08, 0.02, H * 0.5, z0=L * 0.3, n=5)

STYLES = [Ironclad(), Bastion(), Overclock(), Ripjaw(), Afterburn(), Voltaic(), Salvo(), Goliath(), Mantis(), Aegis()]


# =============================================================================== plate-part dispatch
def build_plate_part(style, slot):
    p = Part()
    if style.idx == 1:
        return ic.PARTS[slot]()
    kind = slot.replace("Front", "").replace("Back", "")
    front = slot.endswith("Front")
    if kind in ("Thigh", "Calf", "UpperArm", "LowerArm"):
        c = limb_ctx(kind, front)
        (style.limb_front if front else style.limb_back)(p, c)
    elif kind == "Shoulder":
        (style.shoulder_front if front else style.shoulder_back)(p)
    elif kind == "Body":
        (style.body_front if front else style.body_back)(p)
    elif kind == "Head":
        (style.head_front if front else style.head_back)(p)
    elif kind == "Hand":
        (style.hand_front if front else style.hand_back)(p)
    return p


PLATE_SLOTS = list(ic.PARTS.keys())
LW_SLOTS = ["FootFront", "FootBack"]


def build_lw_part(style, slot):
    p = Part()
    if slot == "FootFront":
        style.foot_toe(p)
    elif slot == "FootBack":
        style.foot_heel(p)
    return p


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO")


def build_sets(indices, do_export=True, skip_ironclad_plates=True):
    ic.reset()
    made = {}
    for idx in indices:
        style = STYLES[idx - 1]
        tag = f"Set{idx:02d}_{style.name}"
        slots = ([] if (idx == 1 and skip_ironclad_plates) else PLATE_SLOTS) + LW_SLOTS
        for slot in slots:
            p = build_plate_part(style, slot) if slot in PLATE_SLOTS else build_lw_part(style, slot)
            obj = p.to_object(f"{slot}_{tag}")
            made[obj.name] = obj
            if do_export:
                export(obj, os.path.join(OUT_DIR, f"{slot}_{tag}.fbx"))
    return made


if __name__ == "__main__":
    sets = [int(s) for s in argval("--sets", "1,2,3,4,5,6,7,8,9,10").split(",")]
    made = build_sets(sets, do_export="--no-export" not in sys.argv, skip_ironclad_plates="--all-ironclad" not in sys.argv)
    if "--blend" in sys.argv:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "clamshell_sets.blend"))
    print("BUILT", len(made))
