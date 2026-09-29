"""
Korean corridor-apartment props for the art-direction comparison, exported to Assets/_Project/Models/Props/*.fbx.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_art_props.py

- UnitDoor: steel front door leaf (0.87 x 2.06 x 0.05 m, pivot at the leaf's bottom center) with a digital door lock,
  peephole, number plate, milk slot and a door closer on the inside. The outside face is UV-mapped 0..1 so a whole
  "door front" texture fits it; the inside face is mirrored.
- DoorFrame: steel frame for a 0.9 x 2.1 m opening in a 0.2 m wall (pivot bottom center).
- CeilingLight: round surface-mounted ceiling light (직부등), 34 cm, pivot at the ceiling.
- HydrantCabinet: indoor fire hydrant cabinet (옥내소화전함) 0.65 x 1.3 x 0.18 m, front UV-mapped 0..1, pivot at the
  bottom center of its back.
Blender Z-up, fronts toward -Y; exported Y-up with the fronts toward Unity +Z.
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Props")
PREVIEW = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "props")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREVIEW, exist_ok=True)


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
        bsdf.inputs["Emission Strength"].default_value = 4.0
    return m


def box(name, size, loc, material, uv_front=None):
    """Axis-aligned box; uv_front=(w, h, x0, z0) maps the -Y face to 0..1 over that rectangle (others planar)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(loc)
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        for loop in f.loops:
            p = loop.vert.co
            if abs(n.y) > 0.5:  # front/back: door-face mapping
                if uv_front:
                    w, h, x0, z0 = uv_front
                    u = (p.x - x0) / w
                    if n.y > 0:
                        u = 1.0 - u  # the back face reads mirrored, like the other side of the same sheet
                    loop[uv].uv = (u, (p.z - z0) / h)
                else:
                    loop[uv].uv = (p.x, p.z)
            elif abs(n.x) > 0.5:
                loop[uv].uv = (p.y, p.z)
            else:
                loop[uv].uv = (p.x, p.y)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    return o


def cyl(name, r, depth, loc, material, axis="Y", verts=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    if axis == "Y":
        o.rotation_euler = (math.radians(90), 0, 0)
    elif axis == "X":
        o.rotation_euler = (0, math.radians(90), 0)
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
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
    print("[props] exported", path)


def preview(name, target, ortho, views=((0, 12), (35, 12), (180, 12))):
    s = bpy.context.scene
    s.render.engine = "BLENDER_WORKBENCH"
    s.display.shading.light = "STUDIO"
    s.display.shading.color_type = "MATERIAL"
    s.display.shading.show_cavity = True
    s.display.shading.show_shadows = True
    w = bpy.data.worlds.new("w")
    w.color = (0.55, 0.56, 0.58)
    s.world = w
    s.display.shading.background_type = "WORLD"
    s.render.resolution_x, s.render.resolution_y = 700, 900
    cd = bpy.data.cameras.new("cam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho
    cam = bpy.data.objects.new("cam", cd)
    s.collection.objects.link(cam)
    s.camera = cam
    for i, (yaw, pitch) in enumerate(views):
        y, p = math.radians(yaw), math.radians(90 - pitch)
        d = 6.0
        cam.location = (target[0] + math.sin(y) * d * math.sin(p), target[1] - math.cos(y) * d * math.sin(p), target[2] + d * math.cos(p))
        cam.rotation_euler = (p, 0, y)
        s.render.filepath = os.path.join(PREVIEW, f"{name}_{i}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ================================================================== UnitDoor
reset()
W, H, T = 0.87, 2.06, 0.05
steel = mat("DoorSteel", (0.23, 0.2, 0.19), 0.45, 0.35)
black = mat("LockBlack", (0.03, 0.03, 0.035), 0.35, 0.1)
glassk = mat("LockGlass", (0.02, 0.025, 0.03), 0.08, 0.0)
brass = mat("Brass", (0.62, 0.5, 0.28), 0.3, 1.0)
plate = mat("NumberPlate", (0.82, 0.8, 0.74), 0.4, 0.0)
alu = mat("Aluminium", (0.6, 0.61, 0.62), 0.35, 1.0)
parts = []
leaf = box("Leaf", (W, T, H), (0, 0, H / 2 + 0.01), steel, uv_front=(W, H, -W / 2, 0.01))
parts.append(leaf)
# shallow pressed panel: a thin raised frame (reads as an embossed border in raking light)
for dz, h in ((0.2, 0.012), (H - 0.2, 0.012)):
    parts.append(box("Rib", (W - 0.16, 0.004, h), (0, -T / 2 - 0.002, dz), steel))
for dx in (-(W - 0.16) / 2, (W - 0.16) / 2):
    parts.append(box("Rib", (0.012, 0.004, H - 0.4), (dx, -T / 2 - 0.002, H / 2), steel))
lx = W / 2 - 0.1  # latch side
# digital door lock, outside: tall body, keypad glass, push-pull grip
parts.append(box("LockBody", (0.075, 0.035, 0.36), (lx, -T / 2 - 0.017, 1.02), black))
parts.append(box("LockPad", (0.055, 0.004, 0.12), (lx, -T / 2 - 0.036, 1.1), glassk))
parts.append(box("LockGrip", (0.05, 0.03, 0.14), (lx, -T / 2 - 0.045, 0.93), black))
# inside body with the thumb turn
parts.append(box("LockInner", (0.085, 0.04, 0.42), (lx, T / 2 + 0.02, 1.02), black))
# peephole and number plate
parts.append(cyl("Peephole", 0.011, 0.012, (0, -T / 2 - 0.006, 1.52), brass))
parts.append(box("Plate", (0.17, 0.006, 0.075), (0, -T / 2 - 0.003, 1.72), plate))
# milk / newspaper slot (older apartments) and the door closer on the inside top
parts.append(box("Slot", (0.24, 0.006, 0.055), (0, -T / 2 - 0.003, 1.25), alu))
parts.append(box("Closer", (0.28, 0.06, 0.07), (-0.12, T / 2 + 0.03, H - 0.07), alu))
parts.append(box("CloserArm", (0.3, 0.02, 0.02), (0.05, T / 2 + 0.07, H - 0.05), alu))
door = join(parts, "UnitDoor")
set_origin(door, (0, 0, 0))
preview("UnitDoor", (0, 0, 1.05), 2.5)
export("UnitDoor")

# ================================================================== DoorFrame
reset()
fr = mat("FrameSteel", (0.2, 0.18, 0.17), 0.5, 0.3)
OW, OH, D, F = 0.9, 2.1, 0.22, 0.05
parts = [
    box("JambL", (F, D, OH + F), (-OW / 2 - F / 2, 0, (OH + F) / 2), fr),
    box("JambR", (F, D, OH + F), (OW / 2 + F / 2, 0, (OH + F) / 2), fr),
    box("Head", (OW + 2 * F, D, F), (0, 0, OH + F / 2), fr),
    box("StopL", (0.015, 0.03, OH), (-OW / 2 + 0.0075, 0.03, OH / 2), fr),
    box("StopR", (0.015, 0.03, OH), (OW / 2 - 0.0075, 0.03, OH / 2), fr),
    box("StopH", (OW, 0.03, 0.015), (0, 0.03, OH - 0.0075), fr),
]
frame = join(parts, "DoorFrame")
set_origin(frame, (0, 0, 0))
preview("DoorFrame", (0, 0, 1.05), 2.6, views=((0, 12), (40, 20)))
export("DoorFrame")

# ================================================================== CeilingLight (직부등)
reset()
base_m = mat("LampBase", (0.86, 0.86, 0.84), 0.5)
diff_m = mat("LampDiffuser", (0.95, 0.95, 0.93), 0.3, emit=(1.0, 0.97, 0.9))
parts = [cyl("Base", 0.17, 0.025, (0, 0, -0.0125), base_m, axis="Z", verts=40)]
bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=16, radius=0.15, location=(0, 0, -0.025))
dome = bpy.context.active_object
dome.name = "Dome"
dome.scale = (1, 1, 0.42)
bpy.ops.object.transform_apply(scale=True)
bm = bmesh.new()
bm.from_mesh(dome.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z > 0.0005], context="VERTS")
bm.to_mesh(dome.data)
bm.free()
dome.data.materials.append(diff_m)
parts.append(dome)
parts.append(cyl("Sensor", 0.018, 0.012, (0.12, 0, -0.03), base_m, axis="Z", verts=16))
lamp = join(parts, "CeilingLight")
bpy.ops.object.shade_smooth()
set_origin(lamp, (0, 0, 0))
preview("CeilingLight", (0, 0, -0.04), 0.6, views=((0, 25), (30, -35)))
export("CeilingLight")

# ================================================================== HydrantCabinet (옥내소화전함)
reset()
red = mat("HydrantRed", (0.62, 0.06, 0.05), 0.4, 0.2)
glass = mat("HydrantGlass", (0.08, 0.1, 0.11), 0.08, 0.0)
hinge = mat("Chrome", (0.75, 0.76, 0.78), 0.2, 1.0)
CW, CH, CD = 0.65, 1.3, 0.18
parts = [
    box("Body", (CW, CD, CH), (0, -CD / 2, CH / 2 + 0.5), red),
    box("Door", (CW - 0.04, 0.012, CH - 0.04), (0, -CD - 0.006, CH / 2 + 0.5), red, uv_front=(CW - 0.04, CH - 0.04, -(CW - 0.04) / 2, 0.52)),
    box("Window", (CW - 0.22, 0.004, 0.42), (0, -CD - 0.014, 0.5 + CH - 0.33), glass),
    box("Handle", (0.03, 0.035, 0.16), (CW / 2 - 0.07, -CD - 0.03, 0.5 + CH / 2), hinge),
]
cab = join(parts, "HydrantCabinet")
set_origin(cab, (0, 0, 0))
preview("HydrantCabinet", (0, -0.1, 1.15), 1.8, views=((0, 10), (35, 15)))
export("HydrantCabinet")
print("[props] done")
