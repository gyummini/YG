"""
More corridor dressing for the art-direction test, exported to Assets/_Project/Models/Props/*.fbx, plus baked
ambient occlusion for the hydrant cabinet (Assets/_Project/Textures/ArtTest/hydrant_ao.png).

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_art_props2.py

- ParcelBox: two stacked cardboard delivery boxes (택배 상자), pivot on the floor.
- MeterBox: grey steel utility meter cabinet (계량기함) with a small window, pivot at the back bottom center.
- PottedPlant: plastic pot with a clump of long leaves, pivot on the floor.
- NoticeBoard: framed corkboard for notices (게시판), front UV 0..1, pivot at the back bottom center.
- ElevatorPanel: floor indicator above the doors + hall call button plate, pivot at the wall at the door center.
AO bakes use Cycles; the cabinet front is UV 0..1 so the AO lines up with its face texture (the unit door's AO and
normal map are baked by Tools/blender_art_bakes.py).
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Props")
TEX = os.path.join(ROOT, "Assets", "_Project", "Textures", "ArtTest")
PREVIEW = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "props")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREVIEW, exist_ok=True)
random.seed(3)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, color, rough=0.5, metal=0.0, emit=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 3.0
    return m


def box(name, size, loc, material, uv_front=None, rot_z=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        for loop in f.loops:
            p = loop.vert.co
            if abs(n.y) > 0.5 and uv_front:
                u = p.x / size[0] + 0.5
                loop[uv].uv = (1.0 - u if n.y > 0 else u, p.z / size[2] + 0.5)
            elif abs(n.x) > 0.5:
                loop[uv].uv = (p.y, p.z)
            elif abs(n.y) > 0.5:
                loop[uv].uv = (p.x, p.z)
            else:
                loop[uv].uv = (p.x, p.y)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (0, 0, rot_z)
    o.data.materials.append(material)
    return o


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return o


def set_origin(o, point):
    bpy.context.scene.cursor.location = point
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")


def export(name):
    bpy.ops.object.select_all(action="SELECT")
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
    print("[props2] exported", path)


def preview(name, target, ortho, yaw=30, pitch=15):
    s = bpy.context.scene
    s.render.engine = "BLENDER_WORKBENCH"
    s.display.shading.light = "STUDIO"
    s.display.shading.color_type = "MATERIAL"
    s.display.shading.show_cavity = True
    w = bpy.data.worlds.new("w")
    w.color = (0.55, 0.56, 0.58)
    s.world = w
    s.display.shading.background_type = "WORLD"
    s.render.resolution_x, s.render.resolution_y = 600, 600
    cd = bpy.data.cameras.new("cam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho
    cam = bpy.data.objects.new("cam", cd)
    s.collection.objects.link(cam)
    s.camera = cam
    y, p = math.radians(yaw), math.radians(90 - pitch)
    d = 6.0
    cam.location = (target[0] + math.sin(y) * d * math.sin(p), target[1] - math.cos(y) * d * math.sin(p), target[2] + d * math.cos(p))
    cam.rotation_euler = (p, 0, y)
    s.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


def bake_ao(fbx, face_w, face_h, face_center, out_png, size=(512, 1024)):
    """AO of a prop's front face, baked 'selected to active' from the full model onto a plane that has the same
    0..1 UVs as the face (so small parts like the lock and the plate shade the face around them)."""
    reset()
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, fbx))
    source = next(o for o in bpy.data.objects if o.type == "MESH")
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))  # corridor floor for contact shading
    floor = bpy.context.active_object
    bpy.ops.mesh.primitive_plane_add(size=1, location=face_center, rotation=(math.pi / 2, 0, 0))
    target = bpy.context.active_object
    target.scale = (face_w, face_h, 1)
    bpy.ops.object.transform_apply(scale=True)
    target.location.y -= 0.004  # just in front of the face
    img = bpy.data.images.new("ao", width=size[0], height=size[1])
    m = bpy.data.materials.new("AOTarget")
    m.use_nodes = True
    node = m.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    m.node_tree.nodes.active = node
    target.data.materials.append(m)
    s = bpy.context.scene
    s.render.engine = "CYCLES"
    s.cycles.device = "CPU"
    s.cycles.samples = 96
    bpy.ops.object.select_all(action="DESELECT")
    source.select_set(True)
    floor.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.bake(type="AO", use_selected_to_active=True, cage_extrusion=0.06, max_ray_distance=0.12, margin=4)
    img.filepath_raw = out_png
    img.file_format = "PNG"
    img.save()
    print("[props2] baked", out_png)


# ================================================================== ParcelBox
reset()
card = mat("Cardboard", (0.55, 0.4, 0.25), 0.8)
tape = mat("Tape", (0.72, 0.6, 0.42), 0.35)
label = mat("ParcelLabel", (0.9, 0.9, 0.88), 0.6)
parts = [
    box("Box1", (0.5, 0.36, 0.32), (0, 0, 0.16), card),
    box("Tape1", (0.07, 0.365, 0.322), (0, 0, 0.16), tape),
    box("Label1", (0.14, 0.002, 0.09), (0.12, -0.181, 0.2), label),
    box("Box2", (0.34, 0.26, 0.22), (0.03, 0.02, 0.43), card, rot_z=math.radians(8)),
    box("Tape2", (0.05, 0.265, 0.222), (0.03, 0.02, 0.43), tape, rot_z=math.radians(8)),
]
o = join(parts, "ParcelBox")
set_origin(o, (0, 0, 0))
preview("ParcelBox", (0, 0, 0.28), 1.0)
export("ParcelBox")

# ================================================================== MeterBox (계량기함)
reset()
grey = mat("MeterGrey", (0.5, 0.52, 0.5), 0.5, 0.4)
win = mat("MeterWindow", (0.1, 0.12, 0.12), 0.1)
dial = mat("MeterDial", (0.85, 0.85, 0.8), 0.5)
parts = [
    box("Cab", (0.42, 0.14, 0.58), (0, -0.07, 1.55), grey),
    box("Door", (0.4, 0.01, 0.56), (0, -0.145, 1.55), grey),
    box("Win", (0.16, 0.004, 0.1), (0, -0.152, 1.66), win),
    box("Dial", (0.1, 0.002, 0.05), (0, -0.151, 1.66), dial),
    box("Latch", (0.02, 0.02, 0.05), (0.15, -0.155, 1.52), grey),
]
o = join(parts, "MeterBox")
set_origin(o, (0, 0, 0))
preview("MeterBox", (0, -0.07, 1.55), 0.9)
export("MeterBox")

# ================================================================== PottedPlant
reset()
pot = mat("PotPlastic", (0.2, 0.22, 0.2), 0.55)
leaf = mat("Leaf", (0.12, 0.26, 0.1), 0.6)
soil = mat("Soil", (0.12, 0.08, 0.05), 0.95)
bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=0.12, radius2=0.16, depth=0.3, location=(0, 0, 0.15))
p = bpy.context.active_object
p.data.materials.append(pot)
parts = [p]
bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.15, depth=0.02, location=(0, 0, 0.29))
s_ = bpy.context.active_object
s_.data.materials.append(soil)
parts.append(s_)
for i in range(22):
    a = random.uniform(0, 2 * math.pi)
    tilt = random.uniform(0.15, 0.65)
    length = random.uniform(0.45, 0.8)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, 0))
    lf = bpy.context.active_object
    lf.scale = (0.035, length / 2, 1)
    bpy.ops.object.transform_apply(scale=True)
    for v in lf.data.vertices:
        v.co.y += length / 2
        v.co.z += 0.18 * (v.co.y / length) ** 2 * -1.0
    lf.rotation_euler = (math.pi / 2 - tilt, 0, a)
    lf.location = (math.cos(a) * 0.03, math.sin(a) * 0.03, 0.29)
    lf.data.materials.append(leaf)
    parts.append(lf)
o = join(parts, "PottedPlant")
set_origin(o, (0, 0, 0))
preview("PottedPlant", (0, 0, 0.5), 1.3)
export("PottedPlant")

# ================================================================== NoticeBoard (게시판)
reset()
frame = mat("BoardFrame", (0.55, 0.56, 0.57), 0.4, 0.8)
cork = mat("NoticeBoard", (0.62, 0.48, 0.32), 0.9)
parts = [
    box("Frame", (0.92, 0.03, 0.66), (0, -0.015, 1.45), frame),
    box("Cork", (0.86, 0.008, 0.6), (0, -0.034, 1.45), cork, uv_front=True),
]
o = join(parts, "NoticeBoard")
set_origin(o, (0, 0, 0))
preview("NoticeBoard", (0, -0.02, 1.45), 1.2)
export("NoticeBoard")

# ================================================================== ElevatorPanel (층 표시기 + 호출 버튼)
reset()
steel = mat("Stainless", (0.66, 0.67, 0.68), 0.25, 1.0)
lcd = mat("FloorDisplay", (0.02, 0.02, 0.02), 0.2, emit=(1.0, 0.35, 0.08))
btn = mat("CallButton", (0.9, 0.9, 0.9), 0.3, emit=(0.9, 0.95, 1.0))
parts = [
    box("Indicator", (0.36, 0.03, 0.16), (0, -0.015, 2.42), steel),
    box("Digits", (0.18, 0.004, 0.09), (0, -0.032, 2.42), lcd),
    box("CallPlate", (0.1, 0.02, 0.2), (0.95, -0.01, 1.1), steel),
    box("Up", (0.035, 0.008, 0.035), (0.95, -0.024, 1.15), btn),
    box("Down", (0.035, 0.008, 0.035), (0.95, -0.024, 1.05), btn),
]
o = join(parts, "ElevatorPanel")
set_origin(o, (0, 0, 0))
preview("ElevatorPanel", (0.45, 0, 1.8), 2.2, yaw=10, pitch=5)
export("ElevatorPanel")

# ================================================================== AO bakes
# the door's AO (with its pressed panels) and normal map come from Tools/blender_art_bakes.py
bake_ao("HydrantCabinet.fbx", 0.61, 1.26, (0, -0.192, 1.15), os.path.join(TEX, "hydrant_ao.png"), (512, 1056))
print("[props2] done")
