"""
IronFauna - hand-built Ironclad clamshell set (Set01) in Blender.

Authoring convention (matches ClamshellMeshLibrary):
  X = plate width, Z = along the bone, the plate sits on the -Y side (outward/front),
  inner seam face at Y = 0, outer details reach Y = -1.6 * envelope depth.
Back halves are authored the same way (outward = -Y); Unity mirrors them onto the back.
Materials name the colour role: Shell, Panel, Hazard, Dark, Frame, Glow.
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

OUT_DIR = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else "/tmp/ironclad"
os.makedirs(OUT_DIR, exist_ok=True)

ROLE_COLORS = {
    "Shell": (0.58, 0.03, 0.04, 1),
    "Panel": (0.87, 0.84, 0.76, 1),
    "Hazard": (0.79, 0.48, 0.0, 1),
    "Dark": (0.017, 0.02, 0.026, 1),
    "Frame": (0.042, 0.05, 0.06, 1),
    "Glow": (0.02, 0.78, 1.0, 1),
}
ROLE_ORDER = list(ROLE_COLORS.keys())


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for role, col in ROLE_COLORS.items():
        m = bpy.data.materials.new(role)
        m.diffuse_color = col
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = col
            bsdf.inputs["Roughness"].default_value = 0.55
            if role == "Glow":
                bsdf.inputs["Emission Color"].default_value = col
                bsdf.inputs["Emission Strength"].default_value = 4.0


class Part:
    """Collects convex pieces into one bmesh, each piece tagged with a material role."""

    def __init__(self):
        self.bm = bmesh.new()

    # ---------------------------------------------------------------- core loft
    def loft(self, section, stations, role, offset=(0, 0, 0), sign_out=-1.0):
        """section: [(x, d)] convex polygon (d = outward distance); stations: [(z, sx, sd)].
        Point -> (x*sx + ox, sign_out*(d*sd) + oy, z + oz)."""
        ox, oy, oz = offset
        rings = []
        for z, sx, sd in stations:
            ring = [self.bm.verts.new((x * sx + ox, sign_out * (d * sd) + oy, z + oz)) for x, d in section]
            rings.append(ring)
        mi = ROLE_ORDER.index(role)
        faces = []
        n = len(section)
        for a, b in zip(rings, rings[1:]):
            for i in range(n):
                j = (i + 1) % n
                quad = [a[i], a[j], b[j], b[i]]
                uniq = []
                for v in quad:
                    if all((v.co - u.co).length > 1e-7 for u in uniq):
                        uniq.append(v)
                if len(uniq) >= 3:
                    try:
                        faces.append(self.bm.faces.new(uniq))
                    except ValueError:
                        pass
        for ring in (rings[0], rings[-1]):
            pts = []
            for v in ring:
                if all((v.co - u.co).length > 1e-7 for u in pts):
                    pts.append(v)
            if len(pts) >= 3:
                try:
                    faces.append(self.bm.faces.new(pts))
                except ValueError:
                    pass
        for f in faces:
            f.material_index = mi
        # orient this piece's faces outward from its own centroid (each piece is convex)
        verts = {v for f in faces for v in f.verts}
        c = sum((v.co for v in verts), Vector()) / max(1, len(verts))
        for f in faces:
            f.normal_update()
            if f.normal.dot(f.calc_center_median() - c) < 0:
                f.normal_flip()
        return faces

    @staticmethod
    def octagon(w, d, ch, base=0.0):
        """Chamfered rectangle in (x, outward) with its inner face at outward = base."""
        hw = w / 2
        ch = min(ch, hw * 0.9, d * 0.45)
        return [(-hw + ch, base), (hw - ch, base), (hw, base + ch), (hw, base + d - ch),
                (hw - ch, base + d), (-hw + ch, base + d), (-hw, base + d - ch), (-hw, base + ch)]

    # ---------------------------------------------------------------- shapes
    def plate(self, w, L, d, base, role="Shell", bevel=None, end_w=0.55, bulge=1.06, ch=None, z0=0.0, x0=0.0):
        """Armor plate: half-octagon section with a slight crown, both ends beveled 45 degrees."""
        bevel = min(bevel if bevel is not None else d * 0.85, L * 0.3)
        ch = ch if ch is not None else min(w * 0.5, d) * 0.4
        hw = w / 2
        sec = [(-hw, 0), (-hw, d - ch), (-hw + ch, d), (hw - ch, d), (hw, d - ch), (hw, 0)]
        h = L / 2
        end_d = max(0.05, (d - bevel) / d)
        st = [(-h, end_w, end_d), (-h + bevel, 1.0, 1.0), (-h * 0.25, 1.0, bulge), (h * 0.25, 1.0, bulge),
              (h - bevel, 1.0, 1.0), (h, end_w, end_d)]
        return self.loft(sec, st, role, offset=(x0, -base, z0))

    def block(self, w, L, d, base, role, ch=None, x0=0.0, z0=0.0, taper=1.0):
        ch = ch if ch is not None else min(w, L, d) * 0.22
        sec = self.octagon(w, d, ch)
        h = L / 2
        b = min(ch, h * 0.9)
        st = [(-h, (w - 2 * b) / w * taper, (d - b) / d), (-h + b, taper, 1.0), (h - b, 1.0, 1.0), (h, (w - 2 * b) / w, (d - b) / d)]
        return self.loft(sec, st, role, offset=(x0, -base, z0))

    def prism(self, r, length, base, role, axis="y", sides=6, x0=0.0, z0=0.0):
        """Cylinder. axis 'y' = pointing outward (bolts), 'z' = along the bone, 'x' = across."""
        circ = [(math.cos((i + 0.5) / sides * 2 * math.pi) * r, math.sin((i + 0.5) / sides * 2 * math.pi) * r) for i in range(sides)]
        if axis == "y":
            faces = []
            rings = []
            for d in (0.0, length):
                rings.append([self.bm.verts.new((x0 + cx, -(base + d), z0 + cz)) for cx, cz in circ])
            mi = ROLE_ORDER.index(role)
            for i in range(sides):
                j = (i + 1) % sides
                faces.append(self.bm.faces.new([rings[0][i], rings[0][j], rings[1][j], rings[1][i]]))
            faces.append(self.bm.faces.new(rings[0]))
            faces.append(self.bm.faces.new(rings[1]))
            c = Vector((x0, -(base + length / 2), z0))
            for f in faces:
                f.material_index = mi
                f.normal_update()
                if f.normal.dot(f.calc_center_median() - c) < 0:
                    f.normal_flip()
            return faces
        if axis == "z":
            sec = [(cx, cy + r) for cx, cy in circ]
            return self.loft(sec, [(-length / 2, 1, 1), (length / 2, 1, 1)], role, offset=(x0, -base, z0))
        # axis x: section in (z, outward), swept along x
        faces = []
        rings = []
        for x in (-length / 2, length / 2):
            rings.append([self.bm.verts.new((x0 + x, -(base + r + cy), z0 + cx)) for cx, cy in circ])
        mi = ROLE_ORDER.index(role)
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(self.bm.faces.new([rings[0][i], rings[0][j], rings[1][j], rings[1][i]]))
        faces.append(self.bm.faces.new(rings[0]))
        faces.append(self.bm.faces.new(rings[1]))
        c = Vector((x0, -(base + r), z0))
        for f in faces:
            f.material_index = mi
            f.normal_update()
            if f.normal.dot(f.calc_center_median() - c) < 0:
                f.normal_flip()
        return faces

    def wedge_ridge(self, w, L, h, base, role, z0=0.0, x0=0.0, start=0.15):
        sec = [(-w / 2, 0), (-w * 0.12, h), (w * 0.12, h), (w / 2, 0)]
        sec = [(x, y) for x, y in sec]
        return self.loft(sec, [(-L / 2, 1, start), (-L * 0.2, 1, 1), (L / 2, 0.6, 0.85)], role, offset=(x0, -base, z0))

    def hazard_band(self, w, L, d, base, z0=0.0, n=5, x0=0.0):
        """Alternating yellow/dark slanted slats."""
        slat = w / n
        for i in range(n):
            role = "Hazard" if i % 2 == 0 else "Dark"
            x = x0 - w / 2 + slat * (i + 0.5)
            self.block(slat * 1.02, L, d, base, role, ch=d * 0.2, x0=x, z0=z0)

    def bolts(self, pts, base, r=0.018, role="Dark"):
        for x, z in pts:
            self.prism(r, r * 0.9, base, role, axis="y", sides=6, x0=x, z0=z)

    def latch_tabs(self, w, L):
        """Toggle-latch flanges at the seam on both sides."""
        for sx in (-1, 1):
            for sz in (-1, 1):
                self.block(0.04, L * 0.16, 0.06, -0.03, "Frame", ch=0.008, x0=sx * (w / 2 + 0.015), z0=sz * L * 0.24)

    # ---------------------------------------------------------------- finish
    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        for role in ROLE_ORDER:
            me.materials.append(bpy.data.materials[role])
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        for p in me.polygons:
            p.use_smooth = False
        bev = obj.modifiers.new("Chamfer", "BEVEL")
        bev.width = 0.004
        bev.segments = 1
        bev.limit_method = "ANGLE"
        bev.angle_limit = math.radians(35)
        return obj


# ======================================================================== parts
# Envelopes (Unity): width x length x (1.6 x depth)
ENV = {
    "Thigh": (0.46, 0.73, 0.22),
    "Calf": (0.42, 0.73, 0.22),
    "Body": (1.00, 1.06, 0.30),
    "Head": (0.56, 0.54, 0.15),
    "Shoulder": (0.44, 0.35, 0.18),
    "UpperArm": (0.34, 0.34, 0.15),
    "LowerArm": (0.36, 0.33, 0.16),
    "Hand": (0.30, 0.20, 0.12),
}


def limb_front(p, w, L, d, stripe=True, ridge=False, rail=False, layer_z=0.06, layer_len=0.62, layer_h=0.32):
    """Front plate: main crowned plate, a second armor layer, panel seam, stripe, bolts.
    layer_z / layer_len place the raised layer along the bone (legs keep it away from the knee)."""
    p.plate(w, L, d, 0.0)
    p.plate(w * 0.74, L * layer_len, d * layer_h, d * 0.98, bevel=d * min(0.28, layer_h * 0.85), end_w=0.7, z0=L * layer_z)
    # panel seam groove across the lower third
    p.block(w * 0.62, L * 0.025, d * 0.06, d * 1.02, "Dark", ch=0.002, z0=-L * 0.27)
    if stripe:
        p.block(w * 0.11, L * layer_len * 0.8, d * 0.08, d * (0.97 + layer_h), "Panel", ch=0.004, x0=w * 0.22, z0=L * layer_z)
    if ridge:
        p.wedge_ridge(w * 0.3, L * layer_len * 0.9, d * 0.22, d * (0.95 + layer_h), "Shell", z0=L * layer_z)
    if rail:
        for sx in (-1, 1):
            p.block(w * 0.06, L * 0.6, d * 0.12, d * 1.27, "Frame", ch=0.004, x0=sx * w * 0.2)
    p.bolts([(-w * 0.31, L * 0.3), (w * 0.31, L * 0.3), (-w * 0.31, -L * 0.18), (w * 0.31, -L * 0.18)], d * 1.0, r=w * 0.035)
    p.latch_tabs(w, L)


def limb_back(p, w, L, d, vents=5, hazard=True):
    """Back plate: crowned plate, recessed vent bank, hazard block."""
    p.plate(w, L, d, 0.0)
    # vent housing + slats
    p.block(w * 0.62, L * 0.42, d * 0.14, d * 0.98, "Frame", ch=0.006, z0=L * 0.12)
    for i in range(vents):
        z = L * 0.12 - L * 0.17 + i * (L * 0.34 / max(1, vents - 1))
        p.block(w * 0.54, L * 0.025, d * 0.12, d * 1.1, "Dark", ch=0.002, z0=z)
    if hazard:
        p.hazard_band(w * 0.66, L * 0.12, d * 0.1, d * 0.98, z0=-L * 0.26)
    p.bolts([(-w * 0.34, L * 0.34), (w * 0.34, L * 0.34)], d * 1.0, r=w * 0.03)
    p.latch_tabs(w, L)


def build_thigh_front():
    # Raised layer sits on the hip half of the plate so a deep reverse fold can't close it on the calf.
    p = Part(); w, L, d = ENV["Thigh"]; limb_front(p, w, L, d, layer_z=0.16, layer_len=0.5, layer_h=0.24); return p


def build_thigh_back():
    p = Part(); w, L, d = ENV["Thigh"]; limb_back(p, w, L, d); return p


def build_calf_front():
    p = Part(); w, L, d = ENV["Calf"]; limb_front(p, w, L, d, stripe=False, ridge=True, layer_z=-0.04, layer_len=0.46, layer_h=0.2); return p


def build_calf_back():
    p = Part(); w, L, d = ENV["Calf"]
    p.plate(w, L, d, 0.0)
    # hydraulic piston down the calf
    p.prism(w * 0.1, L * 0.5, d * 0.95, "Frame", axis="z", sides=8, z0=L * 0.05)
    p.prism(w * 0.13, L * 0.12, d * 0.92, "Hazard", axis="z", sides=8, z0=L * 0.24)
    p.block(w * 0.7, L * 0.08, d * 0.12, d * 0.98, "Dark", ch=0.004, z0=-L * 0.28)
    p.latch_tabs(w, L)
    return p


def build_body_front():
    p = Part(); w, L, d = ENV["Body"]
    p.plate(w, L, d, 0.0, bevel=d * 0.7, end_w=0.75)
    # twin pectoral armor plates
    for sx in (-1, 1):
        p.plate(w * 0.38, L * 0.42, d * 0.28, d * 1.0, bevel=d * 0.22, end_w=0.7, x0=sx * w * 0.22, z0=L * 0.16)
    # central keel ridge and cockpit slit
    p.wedge_ridge(w * 0.14, L * 0.7, d * 0.34, d * 1.0, "Frame", z0=-L * 0.02)
    p.block(w * 0.3, L * 0.035, d * 0.06, d * 1.28, "Glow", ch=0.003, z0=L * 0.3)
    # abdominal plates
    for i in range(3):
        p.block(w * (0.5 - i * 0.06), L * 0.07, d * 0.18, d * 1.0, "Shell", ch=0.01, z0=-L * 0.12 - i * L * 0.095)
    p.block(w * 0.09, L * 0.55, d * 0.06, d * 1.28, "Panel", ch=0.004, x0=-w * 0.4, z0=L * 0.05)
    p.bolts([(sx * w * 0.42, sz * L * 0.36) for sx in (-1, 1) for sz in (-1, 1)], d * 1.0, r=0.024)
    p.latch_tabs(w, L)
    return p


def build_body_back():
    p = Part(); w, L, d = ENV["Body"]
    p.plate(w, L, d, 0.0, bevel=d * 0.7, end_w=0.75)
    # reactor backpack with twin vent banks
    p.block(w * 0.62, L * 0.55, d * 0.4, d * 0.98, "Frame", ch=0.02, z0=L * 0.06)
    for sx in (-1, 1):
        for i in range(6):
            p.block(w * 0.22, L * 0.022, d * 0.1, d * 1.36, "Dark", ch=0.002, x0=sx * w * 0.15, z0=L * 0.24 - i * L * 0.07)
    p.block(w * 0.08, L * 0.3, d * 0.08, d * 1.36, "Glow", ch=0.003, z0=L * 0.06)
    p.hazard_band(w * 0.7, L * 0.08, d * 0.1, d * 0.98, z0=-L * 0.32, n=7)
    p.latch_tabs(w, L)
    return p


def build_head_front():
    p = Part(); w, L, d = ENV["Head"]
    p.plate(w, L, d, 0.0, bevel=d * 0.6, end_w=0.8)
    # brow overhang, visor slit, cheek blocks, chin
    p.block(w * 0.92, L * 0.12, d * 0.5, d * 0.95, "Shell", ch=0.012, z0=L * 0.28)
    p.block(w * 0.66, L * 0.09, d * 0.12, d * 1.0, "Glow", ch=0.004, z0=L * 0.14)
    for sx in (-1, 1):
        p.block(w * 0.2, L * 0.26, d * 0.42, d * 0.98, "Shell", ch=0.01, x0=sx * w * 0.32, z0=-L * 0.06)
        for i in range(3):
            p.block(w * 0.12, L * 0.02, d * 0.08, d * 1.38, "Dark", ch=0.002, x0=sx * w * 0.32, z0=-L * 0.0 - i * L * 0.06)
    p.block(w * 0.36, L * 0.12, d * 0.36, d * 0.98, "Frame", ch=0.01, z0=-L * 0.3)
    p.latch_tabs(w, L)
    return p


def build_head_back():
    p = Part(); w, L, d = ENV["Head"]
    p.plate(w, L, d, 0.0, bevel=d * 0.6, end_w=0.8)
    p.wedge_ridge(w * 0.16, L * 0.8, d * 0.55, d * 1.0, "Shell", z0=L * 0.05, start=0.4)
    p.hazard_band(w * 0.5, L * 0.08, d * 0.08, d * 0.98, z0=-L * 0.3)
    p.latch_tabs(w, L)
    return p


def build_shoulder_front():
    p = Part(); w, L, d = ENV["Shoulder"]
    p.plate(w, L, d, 0.0, bevel=d * 0.55, end_w=0.8)
    p.plate(w * 0.8, L * 0.7, d * 0.3, d * 1.0, bevel=d * 0.2, end_w=0.75)
    p.block(w * 0.1, L * 0.6, d * 0.08, d * 1.3, "Panel", ch=0.004, x0=w * 0.24)
    p.bolts([(-w * 0.3, L * 0.3), (-w * 0.3, -L * 0.3)], d * 1.0, r=0.02)
    p.latch_tabs(w, L)
    return p


def build_shoulder_back():
    p = Part(); w, L, d = ENV["Shoulder"]
    p.plate(w, L, d, 0.0, bevel=d * 0.55, end_w=0.8)
    p.hazard_band(w * 0.7, L * 0.2, d * 0.1, d * 0.98, z0=0.0)
    p.latch_tabs(w, L)
    return p


def build_upperarm_front():
    p = Part(); w, L, d = ENV["UpperArm"]; limb_front(p, w, L, d); return p


def build_upperarm_back():
    p = Part(); w, L, d = ENV["UpperArm"]; limb_back(p, w, L, d, vents=4); return p


def build_lowerarm_front():
    p = Part(); w, L, d = ENV["LowerArm"]; limb_front(p, w, L, d, stripe=False, rail=True); return p


def build_lowerarm_back():
    p = Part(); w, L, d = ENV["LowerArm"]; limb_back(p, w, L, d, vents=3, hazard=True); return p


def build_hand_front():
    p = Part(); w, L, d = ENV["Hand"]
    p.plate(w, L, d, 0.0, bevel=d * 0.5, end_w=0.8)
    for i in range(4):
        x = -w * 0.33 + i * w * 0.22
        p.block(w * 0.18, L * 0.32, d * 0.5, d * 0.95, "Frame", ch=0.008, x0=x, z0=-L * 0.3)
    p.hazard_band(w * 0.6, L * 0.14, d * 0.1, d * 0.98, z0=L * 0.22, n=5)
    return p


def build_hand_back():
    p = Part(); w, L, d = ENV["Hand"]
    p.plate(w, L, d, 0.0, bevel=d * 0.5, end_w=0.8)
    p.block(w * 0.6, L * 0.5, d * 0.25, d * 0.98, "Dark", ch=0.01, z0=0.0)
    return p


PARTS = {
    "ThighFront": build_thigh_front, "ThighBack": build_thigh_back,
    "CalfFront": build_calf_front, "CalfBack": build_calf_back,
    "BodyFront": build_body_front, "BodyBack": build_body_back,
    "HeadFront": build_head_front, "HeadBack": build_head_back,
    "ShoulderFront": build_shoulder_front, "ShoulderBack": build_shoulder_back,
    "UpperArmFront": build_upperarm_front, "UpperArmBack": build_upperarm_back,
    "LowerArmFront": build_lowerarm_front, "LowerArmBack": build_lowerarm_back,
    "HandFront": build_hand_front, "HandBack": build_hand_back,
}


def export_all():
    reset()
    objs = {}
    for slot, fn in PARTS.items():
        obj = fn().to_object(f"{slot}_Set01_Ironclad")
        objs[slot] = obj
    stats = {}
    for slot, obj in objs.items():
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        path = os.path.join(OUT_DIR, f"{slot}_Set01_Ironclad.fbx")
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
            axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
            path_mode="AUTO",
        )
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        stats[slot] = len(ev.data.polygons)
    return objs, stats


if __name__ == "__main__":
    objs, stats = export_all()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "ironclad_set.blend"))
    print("EXPORTED", stats)
