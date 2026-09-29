"""
Builds the person-sized figure used by 동승자 (seen only in the elevator mirror) and 뒷사람 (an invisible body that
only casts a shadow) and exports Assets/_Project/Models/Person.fbx.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_person.py

About 1.7 m, thin, shoulders slumped, head tipped slightly forward, long hair down the back and sides. Three material
slots so the game can tint them: Body (dark clothes), Skin (pale face and hands), Eyes (dark sockets that meet your
eyes in the mirror). Blender Z-up meters, face toward -Y; exported Y-up with the face toward Unity +Z.
"""
import math
import os

import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Person.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)


def cylinder_between(a, b, r0, r1, name, verts=12):
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


body, skin, eyes = [], [], []

# legs (feet at z=0), knees ~0.47
for s in (-1, 1):
    x = 0.09 * s
    body.append(cylinder_between((x, 0, 0.86), (x * 0.95, 0.005, 0.47), 0.075, 0.055, f"Thigh{s}"))
    body.append(cylinder_between((x * 0.95, 0.005, 0.47), (x * 0.9, 0.01, 0.06), 0.055, 0.04, f"Shin{s}"))
    body.append(cylinder_between((x * 0.9, 0.02, 0.04), (x * 0.9, -0.16, 0.03), 0.045, 0.035, f"Foot{s}"))
# hips, torso leaning a little forward, slumped shoulders
body.append(sphere((0, 0, 0.9), (0.16, 0.11, 0.1), "Hips"))
body.append(cylinder_between((0, 0, 0.88), (0, -0.03, 1.42), 0.14, 0.17, "Torso", 14))
body.append(sphere((0, -0.03, 1.4), (0.19, 0.11, 0.07), "ShoulderLine"))
# neck and head tipped forward (face toward -Y)
skin.append(cylinder_between((0, -0.03, 1.42), (0, -0.06, 1.53), 0.045, 0.04, "Neck"))
head_c = Vector((0, -0.085, 1.61))
skin.append(sphere(head_c, (0.085, 0.1, 0.115), "Head"))
# eye sockets: two dark hollows on the face
for s in (-1, 1):
    eyes.append(sphere(head_c + Vector((0.032 * s, -0.088, 0.018)), (0.019, 0.012, 0.013), f"Eye{s}", 10))
# long hair: a cap over the skull and a curtain falling down the back and sides
body.append(sphere(head_c + Vector((0, 0.012, 0.03)), (0.093, 0.105, 0.11), "HairCap"))
for i, (dx, dy) in enumerate(((0, 0.07), (-0.07, 0.02), (0.07, 0.02), (-0.05, 0.055), (0.05, 0.055))):
    a = head_c + Vector((dx, dy, 0.02))
    b = a + Vector((dx * 0.15, dy * 0.2 + 0.01, -0.36))
    body.append(cylinder_between(a, b, 0.045, 0.02, f"Hair{i}", 10))
# arms hanging straight down, hands open
for s in (-1, 1):
    sh = Vector((0.19 * s, -0.02, 1.39))
    el = Vector((0.215 * s, -0.01, 1.08))
    wr = Vector((0.22 * s, -0.03, 0.8))
    body.append(sphere(sh, (0.055, 0.055, 0.055), f"Shoulder{s}", 10))
    body.append(cylinder_between(sh, el, 0.045, 0.037, f"UpperArm{s}"))
    body.append(cylinder_between(el, wr, 0.037, 0.03, f"Forearm{s}"))
    bpy.ops.mesh.primitive_cube_add(size=1, location=(wr.x, wr.y, wr.z - 0.06))
    palm = bpy.context.active_object
    palm.name = f"Palm{s}"
    palm.scale = (0.022, 0.08, 0.1)
    skin.append(palm)
    for i, dy in enumerate((-0.027, -0.009, 0.009, 0.027)):
        a = Vector((wr.x, wr.y + dy, wr.z - 0.11))
        skin.append(cylinder_between(a, a + Vector((0.004 * s, dy * 0.2, -0.075)), 0.008, 0.006, f"Finger{s}_{i}", 6))
    a = Vector((wr.x, wr.y - 0.035, wr.z - 0.04))
    skin.append(cylinder_between(a, a + Vector((0.012 * s, -0.03, -0.06)), 0.009, 0.007, f"Thumb{s}", 6))


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    return o


mats = {n: bpy.data.materials.new(n) for n in ("Body", "Skin", "Eyes")}
parts = []
for objs, mname in ((body, "Body"), (skin, "Skin"), (eyes, "Eyes")):
    o = join(objs, mname + "Part")
    o.data.materials.append(mats[mname])
    parts.append(o)
person = join(parts, "Person")
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bpy.ops.object.shade_smooth()

# empty for gameplay: between the eyes (mirror eye contact, head sounds)
e = bpy.data.objects.new("HeadPoint", None)
e.location = head_c + Vector((0, -0.09, 0.018))
bpy.context.scene.collection.objects.link(e)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH", "EMPTY"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
tris = len(person.data.polygons)
print(f"[person] exported {OUT} faces={tris} slots={[m.name for m in person.data.materials]} "
      f"height={max((person.matrix_world @ Vector(c)).z for c in person.bound_box):.2f}")
