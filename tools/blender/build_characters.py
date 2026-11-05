"""
Djancoeg Tournament - character sculpt, rig & animation pipeline for Blender (5.x).

Builds every fighter as a low-poly "toy figure": vertex-coloured primitive parts rigidly
skinned to a shared 12-bone humanoid rig, then keys four actions (idle, run, shoot, die)
as NLA tracks and exports one glTF binary per character to assets/models/characters/.

Run inside Blender (Blender MCP `execute_blender_code`, or the Scripting tab):
    exec(open(r"<repo>/tools/blender/build_characters.py").read()); build_all()
or headless:
    blender -b --python tools/blender/build_characters.py -- all

Conventions: Blender Z-up, characters face -Y (glTF +Z = game forward), feet at origin,
1.8 m tall. Colours are stored as raw sRGB in a FLOAT_COLOR attribute so they arrive in
the game exactly as authored. Made by Ariana Mischa Fadhila of Syubadubin Studios.
"""
import bpy, bmesh, math, os, sys
from mathutils import Matrix, Vector, Euler

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__) if "__file__" in globals() else ".", "..", ".."))
if not os.path.isdir(os.path.join(REPO, "assets")):
    REPO = r"C:/experiment/syubadubin/DjancoegTournament"
OUT = os.path.join(REPO, "assets", "models", "characters")
FPS = 24

BONES = {
    "hips": ((0, 0, 0.92), (0, 0, 1.08), None),
    "spine": ((0, 0, 1.08), (0, 0, 1.32), "hips"),
    "chest": ((0, 0, 1.32), (0, 0, 1.50), "spine"),
    "head": ((0, 0, 1.50), (0, 0, 1.85), "chest"),
    "upperarm.R": ((-0.23, 0, 1.46), (-0.25, 0, 1.19), "chest"),
    "forearm.R": ((-0.25, 0, 1.19), (-0.26, 0, 0.95), "upperarm.R"),
    "upperarm.L": ((0.23, 0, 1.46), (0.25, 0, 1.19), "chest"),
    "forearm.L": ((0.25, 0, 1.19), (0.26, 0, 0.95), "upperarm.L"),
    "thigh.R": ((-0.1, 0, 0.92), (-0.1, 0, 0.5), "hips"),
    "shin.R": ((-0.1, 0, 0.5), (-0.1, 0, 0.06), "thigh.R"),
    "thigh.L": ((0.1, 0, 0.92), (0.1, 0, 0.5), "hips"),
    "shin.L": ((0.1, 0, 0.5), (0.1, 0, 0.06), "thigh.L"),
}

# id: (shape, primary, secondary, skin)
CHARACTERS = {
    "gatot-kaca": ("humanoid", "#1F3A93", "#F5B041", "#8D5524"),
    "si-pitung": ("humanoid", "#151515", "#C0392B", "#A0663C"),
    "malin-kundang": ("humanoid", "#16A085", "#7F8C8D", "#B07D56"),
    "tukang-ojol": ("humanoid", "#27AE60", "#2C3E50", "#9C6B43"),
    "emak-daster": ("humanoid", "#8E44AD", "#F1948A", "#C68642"),
    "satpam-komplek": ("humanoid", "#22313F", "#ECF0F1", "#8D5524"),
    "pocong": ("shrouded", "#F4F6F7", "#BDC3C7", "#D5DBDB"),
    "kuntilanak": ("ghostly", "#FDFEFE", "#141414", "#E5E8E8"),
    "tuyul": ("tiny", "#A9CCE3", "#F2F3F4", "#9FB4C7"),
    "genderuwo": ("brute", "#3E2723", "#C0392B", "#4E342E"),
    "komodo": ("reptile", "#6E7F43", "#C9B458", "#6E7F43"),
    "kucing-oren": ("cat", "#E67E22", "#FDEBD0", "#E67E22"),
    "bocil-kematian": ("kid", "#2980B9", "#E74C3C", "#C68642"),
    "abang-bakso": ("humanoid", "#FAFAFA", "#1565C0", "#A0663C"),
    # v1.2 "Pasar Rakyat"
    "si-kancil": ("deer", "#A1662F", "#F3D9B1", "#A1662F"),
    "leak": ("leak", "#2E1A47", "#C0392B", "#D5C4A1"),
    "tukang-parkir": ("humanoid", "#F57C00", "#37474F", "#8D5524"),
    "bapak-ronda": ("humanoid", "#263238", "#4E6E58", "#8D5524"),
}

DARK = "#1B1B1B"


def hexc(h):
    h = h.lstrip('#')
    return (int(h[0:2], 16) / 255, int(h[2:4], 16) / 255, int(h[4:6], 16) / 255, 1.0)


def deselect_all():
    for o in bpy.data.objects:
        if o is not None and o.name in bpy.context.view_layer.objects:
            o.select_set(False)


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def part(parts, shape, size, loc, color, bone, rot=(0, 0, 0), segs=10, r2=None):
    """Adds one primitive, rigidly bound to `bone`, filled with `color`."""
    bm = bmesh.new()
    if shape == 'box':
        bmesh.ops.create_cube(bm, size=1.0)
    elif shape == 'sphere':
        bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(4, segs // 2 + 1), radius=0.5)
    else:  # cyl / cone
        top = 0.5 if shape == 'cyl' else (r2 if r2 is not None else 0.0)
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=0.5, radius2=top, depth=1.0)
    m = Matrix.Translation(Vector(loc)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal(Vector((*size, 1)))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    me = bpy.data.meshes.new("part")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("part", me)
    bpy.context.collection.objects.link(o)
    vg = o.vertex_groups.new(name=bone)
    vg.add(range(len(me.vertices)), 1.0, 'REPLACE')
    col = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER')
    c = hexc(color)
    for d in col.data:
        d.color = c
    parts.append(o)


def build_armature(name):
    arm = bpy.data.armatures.new(name + "_rig")
    ao = bpy.data.objects.new(name, arm)
    bpy.context.collection.objects.link(ao)
    deselect_all()
    bpy.context.view_layer.objects.active = ao
    ao.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for bn, (h, t, p) in BONES.items():
        b = arm.edit_bones.new(bn)
        b.head, b.tail, b.roll = h, t, 0
        if p:
            b.parent = eb[p]
            b.use_connect = False
        eb[bn] = b
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in ao.pose.bones:
        pb.rotation_mode = 'XYZ'
    return ao


# ------------------------------------------------------------------ sculpting

def limbs(P, prim, sec, skin, shoes=DARK, arm_r=0.065, leg_r=0.085, long_arms=1.0, sleeves=None):
    sleeves = sleeves or prim
    for s, x in (("R", -1), ("L", 1)):
        part(P, 'cyl', (arm_r * 2, arm_r * 2, 0.28 * long_arms), (x * 0.245, 0, 1.33 - 0.03 * (long_arms - 1)), sleeves, "upperarm." + s)
        part(P, 'cyl', (arm_r * 1.8, arm_r * 1.8, 0.24 * long_arms), (x * 0.255, 0, 1.07 - 0.25 * (long_arms - 1)), skin, "forearm." + s)
        part(P, 'sphere', (0.1, 0.1, 0.1), (x * 0.26, -0.01, 0.93 - 0.45 * (long_arms - 1)), skin, "forearm." + s, segs=8)
        part(P, 'cyl', (leg_r * 2, leg_r * 2, 0.44), (x * 0.1, 0, 0.72), sec, "thigh." + s)
        part(P, 'cyl', (leg_r * 1.7, leg_r * 1.7, 0.44), (x * 0.1, 0, 0.29), sec, "shin." + s)
        part(P, 'box', (0.13, 0.24, 0.08), (x * 0.1, -0.04, 0.04), shoes, "shin." + s)


def face(P, z, skin, eye="#111111", y=-0.155, spread=0.065, size=0.045):
    for x in (-spread, spread):
        part(P, 'sphere', (size, size * 0.6, size), (x, y, z), "#FFFFFF", "head", segs=8)
        part(P, 'sphere', (size * 0.55, size * 0.4, size * 0.55), (x, y - 0.015, z), eye, "head", segs=6)


def sculpt(cid, shape, prim, sec, skin):
    P = []
    if shape == "shrouded":  # pocong: wrapped body, knot on top, hops
        part(P, 'cyl', (0.5, 0.46, 0.75), (0, 0, 0.42), prim, "hips", segs=12)
        part(P, 'cyl', (0.52, 0.48, 0.62), (0, 0, 1.08), prim, "spine", segs=12)
        part(P, 'sphere', (0.42, 0.4, 0.46), (0, 0, 1.6), prim, "head", segs=12)
        part(P, 'sphere', (0.27, 0.06, 0.3), (0, -0.18, 1.6), skin, "head", segs=10)
        face(P, 1.64, skin, y=-0.21, spread=0.07, size=0.07)
        part(P, 'sphere', (0.13, 0.13, 0.13), (0, 0, 1.88), prim, "head", segs=8)
        part(P, 'cone', (0.12, 0.12, 0.18), (0.08, 0, 1.97), prim, "head", rot=(0, 35, 0), segs=6)
        part(P, 'cone', (0.12, 0.12, 0.18), (-0.08, 0, 1.97), prim, "head", rot=(0, -35, 0), segs=6)
        for z, b in ((1.4, "chest"), (0.85, "spine"), (0.25, "hips")):
            part(P, 'cyl', (0.54, 0.5, 0.05), (0, 0, z), sec, b, segs=12)
        return P

    if shape == "leak":  # Balinese leak: floating crowned head, trailing ribbons
        part(P, 'sphere', (0.62, 0.58, 0.6), (0, 0, 1.45), skin, "head", segs=14)
        part(P, 'sphere', (0.82, 0.5, 0.7), (0, 0.12, 1.5), "#1B1B1B", "head", segs=10)  # wild hair
        face(P, 1.52, skin, eye="#F4D03F", y=-0.28, spread=0.13, size=0.12)
        part(P, 'box', (0.12, 0.06, 0.3), (0, -0.3, 1.25), "#E74C3C", "head", rot=(-25, 0, 0))   # tongue
        part(P, 'cone', (0.7, 0.6, 0.22), (0, 0, 1.82), "#F5B041", "head", segs=8, r2=0.2)        # crown
        part(P, 'box', (0.66, 0.08, 0.1), (0, -0.25, 1.72), "#C0392B", "head")
        for i, x in enumerate((-0.16, -0.05, 0.06, 0.17)):
            bone = ("hips", "spine", "chest", "hips")[i]
            part(P, 'cone', (0.1, 0.1, 0.9), (x, 0.02, 0.75), sec, bone, segs=6, r2=0.02)   # glowing ribbons
        return P

    if shape == "ghostly":  # kuntilanak: long dress, long hair, floats
        part(P, 'cone', (0.62, 0.5, 1.1), (0, 0, 0.72), prim, "hips", segs=12, r2=0.32)
        part(P, 'box', (0.38, 0.24, 0.36), (0, 0, 1.33), prim, "spine")
        part(P, 'box', (0.4, 0.25, 0.16), (0, 0, 1.45), prim, "chest")
        part(P, 'sphere', (0.28, 0.28, 0.32), (0, 0, 1.66), skin, "head", segs=12)
        face(P, 1.68, skin, eye="#7B241C")
        part(P, 'box', (0.4, 0.14, 1.0), (0, 0.13, 1.28), sec, "head")          # long hair
        part(P, 'box', (0.34, 0.1, 0.12), (0, -0.08, 1.8), sec, "head")         # bangs
        part(P, 'box', (0.1, 0.1, 0.7), (-0.18, -0.02, 1.38), sec, "head")
        part(P, 'box', (0.1, 0.1, 0.7), (0.18, -0.02, 1.38), sec, "head")
        for s, x in (("R", -1), ("L", 1)):
            part(P, 'cone', (0.17, 0.17, 0.3), (x * 0.245, 0, 1.32), prim, "upperarm." + s, segs=8, r2=0.3)
            part(P, 'cyl', (0.09, 0.09, 0.26), (x * 0.255, 0, 1.07), skin, "forearm." + s, segs=8)
            part(P, 'sphere', (0.09, 0.09, 0.09), (x * 0.26, -0.01, 0.93), skin, "forearm." + s, segs=8)
        return P

    big_head = shape in ("tiny", "kid")
    brute = shape == "brute"
    torso_w = 0.62 if brute else 0.4
    head_s = 0.44 if big_head else 0.3
    head_z = 1.7 if big_head else 1.66

    # Torso & pelvis
    part(P, 'box', (torso_w * 0.85, 0.22, 0.2), (0, 0, 0.98), sec if shape not in ("tiny",) else "#ECF0F1", "hips")
    part(P, 'box', (torso_w, 0.26 if not brute else 0.36, 0.36), (0, 0, 1.27), prim, "spine")
    part(P, 'box', (torso_w * 1.08, 0.28 if not brute else 0.38, 0.18), (0, 0, 1.45), prim, "chest")

    if shape == "tiny":  # tuyul: bare grey-blue body, diaper
        P[-1].data.color_attributes["Col"].data.foreach_set("color", list(hexc(skin)) * len(P[-1].data.color_attributes["Col"].data))
        P[-2].data.color_attributes["Col"].data.foreach_set("color", list(hexc(skin)) * len(P[-2].data.color_attributes["Col"].data))

    # Head
    if shape == "reptile":
        part(P, 'box', (0.26, 0.3, 0.24), (0, -0.02, 1.64), skin, "head")
        part(P, 'box', (0.2, 0.26, 0.14), (0, -0.25, 1.6), skin, "head")       # snout
        part(P, 'box', (0.04, 0.12, 0.01), (0, -0.4, 1.56), "#C0392B", "head") # tongue
        face(P, 1.71, skin, eye="#F4D03F", y=-0.14, spread=0.1)
        part(P, 'cone', (0.42, 0.42, 0.16), (0, -0.02, 1.47), "#E74C3C", "chest", segs=8, r2=0.18)  # bandana
        part(P, 'cone', (0.22, 0.22, 0.9), (0, 0.38, 0.62), prim, "hips", rot=(-60, 0, 0), segs=8)  # tail
    elif shape == "deer":
        part(P, 'sphere', (0.32, 0.36, 0.3), (0, 0, 1.68), prim, "head", segs=12)
        part(P, 'box', (0.16, 0.22, 0.14), (0, -0.2, 1.62), sec, "head")                       # snout
        part(P, 'sphere', (0.06, 0.04, 0.05), (0, -0.31, 1.64), "#1B1B1B", "head", segs=6)     # nose
        face(P, 1.73, skin, eye="#1B1B1B", y=-0.155, spread=0.09, size=0.06)
        for x in (-1, 1):
            part(P, 'cone', (0.16, 0.06, 0.3), (x * 0.15, 0, 1.9), prim, "head", rot=(0, x * 25, 0), segs=5, r2=0.03)
        part(P, 'box', (torso_w * 0.7, 0.05, 0.3), (0, -0.14, 1.27), sec, "spine")
        part(P, 'cone', (0.12, 0.12, 0.2), (0, 0.18, 1.0), sec, "hips", rot=(-60, 0, 0), segs=5)  # tail tuft
    elif shape == "cat":
        part(P, 'sphere', (0.36, 0.33, 0.32), (0, 0, 1.67), prim, "head", segs=12)
        part(P, 'sphere', (0.18, 0.1, 0.12), (0, -0.15, 1.61), sec, "head", segs=8)  # muzzle
        face(P, 1.71, skin, eye="#2ECC71", y=-0.155, spread=0.08, size=0.06)
        for x in (-0.11, 0.11):
            part(P, 'cone', (0.12, 0.08, 0.16), (x, 0, 1.86), prim, "head", segs=4)
        part(P, 'cyl', (0.07, 0.07, 0.75), (0, 0.32, 1.05), prim, "hips", rot=(-35, 0, 0), segs=6)  # tail
        part(P, 'box', (torso_w * 0.7, 0.05, 0.3), (0, -0.14, 1.27), sec, "spine")  # belly
    else:
        part(P, 'sphere', (head_s, head_s, head_s * 1.12), (0, 0, head_z), skin, "head", segs=12)
        face(P, head_z + 0.03, skin, eye="#C0392B" if brute else "#111111",
             y=-head_s / 2 + 0.005, spread=0.085 if big_head else 0.065, size=0.06 if big_head else 0.045)

    # Limbs
    if shape == "brute":
        limbs(P, prim, sec, skin, arm_r=0.1, leg_r=0.12, long_arms=1.25)
        for x in (-0.05, 0.05):
            part(P, 'cone', (0.04, 0.04, 0.08), (x, -0.16, 1.6), "#FFFFFF", "head", rot=(180, 0, 0), segs=5)
        part(P, 'sphere', (0.5, 0.3, 0.3), (0, 0.05, 1.82), prim, "head", segs=8)  # hair mane
    elif shape == "tiny":
        limbs(P, skin, skin, skin, shoes=skin, arm_r=0.055, leg_r=0.075, sleeves=skin)
        for x in (-1, 1):
            part(P, 'cone', (0.08, 0.06, 0.2), (x * 0.24, 0, 1.75), skin, "head", rot=(0, x * 90, 0), segs=5)  # pointy ears
        part(P, 'box', (0.18, 0.08, 0.12), (0.1, -0.2, 1.0), "#8D6E63", "forearm.L")  # coin purse
    elif shape == "kid":
        limbs(P, prim, "#34495E", skin)
        part(P, 'sphere', (0.47, 0.47, 0.34), (0, 0.02, 1.86), "#2C2C2C", "head", segs=10)  # hair
        for x in (-1, 1):
            part(P, 'cyl', (0.16, 0.16, 0.08), (x * 0.235, 0, 1.72), sec, "head", rot=(0, 90, 0), segs=10)  # headset cups
        part(P, 'box', (0.5, 0.06, 0.05), (0, 0, 1.96), sec, "head")
        part(P, 'cyl', (0.02, 0.02, 0.2), (-0.17, -0.12, 1.62), sec, "head", rot=(60, 0, 0), segs=4)  # mic
    elif shape == "reptile":
        limbs(P, prim, prim, skin, shoes=prim, arm_r=0.075, leg_r=0.1)
    elif shape == "cat":
        limbs(P, prim, prim, prim, shoes=sec, arm_r=0.06, leg_r=0.08)
    elif shape == "deer":
        limbs(P, prim, prim, prim, shoes="#3E2723", arm_r=0.05, leg_r=0.06)
    else:
        sleeves = prim
        limbs(P, prim, sec, skin, sleeves=sleeves)

    # Character accessories
    if cid == "gatot-kaca":
        part(P, 'cone', (0.3, 0.3, 0.22), (0, 0, 1.9), sec, "head", segs=8, r2=0.18)
        part(P, 'cyl', (0.34, 0.34, 0.06), (0, 0, 1.8), sec, "head", segs=12)
        part(P, 'box', (0.46, 0.3, 0.06), (0, 0, 1.52), sec, "chest")                # gold collar
        part(P, 'box', (0.5, 0.05, 0.6), (0, 0.16, 1.15), "#C0392B", "chest")         # cape
    elif cid == "si-pitung":
        part(P, 'cyl', (0.29, 0.29, 0.12), (0, 0, 1.83), DARK, "head", segs=12)       # peci
        part(P, 'box', (0.12, 0.3, 0.55), (-0.08, 0, 1.3), sec, "chest", rot=(0, 30, 0))  # sarong sash
        part(P, 'box', (0.02, 0.025, 0.05), (-0.04, -0.15, 1.62), DARK, "head")       # moustache
        part(P, 'box', (0.02, 0.025, 0.05), (0.04, -0.15, 1.62), DARK, "head")
    elif cid == "malin-kundang":
        part(P, 'box', (0.22, 0.27, 0.36), (0.1, -0.005, 1.27), sec, "spine")         # stone half
        part(P, 'cyl', (0.14, 0.14, 0.3), (0.245, 0, 1.33), sec, "upperarm.L")
        part(P, 'sphere', (0.12, 0.12, 0.12), (0.12, -0.05, 1.72), sec, "head", segs=6)
    elif cid == "tukang-ojol":
        part(P, 'sphere', (0.36, 0.36, 0.36), (0, 0.01, 1.71), prim, "head", segs=12)  # helmet
        part(P, 'box', (0.26, 0.04, 0.1), (0, -0.17, 1.7), "#5D6D7E", "head")          # visor
        part(P, 'box', (0.42, 0.05, 0.04), (0, -0.14, 1.36), "#F4D03F", "chest")       # reflective stripe
    elif cid == "emak-daster":
        part(P, 'cone', (0.62, 0.52, 0.72), (0, 0, 0.82), prim, "hips", segs=12, r2=0.34)  # daster skirt
        part(P, 'sphere', (0.18, 0.18, 0.16), (0, 0.1, 1.82), "#2C2C2C", "head", segs=8)  # bun
        for i, x in enumerate((-0.1, 0.0, 0.1)):
            part(P, 'cyl', (0.06, 0.06, 0.14), (x, 0.0, 1.82), ("#F1948A", "#85C1E9", "#F7DC6F")[i], "head", rot=(0, 90, 0), segs=6)
        for i in range(5):  # floral dots
            part(P, 'sphere', (0.06, 0.02, 0.06), (-0.14 + i * 0.07, -0.14, 1.2 + (i % 2) * 0.12), sec, "spine", segs=6)
    elif cid == "satpam-komplek":
        part(P, 'sphere', (0.34, 0.3, 0.12), (0.03, 0, 1.82), DARK, "head", rot=(0, -12, 0), segs=10)  # beret
        part(P, 'box', (0.1, 0.03, 0.12), (0.1, -0.15, 1.42), "#F4D03F", "chest")      # badge
        part(P, 'box', (0.16, 0.04, 0.3), (0, -0.14, 1.27), sec, "spine")             # white shirt front
    elif cid == "abang-bakso":
        part(P, 'cyl', (0.31, 0.31, 0.1), (0, 0, 1.82), sec, "head", segs=12)         # cap
        part(P, 'box', (0.22, 0.14, 0.03), (0, -0.2, 1.79), sec, "head")              # brim
        part(P, 'box', (0.46, 0.3, 0.07), (0, 0, 1.52), "#FFFFFF", "chest")           # towel
        part(P, 'box', (0.08, 0.04, 0.3), (-0.12, -0.15, 1.32), "#85C1E9", "chest")
    elif cid == "genderuwo":
        pass
    elif cid == "tukang-parkir":
        part(P, 'cyl', (0.31, 0.31, 0.1), (0, 0, 1.83), "#455A64", "head", segs=12)          # cap
        part(P, 'box', (0.22, 0.14, 0.03), (0, -0.2, 1.8), "#455A64", "head")
        part(P, 'box', (0.44, 0.04, 0.05), (0, -0.14, 1.38), "#ECEFF1", "chest")              # reflective stripes
        part(P, 'box', (0.42, 0.04, 0.05), (0, -0.14, 1.2), "#ECEFF1", "spine")
        part(P, 'cyl', (0.05, 0.05, 0.08), (0, -0.17, 1.6), "#FDD835", "head", rot=(90, 0, 0), segs=6)  # whistle
    elif cid == "bapak-ronda":
        part(P, 'sphere', (0.46, 0.36, 0.42), (0, -0.06, 1.18), prim, "spine", segs=10)      # belly
        part(P, 'cyl', (0.29, 0.29, 0.12), (0, 0, 1.83), "#111111", "head", segs=12)         # peci
        part(P, 'cone', (0.5, 0.42, 0.6), (0, 0, 0.72), sec, "hips", segs=12, r2=0.32)       # sarong
        part(P, 'box', (0.12, 0.3, 0.6), (0.1, 0, 1.32), "#C0392B", "chest", rot=(0, -28, 0))  # sarong over shoulder
        part(P, 'box', (0.03, 0.025, 0.05), (-0.045, -0.155, 1.62), "#111111", "head")       # moustache
        part(P, 'box', (0.03, 0.025, 0.05), (0.045, -0.155, 1.62), "#111111", "head")
        part(P, 'cyl', (0.1, 0.1, 0.45), (-0.26, -0.12, 0.98), "#C8A951", "forearm.R", rot=(70, 0, 0), segs=8)  # kentongan
    return P


def assemble(cid):
    shape, prim, sec, skin = CHARACTERS[cid]
    clear_scene()
    ao = build_armature(cid)
    parts = sculpt(cid, shape, prim, sec, skin)
    deselect_all()
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = cid + "_body"
    body.data.name = cid + "_mesh"
    # Merge coincident verts per part is unnecessary (rigid parts); recalc normals for clean shading.
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    body.parent = ao
    mod = body.modifiers.new("Armature", 'ARMATURE')
    mod.object = ao
    body.data.color_attributes.active_color = body.data.color_attributes["Col"]
    # One plain material that uses the vertex colour, so the exporter keeps COLOR_0.
    mat = bpy.data.materials.get("djancoeg_vertex") or bpy.data.materials.new("djancoeg_vertex")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    attr = next((n for n in nt.nodes if n.type == 'ATTRIBUTE'), None) or nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    if not bsdf.inputs['Base Color'].is_linked:
        nt.links.new(attr.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.8
    body.data.materials.clear()
    body.data.materials.append(mat)
    return ao, body, shape


# ------------------------------------------------------------------ animation

def forward_sign(ao, bone, axis=0):
    """Returns +1/-1 so that rotating `bone` by +sign about local `axis` swings its tail towards -Y (front)."""
    pb = ao.pose.bones[bone]
    rest = (ao.matrix_world @ pb.tail).copy()
    e = [0, 0, 0]
    e[axis] = math.radians(30)
    pb.rotation_euler = e
    bpy.context.view_layer.update()
    moved = ao.matrix_world @ pb.tail
    pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    return 1 if moved.y < rest.y else -1


def key(ao, frame, poses, hips_loc=None):
    """Keys a FULL pose (every bone) so clips never inherit channels from each other."""
    for pb in ao.pose.bones:
        x, y, z = poses.get(pb.name, (0, 0, 0))
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
        pb.keyframe_insert("rotation_euler", frame=frame)
        pb.location = hips_loc if (pb.name == "hips" and hips_loc is not None) else (0, 0, 0)
        pb.keyframe_insert("location", frame=frame)


def reset_pose(ao):
    for pb in ao.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)


def make_action(ao, name, builder, cyclic=True):
    reset_pose(ao)
    if ao.animation_data is None:
        ao.animation_data_create()
    act = bpy.data.actions.new(f"{ao.name}_{name}")
    ao.animation_data.action = act
    end = builder(ao)
    for fc in getattr(act, "fcurves", []):
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'
        if cyclic:
            fc.modifiers.new('CYCLES')
    ao.animation_data.action = None
    track = ao.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 1, act)
    strip.frame_end = end
    track.mute = True
    return act


def animate(ao, shape):
    s_arm = forward_sign(ao, "upperarm.R")
    s_leg = forward_sign(ao, "thigh.R")
    s_spine = forward_sign(ao, "spine")
    s_fore = forward_sign(ao, "forearm.R")
    aim = 78 * s_arm
    bend = 14 * s_fore
    lean = 6 * s_spine

    def arms(extra=0.0):
        return {
            "upperarm.R": (aim + extra, 0, 0), "forearm.R": (bend, 0, 0),
            "upperarm.L": (aim * 0.85 + extra, 0, -18), "forearm.L": (bend * 2, 0, 0),
        }

    def idle(ao):
        for f, br in ((1, 0), (25, 3), (49, 0)):
            p = arms(br * 0.5)
            p.update({"chest": (-br * s_spine, 0, 0), "head": (br * 0.5 * s_spine, 0, 0)})
            if shape in ("ghostly", "leak"):
                key(ao, f, p, hips_loc=(0, 0, 0.12 + br * 0.02))
            else:
                key(ao, f, p, hips_loc=(0, 0, 0))
        return 49

    def run(ao):
        if shape == "shrouded":  # pocong hop
            for f, h, sq in ((1, 0.0, 0), (6, 0.32, 0), (11, 0.42, 0), (16, 0.18, 0), (21, 0.0, 0)):
                key(ao, f, {"spine": (lean, 0, 0), "head": (-lean, 0, 0)}, hips_loc=(0, 0, h))
            return 21
        if shape in ("ghostly", "leak"):
            for f, b in ((1, 0.12), (11, 0.2), (21, 0.12)):
                p = arms()
                p.update({"spine": (lean * 2, 0, 0)})
                key(ao, f, p, hips_loc=(0, 0, b))
            return 21
        swing = 38 * s_leg
        for f, k in ((1, 1), (6, 0), (11, -1), (16, 0), (21, 1)):
            p = arms()
            p.update({
                "spine": (lean, 0, 0),
                "thigh.R": (swing * k, 0, 0), "thigh.L": (-swing * k, 0, 0),
                "shin.R": (-abs(swing) * 0.9 * s_leg if k < 0 else 0, 0, 0),
                "shin.L": (-abs(swing) * 0.9 * s_leg if k > 0 else 0, 0, 0),
            })
            key(ao, f, p, hips_loc=(0, 0, 0.05 if k == 0 else 0.0))
        return 21

    def shoot(ao):
        for f, kick in ((1, 0), (3, -14), (9, 0)):
            p = arms(kick * s_arm)
            p.update({"chest": (-kick * 0.3 * s_spine, 0, 0)})
            key(ao, f, p, hips_loc=(0, 0, 0.12 if shape == "ghostly" else 0))
        return 9

    def die(ao):
        key(ao, 1, arms(), hips_loc=(0, 0, 0))
        p = {"hips": (-40 * s_spine, 0, 8), "upperarm.R": (-60 * s_arm, 0, 40), "upperarm.L": (-60 * s_arm, 0, -40),
             "thigh.R": (25 * s_leg, 0, 0), "head": (-20 * s_spine, 0, 0)}
        key(ao, 10, p, hips_loc=(0, 0, 0.1))
        p = {"hips": (-88 * s_spine, 0, 10), "upperarm.R": (-150 * s_arm, 0, 70), "upperarm.L": (-150 * s_arm, 0, -70),
             "thigh.R": (35 * s_leg, 0, 0), "thigh.L": (-10 * s_leg, 0, 0), "head": (-10 * s_spine, 0, 0)}
        key(ao, 24, p, hips_loc=(0, 0, -0.75))
        return 24

    make_action(ao, "idle", idle)
    make_action(ao, "run", run)
    make_action(ao, "shoot", shoot)
    make_action(ao, "die", die, cyclic=False)
    reset_pose(ao)


def export(cid, ao, body):
    os.makedirs(OUT, exist_ok=True)
    deselect_all()
    ao.select_set(True)
    body.select_set(True)
    path = os.path.join(OUT, cid + ".glb")
    bpy.ops.export_scene.gltf(
        filepath=path, use_selection=True, export_format='GLB', export_yup=True,
        export_animations=True, export_animation_mode='NLA_TRACKS', export_force_sampling=True,
        export_frame_step=1, export_def_bones=True, export_vertex_color='ACTIVE',
        export_all_vertex_colors=False, export_normals=True, export_texcoords=False,
        export_materials='EXPORT', export_skins=True, export_morph=False)
    return path


def build(cid):
    ao, body, shape = assemble(cid)
    animate(ao, shape)
    path = export(cid, ao, body)
    return f"{cid}: {len(body.data.polygons)} faces -> {os.path.getsize(path)} bytes"


def build_all(ids=None):
    bpy.context.scene.render.fps = FPS
    out = [build(cid) for cid in (ids or CHARACTERS.keys())]
    for line in out:
        print(line)
    return out


if __name__ == "__main__" and "--" in sys.argv:
    args = sys.argv[sys.argv.index("--") + 1:]
    build_all(None if not args or args[0] == "all" else args)
