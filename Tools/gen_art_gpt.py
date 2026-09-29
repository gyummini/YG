"""
GPT (Codex image generation) side of the art-direction test.

    python Tools/gen_art_gpt.py concepts [key ...]   paint-overs of the in-engine shots -> TestResults/art/<shot>_G<look>.png
    python Tools/gen_art_gpt.py tiles [name ...]     material tiles -> %TEMP%/nocx/art/tile_<name>.png
    python Tools/gen_art_gpt.py build                tiles -> Assets/_Project/Textures/ArtTest/gpt/ (same maps and layout
                                                     as Tools/gen_art_textures.py: delit, color-matched, made seamless)
    python Tools/gen_art_gpt.py status

Concepts attach the in-engine capture of the same shot and look (TestResults/art/<shot>_<look>.png, from
NightOffice/Art/Capture Look Comparison) so the paint-over keeps our camera and layout; the gap between the two is the
to-do list for that direction. Tiles become looks D (= A) and E (= C) in the capture tool.
Needs the Codex CLI (CODEX env var, default %TEMP%/nocx/codex.exe with codex-code-mode-host.exe beside it).
"""
import os
import shutil
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_art_textures as proc  # noqa: E402  (shared layouts and map writers)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "TestResults", "art")
WORK = os.environ.get("ART_GPT_WORK", os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "art"))
GPT_OUT = os.environ.get("ART_GPT_OUT", os.path.join(proc.OUT, "gpt"))
CODEX = os.environ.get("CODEX", os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "codex.exe"))

GEN = ("Use your image generation tool to create ONE image and save it as a PNG file named {file} in the current "
       "working directory (overwrite if it exists). Do not draw it with code; generate it as an image.\n")

# ------------------------------------------------------------------ concepts
SETTING = ("Setting: the common areas of an old 1990s Korean corridor-type apartment block (복도식 아파트) at night, as the "
           "night-shift building manager sees them. Cream-painted concrete walls with a dark green painted lower band up "
           "to about 1 m, terrazzo floor, painted concrete ceiling with round surface-mounted ceiling lights, steel unit "
           "front doors with digital keypad locks and small number plates, grey electric meter boxes beside the doors.")
SHOTS = {
    "corridor": "This shot: the 3rd-floor open corridor — a long straight walkway; the open side with a concrete parapet "
                "and handrail on the left looks out into the night; unit front doors and meter boxes along the right "
                "wall; ceiling lights receding into the distance.",
    "hall": "This shot: the 3rd-floor elevator hall — stainless elevator doors with a small orange floor indicator above "
            "and the call button beside them, a cork notice board with pinned notices on the left, a painted floor "
            "number in a circle on the wall, a grey steel fire door on the right.",
    "lobby": "This shot: the ground-floor lobby — a wide, empty space; a wall board on the left, the management office "
             "door with a small blue sign in the middle, another door on the right, round ceiling lights.",
    "door": "This shot: one resident's front door up close — dark brown painted steel door with a digital keypad lock on "
            "the latch side and a small number plate, in a steel frame; a grey electric meter box on the wall beside it.",
}
DIRECTIONS = {
    "A": "Direction A, grounded photorealism: looks like a real photograph or a modern Unreal Engine 5 horror game. "
         "Neutral-warm white ceiling lights (about 4000 K) with natural falloff and soft shadows, physically plausible "
         "materials (painted concrete, terrazzo, enamel, stainless), subtle dust, grime and wear, believable everyday "
         "details. Quiet, lonely and uneasy.",
    "B": "Direction B, stylized low-fi PS1-era survival horror: low-poly shapes, low-resolution pixelated textures with "
         "visible texel blocks, 15-bit color with ordered dithering, a limited palette, dense dark fog swallowing "
         "everything beyond a few meters; it should look like a 320x240 frame upscaled with hard pixels. Eerie and "
         "nostalgic.",
    "C": "Direction C, restrained cinematic: a still from a quiet Korean horror film shot on a digital cinema camera. "
         "Cold greenish fluorescent grade, deep soft blacks, muted desaturated colors, gentle film grain, subtle vignette "
         "and slight chromatic aberration, a lot of negative space. Dread through restraint.",
}
CONCEPT_RULES = ("The attached image is an in-engine screenshot of this shot from our work-in-progress game. Paint over it: "
                 "keep the same camera position, angle, field of view and composition, and keep every major element in "
                 "the same place and at the same size (walls, floor, ceiling, doors, lights, props, openings). Improve "
                 "materials, lighting, detail and atmosphere so it looks like a finished, shippable game in the direction "
                 "below. No people or creatures, no added text, captions, UI or watermarks. Landscape 3:2 image.")


def concept_keys():
    return [f"{shot}_G{look}" for look in "ACB" for shot in SHOTS]


def concept_prompt(key):
    shot, look = key.split("_G")
    return (GEN.format(file=key + ".png") + CONCEPT_RULES + "\n" + SETTING + "\n" + SHOTS[shot] + "\n" + DIRECTIONS[look]
            + "\nReply with just the saved file path.")


# ------------------------------------------------------------------ material tiles
TILE_STYLE = ("Seamless tileable PBR albedo texture, square, viewed straight on (orthographic, no perspective), flat even "
              "lighting with no shadows, highlights or vignetting, filling the whole image edge to edge; no objects, no "
              "text, no borders.")
TILES = {
    "wall_paint": "old matte cream/off-white latex paint on a concrete wall of a 1990s Korean apartment corridor: subtle "
                  "roller texture, a few hairline cracks, faint scuffs and light grime; about 1.6 m x 1.6 m of wall.",
    "band_paint": "dark green-grey glossy enamel paint (the lower wainscot band of a Korean apartment corridor): slightly "
                  "uneven brush strokes, small chips showing lighter paint underneath, scuffs from shoes and trolleys; "
                  "about 1.6 m x 1.6 m.",
    "floor_terrazzo": "polished terrazzo floor (인조석 물갈기) in grey cement with small white, black and beige stone chips "
                      "3-15 mm across, worn and slightly dirty with fine scratches; about 2 m x 2 m seen from above.",
    "ceiling_paint": "matte white painted concrete ceiling: very subtle unevenness and one or two faint yellowish water "
                     "stains; about 2 m x 2 m.",
    "door_paint": "dark brown baked-enamel painted steel of an apartment front door: fine orange-peel texture, light "
                  "scratches and scuffs, slight dirt; about 1 m x 1 m.",
    "stainless": "brushed stainless steel sheet with fine horizontal hairline brushing, a few fingerprints and smudges, "
                 "as on old elevator doors; about 1 m x 1 m.",
    "parapet_concrete": "weathered painted exterior concrete, light grey, rough with small pores, rain streaks and dirt, "
                        "as on the parapet of an open-air apartment corridor; about 2 m x 2 m.",
}


def tile_prompt(name):
    return GEN.format(file=f"tile_{name}.png") + TILE_STYLE + "\nSubject: " + TILES[name] + "\nReply with just the saved file path."


# ------------------------------------------------------------------ codex
def run_codex(prompt, out_file, attach=None):
    cmd = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral"]
    if attach:
        cmd += ["-i", attach]
    cmd += ["-s", "workspace-write", "-C", WORK, "-"]
    t0 = time.time()
    p = subprocess.run(cmd, input=prompt.encode("utf-8"), capture_output=True, timeout=900)
    ok = os.path.exists(out_file)
    tail = "" if ok else " | ".join(p.stdout.decode("utf-8", "replace").strip().splitlines()[-2:])
    return ok, tail, time.time() - t0


def gen_concept(key):
    out = os.path.join(WORK, key + ".png")
    if os.path.exists(out):
        return key, "exists", 0.0
    shot, look = key.split("_G")
    ref = os.path.join(ART, f"{shot}_{look}.png")
    if not os.path.exists(ref):
        return key, "no in-engine capture " + ref, 0.0
    attach = os.path.join(WORK, f"ref_{shot}_{look}.png")
    shutil.copyfile(ref, attach)
    ok, tail, sec = run_codex(concept_prompt(key), out, attach)
    if ok:
        place_concept(key)
    return key, "ok" if ok else "FAILED " + tail, sec


def place_concept(key):
    """Center-crop to 16:9 and scale to the capture size so the sheet cells line up with the in-engine shots."""
    im = Image.open(os.path.join(WORK, key + ".png")).convert("RGB")
    h = int(im.width * 9 / 16)
    if h <= im.height:
        y0 = (im.height - h) // 2
        im = im.crop((0, y0, im.width, y0 + h))
    else:
        w = int(im.height * 16 / 9)
        x0 = (im.width - w) // 2
        im = im.crop((x0, 0, x0 + w, im.height))
    im.resize((1600, 900), Image.LANCZOS).save(os.path.join(ART, key + ".png"))


def gen_tile(name):
    out = os.path.join(WORK, f"tile_{name}.png")
    if os.path.exists(out):
        return name, "exists", 0.0
    ok, tail, sec = run_codex(tile_prompt(name), out)
    return name, "ok" if ok else "FAILED " + tail, sec


def generate(fn, keys):
    os.makedirs(WORK, exist_ok=True)
    with ThreadPoolExecutor(max_workers=4) as ex:
        for key, status, sec in ex.map(fn, keys):
            print(f"{key:22s} {status} {sec:6.1f}s", flush=True)


# ------------------------------------------------------------------ build maps from the tiles
def blur(a, sigma):
    """Periodic Gaussian blur (the tiles wrap)."""
    fy = np.fft.fftfreq(a.shape[0])[:, None]
    fx = np.fft.fftfreq(a.shape[1])[None, :]
    g = np.exp(-2 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    if a.ndim == 2:
        return np.real(np.fft.ifft2(np.fft.fft2(a) * g))
    return np.stack([np.real(np.fft.ifft2(np.fft.fft2(a[..., c]) * g)) for c in range(a.shape[2])], -1)


def seam_ratio(a):
    """Jump across the wrap vs the average jump between neighbouring pixels (~1 when it tiles)."""
    lum = a.mean(-1)
    inner = np.abs(np.diff(lum, axis=0)).mean() + np.abs(np.diff(lum, axis=1)).mean()
    wrap = np.abs(lum[0] - lum[-1]).mean() + np.abs(lum[:, 0] - lum[:, -1]).mean()
    return wrap / (inner + 1e-9)


def make_seamless(a, feather=0.2):
    """Blend with half-shifted copies near the borders (the shifted copies are continuous across the wrap there)."""
    def weight(k):
        d = np.minimum(np.arange(k), np.arange(k)[::-1]) / (feather * k)
        t = np.clip(d, 0, 1)
        return t * t * (3 - 2 * t)  # 0 at the border, 1 inside
    fy, fx = weight(a.shape[0])[:, None, None], weight(a.shape[1])[None, :, None]
    hy, hx = a.shape[0] // 2, a.shape[1] // 2
    return (fx * fy * a + (1 - fx) * fy * np.roll(a, hx, 1) + fx * (1 - fy) * np.roll(a, hy, 0)
            + (1 - fx) * (1 - fy) * np.roll(a, (hy, hx), (0, 1)))


def load_tile(name, target_mean, delight=0.8):
    """RGB float tile: seamless, large-scale lighting divided out, colour matched to the procedural palette so the
    comparison is about surface detail rather than paint colour."""
    a = np.asarray(Image.open(os.path.join(WORK, f"tile_{name}.png")).convert("RGB")).astype(float) / 255.0
    before = seam_ratio(a)
    if before > 1.5:
        a = make_seamless(a)
    low = blur(a, a.shape[0] / 8)
    a = a * (low.mean((0, 1)) / np.maximum(low, 1e-3)) ** delight
    a = a * (np.asarray(target_mean) / a.mean((0, 1)))
    print(f"  {name:18s} seam {before:5.2f} -> {seam_ratio(a):4.2f}  mean {np.round(a.mean((0, 1)), 3)}")
    return np.clip(a, 0, 1)


def tiled(a, size, reps):
    """Scale a tile to size/reps pixels and repeat it reps x reps times."""
    k = size // reps
    im = Image.fromarray((a * 255).astype(np.uint8)).resize((k, k), Image.LANCZOS)
    return np.tile(np.asarray(im).astype(float) / 255.0, (reps, reps, 1))


def relief(rgb, sigma=6.0):
    """Height for the normal map from the tile's fine luminance detail (high-pass, robustly normalized)."""
    lum = rgb.mean(-1)
    hp = lum - blur(lum, sigma)
    lo, hi = np.percentile(hp, [1, 99])
    return np.clip((hp - lo) / (hi - lo + 1e-9), 0, 1)


def build():
    os.makedirs(GPT_OUT, exist_ok=True)
    have = lambda n: os.path.exists(os.path.join(WORK, f"tile_{n}.png"))  # noqa: E731
    if have("wall_paint") and have("band_paint"):
        paint = tiled(load_tile("wall_paint", proc.WALL_PAINT), 2048, 2)
        band = tiled(load_tile("band_paint", proc.WALL_BAND), 2048, 2)
        _, is_band, _, _ = proc.wall_layout(2048)
        proc.wall(paint, band, relief(proc.lerp(paint, band, is_band)) * 0.8, GPT_OUT)
    if have("floor_terrazzo"):
        col = tiled(load_tile("floor_terrazzo", (0.46, 0.46, 0.44), 0.6), 2048, 1)
        proc.save_rgb("floor_albedo", col, GPT_OUT)
        proc.save_normal("floor_normal", relief(col, 3.0), 0.8, GPT_OUT)
        wear = blur(col.mean(-1), 40)
        wear = (wear - wear.min()) / (wear.max() - wear.min() + 1e-9)
        proc.save_mask("floor_mask", np.zeros(col.shape[:2]), 0.4 + 0.25 * wear, GPT_OUT)
    if have("ceiling_paint"):
        proc.save_rgb("ceiling_albedo", tiled(load_tile("ceiling_paint", (0.8, 0.8, 0.78)), 2048, 2), GPT_OUT)
    if have("parapet_concrete"):
        col = tiled(load_tile("parapet_concrete", (0.68, 0.67, 0.64), 0.6), 1024, 1)
        proc.save_rgb("parapet_albedo", col, GPT_OUT)
        proc.save_normal("parapet_normal", relief(col, 4.0), 3.0, GPT_OUT)
    if have("door_paint"):
        # one tile = 1 m; the face texture is 512 x 1216 px over 0.87 x 2.06 m (~589 px/m)
        t = tiled(load_tile("door_paint", proc.DOOR_PAINT), 589 * 3, 3)
        proc.door(t[:proc.DOOR_H, :proc.DOOR_W], GPT_OUT)
    if have("stainless"):
        proc.stainless(tiled(load_tile("stainless", proc.STAINLESS, 0.5), 1024, 1), GPT_OUT)
    print("gpt maps ->", GPT_OUT, sorted(os.listdir(GPT_OUT)))


def status():
    for k in concept_keys():
        print(f"{k:22s} {'done' if os.path.exists(os.path.join(ART, k + '.png')) else '-'}")
    for n in TILES:
        print(f"tile_{n:17s} {'done' if os.path.exists(os.path.join(WORK, f'tile_{n}.png')) else '-'}")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    args = sys.argv[2:]
    if cmd == "concepts":
        generate(gen_concept, args or concept_keys())
    elif cmd == "tiles":
        generate(gen_tile, args or list(TILES))
    elif cmd == "build":
        build()
    else:
        status()
