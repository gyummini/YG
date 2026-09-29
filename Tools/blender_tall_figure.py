"""
Builds the tall figure used by 키다리 / 배웅꾼 and exports Assets/_Project/Models/TallFigure.fbx.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_tall_figure.py

About 2.55 m tall (head clearly above a 2.1 m door frame), thin, arms hanging below the knees. Each hand has a thumb
and four long fingers in the body mesh; one more finger per hand is a separate object (ExtraFinger_L / _R) so the game
can show it for 키다리 (6 fingers) and hide it for 배웅꾼 (5 fingers). Blender Z-up meters; exported Y-up for Unity.
"""
import math
import os

import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "TallFigure.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)


def cylinder_between(a, b, r0, r1, name, verts=10):
    """Tapered cylinder from point a (radius r0) to point b (radius r1)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return o


def sphere(center, scale, name, segs=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=segs // 2, radius=1.0, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    return o


body = []
# legs (feet at z=0), knees ~0.66
for s in (-1, 1):
    x = 0.085 * s
    body.append(cylinder_between((x, 0, 1.30), (x * 0.9, 0, 0.66), 0.06, 0.045, f"Thigh{s}"))
    body.append(cylinder_between((x * 0.9, 0, 0.66), (x * 0.85, 0.01, 0.05), 0.045, 0.032, f"Shin{s}"))
    body.append(cylinder_between((x * 0.85, 0.0, 0.03), (x * 0.85, -0.17, 0.02), 0.035, 0.028, f"Foot{s}"))
# torso, neck, head (face points -Y in Blender = +Z forward in Unity after export)
body.append(cylinder_between((0, 0, 1.28), (0, 0, 2.02), 0.11, 0.17, "Torso", 12))
body.append(cylinder_between((0, 0, 2.02), (0, -0.01, 2.20), 0.045, 0.04, "Neck"))
body.append(sphere((0, -0.015, 2.36), (0.095, 0.11, 0.17), "Head"))
# shoulders and long arms hanging below the knees
hand_tops = {}
for s in (-1, 1):
    sh = Vector((0.2 * s, 0, 1.99))
    el = Vector((0.23 * s, 0.02, 1.34))
    wr = Vector((0.225 * s, 0.0, 0.72))
    body.append(sphere(sh, (0.055, 0.055, 0.055), f"Shoulder{s}", 10))
    body.append(cylinder_between(sh, el, 0.038, 0.03, f"UpperArm{s}"))
    body.append(cylinder_between(el, wr, 0.03, 0.024, f"Forearm{s}"))
    # palm: thin slab facing inward, hanging down
    bpy.ops.mesh.primitive_cube_add(size=1, location=(wr.x, wr.y + 0.012, wr.z - 0.07))
    palm = bpy.context.active_object
    palm.name = f"Palm{s}"
    palm.scale = (0.02, 0.13, 0.13)
    body.append(palm)
    hand_tops[s] = Vector((wr.x, wr.y, wr.z - 0.13))

# digits: thumb + four fingers in the body (a normal five), the fifth finger kept separate per hand
# (shown = six digits for 키다리). Thick and fanned so they can be counted in a flashlight beam at 3-4 m.
extras = []
for s in (-1, 1):
    base = hand_tops[s]
    spread = [-0.042, -0.014, 0.014, 0.042, 0.07]  # along y across the hand; index 4 is the extra finger
    for i, dy in enumerate(spread):
        a = base + Vector((0, dy, 0.005))
        b = a + Vector((0.012 * s, dy * 0.55, -0.19 + 0.012 * abs(i - 1.5)))
        f = cylinder_between(a, b, 0.012, 0.008, f"Finger{s}_{i}", 8)
        (extras if i == 4 else body).append(f)
    # thumb, angled forward and out
    a = base + Vector((0.0, -0.06, 0.07))
    body.append(cylinder_between(a, a + Vector((0.02 * s, -0.08, -0.11)), 0.012, 0.009, f"Thumb{s}", 8))


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.shade_smooth()
    return o


mat = bpy.data.materials.new("Figure")
fig = join(body, "Figure")
fig.data.materials.append(mat)
for s, label in ((-1, "R"), (1, "L")):
    ef = [o for o in extras if o.name.startswith(f"Finger{s}_")][0]
    ef.name = f"ExtraFinger_{label}"
    bpy.ops.object.select_all(action="DESELECT")
    ef.select_set(True)
    bpy.context.view_layer.objects.active = ef
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.shade_smooth()
    ef.data.materials.append(mat)

# empties for gameplay: head (face check) and hands (finger check)
for name, loc in (("HeadPoint", (0, -0.1, 2.36)), ("HandPoint_L", (0.225, 0, 0.55)), ("HandPoint_R", (-0.225, 0, 0.55))):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    bpy.context.scene.collection.objects.link(e)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH", "EMPTY"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
tris = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
print(f"[figure] exported {OUT} faces={tris} height={max((o.matrix_world @ Vector(c)).z for o in [fig] for c in o.bound_box):.2f}")
