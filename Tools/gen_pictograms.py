"""
Generates the terminal manual's pictograms with Codex image generation, then converts them for the UI.

    python Tools/gen_pictograms.py gen  [name ...]   -> raw PNGs into %TEMP%/nocx/pictos (skips existing)
    python Tools/gen_pictograms.py convert           -> 256px white-on-transparent PNGs into Assets/_Project/UI/Pictograms

Needs the Codex CLI (see Tools/codex_cli.py; runs at low effort, CODEX_EFFORT overrides).
"""
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageOps

from codex_cli import exec_args

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORK = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "pictos")
OUT = os.path.join(ROOT, "Assets", "_Project", "UI", "Pictograms")

STYLE = ("Style: flat safety-sign pictogram like ISO 7010 / Korean public signage. Pure black shapes with thick uniform "
         "strokes on a pure white background. No text, no letters, no numbers, no border or frame, no shading, no "
         "gradients. Centered with generous white margin. Square image. Same visual language as a set of matching icons.")

# name -> subject. Entities (e_*) and actions (a_*) used by the 단말기 매뉴얼.
SUBJECTS = {
    "e_tallone": "an unnaturally tall, thin humanoid silhouette standing in a corridor next to an ordinary door frame; its head is clearly higher than the top of the door frame; very long arms hanging below its knees.",
    "e_escort": "the same unnaturally tall, thin humanoid silhouette caught mid-stride, walking along a corridor, with a trail of footprints behind it on the floor.",
    "e_lighteater": "a row of four ceiling light tubes seen from below; the two on the right are lit with short rays, the two on the left are dark and being swallowed by a formless black mass coming from the left.",
    "e_short": "an open wall-mounted electrical panel box with a jagged lightning spark coming out of it and a single flickering light bulb beside it with broken rays.",
    "a_eyecontact": "two eyes facing each other from left and right, connected by one straight bold line between them (holding eye contact).",
    "a_standstill": "a person standing upright with feet together and arms at the sides, two short horizontal bars under the feet showing they do not move.",
    "a_headdown": "a person standing still with the head bowed far down, looking at the floor in front of the feet.",
    "a_flashlight_floor": "a hand letting go of a flashlight that lies on the floor, the flashlight beam spreading along the floor.",
    "a_backtowall": "a person standing with their back pressed flat against a wall, head bowed, arms at the sides.",
    "a_enterroom": "a person stepping through an open doorway into a room, with a curved arrow showing the door being pulled shut behind them.",
    "a_lights_on": "a ceiling lamp switched on with bold rays, and next to it a small handheld walkie-talkie.",
    "a_lights_off": "a ceiling lamp with no light, crossed by one bold diagonal slash.",
    "a_flashlight_off": "a handheld flashlight with no beam, crossed by one bold diagonal slash.",
    "a_wait": "a stopwatch with no numbers on its face, a filled pie wedge marking a short time.",
    "a_upstairs": "a person climbing a staircase upward, with a bold arrow pointing up the stairs.",
    "a_lure_below": "a staircase: a person standing at the top, and a lit lamp with rays on the lower floor at the bottom.",
    "a_panel_reset": "a hand pushing up a breaker lever inside an open electrical panel box.",
    # stage 3: 묶음 B (빈 층 · 동승자), D (뒷사람 · 울림), 흉내쟁이
    "e_emptyfloor": "an elevator car seen from the front with both doors wide open and nobody inside; outside the doors an empty corridor floor line.",
    "e_passenger": "inside an elevator car: one person standing facing the closed doors, and behind them a second figure drawn only as a dashed outline, also visible in a mirror on the side wall.",
    "e_follower": "a person walking forward, and right behind them a second set of footprints following exactly in step with no one standing there.",
    "e_echo": "a person walking up a staircase, with faint curved sound-wave arcs rising from the empty stairs below and fading out.",
    "e_mimic": "a closed door seen from the inside with three small impact marks near its middle (knocking), and a hollow dashed outline of a person standing outside behind the door.",
    "a_no_buttons": "a hand reaching toward a vertical column of round elevator buttons, crossed by one bold diagonal slash.",
    "a_remote_close": "a small desktop computer monitor on the left and, on the right, elevator doors sliding shut with two arrows pointing toward each other.",
    "a_no_talk": "a handheld walkie-talkie next to an open mouth, crossed by one bold diagonal slash.",
    "a_no_lookback": "a person seen from the side turning their head back over their shoulder, crossed by one bold diagonal slash.",
    "a_ride_down": "an elevator car with a bold arrow pointing straight down beside it toward a thick ground line at the bottom.",
    "a_stop_nearest": "an elevator car in a shaft with a raised open-palm stop hand beside it and a short bar marking the next floor.",
    "a_walk_out": "a person walking straight forward out through open elevator doors, with a bold arrow pointing forward.",
    "a_ptt_release": "a hand opening its fingers away from the push-to-talk button on the side of a walkie-talkie, with small release lines.",
    "a_walk_in_step": "a person walking forward with a steady stride above a row of evenly spaced footprints.",
    "a_ignore": "a person walking forward without turning, faint curved sound-wave arcs behind them, a bold arrow pointing forward.",
    "a_dont_open": "a closed door with a door handle, crossed by one bold diagonal slash.",
}


def gen_one(name):
    path = os.path.join(WORK, name + ".png")
    if os.path.exists(path):
        return name, "exists", 0.0
    prompt = (f"Use your image generation tool to create ONE pictogram and save it as a PNG file named {name}.png in the "
              f"current working directory (overwrite if it exists). Do not draw it with code; generate it as an image.\n"
              f"{STYLE}\nSubject: {SUBJECTS[name]}\nReply with just the saved file path.")
    t0 = time.time()
    p = subprocess.run(exec_args("low") + ["-s", "workspace-write", "-C", WORK, "-"],
                       input=prompt.encode("utf-8"), capture_output=True, timeout=600)
    ok = os.path.exists(path)
    tail = p.stdout.decode("utf-8", "replace").strip().splitlines()[-1:] if not ok else []
    return name, "ok" if ok else f"FAILED {tail}", time.time() - t0


def gen(names):
    os.makedirs(WORK, exist_ok=True)
    todo = [n for n in (names or SUBJECTS) if n in SUBJECTS]
    with ThreadPoolExecutor(max_workers=4) as ex:
        for name, status, sec in ex.map(gen_one, todo):
            print(f"{name:22s} {status} {sec:5.1f}s", flush=True)


def convert():
    """Black-on-white → white glyph with alpha, cropped to content, padded square, 256 px."""
    os.makedirs(OUT, exist_ok=True)
    for name in SUBJECTS:
        src = os.path.join(WORK, name + ".png")
        if not os.path.exists(src):
            print("missing", name)
            continue
        im = Image.open(src)
        if im.mode in ("RGBA", "LA", "P"):
            # some generations come back with a transparent background: flatten onto white first
            im = im.convert("RGBA")
            white = Image.new("RGBA", im.size, (255, 255, 255, 255))
            im = Image.alpha_composite(white, im)
        g = ImageOps.grayscale(im.convert("RGB"))
        alpha = ImageOps.invert(g).point(lambda v: 0 if v < 40 else min(255, int((v - 40) * 255 / 170)))
        box = alpha.getbbox() or (0, 0, g.width, g.height)
        alpha = alpha.crop(box)
        side = int(max(alpha.width, alpha.height) * 1.12)
        sq = Image.new("L", (side, side), 0)
        sq.paste(alpha, ((side - alpha.width) // 2, (side - alpha.height) // 2))
        sq = sq.resize((256, 256), Image.LANCZOS)
        rgba = Image.new("RGBA", sq.size, (255, 255, 255, 0))
        rgba.putalpha(sq)
        rgba.save(os.path.join(OUT, name + ".png"))
        print("converted", name)
    write_uss()


def write_uss():
    """One USS class per pictogram (picto--<name>) so the UI only toggles classes."""
    names = sorted(f[:-4] for f in os.listdir(OUT) if f.endswith(".png"))
    lines = [
        "/* Generated by Tools/gen_pictograms.py — one class per pictogram. */",
        ".picto {",
        "    -unity-background-scale-mode: scale-to-fit;",
        "    -unity-background-image-tint-color: var(--color-text);",
        "}",
        "",
    ]
    for n in names:
        lines += [f".picto--{n} {{", f'    background-image: url("project://database/Assets/_Project/UI/Pictograms/{n}.png");', "}", ""]
    path = os.path.join(ROOT, "Assets", "_Project", "UI", "Screens", "Pictograms.uss")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("wrote", path, len(names), "classes")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "gen"
    if cmd == "gen":
        gen(sys.argv[2:])
    elif cmd == "convert":
        convert()
