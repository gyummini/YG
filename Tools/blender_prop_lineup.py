"""
Catalog render of the art-test props (Assets/_Project/Models/Props/*.fbx) in one Cycles shot, for review.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_prop_lineup.py

Renders on the GPU when Cycles finds one (OptiX, then CUDA), else the CPU. The door gets its baked maps
(Textures/ArtTest/door_*.png) so the panel bake can be judged outside the engine. Output: %TEMP%/nocx/props/lineup.png.
"""
import math
import os

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROPS = os.path.join(ROOT, "Assets", "_Project", "Models", "Props")
TEX = os.path.join(ROOT, "Assets", "_Project", "Textures", "ArtTest")
OUT = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "props", "lineup.png")
FONT = "C:/Windows/Fonts/malgunbd.ttf"

# name, label, x position (m); all stand on the floor line, fronts toward -Y
ROW = [
    ("DoorFrame", None, 0.0), ("UnitDoor", "현관문 (패널 베이크)", 0.0), ("MeterBox", "계량기함", 1.05),
    ("ParcelBox", "택배", 1.75), ("PottedPlant", "화분", 2.45), ("HydrantCabinet", "소화전함", 3.35),
    ("NoticeBoard", "게시판", 4.55), ("Mailboxes", "우편함", 6.4), ("ElevatorPanel", "엘리베이터 표시등·호출", 8.6),
    ("CeilingLight", "직부등", 10.0), ("StreetLamp", "가로등", 11.3),
]
WALL_MOUNTED = {"Mailboxes": 1.3}  # pivot at its back center; the others are modelled at their mounting height


def gpu():
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
            devs = [d for d in prefs.devices if d.type == kind]
            if devs:
                for d in prefs.devices:
                    d.use = d.type == kind
                return kind + ": " + ", ".join(d.name for d in devs)
        except TypeError:
            continue
    return None


def door_maps(obj):
    """Door leaf material gets the baked albedo/normal/AO (the leaf's front is UV 0..1)."""
    for slot in obj.material_slots:
        m = slot.material
        if m is None or not m.name.startswith("DoorSteel"):
            continue
        nt = m.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        alb = nt.nodes.new("ShaderNodeTexImage")
        alb.image = bpy.data.images.load(os.path.join(TEX, "door_albedo.png"))
        ao = nt.nodes.new("ShaderNodeTexImage")
        ao.image = bpy.data.images.load(os.path.join(TEX, "door_ao.png"))
        ao.image.colorspace_settings.name = "Non-Color"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(alb.outputs["Color"], mix.inputs["A"])
        nt.links.new(ao.outputs["Color"], mix.inputs["B"])
        nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
        nrm = nt.nodes.new("ShaderNodeTexImage")
        nrm.image = bpy.data.images.load(os.path.join(TEX, "door_normal.png"))
        nrm.image.colorspace_settings.name = "Non-Color"
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])


bpy.ops.wm.read_factory_settings(use_empty=True)
s = bpy.context.scene
device = gpu()
s.render.engine = "CYCLES"
s.cycles.device = "GPU" if device else "CPU"
s.cycles.samples = 96
s.cycles.use_denoising = True
s.render.resolution_x, s.render.resolution_y = 2000, 700
s.view_settings.view_transform = "AgX"
print("[lineup] device:", device or "CPU")

# backdrop wall + floor
bpy.ops.mesh.primitive_plane_add(size=1, location=(5.6, 0.0, 1.6), rotation=(math.pi / 2, 0, 0))
wall = bpy.context.active_object
wall.scale = (15, 3.2, 1)
bpy.ops.mesh.primitive_plane_add(size=1, location=(5.6, -2.0, 0))
floor = bpy.context.active_object
floor.scale = (15, 4, 1)
for o, col in ((wall, (0.62, 0.6, 0.55)), (floor, (0.25, 0.25, 0.24))):
    m = bpy.data.materials.new(o.name + "Mat")
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*col, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
    o.data.materials.append(m)

label_font = bpy.data.fonts.load(FONT)
for name, label, x in ROW:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(PROPS, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    z = WALL_MOUNTED.get(name, 0.0)
    for r in roots:
        r.location = (x, 0.0, z) if name != "CeilingLight" else (x, -0.6, 1.2)
        if name == "CeilingLight":
            r.rotation_euler = (math.radians(-70), 0, 0)  # tilt the ceiling fixture toward the camera
        if name == "StreetLamp":
            r.scale = (0.3, 0.3, 0.3)
    if name == "UnitDoor":
        for o in new:
            door_maps(o)
    if label:
        bpy.ops.object.text_add(location=(x, -0.02, 2.65), rotation=(math.pi / 2, 0, 0))  # on the wall, above the props
        t = bpy.context.active_object
        t.data.body = label
        t.data.font = label_font
        t.data.size = 0.14
        t.data.align_x = "CENTER"
        m = bpy.data.materials.new("Label")
        m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.05, 0.06, 1)
        t.data.materials.append(m)

# light: soft key from the front-left, rim from above, dim world
bpy.ops.object.light_add(type="AREA", location=(2.0, -5.0, 4.0))
key = bpy.context.active_object
key.data.energy = 500
key.data.size = 6
key.rotation_euler = (math.radians(50), 0, math.radians(-15))
bpy.ops.object.light_add(type="AREA", location=(8.0, -3.0, 5.0))
fill = bpy.context.active_object
fill.data.energy = 160
fill.data.size = 8
fill.rotation_euler = (math.radians(35), 0, math.radians(20))
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.02, 0.022, 0.026, 1)
s.world = world

cd = bpy.data.cameras.new("cam")
cd.lens = 28
cam = bpy.data.objects.new("cam", cd)
s.collection.objects.link(cam)
s.camera = cam
cam.location = (5.6, -10.5, 1.35)
cam.rotation_euler = (math.radians(88), 0, 0)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
s.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("[lineup] wrote", OUT)
