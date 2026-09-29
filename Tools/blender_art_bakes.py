"""
Baked detail for the unit door (art-direction test). The pressed (embossed) panels of a steel apartment door are
modelled as a high-poly front and baked with Cycles onto the door face's 0..1 UVs, so the low-poly door in the game
gets them for free:
    Assets/_Project/Textures/ArtTest/door_normal.png   tangent-space normals (OpenGL / Unity convention)
    Assets/_Project/Textures/ArtTest/door_ao.png       AO of panels + lock + plate + floor contact (replaces the
                                                       flat-face bake from blender_art_props2.py)

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_art_bakes.py

Both bakes are "selected to active" onto a plane with the same 0..1 UVs as the door's outside face (u to the right,
v up, seen from the corridor). Panels sit between the lock stile and the hinge stile, clear of the lock (x = 0.335),
the milk slot (1.25 m) sits between them; the peephole and the number plate are on the upper panel.
"""
import os

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROPS = os.path.join(ROOT, "Assets", "_Project", "Models", "Props")
TEX = os.path.join(ROOT, "Assets", "_Project", "Textures", "ArtTest")
PREVIEW = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "props")
os.makedirs(PREVIEW, exist_ok=True)

W, H = 0.87, 2.06          # door leaf (UnitDoor.fbx)
FRONT_Y, FACE_Z = -0.025, 1.04
PANELS = ((-0.27, 0.27, 1.38, 1.95), (-0.27, 0.27, 0.18, 1.12))  # x0, x1, z0, z1 (m)
RAISE, BEVEL = 0.004, 0.007
SIZE = (768, 1824)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def high_poly():
    """Flat front plus the raised panels (thin beveled boxes half sunk into the face)."""
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, FRONT_Y, FACE_Z), rotation=(1.5707963, 0, 0))
    base = bpy.context.active_object
    base.scale = (W, H, 1)
    parts = [base]
    for x0, x1, z0, z1 in PANELS:
        bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2, FRONT_Y, (z0 + z1) / 2))
        p = bpy.context.active_object
        p.scale = (x1 - x0, RAISE * 2, z1 - z0)
        bpy.ops.object.transform_apply(scale=True)
        bev = p.modifiers.new("bevel", "BEVEL")
        bev.width = BEVEL
        bev.segments = 5
        bev.limit_method = "ANGLE"
        parts.append(p)
    return parts


def target_plane(image):
    """The low-poly stand-in: the face rectangle just in front of the panels, UVs 0..1."""
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, FRONT_Y - RAISE - 0.002, FACE_Z), rotation=(1.5707963, 0, 0))
    t = bpy.context.active_object
    t.scale = (W, H, 1)
    bpy.ops.object.transform_apply(scale=True)
    m = bpy.data.materials.new("BakeTarget")
    m.use_nodes = True
    node = m.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    m.node_tree.nodes.active = node
    t.data.materials.append(m)
    return t


def bake(kind, sources, image, **kw):
    s = bpy.context.scene
    s.render.engine = "CYCLES"
    s.cycles.device = "CPU"
    s.cycles.samples = 128 if kind == "AO" else 8
    t = target_plane(image)
    bpy.ops.object.select_all(action="DESELECT")
    for o in sources:
        o.select_set(True)
    t.select_set(True)
    bpy.context.view_layer.objects.active = t
    bpy.ops.object.bake(type=kind, use_selected_to_active=True, margin=8, **kw)
    bpy.data.objects.remove(t)


def save(image, name):
    path = os.path.join(TEX, name)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("[bakes] wrote", path)


# ------------------------------------------------------------------ normal: panels only
reset()
hp = high_poly()
normal = bpy.data.images.new("door_normal", width=SIZE[0], height=SIZE[1])
normal.colorspace_settings.name = "Non-Color"
bake("NORMAL", hp, normal, normal_space="TANGENT", cage_extrusion=0.01, max_ray_distance=0.03)
save(normal, "door_normal.png")

# ------------------------------------------------------------------ AO: panels + the modelled door parts + floor
reset()
bpy.ops.import_scene.fbx(filepath=os.path.join(PROPS, "UnitDoor.fbx"))
door = [o for o in bpy.data.objects if o.type == "MESH"]
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
floor = bpy.context.active_object
hp = high_poly()
ao = bpy.data.images.new("door_ao", width=SIZE[0] // 2, height=SIZE[1] // 2)
bake("AO", door + [floor] + hp, ao, cage_extrusion=0.06, max_ray_distance=0.12)
save(ao, "door_ao.png")
