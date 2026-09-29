"""
Texture for the lo-fi 키다리 (Tools/blender_tall_figure_lofi.py): GPT paints over the model's own orthographic views,
the painting becomes a PS1-style atlas.

    python Tools/gen_entity_lofi.py gen      ref views -> sheet -> Codex paint-over -> %TEMP%/nocx/entity/paint.png
    python Tools/gen_entity_lofi.py build    paint -> 256 px, 48-colour atlas -> Assets/_Project/Textures/Entities/tallfigure_lofi.png
    python Tools/gen_entity_lofi.py sheet    lo-fi turnaround renders -> Docs/art/entity_tallfigure_lofi.png

The sheet is front | left side | back in thirds, exactly the model's UV projections, so the painted views land on
the faces they were painted for. The GPT painting is archived in Docs/art/gpt_source/entity_tallfigure_paint.jpg and
`build` falls back to it (after the Blender script has rendered the reference views again). Background texels are filled from the nearby figure colours before the downsample so
edges do not pick up the white.
"""
import os
import subprocess
import sys
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORK = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "entity")
OUT = os.path.join(ROOT, "Assets", "_Project", "Textures", "Entities", "tallfigure_lofi.png")
CODEX = os.environ.get("CODEX", os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "codex.exe"))
VIEWS = ("front", "side", "back")
ATLAS, COLORS = 256, 48

PROMPT = """Use your image generation tool to create ONE image and save it as a PNG file named paint.png in the current working directory (overwrite if it exists). Do not draw it with code; generate it as an image.
The attached image is an orthographic sheet of a low-poly 3D character from our horror game: left = front view, middle = left side view (the figure faces left), right = back view. Paint over it to make the model's texture: keep every silhouette exactly where it is and at exactly the same size and proportions (the image is projected straight back onto the model), and keep the plain white background outside the figures. Flat, even lighting only: no cast shadows, no rim light, no highlights, no background.
Style: a PlayStation 1 era survival horror character texture: gritty, slightly photo-sourced look with a limited colour palette, readable at very low resolution.
Character (키다리): an unnaturally tall, thin man-like figure met at night in the corridor of a 1990s Korean apartment block. A long, worn, dark grey-brown wool overcoat buttoned up to the neck, stained and a little too short in the sleeves; thin charcoal slacks; old scuffed black shoes. Long, pale, bony hands and fingers, greyish-white skin. A small, long, pale face: deep dark eye sockets with tiny pale pupils looking straight ahead (the face must read as staring at the viewer), a thin closed mouth, sparse black hair plastered flat. Unsettling, not gory. Front: coat buttons and pockets; side: the coat's side seam; back: the back of the coat with a vent, the back of the head with thin hair.
Square image. Reply with just the saved file path."""


def sheet_path():
    return os.path.join(WORK, "ref_sheet.png")


def make_sheet():
    views = [Image.open(os.path.join(WORK, f"ref_{v}.png")).convert("RGB").resize((512, 1536)) for v in VIEWS]
    sheet = Image.new("RGB", (1536, 1536), "white")
    for i, im in enumerate(views):
        # the clay renders sit on light grey; make the background pure white for the painter
        a = np.asarray(im).astype(int)
        bg = a[5, 5]
        mask = np.abs(a - bg).sum(-1) < 6
        a[mask] = 255
        sheet.paste(Image.fromarray(a.astype(np.uint8)), (i * 512, 0))
    sheet.save(sheet_path())
    return sheet_path()


def gen():
    os.makedirs(WORK, exist_ok=True)
    ref = make_sheet()
    out = os.path.join(WORK, "paint.png")
    if os.path.exists(out):
        os.replace(out, os.path.join(WORK, f"paint_{int(time.time())}.png"))  # keep earlier takes
    t0 = time.time()
    p = subprocess.run([CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "-i", ref, "-s", "workspace-write", "-C", WORK, "-"],
                       input=PROMPT.encode("utf-8"), capture_output=True, timeout=900)
    ok = os.path.exists(out)
    print("paint", "ok" if ok else "FAILED " + " | ".join(p.stdout.decode("utf-8", "replace").strip().splitlines()[-2:]),
          f"{time.time() - t0:.0f}s")


def silhouette(size):
    """Figure mask (True inside) from the clay reference sheet, at `size`."""
    a = np.asarray(Image.open(sheet_path()).convert("RGB").resize((size, size), Image.NEAREST)).astype(int)
    return np.abs(a - 255).sum(-1) > 12


def box_blur(a, r=2):
    """Separable box blur with clamped edges (2-D float array)."""
    k = 2 * r + 1
    c = np.pad(a, r, mode="edge").cumsum(0)
    c = np.vstack([np.zeros((1, c.shape[1])), c])
    a = (c[k:] - c[:-k]) / k
    c = np.hstack([np.zeros((a.shape[0], 1)), a.cumsum(1)])
    return (c[:, k:] - c[:, :-k]) / k


def fill_background(rgb, mask, rounds=24):
    """Spread the figure's colours outward (normalised blur) so downsampled edge texels stay figure-coloured."""
    out = rgb.copy()
    known = mask.astype(float)
    for _ in range(rounds):
        w = box_blur(known)
        acc = np.stack([box_blur(out[..., c] * known) for c in range(3)], -1)
        grow = (known == 0) & (w > 0.05)
        out[grow] = acc[grow] / w[grow][:, None]
        known = np.maximum(known, grow.astype(float))
    return out


def build():
    src = os.path.join(WORK, "paint.png")
    if not os.path.exists(src):
        src = os.path.join(ROOT, "Docs", "art", "gpt_source", "entity_tallfigure_paint.jpg")
    if not os.path.exists(sheet_path()):
        make_sheet()
    paint = Image.open(src).convert("RGB").resize((1024, 1024), Image.LANCZOS)
    rgb = np.asarray(paint).astype(np.float32)
    # unknown = outside the model's silhouette, or background the painter left inside it (its outline runs a bit thin)
    paper = (rgb.min(-1) > 236) & (rgb.max(-1) - rgb.min(-1) < 12)
    rgb = fill_background(rgb, silhouette(1024) & ~paper)
    small = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).resize((ATLAS, ATLAS), Image.LANCZOS)
    # a CLUT-like palette (PS1 textures were 4/8-bit indexed), dithered
    small = small.quantize(colors=COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG).convert("RGB")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    small.save(OUT)
    print("atlas ->", OUT)


def sheet():
    """Turnaround renders (128 x 384 each) blown up with hard pixels, plus the atlas, for review."""
    turns = [Image.open(os.path.join(WORK, f"turn_{r:03d}.png")).convert("RGB") for r in (0, 35, 90, 180)]
    scale = 3
    atlas = Image.open(OUT).convert("RGB").resize((384 * 3 // 2, 384 * 3 // 2), Image.NEAREST)
    W = len(turns) * 128 * scale + atlas.width + 40
    H = 384 * scale + 60
    out = Image.new("RGB", (W, H), (18, 19, 21))
    d = ImageDraw.Draw(out)
    f = ImageFont.truetype("C:/Windows/Fonts/malgunbd.ttf", 22)
    for i, im in enumerate(turns):
        out.paste(im.resize((128 * scale, 384 * scale), Image.NEAREST), (i * 128 * scale, 50))
    out.paste(atlas, (len(turns) * 128 * scale + 40, 50))
    d.text((10, 12), "로파이 키다리 — 삼각형 648개, 256px 48색 텍스처 (GPT가 모델의 직교 뷰 위에 그림)", fill=(222, 190, 120), font=f)
    path = os.path.join(ROOT, "Docs", "art", "entity_tallfigure_lofi.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path)
    print(path)


if __name__ == "__main__":
    {"gen": gen, "build": build, "sheet": sheet}[sys.argv[1] if len(sys.argv) > 1 else "build"]()
