"""
Night backdrop for the open corridor (art-direction test): the next apartment block across the parking lot and a
parking-lot street lamp, exported to Assets/_Project/Models/Props/*.fbx.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_art_backdrop.py

- ApartmentBlock: 12-storey slab (24 bays x 3.4 m, 2.8 m floors) seen from its balcony side: slab bands, piers,
  sash-window balconies on concrete parapets, two stair cores with small landing windows, rooftop machine rooms with
  aviation lights. Windows get per-pane materials (dark / lit warm / curtained / cool / TV glow, ~15 % lit at 3 a.m.),
  so the engine only has to swap materials by name. Pivot at the bottom center of the front face, front = -Y (Unity +Z).
- StreetLamp: 7.5 m pole with a sodium head, pivot at the base.
Also writes a Workbench preview to %TEMP%/nocx/props/.
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Props")
PREVIEW = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "props")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREVIEW, exist_ok=True)
random.seed(105)

FLOORS, FLOOR_H = 12, 2.8
BAYS, BAY_W = 24, 3.4
CORES = (6, 17)  # bay indices that are stair cores
LENGTH = BAYS * BAY_W
HEIGHT = FLOORS * FLOOR_H


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, color, rough=0.6, metal=0.0, emit=None, strength=3.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


class Builder:
    """Axis-aligned boxes into one bmesh with material slots and world-meter UVs (one object, one draw per material)."""

    FACES = (((0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)), ((1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)),
             ((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)), ((0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)),
             ((0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)), ((0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)))

    def __init__(self, materials):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.materials = materials
        self.index = {m.name: i for i, m in enumerate(materials)}

    def box(self, x0, x1, y0, y1, z0, z1, material, skip_back=False):
        xs, ys, zs = (x0, x1), (y0, y1), (z0, z1)
        v = {}
        for ix in (0, 1):
            for iy in (0, 1):
                for iz in (0, 1):
                    v[(ix, iy, iz)] = self.bm.verts.new((xs[ix], ys[iy], zs[iz]))
        for k, quad in enumerate(self.FACES):
            if skip_back and k == 3:
                continue  # +Y faces into the building
            f = self.bm.faces.new([v[c] for c in quad])
            f.material_index = self.index[material]
            n = f.normal if f.normal.length > 0 else Vector((0, 0, 1))
            for loop in f.loops:
                p = loop.vert.co
                if abs(n.x) > 0.5:
                    loop[self.uv].uv = (p.y, p.z)
                elif abs(n.y) > 0.5:
                    loop[self.uv].uv = (p.x, p.z)
                else:
                    loop[self.uv].uv = (p.x, p.y)

    def finish(self, name):
        self.bm.normal_update()
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.materials:
            me.materials.append(m)
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        return o


def export(name):
    bpy.ops.object.select_all(action="SELECT")
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
    print("[backdrop] exported", path)


def preview(name, loc, rot, ortho):
    s = bpy.context.scene
    s.render.engine = "BLENDER_WORKBENCH"
    s.display.shading.light = "STUDIO"
    s.display.shading.color_type = "MATERIAL"
    s.display.shading.show_cavity = True
    w = bpy.data.worlds.new("w")
    w.color = (0.2, 0.21, 0.24)
    s.world = w
    s.display.shading.background_type = "WORLD"
    s.render.resolution_x, s.render.resolution_y = 1400, 700
    cd = bpy.data.cameras.new("cam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho
    cam = bpy.data.objects.new("cam", cd)
    s.collection.objects.link(cam)
    s.camera = cam
    cam.location = loc
    cam.rotation_euler = rot
    s.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ================================================================== ApartmentBlock
reset()
M = [
    mat("FacadeConcrete", (0.5, 0.5, 0.47), 0.9),
    mat("FacadeParapet", (0.58, 0.57, 0.53), 0.9),
    mat("FacadeFrame", (0.7, 0.7, 0.68), 0.4),
    mat("WinDark", (0.015, 0.018, 0.022), 0.05),
    mat("WinLitWarm", (0.1, 0.08, 0.05), 0.3, emit=(1.0, 0.6, 0.28)),
    mat("WinCurtainWarm", (0.1, 0.07, 0.04), 0.6, emit=(0.8, 0.45, 0.2), strength=1.2),
    mat("WinLitCool", (0.08, 0.09, 0.1), 0.3, emit=(0.7, 0.85, 0.95)),
    mat("WinTV", (0.03, 0.04, 0.08), 0.3, emit=(0.25, 0.4, 0.95), strength=1.5),
    mat("StairLit", (0.1, 0.11, 0.1), 0.4, emit=(0.8, 0.9, 0.85), strength=1.0),
    mat("RoofDark", (0.25, 0.25, 0.25), 0.9),
    mat("AviationRed", (0.3, 0.02, 0.02), 0.4, emit=(1.0, 0.05, 0.02), strength=8.0),
]
b = Builder(M)
x_left = -LENGTH / 2

# building mass behind the facade (front face at y = 0.3), roof parapet and machine rooms
b.box(x_left, -x_left, 0.3, 12.0, 0.0, HEIGHT, "FacadeConcrete")
b.box(x_left, -x_left, -0.12, 0.3, HEIGHT, HEIGHT + 1.0, "FacadeParapet", skip_back=True)
for core in CORES:
    cx = x_left + (core + 0.5) * BAY_W
    b.box(cx - 2.5, cx + 2.5, 2.0, 7.0, HEIGHT, HEIGHT + 3.2, "RoofDark")
    b.box(cx + 2.1, cx + 2.4, 1.9, 2.1, HEIGHT + 3.0, HEIGHT + 3.3, "AviationRed")

lit_rooms = 0
for f in range(FLOORS):
    z = f * FLOOR_H
    # slab band + header over the windows below it, one strip per floor line
    b.box(x_left, -x_left, -0.12, 0.3, max(0.0, z - 0.25), z + 0.13, "FacadeConcrete", skip_back=True)
    for bay in range(BAYS):
        x0 = x_left + bay * BAY_W
        x1 = x0 + BAY_W
        if bay in CORES:
            b.box(x0, x1, -0.06, 0.3, z + 0.13, z + FLOOR_H - 0.25, "FacadeConcrete", skip_back=True)
            wz = z + 1.3
            b.box(x0 + 1.25, x1 - 1.25, -0.08, -0.06, wz, wz + 0.9, "StairLit" if random.random() < 0.55 else "WinDark")
            continue
        # piers on both sides of the bay
        b.box(x0, x0 + 0.12, -0.08, 0.3, z + 0.13, z + FLOOR_H - 0.25, "FacadeConcrete", skip_back=True)
        b.box(x1 - 0.12, x1, -0.08, 0.3, z + 0.13, z + FLOOR_H - 0.25, "FacadeConcrete", skip_back=True)
        if f == 0:
            # ground floor: planting strip and a blank wall (piloti-style storage), no windows
            b.box(x0 + 0.12, x1 - 0.12, -0.02, 0.3, z + 0.13, z + FLOOR_H - 0.25, "FacadeParapet", skip_back=True)
            continue
        # parapet, then a three-pane aluminium sash above it
        b.box(x0 + 0.12, x1 - 0.12, -0.05, 0.3, z + 0.13, z + 1.0, "FacadeParapet", skip_back=True)
        room = random.random()
        kind = "lit" if room < 0.11 else "cool" if room < 0.14 else "tv" if room < 0.17 else "dark"
        lit_rooms += kind != "dark"
        pane_w = (BAY_W - 0.24) / 3
        for p in range(3):
            px0 = x0 + 0.12 + p * pane_w
            if kind == "lit":
                m = "WinLitWarm" if random.random() < 0.6 else "WinCurtainWarm"
            elif kind == "cool":
                m = "WinLitCool" if random.random() < 0.7 else "WinCurtainWarm"
            elif kind == "tv":
                m = "WinTV"
            else:
                m = "WinDark"
            b.box(px0, px0 + pane_w, 0.04, 0.06, z + 1.0, z + FLOOR_H - 0.25, m, skip_back=True)
            b.box(px0 + pane_w - 0.03, px0 + pane_w + 0.03, -0.01, 0.04, z + 1.0, z + FLOOR_H - 0.25, "FacadeFrame", skip_back=True)
        b.box(x0 + 0.12, x1 - 0.12, -0.03, 0.04, z + 1.0, z + 1.06, "FacadeFrame", skip_back=True)  # sill rail
block = b.finish("ApartmentBlock")
print(f"[backdrop] block: {len(block.data.polygons)} faces, lit rooms {lit_rooms}/{(FLOORS - 1) * (BAYS - len(CORES))}")
preview("ApartmentBlock", (0, -120, HEIGHT / 2), (math.radians(90), 0, 0), LENGTH * 1.05)
export("ApartmentBlock")

# ================================================================== StreetLamp
reset()
M = [mat("LampPole", (0.3, 0.31, 0.3), 0.5, 0.5), mat("SodiumLamp", (0.4, 0.3, 0.1), 0.3, emit=(1.0, 0.55, 0.15), strength=10.0)]
b = Builder(M)
b.box(-0.08, 0.08, -0.08, 0.08, 0.0, 7.5, "LampPole")
b.box(-0.05, 0.05, -1.3, 0.0, 7.35, 7.45, "LampPole")
b.box(-0.2, 0.2, -1.55, -1.05, 7.25, 7.42, "LampPole")
b.box(-0.17, 0.17, -1.52, -1.08, 7.22, 7.25, "SodiumLamp")
lamp = b.finish("StreetLamp")
preview("StreetLamp", (8, -8, 5), (math.radians(80), 0, math.radians(45)), 9)
export("StreetLamp")
