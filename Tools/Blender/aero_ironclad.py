"""
IronFauna - Ironclad (Set01), second generation: aerodynamic, angular armor.

  blender -b -P aero_ironclad.py -- --out <dir> [--blend]
  python aero_ironclad.py --out <dir> [--blend]        (with the pip `bpy` module)

Shape language (use it for every new set):
  * no rectangles: every plate is a dart - a centre keel with sloped flanks, widest in its bulk half and
    drawn to a point toward the joint it must clear;
  * layers overlap like shingles, each one a smaller dart pointing the same way;
  * edges carry raked blades (strakes), backs carry ramped scoops ending in a dark exhaust mouth;
  * detail is sparse and purposeful: a polished spine strip, a few bolts, one hazard mark per large part.
Authoring frames are unchanged from ironclad.py (plates) and clamshell_sets.py (lengthwise parts), so the
parts drop into the same slots and the fold check still applies.
"""
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ironclad as ic  # noqa: E402
from ironclad import ENV, Part  # noqa: E402

OUT_DIR = ic.OUT_DIR if "--out" in sys.argv else "/tmp/aero"
os.makedirs(OUT_DIR, exist_ok=True)
TAG = "Set01_Ironclad"


# =============================================================================== primitives
def lx(p, section, stations, role, off=(0.0, 0.0, 0.0)):
    """Loft a convex section [(x, depth)] through stations (z, sx, sd[, dx[, dd]]): scale plus shear, so
    plates can taper, rake and sweep. Depth grows toward -Y (outward)."""
    ox, oy, oz = off
    rings = []
    for st in stations:
        z, sx, sd = st[0], st[1], st[2]
        dx = st[3] if len(st) > 3 else 0.0
        dd = st[4] if len(st) > 4 else 0.0
        rings.append([p.bm.verts.new((x * sx + dx + ox, -(d * sd + dd) + oy, z + oz)) for x, d in section])
    n = len(section)
    faces = []
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append(p.bm.faces.new([a[i], a[j], b[j], b[i]]))
    faces.append(p.bm.faces.new(rings[0]))
    faces.append(p.bm.faces.new(rings[-1]))
    mi = ic.ROLE_ORDER.index(role)
    verts = {v for f in faces for v in f.verts}
    c = sum((v.co for v in verts), Vector()) / len(verts)
    for f in faces:
        f.material_index = mi
        f.normal_update()
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()
    return faces


def keel(w, edge, top, flat=0.012, skirt=0.0):
    """Dart cross-section: sloped flanks rising from `edge` at the sides to `top` on the centre keel.
    `skirt` extends the side walls below the base so an overlapping plate sinks into the one beneath."""
    hw = w / 2
    lo, mid = edge * 0.6, edge + (top - edge) * 0.72     # steep chamfered sides, flatter creased top
    return [(-hw, -skirt), (-hw, lo), (-hw * 0.62, mid), (-flat, top), (flat, top), (hw * 0.62, mid), (hw, lo), (hw, -skirt)]


def blade(t, lo, hi):
    """Thin upright blade section, `lo` tall on one face and `hi` on the other (a knife edge)."""
    return [(-t / 2, 0.0), (t / 2, 0.0), (t / 2, lo), (-t / 2, hi)]


def fin(p, t, z0, z1, base, h, role="Shell", x0=0.0):
    """Swept stabiliser fin: grows from the plate at z0 to full height near z1, with a clipped trailing tip."""
    lx(p, blade(t, h, h), [(z0, 1.0, 0.1), (z0 + (z1 - z0) * 0.72, 1.0, 1.0), (z1, 1.0, 0.5)], role, off=(x0, -base, 0))


def leg_fins(p, kind, w, L, d, tip, zc):
    """Twin fins on the back of thighs and calves, sweeping away from the knee."""
    if kind in ("Thigh", "Calf"):
        for sx in (-1, 1):
            fin(p, 0.03, zc + tip * 0.08 * L, zc - tip * 0.32 * L, d * 0.5, d * 0.95, "Shell", x0=sx * (w / 2 - 0.07))


def back_fins(p, w, L, d):
    """Twin stabilisers on the back plate, rising behind the shoulders."""
    for sx in (-1, 1):
        fin(p, 0.04, -0.06 * L, 0.42 * L, d * 0.6, d * 0.95, "Shell", x0=sx * w * 0.4)


def box_sec(w, hgt, ch):
    hw = w / 2
    return [(-hw, 0.0), (hw, 0.0), (hw, hgt - ch), (hw - ch, hgt), (-hw + ch, hgt), (-hw, hgt - ch)]


def dart(L, tip, body=1.0):
    """Stations of a dart `L` long whose sharp end points toward `tip` (-1 or +1)."""
    h = L / 2
    return [(tip * h, 0.10, 0.2), (tip * (h - 0.3 * L), 0.62, 0.7), (0.0, 0.8, 0.95 * body),
            (-tip * 0.3 * L, 0.82, body), (-tip * h, 0.6, 0.5)]


def hood(p, w, L, d, tip, zc, top, base, role="Shell"):
    """Full-width plate over the bulk half with a chevron leading edge, on a dark underlay so the
    layer reads as a separate plate. Together with the narrower dart beneath it makes a barbed arrowhead."""
    st = [(zc + tip * 0.22 * L, 0.35, 0.3), (zc + tip * 0.02 * L, 1.0, 0.9), (zc - tip * 0.2 * L, 1.0, 1.0), (zc - tip * 0.3 * L, 0.85, 0.7)]
    under = [(z + tip * 0.025 * L, sx, sd) for z, sx, sd in st]
    lx(p, keel(w * 0.97, top * 0.4, top * 0.86, skirt=base), under, "Dark", off=(0, -base, 0))
    lx(p, keel(w, top * 0.45, top, skirt=base), st, role, off=(0, -base, 0))


# =============================================================================== limbs
def limb_front(p, w, L, d, tip, zc, hb):
    """tip: which end is drawn to a point (the joint the plate must fold past). zc: centre of the bulk half."""
    lx(p, keel(w, d * 0.42, d * 0.92), dart(L, tip), "Shell")
    top = d * hb
    hood(p, w, L, d, tip, zc, top, d * 0.80)
    p.block(w * 0.05, L * 0.26, 0.012, d * 0.80 + top - 0.004, "Panel", ch=0.003, z0=zc - tip * 0.08 * L)   # polished spine strip
    for sx in (-1, 1):                                                                                     # raked edge blades
        lx(p, blade(0.03, d * 0.95, d * 1.12), [(zc + tip * 0.02 * L, 1.0, 0.75), (zc - tip * 0.27 * L, 1.0, 1.0)],
           "Frame", off=(sx * (w / 2 - 0.05), 0, 0))
    p.bolts([(sx * w * 0.36, -tip * (L / 2 - 0.1 * L)) for sx in (-1, 1)], d * 0.3, r=w * 0.03)
    p.latch_tabs(w, L)


def limb_back(p, w, L, d, tip, zc, hb):
    lx(p, keel(w, d * 0.4, d * 0.78), dart(L, tip), "Shell")
    hood(p, w, L, d, tip, zc, d * 0.2, d * 0.62)
    # ramped scoop rising toward the bulk end, cut off square with a dark exhaust mouth
    ws, hs = w * 0.5, d * (0.34 + hb)
    z_lo, z_hi = zc + tip * 0.22 * L, zc - tip * 0.10 * L
    lx(p, box_sec(ws, hs, hs * 0.2), [(z_lo, 0.7, 0.08), (z_hi, 1.0, 1.0)], "Shell", off=(0, -d * 0.5, 0))
    p.block(ws * 0.78, 0.03, hs * 0.62, d * 0.5 + hs * 0.16, "Dark", ch=0.004, z0=z_hi - tip * 0.012)
    p.block(ws * 0.34, 0.012, hs * 0.16, d * 0.5 + hs * 0.36, "Glow", ch=0.002, z0=z_hi - tip * 0.03)       # recessed thruster glow
    lx(p, keel(ws * 0.9, hs * 0.2, hs * 0.5, skirt=d * 0.3), [(z_hi - tip * 0.02, 1.0, 1.0), (z_hi - tip * 0.16 * L, 0.5, 0.4)],
       "Frame", off=(0, -d * 0.5, 0))                                                                      # nozzle shroud
    p.latch_tabs(w, L)


LIMBS = {  # kind: (tip, bulk centre as a fraction of L, front height budget, back height budget)
    "Thigh": (-1, 0.16, 0.34, 0.26),      # point at the knee, bulk at the hip
    "Calf": (1, -0.06, 0.38, 0.26),       # point at the knee, bulk toward the ankle
    "UpperArm": (-1, 0.12, 0.40, 0.30),   # point at the elbow
    "LowerArm": (-1, 0.12, 0.40, 0.30),   # bracer: bulk at the elbow, point at the wrist
}


def build_limb(kind, front):
    w, L, d = ENV[kind]
    tip, zc, hf, hk = LIMBS[kind]
    p = Part()
    if front:
        limb_front(p, w, L, d, tip, zc * L, hf)
        if kind == "Calf":      # shin blade
            lx(p, blade(0.035, d * 0.32, d * 0.32), [(0.28 * L, 1.0, 0.15), (-0.12 * L, 1.0, 1.0), (-0.36 * L, 1.0, 0.3)], "Frame", off=(0, -d * 0.88, 0))
        if kind == "LowerArm":  # vambrace blade along the outer edge
            lx(p, blade(0.03, d * 0.5, d * 0.5), [(-0.42 * L, 1.0, 0.2), (0.3 * L, 1.0, 1.0)], "Frame", off=(w * 0.3, -d * 0.7, 0))
    else:
        limb_back(p, w, L, d, tip, zc * L, hk)
        leg_fins(p, kind, w, L, d, tip, zc * L)
    return p


# =============================================================================== shoulder
def build_shoulder(front):
    w, L, d = ENV["Shoulder"]     # w = front/back, +Z = outward toward the arm
    h = L / 2
    p = Part()
    st = [(-h, 0.78, 0.55), (-0.1 * L, 1.0, 0.95), (0.2 * L, 0.95, 1.0), (h - 0.06, 0.6, 0.8), (h, 0.2, 0.4)]
    if front:
        lx(p, keel(w, d * 0.5, d), st, "Shell")
        lx(p, keel(w * 0.7, d * 0.2, d * 0.5, skirt=d * 0.4), [(-0.22 * L, 0.85, 0.7), (0.05 * L, 1.0, 1.0), (0.3 * L, 0.7, 0.9), (0.46 * L, 0.15, 0.4)],
           "Shell", off=(0, -d * 0.75, 0))
        lx(p, blade(0.03, d * 0.5, d * 0.5), [(-0.28 * L, 1.0, 0.15), (0.3 * L, 1.0, 1.0), (0.42 * L, 1.0, 0.5)], "Frame", off=(0, -d * 1.02, 0))
        p.block(w * 0.5, L * 0.05, 0.012, d * 0.62, "Hazard", ch=0.003, z0=-0.36 * L)
    else:
        lx(p, keel(w, d * 0.4, d * 0.7), st, "Shell")
        p.block(w * 0.5, L * 0.3, d * 0.2, d * 0.55, "Dark", ch=0.008, z0=-0.05 * L)
        for k in (-1, 0, 1):
            p.block(w * 0.44, L * 0.05, d * 0.1, d * 0.72, "Frame", ch=0.004, z0=-0.05 * L + k * 0.09 * L)
    p.latch_tabs(w, L)
    return p


# =============================================================================== torso
BODY_ST = lambda L: [(-L / 2, 0.32, 0.4), (-0.3 * L, 0.44, 0.62), (0.0, 0.8, 0.9), (0.24 * L, 0.84, 1.0), (L / 2 - 0.08 * L, 0.8, 0.72), (L / 2, 0.6, 0.4)]


def build_body_front():
    w, L, d = ENV["Body"]
    p = Part()
    lx(p, keel(w, d * 0.5, d * 0.95, flat=0.04), BODY_ST(L), "Shell")
    # chest hood: full-width upper plate ending in a chevron that points at the waist
    hood(p, w, L, d, -1, 0.2 * L, d * 0.5, d * 0.72)
    p.block(w * 0.04, L * 0.22, 0.012, d * 1.22 - 0.006, "Panel", ch=0.003, z0=0.2 * L)
    p.block(w * 0.16, L * 0.009, d * 0.1, d * 0.8, "Glow", ch=0.002, z0=-0.065 * L)                      # sensor slit under the chevron point
    for sx in (-1, 1):
        # collar-bone strakes raking back toward the shoulders
        lx(p, blade(0.03, d * 0.34, d * 0.34), [(0.08 * L, 1.0, 0.3), (0.36 * L, 1.0, 1.0)], "Frame", off=(sx * w * 0.3, -d * 1.1, 0))
        # waist intakes: vertical dark slots in the flanks
        p.block(w * 0.05, L * 0.16, d * 0.2, d * 0.42, "Dark", ch=0.006, x0=sx * w * 0.27, z0=-0.24 * L)
    for i in range(3):                                                                                 # lapped abdominal chevrons
        z = -0.17 * L - i * 0.105 * L
        wi = w * (0.5 - i * 0.06)
        lx(p, keel(wi, d * 0.1, d * 0.26, skirt=d * 0.4), [(z - 0.065 * L, 0.7, 0.55), (z + 0.01 * L, 1.0, 1.0), (z + 0.06 * L, 0.92, 0.75)],
           "Shell", off=(0, -d * (0.66 - i * 0.1), 0))
    p.block(w * 0.46, L * 0.045, d * 0.3, d * 0.36, "Frame", ch=0.012, z0=0.445 * L)                    # collar guard
    p.hazard_band(w * 0.3, L * 0.02, 0.012, d * 0.66, z0=0.445 * L, n=7)
    p.bolts([(sx * w * 0.4, 0.3 * L) for sx in (-1, 1)] + [(sx * w * 0.26, -0.38 * L) for sx in (-1, 1)], d * 0.3, r=0.022)
    p.latch_tabs(w, L)
    return p


def build_body_back():
    w, L, d = ENV["Body"]
    p = Part()
    lx(p, keel(w, d * 0.45, d * 0.8, flat=0.08), BODY_ST(L), "Shell")
    hood(p, w, L, d, -1, 0.26 * L, d * 0.16, d * 0.68)
    lx(p, keel(w * 0.2, d * 0.2, d * 0.5, skirt=d * 0.4), [(-0.3 * L, 0.6, 0.5), (0.1 * L, 1.0, 1.0), (0.38 * L, 0.8, 0.6)], "Frame", off=(0, -d * 0.7, 0))
    for sx in (-1, 1):
        # vectoring thruster housings: fairing ramps down the back and ends in a nozzle mouth
        x = sx * w * 0.27
        lx(p, box_sec(w * 0.24, d * 0.7, d * 0.12), [(0.3 * L, 0.75, 0.12), (-0.12 * L, 1.0, 1.0)], "Shell", off=(x, -d * 0.52, 0))
        p.block(w * 0.19, 0.035, d * 0.46, d * 0.6, "Dark", ch=0.006, x0=x, z0=-0.135 * L)
        p.block(w * 0.1, 0.02, d * 0.22, d * 0.72, "Glow", ch=0.004, x0=x, z0=-0.128 * L)
        lx(p, keel(w * 0.22, d * 0.12, d * 0.34, skirt=d * 0.3), [(-0.15 * L, 1.0, 1.0), (-0.34 * L, 0.5, 0.35)], "Frame", off=(x, -d * 0.52, 0))
        for k in range(3):                                                                              # upper cooling louvers
            p.block(w * 0.2, L * 0.018, d * 0.08, d * 0.62, "Dark", ch=0.003, x0=sx * w * 0.3, z0=0.36 * L + k * 0.035 * L)
    p.hazard_band(w * 0.26, L * 0.022, 0.012, d * 0.5, z0=-0.41 * L, n=5)
    back_fins(p, w, L, d)
    p.latch_tabs(w, L)
    return p


# =============================================================================== head
HEAD_ST = lambda L: [(-L / 2, 0.4, 0.55), (-0.22 * L, 0.78, 0.95), (0.08 * L, 1.0, 0.9), (L / 2 - 0.1 * L, 0.92, 0.7), (L / 2, 0.62, 0.38)]


def build_head_front():
    w, L, d = ENV["Head"]
    p = Part()
    lx(p, keel(w, d * 0.5, d), HEAD_ST(L), "Shell")
    lx(p, keel(w * 0.92, d * 0.25, d * 0.5, skirt=d * 0.5), [(0.12 * L, 0.9, 1.0), (0.3 * L, 1.0, 0.75), (0.44 * L, 0.8, 0.4)], "Shell", off=(0, -d * 0.85, 0))  # raked brow
    p.block(w * 0.62, L * 0.02, d * 0.5, d * 0.6, "Glow", ch=0.002, z0=0.06 * L)                       # visor band under the brow
    for sx in (-1, 1):                                                                                 # swept cheek guards
        lx(p, keel(w * 0.26, d * 0.1, d * 0.3, skirt=d * 0.5),
           [(-0.32 * L, 0.4, 0.5, sx * w * 0.2), (-0.1 * L, 1.0, 1.0, sx * w * 0.32), (0.0, 0.9, 0.8, sx * w * 0.34)], "Shell", off=(0, -d * 0.72, 0))
    p.block(w * 0.16, L * 0.1, d * 0.2, d * 0.75, "Dark", ch=0.006, z0=-0.3 * L)                        # chin intake
    lx(p, blade(0.03, d * 0.45, d * 0.45), [(0.18 * L, 1.0, 0.25), (0.47 * L, 1.0, 1.0)], "Frame", off=(0, -d * 1.05, 0))   # crest
    p.latch_tabs(w, L)
    return p


def build_head_back():
    w, L, d = ENV["Head"]
    p = Part()
    lx(p, keel(w, d * 0.45, d * 0.85), HEAD_ST(L), "Shell")
    for sx in (-1, 1):                                                                                 # swept-back comms fins
        lx(p, blade(0.028, d * 0.7, d * 0.7), [(-0.1 * L, 1.0, 0.2), (0.4 * L, 1.0, 1.0), (0.48 * L, 1.0, 0.6)], "Frame", off=(sx * w * 0.24, -d * 0.6, 0))
    p.block(w * 0.3, L * 0.34, d * 0.25, d * 0.7, "Frame", ch=0.01, z0=-0.08 * L)                       # heat exchanger
    for k in range(4):
        p.block(w * 0.25, L * 0.03, d * 0.1, d * 0.93, "Dark", ch=0.003, z0=-0.2 * L + k * 0.075 * L)
    p.latch_tabs(w, L)
    return p


# =============================================================================== hand
def build_hand(front):
    w, L, d = ENV["Hand"]
    h = L / 2
    p = Part()
    st = [(-h, 0.8, 0.6), (0.0, 1.0, 1.0), (h, 0.55, 0.4)]
    if front:
        lx(p, keel(w, d * 0.5, d), st, "Shell")
        for i in (-1, 0, 1):                                                                           # tapered finger plates
            lx(p, box_sec(w * 0.22, d * 0.4, d * 0.08), [(0.0, 1.0, 1.0), (0.46 * L, 0.7, 0.5)], "Frame", off=(i * w * 0.28, -d * 0.78, 0))
    else:
        lx(p, keel(w, d * 0.4, d * 0.7), st, "Shell")
        p.block(w * 0.4, L * 0.3, d * 0.15, d * 0.6, "Dark", ch=0.006)
    return p


# =============================================================================== feet (lengthwise, true size)
def build_foot(toe):
    p = Part()
    if toe:
        W, L, H = 0.50, 0.48, 0.22
        sec = [(-W / 2, 0), (-W / 2, H * 0.45), (-W * 0.12, H), (W * 0.12, H), (W / 2, H * 0.45), (W / 2, 0)]
        lx(p, sec, [(-L / 2, 0.92, 0.55), (-L / 2 + 0.09, 1.0, 1.0), (0.0, 0.92, 0.88), (L / 2 - 0.12, 0.6, 0.55), (L / 2, 0.18, 0.2)], "Shell")
        lx(p, sec, [(L / 2 - 0.13, 0.63, 0.5), (L / 2 + 0.0, 0.2, 0.16)], "Frame", off=(0, 0.004, 0.004))             # steel toe cap
        p.block(W * 0.9, L * 0.86, 0.03, 0.0, "Dark", ch=0.008, z0=-0.02)
        p.block(W * 0.3, L * 0.05, 0.012, H * 0.42, "Hazard", ch=0.003, z0=L / 2 - 0.1)
    else:
        W, L, H = 0.46, 0.28, 0.16
        sec = [(-W / 2, 0), (-W / 2, H * 0.5), (-W * 0.14, H), (W * 0.14, H), (W / 2, H * 0.5), (W / 2, 0)]
        lx(p, sec, [(-L / 2, 0.95, 0.6), (-L / 2 + 0.06, 1.0, 1.0), (L / 2 - 0.08, 0.75, 0.7), (L / 2, 0.3, 0.3)], "Shell")
        p.block(W * 0.86, L * 0.84, 0.03, 0.0, "Dark", ch=0.008, z0=-0.01)
    return p


# =============================================================================== export
def parts():
    out = {}
    for kind in LIMBS:
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
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "aero_ironclad.blend"))
    print("BUILT", made)
