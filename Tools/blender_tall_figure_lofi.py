"""
Lo-fi (PS1-style) 키다리 / 배웅꾼 figure: a low-poly body whose texture is painted over its own orthographic views.

    blender --background --factory-startup --python Tools/blender_tall_figure_lofi.py              model + reference views
    blender --background --factory-startup --python Tools/blender_tall_figure_lofi.py -- preview   lo-fi turnaround (needs the texture)

Same contract as Tools/blender_tall_figure.py so the game can swap it in: ~2.55 m tall (head above a 2.1 m door frame),
face toward -Y (Unity +Z), mesh "Figure" plus separate "ExtraFinger_L/_R" (shown = 6 fingers for 키다리, hidden = 5 for
배웅꾼), empties HeadPoint / HandPoint_L / HandPoint_R. Exported to Assets/_Project/Models/TallFigure_Lofi.fbx.

Texture pipeline (the GPT link): the script renders front / side / back orthographic clay views into
%TEMP%/nocx/entity/ref_*.png; Tools/gen_entity_lofi.py puts them side by side, has GPT paint over the sheet, and turns the
result into a 256 px atlas (Assets/_Project/Textures/Entities/tallfigure_lofi.png). The UVs here are those same camera
projections (front / side / back thirds of the atlas; the right side reuses the left side mirrored), so the painted
views land exactly on the faces they were painted for.
"""
import math
import os
import sys

import bmesh
import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "TallFigure_Lofi.fbx")
TEX = os.path.join(ROOT, "Assets", "_Project", "Textures", "Entities", "tallfigure_lofi.png")
WORK = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "entity")
os.makedirs(WORK, exist_ok=True)
PREVIEW = "preview" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False

VIEW_H, VIEW_W = 2.7, 0.9  # each orthographic view: 2.7 m tall, 0.9 m wide (1:3), centered on x/y = 0
VIEW_Z = 1.33              # vertical center of the views
SIDES = 6                  # cross-section of limbs


# ------------------------------------------------------------------ geometry helpers (one bmesh per object)
def frame(d):
    """Unit vectors across a limb: u = width (x-ish), v = depth (y-ish) for vertical limbs."""
    ref = Vector((0, 1, 0)) if abs(d.z) > 0.9 else Vector((0, 0, 1))
    u = d.cross(ref).normalized()
    v = d.cross(u).normalized()
    return u, v


def tube(bm, pts, radii, sides=SIDES, caps=(True, True)):
    """Tapered tube through pts; radii are (width, depth) pairs or single numbers."""
    pts = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u, v = frame(d)
        rw, rd = radii[i] if isinstance(radii[i], tuple) else (radii[i], radii[i])
        rings.append([bm.verts.new(p + u * math.cos(a) * rw + v * math.sin(a) * rd)
                      for a in (2 * math.pi * (k + 0.5) / sides for k in range(sides))])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(sides):
            bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
    if caps[0]:
        bm.faces.new(list(reversed(rings[0])))
    if caps[1]:
        bm.faces.new(rings[-1])


def blob(bm, center, scale, tilt=0.0, segs=(8, 6)):
    """Low-poly ellipsoid (head, shoulders), tilted forward around X by `tilt` radians."""
    res = bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1.0)
    m = Matrix.Translation(center) @ Matrix.Rotation(tilt, 4, "X") @ Matrix.Diagonal((*scale, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=res["verts"])


def slab(bm, center, size):
    res = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ Matrix.Diagonal((*size, 1.0)), verts=res["verts"])


def finish(bm, name):
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


# ------------------------------------------------------------------ the figure
bpy.ops.wm.read_factory_settings(use_empty=True)
body = bmesh.new()
extra = {"L": bmesh.new(), "R": bmesh.new()}

# legs under the coat: thin slacks, flat worn shoes pointing forward (-Y)
for s in (-1, 1):
    x = 0.085 * s
    tube(body, [(x, 0.0, 1.05), (x * 0.92, 0.0, 0.62), (x * 0.86, 0.01, 0.1)], [0.05, 0.043, 0.034], caps=(True, False))
    tube(body, [(x * 0.86, 0.02, 0.07), (x * 0.86, -0.19, 0.035)], [(0.042, 0.035), (0.036, 0.025)], sides=4)

# long coat: shoulders to just under the knees, slightly flared, the upper body leaning forward (a hunch)
tube(body, [(0, 0.03, 0.55), (0, 0.02, 0.95), (0, 0.0, 1.4), (0, -0.02, 1.8), (0, -0.04, 2.0), (0, -0.05, 2.06)],
     [(0.2, 0.13), (0.17, 0.115), (0.14, 0.1), (0.16, 0.1), (0.19, 0.105), (0.12, 0.08)], sides=8)
# neck and a small, long head hanging forward (looks down at whoever stands in front of it)
tube(body, [(0, -0.05, 2.04), (0, -0.08, 2.19)], [0.045, 0.04], caps=(False, False))
blob(body, Vector((0, -0.1, 2.36)), (0.095, 0.105, 0.17), tilt=math.radians(-12))

# arms in coat sleeves, hanging below the knees; palms and fingers
hand_tops = {}
for s in (-1, 1):
    sh = Vector((0.2 * s, -0.04, 1.99))
    el = Vector((0.235 * s, -0.01, 1.34))
    wr = Vector((0.228 * s, -0.01, 0.74))
    blob(body, sh, (0.06, 0.06, 0.055), segs=(6, 4))
    tube(body, [sh, el, wr], [0.042, 0.035, 0.03])
    slab(body, wr + Vector((0.0, 0.0, -0.075)), (0.024, 0.12, 0.13))
    hand_tops[s] = wr + Vector((0.0, 0.0, -0.135))

for s, label in ((-1, "R"), (1, "L")):
    base = hand_tops[s]
    for i, dy in enumerate((-0.042, -0.014, 0.014, 0.042, 0.07)):  # index 4 is the sixth digit (separate object)
        a = base + Vector((0, dy, 0.005))
        b = a + Vector((0.012 * s, dy * 0.55, -0.2 + 0.012 * abs(i - 1.5)))
        tube(extra[label] if i == 4 else body, [a, (a + b) / 2 + Vector((0.006 * s, 0, 0)), b], [0.013, 0.011, 0.007], sides=4)
    a = base + Vector((0.0, -0.06, 0.07))
    tube(body, [a, a + Vector((0.02 * s, -0.08, -0.11))], [0.012, 0.008], sides=4)

fig = finish(body, "Figure")
extras = [finish(extra[k], f"ExtraFinger_{k}") for k in ("L", "R")]
meshes = [fig] + extras
for o in meshes:
    for p in o.data.polygons:
        p.use_smooth = True  # Gouraud-shaded low poly, as on the PS1

# ------------------------------------------------------------------ cameras for the views and the UV projection
scene = bpy.context.scene
scene.render.resolution_x, scene.render.resolution_y = 512, 1536


def ortho_cam(name, loc, rot_z):
    cd = bpy.data.cameras.new(name)
    cd.type = "ORTHO"
    cd.ortho_scale = VIEW_H
    c = bpy.data.objects.new(name, cd)
    scene.collection.objects.link(c)
    c.location = loc
    c.rotation_euler = (math.pi / 2, 0, rot_z)
    return c


cams = {
    "front": ortho_cam("CamFront", (0, -6, VIEW_Z), 0.0),          # looks +Y at the face
    "side": ortho_cam("CamSide", (6, 0, VIEW_Z), math.pi / 2),     # looks -X at the figure's left side
    "back": ortho_cam("CamBack", (0, 6, VIEW_Z), math.pi),         # looks -Y at the back
}
PANEL = {"front": 0, "side": 1, "back": 2}
bpy.context.view_layer.update()


def project_uvs(o):
    """Each face takes the view it faces most; right-side faces reuse the left-side view mirrored."""
    me = o.data
    uv = me.uv_layers.new(name="UVMap")
    mw = o.matrix_world
    for p in me.polygons:
        n = (mw.to_3x3() @ p.normal).normalized()
        if abs(n.y) >= abs(n.x):
            view, mirror = ("front" if n.y < 0 else "back"), False
        else:
            view, mirror = "side", n.x < 0
        # caps (coat hem, shoe soles, shoulder tops) sit edge-on in every view: sample a little inside the part
        # instead of its outline, where the painting meets the background
        inset = Vector((0, 0, 0.03 if n.z < -0.7 else -0.03 if n.z > 0.7 else 0.0))
        for li in p.loop_indices:
            co = mw @ me.vertices[me.loops[li].vertex_index].co + inset
            if mirror:
                co = Vector((-co.x, co.y, co.z))
            q = world_to_camera_view(scene, cams[view], co)
            uv.data[li].uv = ((PANEL[view] + min(max(q.x, 0.0), 1.0)) / 3.0, min(max(q.y, 0.0), 1.0))


for o in meshes:
    project_uvs(o)

mat = bpy.data.materials.new("Figure")
mat.use_nodes = True
for o in meshes:
    o.data.materials.append(mat)

# gameplay empties (same names and places as the original figure)
for name, loc in (("HeadPoint", (0, -0.12, 2.34)), ("HandPoint_L", (0.228, 0, 0.55)), ("HandPoint_R", (-0.228, 0, 0.55))):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    scene.collection.objects.link(e)

tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
height = max((fig.matrix_world @ Vector(c)).z for c in fig.bound_box)
print(f"[lofi] triangles={tris} height={height:.2f}")


# ------------------------------------------------------------------ renders
def workbench(color_type):
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = color_type
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = False
    w = bpy.data.worlds.new("w")
    w.color = (1, 1, 1)
    scene.world = w
    scene.display.shading.background_type = "WORLD"
    scene.render.film_transparent = False


if not PREVIEW:
    # clay views on white: the reference GPT paints over (same cameras as the UVs)
    workbench("SINGLE")
    scene.display.shading.single_color = (0.62, 0.62, 0.62)
    for view, cam in cams.items():
        scene.camera = cam
        scene.render.filepath = os.path.join(WORK, f"ref_{view}.png")
        bpy.ops.render.render(write_still=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        o.select_set(o.type in ("MESH", "EMPTY"))
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH", "EMPTY"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
    print("[lofi] exported", OUT, "and reference views in", WORK)
else:
    # lo-fi turnaround: the atlas, nearest-filtered, rendered small and blown up by the caller
    img = bpy.data.images.load(TEX)
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    workbench("TEXTURE")
    scene.display.shading.light = "FLAT"
    scene.display.shading.show_cavity = False
    scene.world.color = (0.05, 0.055, 0.06)
    scene.render.resolution_x, scene.render.resolution_y = 128, 384
    for rot in (0, 35, 90, 180):
        c = ortho_cam(f"Turn{rot}", (0, 0, VIEW_Z), 0.0)
        a = math.radians(rot)
        c.location = (6 * math.sin(a), -6 * math.cos(a), VIEW_Z)
        c.rotation_euler = (math.pi / 2, 0, a)
        scene.camera = c
        scene.render.filepath = os.path.join(WORK, f"turn_{rot:03d}.png")
        bpy.ops.render.render(write_still=True)
    print("[lofi] turnaround in", WORK)
