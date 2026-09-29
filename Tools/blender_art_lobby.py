"""
Lobby dressing for the art-direction test: the residents' mailbox bank (세대별 우편함) that replaces the greybox
"Mailboxes" block on the lobby's office wall. Exported to Assets/_Project/Models/Props/Mailboxes.fbx; the number labels
use the atlas Assets/_Project/Textures/ArtTest/signs/mailbox_labels.png (Tools/gen_art_signs.py).

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/blender_art_lobby.py

2.2 x 1.1 x 0.28 m painted-steel cabinet, 9 x 3 small doors (rows 4xx / 3xx / 2xx from the top), each with a mail slot,
a keyhole and a number label (UVs into the atlas), flyers stuck in some slots. Pivot at the back center, front = -Y
(Unity +Z). Also writes a Workbench preview to %TEMP%/nocx/props/.
"""
import math
import os
import random

import bmesh
import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Models", "Props")
PREVIEW = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "props")
os.makedirs(PREVIEW, exist_ok=True)
random.seed(9)

COLS, ROWS = 9, 3  # must match the label atlas in gen_art_signs.py
W, H, D = 2.2, 1.1, 0.28
MARGIN = 0.04
CELL_W, CELL_H = (W - 2 * MARGIN) / COLS, (H - 2 * MARGIN) / ROWS


def mat(name, color, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    return m


class Builder:
    """Axis-aligned boxes into one bmesh with material slots; world-meter UVs unless a front-face UV rect is given."""

    FACES = (((0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)), ((1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)),
             ((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)), ((0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)),
             ((0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)), ((0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)))

    def __init__(self, materials):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.materials = materials
        self.index = {m.name: i for i, m in enumerate(materials)}

    def box(self, x0, x1, y0, y1, z0, z1, material, front_uv=None):
        xs, ys, zs = (x0, x1), (y0, y1), (z0, z1)
        v = {(ix, iy, iz): self.bm.verts.new((xs[ix], ys[iy], zs[iz])) for ix in (0, 1) for iy in (0, 1) for iz in (0, 1)}
        for k, quad in enumerate(self.FACES):
            if k == 3:
                continue  # +Y faces the wall
            f = self.bm.faces.new([v[c] for c in quad])
            f.material_index = self.index[material]
            n = f.normal
            for loop in f.loops:
                p = loop.vert.co
                if k == 2 and front_uv:  # the -Y (front) face: map the rect (u0, v0, u1, v1) across it
                    u0, v0, u1, v1 = front_uv
                    loop[self.uv].uv = (u0 + (p.x - x0) / (x1 - x0) * (u1 - u0), v0 + (p.z - z0) / (z1 - z0) * (v1 - v0))
                elif abs(n.x) > 0.5:
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


bpy.ops.wm.read_factory_settings(use_empty=True)
M = [
    mat("MailboxSteel", (0.42, 0.46, 0.5), 0.5, 0.3),
    mat("MailboxDoor", (0.5, 0.54, 0.57), 0.45, 0.3),
    mat("MailboxSlot", (0.02, 0.02, 0.02), 0.8),
    mat("MailboxLabel", (0.9, 0.9, 0.85), 0.5),
    mat("Brass", (0.62, 0.5, 0.28), 0.3, 1.0),
    mat("Flyer", (0.92, 0.9, 0.82), 0.7),
    mat("FlyerColor", (0.95, 0.75, 0.3), 0.7),
]
b = Builder(M)
front = -D
b.box(-W / 2, W / 2, front, 0.0, -H / 2, H / 2, "MailboxSteel")
flyers = 0
for row in range(ROWS):  # row 0 = top (4xx)
    for col in range(COLS):
        cx = -W / 2 + MARGIN + (col + 0.5) * CELL_W
        cz = H / 2 - MARGIN - (row + 0.5) * CELL_H
        hw, hh = CELL_W / 2 - 0.008, CELL_H / 2 - 0.008
        b.box(cx - hw, cx + hw, front - 0.01, front, cz - hh, cz + hh, "MailboxDoor")
        door = front - 0.01
        b.box(cx - 0.075, cx + 0.075, door - 0.0015, door, cz + 0.068, cz + 0.092, "MailboxSlot")
        # atlas: 9 x 3 cells, v measured from the bottom of the image (row 0 = top)
        u0, u1 = col / COLS, (col + 1) / COLS
        v0, v1 = (ROWS - 1 - row) / ROWS, (ROWS - row) / ROWS
        b.box(cx - 0.06, cx + 0.06, door - 0.0012, door, cz - 0.055, cz - 0.005, "MailboxLabel", (u0, v0, u1, v1))
        b.box(cx + 0.07, cx + 0.085, door - 0.004, door, cz - 0.1, cz - 0.085, "Brass")
        if random.random() < 0.3:
            flyers += 1
            fw = random.uniform(0.09, 0.13)
            ox = random.uniform(-0.03, 0.03)
            b.box(cx + ox - fw / 2, cx + ox + fw / 2, door - random.uniform(0.03, 0.06), door, cz + 0.07, cz + 0.07 + random.uniform(0.05, 0.09),
                  "Flyer" if random.random() < 0.6 else "FlyerColor")
o = b.finish("Mailboxes")
print(f"[lobby] mailboxes: {len(o.data.polygons)} faces, {flyers} flyers")

# preview (Workbench, at an angle)
s = bpy.context.scene
s.render.engine = "BLENDER_WORKBENCH"
s.display.shading.light = "STUDIO"
s.display.shading.color_type = "MATERIAL"
s.display.shading.show_cavity = True
w = bpy.data.worlds.new("w")
w.color = (0.55, 0.56, 0.58)
s.world = w
s.display.shading.background_type = "WORLD"
s.render.resolution_x, s.render.resolution_y = 1000, 560
cd = bpy.data.cameras.new("cam")
cd.type = "ORTHO"
cd.ortho_scale = 2.5
cam = bpy.data.objects.new("cam", cd)
s.collection.objects.link(cam)
s.camera = cam
cam.location = (1.6, -3.0, 0.4)
cam.rotation_euler = (math.radians(84), 0, math.radians(28))
s.render.filepath = os.path.join(PREVIEW, "Mailboxes.png")
bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam)

bpy.ops.object.select_all(action="SELECT")
path = os.path.join(OUT, "Mailboxes.fbx")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
print("[lobby] exported", path)
