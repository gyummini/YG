"""
Turnaround renders of the two entity models (Workbench, orthographic) for review: front / 3-4 / side next to a
0.9 x 2.1 m door frame, 키다리 vs 배웅꾼 hands (6 / 5 fingers), the person's face. PNGs go to %TEMP%/nocx/models.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_render_models.py
"""
import math
import os

import bpy

OUT = os.path.join(os.environ["TEMP"], "nocx", "models")
os.makedirs(OUT, exist_ok=True)
MODELS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets", "_Project", "Models")


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.render.engine = "BLENDER_WORKBENCH"
    s.display.shading.light = "STUDIO"
    s.display.shading.color_type = "MATERIAL"
    s.display.shading.show_cavity = True
    s.display.shading.show_shadows = True
    s.render.film_transparent = False
    w = bpy.data.worlds.new("w")
    w.color = (0.62, 0.63, 0.65)
    s.world = w
    s.display.shading.background_type = "WORLD"
    return s


def camera(s, ortho, loc, rot):
    cd = bpy.data.cameras.new("cam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho
    cam = bpy.data.objects.new("cam", cd)
    s.collection.objects.link(cam)
    s.camera = cam
    cam.location = loc
    cam.rotation_euler = rot
    return cam


def shoot(s, cam, name, ortho, target, yaw_deg, dist=8.0, pitch_deg=90.0, res=(900, 1200)):
    s.render.resolution_x, s.render.resolution_y = res
    cam.data.ortho_scale = ortho
    yaw = math.radians(yaw_deg)
    # camera circles the model; yaw 0 = looking at its face (Blender -Y side)
    cam.location = (target[0] + math.sin(yaw) * dist, target[1] - math.cos(yaw) * dist, target[2])
    cam.rotation_euler = (math.radians(pitch_deg), 0, yaw)
    s.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)


def tint(prefix_colors):
    for m in bpy.data.materials:
        for k, c in prefix_colors.items():
            if m.name.startswith(k):
                m.diffuse_color = c


def door_frame(x):
    """0.9 x 2.1 m door frame outline for scale."""
    mat = bpy.data.materials.new("DoorFrame")
    mat.diffuse_color = (0.42, 0.3, 0.2, 1)
    for n, loc, sc in (("jambL", (x - 0.47, 0, 1.05), (0.04, 0.1, 2.1)), ("jambR", (x + 0.47, 0, 1.05), (0.04, 0.1, 2.1)),
                       ("head", (x, 0, 2.12), (0.98, 0.1, 0.04))):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object
        o.name = n
        o.scale = sc
        o.data.materials.append(mat)


def floor():
    mat = bpy.data.materials.new("Floor")
    mat.diffuse_color = (0.5, 0.5, 0.5, 1)
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(mat)


# ------------------------------------------------------------------ tall figure (키다리 / 배웅꾼)
s = reset()
bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, "TallFigure.fbx"))
tint({"Figure": (0.07, 0.07, 0.08, 1)})
door_frame(1.1)
floor()
cam = camera(s, 3.2, (0, -8, 1.3), (math.radians(90), 0, 0))
shoot(s, cam, "tall_front", 3.2, (0.45, 0, 1.3), 0)
shoot(s, cam, "tall_34", 3.2, (0.2, 0, 1.3), 35)
for o in bpy.data.objects:
    if o.name.startswith(("jamb", "head")):
        o.hide_render = True
shoot(s, cam, "tall_side", 3.2, (0.0, 0, 1.3), 90)
# hands: 키다리 (extra finger shown) vs 배웅꾼 (hidden)
extra = [o for o in bpy.data.objects if o.name.startswith("ExtraFinger")]
hand = next(o for o in bpy.data.objects if o.name.startswith("HandPoint_L"))
hp = hand.matrix_world.translation
shoot(s, cam, "tall_hand6", 0.5, (hp.x, hp.y, hp.z - 0.06), 90, dist=3.0, res=(700, 700))
for o in extra:
    o.hide_render = True
shoot(s, cam, "tall_hand5", 0.5, (hp.x, hp.y, hp.z - 0.06), 90, dist=3.0, res=(700, 700))

# ------------------------------------------------------------------ person (동승자 / 뒷사람)
s = reset()
bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, "Person.fbx"))
tint({"Body": (0.07, 0.07, 0.08, 1), "Skin": (0.78, 0.76, 0.72, 1), "Eyes": (0.01, 0.01, 0.01, 1)})
door_frame(0.95)
floor()
cam = camera(s, 2.6, (0, -8, 1.0), (math.radians(90), 0, 0))
shoot(s, cam, "person_front", 2.6, (0.35, 0, 1.05), 0)
shoot(s, cam, "person_34", 2.6, (0.15, 0, 1.05), 35)
for o in bpy.data.objects:
    if o.name.startswith(("jamb", "head")):
        o.hide_render = True
shoot(s, cam, "person_side", 2.6, (0.0, 0, 1.05), 90)
head = next(o for o in bpy.data.objects if o.name.startswith("HeadPoint"))
h = head.matrix_world.translation
shoot(s, cam, "person_face", 0.5, (h.x, h.y, h.z - 0.03), 0, dist=3.0, res=(700, 700))
print("rendered models to", OUT)
