"""
IronFauna - structural skeleton (inner frame) for every chassis, in three weight classes.

  blender -b -P skeleton.py -- --out <dir> [--blend]
  python skeleton.py --out <dir> [--blend]            (with the pip `bpy` module)

Unlike armor, skeleton pieces are modeled at true size and are NOT fitted: Unity loads them as they are
(ClamshellMeshLibrary.TryGetRaw) and only scales them where noted.

Authoring frame (Blender): X = side, -Y = the mech's front, Z = up, metres, origin = the piece's pivot.
Unity reads this as (x, y, z) = (-X, Z, -Y), so the front stays the front.
  Skel_TorsoHumanoid_<Class>  origin = waist joint centre
  Skel_TorsoAnimal_<Class>    origin = centre of the hip line (ground projection of the body centre, at hip height)
  Skel_<Thigh|Calf|UpperArm|LowerArm>_<Class>  Z along the bone, origin mid-bone, -Z end = the `from` joint
  Skel_Hand                   Z along the hand, origin at the wrist
  Skel_Foot_<Class>           origin on the ground under the ankle
  Skel_Head / Skel_HeadAnimal origin = neck joint centre
  Skel_Joint / Skel_JointCore unit radius, axis X (Unity scales it by the joint radius)

Clearance rule: wherever armor clamps on, the skeleton stays inside the slot between the two halves
(|Y| below the seam offset `inner`), so no armor set can ever touch it. Numbers mirror MechFrameSpec.cs.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ironclad as ic  # noqa: E402
from ironclad import Part  # noqa: E402

for _role, _col in (("Copper", (0.58, 0.16, 0.045, 1)), ("Core", (1.0, 0.19, 0.01, 1))):
    if _role not in ic.ROLE_COLORS:
        ic.ROLE_COLORS[_role] = _col
        ic.ROLE_ORDER.append(_role)

OUT_DIR = ic.OUT_DIR if "--out" in sys.argv else "/tmp/skeleton"
os.makedirs(OUT_DIR, exist_ok=True)

# Weight classes. Keep in sync with Assets/Scripts/Clamshell/MechFrameSpec.cs.
#   ft frame thickness, aw/ad armor width/depth, ai seam offset, jr joint radius,
#   hipX / shX / collar humanoid half-widths, legX quadruped leg half-spacing, sock hardpoint socket radius.
CLASSES = {
    "Light": dict(ft=0.82, aw=0.88, ad=0.85, ai=0.90, jr=0.90, hipX=0.43, shX=1.02, collar=0.32, legX=0.66, sock=0.060),
    "Medium": dict(ft=1.00, aw=1.00, ad=1.00, ai=1.00, jr=1.00, hipX=0.48, shX=1.12, collar=0.36, legX=0.72, sock=0.080),
    "Heavy": dict(ft=1.20, aw=1.14, ad=1.15, ai=1.12, jr=1.12, hipX=0.55, shX=1.24, collar=0.42, legX=0.80, sock=0.105),
}
JOINT_GAP = 0.06
LEG_Z = 0.85  # quadruped leg half-spacing front/back


# =============================================================================== primitives (absolute coords)
def box(p, c, s, role, ch=None):
    """Chamfered box centred at c=(x, y, z) with size s=(sx, sy, sz)."""
    return p.block(s[0], s[2], s[1], -(c[1] + s[1] / 2), role, ch=ch, x0=c[0], z0=c[2])


def cyl(p, c, r, length, axis, role, sides=12):
    """Cylinder centred at c along axis 'x' | 'y' | 'z'."""
    if axis == "y":
        return p.prism(r, length, -c[1] - length / 2, role, axis="y", sides=sides, x0=c[0], z0=c[2])
    return p.prism(r, length, -r - c[1], role, axis=axis, sides=sides, x0=c[0], z0=c[2])


def tube(p, a, b, r, role, sides=8, r2=None):
    """Straight tube from a to b (sides=4 gives a square bar of half-diagonal r)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    d.normalize()
    ref = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    u = d.cross(ref).normalized()
    v = d.cross(u).normalized()
    rings = []
    for pt, rr in ((a, r), (b, r if r2 is None else r2)):
        rings.append([p.bm.verts.new(pt + (u * math.cos((i + 0.5) / sides * 2 * math.pi) + v * math.sin((i + 0.5) / sides * 2 * math.pi)) * rr)
                      for i in range(sides)])
    faces = []
    for i in range(sides):
        j = (i + 1) % sides
        faces.append(p.bm.faces.new([rings[0][i], rings[0][j], rings[1][j], rings[1][i]]))
    faces.append(p.bm.faces.new(rings[0]))
    faces.append(p.bm.faces.new(rings[1]))
    mi = ic.ROLE_ORDER.index(role)
    c = (a + b) / 2
    for f in faces:
        f.material_index = mi
        f.normal_update()
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()
    return faces


def bar(p, a, b, w, role):
    return tube(p, a, b, w * 0.7071, role, sides=4)


def piston(p, a, b, r, split=0.56, eye_axis=(1, 0, 0)):
    """Hydraulic cylinder: dark barrel from a, bright rod into b, gland collar, clevis eyes on both ends."""
    a, b = Vector(a), Vector(b)
    m = a + (b - a) * split
    tube(p, a, m, r, "Dark", sides=10)
    tube(p, m - (b - a).normalized() * r * 0.9, m + (b - a).normalized() * r * 0.5, r * 1.22, "Frame", sides=10)
    tube(p, m, b, r * 0.5, "Panel", sides=8)
    e = Vector(eye_axis).normalized() * r * 1.25
    for pt in (a, b):
        tube(p, pt - e, pt + e, r * 1.25, "Frame", sides=8)


def cable(p, pts, r, clamp_every=2):
    """Flexible line run through pts, with copper clamps."""
    pts = [Vector(q) for q in pts]
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        tube(p, a, b, r, "Dark", sides=6)
        if clamp_every and i % clamp_every == 0:
            d = (b - a).normalized()
            m = (a + b) / 2
            tube(p, m - d * r * 0.9, m + d * r * 0.9, r * 1.5, "Copper", sides=6)


def socket(p, c, r, axis):
    """Weapon hardpoint socket: dark receiver cup, hazard ring at the mouth, locking plug, 4 lugs. axis: '+z', '+y', '-y', '+x', '-x'."""
    ax = axis[1]
    sgn = 1.0 if axis[0] == "+" else -1.0
    n = {"x": Vector((1, 0, 0)), "y": Vector((0, 1, 0)), "z": Vector((0, 0, 1))}[ax] * sgn
    c = Vector(c)
    cyl(p, c + n * 0.035, r, 0.07, ax, "Dark", 12)
    cyl(p, c + n * 0.066, r * 1.2, 0.016, ax, "Hazard", 12)
    cyl(p, c + n * 0.05, r * 0.5, 0.11, ax, "Frame", 8)
    others = [a for a in "xyz" if a != ax]
    for i in range(4):
        off = Vector((0, 0, 0))
        setattr(off, others[0], math.cos(i * math.pi / 2 + math.pi / 4) * r * 1.02)
        setattr(off, others[1], math.sin(i * math.pi / 2 + math.pi / 4) * r * 1.02)
        cyl(p, c + off + n * 0.04, r * 0.16, 0.085, ax, "Frame", 6)


def hanger(p, c, span, axis="x", t=0.03):
    """Armor hanger: a flat cross bar through the bone with a bright mounting pin on each tip (where the
    clamshell's side latches clamp, and where a hit can knock a plate off)."""
    c = Vector(c)
    if axis == "x":
        box(p, c, (span - 0.03, t, t * 1.2), "Frame", ch=0.006)
        for s in (-1, 1):
            cyl(p, c + Vector((s * (span / 2 - 0.012), 0, 0)), 0.017, 0.045, "x", "Panel", 8)
    else:
        box(p, c, (t * 1.2, span - 0.03, t), "Frame", ch=0.006)
        for s in (-1, 1):
            cyl(p, c + Vector((0, s * (span / 2 - 0.012), 0)), 0.017, 0.045, "y", "Panel", 8)


def U(x, up, fwd):
    """Unity-style (x, up, forward) -> Blender (x, y, z)."""
    return (x, -fwd, up)


def ubox(p, c, s, role, ch=None):
    """Box given as Unity-style centre (x, up, fwd) and size (sx, s_up, s_fwd)."""
    return box(p, U(*c), (s[0], s[2], s[1]), role, ch=ch)


# =============================================================================== joint
def build_joint(core):
    """Rotary joint housing, unit radius, axis X, total length 1.1 (matches the old plain drum)."""
    p = Part()
    cyl(p, (0, 0, 0), 1.0, 0.86, "x", "Frame", 16)
    for sx in (-1, 1):
        cyl(p, (sx * 0.3, 0, 0), 1.05, 0.1, "x", "Dark", 16)       # retaining bands
        cyl(p, (sx * 0.49, 0, 0), 0.8, 0.12, "x", "Dark", 16)      # end cap
        for i in range(6):                                          # cap bolts
            a = i / 6 * 2 * math.pi
            cyl(p, (sx * 0.555, math.cos(a) * 0.6, math.sin(a) * 0.6), 0.08, 0.05, "x", "Frame", 6)
    cyl(p, (0, 0, 0), 0.44 if core else 0.36, 1.22, "x", "Core" if core else "Frame", 12)  # axle / glowing core
    return p


# =============================================================================== limb bones
def build_limb(cls, length, r_from, r_to, armor_w, inner, clear_from, clear_to, spar_w, piston_r):
    """Bone between two joints. -Z end = `from` joint, +Z end = `to` joint. Armor shell covers
    [-L/2 + clear_from, L/2 - clear_to]; the cross pins sit where the armor's side latches clamp."""
    C = CLASSES[cls]
    p = Part()
    ft = C["ft"]
    h = length / 2
    sy = 2 * (inner - 0.03)                       # front/back thickness that always fits the armor slot
    sw = spar_w * ft
    z_lo, z_hi = -h + r_from * 0.5, h - r_to * 0.5
    spar_len = z_hi - z_lo
    zc = (z_lo + z_hi) / 2
    box(p, (0, 0, zc), (sw, sy, spar_len), "Frame", ch=min(sw, sy) * 0.22)

    # side channels: lightening slots (Light), a service channel (Medium), stiffener straps (Heavy)
    if cls == "Light":
        for k in (-0.26, 0.0, 0.26):
            for sx in (-1, 1):
                box(p, (sx * sw / 2, 0, zc + k * spar_len), (0.014, sy * 0.5, spar_len * 0.17), "Dark", ch=0.003)
    else:
        for sx in (-1, 1):
            box(p, (sx * sw / 2, 0, zc), (0.014, sy * 0.36, spar_len * 0.74), "Dark", ch=0.003)
    if cls == "Heavy":
        for k in (-0.3, 0.3):
            box(p, (0, 0, zc + k * spar_len), (sw * 1.22, sy * 1.0, 0.06), "Frame", ch=0.008)

    # clevis forks hugging each joint drum (outer fork on `from`, inner fork on `to`, so two bones never share a face)
    for z_end, r, gap in ((-h, r_from, 0.035), (h, r_to, 0.006)):
        half = r * 0.55 + gap + 0.012
        zin = z_end + (1 if z_end < 0 else -1) * r * 0.55
        for sx in (-1, 1):
            box(p, (sx * half, 0, zin), (0.024, sy * 1.1, r * 1.5), "Frame", ch=0.006)
            cyl(p, (sx * (half + 0.014), 0, z_end), r * 0.42, 0.012, "x", "Dark", 10)
        box(p, (0, 0, zin + (1 if z_end < 0 else -1) * r * 0.62), (2 * half + 0.024, sy, 0.05), "Frame", ch=0.008)

    # hydraulics along the bone: Light one actuator + a cable run, Medium two, Heavy two thick ones + cable
    pr = piston_r * ft
    px = sw / 2 + pr * 1.3 + 0.012
    a_z, b_z = zc + spar_len * 0.36, zc - spar_len * 0.36
    sides = (1,) if cls == "Light" else (-1, 1)
    for sx in sides:
        piston(p, (sx * px, 0, a_z), (sx * px, 0, b_z), pr)
        for z in (a_z, b_z):
            box(p, (sx * (sw / 2 + px) / 2, 0, z), (px - sw / 2 + 0.02, pr * 1.6, pr * 2.2), "Frame", ch=0.004)
    if cls == "Light":
        cable(p, [(-px, 0, zc + spar_len * k) for k in (0.42, 0.2, 0.0, -0.2, -0.42)], 0.012 * ft + 0.004)
    if cls == "Heavy":
        cable(p, [(0, sy / 2 + 0.012, zc + spar_len * k) for k in (0.42, 0.2, 0.0, -0.2, -0.42)], 0.011)

    # armor mounting pins: where the clamshell halves latch (and where a hit can knock them off)
    shell_len = length - clear_from - clear_to
    shell_c = -h + clear_from + shell_len / 2
    for k in (-0.22, 0.22):
        z = shell_c + k * shell_len
        hanger(p, (0, 0, z), armor_w + 0.03)
        box(p, (0, 0, z), (sw * 1.08, sy * 0.9, 0.05), "Dark", ch=0.006)
    return p


def limb_specs(cls):
    """(name, length, r_from, r_to, armor_w, inner, clear_from, clear_to, spar_w, piston_r) per bone."""
    C = CLASSES[cls]
    jr, aw, ai = C["jr"], C["aw"], C["ai"]
    r = 0.15 * jr
    leg_clear = r + JOINT_GAP
    ua = math.sqrt(0.06 ** 2 + 0.70 ** 2 + 0.06 ** 2)
    la = math.sqrt(0.02 ** 2 + 0.64 ** 2 + 0.22 ** 2)
    return [
        ("Thigh", 1.15, r, r, 0.46 * aw, 0.09 * ai, leg_clear, leg_clear, 0.15, 0.032),
        ("Calf", 1.15, r * 0.8, r, 0.42 * aw, 0.09 * ai, leg_clear, leg_clear, 0.14, 0.030),
        ("UpperArm", ua, 0.13 * jr, 0.15 * jr, 0.34 * aw, 0.07 * ai, 0.18, 0.20, 0.10, 0.022),
        ("LowerArm", la, 0.11 * jr, 0.13 * jr, 0.36 * aw, 0.07 * ai, 0.16, 0.18, 0.10, 0.022),
    ]


def build_hand():
    """Bare manipulator: palm block, three two-segment fingers and a thumb. Origin at the wrist, +Z to the fingertips."""
    p = Part()
    box(p, (0, 0, 0.13), (0.11, 0.07, 0.10), "Frame", ch=0.012)                  # wrist stem
    box(p, (0, 0, 0.21), (0.20, 0.08, 0.10), "Frame", ch=0.014)                  # palm
    box(p, (0, -0.042, 0.21), (0.12, 0.012, 0.06), "Dark", ch=0.003)             # palm pad (front)
    for i in (-1, 0, 1):
        x = i * 0.066
        cyl(p, (x, 0, 0.265), 0.022, 0.05, "x", "Dark", 8)                       # knuckle
        box(p, (x, 0, 0.292), (0.044, 0.05, 0.05), "Frame", ch=0.006)
        cyl(p, (x, 0, 0.318), 0.017, 0.044, "x", "Dark", 8)
        box(p, (x, -0.006, 0.334), (0.038, 0.04, 0.03), "Frame", ch=0.006)
    box(p, (0.115, -0.01, 0.2), (0.04, 0.045, 0.07), "Frame", ch=0.006)          # thumb
    cyl(p, (0.1, 0, 0.17), 0.02, 0.05, "y", "Dark", 8)
    return p


# =============================================================================== foot
def build_foot(cls):
    """Foot frame under the toe/heel armor. Unity scales X by the leg class's armor width."""
    C = CLASSES[cls]
    ft = C["ft"]
    p = Part()
    ubox(p, (0, 0.015, 0.12), (0.46, 0.03, 1.12), "Dark", ch=0.008)                      # tread plate
    ubox(p, (0, 0.05, 0.12), (0.2 * ft, 0.05, 1.06), "Frame", ch=0.01)                    # keel beam
    for sx in (-1, 1):                                                                   # ankle yoke cheeks
        ubox(p, (sx * 0.105, 0.17, 0.0), (0.03, 0.26, 0.2), "Frame", ch=0.01)
        cyl(p, U(sx * 0.125, 0.26, 0.0), 0.05, 0.012, "x", "Dark", 10)
        # stabiliser struts in the open ankle zone (toe and heel side)
        piston(p, U(sx * 0.15, 0.2, 0.03), U(sx * 0.18, 0.06, 0.17), 0.016 * ft + 0.004)
        if cls != "Light":
            piston(p, U(sx * 0.15, 0.2, -0.03), U(sx * 0.18, 0.06, -0.13), 0.014 * ft + 0.004)
        ubox(p, (sx * 0.18, 0.045, 0.02), (0.07, 0.04, 0.36), "Frame", ch=0.008)          # strut shoe
    ubox(p, (0, 0.1, 0.0), (0.24, 0.06, 0.16), "Frame", ch=0.012)                         # yoke base
    # toe frame (inside the toe armor): centre toe + two side toes, with dark knuckles
    ubox(p, (0, 0.11, 0.42), (0.15, 0.08, 0.42), "Frame", ch=0.014)
    ubox(p, (0, 0.13, 0.3), (0.19, 0.07, 0.06), "Dark", ch=0.008)
    for sx in (-1, 1):
        ubox(p, (sx * 0.14, 0.1, 0.36), (0.085, 0.07, 0.3), "Frame", ch=0.012)
        ubox(p, (sx * 0.14, 0.115, 0.27), (0.1, 0.06, 0.05), "Dark", ch=0.006)
    # heel spur (inside the heel armor)
    ubox(p, (0, 0.095, -0.28), (0.2, 0.07, 0.26), "Frame", ch=0.014)
    ubox(p, (0, 0.11, -0.19), (0.26, 0.05, 0.05), "Dark", ch=0.006)
    if cls == "Heavy":                                                                   # outrigger pads
        for sx in (-1, 1):
            ubox(p, (sx * 0.2, 0.04, -0.3), (0.06, 0.04, 0.2), "Frame", ch=0.008)
    return p


# =============================================================================== heads
def build_head():
    """Skull core with the universal neck collar. Origin = neck joint centre; armor clamps from 0.18 above it."""
    p = Part()
    cyl(p, (0, 0, 0.1), 0.065, 0.2, "z", "Frame", 10)                            # neck post
    cyl(p, (0, 0, 0.15), 0.16, 0.035, "z", "Dark", 16)                           # universal collar ring
    cyl(p, (0, 0, 0.175), 0.125, 0.03, "z", "Frame", 16)
    for i in range(6):                                                           # collar lock bolts
        a = i / 6 * 2 * math.pi
        cyl(p, (math.cos(a) * 0.14, math.sin(a) * 0.14, 0.172), 0.012, 0.016, "z", "Frame", 6)
    for sx in (-1, 1):                                                           # neck actuators
        piston(p, (sx * 0.1, 0.04, 0.06), (sx * 0.1, 0.07, 0.3), 0.014, split=0.5)
    box(p, (0, 0, 0.45), (0.27, 0.25, 0.42), "Frame", ch=0.045)                  # braincase
    box(p, (0, -0.02, 0.24), (0.2, 0.2, 0.08), "Dark", ch=0.02)                  # jaw block
    box(p, (0, -0.128, 0.5), (0.2, 0.012, 0.11), "Dark", ch=0.004)               # sensor panel (face)
    for sx in (-1, 1):
        cyl(p, (sx * 0.05, -0.136, 0.515), 0.026, 0.02, "y", "Glow", 8)          # twin optics
        cyl(p, (sx * 0.136, 0, 0.45), 0.05, 0.012, "x", "Dark", 10)              # temple caps
    cyl(p, (0, -0.132, 0.455), 0.012, 0.016, "y", "Panel", 6)                    # range finder
    for z in (0.33, 0.57):                                                       # armor hangers
        hanger(p, (0, 0, z), 0.59)
    box(p, (0, 0.03, 0.675), (0.08, 0.1, 0.03), "Dark", ch=0.006)                # crest / antenna socket
    cable(p, [(0.07, 0.127, 0.3), (0.07, 0.132, 0.42), (0.07, 0.127, 0.56)], 0.011)  # rear conduit
    cable(p, [(-0.07, 0.127, 0.3), (-0.07, 0.132, 0.42), (-0.07, 0.127, 0.56)], 0.011)
    return p


def build_head_animal():
    """Beast skull frame: braincase, upper and lower jaw with teeth. Origin = neck joint; +forward is -Y."""
    p = Part()
    bar(p, U(0, 0.0, 0.0), U(0, 0.04, 0.2), 0.11, "Frame")                        # neck stem
    cyl(p, U(0, 0.02, 0.17), 0.12, 0.03, "y", "Dark", 14)                         # collar ring
    ubox(p, (0, 0.05, 0.32), (0.24, 0.2, 0.2), "Frame", ch=0.03)                  # braincase
    ubox(p, (0, 0.07, 0.55), (0.17, 0.1, 0.24), "Frame", ch=0.02)                 # upper jaw
    ubox(p, (0, -0.05, 0.5), (0.14, 0.045, 0.3), "Dark", ch=0.012)                # lower jaw
    for sx in (-1, 1):
        cyl(p, U(sx * 0.1, -0.02, 0.38), 0.035, 0.03, "x", "Dark", 8)             # jaw hinge
        piston(p, U(sx * 0.105, 0.08, 0.3), U(sx * 0.085, -0.04, 0.46), 0.011)    # jaw closer
        cyl(p, U(sx * 0.105, 0.09, 0.425), 0.022, 0.016, "x", "Glow", 8)          # optics
        for k in range(3):                                                        # teeth
            ubox(p, (sx * 0.06, 0.005, 0.5 + k * 0.05), (0.02, 0.035, 0.022), "Panel", ch=0.003)
    return p


# =============================================================================== humanoid torso
def build_torso_humanoid(cls):
    C = CLASSES[cls]
    ft, aw, jr = C["ft"], C["aw"], C["jr"]
    hipX, shX, col, sk = C["hipX"], C["shX"], C["collar"], C["sock"]
    W = 1.0 * aw                       # torso armor width
    si = 0.17 * C["ai"] - 0.02         # usable half-depth inside the torso armor slot
    p = Part()

    # --- pelvis: hip track + carriages + bulkhead -------------------------------------------------
    box(p, (0, 0, -0.30), (2 * hipX - 0.2, 0.15 * ft, 0.13 * ft), "Frame", ch=0.02)                 # hip-track rail
    for sy in (-1, 1):                                                                              # track grooves
        box(p, (0, sy * (0.075 * ft + 0.004), -0.30), (2 * hipX - 0.36, 0.012, 0.05 * ft), "Dark", ch=0.003)
    for sx in (-1, 1):
        box(p, (sx * (hipX - 0.135), 0, -0.30), (0.07, 0.2 * ft, 0.2 * ft), "Dark", ch=0.016)        # hip carriage
        bar(p, (sx * 0.1 * ft, 0, -0.08), (sx * (hipX - 0.2), 0, -0.25), 0.06 * ft, "Frame")         # A-brace
    for sx in (-1, 1):                                                                               # hip actuators
        piston(p, (sx * 0.2 * ft, 0.06 * ft, -0.1), (sx * (hipX - 0.2), 0.06 * ft, -0.32), 0.022 * ft, split=0.5)
    box(p, (0, 0, -0.2), (0.34 * ft, 0.24 * ft, 0.3), "Frame", ch=0.045)                             # pelvic bulkhead
    box(p, (0, -0.12 * ft - 0.004, -0.2), (0.2 * ft, 0.012, 0.14), "Dark", ch=0.004)
    box(p, (0, 0.12 * ft + 0.004, -0.2), (0.2 * ft, 0.012, 0.14), "Dark", ch=0.004)
    cyl(p, (0, 0, -0.035), 0.2 * ft, 0.04, "z", "Dark", 16)                                          # waist yaw ring
    if cls == "Heavy":                                                                               # reinforced skirts
        for sx in (-1, 1):
            box(p, (sx * 0.26, 0, -0.2), (0.04, 0.26, 0.22), "Frame", ch=0.012)

    # --- multi-segment spine ------------------------------------------------------------------------
    nv = 5
    for i in range(nv):
        z = 0.2 + i * 0.25
        vw = (0.2 + (0.04 if i >= 3 else 0.0)) * ft
        vd = min(0.2 * ft, 2 * si)
        box(p, (0, 0, z), (vw, vd, 0.17), "Frame", ch=0.03)
        box(p, (0, 0, z), (vw + 0.1, 0.07, 0.05), "Dark", ch=0.008)                                  # transverse lugs
        if i < nv - 1:
            cyl(p, (0, 0, z + 0.125), 0.07 * ft, 0.09, "z", "Dark", 10)                              # disc
    back = min(0.1 * ft, si - 0.03) + 0.014
    for sx in (-1, 1):                                                                               # spinal cable bundle
        cable(p, [(sx * 0.05, back, 0.14 + k * 0.25) for k in range(6)], 0.014)
        piston(p, (sx * 0.13 * ft, 0, -0.08), (sx * 0.27, 0, 0.6), 0.03 * ft)                        # lumbar struts

    # --- rib cage + reactor -------------------------------------------------------------------------
    ribs = (0.62, 0.86, 1.08) if cls != "Light" else (0.62, 1.08)
    for z in ribs:
        box(p, (0, 0, z), (W - 0.14, 0.07, 0.05), "Frame", ch=0.012)
    shell_c, shell_len = 0.24 + 1.06 / 2, 1.06
    for sx in (-1, 1):
        box(p, (sx * (W / 2 - 0.07), 0, 0.85), (0.05, 2 * si * 0.8, 0.62), "Dark", ch=0.012)         # side rail (seen in the seam)
        for k in (-0.22, 0.22):                                                                      # armor mounting pins
            cyl(p, (sx * (W / 2 - 0.02), 0, shell_c + k * shell_len), 0.02, 0.1, "x", "Panel", 8)
        if cls == "Heavy":
            box(p, (sx * (W / 2 - 0.2), 0, 0.85), (0.03, 2 * si * 0.9, 0.5), "Frame", ch=0.008)      # bulkheads
    for sx in (-1, 1):                                                                               # thoracic X-braces
        bar(p, (sx * 0.12 * ft, 0, 1.1), (sx * (W / 2 - 0.1), 0, 0.64), 0.05 * ft, "Frame")
        bar(p, (sx * 0.12 * ft, 0, 0.62), (sx * (W / 2 - 0.1), 0, 1.06), 0.05 * ft, "Frame")
        box(p, (sx * (W / 2 - 0.24), 0, 0.86), (0.11, 2 * si * 0.7, 0.2), "Dark", ch=0.02)            # coolant tanks
    box(p, (0, -(si - 0.012), 0.97), (0.09, 0.02, 0.3), "Dark", ch=0.006)                            # sternum strip
    box(p, (0, -(si - 0.012), 1.09), (0.3, 0.02, 0.05), "Frame", ch=0.006)
    cyl(p, (0, 0, 0.86), 0.15 * ft, 2 * si * 0.9, "y", "Frame", 12)                                  # reactor housing
    cyl(p, (0, 0, 0.86), 0.09 * ft, 2 * si, "y", "Glow", 10)                                         # reactor core
    for sx in (-1, 1):                                                                               # coolant lines
        cable(p, [(sx * 0.16 * ft, 0, 0.86), (sx * (W / 2 - 0.2), 0, 0.74), (sx * (W / 2 - 0.1), 0, 0.74)], 0.014, clamp_every=1)

    # --- shoulder yoke + gimbals ---------------------------------------------------------------------
    box(p, (0, 0, 1.2), (2 * (shX - 0.11), 0.13 * ft, 0.13 * ft), "Frame", ch=0.022)
    sh_from, sh_to = col + 0.20, shX - 0.21
    for sx in (-1, 1):
        bar(p, (sx * 0.1 * ft, 0, 0.98), (sx * col, 0, 1.15), 0.055 * ft, "Frame")                   # gusset
        cyl(p, (sx * (shX - 0.115), 0, 1.2), 0.17 * jr, 0.04, "x", "Dark", 14)                       # shoulder gimbal ring
        cyl(p, (sx * (shX - 0.14), 0, 1.2), 0.2 * jr, 0.014, "x", "Frame", 14)
        for k in (-0.22, 0.22):                                                                      # pauldron mounting pins
            x = sx * ((sh_from + sh_to) / 2 + k * (sh_to - sh_from))
            hanger(p, (x, 0, 1.2), 0.44 * aw + 0.03, axis="y")

    # --- universal neck collar -----------------------------------------------------------------------
    cyl(p, (0, 0, 1.36), 0.085 * ft, 0.2, "z", "Frame", 10)
    cyl(p, (0, 0, 1.40), 0.19, 0.04, "z", "Dark", 16)
    cyl(p, (0, 0, 1.43), 0.15, 0.03, "z", "Frame", 16)

    # --- back weapon hardpoints (above and below the back armor) ----------------------------------------
    for sx in (-1, 1):
        bar(p, (sx * 0.09, 0.07, 1.34), (sx * 0.3, 0.36, 1.40), 0.07 * ft, "Frame")                   # pylon booms
        piston(p, (sx * 0.2, 0.33, 1.375), (sx * 0.075, 0.1, 1.315), 0.02 * ft, split=0.5)            # recoil buffers
        socket(p, (sx * 0.31, 0.38, 1.435), sk, "+z")                                                 # upper L / R
    box(p, (0, 0.38, 1.40), (0.78, 0.09, 0.07), "Frame", ch=0.014)                                    # cross beam
    box(p, (0, 0.43, 1.40), (0.24, 0.12, 0.09), "Frame", ch=0.016)                                    # centre pedestal
    socket(p, (0, 0.44, 1.445), sk * 1.3, "+z")                                                       # heavy centreline
    bar(p, (0, 0.09, 0.2), (0, 0.29, 0.12), 0.08, "Frame")                                            # lower spine boom
    box(p, (0, 0.29, 0.12), (0.14, 0.06, 0.1), "Frame", ch=0.012)
    socket(p, (0, 0.31, 0.12), sk * 0.9, "+y")                                                        # auxiliary mount
    return p


def humanoid_hardpoints(cls):
    """Socket mouths (Blender coords from the waist origin) with their weapon size class."""
    sizes = {"Light": ("Small", "Medium"), "Medium": ("Medium", "Large"), "Heavy": ("Large", "Large")}[cls]
    return [("BackUpperL", (-0.31, 0.38, 1.51), sizes[0]), ("BackUpperR", (0.31, 0.38, 1.51), sizes[0]),
            ("BackCentre", (0, 0.44, 1.52), sizes[1]), ("LowerSpine", (0, 0.39, 0.12), "Small" if cls == "Light" else "Medium")]


# =============================================================================== animal torso
def build_torso_animal(cls):
    C = CLASSES[cls]
    ft, aw, sk, legX = C["ft"], C["aw"], C["sock"], C["legX"]
    BW, BH, SY = 0.90 * aw, 0.70, 0.15      # body armor width / height, spine height above the hip line
    p = Part()

    def arch(f):
        return SY + 0.11 * (1 - (f / 1.02) ** 2)

    # --- arched spine: articulated vertebrae with dorsal processes ------------------------------------
    n = 10
    fs = [-1.0 + i * 2.0 / n for i in range(n + 1)]
    for a, b in zip(fs, fs[1:]):
        fa, fb = a + 0.018, b - 0.018
        bar(p, U(0, arch(fa), fa), U(0, arch(fb), fb), 0.17 * ft, "Frame")
        m = (a + b) / 2
        ubox(p, (0, arch(m) + 0.085 * ft + 0.025, m), (0.04, 0.06, 0.09), "Frame", ch=0.008)          # dorsal process
        ubox(p, (0, arch(m), m), (0.17 * ft + 0.09, 0.05, 0.06), "Dark", ch=0.008)                    # transverse lugs
    for f in fs[1:-1]:
        cyl(p, U(0, arch(f), f), 0.062 * ft, 0.05, "y", "Dark", 10)                                    # discs
    for sx in (-1, 1):                                                                                # heavy cable tracks
        cable(p, [U(sx * (0.085 * ft + 0.075), arch(f) - 0.035, f) for f in fs], 0.026, clamp_every=2)

    # --- ribs ---------------------------------------------------------------------------------------------
    rib_fs = (-0.7, -0.45, -0.22, 0.22, 0.45, 0.7) if cls != "Light" else (-0.6, -0.25, 0.25, 0.6)
    for f in rib_fs:
        ubox(p, (0, arch(f) - 0.03, f), (BW - 0.18, 0.05, 0.05), "Frame", ch=0.01)
        for sx in (-1, 1):
            ubox(p, (sx * (BW / 2 - 0.11), arch(f) - 0.17, f), (0.04, 0.28, 0.05), "Dark", ch=0.008)
    ubox(p, (0, -0.09, 0.0), (0.16 * ft, 0.06, 1.5), "Dark", ch=0.012)                                 # belly keel
    cyl(p, U(0, SY - 0.06, 0.45), 0.13 * ft, 0.3, "y", "Frame", 12)                                    # reactor
    cyl(p, U(0, SY - 0.06, 0.45), 0.08 * ft, 0.34, "y", "Glow", 10)

    # --- shoulder and hip girdles -------------------------------------------------------------------------
    for sz in (-1, 1):
        f = sz * LEG_Z
        ubox(p, (0, 0, f), (2 * legX - 0.2, 0.15 * ft, 0.15 * ft), "Frame", ch=0.02)                   # cross bar
        ubox(p, (0, 0.13, f), (0.46, 0.28, 0.1), "Frame", ch=0.03)                                     # girdle bulkhead
        for sx in (-1, 1):
            ubox(p, (sx * (legX - 0.135), 0, f), (0.07, 0.2 * ft, 0.2 * ft), "Dark", ch=0.016)         # carriage
            if cls == "Heavy":
                bar(p, U(sx * 0.2, 0.22, f), U(sx * 0.34, 0.03, f), 0.06, "Frame")

    # --- mid bulkhead ring at the armor seam: dorsal + flank hardpoints ------------------------------------
    top, bot, side = SY + BH / 2 + 0.02, SY - BH / 2 - 0.02, BW / 2 + 0.02
    c = min(BW, BH) * 0.18 + 0.01
    ubox(p, (0, top, 0), (BW - 2 * c + 0.04, 0.05, 0.08), "Frame", ch=0.012)
    ubox(p, (0, bot, 0), (BW - 2 * c + 0.04, 0.05, 0.08), "Frame", ch=0.012)
    for sx in (-1, 1):
        ubox(p, (sx * side, SY, 0), (0.05, BH - 2 * c + 0.04, 0.08), "Frame", ch=0.012)
        for sy in (-1, 1):
            y_edge = top if sy > 0 else bot
            bar(p, U(sx * (BW / 2 - c), y_edge, 0), U(sx * side, SY + sy * (BH / 2 - c), 0), 0.06, "Dark")
        socket(p, U(sx * (side + 0.02), SY, 0), sk, "+x" if sx > 0 else "-x")                          # flank mounts
    ubox(p, (0, top + 0.035, 0), (0.26, 0.05, 0.2), "Frame", ch=0.014)                                 # dorsal pedestal
    socket(p, U(0, top + 0.06, 0), sk * 1.3, "+z")                                                     # dorsal mount
    for sf in (-1, 1):                                                                                 # dorsal recoil buffers
        piston(p, U(0.09 * sf, top + 0.045, 0.09 * sf), U(0.2 * sf, top + 0.005, 0.02 * sf), 0.016, split=0.5, eye_axis=(0, 1, 0))

    # --- neck boom + tail mount -----------------------------------------------------------------------------
    bar(p, U(0, arch(0.96), 0.96), U(0, SY + 0.18, 1.1), 0.15 * ft, "Frame")
    cyl(p, U(0, SY + 0.14, 1.045), 0.15, 0.03, "y", "Dark", 14)                                        # neck collar
    bar(p, U(0, arch(-0.98), -0.98), U(0, SY + 0.06, -1.09), 0.1 * ft, "Frame")
    socket(p, U(0, SY + 0.06, -1.09), sk * 0.85, "+y")                                                 # tail mount
    return p


def animal_hardpoints(cls):
    """Socket mouths (Unity-style x, up, fwd from the hip-line origin) with their weapon size class."""
    C = CLASSES[cls]
    BW, top = 0.90 * C["aw"], 0.15 + 0.35 + 0.02
    sizes = {"Light": ("Small", "Medium"), "Medium": ("Medium", "Large"), "Heavy": ("Large", "Large")}[cls]
    return [("Dorsal", (0, top + 0.135, 0), sizes[1]), ("FlankL", (-(BW / 2 + 0.115), 0.15, 0), sizes[0]),
            ("FlankR", (BW / 2 + 0.115, 0.15, 0), sizes[0]), ("Tail", (0, 0.21, -1.165), "Small")]


# =============================================================================== export
def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO")


def build_all(do_export=True):
    ic.reset()
    made = {}

    def add(name, part):
        obj = part.to_object(name)
        made[name] = obj
        if do_export:
            export(obj, os.path.join(OUT_DIR, name + ".fbx"))

    add("Skel_Joint", build_joint(False))
    add("Skel_JointCore", build_joint(True))
    add("Skel_Hand", build_hand())
    add("Skel_Head", build_head())
    add("Skel_HeadAnimal", build_head_animal())
    for cls in CLASSES:
        add(f"Skel_TorsoHumanoid_{cls}", build_torso_humanoid(cls))
        add(f"Skel_TorsoAnimal_{cls}", build_torso_animal(cls))
        add(f"Skel_Foot_{cls}", build_foot(cls))
        for spec in limb_specs(cls):
            add(f"Skel_{spec[0]}_{cls}", build_limb(cls, *spec[1:]))
    return made


if __name__ == "__main__":
    made = build_all(do_export="--no-export" not in sys.argv)
    if "--blend" in sys.argv:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "skeleton.blend"))
    print("BUILT", len(made))
