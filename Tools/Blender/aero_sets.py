"""
IronFauna - all ten armor sets in the second-generation (aerodynamic, angular) shape language.

  blender -b -P aero_sets.py -- --out <dir> [--blend]
  python aero_sets.py --out <dir> [--blend]            (with the pip `bpy` module)

Builds every armor part (220): Set01 from aero_ironclad.py; sets 02-10 as the same dart + hood base with each
set's signature feature (from clamshell_sets.py: front_sig / back_sig / eyes / head_back) mounted on a flat deck
on the hood; one pointed toe and heel for every set; quadruped bodies and animal heads on a faceted fuselage shell.
"""
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ironclad as ic  # noqa: E402
from ironclad import ENV, Part  # noqa: E402
import aero_ironclad as A  # noqa: E402
import clamshell_sets as cs  # noqa: E402
from aero_ironclad import lx, keel, blade, box_sec, dart  # noqa: E402

OUT_DIR = ic.OUT_DIR if "--out" in sys.argv else "/tmp/aero_sets"
os.makedirs(OUT_DIR, exist_ok=True)


def deck(w, top, skirt):
    """Hood section with a flat deck (0.64 of the width) for a set's signature feature, chamfered flanks."""
    hw = w / 2
    return [(-hw, -skirt), (-hw, top * 0.4), (-hw * 0.64, top), (hw * 0.64, top), (hw, top * 0.4), (hw, -skirt)]


def deck_hood(p, w, L, tip, zc, top, base):
    st = [(zc + tip * 0.22 * L, 0.35, 0.3), (zc + tip * 0.02 * L, 1.0, 1.0), (zc - tip * 0.2 * L, 1.0, 1.0), (zc - tip * 0.3 * L, 0.85, 0.7)]
    lx(p, deck(w * 0.97, top * 0.86, base), [(z + tip * 0.025 * L, sx, sd) for z, sx, sd in st], "Dark", off=(0, -base, 0))
    lx(p, deck(w, top, base), st, "Shell", off=(0, -base, 0))


def sig_ctx(kind, w, L, d, zc, zl, hb, ww=0.58):
    return cs.Ctx(kind, w, L, d, zc=zc, zl=zl, hb=hb, ww=w * ww)


# ------------------------------------------------------------------------------- limbs
def limb(S, kind, front):
    w, L, d = ENV[kind]
    tip, zcf, hf, hk = A.LIMBS[kind]
    zc = zcf * L
    zs = zc - tip * 0.09 * L                      # centre of the flat deck
    p = Part()
    if front:
        lx(p, keel(w, d * 0.42, d * 0.92), dart(L, tip), "Shell")
        deck_hood(p, w, L, tip, zc, d * 0.2, d * 0.80)
        S.front_sig(p, sig_ctx(kind, w, L, d, zs, 0.30 * L, d * hf), d * 1.0, d * 0.18)
        for sx in (-1, 1):
            lx(p, blade(0.03, d * 0.95, d * 1.1), [(zc + tip * 0.02 * L, 1.0, 0.75), (zc - tip * 0.27 * L, 1.0, 1.0)], "Frame", off=(sx * (w / 2 - 0.05), 0, 0))
    else:
        lx(p, keel(w, d * 0.4, d * 0.78), dart(L, tip), "Shell")
        deck_hood(p, w, L, tip, zc, d * 0.16, d * 0.62)
        S.back_sig(p, sig_ctx(kind, w, L, d, zs, 0.30 * L, d * hk), d * 0.78)
    p.latch_tabs(w, L)
    return p


def shoulder(S, front):
    w, L, d = ENV["Shoulder"]
    h = L / 2
    p = Part()
    st = [(-h, 0.78, 0.55), (-0.1 * L, 1.0, 0.95), (0.2 * L, 0.95, 1.0), (h - 0.06, 0.6, 0.8), (h, 0.2, 0.4)]
    if front:
        lx(p, keel(w, d * 0.5, d), st, "Shell")
        lx(p, deck(w * 0.74, d * 0.2, d * 0.4), [(-0.3 * L, 0.8, 0.7), (-0.18 * L, 1.0, 1.0), (0.2 * L, 1.0, 1.0), (0.4 * L, 0.3, 0.5)], "Shell", off=(0, -d * 0.78, 0))
        S.front_sig(p, sig_ctx("Shoulder", w, L, d, 0.0, 0.34 * L, d * 0.45, ww=0.42), d * 0.98, d * 0.2)
    else:
        lx(p, keel(w, d * 0.4, d * 0.7), st, "Shell")
        lx(p, deck(w * 0.7, d * 0.12, d * 0.3), [(-0.3 * L, 0.8, 0.7), (-0.18 * L, 1.0, 1.0), (0.22 * L, 1.0, 1.0), (0.36 * L, 0.5, 0.6)], "Shell", off=(0, -d * 0.6, 0))
        S.back_sig(p, sig_ctx("Shoulder", w, L, d, 0.0, 0.34 * L, d * 0.4, ww=0.4), d * 0.72)
    p.latch_tabs(w, L)
    return p


# ------------------------------------------------------------------------------- torso
def body(S, front):
    w, L, d = ENV["Body"]
    p = Part()
    if front:
        lx(p, keel(w, d * 0.5, d * 0.95, flat=0.04), A.BODY_ST(L), "Shell")
        deck_hood(p, w, L, -1, 0.2 * L, d * 0.26, d * 0.72)
        S.front_sig(p, sig_ctx("Body", w, L, d, 0.25 * L, 0.3 * L, d * 0.5, ww=0.4), d * 0.98, d * 0.28)
        p.block(w * 0.16, L * 0.02, d * 0.1, d * 0.8, "Glow", ch=0.004, z0=-0.065 * L)                  # sensor slit under the chevron
        for sx in (-1, 1):
            p.block(w * 0.05, L * 0.16, d * 0.2, d * 0.42, "Dark", ch=0.006, x0=sx * w * 0.27, z0=-0.24 * L)   # waist intakes
        for i in range(3):                                                                              # lapped abdominal chevrons
            z = -0.17 * L - i * 0.105 * L
            lx(p, keel(w * (0.5 - i * 0.06), d * 0.1, d * 0.26, skirt=d * 0.4), [(z - 0.065 * L, 0.7, 0.55), (z + 0.01 * L, 1.0, 1.0), (z + 0.06 * L, 0.92, 0.75)],
               "Shell", off=(0, -d * (0.66 - i * 0.1), 0))
        p.block(w * 0.46, L * 0.045, d * 0.3, d * 0.36, "Frame", ch=0.012, z0=0.445 * L)                 # collar guard
    else:
        lx(p, keel(w, d * 0.45, d * 0.8, flat=0.08), A.BODY_ST(L), "Shell")
        deck_hood(p, w, L, -1, 0.24 * L, d * 0.2, d * 0.66)
        S.back_sig(p, sig_ctx("Body", w, L, d, 0.24 * L, 0.36 * L, d * 0.5, ww=0.56), d * 0.86)
        p.hazard_band(w * 0.26, L * 0.022, 0.012, d * 0.5, z0=-0.41 * L, n=5)
    p.latch_tabs(w, L)
    return p


# ------------------------------------------------------------------------------- head
def head_base(p, back=False):
    """Wedge helm with a flat face (so each set's eyes sit flush), narrowing to the chin."""
    w, L, d = ENV["Head"]
    h, hw = L / 2, w / 2
    top = d * (0.92 if back else 1.0)
    sec = [(-hw, 0), (-hw, top * 0.5), (-hw * 0.6, top), (hw * 0.6, top), (hw, top * 0.5), (hw, 0)]
    lx(p, sec, [(-h, 0.45, 0.6), (-0.2 * L, 0.8, 1.0), (0.27 * L, 1.0, 1.0), (h - 0.08 * L, 0.9, 0.75), (h, 0.62, 0.4)], "Shell")
    return w, L, d


def head(S, front):
    p = Part()
    if not front:
        S.head_base = lambda q: (head_base(q, True), ENV["Head"])[1]     # the set's own back details go on the wedge base
        S.head_back(p)
        return p
    w, L, d = head_base(p)
    lx(p, keel(w * 0.92, d * 0.25, d * 0.5, skirt=d * 0.5), [(0.21 * L, 0.9, 1.0), (0.34 * L, 1.0, 0.75), (0.45 * L, 0.8, 0.4)], "Shell", off=(0, -d * 0.85, 0))   # raked brow
    S.eyes(p, w, L, d)
    for sx in (-1, 1):                                                                                   # swept cheek guards
        lx(p, keel(w * 0.26, d * 0.1, d * 0.3, skirt=d * 0.5),
           [(-0.34 * L, 0.4, 0.5, sx * w * 0.2), (-0.12 * L, 1.0, 1.0, sx * w * 0.32), (-0.02 * L, 0.9, 0.8, sx * w * 0.34)], "Shell", off=(0, -d * 0.72, 0))
    p.block(w * 0.16, L * 0.1, d * 0.2, d * 0.82, "Dark", ch=0.006, z0=-0.31 * L)                         # chin intake
    p.latch_tabs(w, L)
    return p


# ------------------------------------------------------------------------------- quadruped shells
def fuselage(p, W, L, H, role="Shell", tip_len=0.22, tip=0.55, seam=1.0, tip_x=None, ch_frac=0.18):
    """Replaces the boxy lengthwise shell: faceted fuselage - flat spine deck, canted shoulders, tucked belly -
    drawn down to a narrow nose. Same signature as SetStyle.lw_shell so every animal detail still fits."""
    hw, hh = W / 2, H / 2
    sec = [(-hw * 0.55, -hh), (hw * 0.55, -hh), (hw, -hh * 0.35), (hw, hh * 0.3), (hw * 0.62, hh), (-hw * 0.62, hh), (-hw, hh * 0.3), (-hw, -hh * 0.35)]
    tx = tip if tip_x is None else tip_x
    st = [(-L / 2, seam, seam), (-L / 2 + L * 0.25, 1.0, 1.0), (L / 2 - tip_len * 1.6, 0.92, 0.94), (L / 2, tx * 0.7, tip * 0.8)]
    return p.loft(sec, st, role, offset=(0, -hh, 0))


cs.SetStyle.lw_shell = staticmethod(fuselage)


def _volt_body(self, p, W, L, H, front):
    """Volt-Viper on the fuselage: segment bands follow the faceted section instead of boxing it in."""
    for i in range(5):
        z = -L * 0.3 + i * L * 0.15
        hw, hh = W / 2 * 1.03, H / 2 * 1.03
        sec = [(-hw * 0.55, -hh), (hw * 0.55, -hh), (hw, -hh * 0.35), (hw, hh * 0.3), (hw * 0.62, hh), (-hw * 0.62, hh), (-hw, hh * 0.3), (-hw, -hh * 0.35)]
        p.loft(sec, [(z - 0.015, 1, 1), (z + 0.015, 1, 1)], "Dark", offset=(0, -H / 2, 0))
    for i in range(4):
        p.prism(0.06 if i % 2 == 0 else 0.045, 0.06, H * 0.98, "Copper" if i % 2 == 0 else "Dark", axis="z", sides=10, z0=L * 0.05 + i * 0.065)


cs.Voltaic.animal_body = _volt_body


# ------------------------------------------------------------------------------- build
def set_parts(S):
    out = {}
    for kind in A.LIMBS:
        out[kind + "Front"], out[kind + "Back"] = limb(S, kind, True), limb(S, kind, False)
    out["ShoulderFront"], out["ShoulderBack"] = shoulder(S, True), shoulder(S, False)
    out["BodyFront"], out["BodyBack"] = body(S, True), body(S, False)
    out["HeadFront"], out["HeadBack"] = head(S, True), head(S, False)
    out["HandFront"], out["HandBack"] = A.build_hand(True), A.build_hand(False)
    out["FootFront"], out["FootBack"] = A.build_foot(True), A.build_foot(False)
    return out


if __name__ == "__main__":
    ic.reset()
    made = 0
    export = "--no-export" not in sys.argv
    for S in cs.STYLES:
        tag = f"Set{S.idx:02d}_{S.name}"
        parts = A.parts() if S.idx == 1 else set_parts(S)
        for slot in ("QuadBodyFront", "QuadBodyBack", "AnimalHeadFront", "AnimalHeadBack"):
            parts[slot] = cs.build_lw_part(S, slot)
        for slot, part in parts.items():
            obj = part.to_object(f"{slot}_{tag}")
            if export:
                A.export(obj, os.path.join(OUT_DIR, f"{slot}_{tag}.fbx"))
            made += 1
    if "--blend" in sys.argv:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "aero_sets.blend"))
    print("BUILT", made)
